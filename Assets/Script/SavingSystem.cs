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
            data.username = string.Empty;
            data.avtarIndex = 0;
            data.coins = 0;

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
    public string username;
    public int avtarIndex;
    public int coins;

    public AudioData audioData;
}

[System.Serializable]
public struct AudioData
{
    public bool isMusicMute;
    public bool isSoundMute;
    public float musicVolume;
    public float soundVolume;
}
