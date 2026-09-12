using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FreeFlow.GamePlay;
using FreeFlow.Input;

namespace FreeFlow.UI
{
    /// <summary>
    /// Populates the Daily Challenge hub: streak cards, the current calendar week's solved/today/
    /// future chain, and today's actual pick. UIController still owns activation and the
    /// Back/Play navigation directly (same split as PackSelectScreenController/LevelsScreenController).
    ///
    /// The reference art's "TODAY" section shows 5 level buttons of increasing board size, as if
    /// several daily levels existed per day -- but DailyChallengeSelector picks exactly ONE level
    /// per calendar day (see its own doc comment) and no such multi-level system exists anywhere
    /// in this codebase. Rather than invent one, this screen shows a single TODAY card for the
    /// real pick. See freeflow_newui_redesign memory for the full reasoning.
    /// </summary>
    public class DailyChallengePage : Page
    {
        [SerializeField] private TopPanel topPanel;

        [Header("Header")]
        [SerializeField] private TextMeshProUGUI dateText;

        [Header("Streak cards")]
        [SerializeField] private TextMeshProUGUI currentStreakText;
        [SerializeField] private TextMeshProUGUI bestStreakText;

        [Header("This week")]
        [SerializeField] private TextMeshProUGUI weekTallyText;
        [SerializeField] private Image[] dayCircles;
        [SerializeField] private TextMeshProUGUI[] dayNumbers;
        [SerializeField] private Sprite daySolvedSprite;
        [SerializeField] private Sprite dayTodaySprite;
        [SerializeField] private Sprite dayFutureSprite;
        [SerializeField] private RectTransform chainFillRect;

        [Header("Today card")]
        [SerializeField] private TextMeshProUGUI todayStatusText;
        [SerializeField] private TextMeshProUGUI todayLevelNumberText;
        [SerializeField] private TextMeshProUGUI todaySizeText;
        [SerializeField] private GameObject todayCheckmark;
        [SerializeField] private Image todayCardImage;
        [SerializeField] private Sprite todayCardDoneSprite;
        [SerializeField] private Sprite todayCardCurrentSprite;

        [Header("Countdown")]
        [SerializeField] private TextMeshProUGUI countdownText;

        private float chainTrackWidth = -1f;
        private float countdownTimer;

        // Parity with its sibling pages (MainMenuPage/PackSelectPage/LevelsPage all refresh
        // themselves on enable) -- previously UIController called Refresh() by hand right before
        // activating this screen, which this now makes unnecessary.
        private void OnEnable()
        {
            Refresh();
        }

        public void Refresh()
        {
            SaveData data = SavingSystem.Instance.Load();

            // Subtitle left blank -- dateText below already shows the specific date.
            if (topPanel != null) { topPanel.SetTopPanel("DAILY CHALLENGE", ""); }

            if (dateText != null)
            {
                dateText.text = System.DateTime.Now
                    .ToString("ddd d MMM", System.Globalization.CultureInfo.InvariantCulture)
                    .ToUpperInvariant();
            }

            if (currentStreakText != null) { currentStreakText.text = data.dailyChallengeStreak.ToString(); }
            if (bestStreakText != null) { bestStreakText.text = data.bestDailyChallengeStreak.ToString(); }

            RefreshWeekChain(data);
            RefreshTodayCard();
            countdownTimer = 0f;
            RefreshCountdown();
        }

        private void Update()
        {
            if (countdownText == null) { return; }
            countdownTimer -= Time.unscaledDeltaTime;
            if (countdownTimer <= 0f)
            {
                countdownTimer = 1f;
                RefreshCountdown();
            }
        }

