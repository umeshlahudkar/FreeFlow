using FreeFlow.Enums;
using System.Collections.Generic;
using UnityEngine;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Sanity-checks a generated board against the level data that produced it, logging one
    /// error per problem rather than throwing: bad level content should be loud, not fatal.
    ///
    /// This exists because most ways of mis-authoring a level fail *silently*. A Checkpoint or
    /// ForbiddenForPair cell with no pairId simply never applies -- a rule that does nothing, with
    /// nothing in the console. A
    /// requiredEntryDirection on a cell that isn't OneWay is enforced by CanEnterFrom but drawn
    /// by nothing, so it's a rule the player cannot see. None of that is visible in the packed
    /// hex the level assets store, which is exactly why it needs checking at load.
    /// </summary>
    public static class LevelValidator
    {
        private static readonly Direction[] Steps =
        {
            Direction.Left, Direction.Right, Direction.Up, Direction.Down
        };

        public static void Validate(Block[,] grid, int rowCount, int colCount)
        {
            if (grid == null || rowCount <= 0 || colCount <= 0) { return; }

            Dictionary<int, List<Block>> dots = CollectDots(grid, rowCount, colCount);

            ValidateDotCounts(grid, rowCount, colCount, dots);
            ValidateRuleCells(grid, rowCount, colCount, dots);
            ValidateOneWayCells(grid, rowCount, colCount);
            ValidateArrowCells(grid, rowCount, colCount);
            ValidateBridgeCells(grid, rowCount, colCount);
            ValidateSharedGoals(grid, rowCount, colCount, dots);
            ValidateReachability(grid, rowCount, colCount, dots);
        }

        /// <summary>
        /// The real solvability question Validate cannot answer: does a full-coverage arrangement
        /// of every pair's path actually exist? Deliberately NOT part of Validate, and never called
        /// automatically from a level load -- PuzzleSolver's search has no performance guarantee
        /// (see its own class doc), so running it on every GamePlayController.LoadLevel would risk
        /// stalling the very gameplay this project's performance principles (plan §4.3/§40) say
        /// must stay responsive. This is for offline use: the level generator (rejecting a
        /// candidate board), an editor tool, or a test -- never the runtime load path.
        /// </summary>
        public static PuzzleSolver.SolveResult ValidateSolvability(Block[,] grid, int rowCount, int colCount,
            PuzzleSolver.SolverOptions options = default)
        {
            PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, rowCount, colCount, options);

            if (result.Status == PuzzleSolver.SolveStatus.Unsolvable)
            {
                Error("this board has no full-coverage solution -- no arrangement of every pair's " +
                      "path, respecting every mechanic, leaves every usable cell occupied.");
            }
            else if (result.Status == PuzzleSolver.SolveStatus.Inconclusive)
            {
                Error("solvability could not be determined within the search budget -- this does " +
                      "not mean the board is invalid, only that validation could not prove it " +
                      "either way in time. Retry with a larger SolverOptions.MaxSteps.");
            }

            return result;
        }

        private static Dictionary<int, List<Block>> CollectDots(Block[,] grid, int rowCount, int colCount)
        {
            return BoardTopology.CollectDots(grid, rowCount, colCount);
        }

        /// <summary>
        /// A shared destination must name two different, real pairs -- otherwise it is either a
        /// plain dot wearing a second colour that belongs to nobody, or a dot claiming to be its
        /// own partner.
        /// </summary>
        private static void ValidateSharedGoals(Block[,] grid, int rowCount, int colCount,
                                               Dictionary<int, List<Block>> dots)
        {
            for (int i = 0; i < rowCount; i++)
            {
                for (int j = 0; j < colCount; j++)
                {
                    Block block = grid[i, j];
                    if (block == null) { continue; }
                    if (block.SecondPairId == 0 && block.ThirdPairId == 0 && block.FourthPairId == 0)
                    {
                        continue;
                    }

                    // The permission rules own the same two id columns for a different purpose --
                    // their SecondPairId is a named colour, not a second dot -- and ValidateRuleCells
                    // already checks them. Without this they fail every rule below, starting with
                    // "is not a dot at all".
                    if (Block.SecondIdNamesAPair(block.BlockType)) { continue; }

                    string where = "shared destination at (" + i + "," + j + ")";

                    if (!block.IsPairBlock)
                    {
                        Error(where + " names extra pairs but is not a dot at all, so nothing " +
                              "there belongs to any of them.");
                    }

                    // Filled in order, so a gap means a level meant to name a pair and did not.
                    if (block.SecondPairId == 0 && (block.ThirdPairId != 0 || block.FourthPairId != 0))
                    {
                        Error(where + " skips its second pair slot but fills a later one.");
                    }
                    else if (block.ThirdPairId == 0 && block.FourthPairId != 0)
                    {
                        Error(where + " skips its third pair slot but fills the fourth.");
                    }

                    int[] named = { block.PairId, block.SecondPairId, block.ThirdPairId, block.FourthPairId };
                    for (int k = 1; k < named.Length; k++)
                    {
                        if (named[k] == 0) { continue; }

                        bool duplicate = false;
                        for (int earlier = 0; earlier < k; earlier++)
                        {
                            if (named[earlier] == named[k]) { duplicate = true; break; }
                        }

                        if (duplicate)
                        {
                            Error(where + " names pair " + named[k] + " more than once.");
                        }
                        else if (!dots.ContainsKey(named[k]))
                        {
                            Error(where + " names pair " + named[k] +
                                  ", which has no other dot on this board.");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Exactly two dots per pair.
        /// </summary>
        private static void ValidateDotCounts(Block[,] grid, int rowCount, int colCount,
                                             Dictionary<int, List<Block>> dots)
        {
            foreach (KeyValuePair<int, List<Block>> pair in dots)
            {
                if (pair.Value.Count != 2)
                {
                    Error("pair id " + pair.Key + " has " + pair.Value.Count + " dot(s) on this " +
                          "board, expected exactly 2.");
                }
            }
        }

        /// <summary>
        /// Checkpoint, ForbiddenForPair and AllowedForPairs all repurpose a non-dot cell's
        /// PairId as "which pair this rule is about", so a missing or unknown id makes the rule a
        /// no-op -- or, for a permit cell, a wall for everyone.
        /// The two permission rules also use SecondPairId for an optional second named pair, which
        /// gets its own checks below; see Block.SecondIdNamesAPair.
        /// </summary>
        private static void ValidateRuleCells(Block[,] grid, int rowCount, int colCount,
                                              Dictionary<int, List<Block>> dots)
        {
            for (int i = 0; i < rowCount; i++)
            {
                for (int j = 0; j < colCount; j++)
                {
                    Block block = grid[i, j];
                    if (block == null) { continue; }

                    BlockType type = block.BlockType;
                    bool namesAPair = type == BlockType.Checkpoint
                                   || type == BlockType.ForbiddenForPair
                                   || type == BlockType.AllowedForPairs;
                    if (!namesAPair) { continue; }

                    string where = type + " cell at (" + i + "," + j + ")";

                    if (Block.SecondIdNamesAPair(type) && block.SecondPairId != 0)
                    {
                        if (block.SecondPairId == block.PairId)
                        {
                            Error(where + " names pair " + block.PairId + " twice; the second " +
                                  "slot should either name a different pair or be left empty.");
                        }
                        else if (!dots.ContainsKey(block.SecondPairId))
                        {
                            Error(where + " names pair " + block.SecondPairId +
                                  " as its second colour, which has no dots on this board.");
                        }
                    }

                    if (block.PairId == 0)
                    {
                        Error(where + " has no pairId, so the rule can never apply.");
                    }
                    else if (!dots.ContainsKey(block.PairId))
                    {
                        Error(where + " names pair " + block.PairId + ", which has no dots on this board.");
                    }
                    else if (block.IsPairBlock)
                    {
                        Error(where + " is also a pair dot; those two meanings of PairId cannot share a cell.");
                    }
                }
            }
        }

        private static void ValidateOneWayCells(Block[,] grid, int rowCount, int colCount)
        {
            for (int i = 0; i < rowCount; i++)
            {
                for (int j = 0; j < colCount; j++)
                {
                    Block block = grid[i, j];
                    if (block == null) { continue; }

                    bool isOneWay = block.BlockType == BlockType.OneWay;
                    Direction required = block.RequiredEntryDirection;

                    if (!isOneWay && required != Direction.None)
                    {
                        Error("cell (" + i + "," + j + ") is " + block.BlockType + " but has a " +
                              "requiredEntryDirection of " + required + ". CanEnterFrom enforces it " +
                              "anyway and nothing draws it, so it is an invisible rule.");
                    }
                    else if (isOneWay && required == Direction.None)
                    {
                        Error("OneWay cell at (" + i + "," + j + ") has no requiredEntryDirection, " +
                              "so it behaves as a plain cell.");
                    }
                    else if (isOneWay && block.HasWall(Opposite(required)))
                    {
                        Error("OneWay cell at (" + i + "," + j + ") must be entered moving " + required +
                              ", but its " + Opposite(required) + " edge is walled, so it can never be " +
                              "entered at all.");
                    }
                }
            }
        }

        /// <summary>
        /// An arrow with no direction is a plain cell; an arrow whose forced exit leads off the
        /// board or through a wall can be entered but never left, which strands a path; and an
        /// arrow on a pair dot has no meaning, since a path starts there rather than passing
        /// through.
        /// </summary>
        private static void ValidateArrowCells(Block[,] grid, int rowCount, int colCount)
        {
            for (int i = 0; i < rowCount; i++)
            {
                for (int j = 0; j < colCount; j++)
                {
                    Block block = grid[i, j];
                    if (block == null || block.BlockType != BlockType.Arrow) { continue; }

                    Direction forced = block.ForcedExitDirection;
                    string where = "Arrow cell at (" + i + "," + j + ")";

                    if (forced == Direction.None)
                    {
                        Error(where + " has no forcedExitDirection, so it behaves as a plain cell.");
                        continue;
                    }

                    if (block.IsPairBlock)
                    {
                        Error(where + " is also a pair dot; a path starts at a dot rather than " +
                              "passing through it, so the forced exit has nothing to act on.");
                    }

                    Block target = Neighbor(grid, rowCount, colCount, block, forced);
                    if (target == null)
                    {
                        Error(where + " points " + forced + " off the board, so any path entering " +
                              "it could never leave.");
                    }
                    else if (block.HasWall(forced) || target.HasWall(Opposite(forced)))
                    {
                        Error(where + " points " + forced + " through a wall, so any path entering " +
                              "it could never leave.");
                    }
                    else if (target.BlockType == BlockType.Blocked)
                    {
                        Error(where + " points " + forced + " into a blocked cell, so any path " +
                              "entering it could never leave.");
                    }
                }
            }
        }

        /// <summary>
        /// A bridge is only a bridge if something can cross it. Each axis needs both of its
        /// neighbours to exist and be enterable, or that lane is a dead end and the cell is
        /// really just a one-lane corridor wearing crossing art.
        /// </summary>
        private static void ValidateBridgeCells(Block[,] grid, int rowCount, int colCount)
        {
            for (int i = 0; i < rowCount; i++)
            {
                for (int j = 0; j < colCount; j++)
                {
                    Block block = grid[i, j];
                    if (block == null || block.BlockType != BlockType.Bridge) { continue; }

                    string where = "Bridge cell at (" + i + "," + j + ")";

                    if (block.IsPairBlock)
                    {
                        Error(where + " is also a pair dot; a path starts at a dot instead of " +
                              "crossing, so there is no lane to hold.");
                    }

                    bool horizontal = LaneIsOpen(grid, rowCount, colCount, block, Direction.Left, Direction.Right);
                    bool vertical = LaneIsOpen(grid, rowCount, colCount, block, Direction.Up, Direction.Down);

                    if (!horizontal && !vertical)
                    {
                        Error(where + " has no crossable lane at all -- both axes are walled or " +
                              "blocked, so nothing can pass through it.");
                    }
                    else if (!horizontal || !vertical)
                    {
                        Error(where + " only has its " + (horizontal ? "horizontal" : "vertical") +
                              " lane open, so it can never hold two paths and the crossing art is " +
                              "a lie. Use a plain cell, or open the other axis.");
                    }
                }
            }
        }

        private static bool LaneIsOpen(Block[,] grid, int rowCount, int colCount, Block bridge,
                                      Direction a, Direction b)
        {
            return SideIsOpen(grid, rowCount, colCount, bridge, a)
                && SideIsOpen(grid, rowCount, colCount, bridge, b);
        }

        private static bool SideIsOpen(Block[,] grid, int rowCount, int colCount, Block bridge, Direction dir)
        {
            if (bridge.HasWall(dir)) { return false; }

            Block neighbor = Neighbor(grid, rowCount, colCount, bridge, dir);
            if (neighbor == null) { return false; }
            if (neighbor.HasWall(Opposite(dir))) { return false; }
            return neighbor.BlockType != BlockType.Blocked;
        }

        /// <summary>
        /// Each pair must have some legal route between its dots, and each of its checkpoints
        /// must be somewhere it can get to. A lower bound, not a solver: it walks one pair at a
        /// time and knows nothing about pairs competing for the same cells, so it catches
        /// "one blocked cell too many" and not "these two routes cannot both exist".
        /// </summary>
        private static void ValidateReachability(Block[,] grid, int rowCount, int colCount,
                                                Dictionary<int, List<Block>> dots)
        {
            foreach (KeyValuePair<int, List<Block>> pair in dots)
            {
                if (pair.Value.Count != 2) { continue; }

                int pairId = pair.Key;
                Block from = pair.Value[0];
                Block to = pair.Value[1];

                // One-way cells make the board directed, so a route may exist in one direction
                // only -- and the player can draw from either dot. Both walks count.
                bool[,] forward = Flood(grid, rowCount, colCount, from, pairId);
                bool[,] backward = Flood(grid, rowCount, colCount, to, pairId);

                if (!forward[to.Row_ID, to.Coloum_ID] && !backward[from.Row_ID, from.Coloum_ID])
                {
                    Error("pair " + pairId + " has no legal route between its dots at (" +
                          from.Row_ID + "," + from.Coloum_ID + ") and (" +
                          to.Row_ID + "," + to.Coloum_ID + ").");
                    continue;
                }

                for (int i = 0; i < rowCount; i++)
                {
                    for (int j = 0; j < colCount; j++)
                    {
                        Block cell = grid[i, j];
                        if (cell == null || cell.BlockType != BlockType.Checkpoint || cell.PairId != pairId)
                        {
                            continue;
                        }

                        if (!forward[i, j] && !backward[i, j])
                        {
                            Error("pair " + pairId + " must pass through the checkpoint at (" + i + "," + j +
                                  "), but cannot reach it from either of its dots.");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Which cells a path for <paramref name="pairId"/> can reach starting from
        /// <paramref name="start"/>.
        ///
        /// The walk carries the direction it arrived by, not just the cell: an arrow and a bridge
        /// both constrain where a path may go *next* based on how it got in, so "can I reach this
        /// cell" is not enough state to answer "can I leave it". States are (cell, arrival
        /// direction), which is four per cell at worst, and the exit rule is asked of
        /// <see cref="Block.CanExitFrom"/> so this cannot drift from what the game enforces.
        /// </summary>
        private static bool[,] Flood(Block[,] grid, int rowCount, int colCount, Block start, int pairId)
        {
            bool[,] seen = new bool[rowCount, colCount];

            // index [row, col, (int)direction] -- direction 0 (None) is the starting cell, which
            // was never entered from anywhere
            bool[,,] seenState = new bool[rowCount, colCount, Steps.Length + 1];

            Queue<Block> cells = new Queue<Block>();
            Queue<Direction> arrivals = new Queue<Direction>();

            seen[start.Row_ID, start.Coloum_ID] = true;
            seenState[start.Row_ID, start.Coloum_ID, 0] = true;
            cells.Enqueue(start);
            arrivals.Enqueue(Direction.None);

            while (cells.Count > 0)
            {
                Block current = cells.Dequeue();
                Direction arrivedBy = arrivals.Dequeue();

                for (int i = 0; i < Steps.Length; i++)
                {
                    Direction dir = Steps[i];

                    // An arrow does not offer a choice, and a bridge does not allow a turn, so
                    // expanding any other way would claim reachability the player does not have.
                    if (!current.CanExitFrom(arrivedBy, dir)) { continue; }

                    Block next = Neighbor(grid, rowCount, colCount, current, dir);

                    if (next == null) { continue; }
                    if (seenState[next.Row_ID, next.Coloum_ID, (int)dir]) { continue; }
                    if (!CanStep(current, next, dir, pairId)) { continue; }

                    seen[next.Row_ID, next.Coloum_ID] = true;
                    seenState[next.Row_ID, next.Coloum_ID, (int)dir] = true;
                    cells.Enqueue(next);
                    arrivals.Enqueue(dir);
                }
            }

            return seen;
        }

        /// <summary>
        /// The permanent half of the movement rules: walls, blocked cells, one-way entry, and
        /// other pairs' dots.
        /// </summary>
        private static bool CanStep(Block from, Block to, Direction dir, int pairId)
        {
            if (to.BlockType == BlockType.Blocked) { return false; }
            // both permission rules read the same two ids and differ only in the conclusion
            bool named = to.PairId == pairId || to.SecondPairId == pairId;
            if (to.BlockType == BlockType.ForbiddenForPair && named) { return false; }
            if (to.BlockType == BlockType.AllowedForPairs && !named) { return false; }
            if (to.IsPairBlock && !to.IsDotFor(pairId)) { return false; }
            if (from.HasWall(dir) || to.HasWall(Opposite(dir))) { return false; }
            // one-way entry, or head-on into an arrow
            if (!to.CanEnterFrom(dir)) { return false; }
            return true;
        }

        private static Block Neighbor(Block[,] grid, int rowCount, int colCount, Block from, Direction dir)
        {
            return BoardTopology.Neighbor(grid, rowCount, colCount, from, dir);
        }

        private static Direction Opposite(Direction dir)
        {
            return BoardTopology.Opposite(dir);
        }

        private static void Error(string message)
        {
            Debug.LogError("FreeFlow level data error: " + message);
        }
    }
}
