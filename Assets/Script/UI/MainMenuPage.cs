using UnityEngine;
using TMPro;
using FreeFlow.Enums;
using FreeFlow.Input;

namespace FreeFlow.UI
{
    /// <summary>
    /// Owns behavior local to the main menu screen itself: the header's "next level" chip, and
    /// each mode card's real progress line ("37 / 100 levels"). Cross-screen flow (which screen
    /// is active, level loading) stays in UIController; this only owns what belongs to this one
    /// screen. Replaces the earlier single-PLAY-button + Classic/Advanced tab design -- the
    /// updated reference shows two independent, always-visible mode cards instead, each with its
    /// own PLAY button (see UIController.OnPlayClassicButtonClick/OnPlayAdvancedButtonClick).
    /// </summary>
    public class MainMenuPage : Page
    {
        [SerializeField] private TopPanel topPanel;

        [Header("Header")]
        [SerializeField] private TextMeshProUGUI levelChipText;

        [Header("Mode cards")]
        [SerializeField] private TextMeshProUGUI classicProgressText;
        [SerializeField] private TextMeshProUGUI advancedProgressText;

        [Header("Daily challenge card")]
        // Today's state in one line, plus the best streak as a tail.
        [SerializeField] private TextMeshProUGUI dailySubtitleText;

        // The streak chip on the right. Hidden outright at streak 0 rather than showing "0-DAY",
        // which would advertise the absence of the thing it exists to celebrate.
        [SerializeField] private GameObject streakPill;
        [SerializeField] private TextMeshProUGUI streakPillText;

        // Shown from a daily reset until the player opens the hub -- unseen content, not unplayed
        // content, so it clears on a visit even if nothing is solved. See
        // SaveData.dailyChallengeLastSeenDay.
        [SerializeField] private GameObject newBadge;

        private void OnEnable()
        {
            Refresh();
        }

        /// <summary>Re-reads save data and updates the header chip + both cards' progress lines.
        /// Called on enable (not just Start) since returning from a level changes this screen's
        /// own numbers without the GameObject being recreated.</summary>
        public void Refresh()
        {
            UIController ui = UIController.Instance;
            if (ui == null) { return; }
            SaveData data = SavingSystem.Instance.Load();

            // MainMenu is the root page (nothing to go back to) and already has its own
            // branding (GameNameLabel/Wordmark) plus the level chip below -- only the Setting
            // button is needed here, no title/subtitle/Back/Option.
            if (topPanel != null) { topPanel.SetTopPanel("", "", showBack: false, showSetting: true, showOption: false); }

            if (levelChipText != null)
            {
                int nextLevel = data.CompletedLevelForKey(DefaultKey(ui, GameMode.Classic)) + 1;
                levelChipText.text = "Level " + nextLevel;
            }

            SetProgressText(classicProgressText, data, ui, GameMode.Classic);
            SetProgressText(advancedProgressText, data, ui, GameMode.Advanced);
            SetDailyChallengeCard(data, ui);
        }

        /// <summary>
        /// Fills the daily-challenge card from the save: how far today has got, whether it has
        /// been looked at yet, and the streaks.
        ///
        /// Deliberately does NOT call UIController.EnsureTodayDailyPicks. That would commit (and
        /// persist) the day's picks merely because the main menu was displayed; a card is a
        /// readout, not a decision. So when today has not been selected yet, this describes the
        /// day from the configured length instead -- which is what the player will get -- and
        /// leaves selecting it to the moment they actually open the hub.
        /// </summary>
        private void SetDailyChallengeCard(SaveData data, UIController ui)
        {
            int today = FreeFlow.GamePlay.DailyChallengeSelector.DayIndex(System.DateTime.UtcNow);
            bool selectedToday = data.dailyChallengeCachedDay == today;

            int total = selectedToday ? data.DailyChallengeCount : ui.ConfiguredDailyChallengeCount;
            int solved = selectedToday ? data.SolvedDailyChallengeCount() : 0;

            if (newBadge != null)
            {
                newBadge.SetActive(data.dailyChallengeLastSeenDay != today);
            }

            if (streakPill != null)
            {
                bool hasStreak = data.dailyChallengeStreak > 0;
                streakPill.SetActive(hasStreak);
                if (hasStreak && streakPillText != null)
                {
                    streakPillText.text = data.dailyChallengeStreak + "-DAY";
                }
            }

            if (dailySubtitleText != null)
            {
                string progress = solved == 0 ? total + " challenges today"
                    : solved < total ? solved + " / " + total + " solved today"
                    : "all " + total + " done today";

                // The best streak only earns its space once there is one; on a fresh save the
                // line stays about today rather than trailing a hollow "best 0".
                string best = data.bestDailyChallengeStreak > 0
                    ? "  ·  best " + data.bestDailyChallengeStreak
                    : "";

                dailySubtitleText.text = progress + best;
            }
        }

        /// <summary>Summed across every pack size in the mode (e.g. Classic's 5x5 + 6x6 + 7x7 +
        /// ...), not just one representative size -- this is the mode's true overall progress,
        /// which is what the card is actually claiming to show.</summary>
        private static void SetProgressText(TextMeshProUGUI text, SaveData data, UIController ui, GameMode mode)
        {
            if (text == null) { return; }

            int completed = 0;
            int total = 0;
            int[] sizes = ui.PackSizesFor(mode);
            for (int i = 0; i < sizes.Length; i++)
            {
                completed += data.CompletedLevelForKey(ui.KeyFor(mode, sizes[i]));
                total += ui.PackLevelCountFor(sizes[i]);
            }

            text.text = "<color=#0F9E88>" + completed + "</color> / " + total + " levels";
        }

        /// <summary>The pack-progress key for a mode's first/default pack size -- each card shows
        /// one representative progress number ("at your own pace"), not an aggregate across every
        /// size, mirroring how the original PLAY button's own progress bar worked.</summary>
        private static string DefaultKey(UIController ui, GameMode mode)
        {
            return ui.KeyFor(mode, ui.PackSizesFor(mode)[0]);
        }

        /// <summary>Each mode card (CLASSIC/ADVANCED) has its own PLAY button -- these set the
        /// mode the tapped card belongs to before opening pack-select.</summary>
        public void OnPlayClassicButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                UIController.Instance.SetMode(GameMode.Classic);
                PageManager.Instance.OpenPage(PageType.PackSelect);
            }
        }

        public void OnPlayAdvancedButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                UIController.Instance.SetMode(GameMode.Advanced);
                PageManager.Instance.OpenPage(PageType.PackSelect);
            }
        }

        /// <summary>Opens the Daily Challenge hub (streak, this week, today's pick) rather than
        /// jumping straight into gameplay -- actually loading a challenge is done by tapping its
        /// own card on that hub.</summary>
        public void OnDailyChallengeButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                PageManager.Instance.OpenPage(PageType.DailyChallenge);
            }
        }
    }
}
