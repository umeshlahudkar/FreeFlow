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
}

[System.Serializable]
public struct AudioData
{
    public bool isMusicMute;
    public bool isSoundMute;
    public float musicVolume;
    public float soundVolume;
}
