using System.Text.RegularExpressions;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers LevelValidator.Validate against small, hand-built grids. Each test isolates one
    /// error path: it sets up only the state that path needs (extra dots or mechanics elsewhere
    /// on the grid would trigger their own unrelated errors and break LogAssert's expectations),
    /// so a passing "clean" test and a failing "Expect" test both mean exactly what they say.
    /// </summary>
    public class LevelValidatorTests
    {
        private Block[,] grid;

        [TearDown]
        public void TearDown()
        {
            if (grid == null) { return; }
            foreach (Block block in grid)
            {
                BlockTestHarness.Destroy(block);
            }
            grid = null;
        }

        private Block[,] CreateGrid(int rows, int cols)
        {
            grid = new Block[rows, cols];
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    grid[i, j] = BlockTestHarness.CreateBlock(i, j);
                }
            }
            return grid;
        }

        // -- Happy path --------------------------------------------------------------------

        [Test]
        public void StraightLinePair_ValidatesCleanly()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);

            LevelValidator.Validate(grid, 1, 3);

            LogAssert.NoUnexpectedReceived();
        }

        // -- Dot counts ----------------------------------------------------------------------

        [Test]
        public void Pair_WithOnlyOneDot_LogsError()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);

            LogAssert.Expect(LogType.Error, new Regex("expected exactly 2"));
            LevelValidator.Validate(grid, 1, 3);
        }

        // -- Rule cells: Checkpoint / ForbiddenForPair / AllowedForPairs -------------------

        [Test]
        public void CheckpointCell_WithNoPairId_LogsError()
        {
            CreateGrid(1, 1);
            BlockTestHarness.SetRuleCell(grid[0, 0], BlockType.Checkpoint, pairId: 0);

            LogAssert.Expect(LogType.Error, new Regex("has no pairId"));
            LevelValidator.Validate(grid, 1, 1);
        }

        [Test]
        public void CheckpointCell_NamingUnknownPair_LogsError()
        {
            CreateGrid(1, 1);
            BlockTestHarness.SetRuleCell(grid[0, 0], BlockType.Checkpoint, pairId: 9);

            LogAssert.Expect(LogType.Error, new Regex("which has no dots on this board"));
            LevelValidator.Validate(grid, 1, 1);
        }

        [Test]
        public void CheckpointCell_ThatIsAlsoADot_LogsError()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);
            BlockTestHarness.SetRuleCell(grid[0, 0], BlockType.Checkpoint, pairId: 1);

            LogAssert.Expect(LogType.Error, new Regex("is also a pair dot"));
            LevelValidator.Validate(grid, 1, 3);
        }

        // -- One-Way ---------------------------------------------------------------------

        [Test]
        public void OneWayCell_WithNoRequiredDirection_LogsError()
        {
            CreateGrid(1, 1);
            BlockTestHarness.SetOneWay(grid[0, 0], Direction.None);

            LogAssert.Expect(LogType.Error, new Regex("behaves as a plain cell"));
            LevelValidator.Validate(grid, 1, 1);
        }

        [Test]
        public void OneWayCell_WithItsOwnEntryEdgeWalled_LogsError()
        {
            CreateGrid(1, 1);
            // Must be entered moving Right -- i.e. through its Left edge -- which is walled off.
            BlockTestHarness.SetOneWay(grid[0, 0], Direction.Right);
            BlockTestHarness.SetWall(grid[0, 0], Direction.Left);

            LogAssert.Expect(LogType.Error, new Regex("can never be entered at all"));
            LevelValidator.Validate(grid, 1, 1);
        }

        // -- Arrow -------------------------------------------------------------------------

        [Test]
        public void ArrowCell_WithNoForcedExitDirection_LogsError()
        {
            CreateGrid(1, 1);
            BlockTestHarness.SetArrow(grid[0, 0], Direction.None);

            LogAssert.Expect(LogType.Error, new Regex("behaves as a plain cell"));
            LevelValidator.Validate(grid, 1, 1);
        }

        [Test]
        public void ArrowCell_PointingOffTheBoard_LogsError()
        {
            CreateGrid(1, 1);
            BlockTestHarness.SetArrow(grid[0, 0], Direction.Right);

            LogAssert.Expect(LogType.Error, new Regex("off the board"));
            LevelValidator.Validate(grid, 1, 1);
        }

        // -- Bridge ------------------------------------------------------------------------

        [Test]
        public void BridgeCell_WithBothAxesWalled_LogsError()
        {
            CreateGrid(3, 3);
            BlockTestHarness.SetBridge(grid[1, 1]);
            BlockTestHarness.SetWall(grid[1, 1], Direction.Left);
            BlockTestHarness.SetWall(grid[1, 1], Direction.Right);
            BlockTestHarness.SetWall(grid[1, 1], Direction.Up);
            BlockTestHarness.SetWall(grid[1, 1], Direction.Down);

            LogAssert.Expect(LogType.Error, new Regex("no crossable lane at all"));
            LevelValidator.Validate(grid, 3, 3);
        }

        [Test]
        public void BridgeCell_WithOnlyOneAxisOpen_LogsError()
        {
            CreateGrid(3, 3);
            BlockTestHarness.SetBridge(grid[1, 1]);
            BlockTestHarness.SetWall(grid[1, 1], Direction.Up);
            BlockTestHarness.SetWall(grid[1, 1], Direction.Down);

            LogAssert.Expect(LogType.Error, new Regex("only has its horizontal lane open"));
            LevelValidator.Validate(grid, 3, 3);
        }

        // -- Shared destinations -----------------------------------------------------------
        //
        // CollectDots registers a shared-destination cell as a dot for EVERY pair it names, not
        // just its primary PairId -- so a cell with thirdPairId = 7 counts as one of pair 7's two
        // dots all by itself. Each pair named on such a cell therefore needs exactly one more real
        // dot elsewhere, not a fresh pair of dots, or ValidateDotCounts fires an unwanted error.

        [Test]
        public void SharedGoal_WithBothPairsProperlyDotted_ValidatesCleanly()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 1], pairId: 1, secondPairId: 2); // shared destination
            BlockTestHarness.SetDot(grid[0, 2], pairId: 2);

            LevelValidator.Validate(grid, 1, 3);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SharedGoal_SkippingTheSecondSlotButFillingTheThird_LogsError()
        {
            CreateGrid(2, 2);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1, thirdPairId: 7); // shared destination
            BlockTestHarness.SetDot(grid[0, 1], pairId: 1); // pair 1's other dot
            BlockTestHarness.SetDot(grid[1, 0], pairId: 7); // pair 7's other dot

            LogAssert.Expect(LogType.Error, new Regex("skips its second pair slot but fills a later one"));
            LevelValidator.Validate(grid, 2, 2);
        }

        [Test]
        public void SharedGoal_NamingTheSamePairTwice_LogsError()
        {
            CreateGrid(1, 1);
            // Names pair 1 as both its primary and second colour -- CollectDots registers this one
            // cell as both of pair 1's two dots, so ValidateDotCounts stays clean and only the
            // duplicate-name check fires.
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1, secondPairId: 1);

            LogAssert.Expect(LogType.Error, new Regex("more than once"));
            LevelValidator.Validate(grid, 1, 1);
        }

        // -- Reachability --------------------------------------------------------------------

        [Test]
        public void PairSeparatedByABlockedCell_LogsNoRouteError()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);
            BlockTestHarness.SetBlocked(grid[0, 1]);

            LogAssert.Expect(LogType.Error, new Regex("no legal route"));
            LevelValidator.Validate(grid, 1, 3);
        }
    }
}
