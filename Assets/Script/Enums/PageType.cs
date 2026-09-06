namespace FreeFlow.Enums
{
    /// <summary>Every navigable screen and overlay PageManager knows how to open. MainMenu,
    /// PackSelect, Levels, DailyChallenge and Gameplay are full pages on the back-stack; Pause,
    /// Setting and LevelComplete are overlays opened on top of whatever page is current and
    /// closed by their own explicit action, never by the back-stack.</summary>
    public enum PageType
    {
        MainMenu = 0,
        PackSelect,
        Levels,
        DailyChallenge,
        Gameplay,
        Pause,
        Setting,
        LevelComplete
    }
}
