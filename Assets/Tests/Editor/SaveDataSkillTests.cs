using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// SaveData's Phase 9 additions: schema versioning/migration, per-mechanic skill tracking, and
    /// per-level hint counts. SaveData is a plain serialisable struct with no Unity dependencies,
    /// so these run against it directly rather than through SavingSystem's file I/O.
    /// </summary>
    public class SaveDataSkillTests
    {
        // -- schema migration -----------------------------------------------------------------

        [Test]
        public void Migrate_StampsCurrentVersion_OnAZeroVersionSave()
        {
            SaveData data = new SaveData(); // schemaVersion defaults to 0, as an old save would
            SaveData.Migrate(ref data);

            Assert.AreEqual(SaveData.CurrentSchemaVersion, data.schemaVersion);
        }

        [Test]
        public void Migrate_PreservesExistingProgress()
        {
            SaveData data = new SaveData { completedLevel = 12 };
            SaveData.Migrate(ref data);

            Assert.AreEqual(12, data.completedLevel);
        }

        // -- per-mechanic skill -----------------------------------------------------------------

        [Test]
        public void UnseenMechanic_RatesZero_NotSomethingElse()
        {
            SaveData data = new SaveData();
            Assert.AreEqual(0f, data.MechanicSkillRating("Bridge"));
            Assert.AreEqual(0f, data.OverallSkillRating());
        }

        [Test]
        public void RecordMechanicAttempt_GrowsTheArrayExactlyOnce_PerNewMechanic()
        {
            SaveData data = new SaveData();
            data.RecordMechanicAttempt("Bridge");
            data.RecordMechanicAttempt("Bridge");
            data.RecordMechanicAttempt("Checkpoint");

            Assert.AreEqual(2, data.mechanicSkills.Length);
        }

        [Test]
        public void MechanicSkillRating_IsCompletionsOverAttempts_AsAPercentage()
        {
            SaveData data = new SaveData();
            data.RecordMechanicAttempt("Bridge");
            data.RecordMechanicAttempt("Bridge");
            data.RecordMechanicAttempt("Bridge");
            data.RecordMechanicAttempt("Bridge");
            data.RecordMechanicCompletion("Bridge");

            Assert.AreEqual(25f, data.MechanicSkillRating("Bridge"));
        }

        [Test]
        public void OverallSkillRating_PoolsAcrossEveryMechanicSeen()
        {
            SaveData data = new SaveData();

            // Bridge: 2 attempts, 1 completion.
            data.RecordMechanicAttempt("Bridge");
            data.RecordMechanicAttempt("Bridge");
            data.RecordMechanicCompletion("Bridge");

            // BasicFlow: 2 attempts, 2 completions.
            data.RecordMechanicAttempt(FreeFlow.GamePlay.LevelMechanics.BasicFlowKey);
            data.RecordMechanicAttempt(FreeFlow.GamePlay.LevelMechanics.BasicFlowKey);
            data.RecordMechanicCompletion(FreeFlow.GamePlay.LevelMechanics.BasicFlowKey);
            data.RecordMechanicCompletion(FreeFlow.GamePlay.LevelMechanics.BasicFlowKey);

            // Pooled: 3 completions / 4 attempts = 75%, not a 50/50 average of the two mechanics'
            // individual rates (25% and 100%) -- attempts-weighted, not mechanic-weighted.
            Assert.AreEqual(75f, data.OverallSkillRating());
        }

        [Test]
        public void RecordMechanicCompletion_OnAMechanicNeverAttempted_StillCounts()
        {
            // Should not happen in practice (SetSolution always records the attempt first), but
            // the accessor itself must not throw or silently drop the completion.
            SaveData data = new SaveData();
            data.RecordMechanicCompletion("Arrow");

            Assert.AreEqual(1, data.mechanicSkills.Length);
            Assert.AreEqual(1, data.mechanicSkills[0].completions);
            Assert.AreEqual(0, data.mechanicSkills[0].attempts);
        }

        // -- per-level hint counts --------------------------------------------------------------

        [Test]
        public void HintsForKey_OnAnUnplayedPack_IsNull()
        {
            SaveData data = new SaveData();
            Assert.IsNull(data.HintsForKey("Classic6x6"));
        }

        [Test]
        public void SetHintsForKey_ThenRead_RoundTrips()
        {
            SaveData data = new SaveData();
            data.SetHintsForKey("Classic6x6", new[] { 0, 2, 0 });

            CollectionAssert.AreEqual(new[] { 0, 2, 0 }, data.HintsForKey("Classic6x6"));
        }

        [Test]
        public void HintsForKey_OnALegacyKey_IsAlwaysNull()
        {
            // The legacy linear campaigns never gained a hint column -- their levels predate the
            // stored-solution column the hint button requires, so the button is never enabled
            // there in the first place (GAME_EXPANSION_PLAN §6.41).
            SaveData data = new SaveData();
            data.SetHintsForKey("Classic", new[] { 5 }); // no-op: legacy keys refuse the write
            Assert.IsNull(data.HintsForKey("Classic"));
        }

        [Test]
        public void SetHintsForKey_DoesNotDisturbAttemptsOrSecondsOnTheSamePack()
        {
            SaveData data = new SaveData();
            data.SetAttemptsForKey("Advanced6x6", new[] { 3, 1 });
            data.SetHintsForKey("Advanced6x6", new[] { 0, 1 });

            CollectionAssert.AreEqual(new[] { 3, 1 }, data.AttemptsForKey("Advanced6x6"));
            CollectionAssert.AreEqual(new[] { 0, 1 }, data.HintsForKey("Advanced6x6"));
        }
    }
}
