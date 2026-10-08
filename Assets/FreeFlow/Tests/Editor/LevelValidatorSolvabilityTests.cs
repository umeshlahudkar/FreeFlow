using System.Text.RegularExpressions;
using FreeFlow.GamePlay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers LevelValidator.ValidateSolvability -- the solver-backed check layered on top of
    /// Validate's structural checks. Kept in its own file since it exercises a different
    /// dependency (PuzzleSolver) than the rest of LevelValidatorTests.
    /// </summary>
    public class LevelValidatorSolvabilityTests
    {
        private Block[,] grid;

        [TearDown]
        public void TearDown()
        {
            if (grid == null) { return; }
            foreach (Block block in grid) { BlockTestHarness.Destroy(block); }
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

        [Test]
        public void SolvableBoard_ReportsSolvedAndLogsNothing()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);

            PuzzleSolver.SolveResult result = LevelValidator.ValidateSolvability(grid, 1, 3);

            Assert.AreEqual(PuzzleSolver.SolveStatus.Solved, result.Status);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void UnsolvableBoard_LogsErrorAndReportsUnsolvable()
        {
            // Parity-mismatched 2x3 board (see PuzzleSolverTests.ParityMismatch...): no
            // full-coverage Hamiltonian path can exist between these two same-colour dots.
            CreateGrid(2, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);

            LogAssert.Expect(LogType.Error, new Regex("no full-coverage solution"));
            PuzzleSolver.SolveResult result = LevelValidator.ValidateSolvability(grid, 2, 3);

            Assert.AreEqual(PuzzleSolver.SolveStatus.Unsolvable, result.Status);
        }

        [Test]
        public void InsufficientBudget_LogsErrorAndReportsInconclusive()
        {
            CreateGrid(2, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[1, 0], pairId: 1);

            LogAssert.Expect(LogType.Error, new Regex("could not be determined"));
            PuzzleSolver.SolveResult result = LevelValidator.ValidateSolvability(
                grid, 2, 3, new PuzzleSolver.SolverOptions(1));

            Assert.AreEqual(PuzzleSolver.SolveStatus.Inconclusive, result.Status);
        }
    }
}
