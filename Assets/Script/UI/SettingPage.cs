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

    // Developer-only: shorten the daily-challenge period so a reset can be watched in seconds
    // instead of waiting for UTC midnight. The fields are declared unconditionally so the scene
    // wiring stays valid in every build configuration -- it is the SECTION and the LOGIC that are
    // gated, in RefreshDeveloperSection and OnDailyResetSecondsChanged. In a build without DEBUG
    // the section is switched off and the handler compiles away to nothing.
    [Header("Developer (DEBUG builds only)")]
    [SerializeField] private GameObject developerSection;
    [SerializeField] private TMP_InputField dailyResetSecondsInput;
    [SerializeField] private TextMeshProUGUI dailyResetStatusText;

    // Upper case to match the other rows on this screen ("SHARE PATHZA", "PRIVACY POLICY").
    private const string ResetPromptText = "RESET ALL PROGRESS";
    private const string ResetConfirmText = "TAP AGAIN TO CONFIRM";
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
            versionText.text = "PATHZA " + Application.version;
        }
        ResetConfirmState();
        RefreshDeveloperSection();
    }

    /// <summary>Shows the developer section and fills it from the current override, or -- in a
    /// build without the DEBUG symbol -- switches the whole thing off so a player can never see
    /// it. Unity defines DEBUG in the Editor and in Development Builds, and not in a release
    /// build, which is exactly the split wanted here.</summary>
    private void RefreshDeveloperSection()
    {
        if (developerSection == null) { return; }

#if DEBUG
        developerSection.SetActive(true);

        int seconds = FreeFlow.GamePlay.DailyChallengeSelector.DebugDayLengthSeconds;
        if (dailyResetSecondsInput != null)
        {
            // WithoutNotify: filling the box must not read as the developer having typed in it,
            // which would write the value straight back and fight whatever they are mid-edit on.
            dailyResetSecondsInput.SetTextWithoutNotify(seconds > 0 ? seconds.ToString() : "");
        }
        SetDailyResetStatus(seconds);
#else
        developerSection.SetActive(false);
#endif
    }

    /// <summary>Applies a typed daily-reset period, in seconds. Blank or 0 restores real calendar
    /// days. Takes effect immediately: everything about the daily challenge keys off
    /// DailyChallengeSelector.DayIndex, so the next time any screen asks what day it is, it gets
    /// the compressed answer -- and the hub, which re-checks once a second, rebuilds itself on the
    /// next period boundary without needing to be reopened.</summary>
    public void OnDailyResetSecondsChanged(string value)
    {
#if DEBUG
        int seconds;
        if (!int.TryParse(value, out seconds) || seconds < 0) { seconds = 0; }

        FreeFlow.GamePlay.DailyChallengeSelector.DebugDayLengthSeconds = seconds;
        SetDailyResetStatus(seconds);
#endif
    }

#if DEBUG
    private void SetDailyResetStatus(int seconds)
    {
        if (dailyResetStatusText == null) { return; }

        dailyResetStatusText.text = seconds > 0
            ? "Daily resets every " + seconds + "s  (debug override)"
            : "Daily resets at UTC midnight  (normal)";
    }
#endif

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

    /// <summary>The General card's Share row: promotes the game itself, any time, with no result
    /// attached -- see ShareService.ShareGame. The level-complete screen's own Share button is a
    /// different share (a result, with a card image), not this one with different text.</summary>
    public void OnShareGameClick()
    {
        if (!FreeFlow.Input.InputManager.Instance.CanInput()) { return; }
        AudioManager.Instance.PlayButtonClickSound();

        FreeFlow.Share.ShareService.Instance.ShareGame();
    }

    /// <summary>
    /// Deletes the entire save file -- every pack's progress, the daily-challenge streaks and
    /// history, and the audio/vibration/hint preferences -- then reloads the scene so the whole
    /// game rebuilds from the defaults SavingSystem.Awake writes for a first run. Nothing here is
    /// recoverable afterwards.
    ///
    /// Which is why it takes two taps within <see cref="ResetConfirmWindow"/>: the first arms it
    /// and changes the label to say so, and the window lapses on its own if the player thinks
    /// better of it. Leaving and reopening Settings disarms it too (see OnEnable). A single stray
    /// tap on a row sitting directly under "PRIVACY POLICY" must never be able to wipe a save.
    /// </summary>
    public void OnResetProgressClick()
    {
        if (!FreeFlow.Input.InputManager.Instance.CanInput()) { return; }
        AudioManager.Instance.PlayButtonClickSound();

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

    /// <summary>Lets the armed state lapse on its own, so a confirm prompt left on screen does not
    /// stay live indefinitely waiting for a second tap that was never coming.</summary>
    private void Update()
    {
        if (resetArmed && Time.unscaledTime > resetArmedUntil) { ResetConfirmState(); }
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
