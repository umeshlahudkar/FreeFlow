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
    /// ForbiddenForPair cell with no pairId simply never applies; a Gate with no pairId locks
    /// forever, because IsPairSolved(0) can never be true and completedPairs is only ever keyed
    /// by a real dot's id -- an unsolvable board with nothing in the console. A
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

        public static void Validate(Block[,] grid, int rowCount, int colCount, PairConstraint[] constraints)
        {
            if (grid == null || rowCount <= 0 || colCount <= 0) { return; }

            Dictionary<int, List<Block>> dots = CollectDots(grid, rowCount, colCount);

            ValidateDotCounts(grid, rowCount, colCount, dots);
            ValidateRuleCells(grid, rowCount, colCount, dots);
            ValidateOneWayCells(grid, rowCount, colCount);
            ValidateArrowCells(grid, rowCount, colCount);
            ValidateBridgeCells(grid, rowCount, colCount);
            ValidateRotatorCells(grid, rowCount, colCount);
            ValidateSharedGoals(grid, rowCount, colCount, dots);
            ValidateConstraints(constraints, dots);
            ValidateReachability(grid, rowCount, colCount, dots);
        }

        private static Dictionary<int, List<Block>> CollectDots(Block[,] grid, int rowCount, int colCount)
        {
            Dictionary<int, List<Block>> dots = new Dictionary<int, List<Block>>();

            for (int i = 0; i < rowCount; i++)
            {
                for (int j = 0; j < colCount; j++)
                {
                    Block block = grid[i, j];
                    if (block == null || !block.IsPairBlock) { continue; }

                    // A shared destination counts as a dot for BOTH of its pairs.
                    Register(dots, block.PairId, block);
                    if (block.SecondPairId != 0) { Register(dots, block.SecondPairId, block); }
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
                    if (block == null || block.SecondPairId == 0) { continue; }

                    string where = "shared destination at (" + i + "," + j + ")";

                    if (!block.IsPairBlock)
                    {
                        Error(where + " has a secondPairId but is not a dot at all, so nothing " +
                              "there belongs to either pair.");
                    }
                    if (block.SecondPairId == block.PairId)
                    {
                        Error(where + " names pair " + block.PairId + " twice.");
                    }
                    if (!dots.ContainsKey(block.SecondPairId))
                    {
                        Error(where + " names pair " + block.SecondPairId +
                              ", which has no other dot on this board.");
                    }
                }
            }
        }

        /// <summary>
        /// Two dots per pair, or three when the pair runs through a splitter junction. The old
        /// flat "exactly 2" was correct until splitter pairs existed and is the assertion the
        /// mechanic was always going to break.
        /// </summary>
        private static void ValidateDotCounts(Block[,] grid, int rowCount, int colCount,
                                             Dictionary<int, List<Block>> dots)
        {
            foreach (KeyValuePair<int, List<Block>> pair in dots)
            {
                int expected = HasSplitterFor(grid, rowCount, colCount, pair.Key) ? 3 : 2;

                if (pair.Value.Count != expected)
                {
                    Error("pair id " + pair.Key + " has " + pair.Value.Count + " dot(s) on this " +
                          "board, expected exactly " + expected +
                          (expected == 3 ? " (it has a splitter junction)." : "."));
                }
            }
        }

        private static bool HasSplitterFor(Block[,] grid, int rowCount, int colCount, int pairId)
        {
            for (int i = 0; i < rowCount; i++)
            {
                for (int j = 0; j < colCount; j++)
                {
                    Block cell = grid[i, j];
                    if (cell != null && cell.BlockType == BlockType.Splitter && cell.PairId == pairId)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Checkpoint, ForbiddenForPair and Gate all repurpose a non-dot cell's PairId as "which
        /// pair this rule is about", so a missing or unknown id makes the rule a no-op -- or, for
        /// a gate, an unopenable wall.
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
                                   || type == BlockType.Gate
                                   || type == BlockType.Splitter;
                    if (!namesAPair) { continue; }

                    string where = type + " cell at (" + i + "," + j + ")";

                    if (block.PairId == 0)
                    {
                        Error(where + " has no pairId, so the rule can never apply" +
                              (type == BlockType.Gate ? " and this gate can never open." : "."));
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
        /// A rotator on a dot has nothing to turn (a path starts at a dot rather than passing
        /// through), and one with fewer than two open edges can never join anything, whichever way
        /// the player turns it.
        /// </summary>
        private static void ValidateRotatorCells(Block[,] grid, int rowCount, int colCount)
        {
            for (int i = 0; i < rowCount; i++)
            {
                for (int j = 0; j < colCount; j++)
                {
                    Block block = grid[i, j];
                    if (block == null || block.BlockType != BlockType.Rotator) { continue; }

                    string where = "Rotator cell at (" + i + "," + j + ")";

                    if (block.IsPairBlock)
                    {
                        Error(where + " is also a pair dot; a path starts at a dot instead of " +
                              "turning through it, so the elbow has nothing to act on.");
                    }

                    int open = 0;
                    for (int d = 0; d < Steps.Length; d++)
                    {
                        if (SideIsOpen(grid, rowCount, colCount, block, Steps[d])) { open++; }
                    }

                    if (open < 2)
                    {
                        Error(where + " has " + open + " open edge(s), so no rotation of it can " +
                              "join two cells.");
                    }
                }
            }
        }

        private static void ValidateConstraints(PairConstraint[] constraints, Dictionary<int, List<Block>> dots)
        {
            if (constraints == null) { return; }

            for (int i = 0; i < constraints.Length; i++)
            {
                int pairId = constraints[i].pairId;
                int required = constraints[i].requiredPathLength;
                if (required <= 0) { continue; }

                if (!dots.TryGetValue(pairId, out List<Block> pairDots) || pairDots.Count != 2)
                {
                    Error("pairConstraint targets pair " + pairId + ", which is not a valid pair on this board.");
                    continue;
                }

                // Shortest possible path is the Manhattan distance plus the two endpoints minus
                // the shared step, i.e. distance + 1 cells. Every detour adds a cell going out
                // and a cell coming back, so anything longer has to differ by an even number --
                // an even/odd mismatch is unsatisfiable no matter how the player routes it.
                int distance = Mathf.Abs(pairDots[0].Row_ID - pairDots[1].Row_ID)
                             + Mathf.Abs(pairDots[0].Coloum_ID - pairDots[1].Coloum_ID);
                int shortest = distance + 1;

                if (required < shortest)
                {
                    Error("pair " + pairId + " requires a path of " + required +
                          " cells but its dots are " + shortest + " cells apart at best.");
                }
                else if (((required - shortest) & 1) != 0)
                {
                    Error("pair " + pairId + " requires a path of " + required + " cells; only " +
                          "lengths of the same parity as " + shortest + " are reachable on a grid.");
                }
            }
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

                // A splitter pair is complete only when every dot reaches the junction, so every
                // dot needs a route to it -- pairwise dot-to-dot says nothing about that.
                ValidateSplitterBranches(grid, rowCount, colCount, pairId, pair.Value);

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
        /// <paramref name="start"/>. Rotators are walked in their most permissive form, because
        /// their orientation belongs to the player: a level whose whole point is "turn this" would
        /// otherwise read as unsolvable.
        ///
        /// The walk carries the direction it arrived by, not just the cell: an arrow and a bridge
        /// both constrain where a path may go *next* based on how it got in, so "can I reach this
        /// cell" is not enough state to answer "can I leave it". States are (cell, arrival
        /// direction), which is four per cell at worst, and the exit rule is asked of
        /// <see cref="Block.CanExitFrom"/> so this cannot drift from what the game enforces.
        /// </summary>
        /// <summary>
        /// For a pair with a splitter junction: every one of its dots must be able to reach that
        /// junction, since the pair is only solved when all of them meet there.
        /// </summary>
        private static void ValidateSplitterBranches(Block[,] grid, int rowCount, int colCount,
                                                    int pairId, List<Block> dots)
        {
            for (int i = 0; i < rowCount; i++)
            {
                for (int j = 0; j < colCount; j++)
                {
                    Block junction = grid[i, j];
                    if (junction == null || junction.BlockType != BlockType.Splitter) { continue; }
                    if (junction.PairId != pairId) { continue; }

                    for (int d = 0; d < dots.Count; d++)
                    {
                        bool[,] reach = Flood(grid, rowCount, colCount, dots[d], pairId);
                        if (!reach[i, j])
                        {
                            Error("pair " + pairId + " has a dot at (" + dots[d].Row_ID + "," +
                                  dots[d].Coloum_ID + ") that cannot reach its splitter junction at (" +
                                  i + "," + j + ").");
                        }
                    }
                }
            }
        }

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
                    if (!current.CanExitFromUnderAnyRotation(arrivedBy, dir)) { continue; }

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
        /// other pairs' dots. Gates are treated as passable on purpose -- a gate's whole job is
        /// to open once its dependency pair is solved, so counting it as a wall would flag every
        /// correctly built gate level as broken.
        /// </summary>
        private static bool CanStep(Block from, Block to, Direction dir, int pairId)
        {
            if (to.BlockType == BlockType.Blocked) { return false; }
            if (to.BlockType == BlockType.ForbiddenForPair && to.PairId == pairId) { return false; }
            if (to.IsPairBlock && !to.IsDotFor(pairId)) { return false; }
            if (from.HasWall(dir) || to.HasWall(Opposite(dir))) { return false; }
            // one-way entry, head-on into an arrow -- and any edge of a rotator, since which two
            // edges it joins is the player's to change
            if (!to.CanEnterFromUnderAnyRotation(dir)) { return false; }
            return true;
        }

        private static Block Neighbor(Block[,] grid, int rowCount, int colCount, Block from, Direction dir)
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

        private static Direction Opposite(Direction dir)
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

        private static void Error(string message)
        {
            Debug.LogError("FreeFlow level data error: " + message);
        }
    }
}
