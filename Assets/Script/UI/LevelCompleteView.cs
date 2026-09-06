using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FreeFlow.UI
{
    /// <summary>
    /// Populates the Level Complete screen's stat cards, pack-progress bar, star rating and
    /// daily-streak banner. UIController still owns screen activation and the Retry/Home/Next
    /// navigation directly (the same split as PackSelectScreenController/LevelsScreenController) --
    /// this controller only renders the numbers UIController.ActivateLevelCompleteScreen hands it.
    /// </summary>
    /// <summary>Renders the level-complete stat cards, progress bar, star rating and streak
    /// banner -- purely a content view, not itself a page. The container this sits under
    /// (GameOverScreen/LevelCompletePage) is what PageManager actually opens/closes.</summary>
    public class LevelCompleteView : MonoBehaviour
    {
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

        public void Refresh(int movesCount, int hintsThisAttempt, float secondsTaken,
            int oldCompletedLevel, int newCompletedLevel, int totalLevelCount, bool isDailyChallenge, int dailyStreak)
        {
            if (movesStatText != null) { movesStatText.text = movesCount.ToString(); }
            if (timeStatText != null) { timeStatText.text = FormatTime(secondsTaken); }
            if (hintsStatText != null) { hintsStatText.text = hintsThisAttempt.ToString(); }

            SetStars(hintsThisAttempt);
            SetProgressBar(oldCompletedLevel, newCompletedLevel, totalLevelCount);

            if (streakBanner != null)
            {
                streakBanner.SetActive(isDailyChallenge);
                if (isDailyChallenge && streakText != null)
                {
                    streakText.text = dailyStreak + "-day streak!";
                }
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

        private void SetProgressBar(int oldCompleted, int newCompleted, int total)
        {
            if (progressLabelText != null)
            {
                progressLabelText.text = oldCompleted + "/" + total + "  →  " + newCompleted + "/" + total;
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
    }
}
