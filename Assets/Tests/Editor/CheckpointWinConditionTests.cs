using System.Collections.Generic;
using System.Reflection;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;
using UnityEngine;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers GamePlayController.IsPairSatisfied's checkpoint half -- the rule that a pair is only
    /// complete when every Checkpoint naming it lies on that pair's OWN drawn path.
    ///
    /// Sibling to <see cref="BoardCoverageTests"/>, which covers the other half of the same win
    /// condition (`GetPairCompleteCount() >= CurrentLevelGoal && IsBoardFullyCovered()`). Coverage
    /// was tested; this half was not, which meant the only thing standing behind "the level cannot
    /// end with a checkpoint held by the wrong colour" was reading the code.
    ///
    /// That gap matters more here than it looks. A Checkpoint is the one mechanic that does not
    /// police entry -- Block.CanEnter does not mention it -- so any colour may sit on the cell and
    /// nothing refuses the move. The rule exists only at completion time. If this check were ever
    /// dropped, every board would still play normally and levels would simply finish early, which
    /// no other test would catch.
    /// </summary>
    public class CheckpointWinConditionTests
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
        /// A 1x3 board: dot, middle cell, dot. The middle cell is the interesting one.
        /// `drawnBy` is the pair whose segment is recorded as running through all three cells.
        /// </summary>
        private bool IsPairSatisfied(int pairId, BlockType middleType, int middleNamedPair, int drawnBy)
        {
            grid = new Block[1, 3];
            grid[0, 0] = BlockTestHarness.CreateBlock(0, 0);
            grid[0, 1] = BlockTestHarness.CreateBlock(0, 1);
            grid[0, 2] = BlockTestHarness.CreateBlock(0, 2);

            BlockTestHarness.SetDot(grid[0, 0], drawnBy);
            BlockTestHarness.SetDot(grid[0, 2], drawnBy);
            if (middleType == BlockType.Checkpoint)
            {
                BlockTestHarness.SetRuleCell(grid[0, 1], BlockType.Checkpoint, middleNamedPair);
            }

            controller = new GameObject("TestGamePlayController").AddComponent<GamePlayController>();
            controller.grid = grid;
            controller.gridRow = 1;
            controller.gridCol = 3;

            // The drawn path: whoever drew it owns all three cells end to end.
            var segments = new Dictionary<int, List<List<Block>>>
            {
                { drawnBy, new List<List<Block>> { new List<Block> { grid[0, 0], grid[0, 1], grid[0, 2] } } }
            };
            FieldInfo field = typeof(GamePlayController).GetField("pairSegments",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "GamePlayController has no field 'pairSegments' -- update this test.");
            field.SetValue(controller, segments);

            MethodInfo method = typeof(GamePlayController).GetMethod("IsPairSatisfied",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "GamePlayController has no private method 'IsPairSatisfied' -- "
                + "update this test to match GamePlayController.cs.");
            return (bool)method.Invoke(controller, new object[] { pairId });
        }

        [Test]
        public void CheckpointOnItsOwnPairsPath_IsSatisfied()
        {
            Assert.IsTrue(IsPairSatisfied(pairId: 1, BlockType.Checkpoint, middleNamedPair: 1, drawnBy: 1),
                "pair 1 connects its dots and its own checkpoint lies on that path");
        }

        [Test]
        public void CheckpointHeldByAnotherColour_IsNotSatisfied()
        {
            // The reported scenario: every cell occupied and the pair's dots joined, but the
            // checkpoint belongs to pair 2 while pair 1's path is what runs through it.
            // Pair 2 has drawn nothing, so its checkpoint is unmet and it cannot be complete --
            // which is what keeps GetPairCompleteCount below the goal and the level unfinished.
            Assert.IsFalse(IsPairSatisfied(pairId: 2, BlockType.Checkpoint, middleNamedPair: 2, drawnBy: 1),
                "a checkpoint occupied by a DIFFERENT colour must not count as satisfied -- "
                + "otherwise the level ends with its headline rule ignored");
        }

        [Test]
        public void PairWithNoCheckpointOfItsOwn_IsUnaffected()
        {
            // Pair 1 joins its dots; the checkpoint on its route names pair 2, not pair 1.
            // Pair 1 is complete regardless -- a checkpoint constrains only the pair it names.
            Assert.IsTrue(IsPairSatisfied(pairId: 1, BlockType.Checkpoint, middleNamedPair: 2, drawnBy: 1),
                "a checkpoint naming another pair must not block this one");
        }

        [Test]
        public void ConnectedPairWithNoCheckpointsAtAll_IsSatisfied()
        {
            // Baseline, so a failure above is read as "the checkpoint rule broke" rather than
            // "the connectivity check broke".
            Assert.IsTrue(IsPairSatisfied(pairId: 1, BlockType.Normal, middleNamedPair: 0, drawnBy: 1));
        }
    }
}
