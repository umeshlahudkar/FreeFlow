using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FreeFlow.Enums;

namespace FreeFlow.UI
{
public class SettingPage : Page
{
    [SerializeField] private TopPanel topPanel;

    [Header("Music")]
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private TextMeshProUGUI musicVolumeLabel;

    [Header("Sound")]
    [SerializeField] private Slider soundVolumeSlider;
    [SerializeField] private TextMeshProUGUI soundVolumeLabel;

    [Header("Gameplay Toggles")]
    [SerializeField] private Toggle vibrationToggle;
    [SerializeField] private Image vibrationTrackImage;
    [SerializeField] private RectTransform vibrationKnob;

    [SerializeField] private Toggle showHintButtonToggle;
    [SerializeField] private Image showHintButtonTrackImage;
    [SerializeField] private RectTransform showHintButtonKnob;

    [SerializeField] private Sprite toggleTrackOnSprite;
    [SerializeField] private Sprite toggleTrackOffSprite;
    [SerializeField] private float toggleKnobInset = 6f;

    [Header("Footer")]
    [SerializeField] private TextMeshProUGUI versionText;
    [SerializeField] private TextMeshProUGUI resetProgressText;

    private const string ResetPromptText = "Reset all progress";
    private const string ResetConfirmText = "Tap again to confirm";
    private const float ResetConfirmWindow = 3f;

    private bool resetArmed;
    private float resetArmedUntil;

    private void OnEnable()
    {
        // No Setting button here (already on this screen) and no Option -- Back doubles as this
        // overlay's own close action (see OnCloseButtonClick), wired on this instance instead of
        // the generic TopPanel.OnBackButtonClick since this is an overlay (CloseOverlay), not a
        // stacked page (ClosePage would incorrectly pop whatever page is underneath instead).
        if (topPanel != null) { topPanel.SetTopPanel("SETTINGS", "", showBack: true, showSetting: false, showOption: false); }

        musicVolumeSlider.value = AudioManager.Instance.BgVolume;
        soundVolumeSlider.value = AudioManager.Instance.SFXVolume;
        UpdateVolumeLabel(musicVolumeLabel, musicVolumeSlider.value);
        UpdateVolumeLabel(soundVolumeLabel, soundVolumeSlider.value);

        SaveData data = SavingSystem.Instance.Load();
        if (vibrationToggle != null)
        {
            vibrationToggle.SetIsOnWithoutNotify(data.vibrationEnabled);
            SetToggleVisual(vibrationTrackImage, vibrationKnob, data.vibrationEnabled);
        }
        if (showHintButtonToggle != null)
        {
            showHintButtonToggle.SetIsOnWithoutNotify(data.showHintButton);
            SetToggleVisual(showHintButtonTrackImage, showHintButtonKnob, data.showHintButton);
        }

        if (versionText != null)
        {
            versionText.text = "PATHZA " + Application.version + " · OFFLINE";
        }
        ResetConfirmState();
    }

    public void OnMusicSliderValueChanged()
    {
        AudioManager.Instance.UpdateBgVolume(musicVolumeSlider.value);
        UpdateVolumeLabel(musicVolumeLabel, musicVolumeSlider.value);
    }

    public void OnSoundSliderValueChanged()
    {
        AudioManager.Instance.UpdateSFXVolume(soundVolumeSlider.value);
        UpdateVolumeLabel(soundVolumeLabel, soundVolumeSlider.value);
    }

    private void UpdateVolumeLabel(TextMeshProUGUI label, float value)
    {
        if (label == null) { return; }
        label.text = Mathf.RoundToInt(value * 100f) + "%";
    }

    // Persisted through SavingSystem (like audioData) rather than PlayerPrefs, so every player
    // preference lives in one save file. Nothing triggers an actual vibration anywhere in the
    // codebase yet -- this only stores the preference for whenever that lands.
    public void OnVibrationToggleChanged(bool isOn)
    {
        SaveData data = SavingSystem.Instance.Load();
        data.vibrationEnabled = isOn;
        SavingSystem.Instance.Save(data);
        SetToggleVisual(vibrationTrackImage, vibrationKnob, isOn);
    }

    public void OnShowHintButtonToggleChanged(bool isOn)
    {
        SaveData data = SavingSystem.Instance.Load();
        data.showHintButton = isOn;
        SavingSystem.Instance.Save(data);
        SetToggleVisual(showHintButtonTrackImage, showHintButtonKnob, isOn);
    }

    // Unity's stock Toggle has no built-in support for "swap the whole track sprite and slide a
    // separate knob" -- that's a custom on/off switch look, not a checkbox -- so this does both
    // halves by hand. Knob X is derived from the track's own rendered width rather than hardcoded,
    // so it stays correct if the track is ever resized.
    private void SetToggleVisual(Image trackImage, RectTransform knob, bool isOn)
    {
        if (trackImage == null || knob == null) { return; }

        trackImage.sprite = isOn ? toggleTrackOnSprite : toggleTrackOffSprite;

        float trackWidth = trackImage.rectTransform.rect.width;
        float knobRadius = knob.rect.width * 0.5f;
        float x = (trackWidth * 0.5f) - knobRadius - toggleKnobInset;
        knob.anchoredPosition = new Vector2(isOn ? x : -x, knob.anchoredPosition.y);
    }

    // Requires two taps within ResetConfirmWindow so a single stray tap can never wipe a save.
    public void OnResetProgressClick()
    {
        if (resetArmed && Time.unscaledTime <= resetArmedUntil)
        {
            SavingSystem.Instance.DeleteFile();
            ResetConfirmState();
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            return;
        }

        resetArmed = true;
        resetArmedUntil = Time.unscaledTime + ResetConfirmWindow;
        if (resetProgressText != null) { resetProgressText.text = ResetConfirmText; }
    }

    private void ResetConfirmState()
    {
        resetArmed = false;
        if (resetProgressText != null) { resetProgressText.text = ResetPromptText; }
    }

    public void OnCloseButtonClick()
    {
        AudioManager.Instance.PlayButtonClickSound();
        PageManager.Instance.CloseOverlay(PageType.Setting);
    }
}
}
