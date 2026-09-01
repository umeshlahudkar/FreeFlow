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
            data.completedLevel = 0;
            data.completedlevelMoves = null;

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
            return JsonUtility.FromJson<SaveData>(jsonData);
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
    // Classic's progress deliberately keeps the ORIGINAL field names. JsonUtility fills any field
    // missing from an existing save with its default, so a save written before the two modes
    // existed would silently reset whichever campaign got renamed. Classic is the default mode and
    // the one a returning player is most likely mid-way through, so it inherits the old fields and
    // the old progress; Advanced starts empty, which is correct -- it did not exist before.
    public int completedLevel;
    public int[] completedlevelMoves;

    public int advancedCompletedLevel;
    public int[] advancedCompletedLevelMoves;

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

    /// <summary>Per-level move counts for <paramref name="mode"/>. Null until that mode is played.</summary>
    public int[] MovesFor(FreeFlow.Enums.GameMode mode)
    {
        return mode == FreeFlow.Enums.GameMode.Advanced ? advancedCompletedLevelMoves : completedlevelMoves;
    }

    public void SetMovesFor(FreeFlow.Enums.GameMode mode, int[] value)
    {
        if (mode == FreeFlow.Enums.GameMode.Advanced) { advancedCompletedLevelMoves = value; }
        else { completedlevelMoves = value; }
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
    // rather than style. Written as `packProgress[PackIndex(key)].moves = value`, C# evaluates the
    // ARRAY REFERENCE first, then calls PackIndex -- which allocates a larger array and assigns it
    // to the field. The indexer then writes through the reference captured a moment earlier, so on
    // a save that has never held a pack that reference is null and the assignment throws.
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

    public int[] MovesForKey(string key)
    {
        if (key == LegacyClassicKey) { return completedlevelMoves; }
        if (key == LegacyAdvancedKey) { return advancedCompletedLevelMoves; }

        int found = FindPack(key);
        return found < 0 ? null : packProgress[found].moves;
    }

    public void SetMovesForKey(string key, int[] value)
    {
        if (key == LegacyClassicKey) { completedlevelMoves = value; return; }
        if (key == LegacyAdvancedKey) { advancedCompletedLevelMoves = value; return; }
        int index = PackIndex(key);      // must resolve BEFORE indexing -- see below
        packProgress[index].moves = value;
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
    public int[] moves;
    public int[] attempts;
    public float[] seconds;
}

[System.Serializable]
public struct AudioData
{
    public bool isMusicMute;
    public bool isSoundMute;
    public float musicVolume;
    public float soundVolume;
}
