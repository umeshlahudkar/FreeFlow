using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using FreeFlow.Input;

namespace FreeFlow.UI
{
    /// <summary>The gameplay screen. Its HUD content (progress slider, hint button) is still
    /// driven directly by UIController; this page owns the shared TopPanel's title/subtitle and
    /// the footer's prev/next buttons, both of which it reads straight off UIController rather
    /// than composing itself -- so the header and the buttons say the same thing here as on the
    /// level-complete overlay, whether the player came in from a pack or from the daily
    /// challenge.</summary>
    public class GameplayPage : Page
    {
        [SerializeField] private TopPanel topPanel;

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
                topPanel.SetTopPanel(ui.LevelHeaderTitle, ui.LevelHeaderSubtitle);
            }

            SetStepButton(prevButton, prevGroup, prevTitleText, prevSubtitleText,
                ui.HasPrevLevel, ui.PrevStepTitle, ui.PrevStepSubtitle);
            SetStepButton(nextButton, nextGroup, nextTitleText, nextSubtitleText,
                ui.HasNextLevel, ui.NextStepTitle, ui.NextStepSubtitle);
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

        public void OnPauseButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                GamePlayController.Instance.GameState = Enums.GameState.Paused;
                PageManager.Instance.OpenAsOverlay(PageType.Pause);
            }
        }

        /// <summary>
        /// Joins one pair along the level's own answer. One hint, one pair, no limit on how many
        /// times it can be used -- the only cost is the move it adds, and a player who taps it for
        /// every pair has asked to be shown the board rather than to play it.
        ///
        /// A hint that finds nothing to do is silent by design: the board is either already correct
        /// or has no stored answer, and in the second case the button is not interactable anyway.
        /// </summary>
        public void OnHintButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                GamePlayController.Instance.TryApplyHint();
            }
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
                AudioManager.Instance.PlayButtonClickSound();
                UIController.Instance.GoToPrevLevel();
            }
        }

        public void OnNextLevelClick()
        {
            if (InputManager.Instance.CanInput() && UIController.Instance.HasNextLevel)
            {
                AudioManager.Instance.PlayButtonClickSound();
                UIController.Instance.GoToNextLevel();
            }
        }
    }
}
