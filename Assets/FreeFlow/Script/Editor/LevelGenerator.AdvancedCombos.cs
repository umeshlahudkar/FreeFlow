using System.Collections.Generic;
using System.Text;
using FreeFlow.Enums;
using UnityEditor;
using UnityEngine;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Combination levels for an Advanced pack build: boards carrying TWO mechanics, gathered
    /// alongside the single-rule pool and scheduled into the second half of the pack by
    /// <see cref="PlanAdvancedOrder"/> (GAME_EXPANSION_PLAN §6.46-6.47).
    ///
    /// The bar is the one the user accepted for these levels only, after playing the 60 prototypes:
    /// everything a single-rule level must be (unique at 8M steps, stored answer matches, structural
    /// gates, working hint, at least two cells of each cell rule / three wall edges) with ONE
    /// difference -- each mechanic must be needed AS A WHOLE (strip every cell of it at once and the
    /// board loses its unique answer) rather than every individual cell. Redundant cells are thinned
    /// first, majority rule first and never below the floor, so the looser bar is not an excuse for
    /// padding. The strict per-cell bar was measured at about 2 boards in 4,200 attempts; this one
    /// yields a playable level every few seconds at 6x6.
    /// </summary>
    public static partial class LevelGenerator
    {
        /// <summary>At most this many boards of any one pairing, so the easy pairings cannot crowd
        /// the rest out (Arrow + ForbiddenForPair took 13 of the first 24 prototypes).</summary>
        internal const int CombosPerPairing = 4;

        /// <summary>Combination boards gathered per level the pack wants -- a pool to choose
        /// from, not a quota, so the ramp, the dips and the no-repeat rule have options.</summary>
        internal const float ComboPoolFactor = 2f;

        /// <summary>A hard stop on combination gathering at one size. Reaching it ends the gather
        /// short rather than failing the build; the schedule then thins the earliest bands first.</summary>
        internal const int ComboGatherMaxSeconds = 4 * 3600;

        /// <summary>Once the pack's own combination count is gathered, stop after this long without
        /// a new find rather than chase the larger pool.</summary>
        internal const int ComboDrySpellMinutes = 20;

        internal struct ComboCandidate
        {
            public LevelData Data;
            public string Family;
            public string Mechanic;     // first mechanic: a rule name, "Bridge" or "SharedGoal"
            public string RuleB;        // second mechanic: a rule name or "Wall"
            public bool Strict;         // every cell proven needed, as a single-rule level would be
        }

        private static string FamilyOf(ComboKind kind)
        {
            if (kind.Structural == 1) { return FamilyBridge; }
            if (kind.Structural == 2) { return FamilyShared; }
            return kind.BIsWalls ? FamilyRuleWall : FamilyTwoRules;
        }

        private static string FirstMechanicOf(ComboKind kind)
        {
            return kind.Structural == 1 ? "Bridge" : kind.Structural == 2 ? "SharedGoal" : kind.A.ToString();
        }

        private static string SecondMechanicOf(ComboKind kind)
        {
            return kind.BIsWalls ? "Wall" : kind.B.ToString();
        }

        /// <summary>The 27 combination kinds: all ten pairings of the five cell rules, each cell rule
        /// with walls, and Bridge / Shared Destination each with every cell rule and with walls.</summary>
        internal static List<ComboKind> AllComboKinds()
        {
            BlockType[] rules =
            {
                BlockType.OneWay, BlockType.Arrow, BlockType.Checkpoint,
                BlockType.ForbiddenForPair, BlockType.AllowedForPairs
            };

            List<ComboKind> kinds = new List<ComboKind>();
            for (int i = 0; i < rules.Length; i++)
            {
                for (int j = i + 1; j < rules.Length; j++)
                {
                    kinds.Add(new ComboKind { A = rules[i], B = rules[j] });
                }
            }
            for (int i = 0; i < rules.Length; i++)
            {
                kinds.Add(new ComboKind { A = rules[i], BIsWalls = true });
            }
            for (int structural = 1; structural <= 2; structural++)
            {
                for (int i = 0; i < rules.Length; i++)
                {
                    kinds.Add(new ComboKind { Structural = structural, B = rules[i] });
                }
                kinds.Add(new ComboKind { Structural = structural, BIsWalls = true });
            }
            return kinds;
        }

        /// <summary>
        /// Gathers combination boards for one pack. Every kind is tried in rotation (so each family
        /// fills evenly), at deficits 1-3, until the pool target or the time limit. Boards are NOT
        /// scored here -- the schedule scores only the families the difficulty model can rate.
        /// </summary>
        private static List<ComboCandidate> GatherCombinations(int size, int cellsPerColour, int comboLevels,
            HashSet<string> seen, System.Random rng, CpuThrottle throttle,
            System.Diagnostics.Stopwatch total, ref bool cancelled, StringBuilder report)
        {
            List<ComboCandidate> found = new List<ComboCandidate>();
            if (comboLevels <= 0) { return found; }

            List<ComboKind> kinds = AllComboKinds();
            int target = Mathf.CeilToInt(comboLevels * ComboPoolFactor);

            // 7x7's 7 is what the prototypes were measured at; 6x6's 9 likewise. Above that nothing
            // has been measured, so the pack's own ratio is used rather than a guess.
            int comboCellsPerColour = size <= 6 ? 9 : size == 7 ? 7 : cellsPerColour;

            Dictionary<string, int> perPairing = new Dictionary<string, int>();
            Dictionary<string, int> triedPairing = new Dictionary<string, int>();
            int attempts = 0, built = 0, underFloor = 0, notWhole = 0, unsound = 0, badHint = 0, strictKept = 0;
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

            bool pruneBefore = ProbePruneAllSizes;
            ProbePruneAllSizes = false; // removal is done here, floor-protected, not by the builder

            try
            {
                long lastFindMs = 0;
                while (found.Count < target && !cancelled && clock.ElapsedMilliseconds < ComboGatherMaxSeconds * 1000L)
                {
                    throttle.Tick();

                    // Once the pack's own 36 are in hand, a long dry spell means the remaining
                    // pairings are not going to yield: stop rather than cycle capped and barren
                    // pairings for hours chasing the 2x pool.
                    if (found.Count >= comboLevels
                        && clock.ElapsedMilliseconds - lastFindMs > ComboDrySpellMinutes * 60000L)
                    { break; }

                    if ((attempts % 8) == 0 && EditorUtility.DisplayCancelableProgressBar(
                            "Building the Advanced " + size + "x" + size + " pack  -  combination levels",
                            found.Count + "/" + target + " kept  -  attempt " + attempts
                                + "  -  " + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                            0.45f + 0.05f * Mathf.Clamp01(found.Count / (float)target)))
                    { if (ConfirmStop()) { cancelled = true; break; } }

                    ComboKind kind = kinds[attempts % kinds.Count];
                    int deficit = 1 + ((attempts / kinds.Count) % 3);
                    attempts++;

                    string pairing = kind.Name;
                    perPairing.TryGetValue(pairing, out int haveKind);
                    if (haveKind >= CombosPerPairing) { continue; }
                    triedPairing.TryGetValue(pairing, out int tried);
                    triedPairing[pairing] = tried + 1;

                    LevelData data;
                    int countA, countB;

                    if (kind.Structural > 0)
                    {
                        if (!TryBuildStructuralCombo(size, kind, rng, out data)) { continue; }
                        built++;

                        if (!kind.BIsWalls) { PruneComboFloorProtected(ref data, kind.B, kind.B, rng); }

                        countA = kind.Structural == 1 ? CountCellsOfType(data, BlockType.Bridge) : CountSharedGoals(data);
                        countB = kind.BIsWalls ? CountWalls(data) : CountCellsOfType(data, kind.B);
                        if (countA < 1 || countB < (kind.BIsWalls ? 3 : 2)) { underFloor++; continue; }
                    }
                    else
                    {
                        bool[,] usable = PlaceBlockedCells(size, 0, true, rng);
                        int usableCount = size * size;

                        MechanicRecipe recipe = new MechanicRecipe(kind.A, kind.BIsWalls ? BlockType.Normal : kind.B,
                            InstanceCeilingFor(size), deficit);
                        recipe.MinInstances = 2;
                        recipe.SecondIsWalls = kind.BIsWalls;

                        int colours = Mathf.Max(3, usableCount / comboCellsPerColour) + (attempts % ColourSweepWidth);
                        if (colours > MaxDistinctColors) { continue; }

                        if (!TryBuildMechanicDependentBoard(size, usable, usableCount, colours, recipe, rng, out data))
                        {
                            continue;
                        }
                        built++;

                        PruneComboFloorProtected(ref data, kind.A, kind.BIsWalls ? kind.A : kind.B, rng);

                        countA = CountCellsOfType(data, kind.A);
                        countB = kind.BIsWalls ? CountWalls(data) : CountCellsOfType(data, kind.B);
                        if (countA < 2 || countB < (kind.BIsWalls ? 3 : 2)) { underFloor++; continue; }
                    }

                    Block[,] grid = BuildBlockGrid(data, out int rows, out int cols);
                    try
                    {
                        string key = LevelCanonicalizer.ComputeCanonicalKey(grid, rows, cols);
                        if (seen.Contains(key)) { continue; }

                        PuzzleSolver.SolveResult solved = PuzzleSolver.Solve(grid, rows, cols,
                            new PuzzleSolver.SolverOptions(8000000, 2));
                        if (solved.Status != PuzzleSolver.SolveStatus.Solved || solved.SolutionsFound != 1
                            || !solved.SearchExhausted || !StoredSolutionMatchesSolver(data, solved, rows, cols))
                        { unsound++; continue; }

                        int live = 0;
                        for (int r = 0; r < rows; r++)
                        {
                            for (int c = 0; c < cols; c++)
                            {
                                if (grid[r, c].BlockType != BlockType.Blocked) { live++; }
                            }
                        }
                        if (!StructuralGates.Evaluate(solved, live).Passed) { unsound++; continue; }

                        if (!HintRoutesCoverAnswer(data, grid, rows, cols)) { badHint++; continue; }

                        // A Shared Destination cannot be stripped -- it is how the pairs are defined, not
                        // a rule laid over them -- so only its partner is tested.
                        bool wholeA = kind.Structural == 2 ? true
                            : MechanicGroupProvenRequired(data, kind.Structural == 1 ? BlockType.Bridge : kind.A, false);
                        bool wholeB = MechanicGroupProvenRequired(data, kind.B, kind.BIsWalls);
                        if (!wholeA || !wholeB) { notWhole++; continue; }

                        bool strictA = kind.Structural == 2 ? true
                            : AllCellsOfTypeProvenRequired(grid, rows, cols, kind.Structural == 1 ? BlockType.Bridge : kind.A);
                        bool strict = strictA
                            && (kind.BIsWalls ? AllWallsAreNecessary(grid, rows, cols)
                                              : AllCellsOfTypeProvenRequired(grid, rows, cols, kind.B));

                        seen.Add(key);
                        found.Add(new ComboCandidate
                        {
                            Data = data,
                            Family = FamilyOf(kind),
                            Mechanic = FirstMechanicOf(kind),
                            RuleB = SecondMechanicOf(kind),
                            Strict = strict
                        });
                        perPairing[pairing] = haveKind + 1;
                        lastFindMs = clock.ElapsedMilliseconds;
                        if (strict) { strictKept++; }
                    }
                    finally { DestroyBlockGrid(grid); }
                }
            }
            finally { ProbePruneAllSizes = pruneBefore; }

            report.Append("  combinations: kept ").Append(found.Count).Append("/").Append(target)
                .Append(" (").Append(strictKept).Append(" also meet the per-cell standard) from ")
                .Append(attempts).Append(" attempts in ").Append(clock.ElapsedMilliseconds / 1000).Append("s -- built ")
                .Append(built).Append(", under the floor after removal ").Append(underFloor)
                .Append(", a mechanic not needed as a whole ").Append(notWhole)
                .Append(", unsound ").Append(unsound)
                .Append(", hint route could not be rebuilt ").Append(badHint).AppendLine();
            report.Append("    by pairing:");
            foreach (ComboKind k in kinds)
            {
                perPairing.TryGetValue(k.Name, out int n);
                triedPairing.TryGetValue(k.Name, out int t);
                report.Append("  ").Append(k.Name).Append('=').Append(n).Append('/').Append(t);
            }
            report.AppendLine();
            Debug.Log("Advanced " + size + "x" + size + " combination gather:\n" + report);

            return found;
        }

        /// <summary>
        /// Scores the combinations the difficulty model can rate (cell rules and walls; never Bridge or
        /// Shared Destination, which HumanSolver.CanRate refuses) and turns every candidate into a
        /// schedule Entry. A rateable board the model rejects is simply placed by position, like a
        /// structural one -- better than dropping a board that passed every gate.
        /// </summary>
        private static List<Entry> ScoreCombinations(List<ComboCandidate> candidates, CpuThrottle throttle,
            System.Diagnostics.Stopwatch total, string label, ref bool cancelled)
        {
            List<Entry> result = new List<Entry>(candidates.Count);
            for (int i = 0; i < candidates.Count && !cancelled; i++)
            {
                throttle.Tick();
                ComboCandidate c = candidates[i];

                if ((i % 4) == 0 && EditorUtility.DisplayCancelableProgressBar(
                        "Building the " + label + " pack  -  scoring combination levels",
                        i + "/" + candidates.Count + "  -  " + (total.ElapsedMilliseconds / 1000f).ToString("0") + "s",
                        0.9f + 0.05f * i / Mathf.Max(1, candidates.Count)))
                { if (ConfirmStop()) { cancelled = true; break; } }

                Entry e = new Entry
                {
                    Data = c.Data,
                    Mechanic = c.Mechanic,
                    RuleB = c.RuleB,
                    Family = c.Family,
                    Instances = 2,
                    Deficit = 0,
                    Unranked = true,
                    Score = 0f
                };

                if (c.Family == FamilyTwoRules || c.Family == FamilyRuleWall)
                {
                    DifficultyModel.Profile p = DifficultyModel.Measure(c.Data);
                    if (p.Valid && p.WellFormed) { e.Score = p.Score; e.Unranked = false; }
                }
                result.Add(e);
            }
            return result;
        }

        /// <summary>
        /// Keeps the gathered pool after the pack is written. The build used to delete the cache, and
        /// with it the ~800 boards the 100 levels were not drawn from -- the material the premium pack
        /// would come from. A copy goes to Library/AdvancedPoolArchive/ (dated, never overwritten), and
        /// the checkpoint itself is then deleted as before so the next build starts fresh.
        /// </summary>
        internal static string ArchiveAdvancedCache(int size)
        {
            string source = AdvancedCachePath(size);
            if (!System.IO.File.Exists(source)) { return null; }

            string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source) ?? ".", "..", "AdvancedPoolArchive");
            // A failed archive must never block the cleanup that follows it: the pack is already
            // written, and a leftover checkpoint would make the next build resume the old pool.
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                string target = System.IO.Path.Combine(dir, size + "x" + size + "_pool_"
                    + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".json");
                System.IO.File.Copy(source, target, false);
                return System.IO.Path.GetFullPath(target);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Advanced " + size + "x" + size + ": could not archive the pool (" + e.Message + ")");
                return null;
            }
        }

        /// <summary>
        /// The score spread of everything gathered, per size: written after every build so the premium
        /// pack can be sized from fact instead of the old "about 650 leftovers" guess. Boards that
        /// were not placed in the 100 are the premium candidates, so this counts them by band.
        /// </summary>
        private static string DescribeLeftovers(List<Entry> scored, List<Entry> combos, List<Entry> ordered)
        {
            HashSet<LevelData> placed = new HashSet<LevelData>();
            foreach (Entry e in ordered) { placed.Add(e.Data); }

            List<float> left = new List<float>();
            foreach (Entry e in scored) { if (!placed.Contains(e.Data)) { left.Add(e.Score); } }
            int combosLeft = 0;
            foreach (Entry e in combos) { if (!placed.Contains(e.Data)) { combosLeft++; } }
            left.Sort();

            StringBuilder sb = new StringBuilder();
            sb.Append("  not placed: ").Append(left.Count).Append(" single-rule boards, ").Append(combosLeft)
                .AppendLine(" combination boards");
            if (left.Count == 0) { return sb.ToString(); }

            float[] edges = { 0f, 40f, 50f, 60f, 70f, 80f, 101f };
            sb.Append("    score spread of the single-rule boards not placed:");
            for (int b = 0; b + 1 < edges.Length; b++)
            {
                int n = 0;
                foreach (float s in left) { if (s >= edges[b] && s < edges[b + 1]) { n++; } }
                sb.Append("  ").Append(edges[b].ToString("0")).Append("-").Append((edges[b + 1] - 1).ToString("0"))
                    .Append("=").Append(n);
            }
            sb.AppendLine();
            return sb.ToString();
        }
    }
}
