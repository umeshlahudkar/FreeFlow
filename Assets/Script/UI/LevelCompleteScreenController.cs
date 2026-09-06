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
    public class LevelCompleteScreenController : MonoBehaviour
    {
        [Header("Stat cards")]
        [SerializeField] private TextMeshProUGUI movesStatText;
        [SerializeField] private TextMeshProUGUI oldBestStatText;
        [SerializeField] private TextMeshProUGUI hintsStatText;

        [Header("Pack progress")]
        [SerializeField] private RectTransform progressOldFillRect;
        [SerializeField] private RectTransform progressNewFillRect;
        [SerializeField] private RectTransform progressKnobRect;
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

        private float progressTrackWidth = -1f;

        public void Refresh(int movesCount, int hintsThisAttempt, int oldBestMoves,
            int oldCompletedLevel, int newCompletedLevel, int totalLevelCount, bool isDailyChallenge, int dailyStreak)
        {
            if (movesStatText != null) { movesStatText.text = movesCount.ToString(); }
            // 0 means no record predates this completion (see PackProgress.bestMoves) -- shown as
            // "--" rather than a misleading "0 moves".
            if (oldBestStatText != null) { oldBestStatText.text = oldBestMoves > 0 ? oldBestMoves.ToString() : "--"; }
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

            // Cached lazily from the fill's own parent -- same pattern UIController.SetGameplayFillWidth
            // uses for the HUD cells bar -- so this doesn't need a separately-serialized track width.
            if (progressTrackWidth < 0f && progressNewFillRect != null && progressNewFillRect.parent is RectTransform parent)
            {
                progressTrackWidth = parent.rect.width;
            }
            if (progressTrackWidth <= 0f) { return; }

            float oldFraction = total > 0 ? (float)oldCompleted / total : 0f;
            float newFraction = total > 0 ? (float)newCompleted / total : 0f;

            if (progressOldFillRect != null)
            {
                Vector2 size = progressOldFillRect.sizeDelta;
                size.x = progressTrackWidth * oldFraction;
                progressOldFillRect.sizeDelta = size;
            }
            if (progressNewFillRect != null)
            {
                Vector2 size = progressNewFillRect.sizeDelta;
                size.x = progressTrackWidth * newFraction;
                progressNewFillRect.sizeDelta = size;
            }
            if (progressKnobRect != null)
            {
                Vector2 pos = progressKnobRect.anchoredPosition;
                pos.x = progressTrackWidth * newFraction;
                progressKnobRect.anchoredPosition = pos;
            }
        }
    }
}
