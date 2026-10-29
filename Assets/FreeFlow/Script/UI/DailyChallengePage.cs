using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FreeFlow.GamePlay;
using FreeFlow.Input;
using FreeFlow.Enums;
using FreeFlow.Util;

namespace FreeFlow.UI
{
    /// <summary>
    /// Populates the Daily Challenge hub: a day card at the top (<see cref="dayCard"/>, the chosen
    /// day in its month's colour, with that month's solved-days ring), a shelf of the most recent
    /// months (<see cref="monthChips"/>), a nav row (title + prev/next), a masked viewport holding
    /// TWO calendar blocks
    /// (<see cref="blockA"/>/<see cref="blockB"/>, each its own weekday header + 6x7 day grid),
    /// and a footer with a Play button. The day card follows the chosen day, the same day the
    /// Play button describes; the shelf highlights whichever month the calendar is showing.
    ///
    /// A day now holds exactly ONE curated level (see DailyChallengeCalendar) rather than several
    /// drawn from the packs, so there is no "N of 5" concept left anywhere on this screen: a cell
    /// is Completed, open (playable, today or any earlier day), or Locked (a future day, not yet
    /// unlocked).
    ///
    /// Every calendar element -- <see cref="monthTitleText"/>, the nav buttons,
    /// <see cref="calendarViewport"/>, both <see cref="CalendarBlock"/>s and their 2x42
    /// pre-authored CalendarDayButton children, and the footer fields -- is a REAL, pre-built object in
    /// MainScene.unity, not something this script constructs at runtime -- built once via an
    /// Editor script so it is a normal, Inspector-editable part of the scene from here on
    /// (positions/colours/fonts can all be changed by hand in the Editor, the same as any other
    /// page). This script only fills in text/sprites/interactable state and animates position on
    /// objects that already exist; it creates nothing.
    /// </summary>
    public class DailyChallengePage : Page
    {
        [SerializeField] private TopPanel topPanel;

        [SerializeField] private Sprite daySolvedSprite;
        [SerializeField] private Sprite dayTodaySprite;

        [Header("Month calendar (all pre-built scene objects)")]
        [SerializeField] private TextMeshProUGUI monthTitleText;
        [SerializeField] private Button prevMonthButton;
        [SerializeField] private Button nextMonthButton;

        // Clips blockA/blockB to one month's worth of width -- see EnsureBlocksParked, which relies
        // on this rect's width to know how far off-screen the resting/incoming block should sit.
        [SerializeField] private RectTransform calendarViewport;

        /// <summary>One month's worth of calendar UI: its own weekday header (authored once,
        /// content never changes, so this script never touches it after scene-build) and its own
        /// 6x7 day grid. Two of these exist side by side in <see cref="calendarViewport"/> so
        /// paging between months can slide one out while the other slides in, instead of a hard
        /// cut -- see <see cref="StepMonth"/>.</summary>
        [System.Serializable]
        private class CalendarBlock
        {
            public RectTransform root;
            public RectTransform dayGrid;

            // Explicit references, wired by hand -- row-major order (row0col0..row0col6,
            // row1col0..), matching how GridLayoutGroup lays its children out by sibling index.
            // Deliberately not looked up via GetComponentsInChildren: a cell that's missing its
            // component or wired into the wrong slot should show up as a broken/misordered
            // Inspector reference, not silently vanish from (or shuffle within) the list.
            public List<CalendarDayButton> dayCells = new List<CalendarDayButton>();
        }

        [SerializeField] private CalendarBlock blockA;
        [SerializeField] private CalendarBlock blockB;

        // Which of blockA/blockB is the one currently sitting in view (at rest, anchoredPosition
        // (0,0)) -- the other one is either parked off-screen or mid-transition into/out of view.
        private int activeBlockIndex;

        private CalendarBlock ActiveBlock { get { return activeBlockIndex == 0 ? blockA : blockB; } }
        private CalendarBlock InactiveBlock { get { return activeBlockIndex == 0 ? blockB : blockA; } }

        // How long a month-to-month slide takes, and the coroutine driving it -- guarded by
        // isTransitioning so a second nav tap mid-slide cannot start a conflicting animation or
        // flip activeBlockIndex out from under one already in flight.
        [SerializeField] private float monthSlideSeconds = 0.3f;
        private Coroutine monthSlideRoutine;
        private bool isTransitioning;

