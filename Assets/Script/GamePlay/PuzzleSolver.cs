using System;
using System.Collections.Generic;
using FreeFlow.Enums;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Given a board, finds a set of paths -- one per pair, respecting every mechanic, covering
    /// every usable cell exactly once (twice on a Bridge, once per lane) -- or proves none exists.
    ///
    /// This is the one place that understands "is this board actually solvable", as opposed to
    /// LevelValidator's per-pair reachability walk, which is explicitly a lower bound: it checks
    /// each pair in isolation and knows nothing about pairs competing for the same cells or about
    /// full-board coverage at all (see its own doc comment). PuzzleSolver is the real thing, meant
    /// to be reused by level generation, validation, difficulty scoring, solution-uniqueness
    /// checking, and hints alike, rather than each of those growing its own search.
    ///
    /// Approach: solve pairs one at a time (shortest dot-to-dot span first, a cheap ordering
    /// heuristic that tends to fail fast), and for the pair currently being solved, walk a single
    /// path cell-by-cell via backtracking DFS -- trying every legal next cell, not just the
    /// shortest route, since full coverage routinely requires a pair's path to detour through
    /// cells no other pair can reach. Legality at each step reuses Block's own predicates
    /// (CanEnter, CanEnterFrom, CanExitFrom) exactly as gameplay and LevelValidator do; only the
    /// "who else already occupies this cell" bookkeeping is the solver's own, kept separate from
    /// Block's real occupant-tracking fields so solving never touches (or fights with) whatever a
    /// live board is currently displaying.
    ///
    /// Finding more than one solution (SolverOptions.MaxSolutionsToFind) reuses the exact same
    /// backtracking: instead of stopping the instant a full-coverage arrangement is found, that
    /// arrangement is recorded and the search is told to keep going as if it had failed, which
    /// makes the ordinary backtracking machinery hunt for a genuinely different arrangement on its
    /// own. This is what solution-uniqueness checking needs -- "is there a second solution?" --
    /// without a separate search implementation.
    /// </summary>
    public static class PuzzleSolver
    {
        public enum SolveStatus
        {
            /// <summary>At least one full solution was found; see SolveResult.Solutions.</summary>
            Solved,
            /// <summary>The search exhausted every possibility: no solution exists.</summary>
            Unsolvable,
            /// <summary>
            /// The step budget ran out before the search could finish. If SolutionsFound is 0,
            /// solvability itself is unknown; if greater, at least that many solutions exist but
            /// there may be more the budget did not allow ruling out (see SearchExhausted).
            /// </summary>
            Inconclusive
        }

        public readonly struct SolverOptions
        {
            /// <summary>
            /// Upper bound on search steps before giving up and reporting Inconclusive rather than
            /// hanging indefinitely -- full-coverage multi-pair solving is NP-hard in general, so
            /// nothing about this search is guaranteed fast on a large or heavily-constrained board.
            /// </summary>
            public readonly int MaxSteps;

            /// <summary>
            /// Stop as soon as this many distinct solutions have been found. Defaults to 1 (just
            /// prove solvability). Pass 2 to check uniqueness: if the search exhausts itself having
            /// found only 1, the solution is provably unique; if it finds 2, it is provably not.
            /// </summary>
            public readonly int MaxSolutionsToFind;

            /// <summary>
            /// When true, a board counts as solved the moment every pair is connected, even if
            /// cells are left empty -- the ordinary Flow win condition WITHOUT this game's extra
            /// full-coverage rule.
            ///
            /// Stored inverted (rather than as a RequireFullCoverage flag) on purpose: this is a
            /// struct, so `default(SolverOptions)` and both constructors below must keep meaning
            /// "full coverage required", and a bool field defaults to false. Gameplay and
            /// LevelValidator never set this; it exists for LevelGenerator, which needs to ask the
            /// opposite question -- "can a player connect everything and still leave a hole?" --
            /// to reject boards that would let a player reach a connected-but-incomplete state.
            /// See LevelGenerator.EveryPairingCoversTheBoard.
            /// </summary>
            public readonly bool AllowPartialCoverage;

            public SolverOptions(int maxSteps) : this(maxSteps, 1) { }

            public SolverOptions(int maxSteps, int maxSolutionsToFind)
                : this(maxSteps, maxSolutionsToFind, false) { }

            public SolverOptions(int maxSteps, int maxSolutionsToFind, bool allowPartialCoverage)
            {
                MaxSteps = maxSteps;
                MaxSolutionsToFind = maxSolutionsToFind;
                AllowPartialCoverage = allowPartialCoverage;
            }

            public static SolverOptions Default => new SolverOptions(500000, 1);
        }

        public sealed class PairSolution
        {
            public int PairId;
            public List<(int Row, int Col)> Cells;
        }

        public sealed class SolveResult
        {
            public SolveStatus Status;

            /// <summary>The first solution found, or an empty list when none was.</summary>
            public List<PairSolution> Solutions;

            /// <summary>
            /// Every solution found, not just the first -- same list Solutions is the head of, so
            /// AllSolutions[0] and Solutions are the same arrangement. Only interesting to callers
            /// that need to reason about the whole solution SET rather than prove one exists (e.g.
            /// LevelGenerator.EveryPairingCoversTheBoard, which must inspect all of them); ordinary
            /// solvability and uniqueness checks want Solutions and SolutionsFound instead.
            /// </summary>
            public List<List<PairSolution>> AllSolutions;

            /// <summary>
            /// How many distinct solutions were actually found, capped at
            /// SolverOptions.MaxSolutionsToFind.
            /// </summary>
            public int SolutionsFound;

            /// <summary>
            /// True if the search space was fully explored (so SolutionsFound is exact, up to the
            /// cap); false if the step budget cut it short, in which case SolutionsFound is only a
            /// lower bound -- there may be more solutions the search never got to rule out.
            /// </summary>
            public bool SearchExhausted;

            /// <summary>
            /// Search steps actually taken -- a raw measure of how much work finding (or ruling
            /// out) a solution took. Exists for DifficultyAnalyzer's "solver search effort" factor;
            /// gameplay/generation code has no reason to read it.
            /// </summary>
            public int StepsTaken;

            /// <summary>
            /// How many times, while building a path, more than one direction was legal at once --
            /// a point where the search (and a player) genuinely had to choose. Counted only along
            /// the path actually being built, not inside the reachability prune.
            /// </summary>
            public int DecisionPointCount;

            /// <summary>
            /// How many times a path being built ran out of legal directions before reaching its
            /// target, forcing a backtrack. A board riddled with these is one where it is easy to
            /// paint yourself into a corner -- a real difficulty signal distinct from how many
            /// choices existed.
            /// </summary>
            public int DeadEndCount;
        }

        private sealed class BudgetExceededException : Exception { }

        private static readonly Direction[] Directions =
        {
            Direction.Left, Direction.Right, Direction.Up, Direction.Down
        };

        /// <summary>Everything a single Solve() call threads through its recursion, bundled so the
        /// recursive methods below don't carry an ever-growing parameter list.</summary>
        private sealed class SearchContext
        {
            public Dictionary<int, List<Block>> Dots;
            public Dictionary<int, List<(int, int)>> CheckpointsByPair;
            public List<int> PairIds;
            public int MaxSteps;
            public int Steps;
            public int MaxSolutions;
            public bool AllowPartialCoverage;
            public List<List<PairSolution>> FoundSolutions;
            public int DecisionPointCount;
            public int DeadEndCount;

            /// <summary>
            /// Whether the stranded-cell prune may run. Off when the board carries Bridge or
            /// shared-goal cells: those hold more than one occupant, so the "an empty cell needs
            /// two ways in and out" arithmetic the prune relies on stops being true. Correctness
            /// first -- an unsound prune would not merely slow generation, it would silently
            /// declare solvable boards unsolvable and unique ones non-unique.
            /// </summary>
            public bool StrandPruningEnabled;

            /// <summary>
            /// Scratch for the connectivity prune: a component id per cell, and a stack for the
            /// flood fill. Allocated once per solve rather than per move -- the prune runs on every
            /// move of a search that takes millions of steps, so an allocation here would dominate
            /// everything it saves.
            /// </summary>
            public int[,] Component;
            public int[] FloodStack;
            public int ComponentStamp;
        }

        public static SolveResult Solve(Block[,] grid, int rowCount, int colCount, SolverOptions options = default)
        {
            if (options.MaxSteps <= 0) { options = SolverOptions.Default; }
            int maxSolutions = Math.Max(1, options.MaxSolutionsToFind);

            Dictionary<int, List<Block>> dots = BoardTopology.CollectDots(grid, rowCount, colCount);
            List<int> pairIds = new List<int>();
            foreach (KeyValuePair<int, List<Block>> kv in dots)
            {
                // A pair without exactly two dots is malformed data LevelValidator already flags
                // elsewhere; the solver has nothing well-defined to connect it with, so it is
                // simply left out rather than treated as an unrelated hard failure here.
                if (kv.Value.Count == 2) { pairIds.Add(kv.Key); }
            }

            // Shortest span first: a cheap heuristic that tends to pin down the most constrained
            // pairs earliest, failing bad branches sooner. Tie-broken by pair id purely so the
            // search order -- and therefore which valid solution is found first -- is deterministic.
            pairIds.Sort((a, b) =>
            {
                int cmp = PairSpan(dots[a]).CompareTo(PairSpan(dots[b]));
                return cmp != 0 ? cmp : a.CompareTo(b);
            });

            SolverState state = new SolverState(grid, rowCount, colCount);

            if (pairIds.Count == 0)
            {
                bool trivial = state.IsFullyCovered();
                return new SolveResult
                {
                    Status = trivial ? SolveStatus.Solved : SolveStatus.Unsolvable,
                    Solutions = new List<PairSolution>(),
                    AllSolutions = new List<List<PairSolution>>(),
                    SolutionsFound = trivial ? 1 : 0,
                    SearchExhausted = true
                };
            }

            SearchContext ctx = new SearchContext
            {
                Dots = dots,
                CheckpointsByPair = CollectCheckpoints(grid, rowCount, colCount),
                PairIds = pairIds,
                MaxSteps = options.MaxSteps,
                Steps = 0,
                MaxSolutions = maxSolutions,
                AllowPartialCoverage = options.AllowPartialCoverage,
                FoundSolutions = new List<List<PairSolution>>(),
                // Full coverage is what makes the prune sound: every empty cell MUST end up on
                // some path, so one that can no longer be entered and left is a dead board. With
                // partial coverage allowed, leaving a cell empty is legal and the prune would be
                // wrong.
                StrandPruningEnabled = !options.AllowPartialCoverage
                    && !BoardHasSharedCapacityCells(grid, rowCount, colCount),
                Component = new int[rowCount, colCount],
                FloodStack = new int[rowCount * colCount],
                ComponentStamp = 0
            };

            bool exhausted = true;
            try
            {
                Block firstStart = dots[pairIds[0]][0];
                state.Occupy(firstStart, pairIds[0], Direction.None);
                Search(ctx, state, 0, firstStart, Direction.None);
            }
            catch (BudgetExceededException)
            {
                exhausted = false;
            }

            bool solved = ctx.FoundSolutions.Count > 0;
            return new SolveResult
            {
                Status = solved ? SolveStatus.Solved : (exhausted ? SolveStatus.Unsolvable : SolveStatus.Inconclusive),
                Solutions = solved ? ctx.FoundSolutions[0] : new List<PairSolution>(),
                AllSolutions = ctx.FoundSolutions,
                SolutionsFound = ctx.FoundSolutions.Count,
                SearchExhausted = exhausted,
                StepsTaken = ctx.Steps,
                DecisionPointCount = ctx.DecisionPointCount,
                DeadEndCount = ctx.DeadEndCount
            };
        }

        /// <summary>
        /// Returns true once the search should stop entirely (enough solutions found, or this
        /// branch reached a dead end with nothing left to try) -- NOT "was this specific call a
        /// success". Reaching full coverage on the last pair records a solution and then returns
        /// false unless the requested count has been reached, which is what makes ordinary
        /// backtracking keep hunting for a genuinely different arrangement (see the class doc).
        /// </summary>
        private static bool Search(SearchContext ctx, SolverState state, int pairIndex, Block currentCell,
            Direction entryDir)
        {
            if (++ctx.Steps > ctx.MaxSteps) { throw new BudgetExceededException(); }

            int pairId = ctx.PairIds[pairIndex];
            // The pair's target dot, fixed for the whole search -- always index 1, since every
            // caller (Solve, and the pair-transition below) starts a pair's search from index 0.
            // This must NOT be recomputed from currentCell as the path wanders: "the dot that
            // isn't wherever I'm currently standing" gives the wrong answer the instant the path
            // actually reaches the target, since at that point it "isn't currentCell" either.
            Block dotB = ctx.Dots[pairId][1];

            if (currentCell == dotB)
            {
                if (!CheckpointsSatisfied(state, pairId, ctx.CheckpointsByPair)) { return false; }

                if (pairIndex + 1 == ctx.PairIds.Count)
                {
                    if (!ctx.AllowPartialCoverage && !state.IsFullyCovered()) { return false; }

                    ctx.FoundSolutions.Add(state.BuildSolutions(ctx.PairIds));
                    return ctx.FoundSolutions.Count >= ctx.MaxSolutions;
                }

                int nextPairId = ctx.PairIds[pairIndex + 1];
                Block nextStart = ctx.Dots[nextPairId][0];
                Block nextEnd = OtherDot(ctx.Dots[nextPairId], nextStart);

                // A cheap, direction-agnostic reachability check: if the next pair's own two dots
                // are already cut off from each other by what has been placed so far, there is no
                // point recursing deeper just to discover that many steps later.
                if (!IsOptimisticallyReachable(ctx, state, nextStart, nextEnd, nextPairId))
                {
                    return false;
                }

                state.Occupy(nextStart, nextPairId, Direction.None);
                bool stop = Search(ctx, state, pairIndex + 1, nextStart, Direction.None);
                if (!stop) { state.Release(nextStart, nextPairId, Direction.None); }
                return stop;
            }

            // Collected up front, rather than tried one at a time, so DecisionPointCount/
            // DeadEndCount reflect how many options genuinely existed at this cell -- not just
            // whether the first one tried happened to work.
            List<Direction> legalMoves = null;
            List<Block> legalNeighbors = null;
            for (int i = 0; i < Directions.Length; i++)
            {
                Direction dir = Directions[i];
                Block neighbor = BoardTopology.Neighbor(state.Grid, state.Rows, state.Cols, currentCell, dir);
                if (neighbor == null) { continue; }
                if (currentCell.HasWall(dir) || neighbor.HasWall(BoardTopology.Opposite(dir))) { continue; }
                if (!currentCell.CanExitFrom(entryDir, dir)) { continue; }
                if (!state.CanEnter(neighbor, pairId, dir)) { continue; }

                if (legalMoves == null) { legalMoves = new List<Direction>(4); legalNeighbors = new List<Block>(4); }
                legalMoves.Add(dir);
                legalNeighbors.Add(neighbor);
            }

            if (legalMoves == null) { ctx.DeadEndCount++; return false; }
            if (legalMoves.Count > 1) { ctx.DecisionPointCount++; }

            for (int i = 0; i < legalMoves.Count; i++)
            {
                Direction dir = legalMoves[i];
                Block neighbor = legalNeighbors[i];

                state.Occupy(neighbor, pairId, dir);

                // Cheapest possible rejection: if this move just sealed a neighbouring empty cell
                // off, no continuation of it can cover the board, so do not recurse at all.
                if (ctx.StrandPruningEnabled
                    && (StrandsAnEmptyNeighbour(ctx, state, neighbor, neighbor)
                        || !RemainingPairsStillConnectable(ctx, state, pairIndex, neighbor)))
                {
                    state.Release(neighbor, pairId, dir);
                    continue;
                }

                bool stop = Search(ctx, state, pairIndex, neighbor, dir);
                if (stop) { return true; }

                state.Release(neighbor, pairId, dir);
            }

            return false;
        }

        /// <summary>
        /// Whether <paramref name="end"/> is still reachable from <paramref name="start"/>
        /// through cells that would currently accept <paramref name="pairId"/> -- ignoring
        /// direction-sensitive rules (One-Way, Arrow) entirely. That makes this an optimistic
        /// over-approximation: it can only ever call a truly-blocked route "reachable" by mistake,
        /// never the reverse, which is exactly what a safe prune needs -- it must never reject a
        /// branch that the real, fully-constrained search could still solve.
        /// </summary>
        /// <summary>
        /// Whether every pair that still has to be routed can still reach its own other dot
        /// through cells that are free right now.
        ///
        /// <b>This is the prune that makes big boards possible.</b> Before it, the only reachability
        /// test ran once per PAIR TRANSITION, so the search could fill dozens of cells -- and branch
        /// on every one -- after the board had already been cut in two, discovering the problem only
        /// when it eventually ran out of room. On a 9x9 the solver could not find even one solution
        /// inside eight million steps. Checking after every move instead means a move that severs
        /// the board is abandoned immediately.
        ///
        /// One flood fill labels the connected components of free cells, then each unrouted pair is
        /// asked whether its two dots carry the same label. The current pair is asked whether its
        /// head can still reach its target. O(cells) per move, which is worth it many times over
        /// against the subtrees it removes.
        ///
        /// Sound in the same way the stranded-cell prune is: it only ever reports "these dots are
        /// already disconnected", which no continuation could repair, since paths never free a cell
        /// they have taken.
        /// </summary>
        private static bool RemainingPairsStillConnectable(SearchContext ctx, SolverState state,
            int pairIndex, Block head)
        {
            int rows = state.Rows;
            int cols = state.Cols;
            int stamp = ++ctx.ComponentStamp;
            int[,] component = ctx.Component;
            int[] stack = ctx.FloodStack;

            // Component ids are (stamp, id) packed as stamp * 64 + id so the scratch array never
            // needs clearing between moves; anything not written this move reads as a stale stamp.
            int nextId = 0;
            int[,] label = component;

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    Block seed = state.Grid[i, j];
                    if (seed == null || !state.IsUnoccupied(seed)) { continue; }
                    if (label[i, j] / 64 == stamp) { continue; }

                    int id = ++nextId;
                    int top = 0;
                    stack[top++] = i * cols + j;
                    label[i, j] = stamp * 64 + id;

                    while (top > 0)
                    {
                        int packed = stack[--top];
                        int r = packed / cols, c = packed % cols;
                        Block cell = state.Grid[r, c];

                        for (int d = 0; d < Directions.Length; d++)
                        {
                            Direction dir = Directions[d];
                            Block next = BoardTopology.Neighbor(state.Grid, rows, cols, cell, dir);
                            if (next == null) { continue; }
                            if (cell.HasWall(dir) || next.HasWall(BoardTopology.Opposite(dir))) { continue; }
                            if (!state.IsUnoccupied(next)) { continue; }

                            int nr = next.Row_ID, nc = next.Coloum_ID;
                            if (label[nr, nc] / 64 == stamp) { continue; }
                            label[nr, nc] = stamp * 64 + id;
                            stack[top++] = nr * cols + nc;
                        }
                    }
                }
            }

            // The pair being routed: its head must still touch a component holding its target,
            // or be next to the target outright.
            int currentPairId = ctx.PairIds[pairIndex];
            Block target = ctx.Dots[currentPairId][1];
            if (state.IsUnoccupied(target) && !TouchesComponentOf(ctx, state, head, target, stamp))
            {
                return false;
            }

            // Pairs not started yet: both their dots are still free, so they must share a label.
            for (int k = pairIndex + 1; k < ctx.PairIds.Count; k++)
            {
                int pid = ctx.PairIds[k];
                Block a = ctx.Dots[pid][0];
                Block b = ctx.Dots[pid][1];
                if (!state.IsUnoccupied(a) || !state.IsUnoccupied(b)) { continue; }

                if (component[a.Row_ID, a.Coloum_ID] != component[b.Row_ID, b.Coloum_ID])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether <paramref name="from"/> has a free neighbour sharing
        /// <paramref name="target"/>'s component, or is adjacent to the target itself.</summary>
        private static bool TouchesComponentOf(SearchContext ctx, SolverState state, Block from,
            Block target, int stamp)
        {
            int want = ctx.Component[target.Row_ID, target.Coloum_ID];

            for (int d = 0; d < Directions.Length; d++)
            {
                Direction dir = Directions[d];
                Block next = BoardTopology.Neighbor(state.Grid, state.Rows, state.Cols, from, dir);
                if (next == null) { continue; }
                if (from.HasWall(dir) || next.HasWall(BoardTopology.Opposite(dir))) { continue; }
                if (next == target) { return true; }
                if (!state.IsUnoccupied(next)) { continue; }
                if (ctx.Component[next.Row_ID, next.Coloum_ID] == want) { return true; }
            }

            return false;
        }

        /// <summary>
        /// Whether any cell on the board can hold more than one path -- a Bridge, or a dot shared
        /// by several pairs. Their presence disables the stranded-cell prune; see
        /// SearchContext.StrandPruningEnabled.
        /// </summary>
        private static bool BoardHasSharedCapacityCells(Block[,] grid, int rowCount, int colCount)
        {
            for (int i = 0; i < rowCount; i++)
            {
                for (int j = 0; j < colCount; j++)
                {
                    Block cell = grid[i, j];
                    if (cell == null) { continue; }
                    if (cell.BlockType == BlockType.Bridge || cell.IsSharedGoal) { return true; }
                }
            }
            return false;
        }

        /// <summary>
        /// Whether the move just made orphaned a neighbouring empty cell.
        ///
        /// Under full coverage every empty cell has to end up on some path, and a path that runs
        /// THROUGH a cell needs two ways out of it -- one to enter by, one to leave by. A dot needs
        /// only one, being where a path stops. So an empty cell whose available connections have
        /// dropped below that threshold can never be covered, and the whole branch is dead however
        /// deep it is pursued.
        ///
        /// Only the four neighbours of the cell just filled are examined, because nothing else on
        /// the board changed. That keeps the check O(16) per move while catching the strand at the
        /// move that caused it, rather than thousands of steps later when the search finally runs
        /// out of room -- the difference between 469 ms and something usable on a full 8x8.
        ///
        /// <paramref name="head"/> is the path's current tip, which counts as an available
        /// connection for its neighbours: the path can still grow out of it.
        /// </summary>
        private static bool StrandsAnEmptyNeighbour(SearchContext ctx, SolverState state, Block filled, Block head)
        {
            for (int i = 0; i < Directions.Length; i++)
            {
                Block cell = BoardTopology.Neighbor(state.Grid, state.Rows, state.Cols, filled, Directions[i]);
                if (cell == null || !state.IsUnoccupied(cell)) { continue; }

                int available = 0;
                for (int j = 0; j < Directions.Length; j++)
                {
                    Direction dir = Directions[j];
                    Block other = BoardTopology.Neighbor(state.Grid, state.Rows, state.Cols, cell, dir);
                    if (other == null) { continue; }
                    // A wall seals the edge for every pair, so it removes the connection outright.
                    if (cell.HasWall(dir) || other.HasWall(BoardTopology.Opposite(dir))) { continue; }

                    if (state.IsUnoccupied(other) || other == head) { available++; }
                }

                int needed = cell.IsPairBlock ? 1 : 2;
                if (available < needed) { return true; }
            }

            return false;
        }

        private static bool IsOptimisticallyReachable(SearchContext ctx, SolverState state, Block start, Block end,
            int pairId)
        {
            if (start == end) { return true; }

            bool[,] seen = new bool[state.Rows, state.Cols];
            Queue<Block> queue = new Queue<Block>();
            seen[start.Row_ID, start.Coloum_ID] = true;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                if (++ctx.Steps > ctx.MaxSteps) { throw new BudgetExceededException(); }

                Block current = queue.Dequeue();

                for (int i = 0; i < Directions.Length; i++)
                {
                    Direction dir = Directions[i];
                    Block next = BoardTopology.Neighbor(state.Grid, state.Rows, state.Cols, current, dir);
                    if (next == null || seen[next.Row_ID, next.Coloum_ID]) { continue; }
                    if (current.HasWall(dir) || next.HasWall(BoardTopology.Opposite(dir))) { continue; }
                    if (!state.CanPassOptimistically(next, pairId)) { continue; }

                    if (next == end) { return true; }

                    seen[next.Row_ID, next.Coloum_ID] = true;
                    queue.Enqueue(next);
                }
            }

            return false;
        }

        private static bool CheckpointsSatisfied(SolverState state, int pairId,
            Dictionary<int, List<(int, int)>> checkpointsByPair)
        {
            if (!checkpointsByPair.TryGetValue(pairId, out List<(int, int)> checkpoints)) { return true; }

            List<(int, int)> path = state.PathOf(pairId);
            for (int i = 0; i < checkpoints.Count; i++)
            {
                if (!path.Contains(checkpoints[i])) { return false; }
            }
            return true;
        }

        private static Dictionary<int, List<(int, int)>> CollectCheckpoints(Block[,] grid, int rowCount, int colCount)
        {
            Dictionary<int, List<(int, int)>> result = new Dictionary<int, List<(int, int)>>();

            for (int i = 0; i < rowCount; i++)
            {
                for (int j = 0; j < colCount; j++)
                {
                    Block cell = grid[i, j];
                    if (cell == null || cell.BlockType != BlockType.Checkpoint) { continue; }

                    if (!result.TryGetValue(cell.PairId, out List<(int, int)> list))
                    {
                        list = new List<(int, int)>();
                        result[cell.PairId] = list;
                    }
                    list.Add((i, j));
                }
            }

            return result;
        }

        private static Block OtherDot(List<Block> dotsOfPair, Block current)
        {
            return ReferenceEquals(dotsOfPair[0], current) ? dotsOfPair[1] : dotsOfPair[0];
        }

        private static int PairSpan(List<Block> dotsOfPair)
        {
            Block a = dotsOfPair[0];
            Block b = dotsOfPair[1];
            return Math.Abs(a.Row_ID - b.Row_ID) + Math.Abs(a.Coloum_ID - b.Coloum_ID);
        }

        /// <summary>
        /// The solver's own scratch bookkeeping for "who occupies this cell right now" during the
        /// search. Deliberately independent of Block's real occupant-tracking fields: those exist
        /// to drive gameplay visuals (see Block.AddOccupant/HighlightBlockDirection), and mutating
        /// them here would either crash on a bare non-visual Block or paint a live board with
        /// scratch state while it searches -- neither of which the search should ever do.
        /// </summary>
        private sealed class SolverState
        {
            public readonly Block[,] Grid;
            public readonly int Rows;
            public readonly int Cols;

            // Every cell except a Bridge or a shared-destination dot holds at most one pair, so a
            // single id (0 = free) is enough. Bridge and dot cells are tracked separately below,
            // since both can legitimately hold more than one pair at once.
            private readonly int[,] soleOccupant;
            private readonly List<int>[,] dotOccupants;
            private readonly int[,] bridgeHorizontalOwner;
            private readonly int[,] bridgeVerticalOwner;
            private readonly Dictionary<int, List<(int Row, int Col)>> pathSoFar;

            public SolverState(Block[,] grid, int rows, int cols)
            {
                Grid = grid;
                Rows = rows;
                Cols = cols;
                soleOccupant = new int[rows, cols];
                dotOccupants = new List<int>[rows, cols];
                bridgeHorizontalOwner = new int[rows, cols];
                bridgeVerticalOwner = new int[rows, cols];
                pathSoFar = new Dictionary<int, List<(int, int)>>();
            }

            /// <summary>
            /// Whether <paramref name="pairId"/> may step into <paramref name="cell"/> while
            /// moving <paramref name="incomingDir"/>: Block's own admission rules, plus whichever
            /// capacity this cell has left given who is already here.
            /// </summary>
            public bool CanEnter(Block cell, int pairId, Direction incomingDir)
            {
                if (!cell.CanEnter(pairId)) { return false; }
                if (!cell.CanEnterFrom(incomingDir)) { return false; }

                if (cell.BlockType == BlockType.Bridge)
                {
                    bool horizontal = IsHorizontal(incomingDir);
                    int owner = horizontal
                        ? bridgeHorizontalOwner[cell.Row_ID, cell.Coloum_ID]
                        : bridgeVerticalOwner[cell.Row_ID, cell.Coloum_ID];
                    return owner == 0 || owner == pairId;
                }

                if (cell.IsPairBlock)
                {
                    // Only ever true for one of the pair's own two dots -- LevelValidator's
                    // ValidateDotCounts guarantees exactly two, and a well-formed path only ever
                    // asks to enter the OTHER one, since its own start is already occupied.
                    if (!cell.IsDotFor(pairId)) { return false; }
                    List<int> occupants = dotOccupants[cell.Row_ID, cell.Coloum_ID];
                    return occupants == null || !occupants.Contains(pairId);
                }

                return soleOccupant[cell.Row_ID, cell.Coloum_ID] == 0;
            }

            /// <summary>
            /// Same admission question as <see cref="CanEnter"/>, but ignoring the direction-only
            /// rules (One-Way, Arrow) entirely -- see IsOptimisticallyReachable, the only caller.
            /// </summary>
            public bool CanPassOptimistically(Block cell, int pairId)
            {
                if (cell.BlockType == BlockType.Blocked) { return false; }
                if (!cell.CanEnter(pairId)) { return false; }

                if (cell.BlockType == BlockType.Bridge)
                {
                    return bridgeHorizontalOwner[cell.Row_ID, cell.Coloum_ID] == 0
                        || bridgeVerticalOwner[cell.Row_ID, cell.Coloum_ID] == 0;
                }

                if (cell.IsPairBlock)
                {
                    if (!cell.IsDotFor(pairId)) { return false; }
                    List<int> occupants = dotOccupants[cell.Row_ID, cell.Coloum_ID];
                    return occupants == null || !occupants.Contains(pairId);
                }

                return soleOccupant[cell.Row_ID, cell.Coloum_ID] == 0;
            }

            public void Occupy(Block cell, int pairId, Direction incomingDir)
            {
                if (cell.BlockType == BlockType.Bridge)
                {
                    if (IsHorizontal(incomingDir)) { bridgeHorizontalOwner[cell.Row_ID, cell.Coloum_ID] = pairId; }
                    else { bridgeVerticalOwner[cell.Row_ID, cell.Coloum_ID] = pairId; }
                }
                else if (cell.IsPairBlock)
                {
                    List<int> occupants = dotOccupants[cell.Row_ID, cell.Coloum_ID];
                    if (occupants == null)
                    {
                        occupants = new List<int>();
                        dotOccupants[cell.Row_ID, cell.Coloum_ID] = occupants;
                    }
                    occupants.Add(pairId);
                }
                else
                {
                    soleOccupant[cell.Row_ID, cell.Coloum_ID] = pairId;
                }

                if (!pathSoFar.TryGetValue(pairId, out List<(int, int)> path))
                {
                    path = new List<(int, int)>();
                    pathSoFar[pairId] = path;
                }
                path.Add((cell.Row_ID, cell.Coloum_ID));
            }

            public void Release(Block cell, int pairId, Direction incomingDir)
            {
                if (cell.BlockType == BlockType.Bridge)
                {
                    if (IsHorizontal(incomingDir)) { bridgeHorizontalOwner[cell.Row_ID, cell.Coloum_ID] = 0; }
                    else { bridgeVerticalOwner[cell.Row_ID, cell.Coloum_ID] = 0; }
                }
                else if (cell.IsPairBlock)
                {
                    dotOccupants[cell.Row_ID, cell.Coloum_ID]?.Remove(pairId);
                }
                else
                {
                    soleOccupant[cell.Row_ID, cell.Coloum_ID] = 0;
                }

                List<(int, int)> path = pathSoFar[pairId];
                path.RemoveAt(path.Count - 1);
            }

            /// <summary>
            /// Whether <paramref name="cell"/> still has nothing in it. Used only by the
            /// stranded-cell prune, which is disabled on boards carrying Bridge or shared-goal
            /// cells -- those hold more than one occupant, so "empty" stops being a yes/no.
            /// </summary>
            public bool IsUnoccupied(Block cell)
            {
                if (cell.BlockType == BlockType.Blocked) { return false; }
                if (cell.IsPairBlock)
                {
                    List<int> occupants = dotOccupants[cell.Row_ID, cell.Coloum_ID];
                    return occupants == null || occupants.Count == 0;
                }
                return soleOccupant[cell.Row_ID, cell.Coloum_ID] == 0;
            }

            public List<(int, int)> PathOf(int pairId)
            {
                return pathSoFar.TryGetValue(pairId, out List<(int, int)> path) ? path : new List<(int, int)>();
            }

            /// <summary>
            /// Whether every usable cell currently has an occupant -- the same rule
            /// GamePlayController.IsBoardFullyCovered checks against the live board, evaluated
            /// here against the solver's own scratch occupancy instead.
            /// </summary>
            public bool IsFullyCovered()
            {
                return BoardTopology.IsFullyCovered(Grid, Rows, Cols, cell =>
                {
                    if (cell.BlockType == BlockType.Bridge)
                    {
                        return bridgeHorizontalOwner[cell.Row_ID, cell.Coloum_ID] != 0
                            || bridgeVerticalOwner[cell.Row_ID, cell.Coloum_ID] != 0;
                    }
                    if (cell.IsPairBlock)
                    {
                        List<int> occupants = dotOccupants[cell.Row_ID, cell.Coloum_ID];
                        return occupants != null && occupants.Count > 0;
                    }
                    return soleOccupant[cell.Row_ID, cell.Coloum_ID] != 0;
                });
            }

            public List<PairSolution> BuildSolutions(List<int> pairIds)
            {
                List<PairSolution> result = new List<PairSolution>();
                for (int i = 0; i < pairIds.Count; i++)
                {
                    result.Add(new PairSolution { PairId = pairIds[i], Cells = new List<(int, int)>(PathOf(pairIds[i])) });
                }
                return result;
            }

            private static bool IsHorizontal(Direction dir)
            {
                return dir == Direction.Left || dir == Direction.Right;
            }
        }
    }
}
