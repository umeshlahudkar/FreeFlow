using UnityEngine;
using System.Collections.Generic;
using System.IO;
using FreeFlow.Util;

/// <summary>
/// Daily Challenge state -- kept in its own file, separate from <see cref="SaveData"/>, because
/// for now this is staying local rather than going up with whatever eventually syncs as player
/// progress. If that changes later, this is the one file that needs to move, rather than picking
/// daily-challenge fields back out of a shared payload.
/// </summary>
public class DailyChallengeSystem : Singleton<DailyChallengeSystem>
{
    private readonly string fileName = "DailyChallenge.json";
    private string filePath = string.Empty;

    private void Awake()
    {
        filePath = Path.Combine(Application.persistentDataPath, fileName);
    }

    public void Save(DailyChallengeData data)
    {
        string jsonData = JsonUtility.ToJson(data);
        File.WriteAllText(filePath, jsonData);
    }

    public DailyChallengeData Load()
    {
        if (File.Exists(filePath))
        {
            string jsonData = File.ReadAllText(filePath);
            return JsonUtility.FromJson<DailyChallengeData>(jsonData);
        }
        return default;
    }

    public void DeleteFile()
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}

[System.Serializable]
public struct DailyChallengeData
{
    // Which level DailyChallengeSelector picked for the currently-cached day, and streak
    // bookkeeping. dailyChallengeCachedDay/dailyChallengeLastCompletedDay use 0 as "never" rather
    // than a separate bool: DailyChallengeSelector.Epoch is fixed in the past far enough that day
    // 0 (the epoch date itself) can never be "today" again in real play, so 0 is unambiguous.
    //
    // The pick is cached rather than recomputed on every visit so a player who plays other levels
    // between opening the daily challenge and finishing it does not have the board change under
    // them because their skill rating moved.
    //
    // playerSalt makes the pick per-install rather than shared: without it, every player in the
    // same skill band would see the identical level on the same day (there being no backend to
    // make that meaningful anyway). 0 means "not yet assigned" -- see EnsurePlayerSalt, which is
    // the only thing allowed to set it, and only ever ONCE per save, since changing it later would
    // silently reshuffle every future day's pick for a player who already has a rhythm going.
    public int playerSalt;
    public int dailyChallengeCachedDay;

    // The cached day's challenges, in play order (ascending board size -- see
    // DailyChallengeSelector.SelectDay), each carrying its own solved flag.
    public DailyPick[] dailyChallengePicks;

    public int dailyChallengeLastCompletedDay;
    public int dailyChallengeStreak;
    public int bestDailyChallengeStreak;

    // ---- calendar daily challenge (one curated level per calendar day) --------------------
    //
    // Supersedes dailyChallengePicks/dailyChallengeCachedDay above for the new flow: a day now
    // has exactly one level, the same for every player (see DailyChallengeCalendar), so there is
    // nothing left to "cache" -- only whether that one day's level has been solved. The old
    // fields stay put, unwritten by anything any more, purely so an existing save file still
    // deserialises cleanly rather than losing unrelated data on load.
    //
    // Absolute day indices (DailyChallengeSelector.DayIndex/EpochUtc), same numbering the streak
    // fields above already use -- a List rather than a Dictionary because JsonUtility cannot
    // serialise the latter at all, and this only ever needs "was day N completed", never a value
    // per day.
    public List<int> completedDayIndices;

    public bool IsDayCompleted(int dayIndex)
    {
        return completedDayIndices != null && completedDayIndices.Contains(dayIndex);
    }

    /// <summary>Marks one calendar day's single challenge solved and credits the streak.
    /// Idempotent -- replaying an already-completed day changes nothing (RecordDailyChallengeCompletion
    /// is itself idempotent per day on top of the guard here, so this is safe to call more than
    /// once for the same day from more than one place).</summary>
    public void MarkDayCompleted(int dayIndex)
    {
        if (IsDayCompleted(dayIndex)) { return; }
        if (completedDayIndices == null) { completedDayIndices = new List<int>(); }
        completedDayIndices.Add(dayIndex);
        RecordDailyChallengeCompletion(dayIndex);
    }

    /// <summary>Adopts <paramref name="candidateSalt"/> as this save's permanent per-install salt
    /// if none is set yet, otherwise does nothing -- the salt is assigned once, ever, not
    /// refreshed. Takes the candidate as a parameter rather than generating one itself so this
    /// struct stays plain C# with no Unity RNG dependency; the caller (UIController) is expected
    /// to pass a value that is never 0, since 0 is what "unset" looks like.</summary>
    public void EnsurePlayerSalt(int candidateSalt)
    {
        if (playerSalt == 0) { playerSalt = candidateSalt; }
    }

    /// <summary>How many daily challenges the cached day holds. 0 before any day has been
    /// cached.</summary>
    public int DailyChallengeCount
    {
        get { return dailyChallengePicks == null ? 0 : dailyChallengePicks.Length; }
    }

    /// <summary>Replaces the cached day wholesale: new day, new picks, every solved flag cleared.
    /// The only thing allowed to write <see cref="dailyChallengeCachedDay"/>, so a half-updated
    /// cache (new picks, stale day, or stale solved flags) cannot exist.</summary>
    public void SetDailyChallenges(int dayIndex, DailyPick[] picks)
    {
        dailyChallengePicks = picks ?? new DailyPick[0];
        dailyChallengeCachedDay = dayIndex;
    }

