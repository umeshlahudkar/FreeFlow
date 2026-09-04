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
        /// <summary>
        /// Clears any progress bar left behind by a run that a domain reload killed mid-flight --
        /// which is how every long generation ends if a script is touched or the editor regains
        /// focus while one is going. Without this the bar can persist with nothing behind it.
        /// </summary>
        [UnityEditor.InitializeOnLoadMethod]
        private static void ClearStaleProgressBar()
        {
            EditorUtility.ClearProgressBar();
        }

        [MenuItem("FreeFlow/Level Generator/Generate Levels 1-10 (Basic Flow + Blocked Cell)")]
        public static void GenerateLevels1To10()
        {
            const string levelsFolder = "Assets/Resources/Levels/Classic";
            const int levelCount = 10;
            // Classic numbering already starts at 1, so nothing to subtract -- declared anyway so
            // every generate method reads the same way.
            const int outputOffset = 0;

            EnsureLevelFolder(levelsFolder);

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

                SaveLevelAsset(levelsFolder, levelNumber - outputOffset, generated.Data, generated.DifficultyScore);
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
            const string levelsFolder = "Assets/Resources/Levels/Advanced";
            const int startLevel = 11;
            // Campaign numbering stays as authored (the specs and the plan doc both use it);
            // the mode's own level files are numbered from 1, so subtract the offset on write.
            const int outputOffset = 10;
            const int endLevel = 15;
            const int gridSize = 6;

            System.Random rng = new System.Random(20260831); // a fresh seed for this level range
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1 - outputOffset, seenCanonicalKeys);

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

                SaveLevelAsset(levelsFolder, levelNumber - outputOffset, generated.Data, generated.DifficultyScore);
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
            const string levelsFolder = "Assets/Resources/Levels/Advanced";
            const int startLevel = 16;
            // Campaign numbering stays as authored (the specs and the plan doc both use it);
            // the mode's own level files are numbered from 1, so subtract the offset on write.
            const int outputOffset = 10;
            const int endLevel = 20;
            const int gridSize = 6;

            System.Random rng = new System.Random(20260901); // a fresh seed for this level range
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1 - outputOffset, seenCanonicalKeys);

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

                SaveLevelAsset(levelsFolder, levelNumber - outputOffset, generated.Data, generated.DifficultyScore);
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
            const string levelsFolder = "Assets/Resources/Levels/Advanced";
            const int startLevel = 21;
            // Campaign numbering stays as authored (the specs and the plan doc both use it);
            // the mode's own level files are numbered from 1, so subtract the offset on write.
            const int outputOffset = 10;
            const int endLevel = 25;
            const int gridSize = 7; // 7x7 became reachable once the strict coverage rule was dropped

            System.Random rng = new System.Random(20260902); // a fresh seed for this level range
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1 - outputOffset, seenCanonicalKeys);

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

                SaveLevelAsset(levelsFolder, levelNumber - outputOffset, generated.Data, generated.DifficultyScore);
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
            const string levelsFolder = "Assets/Resources/Levels/Advanced";
            const int startLevel = 31;
            // Campaign numbering stays as authored (the specs and the plan doc both use it);
            // the mode's own level files are numbered from 1, so subtract the offset on write.
            const int outputOffset = 10;
            const int endLevel = 33;

            System.Random rng = new System.Random(20260904);
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1 - outputOffset, seenCanonicalKeys);

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

                SaveLevelAsset(levelsFolder, levelNumber - outputOffset, generated.Data, generated.DifficultyScore);
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
            const string levelsFolder = "Assets/Resources/Levels/Advanced";
            const int startLevel = 26;
            // Campaign numbering stays as authored (the specs and the plan doc both use it);
            // the mode's own level files are numbered from 1, so subtract the offset on write.
            const int outputOffset = 10;
            const int endLevel = 30;
            const int gridSize = 7; // 7x7 became reachable once the strict coverage rule was dropped

            System.Random rng = new System.Random(20260903); // a fresh seed for this level range
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1 - outputOffset, seenCanonicalKeys);

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

                SaveLevelAsset(levelsFolder, levelNumber - outputOffset, generated.Data, generated.DifficultyScore);
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

        [MenuItem("FreeFlow/Level Generator/Generate Levels 51-55 (Mastery: 8x8)")]
        public static void GenerateLevels51To55()
        {
            const string levelsFolder = "Assets/Resources/Levels/Advanced";
            const int startLevel = 51;
            // Campaign numbering stays as authored (the specs and the plan doc both use it);
            // the mode's own level files are numbered from 1, so subtract the offset on write.
            const int outputOffset = 10;
            const int endLevel = 55;
            const int gridSize = 8; // first range past 7x7 -- see SpecForLevel51To55

            System.Random rng = new System.Random(20261003);
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1 - outputOffset, seenCanonicalKeys);

            StringBuilder report = new StringBuilder();
            int savedCount = 0;
            bool cancelled = false;

            EnsureLevelFolder(levelsFolder);
            EditorUtility.ClearProgressBar(); // sticky cancel flag -- see GenerateLevels31To35

            for (int levelNumber = startLevel; levelNumber <= endLevel; levelNumber++)
            {
                GenerationSpec spec = SpecForLevel51To55(levelNumber, gridSize);
                GeneratedLevel generated = TryGenerateLevel(spec, rng, seenCanonicalKeys,
                    attempt => { cancelled = cancelled || ReportGenerationProgress("Levels 51-55", levelNumber, attempt, spec.MaxAttempts); return cancelled; });
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

                SaveLevelAsset(levelsFolder, levelNumber - outputOffset, generated.Data, generated.DifficultyScore);
                savedCount++;
                report.Append("Level ").Append(levelNumber)
                    .Append(": ").Append(gridSize).Append("x").Append(gridSize)
                    .Append(" colors=").Append(generated.Data.pairCount)
                    .Append(" blocked=").Append(spec.BlockedCellCount)
                    .Append(" walls=").Append(spec.WallCount)
                    .Append(" mechanics=")
                    .Append(spec.CheckpointCount > 0 ? "Chk " : "")
                    .Append(spec.ForbiddenCount > 0 ? "Fbd " : "")
                    .Append(spec.ArrowCount > 0 ? "Arw " : "")
                    .Append(spec.PermittedCount > 0 ? "Prm " : "")
                    .Append(spec.OneWayCount > 0 ? "1Way " : "")
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

            Debug.Log("LevelGenerator: Levels 51-55 generation complete -- " + savedCount + "/" +
                (endLevel - startLevel + 1) + " levels saved.\n" + report);
        }

        [MenuItem("FreeFlow/Level Generator/Generate Levels 46-50 (Shared Destination)")]
        public static void GenerateLevels46To50()
        {
            const string levelsFolder = "Assets/Resources/Levels/Advanced";
            const int startLevel = 46;
            // Campaign numbering stays as authored (the specs and the plan doc both use it);
            // the mode's own level files are numbered from 1, so subtract the offset on write.
            const int outputOffset = 10;
            const int endLevel = 50;
            const int gridSize = 7;

            System.Random rng = new System.Random(20260926); // a fresh seed for this level range
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1 - outputOffset, seenCanonicalKeys);

            StringBuilder report = new StringBuilder();
            int savedCount = 0;
            bool cancelled = false;

            EnsureLevelFolder(levelsFolder);
            EditorUtility.ClearProgressBar(); // sticky cancel flag -- see GenerateLevels31To35

            for (int levelNumber = startLevel; levelNumber <= endLevel; levelNumber++)
            {
                GenerationSpec spec = SpecForLevel46To50(levelNumber, gridSize);
                GeneratedLevel generated = TryGenerateLevel(spec, rng, seenCanonicalKeys,
                    attempt => { cancelled = cancelled || ReportGenerationProgress("Levels 46-50", levelNumber, attempt, spec.MaxAttempts); return cancelled; });
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

                SaveLevelAsset(levelsFolder, levelNumber - outputOffset, generated.Data, generated.DifficultyScore);
                savedCount++;
                report.Append("Level ").Append(levelNumber)
                    .Append(": colors=").Append(generated.Data.pairCount)
                    .Append(" blocked=").Append(spec.BlockedCellCount)
                    .Append(" walls=").Append(spec.WallCount)
                    .Append(" sharedGoals=").Append(spec.SharedGoalCount)
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

            Debug.Log("LevelGenerator: Levels 46-50 generation complete -- " + savedCount + "/" +
                (endLevel - startLevel + 1) + " levels saved.\n" + report);
        }

        [MenuItem("FreeFlow/Level Generator/Generate Levels 41-45 (Checkpoint)")]
        public static void GenerateLevels41To45()
        {
            const string levelsFolder = "Assets/Resources/Levels/Advanced";
            const int startLevel = 41;
            // Campaign numbering stays as authored (the specs and the plan doc both use it);
            // the mode's own level files are numbered from 1, so subtract the offset on write.
            const int outputOffset = 10;
            const int endLevel = 45;
            const int gridSize = 7;

            System.Random rng = new System.Random(20260919); // a fresh seed for this level range
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1 - outputOffset, seenCanonicalKeys);

            StringBuilder report = new StringBuilder();
            int savedCount = 0;
            bool cancelled = false;

            EnsureLevelFolder(levelsFolder);
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

                SaveLevelAsset(levelsFolder, levelNumber - outputOffset, generated.Data, generated.DifficultyScore);
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
            const string levelsFolder = "Assets/Resources/Levels/Advanced";
            const int startLevel = 36;
            // Campaign numbering stays as authored (the specs and the plan doc both use it);
            // the mode's own level files are numbered from 1, so subtract the offset on write.
            const int outputOffset = 10;
            const int endLevel = 40;
            const int gridSize = 7;

            System.Random rng = new System.Random(20260912); // a fresh seed for this level range
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1 - outputOffset, seenCanonicalKeys);

            StringBuilder report = new StringBuilder();
            int savedCount = 0;
            bool cancelled = false;

            EnsureLevelFolder(levelsFolder);
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

                SaveLevelAsset(levelsFolder, levelNumber - outputOffset, generated.Data, generated.DifficultyScore);
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
            const string levelsFolder = "Assets/Resources/Levels/Advanced";
            const int startLevel = 31;
            // Campaign numbering stays as authored (the specs and the plan doc both use it);
            // the mode's own level files are numbered from 1, so subtract the offset on write.
            const int outputOffset = 10;
            const int endLevel = 35;
            const int gridSize = 7; // 7x7 became reachable once the strict coverage rule was dropped

            System.Random rng = new System.Random(20260905); // a fresh seed for this level range
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1 - outputOffset, seenCanonicalKeys);

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

                SaveLevelAsset(levelsFolder, levelNumber - outputOffset, generated.Data, generated.DifficultyScore);
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

            /// <summary>How many shared-destination cells to place: cells that are the second dot
            /// of TWO colours at once, so both paths end there. Like BridgeCount and unlike every
            /// placement count above, this feeds the partition builder -- a path's ends become its
            /// dots, so sharing one has to be built in. See ChooseSharedGoalCells.
            ///
            /// Note this mechanic has no BlockType and so no necessity check: it is dot identity,
            /// not a strippable rule, and RequiredMechanicValidator says as much. It cannot be
            /// decorative either -- both colours must reach the cell or the level is unsolved.</summary>
            public int SharedGoalCount;

            /// <summary>How many of the board's headline mechanics must be individually
            /// load-bearing. 0 keeps the strict rule -- every one of them, which is what levels
            /// 1-50 use.
            ///
            /// Above 0 this is the K of "K of M", and it exists because the strict rule cannot be
            /// satisfied once mechanics are stacked: they start ruling out the same wrong routes,
            /// so removing either alone changes nothing and both measure as unnecessary. Across 175
            /// uniquely solvable 3-mechanic boards, 0% had all three individually load-bearing and
            /// 7.4% had two. The mechanics that fail individually are still checked collectively --
            /// see MechanicNecessityHolds -- so this loosens "each must matter alone" without
            /// loosening "all of them must matter".
            ///
            /// Blocked and Wall are exempt and always judged individually: a decorative hole or
            /// wall is noise at any density.</summary>
            public int MinNecessaryMechanics;

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
                        // Blocked and Wall are judged individually no matter what, because they are
                        // not what MinNecessaryMechanics is about: a decorative hole or a wall that
                        // rules nothing out is pure noise at any density, and both are cheap to
                        // test. The headline BlockTypes go through the K-of-M pass below.
                        if (spec.WallCount > 0 && !AllWallsAreNecessary(grid, rows, cols))
                        {
                            continue;
                        }
                        if (spec.BlockedCellCount > 0 && !AllCellsOfTypeAreNecessary(grid, rows, cols, BlockType.Blocked))
                        {
                            continue;
                        }
                        if (!MechanicNecessityHolds(spec, grid, rows, cols)) { continue; }
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

        /// <summary>
        /// Ceiling on how many cells of a rule the deficit-climb may place, for a board of this
        /// side length. A ceiling only ever PERMITS: the climb stops the moment the board is
        /// unique (see MechanicRecipe.Instances), so raising it can never make a level carry more
        /// of a rule than it needed. What it prevents is the silent discard of a board that
        /// legitimately needed one more cell -- a failure that reads as "this size will not
        /// generate" rather than "the ceiling was too low" (GAME_EXPANSION_PLAN §6.40).
        ///
        /// 6 was hard-coded for every size. It stays exactly 6 at 7x7 and below, where it was
        /// never observed to bind -- the shipped 7x7 pack topped out at five cells (Arrowx5,
        /// OneWayx5) -- so 6x6 and 7x7 generation is bit-for-bit unchanged. 8x8 carries 31% more
        /// cells than 7x7, which puts a board needing seven well inside the plausible range.
        /// </summary>
        private static int InstanceCeilingFor(int gridSize)
        {
            return gridSize >= 8 ? 8 : 6;
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
        /// The mechanic-necessity rule, in either of its two modes.
        ///
        /// <b>Strict (MinNecessaryMechanics = 0, levels 1-50).</b> Every headline mechanic on the
        /// board must be individually load-bearing: removing it alone must open a second solution.
        /// That is what turned 13-of-41 decorative instances into 41-of-41 (§6.18).
        ///
        /// <b>K-of-M (MinNecessaryMechanics = K, the Mastery ranges).</b> The strict rule does not
        /// survive stacking, and the reason is worth stating because it is not "the boards got
        /// rarer". Individual necessity asks a MARGINAL question -- does removing this ONE mechanic
        /// open an alternative -- and once several mechanics sit on one board they begin ruling out
        /// the SAME wrong routes. Remove either one alone and the route is still blocked by the
        /// other, so both measure as unnecessary. Redundancy between mechanics reads as uselessness
        /// of each. Measured over 175 uniquely solvable 3-mechanic boards: 55.4% had none
        /// load-bearing, 37.1% one, 7.4% two, and 0% all three.
        ///
        /// So K-of-M requires at least K individually, then guards the remainder COLLECTIVELY: the
        /// mechanics that failed on their own are stripped together, and the board must stop being
        /// uniquely solvable. That distinguishes a mechanic which is redundant with another (it
        /// still constrains, just not alone -- keep it) from one that constrains nothing at all
        /// (decoration -- reject the board). Without that second step K-of-M would happily ship
        /// boards carrying M-K ornaments.
        /// </summary>
        private static bool MechanicNecessityHolds(GenerationSpec spec, Block[,] grid, int rows, int cols)
        {
            List<BlockType> present = new List<BlockType>();
            if (spec.ArrowCount > 0) { present.Add(BlockType.Arrow); }
            if (spec.ForbiddenCount > 0) { present.Add(BlockType.ForbiddenForPair); }
            if (spec.PermittedCount > 0) { present.Add(BlockType.AllowedForPairs); }
            if (spec.CheckpointCount > 0) { present.Add(BlockType.Checkpoint); }
            // A Bridge GRANTS capacity rather than restricting, so stripping it makes the board
            // unsolvable outright rather than multi-solution. RequiredMechanicValidator.Classify
            // already reads that as Required, so it needs no special case here.
            if (spec.BridgeCount > 0) { present.Add(BlockType.Bridge); }
            if (spec.OneWayCount > 0) { present.Add(BlockType.OneWay); }

            if (present.Count == 0) { return true; }

            List<BlockType> unnecessary = new List<BlockType>();
            int necessaryCount = 0;
            for (int i = 0; i < present.Count; i++)
            {
                if (AllCellsOfTypeAreNecessary(grid, rows, cols, present[i])) { necessaryCount++; }
                else { unnecessary.Add(present[i]); }
            }

            int required = spec.MinNecessaryMechanics > 0
                ? Math.Min(spec.MinNecessaryMechanics, present.Count)
                : present.Count;

            if (necessaryCount < required) { return false; }

            // Fewer than two failures means there is nothing collective left to ask. Stripping a
            // SINGLE type is bit-for-bit the individual test that just failed, so running the group
            // check on one mechanic can only ever fail again -- an early version did exactly that
            // and rejected every board at K=2 of 3, which looked like the spec being impossible
            // rather than the gate contradicting itself. One mechanic that does not matter alone is
            // precisely the slack K-of-M is chosen to allow; two or more may be masking each other,
            // and that is the case worth testing.
            if (unnecessary.Count < 2) { return true; }

            return MechanicsMatterTogether(grid, rows, cols, unnecessary);
        }

        /// <summary>
        /// Whether the given mechanic types, stripped ALL AT ONCE, cost the board its single
        /// solution. Answers "are these redundant with each other, or with nothing at all" -- the
        /// question individual necessity cannot ask. One extra solve, and only on candidates that
        /// have already passed everything else.
        ///
        /// Delegates the cloning and stripping to RequiredMechanicValidator rather than repeating
        /// them: a second copy of "how to remove a mechanic from a cell" is exactly the kind of
        /// duplicate that drifts out of sync, which is how BuildBlockGrid ended up silently
        /// ignoring columns BoardGenerator read (§6.22).
        /// </summary>
        private static bool MechanicsMatterTogether(Block[,] grid, int rows, int cols,
            List<BlockType> types)
        {
            RequiredMechanicValidator.RequirementResult result =
                RequiredMechanicValidator.CheckBlockTypesRequiredTogether(grid, rows, cols, types,
                    new PuzzleSolver.SolverOptions(SolverBudgetFor(rows), 2));

            return result.Status == RequiredMechanicValidator.RequirementStatus.Required;
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
        /// Measures what the solver and the refinement generator can actually do on full grids.
        ///
        /// Reports progress through a cancellable bar and writes a Console line after EVERY board
        /// rather than one summary at the end. Both matter: an earlier version logged only on
        /// completion, so a run that had silently never started looked identical to one grinding
        /// away, and twenty minutes were spent waiting on nothing.
        /// </summary>
        /// <summary>
        /// End-to-end cost of producing one shippable Classic board per size, 5x5 through 10x10.
        ///
        /// <b>Uses a deliberately SHORT step budget.</b> Solve cost varies enormously between
        /// instances rather than with board size -- measured, a 10x10 proved out in 14 s while a
        /// 9x9 was still running after 104 s and twenty million steps. Since generation only needs
        /// SOME good board rather than one specific board, abandoning an expensive instance is
        /// nearly free: the budget cap makes SearchExhausted false, which is already treated as
        /// "not proven unique" and rejected. Cheap boards are found instead, and the pathological
        /// ones are never waited on.
        /// </summary>
        /// <summary>
        /// Measures generation cost per board size, 5x5 through 10x10, with colour escalation.
        ///
        /// <b>Blocking, and deliberately so.</b> An earlier version drove this from
        /// EditorApplication.update to keep the editor responsive. That failed for a reason worth
        /// recording: Unity does not tick editor updates while its window is unfocused, so the job
        /// simply stopped whenever attention moved elsewhere -- measured at 0.3% CPU, indefinitely.
        /// A blocking loop that calls DisplayCancelableProgressBar often does show a live,
        /// cancellable bar; that is exactly how the shipping generators report progress. The bar
        /// updates on every attempt here.
        ///
        /// <b>Colour escalation.</b> Each size starts at the fewest colours worth attempting and
        /// steps up after a budget of failures -- 9x9 tries 10, then 11, then 12, and so on to the
        /// palette ceiling. Fewer colours is always the better puzzle, since it means longer paths,
        /// but a board too loose to prove unique is worth abandoning quickly. Merge-down still pulls
        /// the count back afterwards, so a higher START does not mean a higher result.
        ///
        /// Note a domain reload -- triggered by editing any script, or by Unity regaining focus and
        /// refreshing -- kills a run in progress. Leave scripts alone while this is going.
        /// </summary>
        /// <summary>
        /// Builds the whole Classic campaign: 100 levels, full grids, no mechanics of any kind.
        ///
        /// <b>Blocked by board size, with the colour count ramping DOWN inside each block.</b> Size
        /// gives the player a legible sense of chapter -- a bigger grid reads as harder before a
        /// line is drawn -- while the colour ramp supplies the actual difficulty curve within it.
        ///
        /// The downward direction is deliberate and measured. Fewer colours means longer paths,
        /// and path length is what tracks how hard a board feels (§6.14); more colours means
        /// shorter paths and an easier level, which is exactly the complaint that sank two earlier
        /// level sets. So each block opens generously and tightens.
        ///
        /// Capped at 7x7 on purpose for this pass: the whole run takes about a minute, so the
        /// curve can be played and rebuilt quickly. 8x8 costs 8s a level and 9x9 costs 204s, which
        /// are fine for a final build but ruinous for iteration.
        /// </summary>
        /// <summary>
        /// Rebuilds ONLY the 6x6 block (Classic 26-60), for iterating on difficulty quickly.
        ///
        /// <b>Builds a large pool, keeps the hardest, and orders those by effort.</b> Every earlier
        /// version fixed a colour count per level and accepted a board that met it. That control
        /// does not survive contact with the evidence: colour count and path length turned out not
        /// to predict difficulty at all, while solver decision points do (§6.29). So the colour
        /// target is gone as a difficulty dial -- candidates are drawn across a RANGE of colour
        /// counts, scored by the search each one demands, and the hardest are kept and sorted.
        ///
        /// Ordering the survivors ascending gives a real ramp measured in the thing that matters,
        /// rather than a ramp in a proxy that was pointing the wrong way half the time.
        /// </summary>
        /// <summary>
        /// Grows a partition whose paths INTERLEAVE, instead of one whose paths are merely valid.
        ///
        /// <b>Why the normal builder cannot produce these.</b> TryGeneratePathPartition grows by
        /// Warnsdorff, always extending into the most enclosed free cell. That is the right rule
        /// for never stranding a cell, and it is why it was adopted -- but it fills corners and
        /// edges first, so each colour accretes into a compact blob in its own corner of the board.
        /// Measured against a real Flow Free board, ours turned twice as often inside a third less
        /// space and touched other colours half as much: scribbly rather than tangled. Ranking a
        /// pool by tangle lifted the average from ~40 to 82, then stopped, because selection can
        /// only pick the best of what the builder happens to make.
        ///
        /// <b>What this does differently.</b> Three changes, each aimed at one of the measured
        /// gaps:
        ///   - <b>Round-robin growth.</b> Every path advances one cell per turn rather than one
        ///     path being driven to completion, so they weave through each other as they grow.
        ///   - <b>Straightness is rewarded.</b> Flow Free's paths turn 0.17 times per cell against
        ///     our 0.44-0.61; long runs are what carry a path across the board instead of curling
        ///     it up next to itself.
        ///   - <b>Touching yourself is penalised, touching others is rewarded.</b> This is the
        ///     direct attack on blob formation, and on the metric that separated the two most
        ///     clearly: cross-colour adjacency, 51% for Flow Free against 22-41% for ours.
        ///
        /// Warnsdorff is kept as an override rather than discarded: a free cell down to its last
        /// free neighbour is taken immediately whatever the aesthetic score says, because a
        /// stranded cell is not a worse board, it is no board at all. Failure stays cheap -- return
        /// null and let the caller retry with fresh seeds.
        /// </summary>
        private static List<List<(int Row, int Col)>> TryGenerateTangledPartition(int size,
            bool[,] usable, int usableCount, int pathCount, System.Random rng)
        {
            if (pathCount < 1 || usableCount < pathCount * 2) { return null; }

            int[,] taken = new int[size, size];
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++) { taken[r, c] = -1; }
            }

            List<(int Row, int Col)> free = new List<(int, int)>();
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++)
                {
                    if (usable[r, c]) { free.Add((r, c)); }
                }
            }
            for (int i = free.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (free[i], free[j]) = (free[j], free[i]);
            }

            List<List<(int Row, int Col)>> paths = new List<List<(int, int)>>();
            for (int i = 0; i < pathCount; i++)
            {
                taken[free[i].Row, free[i].Col] = i;
                paths.Add(new List<(int, int)> { free[i] });
            }

            int placed = pathCount;
            bool[] stuck = new bool[pathCount];

            while (placed < usableCount)
            {
                bool anyGrew = false;

                for (int p = 0; p < pathCount && placed < usableCount; p++)
                {
                    if (stuck[p]) { continue; }

                    (int Row, int Col) head = paths[p][paths[p].Count - 1];
                    (int Row, int Col) prev = paths[p].Count > 1 ? paths[p][paths[p].Count - 2] : head;

                    int bestScore = int.MinValue;
                    (int Row, int Col) bestCell = (0, 0);
                    int ties = 0;

                    for (int d = 0; d < Directions.Length; d++)
                    {
                        int nr = head.Row, nc = head.Col;
                        switch (Directions[d])
                        {
                            case Direction.Left: nc--; break;
                            case Direction.Right: nc++; break;
                            case Direction.Up: nr--; break;
                            case Direction.Down: nr++; break;
                        }
                        if (nr < 0 || nr >= size || nc < 0 || nc >= size) { continue; }
                        if (!usable[nr, nc] || taken[nr, nc] != -1) { continue; }

                        int score = 0;

                        // Keep going the way we were: long runs cross the board.
                        bool straight = (nr - head.Row) == (head.Row - prev.Row)
                                     && (nc - head.Col) == (head.Col - prev.Col);
                        if (straight && paths[p].Count > 1) { score += 6; }

                        int ownNeighbours = 0, otherNeighbours = 0, freeNeighbours = 0;
                        for (int e = 0; e < Directions.Length; e++)
                        {
                            int ar = nr, ac = nc;
                            switch (Directions[e])
                            {
                                case Direction.Left: ac--; break;
                                case Direction.Right: ac++; break;
                                case Direction.Up: ar--; break;
                                case Direction.Down: ar++; break;
                            }
                            if (ar < 0 || ar >= size || ac < 0 || ac >= size) { continue; }
                            if (!usable[ar, ac]) { continue; }
                            if (taken[ar, ac] == -1) { freeNeighbours++; }
                            else if (taken[ar, ac] == p) { ownNeighbours++; }
                            else { otherNeighbours++; }
                        }

                        // Hugging your own body is what makes a blob; brushing other colours is
                        // what makes the board feel woven.
                        score -= 5 * (ownNeighbours - 1);   // -1: the head itself always counts
                        score += 4 * otherNeighbours;

                        // Warnsdorff, kept as an override: a cell about to be sealed off has to be
                        // taken now regardless of how it scores.
                        if (freeNeighbours <= 1) { score += 40; }

                        if (score > bestScore) { bestScore = score; bestCell = (nr, nc); ties = 1; }
                        else if (score == bestScore) { ties++; if (rng.Next(ties) == 0) { bestCell = (nr, nc); } }
                    }

                    if (bestScore == int.MinValue) { stuck[p] = true; continue; }

                    taken[bestCell.Row, bestCell.Col] = p;
                    paths[p].Add(bestCell);
                    placed++;
                    anyGrew = true;
                }

                // Every path walled in with cells still free -- cheap failure, caller retries.
                if (!anyGrew) { return null; }
            }

            for (int i = 0; i < paths.Count; i++)
            {
                if (paths[i].Count < 2) { return null; }
            }
            return paths;
        }

        /// <summary>
        /// How TANGLED a board's solution is: colours interleaving rather than each owning a
        /// compact region of its own.
        ///
        /// <b>Why this and not solver effort.</b> Four search-based metrics were tried and each
        /// said the shipped boards should be hard while play said otherwise. Measuring the SHAPE of
        /// the solution finally separated them. Against Flow Free's 8x8:
        ///
        ///   turns per cell        ours 0.44-0.61   theirs 0.17
        ///   bounding-box fill     ours 0.73-0.87   theirs 0.63
        ///   cross-colour touching ours 22-41%      theirs 51%
        ///
        /// Their paths turn LESS, sprawl MORE, and run alongside other colours far more often.
        /// Ours wiggle inside a small area -- scribbly, not tangled, each colour keeping to its own
        /// blob. That is a direct consequence of Warnsdorff growth, which fills corners and edges
        /// first: excellent at not stranding cells, and the wrong shape entirely.
        ///
        /// The hardest 6x6 by decision count scored 25 here against Flow Free's 81, which is why
        /// the search metrics and the playtests kept disagreeing.
        /// </summary>
        public static float TangleScore(Block[,] grid, int rows, int cols, PuzzleSolver.SolveResult solved)
        {
            if (solved.Solutions == null || solved.Solutions.Count == 0) { return 0f; }

            int[,] ownerOf = new int[rows, cols];
            foreach (PuzzleSolver.PairSolution ps in solved.Solutions)
            {
                for (int i = 0; i < ps.Cells.Count; i++)
                {
                    ownerOf[ps.Cells[i].Row, ps.Cells[i].Col] = ps.PairId;
                }
            }

            float boxFillSum = 0f;
            foreach (PuzzleSolver.PairSolution ps in solved.Solutions)
            {
                int minR = int.MaxValue, maxR = -1, minC = int.MaxValue, maxC = -1;
                for (int i = 0; i < ps.Cells.Count; i++)
                {
                    (int Row, int Col) v = ps.Cells[i];
                    if (v.Row < minR) { minR = v.Row; }
                    if (v.Row > maxR) { maxR = v.Row; }
                    if (v.Col < minC) { minC = v.Col; }
                    if (v.Col > maxC) { maxC = v.Col; }
                }
                boxFillSum += ps.Cells.Count / (float)((maxR - minR + 1) * (maxC - minC + 1));
            }
            float boxFill = boxFillSum / solved.Solutions.Count;

            int cross = 0, total = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (ownerOf[r, c] == 0) { continue; }
                    if (r + 1 < rows && ownerOf[r + 1, c] != 0)
                    { total++; if (ownerOf[r + 1, c] != ownerOf[r, c]) { cross++; } }
                    if (c + 1 < cols && ownerOf[r, c + 1] != 0)
                    { total++; if (ownerOf[r, c + 1] != ownerOf[r, c]) { cross++; } }
                }
            }
            if (total == 0 || boxFill <= 0f) { return 0f; }

            return (100f * cross / total) / boxFill;
        }

        /// <summary>
        /// Writes three CALIBRATION boards into Classic 58, 59 and 60 -- the hardest 6x6, 7x7 and
        /// 8x8 this generator can currently produce, with no ramp and no other constraint.
        ///
        /// <b>Why measure against a person instead of another metric.</b> Three different proxies
        /// each said the shipped levels should be hard, and play said otherwise every time: path
        /// length (§6.29 retracted it), alternative-pairing count (Flow Free's board has none and
        /// is hard), and solver decision points (a 6x6 reached 7192, above Flow Free's 4600, and
        /// still played easy). Forced-move collapse was checked too and pointed the other way --
        /// our boards are LESS deducible than Flow Free's, 25% against 33%.
        ///
        /// What has not been tested is whether a 6x6 can be hard at all for an experienced player.
        /// 36 cells and four pairs may simply not hold enough uncertainty, in which case no amount
        /// of selection pressure on a 6x6 will help and the answer is board size. These three
        /// boards put that question to the only instrument that has been reliable so far.
        /// </summary>
        /// <summary>
        /// Rebuilds Classic levels 1-50, selecting boards by TANGLE.
        ///
        /// <b>Tangle is the criterion because it is the only one play agreed with.</b> Four
        /// search-effort metrics were tried first -- path length, alternative-pairing count, solver
        /// decision points, forced-move collapse -- and every one of them rated boards as hard that
        /// played easy. Measuring the SHAPE of the solution instead separated them immediately:
        /// Flow Free's paths turn less, sprawl more, and run alongside other colours far more than
        /// ours did, which is what "having to route around everyone else" actually is.
        ///
        /// <b>The two criteria oppose each other</b>, which is why the earlier attempts failed. The
        /// board a solver flails hardest on is a compact scribble: our most decision-heavy 6x6
        /// scored 16598 decisions and 25 tangle, while the most tangled scored 86 tangle and 728
        /// decisions. Optimising effort was actively selecting against the property wanted.
        ///
        /// Each block generates a pool, keeps the most tangled, then orders those ascending so the
        /// block still ramps -- every level above a high floor rather than merely the last one.
        /// </summary>
        [MenuItem("FreeFlow/Level Generator/Classic/Rebuild levels 1-50 on tangle")]
        public static void RebuildClassicFirstFifty()
        {
            const string levelsFolder = "Assets/Resources/Levels/Classic";

            // size, firstLevel, lastLevel, poolTarget
            int[,] blocks =
            {
                { 5,  1, 15, 320 },
                { 6, 16, 32, 400 },
                { 7, 33, 50, 260 },
            };

            EnsureLevelFolder(levelsFolder);
            EditorUtility.ClearProgressBar();

            HashSet<string> seen = new HashSet<string>();
            StringBuilder report = new StringBuilder();
            System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();
            bool cancelled = false;

            try
            {
                for (int b = 0; b < blocks.GetLength(0) && !cancelled; b++)
                {
                    int size = blocks[b, 0];
                    int firstLevel = blocks[b, 1];
                    int lastLevel = blocks[b, 2];
                    int poolTarget = blocks[b, 3];
                    int needed = lastLevel - firstLevel + 1;
                    int cells = size * size;

                    bool[,] usable = new bool[size, size];
                    for (int r = 0; r < size; r++)
                    {
                        for (int c = 0; c < size; c++) { usable[r, c] = true; }
                    }

                    System.Random rng = new System.Random(20261225 + size);
                    List<(float Tangle, int Colours, int Decisions, LevelData Data)> pool =
                        new List<(float, int, int, LevelData)>();

                    for (int attempt = 0; attempt < 12000 && pool.Count < poolTarget; attempt++)
                    {
                        if ((attempt % 4) == 0 && EditorUtility.DisplayCancelableProgressBar(
                                "Rebuilding Classic 1-50  (block " + (b + 1) + "/3)",
                                size + "x" + size + "  -  pool " + pool.Count + "/" + poolTarget
                                    + "  -  " + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                                (b + pool.Count / (float)poolTarget) / 3f))
                        { cancelled = true; break; }

                        // Sweep colour counts; which one yields a tangled board is not predictable.
                        int colours = Mathf.Max(3, cells / 9) + (attempt % 6);
                        if (colours > MaxDistinctColors) { continue; }

                        if (!TryGenerateUniqueByRefinement(size, usable, cells, colours,
                                MaxDistinctColors, 2000000, 3, rng,
                                out LevelData data, out int finalColours, out int splits, colours))
                        {
                            continue;
                        }

                        Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
                        string key;
                        float tangle;
                        int decisions;
                        try
                        {
                            key = LevelCanonicalizer.ComputeCanonicalKey(grid, rows, cols);
                            if (seen.Contains(key)) { continue; }
                            PuzzleSolver.SolveResult solved = PuzzleSolver.Solve(grid, rows, cols,
                                new PuzzleSolver.SolverOptions(8000000, 1));
                            tangle = TangleScore(grid, rows, cols, solved);
                            decisions = solved.DecisionPointCount;
                        }
                        finally { DestroyBlockGrid(grid); }

                        seen.Add(key);
                        pool.Add((tangle, finalColours, decisions, data));
                    }

                    if (pool.Count < needed)
                    {
                        Debug.LogError("Block " + size + "x" + size + ": only " + pool.Count
                            + " candidates for " + needed + " levels; block skipped.");
                        continue;
                    }

                    pool.Sort((x, y) => y.Tangle.CompareTo(x.Tangle));          // most tangled first
                    List<(float Tangle, int Colours, int Decisions, LevelData Data)> chosen =
                        pool.GetRange(0, needed);
                    chosen.Sort((x, y) => x.Tangle.CompareTo(y.Tangle));        // then ramp upward

                    for (int i = 0; i < chosen.Count; i++)
                    {
                        SaveLevelAsset(levelsFolder, firstLevel + i, chosen[i].Data, 0f);
                    }

                    report.Append(size).Append('x').Append(size)
                          .Append("  levels ").Append(firstLevel).Append('-').Append(lastLevel)
                          .Append(":  tangle ").Append(chosen[0].Tangle.ToString("0"))
                          .Append("..").Append(chosen[chosen.Count - 1].Tangle.ToString("0"))
                          .Append("   from a pool of ").Append(pool.Count)
                          .Append(" spanning ").Append(pool[pool.Count - 1].Tangle.ToString("0"))
                          .Append("..").Append(pool[0].Tangle.ToString("0"))
                          .AppendLine();
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            total.Stop();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("Classic 1-50 rebuilt on tangle in "
                + (total.ElapsedMilliseconds / 1000f).ToString("0.0") + "s"
                + (cancelled ? " (CANCELLED)" : "") + ".\n" + report
                + "Flow Free 8x8 reference: tangle 81.");
        }

        /// <summary>
        /// Rebuilds Classic 1-50 using the full difficulty model: structural gates as a filter,
        /// then the four-metric blend as the ranking.
        ///
        /// <b>What is different from the tangle rebuild this replaces.</b> Tangle is still in the
        /// blend, but it is now one term of four rather than the whole criterion, and -- more
        /// importantly -- a board is REJECTED before it is ever ranked. Ranking was doing all the
        /// work before, and ranking cannot fix a malformed board; it can only prefer one malformed
        /// board over another. The first calibration run made that concrete: of ten shipped Classic
        /// levels, five had a link touching itself and two had near-uniform path lengths, and one
        /// of the self-touching boards scored ABOVE the Flow Free reference.
        ///
        /// <b>Two passes, because the model is not cheap.</b> Measuring every term costs roughly
        /// (colours + 2) solves per board, which is far too much to spend on a candidate that a
        /// free check would have thrown out. So:
        ///   1. generate, solve for uniqueness (needed anyway), and run the structural gates, which
        ///      read only the solution already in hand;
        ///   2. shortlist the survivors by tangle -- also free, already computed -- and pay for the
        ///      full model on the shortlist only.
        /// The shortlist is deliberately wider than the block needs, so the expensive measure still
        /// has room to disagree with the cheap one.
        /// </summary>
        /// <summary>
        /// The 5x5 pack. Its shortlist and pool are the largest RELATIVE to board size, which looks
        /// backwards until you look at what limits this size.
        ///
        /// The first build picked 100 levels from 500 scored -- 5x -- against 7x7's 20x, on the
        /// assumption that 25 cells could not stretch further. It duplicated 70% of the time, but
        /// that turned out not to be a fixed property: a fresh 2000-attempt probe duplicates at
        /// ~50%, and the rate climbs as the pool fills. It is a coupon-collector curve against a
        /// finite reachable set, not a wall -- the generator never stopped finding new boards, it
        /// just found them more slowly, so the honest fix is to keep asking.
        ///
        /// Widening the colour sweep was tried first and measured WORSE: asking 3-7 yielded 92
        /// distinct sound boards per 2000 attempts against 3-5's 122, because high-colour attempts
        /// mostly fail to generate at all. That lever is closed; more attempts is the one that works.
        ///
        /// If the pool cannot be filled, the gather loop simply exits on its attempt cap and the
        /// pack is built from what it found -- thinner selection, never fewer levels.
        /// </summary>
        /// <summary>
        /// How many sound 8x8 boards each colour count actually yields, and how much they cost.
        ///
        /// Run as an editor job rather than an inline snippet because the interesting attempts are
        /// the slow ones: at low colour counts nearly every attempt dies instantly in the partition
        /// builder, and the rare one that gets through can spend a long time proving uniqueness.
        /// Short-timeout tooling sees only the instant failures and times out on the informative
        /// cases -- which is how "8x8 needs 10-12 colours" got recorded off samples of ten and
        /// twelve attempts at a yield rate near 5%. That was not a measurement.
        /// </summary>
        [MenuItem("FreeFlow/Level Generator/Classic/Packs/PROBE 8x8 colour counts")]
        public static void ProbeEightByEightColours()
        {
            ProbeColourCounts(8, new int[] { 7, 8, 9, 10 }, 1500);
        }

        /// <summary>
        /// 9x9, probed before anything long is started. Its costs are not an extrapolation of 8x8's:
        /// §6.31 measured 9x9 at 1243 ms average solve against 7x7's 9 ms, so the attempt count and
        /// colour range that suit 8x8 may be hopeless here. Fewer attempts because each is dearer --
        /// enough to see a rate, not enough to be a run.
        /// </summary>
        [MenuItem("FreeFlow/Level Generator/Classic/Packs/PROBE 9x9 colour counts")]
        public static void ProbeNineByNineColours()
        {
            ProbeColourCounts(9, new int[] { 9, 10, 11, 12 }, 400);
        }

        private static void ProbeColourCounts(int size, int[] asks, int attemptsPer)
        {
            int cells = size * size;

            bool[,] usable = new bool[size, size];
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++) { usable[r, c] = true; }
            }

            StringBuilder report = new StringBuilder();
            System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();
            EditorUtility.ClearProgressBar();
            bool cancelled = false;

            try
            {
                for (int i = 0; i < asks.Length && !cancelled; i++)
                {
                    int ask = asks[i];
                    System.Random rng = new System.Random(90210 + ask);
                    int generated = 0, sound = 0;
                    float pathSum = 0f;
                    Dictionary<int, int> finalColours = new Dictionary<int, int>();
                    System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();

                    for (int a = 0; a < attemptsPer; a++)
                    {
                        if ((a % 16) == 0 && EditorUtility.DisplayCancelableProgressBar(
                                "Probing " + size + "x" + size + " colour counts",
                                "ask " + ask + " colours  -  " + a + "/" + attemptsPer
                                    + "  -  " + sound + " sound"
                                    + "  -  " + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                                (i + a / (float)attemptsPer) / asks.Length))
                        { cancelled = true; break; }

                        if (!TryGenerateUniqueByRefinement(size, usable, cells, ask,
                                MaxDistinctColors, 2000000, 3, rng,
                                out LevelData data, out int finalCount, out int splits, ask))
                        {
                            continue;
                        }
                        generated++;

                        Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
                        try
                        {
                            PuzzleSolver.SolveResult solved = PuzzleSolver.Solve(grid, rows, cols,
                                new PuzzleSolver.SolverOptions(8000000, 2));
                            bool unique = solved.Status == PuzzleSolver.SolveStatus.Solved
                                && solved.SolutionsFound == 1 && solved.SearchExhausted;

                            int usableCells = 0;
                            for (int r = 0; r < rows; r++)
                            {
                                for (int c = 0; c < cols; c++)
                                {
                                    if (grid[r, c] != null && grid[r, c].BlockType != BlockType.Blocked)
                                    {
                                        usableCells++;
                                    }
                                }
                            }

                            StructuralGates.Report rep = StructuralGates.Evaluate(solved, usableCells);
                            if (!unique || !rep.Passed) { continue; }

                            sound++;
                            pathSum += rep.MeanPath;
                            int k = solved.Solutions.Count;
                            finalColours[k] = finalColours.ContainsKey(k) ? finalColours[k] + 1 : 1;
                        }
                        finally { DestroyBlockGrid(grid); }
                    }
                    timer.Stop();

                    report.Append("  ask ").Append(ask).Append(": generated ").Append(generated)
                          .Append(", sound ").Append(sound);
                    if (sound > 0)
                    {
                        report.Append("   meanPath ").Append((pathSum / sound).ToString("0.0"))
                              .Append("   ").Append((timer.ElapsedMilliseconds / (float)sound).ToString("0"))
                              .Append(" ms/sound   final:");
                        List<int> keys = new List<int>(finalColours.Keys);
                        keys.Sort();
                        for (int k = 0; k < keys.Count; k++)
                        {
                            report.Append(' ').Append(keys[k]).Append('x').Append(finalColours[keys[k]]);
                        }
                    }
                    report.Append("   (").Append((timer.ElapsedMilliseconds / 1000f).ToString("0"))
                          .AppendLine("s)");
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            total.Stop();
            Debug.Log(size + "x" + size + " colour probe, " + attemptsPer + " attempts each, "
                + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s"
                + (cancelled ? " (CANCELLED)" : "") + ":\n" + report
                + "  reference -- Flow Free 8x8: 9 colours, meanPath 7.1, 13 assumptions\n"
                + "  shipped packs: 7x7 path 8.7 / 7.1 assumptions, 8x8 path 7.4 / 7.8 assumptions");
        }

        [MenuItem("FreeFlow/Level Generator/Classic/Packs/Build 5x5 pack (100)")]
        public static void BuildPack5x5() { BuildSizePack(5, 100, 12, 1500); }

        [MenuItem("FreeFlow/Level Generator/Classic/Packs/Build 6x6 pack (100)")]
        public static void BuildPack6x6() { BuildSizePack(6, 100, 10, 1100); }

        [MenuItem("FreeFlow/Level Generator/Classic/Packs/Build 7x7 pack (100)")]
        public static void BuildPack7x7() { BuildSizePack(7, 100, 20, 2100); }

        /// <summary>
        /// The 8x8 pack, which is NOT a harder 7x7 and should not be expected to be.
        ///
        /// It needs its own colour target because the ratio the other packs use produces nothing
        /// here: cells/12 asks for 5 colours on 64 cells, and 162 attempts at 5-7 yielded zero
        /// boards. 8x8 only generates at 10-12. That is the puzzle's own arithmetic -- more cells
        /// need more colours before uniqueness can be proved -- and more colours means shorter
        /// paths: mean 6.0 here against the 7x7 pack's 8.7.
        ///
        /// <b>The "8x8 needs 10-12 colours" claim was wrong, and wrongly arrived at.</b> It came
        /// from samples of ten and twelve attempts at a yield rate near 5%, where zero results is
        /// the EXPECTED outcome even when the colour count works perfectly well. Probed properly at
        /// 1500 attempts each:
        ///
        /// | ask | sound / 1500 | mean path | ms per sound board |
        /// |---|---|---|---|
        /// | 7 | 51 | 8.9 | 6772 |
        /// | 8 | 71 | 7.9 | 1916 |
        /// | 9 | 54 | 7.1 | 877 |
        /// | 10 | 83 | 6.4 | 251 |
        ///
        /// Seven, eight and nine all yield perfectly well -- they are just far dearer per board,
        /// which is exactly what a ten-attempt sample cannot see: it observes only the instant
        /// partition failures and never reaches the informative case. Nine colours also reproduces
        /// Flow Free's own 8x8 exactly, 9 colours at mean path 7.1.
        ///
        /// So this pack asks for 7-9 rather than 10-12: fewer colours to track AND longer paths,
        /// which is better on both counts. The first build at 10-12 gave mean path 5.6 and 4.9
        /// assumptions, below the 7x7 pack's 8.7 and 7.1 -- the worst of both worlds.
        ///
        /// Costs were measured per stage first (197 ms to gather a board, 26 ms stage one, 1909 ms
        /// stage two) after three run estimates in a row went wrong by omitting whichever stage was
        /// not front of mind.
        /// </summary>
        [MenuItem("FreeFlow/Level Generator/Classic/Packs/Build 8x8 pack (100)")]
        public static void BuildPack8x8() { BuildSizePack(8, 100, 12, 1200, 9); }

        /// <summary>
        /// The 9x9 pack. An overnight job, chosen deliberately over the cheaper configurations.
        ///
        /// Probed at 400 attempts per colour count before committing, because 9x9's costs are not
        /// an extrapolation of 8x8's:
        ///
        /// | ask | sound / 400 | mean path | ms per sound board |
        /// |---|---|---|---|
        /// | 9 | 8 | 8.7 | 19,491 |
        /// | 10 | 2 | 7.7 | 62,011 (2 samples -- noise, not a measurement) |
        /// | 11 | 13 | 7.3 | 11,622 |
        /// | 12 | 9 | 6.7 | 2,595 |
        ///
        /// <b>The affordable configurations are the ones that make a worse puzzle.</b> At 12 colours
        /// a board costs 2.6s and gives a 6.7-cell path -- SHORTER than the 8x8 pack already manages
        /// at 7-9 colours. Only 9-11 colours beat it, and those run 12-20s per board. So this asks
        /// for 9-11 and pays for it: roughly 11 hours to gather 1200 boards at a 60% duty cycle,
        /// plus about 1.7 hours to score them.
        ///
        /// Worth stating plainly, since the intuition keeps reasserting itself and has now been
        /// wrong twice: a bigger board does not mean a harder pack. More cells need more colours
        /// before uniqueness can be proved, and more colours means shorter paths.
        /// </summary>
        [MenuItem("FreeFlow/Level Generator/Classic/Packs/Build 9x9 pack (100)")]
        public static void BuildPack9x9() { BuildSizePack(9, 100, 12, 1200, 9); }

        /// <summary>
        /// Builds one self-contained PACK of <paramref name="count"/> levels at a single board size,
        /// into <c>Assets/Resources/Levels/Classic/{size}x{size}</c>.
        ///
        /// <b>Packs are chosen, not progressed through.</b> The player picks a board size and plays
        /// that pack from 1, so each pack ramps on its own from the easiest board the size can
        /// produce to the hardest -- there is no cross-pack ordering to preserve, and every pack
        /// starts gently on purpose.
        ///
        /// <b>Selection is STRATIFIED, not top-N</b>, and that is the difference between a ramp and
        /// a plateau. Taking the hardest 100 of 2000 gives a hundred hard levels with almost no
        /// gradient between them; the earlier 18-level blocks could get away with it because they
        /// were a slice appended to a campaign, not a hundred-level arc. See
        /// <see cref="SelectStratified"/>.
        ///
        /// <b>Scoring is two-stage</b>, because relaxation is 98% of the model's cost (55 ms per
        /// 7x7 board without it, 2243 ms with) while carrying only 23% of its weight. Stage one
        /// ranks every candidate cheaply; stage two pays the full price for a wide slice spanning
        /// the whole range, so the final ramp is still picked with complete information. For a
        /// 100-level 7x7 pack that is 13 minutes rather than 75.
        ///
        /// <paramref name="shortlistPerLevel"/> is per size for a measured reason: the 5x5 board has
        /// a hard material ceiling. 4000 attempts yielded 226 canonically distinct boards, on a
        /// curve already flattening (74 / 140 / 187 / 226 across the run), so a 20x shortlist there
        /// is not merely slow, it may not exist. 100 distinct is comfortable; 2000 is not.
        /// </summary>
        /// <param name="cellsPerColour">
        /// Overrides <see cref="CellsPerColourTarget"/> for this pack. It exists because the ratio
        /// that works for 5x5-7x7 produces NOTHING at 8x8: cells/12 asks for 5 colours on 64 cells,
        /// and 162 attempts at 5-7 colours yielded zero boards. 8x8 only generates at 10-12, which
        /// is a property of the puzzle rather than a tuning choice -- more cells need more colours
        /// to keep uniqueness provable, and that shortens every path.
        /// </param>
        /// <summary>
        /// Fraction of wall-clock time a long generation run is allowed to spend working. The rest
        /// is spent asleep, so a long job does not pin a core flat out and cook the machine into
        /// thermal throttling -- which slows the run down anyway, on top of everything else it
        /// risks.
        ///
        /// 0.9 leaves a tenth of the time idle: enough to break up sustained load, at a cost of
        /// about a ninth on wall-clock rather than the two thirds 0.6 added. Raised from 0.6 once
        /// the machine had been observed through a 23-hour run at that setting. Lower it again for
        /// anything that will run unattended overnight.
        ///
        /// Now 1f -- throttling off by explicit request, for an attended run on a machine
        /// whose thermals are being watched. Tick() early-outs at duty >= 1f, so this
        /// disables the mechanism outright rather than sleeping zero. Restore 0.9f for
        /// unattended runs.
        /// </summary>
        private const float GenerationDutyCycle = 1f;

        /// <summary>
        /// Holds a target duty cycle by sleeping in proportion to work actually done, rather than
        /// sleeping a fixed amount every N iterations. That distinction matters here because the
        /// cost of one attempt varies enormously -- an 8x8 partition failure returns in under a
        /// millisecond while proving uniqueness on a 7-colour board can take seven seconds -- so a
        /// per-iteration sleep would throttle the cheap work hard and the expensive work not at all.
        /// </summary>
        private sealed class CpuThrottle
        {
            private readonly float duty;
            private readonly System.Diagnostics.Stopwatch since = System.Diagnostics.Stopwatch.StartNew();

            public CpuThrottle(float dutyCycle) { duty = Mathf.Clamp(dutyCycle, 0.05f, 1f); }

            public void Tick()
            {
                if (duty >= 1f) { return; }

                long worked = since.ElapsedMilliseconds;
                if (worked < 100) { return; }        // only rest after a meaningful slice of work

                int rest = (int)(worked * (1f / duty - 1f));

                // Paid off in slices rather than truncated to one. The first version slept
                // Min(rest, 250) and moved on, which silently defeated the whole mechanism on
                // exactly the work that needed it: a single 7-colour 8x8 attempt costs ~6.8s, owes
                // ~4.5s of rest, and got 250ms -- so a run intended to hold 60% duty measured at
                // 92% of a core. The cap belongs on each SLEEP, so the progress bar keeps
                // repainting and Cancel stays responsive, not on the debt.
                while (rest > 0)
                {
                    int slice = Mathf.Min(rest, 250);
                    System.Threading.Thread.Sleep(slice);
                    rest -= slice;
                }
                since.Restart();
            }
        }

        private static void BuildSizePack(int size, int count, int shortlistPerLevel, int poolTarget,
            int cellsPerColour = CellsPerColourTarget)
        {
            string levelsFolder = "Assets/Resources/Levels/Classic/" + size + "x" + size;
            int cells = size * size;

            EnsureLevelFolder(levelsFolder);
            EditorUtility.ClearProgressBar();

            bool[,] usable = new bool[size, size];
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++) { usable[r, c] = true; }
            }

            HashSet<string> seen = new HashSet<string>();
            System.Random rng = new System.Random(20260901 + size);
            System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();
            bool cancelled = false;

            List<LevelData> survivors = new List<LevelData>();
            CpuThrottle throttle = new CpuThrottle(GenerationDutyCycle);
            int generated = 0, rejectedStructure = 0, duplicates = 0, notUnique = 0, badSolution = 0;
            int shortlistTarget = count * shortlistPerLevel;

            try
            {
                // --- gather: unique, structurally sound, canonically distinct ------------------
                for (int attempt = 0; attempt < 200000 && survivors.Count < poolTarget; attempt++)
                {
                    throttle.Tick();

                    if ((attempt % 8) == 0 && EditorUtility.DisplayCancelableProgressBar(
                            "Building the " + size + "x" + size + " pack  -  gathering",
                            "kept " + survivors.Count + "/" + poolTarget
                                + "  -  " + rejectedStructure + " unsound, " + duplicates + " dupes"
                                + (notUnique > 0 ? ", " + notUnique + " NOT UNIQUE" : string.Empty)
                                + (badSolution > 0 ? ", " + badSolution + " BAD SOLUTION" : string.Empty)
                                + "  -  " + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                            0.5f * survivors.Count / poolTarget))
                    { cancelled = true; break; }

                    int colours = Mathf.Max(3, cells / cellsPerColour)
                                + (attempt % ColourSweepWidth);
                    if (colours > MaxDistinctColors) { continue; }

                    if (!TryGenerateUniqueByRefinement(size, usable, cells, colours,
                            MaxDistinctColors, 2000000, 3, rng,
                            out LevelData data, out int finalColours, out int splits, colours))
                    {
                        continue;
                    }
                    generated++;

                    Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
                    bool keep;
                    try
                    {
                        string key = LevelCanonicalizer.ComputeCanonicalKey(grid, rows, cols);
                        if (!seen.Add(key)) { duplicates++; continue; }

                        // Two solutions requested, not one, so this RE-PROVES uniqueness rather
                        // than merely finding a solution to measure. Refinement already proved it
                        // for this exact LevelData, so on paper the check is redundant -- but §6.20
                        // is the counter-example: the Bridge constructor guaranteed two colours
                        // crossed, and the dots DERIVED from it admitted a different unique
                        // solution. The lesson recorded then was to verify construction-time
                        // guarantees against the solved board, and a hundred-level pack is not the
                        // place to trust an argument over a measurement.
                        PuzzleSolver.SolveResult solved = PuzzleSolver.Solve(grid, rows, cols,
                            new PuzzleSolver.SolverOptions(8000000, 2));

                        bool unique = solved.Status == PuzzleSolver.SolveStatus.Solved
                            && solved.SolutionsFound == 1
                            && solved.SearchExhausted;
                        if (!unique) { notUnique++; }

                        // The stored answer has to BE the answer. Cheap here, and the failure it
                        // guards is silent: a level whose solutionPairId disagrees with the solver
                        // would give wrong hints forever without anything else noticing.
                        if (unique && !StoredSolutionMatchesSolver(data, solved, rows, cols))
                        {
                            badSolution++;
                            unique = false;
                        }

                        int usableCells = 0;
                        for (int r = 0; r < rows; r++)
                        {
                            for (int c = 0; c < cols; c++)
                            {
                                if (grid[r, c] != null && grid[r, c].BlockType != BlockType.Blocked)
                                {
                                    usableCells++;
                                }
                            }
                        }
                        keep = unique && StructuralGates.Evaluate(solved, usableCells).Passed;
                    }
                    finally { DestroyBlockGrid(grid); }

                    if (!keep) { rejectedStructure++; continue; }
                    survivors.Add(data);
                }

                if (survivors.Count < count)
                {
                    Debug.LogError("Pack " + size + "x" + size + ": only " + survivors.Count
                        + " distinct sound boards for " + count + " levels. Generated " + generated
                        + ", " + duplicates + " duplicates, " + rejectedStructure + " unsound."
                        + (duplicates > generated / 2
                            ? "  Duplicates dominate -- this size may be near its material ceiling."
                            : string.Empty));
                    return;
                }

                ScoreAndWritePack(survivors, levelsFolder, size + "x" + size, count,
                    shortlistTarget, throttle, total, ref cancelled,
                    "  generated " + generated + ", " + duplicates + " duplicates, "
                        + rejectedStructure + " unsound, " + notUnique + " not unique, "
                        + badSolution + " bad solution, " + survivors.Count + " distinct kept");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        /// <summary>
        /// Whether the solution recorded in <c>GridRow.solutionPairId</c> is the same one the solver
        /// finds. Only meaningful on a board already proved unique -- with two solutions there is no
        /// "the" answer to agree with, which is precisely why hints need uniqueness.
        /// </summary>
        private static bool StoredSolutionMatchesSolver(LevelData data,
            PuzzleSolver.SolveResult solved, int rows, int cols)
        {
            if (solved.Solutions == null) { return false; }

            int[,] fromSolver = new int[rows, cols];
            for (int i = 0; i < solved.Solutions.Count; i++)
            {
                List<(int Row, int Col)> cells = solved.Solutions[i].Cells;
                for (int j = 0; j < cells.Count; j++)
                {
                    fromSolver[cells[j].Row, cells[j].Col] = solved.Solutions[i].PairId;
                }
            }

            for (int r = 0; r < rows; r++)
            {
                int[] stored = data.gridRows[r].solutionPairId;
                if (stored == null) { return false; }

                for (int c = 0; c < cols; c++)
                {
                    if (stored[c] != fromSolver[r, c]) { return false; }
                }
            }
            return true;
        }

        /// <summary>
        /// Builds a handful of Bridge and Shared Destination boards through the spec-based pipeline
        /// that already knows how to make them.
        ///
        /// Deliberately few: they cannot be difficulty-ranked, so every one placed is a slot the
        /// ramp does not get to choose. Enough to introduce each mechanic and revisit it, not enough
        /// to dilute the pack.
        /// </summary>
        private static List<(LevelData Data, string Mechanic)> GatherStructuralMechanics(int size,
            int count, HashSet<string> seen, System.Random rng, CpuThrottle throttle,
            System.Diagnostics.Stopwatch total, ref bool cancelled)
        {
            List<(LevelData Data, string Mechanic)> found = new List<(LevelData, string)>();
            int wanted = Mathf.Max(2, count / 25);

            // More than one of each, for the same reason every other rule now has a floor of two:
            // a single bridge reads as a curiosity of that board rather than a mechanic. These are
            // structural, so the count is asked of the spec rather than climbed to.
            (string Name, int Bridges, int SharedGoals)[] kinds =
            {
                ("Bridge", 2, 0),
                ("SharedGoal", 0, 2),
            };

            for (int k = 0; k < kinds.Length && !cancelled; k++)
            {
                int made = 0;
                // 1200 rather than the original 400: the MinPathCells gate below rejects
                // boards that previously passed, and these levels sit at FIXED positions
                // in the schedule, so coming up short leaves visible holes rather than
                // merely thinning a pool. Structural boards are cheap -- most attempts
                // fail fast on the spec before any solve happens.
                for (int attempt = 0; attempt < 1200 && made < wanted && !cancelled; attempt++)
                {
                    throttle.Tick();

                    if ((attempt % 8) == 0 && EditorUtility.DisplayCancelableProgressBar(
                            "Building the Advanced " + size + "x" + size + " pack  -  " + kinds[k].Name,
                            made + "/" + wanted
                                + "  -  " + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                            0.45f))
                    { cancelled = true; break; }

                    GenerationSpec spec = SpecForStructural(size, kinds[k].Bridges, kinds[k].SharedGoals);
                    GeneratedLevel built = TryGenerateLevel(spec, rng, seen);

                    // Null means the spec could not be met. Uniqueness is REPORTED rather than
                    // guaranteed, so it is checked here -- the spec asks for it, and asking is not
                    // the same as getting it.
                    if (built == null) { continue; }
                    if (built.SolutionsFound != 1 || !built.SearchExhausted) { continue; }

                    // The spec pipeline predates solutionPairId and never fills it, so these boards
                    // arrived with a null answer and no hint could be given on them -- eight of a
                    // hundred, and invisible until the stored solution was verified against the
                    // solver. Filled here from the solve we just proved unique.
                    LevelData withSolution = built.Data;
                    if (!FillStoredSolution(ref withSolution, out int shortestPath)) { continue; }

                    // SpecForStructural asks for MinPathCells and does not get it -- the same gap
                    // the uniqueness check above exists to close, and for the same reason: asking
                    // is not getting. Measured on two shipped packs, both times on Shared
                    // Destination levels, where a pair's dot sits next to the shared cell: the
                    // 6x6 rebuild put a 2-cell path on L56 and L89, the pack before it on L78.
                    // A 2-cell link is two adjacent dots and one drag -- nothing to work out, and
                    // exactly what §6.35 identified as the reason the early packs felt unchallenging.
                    if (shortestPath < StructuralGates.MinPathCells) { continue; }

                    found.Add((withSolution, kinds[k].Name));
                    made++;
                }
            }
            return found;
        }

        /// <summary>
        /// The spec for a structural-mechanic board: one bridge or one shared destination, plain
        /// otherwise. Kept minimal on purpose -- these levels exist to show the mechanic, and
        /// anything else on the board competes with it for the player's attention.
        /// </summary>
        private static GenerationSpec SpecForStructural(int size, int bridges, int sharedGoals)
        {
            int cells = size * size;
            return new GenerationSpec
            {
                GridSize = size,
                MinColorCount = Mathf.Max(3, cells / 9),
                MaxColorCount = Mathf.Max(4, cells / 7),
                MinPathCells = StructuralGates.MinPathCells,
                Uniqueness = UniquenessPolicy.Require,
                RequireMechanicsNecessary = true,
                BridgeCount = bridges,
                SharedGoalCount = sharedGoals,
                MaxAttempts = 400
            };
        }

        /// <summary>
        /// Orders an Advanced pack: teach each rule briefly, then interleave and escalate.
        ///
        /// <b>Why not one global difficulty ramp, as Classic uses.</b> Classic has one rule, so a
        /// single ramp is the whole design. Advanced has several, and sorting purely by difficulty
        /// shuffles them: the player meets their first Checkpoint mid-pack with no explanation and
        /// never sees another for ten levels. Nothing teaches, and no rule stays long enough to
        /// develop a feel for.
        ///
        /// <b>The shape comes from two findings that agree.</b> Learning research on the contextual
        /// interference effect says interleaved practice gives worse in-the-moment performance but
        /// better long-term retention and transfer than blocked practice -- while initial BLOCKED
        /// practice still matters for acquiring the thing in the first place. Puzzle-design pacing
        /// says scale density back when introducing a mechanic and make each introduction a valley
        /// in the saw curve. Block to acquire, interleave to retain.
        ///
        /// So:
        ///   - a <b>warm-up</b> of blocked-cell boards, which need no teaching;
        ///   - a short <b>run</b> per rule -- three practice levels, stratified from that rule's own
        ///     range so the first is the easiest board carrying it. That opening level is the
        ///     introduction: at deficit 1 it usually carries a single cell of the rule, which is the
        ///     clearest form it takes, so a separate teaching slot earned nothing;
        ///   - <b>consolidation</b>, interleaved across every rule learned, still gentle;
        ///   - <b>escalation</b>, interleaved and drawn from deficit 2-3 so boards need three or
        ///     four load-bearing cells rather than one.
        ///
        /// <b>Interleaving is how the rules "mix".</b> Two rules cannot share a board -- measured
        /// three ways, 4 usable boards in 1600 attempts, because the second rule subsumes the first.
        /// But mixing them level to level is both achievable AND the form the retention research
        /// actually endorses, so the constraint and the good design agree.
        /// </summary>
        private static void ScheduleAndWriteAdvancedPack(
            List<(LevelData Data, string Mechanic, int Instances, int Deficit)> survivors,
            List<(LevelData Data, string Mechanic)> structural,
            string levelsFolder, string label, int count, CpuThrottle throttle,
            System.Diagnostics.Stopwatch total, ref bool cancelled, string gatherReport)
        {
            List<Entry> scored = new List<Entry>();

            for (int i = 0; i < survivors.Count && !cancelled; i++)
            {
                throttle.Tick();

                if ((i % 16) == 0 && EditorUtility.DisplayCancelableProgressBar(
                        "Building the " + label + " pack  -  scoring",
                        i + "/" + survivors.Count
                            + "  -  " + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                        0.5f + 0.4f * i / survivors.Count))
                { cancelled = true; break; }

                DifficultyModel.Profile p = DifficultyModel.Measure(survivors[i].Data);
                if (!p.Valid || !p.WellFormed) { continue; }

                scored.Add(new Entry
                {
                    Score = p.Score,
                    Data = survivors[i].Data,
                    Mechanic = survivors[i].Mechanic,
                    Instances = survivors[i].Instances,
                    Deficit = survivors[i].Deficit
                });
            }

            if (scored.Count < count)
            {
                Debug.LogError("Pack " + label + ": only " + scored.Count
                    + " well-formed boards for " + count + " levels.\n" + gatherReport);
                return;
            }

            // Slot budget. No dedicated teaching level: each run is practice only, drawn
            // stratified from that rule's own difficulty range, so its FIRST level is the easiest
            // board carrying the rule and introduces it anyway. An explicit teaching slot was
            // tried and removed as redundant with that.
            const int PracticePerRule = 3;

            List<string> rules = new List<string>();
            foreach (Entry e in scored)
            {
                if (e.Mechanic != "BlockedOnly" && !rules.Contains(e.Mechanic)) { rules.Add(e.Mechanic); }
            }
            rules.Sort((a, b) => MedianOf(scored, a).CompareTo(MedianOf(scored, b)));

            int warmUp = Mathf.Min(count / 25 + 2, CountOf(scored, "BlockedOnly"));
            int firstHalf = count / 2;

            // Runs shrink rather than overflow when the pack is too short to hold three levels of
            // every rule, but never below one: a rule the player never meets at all is worse than a
            // rule they meet once. Without this the budget overflowed and the WRITE truncated the
            // tail, silently dropping the escalation half of a small pack.
            int practicePerRule = PracticePerRule;
            while (practicePerRule > 1
                && rules.Count * practicePerRule > firstHalf - warmUp)
            {
                practicePerRule--;
            }

            int teachBlock = rules.Count * practicePerRule;
            int consolidation = Mathf.Max(0, firstHalf - warmUp - teachBlock);
            int escalation = count - firstHalf;

            HashSet<LevelData> used = new HashSet<LevelData>();
            List<Entry> ordered = new List<Entry>();
            StringBuilder plan = new StringBuilder();

            // The pack's own difficulty envelope: every well-formed board's score, ascending.
            // Blocks pick against THIS rather than against their own candidate pool's internal
            // range. That distinction is the whole fix: Stratify spreads from the easiest to the
            // hardest board IN THE LIST IT IS GIVEN, so a per-rule run whose easiest board is
            // already hard (AllowedForPairs measured 57..87 at 7x7) planted that rule's HARDEST
            // board in the first quarter of the pack -- above the escalation block's own floor,
            // and above the finale. Measured on the shipped 7x7 pack: L13, L22 and L25 all scored
            // 87 against L100's 83, with L54 at 36 (GAME_EXPANSION_PLAN §6.44b).
            List<float> envelope = new List<float>(scored.Count);
            for (int i = 0; i < scored.Count; i++) { envelope.Add(scored[i].Score); }
            envelope.Sort();

            // --- warm-up ------------------------------------------------------------------
            List<Entry> blocked = Where(scored, "BlockedOnly", 0, 9, used);
            foreach (Entry e in PickAlongRamp(blocked, warmUp, ordered.Count, count,
                                              envelope, used))
            {
                ordered.Add(e); used.Add(e.Data);
            }
            plan.Append("  warm-up: Blocked x").Append(warmUp).AppendLine();

            // --- one short run per rule ---------------------------------------------------
            for (int r = 0; r < rules.Count; r++)
            {
                string rule = rules[r];

                // Deficit 1-2 keeps the introduction gentle: at one colour down a board usually
                // needs a single cell of the rule, which is the clearest form it takes.
                List<Entry> practice = Where(scored, rule, 1, 2, used);
                List<Entry> chosen = PickAlongRamp(practice,
                    Mathf.Min(practicePerRule, practice.Count), ordered.Count, count,
                    envelope, used);
                foreach (Entry e in chosen) { ordered.Add(e); used.Add(e.Data); }

                plan.Append("  run ").Append(r + 1).Append(": ").Append(rule)
                    .Append("  x").Append(chosen.Count).Append(Range(chosen)).AppendLine();
            }

            // --- consolidation: every rule, interleaved, still gentle ----------------------
            List<Entry> gentle = WhereAnyRule(scored, 1, 2, used);
            // PickAlongRamp here too, for the reason measured on the first rebuild: Stratify
            // spread this block across 27..84 while it occupies positions 25-54, so it ended
            // at 84 immediately before escalation restarted at 43. Interleave then made it
            // visible as a zigzag -- it takes the easiest remaining board whose rule differs,
            // and when cheap and expensive rules alternate so do the scores. Interleave is
            // kept: the zigzag is only harmful because the range was wide, and drawing
            // against the pack ramp narrows it to what these positions should hold.
            List<Entry> consolidated = Interleave(PickAlongRamp(gentle,
                Mathf.Min(consolidation, gentle.Count), ordered.Count, count, envelope, used));
            foreach (Entry e in consolidated) { ordered.Add(e); used.Add(e.Data); }
            plan.Append("  consolidation: x").Append(consolidated.Count)
                .Append(Range(consolidated)).AppendLine();

            // --- escalation: deficit 2-3, so boards need several cells of their rule -------
            List<Entry> hard = WhereAnyRule(scored, 2, 3, used);
            List<Entry> escalated = Interleave(PickAlongRamp(hard,
                Mathf.Min(escalation, hard.Count), ordered.Count, count, envelope, used));
            foreach (Entry e in escalated) { ordered.Add(e); used.Add(e.Data); }
            plan.Append("  escalation: x").Append(escalated.Count)
                .Append(Range(escalated)).AppendLine();

            // Any shortfall is topped up from whatever is left, easiest first, so the pack is
            // always the requested length even when one bucket ran thin.
            if (ordered.Count < count)
            {
                List<Entry> spare = WhereAnyRule(scored, 0, 9, used);
                spare.Sort((x, y) => x.Score.CompareTo(y.Score));
                for (int i = 0; i < spare.Count && ordered.Count < count; i++)
                {
                    ordered.Add(spare[i]);
                    used.Add(spare[i].Data);
                }
                plan.Append("  topped up to ").Append(ordered.Count).AppendLine();
            }

            if (ordered.Count < count)
            {
                Debug.LogError("Pack " + label + ": schedule filled only " + ordered.Count
                    + " of " + count + " slots.\n" + gatherReport + "\n" + plan);
                return;
            }

            // Bridge and Shared Destination go in at authored positions. They carry no difficulty
            // score, so they cannot take part in the ramp -- spacing them evenly is the honest
            // placement: each is met early enough to be learned and seen again later, without
            // pretending to a difficulty nobody measured.
            if (structural != null && structural.Count > 0)
            {
                int spacing = Mathf.Max(1, count / (structural.Count + 1));
                for (int i = 0; i < structural.Count; i++)
                {
                    int slot = Mathf.Min(ordered.Count, (i + 1) * spacing);
                    ordered.Insert(slot, new Entry
                    {
                        Score = 0f,                     // unranked, and recorded as such
                        Data = structural[i].Data,
                        Mechanic = structural[i].Mechanic,
                        Instances = 1,
                        Deficit = 0
                    });
                }
                plan.Append("  unranked: ").Append(structural.Count)
                    .Append(" structural levels, every ").Append(spacing).AppendLine();
            }

            for (int i = 0; i < count; i++)
            {
                SaveLevelAsset(levelsFolder, i + 1, ordered[i].Data, ordered[i].Score);
            }

            total.Stop();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("Pack " + label + ": " + count + " levels written to " + levelsFolder
                + " in " + (total.ElapsedMilliseconds / 1000f).ToString("0.0") + "s"
                + (cancelled ? " (CANCELLED)" : "") + ".\n" + gatherReport + "\n"
                + "  scored " + survivors.Count + " -> " + scored.Count + " well-formed\n" + plan);
        }

        private sealed class Entry
        {
            public float Score;
            public LevelData Data;
            public string Mechanic;
            public int Instances;
            public int Deficit;
        }

        private static List<Entry> Where(List<Entry> all, string mechanic,
            int minDeficit, int maxDeficit, HashSet<LevelData> used)
        {
            List<Entry> found = new List<Entry>();
            foreach (Entry e in all)
            {
                if (e.Mechanic != mechanic) { continue; }
                if (e.Deficit < minDeficit || e.Deficit > maxDeficit) { continue; }
                if (used.Contains(e.Data)) { continue; }
                found.Add(e);
            }
            return found;
        }

        private static List<Entry> WhereAnyRule(List<Entry> all, int minDeficit, int maxDeficit,
            HashSet<LevelData> used)
        {
            List<Entry> found = new List<Entry>();
            foreach (Entry e in all)
            {
                if (e.Deficit < minDeficit || e.Deficit > maxDeficit) { continue; }
                if (used.Contains(e.Data)) { continue; }
                found.Add(e);
            }
            return found;
        }

        private static int CountOf(List<Entry> all, string mechanic)
        {
            int n = 0;
            foreach (Entry e in all) { if (e.Mechanic == mechanic) { n++; } }
            return n;
        }

        private static float MedianOf(List<Entry> all, string mechanic)
        {
            List<float> scores = new List<float>();
            foreach (Entry e in all) { if (e.Mechanic == mechanic) { scores.Add(e.Score); } }
            scores.Sort();
            return scores.Count == 0 ? 0f : scores[scores.Count / 2];
        }

        private static string Range(List<Entry> entries)
        {
            if (entries.Count == 0) { return string.Empty; }
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (Entry e in entries)
            {
                if (e.Score < lo) { lo = e.Score; }
                if (e.Score > hi) { hi = e.Score; }
            }
            return "  (" + lo.ToString("0") + ".." + hi.ToString("0") + ")";
        }

        /// <summary>
        /// Reorders a difficulty-sorted run so consecutive levels rarely repeat a rule, WITHOUT
        /// disturbing the ramp more than it has to: each step takes the easiest remaining board
        /// whose rule differs from the one just placed, and falls back to the easiest of any rule
        /// when that is all that is left.
        ///
        /// This is the "mixing" the pack can actually offer. Two rules on one board is not
        /// available, and interleaving between levels is what the retention research points at
        /// anyway -- the player has to retrieve the rule fresh each time instead of running a rote
        /// response from the level before.
        /// </summary>
        private static List<Entry> Interleave(List<Entry> ramp)
        {
            List<Entry> pool = new List<Entry>(ramp);
            pool.Sort((x, y) => x.Score.CompareTo(y.Score));

            List<Entry> result = new List<Entry>(pool.Count);
            string last = null;

            while (pool.Count > 0)
            {
                int pick = -1;
                for (int i = 0; i < pool.Count; i++)
                {
                    if (pool[i].Mechanic != last) { pick = i; break; }
                }
                if (pick < 0) { pick = 0; }      // only one rule left -- ramp wins over variety

                result.Add(pool[pick]);
                last = pool[pick].Mechanic;
                pool.RemoveAt(pick);
            }
            return result;
        }

        /// <summary>
        /// Pick <paramref name="count"/> boards from <paramref name="candidates"/> that sit where
        /// the PACK's ramp wants them, given the positions they will occupy.
        ///
        /// The difference from Stratify is the frame of reference, and it is the whole point.
        /// Stratify spreads evenly between the easiest and hardest entry in the list it is handed,
        /// which is right for a block drawn from every rule at once and wrong for one rule's
        /// practice run: it guarantees the run's last level is that rule's hardest board, wherever
        /// in the pack the run happens to sit. This asks instead what score the pack wants at
        /// position N and takes the nearest available board to it, so a rule with no easy material
        /// contributes its easiest rather than reaching for its hardest.
        ///
        /// Every block now draws against the ramp. Stratify was kept for consolidation and
        /// escalation at first, on the strength of their block MEANS looking monotone. The
        /// first rebuild showed that hid a 57-point zigzag: consolidation spanning 27..84
        /// across positions 25-54, ending at 84 immediately before escalation restarted at
        /// 43. Block means are not enough to judge a ramp -- read the per-level sequence.
        /// </summary>
        private static List<Entry> PickAlongRamp(List<Entry> candidates, int count,
            int startPosition, int packLength, List<float> envelope, HashSet<LevelData> used)
        {
            List<Entry> picked = new List<Entry>();
            if (count <= 0 || candidates == null || candidates.Count == 0) { return picked; }

            HashSet<LevelData> taken = new HashSet<LevelData>();
            for (int k = 0; k < count; k++)
            {
                float target = RampTargetAt(startPosition + k, packLength, envelope);

                Entry best = null;
                float bestGap = float.MaxValue;
                for (int j = 0; j < candidates.Count; j++)
                {
                    Entry c = candidates[j];
                    if (taken.Contains(c.Data) || used.Contains(c.Data)) { continue; }

                    float gap = Mathf.Abs(c.Score - target);
                    if (gap < bestGap) { bestGap = gap; best = c; }
                }

                if (best == null) { break; }     // this rule ran out of unused material
                taken.Add(best.Data);
                picked.Add(best);
            }
            return picked;
        }

        /// <summary>
        /// The score the pack's ramp wants at <paramref name="position"/>, read off the envelope of
        /// every well-formed board's score. Position is clamped, so a block that overruns the
        /// nominal pack length asks for the hardest end rather than walking off the array.
        /// </summary>
        private static float RampTargetAt(int position, int packLength, List<float> envelope)
        {
            if (envelope == null || envelope.Count == 0) { return 0f; }
            if (packLength <= 1) { return envelope[envelope.Count - 1]; }

            float f = Mathf.Clamp01(position / (float)(packLength - 1));
            int idx = Mathf.Clamp(Mathf.RoundToInt(f * (envelope.Count - 1)), 0, envelope.Count - 1);
            return envelope[idx];
        }

        /// <summary>
        /// Spread <paramref name="count"/> picks evenly between the easiest and hardest entry
        /// in <paramref name="entries"/> -- by that list's OWN range.
        ///
        /// NO LONGER CALLED. Every scheduling block now uses PickAlongRamp instead, because a
        /// candidate list's own range is the wrong frame of reference when the block occupies a
        /// known slice of the pack. Kept rather than deleted because the algorithm is still
        /// correct whenever the candidates ARE the whole population rather than a slice of it,
        /// and because DifficultyModelTests exercises a copy of it.
        /// </summary>
        private static List<Entry> Stratify(List<Entry> entries, int count)
        {
            List<Entry> sorted = new List<Entry>(entries);
            sorted.Sort((x, y) => x.Score.CompareTo(y.Score));

            if (count <= 0) { return new List<Entry>(); }
            if (count >= sorted.Count) { return sorted; }
            if (count == 1)
            {
                return new List<Entry> { sorted[sorted.Count - 1] };
            }

            float lo = sorted[0].Score;
            float hi = sorted[sorted.Count - 1].Score;
            bool[] taken = new bool[sorted.Count];
            List<Entry> picked = new List<Entry>(count);

            for (int i = 0; i < count; i++)
            {
                float target = lo + (hi - lo) * i / (count - 1);
                int best = -1;
                float bestGap = float.MaxValue;
                for (int j = 0; j < sorted.Count; j++)
                {
                    if (taken[j]) { continue; }
                    float gap = Mathf.Abs(sorted[j].Score - target);
                    if (gap < bestGap) { bestGap = gap; best = j; }
                }
                taken[best] = true;
                picked.Add(sorted[best]);
            }

            picked.Sort((x, y) => x.Score.CompareTo(y.Score));
            return picked;
        }

        /// <summary>The colour covering one cell in the intended solution, or 0.</summary>
        private static int OwnerOfCell(List<List<(int Row, int Col)>> paths,
            List<PairColorType> palette, int row, int col)
        {
            for (int i = 0; i < paths.Count; i++)
            {
                for (int j = 0; j < paths[i].Count; j++)
                {
                    if (paths[i][j].Row == row && paths[i][j].Col == col) { return (int)palette[i]; }
                }
            }
            return 0;
        }

        /// <summary>A colour from the palette other than the one or two named, or 0 if the board is
        /// too small to have one to spare.</summary>
        private static int PickAnotherColour(List<PairColorType> palette, System.Random rng,
            int avoidFirst, int avoidSecond)
        {
            List<int> options = new List<int>();
            for (int i = 0; i < palette.Count; i++)
            {
                int id = (int)palette[i];
                if (id != avoidFirst && id != avoidSecond) { options.Add(id); }
            }
            return options.Count == 0 ? 0 : options[rng.Next(options.Count)];
        }

        /// <summary>Which cells a LevelData leaves playable -- PlaceWalls needs the board shape,
        /// and by this point the only record of it is the level data itself.</summary>
        private static bool[,] UsableFrom(LevelData data)
        {
            int size = (int)data.gridSize;
            bool[,] usable = new bool[size, size];
            for (int r = 0; r < size; r++)
            {
                BlockType[] row = data.gridRows[r].blockType;
                for (int c = 0; c < size; c++)
                {
                    usable[r, c] = row == null || c >= row.Length || row[c] != BlockType.Blocked;
                }
            }
            return usable;
        }

        /// <summary>
        /// Records the board's own solution into <c>solutionPairId</c>, for boards built by a path
        /// that does not already do it.
        ///
        /// <b>One caveat, and it is inherent to the column rather than to this method.</b>
        /// <c>solutionPairId</c> holds one colour per cell, which is true of every mechanic except
        /// Bridge and Shared Destination -- a bridge cell carries two crossing paths, and a shared
        /// destination is the endpoint of two. At those cells the stored value names ONE of the two
        /// colours. That is incomplete rather than wrong: the colour named really does cover the
        /// cell, so a hint built on it cannot mislead, it can only under-report. Storing nothing at
        /// all was the alternative, and that leaves the hint system with no answer whatsoever.
        /// </summary>
        private static bool FillStoredSolution(ref LevelData data, out int shortestPath)
        {
            shortestPath = 0;

            Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
            try
            {
                PuzzleSolver.SolveResult solved = PuzzleSolver.Solve(grid, rows, cols,
                    new PuzzleSolver.SolverOptions(4000000, 1));
                if (solved.Status != PuzzleSolver.SolveStatus.Solved || solved.Solutions == null)
                {
                    return false;
                }

                int[,] map = new int[rows, cols];
                int shortest = int.MaxValue;
                for (int i = 0; i < solved.Solutions.Count; i++)
                {
                    List<(int Row, int Col)> cells = solved.Solutions[i].Cells;

                    // Reported from the SOLVER's paths, never from the column written below: at a
                    // bridge or a shared destination that column records only one of the two
                    // colours covering the cell, so counting it would under-report the other
                    // pair's length and reject boards that are actually fine.
                    if (cells.Count < shortest) { shortest = cells.Count; }

                    for (int j = 0; j < cells.Count; j++)
                    {
                        map[cells[j].Row, cells[j].Col] = solved.Solutions[i].PairId;
                    }
                }
                shortestPath = shortest == int.MaxValue ? 0 : shortest;

                for (int r = 0; r < rows; r++)
                {
                    int[] row = new int[cols];
                    for (int c = 0; c < cols; c++) { row[c] = map[r, c]; }
                    data.gridRows[r].solutionPairId = row;
                }
                return true;
            }
            finally { DestroyBlockGrid(grid); }
        }

        /// <summary>Walls actually on the board. Counted from the data rather than taken from the
        /// recipe, which holds the CEILING the climb was allowed to reach -- reporting that made
        /// every wall level look like it needed six.</summary>
        private static int CountWalls(LevelData data)
        {
            int size = (int)data.gridSize;
            int walls = 0;
            for (int r = 0; r < size; r++)
            {
                int[] row = data.gridRows[r].wallMask;
                if (row == null) { continue; }
                for (int c = 0; c < size && c < row.Length; c++)
                {
                    int mask = row[c];
                    while (mask != 0) { walls += mask & 1; mask >>= 1; }
                }
            }
            return walls;
        }

        private static int CountCellsOfType(LevelData data, BlockType type)
        {
            int size = (int)data.gridSize;
            int found = 0;
            for (int r = 0; r < size; r++)
            {
                BlockType[] row = data.gridRows[r].blockType;
                if (row == null) { continue; }
                for (int c = 0; c < size && c < row.Length; c++)
                {
                    if (row[c] == type) { found++; }
                }
            }
            return found;
        }

        private static string DescribeRecipeMix(Dictionary<string, int> keptByRecipe)
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in keptByRecipe)
            {
                parts.Add(kv.Key + "=" + kv.Value);
            }
            parts.Sort();
            return parts.Count == 0 ? "(none)" : string.Join(", ", parts);
        }

        /// <summary>
        /// The half of pack building that has nothing to do with how the boards were made: score
        /// cheaply, score the finalists properly, stratify, write.
        ///
        /// Shared by the Classic and Advanced builders because it is the part worth getting right
        /// once -- two-stage scoring, and stratified selection applied twice (to pick the finalists
        /// and again to pick the ramp). Only the GATHERING differs between the two modes.
        /// </summary>
        private static void ScoreAndWritePack(List<LevelData> survivors, string levelsFolder,
            string label, int count, int shortlistTarget, CpuThrottle throttle,
            System.Diagnostics.Stopwatch total, ref bool cancelled, string gatherReport)
        {
            List<(float Score, LevelData Data)> stageOne = new List<(float, LevelData)>();
            int stageOneCount = Mathf.Min(survivors.Count, shortlistTarget);

            for (int i = 0; i < stageOneCount && !cancelled; i++)
            {
                throttle.Tick();

                if ((i % 16) == 0 && EditorUtility.DisplayCancelableProgressBar(
                        "Building the " + label + " pack  -  stage 1 (cheap)",
                        i + "/" + stageOneCount
                            + "  -  " + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                        0.5f + 0.25f * i / stageOneCount))
                { cancelled = true; break; }

                DifficultyModel.Profile p = DifficultyModel.Measure(survivors[i], 14, 2000000, false);
                if (p.Valid && p.WellFormed) { stageOne.Add((p.Score, survivors[i])); }
            }

            if (stageOne.Count < count)
            {
                Debug.LogError("Pack " + label + ": only " + stageOne.Count
                    + " well-formed after stage one, needed " + count + ".\n" + gatherReport);
                return;
            }

            // Stratified rather than top-N even here: the finalists have to span the whole range,
            // because the easy end of the ramp is being chosen from them too.
            List<(float Score, LevelData Data)> finalists =
                SelectStratified(stageOne, Mathf.Min(stageOne.Count, count * 3));

            List<(float Score, LevelData Data)> scored = new List<(float, LevelData)>();
            for (int i = 0; i < finalists.Count && !cancelled; i++)
            {
                throttle.Tick();

                if ((i % 4) == 0 && EditorUtility.DisplayCancelableProgressBar(
                        "Building the " + label + " pack  -  stage 2 (full model)",
                        i + "/" + finalists.Count
                            + "  -  " + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                        0.75f + 0.25f * i / finalists.Count))
                { cancelled = true; break; }

                DifficultyModel.Profile p = DifficultyModel.Measure(finalists[i].Data);
                if (p.Valid && p.WellFormed) { scored.Add((p.Score, finalists[i].Data)); }
            }

            if (scored.Count < count)
            {
                Debug.LogError("Pack " + label + ": only " + scored.Count
                    + " survived stage two, needed " + count + ".\n" + gatherReport);
                return;
            }

            List<(float Score, LevelData Data)> chosen = SelectStratified(scored, count);
            for (int i = 0; i < chosen.Count; i++)
            {
                SaveLevelAsset(levelsFolder, i + 1, chosen[i].Data, chosen[i].Score);
            }

            total.Stop();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("Pack " + label + ": " + chosen.Count + " levels written to " + levelsFolder
                + " in " + (total.ElapsedMilliseconds / 1000f).ToString("0.0") + "s"
                + (cancelled ? " (CANCELLED)" : "") + ".\n" + gatherReport + "\n"
                + "  stage 1 scored " + stageOneCount + " -> " + stageOne.Count + " well-formed\n"
                + "  stage 2 scored " + finalists.Count + " -> " + scored.Count + " well-formed\n"
                + "  ramp: level 1 scores " + chosen[0].Score.ToString("0")
                + ", level " + chosen.Count + " scores " + chosen[chosen.Count - 1].Score.ToString("0"));
        }

        /// <summary>
        /// One mechanic recipe for an Advanced board: which extra rule to lay over the routing, and
        /// how much of it.
        ///
        /// <b>At most one type beyond Blocked, and that is a measured limit rather than taste.</b>
        /// §6.31 stacked three mechanics on 7x7 boards and found that NONE of 175 uniquely solvable
        /// boards had all three load-bearing, while 55% had none at all: past two types they start
        /// ruling out the same alternative routes, so removing any one alone changes nothing and
        /// each measures as unnecessary. The player is then tracking complexity they cannot feel.
        /// Multiple INSTANCES of a type are free -- several blocked cells, three arrows, an L of
        /// checkpoints -- because instances of one rule reinforce rather than mask each other.
        /// </summary>
        private struct MechanicRecipe
        {
            /// <summary>
            /// What this recipe is called, and the key the schedule groups runs by.
            ///
            /// A name rather than a <see cref="BlockType"/> because a WALL is not a cell type at
            /// all -- it is a blocked edge, stored in <c>wallMask</c>, and there is no
            /// <c>BlockType.Wall</c> to key on. Keying the schedule on cell type would have made
            /// walls unrepresentable without inventing a fake enum member.
            /// </summary>
            public string Name;

            /// <summary>
            /// The rule as the PLAYER sees it, which is what the schedule groups runs by.
            ///
            /// A two-colour forbidden cell is still a forbidden cell -- same border, same rule, one
            /// more colour named. Grouping by <see cref="Name"/> gave it a run of its own and split
            /// six rules into eight groups, so each got fewer levels and the variants read as
            /// separate mechanics. The variant stays distinct for the POOL cap, so both forms still
            /// get generated; it just does not earn its own teaching run.
            /// </summary>
            public string BaseName;

            /// <summary>Walls rather than a cell rule. Mutually exclusive with a non-Normal
            /// <see cref="Type"/>.</summary>
            public bool UsesWalls;

            /// <summary>
            /// Name TWO colours on each permission cell rather than one. Only meaningful for
            /// ForbiddenForPair and AllowedForPairs, which both read <c>secondPairId</c> through
            /// <c>Block.NamesPair</c> and draw a border slice per named colour.
            ///
            /// The two rules move in opposite directions when a second colour is added, which is
            /// why both variants are worth generating: a second FORBIDDEN colour refuses one more
            /// path and tightens the board, while a second PERMITTED colour admits one more and
            /// loosens it. So the pair covers boards the one-colour form over-constrains as well as
            /// boards it under-constrains.
            ///
            /// Two is the ceiling in the data model, not a choice made here -- past two the cell
            /// stops being readable at a glance and the honest form would be a bitmask naming every
            /// colour's status, which nothing else needs.
            /// </summary>
            public bool NameTwoColours;

            public BlockType Type;          // Normal means "blocked cells only", or walls

            /// <summary>A second rule laid on after the first, or Normal for none. Used only by the
            /// combination tier: the standard escalation in puzzle design is A, B, then A+B, and
            /// three types together is measured NOT to work here (§6.31: 0 of 175 boards had all
            /// three load-bearing).</summary>
            public BlockType SecondType;

            /// <summary>
            /// Add WALLS as the second constraint rather than a second cell rule.
            ///
            /// The earlier finding that two rules cannot share a board was measured only on pairs of
            /// CELL rules -- One-Way+Checkpoint, Arrow+Forbidden, Checkpoint+Forbidden,
            /// One-Way+Arrow -- all of which restrict entry to cells, which is why one kept making
            /// the other redundant. A wall blocks an EDGE, irrespective of colour or direction, so
            /// it constrains a different thing entirely and has far less reason to subsume, or be
            /// subsumed by, a cell rule.
            /// </summary>
            public bool SecondIsWalls;

            public int BlockedCells;

            /// <summary>Ceiling, not a target: the builder climbs until the board is unique and
            /// stops there, so a level never carries more of a rule than it needed.</summary>
            public int Instances;

            /// <summary>
            /// Where the climb STARTS. Two for every rule: one cell of anything reads as a quirk of
            /// that particular board rather than a rule the player is being taught. Caught in play
            /// on single-arrow levels, and it is the same argument that first applied to Checkpoint
            /// and One-Way -- it just was not carried across until the levels were played.
            ///
            /// This does not pad the board, because it does not bypass the necessity gate:
            /// <c>AllCellsOfTypeAreNecessary</c> requires EVERY cell of the type to be individually
            /// load-bearing, so a board where one cell would have sufficed is rejected as decorative
            /// rather than shipped with a spare. Raising the floor selects for boards that genuinely
            /// need two; it cannot invent them.
            /// </summary>
            public int MinInstances;

            /// <summary>How many colours short of uniqueness to start. 1 leaves so little ambiguity
            /// that a single mechanic cell almost always pins the board -- which is why teaching
            /// levels come out at one cell, and equally why a SECOND rule has nothing left to do.
            /// Raising it is the lever for both multi-instance and mixed levels.</summary>
            public int ColourDeficit;

            public MechanicRecipe(BlockType type, int blockedCells, int instances)
            {
                Type = type; SecondType = BlockType.Normal; UsesWalls = false; NameTwoColours = false;
                SecondIsWalls = false;
                MinInstances = type == BlockType.Normal ? 1 : 2;
                Name = type == BlockType.Normal ? "BlockedOnly" : type.ToString();
                BaseName = Name;
                BlockedCells = blockedCells; Instances = instances; ColourDeficit = 1;
            }

            /// <summary>A permission recipe naming two colours instead of one.</summary>
            public static MechanicRecipe TwoColour(BlockType type, int blockedCells, int instances,
                int deficit)
            {
                MechanicRecipe recipe = new MechanicRecipe(type, blockedCells, instances);
                recipe.Name = type + "2";
                recipe.BaseName = type.ToString();
                recipe.NameTwoColours = true;
                recipe.MinInstances = 2;
                recipe.ColourDeficit = deficit;
                return recipe;
            }

            /// <summary>A wall recipe: blocked EDGES rather than a cell rule.</summary>
            public static MechanicRecipe Walls(int blockedCells, int instances, int deficit)
            {
                MechanicRecipe recipe = new MechanicRecipe(BlockType.Normal, blockedCells, instances);
                recipe.Name = "Wall";
                recipe.BaseName = "Wall";
                recipe.UsesWalls = true;

                // Three, not two, because PlaceWalls grows a CONNECTED barrier -- walls joining at
                // a shared lattice corner, so they read as one obstacle rather than scattered stubs
                // (§6.17). Two edges can only ever make an L; three is where a T or a longer run
                // becomes possible. A single wall cannot form a barrier at all, which is why the
                // pack that shipped with one wall on most of its wall levels read as noise.
                recipe.MinInstances = 3;
                recipe.ColourDeficit = deficit;
                return recipe;
            }

            public MechanicRecipe(BlockType type, BlockType secondType, int instances, int deficit)
            {
                Type = type; SecondType = secondType; UsesWalls = false; NameTwoColours = false;
                SecondIsWalls = false;
                MinInstances = 1;
                Name = type.ToString();
                BaseName = Name;
                BlockedCells = 0; Instances = instances; ColourDeficit = deficit;
            }
        }

        /// <summary>
        /// Builds an Advanced pack: same size-based structure and the same scoring as Classic, with
        /// one mechanic laid over each board.
        ///
        /// <b>Why by size and not by mechanic.</b> Difficulty in this game comes from board size and
        /// colour ratio -- that is what §6.35-6.38 established and play confirmed. Mechanics have
        /// never once moved it. A pack per mechanic would also be unrankable for the two most
        /// distinctive ones: <see cref="HumanSolver.CanRate"/> refuses Bridge and Shared Destination
        /// on structural grounds, and the blend takes 77% of its score from that solver. So the
        /// packs are built on the axis that works, and mechanics supply texture.
        ///
        /// <b>Bridge and Shared Destination therefore never appear here</b>, and that is deliberate
        /// rather than an oversight: an unrateable board cannot pass WellFormed, so selection would
        /// drop it anyway. Both live in the hand-built Advanced 1-45 ladder, which teaches each
        /// mechanic in turn and is not ranked.
        ///
        /// Every mechanic must EARN its place. A candidate is kept only if stripping the mechanic
        /// costs the board its unique solution -- otherwise it is a plain board wearing a costume,
        /// which is what §6.31 found most mechanic boards actually are.
        /// </summary>
        private static void BuildAdvancedPack(int size, int count, int shortlistPerLevel,
            int poolTarget, int cellsPerColour)
        {
            string levelsFolder = "Assets/Resources/Levels/Advanced/" + size + "x" + size;
            int cells = size * size;
            int holes = Mathf.Max(2, cells / 12);

            // Blocked cells appear in most recipes because they are the least intrusive rule -- they
            // constrain routing without anything to remember. The rest appear alone or alongside
            // them, never two together.
            // Each rule appears at every deficit, because the deficit is what decides how many
            // cells the board ends up needing -- 1 gives mostly single-cell boards (the teaching
            // shape), 3 gives mostly four-cell ones (the escalation shape). Generating all three
            // up front means the schedule can draw whichever it needs without a second run.
            List<MechanicRecipe> recipeList = new List<MechanicRecipe>();
            BlockType[] rules =
            {
                BlockType.OneWay, BlockType.Arrow, BlockType.ForbiddenForPair,
                BlockType.AllowedForPairs, BlockType.Checkpoint
            };

            recipeList.Add(new MechanicRecipe(BlockType.Normal, holes, 0));
            for (int d = 1; d <= 3; d++)
            {
                for (int i = 0; i < rules.Length; i++)
                {
                    // Blocked-plus-rule boards are entered TWICE against the bare form's once.
                    // Holes remove cells before the partition is even built, so those boards are
                    // markedly harder to pin and lose the pool race badly: at equal weight only 4
                    // of 100 shipped levels carried both, though half the recipes asked for it.
                    // Weighting the attempts is the cheapest correction -- the pool cap still stops
                    // any one recipe running away with the pack.
                    MechanicRecipe withHoles = new MechanicRecipe(rules[i], holes, InstanceCeilingFor(size));
                    withHoles.ColourDeficit = d;
                    recipeList.Add(withHoles);
                    recipeList.Add(withHoles);

                    MechanicRecipe bare = new MechanicRecipe(rules[i], 0, InstanceCeilingFor(size));
                    bare.ColourDeficit = d;
                    recipeList.Add(bare);
                }

                // Walls are edges, not cells, so they get their own recipe rather than a place in
                // the rules array. PlaceWalls only ever picks edges the solution does not cross, so
                // a wall can constrain the board without contradicting the intended paths.
                recipeList.Add(MechanicRecipe.Walls(holes, 6, d));
                recipeList.Add(MechanicRecipe.Walls(0, 6, d));

                // Both permission rules can name a second colour, which the one-colour form was
                // leaving unused. They pull in opposite directions -- a second forbidden colour
                // tightens, a second permitted colour loosens -- so both are generated.
                recipeList.Add(MechanicRecipe.TwoColour(BlockType.ForbiddenForPair, 0, 6, d));
                recipeList.Add(MechanicRecipe.TwoColour(BlockType.AllowedForPairs, 0, 6, d));
            }
            MechanicRecipe[] recipes = recipeList.ToArray();

            EnsureLevelFolder(levelsFolder);
            EditorUtility.ClearProgressBar();

            HashSet<string> seen = new HashSet<string>();
            System.Random rng = new System.Random(20260915 + size);
            System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();
            CpuThrottle throttle = new CpuThrottle(GenerationDutyCycle);
            bool cancelled = false;

            // Blocked-only boards succeed far more often than mechanic ones -- holes constrain
            // the grid before it is even partitioned, so they never fight the uniqueness proof. Left
            // uncapped they took 50% of the pool and three of every five written levels, which is a
            // Classic pack with holes rather than an Advanced one. The cap is on the POOL, so the
            // difficulty ramp still chooses freely from what remains.
            int blockedOnlyCap = Mathf.Max(1, poolTarget / 4);

            // And a ceiling per rule, for the same reason at the other end. Checkpoint boards are
            // much easier to construct than the rest -- measured, 94 of 260 against AllowedForPairs'
            // 8 -- so without a cap the common rules crowd out the rare ones and their runs starve
            // while Checkpoint has five times the slots it needs. Capping the plentiful ones makes
            // gathering keep hunting for the scarce ones instead of stopping early on an easy mix.
            int perMechanicCap = Mathf.Max(1, poolTarget / 12);
            Dictionary<string, int> keptPerMechanic = new Dictionary<string, int>();

            List<(LevelData Data, string Mechanic, int Instances, int Deficit)> survivors =
                new List<(LevelData, string, int, int)>();
            Dictionary<string, int> keptByRecipe = new Dictionary<string, int>();
            int blockedOnlyKept = 0;
            int generated = 0, duplicates = 0, unsound = 0, notUnique = 0, decorative = 0;
            int shortlistTarget = count * shortlistPerLevel;

            // Raised from Classic's 200,000 (BuildSizePack, untouched) specifically for Advanced.
            // The figure is now MEASURED end to end rather than extrapolated from a probe: the
            // 1,000,000-cap run (GAME_EXPANSION_PLAN §6.44b) returned 463 survivors, i.e. ~0.046%
            // per attempt -- 3.6x below the 0.167% the colour-ratio sweep predicted off 5 kept
            // boards. At that real rate poolTarget=900 needs ~1,940,000 attempts, so 2,000,000
            // would carry essentially no margin, and the marginal rate only worsens as the pool
            // fills and canonical dedup begins rejecting (that run reported 0 duplicates at 463 --
            // dedup had not started to bite yet).
            //
            // 3,000,000 is deliberate headroom, not a target. The loop stops the moment poolTarget
            // is reached, so the cap only sets the WORST case: ~4.1h per million attempts as
            // measured, hence ~12h if the pool never fills. Correctness over time is the standing
            // instruction here (§6.44 step 1), and a cap that halts the gather short of the pool
            // the scheduling logic is designed around is the exact failure this already hit once.
            const int advancedMaxAttempts = 3000000;

            try
            {
                for (int attempt = 0; attempt < advancedMaxAttempts && survivors.Count < poolTarget; attempt++)
                {
                    throttle.Tick();

                    if ((attempt % 8) == 0 && EditorUtility.DisplayCancelableProgressBar(
                            "Building the Advanced " + size + "x" + size + " pack  -  gathering",
                            "kept " + survivors.Count + "/" + poolTarget
                                + "  -  " + unsound + " unsound, " + duplicates + " dupes, "
                                + decorative + " decorative"
                                + "  -  " + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                            0.5f * survivors.Count / poolTarget))
                    { cancelled = true; break; }

                    MechanicRecipe recipe = recipes[attempt % recipes.Length];
                    if (recipe.Instances == 0 && blockedOnlyKept >= blockedOnlyCap) { continue; }
                    // Capped per rule AND per deficit: a rule that is plentiful at deficit 1 must
                    // not fill the pool and leave the escalation half with nothing at deficit 3.
                    // Blocked-plus-rule and bare boards are capped SEPARATELY, so weighting the
                    // attempts above cannot simply be undone by one form filling the other's cap.
                    string bucket = recipe.Name + "@" + recipe.ColourDeficit
                                  + (recipe.BlockedCells > 0 ? "+holes" : string.Empty);
                    if (recipe.Instances > 0
                        && keptPerMechanic.TryGetValue(bucket, out int already)
                        && already >= perMechanicCap)
                    {
                        continue;
                    }

                    bool[,] usable = PlaceBlockedCells(size, recipe.BlockedCells, true, rng);
                    int usableCount = 0;
                    for (int r = 0; r < size; r++)
                    {
                        for (int c = 0; c < size; c++) { if (usable[r, c]) { usableCount++; } }
                    }

                    int colours = Mathf.Max(3, usableCount / cellsPerColour)
                                + (attempt % ColourSweepWidth);
                    if (colours > MaxDistinctColors) { continue; }

                    LevelData data;

                    if (recipe.Instances == 0)
                    {
                        // Blocked-only: the holes shape the board BEFORE it is partitioned, so they
                        // are part of how uniqueness is reached and refinement is the right tool.
                        if (!TryGenerateUniqueByRefinement(size, usable, usableCount, colours,
                                MaxDistinctColors, 2000000, 3, rng,
                                out data, out int finalColours, out int splits, colours))
                        {
                            continue;
                        }
                    }
                    else if (!TryBuildMechanicDependentBoard(size, usable, usableCount, colours,
                                 recipe, rng, out data))
                    {
                        continue;
                    }
                    generated++;

                    Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
                    bool keep = false;
                    try
                    {
                        string key = LevelCanonicalizer.ComputeCanonicalKey(grid, rows, cols);
                        if (!seen.Add(key)) { duplicates++; continue; }

                        PuzzleSolver.SolveResult solved = PuzzleSolver.Solve(grid, rows, cols,
                            new PuzzleSolver.SolverOptions(8000000, 2));
                        bool unique = solved.Status == PuzzleSolver.SolveStatus.Solved
                            && solved.SolutionsFound == 1 && solved.SearchExhausted;
                        if (!unique) { notUnique++; continue; }

                        if (!StoredSolutionMatchesSolver(data, solved, rows, cols)) { notUnique++; continue; }

                        int liveCells = 0;
                        for (int r = 0; r < rows; r++)
                        {
                            for (int c = 0; c < cols; c++)
                            {
                                if (grid[r, c] != null && grid[r, c].BlockType != BlockType.Blocked)
                                {
                                    liveCells++;
                                }
                            }
                        }
                        if (!StructuralGates.Evaluate(solved, liveCells).Passed) { unsound++; continue; }

                        // Every rule on the board has to be load-bearing, or the level is a plain
                        // board wearing a costume -- §6.31 measured 55% of three-mechanic boards
                        // that way.
                        if (recipe.Instances > 0 && recipe.UsesWalls
                            && !AllWallsAreNecessary(grid, rows, cols))
                        {
                            decorative++;
                            continue;
                        }
                        if (recipe.Instances > 0 && !recipe.UsesWalls
                            && !AllCellsOfTypeAreNecessary(grid, rows, cols, recipe.Type))
                        {
                            decorative++;
                            continue;
                        }
                        if (recipe.SecondType != BlockType.Normal
                            && !AllCellsOfTypeAreNecessary(grid, rows, cols, recipe.SecondType))
                        {
                            decorative++;
                            continue;
                        }
                        if (recipe.BlockedCells > 0
                            && !AllCellsOfTypeAreNecessary(grid, rows, cols, BlockType.Blocked))
                        {
                            decorative++;
                            continue;
                        }

                        keep = true;
                    }
                    finally { DestroyBlockGrid(grid); }

                    if (keep)
                    {
                        survivors.Add((data, recipe.BaseName,
                            recipe.Instances == 0
                                ? 0
                                : (recipe.UsesWalls ? CountWalls(data) : CountCellsOfType(data, recipe.Type)),
                            recipe.ColourDeficit));
                        if (recipe.Instances == 0) { blockedOnlyKept++; }
                        else
                        {
                            keptPerMechanic.TryGetValue(bucket, out int had);
                            keptPerMechanic[bucket] = had + 1;
                        }

                        // Tallied by what the board actually ENDED UP with, not what the recipe
                        // asked for: the builder stops as soon as the board is pinned, so the count
                        // it settled on is the interesting number.
                        string name = recipe.Instances == 0
                            ? "BlockedOnly"
                            : recipe.Name + "x" + (recipe.UsesWalls
                                ? CountWalls(data)
                                : CountCellsOfType(data, recipe.Type));
                        keptByRecipe[name] = keptByRecipe.ContainsKey(name) ? keptByRecipe[name] + 1 : 1;
                    }
                }

                // Bridge and Shared Destination are gathered separately, by the older spec-based
                // pipeline. They are not overlays laid on a finished partition -- they change the
                // partition's SHAPE, splitting one cell into two lanes or making one cell the
                // endpoint of two paths -- so the deficit-and-climb construction above cannot
                // express them. That pipeline already builds and verifies them (it produced the
                // shipped levels 36-40 and 46-50, including the EveryBridgeCarriesTwoColours check
                // that caught the L40 self-crossing bug), so it is reused rather than reimplemented.
                //
                // They arrive UNRANKED: HumanSolver.CanRate refuses both, so DifficultyModel cannot
                // score them and they can never pass WellFormed. The schedule places them at
                // authored positions instead of in the ramp -- honest about a known limit rather
                // than inventing a number for them.
                List<(LevelData Data, string Mechanic)> structural =
                    GatherStructuralMechanics(size, count, seen, rng, throttle, total, ref cancelled);

                if (survivors.Count < count)
                {
                    Debug.LogError("Advanced pack " + size + "x" + size + ": only " + survivors.Count
                        + " boards for " + count + " levels. Generated " + generated + ", "
                        + duplicates + " duplicates, " + unsound + " unsound, "
                        + decorative + " with a decorative mechanic.");
                    return;
                }

                ScheduleAndWriteAdvancedPack(survivors, structural, levelsFolder,
                    "Advanced " + size + "x" + size, count, throttle, total, ref cancelled,
                    "  generated " + generated + ", " + duplicates + " duplicates, "
                        + unsound + " unsound, " + notUnique + " not unique, "
                        + decorative + " decorative, " + survivors.Count + " kept\n"
                        + "  kept by mechanic: " + DescribeRecipeMix(keptByRecipe));
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        /// <summary>
        /// Builds a board whose mechanic is LOAD-BEARING, by starting from one that is deliberately
        /// under-constrained and letting the mechanic supply what is missing.
        ///
        /// <b>Why the obvious order does not work.</b> The first version generated a uniquely
        /// solvable board and then laid a mechanic over it. A mechanic added to a board that is
        /// already unique can never matter: uniqueness was reached without it, so stripping it
        /// changes nothing and the necessity check throws it out. Measured, that rejected <b>633 of
        /// 1139</b> candidates as decorative, and the only survivors were blocked-cell boards --
        /// where the holes shape the grid before it is partitioned and so genuinely participate.
        /// An Advanced pack built that way would have been Classic with holes.
        ///
        /// So: partition with FEWER colours than uniqueness needs, confirm the board really is
        /// ambiguous, then place the mechanic on the intended solution and require that it becomes
        /// unique. Necessity then holds by construction rather than by luck, and is still verified.
        ///
        /// The mechanic goes on the INTENDED solution, not on whichever one the solver happens to
        /// return first -- with several solutions available those are different boards. The check
        /// that the surviving unique solution is the intended one is
        /// <see cref="StoredSolutionMatchesSolver"/>, which works because
        /// <see cref="BuildPlainLevelData"/> stores the partition it was given.
        /// </summary>
        private static bool TryBuildMechanicDependentBoard(int size, bool[,] usable, int usableCount,
            int colours, MechanicRecipe recipe, System.Random rng, out LevelData data)
        {
            data = default;

            // Short of what this density would normally need, so the board is ambiguous and the
            // mechanic has something to do. How short is the whole lever: at one colour down a
            // single cell usually pins it, at three there is real work left for several.
            int deficit = Mathf.Max(2, colours - Mathf.Max(1, recipe.ColourDeficit));

            // Unbalanced: StructuralGates demands a real spread between the shortest and longest
            // path, and a probe found "too uniform" as the dominant rejection reason regardless of
            // colour ratio. See TryGeneratePathPartitionUnbalanced's own doc comment.
            List<List<(int Row, int Col)>> paths =
                TryGeneratePathPartitionUnbalanced(size, usable, usableCount, deficit, rng);
            if (paths == null) { return false; }

            LevelData candidate = BuildPlainLevelData(size, usable, paths, rng);

            Block[,] grid = BuildBlockGrid(candidate, out int rows, out int cols);
            try
            {
                PuzzleSolver.SolveResult before = PuzzleSolver.Solve(grid, rows, cols,
                    new PuzzleSolver.SolverOptions(2000000, 2));

                // Unsolvable is a dead end; already unique means the mechanic would be decorative,
                // which is the whole thing this method exists to avoid.
                if (before.Status != PuzzleSolver.SolveStatus.Solved) { return false; }
                if (before.SolutionsFound < 2) { return false; }
            }
            finally { DestroyBlockGrid(grid); }

            // Add mechanic cells one at a time until the board is pinned, exactly as refinement
            // adds colours. Guessing a fixed count was the previous version's failure: two or three
            // cells often fail to collapse an ambiguous board back to one solution, and 513 of 867
            // candidates were thrown out as still-not-unique. Climbing finds the MINIMUM that
            // works, which is also the answer to "do not overdo it" -- no board carries a rule it
            // did not need.
            for (int instances = Mathf.Max(1, recipe.MinInstances);
                 instances <= recipe.Instances;
                 instances++)
            {
                LevelData trial = CloneLevelData(candidate);
                MechanicRecipe step = recipe;
                step.Instances = instances;

                HashSet<(int Row, int Col)> used = new HashSet<(int, int)>();
                if (!LayMechanicOnPaths(ref trial, paths, step, rng, used)) { continue; }

                PinState state = Pinned(trial, paths);
                if (state == PinState.Broken) { continue; }

                if (state == PinState.Unique)
                {
                    // One rule was enough. For a combination recipe that is a failure, not a
                    // success: the second rule would be decorative, which is the thing this whole
                    // construction exists to prevent.
                    if (recipe.SecondType != BlockType.Normal) { continue; }
                    data = trial;
                    return true;
                }

                if (recipe.SecondType == BlockType.Normal && !recipe.SecondIsWalls) { continue; }

                // Still ambiguous, so the second rule has something left to do. A+B, with both
                // load-bearing by construction rather than by luck.
                for (int second = 1; second <= recipe.Instances; second++)
                {
                    LevelData combo = CloneLevelData(trial);
                    MechanicRecipe secondStep = recipe;
                    if (recipe.SecondIsWalls)
                    {
                        secondStep.UsesWalls = true;
                    }
                    else
                    {
                        secondStep.Type = recipe.SecondType;
                    }
                    secondStep.Instances = second;

                    HashSet<(int Row, int Col)> alsoUsed = new HashSet<(int, int)>(used);
                    if (!LayMechanicOnPaths(ref combo, paths, secondStep, rng, alsoUsed)) { continue; }

                    if (Pinned(combo, paths) == PinState.Unique) { data = combo; return true; }
                }
            }

            return false;
        }

        private enum PinState { Broken, Ambiguous, Unique }

        /// <summary>
        /// Whether <paramref name="data"/> now has exactly one solution, and it is the intended one.
        /// Those are two different claims when the board started out ambiguous, and only the pair of
        /// them means the mechanic pinned the puzzle the generator meant to build.
        /// </summary>
        private static PinState Pinned(LevelData data, List<List<(int Row, int Col)>> paths)
        {
            Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
            try
            {
                PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, rows, cols,
                    new PuzzleSolver.SolverOptions(2000000, 2));

                if (result.Status != PuzzleSolver.SolveStatus.Solved) { return PinState.Broken; }
                if (result.SolutionsFound > 1 || !result.SearchExhausted) { return PinState.Ambiguous; }
                return StoredSolutionMatchesSolver(data, result, rows, cols)
                    ? PinState.Unique
                    : PinState.Broken;
            }
            finally { DestroyBlockGrid(grid); }
        }

        /// <summary>A copy whose per-cell columns can be written without touching the original --
        /// needed because each mechanic-count trial must start from the same clean board.</summary>
        private static LevelData CloneLevelData(LevelData source)
        {
            int size = (int)source.gridSize;
            LevelData copy = source;
            copy.gridRows = new GridRow[size];

            for (int r = 0; r < size; r++)
            {
                GridRow src = source.gridRows[r];
                copy.gridRows[r] = new GridRow
                {
                    coloum = (PairColorType[])src.coloum?.Clone(),
                    pairId = (int[])src.pairId?.Clone(),
                    blockType = (BlockType[])src.blockType?.Clone(),
                    wallMask = (int[])src.wallMask?.Clone(),
                    requiredEntryDirection = (Direction[])src.requiredEntryDirection?.Clone(),
                    forcedExitDirection = (Direction[])src.forcedExitDirection?.Clone(),
                    secondPairId = (int[])src.secondPairId?.Clone(),
                    thirdPairId = (int[])src.thirdPairId?.Clone(),
                    fourthPairId = (int[])src.fourthPairId?.Clone(),
                    solutionPairId = (int[])src.solutionPairId?.Clone()
                };
            }
            return copy;
        }

        /// <summary>
        /// Writes one mechanic into <paramref name="data"/>, placed against <paramref name="paths"/>
        /// -- the intended solution.
        /// </summary>
        private static bool LayMechanicOnPaths(ref LevelData data,
            List<List<(int Row, int Col)>> paths, MechanicRecipe recipe, System.Random rng,
            HashSet<(int Row, int Col)> reserved = null)
        {
            List<PairColorType> palette = new List<PairColorType>();
            for (int i = 0; i < paths.Count; i++)
            {
                (int Row, int Col) head = paths[i][0];
                palette.Add(data.gridRows[head.Row].coloum[head.Col]);
            }

            // A dot already means something; overwriting one would delete a pair. `reserved`
            // carries cells a previous mechanic already claimed -- BlockType is one per cell, so a
            // second mechanic landing on the first would silently erase it.
            HashSet<(int Row, int Col)> excluded = new HashSet<(int, int)>();
            for (int i = 0; i < paths.Count; i++)
            {
                excluded.Add(paths[i][0]);
                excluded.Add(paths[i][paths[i].Count - 1]);
            }
            if (reserved != null) { excluded.UnionWith(reserved); }

            // Which way round the solver will actually walk each path. A One-Way's entry and an
            // Arrow's exit are directional, so they must be encoded relative to the direction the
            // SOLVER takes -- it starts each pair from the row-major-first endpoint, which is array
            // index 0 only sometimes. Passing an empty dictionary here threw KeyNotFoundException on
            // the first cell; had the helpers defaulted instead, the levels would have been quietly
            // wrong half the time.
            Dictionary<(int Row, int Col), bool> reversedByCell = new Dictionary<(int, int), bool>();
            for (int i = 0; i < paths.Count; i++)
            {
                bool reversed = !IsRowMajorBefore(paths[i][0], paths[i][paths[i].Count - 1]);
                for (int j = 0; j < paths[i].Count; j++)
                {
                    reversedByCell[paths[i][j]] = reversed;
                }
            }

            if (recipe.UsesWalls)
            {
                List<(int Row, int Col, Direction Dir)> walls =
                    PlaceWalls(UsableFrom(data), (int)data.gridSize, paths, recipe.Instances, rng);
                if (walls.Count == 0) { return false; }

                foreach ((int Row, int Col, Direction Dir) wall in walls)
                {
                    data.gridRows[wall.Row].wallMask[wall.Col] |= WallBit(wall.Dir);
                    reserved?.Add((wall.Row, wall.Col));
                }
                return true;
            }

            switch (recipe.Type)
            {
                case BlockType.OneWay:
                {
                    var placed = PlaceOneWayCells(paths, excluded, reversedByCell, recipe.Instances, rng);
                    if (placed.Count == 0) { return false; }
                    foreach (var cell in placed)
                    {
                        data.gridRows[cell.Row].blockType[cell.Col] = BlockType.OneWay;
                        data.gridRows[cell.Row].requiredEntryDirection[cell.Col] = cell.EntryDir;
                        reserved?.Add((cell.Row, cell.Col));
                    }
                    return true;
                }
                case BlockType.Arrow:
                {
                    var placed = PlaceArrowCells(paths, excluded, reversedByCell, recipe.Instances, rng);
                    if (placed.Count == 0) { return false; }
                    foreach (var cell in placed)
                    {
                        data.gridRows[cell.Row].blockType[cell.Col] = BlockType.Arrow;
                        data.gridRows[cell.Row].forcedExitDirection[cell.Col] = cell.ExitDir;
                        reserved?.Add((cell.Row, cell.Col));
                    }
                    return true;
                }
                case BlockType.ForbiddenForPair:
                {
                    var placed = PlaceForbiddenCells(paths, excluded, palette, recipe.Instances, rng);
                    if (placed.Count == 0) { return false; }
                    foreach (var cell in placed)
                    {
                        data.gridRows[cell.Row].blockType[cell.Col] = BlockType.ForbiddenForPair;
                        data.gridRows[cell.Row].pairId[cell.Col] = cell.ForbiddenPairId;

                        if (recipe.NameTwoColours)
                        {
                            // Any colour except the one that actually covers this cell in the
                            // intended solution -- forbidding that would make the level unsolvable.
                            int second = PickAnotherColour(palette, rng,
                                cell.ForbiddenPairId, OwnerOfCell(paths, palette, cell.Row, cell.Col));
                            data.gridRows[cell.Row].secondPairId[cell.Col] = second;
                        }
                        reserved?.Add((cell.Row, cell.Col));
                    }
                    return true;
                }
                case BlockType.AllowedForPairs:
                {
                    var placed = PlacePermittedCells(paths, excluded, palette, recipe.Instances, rng);
                    if (placed.Count == 0) { return false; }
                    foreach (var cell in placed)
                    {
                        data.gridRows[cell.Row].blockType[cell.Col] = BlockType.AllowedForPairs;
                        data.gridRows[cell.Row].pairId[cell.Col] = cell.AllowedPairId;

                        if (recipe.NameTwoColours)
                        {
                            // The cell's own colour is already named, so any other will do -- this
                            // deliberately LOOSENS the rule, admitting a second path that the
                            // one-colour form refused.
                            int second = PickAnotherColour(palette, rng, cell.AllowedPairId, 0);
                            data.gridRows[cell.Row].secondPairId[cell.Col] = second;
                        }
                        reserved?.Add((cell.Row, cell.Col));
                    }
                    return true;
                }
                case BlockType.Checkpoint:
                {
                    var placed = PlaceCheckpointCells(paths, excluded, palette, recipe.Instances, rng);
                    if (placed.Count == 0) { return false; }
                    foreach (var cell in placed)
                    {
                        data.gridRows[cell.Row].blockType[cell.Col] = BlockType.Checkpoint;
                        data.gridRows[cell.Row].pairId[cell.Col] = cell.CheckpointPairId;
                        reserved?.Add((cell.Row, cell.Col));
                    }
                    return true;
                }
                default:
                    return true;
            }
        }

        [MenuItem("FreeFlow/Level Generator/Advanced/Build 6x6 pack (100)")]
        public static void BuildAdvancedPack6x6() { BuildAdvancedPack(6, 100, 10, 900, 9); }

        // cellsPerColour=7, not the originally-wired 10: the 10 call hits the pipeline's
        // 200,000-attempt cap at a 0.023% yield and fails outright (see GAME_EXPANSION_PLAN
        // §6.44). 7 was MEASURED, not guessed -- a colour-ratio sweep after fixing a
        // length-equalising bias in the shared partition builder (also §6.44) found it the best
        // combination of yield (~3.3h estimated to a 900-board pool) and a mean path length close
        // to Classic's own measured 7x7 reference. Still a small-sample estimate; re-measure with
        // ProbeColourRatioSweep7x7 first if this configuration is ever revisited.
        [MenuItem("FreeFlow/Level Generator/Advanced/Build 7x7 pack (100)")]
        public static void BuildAdvancedPack7x7() { BuildAdvancedPack(7, 100, 10, 900, 7); }

        /// <summary>
        /// Does a TWO-mechanic board exist at a workable rate? The standard escalation in puzzle
        /// design is A, B, then A+B, and §6.31 measured that three types together does not work
        /// here -- 0 of 175 boards had all three load-bearing. Two has never been measured, and the
        /// deficit-climb construction changes the odds: lay A until the board is nearly pinned, let
        /// B finish it, and both are load-bearing because neither alone sufficed.
        ///
        /// Answers whether the pack gets a real combination tier or just its hardest single-mechanic
        /// boards at the top.
        /// </summary>
        /// <summary>
        /// How the colour deficit changes what the mechanics have to do. Two questions at once,
        /// because they turned out to be the same question: can a level need SEVERAL cells of a
        /// rule, and can it need TWO rules?
        ///
        /// At a one-colour deficit both answers were no -- 22 of 30 boards needed a single cell, and
        /// two rules were both load-bearing on 4 boards in 1600. The suspicion is that this is not a
        /// property of the mechanics but of how little ambiguity one colour down leaves behind: the
        /// first cell pins the board, so nothing remains for a second cell or a second rule.
        /// </summary>
        /// <summary>
        /// Can a WALL be the second constraint on a board that already carries a cell rule?
        ///
        /// The earlier "two rules cannot share a board" result -- 4 usable boards in 1600 -- was
        /// measured only on pairs of CELL rules, every one of which restricts entry to cells. One
        /// kept subsuming the other, which is unsurprising given they do the same kind of work.
        ///
        /// A wall blocks an EDGE regardless of colour or direction. It is a different kind of
        /// constraint, so the earlier conclusion does not obviously extend to it, and it was never
        /// tested. This settles whether "some cells arrow, some edges walled" is a level the
        /// generator can actually make.
        /// </summary>
        [MenuItem("FreeFlow/Level Generator/Advanced/PROBE wall + rule pairing")]
        public static void ProbeWallPairing()
        {
            const int size = 6;
            const int cells = size * size;
            const int attemptsPer = 400;

            bool[,] usable = new bool[size, size];
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++) { usable[r, c] = true; }
            }

            BlockType[] partners =
            {
                BlockType.Arrow, BlockType.OneWay, BlockType.Checkpoint, BlockType.ForbiddenForPair
            };

            StringBuilder report = new StringBuilder();
            System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();
            CpuThrottle throttle = new CpuThrottle(GenerationDutyCycle);
            bool cancelled = false;

            try
            {
                for (int i = 0; i < partners.Length && !cancelled; i++)
                {
                    System.Random rng = new System.Random(7700 + i);
                    int built = 0, bothNeeded = 0;
                    Dictionary<string, int> shapes = new Dictionary<string, int>();

                    for (int a = 0; a < attemptsPer && !cancelled; a++)
                    {
                        throttle.Tick();

                        if ((a % 8) == 0 && EditorUtility.DisplayCancelableProgressBar(
                                "Wall + rule pairing",
                                partners[i] + " + Wall  -  " + a + "/" + attemptsPer
                                    + "  -  " + bothNeeded + " with both load-bearing"
                                    + "  -  " + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                                (i + a / (float)attemptsPer) / partners.Length))
                        { cancelled = true; break; }

                        MechanicRecipe recipe = new MechanicRecipe(partners[i], 0, 6);
                        recipe.SecondIsWalls = true;
                        recipe.ColourDeficit = 1 + (a % 3);

                        int colours = Mathf.Max(3, cells / 9) + (a % 3);
                        if (!TryBuildMechanicDependentBoard(size, usable, cells, colours, recipe, rng,
                                out LevelData data))
                        {
                            continue;
                        }
                        built++;

                        Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
                        try
                        {
                            if (AllCellsOfTypeAreNecessary(grid, rows, cols, partners[i])
                                && AllWallsAreNecessary(grid, rows, cols))
                            {
                                bothNeeded++;
                                string shape = CountCellsOfType(data, partners[i]) + "+"
                                             + CountWalls(data) + "w";
                                shapes[shape] = shapes.ContainsKey(shape) ? shapes[shape] + 1 : 1;
                            }
                        }
                        finally { DestroyBlockGrid(grid); }
                    }

                    report.Append("  ").Append(partners[i]).Append(" + Wall: built ").Append(built)
                          .Append(", both load-bearing ").Append(bothNeeded)
                          .Append(" / ").Append(attemptsPer);
                    if (shapes.Count > 0)
                    {
                        report.Append("   shapes:");
                        List<string> keys = new List<string>(shapes.Keys);
                        keys.Sort();
                        for (int k = 0; k < keys.Count; k++)
                        {
                            report.Append(' ').Append(keys[k]).Append('x').Append(shapes[keys[k]]);
                        }
                    }
                    report.AppendLine();
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            total.Stop();
            Debug.Log("Wall + rule pairing, " + attemptsPer + " attempts per partner, "
                + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s"
                + (cancelled ? " (CANCELLED)" : "") + ":\n" + report
                + "  reference -- two CELL rules managed 4 boards in 1600 attempts");
        }

        [MenuItem("FreeFlow/Level Generator/Advanced/PROBE colour deficit")]
        public static void ProbeColourDeficit()
        {
            const int size = 6;
            const int cells = size * size;
            const int attemptsPer = 300;

            bool[,] usable = new bool[size, size];
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++) { usable[r, c] = true; }
            }

            StringBuilder report = new StringBuilder();
            System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();
            CpuThrottle throttle = new CpuThrottle(GenerationDutyCycle);
            bool cancelled = false;

            try
            {
                for (int deficit = 1; deficit <= 3 && !cancelled; deficit++)
                {
                    System.Random rng = new System.Random(600 + deficit);
                    Dictionary<int, int> cellCounts = new Dictionary<int, int>();
                    int singles = 0;

                    for (int a = 0; a < attemptsPer && !cancelled; a++)
                    {
                        throttle.Tick();
                        if ((a % 8) == 0 && EditorUtility.DisplayCancelableProgressBar(
                                "Colour deficit probe",
                                "deficit " + deficit + "  -  one rule  -  " + a + "/" + attemptsPer
                                    + "  -  " + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                                (deficit - 1) / 3f))
                        { cancelled = true; break; }

                        MechanicRecipe recipe = new MechanicRecipe(BlockType.Checkpoint, 0, 6);
                        recipe.ColourDeficit = deficit;
                        int colours = Mathf.Max(3, cells / 9) + (a % 3);

                        if (!TryBuildMechanicDependentBoard(size, usable, cells, colours, recipe, rng,
                                out LevelData data))
                        {
                            continue;
                        }
                        singles++;
                        int used = CountCellsOfType(data, BlockType.Checkpoint);
                        cellCounts[used] = cellCounts.ContainsKey(used) ? cellCounts[used] + 1 : 1;
                    }

                    System.Random rng2 = new System.Random(900 + deficit);
                    int builtPairs = 0, bothNeeded = 0;

                    for (int a = 0; a < attemptsPer && !cancelled; a++)
                    {
                        throttle.Tick();
                        if ((a % 8) == 0 && EditorUtility.DisplayCancelableProgressBar(
                                "Colour deficit probe",
                                "deficit " + deficit + "  -  two rules  -  " + a + "/" + attemptsPer
                                    + "  -  " + bothNeeded + " with both needed"
                                    + "  -  " + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                                (deficit - 0.5f) / 3f))
                        { cancelled = true; break; }

                        MechanicRecipe recipe = new MechanicRecipe(
                            BlockType.OneWay, BlockType.Checkpoint, 6, deficit);
                        int colours = Mathf.Max(3, cells / 9) + (a % 3);

                        if (!TryBuildMechanicDependentBoard(size, usable, cells, colours, recipe, rng2,
                                out LevelData data))
                        {
                            continue;
                        }
                        builtPairs++;

                        Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
                        try
                        {
                            if (AllCellsOfTypeAreNecessary(grid, rows, cols, BlockType.OneWay)
                                && AllCellsOfTypeAreNecessary(grid, rows, cols, BlockType.Checkpoint))
                            {
                                bothNeeded++;
                            }
                        }
                        finally { DestroyBlockGrid(grid); }
                    }

                    report.Append("  deficit ").Append(deficit)
                          .Append(":  one-rule boards ").Append(singles).Append(" -> cells needed");
                    List<int> keys = new List<int>(cellCounts.Keys);
                    keys.Sort();
                    for (int k = 0; k < keys.Count; k++)
                    {
                        report.Append(' ').Append(keys[k]).Append('x').Append(cellCounts[keys[k]]);
                    }
                    report.Append("   |  two-rule built ").Append(builtPairs)
                          .Append(", both load-bearing ").Append(bothNeeded)
                          .AppendLine();
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            total.Stop();
            Debug.Log("Colour deficit probe, " + attemptsPer + " attempts per cell, "
                + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s"
                + (cancelled ? " (CANCELLED)" : "") + ":\n" + report);
        }

        [MenuItem("FreeFlow/Level Generator/Advanced/PROBE two-mechanic yield")]
        public static void ProbeTwoMechanicYield()
        {
            const int size = 6;
            const int cells = size * size;
            const int attemptsPer = 400;

            (BlockType A, BlockType B)[] pairs =
            {
                (BlockType.OneWay, BlockType.Checkpoint),
                (BlockType.Arrow, BlockType.ForbiddenForPair),
                (BlockType.Checkpoint, BlockType.ForbiddenForPair),
                (BlockType.OneWay, BlockType.Arrow),
            };

            bool[,] usable = new bool[size, size];
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++) { usable[r, c] = true; }
            }

            StringBuilder report = new StringBuilder();
            System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();
            CpuThrottle throttle = new CpuThrottle(GenerationDutyCycle);
            bool cancelled = false;

            try
            {
                for (int i = 0; i < pairs.Length && !cancelled; i++)
                {
                    System.Random rng = new System.Random(31337 + i);
                    int built = 0, bothNecessary = 0;
                    Dictionary<string, int> shapes = new Dictionary<string, int>();

                    for (int a = 0; a < attemptsPer; a++)
                    {
                        throttle.Tick();

                        if ((a % 8) == 0 && EditorUtility.DisplayCancelableProgressBar(
                                "Two-mechanic yield",
                                pairs[i].A + " + " + pairs[i].B + "  -  " + a + "/" + attemptsPer
                                    + "  -  " + bothNecessary + " with both load-bearing"
                                    + "  -  " + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                                (i + a / (float)attemptsPer) / pairs.Length))
                        { cancelled = true; break; }

                        MechanicRecipe recipe = new MechanicRecipe(pairs[i].A, pairs[i].B, 4, 1);
                        int colours = Mathf.Max(3, cells / 9) + (a % 3);

                        if (!TryBuildMechanicDependentBoard(size, usable, cells, colours,
                                recipe, rng, out LevelData data))
                        {
                            continue;
                        }
                        built++;

                        Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
                        try
                        {
                            if (AllCellsOfTypeAreNecessary(grid, rows, cols, pairs[i].A)
                                && AllCellsOfTypeAreNecessary(grid, rows, cols, pairs[i].B))
                            {
                                bothNecessary++;
                                string shape = CountCellsOfType(data, pairs[i].A) + "+"
                                             + CountCellsOfType(data, pairs[i].B);
                                shapes[shape] = shapes.ContainsKey(shape) ? shapes[shape] + 1 : 1;
                            }
                        }
                        finally { DestroyBlockGrid(grid); }
                    }

                    report.Append("  ").Append(pairs[i].A).Append(" + ").Append(pairs[i].B)
                          .Append(": built ").Append(built)
                          .Append(", both load-bearing ").Append(bothNecessary)
                          .Append(" / ").Append(attemptsPer).Append(" attempts");
                    if (shapes.Count > 0)
                    {
                        report.Append("   shapes:");
                        List<string> keys = new List<string>(shapes.Keys);
                        keys.Sort();
                        for (int k = 0; k < keys.Count; k++)
                        {
                            report.Append(' ').Append(keys[k]).Append('x').Append(shapes[keys[k]]);
                        }
                    }
                    report.AppendLine();
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            total.Stop();
            Debug.Log("Two-mechanic yield, " + attemptsPer + " attempts per pair, "
                + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s"
                + (cancelled ? " (CANCELLED)" : "") + ":\n" + report
                + "  single-mechanic reference: ~40 kept per 288 generated at 6x6");
        }

        [MenuItem("FreeFlow/Level Generator/Advanced/PROBE Advanced yield")]
        public static void ProbeAdvancedYield() { BuildAdvancedPack(6, 24, 4, 260, 9); }

        /// <summary>
        /// Same probe, at 7x7, before committing to a full 900-candidate/100-level run.
        /// cellsPerColour=10 here is an EXTRAPOLATION from 6x6's measured 9 (see
        /// GAME_EXPANSION_PLAN §6.40) and from Classic's own 7x7 pack (which measured 4-7 colours,
        /// §6.38) -- not a separate measurement for the Advanced pipeline specifically, which adds
        /// mechanic-necessity gates Classic's pipeline does not have. This is exactly the kind of
        /// unverified analogy that put the first 8x8 Classic pack at the wrong colour count twice
        /// (§6.38) -- probe before trusting it.
        /// </summary>
        [MenuItem("FreeFlow/Level Generator/Advanced/PROBE Advanced yield 7x7")]
        public static void ProbeAdvancedYield7x7() { BuildAdvancedPack(7, 24, 4, 260, 10); }

        /// <summary>
        /// Cheap colour-ratio sweep for the Advanced pipeline, run BEFORE another full
        /// BuildAdvancedPack probe. The 7x7 probe at cellsPerColour=10 cost 24284.7s (6.75h) to
        /// gather only 46 of a 260 target -- it hit the pipeline's hard 200,000-attempt cap, not
        /// the pool target, at a raw yield of 0.023%. A full pack at that rate would need roughly
        /// 3.9M attempts (~5.5 days) to reach a 900-board pool. That is a wrong configuration, not
        /// a patience problem -- re-running the same call longer will not fix it.
        ///
        /// This samples a BOUNDED number of attempts per candidate cellsPerColour value (no
        /// 200,000-attempt pipeline cap, no disk writes) and reports exactly where attempts die:
        /// generated / duplicate / unsound (broken down by StructuralGates' own failure reason) /
        /// decorative / kept, plus ms-per-kept-board. That breakdown is what a single pass/fail
        /// number from a full probe cannot show -- see GAME_EXPANSION_PLAN §6.38, which made this
        /// exact kind of colour-count mistake twice for Classic before probing properly.
        ///
        /// Deliberately reuses BuildAdvancedPack's own recipe list and per-attempt logic (copied,
        /// not shared) rather than refactoring the production method -- this is a throwaway
        /// diagnostic, and changing the code path every real pack build uses to accommodate it
        /// would risk the exact class of bug this project has hit before (§0's gotchas).
        /// </summary>
        public static void ProbeColourRatioSweep(int size, int[] cellsPerColourValues, int attemptsPerValue)
        {
            int cells = size * size;
            int holes = Mathf.Max(2, cells / 12);

            BlockType[] rules =
            {
                BlockType.OneWay, BlockType.Arrow, BlockType.ForbiddenForPair,
                BlockType.AllowedForPairs, BlockType.Checkpoint
            };

            StringBuilder summary = new StringBuilder();
            summary.Append("Colour-ratio sweep, ").Append(size).Append("x").Append(size)
                .Append(", ").Append(attemptsPerValue).Append(" attempts per value:\n");

            for (int v = 0; v < cellsPerColourValues.Length; v++)
            {
                int cellsPerColour = cellsPerColourValues[v];

                List<MechanicRecipe> recipeList = new List<MechanicRecipe>();
                recipeList.Add(new MechanicRecipe(BlockType.Normal, holes, 0));
                for (int d = 1; d <= 3; d++)
                {
                    for (int i = 0; i < rules.Length; i++)
                    {
                        MechanicRecipe withHoles = new MechanicRecipe(rules[i], holes, InstanceCeilingFor(size));
                        withHoles.ColourDeficit = d;
                        recipeList.Add(withHoles);
                        recipeList.Add(withHoles);

                        MechanicRecipe bare = new MechanicRecipe(rules[i], 0, InstanceCeilingFor(size));
                        bare.ColourDeficit = d;
                        recipeList.Add(bare);
                    }
                    recipeList.Add(MechanicRecipe.Walls(holes, 6, d));
                    recipeList.Add(MechanicRecipe.Walls(0, 6, d));
                    recipeList.Add(MechanicRecipe.TwoColour(BlockType.ForbiddenForPair, 0, 6, d));
                    recipeList.Add(MechanicRecipe.TwoColour(BlockType.AllowedForPairs, 0, 6, d));
                }
                MechanicRecipe[] recipes = recipeList.ToArray();

                System.Random rng = new System.Random(20260915 + size * 1000 + cellsPerColour);
                System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();

                int generated = 0, duplicates = 0, decorative = 0, kept = 0;
                int selfTouch = 0, shortLink = 0, uniformSpread = 0, badCoverage = 0;
                long pathCellsTotal = 0, pairsTotal = 0;
                HashSet<string> seen = new HashSet<string>();
                bool cancelled = false;

                for (int attempt = 0; attempt < attemptsPerValue; attempt++)
                {
                    if ((attempt % 50) == 0 && EditorUtility.DisplayCancelableProgressBar(
                            "Colour-ratio sweep " + size + "x" + size + "  -  cellsPerColour=" + cellsPerColour,
                            attempt + "/" + attemptsPerValue + "  kept " + kept,
                            (float)(v * attemptsPerValue + attempt) / (cellsPerColourValues.Length * attemptsPerValue)))
                    { cancelled = true; break; }

                    MechanicRecipe recipe = recipes[attempt % recipes.Length];

                    bool[,] usable = PlaceBlockedCells(size, recipe.BlockedCells, true, rng);
                    int usableCount = 0;
                    for (int r = 0; r < size; r++)
                    {
                        for (int c = 0; c < size; c++) { if (usable[r, c]) { usableCount++; } }
                    }

                    int colours = Mathf.Max(3, usableCount / cellsPerColour) + (attempt % ColourSweepWidth);
                    if (colours > MaxDistinctColors) { continue; }

                    LevelData data;
                    if (recipe.Instances == 0)
                    {
                        if (!TryGenerateUniqueByRefinement(size, usable, usableCount, colours,
                                MaxDistinctColors, 2000000, 3, rng,
                                out data, out int finalColours, out int splits, colours))
                        { continue; }
                    }
                    else if (!TryBuildMechanicDependentBoard(size, usable, usableCount, colours,
                                 recipe, rng, out data))
                    { continue; }
                    generated++;

                    Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
                    try
                    {
                        string key = LevelCanonicalizer.ComputeCanonicalKey(grid, rows, cols);
                        if (!seen.Add(key)) { duplicates++; continue; }

                        PuzzleSolver.SolveResult solved = PuzzleSolver.Solve(grid, rows, cols,
                            new PuzzleSolver.SolverOptions(8000000, 2));
                        if (solved.Status != PuzzleSolver.SolveStatus.Solved
                            || solved.SolutionsFound != 1 || !solved.SearchExhausted)
                        { continue; } // not unique -- rare enough by construction not to need its own tally here

                        if (!StoredSolutionMatchesSolver(data, solved, rows, cols)) { continue; }

                        int liveCells = 0;
                        for (int r = 0; r < rows; r++)
                        {
                            for (int c = 0; c < cols; c++)
                            {
                                if (grid[r, c] != null && grid[r, c].BlockType != BlockType.Blocked) { liveCells++; }
                            }
                        }

                        StructuralGates.Report gate = StructuralGates.Evaluate(solved, liveCells);
                        if (!gate.Passed)
                        {
                            if (gate.SelfTouches > 0) { selfTouch++; }
                            else if (gate.ShortestPath < StructuralGates.MinPathCells) { shortLink++; }
                            else if (gate.LengthSpread < StructuralGates.DefaultMinLengthSpread) { uniformSpread++; }
                            else { badCoverage++; }
                            continue;
                        }

                        if (recipe.Instances > 0 && recipe.UsesWalls
                            && !AllWallsAreNecessary(grid, rows, cols)) { decorative++; continue; }
                        if (recipe.Instances > 0 && !recipe.UsesWalls
                            && !AllCellsOfTypeAreNecessary(grid, rows, cols, recipe.Type)) { decorative++; continue; }
                        if (recipe.SecondType != BlockType.Normal
                            && !AllCellsOfTypeAreNecessary(grid, rows, cols, recipe.SecondType)) { decorative++; continue; }
                        if (recipe.BlockedCells > 0
                            && !AllCellsOfTypeAreNecessary(grid, rows, cols, BlockType.Blocked)) { decorative++; continue; }

                        kept++;
                        pathCellsTotal += liveCells;
                        pairsTotal += solved.Solutions.Count;
                    }
                    finally { DestroyBlockGrid(grid); }
                }

                total.Stop();
                int unsound = selfTouch + shortLink + uniformSpread + badCoverage;
                double msPerKept = kept > 0 ? total.ElapsedMilliseconds / (double)kept : -1;
                double meanPath = pairsTotal > 0 ? pathCellsTotal / (double)pairsTotal : 0;

                summary.Append("  cellsPerColour=").Append(cellsPerColour)
                    .Append("  meanPath=").Append(meanPath.ToString("0.0"))
                    .Append("  attempts=").Append(attemptsPerValue)
                    .Append("  generated=").Append(generated)
                    .Append("  dup=").Append(duplicates)
                    .Append("  unsound=").Append(unsound)
                    .Append(" [selfTouch=").Append(selfTouch)
                    .Append(" short=").Append(shortLink)
                    .Append(" spread=").Append(uniformSpread)
                    .Append(" cov=").Append(badCoverage).Append("]")
                    .Append("  decorative=").Append(decorative)
                    .Append("  kept=").Append(kept)
                    .Append("  time=").Append((total.ElapsedMilliseconds / 1000f).ToString("0"))
                    .Append("s  ms/kept=").Append(msPerKept >= 0 ? msPerKept.ToString("0") : "n/a")
                    .AppendLine();

                if (cancelled) { summary.Append("  (CANCELLED)\n"); EditorUtility.ClearProgressBar(); break; }
            }

            EditorUtility.ClearProgressBar();
            Debug.Log(summary.ToString());
        }

        [MenuItem("FreeFlow/Level Generator/Advanced/PROBE 7x7 colour ratio sweep")]
        public static void ProbeColourRatioSweep7x7()
        {
            ProbeColourRatioSweep(7, new[] { 6, 7, 8, 9, 10, 12 }, 3000);
        }

        /// <summary>
        /// The 8x8 sibling. Deliberately the same six ratios as 7x7 so the two tables are directly
        /// comparable; at 64 cells that spans 5.3 to 10.7 colours, which brackets both reference
        /// points worth hitting -- Classic's own 8x8 pack (~8.7 colours, 7.4 cells each) and Flow
        /// Free's 8x8 (9 colours, mean path 7.1).
        ///
        /// MEASURE before wiring a build. §6.38 and §6.44 each record a pack configured by
        /// extrapolating a colour ratio from a different board size, and both were wrong; §6.44's
        /// took 6.75 hours to discover, and the sweep that replaced it found the best ratio was
        /// not the guessed one. This probe is bounded and cancellable, and logs its partial table
        /// on cancel, so a slow run still yields the comparison.
        /// </summary>
        [MenuItem("FreeFlow/Level Generator/Advanced/PROBE 8x8 colour ratio sweep")]
        public static void ProbeColourRatioSweep8x8()
        {
            ProbeColourRatioSweep(8, new[] { 6, 7, 8, 9, 10, 12 }, 3000);
        }

        /// <summary>
        /// Picks <paramref name="count"/> boards spread evenly across the SCORE RANGE, ascending.
        ///
        /// <b>Why not just take the hardest N.</b> Top-N is right for a short block appended to a
        /// campaign and wrong for a pack the player enters at level 1: the hardest hundred of two
        /// thousand are all bunched at the top of the range, so the pack opens hard and barely
        /// climbs. Stratifying spends the material on a gradient instead.
        ///
        /// <b>Even across the RANGE, not across the population.</b> Taking every twentieth board by
        /// rank would follow the distribution -- and since scores cluster in the middle, that yields
        /// a pack where most levels feel alike and the ends are sparse. Walking the range asks for a
        /// board at each target difficulty and takes the nearest one still unused, so the ramp is
        /// even in the thing the player actually feels.
        ///
        /// Nearest-unused is greedy and can drift where the pool is thin, which is honest: a gap in
        /// the material shows up as a flat stretch rather than being papered over.
        /// </summary>
        private static List<(float Score, LevelData Data)> SelectStratified(
            List<(float Score, LevelData Data)> scored, int count)
        {
            List<(float Score, LevelData Data)> sorted =
                new List<(float, LevelData)>(scored);
            sorted.Sort((x, y) => x.Score.CompareTo(y.Score));

            if (count >= sorted.Count) { return sorted; }
            if (count <= 1) { return new List<(float, LevelData)> { sorted[sorted.Count - 1] }; }

            float lo = sorted[0].Score;
            float hi = sorted[sorted.Count - 1].Score;

            bool[] used = new bool[sorted.Count];
            List<(float Score, LevelData Data)> picked = new List<(float, LevelData)>(count);

            for (int i = 0; i < count; i++)
            {
                float target = lo + (hi - lo) * i / (count - 1);

                int best = -1;
                float bestGap = float.MaxValue;
                for (int j = 0; j < sorted.Count; j++)
                {
                    if (used[j]) { continue; }
                    float gap = Mathf.Abs(sorted[j].Score - target);
                    if (gap < bestGap) { bestGap = gap; best = j; }
                }

                used[best] = true;
                picked.Add(sorted[best]);
            }

            picked.Sort((x, y) => x.Score.CompareTo(y.Score));
            return picked;
        }

        [MenuItem("FreeFlow/Level Generator/Classic/Rebuild levels 1-50 on the difficulty model")]
        public static void RebuildClassicOnDifficultyModel()
        {
            // size, firstLevel, lastLevel, poolTarget
            RebuildClassicBlocks(new int[,]
            {
                { 5,  1, 15, 200 },
                { 6, 16, 32, 200 },
                { 7, 33, 50, 150 },
            }, "Classic 1-50");
        }

        /// <summary>
        /// The 7x7 block on its own, so one block can be redone without discarding the others.
        ///
        /// This exists because the first full run was cancelled an hour in, part-way through 7x7,
        /// with levels 1-32 already built and passing every gate. Re-running everything to fix the
        /// last block would have thrown that away.
        /// </summary>
        [MenuItem("FreeFlow/Level Generator/Classic/Rebuild 7x7 block only (33-50)")]
        public static void RebuildClassicSevenBlock()
        {
            RebuildClassicBlocks(new int[,] { { 7, 33, 50, 450 } }, "Classic 33-50 (7x7)");
        }

        /// <summary>
        /// The 5x5 and 6x6 blocks, brought up to the calibration that play confirmed on 7x7.
        ///
        /// These two were built before §6.35 and before the self-touch fix, from pools that were
        /// 88-96% rejected -- so their shortlists had almost nothing to choose between, and the
        /// campaign dipped in the middle: 6x6 topped out at 65 against the 5x5's 68, then jumped to
        /// 74 at level 33.
        ///
        /// The pool targets are sized from a measurement rather than a guess, which is the lesson
        /// from getting it wrong twice: generation is cheap, the SCORING pass sets the clock. At
        /// 16ms and 19ms per board for 5x5, and 120ms and 209ms for 6x6, the two blocks together
        /// come to about two minutes -- against thirty-nine for 7x7, whose solves are far dearer.
        /// The pools are deliberately only a little above `needed x ShortlistPerLevel`, because a
        /// candidate the shortlist will never reach is a candidate generated for nothing.
        /// </summary>
        [MenuItem("FreeFlow/Level Generator/Classic/Rebuild 5x5 and 6x6 blocks (1-32)")]
        public static void RebuildClassicSmallBlocks()
        {
            RebuildClassicBlocks(new int[,]
            {
                { 5,  1, 15, 320 },
                { 6, 16, 32, 360 },
            }, "Classic 1-32 (5x5, 6x6)");
        }

        /// <summary>
        /// How many candidates get the full difficulty model per level kept.
        ///
        /// Was 3, which meant keeping a third of everything looked at -- easy tail included. The
        /// measured consequence: across the 18 levels of the 7x7 block, ten sat at 7-8 assumptions
        /// while the top two reached 13-14, so most of the block was the easy half. The reference
        /// Flow Free board needs 13.
        ///
        /// 3 was chosen when a candidate was expensive. It is not any more -- refusing self-touching
        /// growth took 7x7 acceptance from "never finished in an hour" to 203 boards in 122s -- so
        /// the ratio can buy selection pressure instead of throughput.
        /// </summary>
        private const int ShortlistPerLevel = 20;

        /// <summary>
        /// Colours to aim for, as a divisor of the cell count, and how far the sweep ranges above it.
        ///
        /// Fewer colours means longer paths, and the 7x7 block's own numbers say that is the single
        /// clean correlate of difficulty we have: colours 10 -> 5 tracked score 53 -> 78, monotonic,
        /// with nothing else in the table behaving as tidily. The sweep used to start at cells/9 and
        /// range six wide (5-10 at 7x7), so most candidates were born easy.
        ///
        /// cells/12 with a spread of 3 asks for 4-6 at 7x7. Four may well be unreachable -- at five
        /// colours a 7x7 is already one pair per 9.8 cells, MORE stretched than Flow Free's 7.1 --
        /// but an attempt that fails to prove uniqueness is cheap, and refinement splits upward from
        /// the target anyway, so aiming low costs little and biases the whole pool down.
        /// </summary>
        private const int CellsPerColourTarget = 12;
        private const int ColourSweepWidth = 3;

        private static void RebuildClassicBlocks(int[,] blocks, string label)
        {
            const string levelsFolder = "Assets/Resources/Levels/Classic";
            int blockCount = blocks.GetLength(0);

            EnsureLevelFolder(levelsFolder);
            EditorUtility.ClearProgressBar();

            HashSet<string> seen = new HashSet<string>();
            StringBuilder report = new StringBuilder();
            System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();
            bool cancelled = false;

            try
            {
                for (int b = 0; b < blockCount && !cancelled; b++)
                {
                    int size = blocks[b, 0];
                    int firstLevel = blocks[b, 1];
                    int lastLevel = blocks[b, 2];
                    int poolTarget = blocks[b, 3];
                    int needed = lastLevel - firstLevel + 1;
                    int cells = size * size;

                    bool[,] usable = new bool[size, size];
                    for (int r = 0; r < size; r++)
                    {
                        for (int c = 0; c < size; c++) { usable[r, c] = true; }
                    }

                    System.Random rng = new System.Random(20261225 + size);
                    List<(float Tangle, LevelData Data)> survivors = new List<(float, LevelData)>();
                    int generated = 0, rejectedStructure = 0;

                    for (int attempt = 0; attempt < 14000 && survivors.Count < poolTarget; attempt++)
                    {
                        if ((attempt % 4) == 0 && EditorUtility.DisplayCancelableProgressBar(
                                "Rebuilding " + label + "  (block " + (b + 1) + "/" + blockCount + ")",
                                size + "x" + size + "  -  kept " + survivors.Count + "/" + poolTarget
                                    + "  -  " + rejectedStructure + " rejected on structure"
                                    + "  -  " + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                                (b + survivors.Count / (float)poolTarget) / blockCount))
                        { cancelled = true; break; }

                        int colours = Mathf.Max(3, cells / CellsPerColourTarget)
                                    + (attempt % ColourSweepWidth);
                        if (colours > MaxDistinctColors) { continue; }

                        if (!TryGenerateUniqueByRefinement(size, usable, cells, colours,
                                MaxDistinctColors, 2000000, 3, rng,
                                out LevelData data, out int finalColours, out int splits, colours))
                        {
                            continue;
                        }
                        generated++;

                        Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
                        string key;
                        float tangle;
                        bool structurallySound;
                        try
                        {
                            key = LevelCanonicalizer.ComputeCanonicalKey(grid, rows, cols);
                            if (seen.Contains(key)) { continue; }

                            PuzzleSolver.SolveResult solved = PuzzleSolver.Solve(grid, rows, cols,
                                new PuzzleSolver.SolverOptions(8000000, 1));

                            int usableCells = 0;
                            for (int r = 0; r < rows; r++)
                            {
                                for (int c = 0; c < cols; c++)
                                {
                                    if (grid[r, c] != null && grid[r, c].BlockType != BlockType.Blocked)
                                    {
                                        usableCells++;
                                    }
                                }
                            }

                            structurallySound = StructuralGates.Evaluate(solved, usableCells).Passed;
                            tangle = TangleScore(grid, rows, cols, solved);
                        }
                        finally { DestroyBlockGrid(grid); }

                        seen.Add(key);
                        if (!structurallySound) { rejectedStructure++; continue; }
                        survivors.Add((tangle, data));
                    }

                    if (survivors.Count < needed)
                    {
                        Debug.LogError("Block " + size + "x" + size + ": only " + survivors.Count
                            + " structurally sound candidates for " + needed + " levels; block skipped."
                            + "  (" + generated + " generated, " + rejectedStructure + " rejected)");
                        continue;
                    }

                    // Pass 2: pay for the full model on a shortlist, not on the whole pool.
                    survivors.Sort((x, y) => y.Tangle.CompareTo(x.Tangle));
                    int shortlistSize = Mathf.Min(survivors.Count, needed * ShortlistPerLevel);

                    List<(float Score, LevelData Data, string Detail)> scored =
                        new List<(float, LevelData, string)>();

                    for (int i = 0; i < shortlistSize && !cancelled; i++)
                    {
                        if ((i % 2) == 0 && EditorUtility.DisplayCancelableProgressBar(
                                "Scoring the shortlist  (block " + (b + 1) + "/" + blockCount + ")",
                                size + "x" + size + "  -  " + i + "/" + shortlistSize
                                    + "  -  " + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                                (b + i / (float)shortlistSize) / blockCount))
                        { cancelled = true; break; }

                        DifficultyModel.Profile profile = DifficultyModel.Measure(survivors[i].Data);
                        if (!profile.Valid || !profile.WellFormed) { continue; }
                        scored.Add((profile.Score, survivors[i].Data, profile.ToString()));
                    }

                    if (scored.Count < needed)
                    {
                        Debug.LogError("Block " + size + "x" + size + ": only " + scored.Count
                            + " well-formed candidates survived scoring, needed " + needed
                            + "; block skipped.");
                        continue;
                    }

                    scored.Sort((x, y) => y.Score.CompareTo(x.Score));       // hardest first
                    List<(float Score, LevelData Data, string Detail)> chosen = scored.GetRange(0, needed);
                    chosen.Sort((x, y) => x.Score.CompareTo(y.Score));       // then ramp upward

                    for (int i = 0; i < chosen.Count; i++)
                    {
                        SaveLevelAsset(levelsFolder, firstLevel + i, chosen[i].Data, chosen[i].Score);
                    }

                    report.Append(size).Append('x').Append(size)
                          .Append("  levels ").Append(firstLevel).Append('-').Append(lastLevel)
                          .Append(":  score ").Append(chosen[0].Score.ToString("0"))
                          .Append("..").Append(chosen[chosen.Count - 1].Score.ToString("0"))
                          .Append("   generated ").Append(generated)
                          .Append(", rejected ").Append(rejectedStructure).Append(" on structure")
                          .Append(", scored ").Append(shortlistSize)
                          .Append(", well-formed ").Append(scored.Count)
                          .AppendLine();
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            total.Stop();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(label + " rebuilt on the difficulty model in "
                + (total.ElapsedMilliseconds / 1000f).ToString("0.0") + "s"
                + (cancelled ? " (CANCELLED)" : "") + ".\n" + report
                + "Flow Free 8x8 reference: score 67, tangle 81, 13 assumptions, dependency 1.33.");
        }

        [MenuItem("FreeFlow/Level Generator/Classic/Write 3 calibration levels (58,59,60)")]
        public static void WriteCalibrationLevels()
        {
            const string levelsFolder = "Assets/Resources/Levels/Classic";
            int[] sizes = { 6, 7, 8 };
            int[] slots = { 58, 59, 60 };
            int[] poolFor = { 400, 250, 60 };

            EnsureLevelFolder(levelsFolder);
            EditorUtility.ClearProgressBar();
            StringBuilder report = new StringBuilder();
            bool cancelled = false;

            try
            {
                for (int i = 0; i < sizes.Length && !cancelled; i++)
                {
                    int size = sizes[i];
                    int cells = size * size;
                    bool[,] usable = new bool[size, size];
                    for (int r = 0; r < size; r++)
                    {
                        for (int c = 0; c < size; c++) { usable[r, c] = true; }
                    }

                    System.Random rng = new System.Random(20261220 + size);
                    LevelData best = default;
                    int bestDecisions = -1, bestColours = 0, scored = 0;
                    float bestTangle = -1f;
                    System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();

                    for (int attempt = 0; attempt < 8000 && scored < poolFor[i]; attempt++)
                    {
                        if ((attempt % 4) == 0 && EditorUtility.DisplayCancelableProgressBar(
                                "Calibration boards",
                                size + "x" + size + "  -  scored " + scored + "/" + poolFor[i]
                                    + "  -  hardest so far " + bestDecisions + " decisions"
                                    + "  -  " + (timer.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                                (i + scored / (float)poolFor[i]) / sizes.Length))
                        { cancelled = true; break; }

                        int colours = Mathf.Max(3, cells / 9) + (attempt % 7);
                        if (colours > MaxDistinctColors) { continue; }

                        if (!TryGenerateUniqueByRefinement(size, usable, cells, colours,
                                MaxDistinctColors, 2000000, 3, rng,
                                out LevelData data, out int finalColours, out int splits, colours))
                        {
                            continue;
                        }

                        Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
                        int decisions;
                        float tangle;
                        try
                        {
                            PuzzleSolver.SolveResult effort = PuzzleSolver.Solve(grid, rows, cols,
                                new PuzzleSolver.SolverOptions(8000000, 1));
                            decisions = effort.DecisionPointCount;
                            tangle = TangleScore(grid, rows, cols, effort);
                        }
                        finally { DestroyBlockGrid(grid); }

                        scored++;
                        // Ranked on TANGLE rather than solver effort. Four effort metrics each said
                        // the shipped boards should be hard and play disagreed every time; the
                        // hardest 6x6 by decision count scored 25 tangle against Flow Free's 81.
                        if (tangle > bestTangle)
                        {
                            bestTangle = tangle;
                            bestDecisions = decisions;
                            bestColours = finalColours;
                            best = data;
                        }
                    }
                    timer.Stop();

                    if (bestTangle < 0f) { report.Append("L").Append(slots[i]).AppendLine(": FAILED"); continue; }

                    SaveLevelAsset(levelsFolder, slots[i], best, 0f);
                    report.Append("L").Append(slots[i]).Append(" = hardest ").Append(size).Append('x').Append(size)
                          .Append(":  colours=").Append(bestColours)
                          .Append("  TANGLE=").Append(bestTangle.ToString("0"))
                          .Append("  decisions=").Append(bestDecisions)
                          .Append("  (best of ").Append(scored).Append(" in ")
                          .Append((timer.ElapsedMilliseconds / 1000f).ToString("0")).AppendLine("s)");
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Calibration levels written" + (cancelled ? " (CANCELLED)" : "")
                + ".\n" + report + "\nFlow Free 8x8 reference: TANGLE 81, decisions 4600.");
        }

        [MenuItem("FreeFlow/Level Generator/Classic/Rebuild 6x6 block only (26-60)")]
        public static void RebuildClassicSixBlock()
        {
            const string levelsFolder = "Assets/Resources/Levels/Classic";
            const int firstLevel = 26;
            const int lastLevel = 60;
            const int size = 6;
            const int poolTarget = 240;     // candidates to score before choosing
            int needed = lastLevel - firstLevel + 1;

            EnsureLevelFolder(levelsFolder);
            EditorUtility.ClearProgressBar();

            int cells = size * size;
            bool[,] usable = new bool[size, size];
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++) { usable[r, c] = true; }
            }

            System.Random rng = new System.Random(20261210);
            HashSet<string> seen = new HashSet<string>();
            List<(int Decisions, int Colours, LevelData Data)> pool =
                new List<(int, int, LevelData)>();

            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            bool cancelled = false;

            try
            {
                for (int attempt = 0; attempt < 6000 && pool.Count < poolTarget && !cancelled; attempt++)
                {
                    if ((attempt % 4) == 0 && EditorUtility.DisplayCancelableProgressBar(
                            "Rebuilding 6x6 block",
                            "pool " + pool.Count + "/" + poolTarget
                                + "   attempt " + (attempt + 1)
                                + "   " + (timer.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                            pool.Count / (float)poolTarget))
                    { cancelled = true; break; }

                    // Sweep colour counts rather than fixing one: which count yields the hardest
                    // board is not predictable, so let the score decide.
                    int colours = 3 + (attempt % 6);   // 3..8

                    if (!TryGenerateUniqueByRefinement(size, usable, cells, colours,
                            MaxDistinctColors, 2000000, 3, rng,
                            out LevelData data, out int finalColours, out int splits, colours))
                    {
                        continue;
                    }

                    Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
                    string key;
                    int decisions;
                    try
                    {
                        key = LevelCanonicalizer.ComputeCanonicalKey(grid, rows, cols);
                        if (seen.Contains(key)) { continue; }
                        PuzzleSolver.SolveResult effort = PuzzleSolver.Solve(grid, rows, cols,
                            new PuzzleSolver.SolverOptions(8000000, 1));
                        decisions = effort.DecisionPointCount;
                    }
                    finally { DestroyBlockGrid(grid); }

                    seen.Add(key);
                    pool.Add((decisions, finalColours, data));
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            timer.Stop();

            if (pool.Count < needed)
            {
                Debug.LogError("6x6 block: only " + pool.Count + " candidates for " + needed
                    + " levels; nothing written.");
                return;
            }

            // Hardest N, then ascending so the block ramps.
            pool.Sort((a, b) => b.Decisions.CompareTo(a.Decisions));
            List<(int Decisions, int Colours, LevelData Data)> chosen = pool.GetRange(0, needed);
            chosen.Sort((a, b) => a.Decisions.CompareTo(b.Decisions));

            StringBuilder report = new StringBuilder();
            for (int i = 0; i < chosen.Count; i++)
            {
                SaveLevelAsset(levelsFolder, firstLevel + i, chosen[i].Data, 0f);
                if (i == 0 || i == chosen.Count / 2 || i == chosen.Count - 1)
                {
                    report.Append("L").Append(firstLevel + i)
                          .Append(": colours=").Append(chosen[i].Colours)
                          .Append("  decisions=").Append(chosen[i].Decisions)
                          .AppendLine();
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int poolMin = pool[pool.Count - 1].Decisions;
            int poolMax = pool[0].Decisions;
            Debug.Log("6x6 block rebuilt: " + needed + " levels from a pool of " + pool.Count
                + " in " + (timer.ElapsedMilliseconds / 1000f).ToString("0.0") + "s"
                + (cancelled ? " (CANCELLED early)" : "")
                + ".\n  pool decisions " + poolMin + ".." + poolMax
                + "   kept " + chosen[0].Decisions + ".." + chosen[chosen.Count - 1].Decisions
                + "\n" + report);
        }

        [MenuItem("FreeFlow/Level Generator/Classic/Generate Classic campaign (100, up to 7x7)")]
        public static void GenerateClassicCampaign()
        {
            const string levelsFolder = "Assets/Resources/Levels/Classic";
            const int totalLevels = 100;
            // How many valid boards to generate and score before keeping the hardest. Higher means
            // harder levels and a longer run; 12 costs seconds per level at these sizes.
            const int CandidatesPerLevel = 12;

            EnsureLevelFolder(levelsFolder);
            EditorUtility.ClearProgressBar(); // sticky cancel flag -- see GenerateLevels31To35

            System.Random rng = new System.Random(20261201);
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            StringBuilder report = new StringBuilder();
            int saved = 0;
            bool cancelled = false;
            System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                for (int level = 1; level <= totalLevels && !cancelled; level++)
                {
                    ClassicShapeFor(level, out int size, out int targetColours);
                    int cells = size * size;

                    bool[,] usable = new bool[size, size];
                    for (int r = 0; r < size; r++)
                    {
                        for (int c = 0; c < size; c++) { usable[r, c] = true; }
                    }

                    LevelData chosen = default;
                    string chosenKey = null;
                    int chosenColours = 0;
                    int chosenDecisions = -1;
                    bool built = false;
                    int candidatesSeen = 0;

                    // Keep the HARDEST board found, not the first that works.
                    //
                    // Three playtests in a row reported "too easy", and the reason was the
                    // acceptance test rather than any tuning value: it took the first uniquely
                    // solvable board it produced. Measured against Flow Free's own 8x8, our
                    // supposedly hardest level needed 188 solver decision points against their
                    // 4600 -- twenty-four times less thinking, on the level they ship FIRST.
                    //
                    // Decision points are the honest measure here: places where the solver had a
                    // real choice, which is where a player has to reason rather than follow a
                    // forced line. Path length is not -- our longest-path level demanded seven
                    // times FEWER decisions than a shorter-path one -- and neither is the count of
                    // alternative pairings, since the Flow Free board has none at all and is still
                    // hard.
                    for (int attempt = 0; attempt < 600 && candidatesSeen < CandidatesPerLevel; attempt++)
                    {
                        if ((attempt % 5) == 0 && EditorUtility.DisplayCancelableProgressBar(
                                "Classic campaign  (" + level + "/" + totalLevels + ")",
                                "Level " + level + "  -  " + size + "x" + size
                                    + "  -  target " + targetColours + " colours  -  attempt " + (attempt + 1)
                                    + "  -  " + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s elapsed",
                                (level - 1) / (float)totalLevels))
                        { cancelled = true; break; }

                        // Start AT the target, not below it. Refinement only ever splits upward
                        // and merge-down only ever stops early, so a start below the target makes
                        // the target unreachable from either side: an earlier build began every
                        // 7x7 at 6 colours, landed on 8, and produced forty consecutive levels of
                        // identical difficulty because the ramp had nothing to act on.
                        if (!TryGenerateUniqueByRefinement(size, usable, cells,
                                Mathf.Max(3, targetColours), MaxDistinctColors, 2000000, 3, rng,
                                out LevelData data, out int colours, out int splits, targetColours))
                        {
                            continue;
                        }

                        // Dedup across the whole campaign, not just this size.
                        Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
                        string key;
                        try { key = LevelCanonicalizer.ComputeCanonicalKey(grid, rows, cols); }
                        finally { DestroyBlockGrid(grid); }

                        if (seenCanonicalKeys.Contains(key)) { continue; }

                        // Re-solve to see how much searching this board actually demands.
                        Block[,] scored = BuildBlockGrid(data, out int srows, out int scols);
                        int decisions;
                        try
                        {
                            PuzzleSolver.SolveResult effort = PuzzleSolver.Solve(scored, srows, scols,
                                new PuzzleSolver.SolverOptions(8000000, 1));
                            decisions = effort.DecisionPointCount;
                        }
                        finally { DestroyBlockGrid(scored); }

                        candidatesSeen++;
                        if (decisions > chosenDecisions)
                        {
                            chosen = data;
                            chosenColours = colours;
                            chosenDecisions = decisions;
                            chosenKey = key;
                            built = true;
                        }
                    }

                    if (built) { seenCanonicalKeys.Add(chosenKey); }

                    if (cancelled) { break; }

                    if (!built)
                    {
                        Debug.LogError("Classic level " + level + " (" + size + "x" + size
                            + ", target " + targetColours + " colours) could not be generated.");
                        report.Append("Level ").Append(level).AppendLine(": FAILED");
                        continue;
                    }

                    SaveLevelAsset(levelsFolder, level, chosen, 0f);
                    saved++;
                    report.Append("L").Append(level).Append(": ").Append(size).Append('x').Append(size)
                          .Append("  colours=").Append(chosenColours)
                          .Append("  decisions=").Append(chosenDecisions)
                          .Append("  avgPath=").Append((cells / (float)chosenColours).ToString("0.0"))
                          .AppendLine();
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            total.Stop();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("Classic campaign: " + saved + "/" + totalLevels + " levels in "
                + (total.ElapsedMilliseconds / 1000f).ToString("0.0") + "s"
                + (cancelled ? " (CANCELLED)" : "") + ".\n" + report);
        }

        /// <summary>
        /// Board size and target colour count for a Classic level.
        ///
        /// Three blocks by size; inside each, colours fall from the generous opening value to the
        /// tight closing one. The closing values are the fewest that were actually observed to be
        /// reachable per size (3, 3 and 6 respectively), so the hardest level of each block sits at
        /// the edge of what the generator can produce rather than at an arbitrary number.
        /// </summary>
        private static void ClassicShapeFor(int level, out int size, out int targetColours)
        {
            int from, to, first, last;

            if (level <= 25) { size = 5; from = 5; to = 3; first = 1; last = 25; }
            else if (level <= 60) { size = 6; from = 7; to = 4; first = 26; last = 60; }
            else { size = 7; from = 10; to = 6; first = 61; last = 100; }

            float t = last == first ? 0f : (level - first) / (float)(last - first);
            targetColours = Mathf.RoundToInt(Mathf.Lerp(from, to, t));
        }

        [MenuItem("FreeFlow/Level Generator/Classic/Measure generation 5x5-10x10")]
        public static void MeasureClassicGeneration()
        {
            int[] sizes = { 5, 6, 7, 8, 9, 10 };
            const int targetPerSize = 3;
            const int attemptsPerColourCount = 15;
            const int budgetSteps = 2000000;

            StringBuilder report = new StringBuilder();
            bool cancelled = false;

            EditorUtility.ClearProgressBar(); // sticky cancel flag -- see GenerateLevels31To35
            Debug.Log("MEASURE: starting (blocking; the bar updates every attempt).");

            try
            {
                for (int i = 0; i < sizes.Length && !cancelled; i++)
                {
                    int size = sizes[i];
                    int cells = size * size;
                    bool[,] usable = new bool[size, size];
                    for (int r = 0; r < size; r++)
                    {
                        for (int c = 0; c < size; c++) { usable[r, c] = true; }
                    }

                    System.Random rng = new System.Random(20261115 + size);
                    int colourCount = Mathf.Max(3, cells / 8);
                    int ceiling = Mathf.Min(MaxDistinctColors, cells / 3);
                    int built = 0, colourSum = 0, best = 99, totalAttempts = 0, atThisCount = 0;
                    System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();

                    while (built < targetPerSize && colourCount <= ceiling)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "Measuring generation  (" + (i + 1) + "/" + sizes.Length + ")",
                                size + "x" + size + "  -  trying " + colourCount + " colours  -  attempt "
                                    + (atThisCount + 1) + "/" + attemptsPerColourCount
                                    + "  -  built " + built + "/" + targetPerSize
                                    + "  -  " + (timer.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                                (i + built / (float)targetPerSize) / sizes.Length))
                        { cancelled = true; break; }

                        atThisCount++;
                        totalAttempts++;

                        if (TryGenerateUniqueByRefinement(size, usable, cells, colourCount,
                                MaxDistinctColors, budgetSteps, 3, rng,
                                out LevelData data, out int colours, out int splits))
                        {
                            built++;
                            colourSum += colours;
                            if (colours < best) { best = colours; }
                        }

                        if (atThisCount >= attemptsPerColourCount)
                        {
                            atThisCount = 0;
                            colourCount++;
                        }
                    }
                    timer.Stop();

                    string line = "MEASURE " + size + "x" + size + " (" + cells + " cells): built="
                        + built + "/" + totalAttempts + " attempts in "
                        + (timer.ElapsedMilliseconds / 1000f).ToString("0.0") + "s";
                    if (built > 0)
                    {
                        float avg = colourSum / (float)built;
                        line += "   colours=" + avg.ToString("0.0") + " best=" + best
                             + "   avgPath=" + (cells / avg).ToString("0.0")
                             + "   " + (timer.ElapsedMilliseconds / (float)built / 1000f).ToString("0.0")
                             + "s per level";
                    }
                    else { line += "   (none, escalated to " + colourCount + " colours)"; }

                    Debug.Log(line);
                    report.AppendLine(line);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            Debug.Log("MEASURE complete" + (cancelled ? " (CANCELLED)" : "") + ".\n" + report);
        }

        [MenuItem("FreeFlow/Level Generator/Classic/PROBE generation cost 5x5-10x10")]
        public static void ProbeGenerationCost()
        {
            EditorUtility.ClearProgressBar();

            int[] sizes = { 5, 6, 7, 8, 9, 10 };
            const int budgetSteps = 2000000;   // ~10 s worst case, vs the 20M that ran for minutes
            StringBuilder report = new StringBuilder();
            bool cancelled = false;

            Debug.Log("PROBE2: generation cost, budget " + budgetSteps + " steps per proof.");

            try
            {
                for (int i = 0; i < sizes.Length && !cancelled; i++)
                {
                    int size = sizes[i];
                    int cells = size * size;
                    bool[,] usable = new bool[size, size];
                    for (int r = 0; r < size; r++)
                    {
                        for (int c = 0; c < size; c++) { usable[r, c] = true; }
                    }

                    System.Random rng = new System.Random(20261101 + size);
                    int built = 0, colourSum = 0, best = 99, attempts = 0;
                    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

                    while (built < 3 && attempts < 400 && sw.ElapsedMilliseconds < 45000)
                    {
                        attempts++;
                        if (EditorUtility.DisplayCancelableProgressBar("Generation cost probe",
                                size + "x" + size + " -- attempt " + attempts + ", built " + built + "/3",
                                (i + built / 3f) / sizes.Length))
                        { cancelled = true; break; }

                        // Start CONSTRAINED and merge down, rather than starting loose and
                        // splitting up. The first uniqueness proof dominates cost, and it is
                        // exponentially cheaper on a tight board: a 9x9 at 11 colours did not
                        // finish in twenty million steps, while a 10x10 at 14 finished in two.
                        // Merge-down then recovers the long paths, and its trials are individually
                        // cheap, so paying there instead is the right trade.
                        int startColours = Mathf.Max(3, cells / 6);
                        if (TryGenerateUniqueByRefinement(size, usable, cells, startColours, 20,
                                budgetSteps, 3, rng, out LevelData d, out int colours, out int splits))
                        {
                            built++;
                            colourSum += colours;
                            if (colours < best) { best = colours; }
                        }
                    }
                    sw.Stop();

                    string line = "PROBE2 " + size + "x" + size + " (" + cells + " cells): built=" + built
                        + "/" + attempts + " attempts in " + (sw.ElapsedMilliseconds / 1000f).ToString("0.0") + "s";
                    if (built > 0)
                    {
                        float avg = colourSum / (float)built;
                        line += "   colours=" + avg.ToString("0.0") + " best=" + best
                             + "   avgPath=" + (cells / avg).ToString("0.0")
                             + "   " + (sw.ElapsedMilliseconds / (float)built / 1000f).ToString("0.0") + "s per level";
                    }
                    Debug.Log(line);
                    report.AppendLine(line);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            Debug.Log("PROBE2 complete" + (cancelled ? " (CANCELLED)" : "") + ".\n" + report);
        }

        [MenuItem("FreeFlow/Level Generator/Classic/PROBE refine+merge on full grids")]
        public static void ProbeRefineMerge()
        {
            EditorUtility.ClearProgressBar(); // sticky cancel flag -- see GenerateLevels31To35

            int[] solveSizes = { 8, 9, 10, 11, 12 };
            int[] buildSizes = { 7, 8, 9 };
            int totalSteps = solveSizes.Length + buildSizes.Length;
            int stepDone = 0;
            bool cancelled = false;

            StringBuilder report = new StringBuilder();
            Debug.Log("PROBE: starting -- one uniqueness proof per size, then refine+merge.");

            try
            {
                foreach (int size in solveSizes)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Solver probe",
                            "Uniqueness proof on a full " + size + "x" + size,
                            stepDone / (float)totalSteps))
                    { cancelled = true; break; }

                    bool[,] usable = new bool[size, size];
                    for (int r = 0; r < size; r++)
                    {
                        for (int c = 0; c < size; c++) { usable[r, c] = true; }
                    }

                    System.Random rng = new System.Random(99);
                    int colours = Mathf.Max(4, (size * size) / 7);
                    List<List<(int Row, int Col)>> paths =
                        TryGeneratePathPartition(size, usable, size * size, colours, rng);
                    stepDone++;
                    if (paths == null) { Debug.Log("PROBE " + size + "x" + size + ": partition failed"); continue; }

                    LevelData data = BuildPlainLevelData(size, usable, paths, rng);
                    Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
                    try
                    {
                        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                        PuzzleSolver.SolveResult res = PuzzleSolver.Solve(grid, rows, cols,
                            new PuzzleSolver.SolverOptions(20000000, 2));
                        sw.Stop();
                        string line = "PROBE solve " + size + "x" + size + "/" + colours + "c: "
                            + res.Status + " sols=" + res.SolutionsFound
                            + " exhausted=" + res.SearchExhausted
                            + "  " + sw.ElapsedMilliseconds + "ms, " + res.StepsTaken + " steps";
                        Debug.Log(line);
                        report.AppendLine(line);
                    }
                    finally { DestroyBlockGrid(grid); }
                }

                if (!cancelled)
                {
                    foreach (int size in buildSizes)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar("Solver probe",
                                "refine + merge-down on a full " + size + "x" + size,
                                stepDone / (float)totalSteps))
                        { cancelled = true; break; }

                        bool[,] usable = new bool[size, size];
                        for (int r = 0; r < size; r++)
                        {
                            for (int c = 0; c < size; c++) { usable[r, c] = true; }
                        }

                        int cells = size * size;
                        System.Random rng = new System.Random(4242);
                        int built = 0, colourSum = 0, best = 99;
                        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

                        for (int attempt = 0; attempt < 30 && built < 3 && sw.ElapsedMilliseconds < 60000; attempt++)
                        {
                            if (EditorUtility.DisplayCancelableProgressBar("Solver probe",
                                    size + "x" + size + " -- attempt " + (attempt + 1) + ", built " + built + "/3",
                                    (stepDone + attempt / 30f) / totalSteps))
                            { cancelled = true; break; }

                            int startColours = Mathf.Max(4, cells / 8);
                            if (TryGenerateUniqueByRefinement(size, usable, cells, startColours, 18,
                                    SolverBudgetFor(size), 3, rng, out LevelData d, out int colours, out int splits))
                            {
                                built++;
                                colourSum += colours;
                                if (colours < best) { best = colours; }
                            }
                        }
                        sw.Stop();
                        stepDone++;

                        string line = "PROBE build " + size + "x" + size + " (" + cells + " cells): built=" + built;
                        if (built > 0)
                        {
                            float avg = colourSum / (float)built;
                            line += "  colours=" + avg.ToString("0.0") + " best=" + best
                                 + "  avgPath=" + (cells / avg).ToString("0.0")
                                 + "  " + (sw.ElapsedMilliseconds / (float)built / 1000f).ToString("0.0") + "s each";
                        }
                        else { line += "  (none in " + (sw.ElapsedMilliseconds / 1000f).ToString("0") + "s)"; }
                        Debug.Log(line);
                        report.AppendLine(line);

                        if (cancelled) { break; }
                    }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            Debug.Log("LevelGenerator: PROBE complete" + (cancelled ? " (CANCELLED)" : "") + ".\n" + report);
        }

        [MenuItem("FreeFlow/Level Generator/Classic/Generate Classic 11-35 (6x6)")]
        public static void GenerateClassic11To35()
        {
            GenerateClassicRange(11, 35, 20261010, "Classic 11-35 (6x6)");
        }

        [MenuItem("FreeFlow/Level Generator/Classic/Generate Classic 36-65 (7x7)")]
        public static void GenerateClassic36To65()
        {
            GenerateClassicRange(36, 65, 20261017, "Classic 36-65 (7x7)");
        }

        [MenuItem("FreeFlow/Level Generator/Classic/Generate Classic 66-100 (8x8)")]
        public static void GenerateClassic66To100()
        {
            GenerateClassicRange(66, 100, 20261024, "Classic 66-100 (8x8)");
        }

        /// <summary>
        /// Shared driver for the Classic blocks. One method rather than a copy per range, because
        /// Classic ranges differ only in which levels they cover -- SpecForClassic already holds
        /// everything that varies per level.
        /// </summary>
        private static void GenerateClassicRange(int startLevel, int endLevel, int seed, string label)
        {
            const string levelsFolder = "Assets/Resources/Levels/Classic";

            System.Random rng = new System.Random(seed);
            HashSet<string> seenCanonicalKeys = new HashSet<string>();
            // Dedup against every Classic level already built, not just this block.
            SeedExistingCanonicalKeys(levelsFolder, 1, startLevel - 1, seenCanonicalKeys);

            StringBuilder report = new StringBuilder();
            int savedCount = 0;
            bool cancelled = false;

            EnsureLevelFolder(levelsFolder);
            EditorUtility.ClearProgressBar(); // sticky cancel flag -- see GenerateLevels31To35

            for (int levelNumber = startLevel; levelNumber <= endLevel; levelNumber++)
            {
                GenerationSpec spec = SpecForClassic(levelNumber);
                GeneratedLevel generated = TryGenerateLevel(spec, rng, seenCanonicalKeys,
                    attempt => { cancelled = cancelled || ReportGenerationProgress(label, levelNumber, attempt, spec.MaxAttempts); return cancelled; });
                if (cancelled)
                {
                    report.Append("Classic ").Append(levelNumber).AppendLine(": CANCELLED by user");
                    break;
                }

                if (generated == null)
                {
                    Debug.LogError("LevelGenerator: failed to generate Classic level " + levelNumber +
                        " after " + spec.MaxAttempts + " attempts.");
                    report.Append("Classic ").Append(levelNumber).Append(": FAILED\n");
                    continue;
                }

                SaveLevelAsset(levelsFolder, levelNumber, generated.Data, generated.DifficultyScore);
                savedCount++;
                report.Append("Classic ").Append(levelNumber)
                    .Append(": ").Append(spec.GridSize).Append('x').Append(spec.GridSize)
                    .Append(" colors=").Append(generated.Data.pairCount)
                    .Append(" holes=").Append(spec.BlockedCellCount)
                    .Append(" score=").Append(generated.DifficultyScore.ToString("0.0"))
                    .Append(" solutions=").Append(generated.SolutionsFound)
                    .Append(generated.SolutionsFound == 1 ? " (unique)" : "")
                    .Append('\n');
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("LevelGenerator: " + label + " generation complete -- " + savedCount + "/" +
                (endLevel - startLevel + 1) + " levels saved.\n" + report);
        }

        /// <summary>
        /// Builds a board that is uniquely solvable by REFINEMENT rather than by filtering.
        ///
        /// <b>Why filtering could not reach a full 9x9.</b> Everything before this generated a
        /// random partition, derived dots from its path ends, and asked whether the result happened
        /// to be unique. On a board with holes that works -- the holes pin the routing down. On a
        /// FULL grid there are too many ways to re-route, so a random board almost never is:
        /// measured 0 unique boards in ~230 attempts at a full 8x8, and a single 9x9 attempt costs
        /// 3.6 seconds. Filtering cannot find something that rare.
        ///
        /// <b>Use the ambiguity instead of discarding it.</b> If a solve returns two solutions they
        /// must disagree about some cell. Split the intended path at that cell: one colour becomes
        /// two, the cut becomes a pair of dots, and the routing there is pinned. The alternative
        /// that caused the split cannot survive it, so ambiguity falls monotonically and the loop
        /// terminates.
        ///
        /// Two properties fall out that filtering never had. The board lands on the FEWEST colours
        /// that make it unique -- the same as the longest paths that stay unique, which is the
        /// difficulty axis. And proving uniqueness gets cheaper at every step, since each split
        /// shrinks the search space, so the expensive exhaustive proof only runs on a board that is
        /// already constrained. Finding two solutions is cheap because the search stops at the
        /// second; only the final "exactly one, exhausted" costs, and by then the board is tight.
        /// </summary>
        private static bool TryGenerateUniqueByRefinement(int size, bool[,] usable, int usableCount,
            int startColorCount, int maxColorCount, int solverBudget, int minPathCells,
            System.Random rng, out LevelData data, out int finalColorCount, out int splits,
            int targetColorCount = 2)
        {
            data = default;
            finalColorCount = 0;
            splits = 0;

            // NOTE: an interleaved round-robin builder was tried here and measured WORSE --
            // average tangle 41 against Warnsdorff's 49, max 55 against 78, and it stranded cells
            // in 11 of 25 attempts against 1. Growing every path a cell at a time from scattered
            // seeds gives each one the region around its own seed, which is more compartmentalised
            // rather than less. TryGenerateTangledPartition is kept for the record; do not wire it
            // in again without re-measuring.
            List<List<(int Row, int Col)>> paths =
                TryGeneratePathPartition(size, usable, usableCount, startColorCount, rng);
            if (paths == null) { return false; }

            // Splitting adds a colour each time, so the palette is the real ceiling however
            // generous the caller was.
            maxColorCount = Math.Min(maxColorCount, MaxDistinctColors);
            if (startColorCount > maxColorCount) { return false; }

            int maxSplits = maxColorCount - startColorCount;

            for (int step = 0; step <= maxSplits; step++)
            {
                if (paths.Count > maxColorCount) { return false; }

                LevelData candidate = BuildPlainLevelData(size, usable, paths, rng);
                Block[,] grid = BuildBlockGrid(candidate, out int rows, out int cols);
                try
                {
                    PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, rows, cols,
                        new PuzzleSolver.SolverOptions(solverBudget, 2));

                    if (result.Status != PuzzleSolver.SolveStatus.Solved) { return false; }

                    if (result.SolutionsFound == 1 && result.SearchExhausted)
                    {
                        // Unique -- but refinement only ever ADDS colours, so this is the first
                        // unique board found rather than a good one. Walk back down before
                        // accepting it; see MergeDownWhileUnique.
                        MergeDownWhileUnique(size, usable, paths, solverBudget, minPathCells, rng,
                            targetColorCount);

                        for (int i = 0; i < paths.Count; i++)
                        {
                            if (paths[i].Count < minPathCells) { return false; }
                        }

                        data = BuildPlainLevelData(size, usable, paths, rng);
                        finalColorCount = paths.Count;
                        return true;
                    }

                    if (result.AllSolutions == null || result.AllSolutions.Count < 2) { return false; }

                    if (!TrySplitAtDivergence(paths, result.AllSolutions[0], result.AllSolutions[1], rng))
                    {
                        return false;
                    }
                    splits++;
                }
                finally { DestroyBlockGrid(grid); }
            }

            return false;
        }

        /// <summary>
        /// Drives the colour count back DOWN once the board is unique, by joining paths whose ends
        /// touch and keeping every join that leaves the board still uniquely solvable.
        ///
        /// <b>Why refinement alone is not enough.</b> Splitting only ever ADDS colours, and it stops
        /// the moment uniqueness is reached, so it lands on the first unique board it stumbles into
        /// rather than a good one. On a full 9x9 that was 18+ colours and paths under 4.5 cells --
        /// and more colours means shorter paths means an easier level. Flow Free's own 8x8 boards
        /// are unique at NINE colours with 7.1-cell paths, so such boards plainly exist; the split
        /// loop simply has no way to move toward them.
        ///
        /// Merging is the inverse move. Two paths whose endpoints are adjacent can be joined into
        /// one longer path, spending one fewer colour. The join is kept only if the board is still
        /// uniquely solvable, so this walks downhill to a LOCALLY MINIMAL colour count -- which is
        /// the same thing as locally maximal path length, the axis difficulty actually lives on.
        ///
        /// Affordable only because of the connectivity prune: each trial is a full uniqueness
        /// proof, and those went from minutes to milliseconds on a constrained board.
        /// </summary>
        private static void MergeDownWhileUnique(int size, bool[,] usable,
            List<List<(int Row, int Col)>> paths, int solverBudget, int minPathCells, System.Random rng,
            int stopAtCount = 2)
        {
            // Merge trials get a FRACTION of the solver budget, and there is a hard ceiling on how
            // many are run. Both matter, and for the same reason: every successful merge leaves the
            // board less constrained, so each proof after it is more expensive than the last, and
            // the final round -- the one that tries every remaining join and fails all of them --
            // is the most expensive of all. Unbounded, that made a single 8x8 candidate cost 20-45
            // seconds and the size produced nothing at all.
            //
            // Cutting a trial short is safe rather than merely cheap: an unproven merge is simply
            // not taken, so the board keeps a colour it might have shed. The result is a slightly
            // less aggressive merge, not a wrong one.
            int trialBudget = Math.Max(120000, solverBudget / 8);
            int trialsLeft = 60;

            // stopAtCount lets a caller ask for a SPECIFIC colour count rather than the fewest
            // possible. Difficulty inside a block is ramped by walking this down (fewer colours =
            // longer paths = harder), so the generator has to be able to stop short of the minimum.
            bool progressed = true;
            while (progressed && paths.Count > Math.Max(2, stopAtCount) && trialsLeft > 0)
            {
                progressed = false;

                List<(int A, int B, bool AFront, bool BFront)> joins = FindPossibleJoins(paths);
                for (int i = joins.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (joins[i], joins[j]) = (joins[j], joins[i]);
                }

                for (int k = 0; k < joins.Count && trialsLeft > 0; k++)
                {
                    var join = joins[k];
                    List<(int Row, int Col)> a = paths[join.A];
                    List<(int Row, int Col)> b = paths[join.B];

                    List<(int Row, int Col)> merged = new List<(int, int)>(a.Count + b.Count);
                    // Orient both so the touching ends meet in the middle of the new path.
                    if (join.AFront) { for (int i = a.Count - 1; i >= 0; i--) { merged.Add(a[i]); } }
                    else { merged.AddRange(a); }
                    if (join.BFront) { merged.AddRange(b); }
                    else { for (int i = b.Count - 1; i >= 0; i--) { merged.Add(b[i]); } }

                    List<List<(int Row, int Col)>> trial = new List<List<(int, int)>>();
                    for (int p = 0; p < paths.Count; p++)
                    {
                        if (p == join.A || p == join.B) { continue; }
                        trial.Add(paths[p]);
                    }
                    trial.Add(merged);

                    // Growth refuses to lay a path alongside itself, but a MERGE can still create
                    // one: two paths that never touched themselves become a single path whose two
                    // halves run past each other. Checked before the solver call, because it costs
                    // nothing next to a uniqueness proof.
                    if (PathTouchesItself(merged)) { trialsLeft--; continue; }

                    trialsLeft--;
                    if (!StillUniquelySolvable(size, usable, trial, trialBudget, rng)) { continue; }

                    paths.Clear();
                    paths.AddRange(trial);
                    progressed = true;
                    break;
                }
            }
        }

        /// <summary>
        /// Whether any two cells of one path are adjacent without being consecutive along it --
        /// the link running alongside itself, which the genre forbids and which invalidates the
        /// corner dual law that <see cref="HumanSolver"/> leans on.
        /// </summary>
        private static bool PathTouchesItself(List<(int Row, int Col)> path)
        {
            Dictionary<int, int> indexAt = new Dictionary<int, int>(path.Count);
            for (int i = 0; i < path.Count; i++)
            {
                indexAt[path[i].Row * 1024 + path[i].Col] = i;
            }

            for (int i = 0; i < path.Count; i++)
            {
                // Right and down only; looking at all four would find each pair twice.
                if (Touches(indexAt, path[i].Row, path[i].Col + 1, i)) { return true; }
                if (Touches(indexAt, path[i].Row + 1, path[i].Col, i)) { return true; }
            }
            return false;
        }

        private static bool Touches(Dictionary<int, int> indexAt, int row, int col, int fromIndex)
        {
            if (!indexAt.TryGetValue(row * 1024 + col, out int other)) { return false; }
            return Math.Abs(other - fromIndex) > 1;
        }

        /// <summary>
        /// Every way two paths could be joined: an endpoint of one orthogonally touching an
        /// endpoint of the other. Records which END of each, since the halves have to be oriented
        /// so those ends meet.
        /// </summary>
        private static List<(int A, int B, bool AFront, bool BFront)> FindPossibleJoins(
            List<List<(int Row, int Col)>> paths)
        {
            List<(int, int, bool, bool)> joins = new List<(int, int, bool, bool)>();

            for (int i = 0; i < paths.Count; i++)
            {
                for (int j = i + 1; j < paths.Count; j++)
                {
                    for (int ea = 0; ea < 2; ea++)
                    {
                        (int Row, int Col) endA = ea == 0 ? paths[i][0] : paths[i][paths[i].Count - 1];
                        for (int eb = 0; eb < 2; eb++)
                        {
                            (int Row, int Col) endB = eb == 0 ? paths[j][0] : paths[j][paths[j].Count - 1];
                            int dr = Math.Abs(endA.Row - endB.Row);
                            int dc = Math.Abs(endA.Col - endB.Col);
                            if (dr + dc != 1) { continue; }
                            joins.Add((i, j, ea == 0, eb == 0));
                        }
                    }
                }
            }

            return joins;
        }

        private static bool StillUniquelySolvable(int size, bool[,] usable,
            List<List<(int Row, int Col)>> paths, int solverBudget, System.Random rng)
        {
            LevelData data = BuildPlainLevelData(size, usable, paths, rng);
            Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
            try
            {
                PuzzleSolver.SolveResult result = PuzzleSolver.Solve(grid, rows, cols,
                    new PuzzleSolver.SolverOptions(solverBudget, 2));
                return result.Status == PuzzleSolver.SolveStatus.Solved
                    && result.SolutionsFound == 1
                    && result.SearchExhausted;
            }
            finally { DestroyBlockGrid(grid); }
        }

        /// <summary>
        /// Finds a cell the two solutions disagree about and splits whichever intended path owns
        /// it, turning one colour into two. Returns false when no split is possible -- the only
        /// lever this algorithm has, so the caller treats that as a dead candidate.
        /// </summary>
        private static bool TrySplitAtDivergence(List<List<(int Row, int Col)>> paths,
            List<PuzzleSolver.PairSolution> first, List<PuzzleSolver.PairSolution> second,
            System.Random rng)
        {
            Dictionary<(int, int), int> ownerA = OwnershipMap(first);
            Dictionary<(int, int), int> ownerB = OwnershipMap(second);

            List<(int Row, int Col)> disputed = new List<(int, int)>();
            foreach (KeyValuePair<(int, int), int> kv in ownerA)
            {
                if (ownerB.TryGetValue(kv.Key, out int other) && other != kv.Value)
                {
                    disputed.Add(kv.Key);
                }
            }
            if (disputed.Count == 0) { return false; }

            // Shuffled so repeated runs explore different cuts rather than always taking the first
            // disagreement in scan order.
            for (int i = disputed.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (disputed[i], disputed[j]) = (disputed[j], disputed[i]);
            }

            for (int d = 0; d < disputed.Count; d++)
            {
                for (int p = 0; p < paths.Count; p++)
                {
                    int at = paths[p].IndexOf(disputed[d]);
                    // Both halves need two cells to carry two distinct dots, so the cut cannot sit
                    // at either end or one cell in from it.
                    if (at < 1 || at > paths[p].Count - 3) { continue; }

                    List<(int Row, int Col)> tail = paths[p].GetRange(at + 1, paths[p].Count - at - 1);
                    paths[p] = paths[p].GetRange(0, at + 1);
                    paths.Add(tail);
                    return true;
                }
            }

            return false;
        }

        private static Dictionary<(int, int), int> OwnershipMap(List<PuzzleSolver.PairSolution> solution)
        {
            Dictionary<(int, int), int> map = new Dictionary<(int, int), int>();
            for (int i = 0; i < solution.Count; i++)
            {
                List<(int Row, int Col)> cells = solution[i].Cells;
                for (int c = 0; c < cells.Count; c++) { map[cells[c]] = solution[i].PairId; }
            }
            return map;
        }

        /// <summary>
        /// A LevelData carrying nothing but dots and, optionally, holes -- no rule cells, no walls.
        /// This is what Classic is; see GameMode.
        /// </summary>
        private static LevelData BuildPlainLevelData(int size, bool[,] usable,
            List<List<(int Row, int Col)>> paths, System.Random rng)
        {
            List<PairColorType> palette = PickDistinctColors(paths.Count, rng);

            PairColorType[,] colorGrid = new PairColorType[size, size];

            // The partition IS the solution, and this is the one place that still has it -- every
            // caller downstream sees only the derived dots. Recording it here costs nothing and
            // saves the game from searching for an answer the generator already knew.
            //
            // The stored id is the COLOUR cast to an int, because that is what BuildBlockGrid gives
            // a cell as its pair id; the LevelData pairId column is not read for the primary
            // identity. Getting this wrong is not hypothetical -- RelaxationMetrics read the pairId
            // column, found nothing, and silently reported zero for every board.
            int[,] solutionGrid = new int[size, size];

            for (int p = 0; p < paths.Count; p++)
            {
                (int Row, int Col) a = paths[p][0];
                (int Row, int Col) b = paths[p][paths[p].Count - 1];
                colorGrid[a.Row, a.Col] = palette[p];
                colorGrid[b.Row, b.Col] = palette[p];

                for (int i = 0; i < paths[p].Count; i++)
                {
                    solutionGrid[paths[p][i].Row, paths[p][i].Col] = (int)palette[p];
                }
            }

            LevelData data = new LevelData
            {
                gridSize = (GridSize)size,
                pairCount = paths.Count,
                gridRows = new GridRow[size]
            };

            for (int r = 0; r < size; r++)
            {
                PairColorType[] colorRow = new PairColorType[size];
                BlockType[] typeRow = new BlockType[size];
                int[] solutionRow = new int[size];
                for (int c = 0; c < size; c++)
                {
                    colorRow[c] = colorGrid[r, c];
                    typeRow[c] = usable[r, c] ? BlockType.Normal : BlockType.Blocked;
                    solutionRow[c] = solutionGrid[r, c];
                }
                data.gridRows[r] = new GridRow
                {
                    coloum = colorRow,
                    pairId = new int[size],
                    blockType = typeRow,
                    wallMask = new int[size],
                    requiredEntryDirection = new Direction[size],
                    forcedExitDirection = new Direction[size],
                    secondPairId = new int[size],
                    solutionPairId = solutionRow
                };
            }

            return data;
        }

        /// <summary>
        /// The Classic campaign, levels 11-100. Pure routing: no rule cells, no walls, ever.
        ///
        /// <b>Only three dials exist here</b> -- board size, colour count and hole count -- because
        /// removing mechanics removes every other one. That is the point of the mode (§6.25): a
        /// blocked cell constrains the board exactly as well as a rule does, and costs the player
        /// nothing to remember.
        ///
        /// <b>Difficulty rises by REMOVING holes.</b> Counter-intuitive but measured: fewer holes
        /// means longer paths, which is the metric that actually tracks how a board feels (§6.14).
        /// Each block therefore starts hole-rich and thins out. Board size steps up between blocks,
        /// which resets path length but raises the count of simultaneous pairs -- the same shape
        /// Flow Free's own packs use, where board size increases every few levels.
        ///
        ///   11-35  6x6, 5 colours, 6 -> 3 holes   (path 6.0 -> 6.6, 36% -> 14% of candidates unique)
        ///   36-65  7x7, 6 colours, 9 -> 4 holes   (path 6.7 -> 7.5, 20% -> 4.3%)
        ///   66-100 8x8, 8 colours, 12 -> 8 holes  (path ~6.5 -> 7.0, ~13% -> 3.7%)
        ///
        /// <b>Levels 1-10 are not generated by this</b> and keep the STRICT coverage rule
        /// (RequireEveryPairingCoversBoard) as the tutorial tier, so a beginner can never connect
        /// every pair and be left staring at empty cells. That rule does not survive past 6x6 --
        /// measured 10 clean boards from 477 unique ones at 6x6, and zero at 7x7 and 8x8 -- which
        /// is exactly why it stops there rather than as a matter of taste.
        /// </summary>
        private static GenerationSpec SpecForClassic(int levelNumber)
        {
            int gridSize, colorCount, holesFrom, holesTo, blockFrom, blockTo;
            float pathMin, pathMax;

            if (levelNumber <= 35)
            {
                gridSize = 6; colorCount = 5; holesFrom = 6; holesTo = 3;
                blockFrom = 11; blockTo = 35; pathMin = 5.0f; pathMax = 8.0f;
            }
            else if (levelNumber <= 65)
            {
                gridSize = 7; colorCount = 6; holesFrom = 9; holesTo = 4;
                blockFrom = 36; blockTo = 65; pathMin = 5.5f; pathMax = 9.0f;
            }
            else
            {
                gridSize = 8; colorCount = 8; holesFrom = 12; holesTo = 8;
                blockFrom = 66; blockTo = 100; pathMin = 5.5f; pathMax = 9.0f;
            }

            float t = blockTo == blockFrom ? 0f : (levelNumber - blockFrom) / (float)(blockTo - blockFrom);
            int holes = Mathf.RoundToInt(Mathf.Lerp(holesFrom, holesTo, t));

            return new GenerationSpec
            {
                GridSize = gridSize,
                MinColorCount = colorCount,
                MaxColorCount = colorCount,
                StraightnessBias = Mathf.Lerp(0.45f, 0.28f, t),
                TargetScoreMin = 0f,
                TargetScoreMax = 100f,
                Uniqueness = UniquenessPolicy.Require,
                BlockedCellCount = holes,
                BlockedCellsInteriorOnly = true,
                // Every mechanic count stays 0. Classic is defined by their absence.
                MinWrongRoutes = 3,
                MinPathCells = 3,
                TargetAvgPathMin = pathMin,
                TargetAvgPathMax = pathMax,
                RequireEveryPairingCoversBoard = false,
                // Blocked cells still have to earn their place -- a hole that changes nothing is
                // as much noise as a decorative rule, and this is the only mechanic Classic has.
                RequireMechanicsNecessary = true,
                MaxAttempts = 30000
            };
        }

        /// <summary>
        /// Levels 51-55: the first Mastery range. 8x8, eight colours, ONE mechanic, a heavily
        /// shaped board.
        ///
        /// <b>Second rebuild, and the second playtest correction.</b> The first build used twelve
        /// colours and played too easy (4.8-cell paths). The rebuild fixed the paths by stacking
        /// three mechanics per level, and played *annoying* -- "too much of mechanics". Both were
        /// the same error: reaching for a number to raise instead of asking what makes this genre
        /// hard. Numberlink, which this is, has no mechanics at all; its difficulty is routing.
        ///
        /// <b>Constraint from board shape, not from rules.</b> A board needs constraint to have a
        /// unique solution, but there are two places to get it and they are not equivalent to the
        /// player. A rule cell is something to remember and check; a BLOCKED cell is a hole you
        /// route around, costing nothing to hold in mind. Measured at 8x8 / 8 colours:
        ///
        ///   3 mechanics, 6 blocked : ~1 unique per 1000 attempts, path 7.3, wrong routes 13-399
        ///   1 mechanic, 10 blocked : 95 unique per 908,           path 6.8, wrong routes ~131
        ///   1 mechanic, 14 blocked : 452 unique per 3105,         path 6.3, wrong routes ~40
        ///
        /// One mechanic with more holes holds path length, produces MORE search than the
        /// three-mechanic build, and generates a hundred times more easily. Fourteen holes
        /// over-constrains -- wrong routes collapse to 40 and paths shorten -- so ten is the pick.
        ///
        /// The mechanic still rotates across the range, so Mastery revisits what was taught; it is
        /// kept under the strict rule, since with one mechanic there is no redundancy to excuse.
        /// </summary>
        private static GenerationSpec SpecForLevel51To55(int levelNumber, int gridSize)
        {
            int slot = levelNumber - 51;

            return new GenerationSpec
            {
                GridSize = gridSize,
                MinColorCount = 8,
                MaxColorCount = 8,
                StraightnessBias = Mathf.Lerp(0.4f, 0.28f, slot / 4f),
                TargetScoreMin = 0f,
                TargetScoreMax = 100f,
                Uniqueness = UniquenessPolicy.Require,
                // The difficulty lever for this range. Interior-only, so every one of them forces
                // paths to bend around it rather than quietly shrinking the board at an edge.
                BlockedCellCount = 10,
                BlockedCellsInteriorOnly = true,
                // No walls. They are a holdover from the 7x7 template and have no work left here:
                // with ten holes already shaping the board, removing a wall almost never opens a
                // second solution, so it fails the necessity gate as decoration -- measured at 63
                // rejections out of 65 candidates that reached it, which also made the wall levels
                // ungeneratable. A wall the player cannot tell is doing anything is exactly the
                // clutter this rebuild is removing.
                WallCount = 0,
                CheckpointCount = slot == 0 ? 1 : 0,
                ForbiddenCount  = slot == 1 ? 1 : 0,
                ArrowCount      = slot == 2 ? 1 : 0,
                PermittedCount  = slot == 3 ? 1 : 0,
                OneWayCount     = slot == 4 ? 1 : 0,
                MinNecessaryMechanics = 0,   // one mechanic: it must be load-bearing, no slack
                // Raised from 3: this range measures ~131 wrong routes, so a floor of 3 would not
                // be doing anything. The point of the range is search, so ask for it.
                MinWrongRoutes = 10,
                MinPathCells = 3,
                TargetAvgPathMin = 5.5f,
                TargetAvgPathMax = 9.0f,
                RequireEveryPairingCoversBoard = false,
                RequireMechanicsNecessary = true,
                MaxAttempts = 25000
            };
        }

        /// <summary>
        /// Levels 46-50: Shared Destination introduced -- one cell that is the second dot of TWO
        /// colours, so both of their paths end there. Levels 49-50 recombine it with Wall and
        /// Blocked.
        ///
        /// The ninth and last mechanic, and the second (after Bridge) that had to be built into the
        /// partition rather than laid over one -- see TryGeneratePathPartition's sharedGoals
        /// overload.
        ///
        /// It is also the only mechanic with no necessity check, and that is correct rather than an
        /// omission: RequiredMechanicValidator answers "is the board different without this rule",
        /// but a shared destination is not a rule on a cell, it is the identity of a dot. There is
        /// no board without it to compare against. Nor can it be decorative -- both colours have to
        /// reach that cell or the level is simply unfinished.
        ///
        /// Same 7x7 / 6 colours / 5 blocked shape as the Checkpoint range.
        /// </summary>
        private static GenerationSpec SpecForLevel46To50(int levelNumber, int gridSize)
        {
            bool combineOthers = levelNumber >= 49;

            float t = (levelNumber - 46) / 4f;
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
                SharedGoalCount = 1,
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

            HashSet<(int Row, int Col)> sharedGoals =
                ChooseSharedGoalCells(size, usable, bridges, spec.SharedGoalCount, rng);
            if (sharedGoals.Count < spec.SharedGoalCount) { return false; } // board can't seat them -- retry

            // Grows every colour's path at once rather than cutting one Hamiltonian path. Every
            // consumer below takes these per-path lists, which is what keeps the directional
            // mechanics honest -- see TryGeneratePathPartition.
            List<List<(int Row, int Col)>> segments =
                TryGeneratePathPartition(size, usable, usableCount, colorCount, bridges, sharedGoals, rng);
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

            // A shared destination is a dot for two colours, so it needs BOTH ids recorded: the
            // first sharer in the ordinary colour/pairId columns, the second in secondPairId. The
            // cell can only draw one colour, which is the first -- Block renders a shared goal as a
            // cluster and reads the rest through IsDotFor, so the display colour is not the whole
            // identity here the way it is on an ordinary dot.
            PairColorType[,] colorGrid = new PairColorType[size, size];
            int[,] secondPairIdGrid = new int[size, size];
            for (int s = 0; s < segments.Count; s++)
            {
                (int Row, int Col) start = segments[s][0];
                (int Row, int Col) end = segments[s][segments[s].Count - 1];

                if (colorGrid[start.Row, start.Col] == PairColorType.None)
                {
                    colorGrid[start.Row, start.Col] = palette[s];
                }
                else
                {
                    // Only a shared goal is ever claimed twice; the partition gives every other
                    // cell to one path, and anchoring puts both sharers' seeds at index 0.
                    secondPairIdGrid[start.Row, start.Col] = (int)palette[s];
                }

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
                int[] secondPairIdRow = new int[size];
                for (int c = 0; c < size; c++)
                {
                    colorRow[c] = colorGrid[r, c];
                    typeRow[c] = typeGrid[r, c];
                    wallRow[c] = wallMaskGrid[r, c];
                    entryRow[c] = requiredEntryGrid[r, c];
                    exitRow[c] = forcedExitGrid[r, c];
                    pairIdRow[c] = pairIdGrid[r, c];
                    secondPairIdRow[c] = secondPairIdGrid[r, c];
                }
                data.gridRows[r] = new GridRow
                {
                    coloum = colorRow,
                    pairId = pairIdRow,
                    blockType = typeRow,
                    wallMask = wallRow,
                    requiredEntryDirection = entryRow,
                    forcedExitDirection = exitRow,
                    secondPairId = secondPairIdRow
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
            return TryGeneratePathPartition(size, usable, usableCount, pathCount, bridges, null, rng);
        }

        /// <summary>
        /// As above, plus <paramref name="sharedGoals"/>: cells where TWO colours' paths both end,
        /// the shared-destination mechanic.
        ///
        /// The second construction-time mechanic, and for the same reason as Bridge -- it changes
        /// the shape of the partition rather than decorating one. Here it is the DOTS that change:
        /// a path's two ends become its colour's dots, so two colours sharing a destination means
        /// two paths ending on one cell. No rule laid over a finished partition can produce that.
        ///
        /// Handled with the same node splitting, aimed differently. A shared cell becomes two nodes
        /// as a bridge does, but where a bridge's lanes must be INTERIOR to their paths, a shared
        /// goal's nodes must be ENDPOINTS of theirs. Rather than generating partitions and throwing
        /// away the ones that miss (a bridge's lanes are naturally interior, but nothing makes a
        /// 4-neighbour node naturally terminal), the two nodes are used as path SEEDS and their
        /// paths are anchored: an anchored path may only grow from its tail, so the seed stays at
        /// index 0 and ends up a dot by construction.
        ///
        /// Two properties then come for free. The two nodes are in different paths because each
        /// seeds its own. And the paths arrive on different edges -- the cell each one steps back
        /// through belongs to exactly one path -- which matters because LevelData's own doc caps
        /// sharing at four colours for precisely that reason: a path ending in a cell claims the
        /// edge it arrived through, and a cell has four.
        ///
        /// </summary>
        private static List<List<(int Row, int Col)>> TryGeneratePathPartition(int size, bool[,] usable,
            int usableCount, int pathCount, HashSet<(int Row, int Col)> bridges,
            HashSet<(int Row, int Col)> sharedGoals, System.Random rng)
        {
            return BuildPathPartitionCore(size, usable, usableCount, pathCount, bridges, sharedGoals, rng,
                shortPathProtectionFloor: int.MaxValue);
        }

        /// <summary>
        /// Advanced's mechanic-dependent board construction only, NOT a general-purpose entry
        /// point -- see BuildPathPartitionCore's own doc comment for why this exists and what it
        /// changes. Kept as a distinctly-NAMED method rather than an added parameter on
        /// TryGeneratePathPartition's own overloads: two test files resolve those overloads via
        /// reflection matched on an exact parameter list/count
        /// (LevelGeneratorBridgeTests/LevelGeneratorSharedGoalTests), and an appended optional
        /// parameter silently breaks or, worse, silently MISMATCHES that lookup rather than
        /// failing loudly -- found the hard way, by a real test failure, before this method existed.
        ///
        /// <paramref name="shortPathProtectionFloor"/> defaults to a small protection band rather
        /// than none at all -- an unconditional free-for-all (floor 0) was tried first and a probe
        /// showed it just trades "too uniform" rejections for "too short" ones (a 2-cell link,
        /// StructuralGates.MinPathCells) at low colour counts, since nothing stops one path
        /// starving while others run long. Protecting only paths still under the floor, and letting
        /// anything past it diverge freely, is the hybrid BuildPathPartitionCore's own doc comment
        /// describes.
        /// </summary>
        private static List<List<(int Row, int Col)>> TryGeneratePathPartitionUnbalanced(int size,
            bool[,] usable, int usableCount, int pathCount, System.Random rng,
            int shortPathProtectionFloor = StructuralGates.MinPathCells + 2)
        {
            return BuildPathPartitionCore(size, usable, usableCount, pathCount, null, null, rng,
                shortPathProtectionFloor);
        }

        /// <summary>
        /// The shared implementation behind every TryGeneratePathPartition overload and
        /// TryGeneratePathPartitionUnbalanced. See the public overloads' own doc comments for
        /// Bridge/Shared-Destination node splitting; this comment covers only
        /// <paramref name="shortPathProtectionFloor"/>.
        ///
        /// It replaces path.Count in the growth loop's tie-break with
        /// <c>min(path.Count, shortPathProtectionFloor)</c> -- a "danger" value clamped at the
        /// floor. Two paths already at or past the floor both read as exactly the floor, so ties
        /// between two SAFE paths fall through to pure randomness (no bias); a path still short of
        /// it reads as its real, smaller length, so it keeps winning ties against a safe path (and
        /// against another short one, the shorter of the two wins, protecting whichever is more at
        /// risk first). <c>int.MaxValue</c> recovers the original always-prefer-shortest behaviour
        /// (every real length is below it, so the clamp never engages) -- this is what every
        /// TryGeneratePathPartition overload passes, keeping Classic's and the shipped Advanced 6x6
        /// pack's paths exactly as before. <c>0</c> is the opposite extreme, no protection at all.
        ///
        /// Needed because StructuralGates demands a real spread between the shortest and longest
        /// path (see its own doc comment, and its citation of PuzzleMadison requiring "a deliberate
        /// spread of link lengths"), and a probe on the Advanced pipeline found "too uniform" as the
        /// dominant rejection reason at EVERY colour ratio tried -- the growth heuristic itself, not
        /// the colour count, was fighting the gate. A second probe at floor 0 (no protection) showed
        /// the failure mode just moves to "too short" at low colour counts instead -- the floor
        /// exists to take the fix only as far as it needs to go.
        /// </summary>
        private static List<List<(int Row, int Col)>> BuildPathPartitionCore(int size, bool[,] usable,
            int usableCount, int pathCount, HashSet<(int Row, int Col)> bridges,
            HashSet<(int Row, int Col)> sharedGoals, System.Random rng, int shortPathProtectionFloor)
        {
            if (pathCount < 1) { return null; }

            int bridgeCount = bridges == null ? 0 : bridges.Count;
            int sharedCount = sharedGoals == null ? 0 : sharedGoals.Count;

            // Two colours per shared goal, and each needs its own path.
            if (pathCount < sharedCount * 2) { return null; }

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
            List<int> goalNodes = new List<int>();
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++)
                {
                    if (!usable[r, c]) { continue; }
                    bool bridge = bridgeCount > 0 && bridges.Contains((r, c));
                    bool shared = sharedCount > 0 && sharedGoals.Contains((r, c));
                    if (bridge && shared) { return null; } // a dot is not a crossing; see ChooseSharedGoalCells

                    nodeOfCell[r, c] = cellOfNode.Count;
                    if (shared) { goalNodes.Add(cellOfNode.Count); }
                    cellOfNode.Add((r, c));
                    isLane.Add(bridge);

                    if (!bridge && !shared) { continue; }
                    verticalNodeOfCell[r, c] = cellOfNode.Count;
                    if (shared) { goalNodes.Add(cellOfNode.Count); }
                    cellOfNode.Add((r, c));
                    isLane.Add(bridge);
                }
            }

            int nodeCount = cellOfNode.Count;
            if (nodeCount != usableCount + bridgeCount + sharedCount) { return null; }
            if (nodeCount < pathCount * 2) { return null; }

            int[][] neighbours = BuildPartitionAdjacency(size, usable, bridges, sharedGoals, nodeOfCell,
                verticalNodeOfCell, cellOfNode, bridgeCount, sharedCount);

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

            // Shared-goal nodes are seeded FIRST and their paths anchored, so each stays at index 0
            // and ends up a dot. Nothing else can take them either, since seeding claims them before
            // the first growth step -- which is what keeps a shared cell to exactly two colours.
            List<List<int>> nodePaths = new List<List<int>>();
            bool[] anchored = new bool[pathCount];
            HashSet<int> reserved = new HashSet<int>(goalNodes);
            int nextFree = 0;
            for (int i = 0; i < pathCount; i++)
            {
                int seed;
                if (i < goalNodes.Count)
                {
                    seed = goalNodes[i];
                    anchored[i] = true;
                }
                else
                {
                    while (reserved.Contains(freeNodes[nextFree])) { nextFree++; }
                    seed = freeNodes[nextFree++];
                }

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
                    //
                    // An anchored path is the exception: it grows from its tail only, which is what
                    // pins its shared-goal seed at index 0 and makes that cell a dot rather than
                    // somewhere the path merely passes through.
                    for (int side = anchored[p] ? 1 : 0; side < 2; side++)
                    {
                        int end = side == 0 ? path[0] : path[path.Count - 1];
                        int[] adjacency = neighbours[end];

                        for (int d = 0; d < adjacency.Length; d++)
                        {
                            int candidate = adjacency[d];
                            if (taken[candidate] != -1) { continue; }

                            // Never step somewhere that would put this path alongside itself.
                            if (TouchesOwnPath(neighbours, taken, candidate, p, end)) { continue; }

                            int freeNeighbours = CountFreeNeighbours(neighbours, taken, candidate);

                            // Most-constrained node first; among equally-constrained candidates,
                            // whichever path is more "in danger" of missing shortPathProtectionFloor
                            // breaks ties (see BuildPathPartitionCore's own doc comment) -- clamping
                            // both lengths at the floor means two paths already past it always read
                            // as equal, so the random tie-break below is what decides between them.
                            int thisDanger = Math.Min(path.Count, shortPathProtectionFloor);
                            int bestDanger = Math.Min(bestPathLength, shortPathProtectionFloor);
                            bool better = freeNeighbours < bestFreeNeighbours
                                || (freeNeighbours == bestFreeNeighbours && thisDanger < bestDanger);
                            bool equal = freeNeighbours == bestFreeNeighbours && thisDanger == bestDanger;

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

            // Anchoring should have kept every shared-goal seed at index 0, making it a dot. This
            // re-states that as a check rather than trusting it: the cost is nothing, and a silent
            // regression here produces boards whose shared cell is merely passed through, which
            // nothing downstream would reject -- LevelValidator only ever sees the finished dots.
            for (int i = 0; i < goalNodes.Count; i++)
            {
                int owner = taken[goalNodes[i]];
                if (owner < 0 || nodePaths[owner][0] != goalNodes[i]) { return null; }
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
            HashSet<(int Row, int Col)> bridges, HashSet<(int Row, int Col)> sharedGoals,
            int[,] nodeOfCell, int[,] verticalNodeOfCell,
            List<(int Row, int Col)> cellOfNode, int bridgeCount, int sharedCount)
        {
            int[][] neighbours = new int[cellOfNode.Count][];
            List<int> scratch = new List<int>(8);

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
                    // A shared goal's two nodes carry no such restriction -- either colour may
                    // arrive from any side, and they end up on different edges because the cell
                    // each steps back through belongs to exactly one path.
                    if (onBridge && horizontalStep == verticalLane) { continue; }
                    if (nr < 0 || nr >= size || nc < 0 || nc >= size) { continue; }
                    if (!usable[nr, nc]) { continue; }

                    bool neighbourIsBridge = bridgeCount > 0 && bridges.Contains((nr, nc));
                    if (neighbourIsBridge)
                    {
                        scratch.Add(horizontalStep ? nodeOfCell[nr, nc] : verticalNodeOfCell[nr, nc]);
                        continue;
                    }

                    scratch.Add(nodeOfCell[nr, nc]);

                    // Both of a shared goal's nodes are listed, keeping adjacency symmetric. They
                    // are seeded before the first growth step and so are never free, which is why
                    // listing both cannot let a third colour wander in.
                    if (sharedCount > 0 && sharedGoals.Contains((nr, nc)))
                    {
                        scratch.Add(verticalNodeOfCell[nr, nc]);
                    }
                }

                neighbours[node] = scratch.ToArray();
            }

            return neighbours;
        }

        /// <summary>
        /// Whether growing <paramref name="pathIndex"/> into <paramref name="candidate"/> would put
        /// the path next to a cell of its own that it is not actually connected to.
        ///
        /// <b>Why this is a growth rule and not a filter.</b> "No link touches itself" is half of
        /// the genre's definition of a well-formed Numberlink -- and we were not enforcing it
        /// anywhere. Adding it as a post-hoc gate revealed the scale of the problem rather than
        /// solving it: of the boards this builder produced, <b>88% of 5x5s and 96% of 6x6s were
        /// thrown away for self-touching</b>, and the 7x7 block could not finish inside an hour
        /// because almost everything it made was discarded.
        ///
        /// Generating garbage and filtering it is the wrong shape. Refusing the step costs one
        /// adjacency scan per candidate, and the candidate was about to be scored anyway.
        ///
        /// <paramref name="growingEnd"/> is exempt for the obvious reason: it is the cell we are
        /// growing FROM, so being adjacent to it is the connection itself, not a touch.
        /// </summary>
        private static bool TouchesOwnPath(int[][] neighbours, int[] taken, int candidate,
            int pathIndex, int growingEnd)
        {
            int[] adjacency = neighbours[candidate];
            for (int i = 0; i < adjacency.Length; i++)
            {
                int node = adjacency[i];
                if (node == growingEnd) { continue; }
                if (taken[node] == pathIndex) { return true; }
            }
            return false;
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
        /// Picks cells to serve as shared destinations, before the partition runs -- see
        /// TryGeneratePathPartition's sharedGoals overload for why this cannot come afterwards.
        ///
        /// A cell qualifies if it has at least two usable neighbours, since each colour ending here
        /// arrives through its own edge. It must not already be a bridge: a bridge is a cell paths
        /// pass THROUGH and a shared goal is one they END at, and a cell cannot be both.
        ///
        /// Kept off each other's neighbours for the same reason bridges are -- two adjacent shared
        /// destinations read as one confusing cluster, and they compete for the same arrival edges.
        /// </summary>
        private static HashSet<(int Row, int Col)> ChooseSharedGoalCells(int size, bool[,] usable,
            HashSet<(int Row, int Col)> bridges, int count, System.Random rng)
        {
            HashSet<(int Row, int Col)> chosen = new HashSet<(int, int)>();
            if (count <= 0) { return chosen; }

            List<(int Row, int Col)> candidates = new List<(int, int)>();
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++)
                {
                    if (!usable[r, c]) { continue; }
                    if (bridges != null && bridges.Contains((r, c))) { continue; }

                    int openSides = 0;
                    if (r > 0 && usable[r - 1, c]) { openSides++; }
                    if (r < size - 1 && usable[r + 1, c]) { openSides++; }
                    if (c > 0 && usable[r, c - 1]) { openSides++; }
                    if (c < size - 1 && usable[r, c + 1]) { openSides++; }
                    if (openSides < 2) { continue; }

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

        /// <summary>How many distinct pair colours the palette can supply. Anything that scales a
        /// colour count with board area has to clamp to this -- see MaxDistinctColors' one-line
        /// story in PickDistinctColors.</summary>
        internal static int MaxDistinctColors
        {
            get { return Enum.GetValues(typeof(PairColorType)).Length - 1; } // less None
        }

        private static List<PairColorType> PickDistinctColors(int count, System.Random rng)
        {
            List<PairColorType> pool = new List<PairColorType>((PairColorType[])Enum.GetValues(typeof(PairColorType)));
            pool.Remove(PairColorType.None);

            // Asking for more colours than exist used to walk off the end of the pool with a bare
            // IndexOutOfRange from deep inside a generation loop, which says nothing about the
            // cause. Any caller scaling colour count with board area can hit this, so it names the
            // problem instead.
            if (count > pool.Count)
            {
                throw new InvalidOperationException("LevelGenerator: asked for " + count +
                    " distinct colours but PairColorType defines only " + pool.Count +
                    ". Add entries to PairColorType and PairColorData, or lower the colour count.");
            }

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

        public static Block[,] BuildBlockGrid(LevelData data, out int rows, out int cols)
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
                int[] secondPairIdRow = data.gridRows[r].secondPairId;
                int[] thirdPairIdRow = data.gridRows[r].thirdPairId;
                int[] fourthPairIdRow = data.gridRows[r].fourthPairId;
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

                        // The extra dot identities of a shared destination. BoardGenerator reads
                        // all three at runtime, so this must too: without them a shared dot looks
                        // like an ordinary one-colour dot here, the pairs sharing it appear to have
                        // a single dot each, and every candidate is validated against a board that
                        // is not the one the game will build. Same omission that once made
                        // Forbidden's pairId a no-op offline -- see the class doc on BoardGenerator.
                        int second = (secondPairIdRow != null && c < secondPairIdRow.Length) ? secondPairIdRow[c] : 0;
                        int third = (thirdPairIdRow != null && c < thirdPairIdRow.Length) ? thirdPairIdRow[c] : 0;
                        int fourth = (fourthPairIdRow != null && c < fourthPairIdRow.Length) ? fourthPairIdRow[c] : 0;
                        if (second != 0) { SetField(block, "secondPairId", second); }
                        if (third != 0) { SetField(block, "thirdPairId", third); }
                        if (fourth != 0) { SetField(block, "fourthPairId", fourth); }
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

        public static void DestroyBlockGrid(Block[,] grid)
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

        /// <summary>Creates Resources/Levels/&lt;Mode&gt; if it is not there yet.</summary>
        /// <summary>
        /// Creates <paramref name="folder"/> and any missing parents.
        ///
        /// The first version hardcoded "Assets/Resources/Levels" as the parent and created only the
        /// last segment, which silently produced the WRONG folder for anything nested: asking for
        /// Levels/Classic/5x5 made Levels/5x5. Per-size packs are two deep, so it walks the path.
        /// </summary>
        private static void EnsureLevelFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) { return; }

            string[] parts = folder.Split('/');
            string path = parts[0];                       // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = path + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) { AssetDatabase.CreateFolder(path, parts[i]); }
                path = next;
            }
        }

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
