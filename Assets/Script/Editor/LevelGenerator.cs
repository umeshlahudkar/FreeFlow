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
            bool cancelled = false;

            // Clear before starting, not just after finishing. Unity's cancel flag is sticky: once
            // DisplayCancelableProgressBar has reported a cancel it keeps reporting one until the
            // bar is cleared, so a cancelled run left the next run to abort on its very first poll
            // -- reported as "CANCELLED by user" when the user had done nothing. It looked
            // intermittent only because recompiling between runs reset it via the domain reload.
            EditorUtility.ClearProgressBar();

            for (int levelNumber = 1; levelNumber <= levelCount; levelNumber++)
            {
                GenerationSpec spec = SpecForLevel1To10(levelNumber);
                GeneratedLevel generated = TryGenerateLevel(spec, rng, seenCanonicalKeys,
                    attempt => { cancelled = cancelled || ReportGenerationProgress("Levels 1-10", levelNumber, attempt, spec.MaxAttempts); return cancelled; });
                if (cancelled)
                {
                    report.Append("Level ").Append(levelNumber).AppendLine(": CANCELLED by user");
                    break;
                }

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

            EditorUtility.ClearProgressBar();

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
            const int gridSize = 6;

            System.Random rng = new System.Random(20260831); // a fresh seed for this level range
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1, seenCanonicalKeys);

            StringBuilder report = new StringBuilder();
            int savedCount = 0;
            bool cancelled = false;

            // Clear before starting, not just after finishing. Unity's cancel flag is sticky: once
            // DisplayCancelableProgressBar has reported a cancel it keeps reporting one until the
            // bar is cleared, so a cancelled run left the next run to abort on its very first poll
            // -- reported as "CANCELLED by user" when the user had done nothing. It looked
            // intermittent only because recompiling between runs reset it via the domain reload.
            EditorUtility.ClearProgressBar();

            for (int levelNumber = startLevel; levelNumber <= endLevel; levelNumber++)
            {
                GenerationSpec spec = SpecForLevel11To15(levelNumber, gridSize);
                GeneratedLevel generated = TryGenerateLevel(spec, rng, seenCanonicalKeys,
                    attempt => { cancelled = cancelled || ReportGenerationProgress("Levels 11-15", levelNumber, attempt, spec.MaxAttempts); return cancelled; });
                if (cancelled)
                {
                    report.Append("Level ").Append(levelNumber).AppendLine(": CANCELLED by user");
                    break;
                }

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

            EditorUtility.ClearProgressBar();

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
            const int gridSize = 6;

            System.Random rng = new System.Random(20260901); // a fresh seed for this level range
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1, seenCanonicalKeys);

            StringBuilder report = new StringBuilder();
            int savedCount = 0;
            bool cancelled = false;

            // Clear before starting, not just after finishing. Unity's cancel flag is sticky: once
            // DisplayCancelableProgressBar has reported a cancel it keeps reporting one until the
            // bar is cleared, so a cancelled run left the next run to abort on its very first poll
            // -- reported as "CANCELLED by user" when the user had done nothing. It looked
            // intermittent only because recompiling between runs reset it via the domain reload.
            EditorUtility.ClearProgressBar();

            for (int levelNumber = startLevel; levelNumber <= endLevel; levelNumber++)
            {
                GenerationSpec spec = SpecForLevel16To20(levelNumber, gridSize);
                GeneratedLevel generated = TryGenerateLevel(spec, rng, seenCanonicalKeys,
                    attempt => { cancelled = cancelled || ReportGenerationProgress("Levels 16-20", levelNumber, attempt, spec.MaxAttempts); return cancelled; });
                if (cancelled)
                {
                    report.Append("Level ").Append(levelNumber).AppendLine(": CANCELLED by user");
                    break;
                }

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

            EditorUtility.ClearProgressBar();

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
            const int gridSize = 7; // 7x7 became reachable once the strict coverage rule was dropped

            System.Random rng = new System.Random(20260902); // a fresh seed for this level range
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1, seenCanonicalKeys);

            StringBuilder report = new StringBuilder();
            int savedCount = 0;
            bool cancelled = false;

            // Clear before starting, not just after finishing. Unity's cancel flag is sticky: once
            // DisplayCancelableProgressBar has reported a cancel it keeps reporting one until the
            // bar is cleared, so a cancelled run left the next run to abort on its very first poll
            // -- reported as "CANCELLED by user" when the user had done nothing. It looked
            // intermittent only because recompiling between runs reset it via the domain reload.
            EditorUtility.ClearProgressBar();

            for (int levelNumber = startLevel; levelNumber <= endLevel; levelNumber++)
            {
                GenerationSpec spec = SpecForLevel21To25(levelNumber, gridSize);
                GeneratedLevel generated = TryGenerateLevel(spec, rng, seenCanonicalKeys,
                    attempt => { cancelled = cancelled || ReportGenerationProgress("Levels 21-25", levelNumber, attempt, spec.MaxAttempts); return cancelled; });
                if (cancelled)
                {
                    report.Append("Level ").Append(levelNumber).AppendLine(": CANCELLED by user");
                    break;
                }

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

            EditorUtility.ClearProgressBar();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("LevelGenerator: Levels 21-25 generation complete -- " + savedCount + "/" +
                (endLevel - startLevel + 1) + " levels saved.\n" + report);
        }

        /// <summary>
        /// EXPERIMENT, not campaign content. Writes levels 31-33 to test one question: does
        /// relaxing the full-coverage generation rule actually make levels more challenging?
        ///
        /// Every shipped level today has exactly ONE possible pairing, because
        /// RequireEveryPairingCoversBoard rejects any board where a second pairing exists (a
        /// second pairing nearly always leaves a hole). One pairing means no wrong routes, so the
        /// player traces the only line that exists rather than searching -- and it is also why
        /// mechanics come out decorative, since "necessary" means "removing it creates a second
        /// solution" and there is no second solution to create.
        ///
        /// These three drop that rule and rely on UniquenessPolicy.Require instead: the
        /// FULL-COVERAGE solution must still be unique, so each level still has exactly one
        /// winning answer. What changes is that wrong routes may exist for the player to try and
        /// reject. IsBoardFullyCovered still gates completion, so those wrong routes lose -- they
        /// are not alternative wins.
        ///
        /// 31 and 32 hold the board at 6x6 to isolate the rule change; 33 goes to 7x7, which the
        /// old rule could not reach at all (levels 9-10 were specced there and failed outright).
        /// What to look for in the report: pairings > 1 means real search exists, and
        /// necessity=Required on the mechanic means it finally does something.
        /// </summary>
        [MenuItem("FreeFlow/Level Generator/Prototype Levels 31-33 (relaxed coverage)")]
        public static void GeneratePrototypeLevels31To33()
        {
            const string levelsFolder = "Assets/Resources/Levels";
            const int startLevel = 31;
            const int endLevel = 33;

            System.Random rng = new System.Random(20260904);
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1, seenCanonicalKeys);

            StringBuilder report = new StringBuilder();
            int savedCount = 0;
            bool cancelled = false;

            // Clear before starting, not just after finishing. Unity's cancel flag is sticky: once
            // DisplayCancelableProgressBar has reported a cancel it keeps reporting one until the
            // bar is cleared, so a cancelled run left the next run to abort on its very first poll
            // -- reported as "CANCELLED by user" when the user had done nothing. It looked
            // intermittent only because recompiling between runs reset it via the domain reload.
            EditorUtility.ClearProgressBar();

            for (int levelNumber = startLevel; levelNumber <= endLevel; levelNumber++)
            {
                GenerationSpec spec = SpecForPrototype31To33(levelNumber);
                GeneratedLevel generated = TryGenerateLevel(spec, rng, seenCanonicalKeys,
                    attempt => { cancelled = cancelled || ReportGenerationProgress("Prototype 31-33", levelNumber, attempt, spec.MaxAttempts); return cancelled; });
                if (cancelled)
                {
                    report.Append("Level ").Append(levelNumber).AppendLine(": CANCELLED by user");
                    break;
                }

                if (generated == null)
                {
                    Debug.LogError("LevelGenerator: prototype level " + levelNumber + " failed after " +
                        spec.MaxAttempts + " attempts.");
                    report.Append("Level ").Append(levelNumber).Append(": FAILED\n");
                    continue;
                }

                SaveLevelAsset(levelsFolder, levelNumber, generated.Data, generated.DifficultyScore);
                savedCount++;
                report.Append("Level ").Append(levelNumber)
                    .Append(": grid=").Append(spec.GridSize)
                    .Append(" colors=").Append(generated.Data.pairCount)
                    .Append(" blocked=").Append(spec.BlockedCellCount)
                    .Append(" walls=").Append(spec.WallCount)
                    .Append(" avgPath=").Append(generated.AveragePathCells.ToString("0.0"))
                    .Append(" minPath=").Append(generated.ShortestPathCells)
                    .Append(" score=").Append(generated.DifficultyScore.ToString("0.0"))
                    .Append(" tier=").Append(generated.DifficultyTier)
                    .Append(" fullCoverageSolutions=").Append(generated.SolutionsFound)
                    .Append(generated.SolutionsFound == 1 ? " (unique)" : " (NOT UNIQUE)")
                    .Append('\n');
            }

            EditorUtility.ClearProgressBar();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("LevelGenerator: prototype 31-33 complete -- " + savedCount + "/" +
                (endLevel - startLevel + 1) + " levels saved.\n" + report);
        }

        /// <summary>Prototype spec -- see GeneratePrototypeLevels31To33. Mirrors the Wall range so
        /// the comparison is like-for-like, with the coverage rule off and uniqueness required.</summary>
        private static GenerationSpec SpecForPrototype31To33(int levelNumber)
        {
            bool bigBoard = levelNumber == 33;

            return new GenerationSpec
            {
                GridSize = bigBoard ? 7 : 6,
                MinColorCount = bigBoard ? 5 : 4,
                MaxColorCount = bigBoard ? 5 : 4,
                StraightnessBias = 0.5f,
                TargetScoreMin = 0f,
                TargetScoreMax = 100f,
                // The full-coverage solution must still be unique -- one winning answer per level.
                Uniqueness = UniquenessPolicy.Require,
                BlockedCellCount = bigBoard ? 4 : 3,
                BlockedCellsInteriorOnly = true,
                WallCount = 2,
                MinPathCells = 4,
                TargetAvgPathMin = 5.5f,
                TargetAvgPathMax = 12.0f,
                // THE experiment: off, where every shipped level has it on.
                RequireEveryPairingCoversBoard = false,
                RequireMechanicsNecessary = true,
                MaxAttempts = 20000
            };
        }

        [MenuItem("FreeFlow/Level Generator/Generate Levels 26-30 (Forbidden)")]
        public static void GenerateLevels26To30()
        {
            const string levelsFolder = "Assets/Resources/Levels";
            const int startLevel = 26;
            const int endLevel = 30;
            const int gridSize = 7; // 7x7 became reachable once the strict coverage rule was dropped

            System.Random rng = new System.Random(20260903); // a fresh seed for this level range
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1, seenCanonicalKeys);

            StringBuilder report = new StringBuilder();
            int savedCount = 0;
            bool cancelled = false;

            // Clear before starting, not just after finishing. Unity's cancel flag is sticky: once
            // DisplayCancelableProgressBar has reported a cancel it keeps reporting one until the
            // bar is cleared, so a cancelled run left the next run to abort on its very first poll
            // -- reported as "CANCELLED by user" when the user had done nothing. It looked
            // intermittent only because recompiling between runs reset it via the domain reload.
            EditorUtility.ClearProgressBar();

            for (int levelNumber = startLevel; levelNumber <= endLevel; levelNumber++)
            {
                GenerationSpec spec = SpecForLevel26To30(levelNumber, gridSize);
                GeneratedLevel generated = TryGenerateLevel(spec, rng, seenCanonicalKeys,
                    attempt => { cancelled = cancelled || ReportGenerationProgress("Levels 26-30", levelNumber, attempt, spec.MaxAttempts); return cancelled; });
                if (cancelled)
                {
                    report.Append("Level ").Append(levelNumber).AppendLine(": CANCELLED by user");
                    break;
                }

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
                    .Append(" forbidden=").Append(spec.ForbiddenCount)
                    .Append(" score=").Append(generated.DifficultyScore.ToString("0.0"))
                    .Append(" tier=").Append(generated.DifficultyTier)
                    .Append(" solutions=").Append(generated.SolutionsFound)
                    .Append(generated.SearchExhausted ? "" : "+")
                    .Append(generated.SolutionsFound == 1 ? " (unique)" : "")
                    .Append('\n');
            }

            EditorUtility.ClearProgressBar();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("LevelGenerator: Levels 26-30 generation complete -- " + savedCount + "/" +
                (endLevel - startLevel + 1) + " levels saved.\n" + report);
        }

        [MenuItem("FreeFlow/Level Generator/Generate Levels 41-45 (Checkpoint)")]
        public static void GenerateLevels41To45()
        {
            const string levelsFolder = "Assets/Resources/Levels";
            const int startLevel = 41;
            const int endLevel = 45;
            const int gridSize = 7;

            System.Random rng = new System.Random(20260919); // a fresh seed for this level range
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1, seenCanonicalKeys);

            StringBuilder report = new StringBuilder();
            int savedCount = 0;
            bool cancelled = false;

            EditorUtility.ClearProgressBar(); // sticky cancel flag -- see GenerateLevels31To35

            for (int levelNumber = startLevel; levelNumber <= endLevel; levelNumber++)
            {
                GenerationSpec spec = SpecForLevel41To45(levelNumber, gridSize);
                GeneratedLevel generated = TryGenerateLevel(spec, rng, seenCanonicalKeys,
                    attempt => { cancelled = cancelled || ReportGenerationProgress("Levels 41-45", levelNumber, attempt, spec.MaxAttempts); return cancelled; });
                if (cancelled)
                {
                    report.Append("Level ").Append(levelNumber).AppendLine(": CANCELLED by user");
                    break;
                }

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
                    .Append(" checkpoints=").Append(spec.CheckpointCount)
                    .Append(" score=").Append(generated.DifficultyScore.ToString("0.0"))
                    .Append(" tier=").Append(generated.DifficultyTier)
                    .Append(" solutions=").Append(generated.SolutionsFound)
                    .Append(generated.SearchExhausted ? "" : "+")
                    .Append(generated.SolutionsFound == 1 ? " (unique)" : "")
                    .Append('\n');
            }

            EditorUtility.ClearProgressBar();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("LevelGenerator: Levels 41-45 generation complete -- " + savedCount + "/" +
                (endLevel - startLevel + 1) + " levels saved.\n" + report);
        }

        [MenuItem("FreeFlow/Level Generator/Generate Levels 36-40 (Bridge)")]
        public static void GenerateLevels36To40()
        {
            const string levelsFolder = "Assets/Resources/Levels";
            const int startLevel = 36;
            const int endLevel = 40;
            const int gridSize = 7;

            System.Random rng = new System.Random(20260912); // a fresh seed for this level range
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1, seenCanonicalKeys);

            StringBuilder report = new StringBuilder();
            int savedCount = 0;
            bool cancelled = false;

            EditorUtility.ClearProgressBar(); // sticky cancel flag -- see GenerateLevels31To35

            for (int levelNumber = startLevel; levelNumber <= endLevel; levelNumber++)
            {
                GenerationSpec spec = SpecForLevel36To40(levelNumber, gridSize);
                GeneratedLevel generated = TryGenerateLevel(spec, rng, seenCanonicalKeys,
                    attempt => { cancelled = cancelled || ReportGenerationProgress("Levels 36-40", levelNumber, attempt, spec.MaxAttempts); return cancelled; });
                if (cancelled)
                {
                    report.Append("Level ").Append(levelNumber).AppendLine(": CANCELLED by user");
                    break;
                }

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
                    .Append(" bridges=").Append(spec.BridgeCount)
                    .Append(" score=").Append(generated.DifficultyScore.ToString("0.0"))
                    .Append(" tier=").Append(generated.DifficultyTier)
                    .Append(" solutions=").Append(generated.SolutionsFound)
                    .Append(generated.SearchExhausted ? "" : "+")
                    .Append(generated.SolutionsFound == 1 ? " (unique)" : "")
                    .Append('\n');
            }

            EditorUtility.ClearProgressBar();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("LevelGenerator: Levels 36-40 generation complete -- " + savedCount + "/" +
                (endLevel - startLevel + 1) + " levels saved.\n" + report);
        }

        [MenuItem("FreeFlow/Level Generator/Generate Levels 31-35 (Permitted)")]
        public static void GenerateLevels31To35()
        {
            const string levelsFolder = "Assets/Resources/Levels";
            const int startLevel = 31;
            const int endLevel = 35;
            const int gridSize = 7; // 7x7 became reachable once the strict coverage rule was dropped

            System.Random rng = new System.Random(20260905); // a fresh seed for this level range
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1, seenCanonicalKeys);

            StringBuilder report = new StringBuilder();
            int savedCount = 0;
            bool cancelled = false;

            // Clear before starting, not just after finishing. Unity's cancel flag is sticky: once
            // DisplayCancelableProgressBar has reported a cancel it keeps reporting one until the
            // bar is cleared, so a cancelled run left the next run to abort on its very first poll
            // -- reported as "CANCELLED by user" when the user had done nothing. It looked
            // intermittent only because recompiling between runs reset it via the domain reload.
            EditorUtility.ClearProgressBar();

            for (int levelNumber = startLevel; levelNumber <= endLevel; levelNumber++)
            {
                GenerationSpec spec = SpecForLevel31To35(levelNumber, gridSize);
                GeneratedLevel generated = TryGenerateLevel(spec, rng, seenCanonicalKeys,
                    attempt => { cancelled = cancelled || ReportGenerationProgress("Levels 31-35", levelNumber, attempt, spec.MaxAttempts); return cancelled; });
                if (cancelled)
                {
                    report.Append("Level ").Append(levelNumber).AppendLine(": CANCELLED by user");
                    break;
                }

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
                    .Append(" permitted=").Append(spec.PermittedCount)
                    .Append(" score=").Append(generated.DifficultyScore.ToString("0.0"))
                    .Append(" tier=").Append(generated.DifficultyTier)
                    .Append(" solutions=").Append(generated.SolutionsFound)
                    .Append(generated.SearchExhausted ? "" : "+")
                    .Append(generated.SolutionsFound == 1 ? " (unique)" : "")
                    .Append('\n');
            }

            EditorUtility.ClearProgressBar();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("LevelGenerator: Levels 31-35 generation complete -- " + savedCount + "/" +
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

            /// <summary>How many Forbidden cells to place on interior (non-dot) path cells, each
            /// naming a colour that does NOT pass through it in the intended solution -- see
            /// PlaceForbiddenCells for why naming the cell's own colour would be self-defeating.</summary>
            public int ForbiddenCount;

            /// <summary>How many Permitted cells to place on interior (non-dot) path cells, each
            /// naming the colour that DOES pass through it -- the exact inverse of Forbidden, since
            /// a Permitted cell refuses every colour it does not name. See PlacePermittedCells.</summary>
            public int PermittedCount;

            /// <summary>How many Bridge cells to place. Unlike every mechanic above, this one is
            /// NOT placed onto a finished solution -- a Bridge carries two paths crossing at right
            /// angles, and no amount of decorating a partition after the fact can create a second
            /// path through a cell that already belongs to one. The cells are chosen BEFORE the
            /// partition and fed into it, where each becomes two independent lane nodes. See
            /// ChooseBridgeCells and TryGeneratePathPartition's bridges parameter.</summary>
            public int BridgeCount;

            /// <summary>How many Checkpoint cells to place on interior (non-dot) path cells, each
            /// naming the colour that passes through it and requiring that it still does. Unlike
            /// every other rule cell, a Checkpoint does not police entry -- anyone may cross it,
            /// and the requirement is tested only when the pair is complete. See
            /// PlaceCheckpointCells.</summary>
            public int CheckpointCount;

            /// <summary>Minimum number of WRONG routes the board must admit -- pairings that
            /// connect every colour but fail to cover the board, so the player can complete all
            /// the dots and still be refused. 0 disables the check.
            ///
            /// This is the honest measure of whether a level is a puzzle or a trace. A board whose
            /// only pairing is the winning one has nothing to search: the player draws the single
            /// line that exists. Levels 11-35 all landed at 3 or more without being asked to, but
            /// two of the first Bridge levels came out at 1, so it is worth stating rather than
            /// hoping for -- see §6.20.</summary>
            public int MinWrongRoutes;

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

        /// <summary>
        /// <paramref name="shouldCancel"/> is polled with the current attempt index and returns
        /// true to abort the search early, keeping whatever candidate is best so far (which may be
        /// none). This is the ONLY way to interrupt generation: the menu entry points run
        /// synchronously on Unity's main thread, so once this loop starts nothing else in the
        /// editor -- rendering, input, the MCP socket -- gets a turn until it returns. Without a
        /// cancel hook a badly-chosen spec means an uninterruptible freeze, which is exactly how an
        /// earlier 7x7 experiment locked the editor up.
        /// </summary>
        public static GeneratedLevel TryGenerateLevel(GenerationSpec spec, System.Random rng,
            HashSet<string> seenCanonicalKeys, Func<int, bool> shouldCancel = null)
        {
            GeneratedLevel best = null;
            string bestKey = null;
            float bestPenalty = float.MaxValue;

            for (int attempt = 0; attempt < spec.MaxAttempts; attempt++)
            {
                // Polled rather than checked every attempt: the callback repaints the editor's
                // progress bar, which costs more than a whole cheap attempt does.
                if (shouldCancel != null && (attempt % 25) == 0 && shouldCancel(attempt)) { break; }

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

                    // Full-coverage multi-pair solving is NP-hard, and the cost climbs steeply with
                    // board size: measured, a 6x6 settles well inside 300k steps, a 7x7 wants
                    // millions, and an 8x8 needs on the order of 8M (~1-3 seconds) to finish rather
                    // than time out. A flat budget silently reports Inconclusive on the larger
                    // boards, which the loop below then discards -- so a too-small budget doesn't
                    // produce bad levels, it just makes big boards look impossible.
                    int solverBudget = SolverBudgetFor(spec.GridSize);
                    PuzzleSolver.SolveResult solveResult = LevelValidator.ValidateSolvability(
                        grid, rows, cols, new PuzzleSolver.SolverOptions(solverBudget, 2));

                    if (solveResult.Status != PuzzleSolver.SolveStatus.Solved) { continue; }

                    // Cheap, and rejects the single most common way a level ends up feeling
                    // trivial, so it runs before anything expensive -- see MinPathCells.
                    MeasurePathLengths(solveResult, out int shortestPath, out float averagePath);
                    if (shortestPath < spec.MinPathCells) { continue; }

                    string key = LevelCanonicalizer.ComputeCanonicalKey(grid, rows, cols);
                    if (seenCanonicalKeys.Contains(key)) { continue; }

                    // A HARD reject, not a penalty: penalties only RANK candidates, and the
                    // best-ranked one still ships when nothing hits its target exactly -- so
                    // expressing this as a penalty would let a board that breaks the rule through
                    // as a fallback. Skipping the candidate outright is what makes the rule
                    // unbreakable.
                    //
                    // Runs HERE, before the necessity checks and the difficulty analysis, because
                    // it is both the cheapest strong filter and the most selective: ~0.5ms, and it
                    // rejects the large majority of candidates. The necessity checks below are the
                    // opposite -- each one clones the board and runs two more solves PER mechanic
                    // instance, so on a board with a few walls plus a One-Way they cost tens of
                    // milliseconds. Running them first meant paying the expensive test on ~99% of
                    // candidates that this cheap test was about to discard anyway. (This gate used
                    // to sit lower, back when it enumerated 200 pairings and really was the
                    // expensive one; cutting PairingEnumerationCap to 2 inverted that, and the
                    // ordering was not revisited.)
                    if (spec.RequireEveryPairingCoversBoard && !EveryPairingCoversTheBoard(grid, rows, cols))
                    {
                        continue;
                    }

                    bool isUnique = solveResult.SolutionsFound == 1 && solveResult.SearchExhausted;

                    // Free (solveResult already knows) and highly selective, so it runs before the
                    // expensive gates below. Only a hard filter under Require, where a non-unique
                    // board is not shippable anyway -- Prefer keeps its soft tie-break penalty.
                    //
                    // This matters most on the ranges that switched the coverage rule OFF: that
                    // rule used to reject ~99% of candidates for ~0.5ms before anything costly ran,
                    // and turning it off silently removed the only cheap filter in front of the
                    // necessity checks. Level 21 then ran for ten minutes without finishing.
                    if (spec.Uniqueness == UniquenessPolicy.Require && !isUnique) { continue; }

                    // The constructor guarantees two DIFFERENT colours cross each bridge, but that
                    // guarantee covers the arrangement it built, not necessarily the one the player
                    // must find: the dots it derives admit other solutions, and the board can turn
                    // out to be uniquely solvable by one of those instead. A single colour running
                    // through both lanes is legal (Block.CanAcceptEntry waves through a pair that
                    // already owns the other axis), so nothing downstream rejects it -- it just
                    // makes the crossing art a lie and teaches the mechanic wrong. Level 40 shipped
                    // exactly that before this gate existed. Free: solveResult is already in hand,
                    // so it sits ahead of the necessity checks that re-solve the board.
                    if (spec.BridgeCount > 0 && !EveryBridgeCarriesTwoColours(solveResult, grid, rows, cols))
                    {
                        continue;
                    }

                    // Is there anything to search? Costs one partial-coverage solve, capped at the
                    // floor+1 so it stops as soon as the answer is known rather than enumerating
                    // every pairing. Only candidates that already passed solve/minPath/uniqueness
                    // get this far -- roughly one attempt in 150 -- so the amortised cost is
                    // negligible even though a single call is comparable to a whole attempt.
                    if (spec.MinWrongRoutes > 0)
                    {
                        PuzzleSolver.SolveResult pairings = LevelValidator.ValidateSolvability(grid, rows, cols,
                            new PuzzleSolver.SolverOptions(solverBudget, spec.MinWrongRoutes + 1, true));
                        if (pairings.SolutionsFound - 1 < spec.MinWrongRoutes) { continue; }
                    }

                    float uniquenessPenalty = isUnique ? 0f : spec.Uniqueness switch
                    {
                        UniquenessPolicy.Require => RequiredUniquenessPenalty,
                        UniquenessPolicy.Prefer => PreferredUniquenessPenalty,
                        _ => 0f
                    };

                    // HARD REJECTS, not penalties. As a penalty this gate almost never passed and
                    // almost always fell back to a candidate that failed it: an audit found only
                    // 13 of 41 mechanic instances across levels 11-30 load-bearing, with every
                    // Arrow in the game doing nothing. A mechanic the player can ignore is
                    // indistinguishable from one that is not there.
                    //
                    // This is only affordable because RequireEveryPairingCoversBoard is off for
                    // these ranges. That rule forced each board to have exactly ONE pairing, and
                    // "necessary" means removing the mechanic creates a SECOND solution -- with one
                    // pairing there was no second solution to create, so the two rules were
                    // mathematically opposed and this gate could not be satisfied. With wrong
                    // routes allowed to exist, a wall that rules one out is genuinely load-bearing:
                    // measured 6 of 6 on the relaxed prototypes against 1 of 5 on the old levels.
                    //
                    // Each check clones the board and re-solves it twice PER mechanic instance, so
                    // these are the most expensive things in the pipeline. Ordered cheapest-and-
                    // most-selective first: the range's HEADLINE mechanic (one instance, and the
                    // one actually at risk of being decorative) before Blocked, which has several
                    // instances and nearly always passes -- removing a Blocked cell adds a cell
                    // that must now be covered, which usually breaks the solution outright. Testing
                    // Blocked first meant paying for several solves before discovering the Arrow
                    // was decorative anyway.
                    if (spec.RequireMechanicsNecessary)
                    {
                        if (spec.ArrowCount > 0 && !AllCellsOfTypeAreNecessary(grid, rows, cols, BlockType.Arrow))
                        {
                            continue;
                        }
                        if (spec.ForbiddenCount > 0 && !AllCellsOfTypeAreNecessary(grid, rows, cols, BlockType.ForbiddenForPair))
                        {
                            continue;
                        }
                        if (spec.PermittedCount > 0 && !AllCellsOfTypeAreNecessary(grid, rows, cols, BlockType.AllowedForPairs))
                        {
                            continue;
                        }
                        if (spec.CheckpointCount > 0 && !AllCellsOfTypeAreNecessary(grid, rows, cols, BlockType.Checkpoint))
                        {
                            continue;
                        }
                        // A Bridge is the one mechanic whose necessity is answered by plain
                        // solvability rather than by solution count: it GRANTS capacity, so
                        // stripping it leaves the crossing with nowhere to go and the board
                        // unsolvable outright. RequiredMechanicValidator.Classify handles that
                        // case; nothing special is needed here beyond asking.
                        if (spec.BridgeCount > 0 && !AllCellsOfTypeAreNecessary(grid, rows, cols, BlockType.Bridge))
                        {
                            continue;
                        }
                        if (spec.OneWayCount > 0 && !AllCellsOfTypeAreNecessary(grid, rows, cols, BlockType.OneWay))
                        {
                            continue;
                        }
                        if (spec.WallCount > 0 && !AllWallsAreNecessary(grid, rows, cols))
                        {
                            continue;
                        }
                        if (spec.BlockedCellCount > 0 && !AllCellsOfTypeAreNecessary(grid, rows, cols, BlockType.Blocked))
                        {
                            continue;
                        }
                    }

                    // Scoring only, so it runs last, on the few candidates that survived every hard
                    // gate. Reuses solveResult rather than solving again -- see the class doc.
                    DifficultyAnalyzer.DifficultyReport report = DifficultyAnalyzer.Analyze(grid, rows, cols, solveResult);

                    int maxSlack = MaxSlackAcrossSolution(solveResult);
                    float slackBandDistance = BandPenalty(maxSlack, spec.MinSlackPerColor, spec.MaxSlackPerColor);
                    float slackPenalty = slackBandDistance > 0f ? RequiredUniquenessPenalty + slackBandDistance : 0f;

                    float penalty = BandPenalty(report.Score, spec.TargetScoreMin, spec.TargetScoreMax)
                        + BandPenalty(averagePath, spec.TargetAvgPathMin, spec.TargetAvgPathMax)
                        + uniquenessPenalty + slackPenalty;

                    // Ranked out already -- nothing below can change that.
                    if (penalty >= bestPenalty) { continue; }

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

        /// <summary>
        /// Shows a cancellable progress bar for one level's search and reports whether the user
        /// asked to stop. Pass as TryGenerateLevel's shouldCancel.
        /// </summary>
        private static bool ReportGenerationProgress(string rangeLabel, int levelNumber, int attempt,
            int maxAttempts)
        {
            float fraction = maxAttempts > 0 ? (float)attempt / maxAttempts : 0f;
            return EditorUtility.DisplayCancelableProgressBar(
                "Generating " + rangeLabel,
                "Level " + levelNumber + " -- attempt " + attempt + " / " + maxAttempts,
                fraction);
        }

        /// <summary>Solver step budget for a board of this side length -- see the call site for the
        /// measured costs this is derived from.</summary>
        private static int SolverBudgetFor(int gridSize)
        {
            if (gridSize <= 6) { return 300000; }
            if (gridSize == 7) { return 2000000; }
            return 8000000;
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
        /// Whether every Bridge on the board is crossed by two DIFFERENT colours in the winning
        /// solution, rather than by one colour using both of its lanes.
        ///
        /// Necessity does not cover this. A self-crossing bridge still passes
        /// RequiredMechanicValidator, because stripping it removes the straight-through rule and
        /// usually opens a second solution -- so it is genuinely load-bearing while still being the
        /// wrong picture. This asks the separate question the art promises: two colours, meeting.
        /// </summary>
        private static bool EveryBridgeCarriesTwoColours(PuzzleSolver.SolveResult solveResult,
            Block[,] grid, int rowCount, int colCount)
        {
            if (solveResult.Solutions == null) { return false; }

            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < colCount; c++)
                {
                    if (grid[r, c].BlockType != BlockType.Bridge) { continue; }

                    int coloursThrough = 0;
                    for (int s = 0; s < solveResult.Solutions.Count; s++)
                    {
                        List<(int Row, int Col)> cells = solveResult.Solutions[s].Cells;
                        int visits = 0;
                        for (int i = 0; i < cells.Count; i++)
                        {
                            if (cells[i].Row == r && cells[i].Col == c) { visits++; }
                        }

                        if (visits > 1) { return false; } // one colour taking both lanes
                        if (visits == 1) { coloursThrough++; }
                    }

                    if (coloursThrough != 2) { return false; }
                }
            }

            return true;
        }

        /// <summary>
        /// Spec's required-mechanic rule (§10/§27), applied to every cell of <paramref
        /// name="type"/> on the board: each one must be load-bearing, not decorative. One
        /// BlockType-parameterized scan covers Blocked, One-Way, Arrow, Forbidden, Allowed and
        /// Bridge alike, rather than a same-shaped copy per mechanic. Only Wall needs its own
        /// version, being an edge property with no BlockType at all (see AllWallsAreNecessary).
        ///
        /// Bridge reaches the right answer through a different branch rather than a different
        /// scan: it grants capacity instead of restricting it, so stripping it makes the board
        /// UNSOLVABLE rather than merely multi-solution. RequiredMechanicValidator.Classify treats
        /// that as Required, so the same call works unchanged.
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
        /// enumerate before giving up and reporting "can't tell". A board with more distinct
        /// pairings than this is treated as unproven, and therefore REJECTED -- never as proven
        /// safe -- so a low cap can only ever cost candidates, never let a bad one through.
        ///
        /// Deliberately 2, not a generous number. Measured across every board that has ever passed
        /// this rule: they have exactly ONE pairing. And whenever a board had more than one, nearly
        /// all the extras were the hole-leaving kind (one 6x6 sample: 104 pairings, 102 partial;
        /// another: 20 with 18 partial). So "a second pairing exists" is, in practice, already the
        /// answer -- enumerating 200 of them to confirm it just burned time. This was the dominant
        /// cost in generation, and dropping the cap from 200 to 2 cuts it by roughly two orders of
        /// magnitude. The trade is a rare false rejection (a board whose several pairings all
        /// happen to cover), which costs one retry.
        /// </summary>
        private const int PairingEnumerationCap = 2;

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
                new PuzzleSolver.SolverOptions(SolverBudgetFor(rowCount), PairingEnumerationCap, true));

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
                case 2: gridSize = 5; colorCount = 5; blockedCount = 0; minPath = 3; avgPathMin = 4.0f; avgPathMax = 6.0f; break;
                case 3: gridSize = 5; colorCount = 4; blockedCount = 0; minPath = 4; avgPathMin = 5.0f; avgPathMax = 7.0f; break;
                case 4: gridSize = 6; colorCount = 6; blockedCount = 0; minPath = 4; avgPathMin = 5.0f; avgPathMax = 7.0f; break;
                case 5: gridSize = 6; colorCount = 5; blockedCount = 0; minPath = 4; avgPathMin = 6.0f; avgPathMax = 8.0f; break;
                // Levels 6-10: the Blocked Cell mechanic is what finally makes LONG paths possible.
                // Blocked cells close off the alternative pairings that a sparse, few-colour board
                // would otherwise have, so colour count can drop and path length climb -- the
                // mechanic earns its place by making the puzzle better, not just by being present.
                case 6: gridSize = 6; colorCount = 5; blockedCount = 3; minPath = 4; avgPathMin = 5.5f; avgPathMax = 7.5f; break;
                case 7: gridSize = 6; colorCount = 5; blockedCount = 4; minPath = 5; avgPathMin = 6.0f; avgPathMax = 8.0f; break;
                case 8: gridSize = 6; colorCount = 4; blockedCount = 4; minPath = 5; avgPathMin = 7.0f; avgPathMax = 9.0f; break;
                // 7x7 was tried here and failed outright: the generator builds those boards fine,
                // but the coverage rule admits only ~11% of 7x7 candidates and 1500 attempts found
                // none that also hit a path-length band. 6x6 with more blocked cells buys the same
                // long paths at a hit rate that actually terminates -- the ceiling is the rule, not
                // the board size (see the class doc).
                case 9: gridSize = 6; colorCount = 4; blockedCount = 5; minPath = 6; avgPathMin = 7.0f; avgPathMax = 9.0f; break;
                default: gridSize = 6; colorCount = 4; blockedCount = 6; minPath = 7; avgPathMin = 7.5f; avgPathMax = 10.0f; break; // 10
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
                Uniqueness = UniquenessPolicy.Ignore, // spec §4/§34: very easy levels may have several solutions
                BlockedCellCount = blockedCount,
                // Every blocked cell in this range sits off the outer ring, so the mechanic is
                // something the player has to route around and therefore actually learns -- see
                // PlaceBlockedCells.
                BlockedCellsInteriorOnly = true,
                // The hard rule. Slack is deliberately left unconstrained (defaults) -- see the
                // class doc for why bounding it was the wrong way to express this.
                // Levels 1-10 KEEP the strict rule while the mechanic ranges relax it. This is
                // the tutorial: a brand-new player who connects every pair and is left staring at
                // empty cells has no idea what the game wants, and that is exactly what was
                // reported here. Guaranteeing it cannot happen is worth more over ten levels than
                // the challenge it costs -- these are meant to be easy. From level 11 the player
                // knows the goal, the cell counter shows progress, and the rule is dropped so the
                // puzzle can actually have wrong routes to reject.
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

            return new GenerationSpec
            {
                GridSize = gridSize,
                MinColorCount = 4,
                MaxColorCount = 4,
                StraightnessBias = straightness,
                TargetScoreMin = 0f,
                TargetScoreMax = 100f, // path length is the honest difficulty control, not the score -- see SpecForLevel1To10
                Uniqueness = UniquenessPolicy.Require,
                BlockedCellCount = hasBlockedCell ? 3 : 2,
                BlockedCellsInteriorOnly = true,
                // Starts at 2, not 1: a single walled edge cannot form a barrier, and PlaceWalls
                // now grows connected runs (an L or a longer wall) rather than scattering stubs.
                WallCount = 2 + (int)(t * 2f), // 2 -> 4 walls across the range
                MinPathCells = 5,
                TargetAvgPathMin = 6.5f,
                TargetAvgPathMax = 9.0f,
                RequireEveryPairingCoversBoard = false,
                RequireMechanicsNecessary = true,
                MaxAttempts = 20000
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

            float t = (levelNumber - 16) / 4f;
            float straightness = Mathf.Lerp(0.7f, 0.3f, t);

            return new GenerationSpec
            {
                GridSize = gridSize,
                MinColorCount = 4,
                MaxColorCount = 4,
                StraightnessBias = straightness,
                TargetScoreMin = 0f,
                TargetScoreMax = 100f,
                Uniqueness = UniquenessPolicy.Require,
                BlockedCellCount = combineOthers ? 3 : 2,
                BlockedCellsInteriorOnly = true,
                WallCount = combineOthers ? 2 : 0,
                OneWayCount = 1,
                MinPathCells = 5,
                TargetAvgPathMin = 6.5f,
                TargetAvgPathMax = 9.0f,
                RequireEveryPairingCoversBoard = false,
                RequireMechanicsNecessary = true,
                // 60000, matching the Arrow range: levels 19-20 share a spec, and at 12000 one
                // succeeded while the other found nothing -- the combination of coverage rule +
                // mechanic necessity admits candidates rarely enough that the budget, not the
                // spec, decides whether a level exists.
                MaxAttempts = 20000
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

            float t = (levelNumber - 21) / 4f;

            // Starts lower than the Wall and One-Way ranges (which begin at 0.7) because Arrow is
            // the most constrained mechanic here: on top of the coverage rule it must also pass the
            // necessity check, and its head-on entry rule rules out an approach the other mechanics
            // allow. Measured directly -- at 12000 attempts, Levels 21 (bias 0.7) and 22 (0.6) found
            // nothing at all while 23-25 (0.5 and below) succeeded first try. A straighter snake
            // yields shorter, straighter paths that cannot reach the average-length band while
            // staying tight enough for the coverage rule.
            float straightness = Mathf.Lerp(0.5f, 0.3f, t);

            return new GenerationSpec
            {
                GridSize = gridSize,
                // Six on a 7x7, not four. Measured: 4 colours cost 127ms per candidate and
                // produced zero unique solutions in 8 tries -- long paths on a big open board
                // make proving uniqueness both rare and slow, which is what left level 21
                // grinding for over five minutes. 6 colours costs 9ms and actually passes
                // (~4% of attempts clear uniqueness AND mechanic necessity). Blocked rises to
                // 5 for the same reason: these ranges introduce their mechanic alone, so
                // walls are unavailable to constrain routes and Blocked has to do that work.
                MinColorCount = 6,
                MaxColorCount = 6,
                StraightnessBias = straightness,
                TargetScoreMin = 0f,
                TargetScoreMax = 100f,
                Uniqueness = UniquenessPolicy.Require,
                // Denser than the Wall and One-Way ranges (which use 1-2). The coverage rule needs
                // the board to have exactly ONE pairing, and an open board simply has more; measured
                // on this range, blocked=1..4 produced zero candidates that cleared the rule in 400
                // attempts, blocked=5 produced 2. Blocked is already taught by level 6, so leaning
                // on it here does not introduce anything new alongside Arrow.
                BlockedCellCount = 5,
                BlockedCellsInteriorOnly = true,
                WallCount = combineOthers ? 2 : 0,
                ArrowCount = 1,
                MinPathCells = 5,
                TargetAvgPathMin = 6.5f,
                TargetAvgPathMax = 9.5f,
                RequireEveryPairingCoversBoard = false,
                RequireMechanicsNecessary = true,
                // Kept, but it is the binding constraint here and it fights the coverage rule:
                // that rule already forces a unique pairing, and necessity asks that REMOVING the
                // Arrow create a second solution -- which a board with only one pairing rarely
                // allows. Measured, the two together admit roughly 1 candidate in 2000, which is
                // why levels 21-22 failed at 12000 attempts while 23-25 happened to succeed. The
                // budget below buys the margin rather than dropping either rule.
                MaxAttempts = 20000
            };
        }

        /// <summary>
        /// Levels 26-30: Forbidden Cell introduced -- a cell that refuses one named colour while
        /// letting every other through. Levels 29-30 combine it with Wall and Blocked, the same
        /// "introduce alone, then recombine with what was already taught" shape the earlier
        /// mechanic ranges use.
        ///
        /// Board density and attempt budget are inherited from the Arrow range rather than
        /// re-derived: both mechanics are pure restrictions gated by RequireMechanicsNecessary on
        /// top of the coverage rule, which is the combination measured at roughly 1 accepted
        /// candidate in 2000 (see SpecForLevel21To25). Starting lower would just rediscover that.
        /// </summary>
        private static GenerationSpec SpecForLevel26To30(int levelNumber, int gridSize)
        {
            bool combineOthers = levelNumber >= 29;

            float t = (levelNumber - 26) / 4f;
            float straightness = Mathf.Lerp(0.5f, 0.3f, t);

            return new GenerationSpec
            {
                GridSize = gridSize,
                // Six on a 7x7, not four. Measured: 4 colours cost 127ms per candidate and
                // produced zero unique solutions in 8 tries -- long paths on a big open board
                // make proving uniqueness both rare and slow, which is what left level 21
                // grinding for over five minutes. 6 colours costs 9ms and actually passes
                // (~4% of attempts clear uniqueness AND mechanic necessity). Blocked rises to
                // 5 for the same reason: these ranges introduce their mechanic alone, so
                // walls are unavailable to constrain routes and Blocked has to do that work.
                MinColorCount = 6,
                MaxColorCount = 6,
                StraightnessBias = straightness,
                TargetScoreMin = 0f,
                TargetScoreMax = 100f,
                Uniqueness = UniquenessPolicy.Require,
                BlockedCellCount = 5,
                BlockedCellsInteriorOnly = true,
                WallCount = combineOthers ? 2 : 0,
                ForbiddenCount = 1,
                MinPathCells = 5,
                TargetAvgPathMin = 6.5f,
                TargetAvgPathMax = 9.5f,
                RequireEveryPairingCoversBoard = false,
                RequireMechanicsNecessary = true,
                // 40000: levels 29 and 30 share a spec and 30 generated while 29 did not, so the
                // spec is satisfiable and the search simply ran out of room. The rng is seeded
                // deterministically, so a plain re-run reproduces the same miss -- the budget has
                // to change for the outcome to.
                MaxAttempts = 40000
            };
        }

        /// <summary>
        /// Levels 41-45: Checkpoint introduced -- a cell one named colour is REQUIRED to pass
        /// through. Levels 44-45 recombine it with Wall and Blocked.
        ///
        /// Checkpoint is the first rule that constrains a colour's route without touching any
        /// cell's admission rules, so it is the one mechanic a player can violate without ever
        /// making an illegal move: the board simply refuses to complete. That makes it worth
        /// introducing on its own, and worth being strict about necessity -- a checkpoint on a cell
        /// its colour would have crossed anyway is invisible.
        ///
        /// Same 7x7 / 6 colours / 5 blocked shape as the Forbidden and Permitted ranges (Blocked
        /// returns to 5; only Bridge needed 4, for its four-neighbour requirement). MinWrongRoutes
        /// carries over from the Bridge range -- see §6.20; it is cheap and there is no reason a
        /// later range should be allowed to ship a board with nothing to search.
        /// </summary>
        private static GenerationSpec SpecForLevel41To45(int levelNumber, int gridSize)
        {
            bool combineOthers = levelNumber >= 44;

            float t = (levelNumber - 41) / 4f;
            float straightness = Mathf.Lerp(0.5f, 0.3f, t);

            return new GenerationSpec
            {
                GridSize = gridSize,
                MinColorCount = 6,
                MaxColorCount = 6,
                StraightnessBias = straightness,
                TargetScoreMin = 0f,
                TargetScoreMax = 100f,
                Uniqueness = UniquenessPolicy.Require,
                BlockedCellCount = 5,
                BlockedCellsInteriorOnly = true,
                WallCount = combineOthers ? 2 : 0,
                CheckpointCount = 1,
                MinWrongRoutes = 3,
                MinPathCells = 5,
                TargetAvgPathMin = 6.5f,
                TargetAvgPathMax = 9.5f,
                RequireEveryPairingCoversBoard = false,
                RequireMechanicsNecessary = true,
                MaxAttempts = 40000
            };
        }

        /// <summary>
        /// Levels 36-40: Bridge introduced -- one cell carrying two colours crossing at right
        /// angles, each running straight through. Levels 39-40 recombine it with Wall and Blocked.
        ///
        /// Same 7x7 / 6 colours / 5 blocked shape as the two ranges before it, for the reason given
        /// on SpecForLevel31To35. What differs is that BridgeCount is not a decoration count like
        /// the others: it feeds the partition builder rather than a placement pass, so a board that
        /// cannot seat a crossing fails during construction instead of being filtered afterwards
        /// (see TryGeneratePathPartition).
        ///
        /// Blocked drops to 4 because a bridge needs all four of its neighbours usable, and five
        /// blocked cells on a 7x7 leave few interior cells that qualify -- construction failures
        /// were cheap but frequent enough to be worth one fewer hole.
        /// </summary>
        private static GenerationSpec SpecForLevel36To40(int levelNumber, int gridSize)
        {
            bool combineOthers = levelNumber >= 39;

            float t = (levelNumber - 36) / 4f;
            float straightness = Mathf.Lerp(0.5f, 0.3f, t);

            return new GenerationSpec
            {
                GridSize = gridSize,
                MinColorCount = 6,
                MaxColorCount = 6,
                StraightnessBias = straightness,
                TargetScoreMin = 0f,
                TargetScoreMax = 100f,
                Uniqueness = UniquenessPolicy.Require,
                BlockedCellCount = 4,
                BlockedCellsInteriorOnly = true,
                WallCount = combineOthers ? 2 : 0,
                BridgeCount = 1,
                MinWrongRoutes = 3,
                MinPathCells = 5,
                TargetAvgPathMin = 6.5f,
                TargetAvgPathMax = 9.5f,
                RequireEveryPairingCoversBoard = false,
                RequireMechanicsNecessary = true,
                MaxAttempts = 40000
            };
        }

        /// <summary>
        /// Levels 31-35: Permitted Cell introduced -- a cell only one named colour may enter, the
        /// mirror of Forbidden. Levels 34-35 combine it with Wall and Blocked, the same
        /// "introduce alone, then recombine with what was already taught" shape as every earlier
        /// mechanic range.
        ///
        /// Shape copied from the Forbidden range (7x7, 6 colours, 5 blocked) because the two are
        /// the same kind of rule -- a per-cell colour permission with one instance per level -- and
        /// that configuration is the one measured to work at 7x7. Six colours rather than four is
        /// load-bearing, not cosmetic: four on a 7x7 costs ~127ms per candidate and produced zero
        /// unique solutions, six costs ~9ms (see SpecForLevel21To25).
        /// </summary>
        private static GenerationSpec SpecForLevel31To35(int levelNumber, int gridSize)
        {
            bool combineOthers = levelNumber >= 34;

            float t = (levelNumber - 31) / 4f;
            float straightness = Mathf.Lerp(0.5f, 0.3f, t);

            return new GenerationSpec
            {
                GridSize = gridSize,
                MinColorCount = 6,
                MaxColorCount = 6,
                StraightnessBias = straightness,
                TargetScoreMin = 0f,
                TargetScoreMax = 100f,
                Uniqueness = UniquenessPolicy.Require,
                BlockedCellCount = 5,
                BlockedCellsInteriorOnly = true,
                WallCount = combineOthers ? 2 : 0,
                PermittedCount = 1,
                MinPathCells = 5,
                TargetAvgPathMin = 6.5f,
                TargetAvgPathMax = 9.5f,
                RequireEveryPairingCoversBoard = false,
                RequireMechanicsNecessary = true,
                MaxAttempts = 40000
            };
        }

        // ---------------------------------------------------------------------------------------
        // Candidate construction: grow every colour's path at once -> per-colour segments -> LevelData.
        // ---------------------------------------------------------------------------------------

        private static bool TryBuildCandidate(GenerationSpec spec, int colorCount, System.Random rng,
            out LevelData data)
        {
            data = default;
            int size = spec.GridSize;

            bool[,] usable = PlaceBlockedCells(size, spec.BlockedCellCount, spec.BlockedCellsInteriorOnly, rng);
            int usableCount = (size * size) - spec.BlockedCellCount;

            // Bridges are chosen BEFORE the partition, unlike every other mechanic: a crossing has
            // to be built into the solution, not read off a finished one. See ChooseBridgeCells.
            HashSet<(int Row, int Col)> bridges = ChooseBridgeCells(size, usable, spec.BridgeCount, rng);
            if (bridges.Count < spec.BridgeCount) { return false; } // board can't seat them -- retry

            // Grows every colour's path at once rather than cutting one Hamiltonian path. Every
            // consumer below takes these per-path lists, which is what keeps the directional
            // mechanics honest -- see TryGeneratePathPartition.
            List<List<(int Row, int Col)>> segments =
                TryGeneratePathPartition(size, usable, usableCount, colorCount, bridges, rng);
            if (segments == null) { return false; }

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

            List<(int Row, int Col, Direction Dir)> walls = PlaceWalls(usable, size, segments, spec.WallCount, rng);
            if (walls.Count < spec.WallCount) { return false; } // not enough non-path edges -- retry

            // Endpoints are excluded by InteriorPathCells itself now, so bridges are the only thing
            // left to pass: a bridge cell IS interior to both its paths and would otherwise be a
            // legal pick, but BlockType is one per cell, so a One-Way here would overwrite the
            // crossing. (This is also what keeps reversedByCell honest -- a bridge cell appears in
            // two segments and gets two conflicting entries above, and this is why nobody reads them.)
            HashSet<(int Row, int Col)> oneWayExcluded = new HashSet<(int, int)>(bridges);

            List<(int Row, int Col, Direction EntryDir)> oneWays =
                PlaceOneWayCells(segments, oneWayExcluded, reversedByCell, spec.OneWayCount, rng);
            if (oneWays.Count < spec.OneWayCount) { return false; } // not enough interior cells -- retry

            // Arrow must never land on a cell One-Way already claimed -- the two are mutually
            // exclusive BlockTypes, so a cell can't carry both. (Dots are already excluded.)
            HashSet<(int Row, int Col)> arrowExcluded = new HashSet<(int, int)>(oneWayExcluded);
            for (int o = 0; o < oneWays.Count; o++) { arrowExcluded.Add((oneWays[o].Row, oneWays[o].Col)); }

            List<(int Row, int Col, Direction ExitDir)> arrows =
                PlaceArrowCells(segments, arrowExcluded, reversedByCell, spec.ArrowCount, rng);
            if (arrows.Count < spec.ArrowCount) { return false; } // not enough interior cells -- retry

            List<PairColorType> palette = PickDistinctColors(colorCount, rng);

            // Placed after the palette because a Forbidden cell names a COLOUR, which does not
            // exist until the palette is drawn -- unlike the mechanics above, which only need the
            // geometry. Excludes cells One-Way or Arrow already claimed: all three are BlockTypes
            // and a cell can carry only one.
            HashSet<(int Row, int Col)> forbiddenExcluded = new HashSet<(int, int)>(arrowExcluded);
            for (int a = 0; a < arrows.Count; a++) { forbiddenExcluded.Add((arrows[a].Row, arrows[a].Col)); }

            List<(int Row, int Col, int ForbiddenPairId)> forbidden =
                PlaceForbiddenCells(segments, forbiddenExcluded, palette, spec.ForbiddenCount, rng);
            if (forbidden.Count < spec.ForbiddenCount) { return false; } // not enough interior cells -- retry

            HashSet<(int Row, int Col)> permittedExcluded = new HashSet<(int, int)>(forbiddenExcluded);
            for (int f = 0; f < forbidden.Count; f++) { permittedExcluded.Add((forbidden[f].Row, forbidden[f].Col)); }

            List<(int Row, int Col, int AllowedPairId)> permitted =
                PlacePermittedCells(segments, permittedExcluded, palette, spec.PermittedCount, rng);
            if (permitted.Count < spec.PermittedCount) { return false; } // not enough interior cells -- retry

            HashSet<(int Row, int Col)> checkpointExcluded = new HashSet<(int, int)>(permittedExcluded);
            for (int p = 0; p < permitted.Count; p++) { checkpointExcluded.Add((permitted[p].Row, permitted[p].Col)); }

            List<(int Row, int Col, int CheckpointPairId)> checkpoints =
                PlaceCheckpointCells(segments, checkpointExcluded, palette, spec.CheckpointCount, rng);
            if (checkpoints.Count < spec.CheckpointCount) { return false; } // not enough interior cells -- retry

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
            foreach ((int Row, int Col) bridge in bridges)
            {
                typeGrid[bridge.Row, bridge.Col] = BlockType.Bridge;
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

            // A Forbidden cell stores the colour it refuses in its own pairId column. That column
            // otherwise identifies which pair a DOT belongs to, and these cells are never dots
            // (InteriorPathCells excludes endpoints), so the two uses cannot collide -- see
            // Block.SecondIdNamesAPair, which documents the same dual meaning.
            int[,] pairIdGrid = new int[size, size];
            for (int f = 0; f < forbidden.Count; f++)
            {
                typeGrid[forbidden[f].Row, forbidden[f].Col] = BlockType.ForbiddenForPair;
                pairIdGrid[forbidden[f].Row, forbidden[f].Col] = forbidden[f].ForbiddenPairId;
            }
            for (int a = 0; a < permitted.Count; a++)
            {
                typeGrid[permitted[a].Row, permitted[a].Col] = BlockType.AllowedForPairs;
                pairIdGrid[permitted[a].Row, permitted[a].Col] = permitted[a].AllowedPairId;
            }
            for (int k = 0; k < checkpoints.Count; k++)
            {
                typeGrid[checkpoints[k].Row, checkpoints[k].Col] = BlockType.Checkpoint;
                pairIdGrid[checkpoints[k].Row, checkpoints[k].Col] = checkpoints[k].CheckpointPairId;
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
                int[] pairIdRow = new int[size];
                for (int c = 0; c < size; c++)
                {
                    colorRow[c] = colorGrid[r, c];
                    typeRow[c] = typeGrid[r, c];
                    wallRow[c] = wallMaskGrid[r, c];
                    entryRow[c] = requiredEntryGrid[r, c];
                    exitRow[c] = forcedExitGrid[r, c];
                    pairIdRow[c] = pairIdGrid[r, c];
                }
                data.gridRows[r] = new GridRow
                {
                    coloum = colorRow,
                    pairId = pairIdRow,
                    blockType = typeRow,
                    wallMask = wallRow,
                    requiredEntryDirection = entryRow,
                    forcedExitDirection = exitRow
                };
            }

            return true;
        }

        /// <summary>
        /// Every cell that is strictly interior to some colour's path -- identified as
        /// (path index, cell index) so the caller can still reach the neighbours on either side.
        ///
        /// Interior means "not an endpoint", and endpoints are exactly the pair dots, so this
        /// excludes dot cells by construction rather than by checking a separate set. That matters
        /// now that the solution is a list of independent paths instead of one contiguous cell
        /// list: a cell's neighbours along its own path can only be found by knowing which path it
        /// belongs to, and reading index-1/index+1 across a path boundary would silently produce a
        /// direction between two cells that are not connected at all.
        /// </summary>
        private static List<(int Path, int Index)> InteriorPathCells(
            List<List<(int Row, int Col)>> paths, HashSet<(int Row, int Col)> excludedCells, System.Random rng)
        {
            List<(int Path, int Index)> candidates = new List<(int, int)>();

            for (int p = 0; p < paths.Count; p++)
            {
                for (int i = 1; i < paths[p].Count - 1; i++)
                {
                    if (excludedCells != null && excludedCells.Contains(paths[p][i])) { continue; }
                    candidates.Add((p, i));
                }
            }

            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            return candidates;
        }

        /// <summary>
        /// Chooses OneWayCount interior (non-dot) path cells and locks each to the direction the
        /// solver will ACTUALLY be moving in when it enters that cell along the intended solution
        /// -- the only direction it will ever be asked to admit, so the solution trivially
        /// satisfies its own constraint. Endpoints are never chosen: a One-Way constraint on a dot
        /// isn't meaningful, since a pair's path touches its own dot exactly once.
        ///
        /// "Actually be moving in" is not always the path's own array order -- see
        /// <paramref name="reversedByCell"/> and TryBuildCandidate's own doc comment on it.
        /// </summary>
        private static List<(int Row, int Col, Direction EntryDir)> PlaceOneWayCells(
            List<List<(int Row, int Col)>> paths, HashSet<(int Row, int Col)> excludedCells,
            Dictionary<(int Row, int Col), bool> reversedByCell, int count, System.Random rng)
        {
            List<(int Row, int Col, Direction EntryDir)> result = new List<(int, int, Direction)>();
            if (count <= 0) { return result; }

            List<(int Path, int Index)> candidates = InteriorPathCells(paths, excludedCells, rng);

            int take = Math.Min(count, candidates.Count);
            for (int k = 0; k < take; k++)
            {
                List<(int Row, int Col)> path = paths[candidates[k].Path];
                int idx = candidates[k].Index;

                // All three exist: idx is strictly interior to this path.
                (int Row, int Col) cell = path[idx];
                (int Row, int Col) prev = path[idx - 1];
                (int Row, int Col) next = path[idx + 1];

                Direction actualEntry = reversedByCell[cell]
                    ? BoardTopology.Opposite(DirectionOfTravel(cell, next))
                    : DirectionOfTravel(prev, cell);
                result.Add((cell.Row, cell.Col, actualEntry));
            }
            return result;
        }

        /// <summary>
        /// Chooses ForbiddenCount interior path cells and names, on each, a colour that is barred
        /// from entering it.
        ///
        /// The colour named is always one OTHER than the colour whose path actually runs through
        /// that cell. Forbidding the cell's own colour would forbid the intended solution itself --
        /// the candidate would simply become unsolvable and be thrown away, so the generator would
        /// spin forever producing nothing. This is the same "derive the mechanic from the solution"
        /// rule the directional mechanics follow, just inverted: One-Way and Arrow record what the
        /// solution DOES do, Forbidden records something it does not.
        ///
        /// Which other colour is picked at random. Whether that choice is load-bearing (the barred
        /// colour could plausibly have wanted this cell) or decorative is not decided here -- the
        /// RequireMechanicsNecessary gate answers that by re-solving with the rule stripped, the
        /// same as for every other mechanic.
        ///
        /// Returns (row, col, pairId-to-bar). The pair id is the one BuildBlockGrid/BoardGenerator
        /// store in the cell's own pairId column, which Block.NamesPair reads -- a Forbidden cell
        /// is not a dot, so that column is free to mean "the colour this cell refuses".
        /// </summary>
        private static List<(int Row, int Col, int ForbiddenPairId)> PlaceForbiddenCells(
            List<List<(int Row, int Col)>> paths, HashSet<(int Row, int Col)> excludedCells,
            List<PairColorType> palette, int count, System.Random rng)
        {
            List<(int Row, int Col, int ForbiddenPairId)> result = new List<(int, int, int)>();
            if (count <= 0 || paths.Count < 2) { return result; }

            List<(int Path, int Index)> candidates = InteriorPathCells(paths, excludedCells, rng);

            int take = Math.Min(count, candidates.Count);
            for (int k = 0; k < take; k++)
            {
                int ownerPath = candidates[k].Path;
                (int Row, int Col) cell = paths[ownerPath][candidates[k].Index];

                // Any colour but the one that owns this cell; each cell belongs to exactly one path.
                int offset = 1 + rng.Next(paths.Count - 1);
                int barredPath = (ownerPath + offset) % paths.Count;

                result.Add((cell.Row, cell.Col, (int)palette[barredPath]));
            }
            return result;
        }

        /// <summary>
        /// Chooses PermittedCount interior path cells and names, on each, the one colour allowed
        /// through it.
        ///
        /// The exact inverse of <see cref="PlaceForbiddenCells"/>, and the inversion is the whole
        /// point: a Forbidden cell refuses the colour it NAMES, a Permitted cell refuses every
        /// colour it does NOT. So Forbidden must name a colour that stays away from the cell, while
        /// Permitted must name the colour whose path actually runs through it -- naming anything
        /// else would bar the intended solution from a cell it needs, and every candidate would be
        /// thrown out as unsolvable.
        ///
        /// Names exactly one colour, the owner, leaving `secondPairId` unset. Block supports a
        /// second named colour, but naming two here only weakens the rule (two colours admitted
        /// instead of one) and makes it less likely to be load-bearing. Naming NO colour would be
        /// worse still: Block's own doc notes that a permit cell naming nobody is
        /// <see cref="BlockType.Blocked"/> under a different name, which LevelValidator rejects.
        /// </summary>
        private static List<(int Row, int Col, int AllowedPairId)> PlacePermittedCells(
            List<List<(int Row, int Col)>> paths, HashSet<(int Row, int Col)> excludedCells,
            List<PairColorType> palette, int count, System.Random rng)
        {
            return PlaceOwnerNamedCells(paths, excludedCells, palette, count, rng);
        }

        /// <summary>
        /// Chooses CheckpointCount interior path cells and names, on each, the colour whose path
        /// runs through it -- the pair now REQUIRED to pass through this cell.
        ///
        /// Shares its body with <see cref="PlacePermittedCells"/> because the placement question is
        /// the same one ("which colour owns this cell"), even though the two rules do opposite
        /// things with the answer: a Permitted cell turns other colours away, while a Checkpoint
        /// lets anyone through and is instead checked at completion time -- Block.CanEnter does not
        /// mention Checkpoint at all, PuzzleSolver.CheckpointsSatisfied does.
        ///
        /// Naming the owner is not a preference here, it is the only option that can ever be
        /// satisfied: the cell belongs to exactly one path in the intended solution, so requiring
        /// any other colour to pass through it demands a second visit that full coverage forbids.
        /// </summary>
        private static List<(int Row, int Col, int CheckpointPairId)> PlaceCheckpointCells(
            List<List<(int Row, int Col)>> paths, HashSet<(int Row, int Col)> excludedCells,
            List<PairColorType> palette, int count, System.Random rng)
        {
            return PlaceOwnerNamedCells(paths, excludedCells, palette, count, rng);
        }

        /// <summary>
        /// Picks <paramref name="count"/> interior (non-dot) path cells and pairs each with the
        /// colour whose path runs through it. Shared by Permitted and Checkpoint, which differ in
        /// what they do with that colour, not in how it is chosen -- see each caller's doc.
        /// </summary>
        private static List<(int Row, int Col, int PairId)> PlaceOwnerNamedCells(
            List<List<(int Row, int Col)>> paths, HashSet<(int Row, int Col)> excludedCells,
            List<PairColorType> palette, int count, System.Random rng)
        {
            List<(int Row, int Col, int PairId)> result = new List<(int, int, int)>();
            if (count <= 0 || paths.Count < 2) { return result; }

            List<(int Path, int Index)> candidates = InteriorPathCells(paths, excludedCells, rng);

            int take = Math.Min(count, candidates.Count);
            for (int k = 0; k < take; k++)
            {
                int ownerPath = candidates[k].Path;
                (int Row, int Col) cell = paths[ownerPath][candidates[k].Index];

                result.Add((cell.Row, cell.Col, (int)palette[ownerPath]));
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
        /// not reversed (see <paramref name="reversedByCell"/>).
        /// </summary>
        private static List<(int Row, int Col, Direction ExitDir)> PlaceArrowCells(
            List<List<(int Row, int Col)>> paths, HashSet<(int Row, int Col)> excludedCells,
            Dictionary<(int Row, int Col), bool> reversedByCell, int count, System.Random rng)
        {
            List<(int Row, int Col, Direction ExitDir)> result = new List<(int, int, Direction)>();
            if (count <= 0) { return result; }

            List<(int Path, int Index)> candidates = InteriorPathCells(paths, excludedCells, rng);

            int take = Math.Min(count, candidates.Count);
            for (int k = 0; k < take; k++)
            {
                List<(int Row, int Col)> path = paths[candidates[k].Path];
                int idx = candidates[k].Index;

                (int Row, int Col) cell = path[idx];
                (int Row, int Col) prev = path[idx - 1];
                (int Row, int Col) next = path[idx + 1];

                Direction actualExit = reversedByCell[cell]
                    ? BoardTopology.Opposite(DirectionOfTravel(prev, cell))
                    : DirectionOfTravel(cell, next);
                result.Add((cell.Row, cell.Col, actualExit));
            }
            return result;
        }

        /// <summary>
        /// Chooses WallCount edges to wall off, restricted to edges the intended solution never
        /// crosses -- placing a wall on an edge the solution itself needs would break the very
        /// thing generation just built (spec: mechanics must be constructed onto the solution,
        /// never bolted on afterward). Each undirected edge is considered exactly once (via
        /// Right/Down only) so the same wall can't be picked twice from either side. Returns fewer
        /// than requested if the board doesn't have enough non-path edges to offer -- the caller
        /// treats that as a failed attempt to retry, not an error.
        ///
        /// Takes the colour paths separately rather than one concatenated list, so the edges it
        /// protects are exactly the ones the solution uses. Concatenating would invent an edge
        /// between the end of one path and the start of the next -- harmless here (it only
        /// over-protects) but wrong, and the same concatenation is genuinely unsafe for the
        /// directional mechanics, so all three now agree on the same representation.
        /// </summary>
        private static List<(int Row, int Col, Direction Dir)> PlaceWalls(bool[,] usable, int size,
            List<List<(int Row, int Col)>> paths, int wallCount, System.Random rng)
        {
            List<(int Row, int Col, Direction Dir)> result = new List<(int, int, Direction)>();
            if (wallCount <= 0) { return result; }

            HashSet<(int, int, int, int)> pathEdges = new HashSet<(int, int, int, int)>();
            for (int p = 0; p < paths.Count; p++)
            {
                List<(int Row, int Col)> path = paths[p];
                for (int i = 0; i < path.Count - 1; i++)
                {
                    pathEdges.Add(NormalizedEdge(path[i], path[i + 1]));
                }
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

            // Grow a CONNECTED barrier rather than scattering independent edges. Picking each wall
            // at random gives one-cell-long stubs spread across the board: each blocks a single
            // step, which the player routes around without ever really seeing it. Walls that meet
            // at a corner read as one obstacle -- an L, or a longer run -- and force a detour around
            // the whole thing, which is what makes them worth having.
            //
            // Two walls join when they share a lattice corner (see WallCorners). After the first
            // pick, each subsequent one prefers a candidate touching a corner already used, falling
            // back to the next random candidate when the barrier cannot be extended -- so a board
            // with few legal edges still gets its walls rather than failing outright.
            HashSet<(int, int)> usedCorners = new HashSet<(int, int)>();
            bool[] taken = new bool[candidates.Count];
            int count = Math.Min(wallCount, candidates.Count);

            for (int placed = 0; placed < count; placed++)
            {
                int chosen = -1;

                if (usedCorners.Count > 0)
                {
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        if (taken[i]) { continue; }

                        ((int, int) a, (int, int) b) = WallCorners(candidates[i]);
                        if (usedCorners.Contains(a) || usedCorners.Contains(b)) { chosen = i; break; }
                    }
                }

                if (chosen < 0)
                {
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        if (!taken[i]) { chosen = i; break; }
                    }
                }

                if (chosen < 0) { break; }

                taken[chosen] = true;
                result.Add(candidates[chosen]);

                ((int, int) c1, (int, int) c2) = WallCorners(candidates[chosen]);
                usedCorners.Add(c1);
                usedCorners.Add(c2);
            }

            return result;
        }

        /// <summary>
        /// The two lattice corners a walled edge runs between. Cell (r,c) spans lattice points
        /// (r,c)..(r+1,c+1), so its Right edge is the vertical segment (r,c+1)-(r+1,c+1) and its
        /// Down edge the horizontal segment (r+1,c)-(r+1,c+1). Two walls meet -- and so read as a
        /// single barrier -- exactly when they share one of these.
        ///
        /// Only Right and Down are produced by the candidate scan above (each undirected edge is
        /// considered once), so those are the only cases handled.
        /// </summary>
        private static ((int, int), (int, int)) WallCorners((int Row, int Col, Direction Dir) wall)
        {
            if (wall.Dir == Direction.Right)
            {
                return ((wall.Row, wall.Col + 1), (wall.Row + 1, wall.Col + 1));
            }
            return ((wall.Row + 1, wall.Col), (wall.Row + 1, wall.Col + 1));
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
        /// Partitions the usable board directly into <paramref name="pathCount"/> simple paths --
        /// one per colour, each path's two ends becoming that colour's dots.
        ///
        /// <b>Replaces the snake-then-cut construction, which could not scale.</b> That approach
        /// asked for a single Hamiltonian path covering the whole board and then cut it into
        /// segments. Finding a Hamiltonian path is exponential, and it showed: 5x5 and 6x6 were
        /// fine, 7x7 did not merely run slowly but hung the editor outright. Since board size is
        /// where this genre's difficulty actually lives (Flow Free ships 5x5-6x6 as tutorial packs
        /// and its real puzzles are 8x8 and up), that ceiling capped how hard any level could be,
        /// no matter how the other knobs were tuned.
        ///
        /// Asking for k paths instead of one is a strictly weaker requirement and needs no
        /// exponential search. Every path grows at once, one cell at a time, and the choice of
        /// which cell to take next is what makes this work:
        ///   - <b>Most-constrained-first (Warnsdorff's rule).</b> Always extend into the free cell
        ///     with the fewest free neighbours of its own. This is the same heuristic used for
        ///     knight's tours, and it exists to avoid the one failure mode that matters here --
        ///     walking past a cell and stranding it with no path able to reach it later. Taking the
        ///     most enclosed cell first means cells never get left behind.
        ///   - <b>Shortest path first, as a tie-break.</b> Keeps path lengths even, which is the
        ///     same thing MinPathCells guards: one colour eating half the board while another gets
        ///     a 2-cell stub is exactly the level that plays as trivial.
        /// Failure is cheap and expected -- a run that strands a cell simply returns null and the
        /// caller retries with fresh seeds, rather than backtracking.
        ///
        /// Returns null if any cell was stranded, or if any path came out shorter than 2 cells (a
        /// colour needs two distinct cells to have two distinct dots).
        /// </summary>
        private static List<List<(int Row, int Col)>> TryGeneratePathPartition(int size, bool[,] usable,
            int usableCount, int pathCount, System.Random rng)
        {
            return TryGeneratePathPartition(size, usable, usableCount, pathCount, null, rng);
        }

        /// <summary>
        /// As above, but with <paramref name="bridges"/> carrying two paths each.
        ///
        /// <b>Why this cannot be a placement pass like every other mechanic.</b> One-Way, Arrow,
        /// Forbidden and Permitted are all decorations applied to a finished partition: the
        /// solution exists first, and the rule is read off it. A Bridge is not a restriction on a
        /// cell -- it is extra capacity, two paths crossing at right angles. A partition gives
        /// every cell to exactly one path, so there is no finished partition to read a crossing
        /// off; the second path has to be there from the start.
        ///
        /// <b>Node splitting.</b> So a bridge cell enters the search as TWO independent nodes: a
        /// horizontal lane adjacent only to the cells left and right of it, and a vertical lane
        /// adjacent only to those above and below. Everything else is unchanged -- the same
        /// Warnsdorff growth covers every node exactly once, and because the two lanes are separate
        /// nodes, both get covered. Two consequences fall out for free rather than needing to be
        /// enforced: a lane has exactly two neighbours, so a path through it is straight (which is
        /// what <see cref="Block.CanExitFrom"/> demands of a bridge), and it can never wander onto
        /// the other axis mid-cell.
        ///
        /// Two things the graph cannot express are checked afterwards instead:
        ///   - <b>A lane must be interior to its path.</b> A path ENDING on a bridge would make
        ///     that cell a pair dot, which LevelValidator rejects outright -- a dot is where a path
        ///     starts, not a crossing.
        ///   - <b>The two lanes must belong to DIFFERENT paths.</b> Nothing stops one colour taking
        ///     both, but that is a path crossing itself, not two colours crossing, and the bridge
        ///     would not be load-bearing.
        /// </summary>
        private static List<List<(int Row, int Col)>> TryGeneratePathPartition(int size, bool[,] usable,
            int usableCount, int pathCount, HashSet<(int Row, int Col)> bridges, System.Random rng)
        {
            if (pathCount < 1) { return null; }

            int bridgeCount = bridges == null ? 0 : bridges.Count;

            // Node ids: every usable cell gets one, and every bridge cell gets a SECOND for its
            // other lane. nodeOfCell holds the horizontal lane for a bridge, verticalNodeOfCell
            // the other; for a normal cell the former is simply the cell and the latter is -1.
            int[,] nodeOfCell = new int[size, size];
            int[,] verticalNodeOfCell = new int[size, size];
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++) { nodeOfCell[r, c] = -1; verticalNodeOfCell[r, c] = -1; }
            }

            List<(int Row, int Col)> cellOfNode = new List<(int, int)>();
            List<bool> isLane = new List<bool>();
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++)
                {
                    if (!usable[r, c]) { continue; }
                    bool bridge = bridgeCount > 0 && bridges.Contains((r, c));

                    nodeOfCell[r, c] = cellOfNode.Count;
                    cellOfNode.Add((r, c));
                    isLane.Add(bridge);

                    if (!bridge) { continue; }
                    verticalNodeOfCell[r, c] = cellOfNode.Count;
                    cellOfNode.Add((r, c));
                    isLane.Add(true);
                }
            }

            int nodeCount = cellOfNode.Count;
            if (nodeCount != usableCount + bridgeCount) { return null; }
            if (nodeCount < pathCount * 2) { return null; }

            int[][] neighbours = BuildPartitionAdjacency(size, usable, bridges, nodeOfCell, verticalNodeOfCell,
                cellOfNode, bridgeCount);

            // taken[node]: -1 free, otherwise the index of the path occupying it.
            int[] taken = new int[nodeCount];
            for (int i = 0; i < nodeCount; i++) { taken[i] = -1; }

            // Seed one node per path, all distinct.
            List<int> freeNodes = new List<int>();
            for (int i = 0; i < nodeCount; i++) { freeNodes.Add(i); }
            for (int i = freeNodes.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (freeNodes[i], freeNodes[j]) = (freeNodes[j], freeNodes[i]);
            }

            List<List<int>> nodePaths = new List<List<int>>();
            for (int i = 0; i < pathCount; i++)
            {
                int seed = freeNodes[i];
                taken[seed] = i;
                nodePaths.Add(new List<int> { seed });
            }

            int placed = pathCount;
            while (placed < nodeCount)
            {
                int bestPath = -1;
                bool bestAtFront = false;
                int bestNode = -1;
                int bestFreeNeighbours = int.MaxValue;
                int bestPathLength = int.MaxValue;
                int tieCount = 0;

                for (int p = 0; p < nodePaths.Count; p++)
                {
                    List<int> path = nodePaths[p];

                    // Both ends of a path can grow. A 1-node path has the same node for both;
                    // considering it twice is harmless, just redundant.
                    for (int side = 0; side < 2; side++)
                    {
                        int end = side == 0 ? path[0] : path[path.Count - 1];
                        int[] adjacency = neighbours[end];

                        for (int d = 0; d < adjacency.Length; d++)
                        {
                            int candidate = adjacency[d];
                            if (taken[candidate] != -1) { continue; }

                            int freeNeighbours = CountFreeNeighbours(neighbours, taken, candidate);

                            // Most-constrained node first; shortest path breaks ties; a random
                            // pick breaks the rest, so repeated runs explore different boards.
                            bool better = freeNeighbours < bestFreeNeighbours
                                || (freeNeighbours == bestFreeNeighbours && path.Count < bestPathLength);
                            bool equal = freeNeighbours == bestFreeNeighbours && path.Count == bestPathLength;

                            if (equal)
                            {
                                tieCount++;
                                if (rng.Next(tieCount) != 0) { continue; }
                            }
                            else if (better) { tieCount = 1; }
                            else { continue; }

                            bestPath = p;
                            bestAtFront = side == 0;
                            bestNode = candidate;
                            bestFreeNeighbours = freeNeighbours;
                            bestPathLength = path.Count;
                        }
                    }
                }

                // Nothing can grow but nodes remain -- they are stranded. Cheap failure; the
                // caller retries with different seeds.
                if (bestPath < 0) { return null; }

                taken[bestNode] = bestPath;
                if (bestAtFront) { nodePaths[bestPath].Insert(0, bestNode); }
                else { nodePaths[bestPath].Add(bestNode); }
                placed++;
            }

            for (int i = 0; i < nodePaths.Count; i++)
            {
                if (nodePaths[i].Count < 2) { return null; }

                // A path ending on a bridge lane would make that cell a pair dot -- see the doc.
                if (isLane[nodePaths[i][0]] || isLane[nodePaths[i][nodePaths[i].Count - 1]]) { return null; }
            }

            if (bridgeCount > 0)
            {
                foreach ((int Row, int Col) bridge in bridges)
                {
                    if (taken[nodeOfCell[bridge.Row, bridge.Col]]
                        == taken[verticalNodeOfCell[bridge.Row, bridge.Col]])
                    {
                        return null; // one colour took both lanes -- not a crossing
                    }
                }
            }

            List<List<(int Row, int Col)>> paths = new List<List<(int, int)>>();
            for (int i = 0; i < nodePaths.Count; i++)
            {
                List<(int Row, int Col)> cells = new List<(int, int)>(nodePaths[i].Count);
                for (int j = 0; j < nodePaths[i].Count; j++) { cells.Add(cellOfNode[nodePaths[i][j]]); }
                paths.Add(cells);
            }

            return paths;
        }

        /// <summary>
        /// Adjacency for the partition graph. A step onto a bridge lands on whichever of its two
        /// lanes matches the direction of travel, which is what keeps the lanes independent and
        /// forces a crossing path to run straight through.
        /// </summary>
        private static int[][] BuildPartitionAdjacency(int size, bool[,] usable,
            HashSet<(int Row, int Col)> bridges, int[,] nodeOfCell, int[,] verticalNodeOfCell,
            List<(int Row, int Col)> cellOfNode, int bridgeCount)
        {
            int[][] neighbours = new int[cellOfNode.Count][];
            List<int> scratch = new List<int>(4);

            for (int node = 0; node < cellOfNode.Count; node++)
            {
                (int Row, int Col) cell = cellOfNode[node];
                bool onBridge = bridgeCount > 0 && bridges.Contains(cell);
                bool verticalLane = onBridge && verticalNodeOfCell[cell.Row, cell.Col] == node;

                scratch.Clear();
                for (int d = 0; d < Directions.Length; d++)
                {
                    int nr = cell.Row, nc = cell.Col;
                    bool horizontalStep = Directions[d] == Direction.Left || Directions[d] == Direction.Right;
                    switch (Directions[d])
                    {
                        case Direction.Left: nc--; break;
                        case Direction.Right: nc++; break;
                        case Direction.Up: nr--; break;
                        case Direction.Down: nr++; break;
                    }

                    // A lane only connects along its own axis; that is the whole point of it.
                    if (onBridge && horizontalStep == verticalLane) { continue; }
                    if (nr < 0 || nr >= size || nc < 0 || nc >= size) { continue; }
                    if (!usable[nr, nc]) { continue; }

                    bool neighbourIsBridge = bridgeCount > 0 && bridges.Contains((nr, nc));
                    scratch.Add(neighbourIsBridge && !horizontalStep
                        ? verticalNodeOfCell[nr, nc]
                        : nodeOfCell[nr, nc]);
                }

                neighbours[node] = scratch.ToArray();
            }

            return neighbours;
        }

        private static int CountFreeNeighbours(int[][] neighbours, int[] taken, int node)
        {
            int count = 0;
            int[] adjacency = neighbours[node];
            for (int i = 0; i < adjacency.Length; i++)
            {
                if (taken[adjacency[i]] == -1) { count++; }
            }
            return count;
        }

        /// <summary>
        /// Picks cells to carry a Bridge, before the partition runs -- see TryGeneratePathPartition
        /// for why this one cannot be chosen afterwards like every other mechanic.
        ///
        /// A cell only qualifies if both of its lanes can actually be crossed, which
        /// LevelValidator.ValidateBridgeCells enforces and would otherwise reject the finished
        /// level over: it must be off the outer ring (an edge cell is missing two of the four
        /// neighbours a crossing needs) and all four of those neighbours must be usable. Bridges
        /// are also kept off each other's neighbours -- adjacent crossings are legal but read as
        /// visual noise, and they make the two lanes of one bridge depend on the other's routing.
        ///
        /// Returns fewer than <paramref name="count"/> cells when the board cannot supply them;
        /// the caller retries.
        /// </summary>
        private static HashSet<(int Row, int Col)> ChooseBridgeCells(int size, bool[,] usable, int count,
            System.Random rng)
        {
            HashSet<(int Row, int Col)> chosen = new HashSet<(int, int)>();
            if (count <= 0) { return chosen; }

            List<(int Row, int Col)> candidates = new List<(int, int)>();
            for (int r = 1; r < size - 1; r++)
            {
                for (int c = 1; c < size - 1; c++)
                {
                    if (!usable[r, c]) { continue; }
                    if (!usable[r - 1, c] || !usable[r + 1, c] || !usable[r, c - 1] || !usable[r, c + 1]) { continue; }
                    candidates.Add((r, c));
                }
            }

            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            for (int i = 0; i < candidates.Count && chosen.Count < count; i++)
            {
                (int Row, int Col) cell = candidates[i];
                if (chosen.Contains((cell.Row - 1, cell.Col)) || chosen.Contains((cell.Row + 1, cell.Col))
                    || chosen.Contains((cell.Row, cell.Col - 1)) || chosen.Contains((cell.Row, cell.Col + 1)))
                {
                    continue;
                }
                chosen.Add(cell);
            }

            return chosen;
        }

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
                int[] pairIdRow = data.gridRows[r].pairId;
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
                    else
                    {
                        // A cell with no colour can still carry a pair id: a permission cell
                        // (ForbiddenForPair / AllowedForPairs) stores the colour it names there.
                        // Mirrors BoardGenerator's "explicit pairId wins" rule -- without this the
                        // offline validation would see the cell as an unconditional Normal and
                        // silently accept boards whose rule does nothing.
                        int explicitPairId = (pairIdRow != null && c < pairIdRow.Length) ? pairIdRow[c] : 0;
                        if (explicitPairId != 0) { SetField(block, "pairId", explicitPairId); }
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
