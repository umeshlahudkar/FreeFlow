namespace FreeFlow.Enums
{
    /// <summary>
    /// Where the level currently in play was opened FROM, and therefore which set of levels
    /// prev/next steps through and what the header says.
    ///
    /// This is deliberately not a property of the level asset: the very same
    /// <c>SingleLevelDataSO</c> is both "Classic 6x6 level 20" and, on the day it is drawn,
    /// today's daily challenge (see <c>DailyChallengeSelector</c> -- a daily challenge is a PICK
    /// out of a shipped pack, not a separate asset). So "which run am I in" can only come from the
    /// route the player took to get here, which is what this records.
    ///
    /// Replaces the old <c>UIController.isDailyChallenge</c> bool, which had to be cleared at the
    /// top of every load and re-armed afterwards by each caller that knew better -- a shape that
    /// had already leaked one bug (retry silently demoting a daily challenge to a pack level).
    /// </summary>
    public enum LevelSource
    {
        /// <summary>Chosen from the pack grid: prev/next walk that pack's level numbers.</summary>
        Pack = 0,

        /// <summary>Chosen from the Daily Challenge hub: prev/next walk TODAY'S picks, which may
        /// each sit in a different pack and board size.</summary>
        Daily = 1
    }
}
