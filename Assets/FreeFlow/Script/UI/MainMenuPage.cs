using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
using TMPro;
using DG.Tweening;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using FreeFlow.Input;

namespace FreeFlow.UI
{
    /// <summary>
    /// Owns behavior local to the main menu screen itself: the header's hint balance, and the
    /// three stacked cards -- Daily challenge, Classic, Advanced -- each of which is one button
    /// (there is no separate PLAY button inside a card). Classic and Advanced are fixed colour
    /// cards; the Daily card takes this month's colour from the same palette the Daily Challenge
    /// page uses, and its ring fills with the days solved this month. Layout is authored in the
    /// scene -- this only sets text, colours, fills and which bits are shown. Cross-screen flow
    /// (which screen is active, level loading) stays in UIController/PageManager.
    /// </summary>
    public class MainMenuPage : Page
    {
        [SerializeField] private TopPanel topPanel;

        [Header("Header")]
        // The player's spendable hint balance (PlayerProgress.hintsRemaining). Display only --
        // hints are topped up from gameplay, not from here.
        [SerializeField] private TextMeshProUGUI hintCountText;

        [Header("Mode cards")]
        [SerializeField] private TextMeshProUGUI classicProgressText;
        [SerializeField] private Slider classicProgressSlider;
        [SerializeField] private TextMeshProUGUI advancedProgressText;
        [SerializeField] private Slider advancedProgressSlider;

        // "START HERE" pill on the Classic card, shown only while nothing has been played in
        // either mode, so a brand-new player is not left choosing between three equal cards.
        [SerializeField] private GameObject classicStartHere;

        // How long each card's number counts up over, from zero to the real count, every time the
        // card is shown -- see AnimateProgressText.
        [SerializeField] private float progressAnimSeconds = 0.35f;
        private readonly ProgressReadout classicProgress = new ProgressReadout();
        private readonly ProgressReadout advancedProgress = new ProgressReadout();

        /// <summary>One mode card's animation state -- the count currently on screen (mid-tween or
        /// not) and the coroutine driving it, kept per card since both animate independently. A
        /// hand-rolled coroutine rather than DOTween.To here: the same pattern LevelCompletePage's
        /// sheet slide ended up needing, after a DOTween tween on this project's build registered
        /// correctly but never actually advanced past its start value.</summary>
        private class ProgressReadout
        {
            public float displayed;
            public Coroutine routine;
        }

        [Header("Daily challenge card")]
        // Today's date, from the same UTC day index the Daily Challenge page and the save file use
        // -- never DateTime.Now, which can already be tomorrow ahead of UTC.
        [FormerlySerializedAs("dailySubtitleText")]
        [SerializeField] private TextMeshProUGUI dailyDateText;
        [SerializeField] private TextMeshProUGUI dailyBoardText;

        // One pill with two states: NEW until today's board is solved, SOLVED after. NEW's text
        // is a darker shade of this month's colour, so it matches the card it sits on.
        [SerializeField] private Image dailyBadgeImage;
        [SerializeField] private TextMeshProUGUI dailyBadgeText;
        [SerializeField] private Color newBadgeColor = Color.white;
        [SerializeField] private Color solvedBadgeColor = Color.white;
        [SerializeField] private Color solvedBadgeTextColor = new Color32(11, 122, 105, 255);

        // The card takes this month's colour, from the palette the Daily Challenge page also uses.
        [SerializeField] private DailyMonthPalette monthPalette;
        [SerializeField] private Image dailyCardImage;
        // Coloured shadow under the card: a darker shade of the month colour at this alpha.
        [SerializeField] private Image dailyCardShadow;
        [SerializeField] private float dailyShadowShade = 0.55f;
        [SerializeField] private float dailyShadowAlpha = 0.45f;

        // White ring around a white dot, filled by this month's solved days, with the count inside.
        [SerializeField] private Image dailyMonthRingFill;
        [SerializeField] private TextMeshProUGUI dailyMonthCountText;
        [SerializeField] private TextMeshProUGUI dailyMonthCountSubText;

        // Everything drawn straight on the card (title, date, board/countdown line, arrow): white
        // on most month colours, dark on the light ones (lime, yellow).
        [SerializeField] private Graphic[] dailyCardForeground;

