using UnityEngine;
using System.IO;
using FreeFlow.Util;

/// <summary>
/// Local device state -- audio, vibration, the hint-button visibility toggle, and which mechanic
/// intro cards have already been shown -- kept in its own file, separate from <see cref="SaveData"/>.
/// None of this is player progress a server needs to know about: the Settings-screen preferences
/// are per-device by choice, and the mechanic-teaching flag is UI state (has this device's player
/// already seen the Bridge card), not an achievement. Keeping both out of SaveData means
/// SavingSystem stays exactly what a future server sync would upload, with nothing here riding
/// along uninvited -- and "reset progress" does not silently reset any of this either.
/// </summary>
public class SettingsSystem : Singleton<SettingsSystem>
{
    private readonly string fileName = "Settings.json";
    private string filePath = string.Empty;

    private void Awake()
    {
        filePath = Path.Combine(Application.persistentDataPath, fileName);
        if (!File.Exists(filePath))
        {
            SettingsData data = new();

            data.audioData.isMusicMute = false;
            data.audioData.isSoundMute = false;
            data.audioData.musicVolume = 0.5f;
            data.audioData.soundVolume = 0.5f;

            data.vibrationEnabled = true;
            data.showHintButton = true;

            Save(data);
        }
    }

    public void Save(SettingsData data)
    {
        string jsonData = JsonUtility.ToJson(data);
        File.WriteAllText(filePath, jsonData);
    }

    public SettingsData Load()
    {
        if (File.Exists(filePath))
        {
            string jsonData = File.ReadAllText(filePath);
            return JsonUtility.FromJson<SettingsData>(jsonData);
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
    // and testing with AND is all "have I met X" ever needed. Lives here, not in SaveData: which
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
