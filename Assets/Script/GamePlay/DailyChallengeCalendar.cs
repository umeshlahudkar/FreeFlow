using System;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Maps a calendar date to one of the 366 dedicated daily-challenge levels in
    /// Assets/Resources/Levels/Daily/{6x6,7x7,8x8,9x9} -- a fixed, curated calendar, not a pick
    /// drawn from the Classic packs. Every player sees the SAME board on the SAME date; there is
    /// no per-install salt and no skill banding here at all (contrast
    /// <see cref="DailyChallengeSelector"/>, the older pack-drawing design -- left in place,
    /// unused by this flow rather than removed, since its DayIndex/Epoch/TimeUntilNextDay are
    /// still reused below and its own tests still cover its own logic).
    ///
    /// <b>The rule:</b> January 1st is always day-of-year 1, always the SAME regular level, every
    /// year. Every other calendar date maps the same way whether or not that year is a leap year
    /// -- EXCEPT February 29th itself, which always shows the one dedicated 9x9 "leap day" level
    /// (Daily/9x9/Level_56) and exists only once every four years. See
    /// <see cref="LevelForDate"/> for the arithmetic that makes a leap year's Feb 29 an EXTRA day
    /// rather than one that shifts every later date's board.
    /// </summary>
    public static class DailyChallengeCalendar
    {
        public struct DailyLevelPick
        {
            public int packSize;
            public int levelNumber;
            public bool isLeapDayExtra;
        }

        // Day-of-year (1-based, non-leap numbering) on which February 29th would fall if it
        // existed -- the slot reserved for the leap-day extra.
        private const int LeapDaySlot = 60;

        // The board sizes and level counts making up the regular (non-leap-day) 365-level
        // calendar, in the fixed order they are interleaved below. MUST match what is actually
        // shipped in Assets/Resources/Levels/Daily -- see the freeflow-daily-pool-365 memory /
        // LevelGenerator.DailyPool.cs's own DailyPacks table for where these numbers came from.
        private static readonly (int packSize, int count)[] RegularPools =
        {
            (6, 60),
            (7, 125),
            (8, 125),
            (9, 55),
        };

        private const int LeapExtraPackSize = 9;
        private const int LeapExtraLevelNumber = 56;

        private static readonly DailyLevelPick[] regularOrder = BuildRegularOrder();

        /// <summary>
        /// Proportionally interleaves each pool's levels across the 365 regular slots using exact
        /// integer arithmetic (cross-multiplied fraction comparison, never floating point), so
        /// the order is bit-for-bit reproducible on every platform without needing a seeded
        /// shuffle -- the same reasoning as DailyChallengeSelector's own hand-rolled Hash: nothing
        /// here depends on any RNG's algorithm staying fixed across runtimes.
        ///
        /// Each pool's OWN levels are consumed in order (Level_1 first, hardest last), so within
        /// one board size the calendar year is also a soft difficulty ramp.
        /// </summary>
        private static DailyLevelPick[] BuildRegularOrder()
        {
            int poolCount = RegularPools.Length;
            int[] used = new int[poolCount];
            int[] counts = new int[poolCount];
            int total = 0;
            for (int i = 0; i < poolCount; i++) { counts[i] = RegularPools[i].count; total += counts[i]; }

            DailyLevelPick[] order = new DailyLevelPick[total];

            for (int slot = 0; slot < total; slot++)
            {
                // The pool whose used-so-far fraction (used+1)/count is smallest -- "most behind"
                // its fair share of the year so far. Compared by cross-multiplication
                // ((used0+1)*count1 vs (used1+1)*count0), which needs no floating point at all.
                int best = -1;
                for (int i = 0; i < poolCount; i++)
                {
                    if (used[i] >= counts[i]) { continue; }
                    if (best == -1) { best = i; continue; }

                    long lhs = (long)(used[i] + 1) * counts[best];
                    long rhs = (long)(used[best] + 1) * counts[i];
                    if (lhs < rhs) { best = i; }
                }

                order[slot] = new DailyLevelPick
                {
                    packSize = RegularPools[best].packSize,
                    levelNumber = ++used[best],
                    isLeapDayExtra = false,
                };
            }

            return order;
        }

        /// <summary>The board for a specific calendar date (UTC). See the class doc comment for
        /// the leap-day rule.</summary>
        public static DailyLevelPick LevelForDate(DateTime utcDate)
        {
            bool leap = DateTime.IsLeapYear(utcDate.Year);
            int doy = utcDate.DayOfYear; // 1-based; 1..365, or 1..366 in a leap year

            if (leap && doy == LeapDaySlot)
            {
                return new DailyLevelPick
                {
                    packSize = LeapExtraPackSize,
                    levelNumber = LeapExtraLevelNumber,
                    isLeapDayExtra = true,
                };
            }

            // Past Feb 29 in a leap year, every day-of-year number is one higher than the SAME
            // calendar date carries in a non-leap year -- subtract the extra day back out so a
            // date like March 15th lands on the same regular slot every year, leap or not.
            int regularDoy = (leap && doy > LeapDaySlot) ? doy - 1 : doy;
            int slot = regularDoy - 1; // 0-based

            if (slot < 0) { slot = 0; }
            if (slot >= regularOrder.Length) { slot = regularOrder.Length - 1; }

            return regularOrder[slot];
        }

        /// <summary>The board for the day <paramref name="absoluteDayIndex"/> days after
        /// <see cref="DailyChallengeSelector.EpochUtc"/> -- the same absolute-day counting the
        /// save file's streak fields already use, so the calendar UI can ask for any day (past,
        /// today, or a browsed future month) with the one integer it already has.</summary>
        public static DailyLevelPick LevelForAbsoluteDay(int absoluteDayIndex)
        {
            return LevelForDate(DailyChallengeSelector.EpochUtc.AddDays(absoluteDayIndex));
        }

        /// <summary>The plain, uncompressed absolute day index for a real calendar date -- the
        /// inverse of <see cref="LevelForAbsoluteDay"/>'s date lookup. Deliberately NOT
        /// <see cref="DailyChallengeSelector.DayIndex"/>: that method's compressed-debug-day
        /// override is meant only for "what is today RIGHT NOW", and would otherwise make a
        /// literal calendar date (e.g. a month a player has paged forward to) read as some
        /// arbitrary compressed period instead of the real day it is.</summary>
        public static int AbsoluteDayIndexFor(DateTime utcDate)
        {
            return (int)(utcDate.Date - DailyChallengeSelector.EpochUtc).TotalDays;
        }
    }
}
