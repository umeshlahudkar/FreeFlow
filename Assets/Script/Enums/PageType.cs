namespace FreeFlow.Enums
{
    /// <summary>Every navigable screen and overlay PageManager knows how to open. MainMenu,
    /// PackSelect, Levels, DailyChallenge and Gameplay are full pages on the back-stack; Pause,
    /// Setting, LevelComplete and Warning are overlays opened on top of whatever page is current
    /// and closed by their own explicit action, never by the back-stack.</summary>
    public enum PageType
    {
        MainMenu = 0,
        PackSelect,
        Levels,
        DailyChallenge,
        Gameplay,
        Pause,
        Setting,
        LevelComplete,

        // The toast that explains why something a player just tapped did not happen -- see
        // WarningNotifier. Appended rather than slotted in beside the other overlays: every Page
        // in the scene serializes its own pageType as this enum's underlying int, so renumbering
        // an existing value would silently repoint those pages at each other.
        Warning
    }
}
