using FreeFlow.Enums;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Picks today's daily-challenge level: deterministic per calendar day, drawn from the
    /// already-shipped packs rather than generating anything new on the fly.
    ///
    /// GAME_EXPANSION_PLAN's Phase 10 note asks for three things -- date+version seeding,
    /// skill-based pool selection, and on-device bounded generation-or-cache. The third is
    /// deliberately NOT real generation: every other phase in this project established that
    /// generation is an offline Editor pipeline (§4.3, §5.1) expensive enough to need its own
    /// tuning passes per configuration, not something a phone can do in a frame. "Generation" here
    /// means picking an index into a pack that is already on disk, which is arithmetic, not search
    /// -- the "cache" half (see SaveData.dailyChallengeCachedDay) is what actually matters: once
    /// picked for a day, the same level keeps showing for the rest of that day even if the
    /// player's skill changes mid-session from playing other levels.
    ///
    /// Pool selection: each pack already ships "ramped from the easiest board that size can
    /// produce to the hardest" (GAME_EXPANSION_PLAN §7), so a level NUMBER within a pack is
    /// already a meaningful difficulty ordinal -- this does not need DifficultyAnalyzer.Score to
    /// be a real cross-pack difficulty target (it explicitly is not yet, see Open Questions) to
    /// pick something reasonable. Skill selects a third of the pack (easy/medium/hard); the day's
    /// hash picks the specific level inside that third, so the exact pick still varies day to day
    /// without leaving the player's comfort band.
    ///
    /// Per-install, not shared: the level hash also folds in a per-install salt (see Select's own
    /// doc comment), so two players in the same skill band on the same day get different levels --
    /// there is no backend to make a single shared puzzle meaningful anyway, and without the salt
    /// every player in a band would otherwise see the identical board.
    /// </summary>
    public static class DailyChallengeSelector
    {
        /// <summary>Bumped only if the selection algorithm itself changes in a way that should
        /// reshuffle everyone's schedule (e.g. a new pack size added mid-band). Folded into the
        /// hash so a version bump changes every future day's pick without touching DayIndex.</summary>
        public const int SeedVersion = 1;

        // An arbitrary fixed reference in the past -- never move this once shipped, or every
        // existing player's day index (and therefore their cached pick and streak) shifts under
        // them. It does not need to mean anything; it only needs to never change.
        private static readonly System.DateTime Epoch = new System.DateTime(2020, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);

        /// <summary>Whole calendar days (UTC) since <see cref="Epoch"/>. The stable identity of
        /// "today" everything else here keys off -- SaveData stores this directly rather than a
        /// date string, so streak comparisons are integer arithmetic, not calendar-aware parsing.</summary>
        public static int DayIndex(System.DateTime utcNow)
        {
            return (int)(utcNow.Date - Epoch).TotalDays;
        }

        public struct Pick
        {
            public GameMode mode;
            public int packSize;
            public int levelNumber;
        }

        /// <summary>
        /// Today's pick. <paramref name="packSizesForMode"/> is whichever pack sizes exist for
        /// <paramref name="mode"/> (rotated through by day, so a week of play sees every size --
        /// this rotation is deliberately NOT salted, so that guarantee holds for every install, not
        /// just on average); <paramref name="skillRating"/> is <c>SaveData.OverallSkillRating()</c>,
        /// 0-100; <paramref name="playerSalt"/> is <c>SaveData.playerSalt</c> -- a value generated
        /// once per install (see UIController.LoadDailyChallenge) so two players in the same skill
        /// band on the same day get DIFFERENT levels, not the identical puzzle. Only the LEVEL
        /// choice is salted, not the pack-size rotation above -- salting that too would trade the
        /// "every size in a week" guarantee for cross-player variety nobody asked for.
        /// </summary>
        public static Pick Select(int dayIndex, GameMode mode, int[] packSizesForMode, int packLevelCount, float skillRating, int playerSalt)
        {
            int packIndex = Mod(dayIndex, packSizesForMode.Length);
            int packSize = packSizesForMode[packIndex];

            // Lower/middle/upper third of the pack -- boundaries are inclusive-exclusive on 30/70
            // so every skill value lands in exactly one band, never zero or two.
            int band = skillRating < 30f ? 0 : (skillRating < 70f ? 1 : 2);
            int bandWidth = System.Math.Max(1, packLevelCount / 3);
            int bandStart = band * bandWidth + 1;
            // The top band absorbs whatever packLevelCount/3 truncated away, so the pack's last
            // level is always reachable regardless of how evenly 3 divides it.
            int bandEnd = band == 2 ? packLevelCount : bandStart + bandWidth - 1;

            int hash = Hash(dayIndex, SeedVersion, packSize, playerSalt);
            int levelNumber = bandStart + Mod(hash, bandEnd - bandStart + 1);

            return new Pick { mode = mode, packSize = packSize, levelNumber = levelNumber };
        }

        // A small, self-contained integer mix (Murmur3-style finalizer) rather than
        // System.Random(seed) -- .NET does not guarantee System.Random's algorithm stays the same
        // across runtime versions, and this only ever needs to be deterministic for the same
        // (day, version, pack size, player salt) on THIS device, not bit-identical to any other
        // implementation or any other player's.
        private static int Hash(int a, int b, int c, int d)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + a;
                h = h * 31 + b;
                h = h * 31 + c;
                h = h * 31 + d;
                h ^= (int)((uint)h >> 15);
                h *= (int)0x85ebca6b;
                h ^= (int)((uint)h >> 13);
                return h & int.MaxValue; // non-negative, so Mod below never needs to correct twice
            }
        }

        private static int Mod(int value, int modulus)
        {
            int m = value % modulus;
            return m < 0 ? m + modulus : m;
        }
    }
}
