using System;
using System.Collections.Generic;
using FreeFlow.Enums;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Turns a solved board into a normalized 0-100 difficulty score (spec §13), built from
    /// several independent factors rather than just grid size or colour count: how much a
    /// solution had to detour past the direct route between its dots, how often the search that
    /// found it had a genuine choice versus a forced move, how often it hit a dead end, how much
    /// of the board different pairs could both potentially reach, how constrained the board is by
    /// walls/mechanics, and whether the solution is unique. Each factor is normalized to 0-1 and
    /// combined with fixed, documented weights.
    ///
    /// Deliberately takes an already-computed PuzzleSolver.SolveResult rather than solving again:
    /// LevelGenerator (and anything else calling this) has typically already solved the board via
    /// LevelValidator.ValidateSolvability, and re-solving here would double that cost for nothing.
    ///
    /// The weights below are a documented first pass, not a validated curve -- tuning them against
    /// real playtesting data is expected follow-up work, same spirit as LevelGenerator's
    /// straightness-bias difficulty proxy it replaces for anything that calls this instead.
    /// </summary>
    public static class DifficultyAnalyzer
    {
        public enum DifficultyTier { VeryEasy, Easy, Medium, Hard, VeryHard, Expert }

        public sealed class DifficultyReport
        {
            public float Score; // 0-100
            public DifficultyTier Tier;

            // Raw inputs behind the score, exposed for tuning/debugging rather than gameplay use.
            public int UsableCells;
            public int ColorCount;
            public float ConstrainedCellRatio;
            public float PathWindingRatio;
            public float DecisionDensity;
            public int DeadEndCount;
            public int SolverStepsTaken;
            public int SolutionsFound;
            public bool SolutionIsUnique;
            public float PathCompetitionRatio;
        }

        private static readonly Direction[] Directions =
        {
            Direction.Left, Direction.Right, Direction.Up, Direction.Down
        };

        // A rough, table-driven "how much does this mechanic add to read/solve" weight -- not
        // derived from anything empirical, just an ordering: Blocked barely changes reasoning
        // (it's just a hole), Bridge asks the player to track two lanes at once and is the
        // heaviest, the rest sit in between.
        private static readonly Dictionary<BlockType, float> MechanicWeights = new Dictionary<BlockType, float>
        {
            { BlockType.Blocked, 1f },
            { BlockType.Checkpoint, 2f },
            { BlockType.OneWay, 2f },
            { BlockType.ForbiddenForPair, 2f },
            { BlockType.AllowedForPairs, 2f },
            { BlockType.Arrow, 2.5f },
            { BlockType.Bridge, 4f }
        };

        private const float SharedDestinationWeight = 1.5f;

        public static DifficultyReport Analyze(Block[,] grid, int rowCount, int colCount,
            PuzzleSolver.SolveResult solveResult)
        {
            int usableCells = CountUsableCells(grid, rowCount, colCount);
            int colorCount = solveResult.Solutions?.Count ?? 0;

            float constrainedRatio = ComputeConstrainedCellRatio(grid, rowCount, colCount);
            float mechanicWeightSum = SumMechanicWeights(grid, rowCount, colCount);
            float windingRatio = ComputePathWindingRatio(solveResult.Solutions);
            float competitionRatio = ComputePathCompetitionRatio(grid, rowCount, colCount);

            float decisionDensity = usableCells > 0 ? (float)solveResult.DecisionPointCount / usableCells : 0f;
            bool solutionIsUnique = solveResult.SolutionsFound == 1 && solveResult.SearchExhausted;

            int gridCells = rowCount * colCount;
            float gridSizeFactor = Clamp01((gridCells - 16f) / (144f - 16f)); // 4x4 .. 12x12
            float colorFactor = Clamp01((colorCount - 2f) / (12f - 2f));
            float windingFactor = Clamp01((windingRatio - 1f) / 3f); // 1.0 = direct route
            float decisionFactor = Clamp01(decisionDensity / 0.5f);
            float deadEndFactor = Clamp01(solveResult.DeadEndCount / (usableCells * 0.5f + 1f));
            float mechanicFactor = Clamp01(mechanicWeightSum / (usableCells * 2f + 1f));
            float searchEffortFactor = Clamp01((float)Math.Log(solveResult.StepsTaken + 1) / (float)Math.Log(50000));
            float uniquenessFactor = solveResult.SolutionsFound == 0 ? 0.6f // unknown/never solved
                : solutionIsUnique ? 1f
                : solveResult.SolutionsFound >= 2 ? 0.3f
                : 0.6f; // found once but search didn't exhaust -- uniqueness genuinely unknown

            float score01 =
                0.10f * gridSizeFactor +
                0.10f * colorFactor +
                0.15f * windingFactor +
                0.15f * decisionFactor +
                0.10f * deadEndFactor +
                0.15f * mechanicFactor +
                0.10f * constrainedRatio +
                0.05f * competitionRatio +
                0.05f * searchEffortFactor +
                0.05f * uniquenessFactor;

            float score = Clamp01(score01) * 100f;

            return new DifficultyReport
            {
                Score = score,
                Tier = TierFor(score),
                UsableCells = usableCells,
                ColorCount = colorCount,
                ConstrainedCellRatio = constrainedRatio,
                PathWindingRatio = windingRatio,
                DecisionDensity = decisionDensity,
                DeadEndCount = solveResult.DeadEndCount,
                SolverStepsTaken = solveResult.StepsTaken,
                SolutionsFound = solveResult.SolutionsFound,
                SolutionIsUnique = solutionIsUnique,
                PathCompetitionRatio = competitionRatio
            };
        }

        public static DifficultyTier TierFor(float score)
        {
            if (score <= 20f) { return DifficultyTier.VeryEasy; }
            if (score <= 40f) { return DifficultyTier.Easy; }
            if (score <= 60f) { return DifficultyTier.Medium; }
            if (score <= 75f) { return DifficultyTier.Hard; }
            if (score <= 90f) { return DifficultyTier.VeryHard; }
            return DifficultyTier.Expert;
        }

        /// <summary>Fraction of usable cells that carry a wall on any edge or a non-Normal
        /// BlockType -- how much of the board is "under a rule" rather than plain.</summary>
        public static float ComputeConstrainedCellRatio(Block[,] grid, int rowCount, int colCount)
        {
            int usable = 0;
            int constrained = 0;

            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < colCount; c++)
                {
                    Block cell = grid[r, c];
                    if (cell == null || cell.BlockType == BlockType.Blocked) { continue; }

                    usable++;

                    bool hasWall = cell.HasWall(Direction.Left) || cell.HasWall(Direction.Right)
                        || cell.HasWall(Direction.Up) || cell.HasWall(Direction.Down);
                    bool hasMechanic = cell.BlockType != BlockType.Normal;

                    if (hasWall || hasMechanic) { constrained++; }
                }
            }

            return usable > 0 ? (float)constrained / usable : 0f;
        }

        /// <summary>
        /// Average, over every pair, of solved-path-length divided by (Manhattan distance between
        /// its dots + 1) -- 1.0 means every pair went the direct way, higher means solving this
        /// board required real detouring to reach full coverage.
        /// </summary>
        public static float ComputePathWindingRatio(List<PuzzleSolver.PairSolution> solutions)
        {
            if (solutions == null || solutions.Count == 0) { return 1f; }

            float sum = 0f;
            int count = 0;
            for (int i = 0; i < solutions.Count; i++)
            {
                List<(int Row, int Col)> cells = solutions[i].Cells;
                if (cells == null || cells.Count < 2) { continue; }

                (int Row, int Col) a = cells[0];
                (int Row, int Col) b = cells[cells.Count - 1];
                int manhattan = Math.Abs(a.Row - b.Row) + Math.Abs(a.Col - b.Col);

                sum += (float)cells.Count / (manhattan + 1);
                count++;
            }

            return count > 0 ? sum / count : 1f;
        }

        /// <summary>
        /// Fraction of usable cells that more than one pair could potentially reach on its own
        /// (walls/blocked/forbidden respected, but ignoring what any OTHER pair has claimed) --
        /// how much pairs have to compete for the same territory rather than staying out of each
        /// other's way. A purely structural board metric, independent of any particular solve.
        /// </summary>
        public static float ComputePathCompetitionRatio(Block[,] grid, int rowCount, int colCount)
        {
            Dictionary<int, List<Block>> dots = BoardTopology.CollectDots(grid, rowCount, colCount);
            if (dots.Count == 0) { return 0f; }

            int[,] reachCount = new int[rowCount, colCount];
            foreach (KeyValuePair<int, List<Block>> kv in dots)
            {
                if (kv.Value.Count != 2) { continue; }

                bool[,] seen = FloodIgnoringDirection(grid, rowCount, colCount, kv.Value[0], kv.Key);
                for (int r = 0; r < rowCount; r++)
                {
                    for (int c = 0; c < colCount; c++)
                    {
                        if (seen[r, c]) { reachCount[r, c]++; }
                    }
                }
            }

            int usable = 0;
            int contested = 0;
            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < colCount; c++)
                {
                    Block cell = grid[r, c];
                    if (cell == null || cell.BlockType == BlockType.Blocked) { continue; }

                    usable++;
                    if (reachCount[r, c] >= 2) { contested++; }
                }
            }

            return usable > 0 ? (float)contested / usable : 0f;
        }

        private static bool[,] FloodIgnoringDirection(Block[,] grid, int rowCount, int colCount, Block start,
            int pairId)
        {
            bool[,] seen = new bool[rowCount, colCount];
            seen[start.Row_ID, start.Coloum_ID] = true;

            Queue<Block> queue = new Queue<Block>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                Block current = queue.Dequeue();

                for (int i = 0; i < Directions.Length; i++)
                {
                    Direction dir = Directions[i];
                    Block next = BoardTopology.Neighbor(grid, rowCount, colCount, current, dir);
                    if (next == null || seen[next.Row_ID, next.Coloum_ID]) { continue; }
                    if (current.HasWall(dir) || next.HasWall(BoardTopology.Opposite(dir))) { continue; }
                    if (next.BlockType == BlockType.Blocked) { continue; }
                    if (!next.CanEnter(pairId)) { continue; }
                    if (next.IsPairBlock && !next.IsDotFor(pairId)) { continue; }

                    seen[next.Row_ID, next.Coloum_ID] = true;
                    queue.Enqueue(next);
                }
            }

            return seen;
        }

        private static int CountUsableCells(Block[,] grid, int rowCount, int colCount)
        {
            int count = 0;
            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < colCount; c++)
                {
                    Block cell = grid[r, c];
                    if (cell != null && cell.BlockType != BlockType.Blocked) { count++; }
                }
            }
            return count;
        }

        private static float SumMechanicWeights(Block[,] grid, int rowCount, int colCount)
        {
            float sum = 0f;
            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < colCount; c++)
                {
                    Block cell = grid[r, c];
                    if (cell == null) { continue; }

                    if (MechanicWeights.TryGetValue(cell.BlockType, out float weight)) { sum += weight; }
                    if (cell.IsSharedGoal) { sum += SharedDestinationWeight; }
                }
            }
            return sum;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) { return 0f; }
            if (value > 1f) { return 1f; }
            return value;
        }
    }
}
