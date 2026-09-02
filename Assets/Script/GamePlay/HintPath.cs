using FreeFlow.Enums;
using System.Collections.Generic;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Turns the answer a level ships with -- <c>GridRow.solutionPairId</c>, one pair id per cell --
    /// back into an ORDERED route for one pair, dot to dot, which is what a hint has to draw.
    ///
    /// <b>The stored column is a colouring, not a route.</b> It says which pair covers each cell and
    /// nothing about the order they are visited in, so recovering the order is a search rather than
    /// a walk: a Flow path routinely runs alongside itself, and then two cells are neighbours on the
    /// grid while being far apart along the path. "Step to whichever neighbour is also mine" picks
    /// the wrong one the first time a path doubles back, and does it silently -- the hint would draw
    /// a shortcut that skips half the pair's cells and leaves the board uncoverable. This backtracks
    /// instead, and requires the route to use EVERY cell the answer gave the pair. That requirement
    /// is what makes the reconstruction unambiguous rather than merely lucky.
    ///
    /// Every step is checked against the same movement rules gameplay enforces -- walls, one-way
    /// entry, an arrow's forced exit, a bridge's straight-through-only -- minus the one that reads
    /// live occupancy (<see cref="Block.CanAcceptEntry"/>). The route is a property of the LEVEL,
    /// not of what the player has currently drawn, so what is already on the board must not change
    /// what the answer is; clearing whatever stands in its way is the caller's job.
    /// </summary>
    public static class HintPath
    {
        // Ceiling on branches explored before the search gives up and answers "no route". A real
        // pair's cells form a path, so the search walks almost straight down it and never comes
        // close to this; the budget exists so that malformed data -- a solution column that does not
        // describe a path at all -- costs a bounded amount of time on a tap rather than freezing the
        // game. Generous on purpose: exhausting it is a bug to be seen, not a limit to be tuned.
        private const int MaxSteps = 200000;

        private static readonly Direction[] Directions =
        {
            Direction.Right, Direction.Down, Direction.Left, Direction.Up
        };

        /// <summary>
        /// The level's stored answer as a grid, or null when it has none. Levels generated before
        /// the column existed simply do not carry one (every hand-authored level, and the legacy
        /// Advanced 1-45 campaign), and a caller must treat that as "no hint available" rather than
        /// solving on the spot -- see <c>LevelData.solutionPairId</c> for why the answer is stored
        /// rather than derived.
        /// </summary>
        public static int[,] ReadSolution(LevelData data)
        {
            if (data.gridRows == null || data.gridRows.Length == 0) { return null; }

            int rows = (int)data.gridSize;
            int cols = rows;
            if (data.gridRows.Length < rows) { return null; }

            int[,] solution = new int[rows, cols];
            bool anyCovered = false;

            for (int r = 0; r < rows; r++)
            {
                int[] row = data.gridRows[r].solutionPairId;
                if (row == null) { return null; }

                for (int c = 0; c < cols && c < row.Length; c++)
                {
                    solution[r, c] = row[c];
                    if (row[c] != 0) { anyCovered = true; }
                }
            }

            // An all-zero column is a level that was written before the answer was recorded, not a
            // level whose answer is "nothing covers anything".
            return anyCovered ? solution : null;
        }

        /// <summary>
        /// The cells <paramref name="pairId"/> visits in the stored answer, in travel order, or null
        /// when no legal route through exactly those cells exists.
        ///
        /// The route starts at whichever dot is NOT a shared destination, so it reads the way the
        /// player would have drawn it: a drag may only begin at a plain dot (a shared cell belongs
        /// to two pairs and a press on it cannot say which was meant), and the colour a segment is
        /// drawn in comes from its first cell.
        /// </summary>
        public static List<Block> Build(Block[,] grid, int rows, int cols, int[,] solution, int pairId)
        {
            if (grid == null || solution == null || pairId == 0) { return null; }
            if (solution.GetLength(0) < rows || solution.GetLength(1) < cols) { return null; }

            bool[,] mine = new bool[rows, cols];
            List<Block> dots = new List<Block>();
            int owned = 0;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    Block cell = grid[r, c];
                    if (cell == null) { continue; }

                    // A dot of this pair belongs to it whatever the answer says. At a shared
                    // destination the stored column names only ONE of the pairs that end there
                    // (see LevelGenerator.FillStoredSolution) and the other still has to reach it.
                    bool isDot = cell.IsDotFor(pairId);
                    if (isDot) { dots.Add(cell); }

                    if (solution[r, c] == pairId || isDot)
                    {
                        mine[r, c] = true;
                        owned++;
                    }
                }
            }

            // Two dots is what a pair is. Anything else is level data this cannot describe a single
            // route through, and guessing one would be worse than declining.
            if (dots.Count != 2 || owned < 2) { return null; }

            Block start = dots[0].IsSharedGoal && !dots[1].IsSharedGoal ? dots[1] : dots[0];
            Block goal = start == dots[0] ? dots[1] : dots[0];

            List<Block> path = new List<Block>(owned) { start };
            bool[,] visited = new bool[rows, cols];
            visited[start.Row_ID, start.Coloum_ID] = true;

            int budget = MaxSteps;
            bool found = Extend(grid, rows, cols, mine, visited, path, start, Direction.None,
                goal, pairId, owned - 1, ref budget);

            return found ? path : null;
        }

        /// <summary>
        /// Depth-first continuation of <paramref name="path"/> from <paramref name="current"/>,
        /// entered while travelling <paramref name="entryDir"/>. <paramref name="remaining"/> counts
        /// the pair's cells not yet visited, so the route is only accepted once it ends on the
        /// second dot with nothing of the pair's left over.
        /// </summary>
        private static bool Extend(Block[,] grid, int rows, int cols, bool[,] mine, bool[,] visited,
            List<Block> path, Block current, Direction entryDir, Block goal, int pairId,
            int remaining, ref int budget)
        {
            if (budget-- <= 0) { return false; }

            // Arriving at the far dot ends the route whether or not it covered everything -- a path
            // cannot run THROUGH its own endpoint, so a branch that reaches it early is dead rather
            // than continuable.
            if (current == goal) { return remaining == 0; }

            for (int d = 0; d < Directions.Length; d++)
            {
                Direction dir = Directions[d];
                Block next = BoardTopology.Neighbor(grid, rows, cols, current, dir);
                if (next == null) { continue; }
                if (!StepAllowed(current, next, entryDir, dir, pairId)) { continue; }

                if (visited[next.Row_ID, next.Coloum_ID]) { continue; }

                if (mine[next.Row_ID, next.Coloum_ID])
                {
                    visited[next.Row_ID, next.Coloum_ID] = true;
                    path.Add(next);

                    if (Extend(grid, rows, cols, mine, visited, path, next, dir, goal, pairId,
                        remaining - 1, ref budget))
                    {
                        return true;
                    }

                    path.RemoveAt(path.Count - 1);
                    visited[next.Row_ID, next.Coloum_ID] = false;
                    continue;
                }

                // A bridge the answer handed to the OTHER pair. The column holds one colour per
                // cell, so at a crossing only one of the two paths is recorded there and the other
                // one's cells are left split in two by a gap it does in fact pass through. Crossing
                // it is the one step onto a cell the answer did not give us: straight in, straight
                // out, which is all a bridge ever permits anyway.
                if (next.BlockType != BlockType.Bridge) { continue; }

                Block after = BoardTopology.Neighbor(grid, rows, cols, next, dir);
                if (after == null) { continue; }
                if (!mine[after.Row_ID, after.Coloum_ID] || visited[after.Row_ID, after.Coloum_ID]) { continue; }
                if (!StepAllowed(next, after, dir, dir, pairId)) { continue; }

                visited[next.Row_ID, next.Coloum_ID] = true;
                visited[after.Row_ID, after.Coloum_ID] = true;
                path.Add(next);
                path.Add(after);

                if (Extend(grid, rows, cols, mine, visited, path, after, dir, goal, pairId,
                    remaining - 1, ref budget))
                {
                    return true;
                }

                path.RemoveAt(path.Count - 1);
                path.RemoveAt(path.Count - 1);
                visited[after.Row_ID, after.Coloum_ID] = false;
                visited[next.Row_ID, next.Coloum_ID] = false;
            }

            return false;
        }

        /// <summary>
        /// Whether a path of <paramref name="pairId"/> may step from <paramref name="from"/> to
        /// <paramref name="to"/> in <paramref name="dir"/>, having arrived at
        /// <paramref name="from"/> travelling <paramref name="entryDir"/>. The board's own
        /// predicates answer all four questions, so this is the same rule set gameplay uses --
        /// deliberately minus <see cref="Block.CanAcceptEntry"/>, which asks who is drawn there now.
        /// </summary>
        private static bool StepAllowed(Block from, Block to, Direction entryDir, Direction dir, int pairId)
        {
            if (from.HasWall(dir) || to.HasWall(BoardTopology.Opposite(dir))) { return false; }
            if (!to.CanEnter(pairId)) { return false; }
            if (!to.CanEnterFrom(dir)) { return false; }
            if (!from.CanExitFrom(entryDir, dir)) { return false; }
            return true;
        }
    }
}
