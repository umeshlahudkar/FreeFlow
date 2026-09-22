using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// LevelMechanics.Identify is the single source of truth UIController's HUD label and the
    /// mechanic-teaching gate both read (see the class's own doc comment) -- these tests pin
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

        // -- FlagFor: the reverse of Keys --------------------------------------------------------

        [Test]
        public void FlagFor_RoundTripsEveryKeyKeysCanProduce()
        {
            MechanicFlags all = MechanicFlags.Blocked | MechanicFlags.Wall | MechanicFlags.OneWay
                | MechanicFlags.Arrow | MechanicFlags.Forbidden | MechanicFlags.Permitted
                | MechanicFlags.Bridge | MechanicFlags.Checkpoint | MechanicFlags.SharedDestination;

            foreach (string key in LevelMechanics.Keys(all))
            {
                Assert.AreNotEqual(MechanicFlags.None, LevelMechanics.FlagFor(key), key);
            }
        }

        [Test]
        public void FlagFor_OnBasicFlow_IsNone()
        {
            // BasicFlowKey names a mechanic-free board, not a mechanic -- there is no flag for it.
            Assert.AreEqual(MechanicFlags.None, LevelMechanics.FlagFor(LevelMechanics.BasicFlowKey));
        }
    }
}
