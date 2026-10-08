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

    // The row that opens the developer tools screen (DeveloperPage) -- the daily-reset override,
    // the level unlock, and anything else of that kind. The TOOLS themselves live on their own
    // page; what is left here is the door to it, and switching that door off is what keeps a
    // player from ever reaching them. See RefreshDeveloperSection.
    //
    // The heading is its own field because this screen lays every section out as a Label_X/Card_X
    // pair of SIBLINGS rather than nesting the label inside the card, so hiding the card alone
    // leaves the heading behind.
    [Header("Developer (absent from a FINAL_BUILD)")]
    [SerializeField] private GameObject developerHeading;
    [SerializeField] private GameObject developerSection;

    // Rows a build can lack the service for, each with the divider under it. Hidden together, and
    // the rest of the screen moves up -- see HideUnavailableRows.
    [Header("Rows hidden when the build lacks the service")]
    [SerializeField] private RectTransform vibrationRow;
    [SerializeField] private RectTransform vibrationDivider;
    [SerializeField] private RectTransform shareRow;
    [SerializeField] private RectTransform shareDivider;
    [SerializeField] private RectTransform privacyRow;
    [SerializeField] private RectTransform privacyDivider;

    // Upper case to match the other rows on this screen ("SHARE PATHZA", "PRIVACY POLICY").
    private const string ResetPromptText = "RESET ALL PROGRESS";
    private const string ResetConfirmText = "TAP AGAIN TO CONFIRM";
    private const float ResetConfirmWindow = 3f;

    private bool resetArmed;
    private float resetArmedUntil;

    private void Awake()
    {
        HideUnavailableRows();
    }

    /// <summary>Hides the rows this build has nothing behind -- no vibration motor, no share
    /// sheet, no way to open an outside page (all three on YouTube Playables) -- and moves the rest
    /// of the screen up to close the gaps. Asks the services rather than checking the platform, so
    /// a new platform needs no change here. Runs once, the first time the screen opens; on Android
    /// and iOS nothing is hidden and nothing moves.</summary>
    private void HideUnavailableRows()
    {
        HideRows(!Haptics.IsSupported, vibrationRow, vibrationDivider);

        // Share and privacy share one card, so they collapse in one pass.
        bool hideShare = !FreeFlow.Share.ShareService.Instance.IsAvailable;
        bool hidePrivacy = !FreeFlow.Util.PlatformManager.CanOpenExternalLinks;
        RectTransform generalCard = shareRow != null ? shareRow.parent as RectTransform : null;
        var hidden = new System.Collections.Generic.List<RectTransform>();
        if (hideShare) { hidden.Add(shareRow); hidden.Add(shareDivider); }
        if (hidePrivacy) { hidden.Add(privacyRow); hidden.Add(privacyDivider); }
        FreeFlow.Util.ManualLayout.CollapseStack(generalCard, hidden);
    }

    private static void HideRows(bool hide, RectTransform row, RectTransform divider)
    {
        if (!hide || row == null) { return; }
        FreeFlow.Util.ManualLayout.CollapseStack(row.parent as RectTransform, new[] { row, divider });
    }

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

        SettingsData settings = ProfileManager.Instance.LoadSettings();
        if (vibrationToggle != null)
        {
            vibrationToggle.SetIsOnWithoutNotify(settings.vibrationEnabled);
            SetToggleVisual(vibrationTrackImage, vibrationKnob, settings.vibrationEnabled);
        }
        if (showHintButtonToggle != null)
        {
            showHintButtonToggle.SetIsOnWithoutNotify(settings.showHintButton);
            SetToggleVisual(showHintButtonTrackImage, showHintButtonKnob, settings.showHintButton);
        }

        if (versionText != null)
        {
            versionText.text = "PATHZA " + Application.version;
        }
        ResetConfirmState();
        RefreshDeveloperSection();
    }

    /// <summary>Shows the row that opens the developer tools, or -- in a build that defines
    /// FINAL_BUILD -- switches it off so a player can never reach them.
    ///
    /// FINAL_BUILD is OUR symbol, added to Player Settings' Scripting Define Symbols for the
    /// store build only, rather than one Unity maintains. Two things follow from that, and both
    /// are the point. The tools are present by DEFAULT, so every build made along the way has
    /// them -- including a non-development build handed to a playtester, which Unity's DEBUG
    /// would have switched them off in, that being the one build where they are most wanted. And
    /// stripping them is one deliberate act -- adding FINAL_BUILD -- rather than something that
    /// rides on whether anyone remembered to leave "Development Build" unticked.
    ///
    /// This is the ONLY route to DeveloperPage, so hiding the row is what gates the whole screen.
    /// The tools on it are separately gated as well (every handler there has a
    /// <c>#if !FINAL_BUILD</c> body), so a final build that somehow opened it could still not
    /// change anything.</summary>
    private void RefreshDeveloperSection()
    {
#if !FINAL_BUILD
        SetDeveloperVisible(true);
#else
        SetDeveloperVisible(false);
#endif
    }

    /// <summary>Switches the developer block on or off as a unit -- the "DEVELOPER" heading as
    /// well as the card under it.
    ///
    /// Both are needed because they are siblings, not parent and child: every section on this
    /// screen is a Label_X/Card_X pair under Content. Hiding only the card left "DEVELOPER"
    /// sitting above the version line in a release build, heading nothing at all.
    ///
    /// Each reference is guarded separately rather than behind one early return, so a missing
    /// wire on one of them cannot silently leave the other one showing.</summary>
    private void SetDeveloperVisible(bool visible)
    {
        if (developerHeading != null) { developerHeading.SetActive(visible); }
        if (developerSection != null) { developerSection.SetActive(visible); }
    }

    /// <summary>Opens the developer tools screen. As an OVERLAY on top of this one, not a stacked
    /// page: this screen is itself an overlay, so pushing DeveloperPage would close whatever full
    /// page is underneath Settings and leave Back returning to the wrong place. Closing it (see
    /// DeveloperPage.OnCloseButtonClick) simply reveals this screen again.</summary>
    public void OnDeveloperToolsClick()
    {
        if (!FreeFlow.Input.InputManager.Instance.CanInput()) { return; }
        AudioManager.Instance.PlaySFX(SoundType.ButtonClick);

        PageManager.Instance.OpenAsOverlay(PageType.Developer);
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

    // Persisted through ProfileManager's settings file (like audioData) rather than PlayerPrefs,
    // so every local device preference lives in one file, separate from progress (see PlayerProgress).
    public void OnVibrationToggleChanged(bool isOn)
    {
        SettingsData settings = ProfileManager.Instance.LoadSettings();
        settings.vibrationEnabled = isOn;
        ProfileManager.Instance.SaveSettings(settings);
        SetToggleVisual(vibrationTrackImage, vibrationKnob, isOn);

        // Haptics keep the preference cached rather than re-reading the save on every dot picked
        // up, so the switch has to tell them it moved.
        Haptics.SetEnabled(isOn);

        // And then demonstrate it. A switch controlling something you cannot see is only
        // answerable by feel, so turning it on plays the tap it just enabled.
        if (isOn) { Haptics.Play(HapticType.Selection); }
    }

    public void OnShowHintButtonToggleChanged(bool isOn)
    {
        SettingsData settings = ProfileManager.Instance.LoadSettings();
        settings.showHintButton = isOn;
        ProfileManager.Instance.SaveSettings(settings);
        SetToggleVisual(showHintButtonTrackImage, showHintButtonKnob, isOn);

        // Tell the gameplay HUD now. It reads this flag when it refreshes, and refreshing means a
        // level load or a hint spent -- neither of which happens while this screen is open, because
        // Settings is an OVERLAY: the gameplay page underneath is never disabled, so its OnEnable
        // does not fire when this closes. Without this the player turns the button off, returns to
        // the board, and finds it still sitting there until the next level.
        GameplayPage gameplay = PageManager.Instance.Get<GameplayPage>(PageType.Gameplay);
        if (gameplay != null) { gameplay.RefreshHintButton(); }
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
        AudioManager.Instance.PlaySFX(SoundType.ButtonClick);

        FreeFlow.Share.ShareService.Instance.ShareGame();
    }

    /// <summary>
    /// Deletes SaveData.json -- every pack's progress, plus the daily-challenge streaks and
    /// history it now carries alongside them -- then reloads the scene. This reloads MainScene, not
    /// StartScene, so GameBootstrap/ProfileManager.Initialize do not run again -- the fresh defaults
    /// come from ProfileManager.EnsureProgressLoaded instead, the same lazy "create if missing"
    /// logic Initialize itself calls, triggered the moment anything on the rebuilt screen next asks
    /// for progress. Nothing here is recoverable afterwards.
    ///
    /// Deliberately leaves Settings.json untouched: audio/vibration/hint-button are local device
    /// preferences the player set up on this screen, not progress, and "reset progress" resetting
    /// them too would be a surprise -- the player would have to re-lower their volume right after
    /// wiping a save, for something that was never part of what they asked to reset.
    ///
    /// Which is why it takes two taps within <see cref="ResetConfirmWindow"/>: the first arms it
    /// and changes the label to say so, and the window lapses on its own if the player thinks
    /// better of it. Leaving and reopening Settings disarms it too (see OnEnable). A single stray
    /// tap on a row sitting directly under "PRIVACY POLICY" must never be able to wipe a save.
    /// </summary>
    public void OnResetProgressClick()
    {
        if (!FreeFlow.Input.InputManager.Instance.CanInput()) { return; }
        AudioManager.Instance.PlaySFX(SoundType.ButtonClick);

        if (resetArmed && Time.unscaledTime <= resetArmedUntil)
        {
            ProfileManager.Instance.DeleteAllProgress();
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
        AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
        PageManager.Instance.CloseOverlay(PageType.Setting);
    }
}
}
