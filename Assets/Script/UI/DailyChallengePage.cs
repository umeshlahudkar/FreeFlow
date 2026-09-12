using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FreeFlow.GamePlay;
using FreeFlow.Input;

namespace FreeFlow.UI
{
    /// <summary>
    /// Populates the Daily Challenge hub: streak cards, the current calendar week's solved/today/
    /// future chain, and today's actual picks.
    ///
    /// The reference art's "TODAY" section shows several level buttons of increasing board size,
    /// and that is now literally what a day is: DailyChallengeSelector.SelectDay draws
    /// UIController's configured number of levels for the day, one per rotation step through the
    /// mode's pack sizes, sorted easy-board-first. This screen instantiates one LevelButton per
    /// pick (the same prefab/visual states LevelsPage uses -- Locked never applies here, since
    /// every one of the day's challenges is playable from the moment the day starts, so only
    /// Current/Done show) into <see cref="levelsParent"/>.
    ///
    /// The day counts toward the streak only when EVERY one of its challenges is solved -- see
    /// SaveData.AllDailyChallengesSolved -- which is why the week chain and the "solved" tallies
    /// here all key off that rather than off any single completion.
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

        // Pooled across refreshes rather than rebuilt: the day's length only changes when the day
        // does (or when the count is reconfigured), so spawning is a one-off in practice, and
        // keeping the instances means a refresh cannot briefly empty the row.
        private readonly List<LevelButton> todayLevelButtons = new List<LevelButton>();

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
            RefreshTodayLevelButtons();
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
                // challenges are all done; otherwise the plain "today" ring.
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

        /// <summary>Instantiates (once) or refreshes one LevelButton per daily challenge the day
        /// holds, reusing the exact same prefab/visual states (locked/current/done sprites, check
        /// icon) LevelsPage uses -- including Locked, since a day is played in order: only the
        /// challenge after the last solved one is open, and the rest stay gated behind it.</summary>
        private void RefreshTodayLevelButtons()
        {
            UIController ui = UIController.Instance;
            if (ui == null || levelButtonPrefab == null || levelsParent == null) { return; }

            DailyPick[] picks = ui.EnsureTodayDailyPicks();

            int solved = 0;
            for (int i = 0; i < picks.Length; i++)
            {
                if (picks[i].solved) { solved++; }
            }

            if (todayTallyText != null) { todayTallyText.text = solved + " / " + picks.Length + " solved"; }

            // Mirrors SaveData.UnlockedDailyChallengeThrough, computed from the picks already in
            // hand rather than a second save read. The day is solved strictly in order, so the
            // solved COUNT is also the index of the first unsolved one; a fully solved day leaves
            // every challenge open to replay.
            int unlockedThrough = solved < picks.Length ? solved : picks.Length - 1;

            for (int i = 0; i < picks.Length; i++)
            {
                LevelButton button = ButtonAt(i);
                LevelTileState state = picks[i].solved ? LevelTileState.Done
                    : i <= unlockedThrough ? LevelTileState.Current
                    : LevelTileState.Locked;

                // Numbered by position in the day (1..5), not by the pack level each pick came
                // from -- see LevelButton's three-argument SetDetails. The tile still loads
                // picks[i].levelNumber; only the caption counts the day.
                button.SetDetails(picks[i].levelNumber, state, i + 1);
            }

            // The day got shorter (the configured count was lowered, or a skill band cannot supply
            // as many distinct levels) -- park the surplus rather than destroying it, so a later
            // longer day can reuse them.
            for (int i = picks.Length; i < todayLevelButtons.Count; i++)
            {
                todayLevelButtons[i].gameObject.SetActive(false);
            }
        }

        private LevelButton ButtonAt(int slot)
        {
            while (todayLevelButtons.Count <= slot)
            {
                LevelButton spawned = Instantiate(levelButtonPrefab, levelsParent);
                spawned.ThisTransform.localPosition = Vector3.zero;
                spawned.ThisTransform.sizeDelta = new Vector2(120f, 120f);

                // LevelButton.OnButtonClick's default action always calls UIController.LoadLevel
                // directly, which knows nothing about daily challenges (it would open the level as
                // an ordinary pack level, skipping the day's bookkeeping and its own prev/next) --
                // override it to open that SLOT of the day instead. The slot is copied into a
                // local first: captured straight, every button would close over the same variable
                // and all of them would open the last one. No CanInput gate here either:
                // OnButtonClick's own gate already covers this call (see the override field's own
                // doc comment on LevelButton for why a second gate would always silently no-op).
                int capturedSlot = todayLevelButtons.Count;
                spawned.SetClickOverride(() => UIController.Instance.LoadDailyChallenge(capturedSlot));

                todayLevelButtons.Add(spawned);
            }

            todayLevelButtons[slot].gameObject.SetActive(true);
            return todayLevelButtons[slot];
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

        /// <summary>Jumps into the first of today's challenges the player has not finished --
        /// LoadDailyChallenge opens the Gameplay page itself via PageManager, which closes
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
