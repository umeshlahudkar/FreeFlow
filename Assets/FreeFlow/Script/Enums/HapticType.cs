namespace FreeFlow.Enums
{
    /// <summary>The kinds of tap the game can ask the phone for, named after the MOMENT rather
    /// than a duration -- callers say what just happened and Haptics decides what that feels like
    /// on the platform it is running on.
    ///
    /// Deliberately few. Haptics stop being information and start being noise the moment everything
    /// buzzes, so these are the events a player actually wants confirmed through their fingers: a
    /// route starting, a pair landing, a level finishing, and a tap the game had to refuse.</summary>
    public enum HapticType
    {
        /// <summary>The lightest tick there is -- picking a dot up. This fires most often, so it
        /// has to be the one a player stops noticing.</summary>
        Selection = 0,

        /// <summary>Something landed: a pair joined.</summary>
        Light,

        /// <summary>A bigger landing, for a moment worth more than a pair.</summary>
        Medium,

        /// <summary>The board finished. The one celebratory pattern.</summary>
        Success,

        /// <summary>The game refused something -- an illegal step, or a hint with nothing left to
        /// spend. Distinct from the others on purpose: a refusal should not feel like a
        /// confirmation.</summary>
        Warning,
    }
}