        // How far back the calendar can be paged: the oldest browsable month is exactly this many
        // months before the real current one (today's month counts as one of the visible months,
        // so 6 here means the current month plus 5 before it are reachable). Prev refuses to page
        // any further back, mirroring how Next refuses to page past the current month -- see
        // IsEarliestMonthDisplayed/IsCurrentMonthDisplayed.
        [SerializeField] private int maxMonthsBack = 6;

        // The month currently shown, independent of which day is selected -- paging to a
        // different month does not change the footer/selection until a day in it is tapped.
        private int displayedYear;
        private int displayedMonth; // 1-12

        // The day the footer/Play button currently describe. int.MinValue until the first
        // Refresh, which no real absolute day index can be.
        private int selectedAbsoluteDay = int.MinValue;

        [Header("Footer (all pre-built scene objects)")]
        [SerializeField] private Button playButton;
        [SerializeField] private TextMeshProUGUI playButtonText;
        [SerializeField] private TextMeshProUGUI playButtonSubtitleText;

        [Header("Day strip and month shelf (pre-built scene objects)")]
        [SerializeField] private DailyDayCard dayCard;

        // Shown ONLY when today is already solved and the page has walked back to an older
        // unsolved day, so that the strip and the Play button are both describing a catch-up --
        // see RefreshFooter. It keeps a permanent slot in the layout rather than pushing the strip
        // down when it appears: nothing on this screen moves a transform at runtime.
        [SerializeField] private GameObject todaySolvedBanner;
        [SerializeField] private TextMeshProUGUI todaySolvedText;

        // The bar under the month title, and the "12 / 31" beside it. A real Slider driven by its
        // value, laid out and animated the same way the main menu's mode cards drive theirs -- see
        // CountMonthProgress.
        [SerializeField] private Slider monthProgressSlider;
        [SerializeField] private TextMeshProUGUI monthProgressText;

        // How long the bar and the number take to count up from zero, every time the month on
        // screen changes. Matches MainMenuPage's own progressAnimSeconds.
        [SerializeField] private float progressAnimSeconds = 0.35f;

        // The count currently drawn (mid-count or settled) and the coroutine drawing it. Hand-
        // rolled rather than DOTween for the reason MainMenuPage gives at its own ProgressReadout:
        // a DOTween tween on this project's build registered correctly but never advanced past its
        // start value.
        private float progressDisplayed;
        private Coroutine progressRoutine;

        // Oldest month first, the current month last -- see RefreshMonthSummary. Never shows a
        // month older than maxMonthsBack allows, so every chip is a month the calendar can page to.
        [SerializeField] private List<DailyMonthChip> monthChips = new List<DailyMonthChip>();

        // One colour per calendar month, shared with the main menu's Daily card.
        [SerializeField] private DailyMonthPalette monthPalette;

        // The nav/footer buttons are pre-built scene Buttons, wired to these handlers once (not
        // per-button-press) the first time this page refreshes -- see EnsureListenersWired. Day
        // cells are the exception: each needs its OWN date captured in a closure (SetClickOverride),
        // set fresh every PopulateBlock, since which date a given grid slot shows changes as the
        // player pages between months.
        private bool listenersWired;

        // Throttles the day-rollover check in Update() to once a second rather than every frame.
        private float dayRolloverPollTimer;

        // Whether the Play button's subtitle is currently a countdown rather than a date -- see
        // RefreshFooter, which is the only thing that decides it, and TickCountdowns, which may
        // only overwrite that subtitle while it is true.
        private bool footerShowsCountdown;

        // The calendar day this screen's "today" state (lock/complete state on the grid) was built
        // for -- see Update, which rebuilds everything on roll-over.
        private int shownDayIndex = -1;

        private void OnEnable()
        {
            Refresh();
        }

