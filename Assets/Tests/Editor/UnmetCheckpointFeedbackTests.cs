using System.Collections.Generic;
using System.Reflection;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;
using UnityEngine;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers GamePlayController.RefreshUnmetCheckpointFeedback -- which Checkpoints get flagged
    /// as the player draws.
    ///
    /// The rule is a judgement call as much as a mechanism, so both halves are pinned. Flagging too
    /// eagerly is the failure that matters most: an EMPTY checkpoint is unfinished, not wrong --
    /// its colour may simply not be drawn yet -- and blinking through most of a normal solve is
    /// noise a player learns to ignore. A checkpoint holding SOMEONE ELSE is wrong the instant it
    /// happens, because the cell takes one occupant and the rule cannot be met until that colour
    /// leaves.
    ///
    /// Reads live occupancy rather than committed segments on purpose, so the warning lands during
    /// the drag that causes it rather than after release.
    /// </summary>
    public class UnmetCheckpointFeedbackTests
    {
        private GamePlayController controller;
        private Block[,] grid;

        [TearDown]
        public void TearDown()
        {
            if (grid != null)
            {
                foreach (Block block in grid) { BlockTestHarness.Destroy(block); }
                grid = null;
            }
            if (controller != null) { Object.DestroyImmediate(controller.gameObject); }
        }

        private void BuildBoard(int cells)
        {
            grid = new Block[1, cells];
            for (int c = 0; c < cells; c++) { grid[0, c] = BlockTestHarness.CreateBlock(0, c); }

            controller = new GameObject("TestGamePlayController").AddComponent<GamePlayController>();
            controller.grid = grid;
            controller.gridRow = 1;
            controller.gridCol = cells;
            typeof(GamePlayController)
                .GetField("pairSegments", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(controller, new Dictionary<int, List<List<Block>>>());
        }

        private List<Block> Refresh()
        {
            MethodInfo refresh = typeof(GamePlayController).GetMethod("RefreshUnmetCheckpointFeedback",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(refresh, "GamePlayController has no 'RefreshUnmetCheckpointFeedback' -- "
                + "update this test to match GamePlayController.cs.");
            refresh.Invoke(controller, null);

            return (List<Block>)typeof(GamePlayController)
                .GetField("unmetCheckpointBlocks", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(controller);
        }

        [Test]
        public void CheckpointHeldByAnotherColour_IsFlaggedImmediately()
        {
            // No need to wait for a full board: one occupant per cell means the rule is already
            // unsatisfiable while pair 9 sits here.
            BuildBoard(1);
            BlockTestHarness.SetRuleCell(grid[0, 0], BlockType.Checkpoint, pairId: 3);
            BlockTestHarness.ClaimDirection(grid[0, 0], Direction.Right, pairId: 9);

            List<Block> flagged = Refresh();

            Assert.AreEqual(1, flagged.Count);
            Assert.AreSame(grid[0, 0], flagged[0]);
        }

        [Test]
        public void EmptyCheckpoint_IsNotFlagged()
        {
            // Unfinished, not wrong -- pair 3 may still be drawn through it.
            BuildBoard(1);
            BlockTestHarness.SetRuleCell(grid[0, 0], BlockType.Checkpoint, pairId: 3);

            Assert.AreEqual(0, Refresh().Count);
        }

        [Test]
        public void CheckpointHeldByItsOwnColour_IsNotFlagged()
        {
            BuildBoard(1);
            BlockTestHarness.SetRuleCell(grid[0, 0], BlockType.Checkpoint, pairId: 3);
            BlockTestHarness.ClaimDirection(grid[0, 0], Direction.Right, pairId: 3);

            Assert.AreEqual(0, Refresh().Count);
        }

        [Test]
        public void MultipleCheckpoints_EachIsJudgedOnItsOwn()
        {
            // A level may carry several, and they need not name the same pair. Only the wrong ones
            // should blink -- flagging all of them would point the player at a correct cell.
            BuildBoard(3);
            BlockTestHarness.SetRuleCell(grid[0, 0], BlockType.Checkpoint, pairId: 3);
            BlockTestHarness.SetRuleCell(grid[0, 1], BlockType.Checkpoint, pairId: 5);
            BlockTestHarness.SetRuleCell(grid[0, 2], BlockType.Checkpoint, pairId: 7);

            BlockTestHarness.ClaimDirection(grid[0, 0], Direction.Right, pairId: 3); // correct
            BlockTestHarness.ClaimDirection(grid[0, 1], Direction.Right, pairId: 9); // wrong
            //                                     grid[0, 2] left empty            // unfinished

            List<Block> flagged = Refresh();

            Assert.AreEqual(1, flagged.Count, "only the checkpoint held by the wrong colour");
            Assert.AreSame(grid[0, 1], flagged[0]);
        }

        [Test]
        public void OnceTheWrongColourLeaves_TheFlagIsDropped()
        {
            // A stale blink is worse than none: it would accuse a cell that is now correct.
            BuildBoard(1);
            BlockTestHarness.SetRuleCell(grid[0, 0], BlockType.Checkpoint, pairId: 3);
            BlockTestHarness.ClaimDirection(grid[0, 0], Direction.Right, pairId: 9);
            Assert.AreEqual(1, Refresh().Count);

            BlockTestHarness.Destroy(grid[0, 0]);
            grid[0, 0] = BlockTestHarness.CreateBlock(0, 0);
            BlockTestHarness.SetRuleCell(grid[0, 0], BlockType.Checkpoint, pairId: 3);
            BlockTestHarness.ClaimDirection(grid[0, 0], Direction.Right, pairId: 3);
            controller.grid = grid;

            Assert.AreEqual(0, Refresh().Count);
        }
    }
}
