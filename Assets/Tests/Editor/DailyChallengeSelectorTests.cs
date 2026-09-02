using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// DailyChallengeSelector.Select is pure arithmetic over already-shipped packs (see its own
    /// doc comment for why this is not real on-device generation) -- these tests pin down
    /// determinism, the skill-band boundaries, the pack-size rotation, and the per-install salt,
    /// without touching Unity.
    /// </summary>
    public class DailyChallengeSelectorTests
    {
        private static readonly int[] ClassicPackSizes = { 5, 6, 7, 8, 9 };
        private const int TestSalt = 424242;
        private const int OtherSalt = 13;

        [Test]
        public void SamedayAndSkillAndSalt_AlwaysPicksTheSameLevel()
        {
            var a = DailyChallengeSelector.Select(2000, GameMode.Classic, ClassicPackSizes, 100, 50f, TestSalt);
            var b = DailyChallengeSelector.Select(2000, GameMode.Classic, ClassicPackSizes, 100, 50f, TestSalt);

            Assert.AreEqual(a.packSize, b.packSize);
            Assert.AreEqual(a.levelNumber, b.levelNumber);
        }

        [Test]
        public void PackSize_RotatesByDayIndex_ThroughEveryAvailableSize()
        {
            var picks = new System.Collections.Generic.HashSet<int>();
            for (int day = 0; day < ClassicPackSizes.Length; day++)
            {
                picks.Add(DailyChallengeSelector.Select(day, GameMode.Classic, ClassicPackSizes, 100, 50f, TestSalt).packSize);
            }

            CollectionAssert.AreEquivalent(ClassicPackSizes, picks);
        }

        [Test]
        public void PackSize_RepeatsAfterOneFullRotation()
        {
            var first = DailyChallengeSelector.Select(0, GameMode.Classic, ClassicPackSizes, 100, 50f, TestSalt);
            var wrapped = DailyChallengeSelector.Select(ClassicPackSizes.Length, GameMode.Classic, ClassicPackSizes, 100, 50f, TestSalt);

            Assert.AreEqual(first.packSize, wrapped.packSize);
        }

        [Test]
        public void PackSize_IsTheSame_RegardlessOfSalt()
        {
            // The pack-size rotation is deliberately NOT salted -- see Select's own doc comment --
            // so the "every board size within a week" guarantee holds per install, not on average.
            var a = DailyChallengeSelector.Select(3, GameMode.Classic, ClassicPackSizes, 100, 50f, TestSalt);
            var b = DailyChallengeSelector.Select(3, GameMode.Classic, ClassicPackSizes, 100, 50f, OtherSalt);

            Assert.AreEqual(a.packSize, b.packSize);
        }

        [Test]
        public void LowSkill_StaysInTheBottomThird()
        {
            for (int day = 0; day < 30; day++)
            {
                var pick = DailyChallengeSelector.Select(day, GameMode.Classic, ClassicPackSizes, 99, 0f, TestSalt);
                Assert.LessOrEqual(pick.levelNumber, 33, "day " + day);
                Assert.GreaterOrEqual(pick.levelNumber, 1, "day " + day);
            }
        }

        [Test]
        public void MidSkill_StaysInTheMiddleThird()
        {
            for (int day = 0; day < 30; day++)
            {
                var pick = DailyChallengeSelector.Select(day, GameMode.Classic, ClassicPackSizes, 99, 50f, TestSalt);
                Assert.GreaterOrEqual(pick.levelNumber, 34, "day " + day);
                Assert.LessOrEqual(pick.levelNumber, 66, "day " + day);
            }
        }

        [Test]
        public void HighSkill_StaysInTheTopThird_AndReachesTheFinalLevel()
        {
            bool reachedLast = false;
            for (int day = 0; day < 200; day++)
            {
                var pick = DailyChallengeSelector.Select(day, GameMode.Classic, ClassicPackSizes, 99, 100f, TestSalt);
                Assert.GreaterOrEqual(pick.levelNumber, 67, "day " + day);
                Assert.LessOrEqual(pick.levelNumber, 99, "day " + day);
                if (pick.levelNumber == 99) { reachedLast = true; }
            }

            // The top band absorbs whatever packLevelCount/3 truncated away (99/3 = 33 exactly
            // here, so this is really just confirming inclusivity) -- see the class's own comment.
            Assert.IsTrue(reachedLast, "expected level 99 (the pack's last level) to be reachable");
        }

        [Test]
        public void SkillBandBoundaries_30And70_AreExclusiveOnTheLowerBand()
        {
            var at30 = DailyChallengeSelector.Select(5, GameMode.Classic, ClassicPackSizes, 99, 30f, TestSalt);
            var at70 = DailyChallengeSelector.Select(5, GameMode.Classic, ClassicPackSizes, 99, 70f, TestSalt);
            var justBelow30 = DailyChallengeSelector.Select(5, GameMode.Classic, ClassicPackSizes, 99, 29.999f, TestSalt);

            Assert.GreaterOrEqual(at30.levelNumber, 34);   // 30 itself is already the middle band
            Assert.GreaterOrEqual(at70.levelNumber, 67);   // 70 itself is already the top band
            Assert.LessOrEqual(justBelow30.levelNumber, 33);
        }

        [Test]
        public void DifferentDays_CanPickDifferentLevelsWithinTheSameBand()
        {
            // Not a hard requirement of any single pair of days (the hash could coincide), but
            // across enough days the picks should not all collapse to one level -- otherwise the
            // "day's hash picks within the band" half of the design would be doing nothing.
            var levels = new System.Collections.Generic.HashSet<int>();
            for (int day = 0; day < 30; day++)
            {
                levels.Add(DailyChallengeSelector.Select(day, GameMode.Classic, ClassicPackSizes, 99, 50f, TestSalt).levelNumber);
            }

            Assert.Greater(levels.Count, 1);
        }

        [Test]
        public void DifferentSalts_CanPickDifferentLevels_SameDaySameSkill()
        {
            // The whole point of the salt: two "players" (different salts) in the same band on the
            // same day should not be guaranteed the identical puzzle. Checked across several salts
            // rather than one pair, since any single pair could coincide by chance.
            var levels = new System.Collections.Generic.HashSet<int>();
            for (int salt = 1; salt <= 20; salt++)
            {
                levels.Add(DailyChallengeSelector.Select(7, GameMode.Classic, ClassicPackSizes, 99, 50f, salt).levelNumber);
            }

            Assert.Greater(levels.Count, 1);
        }

        [Test]
        public void DifferentSalts_StayWithinTheSameSkillBand()
        {
            // Salt only picks WHICH level inside the band, never a different band -- skill still
            // governs difficulty regardless of which install is asking.
            for (int salt = 1; salt <= 20; salt++)
            {
                var pick = DailyChallengeSelector.Select(7, GameMode.Classic, ClassicPackSizes, 99, 0f, salt);
                Assert.LessOrEqual(pick.levelNumber, 33, "salt " + salt);
            }
        }

        [Test]
        public void DayIndex_IsStableForTheSameCalendarDay_RegardlessOfTimeOfDay()
        {
            var morning = new System.DateTime(2026, 3, 5, 1, 0, 0, System.DateTimeKind.Utc);
            var night = new System.DateTime(2026, 3, 5, 23, 59, 0, System.DateTimeKind.Utc);

            Assert.AreEqual(DailyChallengeSelector.DayIndex(morning), DailyChallengeSelector.DayIndex(night));
        }

        [Test]
        public void DayIndex_AdvancesByOnePerCalendarDay()
        {
            var day1 = new System.DateTime(2026, 3, 5, 12, 0, 0, System.DateTimeKind.Utc);
            var day2 = new System.DateTime(2026, 3, 6, 12, 0, 0, System.DateTimeKind.Utc);

            Assert.AreEqual(1, DailyChallengeSelector.DayIndex(day2) - DailyChallengeSelector.DayIndex(day1));
        }
    }

    /// <summary>SaveData.RecordDailyChallengeCompletion's streak arithmetic: consecutive days
    /// extend it, a gap resets it, and the same day twice is a no-op.</summary>
    public class DailyChallengeStreakTests
    {
        [Test]
        public void FirstEverCompletion_StartsAStreakOfOne()
        {
            SaveData data = new SaveData();
            data.RecordDailyChallengeCompletion(100);

            Assert.AreEqual(1, data.dailyChallengeStreak);
            Assert.AreEqual(100, data.dailyChallengeLastCompletedDay);
            Assert.AreEqual(1, data.dailyChallengesCompletedTotal);
        }

        [Test]
        public void ConsecutiveDays_ExtendTheStreak()
        {
            SaveData data = new SaveData();
            data.RecordDailyChallengeCompletion(100);
            data.RecordDailyChallengeCompletion(101);
            data.RecordDailyChallengeCompletion(102);

            Assert.AreEqual(3, data.dailyChallengeStreak);
            Assert.AreEqual(3, data.dailyChallengesCompletedTotal);
        }

        [Test]
        public void AGap_ResetsTheStreakToOne_ButNotTheLifetimeTotal()
        {
            SaveData data = new SaveData();
            data.RecordDailyChallengeCompletion(100);
            data.RecordDailyChallengeCompletion(101);
            data.RecordDailyChallengeCompletion(105); // skipped several days

            Assert.AreEqual(1, data.dailyChallengeStreak);
            Assert.AreEqual(3, data.dailyChallengesCompletedTotal);
        }

        [Test]
        public void TheSameDayTwice_DoesNotInflateTheStreakOrTheTotal()
        {
            SaveData data = new SaveData();
            data.RecordDailyChallengeCompletion(100);
            data.RecordDailyChallengeCompletion(100); // e.g. retried after already completing today
            data.RecordDailyChallengeCompletion(100);

            Assert.AreEqual(1, data.dailyChallengeStreak);
            Assert.AreEqual(1, data.dailyChallengesCompletedTotal);
        }

        [Test]
        public void NeverCompleted_ReadsAsZero()
        {
            SaveData data = new SaveData();
            Assert.AreEqual(0, data.dailyChallengeStreak);
            Assert.AreEqual(0, data.dailyChallengeLastCompletedDay);
            Assert.AreEqual(0, data.dailyChallengesCompletedTotal);
        }
    }

    /// <summary>SaveData.EnsurePlayerSalt: assigned once, ever, and never overwritten.</summary>
    public class PlayerSaltTests
    {
        [Test]
        public void UnsetSalt_AdoptsTheCandidate()
        {
            SaveData data = new SaveData();
            data.EnsurePlayerSalt(777);

            Assert.AreEqual(777, data.playerSalt);
        }

        [Test]
        public void AlreadySetSalt_IgnoresANewCandidate()
        {
            SaveData data = new SaveData();
            data.EnsurePlayerSalt(777);
            data.EnsurePlayerSalt(999); // e.g. a second call across app launches

            Assert.AreEqual(777, data.playerSalt);
        }

        [Test]
        public void NeverAssigned_ReadsAsZero()
        {
            SaveData data = new SaveData();
            Assert.AreEqual(0, data.playerSalt);
        }
    }
}
