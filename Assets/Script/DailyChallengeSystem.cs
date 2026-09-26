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
    // How many months of history to keep -- must match DailyChallengePage.maxMonthsBack, since
    // that is exactly how far back the calendar ever lets the player browse. Nothing older is
    // reachable from the UI at all, so there is no reason to let this list grow forever the
    // longer someone keeps playing.
    private const int RetentionMonths = 6;

    // Absolute day indices (DailyChallengeSelector.DayIndex/EpochUtc) -- a List rather than a
    // Dictionary because JsonUtility cannot serialise the latter at all, and this only ever needs
    // "was day N completed", never a value per day. Pruned to the last RetentionMonths calendar
    // months every time a new day is added -- see MarkDayCompleted.
    public List<int> completedDayIndices;

    public bool IsDayCompleted(int dayIndex)
    {
        return completedDayIndices != null && completedDayIndices.Contains(dayIndex);
    }

    /// <summary>Marks one calendar day's single challenge solved, then prunes anything older than
    /// the calendar's own browsable floor. Idempotent -- replaying an already-completed day is a
    /// no-op, including the prune (nothing changed, so there's nothing new to prune for).</summary>
    public void MarkDayCompleted(int dayIndex)
    {
        if (IsDayCompleted(dayIndex)) { return; }
        if (completedDayIndices == null) { completedDayIndices = new List<int>(); }
        completedDayIndices.Add(dayIndex);
        PruneBeyondRetention();
    }

    /// <summary>Drops every completed-day entry older than the earliest month the calendar screen
    /// still lets the player browse to -- computed from the REAL current date, not
    /// <see cref="MarkDayCompleted"/>'s own <c>dayIndex</c> parameter, since a completion can be
    /// for a past backlog day rather than today. Mirrors DailyChallengePage.IsEarliestMonthDisplayed's
    /// own month-floor arithmetic exactly, so a day is never pruned while it's still reachable from
    /// the UI. Reconstructs dates via EpochUtc.AddDays/subtraction rather than DailyChallengeSelector.DayIndex
    /// for the reverse conversion -- DayIndex has a separate dev-only "compressed day" mode (see its
    /// own doc comment) that is not the same arithmetic AddDays' inverse needs.</summary>
    private void PruneBeyondRetention()
    {
        if (completedDayIndices == null || completedDayIndices.Count == 0) { return; }

        int todayIndex = FreeFlow.GamePlay.DailyChallengeSelector.DayIndex(System.DateTime.UtcNow);
        System.DateTime today = FreeFlow.GamePlay.DailyChallengeSelector.EpochUtc.AddDays(todayIndex);
        System.DateTime firstOfCurrentMonth = new System.DateTime(today.Year, today.Month, 1, 0, 0, 0, System.DateTimeKind.Utc);
        System.DateTime earliestKeptMonth = firstOfCurrentMonth.AddMonths(-(RetentionMonths - 1));
        int cutoffDayIndex = (int)(earliestKeptMonth - FreeFlow.GamePlay.DailyChallengeSelector.EpochUtc).TotalDays;

        completedDayIndices.RemoveAll(d => d < cutoffDayIndex);
    }
}
