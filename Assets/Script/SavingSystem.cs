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
}

[System.Serializable]
public struct AudioData
{
    public bool isMusicMute;
    public bool isSoundMute;
    public float musicVolume;
    public float soundVolume;
}
