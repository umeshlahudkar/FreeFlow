using System.Collections.Generic;
using System.Text;
using FreeFlow.Enums;
using UnityEditor;
using UnityEngine;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Verifies a pack that has ALREADY BEEN WRITTEN, against the runbook in GAME_EXPANSION_PLAN §0:
    /// the generation log is not evidence, the assets are. Every check here loads the .asset files
    /// back off disk and re-derives its answer rather than trusting anything the build reported.
    ///
    /// This exists because the project had no verification entry point at all -- every prior pack
    /// was checked with hand-written throwaway code, which is unrepeatable and easy to get subtly
    /// wrong. §6.44b's first census is the cautionary case: it reported 18 shared-destination
    /// levels when the true count was 4, because secondPairId is dual-purpose (a second dot
    /// identity, but also a permission target for ForbiddenForPair/AllowedForPairs).
    /// </summary>
    public static class PackVerifier
    {
        [MenuItem("FreeFlow/Level Generator/VERIFY/Advanced 6x6 pack")]
        public static void VerifyAdvanced6x6() { Verify("Assets/Resources/Levels/Advanced/6x6", 100); }

        [MenuItem("FreeFlow/Level Generator/VERIFY/Advanced 7x7 pack")]
        public static void VerifyAdvanced7x7() { Verify("Assets/Resources/Levels/Advanced/7x7", 100); }

        public static void Verify(string folder, int count)
        {
            StringBuilder sb = new StringBuilder();
            StringBuilder fails = new StringBuilder();

            int loaded = 0, unique = 0, storedMatch = 0, hintOk = 0;
            int pairsAll = 0, pairsRouted = 0;
            int ringBad = 0, shortBad = 0, noStored = 0;
            int globalMinPath = int.MaxValue;
            int columnMinPath = int.MaxValue;
            long pathSum = 0;
            int pathPairs = 0;

            for (int i = 1; i <= count; i++)
            {
                SingleLevelDataSO so =
                    AssetDatabase.LoadAssetAtPath<SingleLevelDataSO>(folder + "/Level_" + i + ".asset");
                if (so == null) { fails.Append("L").Append(i).Append(":missing "); continue; }
                loaded++;

                LevelData data = so.levelData;
                int size = (int)data.gridSize;

                // --- structural: outer ring, stored answer present, path lengths ------------
                Dictionary<int, int> cellsPerPair = new Dictionary<int, int>();
                int ringCells = 0;

                for (int r = 0; r < size && data.gridRows != null && r < data.gridRows.Length; r++)
                {
                    GridRow row = data.gridRows[r];
                    for (int c = 0; c < size; c++)
                    {
                        bool edge = (r == 0 || c == 0 || r == size - 1 || c == size - 1);
                        if (edge && row.blockType != null && c < row.blockType.Length
                            && row.blockType[c] == BlockType.Blocked)
                        {
                            ringCells++;
                        }

                        if (row.solutionPairId != null && c < row.solutionPairId.Length)
                        {
                            int id = row.solutionPairId[c];
                            if (id != 0)
                            {
                                int had;
                                cellsPerPair.TryGetValue(id, out had);
                                cellsPerPair[id] = had + 1;
                            }
                        }
                    }
                }

                if (ringCells > 0)
                {
                    ringBad++;
                    fails.Append("L").Append(i).Append(":ring").Append(ringCells).Append(' ');
                }

                if (cellsPerPair.Count == 0)
                {
                    noStored++;
                    fails.Append("L").Append(i).Append(":nostored ");
                }
                else
                {
                    // Informational only. This column reads ONE cell short for a pair that shares a
                    // bridge or destination cell with another, because it stores a single colour per
                    // cell -- so it is not a valid basis for a minimum-path check. Path length is
                    // measured from the solver's paths below instead.
                    int levelMin = int.MaxValue;
                    foreach (KeyValuePair<int, int> kv in cellsPerPair)
                    {
                        if (kv.Value < levelMin) { levelMin = kv.Value; }
                    }
                    if (levelMin < columnMinPath) { columnMinPath = levelMin; }
                }

                // --- solver: uniqueness, and the stored answer IS that unique solution -------
                int rows, cols;
                Block[,] grid = LevelGenerator.BuildBlockGrid(data, out rows, out cols);
                try
                {
                    PuzzleSolver.SolveResult res = LevelValidator.ValidateSolvability(
                        grid, rows, cols, new PuzzleSolver.SolverOptions(2000000, 2));

                    bool proven = res.Status == PuzzleSolver.SolveStatus.Solved
                                  && res.SolutionsFound == 1
                                  && res.SearchExhausted;

                    if (!proven)
                    {
                        fails.Append("L").Append(i).Append(":uniq(").Append(res.Status)
                             .Append(",n=").Append(res.SolutionsFound)
                             .Append(",exhausted=").Append(res.SearchExhausted).Append(") ");
                    }
                    else
                    {
                        unique++;
                        if (StoredMatchesSolver(data, res, rows, cols)) { storedMatch++; }
                        else { fails.Append("L").Append(i).Append(":storedmismatch "); }
                    }

                    // Path length comes from the SOLVER's paths, which is the only honest source.
                    // An earlier version of this method counted solutionPairId instead and reported
                    // 2-cell paths on L56 and L89 that do not exist: both are Shared Destination
                    // levels, and the pair that does not own the shared cell reads one short there.
                    // The generator's own gate uses this same solver-derived number, so measuring
                    // the column here made the verifier disagree with a fix that was working.
                    if (res.Solutions != null)
                    {
                        int solverMin = int.MaxValue;
                        for (int s = 0; s < res.Solutions.Count; s++)
                        {
                            int len = res.Solutions[s].Cells.Count;
                            if (len < solverMin) { solverMin = len; }
                            pathSum += len;
                            pathPairs++;
                        }
                        if (solverMin != int.MaxValue)
                        {
                            if (solverMin <= 2)
                            {
                                shortBad++;
                                fails.Append("L").Append(i).Append(":path").Append(solverMin).Append(' ');
                            }
                            if (solverMin < globalMinPath) { globalMinPath = solverMin; }
                        }
                    }

                    // --- hint: every pair reconstructs, and the routes cover every answer cell
                    int[,] sol = HintPath.ReadSolution(data);
                    if (sol == null)
                    {
                        fails.Append("L").Append(i).Append(":hintnosol ");
                    }
                    else
                    {
                        HashSet<int> ids = new HashSet<int>();
                        for (int r = 0; r < rows; r++)
                        {
                            for (int c = 0; c < cols; c++)
                            {
                                if (sol[r, c] != 0) { ids.Add(sol[r, c]); }
                            }
                        }

                        bool[,] covered = new bool[rows, cols];
                        bool levelOk = true;

                        foreach (int id in ids)
                        {
                            pairsAll++;
                            List<Block> route = HintPath.Build(grid, rows, cols, sol, id);
                            if (route == null || route.Count == 0)
                            {
                                levelOk = false;
                                fails.Append("L").Append(i).Append(":route(p").Append(id).Append(") ");
                                continue;
                            }
                            pairsRouted++;
                            for (int k = 0; k < route.Count; k++)
                            {
                                Block b = route[k];
                                if (b != null) { covered[b.Row_ID, b.Coloum_ID] = true; }
                            }
                        }

                        int uncovered = 0;
                        for (int r = 0; r < rows; r++)
                        {
                            for (int c = 0; c < cols; c++)
                            {
                                if (sol[r, c] != 0 && !covered[r, c]) { uncovered++; }
                            }
                        }
                        if (uncovered > 0)
                        {
                            levelOk = false;
                            fails.Append("L").Append(i).Append(":uncovered").Append(uncovered).Append(' ');
                        }

                        if (levelOk) { hintOk++; }
                    }
                }
                finally { LevelGenerator.DestroyBlockGrid(grid); }
            }

            sb.Append("PACK VERIFY ").Append(folder).AppendLine();
            sb.Append("  assets loaded           : ").Append(loaded).Append(" / ").Append(count).AppendLine();
            sb.Append("  uniquely solvable       : ").Append(unique).Append(" / ").Append(count).AppendLine();
            sb.Append("  stored answer == solver : ").Append(storedMatch).Append(" / ").Append(count).AppendLine();
            sb.Append("  hint routes complete    : ").Append(hintOk).Append(" / ").Append(count)
              .Append("   (pairs ").Append(pairsRouted).Append('/').Append(pairsAll).Append(')').AppendLine();
            sb.Append("  missing stored answer   : ").Append(noStored).AppendLine();
            sb.Append("  blocked on outer ring   : ").Append(ringBad).AppendLine();
            sb.Append("  levels with path <= 2   : ").Append(shortBad)
              .Append("   (solver min ")
              .Append(globalMinPath == int.MaxValue ? 0 : globalMinPath).Append(')').AppendLine();
            sb.Append("  stored-column min path  : ")
              .Append(columnMinPath == int.MaxValue ? 0 : columnMinPath)
              .Append("   (reads one short at bridge/shared cells, by design)").AppendLine();
            sb.Append("  mean path               : ")
              .Append(pathPairs > 0 ? ((float)pathSum / pathPairs).ToString("F2") : "n/a")
              .Append(" over ").Append(pathPairs).Append(" pairs").AppendLine();

            bool clean = loaded == count && unique == count && storedMatch == count
                         && hintOk == count && noStored == 0 && ringBad == 0;

            sb.Append(clean
                ? (shortBad == 0 ? "  RESULT: CLEAN" : "  RESULT: CLEAN except short paths")
                : "  RESULT: PROBLEMS FOUND").AppendLine();
            if (fails.Length > 0) { sb.Append("  FLAGS: ").Append(fails.ToString()).AppendLine(); }

            if (clean) { Debug.Log(sb.ToString()); }
            else { Debug.LogError(sb.ToString()); }
        }

        /// <summary>
        /// Whether the stored answer is the SAME solution the solver just found, cell for cell.
        /// Uniqueness alone is not enough: a board could have exactly one solution while the column
        /// the hint system reads records a different (or stale) one, and the hint would then talk
        /// the player into a board state that cannot complete.
        /// </summary>
        private static bool StoredMatchesSolver(LevelData data, PuzzleSolver.SolveResult solved,
            int rows, int cols)
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
                    int st = c < stored.Length ? stored[c] : 0;
                    if (st != fromSolver[r, c]) { return false; }
                }
            }
            return true;
        }
    }
}
