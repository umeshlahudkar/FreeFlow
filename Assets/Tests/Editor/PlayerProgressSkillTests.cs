using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// PlayerProgress's Phase 9 addition: schema versioning/migration. PlayerProgress is a plain serialisable
    /// struct with no Unity dependencies, so these run against it directly rather than through
    /// ProfileManager's file I/O.
    /// </summary>
    public class PlayerProgressSkillTests
    {
        // -- schema migration -----------------------------------------------------------------

        [Test]
        public void Migrate_StampsCurrentVersion_OnAZeroVersionSave()
        {
            PlayerProgress data = new PlayerProgress(); // schemaVersion defaults to 0, as an old save would
            PlayerProgress.Migrate(ref data);

            Assert.AreEqual(PlayerProgress.CurrentSchemaVersion, data.schemaVersion);
        }

        [Test]
        public void Migrate_PreservesExistingProgress()
        {
            PlayerProgress data = new PlayerProgress();
            data.SetCompletedLevelForKey("Classic6x6", 12);
            PlayerProgress.Migrate(ref data);

            Assert.AreEqual(12, data.CompletedLevelForKey("Classic6x6"));
        }
    }
}
