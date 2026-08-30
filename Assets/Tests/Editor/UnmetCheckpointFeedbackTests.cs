using System.Collections.Generic;
using System.Reflection;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;
using UnityEngine;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers GamePlayController.RefreshUnmetCheckpointFeedback -- which cells get flagged when the
    /// board is full but a Checkpoint is held by the wrong colour.
    ///
    /// The behaviour under test is a judgement call as much as a rule, so it is worth pinning both
    /// halves. Flagging too eagerly is the failure mode that matters most: a checkpoint whose
    /// colour has simply not been drawn yet is not wrong, it is unfinished, and blinking it through
    /// most of a normal solve would be noise the player learns to ignore. Only once every cell is
    /// covered does "not yet" stop being a possible explanation.
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

        /// <summary>
        /// 1x3 board: dot, Checkpoint naming <paramref name="checkpointPair"/>, dot.
        /// <paramref name="drawnBy"/> is the pair recorded as covering all three cells; pass 0 for
        /// "nothing drawn yet". Returns the cells the controller decided to flag.
        /// </summary>
        private List<Block> FlaggedCells(int checkpointPair, int drawnBy, bool boardFull)
        {
            grid = new Block[1, 3];
            grid[0, 0] = BlockTestHarness.CreateBlock(0, 0);
            grid[0, 1] = BlockTestHarness.CreateBlock(0, 1);
            grid[0, 2] = BlockTestHarness.CreateBlock(0, 2);

            BlockTestHarness.SetDot(grid[0, 0], 1);
            BlockTestHarness.SetDot(grid[0, 2], 1);
            BlockTestHarness.SetRuleCell(grid[0, 1], BlockType.Checkpoint, checkpointPair);

            controller = new GameObject("TestGamePlayController").AddComponent<GamePlayController>();
            controller.grid = grid;
            controller.gridRow = 1;
            controller.gridCol = 3;

            var segments = new Dictionary<int, List<List<Block>>>();
            if (drawnBy != 0)
            {
                segments[drawnBy] = new List<List<Block>>
                {
                    new List<Block> { grid[0, 0], grid[0, 1], grid[0, 2] }
                };
            }
            typeof(GamePlayController)
                .GetField("pairSegments", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(controller, segments);

            MethodInfo refresh = typeof(GamePlayController).GetMethod("RefreshUnmetCheckpointFeedback",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(refresh, "GamePlayController has no 'RefreshUnmetCheckpointFeedback' -- "
                + "update this test to match GamePlayController.cs.");
            refresh.Invoke(controller, new object[] { boardFull });

            return (List<Block>)typeof(GamePlayController)
                .GetField("unmetCheckpointBlocks", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(controller);
        }

        [Test]
        public void BoardFull_AndCheckpointHeldByAnotherColour_FlagsThatCell()
        {
            // The dead end: every cell covered, pair 1 joined, but the checkpoint belongs to pair 2.
            List<Block> flagged = FlaggedCells(checkpointPair: 2, drawnBy: 1, boardFull: true);

            Assert.AreEqual(1, flagged.Count, "the one cell standing between the player and the win");
            Assert.AreSame(grid[0, 1], flagged[0]);
        }

        [Test]
        public void BoardNotFull_FlagsNothing()
        {
            // Same unmet checkpoint, but the board is still being filled -- the colour may yet
            // arrive, so saying anything here would be noise rather than a hint.
            List<Block> flagged = FlaggedCells(checkpointPair: 2, drawnBy: 1, boardFull: false);

            Assert.AreEqual(0, flagged.Count);
        }

        [Test]
        public void CheckpointOnItsOwnColoursPath_FlagsNothing()
        {
            List<Block> flagged = FlaggedCells(checkpointPair: 1, drawnBy: 1, boardFull: true);

            Assert.AreEqual(0, flagged.Count, "the rule is satisfied -- nothing is wrong to point at");
        }

        [Test]
        public void RefreshingAgainAfterTheRuleIsMet_ClearsThePreviousFlag()
        {
            // A stale blink is worse than none: it would point at a cell that is now correct.
            List<Block> flagged = FlaggedCells(checkpointPair: 2, drawnBy: 1, boardFull: true);
            Assert.AreEqual(1, flagged.Count);

            typeof(GamePlayController)
                .GetField("pairSegments", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(controller, new Dictionary<int, List<List<Block>>>
                {
                    { 2, new List<List<Block>> { new List<Block> { grid[0, 1] } } }
                });

            typeof(GamePlayController)
                .GetMethod("RefreshUnmetCheckpointFeedback", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(controller, new object[] { true });

            Assert.AreEqual(0, flagged.Count, "the flag must be dropped once its colour arrives");
        }
    }
}
