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
                packLevelCount, (mode, packSize) => skill, TestSalt, count);
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
            var single = DailyChallengeSelector.Select(2000, GameMode.Classic, ClassicPackSizes, 99, (mode, packSize) => 50f, TestSalt);
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
                    ClassicPackSizes, 99, (mode, packSize) => 0f, TestSalt, 5))
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
                3, (mode, packSize) => 0f, TestSalt, 7);

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
            Assert.AreEqual(0, DailyChallengeSelector.SelectDay(2000, GameMode.Classic, new int[0], 99, (mode, packSize) => 50f, TestSalt, 5).Length);
            Assert.AreEqual(0, DailyChallengeSelector.SelectDay(2000, GameMode.Classic, null, 99, (mode, packSize) => 50f, TestSalt, 5).Length);
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

}
