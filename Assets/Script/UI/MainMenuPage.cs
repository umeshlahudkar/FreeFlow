using System.Collections;
using UnityEngine;
using TMPro;
using DG.Tweening;
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
        // Today's state in one line, plus the current and best streaks as a tail. Used to be
        // a separate streak pill next to the card; folded in here when that pill was removed.
        [SerializeField] private TextMeshProUGUI dailySubtitleText;

        // Shown from a daily reset until the player opens the hub -- unseen content, not unplayed
        // content, so it clears on a visit even if nothing is solved. Reads
        // DailyChallengeData.dailyChallengeCachedDay rather than a dedicated "seen" flag: the hub
        // is the only thing that ever advances that field to a new day (see
        // DailyChallengePage.Refresh), so "today's picks are cached" and "the player has opened
        // the hub today" are the same fact in this codebase.
        [SerializeField] private GameObject newBadge;

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

        /// <summary>Re-reads save data and updates the header chip + both cards' progress lines.
        /// Called on enable (not just Start) since returning from a level changes this screen's
        /// own numbers without the GameObject being recreated.</summary>
        public void Refresh()
        {
            UIController ui = UIController.Instance;
            if (ui == null) { return; }
            SaveData data = SavingSystem.Instance.Load();
            DailyChallengeData dailyData = DailyChallengeSystem.Instance.Load();

            // MainMenu is the root page (nothing to go back to) and already has its own
            // branding (GameNameLabel/Wordmark) plus the level chip below -- only the Setting
            // button is needed here, no title/subtitle/Back/Option.
            if (topPanel != null) { topPanel.SetTopPanel("", "", showBack: false, showSetting: true, showOption: false); }

            if (levelChipText != null)
            {
                int nextLevel = data.CompletedLevelForKey(DefaultKey(ui, GameMode.Classic)) + 1;
                levelChipText.text = "Level " + nextLevel;
            }

            AnimateProgressText(classicProgressText, classicProgress, data, ui, GameMode.Classic);
            AnimateProgressText(advancedProgressText, advancedProgress, data, ui, GameMode.Advanced);
            SetDailyChallengeCard(dailyData, ui);
        }

        /// <summary>
        /// Fills the daily-challenge card from the save: whether today's one calendar level has
        /// been solved yet, and the streaks. There is nothing to "select" any more (see
        /// DailyChallengeCalendar) -- every player's today is the same fixed level, so this is a
        /// pure readout of the save file, nothing committed just by the main menu being shown.
        /// </summary>
        private void SetDailyChallengeCard(DailyChallengeData data, UIController ui)
        {
            int today = FreeFlow.GamePlay.DailyChallengeSelector.DayIndex(System.DateTime.UtcNow);
            bool completedToday = data.IsDayCompleted(today);

            if (newBadge != null) { newBadge.SetActive(!completedToday); }

            if (dailySubtitleText != null)
            {
                string progress = completedToday ? "today's puzzle solved" : "today's puzzle waiting";

                // The LIVE streak, not the stored one -- a lapsed run keeps its last count in
                // the save until another day is credited, and a line boasting "3-day streak"
                // days after the streak actually died is worse than not mentioning it at all.
                // Only earns its space once there is one, same as best below.
                int streak = data.LiveDailyChallengeStreak(today);
                string current = streak > 0 ? "  ·  " + streak + "-day streak" : "";

                // The best streak only earns its space once there is one; on a fresh save the
                // line stays about today rather than trailing a hollow "best 0".
                string best = data.bestDailyChallengeStreak > 0
                    ? "  ·  best " + data.bestDailyChallengeStreak
                    : "";

                dailySubtitleText.text = progress + current + best;
            }
        }

        /// <summary>Summed across every pack size in the mode (e.g. Classic's 5x5 + 6x6 + 7x7 +
        /// ...), not just one representative size -- this is the mode's true overall progress,
        /// which is what the card is actually claiming to show.
        ///
        /// Always counts up FROM ZERO, every time the card is shown -- not from whatever was
        /// displayed last visit. An earlier version animated from the last-shown count instead, so
        /// two visits in a row with no level finished in between produced a correct but invisible
        /// zero-distance "animation", which read as broken. Matching PackCard's own reveal treatment
        /// is simpler and always visibly plays. Starts immediately, with no delay tying it to the
        /// card reveal above -- an earlier version waited for that reveal to finish first, which
        /// was pulled per explicit request.</summary>
        private void AnimateProgressText(TextMeshProUGUI text, ProgressReadout readout, SaveData data, UIController ui, GameMode mode)
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

            if (readout.routine != null) { StopCoroutine(readout.routine); }
            readout.routine = StartCoroutine(CountProgress(text, readout, completed, total));
        }

        /// <summary>Eases <paramref name="readout"/>'s displayed count from zero up to
        /// <paramref name="completed"/> over <see cref="progressAnimSeconds"/>.</summary>
        private IEnumerator CountProgress(TextMeshProUGUI text, ProgressReadout readout, int completed, int total)
        {
            readout.displayed = 0f;
            SetProgressLabel(text, 0f, total);

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
                SetProgressLabel(text, readout.displayed, total);
                yield return null;
            }

            readout.displayed = completed;
            SetProgressLabel(text, completed, total);
            readout.routine = null;
        }

        private static void SetProgressLabel(TextMeshProUGUI text, float completed, int total)
        {
            text.text = "<color=#0F9E88>" + Mathf.RoundToInt(completed) + "</color> / " + total + " levels";
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

        /// <summary>Opens the Daily Challenge hub (streak, this week, today's pick) rather than
        /// jumping straight into gameplay -- actually loading a challenge is done by tapping its
        /// own card on that hub.</summary>
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
