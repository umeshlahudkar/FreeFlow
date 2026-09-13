namespace FreeFlow.Share
{
    /// <summary>One finished level, as the share card and the share text both need it. A plain
    /// snapshot taken at the moment the level was completed rather than something read back from
    /// the save: the attempt's moves, time and hints are never persisted anywhere (see
    /// GamePlayController.lastCompletion*), so by the time the player taps Share there is nowhere
    /// else left to get them from.</summary>
    public struct ShareResultData
    {
        /// <summary>"LEVEL 24", or "DAILY CHALLENGE" -- the run's own headline as the UI writes it,
        /// for the CARD, where it is a heading and the caps belong.</summary>
        public string Headline;

        /// <summary>"CLASSIC 6 x 6", or "2 OF 5  ·  6 x 6" for a daily challenge. Card only, same
        /// reason as <see cref="Headline"/>.</summary>
        public string Subheadline;

        /// <summary>The same run written as prose -- "Level 24", "today's Daily Challenge" -- for
        /// the shared TEXT, which is a sentence somebody reads rather than a label they glance at.
        /// See UIController.LevelPhrase.</summary>
        public string Phrase;

        public int Moves;
        public int Hints;
        public float Seconds;
    }
}
