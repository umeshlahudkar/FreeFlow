using System.Collections.Generic;
using FreeFlow.Enums;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Rates a board the way a PERSON experiences it: by which deductions it forces, how many
    /// places each deduction could be applied, and how often pure reasoning runs out.
    ///
    /// <b>Why this exists alongside PuzzleSolver.</b> PuzzleSolver answers "is this board valid" --
    /// solvable, uniquely, covering every cell. It answers that by brute-force search, and its
    /// counters (steps, decision points, dead ends) measure how hard the SEARCH was. Five separate
    /// attempts to rate difficulty from those counters all failed against playtesting, and the
    /// research says why. Pelánek evaluated every published Sudoku difficulty metric against real
    /// human solving times on two independent portals; backtracking scored r = 0.16-0.25, the
    /// WORST family in the table, while a model of human deduction reached 0.83. Search effort is
    /// the field's signal for a defect, not for difficulty. We were optimising it.
    ///
    /// That same table names the two things this class measures, because neither is search volume:
    ///
    ///   - <b>Refutation</b> (r = 0.68-0.83) -- how often deduction stalls and something must be
    ///     assumed. Reported here as <see cref="Rating.Assumptions"/>.
    ///   - <b>Dependency</b> (r = 0.67-0.69) -- how many places a technique could be applied at
    ///     each step. Ten applicable cells means the board carries itself and the solver may start
    ///     anywhere; two means every step has to be found. Reported as
    ///     <see cref="Rating.Dependency"/>, the mean over the opening rounds.
    ///
    /// <b>Neither is meant to be used alone.</b> No single metric in the literature reaches the
    /// headline 0.95 -- a four-metric linear blend does. See <c>DifficultyModel</c>, which combines
    /// these with the relaxation metrics and tangle.
    ///
    /// <b>Caveat.</b> Constraint-propagation models are easy to formalise for Sudoku because its
    /// rules are simple; the literature is explicit that for Nurikabe-like puzzles "it can be
    /// difficult to formulate suitable constraint propagation rules". Flow is the Nurikabe case, so
    /// this hierarchy is built rather than borrowed. The technique set is deliberately incomplete:
    /// parity, cell budget and corridor priority are all real player techniques that are not here
    /// yet, and every one of them missing inflates <see cref="Rating.Assumptions"/>.
    /// </summary>
    public static class HumanSolver
    {
        /// <summary>
        /// Deduction techniques, ordered by how much work they are for a person. The rating is the
        /// hardest one a board forces, which is exactly how Sudoku generators grade.
        /// </summary>
        public enum Technique
        {
            /// <summary>Nothing was needed -- the board was already complete.</summary>
            None = 0,

            /// <summary>A path's head has exactly one legal square to move to. The cheapest
            /// deduction there is; a board solvable by this alone is trivial.</summary>
            ForcedContinuation = 1,

            /// <summary>An empty square has only two ways in or out, so whatever covers it must use
            /// both -- and if one of them is a path's head, that path is the only thing that can
            /// cover it. Requires looking at a cell rather than at a path.</summary>
            ForcedByDegree = 2,

            /// <summary>The corner dual law: a placed turn constrains the square diagonally inside
            /// it, arbitrarily far from either path's head. Whole-board reasoning, and the one
            /// technique here that a casual player genuinely may not see.
            /// See <see cref="CornerDualScan"/> for the law and its proof.</summary>
            CornerDual = 3,

            /// <summary>Deduction ran out entirely and something had to be assumed. This is where
            /// a player says "suppose it goes this way" and follows the consequences.</summary>
            Assumption = 4
        }

        public sealed class Rating
        {
            public bool Solved;

            /// <summary>The hardest technique the board forced.</summary>
            public Technique Hardest;

            /// <summary>How many times pure deduction stalled and a guess was required. Pelánek's
            /// refutation measure, the best-correlating single metric in his table (0.68-0.83).
            /// Zero means the board can be reasoned through end to end.</summary>
            public int Assumptions;

            /// <summary>Mean number of places a technique could have been applied, per round of
            /// deduction, over the opening rounds. LOW IS HARD: two openings means every step must
            /// be found, ten means the board plays itself. Pelánek's dependency measure
            /// (0.67-0.69), and the one our earlier metrics were ranking backwards -- a board with
            /// many simultaneous forced moves has high solver-decision counts and low
            /// dependency.</summary>
            public float Dependency;

            /// <summary>Rounds of top-level propagation, i.e. how far deduction got before the
            /// first guess. Dependency is averaged over at most <see cref="DependencySampleCap"/>
            /// of these.</summary>
            public int DeductionRounds;

            /// <summary>Total rounds of propagation including those inside assumption branches.</summary>
            public int PropagationRounds;

            /// <summary>Cells filled by deduction alone, before any guess was needed.</summary>
            public int CellsBeforeFirstGuess;

            public int UsableCells;

            public override string ToString()
            {
                return (Solved ? "solved" : "UNSOLVED")
                    + "  hardest=" + Hardest
                    + "  assumptions=" + Assumptions
                    + "  dependency=" + Dependency.ToString("0.00")
                    + "  deductionRounds=" + DeductionRounds
                    + "  rounds=" + PropagationRounds
                    + "  deducedBeforeGuess=" + CellsBeforeFirstGuess + "/" + UsableCells;
            }
        }

        /// <summary>Pelánek averages dependency over the first 20-30 steps; beyond that the measure
        /// stops improving. 30 is the top of his useful range.</summary>
        public const int DependencySampleCap = 30;

        private sealed class State
        {
            public Block[,] Grid;
            public int Rows;
            public int Cols;
            public int[,] Owner;            // 0 = empty, else pair id
            public bool[,] IsDot;           // an endpoint: one connection, exempt from the corner law
            public int[,] EnterDir;         // 0 = none, else 1 + index into Directions (travel INTO)
            public int[,] ExitDir;          // 0 = none, else 1 + index into Directions (travel OUT)
            public Dictionary<int, Block> Head;
            public Dictionary<int, Block> Target;
            public HashSet<int> Finished;
            public HashSet<int> Banned;     // edge keys; see BanKey
            public bool UseCornerDual;
            public int Filled;
            public int Usable;

            public State Clone()
            {
                return new State
                {
                    Grid = Grid,
                    Rows = Rows,
                    Cols = Cols,
                    Owner = (int[,])Owner.Clone(),
                    IsDot = IsDot,
                    EnterDir = (int[,])EnterDir.Clone(),
                    ExitDir = (int[,])ExitDir.Clone(),
                    Head = new Dictionary<int, Block>(Head),
                    Target = Target,
                    Finished = new HashSet<int>(Finished),
                    Banned = new HashSet<int>(Banned),
                    UseCornerDual = UseCornerDual,
                    Filled = Filled,
                    Usable = Usable
                };
            }

            /// <summary>
            /// A banned EDGE, not a banned cell.
            ///
            /// The first version keyed bans by (pair, cell), which said "this colour may never
            /// occupy that square". That is far stronger than the corner law actually claims -- the
            /// law forbids one specific link, D to B, and says nothing about the colour reaching D
            /// from some other side later. The over-restriction cut real moves out of the search
            /// and five provably solvable boards came back UNSOLVED.
            ///
            /// Keying by edge needs no pair id: the only cell that can move along B-D is whichever
            /// path already occupies B, so banning the edge bans exactly that one move.
            /// </summary>
            public int BanKey(Block from, Block to)
            {
                int cells = Rows * Cols;
                return (from.Row_ID * Cols + from.Coloum_ID) * cells + to.Row_ID * Cols + to.Coloum_ID;
            }
        }

        private static readonly Direction[] Directions =
        {
            Direction.Left, Direction.Right, Direction.Up, Direction.Down
        };

        /// <summary>
        /// Rates <paramref name="grid"/>. <paramref name="maxAssumptions"/> bounds how deep the
        /// guessing may go before the board is reported unsolved -- a board needing more than a
        /// handful of assumptions is past what a person will reason through anyway.
        /// </summary>
        /// <param name="assumeNoSelfTouch">Whether the corner dual law may be used. It is valid
        /// ONLY on boards whose solution has no link touching itself, so a caller that has not
        /// established that must pass false -- see <see cref="CornerDualScan"/>. Leaving it true on
        /// a self-touching board does not merely weaken the rating, it makes the board come back
        /// UNSOLVED, because the law will keep deriving contradictions from the very shape the
        /// solution relies on.</param>
        public static Rating Rate(Block[,] grid, int rowCount, int colCount, int maxAssumptions = 6,
            bool assumeNoSelfTouch = true)
        {
            Dictionary<int, List<Block>> dots = BoardTopology.CollectDots(grid, rowCount, colCount);

            State state = new State
            {
                Grid = grid,
                Rows = rowCount,
                Cols = colCount,
                Owner = new int[rowCount, colCount],
                IsDot = new bool[rowCount, colCount],
                EnterDir = new int[rowCount, colCount],
                ExitDir = new int[rowCount, colCount],
                Head = new Dictionary<int, Block>(),
                Target = new Dictionary<int, Block>(),
                Finished = new HashSet<int>(),
                Banned = new HashSet<int>(),
                UseCornerDual = assumeNoSelfTouch,
                Filled = 0,
                Usable = 0
            };

            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < colCount; c++)
                {
                    if (grid[r, c] != null && grid[r, c].BlockType != BlockType.Blocked) { state.Usable++; }
                }
            }

            foreach (KeyValuePair<int, List<Block>> kv in dots)
            {
                if (kv.Value.Count != 2) { continue; }
                state.Head[kv.Key] = kv.Value[0];
                state.Target[kv.Key] = kv.Value[1];
                state.Owner[kv.Value[0].Row_ID, kv.Value[0].Coloum_ID] = kv.Key;
                state.Owner[kv.Value[1].Row_ID, kv.Value[1].Coloum_ID] = kv.Key;
                state.IsDot[kv.Value[0].Row_ID, kv.Value[0].Coloum_ID] = true;
                state.IsDot[kv.Value[1].Row_ID, kv.Value[1].Coloum_ID] = true;
                state.Filled += 2;
            }

            Rating rating = new Rating
            {
                Hardest = Technique.None,
                UsableCells = state.Usable,
                CellsBeforeFirstGuess = -1
            };

            int dependencySum = 0;
            int dependencySamples = 0;

            bool solved = Search(state, rating, 0, maxAssumptions, ref dependencySum, ref dependencySamples);

            rating.Solved = solved;
            rating.Dependency = dependencySamples > 0 ? dependencySum / (float)dependencySamples : 0f;
            if (rating.CellsBeforeFirstGuess < 0) { rating.CellsBeforeFirstGuess = state.Usable; }
            return rating;
        }

        /// <summary>
        /// Propagate as far as pure deduction goes; only then assume. Mirrors how a person plays --
        /// take everything that is certain, and guess only when nothing is.
        /// </summary>
        /// <remarks>
        /// Dependency is sampled at <paramref name="depth"/> 0 only. Rounds below that happen after
        /// a guess, so they measure the branch rather than the board the player was handed.
        /// </remarks>
        private static bool Search(State state, Rating rating, int depth, int maxAssumptions,
            ref int dependencySum, ref int dependencySamples)
        {
            bool measure = depth == 0;
            if (!Propagate(state, rating, measure, ref dependencySum, ref dependencySamples))
            {
                return false;
            }

            if (state.Filled >= state.Usable && state.Finished.Count == state.Head.Count)
            {
                return true;
            }

            if (depth >= maxAssumptions) { return false; }

            // Deduction is exhausted. Record how far it got the FIRST time this happens, which is
            // the honest "how much of this board can be reasoned out" figure.
            if (rating.CellsBeforeFirstGuess < 0) { rating.CellsBeforeFirstGuess = state.Filled; }

            int pairId = MostConstrainedUnfinished(state);
            if (pairId == 0) { return false; }

            List<Block> options = LegalMoves(state, pairId);
            for (int i = 0; i < options.Count; i++)
            {
                State branch = state.Clone();
                Apply(branch, pairId, options[i]);

                Rating probe = new Rating
                {
                    Hardest = Technique.None,
                    UsableCells = state.Usable,
                    CellsBeforeFirstGuess = -1
                };
                int ignoredSum = 0, ignoredSamples = 0;
                if (Search(branch, probe, depth + 1, maxAssumptions, ref ignoredSum, ref ignoredSamples))
                {
                    rating.Assumptions = depth + 1;
                    if (probe.Assumptions > rating.Assumptions) { rating.Assumptions = probe.Assumptions; }
                    if (probe.Hardest > rating.Hardest) { rating.Hardest = probe.Hardest; }
                    if (rating.Hardest < Technique.Assumption) { rating.Hardest = Technique.Assumption; }
                    rating.PropagationRounds += probe.PropagationRounds;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Applies every technique in order, cheapest first, restarting from the cheapest whenever
        /// one fires. Returns false on a contradiction.
        /// </summary>
        private static bool Propagate(State state, Rating rating, bool measure,
            ref int dependencySum, ref int dependencySamples)
        {
            bool progress = true;
            while (progress)
            {
                progress = false;
                rating.PropagationRounds++;
                if (measure) { rating.DeductionRounds++; }

                if (!Consistent(state)) { return false; }
                if (!NoUnreachableCell(state)) { return false; }

                // Sampled BEFORE anything fires, so it counts the openings the player is looking at
                // in this position rather than what is left after one is taken.
                if (measure && dependencySamples < DependencySampleCap)
                {
                    int sites = CountFiringSites(state);
                    if (sites > 0)
                    {
                        dependencySum += sites;
                        dependencySamples++;
                    }
                }

                if (ApplyForcedContinuation(state, rating)) { progress = true; continue; }
                if (ApplyForcedByDegree(state, rating)) { progress = true; continue; }

                if (state.UseCornerDual)
                {
                    int corner = ApplyCornerDual(state, rating);
                    if (corner < 0) { return false; }
                    if (corner > 0) { progress = true; continue; }
                }
            }
            return Consistent(state);
        }

        /// <summary>
        /// How many distinct deductions are available right now, across every technique. This is
        /// the raw quantity behind <see cref="Rating.Dependency"/>: a position offering one
        /// deduction has to be found, a position offering twelve does not.
        /// </summary>
        private static int CountFiringSites(State state)
        {
            List<(int Pair, Block Cell)> sites = new List<(int, Block)>();
            FindForcedContinuations(state, sites);
            FindForcedByDegree(state, sites);
            if (!state.UseCornerDual) { return sites.Count; }

            CornerDualScan(state, sites, out bool contradiction, out bool _, true);
            return contradiction ? 0 : sites.Count;
        }

        /// <summary>Technique 1: a head with exactly one legal move has to take it.</summary>
        private static bool ApplyForcedContinuation(State state, Rating rating)
        {
            List<(int Pair, Block Cell)> sites = new List<(int, Block)>();
            FindForcedContinuations(state, sites);
            if (sites.Count == 0) { return false; }

            Apply(state, sites[0].Pair, sites[0].Cell);
            Note(rating, Technique.ForcedContinuation);
            return true;
        }

        private static void FindForcedContinuations(State state, List<(int Pair, Block Cell)> into)
        {
            foreach (KeyValuePair<int, Block> kv in state.Head)
            {
                if (state.Finished.Contains(kv.Key)) { continue; }
                List<Block> moves = LegalMoves(state, kv.Key);
                if (moves.Count == 1) { into.Add((kv.Key, moves[0])); }
            }
        }

        /// <summary>
        /// Technique 2: an empty square with only two ways in or out must use both, so if one of
        /// them is a path's head then that path is the only thing that can ever cover it.
        /// </summary>
        private static bool ApplyForcedByDegree(State state, Rating rating)
        {
            List<(int Pair, Block Cell)> sites = new List<(int, Block)>();
            FindForcedByDegree(state, sites);
            if (sites.Count == 0) { return false; }

            Apply(state, sites[0].Pair, sites[0].Cell);
            Note(rating, Technique.ForcedByDegree);
            return true;
        }

        private static void FindForcedByDegree(State state, List<(int Pair, Block Cell)> into)
        {
            for (int r = 0; r < state.Rows; r++)
            {
                for (int c = 0; c < state.Cols; c++)
                {
                    if (state.Owner[r, c] != 0) { continue; }
                    Block cell = state.Grid[r, c];
                    if (cell == null || cell.BlockType == BlockType.Blocked) { continue; }

                    // An available connection is any neighbour a path could still come from or go
                    // to: an empty square, the HEAD of an unfinished path, or the TARGET dot of one
                    // (a path is allowed to end there). Omitting target dots undercounts the
                    // degree, which made three-way cells look like two-way ones and forced moves
                    // that were not actually forced -- boards then filled up with a head stranded
                    // away from its own target and came back UNSOLVED.
                    int free = 0;
                    int headPair = 0;
                    for (int d = 0; d < Directions.Length; d++)
                    {
                        Block n = Step(state, cell, Directions[d]);
                        if (n == null) { continue; }
                        int owner = state.Owner[n.Row_ID, n.Coloum_ID];
                        if (owner == 0) { free++; continue; }
                        if (state.Finished.Contains(owner)) { continue; }
                        if (IsHeadOf(state, n, owner)) { free++; headPair = owner; continue; }
                        if (state.Target.ContainsKey(owner) && state.Target[owner] == n) { free++; }
                    }

                    if (free != 2 || headPair == 0 || state.Finished.Contains(headPair)) { continue; }
                    if (LegalMoves(state, headPair).Contains(cell)) { into.Add((headPair, cell)); }
                }
            }
        }

        /// <summary>
        /// Technique 3, the corner dual law. The strongest deduction known for this puzzle, and the
        /// reason thomasahle's reference Numberlink solver handles 40x40 boards casually.
        ///
        /// <b>The law.</b> Let X be a cell where a path TURNS, using the edges u and v (say west
        /// and south). Let A = X+u and B = X+v be its two path neighbours, and let D = X+u+v be the
        /// square diagonally inside the turn. Then D is either a dot, or it turns the same way --
        /// using its own u and v edges.
        ///
        /// <b>Why.</b> D is adjacent to both A and B. It cannot connect to A: A already spends one
        /// connection on X, so a D-A link would put D on the same path three steps from B, which D
        /// touches -- a self-touch. The same argument rules out D-B. So D's only remaining edges
        /// are u and v, and an interior cell needs exactly two. Only a dot, needing one, escapes.
        ///
        /// <b>This holds only where no link touches itself</b> -- and OUR GENERATOR DOES NOT
        /// GUARANTEE THAT. Measured across ten Classic levels, five self-touch, and the correlation
        /// with this rule is exact: every self-touching board came back UNSOLVED and every clean one
        /// solved, 10 for 10. So the rule doubles as a defect detector, and callers must gate it on
        /// <c>StructuralGates.Report.SelfTouches == 0</c> rather than assume.
        ///
        /// Three things fall out, in rising order of usefulness:
        ///   - <b>contradiction</b> when D is on the turning path itself, or cannot reach its own
        ///     u and v neighbours, or is already turning some other way;
        ///   - <b>a ban</b>: an empty D may never be entered from A or B, which frequently cuts a
        ///     two-way head down to a forced one;
        ///   - <b>a forced move</b>: if D is some other path's head, its exit is determined -- it
        ///     came in along u or v, so it must leave along the other.
        /// </summary>
        /// <returns>-1 contradiction, 1 something fired, 0 nothing to do.</returns>
        private static int ApplyCornerDual(State state, Rating rating)
        {
            List<(int Pair, Block Cell)> forced = new List<(int, Block)>();
            CornerDualScan(state, forced, out bool contradiction, out bool newBans, false);

            if (contradiction) { return -1; }

            if (forced.Count > 0)
            {
                Apply(state, forced[0].Pair, forced[0].Cell);
                Note(rating, Technique.CornerDual);
                return 1;
            }

            if (newBans) { Note(rating, Technique.CornerDual); return 1; }
            return 0;
        }

        /// <param name="countOnly">Skip recording bans, so the dependency sampler does not mutate
        /// the state it is measuring.</param>
        private static void CornerDualScan(State state, List<(int Pair, Block Cell)> forced,
            out bool contradiction, out bool newBans, bool countOnly)
        {
            contradiction = false;
            newBans = false;

            for (int r = 0; r < state.Rows; r++)
            {
                for (int c = 0; c < state.Cols; c++)
                {
                    int pair = state.Owner[r, c];
                    if (pair == 0) { continue; }

                    int enter = state.EnterDir[r, c];
                    int exit = state.ExitDir[r, c];
                    if (enter == 0 || exit == 0) { continue; }      // a dot, or not yet a full cell

                    Block x = state.Grid[r, c];
                    Direction u = BoardTopology.Opposite(Directions[enter - 1]);   // edge back to A
                    Direction v = Directions[exit - 1];                            // edge on to B
                    if (SameAxis(u, v)) { continue; }                              // straight, not a turn

                    Block a = Neighbour(state, x, u);
                    if (a == null) { continue; }
                    Block d = Neighbour(state, a, v);
                    if (d == null || d.BlockType == BlockType.Blocked) { continue; }

                    // A dot needs one connection, so it is exempt -- this is the "or be a source"
                    // half of the law.
                    if (state.IsDot[d.Row_ID, d.Coloum_ID]) { continue; }

                    int ownerD = state.Owner[d.Row_ID, d.Coloum_ID];

                    // D on the turning path itself is exactly the self-touch the law forbids.
                    if (ownerD == pair) { contradiction = true; return; }

                    // Whatever covers D must be able to use both u and v; a wall or a blocked cell
                    // on either side leaves it no legal shape at all.
                    if (Step(state, d, u) == null || Step(state, d, v) == null)
                    {
                        contradiction = true;
                        return;
                    }

                    if (ownerD == 0)
                    {
                        // D is empty. It may never link to A or B, so forbid the only move that
                        // could do it: the turning path's own head stepping across from B.
                        Block b = Neighbour(state, x, v);
                        if (!countOnly && b != null && IsHeadOf(state, b, pair))
                        {
                            if (state.Banned.Add(state.BanKey(b, d))) { newBans = true; }
                        }
                        continue;
                    }

                    int enterD = state.EnterDir[d.Row_ID, d.Coloum_ID];
                    int exitD = state.ExitDir[d.Row_ID, d.Coloum_ID];
                    if (enterD == 0) { continue; }

                    Direction inEdge = BoardTopology.Opposite(Directions[enterD - 1]);
                    if (inEdge != u && inEdge != v) { contradiction = true; return; }

                    if (exitD != 0)
                    {
                        Direction outEdge = Directions[exitD - 1];
                        if (outEdge != u && outEdge != v) { contradiction = true; return; }
                        continue;
                    }

                    // D is another path's head and came in along one of the two permitted edges, so
                    // its exit is the other one. A forced move that no local rule could see.
                    if (state.Finished.Contains(ownerD)) { continue; }
                    Direction mustLeave = inEdge == u ? v : u;
                    Block next = Step(state, d, mustLeave);
                    if (next == null) { contradiction = true; return; }
                    if (LegalMoves(state, ownerD).Contains(next)) { forced.Add((ownerD, next)); }
                }
            }
        }

        /// <summary>
        /// A cell that no unfinished colour can still reach can never be filled, so this line of
        /// reasoning is already dead.
        /// </summary>
        /// <remarks>
        /// This used to also force a move when exactly one colour could reach a cell. That was
        /// unsound: "must eventually be covered by it" is not "must move there now", and treating
        /// the two as equivalent drove heads down routes that could not reach their own target, so
        /// provably solvable boards came back UNSOLVED. Only the contradiction half survives.
        /// </remarks>
        private static bool NoUnreachableCell(State state)
        {
            Dictionary<int, HashSet<int>> reach = new Dictionary<int, HashSet<int>>();
            foreach (KeyValuePair<int, Block> kv in state.Head)
            {
                if (state.Finished.Contains(kv.Key)) { continue; }
                reach[kv.Key] = FloodFrom(state, kv.Value, kv.Key);
            }

            for (int r = 0; r < state.Rows; r++)
            {
                for (int c = 0; c < state.Cols; c++)
                {
                    if (state.Owner[r, c] != 0) { continue; }
                    Block cell = state.Grid[r, c];
                    if (cell == null || cell.BlockType == BlockType.Blocked) { continue; }

                    int key = r * state.Cols + c;
                    bool reachable = false;
                    foreach (KeyValuePair<int, HashSet<int>> kv in reach)
                    {
                        if (kv.Value.Contains(key)) { reachable = true; break; }
                    }
                    if (!reachable) { return false; }
                }
            }
            return true;
        }

        /// <summary>Cheap contradiction check: an unfinished path with nowhere to go.</summary>
        private static bool Consistent(State state)
        {
            foreach (KeyValuePair<int, Block> kv in state.Head)
            {
                if (state.Finished.Contains(kv.Key)) { continue; }
                if (LegalMoves(state, kv.Key).Count == 0) { return false; }
            }
            return true;
        }

        private static HashSet<int> FloodFrom(State state, Block from, int pairId)
        {
            HashSet<int> seen = new HashSet<int>();
            Stack<Block> stack = new Stack<Block>();
            stack.Push(from);

            while (stack.Count > 0)
            {
                Block cur = stack.Pop();
                for (int d = 0; d < Directions.Length; d++)
                {
                    Block n = Step(state, cur, Directions[d]);
                    if (n == null) { continue; }
                    int owner = state.Owner[n.Row_ID, n.Coloum_ID];
                    if (owner != 0 && n != state.Target[pairId]) { continue; }
                    int key = n.Row_ID * state.Cols + n.Coloum_ID;
                    if (!seen.Add(key)) { continue; }
                    if (owner == 0) { stack.Push(n); }
                }
            }
            return seen;
        }

        private static List<Block> LegalMoves(State state, int pairId)
        {
            List<Block> moves = new List<Block>();
            if (state.Finished.Contains(pairId)) { return moves; }

            Block head = state.Head[pairId];
            Block target = state.Target[pairId];

            for (int d = 0; d < Directions.Length; d++)
            {
                Block n = Step(state, head, Directions[d]);
                if (n == null) { continue; }
                if (n == target) { moves.Add(n); continue; }
                if (state.Owner[n.Row_ID, n.Coloum_ID] != 0) { continue; }
                if (state.Banned.Contains(state.BanKey(head, n))) { continue; }
                moves.Add(n);
            }
            return moves;
        }

        private static void Apply(State state, int pairId, Block cell)
        {
            Block head = state.Head[pairId];
            int dir = DirectionIndexTo(state, head, cell);
            if (dir >= 0)
            {
                state.ExitDir[head.Row_ID, head.Coloum_ID] = dir + 1;
                state.EnterDir[cell.Row_ID, cell.Coloum_ID] = dir + 1;
            }

            if (cell == state.Target[pairId])
            {
                state.Finished.Add(pairId);
                return;
            }
            state.Owner[cell.Row_ID, cell.Coloum_ID] = pairId;
            state.Head[pairId] = cell;
            state.Filled++;
        }

        private static int DirectionIndexTo(State state, Block from, Block to)
        {
            for (int d = 0; d < Directions.Length; d++)
            {
                if (Neighbour(state, from, Directions[d]) == to) { return d; }
            }
            return -1;
        }

        private static bool SameAxis(Direction a, Direction b)
        {
            bool aHorizontal = a == Direction.Left || a == Direction.Right;
            bool bHorizontal = b == Direction.Left || b == Direction.Right;
            return aHorizontal == bHorizontal;
        }

        private static bool IsHeadOf(State state, Block cell, int pairId)
        {
            Block head;
            return state.Head.TryGetValue(pairId, out head) && head == cell
                && !state.Finished.Contains(pairId);
        }

        /// <summary>Geometric neighbour, ignoring walls -- used to locate the diagonal cell in the
        /// corner law, which is a claim about the board's shape rather than about connectivity.</summary>
        private static Block Neighbour(State state, Block from, Direction dir)
        {
            return BoardTopology.Neighbor(state.Grid, state.Rows, state.Cols, from, dir);
        }

        /// <summary>Traversable neighbour: null if a wall, a blocked cell or the edge is in the way.</summary>
        private static Block Step(State state, Block from, Direction dir)
        {
            Block n = Neighbour(state, from, dir);
            if (n == null) { return null; }
            if (from.HasWall(dir) || n.HasWall(BoardTopology.Opposite(dir))) { return null; }
            if (n.BlockType == BlockType.Blocked) { return null; }
            return n;
        }

        private static int MostConstrainedUnfinished(State state)
        {
            int best = 0, fewest = int.MaxValue;
            foreach (KeyValuePair<int, Block> kv in state.Head)
            {
                if (state.Finished.Contains(kv.Key)) { continue; }
                int n = LegalMoves(state, kv.Key).Count;
                if (n < fewest) { fewest = n; best = kv.Key; }
            }
            return best;
        }

        private static void Note(Rating rating, Technique technique)
        {
            if (technique > rating.Hardest) { rating.Hardest = technique; }
        }
    }
}