        public void Refresh()
        {
            shownDayIndex = DailyChallengeSelector.TodayIndex;
            PlayerProgress data = ProfileManager.Instance.LoadProgress();

            // No subtitle. The header used to repeat the date ("SUN 25 OCT") directly above a
            // card that said "SUNDAY, OCT 25", above a calendar circling the 25th, above a Play
            // button reading "Sunday, Oct 25" -- one fact, four times, on a screen with nowhere
            // near enough room for it. The strip below says it once; this names the screen.
            if (topPanel != null) { topPanel.SetTopPanel("DAILY CHALLENGE", ""); }

            // Re-picks the selected day EVERY time this page opens (not just the first time, or
            // after a day rollover) -- finishing today's challenge and coming straight back here
            // must immediately show the next thing to do, not the day just solved. Walks backwards
            // from today for as long as it takes to find a day that isn't completed -- could be
            // months back if the player has a long-neglected backlog -- and jumps the displayed
            // month to wherever that lands, so the highlighted cell is actually visible without the
            // player having to page back to find it themselves.
            int candidate = shownDayIndex;
            while (candidate > 0 && data.IsDayCompleted(candidate)) { candidate--; }
            selectedAbsoluteDay = candidate;

            DateTime selectedDate = DailyChallengeSelector.EpochUtc.AddDays(selectedAbsoluteDay);
            displayedYear = selectedDate.Year;
            displayedMonth = selectedDate.Month;

            // A day-rollover Refresh can land mid-slide (rare, but the poll in Update() really can
            // fire while a swipe is animating) -- cancel it and snap both blocks to a known-good
            // resting state rather than let a stale coroutine fight this rebuild.
            if (monthSlideRoutine != null) { StopCoroutine(monthSlideRoutine); monthSlideRoutine = null; }
            isTransitioning = false;

            EnsureListenersWired();
            UpdateMonthTitleAndNav();
            PopulateBlock(ActiveBlock, displayedYear, displayedMonth, data);
            RefreshMonthSummary(data);
            ParkBlocksAtRest();
            RefreshFooter(data);

            dayRolloverPollTimer = 0f;
        }

        /// <summary>Rebuilds the whole screen the moment the day it was built for actually rolls
        /// over, and in between keeps whichever countdown is on screen honest -- both polled once
        /// a second rather than every frame.</summary>
        private void Update()
        {
            dayRolloverPollTimer -= Time.unscaledDeltaTime;
            if (dayRolloverPollTimer > 0f) { return; }
            dayRolloverPollTimer = 1f;

            DateTime now = TimeManager.Now;
            if (DailyChallengeSelector.DayIndex(now) != shownDayIndex)
            {
                Refresh();
                return;
            }

            TickCountdowns(now);
        }

        /// <summary>Re-reads the clock into whichever "next puzzle in ..." line is currently
        /// showing. A countdown that was written once when the screen opened and then sat still is
        /// worse than no countdown: a player who leaves the page open watches a number that is
        /// quietly wrong. Only ever one of the two is up at a time (the banner is for a chosen
        /// catch-up day, the footer for a chosen solved one), but both are checked rather than
        /// tracked, so there is no flag to get out of step with what is actually on screen.</summary>
        private void TickCountdowns(DateTime utcNow)
        {
            if (todaySolvedBanner != null && todaySolvedBanner.activeSelf && todaySolvedText != null)
            {
                todaySolvedText.text = TodaySolvedText(utcNow);
            }

            if (footerShowsCountdown && playButtonSubtitleText != null)
            {
                playButtonSubtitleText.text = DailyMonthPalette.NextPuzzleText(utcNow);
            }
        }

        private static string TodaySolvedText(DateTime utcNow)
        {
            return "Today solved · " + DailyMonthPalette.NextPuzzleText(utcNow);
        }

        // ---- month calendar --------------------------------------------------------------------

        /// <summary>Hooks the pre-built nav/footer buttons up to their handlers exactly once --
        /// idempotent so it can be called from every Refresh with no effect after the first.</summary>
        private void EnsureListenersWired()
        {
            if (listenersWired) { return; }
            listenersWired = true;

            if (prevMonthButton != null) { prevMonthButton.onClick.AddListener(OnPrevMonthClick); }
            if (nextMonthButton != null) { nextMonthButton.onClick.AddListener(OnNextMonthClick); }
            if (playButton != null) { playButton.onClick.AddListener(OnPlaySelectedDayClicked); }
        }

