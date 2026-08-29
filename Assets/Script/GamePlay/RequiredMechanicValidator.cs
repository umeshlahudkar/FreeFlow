using System;
using System.Reflection;
using FreeFlow.Enums;
using UnityEngine;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Answers spec §10's question -- "is this mechanic actually necessary, or could the player
    /// ignore it and still solve the puzzle?" -- by solving a board twice: once as authored, once
    /// with the one mechanic under test stripped back to a plain cell, and comparing.
    ///
    /// A subtlety worth stating plainly, because it is easy to get backwards: a mechanic that only
    /// RESTRICTS movement (Blocked's neighbours, One-Way, Arrow, Forbidden, Allowed, a Wall) can
    /// never turn a solvable board into an unsolvable one by being REMOVED -- any solution that
    /// obeyed the stricter rule automatically still obeys the relaxed one, since removing a
    /// restriction only ever grows the set of legal moves. So "does the board stop being solvable
    /// without it" is the wrong test for those mechanics; it would never fire. What removing them
    /// CAN do is turn a puzzle with exactly one solution into one with several, by opening up a
    /// route the mechanic used to rule out -- which is the real, checkable signal that the
    /// mechanic was constraining the player toward a specific answer. So the test here is: does
    /// stripping the mechanic turn a UNIQUE solution into a non-unique one (Required), or leave it
    /// unique either way (NotRequired)?
    ///
    /// Bridge and Blocked are the exception, and the one place "does it stay solvable" is the
    /// right question: a Bridge grants a cell EXTRA capacity (two pairs at once) rather than only
    /// restricting it, and a Blocked cell is excluded from the full-coverage requirement entirely
    /// -- removing either can genuinely eliminate the only valid arrangement, not just add
    /// alternatives to it. Both cases are handled by the same classification below.
    /// </summary>
    public static class RequiredMechanicValidator
    {
        public enum RequirementStatus
        {
            /// <summary>Removing the mechanic broke solvability entirely, or turned a unique
            /// solution into a non-unique one -- the player cannot ignore it.</summary>
            Required,
            /// <summary>Removing the mechanic changed nothing observable -- the player could
            /// solve the board exactly as well without it.</summary>
            NotRequired,
            /// <summary>Neither solve reached a confident answer within budget.</summary>
            Inconclusive
        }

        public sealed class RequirementResult
        {
            public RequirementStatus Status;
            public PuzzleSolver.SolveResult WithMechanic;
            public PuzzleSolver.SolveResult WithoutMechanic;
        }

        /// <summary>
        /// Tests whether the BlockType-driven mechanic at (row, col) -- Blocked, One-Way, Arrow,
        /// Checkpoint, ForbiddenForPair, AllowedForPairs, or Bridge -- is required. Leaves any
        /// wall on that cell untouched; a cell can carry a wall and a BlockType mechanic
        /// independently, and they are two separate questions (see CheckWallRequired).
        ///
        /// Not meaningful for a shared destination's extra pair identities (secondPairId etc. on
        /// a dot cell) -- those name which pairs the dot belongs to, not a strippable rule, so
        /// "remove it and re-solve" doesn't apply the same way. Out of scope here.
        /// </summary>
        public static RequirementResult CheckBlockTypeMechanicRequired(Block[,] grid, int rowCount, int colCount,
            int row, int col, PuzzleSolver.SolverOptions options = default)
        {
            return Check(grid, rowCount, colCount, options, clone => StripBlockTypeMechanic(clone[row, col]));
        }

        /// <summary>Tests whether the wall on the given edge of (row, col) is required, leaving
        /// that cell's BlockType (if any) untouched. Clears the mirrored bit on the neighbour too,
        /// so a wall authored one-sided or on both sides is stripped completely either way.</summary>
        public static RequirementResult CheckWallRequired(Block[,] grid, int rowCount, int colCount,
            int row, int col, Direction edge, PuzzleSolver.SolverOptions options = default)
        {
            return Check(grid, rowCount, colCount, options, clone =>
            {
                Block cell = clone[row, col];
                StripWall(cell, edge);

                Block neighbor = BoardTopology.Neighbor(clone, rowCount, colCount, cell, edge);
                if (neighbor != null) { StripWall(neighbor, BoardTopology.Opposite(edge)); }
            });
        }

        private static RequirementResult Check(Block[,] grid, int rowCount, int colCount,
            PuzzleSolver.SolverOptions options, Action<Block[,]> strip)
        {
            // Solution-count comparison is the whole point (see class doc), so MaxSolutionsToFind
            // is fixed at 2 regardless of what the caller passed -- getting this wrong would
            // silently break the classification below, so it isn't left to the caller to remember.
            int maxSteps = options.MaxSteps > 0 ? options.MaxSteps : PuzzleSolver.SolverOptions.Default.MaxSteps;
            PuzzleSolver.SolverOptions solveOptions = new PuzzleSolver.SolverOptions(maxSteps, 2);

            PuzzleSolver.SolveResult withMechanic = PuzzleSolver.Solve(grid, rowCount, colCount, solveOptions);

            Block[,] clone = CloneGrid(grid, rowCount, colCount);
            try
            {
                strip(clone);
                PuzzleSolver.SolveResult withoutMechanic = PuzzleSolver.Solve(clone, rowCount, colCount, solveOptions);

                return new RequirementResult
                {
                    Status = Classify(withMechanic, withoutMechanic),
                    WithMechanic = withMechanic,
                    WithoutMechanic = withoutMechanic
                };
            }
            finally
            {
                DestroyGrid(clone);
            }
        }

        private static RequirementStatus Classify(PuzzleSolver.SolveResult withMechanic,
            PuzzleSolver.SolveResult withoutMechanic)
        {
            // Can't test necessity against a board that wasn't solvable to begin with.
            if (withMechanic.Status != PuzzleSolver.SolveStatus.Solved) { return RequirementStatus.Inconclusive; }
            if (!withMechanic.SearchExhausted) { return RequirementStatus.Inconclusive; }

            // Only reachable by a capacity-granting mechanic (Bridge) or by Blocked's coverage
            // exclusion -- see the class doc. A pure-restriction mechanic can never cause this.
            if (withoutMechanic.Status == PuzzleSolver.SolveStatus.Unsolvable) { return RequirementStatus.Required; }

            if (withoutMechanic.Status != PuzzleSolver.SolveStatus.Solved) { return RequirementStatus.Inconclusive; }

            bool wasUnique = withMechanic.SolutionsFound == 1;
            bool stillUnique = withoutMechanic.SolutionsFound == 1 && withoutMechanic.SearchExhausted;

            if (wasUnique && !stillUnique) { return RequirementStatus.Required; }
            return RequirementStatus.NotRequired;
        }

        // -----------------------------------------------------------------------------------
        // Cloning + stripping. Mirrors LevelGenerator's headless Block[,] construction
        // technique (bare GameObjects, reflection for the private fields) -- kept independent
        // of it and of BlockTestHarness, same reasoning as LevelGenerator's own doc comment:
        // production tooling shouldn't depend on test helpers, and vice versa.
        // -----------------------------------------------------------------------------------

        private static Block[,] CloneGrid(Block[,] source, int rowCount, int colCount)
        {
            Block[,] clone = new Block[rowCount, colCount];
            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < colCount; c++)
                {
                    Block original = source[r, c];
                    GameObject go = new GameObject("MechanicCheckClone_" + r + "_" + c);
                    Block copy = go.AddComponent<Block>();
                    SetField(copy, "row_ID", r);
                    SetField(copy, "coloum_ID", c);
                    CopyState(original, copy);
                    clone[r, c] = copy;
                }
            }
            return clone;
        }

        private static void DestroyGrid(Block[,] grid)
        {
            if (grid == null) { return; }
            foreach (Block block in grid)
            {
                if (block != null) { UnityEngine.Object.DestroyImmediate(block.gameObject); }
            }
        }

        private static void CopyState(Block source, Block target)
        {
            SetField(target, "isPairBlock", source.IsPairBlock);
            SetField(target, "pairColorType", source.PairColorType);
            SetField(target, "pairId", source.PairId);
            SetField(target, "secondPairId", source.SecondPairId);
            SetField(target, "thirdPairId", source.ThirdPairId);
            SetField(target, "fourthPairId", source.FourthPairId);
            SetField(target, "blockType", source.BlockType);
            SetField(target, "requiredEntryDirection", source.RequiredEntryDirection);
            SetField(target, "forcedExitDirection", source.ForcedExitDirection);
            SetField(target, "wallMask", ReadWallMask(source));
        }

        private static int ReadWallMask(Block cell)
        {
            int mask = 0;
            if (cell.HasWall(Direction.Left)) { mask |= 1; }
            if (cell.HasWall(Direction.Right)) { mask |= 2; }
            if (cell.HasWall(Direction.Up)) { mask |= 4; }
            if (cell.HasWall(Direction.Down)) { mask |= 8; }
            return mask;
        }

        /// <summary>
        /// Reverts a cell's BlockType-driven mechanic to Normal. Only clears pairId/secondPairId
        /// when the cell is NOT itself a pair dot -- on a rule cell (Checkpoint/ForbiddenForPair/
        /// AllowedForPairs) those columns name which pair the rule is about and stripping them is
        /// exactly the point, but on a genuine dot they name which pair the dot BELONGS to, and
        /// clearing that would orphan the pair instead of testing anything.
        /// </summary>
        private static void StripBlockTypeMechanic(Block cell)
        {
            if (!cell.IsPairBlock)
            {
                SetField(cell, "pairId", 0);
                SetField(cell, "secondPairId", 0);
            }
            SetField(cell, "blockType", BlockType.Normal);
            SetField(cell, "requiredEntryDirection", Direction.None);
            SetField(cell, "forcedExitDirection", Direction.None);
        }

        private static void StripWall(Block cell, Direction edge)
        {
            int bit = WallBit(edge);
            if (bit == 0) { return; }
            SetField(cell, "wallMask", ReadWallMask(cell) & ~bit);
        }

        private static int WallBit(Direction dir)
        {
            switch (dir)
            {
                case Direction.Left: return 1;
                case Direction.Right: return 2;
                case Direction.Up: return 4;
                case Direction.Down: return 8;
                default: return 0;
            }
        }

        private static void SetField(Block block, string fieldName, object value)
        {
            FieldInfo field = typeof(Block).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(block, value);
        }
    }
}
