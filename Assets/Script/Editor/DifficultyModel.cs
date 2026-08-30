using UnityEngine;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// The one difficulty number, blended from four measures that come from three different
    /// families.
    ///
    /// <b>Why a blend, and not the best single metric.</b> This is the finding that reframed the
    /// whole problem. In Pelánek's evaluation against real human solving times, no individual
    /// metric reaches the headline correlation -- the best single one manages 0.86, and the
    /// published 0.95 comes from a FOUR-METRIC LINEAR MODEL combining families that fail in
    /// different places. We spent five rounds looking for the one number that would rank boards
    /// correctly. There isn't one. That is also why tangle plateaued: one static structural measure
    /// was being asked to do a four-measure job.
    ///
    /// The four terms here, and where each comes from:
    ///
    /// | Term | Source | Reported r | Direction |
    /// |---|---|---|---|
    /// | Refutation | <see cref="HumanSolver.Rating.Assumptions"/> | 0.68-0.83 | more guesses = harder |
    /// | Dependency | <see cref="HumanSolver.Rating.Dependency"/> | 0.67-0.69 | FEWER openings = harder |
    /// | Fixedness | <see cref="RelaxationMetrics.Result.Fixedness"/> | 0.56-0.61 (0.9 on Nurikabe) | see below |
    /// | Tangle | <see cref="LevelGenerator.TangleScore"/> | ours, validated by play only | more tangled = harder |
    ///
    /// <b>The weights are NOT fitted, and this matters.</b> Pelánek fitted his linear model on
    /// thousands of hours of recorded solving times. We have none, so the weights below are simply
    /// the reported correlations, normalised -- a defensible prior and nothing more. Fitting them
    /// needs the play telemetry that <c>SaveData.completedLevelAttempts</c> now records; until enough of
    /// that exists, treat this score as a RANKING within one board size rather than an absolute
    /// difficulty, and do not compare a 5x5's score against a 8x8's.
    ///
    /// Tangle is kept in the blend despite having no published correlation because it is the only
    /// term with direct evidence from this project: it is the one metric a playtest agreed with
    /// ("now it feels tangled"). It is weighted below the two literature-backed terms.
    /// </summary>
    public static class DifficultyModel
    {
        // Weights: the reported correlation of each measure, used as a prior. Normalised at use.
        private const float RefutationWeight = 0.83f;
        private const float DependencyWeight = 0.69f;
        private const float FixednessWeight = 0.61f;
        private const float TangleWeight = 0.50f;

        /// <summary>Assumptions at or above this count as maximally hard. Set from the Flow Free
        /// reference board, which is the only board in this project known to play hard.</summary>
        public const float AssumptionsFullScale = 14f;

        /// <summary>Dependency at or above this counts as maximally easy: with this many openings
        /// per round the board is solving itself and the player only has to notice.</summary>
        public const float DependencyEasyFloor = 8f;

        /// <summary>Tangle at or above this counts as maximally tangled; the Flow Free reference
        /// board measures 81.</summary>
        public const float TangleFullScale = 100f;

        public sealed class Profile
        {
            public HumanSolver.Rating Human;
            public RelaxationMetrics.Result Relaxation;
            public StructuralGates.Report Structure;
            public float Tangle;

            /// <summary>0-100. A ranking within one board size, not an absolute difficulty --
            /// see the class doc on why the weights are unfitted.</summary>
            public float Score;

            /// <summary>False when the board could not be solved or rated at all, in which case
            /// every other field is meaningless.</summary>
            public bool Valid;

            /// <summary>
            /// Whether the board is fit to ship at all, which is a SEPARATE question from how hard
            /// it is. <see cref="Score"/> ranks; this filters, and it must be applied first --
            /// several boards in the first calibration run scored above the Flow Free reference
            /// while being malformed.
            ///
            /// Two conditions, and the second is the interesting one. Nikoli's stated objection to
            /// computer-generated puzzles is that they "often have no straightforward starting
            /// point, requiring advanced logical deductions immediately". A board whose dependency
            /// is 0 over a single deduction round is exactly that: not one deduction is available
            /// from the opening position, so the player's first move can only be a guess. That is
            /// not a hard puzzle, it is an unfair one -- and the blend would otherwise reward it,
            /// since zero openings is the maximum of the dependency term.
            /// </summary>
            public bool WellFormed;

            public override string ToString()
            {
                if (!Valid) { return "INVALID (unsolvable or unrated)"; }
                return "score=" + Score.ToString("0.0") + (WellFormed ? "" : " [MALFORMED]")
                    + "  | " + Human
                    + "  | " + (Relaxation == null ? "relaxation not run" : Relaxation.ToString())
                    + "  | tangle=" + Tangle.ToString("0")
                    + "  | " + Structure;
            }
        }

        /// <summary>
        /// Measures every term for <paramref name="data"/>.
        /// </summary>
        /// <param name="includeRelaxation">
        /// False runs STAGE ONE only: everything except <see cref="RelaxationMetrics"/>.
        ///
        /// <b>Relaxation is 98% of the cost.</b> Measured on 7x7: 55 ms per board without it,
        /// 2243 ms with. It deletes each colour in turn and re-solves, and a board missing a colour
        /// is LESS constrained, so every one of those solves is dearer than the original -- which is
        /// why per-board cost jumps 27x from 6x6 to 7x7 on boards nowhere near 27x bigger.
        ///
        /// <b>And the cheap terms are most of the model.</b> Fixedness carries 0.61 of the 2.63
        /// total weight, about 23%; refutation, dependency and tangle carry the other 77%, including
        /// the heaviest single term and the one play actually responded to. So stage one is not a
        /// crude pre-filter, it is the bulk of the blend -- which is what makes it safe to rank
        /// thousands of candidates on it and pay the full price only for the finalists.
        ///
        /// For a 100-level pack: 75 minutes one-stage, 13 minutes two-stage.
        /// </param>
        public static Profile Measure(LevelData data, int maxAssumptions = 14, int maxSteps = 2000000,
            bool includeRelaxation = true)
        {
            Profile profile = new Profile();

            Block[,] grid = LevelGenerator.BuildBlockGrid(data, out int rows, out int cols);
            try
            {
                PuzzleSolver.SolveResult solved = PuzzleSolver.Solve(grid, rows, cols,
                    new PuzzleSolver.SolverOptions(maxSteps, 1));
                if (solved.Status != PuzzleSolver.SolveStatus.Solved) { return profile; }

                int usable = 0;
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        if (grid[r, c] != null && grid[r, c].BlockType != Enums.BlockType.Blocked) { usable++; }
                    }
                }

                profile.Tangle = LevelGenerator.TangleScore(grid, rows, cols, solved);
                profile.Structure = StructuralGates.Evaluate(solved, usable);

                // The corner dual law is only valid where no link touches itself, so the structural
                // check has to run FIRST and hand its verdict to the deduction model. Get this
                // order wrong and a self-touching board reports UNSOLVED rather than merely
                // unrated, which reads as "impossible" when it means "rule not applicable".
                profile.Human = HumanSolver.Rate(grid, rows, cols, maxAssumptions,
                    profile.Structure.SelfTouches == 0);
            }
            finally { LevelGenerator.DestroyBlockGrid(grid); }

            // Left null on a stage-one pass. Blend drops the term and renormalises, exactly as it
            // does for a board whose colours could not be relaxed away at all.
            if (includeRelaxation) { profile.Relaxation = RelaxationMetrics.Measure(data); }
            profile.Score = Blend(profile);
            profile.Valid = true;
            profile.WellFormed = profile.Structure.Passed
                && profile.Human.Solved
                && profile.Human.Dependency > 0f;
            return profile;
        }

        /// <summary>
        /// Each term is mapped to 0-1 with "1 = harder", then averaged by the weights above.
        /// </summary>
        public static float Blend(Profile profile)
        {
            float refutation = Mathf.Clamp01(profile.Human.Assumptions / AssumptionsFullScale);

            // Inverted deliberately. A board offering many simultaneous deductions is EASY, and
            // ranking it the other way round is precisely the error the earlier solver-effort
            // metrics made: lots of available moves reads as lots of branching, which reads as a
            // hard search, which is not a hard puzzle.
            float dependency = 1f - Mathf.Clamp01(profile.Human.Dependency / DependencyEasyFloor);

            float tangle = Mathf.Clamp01(profile.Tangle / TangleFullScale);

            float weighted = RefutationWeight * refutation
                           + DependencyWeight * dependency
                           + TangleWeight * tangle;
            float total = RefutationWeight + DependencyWeight + TangleWeight;

            // Fixedness only counts when it was actually measurable. On a board where removing ANY
            // colour makes coverage impossible, every relaxed variant is unsolvable and there is
            // nothing to compare -- Fixedness is then 0 because nothing was measured, not because
            // nothing stayed put. Feeding that 0 through the inversion below scored it as maximally
            // hard, which is how a malformed 7x7 came out above the Flow Free reference on the
            // first calibration run. Dropping the term and renormalising is the honest handling of
            // a missing measurement.
            if (profile.Relaxation != null && profile.Relaxation.Variants > 0)
            {
                // Cells that hold their colour when a whole colour is removed were over-determined
                // by the rest of the board, so a high-fixedness board is one the player is being
                // led through.
                weighted += FixednessWeight * (1f - Mathf.Clamp01(profile.Relaxation.Fixedness));
                total += FixednessWeight;
            }

            return (weighted / total) * 100f;
        }
    }
}
