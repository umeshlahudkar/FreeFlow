using UnityEngine;
using System.IO;
using FreeFlow.Util;

public class SavingSystem : Singleton<SavingSystem>
{
    private readonly string fileName = "SaveData.json";
    private string filePath = string.Empty;

    private void Awake()
    {
        filePath = Path.Combine(Application.persistentDataPath, fileName);
        //DeleteFile();
        if (!File.Exists(filePath))
        {
            SaveData data = new();
            data.schemaVersion = SaveData.CurrentSchemaVersion;
            data.completedLevel = 0;

            data.audioData.isMusicMute = false;
            data.audioData.isSoundMute = false;
            data.audioData.musicVolume = 0.5f;
            data.audioData.soundVolume = 0.5f;

            data.vibrationEnabled = true;
            data.showHintButton = true;

            Save(data);
        }
    }

    public void Save(SaveData data)
    {
        string jsonData = JsonUtility.ToJson(data);
        File.WriteAllText(filePath, jsonData);
    }

    public SaveData Load()
    {
        if(File.Exists(filePath))
        {
            string jsonData = File.ReadAllText(filePath);
            SaveData data = JsonUtility.FromJson<SaveData>(jsonData);

            // A save written before schemaVersion existed reads as 0 (JsonUtility's int default),
            // which is indistinguishable from "genuinely on version 0" -- exactly the property a
            // migration seam needs. Written back immediately so a returning player is only ever
            // migrated once, not on every load.
            if (data.schemaVersion < SaveData.CurrentSchemaVersion)
            {
                SaveData.Migrate(ref data);
                Save(data);
            }

            return data;
        }
        return default;
    }

    public void DeleteFile()
    {
        if(File.Exists(filePath))
        {
            File.Delete(filePath);
            Debug.Log("File delete");
        }
    }
}

[System.Serializable]
public struct SaveData
{
    // Bumped whenever a change to this struct needs more than "leave the new field at its
    // JsonUtility default" -- a rename, a unit change, a value that has to be recomputed from
    // what an old save already has. Nothing yet needs that (every field added since this struct
    // shipped -- packProgress, the telemetry arrays, and everything below -- defaults safely, the
    // same pattern GAME_EXPANSION_PLAN §4.4 established for LevelData), so schemaVersion 0->1
    // is a no-op migration. The seam exists so the NEXT structural change has a real place to
    // convert old data instead of inventing versioning under pressure. See SaveData.Migrate.
    public const int CurrentSchemaVersion = 5;
    public int schemaVersion;