        // While today is solved, the board-size line becomes a countdown to the next puzzle --
        // refreshed once a second, which also catches the day rolling over while the menu is open.
        private bool dailySolvedShown;
        private int dailyShownDay = -1;
        private float dailyPollTimer;

        [Header("Card reveal")]
        // Tab_Classic, Tab_Advanced, Button_dailyChallenge -- in the order they should pop in.
        // Exactly the page's three cards, authored in the Inspector rather than found by name so
        // the reveal order is an explicit choice, not whatever GetComponentsInChildren happens to
        // return.
        [SerializeField] private RectTransform[] revealCards;
        [SerializeField] private float revealStaggerSeconds = 0.08f;
        [SerializeField] private float revealCardSeconds = 0.3f;

        // Parallel to revealCards. Resting scale is read once at Awake (almost certainly (1,1,1),
        // but not assumed) rather than hard-coded, and the CanvasGroup is added lazily the same
        // way Page.FadeGroup is -- these cards never needed one before this.
        private Vector3[] revealRestingScale;
        private CanvasGroup[] revealGroups;
        private Tween[] revealTweens;

        private void Awake()
        {
            if (revealCards == null) { return; }

            revealRestingScale = new Vector3[revealCards.Length];
            revealGroups = new CanvasGroup[revealCards.Length];
            revealTweens = new Tween[revealCards.Length];

            for (int i = 0; i < revealCards.Length; i++)
            {
                if (revealCards[i] == null) { continue; }

                revealRestingScale[i] = revealCards[i].localScale;

                CanvasGroup group = revealCards[i].GetComponent<CanvasGroup>();
                if (group == null) { group = revealCards[i].gameObject.AddComponent<CanvasGroup>(); }
                revealGroups[i] = group;
            }
        }

        /// <summary>Pops the three cards in one after another rather than all at once with the
        /// page's own base fade -- runs after base.Open() activates the page, since a card has to
        /// be active for its own tween to actually move it.</summary>
        public override void Open()
        {
            base.Open();

            if (revealCards == null) { return; }

            for (int i = 0; i < revealCards.Length; i++)
            {
                RectTransform card = revealCards[i];
                if (card == null) { continue; }

                revealTweens[i]?.Kill();

                card.localScale = revealRestingScale[i] * 0.85f;
                CanvasGroup group = revealGroups[i];
                if (group != null) { group.alpha = 0f; }

                Sequence reveal = DOTween.Sequence().SetUpdate(true).SetDelay(i * revealStaggerSeconds);
                reveal.Join(card.DOScale(revealRestingScale[i], revealCardSeconds).SetEase(Ease.OutBack));
                if (group != null) { reveal.Join(group.DOFade(1f, revealCardSeconds)); }

                revealTweens[i] = reveal;
            }
        }

        private void OnEnable()
        {
            Refresh();
        }

        /// <summary>Re-reads save data and updates the hint balance and all three cards.
        /// Called on enable (not just Start) since returning from a level changes this screen's
        /// own numbers without the GameObject being recreated.</summary>
        public void Refresh()
        {
            UIController ui = UIController.Instance;
            if (ui == null) { return; }
            PlayerProgress data = ProfileManager.Instance.LoadProgress();

            // MainMenu is the root page (nothing to go back to) and carries no title -- only the
            // Setting button is needed here, no title/subtitle/Back/Option.
            if (topPanel != null) { topPanel.SetTopPanel("", "", showBack: false, showSetting: true, showOption: false); }

            if (hintCountText != null) { hintCountText.text = data.hintsRemaining.ToString(); }

            int classicCompleted = AnimateProgressText(classicProgressText, classicProgressSlider, classicProgress, data, ui, GameMode.Classic);
            int advancedCompleted = AnimateProgressText(advancedProgressText, advancedProgressSlider, advancedProgress, data, ui, GameMode.Advanced);
            if (classicStartHere != null) { classicStartHere.SetActive(classicCompleted == 0 && advancedCompleted == 0); }

            SetDailyChallengeCard(data);
        }

