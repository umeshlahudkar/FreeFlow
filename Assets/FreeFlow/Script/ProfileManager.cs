using UnityEngine;
using System;
using System.Collections.Generic;
using FreeFlow.Core.Services;
using FreeFlow.Util;

/// <summary>
/// The single place all of this project's local save data lives, in two files:
///
/// - PROGRESS (<see cref="PlayerProgress"/>, SaveData.json) -- pack completion, hint balance, and
///   daily-challenge history. Everything here is the same kind of data and will eventually sync
///   through a server together, so it is one struct and one file rather than several.
/// - SETTINGS (<see cref="SettingsData"/>, Settings.json) -- audio, vibration, the hint-button
///   visibility toggle, and which mechanic intro cards have already been shown. Kept in its own
///   file, deliberately never merged with PROGRESS: none of it is player progress a server needs
///   to know about, so "reset progress" must never touch it, and a future server sync must never
///   upload it either.
///
/// Both are read once -- in <see cref="Initialize"/> -- and kept in memory after that. Where they
/// are actually stored is this manager's <see cref="ISaveStorage"/>, built from the build's
/// ServiceConfig: files on the device for Android and iOS.
/// LoadProgress/LoadSettings hand out the cached copy instead of a fresh file read.
///
/// SettingsData is all value types (bools, floats, an enum), so a caller's copy from LoadSettings
/// is genuinely independent -- it can mutate its own copy freely and hand it to SaveSettings the
/// same load-mutate-save way as before. PlayerProgress is not: packProgress (an array) and
/// completedDayIndices (a List) are reference types, so a caller's "copy" from a plain load would
/// still point at the SAME array/list the cache holds. Progress is therefore never handed out for
/// mutation, and no PlayerProgress ever comes back IN from outside either -- callers reach for one
/// of the specific Update.../Grant.../Spend... methods below, which apply the change to the cache
/// directly and persist it, and LoadProgress is for reading only.
///
/// Implements IInitializable so a scene's GameBootstrap loads both files before anything else in
/// the game reads them. On the device that is a synchronous file read; a storage backend that has to
/// fetch its data first (a cloud save) does it in Initialize, without any caller changing.
/// </summary>
public class ProfileManager : Singleton<ProfileManager>, IInitializable
{
    // What a fresh save starts with. Lives here, not in some gameplay screen's Inspector field --
    // this is the one place a save is ever CREATED, so it is the one place that gets to decide
    // what it starts holding.
    private const int StartingHintCount = 3;

    private readonly string progressFileName = "SaveData.json";
    private readonly string settingsFileName = "Settings.json";

    // Where the slots live, built once from the ServiceConfig the first time it is needed.
    private ISaveStorage storage;

    private ISaveStorage Storage
    {
        get
        {
            if (storage == null) { storage = ServiceConfig.Current.CreateSaveStorage(); }
            return storage;
        }
    }

    private PlayerProgress cachedProgress;
    private SettingsData cachedSettings;
    private bool progressLoaded;
    private bool settingsLoaded;

    /// <summary>Loads both files into memory. GameBootstrap calls this before anything else
    /// touches player progress or settings; every Load/Save below also loads lazily on first use,
    /// so this manager still works correctly even if it is reached before Initialize runs.
    ///
    /// The caches are re-read once the storage has loaded: a backend that fetches its data hands
    /// out first-run defaults to anything that reads early, and the real save must replace them.
    /// For files on the device the re-read finds exactly what the cache already holds.</summary>
    public void Initialize(Action onComplete)
    {
        Storage.Load(() =>
        {
            progressLoaded = false;
            settingsLoaded = false;
            EnsureProgressLoaded();
            EnsureSettingsLoaded();
            onComplete?.Invoke();
        });
    }

    // ---- progress: pack completion, hint balance, daily-challenge history --------------------

    private void EnsureProgressLoaded()
    {
        if (progressLoaded) { return; }
        progressLoaded = true;

        bool dirty;
        PlayerProgress data;
        if (Storage.TryRead(progressFileName, out string json))
        {
            data = JsonUtility.FromJson<PlayerProgress>(json);

            // A save written before schemaVersion existed reads as 0 (JsonUtility's int default),
            // which is indistinguishable from "genuinely on version 0" -- exactly the property a
            // migration seam needs.
            dirty = data.schemaVersion < PlayerProgress.CurrentSchemaVersion;
            if (dirty) { PlayerProgress.Migrate(ref data); }
        }
        else
        {
            data = new PlayerProgress();
            data.schemaVersion = PlayerProgress.CurrentSchemaVersion;
            data.hintsRemaining = StartingHintCount;
            dirty = true;
        }

        cachedProgress = data;
        if (dirty) { WriteProgressFile(); }
    }