    /// <summary>Brings a save from whatever <see cref="schemaVersion"/> it was written at up to
    /// <see cref="CurrentSchemaVersion"/>. Called once, by <c>SavingSystem.Load</c>, before the
    /// data reaches any gameplay code -- callers should never need to know a save was old.</summary>
    public static void Migrate(ref SaveData data)
    {
        // 0 -> 1: added schemaVersion itself, per-mechanic skill tracking, and per-level hint
        // counts. All three are additive fields JsonUtility already defaulted to null/0/false on
        // load, so there is nothing to transform -- only the version number itself needs setting.

        // 1 -> 2: added vibrationEnabled and showHintButton (Settings screen toggles). Both new
        // bools would otherwise read false via JsonUtility's default, silently opting a returning
        // player OUT of settings a brand-new player starts WITH ON (see SavingSystem.Awake) --
        // set them explicitly here so an old save gets the same defaults a new one would.
        if (data.schemaVersion < 2)
        {
            data.vibrationEnabled = true;
            data.showHintButton = true;
        }

        // 2 -> 3: added bestDailyChallengeStreak (Daily Challenge screen's "BEST STREAK" card). A
        // bare new int field would read 0 via JsonUtility's default even for a save whose CURRENT
        // streak is already higher than that -- showing "best: 0" next to "current: 6" would look
        // broken rather than merely un-tracked, so backfill it from whatever streak already exists.
        if (data.schemaVersion < 3)
        {
            if (data.dailyChallengeStreak > data.bestDailyChallengeStreak)
            {
                data.bestDailyChallengeStreak = data.dailyChallengeStreak;
            }
        }

        // 3 -> 4: a day can now hold SEVERAL daily challenges (see dailyChallengePicks), where it
        // previously held exactly one in three flat fields. Carry whatever the save had cached for
        // its day across as a one-entry list rather than dropping it: a player who opened today's
        // challenge under the old build and has it half-finished must not have the board swapped
        // out from under them by the upgrade. Its solved flag is recoverable because, when a day
        // held one challenge, "the day is complete" and "that one level is solved" were the same
        // statement. Tomorrow's roll-over is what first produces a full-size list.
        if (data.schemaVersion < 4)
        {
            bool hasCachedDay = data.dailyChallengeCachedDay != 0;
            bool hasPicks = data.dailyChallengePicks != null && data.dailyChallengePicks.Length > 0;
            if (hasCachedDay && !hasPicks)
            {
                data.dailyChallengePicks = new DailyPick[]
                {
                    new DailyPick
                    {
                        mode = data.dailyChallengeMode,
                        packSize = data.dailyChallengePackSize,
                        levelNumber = data.dailyChallengeLevel,
                        solved = data.dailyChallengeLastCompletedDay == data.dailyChallengeCachedDay,
                    }
                };
            }
        }

        // 4 -> 5: hints became a spendable balance (hintsRemaining) rather than only a per-level
        // tally. Nothing to transform: hintsInitialized reads false on any save written before
        // this, which is exactly the state that makes UIController.EnsureHintBalance hand the
        // player their opening hints -- the same number a brand-new save gets. The count itself
        // is authored in the inspector, not here, so a designer can change it without a
        // migration; false means "never granted", not "granted zero".

        data.schemaVersion = CurrentSchemaVersion;
    }

    // Classic's progress deliberately keeps the ORIGINAL field name. JsonUtility fills any field
    // missing from an existing save with its default, so a save written before the two modes
    // existed would silently reset whichever campaign got renamed. Classic is the default mode and
    // the one a returning player is most likely mid-way through, so it inherits the old field and
    // the old progress; Advanced starts empty, which is correct -- it did not exist before.
    public int completedLevel;

    public int advancedCompletedLevel;

    // Play telemetry, per level, per mode.
    //
    // Every shipped system in the genre rates difficulty from play rather than from a model of it.
    // King train bots to IMITATE players specifically so they can predict a level's difficulty
    // before release; Lichess does not model puzzle difficulty at all, and instead scores each
    // attempt as a Glicko2 game between the player and the puzzle, with a rating that stabilises
    // after 20-30 attempts. Offline metrics are everywhere a PRE-FILTER, and play data sets the
    // final order. We have spent five rounds arguing about proxies without ever recording the one
    // signal that would settle it.
    //
    // Attempts is the headline number: the published benchmark for a tuned mobile puzzle curve is
    // about 3.2 attempts per completion once onboarding is past, so a level sitting at 1.0 is not
    // pulling its weight and one at 8 is a wall. Seconds is the tiebreaker, and is what Pelánek's
    // whole Sudoku evaluation regresses against.
    //
    // Both are added fields, so JsonUtility fills them with null on an existing save and nothing
    // resets -- the same reason Classic kept the original field names above.
    public int[] completedLevelAttempts;
    public float[] completedLevelSeconds;

    public int[] advancedCompletedLevelAttempts;
    public float[] advancedCompletedLevelSeconds;

    // Per-PACK progress. Classic ships as five packs of 100 (one per board size) and Advanced is
    // going the same way, so progress can no longer be keyed by mode alone -- finishing 5x5 level 20
    // would otherwise mark 7x7 level 20 complete.
    //
    // An array of keyed entries rather than a dictionary because JsonUtility serialises arrays of
    // serialisable structs and does not serialise dictionaries at all. The legacy linear campaigns
    // keep the flat fields above and are NOT migrated into this: a returning player mid-way through
    // the old Classic run keeps their place, which is the same reason Classic kept the original
    // field names when the two modes split.
    public PackProgress[] packProgress;

