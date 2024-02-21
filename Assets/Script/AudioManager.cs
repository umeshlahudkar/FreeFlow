using UnityEngine;
using FreeFlow.Util;

public class AudioManager : Singleton<AudioManager>
{
    [SerializeField] private AudioSource bgAudioSource;
    [SerializeField] private AudioSource sfxAudioSource;

    [SerializeField] private AudioClip buttonClickClip;
    [SerializeField] private AudioClip blockSelectClip;
    [SerializeField] private AudioClip pairCompleteClip;

    private float bgVolume = 0.5f;
    private float sfxVolume = 0.5f;

    private bool isBgMute = false;
    private bool isSfxMute = false;

    public float BgVolume { get { return bgVolume; } }
    public float SFXVolume { get { return sfxVolume; } }

    public bool IsBgMute { get { return isBgMute; } }
    public bool IsSFXMute { get { return isSfxMute; } }

    private void Start()
    {
        AudioData data = SavingSystem.Instance.Load().audioData;

        isBgMute = data.isMusicMute;
        isSfxMute = data.isSoundMute;
        bgVolume = data.musicVolume;
        sfxVolume = data.soundVolume;

        bgAudioSource.mute = isBgMute;
        sfxAudioSource.mute = isSfxMute;
       
        bgAudioSource.volume = bgVolume;
        sfxAudioSource.volume = sfxVolume;
    }

    public void UpdateBgVolume(float volume)
    {
        isBgMute = (volume <= 0);
        bgAudioSource.mute = isBgMute;

        bgVolume = volume;
        bgVolume = Mathf.Clamp(bgVolume, 0, 1);
        bgAudioSource.volume = bgVolume;

        SaveAudioData();
    }

    public void UpdateSFXVolume(float volume)
    {
        isSfxMute = (volume <= 0);
        sfxAudioSource.mute = isSfxMute;

        sfxVolume = volume;
        sfxVolume = Mathf.Clamp(sfxVolume, 0, 1);
        sfxAudioSource.volume = sfxVolume;
        
        SaveAudioData();
    }

    public void PlayButtonClickSound()
    {
        if(!isSfxMute)
        {
            sfxAudioSource.Stop();
            sfxAudioSource.clip = buttonClickClip;
            sfxAudioSource.Play();
        }
    }

   
    public void PlayBlockSelectSound()
    {
        if (!isSfxMute)
        {
            sfxAudioSource.Stop();
            sfxAudioSource.clip = blockSelectClip;
            sfxAudioSource.Play();
        }
    }

   
    public void PlayPairCompleteSound()
    {
        if (!isSfxMute)
        {
            sfxAudioSource.Stop();
            sfxAudioSource.clip = pairCompleteClip;
            sfxAudioSource.Play();
        }
    }

    private void SaveAudioData()
    {
        SaveData saveData = SavingSystem.Instance.Load();

        saveData.audioData.isMusicMute = isBgMute;
        saveData.audioData.isSoundMute = isSfxMute;
        saveData.audioData.musicVolume = bgVolume;
        saveData.audioData.soundVolume = sfxVolume;

        SavingSystem.Instance.Save(saveData);
    }
}