    /// <summary>For reading. Do not mutate the result -- packProgress/completedDayIndices are the
    /// SAME array/list the cache holds (see this class's own doc comment), so a mutation here would
    /// reach the cache without ever going through one of the Update.../Grant.../Spend... methods
    /// below. Those are the only way to change progress.</summary>
    public PlayerProgress LoadProgress()
    {
        EnsureProgressLoaded();
        return cachedProgress;
    }

    /// <summary>Level-complete (or a developer's pack-unlock override): moves one pack's unlock
    /// frontier under <paramref name="key"/> to <paramref name="completedLevel"/>. Callers decide
    /// WHETHER the frontier may move -- an ordinary completion checks
    /// <see cref="PlayerProgress.PackFrontierAdvances"/> first, a developer override does not need
    /// to -- this only ever applies the write.</summary>
    public void UpdatePackProgress(string key, int completedLevel)
    {
        EnsureProgressLoaded();
        cachedProgress.SetCompletedLevelForKey(key, completedLevel);
        WriteProgressFile();
    }

    /// <summary>Marks one calendar day's daily challenge solved. Idempotent, same as
    /// <see cref="PlayerProgress.MarkDayCompleted"/> itself -- replaying an already-completed day
    /// is a no-op.</summary>
    public void MarkDailyChallengeCompleted(int dayIndex)
    {
        EnsureProgressLoaded();
        cachedProgress.MarkDayCompleted(dayIndex);
        WriteProgressFile();
    }

    /// <summary>Spends one hint -- the balance the hint button counts down. A no-op (but still
    /// persisted the same as any other call, matching every other method here) once the balance is
    /// already at zero.</summary>
    public void SpendHint()
    {
        EnsureProgressLoaded();
        if (cachedProgress.hintsRemaining > 0) { cachedProgress.hintsRemaining--; }
        WriteProgressFile();
    }

    /// <summary>Adds to the hint balance -- the rewarded-ad payout.</summary>
    public void GrantHints(int amount)
    {
        if (amount <= 0) { return; }
        EnsureProgressLoaded();
        cachedProgress.hintsRemaining += amount;
        WriteProgressFile();
    }

    /// <summary>Developer-tools override: sets the hint balance to an exact value, bypassing every
    /// normal rule about how it can change.</summary>
    public void SetHintsRemaining(int amount)
    {
        EnsureProgressLoaded();
        cachedProgress.hintsRemaining = amount;
        WriteProgressFile();
    }

    private void WriteProgressFile()
    {
        Storage.Write(progressFileName, JsonUtility.ToJson(cachedProgress));
    }

    /// <summary>Persists anything the storage still holds unsaved -- PlatformManager calls this when
    /// the host pauses the game. On the device every change is already on disk.</summary>
    public void FlushSave()
    {
        Storage.Flush();
    }

    /// <summary>Wipes pack progress and daily-challenge streaks/history, then puts PlayerProgress back
    /// to its first-run defaults -- used by Settings' "reset progress". Deliberately leaves
    /// Settings.json untouched; see SettingsData's own doc comment for why.</summary>
    public void DeleteAllProgress()
    {
        Storage.Delete(progressFileName);
        progressLoaded = false;
        cachedProgress = default;
    }

    // ---- settings: audio, vibration, hint-button visibility, mechanic teaching ---------------

    private void EnsureSettingsLoaded()
    {
        if (settingsLoaded) { return; }
        settingsLoaded = true;

        if (Storage.TryRead(settingsFileName, out string json))
        {
            cachedSettings = JsonUtility.FromJson<SettingsData>(json);
        }
        else
        {
            SettingsData data = new();
            data.audioData.isMusicMute = false;
            data.audioData.isSoundMute = false;
            data.audioData.musicVolume = 0.5f;
            data.audioData.soundVolume = 0.5f;
            data.vibrationEnabled = true;
            data.showHintButton = true;

            cachedSettings = data;
            WriteSettingsFile();
        }
    }

    public SettingsData LoadSettings()
    {
        EnsureSettingsLoaded();
        return cachedSettings;
    }

    public void SaveSettings(SettingsData data)
    {
        settingsLoaded = true;
        cachedSettings = data;
        WriteSettingsFile();
    }

