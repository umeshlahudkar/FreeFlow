using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class SettingScreen : MonoBehaviour
{
    [Header("Music")]
    [SerializeField] private Slider musicVolumeSlider;

    [Header("SFX")]
    [SerializeField] private Slider soundVolumeSlider;

    private void OnEnable()
    {
        musicVolumeSlider.value = AudioManager.Instance.BgVolume;
        soundVolumeSlider.value = AudioManager.Instance.SFXVolume;
    }

    public void OnMusicSliderValueChanged()
    {
        AudioManager.Instance.UpdateBgVolume(musicVolumeSlider.value);
    }

    public void OnSoundSliderValueChanged()
    {
        AudioManager.Instance.UpdateSFXVolume(soundVolumeSlider.value);
    }

    public void OnCloseButtonClick()
    {
        AudioManager.Instance.PlayButtonClickSound();
        gameObject.Deactivate();
    }
}
