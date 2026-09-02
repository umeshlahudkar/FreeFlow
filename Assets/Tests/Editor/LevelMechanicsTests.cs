using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// LevelMechanics.Identify is the single source of truth UIController's HUD label and the
    /// per-mechanic skill tracker both read (see the class's own doc comment) -- these tests pin
    /// down exactly which LevelData shapes set which flag, so a future edit to one consumer can't
    /// silently stop matching the other.
    /// </summary>
    public class LevelMechanicsTests
    {
        private static GridRow PlainRow(int cells)
        {
            return new GridRow
            {
                coloum = new PairColorType[cells],
                pairId = new int[cells],
                blockType = new BlockType[cells],
            };
        }

        [Test]
        public void EmptyLevel_IsBasicFlowOnly()
        {
            LevelData data = new LevelData { gridRows = new[] { PlainRow(3) } };

            Assert.AreEqual(MechanicFlags.None, LevelMechanics.Identify(data));
            CollectionAssert.AreEqual(new[] { LevelMechanics.BasicFlowKey }, LevelMechanics.Keys(MechanicFlags.None));
        }

        [Test]
        public void NullGridRows_IsBasicFlowOnly()
        {
            LevelData data = new LevelData { gridRows = null };
            Assert.AreEqual(MechanicFlags.None, LevelMechanics.Identify(data));
        }

        [Test]
        public void BlockedCell_SetsBlockedFlag()
        {
            GridRow row = PlainRow(2);
            row.blockType[0] = BlockType.Blocked;
            LevelData data = new LevelData { gridRows = new[] { row } };

            Assert.AreEqual(MechanicFlags.Blocked, LevelMechanics.Identify(data));
        }

        [Test]
        public void WallMask_SetsWallFlag_EvenWithoutAnyBlockType()
        {
            GridRow row = PlainRow(2);
            row.wallMask = new[] { 1, 0 };
            LevelData data = new LevelData { gridRows = new[] { row } };

            Assert.AreEqual(MechanicFlags.Wall, LevelMechanics.Identify(data));
        }

        [Test]
        public void SecondPairId_OnAPlainCell_IsSharedDestination()
        {
            GridRow row = PlainRow(2);
            row.secondPairId = new[] { 0, 5 };
            LevelData data = new LevelData { gridRows = new[] { row } };

            Assert.AreEqual(MechanicFlags.SharedDestination, LevelMechanics.Identify(data));
        }

        [Test]
        public void SecondPairId_NamingAForbiddenPair_IsNotSharedDestination()
        {
            // ForbiddenForPair reuses secondPairId to name a SECOND forbidden pair, not a shared
            // dot -- Block.SecondIdNamesAPair is what tells the two apart.
            GridRow row = PlainRow(2);
            row.blockType[1] = BlockType.ForbiddenForPair;
            row.secondPairId = new[] { 0, 3 };
            LevelData data = new LevelData { gridRows = new[] { row } };

            Assert.AreEqual(MechanicFlags.Forbidden, LevelMechanics.Identify(data));
        }

        [Test]
        public void ThirdOrFourthPairId_IsSharedDestination()
        {
            GridRow row = PlainRow(1);
            row.thirdPairId = new[] { 7 };
            LevelData data = new LevelData { gridRows = new[] { row } };

            Assert.AreEqual(MechanicFlags.SharedDestination, LevelMechanics.Identify(data));
        }

        [Test]
        public void MultipleMechanics_AllFlagsSet_AndAllKeysReturned()
        {
            GridRow row = PlainRow(3);
            row.blockType[0] = BlockType.Bridge;
            row.blockType[1] = BlockType.Checkpoint;
            row.wallMask = new[] { 0, 2, 0 };
            LevelData data = new LevelData { gridRows = new[] { row } };

            MechanicFlags flags = LevelMechanics.Identify(data);
            Assert.AreEqual(MechanicFlags.Bridge | MechanicFlags.Checkpoint | MechanicFlags.Wall, flags);

            string[] keys = LevelMechanics.Keys(flags);
            CollectionAssert.AreEquivalent(new[] { "Bridge", "Checkpoint", "Wall" }, keys);
        }

        [Test]
        public void Keys_OnNone_ReturnsOnlyBasicFlow()
        {
            CollectionAssert.AreEqual(new[] { LevelMechanics.BasicFlowKey }, LevelMechanics.Keys(MechanicFlags.None));
        }

        // -- SkillKeys: Classic's board-size correction -----------------------------------------

        [Test]
        public void SkillKeys_ClassicWithNoMechanic_FoldsInBoardSize()
        {
            string[] keys = LevelMechanics.SkillKeys(MechanicFlags.None, GameMode.Classic, 6);
            CollectionAssert.AreEqual(new[] { "BasicFlow6x6" }, keys);
        }

        [Test]
        public void SkillKeys_ClassicAtDifferentSizes_AreDifferentBuckets()
        {
            string[] small = LevelMechanics.SkillKeys(MechanicFlags.None, GameMode.Classic, 5);
            string[] large = LevelMechanics.SkillKeys(MechanicFlags.None, GameMode.Classic, 9);

            Assert.AreNotEqual(small[0], large[0]);
        }

        [Test]
        public void SkillKeys_AdvancedWithNoMechanic_UsesTheFlatKey()
        {
            // Advanced varies mechanics, not board size (today, one pack: 6x6) -- its mechanic-free
            // case, should one ever ship, is not split by size the way Classic's is.
            string[] keys = LevelMechanics.SkillKeys(MechanicFlags.None, GameMode.Advanced, 6);
            CollectionAssert.AreEqual(new[] { LevelMechanics.BasicFlowKey }, keys);
        }

        [Test]
        public void SkillKeys_ClassicWithNoPackSize_FallsBackToTheFlatKey()
        {
            // packSize 0 is the (now dead, but still handled) legacy linear campaign -- no board
            // size to key by, so there is nothing to fold in.
            string[] keys = LevelMechanics.SkillKeys(MechanicFlags.None, GameMode.Classic, 0);
            CollectionAssert.AreEqual(new[] { LevelMechanics.BasicFlowKey }, keys);
        }

        [Test]
        public void SkillKeys_WithAMechanicPresent_IgnoresModeAndSize()
        {
            // A real mechanic always wins -- board size never matters once a level has one, since
            // Advanced (the only mode that carries mechanics today) does not vary size.
            string[] classic = LevelMechanics.SkillKeys(MechanicFlags.Bridge, GameMode.Classic, 6);
            string[] advanced = LevelMechanics.SkillKeys(MechanicFlags.Bridge, GameMode.Advanced, 9);

            CollectionAssert.AreEqual(new[] { "Bridge" }, classic);
            CollectionAssert.AreEqual(new[] { "Bridge" }, advanced);
        }
    }
}