        /// <summary>Keeps the solved-today countdown ticking, and rebuilds the Daily card if the day
        /// rolls over while the menu is open -- polled once a second, not every frame.</summary>
        private void Update()
        {
            dailyPollTimer -= Time.unscaledDeltaTime;
            if (dailyPollTimer > 0f) { return; }
            dailyPollTimer = 1f;

            System.DateTime now = System.DateTime.UtcNow;
            if (DailyChallengeSelector.DayIndex(now) != dailyShownDay)
            {
                if (ProfileManager.Instance != null) { SetDailyChallengeCard(ProfileManager.Instance.LoadProgress()); }
                return;
            }
            if (dailySolvedShown && dailyBoardText != null) { dailyBoardText.text = NextPuzzleText(now); }
        }

        /// <summary>
        /// Fills the daily-challenge card from the save: today's date, the size of today's board,
        /// and whether it has been solved yet. Every player's today is the same fixed level (see
        /// DailyChallengeCalendar), so this is a pure readout -- nothing is committed just by the
        /// main menu being shown.
        /// </summary>
        private void SetDailyChallengeCard(PlayerProgress data)
        {
            System.DateTime now = System.DateTime.UtcNow;
            int today = DailyChallengeSelector.DayIndex(now);
            bool solvedToday = data.IsDayCompleted(today);
            System.DateTime todayDate = DailyChallengeSelector.EpochUtc.AddDays(today);
            dailyShownDay = today;
            dailySolvedShown = solvedToday;

            Color monthColour = monthPalette != null ? monthPalette.Colour(todayDate.Month) : Color.white;
            bool lightMonth = monthPalette != null && monthPalette.IsLight(monthColour);
            Color foreground = monthPalette != null ? monthPalette.TextOn(monthColour) : Color.white;

            if (dailyCardImage != null) { dailyCardImage.color = monthColour; }
            if (dailyCardShadow != null)
            {
                Color shadow = DailyMonthPalette.Shade(monthColour, dailyShadowShade);
                shadow.a = dailyShadowAlpha;
                dailyCardShadow.color = shadow;
            }
            if (dailyCardForeground != null)
            {
                for (int i = 0; i < dailyCardForeground.Length; i++)
                {
                    if (dailyCardForeground[i] != null) { dailyCardForeground[i].color = foreground; }
                }
            }

            int daysInMonth = System.DateTime.DaysInMonth(todayDate.Year, todayDate.Month);
            int solvedThisMonth = data.CompletedDaysInMonth(todayDate.Year, todayDate.Month);
            if (dailyMonthRingFill != null) { dailyMonthRingFill.fillAmount = (float)solvedThisMonth / daysInMonth; }
            if (dailyMonthCountText != null)
            {
                dailyMonthCountText.text = solvedThisMonth.ToString();
                dailyMonthCountText.color = DailyMonthPalette.Shade(monthColour, lightMonth ? 0.6f : 0.9f);
            }
            if (dailyMonthCountSubText != null)
            {
                dailyMonthCountSubText.text = "OF " + daysInMonth + " DAYS";
                dailyMonthCountSubText.color = DailyMonthPalette.Shade(monthColour, lightMonth ? 0.45f : 0.7f);
            }

            if (dailyDateText != null)
            {
                dailyDateText.text = todayDate.ToString("dddd, MMMM d", CultureInfo.InvariantCulture);
            }

            if (dailyBoardText != null)
            {
                int size = DailyChallengeCalendar.LevelForAbsoluteDay(today).packSize;
                dailyBoardText.text = solvedToday ? NextPuzzleText(now) : size + "×" + size + " board";
            }

            if (dailyBadgeText != null)
            {
                dailyBadgeText.text = solvedToday ? "SOLVED" : "NEW";
                dailyBadgeText.color = solvedToday ? solvedBadgeTextColor : DailyMonthPalette.Shade(monthColour, lightMonth ? 0.55f : 0.8f);
            }
            if (dailyBadgeImage != null) { dailyBadgeImage.color = solvedToday ? solvedBadgeColor : newBadgeColor; }
        }

        private static string NextPuzzleText(System.DateTime utcNow)
        {
            System.TimeSpan left = DailyChallengeSelector.TimeUntilNextDay(utcNow);
            int hours = (int)left.TotalHours;
            return hours > 0 ? "Next in " + hours + "h " + left.Minutes + "m" : "Next in " + Mathf.Max(1, left.Minutes) + "m";
        }

