namespace FreeFlow.Enums
{
    /// <summary>Every sound the game can ask for, named after the MOMENT rather than the file:
    /// callers say what just happened and AudioManager decides what that sounds like, so swapping a
    /// clip is an Inspector change and never a code one.
    ///
    /// Music and one-shots share this list -- there is one vocabulary of sounds -- but they are not
    /// played the same way: one-shots go through AudioManager.PlaySFX and layer on top of each
    /// other, music goes through PlayMusic and loops on its own source. Each entry says which it is
    /// (see AudioManager.SoundEntry.channel), so asking for a two-minute track as a UI click is
    /// caught rather than played.</summary>
    public enum SoundType
    {
        ButtonClick = 0,

        /// <summary>A path being started -- the tap that picks up a dot.</summary>
        PathStart,

        /// <summary>A pair joined end to end.</summary>
        PathComplete,

        /// <summary>The board finished: every pair joined and every cell covered.</summary>
        LevelComplete,

        BGMusic,
    }
}
