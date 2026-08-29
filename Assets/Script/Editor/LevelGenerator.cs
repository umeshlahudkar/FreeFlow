using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using FreeFlow.Enums;
using UnityEditor;
using UnityEngine;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Offline level generator (spec §6/§38): builds a solution first, then derives the level
    /// data from it, rather than authoring a puzzle and hoping it happens to be solvable. Runs
    /// only in the Editor -- this lives under an Editor folder, ships with no player build, and
    /// nothing in gameplay ever calls it (see LevelValidator.ValidateSolvability's own doc comment
    /// about the same runtime-vs-offline split).
    ///
    /// Pipeline per candidate level: generate a Hamiltonian path over the whole board (a "snake"
    /// that visits every cell exactly once) -> cut it into one contiguous segment per colour,
    /// each segment's two ends becoming that colour's dots -> validate structurally
    /// (LevelValidator.Validate) -> confirm solvability and get a uniqueness signal
    /// (LevelValidator.ValidateSolvability, PuzzleSolver underneath) -> reject canonical
    /// duplicates (LevelCanonicalizer) -> save.
    ///
    /// The snake-then-cut construction guarantees full-board-coverage solvability by
    /// construction (the snake itself, cut into segments, IS a valid solution) -- Validate and
    /// ValidateSolvability are run anyway as a defence-in-depth check on the construction itself,
    /// not because failure is expected.
    ///
    /// Difficulty acceptance is DifficultyAnalyzer's real score, not a proxy: each candidate is
    /// analyzed (reusing the same solve ValidateSolvability already ran, no second solve), and a
    /// candidate is only accepted once its score falls in the level's target band -- retrying with
    /// a fresh random snake/colour count/Blocked-cell placement otherwise, falling back to the
    /// closest-scoring valid candidate seen if the band is never hit within MaxAttempts.
    /// StraightnessBias remains a coarse generation-time knob (it nudges the snake's shape) but is
    /// no longer what difficulty is measured by.
    ///
    /// A single mechanic barely moves DifficultyAnalyzer's score: mechanic weight and
    /// constrained-cell ratio are 25 of the formula's 100 points, but one Blocked cell on a
    /// 24-25-usable-cell board contributes only a few tenths of a point to each. Target bands for
    /// early campaign levels are calibrated to what's actually measurable (a small, honest drift),
    /// not an invented ramp -- the same "calibrate to the real ceiling, not a hoped-for spread"
    /// lesson already learned once generating the (now superseded) 50-level World 1 batch.
    /// </summary>
    public static class LevelGenerator
    {
        [MenuItem("FreeFlow/Level Generator/Generate Levels 1-10 (Basic Flow + Blocked Cell)")]
        public static void GenerateLevels1To10()
        {
            const string levelsFolder = "Assets/Resources/Levels";
            const int levelCount = 10;

            if (!AssetDatabase.IsValidFolder(levelsFolder))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "Levels");
            }

            System.Random rng = new System.Random(20260830); // fixed seed for the 200-level campaign's opening levels
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            StringBuilder report = new StringBuilder();
            int savedCount = 0;

            for (int levelNumber = 1; levelNumber <= levelCount; levelNumber++)
            {
                GenerationSpec spec = SpecForLevel1To10(levelNumber);
                GeneratedLevel generated = TryGenerateLevel(spec, rng, seenCanonicalKeys);

                if (generated == null)
                {
                    Debug.LogError("LevelGenerator: failed to generate level " + levelNumber +
                        " after " + spec.MaxAttempts + " attempts.");
                    report.Append("Level ").Append(levelNumber).Append(": FAILED\n");
                    continue;
                }

                SaveLevelAsset(levelsFolder, levelNumber, generated.Data, generated.DifficultyScore);
                savedCount++;
                report.Append("Level ").Append(levelNumber)
                    .Append(": grid=").Append(spec.GridSize)
                    .Append(" colors=").Append(generated.Data.pairCount)
                    .Append(" blocked=").Append(spec.BlockedCellCount)
                    .Append(" avgPath=").Append(generated.AveragePathCells.ToString("0.0"))
                    .Append(" minPath=").Append(generated.ShortestPathCells)
                    .Append(" score=").Append(generated.DifficultyScore.ToString("0.0"))
                    .Append(" tier=").Append(generated.DifficultyTier)
                    .Append(" maxSlack=").Append(generated.MaxSlack)
                    .Append(" solutions=").Append(generated.SolutionsFound)
                    .Append(generated.SearchExhausted ? "" : "+")
                    .Append(generated.SolutionsFound == 1 ? " (unique)" : "")
                    .Append('\n');
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("LevelGenerator: Levels 1-10 generation complete -- " + savedCount + "/" + levelCount +
                " levels saved.\n" + report);
        }

        [MenuItem("FreeFlow/Level Generator/Generate Levels 11-15 (Wall)")]
        public static void GenerateLevels11To15()
        {
            const string levelsFolder = "Assets/Resources/Levels";
            const int startLevel = 11;
            const int endLevel = 15;
            const int gridSize = 5;

            System.Random rng = new System.Random(20260831); // a fresh seed for this level range
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1, seenCanonicalKeys);

            StringBuilder report = new StringBuilder();
            int savedCount = 0;

            for (int levelNumber = startLevel; levelNumber <= endLevel; levelNumber++)
            {
                GenerationSpec spec = SpecForLevel11To15(levelNumber, gridSize);
                GeneratedLevel generated = TryGenerateLevel(spec, rng, seenCanonicalKeys);

                if (generated == null)
                {
                    Debug.LogError("LevelGenerator: failed to generate level " + levelNumber +
                        " after " + spec.MaxAttempts + " attempts.");
                    report.Append("Level ").Append(levelNumber).Append(": FAILED\n");
                    continue;
                }

                SaveLevelAsset(levelsFolder, levelNumber, generated.Data, generated.DifficultyScore);
                savedCount++;
                report.Append("Level ").Append(levelNumber)
                    .Append(": colors=").Append(generated.Data.pairCount)
                    .Append(" blocked=").Append(spec.BlockedCellCount)
                    .Append(" walls=").Append(spec.WallCount)
                    .Append(" score=").Append(generated.DifficultyScore.ToString("0.0"))
                    .Append(" tier=").Append(generated.DifficultyTier)
                    .Append(" solutions=").Append(generated.SolutionsFound)
                    .Append(generated.SearchExhausted ? "" : "+")
                    .Append(generated.SolutionsFound == 1 ? " (unique)" : "")
                    .Append('\n');
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("LevelGenerator: Levels 11-15 generation complete -- " + savedCount + "/" +
                (endLevel - startLevel + 1) + " levels saved.\n" + report);
        }

        [MenuItem("FreeFlow/Level Generator/Generate Levels 16-20 (One-Way)")]
        public static void GenerateLevels16To20()
        {
            const string levelsFolder = "Assets/Resources/Levels";
            const int startLevel = 16;
            const int endLevel = 20;
            const int gridSize = 5;

            System.Random rng = new System.Random(20260901); // a fresh seed for this level range
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1, seenCanonicalKeys);

            StringBuilder report = new StringBuilder();
            int savedCount = 0;

            for (int levelNumber = startLevel; levelNumber <= endLevel; levelNumber++)
            {
                GenerationSpec spec = SpecForLevel16To20(levelNumber, gridSize);
                GeneratedLevel generated = TryGenerateLevel(spec, rng, seenCanonicalKeys);

                if (generated == null)
                {
                    Debug.LogError("LevelGenerator: failed to generate level " + levelNumber +
                        " after " + spec.MaxAttempts + " attempts.");
                    report.Append("Level ").Append(levelNumber).Append(": FAILED\n");
                    continue;
                }

                SaveLevelAsset(levelsFolder, levelNumber, generated.Data, generated.DifficultyScore);
                savedCount++;
                report.Append("Level ").Append(levelNumber)
                    .Append(": colors=").Append(generated.Data.pairCount)
                    .Append(" blocked=").Append(spec.BlockedCellCount)
                    .Append(" walls=").Append(spec.WallCount)
                    .Append(" oneWay=").Append(spec.OneWayCount)
                    .Append(" score=").Append(generated.DifficultyScore.ToString("0.0"))
                    .Append(" tier=").Append(generated.DifficultyTier)
                    .Append(" solutions=").Append(generated.SolutionsFound)
                    .Append(generated.SearchExhausted ? "" : "+")
                    .Append(generated.SolutionsFound == 1 ? " (unique)" : "")
                    .Append('\n');
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("LevelGenerator: Levels 16-20 generation complete -- " + savedCount + "/" +
                (endLevel - startLevel + 1) + " levels saved.\n" + report);
        }

        [MenuItem("FreeFlow/Level Generator/Generate Levels 21-25 (Arrow)")]
        public static void GenerateLevels21To25()
        {
            const string levelsFolder = "Assets/Resources/Levels";
            const int startLevel = 21;
            const int endLevel = 25;
            const int gridSize = 5;

            System.Random rng = new System.Random(20260902); // a fresh seed for this level range
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1, seenCanonicalKeys);

            StringBuilder report = new StringBuilder();
            int savedCount = 0;

            for (int levelNumber = startLevel; levelNumber <= endLevel; levelNumber++)
            {
                GenerationSpec spec = SpecForLevel21To25(levelNumber, gridSize);
                GeneratedLevel generated = TryGenerateLevel(spec, rng, seenCanonicalKeys);

                if (generated == null)
                {
                    Debug.LogError("LevelGenerator: failed to generate level " + levelNumber +
                        " after " + spec.MaxAttempts + " attempts.");
                    report.Append("Level ").Append(levelNumber).Append(": FAILED\n");
                    continue;
                }

                SaveLevelAsset(levelsFolder, levelNumber, generated.Data, generated.DifficultyScore);
                savedCount++;
                report.Append("Level ").Append(levelNumber)
                    .Append(": colors=").Append(generated.Data.pairCount)
                    .Append(" blocked=").Append(spec.BlockedCellCount)
                    .Append(" walls=").Append(spec.WallCount)
                    .Append(" arrows=").Append(spec.ArrowCount)
                    .Append(" score=").Append(generated.DifficultyScore.ToString("0.0"))
                    .Append(" tier=").Append(generated.DifficultyTier)
                    .Append(" solutions=").Append(generated.SolutionsFound)
                    .Append(generated.SearchExhausted ? "" : "+")
                    .Append(generated.SolutionsFound == 1 ? " (unique)" : "")
                    .Append('\n');
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("LevelGenerator: Levels 21-25 generation complete -- " + savedCount + "/" +
                (endLevel - startLevel + 1) + " levels saved.\n" + report);
        }

        /// <summary>
        /// Loads every already-generated level in [fromLevel,toLevel] (skipping any that don't
        /// exist yet) and adds its canonical key to <paramref name="seenCanonicalKeys"/>, so a new
        /// generation run rejects duplicates against the WHOLE campaign built so far, not just the
        /// levels it happens to generate in this one call.
        /// </summary>
        private static void SeedExistingCanonicalKeys(string folder, int fromLevel, int toLevel,
            HashSet<string> seenCanonicalKeys)
        {
            for (int levelNumber = fromLevel; levelNumber <= toLevel; levelNumber++)
            {
                string path = folder + "/Level_" + levelNumber + ".asset";
                SingleLevelDataSO existing = AssetDatabase.LoadAssetAtPath<SingleLevelDataSO>(path);
                if (existing == null) { continue; }

                Block[,] grid = BuildBlockGrid(existing.levelData, out int rows, out int cols);
                try
                {
                    seenCanonicalKeys.Add(LevelCanonicalizer.ComputeCanonicalKey(grid, rows, cols));
                }
                finally
                {
                    DestroyBlockGrid(grid);
                }
            }
        }

        // ---------------------------------------------------------------------------------------
        // Public, reusable generation entry point -- worlds 2+ reuse this with their own
        // GenerationSpec values (and, once later phases exist, mechanics layered on top).
        // ---------------------------------------------------------------------------------------

        public sealed class GenerationSpec
        {
            public int GridSize;

            /// <summary>Each attempt picks a random colour count in [MinColorCount,MaxColorCount]
            /// -- a range rather than a fixed value gives the retry loop more freedom to land a
            /// candidate inside TargetScoreMin/Max.</summary>
            public int MinColorCount;
            public int MaxColorCount;

            /// <summary>0-1: probability the snake continues straight rather than turning at each
            /// step, when there's a choice. A coarse generation-time nudge, not the difficulty
            /// gate itself -- see the class doc.</summary>
            public float StraightnessBias;

            /// <summary>Accepted DifficultyAnalyzer.Score range. A candidate outside this band is
            /// kept only as a fallback in case nothing better turns up within MaxAttempts.</summary>
            public float TargetScoreMin;
            public float TargetScoreMax;

            /// <summary>How strongly a unique solution is preferred -- graduated, not a single
            /// on/off switch: an Easy level is fine with several solutions, a Medium/Hard one
            /// should favour a unique one without rejecting an otherwise-good non-unique candidate,
            /// and an Expert/World-Challenge level should only accept unique.</summary>
            public UniquenessPolicy Uniqueness;

            /// <summary>How many cells to exclude from the board as Blocked (spec: "not usable
            /// cells", excluded from the coverage requirement entirely). 0 for a mechanic-free
            /// level.</summary>
            public int BlockedCellCount;

            /// <summary>Keeps blocked cells off the board's outer ring, so each one is something
            /// the player has to route around rather than a corner they never notice -- see
            /// <see cref="PlaceBlockedCells"/> for why this matters on a level that introduces the
            /// mechanic.</summary>
            public bool BlockedCellsInteriorOnly;

            /// <summary>How many single edges to wall off. Placed only on edges the intended
            /// snake solution never crosses (spec: mechanics must be built onto the solution,
            /// never bolted on afterward) -- placing a wall the solution itself needs would break
            /// the very thing generation just spent effort constructing.</summary>
            public int WallCount;

            /// <summary>How many One-Way cells to place on interior (non-dot) path cells, each
            /// locked to the direction the intended solution actually enters it from -- the only
            /// direction it will ever be asked to admit, so the solution trivially satisfies its
            /// own constraint.</summary>
            public int OneWayCount;

            /// <summary>How many Arrow cells to place on interior (non-dot) path cells, each
            /// locked to the direction the intended solution actually exits it toward -- the only
            /// direction it will ever be asked to leave by, so the solution trivially satisfies
            /// its own constraint. Mirrors OneWayCount exactly, constraining exit instead of
            /// entry (see PlaceArrowCells).</summary>
            public int ArrowCount;

            /// <summary>If true, every placed mechanic above (any count > 0) must be verified
            /// load-bearing via RequiredMechanicValidator before a candidate is fully accepted --
            /// the spec's required-mechanic rule, applied uniformly rather than toggled per
            /// mechanic type. Every campaign level introducing a mechanic wants this; none so far
            /// have wanted a mechanic present but exempt from the check, so one flag covers all of
            /// them rather than growing a same-shaped bool per mechanic. Falls back to the best
            /// candidate found otherwise, same graceful degradation as UniquenessPolicy.Require.</summary>
            public bool RequireMechanicsNecessary;

            /// <summary>
            /// A two-sided TARGET BAND (penalised via <see cref="BandPenalty"/>, same
            /// graceful-fallback shape as TargetScoreMin/Max) for the worst-case gap, across every
            /// colour in the actual solved board, between its real path length and its own two
            /// dots' direct (Manhattan) distance -- i.e. how far the "obvious" straight drag
            /// between two dots falls short of the real solution. Deliberately a BAND, not just a
            /// ceiling: a ceiling alone only ever PERMITS a low-slack (easy) candidate, it never
            /// REQUIRES one, and just as importantly never requires a HARDER one either -- a puzzle
            /// with zero required detour has zero puzzle-solving in it, which is exactly the
            /// mistake this pair of fields corrects (an earlier version of this spec used a
            /// ceiling only, capped at 0 for every early level, and it produced ten levels a
            /// player could solve without thinking at all).
            ///
            /// Defaults ([0, int.MaxValue]) leave candidates unconstrained either direction. See
            /// MaxSlackAcrossSolution's own doc comment for why 0 is only reachable by a perfectly
            /// straight single-colour segment, never guaranteed on a general multi-colour board.
            /// </summary>
            public int MinSlackPerColor = 0;
            public int MaxSlackPerColor = int.MaxValue;

            /// <summary>
            /// Enforces the campaign's hard rule that a player must never be able to connect every
            /// pair and still be looking at empty cells -- see
            /// <see cref="EveryPairingCoversTheBoard"/> for what is actually checked and why this,
            /// rather than a slack limit, is the right way to express it. Expensive (it enumerates
            /// the board's whole pairing space), so it runs last, only on candidates that have
            /// already passed every cheaper gate.
            /// </summary>
            public bool RequireEveryPairingCoversBoard;

            /// <summary>
            /// Rejects any candidate in which SOME colour's path is shorter than this many cells.
            /// The single most direct control over whether a level feels like a puzzle: a 2-cell
            /// path is two adjacent dots, solved by one drag with nothing to work out, and even one
            /// of those on a board makes the whole level feel thin. Guards the floor, not the
            /// average, precisely because an average hides them.
            ///
            /// This exists because DifficultyAnalyzer's score turned out to be actively misleading
            /// for this range: it rewards grid size and colour count, both of which RISE when a
            /// board is packed with more, shorter paths. Measured, a 5x5 whose three colours each
            /// ran 7 cells scored 20.9, while a 6x6 of eight paths averaging 4 cells (two of them
            /// length 2 and 3) scored 43.4 -- the score said the second was twice as hard, real
            /// play said the opposite. Path length is what the player actually feels.
            /// </summary>
            public int MinPathCells;

            /// <summary>Target band for the MEAN path length, used for ranking the same way
            /// TargetScoreMin/Max is. Inert by default.</summary>
            public float TargetAvgPathMin;
            public float TargetAvgPathMax = float.MaxValue;

            public int MaxAttempts;
        }

        public enum UniquenessPolicy
        {
            /// <summary>No preference either way -- several solutions is fine (spec's "Easy: allow
            /// a small number of solutions").</summary>
            Ignore,
            /// <summary>Prefer a unique solution via a small tie-breaking penalty, but still accept
            /// the best-fitting non-unique candidate rather than burn attempts chasing uniqueness
            /// alone ("Medium: prefer 1").</summary>
            Prefer,
            /// <summary>Only fully accept a unique solution; falls back to the best candidate found
            /// rather than leaving a gap if one can't be found within MaxAttempts ("Hard: require
            /// 1" / "Expert: strongly require 1").</summary>
            Require
        }

        public sealed class GeneratedLevel
        {
            public LevelData Data;
            public int SolutionsFound;
            public bool SearchExhausted;
            public float DifficultyScore;
            public DifficultyAnalyzer.DifficultyTier DifficultyTier;
            public int MaxSlack;
            public int ShortestPathCells;
            public float AveragePathCells;
        }

        /// <summary>
        /// A large, fixed penalty for missing a REQUIRED unique solution -- big enough that it
        /// always dominates the (much smaller) score-band distance, so "closest to the target
        /// band" is only ever used to break ties among candidates that already meet the
        /// requirement.
        /// </summary>
        private const float RequiredUniquenessPenalty = 1000f;

        /// <summary>
        /// A small penalty for a PREFERRED (not required) unique solution -- big enough to break a
        /// tie between two otherwise-similar candidates in favour of the unique one, small enough
        /// to never override a genuinely better score-band fit (the band half-width used across
        /// World 1 is 2.5, well above this).
        /// </summary>
        private const float PreferredUniquenessPenalty = 1f;

        public static GeneratedLevel TryGenerateLevel(GenerationSpec spec, System.Random rng,
            HashSet<string> seenCanonicalKeys)
        {
            GeneratedLevel best = null;
            string bestKey = null;
            float bestPenalty = float.MaxValue;

            for (int attempt = 0; attempt < spec.MaxAttempts; attempt++)
            {
                int colorCount = spec.MinColorCount == spec.MaxColorCount
                    ? spec.MinColorCount
                    : rng.Next(spec.MinColorCount, spec.MaxColorCount + 1);

                // A given Blocked-cell placement may leave no Hamiltonian path at all (a
                // connected region isn't sufficient -- parity can still rule it out) -- that's a
                // failed ATTEMPT, not a fatal error, so this retries with fresh placements/snake
                // rather than throwing.
                if (!TryBuildCandidate(spec, colorCount, rng, out LevelData candidate)) { continue; }

                Block[,] grid = BuildBlockGrid(candidate, out int rows, out int cols);

                try
                {
                    // Structural sanity check. Should always pass by construction -- a failure
                    // here means a bug in BuildCandidate, and Validate has already logged it.
                    LevelValidator.Validate(grid, rows, cols);

                    PuzzleSolver.SolveResult solveResult = LevelValidator.ValidateSolvability(
                        grid, rows, cols, new PuzzleSolver.SolverOptions(300000, 2));

                    if (solveResult.Status != PuzzleSolver.SolveStatus.Solved) { continue; }

                    // Cheap, and rejects the single most common way a level ends up feeling
                    // trivial, so it runs before anything expensive -- see MinPathCells.
                    MeasurePathLengths(solveResult, out int shortestPath, out float averagePath);
                    if (shortestPath < spec.MinPathCells) { continue; }

                    string key = LevelCanonicalizer.ComputeCanonicalKey(grid, rows, cols);
                    if (seenCanonicalKeys.Contains(key)) { continue; }

                    // Reuses solveResult rather than solving again -- see the class doc.
                    DifficultyAnalyzer.DifficultyReport report = DifficultyAnalyzer.Analyze(grid, rows, cols, solveResult);
                    bool isUnique = solveResult.SolutionsFound == 1 && solveResult.SearchExhausted;

                    float uniquenessPenalty = isUnique ? 0f : spec.Uniqueness switch
                    {
                        UniquenessPolicy.Require => RequiredUniquenessPenalty,
                        UniquenessPolicy.Prefer => PreferredUniquenessPenalty,
                        _ => 0f
                    };

                    float mechanicPenalty = 0f;
                    if (spec.RequireMechanicsNecessary)
                    {
                        if (spec.BlockedCellCount > 0 && !AllCellsOfTypeAreNecessary(grid, rows, cols, BlockType.Blocked))
                        {
                            mechanicPenalty += RequiredUniquenessPenalty;
                        }
                        if (spec.WallCount > 0 && !AllWallsAreNecessary(grid, rows, cols))
                        {
                            mechanicPenalty += RequiredUniquenessPenalty;
                        }
                        if (spec.OneWayCount > 0 && !AllCellsOfTypeAreNecessary(grid, rows, cols, BlockType.OneWay))
                        {
                            mechanicPenalty += RequiredUniquenessPenalty;
                        }
                        if (spec.ArrowCount > 0 && !AllCellsOfTypeAreNecessary(grid, rows, cols, BlockType.Arrow))
                        {
                            mechanicPenalty += RequiredUniquenessPenalty;
                        }
                    }

                    int maxSlack = MaxSlackAcrossSolution(solveResult);
                    float slackBandDistance = BandPenalty(maxSlack, spec.MinSlackPerColor, spec.MaxSlackPerColor);
                    float slackPenalty = slackBandDistance > 0f ? RequiredUniquenessPenalty + slackBandDistance : 0f;

                    float penalty = BandPenalty(report.Score, spec.TargetScoreMin, spec.TargetScoreMax)
                        + BandPenalty(averagePath, spec.TargetAvgPathMin, spec.TargetAvgPathMax)
                        + uniquenessPenalty + mechanicPenalty + slackPenalty;

                    // Ranked out already -- nothing below can change that, and the coverage check
                    // beneath is far too expensive to spend on a candidate that cannot win.
                    if (penalty >= bestPenalty) { continue; }

                    // A HARD reject, not a penalty: penalties only RANK candidates, and the
                    // best-ranked one still ships when nothing hits its target exactly -- so
                    // expressing this as a penalty would let a board that breaks the rule through
                    // as a fallback. Skipping the candidate outright is what makes the rule
                    // actually unbreakable. Deliberately placed after the ranking check above
                    // (it is by far the most expensive gate, enumerating the board's whole pairing
                    // space) but before anything is recorded, so every candidate that can still
                    // become `best` -- fallbacks included -- has passed it.
                    if (spec.RequireEveryPairingCoversBoard && !EveryPairingCoversTheBoard(grid, rows, cols))
                    {
                        continue;
                    }

                    {
                        bestPenalty = penalty;
                        bestKey = key;
                        best = new GeneratedLevel
                        {
                            Data = candidate,
                            SolutionsFound = solveResult.SolutionsFound,
                            SearchExhausted = solveResult.SearchExhausted,
                            DifficultyScore = report.Score,
                            DifficultyTier = report.Tier,
                            MaxSlack = maxSlack,
                            ShortestPathCells = shortestPath,
                            AveragePathCells = averagePath
                        };

                        if (penalty == 0f) { break; } // in-band and meets uniqueness -- good enough
                    }
                }
                finally
                {
                    DestroyBlockGrid(grid);
                }
            }

            if (best != null)
            {
                seenCanonicalKeys.Add(bestKey);
                if (bestPenalty > 0f)
                {
                    Debug.LogWarning("LevelGenerator: best candidate found still missed its target " +
                        "(score=" + best.DifficultyScore.ToString("0.0") + ", target=[" +
                        spec.TargetScoreMin.ToString("0.0") + "," + spec.TargetScoreMax.ToString("0.0") +
                        "], unique=" + (best.SolutionsFound == 1 && best.SearchExhausted) +
                        ") after " + spec.MaxAttempts + " attempts.");
                }
            }
            return best;
        }

        /// <summary>
        /// Shortest and mean path length, in cells, across the colours of a solved board -- the
        /// most direct measure of whether a level gives the player anything to work out. See
        /// GenerationSpec.MinPathCells for why this is gated on directly instead of trusting
        /// DifficultyAnalyzer's score.
        /// </summary>
        private static void MeasurePathLengths(PuzzleSolver.SolveResult solveResult,
            out int shortestCells, out float averageCells)
        {
            shortestCells = int.MaxValue;
            int total = 0;
            int count = solveResult.Solutions.Count;

            for (int i = 0; i < count; i++)
            {
                int cells = solveResult.Solutions[i].Cells.Count;
                total += cells;
                if (cells < shortestCells) { shortestCells = cells; }
            }

            if (count == 0) { shortestCells = 0; averageCells = 0f; return; }
            averageCells = (float)total / count;
        }

        private static float BandPenalty(float score, float min, float max)
        {
            if (score < min) { return min - score; }
            if (score > max) { return score - max; }
            return 0f;
        }

        /// <summary>
        /// The largest gap, across every colour in the ACTUAL solved board, between how many
        /// steps its path really takes and how many steps the straight-line (Manhattan) distance
        /// between its own two dots would suggest -- i.e. how far short of the real solution a
        /// player's first, most obvious guess (just drag directly between the dots) falls for the
        /// worst-offending colour. Zero only for a colour whose path IS the direct line between
        /// its dots, which -- on any grid wider than 1 cell -- is only possible when that colour's
        /// path length is short enough to fit in a single straight run; a colour covering enough
        /// cells to need more than one row/column's width of straight line can never reach zero,
        /// since the direct distance between two points can never exceed the number of steps a
        /// path confined to a bounded grid needs to actually cover that many cells. This is a
        /// property of full-board-coverage puzzles generally, not a generation flaw: minimizing it
        /// (never eliminating it) is the actual, honest lever available for making an early level's
        /// required path easier to stumble onto.
        /// </summary>
        private static int MaxSlackAcrossSolution(PuzzleSolver.SolveResult solveResult)
        {
            int maxSlack = 0;
            for (int s = 0; s < solveResult.Solutions.Count; s++)
            {
                List<(int Row, int Col)> cells = solveResult.Solutions[s].Cells;
                (int Row, int Col) a = cells[0];
                (int Row, int Col) b = cells[cells.Count - 1];
                int manhattan = Math.Abs(a.Row - b.Row) + Math.Abs(a.Col - b.Col);
                int pathLength = cells.Count - 1;
                int slack = pathLength - manhattan;
                if (slack > maxSlack) { maxSlack = slack; }
            }
            return maxSlack;
        }

        /// <summary>
        /// Spec's required-mechanic rule (§10/§27), applied to every cell of <paramref
        /// name="type"/> on the board: each one must be load-bearing, not decorative. One
        /// BlockType-parameterized scan covers Blocked, One-Way, and (as more mechanics land)
        /// Arrow/Forbidden/Allowed too, rather than a same-shaped copy per mechanic -- only Bridge
        /// and Wall need their own version, since Bridge's necessity is still per-cell/per-type but
        /// Wall is an edge property with no BlockType at all (see AllWallsAreNecessary).
        /// </summary>
        private static bool AllCellsOfTypeAreNecessary(Block[,] grid, int rowCount, int colCount, BlockType type)
        {
            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < colCount; c++)
                {
                    if (grid[r, c].BlockType != type) { continue; }

                    RequiredMechanicValidator.RequirementResult result =
                        RequiredMechanicValidator.CheckBlockTypeMechanicRequired(grid, rowCount, colCount, r, c);
                    if (result.Status != RequiredMechanicValidator.RequirementStatus.Required) { return false; }
                }
            }
            return true;
        }

        /// <summary>
        /// Same rule as <see cref="AllCellsOfTypeAreNecessary"/>, applied to every walled edge:
        /// each must eliminate a route the player could otherwise take, not just decorate an edge
        /// the intended solution never crosses anyway. Scans for HasWall directly rather than
        /// threading the placement list through, since a wall is authored one-sided (matching
        /// existing hand-authored level convention -- see Block.wallVisual's own field comment)
        /// and a scan finds each one exactly once.
        /// </summary>
        private static bool AllWallsAreNecessary(Block[,] grid, int rowCount, int colCount)
        {
            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < colCount; c++)
                {
                    Block cell = grid[r, c];
                    for (int d = 0; d < Directions.Length; d++)
                    {
                        Direction dir = Directions[d];
                        if (!cell.HasWall(dir)) { continue; }

                        RequiredMechanicValidator.RequirementResult result =
                            RequiredMechanicValidator.CheckWallRequired(grid, rowCount, colCount, r, c, dir);
                        if (result.Status != RequiredMechanicValidator.RequirementStatus.Required) { return false; }
                    }
                }
            }
            return true;
        }

        private static readonly Direction[] Directions =
        {
            Direction.Left, Direction.Right, Direction.Up, Direction.Down
        };

        /// <summary>
        /// How many connect-the-pairs arrangements <see cref="EveryPairingCoversTheBoard"/> will
        /// enumerate before giving up and reporting "can't tell". Generous, because the answer is
        /// only trustworthy when the search EXHAUSTS -- a board with more distinct pairings than
        /// this is treated as unproven (and therefore rejected), never as proven safe.
        /// </summary>
        private const int PairingEnumerationCap = 200;

        /// <summary>
        /// The player-facing rule this whole generator exists to guarantee, stated exactly:
        /// <b>there must be no way to connect every pair and still leave a cell empty.</b>
        ///
        /// Not the same thing as "the intended solution covers the board" -- that is guaranteed by
        /// construction and was never the problem. The problem, reported from real play on Level 7,
        /// is the state where a player connects all pairs the short way, sees "Pair 3/3", and the
        /// board still has holes in it: a dead end with no feedback. That state exists whenever
        /// SOME OTHER pairing, different from the intended one, connects everything without filling
        /// the board.
        ///
        /// So this asks the opposite question of every other solve in the pipeline: run the solver
        /// with the full-coverage win condition switched OFF (see SolverOptions.AllowPartialCoverage),
        /// enumerate every ordinary connect-the-pairs arrangement, and confirm that each one happens
        /// to fill the board anyway. If even one leaves a hole, the level is rejected.
        ///
        /// Deliberately conservative about uncertainty: a search that hits its budget without
        /// exhausting proves nothing, so it returns false (reject) rather than assuming the
        /// unexplored branches are fine.
        ///
        /// This replaces an earlier, much cruder attempt at the same rule -- forcing every colour's
        /// path to be the shortest route between its own dots (slack 0), which did guarantee the
        /// rule but only by removing all routing choice, and with it the entire puzzle. See
        /// SpecForLevel1To10's own doc comment.
        /// </summary>
        private static bool EveryPairingCoversTheBoard(Block[,] grid, int rowCount, int colCount)
        {
            int usableCells = 0;
            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < colCount; c++)
                {
                    if (grid[r, c] != null && grid[r, c].BlockType != BlockType.Blocked) { usableCells++; }
                }
            }

            PuzzleSolver.SolveResult pairings = PuzzleSolver.Solve(grid, rowCount, colCount,
                new PuzzleSolver.SolverOptions(300000, PairingEnumerationCap, true));

            // No arrangement at all, or the enumeration never finished -- either way, unproven.
            // Hitting the solution cap counts as "never finished" too: the solver stops early once
            // it has collected MaxSolutionsToFind and still reports SearchExhausted, so the count
            // has to be checked separately or a board with more pairings than the cap would look
            // fully explored when it was only sampled.
            if (pairings.Status != PuzzleSolver.SolveStatus.Solved) { return false; }
            if (!pairings.SearchExhausted) { return false; }
            if (pairings.SolutionsFound >= PairingEnumerationCap) { return false; }

            for (int i = 0; i < pairings.AllSolutions.Count; i++)
            {
                HashSet<(int, int)> covered = new HashSet<(int, int)>();
                List<PuzzleSolver.PairSolution> arrangement = pairings.AllSolutions[i];
                for (int p = 0; p < arrangement.Count; p++)
                {
                    List<(int Row, int Col)> cells = arrangement[p].Cells;
                    for (int k = 0; k < cells.Count; k++) { covered.Add(cells[k]); }
                }

                if (covered.Count < usableCells) { return false; }
            }

            return true;
        }

        /// <summary>
        /// Levels 1-5: Basic Flow only, very easy -> easy (spec §4). Levels 6-10: one Blocked
        /// cell introduced, always checked for necessity (spec §6, "the Blocked Cell must be
        /// necessary for solving the puzzle" -- checking it from level 6 rather than only at level
        /// 10 costs nothing, since Phase 6's own findings showed a Blocked cell in a small
        /// single/few-pair board is nearly always Required anyway).
        ///
        /// <b>HARD RULE: the player must never be able to connect every pair and still be looking
        /// at empty cells.</b> Enforced by <see cref="EveryPairingCoversTheBoard"/> as an absolute
        /// reject, not a penalty. Reaching that formulation took four wrong turns worth recording,
        /// because each one looked correct in isolation:
        ///   - Pass 1 (original): no control at all. Level 1 shipped with a colour whose real path
        ///     was 14 steps longer than the direct route between its dots; the player connected
        ///     both dots the obvious way, left 14 cells empty, and reported the game as broken.
        ///   - Pass 2: capped that gap ("slack") at 0 everywhere, forcing every path to BE the
        ///     direct route. The confusion vanished -- and so did the game. Reported, correctly, as
        ///     "too basic, no challenge at all".
        ///   - Pass 3: made slack a two-sided band that ramps up, restoring measured difficulty.
        ///     Rejected from real play on Level 7, which is exactly the state pass 3 was designed
        ///     to produce: all pairs connected, cells still empty, no feedback.
        ///   - Pass 4: back to a hard slack of 0, this time permanently. Safe, but the measured
        ///     difficulty went DOWN as colour count rose (Level 1: 21.3, Level 10: 13.3) because
        ///     zero slack structurally pins over half of DifficultyAnalyzer's weighting -- winding
        ///     is exactly 1.0 by definition, decisions collapse to forced moves, dead ends vanish.
        ///   - Pass 5 (this one): slack was never the right thing to constrain. It is a property of
        ///     the INTENDED solution, but the bug is about OTHER arrangements -- the player got
        ///     stuck because some different pairing connected everything without filling the board.
        ///     Constraining slack only forbids that by making every path rigid, which is why it
        ///     kept trading the rule against the puzzle. Asking the real question instead
        ///     (enumerate every pairing, require all of them to cover) enforces the rule exactly,
        ///     while leaving paths free to wind as much as they like.
        ///
        /// With slack unconstrained, board size and colour count become real difficulty levers
        /// again. Measured, sampling 25 candidates per configuration and keeping only those that
        /// pass the coverage rule (DifficultyAnalyzer score of the survivors):
        ///   - 4x4 / 4 colours: ~21   (what the previous, zero-slack pass shipped)
        ///   - 4x4 / 3 colours: ~31
        ///   - 5x5 / 5-6 colours: ~40
        ///   - 6x6 / 7-8 colours: ~43-44  (Medium tier)
        /// Note the direction: on a FIXED board, more colours means shorter, more forced segments
        /// and an easier puzzle -- which is why the previous pass got easier as it added colours.
        /// Colour count only helps when the board grows with it, and it has to: bigger boards are
        /// where the coverage rule has room to hold while paths still wind. Note also what does NOT
        /// work: 5x5 at only 4 colours never passed the rule once in 40 samples -- long paths on an
        /// open board leave too much room for some other pairing to short-cut and strand a cell.
        /// Hit rates among solvable candidates are low throughout (~4-16%), hence MaxAttempts far
        /// above other ranges.
        ///
        /// <b>What the rule really demands, measured:</b> boards that pass it almost always have
        /// exactly ONE way to connect the pairs at all. Counting pairings directly on candidate
        /// boards shows why -- as soon as more than one exists, nearly all the extras are the
        /// partial-coverage kind (one 6x6 sample had 104 pairings of which 102 left a hole; another
        /// had 20 with 18 partial). So this rule is, in effect, "the connect-the-pairs problem has
        /// a unique answer", and it is that uniqueness -- not slack, and not raw board size -- that
        /// keeps a player from ever reaching the connected-but-incomplete state. It also explains
        /// the low hit rate: most randomly-generated boards are far too loose to qualify.
        ///
        /// PairingEnumerationCap doubles as the cost guard this needs. A board with more pairings
        /// than the cap is rejected as unproven, which is cheap (the enumeration stops at the cap)
        /// and almost always the right answer anyway, since a board that loose is overwhelmingly
        /// likely to contain a partial one.
        ///
        /// Per-level ramp:
        ///   1-2: 4x4 / 4 colours -- gentlest possible introduction.
        ///   3:   4x4 / 3 colours -- fewer, longer paths on the same board.
        ///   4-5: 5x5 / 6 then 5 colours -- board grows; Basic Flow's capstone.
        ///   6:   5x5 / 5 colours + 2 Blocked -- introduces the mechanic at a deliberately LOWER
        ///        target than Level 5, so the new rule is what the player is thinking about
        ///        (never teach a new rule and spike difficulty in the same level).
        ///   7:   5x5 / 6 colours + 2 Blocked.
        ///   8-10: 6x6 / 7-8 colours + 2,3,3 Blocked -- the range's capstone.
        /// Board size grows alongside the blocked-cell count on purpose. Blocked cells REDUCE
        /// measured difficulty, because DifficultyAnalyzer scores only usable cells: excluding
        /// cells shortens paths and removes decisions, and blocked cells do not even count toward
        /// the constrained-cell ratio. Measured, holding the board fixed at 5x5: 0 blocked scores
        /// ~40, 3 blocked ~21, 4 blocked ~15-33. So adding blocked cells has to be paid for with a
        /// bigger board, or the level gets easier exactly when it is supposed to get harder.
        /// Blocked cells are always placed INTERIOR-ONLY here (see PlaceBlockedCells): the first
        /// version of this range used a single, uniformly-placed cell, which usually landed on an
        /// edge or in a corner where routing around it is indistinguishable from a slightly smaller
        /// board -- the player could finish the level without ever registering that the mechanic
        /// existed. Introducing a mechanic means making it unavoidable in play, not merely present
        /// in the data.
        /// Board and colour count alone only set the rough ceiling each level can reach; the
        /// per-level TargetScoreMin/Max bands are what actually make the curve monotonic, by making
        /// the search keep hunting until a candidate lands in the intended difficulty window
        /// instead of taking the first one that clears the coverage rule.
        /// </summary>
        private static GenerationSpec SpecForLevel1To10(int levelNumber)
        {
            int gridSize, colorCount, blockedCount, minPath;
            float avgPathMin, avgPathMax;

            switch (levelNumber)
            {
                // Levels 1-5: Basic Flow only. With no blocked cells available to close off
                // alternative routes, the coverage rule can only be satisfied by keeping the board
                // fairly full of colours, which caps how long the paths can get -- so this stretch
                // ramps mainly by raising the FLOOR (no trivial 2-3 cell pairs) rather than the mean.
                case 1: gridSize = 4; colorCount = 4; blockedCount = 0; minPath = 3; avgPathMin = 3.5f; avgPathMax = 5.0f; break;
                case 2: gridSize = 4; colorCount = 4; blockedCount = 0; minPath = 3; avgPathMin = 4.0f; avgPathMax = 5.5f; break;
                case 3: gridSize = 4; colorCount = 3; blockedCount = 0; minPath = 4; avgPathMin = 5.0f; avgPathMax = 6.5f; break;
                case 4: gridSize = 5; colorCount = 5; blockedCount = 0; minPath = 4; avgPathMin = 4.5f; avgPathMax = 6.0f; break;
                case 5: gridSize = 4; colorCount = 3; blockedCount = 0; minPath = 5; avgPathMin = 5.0f; avgPathMax = 6.5f; break;
                // Levels 6-10: the Blocked Cell mechanic is what finally makes LONG paths possible.
                // Blocked cells close off the alternative pairings that a sparse, few-colour board
                // would otherwise have, so colour count can drop and path length climb -- the
                // mechanic earns its place by making the puzzle better, not just by being present.
                case 6: gridSize = 5; colorCount = 4; blockedCount = 3; minPath = 4; avgPathMin = 5.0f; avgPathMax = 7.0f; break;
                case 7: gridSize = 5; colorCount = 3; blockedCount = 4; minPath = 5; avgPathMin = 6.5f; avgPathMax = 8.0f; break;
                case 8: gridSize = 6; colorCount = 5; blockedCount = 5; minPath = 5; avgPathMin = 6.0f; avgPathMax = 8.0f; break;
                case 9: gridSize = 6; colorCount = 4; blockedCount = 6; minPath = 6; avgPathMin = 7.0f; avgPathMax = 9.0f; break;
                default: gridSize = 6; colorCount = 4; blockedCount = 6; minPath = 6; avgPathMin = 7.5f; avgPathMax = 9.5f; break; // 10
            }

            // Only a coarse nudge on the snake's shape, and deliberately mild: the acceptance gates
            // below decide difficulty now, and biasing generation too hard toward turning mostly
            // just lowers the hit rate of the coverage rule rather than making better boards.
            float straightness = Mathf.Lerp(0.7f, 0.45f, (levelNumber - 1) / 9f);

            return new GenerationSpec
            {
                GridSize = gridSize,
                MinColorCount = colorCount,
                MaxColorCount = colorCount,
                StraightnessBias = straightness,
                // Deliberately NOT gated on DifficultyAnalyzer's score. For this range that score
                // is actively misleading -- it rewards grid size and colour count, both of which go
                // UP when a board is packed with more, shorter paths, so optimising it produced
                // levels that measured harder and played easier. The path-length band below is the
                // ranking criterion instead. The score is still computed and logged, just not
                // steered toward.
                TargetScoreMin = 0f,
                TargetScoreMax = 100f,
                MinPathCells = minPath,
                TargetAvgPathMin = avgPathMin,
                TargetAvgPathMax = avgPathMax,
                Uniqueness = UniquenessPolicy.Ignore, // the coverage rule below is the real constraint; stacking a uniqueness requirement on top mostly just starves the search
                BlockedCellCount = blockedCount,
                // Every blocked cell in this range sits off the outer ring, so the mechanic is
                // something the player has to route around and therefore actually learns -- see
                // PlaceBlockedCells.
                BlockedCellsInteriorOnly = true,
                // The hard rule. Slack is deliberately left unconstrained (defaults) -- see the
                // class doc for why bounding it was the wrong way to express this.
                RequireEveryPairingCoversBoard = true,
                RequireMechanicsNecessary = true,
                // Much higher than other ranges: the coverage rule rejects roughly 85-95% of
                // otherwise-valid candidates (measured), so the search needs real room. Bounded
                // rather than open-ended because the rule's check is the most expensive thing in
                // the pipeline -- see the note on 6x6 in the class doc.
                MaxAttempts = 1500
            };
        }

        /// <summary>
        /// Levels 11-15: Wall introduced (spec §6) -- a single walled edge throughout, always
        /// checked for necessity. Level 14 specifically combines Wall with a Blocked cell ("Wall
        /// interacts with Blocked Cell").
        /// </summary>
        private static GenerationSpec SpecForLevel11To15(int levelNumber, int gridSize)
        {
            bool hasBlockedCell = levelNumber == 14;

            float t = (levelNumber - 11) / 4f;
            float straightness = Mathf.Lerp(0.7f, 0.3f, t);

            // Same "small honest drift, not an invented ramp" calibration as Levels 6-10 -- one
            // walled edge moves the score about as little as one Blocked cell does.
            float bandCenter = Mathf.Lerp(37f, 41f, t);
            const float halfWidth = 4f;

            return new GenerationSpec
            {
                GridSize = gridSize,
                MinColorCount = 2,
                MaxColorCount = 3,
                StraightnessBias = straightness,
                TargetScoreMin = bandCenter - halfWidth,
                TargetScoreMax = bandCenter + halfWidth,
                Uniqueness = UniquenessPolicy.Ignore,
                BlockedCellCount = hasBlockedCell ? 1 : 0,
                WallCount = 1,
                RequireMechanicsNecessary = true,
                MaxAttempts = 300
            };
        }

        /// <summary>
        /// Levels 16-20: One-Way introduced (spec §7) -- a single One-Way cell throughout, its
        /// required entry direction always matching the intended solution's own approach, always
        /// checked for necessity. Level 19 combines One-Way with Wall and Blocked ("One-Way +
        /// Blocked + Wall"); Level 20 is the closing Learning Challenge for this mechanic, using
        /// the same combination.
        /// </summary>
        private static GenerationSpec SpecForLevel16To20(int levelNumber, int gridSize)
        {
            bool combineOthers = levelNumber >= 19;

            int minColors = levelNumber <= 17 ? 2 : 3;

            float t = (levelNumber - 16) / 4f;
            float straightness = Mathf.Lerp(0.7f, 0.3f, t);

            // Same "small honest drift" calibration as Levels 6-15 -- see the class doc.
            float bandCenter = Mathf.Lerp(38f, 43f, t);
            const float halfWidth = 4f;

            return new GenerationSpec
            {
                GridSize = gridSize,
                MinColorCount = minColors,
                MaxColorCount = 3,
                StraightnessBias = straightness,
                TargetScoreMin = bandCenter - halfWidth,
                TargetScoreMax = bandCenter + halfWidth,
                Uniqueness = UniquenessPolicy.Ignore,
                BlockedCellCount = combineOthers ? 1 : 0,
                WallCount = combineOthers ? 1 : 0,
                OneWayCount = 1,
                RequireMechanicsNecessary = true,
                MaxAttempts = 300
            };
        }

        /// <summary>
        /// Levels 21-25: Arrow introduced (spec's mechanic-teaching order) -- a single Arrow cell
        /// throughout, its forced exit direction always matching the intended solution's own
        /// direction of travel out of that cell, always checked for necessity. Levels 24-25
        /// combine Arrow with Blocked + Wall (the same "introduce alone, then recombine with what
        /// was already taught" shape as Wall's Level 14 and One-Way's Levels 19-20) -- not with
        /// One-Way, matching how One-Way's own combination levels didn't reach back further than
        /// the two mechanics immediately before it.
        /// </summary>
        private static GenerationSpec SpecForLevel21To25(int levelNumber, int gridSize)
        {
            bool combineOthers = levelNumber >= 24;

            int minColors = levelNumber <= 22 ? 2 : 3;

            float t = (levelNumber - 21) / 4f;
            float straightness = Mathf.Lerp(0.7f, 0.3f, t);

            // Same "small honest drift" calibration as Levels 6-20 -- see the class doc.
            float bandCenter = Mathf.Lerp(39f, 44f, t);
            const float halfWidth = 4f;

            return new GenerationSpec
            {
                GridSize = gridSize,
                MinColorCount = minColors,
                MaxColorCount = 3,
                StraightnessBias = straightness,
                TargetScoreMin = bandCenter - halfWidth,
                TargetScoreMax = bandCenter + halfWidth,
                Uniqueness = UniquenessPolicy.Ignore,
                BlockedCellCount = combineOthers ? 1 : 0,
                WallCount = combineOthers ? 1 : 0,
                ArrowCount = 1,
                RequireMechanicsNecessary = true,
                MaxAttempts = 300
            };
        }

        // ---------------------------------------------------------------------------------------
        // Candidate construction: Hamiltonian snake -> cut into per-colour segments -> LevelData.
        // ---------------------------------------------------------------------------------------

        private static bool TryBuildCandidate(GenerationSpec spec, int colorCount, System.Random rng,
            out LevelData data)
        {
            data = default;
            int size = spec.GridSize;

            bool[,] usable = PlaceBlockedCells(size, spec.BlockedCellCount, spec.BlockedCellsInteriorOnly, rng);
            int usableCount = (size * size) - spec.BlockedCellCount;

            List<(int Row, int Col)> snake = TryGenerateHamiltonianSnake(size, usable, usableCount, rng,
                spec.StraightnessBias);
            if (snake == null) { return false; }

            // Cut into segments BEFORE placing any other mechanic: One-Way (and later Arrow) must
            // never land on a dot/endpoint cell, and dot positions aren't known until segments are.
            List<List<(int Row, int Col)>> segments = CutIntoSegments(snake, colorCount, rng);
            HashSet<(int Row, int Col)> dotCells = new HashSet<(int, int)>();
            for (int s = 0; s < segments.Count; s++)
            {
                dotCells.Add(segments[s][0]);
                dotCells.Add(segments[s][segments[s].Count - 1]);
            }

            // PuzzleSolver never explores a pair in both directions: BoardTopology.CollectDots
            // registers a pair's two dots in board row-major scan order, and the solver always
            // starts that pair's search from index 0 of that pair, fixed for the whole search
            // (see PuzzleSolver.Solve's "Block firstStart = dots[pairIds[0]][0]" and the class doc
            // on Search's dotB). A directional mechanic (One-Way's entry, Arrow's exit) is only
            // satisfied by the intended solution if it's encoded relative to whichever direction
            // the solver will ACTUALLY walk that segment in -- which is the segment's own array
            // order only when its row-major-first endpoint happens to be array index 0, and the
            // exact reverse otherwise. Getting this wrong doesn't make a bad level; it makes the
            // intended candidate itself spuriously fail ValidateSolvability half the time,
            // burning attempts (and, for Arrow specifically, its own head-on rule then also blocks
            // entry from the "right" side outright) -- see reversedByCell below.
            Dictionary<(int Row, int Col), bool> reversedByCell = new Dictionary<(int, int), bool>();
            for (int s = 0; s < segments.Count; s++)
            {
                (int Row, int Col) segStart = segments[s][0];
                (int Row, int Col) segEnd = segments[s][segments[s].Count - 1];
                bool reversed = !IsRowMajorBefore(segStart, segEnd);
                for (int i = 0; i < segments[s].Count; i++)
                {
                    reversedByCell[segments[s][i]] = reversed;
                }
            }

            List<(int Row, int Col, Direction Dir)> walls = PlaceWalls(usable, size, snake, spec.WallCount, rng);
            if (walls.Count < spec.WallCount) { return false; } // not enough non-path edges -- retry with a fresh snake

            List<(int Row, int Col, Direction EntryDir)> oneWays =
                PlaceOneWayCells(snake, dotCells, reversedByCell, spec.OneWayCount, rng);
            if (oneWays.Count < spec.OneWayCount) { return false; } // not enough interior cells -- retry with a fresh snake

            // Arrow must never land on a dot/endpoint (same reason as One-Way) or on a cell
            // One-Way already claimed -- the two are mutually exclusive BlockTypes, so a cell
            // can't carry both.
            HashSet<(int Row, int Col)> arrowExcluded = new HashSet<(int, int)>(dotCells);
            for (int o = 0; o < oneWays.Count; o++) { arrowExcluded.Add((oneWays[o].Row, oneWays[o].Col)); }

            List<(int Row, int Col, Direction ExitDir)> arrows =
                PlaceArrowCells(snake, arrowExcluded, reversedByCell, spec.ArrowCount, rng);
            if (arrows.Count < spec.ArrowCount) { return false; } // not enough interior cells -- retry with a fresh snake

            List<PairColorType> palette = PickDistinctColors(colorCount, rng);

            PairColorType[,] colorGrid = new PairColorType[size, size];
            for (int s = 0; s < segments.Count; s++)
            {
                (int Row, int Col) start = segments[s][0];
                (int Row, int Col) end = segments[s][segments[s].Count - 1];
                colorGrid[start.Row, start.Col] = palette[s];
                colorGrid[end.Row, end.Col] = palette[s];
            }

            int[,] wallMaskGrid = new int[size, size];
            for (int w = 0; w < walls.Count; w++)
            {
                wallMaskGrid[walls[w].Row, walls[w].Col] |= WallBit(walls[w].Dir);
            }

            BlockType[,] typeGrid = new BlockType[size, size];
            Direction[,] requiredEntryGrid = new Direction[size, size];
            Direction[,] forcedExitGrid = new Direction[size, size];
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++)
                {
                    typeGrid[r, c] = usable[r, c] ? BlockType.Normal : BlockType.Blocked;
                }
            }
            for (int o = 0; o < oneWays.Count; o++)
            {
                typeGrid[oneWays[o].Row, oneWays[o].Col] = BlockType.OneWay;
                requiredEntryGrid[oneWays[o].Row, oneWays[o].Col] = oneWays[o].EntryDir;
            }
            for (int a = 0; a < arrows.Count; a++)
            {
                typeGrid[arrows[a].Row, arrows[a].Col] = BlockType.Arrow;
                forcedExitGrid[arrows[a].Row, arrows[a].Col] = arrows[a].ExitDir;
            }

            data = new LevelData
            {
                gridSize = (GridSize)size,
                pairCount = colorCount,
                gridRows = new GridRow[size]
            };

            for (int r = 0; r < size; r++)
            {
                PairColorType[] colorRow = new PairColorType[size];
                BlockType[] typeRow = new BlockType[size];
                int[] wallRow = new int[size];
                Direction[] entryRow = new Direction[size];
                Direction[] exitRow = new Direction[size];
                for (int c = 0; c < size; c++)
                {
                    colorRow[c] = colorGrid[r, c];
                    typeRow[c] = typeGrid[r, c];
                    wallRow[c] = wallMaskGrid[r, c];
                    entryRow[c] = requiredEntryGrid[r, c];
                    exitRow[c] = forcedExitGrid[r, c];
                }
                data.gridRows[r] = new GridRow
                {
                    coloum = colorRow,
                    blockType = typeRow,
                    wallMask = wallRow,
                    requiredEntryDirection = entryRow,
                    forcedExitDirection = exitRow
                };
            }

            return true;
        }

        /// <summary>
        /// Chooses OneWayCount interior (non-dot) path cells and locks each to the direction the
        /// solver will ACTUALLY be moving in when it enters that cell along the intended solution
        /// -- the only direction it will ever be asked to admit, so the solution trivially
        /// satisfies its own constraint. Never chosen from index 0 (the very first cell of the
        /// whole snake has no "entry direction" -- it's always a dot anyway) or any cell in
        /// <paramref name="dotCells"/> (a One-Way constraint on an endpoint isn't meaningful: a
        /// pair's path only ever touches its own dot once, at whichever end it happens to be).
        ///
        /// "Actually be moving in" is not always the snake's own array order -- see
        /// <paramref name="reversedByCell"/> and TryBuildCandidate's own doc comment on it.
        /// </summary>
        private static List<(int Row, int Col, Direction EntryDir)> PlaceOneWayCells(
            List<(int Row, int Col)> snake, HashSet<(int Row, int Col)> dotCells,
            Dictionary<(int Row, int Col), bool> reversedByCell, int count, System.Random rng)
        {
            List<(int Row, int Col, Direction EntryDir)> result = new List<(int, int, Direction)>();
            if (count <= 0) { return result; }

            List<int> candidateIndices = new List<int>();
            for (int i = 1; i < snake.Count; i++)
            {
                if (dotCells.Contains(snake[i])) { continue; }
                candidateIndices.Add(i);
            }

            for (int i = candidateIndices.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (candidateIndices[i], candidateIndices[j]) = (candidateIndices[j], candidateIndices[i]);
            }

            int take = Math.Min(count, candidateIndices.Count);
            for (int k = 0; k < take; k++)
            {
                int idx = candidateIndices[k];
                (int Row, int Col) cell = snake[idx];
                (int Row, int Col) prev = snake[idx - 1];
                (int Row, int Col) next = snake[idx + 1];

                // Both always exist here: a surviving candidate index is never a dot cell (the
                // dotCells exclusion above already rules that out for both ends of the segment it
                // sits in), so idx is strictly interior to its segment and both array-neighbours
                // are real path cells.
                Direction actualEntry = reversedByCell[cell]
                    ? BoardTopology.Opposite(DirectionOfTravel(cell, next))
                    : DirectionOfTravel(prev, cell);
                result.Add((cell.Row, cell.Col, actualEntry));
            }
            return result;
        }

        /// <summary>The direction of movement from <paramref name="from"/> to the orthogonally
        /// adjacent <paramref name="to"/> -- the "incoming direction" Block.CanEnterFrom expects,
        /// exactly what a path is doing when it steps from one cell into the next.</summary>
        private static Direction DirectionOfTravel((int Row, int Col) from, (int Row, int Col) to)
        {
            if (to.Row < from.Row) { return Direction.Up; }
            if (to.Row > from.Row) { return Direction.Down; }
            if (to.Col < from.Col) { return Direction.Left; }
            if (to.Col > from.Col) { return Direction.Right; }
            return Direction.None;
        }

        /// <summary>Board row-major scan order -- smaller row first, then smaller column -- the
        /// exact order BoardTopology.CollectDots's nested loop visits cells in, and therefore which
        /// of a pair's two dots PuzzleSolver treats as index 0 (its fixed search start; see
        /// TryBuildCandidate's reversedByCell doc comment).</summary>
        private static bool IsRowMajorBefore((int Row, int Col) a, (int Row, int Col) b)
        {
            if (a.Row != b.Row) { return a.Row < b.Row; }
            return a.Col < b.Col;
        }

        /// <summary>
        /// Chooses ArrowCount interior path cells (never a dot, never a cell PlaceOneWayCells
        /// already claimed) and locks each to the direction the solver will ACTUALLY be moving in
        /// when it leaves that cell along the intended solution -- the only direction it will ever
        /// be asked to leave by, so the solution trivially satisfies its own constraint. Mirrors
        /// PlaceOneWayCells exactly, just reading the NEXT step instead of the previous one when
        /// not reversed (see <paramref name="reversedByCell"/>). Never chosen from the snake's very
        /// last cell (no "next" cell to derive an exit from) -- already covered by the dot-cell
        /// exclusion, since the whole snake's last cell is always its last segment's own endpoint.
        /// </summary>
        private static List<(int Row, int Col, Direction ExitDir)> PlaceArrowCells(
            List<(int Row, int Col)> snake, HashSet<(int Row, int Col)> excludedCells,
            Dictionary<(int Row, int Col), bool> reversedByCell, int count, System.Random rng)
        {
            List<(int Row, int Col, Direction ExitDir)> result = new List<(int, int, Direction)>();
            if (count <= 0) { return result; }

            List<int> candidateIndices = new List<int>();
            for (int i = 0; i < snake.Count - 1; i++)
            {
                if (excludedCells.Contains(snake[i])) { continue; }
                candidateIndices.Add(i);
            }

            for (int i = candidateIndices.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (candidateIndices[i], candidateIndices[j]) = (candidateIndices[j], candidateIndices[i]);
            }

            int take = Math.Min(count, candidateIndices.Count);
            for (int k = 0; k < take; k++)
            {
                int idx = candidateIndices[k];
                (int Row, int Col) cell = snake[idx];
                (int Row, int Col) prev = snake[idx - 1];
                (int Row, int Col) next = snake[idx + 1];

                // Both always exist here -- see PlaceOneWayCells's identical note; excludedCells
                // always includes dotCells, so idx is strictly interior to its segment.
                Direction actualExit = reversedByCell[cell]
                    ? BoardTopology.Opposite(DirectionOfTravel(prev, cell))
                    : DirectionOfTravel(cell, next);
                result.Add((cell.Row, cell.Col, actualExit));
            }
            return result;
        }

        /// <summary>
        /// Chooses WallCount edges to wall off, restricted to edges the intended snake solution
        /// never crosses -- placing a wall on an edge the solution itself needs would break the
        /// very thing generation just built (spec: mechanics must be constructed onto the
        /// solution, never bolted on afterward). Each undirected edge is considered exactly once
        /// (via Right/Down only) so the same wall can't be picked twice from either side. Returns
        /// fewer than requested if the board doesn't have enough non-path edges to offer -- the
        /// caller treats that as a failed attempt to retry with a fresh snake, not an error.
        /// </summary>
        private static List<(int Row, int Col, Direction Dir)> PlaceWalls(bool[,] usable, int size,
            List<(int Row, int Col)> snake, int wallCount, System.Random rng)
        {
            List<(int Row, int Col, Direction Dir)> result = new List<(int, int, Direction)>();
            if (wallCount <= 0) { return result; }

            HashSet<(int, int, int, int)> pathEdges = new HashSet<(int, int, int, int)>();
            for (int i = 0; i < snake.Count - 1; i++)
            {
                pathEdges.Add(NormalizedEdge(snake[i], snake[i + 1]));
            }

            List<(int Row, int Col, Direction Dir)> candidates = new List<(int, int, Direction)>();
            (Direction dir, int dr, int dc)[] probe = { (Direction.Right, 0, 1), (Direction.Down, 1, 0) };

            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++)
                {
                    if (!usable[r, c]) { continue; }

                    for (int p = 0; p < probe.Length; p++)
                    {
                        int nr = r + probe[p].dr;
                        int nc = c + probe[p].dc;
                        if (nr < 0 || nr >= size || nc < 0 || nc >= size || !usable[nr, nc]) { continue; }
                        if (pathEdges.Contains(NormalizedEdge((r, c), (nr, nc)))) { continue; }

                        candidates.Add((r, c, probe[p].dir));
                    }
                }
            }

            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            int count = Math.Min(wallCount, candidates.Count);
            for (int i = 0; i < count; i++) { result.Add(candidates[i]); }
            return result;
        }

        private static (int, int, int, int) NormalizedEdge((int Row, int Col) a, (int Row, int Col) b)
        {
            // Order-independent key so an edge found from either side matches the same entry.
            if (a.Row < b.Row || (a.Row == b.Row && a.Col < b.Col)) { return (a.Row, a.Col, b.Row, b.Col); }
            return (b.Row, b.Col, a.Row, a.Col);
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

        /// <summary>
        /// Chooses BlockedCellCount cells to exclude from the board, retrying until the
        /// remaining usable cells form a single connected region -- a disconnected or
        /// single-cell-island remainder (spec §15-17) could never admit one Hamiltonian path
        /// covering it, so there is no point even trying the snake search against it.
        /// </summary>
        /// <summary>
        /// Excludes <paramref name="blockedCount"/> cells from the board, retrying until whatever
        /// remains is still a single connected region.
        ///
        /// <paramref name="interiorOnly"/> confines them to cells off the outer ring, and exists
        /// for a gameplay reason rather than a technical one: a blocked cell on an edge -- and
        /// especially in a corner -- is invisible in play, because routing around it is
        /// indistinguishable from the board simply being smaller there. Nothing forces the player
        /// to notice it, so a level introducing the mechanic that way teaches nothing. An interior
        /// cell has paths obliged to bend around it on both sides, which is the thing the player is
        /// meant to learn. Uniform placement is heavily biased toward the invisible case (12 of 16
        /// cells on a 4x4 board are edge cells, 16 of 25 on a 5x5), so this has to be asked for
        /// explicitly rather than left to chance.
        /// </summary>
        private static bool[,] PlaceBlockedCells(int size, int blockedCount, bool interiorOnly, System.Random rng)
        {
            bool[,] usable = new bool[size, size];

            if (blockedCount <= 0)
            {
                for (int r = 0; r < size; r++) { for (int c = 0; c < size; c++) { usable[r, c] = true; } }
                return usable;
            }

            int interiorSpan = size - 2; // cells with row/col in [1, size-2]
            if (interiorOnly && (interiorSpan < 1 || blockedCount > interiorSpan * interiorSpan))
            {
                throw new InvalidOperationException("LevelGenerator: asked for " + blockedCount +
                    " interior blocked cell(s) on a " + size + "x" + size + " board, which only has " +
                    Math.Max(0, interiorSpan * interiorSpan) + " interior cell(s). This is a spec " +
                    "authoring error, not a generation failure -- lower the count or grow the board.");
            }

            for (int attempt = 0; attempt < 200; attempt++)
            {
                for (int r = 0; r < size; r++) { for (int c = 0; c < size; c++) { usable[r, c] = true; } }

                int placed = 0;
                int guard = 0;
                while (placed < blockedCount && guard < 2000)
                {
                    guard++;
                    int r = interiorOnly ? 1 + rng.Next(interiorSpan) : rng.Next(size);
                    int c = interiorOnly ? 1 + rng.Next(interiorSpan) : rng.Next(size);
                    if (!usable[r, c]) { continue; }
                    usable[r, c] = false;
                    placed++;
                }

                if (placed == blockedCount && IsSingleConnectedRegion(usable, size)) { return usable; }
            }

            throw new InvalidOperationException("LevelGenerator: could not place " + blockedCount +
                " blocked cell(s) on a " + size + "x" + size + " board while keeping the rest connected.");
        }

        private static bool IsSingleConnectedRegion(bool[,] usable, int size)
        {
            int totalUsable = 0;
            int startRow = -1, startCol = -1;
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++)
                {
                    if (!usable[r, c]) { continue; }
                    totalUsable++;
                    if (startRow < 0) { startRow = r; startCol = c; }
                }
            }
            if (totalUsable == 0) { return false; }

            bool[,] seen = new bool[size, size];
            Queue<(int Row, int Col)> queue = new Queue<(int, int)>();
            seen[startRow, startCol] = true;
            queue.Enqueue((startRow, startCol));
            int reached = 1;

            while (queue.Count > 0)
            {
                (int row, int col) = queue.Dequeue();
                for (int i = 0; i < StepDirs.Length; i++)
                {
                    int nr = row + StepDirs[i].dr;
                    int nc = col + StepDirs[i].dc;
                    if (nr < 0 || nr >= size || nc < 0 || nc >= size) { continue; }
                    if (!usable[nr, nc] || seen[nr, nc]) { continue; }

                    seen[nr, nc] = true;
                    reached++;
                    queue.Enqueue((nr, nc));
                }
            }

            return reached == totalUsable;
        }

        private static readonly (int dr, int dc)[] StepDirs =
        {
            (0, -1), (0, 1), (-1, 0), (1, 0) // Left, Right, Up, Down
        };

        /// <summary>
        /// Returns null (rather than throwing) when no Hamiltonian path over the usable cells was
        /// found within budget -- a specific Blocked-cell placement can be connected yet still
        /// admit no full-coverage path at all (a parity argument, same one Phase 6 documented for
        /// why Blocked cells are so often Required), and that is a failed ATTEMPT for the caller
        /// to retry with a fresh placement, not a fatal error.
        /// </summary>
        private static List<(int Row, int Col)> TryGenerateHamiltonianSnake(int size, bool[,] usable,
            int usableCount, System.Random rng, float straightnessBias)
        {
            for (int restart = 0; restart < 20; restart++)
            {
                bool[,] visited = new bool[size, size];
                List<(int Row, int Col)> path = new List<(int, int)>();

                int startRow, startCol;
                int guard = 0;
                do
                {
                    startRow = rng.Next(size);
                    startCol = rng.Next(size);
                    guard++;
                }
                while (!usable[startRow, startCol] && guard < 1000);
                if (!usable[startRow, startCol]) { return null; }

                path.Add((startRow, startCol));
                visited[startRow, startCol] = true;

                if (ExtendSnake(path, visited, size, usable, usableCount, rng, straightnessBias)) { return path; }
            }

            return null;
        }

        private static bool ExtendSnake(List<(int Row, int Col)> path, bool[,] visited, int size, bool[,] usable,
            int usableCount, System.Random rng, float straightnessBias)
        {
            if (path.Count == usableCount) { return true; }

            (int dr, int dc)[] order = OrderedSteps(path, rng, straightnessBias);
            (int Row, int Col) current = path[path.Count - 1];

            for (int i = 0; i < order.Length; i++)
            {
                int nr = current.Row + order[i].dr;
                int nc = current.Col + order[i].dc;
                if (nr < 0 || nr >= size || nc < 0 || nc >= size || !usable[nr, nc] || visited[nr, nc]) { continue; }

                visited[nr, nc] = true;
                path.Add((nr, nc));

                if (ExtendSnake(path, visited, size, usable, usableCount, rng, straightnessBias)) { return true; }

                path.RemoveAt(path.Count - 1);
                visited[nr, nc] = false;
            }

            return false;
        }

        /// <summary>
        /// A shuffled direction order, biased toward repeating the previous step's direction
        /// first when <paramref name="straightnessBias"/> "hits" -- the one knob that makes the
        /// snake favour long straight runs or constant turning, with nothing else about the
        /// search changing.
        /// </summary>
        private static (int dr, int dc)[] OrderedSteps(List<(int Row, int Col)> path, System.Random rng,
            float straightnessBias)
        {
            (int dr, int dc)[] shuffled = (StepDirs.Clone() as (int, int)[]);
            for (int i = shuffled.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            if (path.Count >= 2 && rng.NextDouble() < straightnessBias)
            {
                (int Row, int Col) last = path[path.Count - 1];
                (int Row, int Col) prev = path[path.Count - 2];
                (int dr, int dc) lastDir = (last.Row - prev.Row, last.Col - prev.Col);

                int idx = Array.IndexOf(shuffled, lastDir);
                if (idx > 0)
                {
                    (shuffled[0], shuffled[idx]) = (shuffled[idx], shuffled[0]);
                }
            }

            return shuffled;
        }

        private static List<List<(int Row, int Col)>> CutIntoSegments(List<(int Row, int Col)> snake,
            int segmentCount, System.Random rng)
        {
            int[] lengths = DistributeLengths(snake.Count, segmentCount, rng);

            List<List<(int Row, int Col)>> segments = new List<List<(int, int)>>();
            int index = 0;
            for (int s = 0; s < segmentCount; s++)
            {
                segments.Add(snake.GetRange(index, lengths[s]));
                index += lengths[s];
            }
            return segments;
        }

        private static int[] DistributeLengths(int total, int segmentCount, System.Random rng)
        {
            int[] lengths = new int[segmentCount];
            for (int i = 0; i < segmentCount; i++) { lengths[i] = 2; } // every pair needs 2 distinct dots

            int remaining = total - (segmentCount * 2);
            for (int i = 0; i < remaining; i++)
            {
                lengths[rng.Next(segmentCount)]++;
            }
            return lengths;
        }

        private static List<PairColorType> PickDistinctColors(int count, System.Random rng)
        {
            List<PairColorType> pool = new List<PairColorType>((PairColorType[])Enum.GetValues(typeof(PairColorType)));
            pool.Remove(PairColorType.None);

            List<PairColorType> chosen = new List<PairColorType>();
            for (int i = 0; i < count; i++)
            {
                int idx = rng.Next(pool.Count);
                chosen.Add(pool[idx]);
                pool.RemoveAt(idx);
            }
            return chosen;
        }

        // ---------------------------------------------------------------------------------------
        // Headless Block[,] construction for validation -- mirrors BoardGenerator.GenerateBoard's
        // structural field mapping, without any of its visual/prefab/GamePlayController.Instance
        // dependencies, which validation has no use for. See BlockTestHarness (Assets/Tests/Editor)
        // for the same technique applied to hand-built test scenarios; kept separate here so the
        // generator has no dependency on test code.
        // ---------------------------------------------------------------------------------------

        private static Block[,] BuildBlockGrid(LevelData data, out int rows, out int cols)
        {
            rows = (int)data.gridSize;
            cols = (int)data.gridSize;
            Block[,] grid = new Block[rows, cols];

            for (int r = 0; r < rows; r++)
            {
                PairColorType[] colorRow = data.gridRows[r].coloum;
                BlockType[] typeRow = data.gridRows[r].blockType;
                int[] wallRow = data.gridRows[r].wallMask;
                Direction[] entryRow = data.gridRows[r].requiredEntryDirection;
                Direction[] exitRow = data.gridRows[r].forcedExitDirection;
                for (int c = 0; c < cols; c++)
                {
                    PairColorType color = colorRow[c];
                    BlockType blockType = (typeRow != null && c < typeRow.Length) ? typeRow[c] : BlockType.Normal;
                    int wallMask = (wallRow != null && c < wallRow.Length) ? wallRow[c] : 0;
                    Direction requiredEntry = (entryRow != null && c < entryRow.Length) ? entryRow[c] : Direction.None;
                    Direction forcedExit = (exitRow != null && c < exitRow.Length) ? exitRow[c] : Direction.None;

                    GameObject go = new GameObject("GenBlock_" + r + "_" + c);
                    Block block = go.AddComponent<Block>();
                    SetField(block, "row_ID", r);
                    SetField(block, "coloum_ID", c);
                    SetField(block, "blockType", blockType);
                    SetField(block, "wallMask", wallMask);
                    SetField(block, "requiredEntryDirection", requiredEntry);
                    SetField(block, "forcedExitDirection", forcedExit);

                    if (color != PairColorType.None)
                    {
                        SetField(block, "isPairBlock", true);
                        SetField(block, "pairColorType", color);
                        SetField(block, "pairId", (int)color); // matches BoardGenerator's own color-fallback
                    }

                    grid[r, c] = block;
                }
            }

            return grid;
        }

        private static void DestroyBlockGrid(Block[,] grid)
        {
            if (grid == null) { return; }
            foreach (Block block in grid)
            {
                if (block != null) { UnityEngine.Object.DestroyImmediate(block.gameObject); }
            }
        }

        private static void SetField(Block block, string fieldName, object value)
        {
            FieldInfo field = typeof(Block).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(block, value);
        }

        // ---------------------------------------------------------------------------------------
        // Saving.
        // ---------------------------------------------------------------------------------------

        private static void SaveLevelAsset(string folder, int levelNumber, LevelData data, float difficultyScore)
        {
            data.difficultyScore = difficultyScore;
            string path = folder + "/Level_" + levelNumber + ".asset";
            SingleLevelDataSO existing = AssetDatabase.LoadAssetAtPath<SingleLevelDataSO>(path);

            if (existing != null)
            {
                existing.levelData = data;
                EditorUtility.SetDirty(existing);
            }
            else
            {
                SingleLevelDataSO asset = ScriptableObject.CreateInstance<SingleLevelDataSO>();
                asset.levelData = data;
                AssetDatabase.CreateAsset(asset, path);
            }
        }
    }
}
