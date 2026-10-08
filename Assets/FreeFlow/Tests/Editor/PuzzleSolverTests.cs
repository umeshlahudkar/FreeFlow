using System.Collections.Generic;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers PuzzleSolver.Solve against small, hand-verified boards -- one per mechanic, plus a
    /// couple of cases that specifically probe full-board coverage (a board is only solved once
    /// every usable cell is used, not merely once every pair is connected by its shortest route).
    /// Board sizes are kept tiny on purpose: this is a correctness suite, not a performance one --
    /// see PuzzleSolver's own class doc about full-coverage multi-pair solving being NP-hard in
    /// general.
    /// </summary>
    public class PuzzleSolverTests
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

        private static void AssertFullyCovers(PuzzleSolver.SolveResult result, int usableCellCount)
        {
            Assert.AreEqual(PuzzleSolver.SolveStatus.Solved, result.Status);

            HashSet<(int, int)> covered = new HashSet<(int, int)>();
            foreach (PuzzleSolver.PairSolution solution in result.Solutions)
            {
                foreach ((int, int) cell in solution.Cells) { covered.Add(cell); }
            }
            Assert.AreEqual(usableCellCount, covered.Count,
                "solver reported Solved but its own solution does not cover every usable cell");
        }

        // -- Basics --------------------------------------------------------------------------

        [Test]
        public void StraightLinePair_SolvesAndCoversBothCells()
        {
            CreateGrid(1, 2);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 1], pairId: 1);

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 1, 2);

            AssertFullyCovers(result, usableCellCount: 2);
        }

        [Test]
        public void IntermediateCell_IsIncludedInTheSolvedPath()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 1, 3);

            AssertFullyCovers(result, usableCellCount: 3);
            CollectionAssert.Contains(result.Solutions[0].Cells, (0, 1));
        }

        // -- Full coverage forces a detour, not just the shortest route -----------------------

        [Test]
        public void DirectShortcut_IsRejectedInFavorOfAFullCoverageDetour()
        {
            // A single pair on a 2x3 board must cover all six cells, so the direct one-step
            // connection between two vertically-adjacent dots is not a valid solution -- only the
            // long way around the board is.
            CreateGrid(2, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[1, 0], pairId: 1);

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 2, 3);

            AssertFullyCovers(result, usableCellCount: 6);
            Assert.Greater(result.Solutions[0].Cells.Count, 2,
                "the solution should wind through every cell, not take the one-step shortcut");
        }

        [Test]
        public void ParityMismatch_HasNoFullCoverageSolution()
        {
            // A 2x3 board is checkerboard-colourable with 3 cells of each colour; a path covering
            // all 6 alternates colour every step, so it must start and end on opposite colours.
            // (0,0) and (0,2) are the SAME colour, so no full-coverage Hamiltonian path between
            // them can exist here, regardless of shape.
            CreateGrid(2, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 2, 3);

            Assert.AreEqual(PuzzleSolver.SolveStatus.Unsolvable, result.Status);
        }

        // -- Walls -----------------------------------------------------------------------------

        [Test]
        public void WallBlockingTheOnlyRemainingRoute_IsUnsolvable()
        {
            // 2x2, single pair (0,0)-(0,1): full coverage requires the long way around --
            // (0,0)-(1,0)-(1,1)-(0,1) -- since the direct edge would leave two cells uncovered.
            // Walling the one edge that route needs leaves no full-coverage solution at all.
            CreateGrid(2, 2);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 1], pairId: 1);
            BlockTestHarness.SetWall(grid[1, 0], Direction.Right);
            BlockTestHarness.SetWall(grid[1, 1], Direction.Left);

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 2, 2);

            Assert.AreEqual(PuzzleSolver.SolveStatus.Unsolvable, result.Status);
        }

        [Test]
        public void WallOnTheShortcutEdge_StillSolvedTheLongWay()
        {
            // Same board, but the wall is on the direct (0,0)-(0,1) edge instead -- the edge full
            // coverage would never use anyway, so the long way around still solves it.
            CreateGrid(2, 2);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 1], pairId: 1);
            BlockTestHarness.SetWall(grid[0, 0], Direction.Right);
            BlockTestHarness.SetWall(grid[0, 1], Direction.Left);

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 2, 2);

            AssertFullyCovers(result, usableCellCount: 4);
        }

        // -- One-Way -----------------------------------------------------------------------------

        [Test]
        public void OneWay_MatchingTheOnlyPossibleDirection_IsSolvable()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);
            BlockTestHarness.SetOneWay(grid[0, 1], Direction.Right); // must be entered moving right

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 1, 3);

            AssertFullyCovers(result, usableCellCount: 3);
        }

        [Test]
        public void OneWay_OpposingTheOnlyPossibleDirection_IsUnsolvable()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);
            BlockTestHarness.SetOneWay(grid[0, 1], Direction.Left); // the only route moves right

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 1, 3);

            Assert.AreEqual(PuzzleSolver.SolveStatus.Unsolvable, result.Status);
        }

        // -- Arrow -----------------------------------------------------------------------------

        [Test]
        public void Arrow_MatchingTheOnlyPossibleDirection_IsSolvable()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);
            BlockTestHarness.SetArrow(grid[0, 1], Direction.Right); // forced to keep heading right

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 1, 3);

            AssertFullyCovers(result, usableCellCount: 3);
        }

        [Test]
        public void Arrow_ForcingABounceBackIntoItsOwnPath_IsUnsolvable()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);
            BlockTestHarness.SetArrow(grid[0, 1], Direction.Left); // forces a walk straight back

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 1, 3);

            Assert.AreEqual(PuzzleSolver.SolveStatus.Unsolvable, result.Status);
        }

        // -- Forbidden-for-pair ------------------------------------------------------------------

        [Test]
        public void ForbiddenForPair_BlockingTheOnlyRoute_IsUnsolvable()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);
            BlockTestHarness.SetRuleCell(grid[0, 1], BlockType.ForbiddenForPair, pairId: 1);

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 1, 3);

            Assert.AreEqual(PuzzleSolver.SolveStatus.Unsolvable, result.Status);
        }

        [Test]
        public void ForbiddenForPair_NamingADifferentPair_IsSolvable()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);
            BlockTestHarness.SetRuleCell(grid[0, 1], BlockType.ForbiddenForPair, pairId: 2);

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 1, 3);

            AssertFullyCovers(result, usableCellCount: 3);
        }

        // -- Bridge ----------------------------------------------------------------------------

        [Test]
        public void Bridge_LetsTwoPairsCrossOnDifferentAxes()
        {
            // A plus-shape: the four corners are Blocked (excluded from coverage), leaving a
            // horizontal pair and a vertical pair to cross through the shared centre cell.
            CreateGrid(3, 3);
            BlockTestHarness.SetBlocked(grid[0, 0]);
            BlockTestHarness.SetBlocked(grid[0, 2]);
            BlockTestHarness.SetBlocked(grid[2, 0]);
            BlockTestHarness.SetBlocked(grid[2, 2]);
            BlockTestHarness.SetBridge(grid[1, 1]);

            BlockTestHarness.SetDot(grid[1, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[1, 2], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 1], pairId: 2);
            BlockTestHarness.SetDot(grid[2, 1], pairId: 2);

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 3, 3);

            AssertFullyCovers(result, usableCellCount: 5);
            CollectionAssert.Contains(FindSolutionFor(result, pairId: 1).Cells, (1, 1));
            CollectionAssert.Contains(FindSolutionFor(result, pairId: 2).Cells, (1, 1));
        }

        // -- Shared destination ------------------------------------------------------------------

        [Test]
        public void SharedDestination_BothPairsConvergeOnTheSameCell()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 1], pairId: 1, secondPairId: 2); // shared destination
            BlockTestHarness.SetDot(grid[0, 2], pairId: 2);

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 1, 3);

            AssertFullyCovers(result, usableCellCount: 3);
            CollectionAssert.Contains(FindSolutionFor(result, pairId: 1).Cells, (0, 1));
            CollectionAssert.Contains(FindSolutionFor(result, pairId: 2).Cells, (0, 1));
        }

        // -- Step budget -------------------------------------------------------------------------

        [Test]
        public void InsufficientStepBudget_ReturnsInconclusiveRatherThanUnsolvable()
        {
            CreateGrid(2, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[1, 0], pairId: 1);

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 2, 3, new PuzzleSolver.SolverOptions(1));

            Assert.AreEqual(PuzzleSolver.SolveStatus.Inconclusive, result.Status);
        }

        // -- Solution uniqueness (MaxSolutionsToFind > 1) ---------------------------------------

        [Test]
        public void ProvablyUniqueSolution_ExhaustsWithExactlyOneFound()
        {
            // A 2x2 board where full coverage forces the long way around the 4-cycle -- the only
            // Hamiltonian path between these two dots -- so asking for up to 2 solutions should
            // still only turn up 1, with the search having genuinely exhausted every possibility.
            CreateGrid(2, 2);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 1], pairId: 1);

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 2, 2, new PuzzleSolver.SolverOptions(200000, 2));

            Assert.AreEqual(PuzzleSolver.SolveStatus.Solved, result.Status);
            Assert.AreEqual(1, result.SolutionsFound);
            Assert.IsTrue(result.SearchExhausted);
        }

        [Test]
        public void MultipleSolutions_FindsBothWithinTheCap()
        {
            // A 3x3 board with a single pair at opposite corners has exactly two full-coverage
            // Hamiltonian paths (mirror images of each other), so asking for up to 2 should find
            // both, and the search should still be able to report it exhausted the possibilities.
            CreateGrid(3, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[2, 2], pairId: 1);

            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, 3, 3, new PuzzleSolver.SolverOptions(200000, 2));

            Assert.AreEqual(PuzzleSolver.SolveStatus.Solved, result.Status);
            Assert.AreEqual(2, result.SolutionsFound);
            Assert.IsTrue(result.SearchExhausted);
        }

        private static PuzzleSolver.PairSolution FindSolutionFor(PuzzleSolver.SolveResult result, int pairId)
        {
            foreach (PuzzleSolver.PairSolution solution in result.Solutions)
            {
                if (solution.PairId == pairId) { return solution; }
            }
            Assert.Fail("no solution recorded for pair " + pairId);
            return null;
        }
    }
}
