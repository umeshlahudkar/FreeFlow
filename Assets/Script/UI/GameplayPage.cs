using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using FreeFlow.Input;

namespace FreeFlow.UI
{
    /// <summary>The gameplay screen, and the owner of every widget on it: the header, the
    /// progress card, the hint button and the footer's prev/next buttons. What those widgets SAY
    /// is still UIController's to decide -- this page reads the wording and the run state straight
    /// off it rather than composing either itself, so the header and the buttons say the same
    /// thing here as on the level-complete overlay, whether the player came in from a pack or
    /// from the daily challenge.</summary>
    public class GameplayPage : Page
    {
        [SerializeField] private TopPanel topPanel;

        [Header("Progress card")]
        // Cells rather than pairs, deliberately -- see UpdateFilledCells.
        [SerializeField] private TextMeshProUGUI cellsText;
        [SerializeField] private TextMeshProUGUI percentText;
        [SerializeField] private Slider progressSlider;

        // How long a change in filled-cell count takes to travel across the cells readout, the
        // percent readout and the slider together -- one duration for all three, since they are
        // one number shown three ways rather than three independent widgets.
        [SerializeField] private float progressAnimSeconds = 0.35f;

        // The count currently ON SCREEN, which may still be animating toward the board's real
        // filled count. Read back as the next animation's start so a second path completing before
        // the first finishes animating continues from where the eye actually is, not from the real
        // (already-ahead) count -- and reset to the target outright on a fresh board (see Refresh),
        // where there is nothing to animate FROM.
        private float displayedFilledCells;
        private Tween progressTween;

        [Header("Hint")]
        // Left interactable with an empty balance on purpose -- see OnHintButtonClick.
        [SerializeField] private Button hintButton;
        // The "x3" pill: how many hints are left to spend.
        [SerializeField] private TextMeshProUGUI hintCountText;
        // The pill itself, shown only while there's a balance to display.
        [SerializeField] private GameObject countPill;
        // Shown in place of the hint pill once the balance hits zero. UI-only for now --
        // the watch-ad reward flow behind them lands in a later change.
        [SerializeField] private GameObject watchAdPill;
        [SerializeField] private GameObject watchAdHint;

        [Header("Level navigation")]
        // The footer's two stepping buttons. Faded and inert rather than hidden when a step is
        // unavailable (level 1, a next level still behind the unlock frontier, the first/last of
        // today's challenges) so the footer does not reshuffle itself as the player moves along it.
        [SerializeField] private Button prevButton;
        [SerializeField] private Button nextButton;

        // Each button's two lines: the heading says what the button DOES ("NEXT DAILY"), the
        // subtitle what it would open ("LEVEL 74 · 6×6"). The same two steps the level-complete
        // overlay offers, in the shorter wording these narrower buttons can hold -- see
        // UIController's footer step labels.
        [SerializeField] private TextMeshProUGUI prevTitleText;
        [SerializeField] private TextMeshProUGUI prevSubtitleText;
        [SerializeField] private TextMeshProUGUI nextTitleText;
        [SerializeField] private TextMeshProUGUI nextSubtitleText;

        // The fade. A CanvasGroup rather than the Button's own disabled tint because that only
        // tints the button's target graphic -- the chevron and both labels are separate graphics
        // and would stay at full strength, so a locked button would read as merely unresponsive.
        [SerializeField] private CanvasGroup prevGroup;
        [SerializeField] private CanvasGroup nextGroup;

        // Faded enough to read as unavailable at a glance, not so faint the level number it names
        // stops being legible -- the labels are the point of the fade, not a casualty of it.
        [SerializeField, Range(0f, 1f)] private float unavailableAlpha = 0.35f;

        [Header("Board-not-covered warning")]
        // The card raised when every pair is joined but empty cells remain -- the exact moment a
        // player believes they have finished and the level does not agree. It is the spoken form
        // of what the progress card already counts (see UpdateFilledCells): the counter is there
        // to pre-empt that confusion, this is for when it happens anyway.
        //
        // Raised by GamePlayController, which is what knows the board's state. This page owns the
        // widget and its timing, not the rule.
        [SerializeField] private GameObject warningCard;