        private void UpdateMonthTitleAndNav()
        {
            if (monthTitleText != null)
            {
                DateTime firstOfMonth = new DateTime(displayedYear, displayedMonth, 1, 0, 0, 0, DateTimeKind.Utc);
                monthTitleText.text = firstOfMonth
                    .ToString("MMMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
            }

            bool canGoNext = !IsCurrentMonthDisplayed();
            bool canGoPrev = !IsEarliestMonthDisplayed();

            // Hidden outright by deactivating the whole button GameObject, rather than dimming it
            // (Button.interactable alone only auto-fades the button's OWN background graphic, which
            // is nearly fully transparent here, so a merely-disabled arrow still looked fully
            // tappable) -- MonthNav has no layout group on it (plain explicit-position RectTransform
            // children), so deactivating one sibling doesn't reflow/reposition the others.
            if (nextMonthButton != null) { nextMonthButton.gameObject.SetActive(canGoNext); }
            if (prevMonthButton != null) { prevMonthButton.gameObject.SetActive(canGoPrev); }
        }

        /// <summary>Fills one block's 42 day cells for a specific (year, month) -- parameterised
        /// rather than always reading <see cref="displayedYear"/>/<see cref="displayedMonth"/>,
        /// since the INCOMING block during a slide represents next/prev month, not the one still
        /// technically "displayed" until the animation settles.</summary>
        private void PopulateBlock(CalendarBlock block, int year, int month, PlayerProgress data)
        {
            DateTime monthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            int daysInMonth = DateTime.DaysInMonth(year, month);
            int leadingBlanks = (int)monthStart.DayOfWeek; // Sunday = 0, matches the S M T W T F S header

            for (int i = 0; i < block.dayCells.Count; i++)
            {
                CalendarDayButton cell = block.dayCells[i];
                if (cell == null) { continue; }

                int dayOfMonth = i - leadingBlanks + 1;

                if (dayOfMonth < 1 || dayOfMonth > daysInMonth)
                {
                    // Stays ACTIVE (see CalendarDayButton.SetBlank's own doc comment) -- a
                    // GridLayoutGroup skips inactive children when positioning cells, so
                    // deactivating this one would collapse the grid by a slot instead of leaving a
                    // gap for it.
                    cell.SetBlank();
                    continue;
                }

                DateTime cellDate = monthStart.AddDays(dayOfMonth - 1);
                int absIdx = DailyChallengeCalendar.AbsoluteDayIndexFor(cellDate);
                bool future = absIdx > shownDayIndex;
                bool completed = data.IsDayCompleted(absIdx);
                bool isSelected = absIdx == selectedAbsoluteDay;

                // Sprite: completed (a permanent fact about the puzzle) beats being the currently
                // selected day -- a selected-but-already-solved day still reads as solved. Anything
                // else (available or locked) gets null, which CalendarDayButton turns into no
                // background at all (just the bare number) -- only a selected or completed day gets
                // a background sprite; locked is just interactable=false plus the grey text.
                Sprite sprite = completed ? daySolvedSprite : isSelected ? dayTodaySprite : null;

                // A completed day can't be selected or replayed -- once solved it's just a record,
                // not something to tap again.
                bool selectable = !future && !completed;
                cell.SetDetails(dayOfMonth, sprite, selectable, completed);

                int capturedAbsIdx = absIdx;
                cell.SetClickOverride(() => OnDayCellClicked(capturedAbsIdx));
            }
        }

        /// <summary>Snaps both blocks to their resting positions with no animation: the active one
        /// at (0,0) inside the viewport, the inactive one parked just off its right edge. Called on
        /// every Refresh (including the very first) so the calendar is always in a sane state
        /// before any transition can begin.</summary>
        private void ParkBlocksAtRest()
        {
            float width = ViewportWidth();
            if (ActiveBlock.root != null) { ActiveBlock.root.anchoredPosition = Vector2.zero; }
            if (InactiveBlock.root != null) { InactiveBlock.root.anchoredPosition = new Vector2(width, 0f); }
        }

        private float ViewportWidth()
        {
            if (calendarViewport != null) { return calendarViewport.rect.width; }
            return blockA.root != null ? blockA.root.rect.width : 944f;
        }

        private void OnDayCellClicked(int absoluteDayIndex)
        {
            selectedAbsoluteDay = absoluteDayIndex;
            PlayerProgress data = ProfileManager.Instance.LoadProgress();
            // Re-resolve the whole active block's sprites/colours so the newly tapped cell picks up
            // the "selected" sprite and the previously selected one gives it back -- PopulateBlock
            // is the only place that knows how to derive that per-cell, and selectedAbsoluteDay just
            // changed underneath it.
            PopulateBlock(ActiveBlock, displayedYear, displayedMonth, data);
            RefreshFooter(data);
        }

        private void RefreshFooter(PlayerProgress data)
        {
            if (selectedAbsoluteDay == int.MinValue) { return; }

            if (playButtonSubtitleText != null)
            {
                DateTime selDate = DailyChallengeSelector.EpochUtc.AddDays(selectedAbsoluteDay);
                playButtonSubtitleText.text = selDate.ToString("dddd, MMM d", CultureInfo.InvariantCulture);
            }

            bool future = selectedAbsoluteDay > shownDayIndex;
            bool completed = data.IsDayCompleted(selectedAbsoluteDay);
            DateTime now = TimeManager.Now;

            // A completed day is a closed record, not replayable -- same rule as the grid cells.
            if (playButton != null) { playButton.interactable = !future && !completed; }
            if (playButtonText != null)
            {
                playButtonText.text = future ? "LOCKED" : (completed ? "SOLVED" : "PLAY");
            }

            // Under a Play button that cannot be pressed, the date it names is the least useful
            // thing the subtitle could carry -- the player can already see which day it is, and
            // cannot act on it. The wait is what they came back to find out, so a solved day's
            // subtitle counts down to the next puzzle instead, and keeps counting (TickCountdowns).
            footerShowsCountdown = completed;
            if (playButtonSubtitleText != null)
            {
                DateTime selDate = DailyChallengeSelector.EpochUtc.AddDays(selectedAbsoluteDay);
                playButtonSubtitleText.text = completed
                    ? DailyMonthPalette.NextPuzzleText(now)
                    : selDate.ToString("dddd, MMM d", CultureInfo.InvariantCulture);
            }

            // The banner answers one situation and no other: today's puzzle IS done, but Refresh
            // walked the selection back to an older unsolved day, so the strip, the Play button and
            // its subtitle are all describing a catch-up -- and without this, finishing today and
            // coming straight back here would show a screen headed "MISSED" with nothing anywhere
            // on it acknowledging what was just solved.
            bool todayDone = data.IsDayCompleted(shownDayIndex);
            bool showBanner = todayDone && selectedAbsoluteDay != shownDayIndex;
            if (todaySolvedBanner != null) { todaySolvedBanner.SetActive(showBanner); }
            if (showBanner && todaySolvedText != null) { todaySolvedText.text = TodaySolvedText(now); }

            RefreshDayCard(data);
        }

        /// <summary>Fills the top card for the chosen day -- the same day the Play button
        /// describes, so it runs from RefreshFooter, whenever the selection changes. Coloured by
        /// the chosen day's own month, which is not always the month the calendar is showing.</summary>
        private void RefreshDayCard(PlayerProgress data)
        {
            if (dayCard == null || selectedAbsoluteDay == int.MinValue) { return; }

            DateTime date = DailyChallengeSelector.EpochUtc.AddDays(selectedAbsoluteDay);
            bool isToday = selectedAbsoluteDay == shownDayIndex;
            bool completed = data.IsDayCompleted(selectedAbsoluteDay);
            int size = DailyChallengeCalendar.LevelForAbsoluteDay(selectedAbsoluteDay).packSize;
            string board = size + "×" + size + " board";

            // The month's solved count is no longer passed: it lives on the bar inside the
            // calendar card now (see RefreshMonthSummary), under the month it actually counts.
            dayCard.SetDay(
                date.ToString("ddd", CultureInfo.InvariantCulture).ToUpperInvariant(),
                date.Day.ToString(CultureInfo.InvariantCulture),
                completed ? (isToday ? "TODAY'S PUZZLE SOLVED" : "PUZZLE SOLVED")
                    : isToday ? "TODAY'S PUZZLE" : "CATCH-UP PUZZLE",
                completed ? board
                    : isToday ? board + " · not solved yet"
                    : board + " · missed on " + date.ToString("MMM d", CultureInfo.InvariantCulture),
                completed ? "SOLVED" : isToday ? "NEW" : "MISSED",
                completed ? DailyDayCard.DayState.Solved
                    : isToday ? DailyDayCard.DayState.Today
                    : DailyDayCard.DayState.Missed,
                MonthColour(date.Month));
        }

        private void OnPrevMonthClick()
        {
            if (!InputManager.Instance.CanInput()) { return; }
            if (isTransitioning) { return; }
            if (IsEarliestMonthDisplayed()) { return; } // the oldest browsable month is the floor -- see maxMonthsBack

            AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
            StepMonth(-1);
        }

        private void OnNextMonthClick()
        {
            if (!InputManager.Instance.CanInput()) { return; }
            if (isTransitioning) { return; }
            if (IsCurrentMonthDisplayed()) { return; } // the current calendar month is the ceiling -- nothing beyond it is unlocked yet

            AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
            StepMonth(1);
        }

        /// <summary>Pages the calendar one month by <paramref name="direction"/> (+1 or -1) -- see
        /// <see cref="SlideToMonth"/>.</summary>
        private void StepMonth(int direction)
        {
            int year = displayedYear;
            int month = displayedMonth + direction;
            if (month < 1) { month = 12; year--; }
            else if (month > 12) { month = 1; year++; }

            SlideToMonth(year, month, direction);
        }

        /// <summary>Makes (<paramref name="year"/>, <paramref name="month"/>) the displayed month,
        /// fills the INACTIVE block with it, and slides it into view while the current one slides
        /// out -- <paramref name="direction"/> (+1 or -1) picks which side each block enters/exits
        /// from, so a later month always feels like paging forward and an earlier one like paging
        /// back, however many months apart they are.</summary>
        private void SlideToMonth(int year, int month, int direction)
        {
            displayedYear = year;
            displayedMonth = month;

            UpdateMonthTitleAndNav();

            CalendarBlock outgoing = ActiveBlock;
            CalendarBlock incoming = InactiveBlock;

            PlayerProgress data = ProfileManager.Instance.LoadProgress();
            PopulateBlock(incoming, displayedYear, displayedMonth, data);
            RefreshMonthSummary(data);

            float width = ViewportWidth();
            if (incoming.root != null) { incoming.root.anchoredPosition = new Vector2(direction * width, 0f); }

            activeBlockIndex = 1 - activeBlockIndex;

            if (monthSlideRoutine != null) { StopCoroutine(monthSlideRoutine); }
            monthSlideRoutine = StartCoroutine(SlideMonths(outgoing.root, incoming.root, direction, width));
        }

        /// <summary>Slides <paramref name="outgoing"/> off the opposite side from
        /// <paramref name="direction"/> while <paramref name="incoming"/> slides in to (0,0) --
        /// hand-rolled rather than a DOTween tween on anchoredPosition, the same choice
        /// LevelCompletePage's sheet slide and MainMenuPage's card reveal already made after a
        /// DOAnchorPos tween on this project's build registered correctly but never actually
        /// advanced past its start value.</summary>
        private IEnumerator SlideMonths(RectTransform outgoing, RectTransform incoming, int direction, float width)
        {
            isTransitioning = true;

            Vector2 outgoingTo = new Vector2(-direction * width, 0f);
            Vector2 incomingFrom = incoming != null ? incoming.anchoredPosition : Vector2.zero;

            float elapsed = 0f;
            while (elapsed < monthSlideSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / monthSlideSeconds);
                float eased = 1f - Mathf.Pow(1f - t, 3f); // ease-out cubic, same as LevelCompletePage.SlideSheet

                if (outgoing != null) { outgoing.anchoredPosition = Vector2.LerpUnclamped(Vector2.zero, outgoingTo, eased); }
                if (incoming != null) { incoming.anchoredPosition = Vector2.LerpUnclamped(incomingFrom, Vector2.zero, eased); }
                yield return null;
            }

            if (outgoing != null) { outgoing.anchoredPosition = outgoingTo; }
            if (incoming != null) { incoming.anchoredPosition = Vector2.zero; }

            isTransitioning = false;
            monthSlideRoutine = null;
        }

