using System.Reflection;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;
using UnityEngine;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers GamePlayController.IsBoardFullyCovered -- the full-board-coverage half of the win
    /// condition checked in OnPointerUp alongside GetPairCompleteCount. Invoked via reflection
    /// since the method is private; everything it reads (grid/gridRow/gridCol, Block.BlockType,
    /// Block.OccupantCount) is public, so no other production code needs touching to test it.
    /// </summary>
    public class BoardCoverageTests
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

        private bool IsBoardFullyCovered(int rows, int cols)
        {
            controller = new GameObject("TestGamePlayController").AddComponent<GamePlayController>();
            controller.grid = grid;
            controller.gridRow = rows;
            controller.gridCol = cols;

            MethodInfo method = typeof(GamePlayController).GetMethod("IsBoardFullyCovered",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "GamePlayController has no private method named " +
                "'IsBoardFullyCovered' -- this test needs updating to match GamePlayController.cs.");
            return (bool)method.Invoke(controller, null);
        }

        [Test]
        public void EveryUsableCellOccupied_ReturnsTrue()
        {
            grid = new Block[1, 2];
            grid[0, 0] = BlockTestHarness.CreateBlock(0, 0);
            grid[0, 1] = BlockTestHarness.CreateBlock(0, 1);
            BlockTestHarness.ClaimDirection(grid[0, 0], Direction.Right, pairId: 1);
            BlockTestHarness.ClaimDirection(grid[0, 1], Direction.Left, pairId: 1);

            Assert.IsTrue(IsBoardFullyCovered(1, 2));
        }

        [Test]
        public void AnEmptyUsableCell_ReturnsFalse()
        {
            grid = new Block[1, 2];
            grid[0, 0] = BlockTestHarness.CreateBlock(0, 0);
            grid[0, 1] = BlockTestHarness.CreateBlock(0, 1);
            BlockTestHarness.ClaimDirection(grid[0, 0], Direction.Right, pairId: 1);
            // grid[0, 1] left empty -- coverage should fail because of it.

            Assert.IsFalse(IsBoardFullyCovered(1, 2));
        }

        [Test]
        public void BlockedCell_IsExcludedFromCoverage()
        {
            grid = new Block[1, 2];
            grid[0, 0] = BlockTestHarness.CreateBlock(0, 0);
            grid[0, 1] = BlockTestHarness.CreateBlock(0, 1);
            BlockTestHarness.SetBlocked(grid[0, 1]);
            BlockTestHarness.ClaimDirection(grid[0, 0], Direction.Right, pairId: 1);
            // grid[0, 1] is Blocked and never occupied -- it must not count against coverage.

            Assert.IsTrue(IsBoardFullyCovered(1, 2));
        }
    }
}