        // Long enough to read one line, short enough to be gone before the player has finished
        // scanning the board for the gap it is telling them about.
        [SerializeField] private float warningVisibleSeconds = 2f;

        // Held so the card can be taken away early -- the level being finished while it is still
        // up -- and so leaving the page does not leave a countdown's worth of state behind. Null
        // whenever none is running.
        private Coroutine warningRoutine;

        // Every load re-opens this page through PageManager, which cycles it (Close then Open)
        // even when Gameplay was already current -- so OnEnable really does run on every
        // prev/next/retry, not only on the first entry into gameplay.
        private void OnEnable()
        {
            Refresh();
        }

        public void Refresh()
        {
            UIController ui = UIController.Instance;
            if (ui == null) { return; }

            if (topPanel != null)
            {
                // The info button belongs to Advanced and to every board in it -- every Advanced
                // level carries at least one mechanic, while Classic carries none at all (no
                // blocked cells, no walls, nothing for a card to explain). Mode alone decides it,
                // which also covers an Advanced board reached through the DAILY CHALLENGE: that
                // route sets the mode from the pick before the board is built, so by the time this
                // runs CurrentMode already says Advanced.
                //
                // Always tappable, not gated on whether this particular mechanic has a card
                // authored yet: a button that greys out on some Advanced levels and not others
                // reads as broken, and the gap is a content one that closes as the remaining cards
                // are written.
                bool advanced = ui.CurrentMode == GameMode.Advanced;
                topPanel.SetTopPanel(ui.LevelHeaderTitle, ui.LevelHeaderSubtitle, showInfo: advanced);
                topPanel.SetInfoInteractable(true);
            }

            SetStepButton(prevButton, prevGroup, prevTitleText, prevSubtitleText,
                ui.HasPrevLevel, ui.PrevStepTitle, ui.PrevStepSubtitle);
            SetStepButton(nextButton, nextGroup, nextTitleText, nextSubtitleText,
                ui.HasNextLevel, ui.NextStepTitle, ui.NextStepSubtitle);

            RefreshHintButton();

            // Snapped, not animated: a fresh board has no prior count on screen to travel from,
            // and animating up from whatever the LAST board left on screen would read as counting
            // down first if this one started further along.
            UpdateFilledCells(animate: false);

            // A fresh board has nothing to warn about yet, and this is also what puts the card
            // into a known state: it is an ordinary GameObject that can be left active in the
            // prefab, and ShowBoardNotCoveredWarning reads its active flag as "already showing".
            HideBoardNotCoveredWarning();
        }

        /// <summary>
        /// Raises the "every pair joined, board not covered" card for
        /// <see cref="warningVisibleSeconds"/>, or does nothing at all if it is already up.
        ///
        /// Ignoring the call rather than restarting the countdown is the point: this is reached
        /// from a board STATE, not from a tap, so it can be reached repeatedly while the same card
        /// is still on screen. Restarting would let a player who keeps tinkering hold the card up
        /// indefinitely; re-activating would flicker it.
        ///
        /// GamePlayController only calls this on the transition into that state, so this is the
        /// second of two guards -- this one is what makes "one showing at a time" true of the card
        /// itself, whoever calls it and however often.
        /// </summary>
        public void ShowBoardNotCoveredWarning()
        {
            if (warningCard == null || warningCard.activeSelf) { return; }

            warningCard.SetActive(true);

            if (warningRoutine != null) { StopCoroutine(warningRoutine); }
            warningRoutine = StartCoroutine(HideWarningAfterDelay());
        }

        /// <summary>Takes the card away now, whatever is left of its countdown -- the level being
        /// finished, or this page being left. Safe to call when it is not showing.</summary>
        public void HideBoardNotCoveredWarning()
        {
            if (warningRoutine != null)
            {
                StopCoroutine(warningRoutine);
                warningRoutine = null;
            }

            if (warningCard != null) { warningCard.SetActive(false); }
        }

        /// <summary>Unscaled, for the same reason WarningNotifier's countdown is: a board that has
        /// stopped would hold a scaled timer still, leaving the card on screen underneath whatever
        /// stopped it.</summary>
        private IEnumerator HideWarningAfterDelay()
        {
            yield return new WaitForSecondsRealtime(warningVisibleSeconds);

            warningRoutine = null;
            if (warningCard != null) { warningCard.SetActive(false); }
        }