        /// <summary>Whether the month currently paged to is the real calendar month "today" falls
        /// in -- the ceiling <see cref="OnNextMonthClick"/> refuses to page past, since no day
        /// beyond it is unlocked anyway.</summary>
        private bool IsCurrentMonthDisplayed()
        {
            DateTime today = DailyChallengeSelector.EpochUtc.AddDays(shownDayIndex);
            return displayedYear == today.Year && displayedMonth == today.Month;
        }

        /// <summary>Whether the month currently paged to is the oldest one the calendar allows
        /// browsing back to -- the floor <see cref="OnPrevMonthClick"/> refuses to page past. See
        /// <see cref="maxMonthsBack"/>'s own comment for exactly which month that is.</summary>
        private bool IsEarliestMonthDisplayed()
        {
            DateTime today = DailyChallengeSelector.EpochUtc.AddDays(shownDayIndex);
            DateTime firstOfCurrentMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime earliest = firstOfCurrentMonth.AddMonths(-(Mathf.Max(1, maxMonthsBack) - 1));
            return displayedYear == earliest.Year && displayedMonth == earliest.Month;
        }

        // ---- month badge and shelf -------------------------------------------------------------

        /// <summary>Lays the shelf out as the most recent months, oldest first, the current month
        /// last, highlighting the displayed one. Called whenever the displayed month changes, so
        /// the highlighted chip always matches the calendar.</summary>
        private void RefreshMonthSummary(PlayerProgress data)
        {
            if (monthChips == null) { return; }

            DateTime today = DailyChallengeSelector.EpochUtc.AddDays(shownDayIndex);
            DateTime currentMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            // A chip older than the calendar's own paging floor would show a month the arrows can't
            // reach, so any chips beyond maxMonthsBack are hidden rather than shown.
            int shownChips = Mathf.Min(monthChips.Count, Mathf.Max(1, maxMonthsBack));

            for (int i = 0; i < monthChips.Count; i++)
            {
                DailyMonthChip chip = monthChips[i];
                if (chip == null) { continue; }

                int monthsBack = monthChips.Count - 1 - i;
                bool visible = monthsBack < shownChips;
                chip.gameObject.SetActive(visible);
                if (!visible) { continue; }

                DateTime month = currentMonth.AddMonths(-monthsBack);
                Color colour = MonthColour(month.Month);
                chip.SetDetails(MonthLabel(month.Year, month.Month, "MMM"), colour, TextColourOn(colour),
                    data.CompletedDaysInMonth(month.Year, month.Month), DateTime.DaysInMonth(month.Year, month.Month),
                    month.Year == displayedYear && month.Month == displayedMonth);

                int chipYear = month.Year;
                int chipMonth = month.Month;
                chip.SetClickOverride(() => OnMonthChipClicked(chipYear, chipMonth));
            }

            RefreshMonthProgress(data);
        }

