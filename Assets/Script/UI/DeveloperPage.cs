using System.Collections.Generic;
using UnityEngine;
using TMPro;
using FreeFlow.Enums;

namespace FreeFlow.UI
{
    /// <summary>
    /// The tools screen, compiled in unless FINAL_BUILD is defined: shortcuts that let a
    /// developer put the game into a state that would otherwise take a day of waiting or an
    /// afternoon of playing. Reached from the Settings screen's DEVELOPER row, which is itself
    /// switched off by that same symbol (see SettingPage.RefreshDeveloperSection, which also
    /// explains why the symbol is ours rather than Unity's DEBUG) -- nothing a player can tap
    /// leads here.
    ///
    /// It is an OVERLAY rather than a stacked page, the same as Settings, and for the same reason:
    /// it is opened on top of Settings, and Back has to return there. Pushing it on the back-stack
    /// would close whatever full page is underneath Settings instead.
    ///
    /// The logic is gated as well as the entry point. Every handler below has a body wrapped in
    /// <c>#if !FINAL_BUILD</c> and a signature that is always present, which keeps the scene
    /// wiring valid in every build configuration -- the same split SettingPage already used when
    /// the daily-reset override lived there.
    /// </summary>
    public class DeveloperPage : Page
    {
        [SerializeField] private TopPanel topPanel;

        // Shorten the daily-challenge period so a reset can be watched in seconds instead of
        // waiting for UTC midnight. Applied on the row's own SAVE button rather than on every
        // keystroke: typing "120" passes through 1 and 12 on the way, and each of those was
        // briefly the live day length -- long enough, at those sizes, to roll the day over and
        // re-pick today's challenges underneath the developer who was still typing.
        [Header("Daily challenge reset")]
        [SerializeField] private TMP_InputField dailyResetSecondsInput;
        [SerializeField] private TextMeshProUGUI dailyResetStatusText;

        // Campaign, pack, and "treat levels 1..N as finished", laid out as one line.
        [Header("Level unlock")]
        [SerializeField] private TMP_Dropdown unlockModeDropdown;
        [SerializeField] private TMP_Dropdown unlockPackDropdown;
        [SerializeField] private TMP_InputField unlockLevelInput;
        [SerializeField] private TextMeshProUGUI unlockStatusText;

        // The hint wallet. One balance for the whole game -- not per level, pack or run (see
        // UIController.HintsRemaining) -- so there is a single number to write here.
        [Header("Hint balance")]
        [SerializeField] private TMP_InputField hintCountInput;
        [SerializeField] private TextMeshProUGUI hintStatusText;

#if !FINAL_BUILD
        // The chosen pack's BOARD SIZE, not the dropdown's index. Advanced ships fewer packs than
        // Classic, so slot 1 is 6x6 in one campaign and does not exist in the other -- holding the
        // index across a mode switch silently retargets the unlock at a different pack.
        private int unlockPackSize;
        private bool unlockSeeded;
#endif

        private void OnEnable()
        {
            // Back is this overlay's own close action (see OnCloseButtonClick), wired on this
            // instance rather than TopPanel's generic handler -- ClosePage would pop the page
            // underneath Settings instead of returning to Settings.
            if (topPanel != null)
            {
                topPanel.SetTopPanel("DEVELOPER", "NOT IN FINAL BUILDS", showBack: true, showSetting: false, showOption: false);
            }

#if !FINAL_BUILD
            int seconds = FreeFlow.GamePlay.DailyChallengeSelector.DebugDayLengthSeconds;
            if (dailyResetSecondsInput != null)
            {
                // WithoutNotify: filling the box must not read as the developer having typed in
                // it, which would write the value straight back and fight whatever they are
                // mid-edit on.
                dailyResetSecondsInput.SetTextWithoutNotify(seconds > 0 ? seconds.ToString() : "");
            }
            SetDailyResetStatus(seconds);

            RefreshUnlockRow();
            RefreshHintRow();
#endif
        }