        // Everything here keys off UTC calendar days, same as DailyChallengeSelector/SaveData's
        // streak fields -- mixing in local-time day boundaries would let this chain disagree with
        // the streak count it is illustrating right at midnight.
        private void RefreshWeekChain(SaveData data)
        {
            System.DateTime todayUtc = System.DateTime.UtcNow.Date;
            int todayIndex = DailyChallengeSelector.DayIndex(todayUtc);

            // DayOfWeek.Sunday == 0 in .NET; remap so Monday is the first column, matching the
            // reference's M T W T F S S ordering.
            int todayDow = ((int)todayUtc.DayOfWeek + 6) % 7;
            System.DateTime monday = todayUtc.AddDays(-todayDow);

            bool hasStreak = data.dailyChallengeStreak > 0;
            int runStart = hasStreak ? data.dailyChallengeLastCompletedDay - data.dailyChallengeStreak + 1 : int.MaxValue;
            int runEnd = data.dailyChallengeLastCompletedDay;

            int solvedExcludingToday = 0;
            for (int i = 0; i < 7; i++)
            {
                System.DateTime day = monday.AddDays(i);
                int dayIndex = DailyChallengeSelector.DayIndex(day);

                if (dayNumbers != null && i < dayNumbers.Length && dayNumbers[i] != null)
                {
                    dayNumbers[i].text = day.Day.ToString();
                }

                Sprite sprite;
                if (dayIndex == todayIndex)
                {
                    // Today always reads as "today", even if already solved -- there is no
                    // solved+today sprite variant, and the reference itself draws today as a ring
                    // even on a day where the streak already includes it.
                    sprite = dayTodaySprite;
                }
                else if (hasStreak && dayIndex >= runStart && dayIndex <= runEnd)
                {
                    sprite = daySolvedSprite;
                    if (dayIndex < todayIndex) { solvedExcludingToday++; }
                }
                else
                {
                    sprite = dayFutureSprite;
                }

                if (dayCircles != null && i < dayCircles.Length && dayCircles[i] != null)
                {
                    dayCircles[i].sprite = sprite;
                }
            }

            if (weekTallyText != null) { weekTallyText.text = solvedExcludingToday + " / 7 solved"; }

            if (chainFillRect != null)
            {
                if (chainTrackWidth < 0f && chainFillRect.parent is RectTransform parent)
                {
                    chainTrackWidth = parent.rect.width;
                }
                if (chainTrackWidth > 0f)
                {
                    // "Filled to today's circle centre" per the pack's own README -- a pacing
                    // indicator for where in the week today sits, independent of solve state.
                    float fraction = (todayDow + 0.5f) / 7f;
                    Vector2 size = chainFillRect.sizeDelta;
                    size.x = chainTrackWidth * fraction;
                    chainFillRect.sizeDelta = size;
                }
            }
        }

        private void RefreshTodayCard()
        {
            UIController ui = UIController.Instance;
            if (ui == null) { return; }

            DailyChallengeSelector.Pick pick = ui.PeekTodayDailyChallenge();
            bool solved = ui.IsTodayDailyChallengeSolved();

            if (todayLevelNumberText != null) { todayLevelNumberText.text = "LEVEL " + pick.levelNumber; }
            if (todaySizeText != null) { todaySizeText.text = pick.packSize + "×" + pick.packSize; }
            if (todayStatusText != null)
            {
                todayStatusText.text = solved ? "TODAY  ·  SOLVED" : "TODAY  ·  NOT YET SOLVED";
            }
            if (todayCheckmark != null) { todayCheckmark.SetActive(solved); }
            if (todayCardImage != null)
            {
                todayCardImage.sprite = solved ? todayCardDoneSprite : todayCardCurrentSprite;
            }
        }

        private void RefreshCountdown()
        {
            if (countdownText == null) { return; }
            System.DateTime nowUtc = System.DateTime.UtcNow;
            System.TimeSpan remaining = nowUtc.Date.AddDays(1) - nowUtc;
            countdownText.text = "RESETS IN " + remaining.Hours + "H " + remaining.Minutes + "M";
        }

        public void OnBackButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                PageManager.Instance.ClosePage();
            }
        }

        /// <summary>Commits today's pick and jumps into gameplay -- LoadLevel (called via
        /// LoadDailyChallenge) opens the Gameplay page itself via PageManager, which closes
        /// whatever page was current (this hub) as part of that same call.</summary>
        public void OnPlayButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                UIController.Instance.LoadDailyChallenge();
            }
        }
    }
}
