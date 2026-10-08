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
        Warning,

        // Developer tools screen, reached from the Settings screen's own DEVELOPER row (which is
        // itself hidden in a FINAL_BUILD). An overlay like Setting, opened on top of it, so
        // closing it returns to Settings rather than popping whatever page is underneath. Added at
        // the END, per the warning above.
        Developer,

        // The card that teaches one board mechanic the first time it is met -- see
        // MechanicIntroPage. An overlay, opened on top of Gameplay as the board loads, and unlike
        // Warning it blocks input until dismissed. Added at the END, per the warning above.
        MechanicIntro,

        // The list of every mechanic on the board being played, opened by the header's info
        // button -- see MechanicGuidePage. An overlay like MechanicIntro, and like it added at the
        // END, per the warning above.
        MechanicGuide
    }
}
