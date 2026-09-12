using FreeFlow.Enums;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Picks a calendar day's daily-challenge levels: deterministic per day, drawn from the
    /// already-shipped packs rather than generating anything new on the fly.
    ///
    /// GAME_EXPANSION_PLAN's Phase 10 note asks for three things -- date+version seeding,
    /// skill-based pool selection, and on-device bounded generation-or-cache. The third is
    /// deliberately NOT real generation: every other phase in this project established that
    /// generation is an offline Editor pipeline (4.3, 5.1) expensive enough to need its own
    /// tuning passes per configuration, not something a phone can do in a frame. "Generation" here
    /// means picking an index into a pack that is already on disk, which is arithmetic, not search
    /// -- the "cache" half (see SaveData.dailyChallengeCachedDay) is what actually matters: once
    /// picked for a day, the same levels keep showing for the rest of that day even if the
    /// player's skill changes mid-session from playing other levels.
    ///
    /// Pool selection: each pack already ships "ramped from the easiest board that size can
    /// produce to the hardest" (GAME_EXPANSION_PLAN 7), so a level NUMBER within a pack is
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

        // How far apart two slots of the SAME day sit in the hash's day axis. Any value would do
        // as long as it is not 0; what is load-bearing is only that slot 0 uses an offset of ZERO,
        // so SelectDay(.., count: 1) and Select(..) produce the identical level and a save written
        // back when a day held exactly one challenge stays valid.
        private const int SlotStride = 7919;

        // An arbitrary fixed reference in the past -- never move this once shipped, or every
        // existing player's day index (and therefore their cached pick and streak) shifts under
        // them. It does not need to mean anything; it only needs to never change.
        private static readonly System.DateTime Epoch = new System.DateTime(2020, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);

#if DEBUG
        /// <summary>
        /// Developer-only override for how long a "day" lasts, in seconds. 0 means off -- real
        /// calendar days. Set from the Settings screen's DEVELOPER section, which only exists in
        /// DEBUG builds, so that waiting 24 hours is not the only way to see a daily reset.
        ///
        /// Kept in PlayerPrefs rather than SaveData for two reasons: a debug setting must never be
        /// able to ride along inside a shipped save file, and wiping progress (which deletes that
        /// file) should not silently switch the override off in the middle of a test.
        ///
        /// The whole daily system keys off <see cref="DayIndex"/>, so overriding it here is enough
        /// -- selection, caching, streaks and the hub's roll-over check all follow automatically.
        /// </summary>
        public const string DebugDayLengthPrefKey = "debug.dailyChallenge.dayLengthSeconds";

        private static int debugDayLengthSeconds = -1;   // -1 = not yet read from PlayerPrefs

        public static int DebugDayLengthSeconds
        {
            get
            {
                if (debugDayLengthSeconds < 0)
                {
                    debugDayLengthSeconds = UnityEngine.PlayerPrefs.GetInt(DebugDayLengthPrefKey, 0);
                }
                return debugDayLengthSeconds;
            }
            set
            {
                debugDayLengthSeconds = System.Math.Max(0, value);
                UnityEngine.PlayerPrefs.SetInt(DebugDayLengthPrefKey, debugDayLengthSeconds);
                UnityEngine.PlayerPrefs.Save();
            }
        }