        /// <summary>Leaving gameplay takes the card with it. Unity kills the coroutine when the
        /// object goes inactive, so without this the stale handle would still read as a running
        /// countdown and the card would come back up with the page.</summary>
        private void OnDisable()
        {
            HideBoardNotCoveredWarning();
        }

        /// <summary>
        /// Shows how much of the board is filled, on the progress card.
        ///
        /// Deliberately cells rather than pairs. Completing a level needs every usable cell
        /// covered, not just every pair joined, so a pair counter reads "4/4" -- the game
        /// announcing the level is done -- while the level refuses to end. Players hit exactly
        /// that and reported it as the game being broken. Cells are the real win condition, so
        /// showing them means the readout can never claim completion the game will not honour.
        ///
        /// Animated by default: a path completing (or a hint stepping through one) counts the
        /// cells/percent readout up and eases the slider across the gain rather than snapping,
        /// so the three widgets read as one number changing rather than a jump-cut. Pass false
        /// only for a fresh board (see Refresh), which has no prior on-screen count to travel from.
        /// </summary>
        public void UpdateFilledCells(bool animate = true)
        {
            GamePlayController controller = GamePlayController.Instance;
            if (controller == null) { return; }

            int filled = controller.FilledCellCount;
            int usable = controller.UsableCellCount;

            progressTween?.Kill();

            if (!animate)
            {
                displayedFilledCells = filled;
                SetProgressDisplay(filled, usable);
                return;
            }

            progressTween = DOTween.To(() => displayedFilledCells, x => displayedFilledCells = x, filled, progressAnimSeconds)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .OnUpdate(() => SetProgressDisplay(displayedFilledCells, usable))
                .OnComplete(() => SetProgressDisplay(filled, usable));
        }

        /// <summary>Draws one filled-cell count across all three widgets. Takes the raw
        /// (possibly fractional, mid-animation) count rather than an int: the slider fills
        /// continuously off it, while the cells/percent text round it themselves, so the numbers
        /// stay whole and legible while the bar underneath them still moves smoothly.</summary>
        private void SetProgressDisplay(float filled, int usable)
        {
            int roundedFilled = Mathf.RoundToInt(filled);

            if (cellsText != null) { cellsText.text = roundedFilled + "/" + usable + " CELLS"; }
            if (percentText != null)
            {
                int percent = usable > 0 ? Mathf.RoundToInt(100f * roundedFilled / usable) : 0;
                percentText.text = percent + "%";
            }
            if (progressSlider != null)
            {
                progressSlider.value = usable > 0 ? filled / usable : 0f;
            }
        }

        /// <summary>
        /// Puts the hint button in the state the save says it should be in: the count on its pill,
        /// whether it is shown at all, and whether it can be tapped. Called on every level load
        /// (through <see cref="Refresh"/>) and after every hint spent, so the pill and the button
        /// can never disagree with the balance behind them.
        ///
        /// The balance itself belongs to UIController (one for the whole game, held in the save);
        /// this only draws it.
        /// </summary>
        public void RefreshHintButton()
        {
            SaveData data = SavingSystem.Instance.Load();

            if (hintCountText != null) { hintCountText.text = "×" + data.hintsRemaining; }

            bool noHintsLeft = data.hintsRemaining <= 0;
            if (countPill != null) { countPill.SetActive(!noHintsLeft); }
            if (watchAdPill != null) { watchAdPill.SetActive(noHintsLeft); }
            if (watchAdHint != null) { watchAdHint.SetActive(noHintsLeft); }

            if (hintButton == null) { return; }

            // Show/hide is the player's own Settings-screen preference, kept in Settings.json
            // rather than the save (see SettingsData) since it is a device preference, not
            // progress. Interactable is only about whether the board can be hinted at all
            // (GamePlayController.HintAvailable -- a level with no stored answer has nothing to
            // show); an empty balance deliberately leaves the button live, because a tap on it is
            // what raises the "No More Hints" notice. A dead button would answer the same tap with
            // nothing at all.
            hintButton.gameObject.SetActive(SettingsSystem.Instance.Load().showHintButton);
            hintButton.interactable = GamePlayController.Instance != null
                && GamePlayController.Instance.HintAvailable;
        }

