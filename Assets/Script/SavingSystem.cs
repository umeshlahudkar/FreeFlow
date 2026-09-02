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
    public const int CurrentSchemaVersion = 1;
    public int schemaVersion;

    /// <summary>Brings a save from whatever <see cref="schemaVersion"/> it was written at up to
    /// <see cref="CurrentSchemaVersion"/>. Called once, by <c>SavingSystem.Load</c>, before the
    /// data reaches any gameplay code -- callers should never need to know a save was old.</summary>
    public static void Migrate(ref SaveData data)
    {
        // 0 -> 1: added schemaVersion itself, per-mechanic skill tracking, and per-level hint
        // counts. All three are additive fields JsonUtility already defaulted to null/0/false on
        // load, so there is nothing to transform -- only the version number itself needs setting.
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

    public AudioData audioData;

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