    private void WriteSettingsFile()
    {
        Storage.Write(settingsFileName, JsonUtility.ToJson(cachedSettings));
    }
}

[System.Serializable]
public struct PlayerProgress
{
    // Bumped whenever a change to this struct needs more than "leave the new field at its
    // JsonUtility default" -- a rename, a unit change, a value that has to be recomputed from
    // what an old save already has. Nothing yet needs that (every field added since this struct
    // shipped -- packProgress, the telemetry arrays, and everything below -- defaults safely, the
    // same pattern GAME_EXPANSION_PLAN §4.4 established for LevelData), so schemaVersion 0->1
    // is a no-op migration. The seam exists so the NEXT structural change has a real place to
    // convert old data instead of inventing versioning under pressure. See PlayerProgress.Migrate.
    public const int CurrentSchemaVersion = 5;
    public int schemaVersion;

    /// <summary>Brings a save from whatever <see cref="schemaVersion"/> it was written at up to
    /// <see cref="CurrentSchemaVersion"/>. Called once, by <c>ProfileManager.LoadProgress</c>,
    /// before the data reaches any gameplay code -- callers should never need to know a save was
    /// old.</summary>
    public static void Migrate(ref PlayerProgress data)
    {
        // 0 -> 1: added schemaVersion itself, per-mechanic skill tracking, and per-level hint
        // counts. All three are additive fields JsonUtility already defaulted to null/0/false on
        // load, so there is nothing to transform -- only the version number itself needs setting.

        data.schemaVersion = CurrentSchemaVersion;
    }

    // Per-PACK progress. Classic ships as five packs of 100 (one per board size) and Advanced is
    // going the same way, so progress can no longer be keyed by mode alone -- finishing 5x5 level 20
    // would otherwise mark 7x7 level 20 complete.
    //
    // An array of keyed entries rather than a dictionary because JsonUtility serialises arrays of
    // serialisable structs and does not serialise dictionaries at all.
    public PackProgress[] packProgress;

    // -- hint balance ------------------------------------------------------------------------
    //
    // One balance for the whole game -- not per level, per pack or per run -- so a hint saved on
    // a Classic 6x6 level is a hint still available in tomorrow's daily challenge. Spendable: each
    // one taken costs one from this, and at zero the hint button stops being interactable.
    //
    // Starts at ProfileManager.StartingHintCount the moment a save is first created (see
    // ProfileManager.EnsureProgressLoaded) -- there is no "never granted" state to track
    // separately from a real, spent-down 0.
    public int hintsRemaining;

    // ---- progress by KEY -------------------------------------------------------------------
    //
    // Every pack lives in packProgress under a key like "Classic7x7". Callers should never build
    // that string themselves -- see UIController.ProgressKey/KeyFor, the only things allowed to.

    // SetCompletedLevelForKey resolves PackIndex into a local before indexing, and that is
    // load-bearing rather than style. Written as `packProgress[PackIndex(key)].completedLevel =
    // value`, C# evaluates the ARRAY REFERENCE first, then calls PackIndex -- which allocates a
    // larger array and assigns it to the field. The indexer then writes through the reference
    // captured a moment earlier, so on a save that has never held a pack that reference is null
    // and the assignment throws. Splitting the call out makes the growth happen first and the
    // write land on the new array.

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
        int found = FindPack(key);
        return found < 0 ? 0 : packProgress[found].completedLevel;
    }

    public void SetCompletedLevelForKey(string key, int value)
    {
        int index = PackIndex(key);      // must resolve BEFORE indexing -- see below
        packProgress[index].completedLevel = value;
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

    // ---- daily challenge history -------------------------------------------------------------

    // How many months of history to keep -- must match DailyChallengePage.maxMonthsBack, since
    // that is exactly how far back the calendar ever lets the player browse. Nothing older is
    // reachable from the UI at all, so there is no reason to let this list grow forever the
    // longer someone keeps playing.
    private const int DailyChallengeRetentionMonths = 6;

    // Absolute day indices (DailyChallengeSelector.DayIndex/EpochUtc) -- a List rather than a
    // Dictionary because JsonUtility cannot serialise the latter at all, and this only ever needs
    // "was day N completed", never a value per day. Pruned to the last DailyChallengeRetentionMonths
    // calendar months every time a new day is added -- see MarkDayCompleted.
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
        PruneDailyChallengeBeyondRetention();
    }

    /// <summary>Drops every completed-day entry older than the earliest month the calendar screen
    /// still lets the player browse to -- computed from the REAL current date, not
    /// <see cref="MarkDayCompleted"/>'s own <c>dayIndex</c> parameter, since a completion can be
    /// for a past backlog day rather than today. Mirrors DailyChallengePage.IsEarliestMonthDisplayed's
    /// own month-floor arithmetic exactly, so a day is never pruned while it's still reachable from
    /// the UI. Reconstructs dates via EpochUtc.AddDays/subtraction rather than DailyChallengeSelector.DayIndex
    /// for the reverse conversion -- DayIndex has a separate dev-only "compressed day" mode (see its
    /// own doc comment) that is not the same arithmetic AddDays' inverse needs.</summary>
    private void PruneDailyChallengeBeyondRetention()
    {
        if (completedDayIndices == null || completedDayIndices.Count == 0) { return; }

        int todayIndex = FreeFlow.GamePlay.DailyChallengeSelector.DayIndex(System.DateTime.UtcNow);
        System.DateTime today = FreeFlow.GamePlay.DailyChallengeSelector.EpochUtc.AddDays(todayIndex);
        System.DateTime firstOfCurrentMonth = new System.DateTime(today.Year, today.Month, 1, 0, 0, 0, System.DateTimeKind.Utc);
        System.DateTime earliestKeptMonth = firstOfCurrentMonth.AddMonths(-(DailyChallengeRetentionMonths - 1));
        int cutoffDayIndex = (int)(earliestKeptMonth - FreeFlow.GamePlay.DailyChallengeSelector.EpochUtc).TotalDays;

        completedDayIndices.RemoveAll(d => d < cutoffDayIndex);
    }
}

