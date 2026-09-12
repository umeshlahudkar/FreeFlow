using UnityEngine;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using FreeFlow.Input;

namespace FreeFlow.UI
{
    /// <summary>The gameplay screen. Its HUD content (progress slider, hint button) is still
    /// driven directly by UIController; only the shared TopPanel's title/subtitle ("LEVEL X" /
    /// mode+size) are refreshed here, from UIController's own public level-state properties, same
    /// as every other page's header.</summary>
    public class GameplayPage : Page
    {
        [SerializeField] private TopPanel topPanel;

        private void OnEnable()
        {
            Refresh();
        }

        public void Refresh()
        {
            UIController ui = UIController.Instance;
            if (ui == null || topPanel == null) { return; }

            string subtitle = ui.CurrentPackSize > 0
                ? ui.CurrentMode.ToString().ToUpperInvariant() + " " + ui.CurrentPackSize + " × " + ui.CurrentPackSize
                : ui.CurrentMode.ToString().ToUpperInvariant();

            topPanel.SetTopPanel("LEVEL " + ui.CurrentLevel, subtitle);
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

        /// <summary>Steps to the previous/next level without leaving gameplay. Next respects the
        /// same unlock frontier as the level grid -- it cannot jump past a level the player has
        /// not reached yet, same as a locked LevelButton refusing a tap.</summary>
        public void OnPrevLevelClick()
        {
            if (InputManager.Instance.CanInput() && UIController.Instance.CurrentLevel > 1)
            {
                AudioManager.Instance.PlayButtonClickSound();
                UIController.Instance.LoadLevel(UIController.Instance.CurrentLevel - 1);
            }
        }

        public void OnNextLevelClick()
        {
            if (InputManager.Instance.CanInput())
            {
                UIController ui = UIController.Instance;
                int unlockedUpTo = SavingSystem.Instance.Load().CompletedLevelForKey(ui.ProgressKey) + 1;
                if (ui.CurrentLevel < ui.TotalLevelCount && ui.CurrentLevel < unlockedUpTo)
                {
                    AudioManager.Instance.PlayButtonClickSound();
                    ui.LoadLevel(ui.CurrentLevel + 1);
                }
            }
        }
    }
}
