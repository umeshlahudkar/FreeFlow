using System.Collections.Generic;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers SolverOptions.AllowPartialCoverage -- the switch that lets LevelGenerator ask the
    /// opposite question from gameplay: not "can this board be solved?" but "can a player connect
    /// every pair and STILL be left staring at empty cells?" That state is the one real players
    /// reported as the game being broken (all pairs joined, board not full, no feedback), and
    /// LevelGenerator.EveryPairingCoversTheBoard rejects any level where it is reachable.
    ///
    /// The board below is that bug at its smallest: a 2x2 with a single pair on the top row. The
    /// direct connection joins the pair using 2 of the 4 cells; the full-coverage solution has to
    /// go the long way round. A generated level must never look like this.
    /// </summary>
    public class PartialCoverageSolveTests
    {
        private Block[,] grid;

        [TearDown]
        public void TearDown()
        {
            if (grid == null) { return; }
            foreach (Block block in grid) { BlockTestHarness.Destroy(block); }
            grid = null;
        }

        /// <summary>2x2, one pair at (0,0)-(0,1). Direct route uses 2 cells; the way round uses 4.</summary>
        private void CreateShortcutBoard()
        {
            grid = new Block[2, 2];
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    grid[i, j] = BlockTestHarness.CreateBlock(i, j);
                }
            }

            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 1], pairId: 1);
        }

        private static int CellsCovered(List<PuzzleSolver.PairSolution> arrangement)
        {
            HashSet<(int, int)> covered = new HashSet<(int, int)>();
            foreach (PuzzleSolver.PairSolution pair in arrangement)
            {
                foreach ((int, int) cell in pair.Cells) { covered.Add(cell); }
            }
            return covered.Count;
        }

        [Test]
        public void ByDefault_PartialCoverageArrangementsAreNotAccepted()
        {
            CreateShortcutBoard();

            // Default options: this game's real win condition, full coverage required.
            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 2, 2,
                new PuzzleSolver.SolverOptions(50000, 5));

            Assert.AreEqual(PuzzleSolver.SolveStatus.Solved, result.Status);
            Assert.IsTrue(result.SearchExhausted);
            Assert.AreEqual(1, result.SolutionsFound, "only the long way round covers this board");

            foreach (List<PuzzleSolver.PairSolution> arrangement in result.AllSolutions)
            {
                Assert.AreEqual(4, CellsCovered(arrangement));
            }
        }

        [Test]
        public void WithAllowPartialCoverage_TheShortcutArrangementIsAlsoFound()
        {
            CreateShortcutBoard();

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 2, 2,
                new PuzzleSolver.SolverOptions(50000, 5, allowPartialCoverage: true));

            Assert.AreEqual(PuzzleSolver.SolveStatus.Solved, result.Status);
            Assert.IsTrue(result.SearchExhausted);
            Assert.AreEqual(2, result.SolutionsFound,
                "both the 2-cell shortcut and the 4-cell full-coverage route connect the pair");

            bool foundShortcut = false;
            foreach (List<PuzzleSolver.PairSolution> arrangement in result.AllSolutions)
            {
                if (CellsCovered(arrangement) < 4) { foundShortcut = true; }
            }
            Assert.IsTrue(foundShortcut,
                "this is the exact state the coverage rule exists to detect -- it must be visible here");
        }

        [Test]
        public void AllSolutions_IsPopulatedAndItsHeadMatchesSolutions()
        {
            CreateShortcutBoard();

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 2, 2,
                new PuzzleSolver.SolverOptions(50000, 5, allowPartialCoverage: true));

            Assert.AreEqual(result.SolutionsFound, result.AllSolutions.Count);
            Assert.AreSame(result.Solutions, result.AllSolutions[0]);
        }
    }
}