    // Per-mechanic skill: completions and attempts, pooled across every pack and mode that
    // mechanic appears in. See RecordMechanicAttempt/RecordMechanicCompletion and
    // GAME_EXPANSION_PLAN's Phase 9 note on why this is a completion-ratio proxy rather than a
    // per-puzzle rating. Additive, so an existing save gets an empty array and starts everyone
    // at "unseen" rather than losing anything.
    public MechanicSkill[] mechanicSkills;

    // -- daily challenge ---------------------------------------------------------------------
    //
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
    // DailyChallengeSelector.SelectDay), each carrying its own solved flag. This is the live
    // record; the three flat fields below are the pre-schema-4 shape, kept ONLY so an existing
    // save's in-progress day survives the upgrade (see Migrate) and so a downgrade to an older
    // build still finds a level to open. They mirror slot 0 and nothing reads them otherwise.
    public DailyPick[] dailyChallengePicks;

    public FreeFlow.Enums.GameMode dailyChallengeMode;
    public int dailyChallengePackSize;
    public int dailyChallengeLevel;

    // The last day whose challenges the player actually LOOKED at -- set when the Daily Challenge
    // hub is opened, not when one is played. Drives the "NEW" badge on the main menu's daily card,
    // which is about unseen content rather than unfinished content: a day can be seen and left
    // unplayed, and the badge should still go away.
    //
    // Needs no schema bump. JsonUtility defaults it to 0 on an existing save, 0 means "no day has
    // been seen", and that correctly shows the badge -- which is right, because on the build that
    // introduces it nobody has seen today's challenges through this UI yet.
    public int dailyChallengeLastSeenDay;