        /// <summary>Applies the typed daily-reset period, in seconds. Blank or 0 restores real
        /// calendar days. Takes effect immediately: everything about the daily challenge keys off
        /// DailyChallengeSelector.DayIndex, so the next time any screen asks what day it is, it
        /// gets the compressed answer -- and the hub, which re-checks once a second, rebuilds
        /// itself on the next period boundary without needing to be reopened.</summary>
        public void OnSaveDailyResetClick()
        {
#if !FINAL_BUILD
            if (!FreeFlow.Input.InputManager.Instance.CanInput()) { return; }
            AudioManager.Instance.PlaySFX(SoundType.ButtonClick);

            int seconds;
            if (dailyResetSecondsInput == null || !int.TryParse(dailyResetSecondsInput.text, out seconds) || seconds < 0)
            {
                seconds = 0;
            }

            FreeFlow.GamePlay.DailyChallengeSelector.DebugDayLengthSeconds = seconds;

            // Echo back what was actually applied -- a blank box and a typed 0 both mean "off",
            // and the row should say so the same way in either case.
            if (dailyResetSecondsInput != null)
            {
                dailyResetSecondsInput.SetTextWithoutNotify(seconds > 0 ? seconds.ToString() : "");
            }
            SetDailyResetStatus(seconds);
#endif
        }

        /// <summary>Sets the hint wallet to the typed number.
        ///
        /// SETS rather than adds, which is what makes the box meaningful: it opens holding the
        /// current balance (see <see cref="SetHintStatus"/>), so what is on screen is what the
        /// player has, and saving it back is a no-op. Adding would make the displayed number mean
        /// nothing and "save" mean "double it".
        /// </summary>
        public void OnGrantHintsClick()
        {
#if !FINAL_BUILD
            if (!FreeFlow.Input.InputManager.Instance.CanInput()) { return; }
            AudioManager.Instance.PlaySFX(SoundType.ButtonClick);

            int count;
            if (hintCountInput == null || !int.TryParse(hintCountInput.text, out count) || count < 0) { count = 0; }

            // count is clamped non-negative above, so this alone also counts as "granted" (see
            // SaveData.hintsRemaining's -1 sentinel) -- a typed 0 is a real, spendable balance,
            // not "never granted" waiting to be refilled by UIController.EnsureHintBalance.
            SaveData data = SavingSystem.Instance.Load();
            data.hintsRemaining = count;
            SavingSystem.Instance.Save(data);

            SetHintStatus();

            // The HUD's hint pill draws this number and only refreshes on a level load or a hint
            // spent, neither of which happens while this screen is open -- this and Settings are
            // both OVERLAYS, so the gameplay page underneath was never disabled and its OnEnable
            // will not fire when they close.
            GameplayPage gameplay = PageManager.Instance.Get<GameplayPage>(PageType.Gameplay);
            if (gameplay != null && gameplay.gameObject.activeInHierarchy) { gameplay.RefreshHintButton(); }
#endif
        }

        /// <summary>Mode dropdown changed: the pack list belongs to a campaign, so it is rebuilt
        /// for the new one.</summary>
        public void OnUnlockModeChanged(int index)
        {
#if !FINAL_BUILD
            RefreshUnlockPackOptions();
#endif
        }

        /// <summary>Pack dropdown changed. Records the BOARD SIZE rather than the index -- see
        /// <see cref="unlockPackSize"/>.</summary>
        public void OnUnlockPackChanged(int index)
        {
#if !FINAL_BUILD
            int[] sizes = UIController.Instance.PackSizesFor(SelectedUnlockMode);
            if (sizes != null && index >= 0 && index < sizes.Length) { unlockPackSize = sizes[index]; }
            SetUnlockStatus();
#endif
        }

