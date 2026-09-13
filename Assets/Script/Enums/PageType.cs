namespace FreeFlow.Enums
{
    /// <summary>Every navigable screen and overlay PageManager knows how to open. MainMenu,
    /// PackSelect, Levels, DailyChallenge and Gameplay are full pages on the back-stack; Setting,
    /// LevelComplete and Warning are overlays opened on top of whatever page is current and closed
    /// by their own explicit action, never by the back-stack.</summary>
    // CHANGING THE ORDER OF THIS ENUM CHANGES THE SCENE. Every Page serializes its own
    // pageType as the underlying int, and PageManager.startingPage stores one too -- so inserting
    // or removing a value silently repoints the pages after it at each other, which is exactly
    // what removing Pause did (Setting started claiming to be LevelComplete, and the last page
    // fell off the end as -1). Adding a value at the END is safe; anything else means fixing every
    // Page in the scene by hand afterwards.
    public enum PageType
    {
        MainMenu = 0,
        PackSelect,
        Levels,
        DailyChallenge,
        Gameplay,
        Setting,
        LevelComplete,

        // The toast that explains why something a player just tapped did not happen -- see
        // WarningNotifier.
        Warning
    }
}