    public int dailyChallengeLastCompletedDay;
    public int dailyChallengeStreak;
    public int dailyChallengesCompletedTotal;
    public int bestDailyChallengeStreak;

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
    /// cache (new picks, stale day, or stale solved flags) cannot exist. Keeps the legacy flat
    /// fields pointed at slot 0 -- see <see cref="dailyChallengePicks"/> for why they still
    /// exist.</summary>
    public void SetDailyChallenges(int dayIndex, DailyPick[] picks)
    {
        dailyChallengePicks = picks ?? new DailyPick[0];
        dailyChallengeCachedDay = dayIndex;

        if (dailyChallengePicks.Length > 0)
        {
            dailyChallengeMode = dailyChallengePicks[0].mode;
            dailyChallengePackSize = dailyChallengePicks[0].packSize;
            dailyChallengeLevel = dailyChallengePicks[0].levelNumber;
        }
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
    /// challenges were PICKED for (SaveData.dailyChallengeCachedDay at load time), not necessarily
    /// the day they happen to be finished on if a session runs past midnight. Idempotent for the
    /// same day, so retrying an already-completed challenge cannot inflate the streak or the
    /// lifetime count.
    ///
    /// A day is credited once, when its LAST challenge is solved -- callers gate on
    /// <see cref="AllDailyChallengesSolved"/>. The lifetime total therefore counts days completed,
    /// not levels played, which is what a streak is about and what it counted back when a day held
    /// exactly one level.</summary>
    public void RecordDailyChallengeCompletion(int dayIndex)
    {
        if (dayIndex == dailyChallengeLastCompletedDay) { return; }

        dailyChallengeStreak = (dailyChallengeLastCompletedDay == dayIndex - 1) ? dailyChallengeStreak + 1 : 1;
        dailyChallengeLastCompletedDay = dayIndex;
        dailyChallengesCompletedTotal++;
        if (dailyChallengeStreak > bestDailyChallengeStreak) { bestDailyChallengeStreak = dailyChallengeStreak; }
    }

    public AudioData audioData;

    // -- settings screen preferences ---------------------------------------------------------
    //
    // Persisted the same way audioData is (through SavingSystem, not PlayerPrefs) so every
    // player preference lives in one file. vibrationEnabled has no consumer yet -- no haptic
    // trigger exists anywhere in the codebase -- so this only stores the preference for future
    // use. showHintButton is read by UIController to decide whether the gameplay hint button is
    // shown at all, independent of GamePlayController.HintAvailable (which decides whether it is
    // interactable once shown).
    public bool vibrationEnabled;
    public bool showHintButton;

    // -- hint balance ------------------------------------------------------------------------
    //
    // Hints are spendable: each one taken costs one from this, and at zero the hint button stops
    // being interactable. Separate from the per-level hint tallies (see HintsForKey), which are
    // telemetry -- they record where hints were spent and drive the level-complete star rating,
    // and they only ever grow. This is the wallet; those are the receipts.
    //
    // hintsInitialized is what separates "spent every hint" from "never had any": a save written
    // before hints were spendable, and a save JsonUtility has just defaulted, both read
    // hintsRemaining as 0, and a returning player must not be handed an empty wallet. It is set
    // once, when UIController.EnsureHintBalance grants the opening hints, and never cleared.
    public int hintsRemaining;
    public bool hintsInitialized;

    /// <summary>Highest level finished in <paramref name="mode"/>.</summary>
    public int CompletedLevelFor(FreeFlow.Enums.GameMode mode)
    {
        return mode == FreeFlow.Enums.GameMode.Advanced ? advancedCompletedLevel : completedLevel;
    }

    public void SetCompletedLevelFor(FreeFlow.Enums.GameMode mode, int value)
    {
        if (mode == FreeFlow.Enums.GameMode.Advanced) { advancedCompletedLevel = value; }
        else { completedLevel = value; }
    }

    /// <summary>How many times each level has been STARTED, completed or not. Null until played.</summary>
    public int[] AttemptsFor(FreeFlow.Enums.GameMode mode)
    {
        return mode == FreeFlow.Enums.GameMode.Advanced ? advancedCompletedLevelAttempts : completedLevelAttempts;
    }

    public void SetAttemptsFor(FreeFlow.Enums.GameMode mode, int[] value)
    {
        if (mode == FreeFlow.Enums.GameMode.Advanced) { advancedCompletedLevelAttempts = value; }
        else { completedLevelAttempts = value; }
    }

    /// <summary>Wall-clock seconds of the attempt that COMPLETED each level. Null until played.</summary>
    public float[] SecondsFor(FreeFlow.Enums.GameMode mode)
    {
        return mode == FreeFlow.Enums.GameMode.Advanced ? advancedCompletedLevelSeconds : completedLevelSeconds;
    }

    public void SetSecondsFor(FreeFlow.Enums.GameMode mode, float[] value)
    {
        if (mode == FreeFlow.Enums.GameMode.Advanced) { advancedCompletedLevelSeconds = value; }
        else { completedLevelSeconds = value; }
    }

    // ---- progress by KEY -------------------------------------------------------------------
    //
    // One entry point for both storage shapes. The legacy linear campaigns keep the flat fields
    // above and are addressed as "Classic" / "Advanced"; every pack lives in packProgress under
    // "Classic7x7" and the like. Callers should never branch on this themselves -- doing so is
    // exactly how LevelScreenController ended up reading Classic's progress while Advanced was on
    // screen, and how a pack write nearly landed in a new entry instead of the legacy field.

    private const string LegacyClassicKey = "Classic";
    private const string LegacyAdvancedKey = "Advanced";

    // Every setter below resolves PackIndex into a local before indexing, and that is load-bearing
    // rather than style. Written as `packProgress[PackIndex(key)].attempts = value`, C# evaluates
    // the ARRAY REFERENCE first, then calls PackIndex -- which allocates a larger array and assigns
    // it to the field. The indexer then writes through the reference captured a moment earlier, so
    // on a save that has never held a pack that reference is null and the assignment throws.
    // Splitting the call out makes the growth happen first and the write land on the new array.

    /// <summary>Whether finishing <paramref name="levelJustCompleted"/> may move a pack's unlock
    /// frontier, which currently sits at <paramref name="completedLevel"/>.
    ///
    /// The frontier is a GATE, not a tally: setting it to N declares levels 1..N finished. An
    /// ordinary play can only ever reach the next locked level, so it always may. A daily
    /// challenge is drawn from anywhere inside a pack (see DailyChallengeSelector), so it may not
    /// -- otherwise one daily pick at level 59 unlocks fifty-eight levels the player never saw,
    /// and then reports that as their pack progress. The exception is a daily that happens to BE
    /// the player's next level, where nothing is skipped.
    ///
    /// A pure rule with no Unity dependency so it can be tested directly; the caller
    /// (GamePlayController.SaveLevelData) supplies <paramref name="fromDailyChallenge"/> from
    /// UIController.IsDailyChallenge.</summary>
    public static bool PackFrontierAdvances(int levelJustCompleted, int completedLevel, bool fromDailyChallenge)
    {
        if (levelJustCompleted <= completedLevel) { return false; }
        return !fromDailyChallenge || levelJustCompleted == completedLevel + 1;
    }

    public int CompletedLevelForKey(string key)
    {
        if (key == LegacyClassicKey) { return completedLevel; }
        if (key == LegacyAdvancedKey) { return advancedCompletedLevel; }

        int found = FindPack(key);
        return found < 0 ? 0 : packProgress[found].completedLevel;
    }

    public void SetCompletedLevelForKey(string key, int value)
    {
        if (key == LegacyClassicKey) { completedLevel = value; return; }
        if (key == LegacyAdvancedKey) { advancedCompletedLevel = value; return; }
        int index = PackIndex(key);      // must resolve BEFORE indexing -- see below
        packProgress[index].completedLevel = value;
    }

    public int[] AttemptsForKey(string key)
    {
        if (key == LegacyClassicKey) { return completedLevelAttempts; }
        if (key == LegacyAdvancedKey) { return advancedCompletedLevelAttempts; }

        int found = FindPack(key);
        return found < 0 ? null : packProgress[found].attempts;
    }

    public void SetAttemptsForKey(string key, int[] value)
    {
        if (key == LegacyClassicKey) { completedLevelAttempts = value; return; }
        if (key == LegacyAdvancedKey) { advancedCompletedLevelAttempts = value; return; }
        int index = PackIndex(key);      // must resolve BEFORE indexing -- see below
        packProgress[index].attempts = value;
    }

    public float[] SecondsForKey(string key)
    {
        if (key == LegacyClassicKey) { return completedLevelSeconds; }
        if (key == LegacyAdvancedKey) { return advancedCompletedLevelSeconds; }

        int found = FindPack(key);
        return found < 0 ? null : packProgress[found].seconds;
    }

    public void SetSecondsForKey(string key, float[] value)
    {
        if (key == LegacyClassicKey) { completedLevelSeconds = value; return; }
        if (key == LegacyAdvancedKey) { advancedCompletedLevelSeconds = value; return; }
        int index = PackIndex(key);      // must resolve BEFORE indexing -- see below
        packProgress[index].seconds = value;
    }

    /// <summary>How many times the hint button has been used on each level. Null until a hint is
    /// first taken on that pack. The legacy linear campaigns never gained this column -- their
    /// levels predate the hint system's stored answer and never enable the button at all (see
    /// GAME_EXPANSION_PLAN §6.41), so there is nothing for them to record.</summary>
    public int[] HintsForKey(string key)
    {
        if (key == LegacyClassicKey || key == LegacyAdvancedKey) { return null; }

        int found = FindPack(key);
        return found < 0 ? null : packProgress[found].hints;
    }

    public void SetHintsForKey(string key, int[] value)
    {
        if (key == LegacyClassicKey || key == LegacyAdvancedKey) { return; }
        int index = PackIndex(key);      // must resolve BEFORE indexing -- see below
        packProgress[index].hints = value;
    }

    /// <summary>Fewest moves each level has ever been solved in. 0 (JsonUtility's own default,
    /// same as an old save that predates this field) means no record yet -- the Level Complete
    /// screen's "OLD BEST" card treats that as "no prior record" rather than a real 0-move solve.
    /// Same legacy-campaign exclusion as <see cref="HintsForKey"/>, for the same reason.</summary>
    public int[] BestMovesForKey(string key)
    {
        if (key == LegacyClassicKey || key == LegacyAdvancedKey) { return null; }

        int found = FindPack(key);
        return found < 0 ? null : packProgress[found].bestMoves;
    }

    public void SetBestMovesForKey(string key, int[] value)
    {
        if (key == LegacyClassicKey || key == LegacyAdvancedKey) { return; }
        int index = PackIndex(key);      // must resolve BEFORE indexing -- see below
        packProgress[index].bestMoves = value;
    }

    // ---- per-mechanic skill --------------------------------------------------------------
    //
    // A first pass, deliberately as simple as DifficultyAnalyzer's own first-pass weights
    // (GAME_EXPANSION_PLAN Phase 5): completions per attempt, 0-100, per mechanic and overall.
    // Not a Glicko2-style rating against each puzzle's own difficulty -- the doc's own §6.32
    // aspiration -- because that needs a per-level difficulty rating to play against, and
    // DifficultyAnalyzer.Score is explicitly NOT that yet (see the open questions). This is the
    // honest, immediately-available proxy: a mechanic the player finishes almost every attempt on
    // is one they have mastered, and one that eats many attempts per completion is not.
    //
    // Classic carries no mechanic at all, so its attempts/completions land under BasicFlowKey
    // (LevelMechanics.BasicFlowKey) -- pure routing is tracked as its own skill, not omitted.
    // A board combining several mechanics (Advanced, once it ships more than one per level)
    // counts as an attempt/completion of EACH mechanic it contains; that double-counts a single
    // play across several rows on purpose, the same way DifficultyAnalyzer's necessity checks
    // treat each mechanic instance as its own question.

    /// <summary>Index of <paramref name="mechanic"/>'s entry, creating it at 0/0 if this is the
    /// first time it has been seen. Mirrors <see cref="PackIndex"/> for the same reason: growing
    /// the array and indexing it must happen in that order, not in one expression.</summary>
    private int MechanicIndex(string mechanic)
    {
        if (mechanicSkills == null) { mechanicSkills = new MechanicSkill[0]; }

        for (int i = 0; i < mechanicSkills.Length; i++)
        {
            if (mechanicSkills[i].mechanic == mechanic) { return i; }
        }

        MechanicSkill[] grown = new MechanicSkill[mechanicSkills.Length + 1];
        System.Array.Copy(mechanicSkills, grown, mechanicSkills.Length);
        grown[mechanicSkills.Length] = new MechanicSkill { mechanic = mechanic };
        mechanicSkills = grown;
        return mechanicSkills.Length - 1;
    }

    public void RecordMechanicAttempt(string mechanic)
    {
        int index = MechanicIndex(mechanic);     // must resolve BEFORE indexing -- see above
        mechanicSkills[index].attempts++;
    }

    public void RecordMechanicCompletion(string mechanic)
    {
        int index = MechanicIndex(mechanic);     // must resolve BEFORE indexing -- see above
        mechanicSkills[index].completions++;
    }

    /// <summary>0-100 completion rate for one mechanic, or 0 before it has been attempted --
    /// indistinguishable from "attempted and always failed to finish", which cannot actually
    /// happen (an abandoned attempt never completes, but it also never regresses the rate below
    /// what finished attempts already earned).</summary>
    public float MechanicSkillRating(string mechanic)
    {
        if (mechanicSkills == null) { return 0f; }

        for (int i = 0; i < mechanicSkills.Length; i++)
        {
            if (mechanicSkills[i].mechanic != mechanic) { continue; }
            if (mechanicSkills[i].attempts == 0) { return 0f; }
            return 100f * mechanicSkills[i].completions / mechanicSkills[i].attempts;
        }

        return 0f;
    }

    /// <summary>0-100 completion rate pooled across every mechanic seen so far, including
    /// <see cref="FreeFlow.GamePlay.LevelMechanics.BasicFlowKey"/>. The single number a
    /// level-select or daily-challenge screen wants when it asks "how good is this player",
    /// per GAME_EXPANSION_PLAN Phase 10's own stated need.</summary>
    public float OverallSkillRating()
    {
        if (mechanicSkills == null || mechanicSkills.Length == 0) { return 0f; }

        int totalAttempts = 0, totalCompletions = 0;
        for (int i = 0; i < mechanicSkills.Length; i++)
        {
            totalAttempts += mechanicSkills[i].attempts;
            totalCompletions += mechanicSkills[i].completions;
        }

        return totalAttempts == 0 ? 0f : 100f * totalCompletions / totalAttempts;
    }

    /// <summary>Index of an existing pack entry, or -1. Does not create -- reads must not mutate.</summary>
    private int FindPack(string key)
    {
        if (packProgress == null) { return -1; }
        for (int i = 0; i < packProgress.Length; i++)
        {
            if (packProgress[i].key == key) { return i; }
        }
        return -1;
    }

    /// <summary>
    /// The entry for <paramref name="key"/>, created empty if this pack has never been played.
    /// Returns an index rather than the struct because PackProgress is a value type -- handing back
    /// a copy would silently discard every write.
    /// </summary>
    public int PackIndex(string key)
    {
        if (packProgress == null) { packProgress = new PackProgress[0]; }

        for (int i = 0; i < packProgress.Length; i++)
        {
            if (packProgress[i].key == key) { return i; }
        }

        PackProgress[] grown = new PackProgress[packProgress.Length + 1];
        System.Array.Copy(packProgress, grown, packProgress.Length);
        grown[packProgress.Length] = new PackProgress { key = key };
        packProgress = grown;
        return packProgress.Length - 1;
    }
}

/// <summary>One of a day's daily challenges: which level DailyChallengeSelector drew for that
/// slot, and whether it has been finished yet.
///
/// Pick and solved-state live in ONE entry rather than in parallel arrays so they cannot drift
/// out of length with each other -- a solved flag that outlived the pick it belonged to would
/// silently credit the wrong board. Mirrors DailyChallengeSelector.Pick's three fields rather
/// than reusing that type directly, because SaveData is plain serialisable data with no
/// dependency on the gameplay assembly's selection logic.</summary>
[System.Serializable]
public struct DailyPick
{
    public FreeFlow.Enums.GameMode mode;
    public int packSize;
    public int levelNumber;
    public bool solved;
}

/// <summary>Everything remembered about one pack: how far the player got, and the telemetry
/// §6.32 added so difficulty can eventually be fitted against real play rather than a prior.</summary>
[System.Serializable]
public struct PackProgress
{
    public string key;              // "Classic7x7", "Advanced6x6"
    public int completedLevel;
    public int[] attempts;
    public float[] seconds;
    public int[] hints;             // how many times the hint button was used, per level
    public int[] bestMoves;         // fewest moves this level has ever been solved in; 0 = no record yet
}

/// <summary>One mechanic's lifetime attempts/completions, pooled across every pack and mode it
/// appears in. <see cref="mechanic"/> is one of the stable keys LevelMechanics.Keys returns
/// (e.g. "Bridge", or LevelMechanics.BasicFlowKey for mechanic-free boards) -- never a display
/// string, so a future re-wording of the HUD label cannot silently split one mechanic's history
/// into two entries.</summary>
[System.Serializable]
public struct MechanicSkill
{
    public string mechanic;
    public int attempts;
    public int completions;
}

[System.Serializable]
public struct AudioData
{
    public bool isMusicMute;
    public bool isSoundMute;
    public float musicVolume;
    public float soundVolume;
}