        /// <summary>Unlocks the chosen pack up to and including the typed level number.
        ///
        /// A pack's unlock frontier IS its completed-level count -- setting it to N declares levels
        /// 1..N finished and opens N+1 (see LevelsPage.RefreshButtons) -- so this writes the number
        /// straight in rather than going through SaveData.PackFrontierAdvances. That guard exists
        /// to stop ORDINARY play from skipping ahead, which is the entire point of this row; and 0,
        /// to put a pack back to untouched, has to stay reachable too.
        ///
        /// Only the frontier moves. Per-level attempts, times, best moves and hint counts are left
        /// alone: they are telemetry about plays that genuinely happened, and inventing rows for a
        /// hundred levels nobody opened would poison the very data the pack ordering is tuned from.
        /// </summary>
        public void OnUnlockApplyClick()
        {
#if !FINAL_BUILD
            if (!FreeFlow.Input.InputManager.Instance.CanInput()) { return; }
            AudioManager.Instance.PlaySFX(SoundType.ButtonClick);

            UIController ui = UIController.Instance;
            GameMode mode = SelectedUnlockMode;
            int[] sizes = ui.PackSizesFor(mode);
            if (sizes == null || sizes.Length == 0)
            {
                if (unlockStatusText != null) { unlockStatusText.text = mode + " has no packs to unlock."; }
                return;
            }

            int index = unlockPackDropdown == null ? 0 : Mathf.Clamp(unlockPackDropdown.value, 0, sizes.Length - 1);
            int packSize = sizes[index];
            int total = ui.PackLevelCountFor(packSize);

            int level;
            if (unlockLevelInput == null || !int.TryParse(unlockLevelInput.text, out level)) { level = 0; }
            level = Mathf.Clamp(level, 0, total);

            SaveData data = SavingSystem.Instance.Load();
            data.SetCompletedLevelForKey(ui.KeyFor(mode, packSize), level);
            SavingSystem.Instance.Save(data);

            // Echo back what was actually applied rather than what was typed -- 999 in a
            // hundred-level pack has to read as 100, or the row silently disagrees with the grid
            // behind it.
            if (unlockLevelInput != null) { unlockLevelInput.SetTextWithoutNotify(level.ToString()); }

            SetUnlockStatus();
            RefreshProgressScreens();
#endif
        }

        public void OnCloseButtonClick()
        {
            AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
            PageManager.Instance.CloseOverlay(PageType.Developer);
        }

#if !FINAL_BUILD
        private void SetDailyResetStatus(int seconds)
        {
            if (dailyResetStatusText == null) { return; }

            dailyResetStatusText.text = seconds > 0
                ? "Daily resets every " + seconds + "s  (debug override)"
                : "Daily resets at UTC midnight  (normal)";
        }

        /// <summary>Which campaign the mode dropdown is pointing at. Resolved through the enum's
        /// own values rather than by casting the index, so adding a third campaign needs no change
        /// here and a dropdown left out of sync with the enum cannot silently pick the wrong
        /// one.</summary>
        private GameMode SelectedUnlockMode
        {
            get
            {
                GameMode[] modes = (GameMode[])System.Enum.GetValues(typeof(GameMode));
                if (unlockModeDropdown == null || modes.Length == 0) { return GameMode.Classic; }
                return modes[Mathf.Clamp(unlockModeDropdown.value, 0, modes.Length - 1)];
            }
        }

        /// <summary>Fills both dropdowns and the status line. The first time this screen is opened
        /// the row starts on whatever pack is actually being played, which is nearly always the one
        /// a developer wants to skip ahead in; after that it keeps whatever was last chosen,
        /// because reopening the screen to adjust a number should not throw the selection
        /// away.</summary>
        private void RefreshUnlockRow()
        {
            if (!unlockSeeded)
            {
                unlockSeeded = true;
                unlockPackSize = UIController.Instance.CurrentPackSize;

                GameMode[] seedModes = (GameMode[])System.Enum.GetValues(typeof(GameMode));
                int modeIndex = System.Array.IndexOf(seedModes, UIController.Instance.CurrentMode);
                if (unlockModeDropdown != null && modeIndex >= 0)
                {
                    unlockModeDropdown.SetValueWithoutNotify(modeIndex);
                }
            }

            if (unlockModeDropdown != null)
            {
                GameMode[] modes = (GameMode[])System.Enum.GetValues(typeof(GameMode));
                List<string> labels = new List<string>(modes.Length);
                for (int i = 0; i < modes.Length; i++) { labels.Add(modes[i].ToString().ToUpperInvariant()); }

                // ClearOptions resets the value to 0, so the selection has to be read BEFORE the
                // rebuild and put back after it -- otherwise every reopen of this screen silently
                // snaps the row back to Classic.
                int keep = Mathf.Clamp(unlockModeDropdown.value, 0, labels.Count - 1);
                unlockModeDropdown.ClearOptions();
                unlockModeDropdown.AddOptions(labels);
                unlockModeDropdown.SetValueWithoutNotify(keep);
                unlockModeDropdown.RefreshShownValue();
            }

            RefreshUnlockPackOptions();
        }

