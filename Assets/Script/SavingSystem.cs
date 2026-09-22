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
            data.hintsRemaining = -1;   // -1 = never granted -- see SaveData.hintsRemaining

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
    // Hints are spendable: each one taken costs one from this, and at zero the hint button stops
    // being interactable.
    //
    // -1 means never granted -- distinct from a real, spendable 0 -- so a player who has spent
    // every hint is not handed a fresh set on the next level load, and a save written before this
    // field existed (which JsonUtility would otherwise default to 0) is not mistaken for one that
    // already has an empty wallet. UIController.EnsureHintBalance is the only thing allowed to
    // move it off -1, and only ever once per save.
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
}

/// <summary>Everything remembered about one pack: how far the player got.</summary>
[System.Serializable]
public struct PackProgress
{
    public string key;              // "Classic7x7", "Advanced6x6"
    public int completedLevel;
}
