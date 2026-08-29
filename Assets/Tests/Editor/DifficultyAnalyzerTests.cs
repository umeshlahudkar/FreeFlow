using System.Collections.Generic;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers DifficultyAnalyzer's individual factor computations in isolation (precise, hand-
    /// verified expected values) plus a few end-to-end Analyze() sanity/monotonicity checks.
    /// </summary>
    public class DifficultyAnalyzerTests
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

        // -- ComputeConstrainedCellRatio ------------------------------------------------------

        [Test]
        public void ConstrainedCellRatio_NoMechanics_IsZero()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);

            Assert.AreEqual(0f, DifficultyAnalyzer.ComputeConstrainedCellRatio(grid, 1, 3));
        }

        [Test]
        public void ConstrainedCellRatio_CountsWallsAndMechanics()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);
            BlockTestHarness.SetWall(grid[0, 0], Direction.Right);

            // 1 of the 3 usable cells carries a wall.
            Assert.AreEqual(1f / 3f, DifficultyAnalyzer.ComputeConstrainedCellRatio(grid, 1, 3), 0.001f);
        }

        // -- ComputePathWindingRatio -----------------------------------------------------------

        [Test]
        public void PathWindingRatio_NoSolutions_ReturnsOne()
        {
            Assert.AreEqual(1f, DifficultyAnalyzer.ComputePathWindingRatio(null));
            Assert.AreEqual(1f, DifficultyAnalyzer.ComputePathWindingRatio(new List<PuzzleSolver.PairSolution>()));
        }

        [Test]
        public void PathWindingRatio_DirectPath_IsOne()
        {
            List<PuzzleSolver.PairSolution> solutions = new List<PuzzleSolver.PairSolution>
            {
                new PuzzleSolver.PairSolution
                {
                    PairId = 1,
                    Cells = new List<(int, int)> { (0, 0), (0, 1) }
                }
            };

            Assert.AreEqual(1f, DifficultyAnalyzer.ComputePathWindingRatio(solutions), 0.001f);
        }

        [Test]
        public void PathWindingRatio_DetourPath_IsGreaterThanOne()
        {
            // 4 cells to connect two dots 1 apart (Manhattan distance) -- twice the direct length.
            List<PuzzleSolver.PairSolution> solutions = new List<PuzzleSolver.PairSolution>
            {
                new PuzzleSolver.PairSolution
                {
                    PairId = 1,
                    Cells = new List<(int, int)> { (0, 0), (1, 0), (1, 1), (0, 1) }
                }
            };

            Assert.AreEqual(2f, DifficultyAnalyzer.ComputePathWindingRatio(solutions), 0.001f);
        }

        // -- ComputePathCompetitionRatio --------------------------------------------------------

        [Test]
        public void PathCompetitionRatio_SeparatedPairs_IsZero()
        {
            CreateGrid(1, 5);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 1], pairId: 1);
            BlockTestHarness.SetBlocked(grid[0, 2]);
            BlockTestHarness.SetDot(grid[0, 3], pairId: 2);
            BlockTestHarness.SetDot(grid[0, 4], pairId: 2);

            Assert.AreEqual(0f, DifficultyAnalyzer.ComputePathCompetitionRatio(grid, 1, 5));
        }

        [Test]
        public void PathCompetitionRatio_OverlappingRegions_IsPositive()
        {
            // 3x3, pair1 at the left column's ends, pair2 at the right column's ends -- every
            // filler cell in between is reachable (ignoring direction/occupancy) by both.
            CreateGrid(3, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[2, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 2);
            BlockTestHarness.SetDot(grid[2, 2], pairId: 2);

            float ratio = DifficultyAnalyzer.ComputePathCompetitionRatio(grid, 3, 3);

            // 5 of the 9 cells (every cell except the two OTHER pair's own dots) are reachable by
            // both pairs.
            Assert.AreEqual(5f / 9f, ratio, 0.001f);
        }

        // -- TierFor -----------------------------------------------------------------------------

        [Test]
        public void TierFor_MapsScoreBoundariesCorrectly()
        {
            Assert.AreEqual(DifficultyAnalyzer.DifficultyTier.VeryEasy, DifficultyAnalyzer.TierFor(0f));
            Assert.AreEqual(DifficultyAnalyzer.DifficultyTier.VeryEasy, DifficultyAnalyzer.TierFor(20f));
            Assert.AreEqual(DifficultyAnalyzer.DifficultyTier.Easy, DifficultyAnalyzer.TierFor(21f));
            Assert.AreEqual(DifficultyAnalyzer.DifficultyTier.Easy, DifficultyAnalyzer.TierFor(40f));
            Assert.AreEqual(DifficultyAnalyzer.DifficultyTier.Medium, DifficultyAnalyzer.TierFor(60f));
            Assert.AreEqual(DifficultyAnalyzer.DifficultyTier.Hard, DifficultyAnalyzer.TierFor(75f));
            Assert.AreEqual(DifficultyAnalyzer.DifficultyTier.VeryHard, DifficultyAnalyzer.TierFor(90f));
            Assert.AreEqual(DifficultyAnalyzer.DifficultyTier.Expert, DifficultyAnalyzer.TierFor(91f));
            Assert.AreEqual(DifficultyAnalyzer.DifficultyTier.Expert, DifficultyAnalyzer.TierFor(100f));
        }

        // -- End-to-end Analyze() ----------------------------------------------------------------

        [Test]
        public void Analyze_TrivialTwoCellBoard_IsVeryEasy()
        {
            CreateGrid(1, 2);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 1], pairId: 1);

            PuzzleSolver.SolveResult solveResult = PuzzleSolver.Solve(grid, 1, 2, new PuzzleSolver.SolverOptions(200000, 2));
            DifficultyAnalyzer.DifficultyReport report = DifficultyAnalyzer.Analyze(grid, 1, 2, solveResult);

            Assert.AreEqual(DifficultyAnalyzer.DifficultyTier.VeryEasy, report.Tier);
            Assert.Less(report.Score, 20f);
        }

        [Test]
        public void Analyze_ReportsUniqueSolutionCorrectly()
        {
            // The 2x2 "long way around" board -- provably one solution (see PuzzleSolverTests).
            CreateGrid(2, 2);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 1], pairId: 1);

            PuzzleSolver.SolveResult solveResult = PuzzleSolver.Solve(grid, 2, 2, new PuzzleSolver.SolverOptions(200000, 2));
            DifficultyAnalyzer.DifficultyReport report = DifficultyAnalyzer.Analyze(grid, 2, 2, solveResult);

            Assert.IsTrue(report.SolutionIsUnique);
        }

        [Test]
        public void Analyze_ReportsNonUniqueSolutionCorrectly()
        {
            // 3x3 opposite corners -- provably two solutions (see PuzzleSolverTests).
            CreateGrid(3, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[2, 2], pairId: 1);

            PuzzleSolver.SolveResult solveResult = PuzzleSolver.Solve(grid, 3, 3, new PuzzleSolver.SolverOptions(200000, 2));
            DifficultyAnalyzer.DifficultyReport report = DifficultyAnalyzer.Analyze(grid, 3, 3, solveResult);

            Assert.IsFalse(report.SolutionIsUnique);
        }

        [Test]
        public void Analyze_AddingAMechanic_ScoresHigherThanThePlainEquivalent()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);
            PuzzleSolver.SolveResult plainResult = PuzzleSolver.Solve(grid, 1, 3, new PuzzleSolver.SolverOptions(200000, 2));
            float plainScore = DifficultyAnalyzer.Analyze(grid, 1, 3, plainResult).Score;
            foreach (Block block in grid) { BlockTestHarness.Destroy(block); }

            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);
            BlockTestHarness.SetRuleCell(grid[0, 1], BlockType.Checkpoint, pairId: 1);
            PuzzleSolver.SolveResult mechanicResult = PuzzleSolver.Solve(grid, 1, 3, new PuzzleSolver.SolverOptions(200000, 2));
            float mechanicScore = DifficultyAnalyzer.Analyze(grid, 1, 3, mechanicResult).Score;

            Assert.Greater(mechanicScore, plainScore);
        }

        [Test]
        public void Analyze_ScoreIsAlwaysWithinValidRange()
        {
            CreateGrid(3, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[2, 2], pairId: 1);

            PuzzleSolver.SolveResult solveResult = PuzzleSolver.Solve(grid, 3, 3, new PuzzleSolver.SolverOptions(200000, 2));
            DifficultyAnalyzer.DifficultyReport report = DifficultyAnalyzer.Analyze(grid, 3, 3, solveResult);

            Assert.GreaterOrEqual(report.Score, 0f);
            Assert.LessOrEqual(report.Score, 100f);
        }
    }
}
