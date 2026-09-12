using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FreeFlow.Input;

namespace FreeFlow.UI
{
    /// <summary>The level-complete (game over) overlay. Opened with PageManager.OpenAsOverlay,
    /// closed with CloseOverlay -- never part of the back-stack. Owns every field this screen's
    /// content needs -- title/subtitle, next-level subtitle, stat cards, progress bar, star
    /// rating and streak banner -- rather than UIController holding any of it directly;
    /// <see cref="SetLevelCompleteData"/> is the one entry point UIController calls to fill it
    /// all in.</summary>
    public class LevelCompletePage : Page
    {
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI subtitleText;
        [SerializeField] private TextMeshProUGUI nextLevelSubtitleText;

        [Header("Stat cards")]
        [SerializeField] private TextMeshProUGUI movesStatText;
        [SerializeField] private TextMeshProUGUI timeStatText;
        [SerializeField] private TextMeshProUGUI hintsStatText;

        [Header("Pack progress")]
        [SerializeField] private Slider progressSlider;
        [SerializeField] private TextMeshProUGUI progressLabelText;

        // No underlying scoring system exists (no per-level "best" or points anywhere in
        // SaveData) -- see freeflow_newui_redesign memory for why. These stars are a display-only
        // rating derived from hints used this attempt, never persisted, so they can't be confused
        // with a real save-backed stat.
        [Header("Star rating (derived, not persisted)")]
        [SerializeField] private Image[] starImages;
        [SerializeField] private Sprite starOnSprite;
        [SerializeField] private Sprite starOffSprite;

        [Header("Daily streak")]
        [SerializeField] private GameObject streakBanner;
        [SerializeField] private TextMeshProUGUI streakText;

        /// <summary>Fills in this screen's content for the attempt that was just completed --
        /// called by UIController.ActivateLevelCompleteScreen right before it opens this overlay.
        /// Only the truly one-shot attempt stats are passed in (moves/hints/time/old progress --
        /// never stored anywhere, so nothing else can supply them); everything else (mode, pack
        /// size, current level, total levels, daily-challenge state) is read directly from
        /// UIController.Instance's own public properties, same as every other page's header.
        /// </summary>
        /// <param name="movesCount">Moves made this attempt.</param>
        /// <param name="hintsUsedThisAttempt">Hints used since this attempt began.</param>
        /// <param name="secondsTaken">Wall-clock time this attempt took to solve the level.</param>
        /// <param name="oldCompletedLevel">CompletedLevelForKey for this pack BEFORE this
        /// completion, so the progress bar can show where the player was, not just where they are.</param>
        public void SetLevelCompleteData(int movesCount, int hintsUsedThisAttempt, float secondsTaken, int oldCompletedLevel)
        {
            UIController ui = UIController.Instance;
            if (ui == null) { return; }

            if (titleText != null) { titleText.text = "LEVEL COMPLETE"; }

            if (subtitleText != null)
            {
                subtitleText.text = ui.CurrentMode.ToString().ToUpperInvariant()
                    + (ui.CurrentPackSize > 0 ? " " + ui.CurrentPackSize + "×" + ui.CurrentPackSize : "")
                    + "  ·  LEVEL " + ui.CurrentLevel
                    + "  ·  SOLVED IN " + movesCount + " MOVES";
            }

            if (movesStatText != null) { movesStatText.text = movesCount.ToString(); }
            if (timeStatText != null) { timeStatText.text = FormatTime(secondsTaken); }
            if (hintsStatText != null) { hintsStatText.text = hintsUsedThisAttempt.ToString(); }

            SetStars(hintsUsedThisAttempt);
            int newCompletedLevel = Mathf.Max(oldCompletedLevel, ui.CurrentLevel);
            SetProgressBar(newCompletedLevel, ui.TotalLevelCount);

            if (streakBanner != null)
            {
                streakBanner.SetActive(ui.IsDailyChallenge);
                if (ui.IsDailyChallenge && streakText != null)
                {
                    SaveData data = SavingSystem.Instance.Load();
                    streakText.text = data.dailyChallengeStreak + "-day streak!";
                }
            }

            if (nextLevelSubtitleText != null)
            {
                // Same wrap-to-1 rule LoadNextLevel itself uses, so the label never promises a
                // level number the NEXT LEVEL button won't actually load.
                int nextLevel = ui.CurrentLevel < ui.TotalLevelCount ? ui.CurrentLevel + 1 : 1;
                nextLevelSubtitleText.text = "LEVEL " + nextLevel
                    + (ui.CurrentPackSize > 0 ? "  ·  " + ui.CurrentPackSize + "×" + ui.CurrentPackSize : "");
            }
        }

        private void SetStars(int hintsThisAttempt)
        {
            if (starImages == null) { return; }

            int earned = hintsThisAttempt <= 0 ? 3 : (hintsThisAttempt <= 2 ? 2 : 1);
            for (int i = 0; i < starImages.Length; i++)
            {
                if (starImages[i] == null) { continue; }
                starImages[i].sprite = i < earned ? starOnSprite : starOffSprite;
            }
        }

        private void SetProgressBar(int newCompleted, int total)
        {
            if (progressLabelText != null)
            {
                progressLabelText.text = newCompleted + "/" + total;
            }

            if (progressSlider != null)
            {
                progressSlider.value = total > 0 ? (float)newCompleted / total : 0f;
            }
        }

        /// <summary>"1:24" for anything a minute or over, "42s" under a minute -- matches how
        /// short a level attempt actually runs, so the common case doesn't carry a redundant
        /// "0:" prefix.</summary>
        private static string FormatTime(float seconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.RoundToInt(seconds));
            int minutes = totalSeconds / 60;
            int remainder = totalSeconds % 60;
            return minutes > 0 ? minutes + ":" + remainder.ToString("00") : remainder + "s";
        }

        public void OnRetryButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                UIController.Instance.RetryCurrentLevel();
            }
        }

        public void OnHomeButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                UIController.Instance.GoToMainMenu();
            }
        }

        public void OnNextButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                UIController.Instance.GoToNextLevel();
            }
        }
    }
}
