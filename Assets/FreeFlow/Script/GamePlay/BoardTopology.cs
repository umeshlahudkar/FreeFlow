using System;
using System.Collections.Generic;
using FreeFlow.Enums;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Pure grid-geometry helpers shared by every algorithm that walks a board: LevelValidator's
    /// reachability check and PuzzleSolver's search both need to step to a neighbour, reverse a
    /// direction, and enumerate a board's dots. Factored out once a second consumer (PuzzleSolver)
    /// needed the same three helpers LevelValidator already had, rather than letting each new
    /// board-walking system grow its own copy.
    /// </summary>
    public static class BoardTopology
    {
        public static Block Neighbor(Block[,] grid, int rowCount, int colCount, Block from, Direction dir)
        {
            int r = from.Row_ID;
            int c = from.Coloum_ID;

            switch (dir)
            {
                case Direction.Left: c--; break;
                case Direction.Right: c++; break;
                case Direction.Up: r--; break;
                case Direction.Down: r++; break;
                default: return null;
            }

            if (r < 0 || r >= rowCount || c < 0 || c >= colCount) { return null; }
            return grid[r, c];
        }

        public static Direction Opposite(Direction dir)
        {
            switch (dir)
            {
                case Direction.Left: return Direction.Right;
                case Direction.Right: return Direction.Left;
                case Direction.Up: return Direction.Down;
                case Direction.Down: return Direction.Up;
                default: return Direction.None;
            }
        }

        /// <summary>
        /// Every pair-id -> dot-cell mapping on the board. A shared destination is registered
        /// under every pair it names, not just its primary PairId, so it counts as one of each
        /// named pair's own two dots -- see ValidateDotCounts and Block.IsDotFor.
        /// </summary>
        public static Dictionary<int, List<Block>> CollectDots(Block[,] grid, int rowCount, int colCount)
        {
            Dictionary<int, List<Block>> dots = new Dictionary<int, List<Block>>();

            for (int i = 0; i < rowCount; i++)
            {
                for (int j = 0; j < colCount; j++)
                {
                    Block block = grid[i, j];
                    if (block == null || !block.IsPairBlock) { continue; }

                    Register(dots, block.PairId, block);
                    if (block.SecondPairId != 0) { Register(dots, block.SecondPairId, block); }
                    if (block.ThirdPairId != 0) { Register(dots, block.ThirdPairId, block); }
                    if (block.FourthPairId != 0) { Register(dots, block.FourthPairId, block); }
                }
            }

            return dots;
        }

        private static void Register(Dictionary<int, List<Block>> dots, int pairId, Block block)
        {
            if (!dots.TryGetValue(pairId, out List<Block> list))
            {
                list = new List<Block>();
                dots[pairId] = list;
            }
            list.Add(block);
        }

        /// <summary>
        /// The one canonical statement of "is this board fully covered": every cell except a
        /// Blocked one must have an occupant. Two callers need this -- GamePlayController,
        /// checking the live board's real occupant tracking during play, and PuzzleSolver,
        /// checking its own scratch occupancy during a search -- and they cannot share a single
        /// occupancy *data structure* (the solver deliberately never touches Block's real
        /// occupant fields, so solving never paints a live board or crashes on a bare test cell --
        /// see PuzzleSolver's own class doc). What they CAN and must share is the *rule*: which
        /// cells need covering, and what "covered" means. <paramref name="hasOccupant"/> is each
        /// caller's own answer to "does this specific cell have one right now"; this method is the
        /// one place that decides which cells get asked at all, so a future change to what counts
        /// as excluded from coverage (only Blocked, today) only ever needs to change here.
        /// </summary>
        public static bool IsFullyCovered(Block[,] grid, int rowCount, int colCount, Func<Block, bool> hasOccupant)
        {
            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < colCount; c++)
                {
                    Block cell = grid[r, c];
                    if (cell == null || cell.BlockType == BlockType.Blocked) { continue; }
                    if (!hasOccupant(cell)) { return false; }
                }
            }
            return true;
        }
    }
}