        /// <summary>Rebuilds the pack dropdown for whichever campaign is selected, keeping the
        /// chosen board size if that campaign has one and falling back to its first pack if it does
        /// not.</summary>
        private void RefreshUnlockPackOptions()
        {
            if (unlockPackDropdown != null)
            {
                int[] sizes = UIController.Instance.PackSizesFor(SelectedUnlockMode);
                List<string> labels = new List<string>();
                for (int i = 0; sizes != null && i < sizes.Length; i++)
                {
                    labels.Add(sizes[i] + "x" + sizes[i]);
                }

                unlockPackDropdown.ClearOptions();
                unlockPackDropdown.AddOptions(labels);

                int index = sizes == null ? -1 : System.Array.IndexOf(sizes, unlockPackSize);
                if (index < 0) { index = 0; }
                unlockPackDropdown.SetValueWithoutNotify(index);
                unlockPackDropdown.RefreshShownValue();

                // Write the resolved choice back, so a mode switch that had to fall back does not
                // leave the field naming a pack the row is no longer showing.
                unlockPackSize = (sizes != null && index < sizes.Length) ? sizes[index] : 0;
            }

            SetUnlockStatus();
        }

        /// <summary>Says where the selected pack currently stands, and pre-fills the level box with
        /// it -- so the common case (nudge this pack forward a bit) starts from the real number
        /// rather than from whatever was typed for a different pack a moment ago.</summary>
        private void SetUnlockStatus()
        {
            UIController ui = UIController.Instance;
            GameMode mode = SelectedUnlockMode;

            if (unlockPackSize <= 0)
            {
                if (unlockStatusText != null) { unlockStatusText.text = mode + " has no packs."; }
                return;
            }

            int completed = SavingSystem.Instance.Load().CompletedLevelForKey(ui.KeyFor(mode, unlockPackSize));
            int total = ui.PackLevelCountFor(unlockPackSize);

            if (unlockLevelInput != null) { unlockLevelInput.SetTextWithoutNotify(completed.ToString()); }
            if (unlockStatusText != null)
            {
                unlockStatusText.text = mode.ToString().ToUpperInvariant() + " " + unlockPackSize + "x" + unlockPackSize
                    + ":  " + completed + "/" + total + " unlocked";
            }
        }

        /// <summary>Fills the hint box and its status line from the save.</summary>
        private void RefreshHintRow()
        {
            SetHintStatus();
        }

        /// <summary>Puts the live balance in both the box and the line under it. The box is
        /// filled WithoutNotify for the same reason the seconds box is: pre-filling must not read
        /// as the developer having typed.</summary>
        private void SetHintStatus()
        {
            int balance = SavingSystem.Instance.Load().hintsRemaining;

            if (hintCountInput != null) { hintCountInput.SetTextWithoutNotify(balance.ToString()); }
            if (hintStatusText != null) { hintStatusText.text = "Hint balance: " + balance; }
        }

        /// <summary>Repaints whatever progress-showing page is currently underneath this overlay.
        /// This screen and Settings are both OVERLAYS, so the page below them was never disabled
        /// and its OnEnable will not fire when they close -- without this the developer unlocks
        /// fifty levels, backs out, and finds the grid still showing padlocks. Pages that are not
        /// on screen are skipped: they re-read the save on their own next open.</summary>
        private void RefreshProgressScreens()
        {
            PageManager pages = PageManager.Instance;

            LevelsPage levels = pages.Get<LevelsPage>(PageType.Levels);
            if (levels != null && levels.gameObject.activeInHierarchy) { levels.Refresh(); }

            PackSelectPage packs = pages.Get<PackSelectPage>(PageType.PackSelect);
            if (packs != null && packs.gameObject.activeInHierarchy) { packs.Refresh(); }

            MainMenuPage menu = pages.Get<MainMenuPage>(PageType.MainMenu);
            if (menu != null && menu.gameObject.activeInHierarchy) { menu.Refresh(); }
        }
#endif
    }
}
