using FreeFlow.Enums;
using FreeFlow.GamePlay;
using FreeFlow.Input;

namespace FreeFlow.UI
{
    /// <summary>The pause overlay. Opened with PageManager.OpenAsOverlay, closed with
    /// CloseOverlay -- never part of the back-stack.</summary>
    public class PausePage : Page
    {
        public void OnResumeButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                PageManager.Instance.CloseOverlay(PageType.Pause);
                GamePlayController.Instance.GameState = Enums.GameState.Playing;
            }
        }

        public void OnRetryButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                UIController.Instance.RetryCurrentLevel();
            }
        }

        public void OnHomeButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                UIController.Instance.GoToMainMenu();
            }
        }
    }
}