    public bool IsDailyChallengeSolved(int slot)
    {
        return slot >= 0 && slot < DailyChallengeCount && dailyChallengePicks[slot].solved;
    }

    /// <summary>Marks one of the cached day's challenges finished. Out-of-range slots are ignored
    /// rather than thrown on: the day can roll over between a level being opened and being
    /// completed, which re-picks a list that the in-flight slot index no longer indexes into.</summary>
    public void MarkDailyChallengeSolved(int slot)
    {
        if (slot < 0 || slot >= DailyChallengeCount) { return; }
        dailyChallengePicks[slot].solved = true;
    }

    public int SolvedDailyChallengeCount()
    {
        int solved = 0;
        for (int i = 0; i < DailyChallengeCount; i++)
        {
            if (dailyChallengePicks[i].solved) { solved++; }
        }
        return solved;
    }

    /// <summary>Whether EVERY one of the cached day's challenges is finished. This is the ONLY
    /// condition under which a day may be credited to the streak (see
    /// <see cref="RecordDailyChallengeCompletion"/>, and its one caller,
    /// GamePlayController.SaveLevelData) -- finishing four of five moves nothing. A day with no
    /// picks cached at all is not "all solved": there is nothing to have solved.</summary>
    public bool AllDailyChallengesSolved()
    {
        if (DailyChallengeCount == 0) { return false; }
        return SolvedDailyChallengeCount() == DailyChallengeCount;
    }

    /// <summary>The first challenge of the cached day the player has not finished, or -1 if the
    /// day is done -- where a "Play" button with no slot of its own should drop them in.</summary>
    public int FirstUnsolvedDailyChallenge()
    {
        for (int i = 0; i < DailyChallengeCount; i++)
        {
            if (!dailyChallengePicks[i].solved) { return i; }
        }
        return -1;
    }

    /// <summary>Highest slot of the cached day the player is allowed to open, or -1 when no day is
    /// cached. A day is played in order: everything already solved, plus the one immediately after
    /// the last solved one, and nothing beyond that. Once the whole day is done every slot stays
    /// open, so a finished challenge can still be replayed.
    ///
    /// Because the day's challenges climb in board size (see DailyChallengeSelector.SelectDay),
    /// this ordering is also a difficulty ramp -- opening the 9x9 before the 5x5 would be handing
    /// the player the hardest board of the day first.</summary>
    public int UnlockedDailyChallengeThrough()
    {
        int count = DailyChallengeCount;
        if (count == 0) { return -1; }

        int firstUnsolved = FirstUnsolvedDailyChallenge();
        return firstUnsolved < 0 ? count - 1 : firstUnsolved;
    }

    /// <summary>Credits one daily-challenge DAY for <paramref name="dayIndex"/> -- the day the
    /// challenges were PICKED for (DailyChallengeData.dailyChallengeCachedDay at load time), not
    /// necessarily the day they happen to be finished on if a session runs past midnight.
    /// Idempotent for the same day, so retrying an already-completed challenge cannot inflate the
    /// streak.
    ///
    /// A day is credited once, when its LAST challenge is solved -- callers gate on
    /// <see cref="AllDailyChallengesSolved"/>.</summary>
    public void RecordDailyChallengeCompletion(int dayIndex)
    {
        if (dayIndex == dailyChallengeLastCompletedDay) { return; }

        dailyChallengeStreak = (dailyChallengeLastCompletedDay == dayIndex - 1) ? dailyChallengeStreak + 1 : 1;
        dailyChallengeLastCompletedDay = dayIndex;
        if (dailyChallengeStreak > bestDailyChallengeStreak) { bestDailyChallengeStreak = dailyChallengeStreak; }
    }

    /// <summary>The streak as it should be SHOWN on <paramref name="todayIndex"/>: the stored
    /// count while the run is still alive, 0 once it has been broken.
    ///
    /// <see cref="dailyChallengeStreak"/> is only ever written when a day is credited (see
    /// <see cref="RecordDailyChallengeCompletion"/>), so between breaking a streak and completing
    /// another day the field holds a number that is no longer true -- miss Wednesday and the card
    /// still reads "2-DAY" on Friday, right up until the next completion quietly resets it to 1.
    /// A run survives exactly while the last credited day is today (the day is already banked) or
    /// yesterday (today is still playable); anything older is a gap, and the next completion
    /// restarts the count at 1 regardless.
    ///
    /// Display-only, deliberately: the stored value is also the LENGTH of the last run, which
    /// DailyChallengePage's week chain reads to light up the days that were actually solved.
    /// Zeroing the field the moment a streak lapsed would erase that history along with it.</summary>
    public int LiveDailyChallengeStreak(int todayIndex)
    {
        if (dailyChallengeStreak <= 0) { return 0; }

        return todayIndex - dailyChallengeLastCompletedDay <= 1 ? dailyChallengeStreak : 0;
    }
}

/// <summary>One of a day's daily challenges: which level DailyChallengeSelector drew for that
/// slot, and whether it has been finished yet.
///
/// Pick and solved-state live in ONE entry rather than in parallel arrays so they cannot drift
/// out of length with each other -- a solved flag that outlived the pick it belonged to would
/// silently credit the wrong board. Mirrors DailyChallengeSelector.Pick's three fields rather
/// than reusing that type directly, because DailyChallengeData is plain serialisable data with no
/// dependency on the gameplay assembly's selection logic.</summary>
[System.Serializable]
public struct DailyPick
{
    public FreeFlow.Enums.GameMode mode;
    public int packSize;
    public int levelNumber;
    public bool solved;
}