        /// <summary>One stepping button: what it leads to, and whether it can be taken. A step
        /// that names a level but cannot be taken (a next level still locked behind the unlock
        /// frontier) keeps its labels and fades -- the player is told which level is waiting and
        /// that it is not open yet, rather than being shown a button that silently does
        /// nothing.</summary>
        private void SetStepButton(Button button, CanvasGroup group, TextMeshProUGUI title,
            TextMeshProUGUI subtitle, bool available, string doesWhat, string leadsTo)
        {
            if (title != null) { title.text = doesWhat; }
            if (subtitle != null) { subtitle.text = leadsTo; }
            if (button != null) { button.interactable = available; }
            if (group != null) { group.alpha = available ? 1f : unavailableAlpha; }
        }

        /// <summary>
        /// Joins one pair along the level's own answer, at the cost of one hint from the player's
        /// balance and the move it adds to this attempt.
        ///
        /// With the balance empty the tap is answered with the "No More Hints" notice instead of
        /// being swallowed: the button stays interactable in that state on purpose (see
        /// <see cref="RefreshHintButton"/>), because a player who has just watched the pill count
        /// down to zero will tap it again, and silence is the one response that explains nothing.
        ///
        /// A hint that finds nothing to do IS silent by design: the board is either already
        /// correct or has no stored answer, and in the second case the button is not interactable
        /// anyway.
        /// </summary>
        public void OnHintButtonClick()
        {
            // One CanInput() for the whole tap -- it is a one-shot debounce, so checking it again
            // further down this call stack would always fail and lose the tap.
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);

                if (UIController.Instance.HintsRemaining <= 0)
                {
                    // CanInput() just disabled the EventSystem for its own 0.25s debounce, timed
                    // for a normal in-game tap -- not for a rewarded ad, which can sit in front of
                    // Unity for as long as the player watches it. Re-enabling here rather than
                    // waiting on that timer means input is never left off for the ad's whole
                    // lifetime, which is what made the game seem stuck once the ad closed.
                    InputManager.Instance.EnableInput();

                    // An empty balance now offers a rewarded ad instead of just refusing the tap --
                    // ADManager.ShowRewardedAd calls back on whichever of the two actually
                    // happened, never both.
                    ADManager.Instance.ShowRewardedAd(OnHintAdCompleted, OnHintAdFailed);
                    return;
                }


                GamePlayController.Instance.TryApplyHint();
            }
        }

        /// <summary>The reward for watching the hint ad to the end: one hint credited and spent in
        /// the same beat, so the pair it draws is the payoff the player watched the ad for rather
        /// than a balance they have to tap Hint again to cash in. TryApplyHint spends back exactly
        /// what GrantHint just added, so a watched ad nets to "one pair hinted", not "one hint
        /// banked" -- the same balance a manual tap would have left, minus the pair it drew.</summary>
        private void OnHintAdCompleted()
        {
            UIController.Instance.GrantHint(1);
            GamePlayController.Instance.TryApplyHint();
            RefreshHintButton();
        }

        /// <summary>The ad didn't pay out -- none was ready, the SDK couldn't show it, or the
        /// player closed it early. Same notice style as the empty-balance refusal it replaced.</summary>
        private void OnHintAdFailed()
        {
            Haptics.Play(HapticType.Warning);
            UIController.Instance.ShowWarning(UIController.Instance.AdNotAvailableMessage);
        }

        /// <summary>Steps to the previous/next level without leaving gameplay -- the footer's two
        /// arrow buttons. What "previous" and "next" mean is UIController's call, not this page's:
        /// within a pack they are level numbers (Next respecting the same unlock frontier the
        /// level grid enforces), within a daily challenge they are the other challenges of the
        /// same day.</summary>
        public void OnPrevLevelClick()
        {
            if (InputManager.Instance.CanInput() && UIController.Instance.HasPrevLevel)
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
                UIController.Instance.GoToPrevLevel();
            }
        }

        public void OnNextLevelClick()
        {
            if (InputManager.Instance.CanInput() && UIController.Instance.HasNextLevel)
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
                UIController.Instance.GoToNextLevel();
            }
        }
    }
}
