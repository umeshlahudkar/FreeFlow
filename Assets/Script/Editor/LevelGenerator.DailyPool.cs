using System.Collections.Generic;
using System.IO;
using System.Text;
using FreeFlow.Enums;
using UnityEditor;
using UnityEngine;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// The daily-challenge pool: 365 Classic boards, one per day of the year, generated into
    /// <c>Assets/Resources/Levels/Daily/{N}x{N}</c> and NOT yet read by anything in the game.
    ///
    /// Same construction as the shipped Classic packs (<see cref="BuildSizePack"/>) -- refinement to
    /// a proven-unique board, the 8M-step re-proof, stored answer checked against the solver,
    /// StructuralGates, two-stage DifficultyModel scoring, stratified selection -- with four
    /// differences, each because a daily pool is a different product from a pack:
    ///
    /// <b>Never a board the player already owns.</b> Every candidate's canonical key (rotation,
    /// reflection and colour relabel all collapse -- see LevelCanonicalizer) is checked against every
    /// shipped Classic and Advanced level and every other daily size, not just the run's own boards.
    /// The seed is new for the same reason: the pack seeds (20260901 + size) would regenerate the
    /// packs' own pools.
    ///
    /// <b>Medium to hard only.</b> The floor is the SHIPPED pack's level 34 score for that size --
    /// where DailyChallengeSelector's middle band starts -- so "medium" means the same thing it
    /// already means to a player who has been through the pack. The ramp is stratified from that
    /// floor to the hardest board found. DifficultyModel is a ranking within one size, which is
    /// exactly how it is used here: a 7x7 daily is compared only against the 7x7 pack.
    ///
    /// <b>Resumable.</b> The gathered pool is checkpointed to Library/DailyPoolCache every 25 boards
    /// and on Cancel, so an 8x8 or 9x9 run that is cancelled, crashes or loses a domain reload
    /// resumes from what it had instead of starting over. Each resume reseeds (seedRound), since
    /// replaying the same RNG would only rediscover the boards already held.
    ///
    /// <b>Observable from outside the Editor.</b> A synchronous menu item freezes the Editor, and
    /// MCP with it, so progress is also appended once a minute to Library/DailyPoolCache/progress.log.
    ///
    /// One size per menu item, on purpose: the sizes are built one at a time, each verified before
    /// the next starts.
    /// </summary>
    public static partial class LevelGenerator
    {
        private const string DailyPoolRoot = "Assets/Resources/Levels/Daily";
        private const string DailyCacheDir = "Library/DailyPoolCache";
        private const string DailyProgressLog = DailyCacheDir + "/progress.log";

        // Level of the shipped pack whose score is the daily floor -- the first level of
        // DailyChallengeSelector's middle band (100 / 3 + 1).
        private const int DailyMediumFloorLevel = 34;

        // Stage one leaves relaxation out and renormalises, so its score is not the final score.
        // Boards within this many points below the floor still go to stage two rather than being
        // dropped on a cheap estimate.
        private const float DailyStageOneMargin = 10f;

        private const int DailySeedBase = 20270000;
        private const int DailyMaxAttempts = 400000;

        // 0.9 rather than GenerationDutyCycle's attended 1f: the 8x8 and 9x9 runs are hours long
        // and will be left alone (see GenerationDutyCycle's own note on unattended runs).
        private const float DailyDutyCycle = 0.9f;

        private struct DailyPackSpec
        {
            public int Size;
            public int Count;
            public int PoolTarget;
            public int CellsPerColour;
        }

        // Colour ratios are the shipped packs' own (BuildPack6x6..9x9). Pools are scaled from the
        // packs' by level count and then roughly doubled: only the upper two thirds of the score
        // range is usable here, and a thin pool shows up as flat stretches in the ramp.
        private static readonly DailyPackSpec[] DailyPacks =
        {
            new DailyPackSpec { Size = 6, Count = 60,  PoolTarget = 1200, CellsPerColour = CellsPerColourTarget },
            new DailyPackSpec { Size = 7, Count = 125, PoolTarget = 2600, CellsPerColour = CellsPerColourTarget },
            new DailyPackSpec { Size = 8, Count = 125, PoolTarget = 1600, CellsPerColour = 9 },
            // 55 for the regular calendar plus one for Feb 29 (added by AddDailyExtra, so the
            // first 55 stay exactly as built) -- 366 in all.
            new DailyPackSpec { Size = 9, Count = 56,  PoolTarget = 900,  CellsPerColour = 9 },
        };

        private static bool dailyRunQueued;

        [MenuItem("FreeFlow/Level Generator/Daily Pool/1. Build 6x6 (60)")]
        public static void BuildDaily6x6() { QueueDailyPack(0); }

        [MenuItem("FreeFlow/Level Generator/Daily Pool/2. Build 7x7 (125)")]
        public static void BuildDaily7x7() { QueueDailyPack(1); }

        [MenuItem("FreeFlow/Level Generator/Daily Pool/3. Build 8x8 (125)")]
        public static void BuildDaily8x8() { QueueDailyPack(2); }

        [MenuItem("FreeFlow/Level Generator/Daily Pool/4. Build 9x9 (56)")]
        public static void BuildDaily9x9() { QueueDailyPack(3); }

        [MenuItem("FreeFlow/Level Generator/Daily Pool/5. Add missing 9x9 levels (keeps existing)")]
        public static void AddDaily9x9Missing() { QueueDailyExtra(3); }

        [MenuItem("FreeFlow/Level Generator/Daily Pool/Verify all built packs")]
        public static void VerifyDailyPool()
        {
            HashSet<string> shipped = ShippedCanonicalKeys();
            HashSet<string> acrossDaily = new HashSet<string>();
            for (int i = 0; i < DailyPacks.Length; i++)
            {
                if (!AssetDatabase.IsValidFolder(DailyFolder(DailyPacks[i].Size))) { continue; }
                VerifyDailyPack(DailyPacks[i], shipped, acrossDaily);
            }
        }

        /// <summary>
        /// Deferred to the next editor tick so the caller -- a menu click, or an MCP
        /// execute_menu_item -- returns immediately instead of blocking for the whole run.
        /// </summary>
        private static void QueueDailyPack(int index)
        {
            if (dailyRunQueued)
            {
                Debug.LogError("Daily pool: a build is already queued or running -- one pack at a time.");
                return;
            }
            dailyRunQueued = true;
            EditorApplication.delayCall += () =>
            {
                try { BuildDailyPack(index); }
                finally { dailyRunQueued = false; }
            };
        }

        private static void QueueDailyExtra(int index)
        {
            if (dailyRunQueued)
            {
                Debug.LogError("Daily pool: a build is already queued or running -- one pack at a time.");
                return;
            }
            dailyRunQueued = true;
            EditorApplication.delayCall += () =>
            {
                try { AddDailyExtra(index); }
                finally { dailyRunQueued = false; }
            };
        }

        /// <summary>
        /// Tops a pack up to its spec's Count WITHOUT touching the levels already written: the new
        /// ones are appended as Level_{n+1}.. and drawn from the cached pool, which is already
        /// proven-unique and structurally sound, so nothing is regenerated. Each candidate is
        /// canonically checked against every shipped and daily level (this pack's included), cheap-
        /// scored, and only the strongest cheap scores pay for the full model; the first to clear
        /// the medium floor is kept. The whole pack is then re-verified from disk.
        ///
        /// Exists for the Feb 29 level: the calendar grew from 365 to 366 after 9x9 had been built,
        /// and a full rebuild would have re-selected (and so changed) all 55 existing levels.
        /// </summary>
        private static void AddDailyExtra(int index)
        {
            DailyPackSpec spec = DailyPacks[index];
            int size = spec.Size;
            string folder = DailyFolder(size);
            string label = size + "x" + size + " extra";

            int have = 0;
            while (AssetDatabase.LoadAssetAtPath<SingleLevelDataSO>(folder + "/Level_" + (have + 1) + ".asset") != null) { have++; }
            int missing = spec.Count - have;

            void Log(string phase, string detail, bool toConsole)
            {
                string line = "[" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] DAILY "
                    + label + " | " + phase + " | " + detail;
                File.AppendAllText(DailyProgressLog, line + "\n");
                if (toConsole) { Debug.Log(line); }
            }

            if (missing <= 0)
            {
                Log("NOTHING TO DO", folder + " already holds " + have + " of " + spec.Count, true);
                return;
            }

            float floor = ShippedScore(size, DailyMediumFloorLevel);
            DailyPoolCache cache = LoadDailyCache(size);
            if (floor <= 0f || cache.pool.Count == 0)
            {
                Log("FAILED", "no medium floor or no cached pool for " + size + "x" + size + " -- run the full build instead", true);
                return;
            }

            EditorUtility.DisplayProgressBar("Daily pool " + label, "Loading canonical keys of every shipped and daily level...", 0f);
            HashSet<string> taken = ShippedCanonicalKeys();
            for (int i = 0; i < DailyPacks.Length; i++) { AddFolderKeys(DailyFolder(DailyPacks[i].Size), taken); }

            Log("START", "have " + have + ", adding " + missing + " from a cached pool of " + cache.pool.Count
                + ", medium floor " + floor.ToString("0.0"), true);

            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            CpuThrottle throttle = new CpuThrottle(DailyDutyCycle);
            int added = 0, checkedCheap = 0, checkedFull = 0, skippedTaken = 0;
            bool cancelled = false;

            try
            {
                // Cheap-score unused boards until there are enough strong candidates to be
                // confident one clears the floor under the full model.
                List<(float Score, LevelData Data)> strong = new List<(float, LevelData)>();
                int wantStrong = missing * 8;
                for (int i = 0; i < cache.pool.Count && strong.Count < wantStrong; i++)
                {
                    throttle.Tick();
                    string key = CanonicalKey(cache.pool[i]);
                    if (taken.Contains(key)) { skippedTaken++; continue; }

                    if (EditorUtility.DisplayCancelableProgressBar("Daily pool " + label + "  -  quick scoring unused boards",
                            "board " + (i + 1) + "/" + cache.pool.Count + " | " + strong.Count + "/" + wantStrong
                            + " strong candidates | " + skippedTaken + " already used", 0.4f * strong.Count / wantStrong))
                    { cancelled = true; break; }

                    DifficultyModel.Profile p = DifficultyModel.Measure(cache.pool[i], 14, 2000000, false);
                    checkedCheap++;
                    if (p.Valid && p.WellFormed && p.Score >= floor + DailyStageOneMargin) { strong.Add((p.Score, cache.pool[i])); }
                }
                Log("CANDIDATES", strong.Count + " strong candidates from " + checkedCheap + " quick-scored ("
                    + skippedTaken + " skipped as already used)", false);

                // Mid-range first rather than hardest first: the hardest boards cost the most to
                // score, and an extra day only needs to sit inside the medium-to-hard band.
                strong.Sort((a, b) => a.Score.CompareTo(b.Score));
                for (int k = 0; k < strong.Count && added < missing && !cancelled; k++)
                {
                    int pick = strong.Count / 2 + ((k % 2 == 0) ? k / 2 : -(k / 2 + 1));
                    if (pick < 0 || pick >= strong.Count) { continue; }
                    throttle.Tick();

                    if (EditorUtility.DisplayCancelableProgressBar("Daily pool " + label + "  -  full difficulty model",
                            "candidate " + (k + 1) + "/" + strong.Count + " | added " + added + "/" + missing
                            + " | " + FormatDuration(clock.Elapsed.TotalSeconds) + " elapsed", 0.4f + 0.6f * k / strong.Count))
                    { cancelled = true; break; }

                    LevelData board = strong[pick].Data;
                    DifficultyModel.Profile p = DifficultyModel.Measure(board);
                    checkedFull++;
                    Log("SCORE-2", "candidate " + (k + 1) + ": " + (p.Valid && p.WellFormed ? p.Score.ToString("0.0") : "not well-formed")
                        + (p.Valid && p.WellFormed && p.Score >= floor ? " -> KEPT" : ""), false);
                    if (!p.Valid || !p.WellFormed || p.Score < floor) { continue; }

                    string key = CanonicalKey(board);
                    if (!taken.Add(key)) { continue; }
                    SaveLevelAsset(folder, have + added + 1, board, p.Score);
                    added++;
                    Log("WRITTEN", folder + "/Level_" + (have + added) + " score " + p.Score.ToString("0.0")
                        + ", " + board.pairCount + " colours", true);
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally { EditorUtility.ClearProgressBar(); }

            if (added < missing)
            {
                Log(cancelled ? "CANCELLED" : "SHORT", "added " + added + " of " + missing + " after " + checkedFull
                    + " full scores -- re-run to continue", true);
                return;
            }

            HashSet<string> acrossDaily = new HashSet<string>();
            for (int i = 0; i < DailyPacks.Length; i++) { if (i != index) { AddFolderKeys(DailyFolder(DailyPacks[i].Size), acrossDaily); } }
            bool clean = VerifyDailyPack(spec, ShippedCanonicalKeys(), acrossDaily);
            Log(clean ? "DONE" : "DONE WITH PROBLEMS", "added " + added + ", pack now " + spec.Count + ", verification "
                + (clean ? "CLEAN" : "FOUND PROBLEMS -- see Console") + ", " + FormatDuration(clock.Elapsed.TotalSeconds), true);
        }

        private static string DailyFolder(int size) { return DailyPoolRoot + "/" + size + "x" + size; }

        private static string DailyCachePath(int size) { return DailyCacheDir + "/" + size + "x" + size + "_pool.json"; }

        [System.Serializable]
        private sealed class DailyPoolCache
        {
            public int size;
            public int attempts;
            public int seedRound;
            public int poolTarget;
            public List<LevelData> pool = new List<LevelData>();
        }

        private static void BuildDailyPack(int index)
        {
            DailyPackSpec spec = DailyPacks[index];
            int size = spec.Size;
            int cells = size * size;
            string label = size + "x" + size + " (pack " + (index + 1) + " of " + DailyPacks.Length + ")";
            string folder = DailyFolder(size);

            Directory.CreateDirectory(DailyCacheDir);
            EnsureLevelFolder(folder);
            EditorUtility.ClearProgressBar();

            System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();
            long lastLogMs = long.MinValue / 2;
            void Progress(string phase, string detail, bool force)
            {
                long now = total.ElapsedMilliseconds;
                if (!force && now - lastLogMs < 60000) { return; }
                lastLogMs = now;
                string line = "[" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] DAILY "
                    + label + " | " + phase + " | " + detail + " | elapsed " + FormatDuration(now / 1000.0);
                File.AppendAllText(DailyProgressLog, line + "\n");
                if (force) { Debug.Log(line); }
            }

            // ---- the medium floor, from the shipped pack ------------------------------------
            string floorPath = "Assets/Resources/Levels/Classic/" + size + "x" + size
                + "/Level_" + DailyMediumFloorLevel + ".asset";
            SingleLevelDataSO floorLevel = AssetDatabase.LoadAssetAtPath<SingleLevelDataSO>(floorPath);
            if (floorLevel == null || floorLevel.levelData.difficultyScore <= 0f)
            {
                Progress("FAILED", "no scored shipped level at " + floorPath + " to take the medium floor from", true);
                Debug.LogError("Daily pool " + label + ": cannot read the medium floor from " + floorPath);
                return;
            }
            float floor = floorLevel.levelData.difficultyScore;

            // ---- everything a daily board must not duplicate ---------------------------------
            EditorUtility.DisplayProgressBar("Daily pool " + label, "Loading canonical keys of every shipped level...", 0f);
            HashSet<string> existing = ShippedCanonicalKeys();
            int shippedCount = existing.Count;
            for (int i = 0; i < DailyPacks.Length; i++)
            {
                if (i == index) { continue; }   // this size's own output is about to be replaced
                AddFolderKeys(DailyFolder(DailyPacks[i].Size), existing);
            }

            // ---- resume the pool, if a previous run left one ------------------------------
            DailyPoolCache cache = LoadDailyCache(size);
            HashSet<string> runKeys = new HashSet<string>();
            int droppedFromCache = 0;
            if (cache.pool.Count > 0)
            {
                List<LevelData> kept = new List<LevelData>(cache.pool.Count);
                for (int i = 0; i < cache.pool.Count; i++)
                {
                    string key = CanonicalKey(cache.pool[i]);
                    if (existing.Contains(key) || !runKeys.Add(key)) { droppedFromCache++; continue; }
                    kept.Add(cache.pool[i]);
                }
                cache.pool = kept;
                cache.seedRound++;
            }
            int poolTarget = Mathf.Max(spec.PoolTarget, cache.poolTarget);
            cache.size = size;
            cache.poolTarget = poolTarget;

            Progress("START", "target " + spec.Count + " levels, pool " + poolTarget
                + ", medium floor " + floor.ToString("0.0") + " (shipped L" + DailyMediumFloorLevel + ")"
                + ", " + shippedCount + " shipped keys loaded"
                + (cache.pool.Count > 0 ? ", resuming with " + cache.pool.Count + " cached boards"
                    + (droppedFromCache > 0 ? " (" + droppedFromCache + " dropped as duplicates)" : "") : ""), true);

            System.Random rng = new System.Random(DailySeedBase + size * 1000 + cache.seedRound);
            CpuThrottle throttle = new CpuThrottle(DailyDutyCycle);
            bool cancelled = false;

            int generated = 0, dupExisting = 0, dupRun = 0, unsound = 0, notUnique = 0, badSolution = 0;
            int keptAtStart = cache.pool.Count, sinceSave = 0;
            System.Diagnostics.Stopwatch gatherClock = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // ---- stage A: gather distinct, proven-unique, structurally sound boards -----
                for (; cache.attempts < DailyMaxAttempts && cache.pool.Count < poolTarget; cache.attempts++)
                {
                    int attempt = cache.attempts;
                    throttle.Tick();

                    if ((attempt % 8) == 0)
                    {
                        int keptNow = cache.pool.Count - keptAtStart;
                        double secs = gatherClock.Elapsed.TotalSeconds;
                        string eta = keptNow > 0
                            ? "~" + FormatDuration((poolTarget - cache.pool.Count) * secs / keptNow) + " left"
                            : "estimating";
                        string detail = "kept " + cache.pool.Count + "/" + poolTarget
                            + " | attempt " + attempt.ToString("N0")
                            + " | dupes " + (dupExisting + dupRun) + " (" + dupExisting + " vs shipped)"
                            + " | unsound " + unsound
                            + (notUnique > 0 ? " | NOT UNIQUE " + notUnique : "")
                            + (badSolution > 0 ? " | BAD SOLUTION " + badSolution : "")
                            + " | " + eta;

                        Progress("GATHER", detail, false);
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "Daily pool " + label + "  -  step 1/3: gathering unique boards",
                                detail + " | " + FormatDuration(total.Elapsed.TotalSeconds) + " elapsed",
                                0.6f * cache.pool.Count / poolTarget))
                        { cancelled = true; break; }
                    }

                    int colours = Mathf.Max(3, cells / spec.CellsPerColour) + (attempt % ColourSweepWidth);
                    if (colours > MaxDistinctColors) { continue; }

                    bool[,] usable = new bool[size, size];
                    for (int r = 0; r < size; r++) { for (int c = 0; c < size; c++) { usable[r, c] = true; } }

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
                        if (existing.Contains(key)) { dupExisting++; continue; }
                        if (!runKeys.Add(key)) { dupRun++; continue; }

                        // Re-proved at 8M steps, exactly as the packs are: refinement proved it at
                        // 2M, and a construction-time guarantee is re-checked against the solve.
                        PuzzleSolver.SolveResult solved = PuzzleSolver.Solve(grid, rows, cols,
                            new PuzzleSolver.SolverOptions(8000000, 2));

                        bool unique = solved.Status == PuzzleSolver.SolveStatus.Solved
                            && solved.SolutionsFound == 1
                            && solved.SearchExhausted;
                        if (!unique) { notUnique++; }

                        if (unique && !StoredSolutionMatchesSolver(data, solved, rows, cols))
                        {
                            badSolution++;
                            unique = false;
                        }

                        keep = unique && StructuralGates.Evaluate(solved, rows * cols).Passed;
                    }
                    finally { DestroyBlockGrid(grid); }

                    if (!keep) { unsound++; continue; }

                    cache.pool.Add(data);
                    if (++sinceSave >= 25) { SaveDailyCache(cache); sinceSave = 0; }
                }

                SaveDailyCache(cache);

                string gatherReport = "gathered this run: " + generated + " generated, "
                    + dupExisting + " dupes of shipped levels, " + dupRun + " dupes within run, "
                    + unsound + " unsound, " + notUnique + " not unique, " + badSolution + " bad solution; "
                    + "pool now " + cache.pool.Count + " after " + cache.attempts.ToString("N0") + " attempts";

                if (cancelled)
                {
                    Progress("CANCELLED", gatherReport + " -- pool cached, re-run the same menu item to resume", true);
                    return;
                }
                Progress("GATHER DONE", gatherReport, true);

                if (cache.pool.Count < spec.Count)
                {
                    Progress("FAILED", "only " + cache.pool.Count + " boards for " + spec.Count + " levels", true);
                    Debug.LogError("Daily pool " + label + ": only " + cache.pool.Count + " boards. " + gatherReport);
                    return;
                }

                // ---- stage B: cheap score over the whole pool -------------------------------
                List<int> candIndex = new List<int>();
                List<float> candScore = new List<float>();
                int stageOneWellFormed = 0;
                for (int i = 0; i < cache.pool.Count; i++)
                {
                    throttle.Tick();
                    if ((i % 16) == 0)
                    {
                        string detail = "scored " + i + "/" + cache.pool.Count
                            + " | " + candIndex.Count + " near or above the medium floor " + floor.ToString("0.0");
                        Progress("SCORE-1", detail, false);
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "Daily pool " + label + "  -  step 2/3: quick difficulty score",
                                detail + " | " + FormatDuration(total.Elapsed.TotalSeconds) + " elapsed",
                                0.6f + 0.1f * i / cache.pool.Count))
                        { cancelled = true; break; }
                    }

                    DifficultyModel.Profile p = DifficultyModel.Measure(cache.pool[i], 14, 2000000, false);
                    if (!p.Valid || !p.WellFormed) { continue; }
                    stageOneWellFormed++;
                    if (p.Score >= floor - DailyStageOneMargin)
                    {
                        candIndex.Add(i);
                        candScore.Add(p.Score);
                    }
                }
                if (cancelled)
                {
                    Progress("CANCELLED", "during quick scoring -- pool is cached, re-run to resume", true);
                    return;
                }
                Progress("SCORE-1 DONE", stageOneWellFormed + " well-formed, " + candIndex.Count
                    + " within " + DailyStageOneMargin + " of the floor or above", true);

                // Stratified finalists first so stage two sees the whole upper range, then the
                // rest hardest-first until there is a comfortable surplus to stratify from.
                List<int> order = StratifiedIndices(candScore, Mathf.Min(candScore.Count, spec.Count * 4));
                HashSet<int> inOrder = new HashSet<int>(order);
                List<int> rest = new List<int>();
                for (int i = 0; i < candScore.Count; i++) { if (!inOrder.Contains(i)) { rest.Add(i); } }
                rest.Sort((a, b) => candScore[b].CompareTo(candScore[a]));
                order.AddRange(rest);
                int mustScore = inOrder.Count;

                // ---- stage C: full model, keep only medium-to-hard ------------------------
                List<(float Score, LevelData Data)> passing = new List<(float, LevelData)>();
                int scoredFull = 0;
                for (int k = 0; k < order.Count; k++)
                {
                    if (k >= mustScore && passing.Count >= spec.Count * 2) { break; }
                    throttle.Tick();

                    if ((k % 4) == 0)
                    {
                        string detail = "scored " + k + "/" + mustScore + (k >= mustScore ? " (+extra)" : "")
                            + " | " + passing.Count + " at or above floor " + floor.ToString("0.0")
                            + " (need " + spec.Count + ")";
                        Progress("SCORE-2", detail, false);
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "Daily pool " + label + "  -  step 3/3: full difficulty model",
                                detail + " | " + FormatDuration(total.Elapsed.TotalSeconds) + " elapsed",
                                0.7f + 0.3f * Mathf.Min(1f, (float)k / Mathf.Max(1, mustScore))))
                        { cancelled = true; break; }
                    }

                    LevelData board = cache.pool[candIndex[order[k]]];
                    DifficultyModel.Profile p = DifficultyModel.Measure(board);
                    scoredFull++;
                    if (p.Valid && p.WellFormed && p.Score >= floor) { passing.Add((p.Score, board)); }
                }
                if (cancelled)
                {
                    Progress("CANCELLED", "during full scoring -- pool is cached, re-run to resume", true);
                    return;
                }

                if (passing.Count < spec.Count)
                {
                    // Grow the next run's pool target so a re-run gathers more instead of
                    // rescoring the same boards to the same shortfall.
                    cache.poolTarget = cache.pool.Count + Mathf.Max(spec.PoolTarget / 2, (spec.Count - passing.Count) * 8);
                    SaveDailyCache(cache);
                    Progress("SHORT", "only " + passing.Count + " medium-to-hard boards of " + spec.Count
                        + " needed; pool target raised to " + cache.poolTarget + " -- re-run to gather more", true);
                    Debug.LogError("Daily pool " + label + ": only " + passing.Count + " boards at or above "
                        + floor.ToString("0.0") + ", needed " + spec.Count + ". Re-run the same menu item to extend the pool.");
                    return;
                }

                // ---- write -----------------------------------------------------------------
                List<(float Score, LevelData Data)> chosen = SelectStratified(passing, spec.Count);
                for (int i = 0; i < chosen.Count; i++)
                {
                    SaveLevelAsset(folder, i + 1, chosen[i].Data, chosen[i].Score);
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Dictionary<int, int> pairMix = new Dictionary<int, int>();
                for (int i = 0; i < chosen.Count; i++)
                {
                    int pc = chosen[i].Data.pairCount;
                    pairMix.TryGetValue(pc, out int had);
                    pairMix[pc] = had + 1;
                }
                List<int> pcs = new List<int>(pairMix.Keys);
                pcs.Sort();
                StringBuilder mix = new StringBuilder();
                foreach (int pc in pcs) { mix.Append(pairMix[pc]).Append('x').Append(pc).Append("c "); }

                string summary = chosen.Count + " levels written to " + folder
                    + " | scores " + chosen[0].Score.ToString("0.0") + " -> " + chosen[chosen.Count - 1].Score.ToString("0.0")
                    + " (floor " + floor.ToString("0.0") + ", shipped L100 "
                    + ShippedScore(size, 100).ToString("0.0") + ")"
                    + " | colours " + mix.ToString().Trim()
                    + " | full-scored " + scoredFull + ", " + passing.Count + " passed the floor";
                Progress("WRITTEN", summary, true);
                File.WriteAllText(DailyCacheDir + "/" + size + "x" + size + "_report.txt",
                    summary + "\n" + gatherReport + "\n");
            }
            finally { EditorUtility.ClearProgressBar(); }

            // ---- verify what was written, from disk --------------------------------------
            HashSet<string> shippedForVerify = ShippedCanonicalKeys();
            HashSet<string> acrossDaily = new HashSet<string>();
            for (int i = 0; i < DailyPacks.Length; i++)
            {
                if (i != index) { AddFolderKeys(DailyFolder(DailyPacks[i].Size), acrossDaily); }
            }
            bool clean = VerifyDailyPack(spec, shippedForVerify, acrossDaily);
            Progress(clean ? "DONE" : "DONE WITH PROBLEMS",
                "verification " + (clean ? "CLEAN" : "FOUND PROBLEMS -- see Console") + ", total "
                + FormatDuration(total.Elapsed.TotalSeconds), true);
        }

        /// <summary>
        /// Re-derives every guarantee from the written assets, never from the build's own report:
        /// proven unique at 8M steps, stored answer equal to the solver's, every hint route
        /// complete, no path under 3 cells, plain Classic (no mechanics), medium-to-hard, and
        /// canonically distinct from every shipped level, every other daily pack and each other.
        /// </summary>
        private static bool VerifyDailyPack(DailyPackSpec spec, HashSet<string> shipped, HashSet<string> acrossDaily)
        {
            int size = spec.Size, count = spec.Count;
            string folder = DailyFolder(size);
            float floor = ShippedScore(size, DailyMediumFloorLevel);

            int loaded = 0, unique = 0, storedOk = 0, hintOk = 0, shortPath = 0, mechanics = 0;
            int dupShipped = 0, dupDaily = 0, dupWithin = 0, belowFloor = 0, wrongSize = 0;
            long pathSum = 0; int pathPairs = 0;
            float lo = float.MaxValue, hi = float.MinValue;
            HashSet<string> within = new HashSet<string>();
            StringBuilder flags = new StringBuilder();

            try
            {
                for (int i = 1; i <= count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Daily pool " + size + "x" + size + "  -  verifying",
                            "level " + i + "/" + count, (float)i / count)) { break; }

                    SingleLevelDataSO so = AssetDatabase.LoadAssetAtPath<SingleLevelDataSO>(folder + "/Level_" + i + ".asset");
                    if (so == null) { flags.Append("L").Append(i).Append(":missing "); continue; }
                    loaded++;
                    LevelData data = so.levelData;

                    if ((int)data.gridSize != size) { wrongSize++; flags.Append("L").Append(i).Append(":size "); }
                    if (data.difficultyScore < floor) { belowFloor++; flags.Append("L").Append(i).Append(":belowfloor "); }
                    lo = Mathf.Min(lo, data.difficultyScore);
                    hi = Mathf.Max(hi, data.difficultyScore);

                    if (HasAnyMechanic(data)) { mechanics++; flags.Append("L").Append(i).Append(":mechanic "); }

                    Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
                    try
                    {
                        string key = LevelCanonicalizer.ComputeCanonicalKey(grid, rows, cols);
                        if (shipped.Contains(key)) { dupShipped++; flags.Append("L").Append(i).Append(":dupshipped "); }
                        if (acrossDaily.Contains(key)) { dupDaily++; flags.Append("L").Append(i).Append(":dupdaily "); }
                        if (!within.Add(key)) { dupWithin++; flags.Append("L").Append(i).Append(":dupwithin "); }

                        PuzzleSolver.SolveResult res = PuzzleSolver.Solve(grid, rows, cols,
                            new PuzzleSolver.SolverOptions(8000000, 2));
                        bool proven = res.Status == PuzzleSolver.SolveStatus.Solved
                            && res.SolutionsFound == 1 && res.SearchExhausted;
                        if (!proven)
                        {
                            flags.Append("L").Append(i).Append(":uniq(").Append(res.Status).Append(",n=")
                                .Append(res.SolutionsFound).Append(",exhausted=").Append(res.SearchExhausted).Append(") ");
                            continue;
                        }
                        unique++;
                        if (StoredSolutionMatchesSolver(data, res, rows, cols)) { storedOk++; }
                        else { flags.Append("L").Append(i).Append(":storedmismatch "); }

                        int minLen = int.MaxValue;
                        for (int s = 0; s < res.Solutions.Count; s++)
                        {
                            int len = res.Solutions[s].Cells.Count;
                            minLen = Mathf.Min(minLen, len);
                            pathSum += len; pathPairs++;
                        }
                        if (minLen <= 2) { shortPath++; flags.Append("L").Append(i).Append(":path").Append(minLen).Append(' '); }

                        int[,] sol = HintPath.ReadSolution(data);
                        bool routesOk = sol != null;
                        if (sol != null)
                        {
                            HashSet<int> ids = new HashSet<int>();
                            for (int r = 0; r < rows; r++) { for (int c = 0; c < cols; c++) { if (sol[r, c] != 0) { ids.Add(sol[r, c]); } } }
                            foreach (int id in ids)
                            {
                                List<Block> route = HintPath.Build(grid, rows, cols, sol, id);
                                if (route == null || route.Count == 0) { routesOk = false; }
                            }
                        }
                        if (routesOk) { hintOk++; } else { flags.Append("L").Append(i).Append(":hint "); }
                    }
                    finally { DestroyBlockGrid(grid); }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            bool clean = loaded == count && unique == count && storedOk == count && hintOk == count
                && shortPath == 0 && mechanics == 0 && dupShipped == 0 && dupDaily == 0 && dupWithin == 0
                && belowFloor == 0 && wrongSize == 0;

            StringBuilder sb = new StringBuilder();
            sb.Append("DAILY POOL VERIFY ").Append(folder).AppendLine();
            sb.Append("  assets loaded             : ").Append(loaded).Append(" / ").Append(count).AppendLine();
            sb.Append("  uniquely solvable (8M)    : ").Append(unique).Append(" / ").Append(count).AppendLine();
            sb.Append("  stored answer == solver   : ").Append(storedOk).Append(" / ").Append(count).AppendLine();
            sb.Append("  hint routes complete      : ").Append(hintOk).Append(" / ").Append(count).AppendLine();
            sb.Append("  duplicates of shipped     : ").Append(dupShipped).AppendLine();
            sb.Append("  duplicates of other daily : ").Append(dupDaily).AppendLine();
            sb.Append("  duplicates within pack    : ").Append(dupWithin).AppendLine();
            sb.Append("  below medium floor ").Append(floor.ToString("0.0")).Append("   : ").Append(belowFloor).AppendLine();
            sb.Append("  score range               : ").Append(lo.ToString("0.0")).Append(" -> ").Append(hi.ToString("0.0")).AppendLine();
            sb.Append("  mechanic cells present    : ").Append(mechanics).AppendLine();
            sb.Append("  levels with path <= 2     : ").Append(shortPath).AppendLine();
            sb.Append("  mean path                 : ")
              .Append(pathPairs > 0 ? ((float)pathSum / pathPairs).ToString("F2") : "n/a").AppendLine();
            sb.Append(clean ? "  RESULT: CLEAN" : "  RESULT: PROBLEMS FOUND").AppendLine();
            if (flags.Length > 0) { sb.Append("  FLAGS: ").Append(flags).AppendLine(); }

            Directory.CreateDirectory(DailyCacheDir);
            File.WriteAllText(DailyCacheDir + "/" + size + "x" + size + "_verify.txt", sb.ToString());
            File.AppendAllText(DailyProgressLog, "[" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                + "] DAILY " + size + "x" + size + " | VERIFY | " + (clean ? "CLEAN" : "PROBLEMS FOUND")
                + " | unique " + unique + "/" + count + ", dupes " + (dupShipped + dupDaily + dupWithin)
                + ", below floor " + belowFloor + ", scores " + lo.ToString("0.0") + " -> " + hi.ToString("0.0") + "\n");

            if (clean) { Debug.Log(sb.ToString()); } else { Debug.LogError(sb.ToString()); }

            // The pack is fully in hand once verified; later sizes check against it from disk.
            foreach (string k in within) { acrossDaily.Add(k); }
            return clean;
        }

        private static bool HasAnyMechanic(LevelData data)
        {
            for (int r = 0; data.gridRows != null && r < data.gridRows.Length; r++)
            {
                GridRow row = data.gridRows[r];
                if (row.blockType != null) { foreach (BlockType t in row.blockType) { if (t != BlockType.Normal) { return true; } } }
                if (row.wallMask != null) { foreach (int m in row.wallMask) { if (m != 0) { return true; } } }
                if (row.secondPairId != null) { foreach (int id in row.secondPairId) { if (id != 0) { return true; } } }
            }
            return false;
        }

        /// <summary>Canonical keys of every shipped pack level, Classic and Advanced alike.</summary>
        private static HashSet<string> ShippedCanonicalKeys()
        {
            HashSet<string> keys = new HashSet<string>();
            AddFolderKeys("Assets/Resources/Levels/Classic", keys);
            AddFolderKeys("Assets/Resources/Levels/Advanced", keys);
            return keys;
        }

        private static void AddFolderKeys(string folder, HashSet<string> keys)
        {
            if (!AssetDatabase.IsValidFolder(folder)) { return; }
            foreach (string guid in AssetDatabase.FindAssets("t:SingleLevelDataSO", new[] { folder }))
            {
                SingleLevelDataSO so = AssetDatabase.LoadAssetAtPath<SingleLevelDataSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (so != null) { keys.Add(CanonicalKey(so.levelData)); }
            }
        }

        private static string CanonicalKey(LevelData data)
        {
            Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
            try { return LevelCanonicalizer.ComputeCanonicalKey(grid, rows, cols); }
            finally { DestroyBlockGrid(grid); }
        }

        private static float ShippedScore(int size, int level)
        {
            SingleLevelDataSO so = AssetDatabase.LoadAssetAtPath<SingleLevelDataSO>(
                "Assets/Resources/Levels/Classic/" + size + "x" + size + "/Level_" + level + ".asset");
            return so == null ? 0f : so.levelData.difficultyScore;
        }

        /// <summary><see cref="SelectStratified"/>'s walk over the score range, returning indices
        /// into <paramref name="scores"/> instead of copies, so the caller can keep track of which
        /// boards it has already scored.</summary>
        private static List<int> StratifiedIndices(List<float> scores, int count)
        {
            List<int> picked = new List<int>();
            if (scores.Count == 0 || count <= 0) { return picked; }

            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < scores.Count; i++) { lo = Mathf.Min(lo, scores[i]); hi = Mathf.Max(hi, scores[i]); }

            bool[] used = new bool[scores.Count];
            for (int i = 0; i < count && i < scores.Count; i++)
            {
                float target = count == 1 ? hi : lo + (hi - lo) * i / (count - 1);
                int best = -1; float bestGap = float.MaxValue;
                for (int j = 0; j < scores.Count; j++)
                {
                    if (used[j]) { continue; }
                    float gap = Mathf.Abs(scores[j] - target);
                    if (gap < bestGap) { bestGap = gap; best = j; }
                }
                used[best] = true;
                picked.Add(best);
            }
            return picked;
        }

        private static DailyPoolCache LoadDailyCache(int size)
        {
            string path = DailyCachePath(size);
            if (!File.Exists(path)) { return new DailyPoolCache { size = size }; }
            DailyPoolCache cache = JsonUtility.FromJson<DailyPoolCache>(File.ReadAllText(path));
            if (cache == null || cache.size != size) { return new DailyPoolCache { size = size }; }
            if (cache.pool == null) { cache.pool = new List<LevelData>(); }
            return cache;
        }

        // Written to a temp file and swapped in, so a crash mid-write cannot leave a truncated
        // cache that loses hours of gathering.
        private static void SaveDailyCache(DailyPoolCache cache)
        {
            string path = DailyCachePath(cache.size);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(cache));
            if (File.Exists(path)) { File.Delete(path); }
            File.Move(tmp, path);
        }

        private static string FormatDuration(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) { return "?"; }
            long s = (long)seconds;
            if (s < 60) { return s + "s"; }
            if (s < 3600) { return (s / 60) + "m" + (s % 60).ToString("00") + "s"; }
            return (s / 3600) + "h" + ((s % 3600) / 60).ToString("00") + "m";
        }
    }
}