#endif

        /// <summary>Whole calendar days (UTC) since <see cref="Epoch"/>. The stable identity of
        /// "today" everything else here keys off -- SaveData stores this directly rather than a
        /// date string, so streak comparisons are integer arithmetic, not calendar-aware parsing.
        ///
        /// In DEBUG builds a developer can compress a "day" to a handful of seconds (see
        /// <see cref="DebugDayLengthSeconds"/>); the index then counts those periods instead. It
        /// is still a whole number that advances by one per period, which is all any caller
        /// assumes about it.</summary>
        public static int DayIndex(System.DateTime utcNow)
        {
#if DEBUG
            int compressed = DebugDayLengthSeconds;
            if (compressed > 0)
            {
                return (int)((utcNow - Epoch).TotalSeconds / compressed);
            }
#endif
            return (int)(utcNow.Date - Epoch).TotalDays;
        }

        /// <summary>How long until the next daily reset -- until UTC midnight normally, or until
        /// the next compressed period in a DEBUG build with the override on. Lives here rather
        /// than on the screen that displays it so the countdown can never disagree with the
        /// <see cref="DayIndex"/> it is counting down to.</summary>
        public static System.TimeSpan TimeUntilNextDay(System.DateTime utcNow)
        {
#if DEBUG
            int compressed = DebugDayLengthSeconds;
            if (compressed > 0)
            {
                double elapsed = (utcNow - Epoch).TotalSeconds % compressed;
                return System.TimeSpan.FromSeconds(compressed - elapsed);
            }
#endif
            return utcNow.Date.AddDays(1) - utcNow;
        }

        public struct Pick
        {
            public GameMode mode;
            public int packSize;
            public int levelNumber;
        }

        /// <summary>
        /// One day's pick. <paramref name="packSizesForMode"/> is whichever pack sizes exist for
        /// <paramref name="mode"/> (rotated through by day, so a week of play sees every size --
        /// this rotation is deliberately NOT salted, so that guarantee holds for every install, not
        /// just on average); <paramref name="skillRating"/> is <c>SaveData.OverallSkillRating()</c>,
        /// 0-100; <paramref name="playerSalt"/> is <c>SaveData.playerSalt</c> -- a value generated
        /// once per install (see UIController.EnsureTodayDailyPicks) so two players in the same
        /// skill band on the same day get DIFFERENT levels, not the identical puzzle. Only the
        /// LEVEL choice is salted, not the pack-size rotation above -- salting that too would trade
        /// the "every size in a week" guarantee for cross-player variety nobody asked for.
        ///
        /// Equivalent to <see cref="SelectDay"/> with a count of 1, and kept as its own entry point
        /// because "the day's level" is still the shape most callers want.
        /// </summary>
        public static Pick Select(int dayIndex, GameMode mode, int[] packSizesForMode, int packLevelCount, float skillRating, int playerSalt)
        {
            return SelectSlot(dayIndex, 0, mode, packSizesForMode, packLevelCount, skillRating, playerSalt);
        }

        /// <summary>
        /// A whole day's worth of daily challenges -- <paramref name="count"/> DISTINCT levels,
        /// returned in ascending board-size order so the hub and the in-game prev/next both walk
        /// them easy-board-first rather than in whatever order the hash happened to emit.
        ///
        /// Each slot rotates one step further through <paramref name="packSizesForMode"/>, so a
        /// day with as many slots as the mode has pack sizes covers every size exactly once --
        /// the "5 levels of increasing board size" the Daily Challenge hub's own reference art
        /// shows. Slot 0 is exactly what <see cref="Select"/> returns for the same day, so raising
        /// the count never moves the level a one-per-day save already committed to.
        ///
        /// <paramref name="count"/> is clamped to what the skill band can actually supply distinct
        /// levels for; asking for more would otherwise repeat a board inside one day.
        /// </summary>
        public static Pick[] SelectDay(int dayIndex, GameMode mode, int[] packSizesForMode, int packLevelCount, float skillRating, int playerSalt, int count)
        {
            if (packSizesForMode == null || packSizesForMode.Length == 0) { return new Pick[0]; }

            BandRange(skillRating, packLevelCount, out int bandStart, out int bandEnd);
            int bandWidth = bandEnd - bandStart + 1;

            if (count < 1) { count = 1; }
            int distinctAvailable = bandWidth * packSizesForMode.Length;
            if (count > distinctAvailable) { count = distinctAvailable; }

            Pick[] picks = new Pick[count];
            for (int i = 0; i < count; i++)
            {
                Pick pick = SelectSlot(dayIndex, i, mode, packSizesForMode, packLevelCount, skillRating, playerSalt);

                // Two slots landing on the same (pack size, level) would show the SAME board twice
                // in one day. Step forward inside the band -- wrapping at its end -- until the pair
                // is unused. Bounded by bandWidth, and the clamp above guarantees a free level
                // exists at this pack size only when the band is not already exhausted, so the
                // guard is what stops a pathological config (count > distinct levels) spinning.
                for (int guard = 0; guard < bandWidth && AlreadyPicked(picks, i, pick); guard++)
                {
                    pick.levelNumber = bandStart + Mod(pick.levelNumber - bandStart + 1, bandWidth);
                }

                picks[i] = pick;
            }

            SortByPackSizeThenLevel(picks);
            return picks;
        }

        private static Pick SelectSlot(int dayIndex, int slot, GameMode mode, int[] packSizesForMode, int packLevelCount, float skillRating, int playerSalt)
        {
            int packIndex = Mod(dayIndex + slot, packSizesForMode.Length);
            int packSize = packSizesForMode[packIndex];

            BandRange(skillRating, packLevelCount, out int bandStart, out int bandEnd);

            int hash = Hash(dayIndex + slot * SlotStride, SeedVersion, packSize, playerSalt);
            int levelNumber = bandStart + Mod(hash, bandEnd - bandStart + 1);

            return new Pick { mode = mode, packSize = packSize, levelNumber = levelNumber };
        }

        /// <summary>The inclusive level range this skill rating plays in. Lower/middle/upper third
        /// of the pack -- boundaries are inclusive-exclusive on 30/70 so every skill value lands in
        /// exactly one band, never zero or two.</summary>
        private static void BandRange(float skillRating, int packLevelCount, out int bandStart, out int bandEnd)
        {
            int band = skillRating < 30f ? 0 : (skillRating < 70f ? 1 : 2);
            int bandWidth = System.Math.Max(1, packLevelCount / 3);
            bandStart = band * bandWidth + 1;
            // The top band absorbs whatever packLevelCount/3 truncated away, so the pack's last
            // level is always reachable regardless of how evenly 3 divides it.
            bandEnd = band == 2 ? packLevelCount : bandStart + bandWidth - 1;
        }

        private static bool AlreadyPicked(Pick[] picks, int filled, Pick candidate)
        {
            for (int i = 0; i < filled; i++)
            {
                if (picks[i].packSize == candidate.packSize && picks[i].levelNumber == candidate.levelNumber)
                {
                    return true;
                }
            }
            return false;
        }

        // Insertion sort rather than System.Array.Sort with a comparer: the array is never more
        // than a handful of entries, and this keeps the ordering rule readable in one place
        // instead of behind a delegate.
        private static void SortByPackSizeThenLevel(Pick[] picks)
        {
            for (int i = 1; i < picks.Length; i++)
            {
                Pick key = picks[i];
                int j = i - 1;
                while (j >= 0 && (picks[j].packSize > key.packSize
                    || (picks[j].packSize == key.packSize && picks[j].levelNumber > key.levelNumber)))
                {
                    picks[j + 1] = picks[j];
                    j--;
                }
                picks[j + 1] = key;
            }
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
