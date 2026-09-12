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
    /// the prev/next arrows, both of which it reads straight off UIController rather than
    /// composing itself -- so the header and the arrows say the same thing here as on the
    /// level-complete overlay, whether the player came in from a pack or from the daily
    /// challenge.</summary>
    public class GameplayPage : Page
    {
        [SerializeField] private TopPanel topPanel;

        [Header("Level navigation")]
        // Faded and inert rather than hidden when a step is unavailable (level 1, a next level
        // still behind the unlock frontier, the first/last of today's challenges) so the HUD does
        // not reshuffle itself as the player moves along it.
        [SerializeField] private Button prevButton;
        [SerializeField] private Button nextButton;

        // Which level each arrow leads to ("LEVEL 74"). Short form: there is no room beside an
        // 88px arrow for the board size, and the header directly above already carries it.
        [SerializeField] private TextMeshProUGUI prevCaptionText;
        [SerializeField] private TextMeshProUGUI nextCaptionText;

        // The fade. A CanvasGroup rather than the Button's own disabled tint because that only
        // tints the button's target graphic -- the chevron and the caption are separate graphics
        // and would stay at full strength, so a locked arrow would read as merely unresponsive.
        [SerializeField] private CanvasGroup prevGroup;
        [SerializeField] private CanvasGroup nextGroup;

        // Faded enough to read as unavailable at a glance, not so faint the level number it names
        // stops being legible -- the caption is the point of the fade, not a casualty of it.
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

            SetArrow(prevButton, prevGroup, prevCaptionText, ui.HasPrevLevel, ui.PrevLevelCaption);
            SetArrow(nextButton, nextGroup, nextCaptionText, ui.HasNextLevel, ui.NextLevelCaption);
        }

        /// <summary>One arrow: what it leads to, and whether it can be taken. A step that names a
        /// level but cannot be taken (a next level still locked behind the unlock frontier) keeps
        /// the caption and fades -- the player is told which level is waiting and that it is not
        /// open yet, rather than being shown a button that silently does nothing.</summary>
        private void SetArrow(Button button, CanvasGroup group, TextMeshProUGUI caption,
            bool available, string leadsTo)
        {
            if (caption != null) { caption.text = leadsTo; }
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

        /// <summary>Steps to the previous/next level without leaving gameplay. What "previous" and
        /// "next" mean is UIController's call, not this page's: within a pack they are level
        /// numbers (Next respecting the same unlock frontier the level grid enforces), within a
        /// daily challenge they are the other challenges of the same day.</summary>
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