/// <summary>Everything remembered about one pack: how far the player got.</summary>
[System.Serializable]
public struct PackProgress
{
    public string key;              // "Classic7x7", "Advanced6x6"
    public int completedLevel;
}

/// <summary>
/// Local device state -- audio, vibration, the hint-button visibility toggle, and which mechanic
/// intro cards have already been shown -- kept in its own file, separate from <see cref="PlayerProgress"/>.
/// None of this is player progress a server needs to know about: the Settings-screen preferences
/// are per-device by choice, and the mechanic-teaching flag is UI state (has this device's player
/// already seen the Bridge card), not an achievement. Keeping both out of PlayerProgress means
/// ProfileManager's progress file stays exactly what a future server sync would upload, with
/// nothing here riding along uninvited -- and "reset progress" does not silently reset any of
/// this either.
/// </summary>
[System.Serializable]
public struct SettingsData
{
    public AudioData audioData;

    // Nothing triggers an actual vibration effect off this itself -- Haptics.Enabled reads it,
    // cached, and every haptic call checks that -- so this field is only ever the source of truth
    // the cache is kept in step with (see SettingPage.OnVibrationToggleChanged).
    public bool vibrationEnabled;

    // Read by GameplayPage to decide whether the gameplay hint button is shown at all,
    // independent of GamePlayController.HintAvailable (which decides whether it is interactable
    // once shown).
    public bool showHintButton;

    // ---- mechanic teaching -----------------------------------------------------------------
    //
    // Gates the first-encounter teaching card (see GamePlayController.FirstUnseenMechanic): a
    // mechanic is "met" the moment a level containing it is attempted, finished or not. A
    // bitmask rather than a per-mechanic record, because MechanicFlags already IS the set of
    // mechanics a board can carry (see LevelMechanics.Identify) -- OR-ing it in once per attempt
    // and testing with AND is all "have I met X" ever needed. Lives here, not in PlayerProgress: which
    // cards this device has already been shown is UI state, not progress a server needs.
    public FreeFlow.GamePlay.MechanicFlags metMechanics;

    /// <summary>Whether <paramref name="mechanic"/> (a key as <see cref="FreeFlow.GamePlay.LevelMechanics.Keys"/>
    /// names it, e.g. "Bridge") has ever been put in front of this player. A key with no
    /// corresponding flag (<see cref="FreeFlow.GamePlay.LevelMechanics.BasicFlowKey"/>) is never
    /// "met", since it names a mechanic-free board rather than a mechanic.</summary>
    public bool HasMetMechanic(string mechanic)
    {
        FreeFlow.GamePlay.MechanicFlags flag = FreeFlow.GamePlay.LevelMechanics.FlagFor(mechanic);
        return flag != FreeFlow.GamePlay.MechanicFlags.None && (metMechanics & flag) != 0;
    }
}

[System.Serializable]
public struct AudioData
{
    public bool isMusicMute;
    public bool isSoundMute;
    public float musicVolume;
    public float soundVolume;
}