        /// <summary>The bar under the month title: how much of the month on screen is solved.
        ///
        /// Counts the DISPLAYED month, not the month the selected day belongs to. The bar sits
        /// inside the calendar card directly beneath that month's name, so it has to answer for
        /// what is actually being looked at -- paging back to September must show September's
        /// count even while the Play button still offers an October day. Paging calls back through
        /// RefreshMonthSummary, so it follows on its own.
        ///
        /// Against the whole month, including days not yet reachable: it is the same figure the
        /// ring on the old hero card showed ("0 OF 31 DAYS"), and the month chips still show, so
        /// all three would otherwise disagree about what "the month" means mid-month.</summary>
        private void RefreshMonthProgress(PlayerProgress data)
        {
            int solvedInMonth = data.CompletedDaysInMonth(displayedYear, displayedMonth);
            int daysInMonth = DateTime.DaysInMonth(displayedYear, displayedMonth);

            if (monthProgressSlider == null && monthProgressText == null) { return; }

            if (progressRoutine != null) { StopCoroutine(progressRoutine); progressRoutine = null; }

            // Refresh runs from OnEnable, so the page is active by the time this is reached -- but
            // StartCoroutine throws on an inactive GameObject, and a bar left mid-count because
            // the page closed under it would come back wrong. Snap to the final figure instead
            // whenever there is nothing running to animate on.
            if (!isActiveAndEnabled)
            {
                progressDisplayed = solvedInMonth;
                SetMonthProgress(solvedInMonth, daysInMonth);
                return;
            }

            progressRoutine = StartCoroutine(CountMonthProgress(solvedInMonth, daysInMonth));
        }

