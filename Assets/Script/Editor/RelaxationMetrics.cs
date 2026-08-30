using System.Collections.Generic;
using FreeFlow.Enums;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Rates a board by RELAXING it -- taking a constraint away and watching what happens to the
    /// solution -- rather than by modelling how a person reasons about it.
    ///
    /// <b>Why this exists alongside HumanSolver.</b> Pelánek's evaluation is explicit that his
    /// constraint-propagation model is easy to build for Sudoku only because Sudoku's rules are
    /// simple, and that "for similar problems like Nurikabe it can be difficult to formulate
    /// suitable constraint propagation rules". Flow is the Nurikabe case: our technique list is
    /// three rules deep and openly missing parity, cell budget and corridor priority. Relaxation
    /// is his portable alternative -- it models nothing about human reasoning, needs no technique
    /// library, and works on any constraint problem.
    ///
    /// <b>And on the puzzle most like ours, it wins.</b> On Sudoku, relaxation (fixedness, r =
    /// 0.56-0.61) trails the propagation model (0.68-0.83). On NURIKABE -- a region-and-
    /// connectivity puzzle, structurally far closer to Flow than Sudoku is -- relaxation reaches
    /// r = 0.9 and BEATS the propagation model's 0.8. That reversal is the whole reason this class
    /// is worth its runtime.
    ///
    /// <b>The relaxation we use</b> is to delete one colour: take both its dots away and let the
    /// remaining colours cover the board. This is the natural analogue of removing Sudoku givens --
    /// it is the board's own constraint, not an incidental one -- and it needs no solver changes,
    /// only a rebuilt <see cref="LevelData"/>.
    ///
    /// Two numbers come out, and both are reported raw because <b>the sign of each has to be
    /// settled by measurement, not assumed</b>. The obvious reading -- "cells that stay put were
    /// over-determined, so a board of them is easy" -- is plausible for fixedness and genuinely
    /// ambiguous for solution growth, where "the board leaned on that clue" and "the board holds
    /// itself together without it" are both defensible. Calibrate against the Flow Free reference
    /// board before either is given a weight in <see cref="DifficultyModel"/>.
    /// </summary>
    public static class RelaxationMetrics
    {
        public sealed class Result
        {
            /// <summary>Fraction of cells that keep the colour they had in the intended solution,
            /// averaged over every one-colour-removed variant. 0..1. High means the board barely
            /// notices losing a colour: the rest of it was already pinned.</summary>
            public float Fixedness;

            /// <summary>Mean number of solutions a one-colour-removed board admits, capped at
            /// <c>maxSolutions</c>. 1.0 means the board is STILL uniquely solved with a colour
            /// missing.</summary>
            public float SolutionGrowth;

            /// <summary>Variants that could be solved at all. Fixedness averages over these.</summary>
            public int Variants;

            /// <summary>Variants with no solution -- removing that colour makes full coverage
            /// impossible. A high count means the colours are load-bearing individually, which is
            /// the opposite of the redundancy that makes a board fall apart on its own.</summary>
            public int UnsolvableVariants;

            /// <summary>Variants abandoned on the step budget; their solution counts are not
            /// included, so a large number here makes the other figures thin.</summary>
            public int InconclusiveVariants;

            public override string ToString()
            {
                return "fixedness=" + Fixedness.ToString("0.000")
                    + "  growth=" + SolutionGrowth.ToString("0.00")
                    + "  variants=" + Variants
                    + "  unsolvable=" + UnsolvableVariants
                    + "  inconclusive=" + InconclusiveVariants;
            }
        }

        /// <summary>
        /// Measures <paramref name="data"/>. Costs one solve per colour, so a 7x7 with six colours
        /// is six solves -- cheap enough to run over a whole generation pool.
        /// </summary>
        public static Result Measure(LevelData data, int maxSolutions = 8, int maxSteps = 2000000)
        {
            Result result = new Result();

            int[,] reference = SolveToColourMap(data, out int rows, out int cols);
            if (reference == null) { return result; }

            List<int> pairIds = CollectPairIds(data);
            int fixednessSamples = 0;
            float fixednessSum = 0f;
            int growthSamples = 0;
            float growthSum = 0f;

            for (int i = 0; i < pairIds.Count; i++)
            {
                int removed = pairIds[i];
                LevelData relaxed = WithoutPair(data, removed);

                Block[,] grid = LevelGenerator.BuildBlockGrid(relaxed, out int r, out int c);
                PuzzleSolver.SolveResult solved;
                try
                {
                    solved = PuzzleSolver.Solve(grid, r, c,
                        new PuzzleSolver.SolverOptions(maxSteps, maxSolutions));

                    if (solved.Status == PuzzleSolver.SolveStatus.Solved && solved.Solutions != null)
                    {
                        growthSum += solved.SolutionsFound;
                        growthSamples++;
                        result.Variants++;

                        fixednessSum += MatchFraction(reference, solved.Solutions, removed, rows, cols);
                        fixednessSamples++;
                    }
                    else if (solved.Status == PuzzleSolver.SolveStatus.Unsolvable)
                    {
                        result.UnsolvableVariants++;
                        // A colour whose removal makes the board unsolvable is maximally
                        // load-bearing; counting it as zero growth keeps the mean honest rather
                        // than silently dropping the hardest case.
                        growthSum += 0f;
                        growthSamples++;
                    }
                    else
                    {
                        result.InconclusiveVariants++;
                    }
                }
                finally { LevelGenerator.DestroyBlockGrid(grid); }
            }

            result.Fixedness = fixednessSamples > 0 ? fixednessSum / fixednessSamples : 0f;
            result.SolutionGrowth = growthSamples > 0 ? growthSum / growthSamples : 0f;
            return result;
        }

        /// <summary>
        /// How much of the intended solution survives, ignoring the cells the removed colour used
        /// -- those cannot match by construction, and counting them would just scale every board by
        /// its own colour count.
        /// </summary>
        private static float MatchFraction(int[,] reference, List<PuzzleSolver.PairSolution> solution,
            int removedPair, int rows, int cols)
        {
            int[,] relaxed = new int[rows, cols];
            for (int i = 0; i < solution.Count; i++)
            {
                List<(int Row, int Col)> cells = solution[i].Cells;
                for (int j = 0; j < cells.Count; j++)
                {
                    relaxed[cells[j].Row, cells[j].Col] = solution[i].PairId;
                }
            }

            int compared = 0, matched = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int was = reference[r, c];
                    if (was == 0 || was == removedPair) { continue; }
                    compared++;
                    if (relaxed[r, c] == was) { matched++; }
                }
            }
            return compared > 0 ? matched / (float)compared : 0f;
        }

        private static int[,] SolveToColourMap(LevelData data, out int rows, out int cols)
        {
            Block[,] grid = LevelGenerator.BuildBlockGrid(data, out rows, out cols);
            try
            {
                PuzzleSolver.SolveResult solved = PuzzleSolver.Solve(grid, rows, cols,
                    new PuzzleSolver.SolverOptions(2000000, 1));
                if (solved.Status != PuzzleSolver.SolveStatus.Solved || solved.Solutions == null)
                {
                    return null;
                }

                int[,] map = new int[rows, cols];
                for (int i = 0; i < solved.Solutions.Count; i++)
                {
                    List<(int Row, int Col)> cells = solved.Solutions[i].Cells;
                    for (int j = 0; j < cells.Count; j++)
                    {
                        map[cells[j].Row, cells[j].Col] = solved.Solutions[i].PairId;
                    }
                }
                return map;
            }
            finally { LevelGenerator.DestroyBlockGrid(grid); }
        }

        /// <summary>
        /// Every colour on the board, by the id the game will actually give it.
        /// </summary>
        /// <remarks>
        /// A cell's PRIMARY pair id is its colour cast to an int -- <see cref="LevelGenerator"/>
        /// builds it that way to match BoardGenerator's own colour fallback, and the LevelData
        /// <c>pairId</c> column is not read for it at all. Reading that column here instead is what
        /// made the first version of this class silently measure nothing: on levels whose pairId
        /// column is empty it found no colours, removed none, and reported a confident zero for
        /// every metric. The extra dot identities (second/third/fourth) ARE read as written.
        /// </remarks>
        private static List<int> CollectPairIds(LevelData data)
        {
            HashSet<int> seen = new HashSet<int>();
            List<int> ids = new List<int>();
            int size = (int)data.gridSize;

            for (int r = 0; r < size; r++)
            {
                GridRow row = data.gridRows[r];
                for (int c = 0; c < size; c++)
                {
                    if (row.coloum != null && c < row.coloum.Length && row.coloum[c] != PairColorType.None)
                    {
                        int id = (int)row.coloum[c];
                        if (seen.Add(id)) { ids.Add(id); }
                    }
                    AddIfPair(row.secondPairId, c, seen, ids);
                    AddIfPair(row.thirdPairId, c, seen, ids);
                    AddIfPair(row.fourthPairId, c, seen, ids);
                }
            }
            return ids;
        }

        private static void AddIfPair(int[] column, int index, HashSet<int> seen, List<int> ids)
        {
            if (column == null || index >= column.Length) { return; }
            int id = column[index];
            if (id != 0 && seen.Add(id)) { ids.Add(id); }
        }

        /// <summary>
        /// A copy of <paramref name="data"/> with every trace of one colour removed. A cell that was
        /// a shared destination keeps its other identities -- only the named pair is dropped -- so
        /// relaxing a shared-goal board does not silently delete the mechanic as well.
        /// </summary>
        private static LevelData WithoutPair(LevelData data, int pairId)
        {
            int size = (int)data.gridSize;
            LevelData copy = data;
            copy.gridRows = new GridRow[size];

            for (int r = 0; r < size; r++)
            {
                GridRow src = data.gridRows[r];
                GridRow row = new GridRow
                {
                    coloum = (PairColorType[])src.coloum?.Clone(),
                    pairId = (int[])src.pairId?.Clone(),
                    blockType = (BlockType[])src.blockType?.Clone(),
                    wallMask = (int[])src.wallMask?.Clone(),
                    requiredEntryDirection = (Direction[])src.requiredEntryDirection?.Clone(),
                    forcedExitDirection = (Direction[])src.forcedExitDirection?.Clone(),
                    secondPairId = (int[])src.secondPairId?.Clone(),
                    thirdPairId = (int[])src.thirdPairId?.Clone(),
                    fourthPairId = (int[])src.fourthPairId?.Clone()
                };

                for (int c = 0; c < size; c++)
                {
                    Compact(row, c, pairId);
                }
                copy.gridRows[r] = row;
            }

            copy.pairCount = data.pairCount - 1;
            return copy;
        }

        /// <summary>
        /// Drops <paramref name="pairId"/> from one cell's four identity slots and closes the gap,
        /// because LevelData's own contract is that the slots fill in order and "a level should
        /// never skip a slot".
        /// </summary>
        private static void Compact(GridRow row, int c, int pairId)
        {
            List<int> ids = new List<int>(4);

            // The colour IS the primary identity, so it leads the list and is rewritten from
            // whatever survives -- a shared destination that loses its first colour is promoted to
            // the next rather than being deleted along with it.
            if (row.coloum != null && c < row.coloum.Length && row.coloum[c] != PairColorType.None)
            {
                int primary = (int)row.coloum[c];
                if (primary != pairId) { ids.Add(primary); }
            }
            Take(row.secondPairId, c, pairId, ids);
            Take(row.thirdPairId, c, pairId, ids);
            Take(row.fourthPairId, c, pairId, ids);

            if (row.coloum != null && c < row.coloum.Length)
            {
                row.coloum[c] = ids.Count > 0 ? (PairColorType)ids[0] : PairColorType.None;
            }
            Put(row.pairId, c, ids, 0);
            Put(row.secondPairId, c, ids, 1);
            Put(row.thirdPairId, c, ids, 2);
            Put(row.fourthPairId, c, ids, 3);
        }

        private static void Take(int[] column, int index, int drop, List<int> into)
        {
            if (column == null || index >= column.Length) { return; }
            int id = column[index];
            if (id != 0 && id != drop) { into.Add(id); }
        }

        private static void Put(int[] column, int index, List<int> ids, int slot)
        {
            if (column == null || index >= column.Length) { return; }
            column[index] = slot < ids.Count ? ids[slot] : 0;
        }
    }
}