        /// <summary>Summed across every pack size in the mode (e.g. Classic's 5x5 + 6x6 + 7x7 +
        /// ...), not just one representative size -- this is the mode's true overall progress,
        /// which is what the card is actually claiming to show.
        ///
        /// Always counts up FROM ZERO, every time the card is shown -- not from whatever was
        /// displayed last visit. An earlier version animated from the last-shown count instead, so
        /// two visits in a row with no level finished in between produced a correct but invisible
        /// zero-distance "animation", which read as broken. The bar fills in step with the number,
        /// from the same displayed value, so the two can never disagree mid-count.
        ///
        /// Returns the mode's real completed count, so Refresh can tell a brand-new player apart
        /// without summing the packs a second time.</summary>
        private int AnimateProgressText(TextMeshProUGUI text, Slider slider, ProgressReadout readout,
            PlayerProgress data, UIController ui, GameMode mode)
        {
            int completed = 0;
            int total = 0;
            int[] sizes = ui.PackSizesFor(mode);
            for (int i = 0; i < sizes.Length; i++)
            {
                completed += data.CompletedLevelForKey(ui.KeyFor(mode, sizes[i]));
                total += ui.PackLevelCountFor(sizes[i]);
            }

            if (text == null && slider == null) { return completed; }

            if (readout.routine != null) { StopCoroutine(readout.routine); }
            readout.routine = StartCoroutine(CountProgress(text, slider, readout, completed, total));
            return completed;
        }

        /// <summary>Eases <paramref name="readout"/>'s displayed count from zero up to
        /// <paramref name="completed"/> over <see cref="progressAnimSeconds"/>.</summary>
        private IEnumerator CountProgress(TextMeshProUGUI text, Slider slider, ProgressReadout readout,
            int completed, int total)
        {
            readout.displayed = 0f;
            SetProgress(text, slider, 0f, total);

            // One frame set aside before timing anything: this coroutine is started synchronously
            // from the middle of a page transition (Instantiate/Destroy, layout rebuilds), and
            // Time.unscaledDeltaTime on that same frame reflects however long the transition took,
            // not an ordinary frame. Counting elapsed time from THAT sample let one hitch account
            // for the whole animation in a single invisible jump -- the number would already read
            // its final value before anyone saw it move.
            yield return null;

            float from = 0f;
            float elapsed = 0f;

            while (elapsed < progressAnimSeconds)
            {
                elapsed += Time.unscaledDeltaTime;

                // Ease-out quad: quick off the start, settling into the final count rather than
                // arriving at a constant rate.
                float t = Mathf.Clamp01(elapsed / progressAnimSeconds);
                float eased = 1f - ((1f - t) * (1f - t));

                readout.displayed = Mathf.LerpUnclamped(from, completed, eased);
                SetProgress(text, slider, readout.displayed, total);
                yield return null;
            }

            readout.displayed = completed;
            SetProgress(text, slider, completed, total);
            readout.routine = null;
        }

        // The count is bold rather than a separate accent colour: these lines now sit on solid
        // colour cards in white, where a coloured number would not read.
        private static void SetProgress(TextMeshProUGUI text, Slider slider, float completed, int total)
        {
            if (text != null)
            {
                text.text = "<b>" + Mathf.RoundToInt(completed) + "</b> / " + total + " levels";
            }
            if (slider != null) { slider.value = total > 0 ? completed / total : 0f; }
        }

        /// <summary>Each mode card (CLASSIC/ADVANCED) is itself the button -- these set the mode
        /// the tapped card belongs to before opening pack-select.</summary>
        public void OnPlayClassicButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
                UIController.Instance.SetMode(GameMode.Classic);
                PageManager.Instance.OpenPage(PageType.PackSelect);
            }
        }

        public void OnPlayAdvancedButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
                UIController.Instance.SetMode(GameMode.Advanced);
                PageManager.Instance.OpenPage(PageType.PackSelect);
            }
        }

        /// <summary>Opens the Daily Challenge calendar rather than jumping straight into gameplay
        /// -- actually loading a challenge is done from that page's own Play button.</summary>
        public void OnDailyChallengeButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
                PageManager.Instance.OpenPage(PageType.DailyChallenge);
            }
        }
    }
}