        /// <summary>Counts the bar and the number up from zero together, the same way the main
        /// menu's mode cards do (MainMenuPage.CountProgress) -- unscaled, ease-out quad, over
        /// <see cref="progressAnimSeconds"/>.
        ///
        /// Always FROM zero rather than from whatever the last month showed: paging from a solved
        /// month to an empty one would otherwise animate downwards, which reads as progress being
        /// taken away rather than a different month being counted.
        ///
        /// The frame set aside before timing starts is not a stylistic pause. This is started from
        /// the middle of a page transition or a month slide, and Time.unscaledDeltaTime on that
        /// same frame reflects however long that took rather than an ordinary frame -- counting
        /// from that sample let one hitch consume the whole animation in a single invisible jump.
        /// MainMenuPage hit exactly this and solved it the same way.</summary>
        private IEnumerator CountMonthProgress(int solved, int daysInMonth)
        {
            progressDisplayed = 0f;
            SetMonthProgress(0f, daysInMonth);

            yield return null;

            float elapsed = 0f;
            while (elapsed < progressAnimSeconds)
            {
                elapsed += Time.unscaledDeltaTime;

                float t = Mathf.Clamp01(elapsed / progressAnimSeconds);
                float eased = 1f - ((1f - t) * (1f - t));

                progressDisplayed = Mathf.LerpUnclamped(0f, solved, eased);
                SetMonthProgress(progressDisplayed, daysInMonth);
                yield return null;
            }

            progressDisplayed = solved;
            SetMonthProgress(solved, daysInMonth);
            progressRoutine = null;
        }

