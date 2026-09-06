namespace FreeFlow.UI
{
    /// <summary>The level-complete (game over) overlay. Opened with PageManager.OpenAsOverlay,
    /// closed with CloseOverlay -- never part of the back-stack. Its child "Sheet" holds the
    /// actual stat-card rendering (<see cref="LevelCompleteView"/>); this component only makes
    /// the container a page PageManager can Open/Close.</summary>
    public class LevelCompletePage : Page
    {
    }
}
