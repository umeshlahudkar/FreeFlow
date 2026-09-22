using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// SaveData's Phase 9 addition: schema versioning/migration. SaveData is a plain serialisable
    /// struct with no Unity dependencies, so these run against it directly rather than through
    /// SavingSystem's file I/O.
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
            SaveData data = new SaveData();
            data.SetCompletedLevelForKey("Classic6x6", 12);
            SaveData.Migrate(ref data);

            Assert.AreEqual(12, data.CompletedLevelForKey("Classic6x6"));
        }
    }
}
