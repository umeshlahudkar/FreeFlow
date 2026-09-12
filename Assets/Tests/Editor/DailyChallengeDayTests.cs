using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// DailyChallengeSelector.SelectDay -- a whole day's worth of challenges rather than the single
    /// pick DailyChallengeSelectorTests covers. What matters here is that a day is a SET: as many
    /// levels as asked for, none of them the same board twice, ordered easy-board-first, and with
    /// slot 0 still agreeing with the one-per-day Select that shipped before days could be longer.
    /// </summary>
    public class DailyChallengeDayTests
    {
        private static readonly int[] ClassicPackSizes = { 5, 6, 7, 8, 9 };
        private const int TestSalt = 424242;

        private static DailyChallengeSelector.Pick[] Day(int dayIndex, int count, float skill = 50f, int packLevelCount = 99)
        {
            return DailyChallengeSelector.SelectDay(dayIndex, GameMode.Classic, ClassicPackSizes,
                packLevelCount, skill, TestSalt, count);
        }

        [Test]
        public void ADay_HoldsAsManyChallengesAsAskedFor()
        {
            Assert.AreEqual(5, Day(2000, 5).Length);
            Assert.AreEqual(1, Day(2000, 1).Length);
            Assert.AreEqual(7, Day(2000, 7).Length);
        }

        [Test]
        public void ACountBelowOne_StillYieldsOneChallenge()
        {
            Assert.AreEqual(1, Day(2000, 0).Length);
            Assert.AreEqual(1, Day(2000, -3).Length);
        }

        [Test]
        public void SlotZero_MatchesTheSingleDailyPickForTheSameDay()
        {
            // Load-bearing: a save written when a day held exactly one challenge must keep pointing
            // at the same board after the count is raised (see SelectDay's own doc comment).
            var single = DailyChallengeSelector.Select(2000, GameMode.Classic, ClassicPackSizes, 99, 50f, TestSalt);
            var day = Day(2000, 1);

            Assert.AreEqual(single.packSize, day[0].packSize);
            Assert.AreEqual(single.levelNumber, day[0].levelNumber);
        }

        [Test]
        public void TheSameDay_AlwaysProducesTheSameChallenges()
        {
            var a = Day(2000, 5);
            var b = Day(2000, 5);

            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].packSize, b[i].packSize, "slot " + i);
                Assert.AreEqual(a[i].levelNumber, b[i].levelNumber, "slot " + i);
            }
        }

        [Test]
        public void NoChallengeInADay_RepeatsTheSameBoard()
        {
            for (int day = 0; day < 60; day++)
            {
                var picks = Day(day, 5);
                var seen = new System.Collections.Generic.HashSet<string>();
                foreach (var pick in picks)
                {
                    Assert.IsTrue(seen.Add(pick.packSize + ":" + pick.levelNumber),
                        "day " + day + " repeats " + pick.packSize + "x" + pick.packSize
                        + " level " + pick.levelNumber);
                }
            }
        }

        [Test]
        public void ADay_IsOrderedByAscendingBoardSize()
        {
            for (int day = 0; day < 30; day++)
            {
                var picks = Day(day, 5);
                for (int i = 1; i < picks.Length; i++)
                {
                    Assert.LessOrEqual(picks[i - 1].packSize, picks[i].packSize, "day " + day + " slot " + i);
                }
            }
        }

        [Test]
        public void AFullLengthDay_CoversEveryBoardSizeExactlyOnce()
        {
            // The hub's art shows one level per board size; with as many slots as the mode has pack
            // sizes, the per-slot rotation is what delivers that regardless of which day it is.
            for (int day = 0; day < 10; day++)
            {
                var sizes = new System.Collections.Generic.List<int>();
                foreach (var pick in Day(day, ClassicPackSizes.Length)) { sizes.Add(pick.packSize); }

                CollectionAssert.AreEquivalent(ClassicPackSizes, sizes, "day " + day);
            }
        }

        [Test]
        public void EveryChallengeInADay_StaysInsideTheSkillBand()
        {
            // Skill still governs difficulty for every slot, not just the first -- a day must not
            // quietly ramp a low-skill player into the top third just because it is longer.
            for (int day = 0; day < 40; day++)
            {
                foreach (var pick in DailyChallengeSelector.SelectDay(day, GameMode.Classic,
                    ClassicPackSizes, 99, 0f, TestSalt, 5))
                {
                    Assert.LessOrEqual(pick.levelNumber, 33, "day " + day);
                    Assert.GreaterOrEqual(pick.levelNumber, 1, "day " + day);
                }
            }
        }

        [Test]
        public void ACountBiggerThanTheBandCanSupply_IsClampedRatherThanRepeating()
        {
            // A 3-level pack gives a one-level-wide band, so the 5 pack sizes can only supply 5
            // distinct boards no matter how many slots are asked for.
            var picks = DailyChallengeSelector.SelectDay(2000, GameMode.Classic, ClassicPackSizes,
                3, 0f, TestSalt, 7);

            Assert.AreEqual(5, picks.Length);

            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var pick in picks)
            {
                Assert.IsTrue(seen.Add(pick.packSize + ":" + pick.levelNumber));
            }
        }

        [Test]
        public void NoPackSizesAtAll_YieldsAnEmptyDayRatherThanThrowing()
        {
            Assert.AreEqual(0, DailyChallengeSelector.SelectDay(2000, GameMode.Classic, new int[0], 99, 50f, TestSalt, 5).Length);
            Assert.AreEqual(0, DailyChallengeSelector.SelectDay(2000, GameMode.Classic, null, 99, 50f, TestSalt, 5).Length);
        }
    }

    /// <summary>
    /// SaveData's side of a multi-challenge day: per-slot solved flags, and the rule that a day
    /// only counts toward the streak once EVERY one of its challenges is finished.
    /// </summary>
    public class DailyChallengeDaySaveDataTests
    {
        private static DailyPick[] ThreePicks()
        {
            return new DailyPick[]
            {
                new DailyPick { mode = GameMode.Classic, packSize = 5, levelNumber = 10 },
                new DailyPick { mode = GameMode.Classic, packSize = 6, levelNumber = 20 },
                new DailyPick { mode = GameMode.Classic, packSize = 7, levelNumber = 30 },
            };
        }

        [Test]
        public void SettingADay_StoresThePicksAndTheDay_AndMirrorsSlotZeroIntoTheLegacyFields()
        {
            SaveData data = new SaveData();
            data.SetDailyChallenges(900, ThreePicks());

            Assert.AreEqual(900, data.dailyChallengeCachedDay);
            Assert.AreEqual(3, data.DailyChallengeCount);
            Assert.AreEqual(5, data.dailyChallengePackSize);
            Assert.AreEqual(10, data.dailyChallengeLevel);
        }

        [Test]
        public void SettingANewDay_ClearsTheSolvedFlagsOfThePreviousOne()
        {
            SaveData data = new SaveData();
            data.SetDailyChallenges(900, ThreePicks());
            data.MarkDailyChallengeSolved(0);
            data.MarkDailyChallengeSolved(1);

            data.SetDailyChallenges(901, ThreePicks());

            Assert.AreEqual(0, data.SolvedDailyChallengeCount());
            Assert.IsFalse(data.AllDailyChallengesSolved());
        }

        [Test]
        public void SolvingOne_DoesNotCompleteTheDay()
        {
            SaveData data = new SaveData();
            data.SetDailyChallenges(900, ThreePicks());
            data.MarkDailyChallengeSolved(1);

            Assert.IsTrue(data.IsDailyChallengeSolved(1));
            Assert.IsFalse(data.IsDailyChallengeSolved(0));
            Assert.AreEqual(1, data.SolvedDailyChallengeCount());
            Assert.IsFalse(data.AllDailyChallengesSolved());
        }

        [Test]
        public void SolvingEveryOne_CompletesTheDay()
        {
            SaveData data = new SaveData();
            data.SetDailyChallenges(900, ThreePicks());
            for (int i = 0; i < 3; i++) { data.MarkDailyChallengeSolved(i); }

            Assert.IsTrue(data.AllDailyChallengesSolved());
        }

        [Test]
        public void ADayWithNoPicks_IsNotComplete()
        {
            SaveData data = new SaveData();
            Assert.IsFalse(data.AllDailyChallengesSolved());
            Assert.AreEqual(-1, data.FirstUnsolvedDailyChallenge());
        }

        [Test]
        public void FirstUnsolved_SkipsWhatIsAlreadyDone_AndReadsMinusOneWhenTheDayIsOver()
        {
            SaveData data = new SaveData();
            data.SetDailyChallenges(900, ThreePicks());

            Assert.AreEqual(0, data.FirstUnsolvedDailyChallenge());

            data.MarkDailyChallengeSolved(0);
            Assert.AreEqual(1, data.FirstUnsolvedDailyChallenge());

            data.MarkDailyChallengeSolved(1);
            data.MarkDailyChallengeSolved(2);
            Assert.AreEqual(-1, data.FirstUnsolvedDailyChallenge());
        }

        [Test]
        public void AFreshDay_UnlocksOnlyItsFirstChallenge()
        {
            SaveData data = new SaveData();
            data.SetDailyChallenges(900, ThreePicks());

            Assert.AreEqual(0, data.UnlockedDailyChallengeThrough());
        }

        [Test]
        public void SolvingOne_UnlocksExactlyTheNextOne()
        {
            SaveData data = new SaveData();
            data.SetDailyChallenges(900, ThreePicks());

            data.MarkDailyChallengeSolved(0);
            Assert.AreEqual(1, data.UnlockedDailyChallengeThrough());

            data.MarkDailyChallengeSolved(1);
            Assert.AreEqual(2, data.UnlockedDailyChallengeThrough());
        }

        [Test]
        public void AFullySolvedDay_LeavesEveryChallengeOpenToReplay()
        {
            SaveData data = new SaveData();
            data.SetDailyChallenges(900, ThreePicks());
            for (int i = 0; i < 3; i++) { data.MarkDailyChallengeSolved(i); }

            Assert.AreEqual(2, data.UnlockedDailyChallengeThrough());
        }

        [Test]
        public void NoDayCached_UnlocksNothing()
        {
            SaveData data = new SaveData();
            Assert.AreEqual(-1, data.UnlockedDailyChallengeThrough());
        }

        [Test]
        public void MarkingAnOutOfRangeSlot_IsIgnoredRatherThanThrowing()
        {
            // The day can roll over between a level being opened and being completed, which
            // re-picks a list the in-flight slot index no longer indexes into.
            SaveData data = new SaveData();
            data.SetDailyChallenges(900, ThreePicks());

            Assert.DoesNotThrow(() => data.MarkDailyChallengeSolved(9));
            Assert.DoesNotThrow(() => data.MarkDailyChallengeSolved(-1));
            Assert.AreEqual(0, data.SolvedDailyChallengeCount());
        }

        /// <summary>Replays GamePlayController.SaveLevelData's daily block verbatim -- mark the
        /// slot, then credit the day only if every slot is now solved.</summary>
        private static void CompleteChallenge(ref SaveData data, int slot)
        {
            data.MarkDailyChallengeSolved(slot);
            if (data.AllDailyChallengesSolved())
            {
                data.RecordDailyChallengeCompletion(data.dailyChallengeCachedDay);
            }
        }

        [Test]
        public void TheStreakOnlyMoves_OnceTheWholeDayIsSolved()
        {
            SaveData data = new SaveData();
            data.SetDailyChallenges(900, ThreePicks());

            CompleteChallenge(ref data, 0);
            Assert.AreEqual(0, data.dailyChallengeStreak, "one of three must not move the streak");

            CompleteChallenge(ref data, 1);
            Assert.AreEqual(0, data.dailyChallengeStreak, "two of three must not move the streak");
            Assert.AreEqual(0, data.dailyChallengeLastCompletedDay, "the day is not credited yet");

            CompleteChallenge(ref data, 2);
            Assert.AreEqual(1, data.dailyChallengeStreak);
            Assert.AreEqual(900, data.dailyChallengeLastCompletedDay);
            Assert.AreEqual(1, data.dailyChallengesCompletedTotal);
        }

        [Test]
        public void ReplayingChallengesAfterTheDayIsDone_DoesNotInflateTheStreak()
        {
            SaveData data = new SaveData();
            data.SetDailyChallenges(900, ThreePicks());
            for (int i = 0; i < 3; i++) { CompleteChallenge(ref data, i); }

            // Every slot stays open to replay once the day is done (see
            // UnlockedDailyChallengeThrough) -- each replay re-runs the same gate.
            for (int i = 0; i < 3; i++) { CompleteChallenge(ref data, i); }

            Assert.AreEqual(1, data.dailyChallengeStreak);
            Assert.AreEqual(1, data.dailyChallengesCompletedTotal);
        }

        [Test]
        public void AStreakAcrossDays_NeedsEveryDayFullySolved()
        {
            SaveData data = new SaveData();

            data.SetDailyChallenges(900, ThreePicks());
            for (int i = 0; i < 3; i++) { CompleteChallenge(ref data, i); }
            Assert.AreEqual(1, data.dailyChallengeStreak);

            // Day 901: only two of three finished, so the day never counts...
            data.SetDailyChallenges(901, ThreePicks());
            CompleteChallenge(ref data, 0);
            CompleteChallenge(ref data, 1);
            Assert.AreEqual(1, data.dailyChallengeStreak, "a half-finished day must not extend the streak");
            Assert.AreEqual(900, data.dailyChallengeLastCompletedDay);

            // ...so day 902, fully solved, starts a new run rather than extending the old one.
            data.SetDailyChallenges(902, ThreePicks());
            for (int i = 0; i < 3; i++) { CompleteChallenge(ref data, i); }
            Assert.AreEqual(1, data.dailyChallengeStreak, "the gap at 901 broke the run");
            Assert.AreEqual(2, data.dailyChallengesCompletedTotal);
        }

        [Test]
        public void ConsecutiveFullySolvedDays_ExtendTheStreak()
        {
            SaveData data = new SaveData();

            for (int day = 900; day <= 902; day++)
            {
                data.SetDailyChallenges(day, ThreePicks());
                for (int i = 0; i < 3; i++) { CompleteChallenge(ref data, i); }
            }

            Assert.AreEqual(3, data.dailyChallengeStreak);
            Assert.AreEqual(3, data.dailyChallengesCompletedTotal);
            Assert.AreEqual(3, data.bestDailyChallengeStreak);
        }
    }

    /// <summary>SaveData.PackFrontierAdvances -- when finishing a level may move a pack's unlock
    /// gate. A daily challenge drawn from deep inside a pack must not unlock everything below
    /// it.</summary>
    public class PackFrontierTests
    {
        [Test]
        public void AnOrdinaryPlay_AdvancesTheFrontierWheneverItBeatsIt()
        {
            Assert.IsTrue(SaveData.PackFrontierAdvances(6, 5, false));
            // Can't actually happen through the level grid (levels above the frontier are locked),
            // but if it did it would still be a real play of every level up to it.
            Assert.IsTrue(SaveData.PackFrontierAdvances(59, 5, false));
        }

        [Test]
        public void ReplayingAnAlreadyFinishedLevel_NeverMovesTheFrontier()
        {
            Assert.IsFalse(SaveData.PackFrontierAdvances(3, 5, false));
            Assert.IsFalse(SaveData.PackFrontierAdvances(5, 5, false));
            Assert.IsFalse(SaveData.PackFrontierAdvances(3, 5, true));
        }

        [Test]
        public void ADailyChallengeDeepInAPack_DoesNotUnlockTheLevelsBelowIt()
        {
            // The shipped bug: a level-59 daily pick in a pack the player had barely started set
            // the frontier to 59, unlocking 58 levels and reporting "59/100" as pack progress.
            Assert.IsFalse(SaveData.PackFrontierAdvances(59, 5, true));
            Assert.IsFalse(SaveData.PackFrontierAdvances(7, 5, true));
        }

        [Test]
        public void ADailyChallengeThatIsExactlyTheNextLevel_StillCounts()
        {
            // Nothing is skipped in this case, so it is an ordinary completion of that level.
            Assert.IsTrue(SaveData.PackFrontierAdvances(6, 5, true));
            Assert.IsTrue(SaveData.PackFrontierAdvances(1, 0, true));
        }
    }

    /// <summary>The schema 3 -> 4 migration: a save whose day held exactly one challenge keeps
    /// that challenge, and its solved state, instead of having the board swapped under it.</summary>
    public class DailyChallengeMigrationTests
    {
        private static SaveData Version3Save(int cachedDay, int lastCompletedDay)
        {
            return new SaveData
            {
                schemaVersion = 3,
                dailyChallengeCachedDay = cachedDay,
                dailyChallengeMode = GameMode.Classic,
                dailyChallengePackSize = 7,
                dailyChallengeLevel = 42,
                dailyChallengeLastCompletedDay = lastCompletedDay,
            };
        }

        [Test]
        public void AnUnfinishedSingleChallengeDay_BecomesAOneEntryUnsolvedDay()
        {
            SaveData data = Version3Save(900, 899);
            SaveData.Migrate(ref data);

            Assert.AreEqual(SaveData.CurrentSchemaVersion, data.schemaVersion);
            Assert.AreEqual(1, data.DailyChallengeCount);
            Assert.AreEqual(7, data.dailyChallengePicks[0].packSize);
            Assert.AreEqual(42, data.dailyChallengePicks[0].levelNumber);
            Assert.IsFalse(data.dailyChallengePicks[0].solved);
            Assert.IsFalse(data.AllDailyChallengesSolved());
        }

        [Test]
        public void AFinishedSingleChallengeDay_CarriesItsSolvedStateAcross()
        {
            // Back when a day held one level, "the day is complete" and "that level is solved"
            // were the same statement -- so the flag is recoverable rather than lost.
            SaveData data = Version3Save(900, 900);
            SaveData.Migrate(ref data);

            Assert.AreEqual(1, data.DailyChallengeCount);
            Assert.IsTrue(data.dailyChallengePicks[0].solved);
            Assert.IsTrue(data.AllDailyChallengesSolved());
        }

        [Test]
        public void ASaveThatNeverOpenedTheDailyChallenge_GetsNoPicks()
        {
            SaveData data = Version3Save(0, 0);
            SaveData.Migrate(ref data);

            Assert.AreEqual(0, data.DailyChallengeCount);
        }
    }
}
