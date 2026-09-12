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
    /// in this codebase. Rather than invent one, this screen instantiates a single LevelButton
    /// (the same prefab/visual states LevelsPage uses -- Locked never applies here, only Current/
    /// Done) for today's real pick, into <see cref="levelsParent"/>. See freeflow_newui_redesign
    /// memory for the full reasoning.
    /// </summary>
    public class DailyChallengePage : Page
    {
        [SerializeField] private TopPanel topPanel;

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
        [SerializeField] private Slider chainSlider;

        [Header("Today")]
        [SerializeField] private TextMeshProUGUI todayTallyText;
        [SerializeField] private LevelButton levelButtonPrefab;
        [SerializeField] private Transform levelsParent;
        private LevelButton todayLevelButton;

        [Header("Countdown")]
        [SerializeField] private TextMeshProUGUI countdownText;

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

            if (topPanel != null)
            {
                string date = System.DateTime.Now
                    .ToString("ddd d MMM", System.Globalization.CultureInfo.InvariantCulture)
                    .ToUpperInvariant();
                topPanel.SetTopPanel("DAILY CHALLENGE", date);
            }

            if (currentStreakText != null) { currentStreakText.text = data.dailyChallengeStreak.ToString(); }
            if (bestStreakText != null) { bestStreakText.text = data.bestDailyChallengeStreak.ToString(); }

            RefreshWeekChain(data);
            RefreshTodayLevelButton();
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

            int solvedCount = 0;
            for (int i = 0; i < 7; i++)
            {
                System.DateTime day = monday.AddDays(i);
                int dayIndex = DailyChallengeSelector.DayIndex(day);

                if (dayNumbers != null && i < dayNumbers.Length && dayNumbers[i] != null)
                {
                    dayNumbers[i].text = day.Day.ToString();
                }

                bool solved = hasStreak && dayIndex >= runStart && dayIndex <= runEnd;
                if (solved) { solvedCount++; }

                // Today shows the same solved fill as any other completed day once its own
                // challenge is done; otherwise the plain "today" ring.
                Sprite sprite = dayIndex == todayIndex ? (solved ? daySolvedSprite : dayTodaySprite)
                    : solved ? daySolvedSprite
                    : dayFutureSprite;

                if (dayCircles != null && i < dayCircles.Length && dayCircles[i] != null)
                {
                    dayCircles[i].sprite = sprite;
                }
            }

            if (weekTallyText != null) { weekTallyText.text = solvedCount + " / 7 solved"; }

            if (chainSlider != null)
            {
                // "Filled to today's circle centre" per the pack's own README -- a pacing
                // indicator for where in the week today sits, independent of solve state.
                chainSlider.value = (todayDow + 0.5f) / 7f;
            }
        }

        /// <summary>Instantiates (once) or refreshes the single LevelButton representing today's
        /// pick, reusing the exact same prefab/visual states (locked/current/done sprites, check
        /// icon) LevelsPage uses -- Locked never actually applies here, since a daily challenge is
        /// always playable; only Current (not yet solved) or Done (already solved) show.</summary>
        private void RefreshTodayLevelButton()
        {
            UIController ui = UIController.Instance;
            if (ui == null || levelButtonPrefab == null || levelsParent == null) { return; }

            DailyChallengeSelector.Pick pick = ui.PeekTodayDailyChallenge();
            bool solved = ui.IsTodayDailyChallengeSolved();

            if (todayTallyText != null) { todayTallyText.text = (solved ? 1 : 0) + " / 1 solved"; }

            if (todayLevelButton == null)
            {
                todayLevelButton = Instantiate(levelButtonPrefab, levelsParent);
                todayLevelButton.gameObject.SetActive(true);
                todayLevelButton.ThisTransform.localPosition = Vector3.zero;
                todayLevelButton.ThisTransform.sizeDelta = new Vector2(120f, 120f);

                // LevelButton.OnButtonClick's default action always calls UIController.LoadLevel
                // directly, which knows nothing about daily challenges (it would skip crediting
                // the streak) -- override it to call LoadDailyChallenge instead. No CanInput gate
                // here: OnButtonClick's own gate already covers this call (see the override
                // field's own doc comment on LevelButton for why a second gate would always
                // silently no-op).
                todayLevelButton.SetClickOverride(UIController.Instance.LoadDailyChallenge);
            }

            todayLevelButton.SetDetails(pick.levelNumber, solved ? LevelTileState.Done : LevelTileState.Current);
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