        /// <summary>Draws one solved-days count across the bar and the number beside it. Takes the
        /// raw (possibly fractional, mid-count) value rather than an int: the bar fills
        /// continuously off it while the number rounds itself, so the figure stays whole while the
        /// bar under it still moves.</summary>
        private void SetMonthProgress(float solved, int daysInMonth)
        {
            if (monthProgressSlider != null)
            {
                monthProgressSlider.value = daysInMonth > 0 ? Mathf.Clamp01(solved / daysInMonth) : 0f;
            }
            if (monthProgressText != null)
            {
                monthProgressText.text = Mathf.RoundToInt(solved) + " / " + daysInMonth;
            }
        }

        /// <summary>A shelf chip was tapped -- DailyMonthChip.OnButtonClick has already gated on
        /// CanInput and played the click, so this must not gate again.</summary>
        private void OnMonthChipClicked(int year, int month)
        {
            if (isTransitioning) { return; }

            int delta = (year - displayedYear) * 12 + (month - displayedMonth);
            if (delta == 0) { return; }

            SlideToMonth(year, month, delta > 0 ? 1 : -1);
        }

        private static string MonthLabel(int year, int month, string format)
        {
            return new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc)
                .ToString(format, CultureInfo.InvariantCulture).ToUpperInvariant();
        }

        private Color MonthColour(int month)
        {
            return monthPalette != null ? monthPalette.Colour(month) : Color.white;
        }

        private Color TextColourOn(Color colour)
        {
            return monthPalette != null ? monthPalette.TextOn(colour) : Color.white;
        }

        private void OnPlaySelectedDayClicked()
        {
            if (!InputManager.Instance.CanInput()) { return; }
            if (selectedAbsoluteDay == int.MinValue || selectedAbsoluteDay > shownDayIndex) { return; }
            if (ProfileManager.Instance.LoadProgress().IsDayCompleted(selectedAbsoluteDay)) { return; }

            AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
            UIController.Instance.LoadDailyChallengeForDay(selectedAbsoluteDay);
        }

        public void OnBackButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
                PageManager.Instance.ClosePage();
            }
        }
    }
}
