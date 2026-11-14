using System.Collections.Generic;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// The 100-level order of an Advanced pack with combination levels (GAME_EXPANSION_PLAN §6.47),
    /// checked on synthetic boards: <see cref="LevelGenerator.PlanAdvancedOrder"/> is a pure function,
    /// so the rules -- 36 combinations from level 36, both rules taught first, structural levels at
    /// fixed spacing, no pairing twice running -- need no generation to test.
    /// </summary>
    public class AdvancedScheduleTests
    {
        private static readonly string[] Rules = { "OneWay", "Arrow", "Checkpoint", "ForbiddenForPair", "AllowedForPairs", "Wall" };

        private int uniqueId;

        // LevelData is a struct, and the schedule keys on it: two default values would be "the same
        // board". A fresh array per board makes each one distinct.
        private LevelData MakeData()
        {
            uniqueId++;
            return new LevelData { gridRows = new GridRow[1 + (uniqueId % 3)] };
        }

        private List<LevelGenerator.Entry> SinglePool(System.Random rng)
        {
            List<LevelGenerator.Entry> pool = new List<LevelGenerator.Entry>();
            foreach (string rule in Rules)
            {
                for (int deficit = 1; deficit <= 3; deficit++)
                {
                    for (int k = 0; k < 14; k++)
                    {
                        pool.Add(new LevelGenerator.Entry
                        {
                            Data = MakeData(), Mechanic = rule, Deficit = deficit, Instances = 2,
                            Score = 25f + deficit * 10f + (float)rng.NextDouble() * 25f
                        });
                    }
                }
            }
            for (int k = 0; k < 20; k++)
            {
                pool.Add(new LevelGenerator.Entry
                {
                    Data = MakeData(), Mechanic = "BlockedOnly", Deficit = 0,
                    Score = 20f + (float)rng.NextDouble() * 30f
                });
            }
            return pool;
        }

        private List<LevelGenerator.Entry> StructuralPool()
        {
            List<LevelGenerator.Entry> structural = new List<LevelGenerator.Entry>();
            for (int i = 0; i < 4; i++)
            {
                structural.Add(new LevelGenerator.Entry
                {
                    Data = MakeData(), Mechanic = "Bridge", Unranked = true, IsStructural = true, Instances = 1
                });
            }
            for (int i = 0; i < 4; i++)
            {
                structural.Add(new LevelGenerator.Entry
                {
                    Data = MakeData(), Mechanic = "SharedGoal", Unranked = true, IsStructural = true, Instances = 1
                });
            }
            return structural;
        }

        private List<LevelGenerator.Entry> ComboPool(System.Random rng, int perPairing)
        {
            List<LevelGenerator.Entry> pool = new List<LevelGenerator.Entry>();
            for (int a = 0; a < 5; a++)
            {
                for (int b = a + 1; b < 5; b++) { AddCombos(pool, rng, perPairing, LevelGenerator.FamilyTwoRules, Rules[a], Rules[b], false); }
                AddCombos(pool, rng, perPairing, LevelGenerator.FamilyRuleWall, Rules[a], "Wall", false);
                AddCombos(pool, rng, perPairing, LevelGenerator.FamilyBridge, "Bridge", Rules[a], true);
                AddCombos(pool, rng, perPairing, LevelGenerator.FamilyShared, "SharedGoal", Rules[a], true);
            }
            AddCombos(pool, rng, perPairing, LevelGenerator.FamilyBridge, "Bridge", "Wall", true);
            AddCombos(pool, rng, perPairing, LevelGenerator.FamilyShared, "SharedGoal", "Wall", true);
            return pool;
        }

        private void AddCombos(List<LevelGenerator.Entry> pool, System.Random rng, int n, string family,
            string a, string b, bool unranked)
        {
            for (int k = 0; k < n; k++)
            {
                pool.Add(new LevelGenerator.Entry
                {
                    Data = MakeData(), Mechanic = a, RuleB = b, Family = family, Instances = 2,
                    Unranked = unranked, Score = unranked ? 0f : 30f + (float)rng.NextDouble() * 60f
                });
            }
        }

        private List<LevelGenerator.Entry> Plan(int combosAvailablePerPairing, int comboTarget = LevelGenerator.AdvancedComboLevels)
        {
            System.Random rng = new System.Random(4242);
            List<LevelGenerator.Entry> scored = SinglePool(rng);
            List<LevelGenerator.Entry> combos = combosAvailablePerPairing > 0 ? ComboPool(rng, combosAvailablePerPairing) : new List<LevelGenerator.Entry>();
            List<LevelGenerator.Entry> structural = StructuralPool();

            bool ok = LevelGenerator.PlanAdvancedOrder(scored, combos, structural, 100, comboTarget,
                out List<LevelGenerator.Entry> ordered, out string text);
            Assert.IsTrue(ok, text);
            return ordered;
        }

        private static bool IsCombo(LevelGenerator.Entry e) { return e.Family != null; }

        [Test]
        public void FillsExactlyOneHundredDistinctLevels()
        {
            List<LevelGenerator.Entry> ordered = Plan(4);
            Assert.AreEqual(100, ordered.Count);
            HashSet<LevelData> seen = new HashSet<LevelData>();
            for (int i = 0; i < ordered.Count; i++)
            {
                Assert.IsNotNull(ordered[i], "level " + (i + 1) + " is empty");
                Assert.IsTrue(seen.Add(ordered[i].Data), "level " + (i + 1) + " repeats a board");
            }
        }

        [Test]
        public void ThirtySixCombinationsAllAfterLevelThirtyFive()
        {
            List<LevelGenerator.Entry> ordered = Plan(4);
            int combos = 0;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (!IsCombo(ordered[i])) { continue; }
                combos++;
                Assert.GreaterOrEqual(i, 35, "combination on level " + (i + 1) + " -- before the warm-up and teaching are over");
            }
            Assert.AreEqual(36, combos);
        }

        [Test]
        public void StructuralLevelsStayAtTheirFixedSlots()
        {
            List<LevelGenerator.Entry> ordered = Plan(4);
            int[] expected = { 12, 23, 34, 45, 56, 67, 78, 89 };
            for (int i = 0; i < expected.Length; i++)
            {
                LevelGenerator.Entry e = ordered[expected[i] - 1];
                Assert.IsTrue(e.IsStructural, "level " + expected[i] + " should be a structural level");
                Assert.AreEqual(i < 4 ? "Bridge" : "SharedGoal", e.Mechanic);
            }
        }

        [Test]
        public void BothRulesAreTaughtBeforeEveryCombination()
        {
            List<LevelGenerator.Entry> ordered = Plan(4);
            for (int i = 0; i < ordered.Count; i++)
            {
                if (!IsCombo(ordered[i])) { continue; }

                foreach (string mechanic in new[] { ordered[i].Mechanic, ordered[i].RuleB })
                {
                    int before = 0;
                    for (int j = 0; j < i; j++)
                    {
                        if (!IsCombo(ordered[j]) && ordered[j].Mechanic == mechanic) { before++; }
                    }

                    // Bridge and Shared Destination are introduced by one structural level; a cell
                    // rule or walls by a run of three.
                    int needed = (mechanic == "Bridge" || mechanic == "SharedGoal") ? 1 : 3;
                    Assert.GreaterOrEqual(before, needed,
                        "level " + (i + 1) + " (" + ordered[i].Pairing + ") comes before " + mechanic + " has been taught");
                }
            }
        }

        [Test]
        public void NoPairingTwiceRunning()
        {
            List<LevelGenerator.Entry> ordered = Plan(4);
            string previous = null;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (!IsCombo(ordered[i])) { previous = null; continue; }
                Assert.AreNotEqual(previous, ordered[i].Pairing, "level " + (i + 1) + " repeats " + previous);
                previous = ordered[i].Pairing;
            }
        }

        [Test]
        public void ComboDensityRisesBandByBand()
        {
            List<LevelGenerator.Entry> ordered = Plan(4);
            int[] edges = { 35, 50, 60, 70, 80, 90, 100 };
            int[] counts = new int[6];
            for (int i = 0; i < ordered.Count; i++)
            {
                if (!IsCombo(ordered[i])) { continue; }
                for (int b = 0; b < 6; b++) { if (i >= edges[b] && i < edges[b + 1]) { counts[b]++; } }
            }
            Assert.AreEqual(new[] { 3, 5, 6, 7, 7, 8 }, counts);
        }

        [Test]
        public void ScarceCombinationsThinTheEarliestBandsFirst()
        {
            // Only six pairings, one board each... far fewer than 36.
            System.Random rng = new System.Random(7);
            List<LevelGenerator.Entry> scored = SinglePool(rng);
            List<LevelGenerator.Entry> combos = new List<LevelGenerator.Entry>();
            AddCombos(combos, rng, 1, LevelGenerator.FamilyTwoRules, "OneWay", "Arrow", false);
            AddCombos(combos, rng, 1, LevelGenerator.FamilyTwoRules, "Arrow", "Checkpoint", false);
            AddCombos(combos, rng, 1, LevelGenerator.FamilyTwoRules, "Checkpoint", "ForbiddenForPair", false);
            AddCombos(combos, rng, 1, LevelGenerator.FamilyRuleWall, "OneWay", "Wall", false);
            AddCombos(combos, rng, 1, LevelGenerator.FamilyRuleWall, "Arrow", "Wall", false);
            AddCombos(combos, rng, 1, LevelGenerator.FamilyRuleWall, "Checkpoint", "Wall", false);

            bool ok = LevelGenerator.PlanAdvancedOrder(scored, combos, StructuralPool(), 100, 36,
                out List<LevelGenerator.Entry> ordered, out string text);
            Assert.IsTrue(ok, text);
            Assert.AreEqual(100, ordered.Count);

            int placed = 0, firstAt = 100;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (!IsCombo(ordered[i])) { continue; }
                placed++;
                if (i < firstAt) { firstAt = i; }
            }
            Assert.LessOrEqual(placed, 6);
            Assert.GreaterOrEqual(firstAt, 50, "the few combinations should sit in the later bands");
        }

        [Test]
        public void NoCombinationsStillMakesAFullPack()
        {
            List<LevelGenerator.Entry> ordered = Plan(0, 0);
            Assert.AreEqual(100, ordered.Count);
            foreach (LevelGenerator.Entry e in ordered) { Assert.IsFalse(IsCombo(e)); }
        }

        [Test]
        public void ARuleWithNoEasyBoardsIsNeverCountedAsTaught()
        {
            // AllowedForPairs has only deficit-3 boards, so its teaching run cannot be filled.
            System.Random rng = new System.Random(99);
            List<LevelGenerator.Entry> scored = SinglePool(rng);
            scored.RemoveAll(e => e.Mechanic == "AllowedForPairs" && e.Deficit < 3);

            bool ok = LevelGenerator.PlanAdvancedOrder(scored, ComboPool(rng, 4), StructuralPool(), 100, 36,
                out List<LevelGenerator.Entry> ordered, out string text);
            Assert.IsTrue(ok, text);
            Assert.AreEqual(100, ordered.Count);

            int firstSingle = -1, firstCombo = 100;
            for (int i = 0; i < ordered.Count; i++)
            {
                LevelGenerator.Entry e = ordered[i];
                if (!IsCombo(e) && e.Mechanic == "AllowedForPairs" && firstSingle < 0) { firstSingle = i; }
                if (IsCombo(e) && (e.Mechanic == "AllowedForPairs" || e.RuleB == "AllowedForPairs") && i < firstCombo)
                {
                    firstCombo = i;
                }
            }
            Assert.IsTrue(firstCombo == 100 || (firstSingle >= 0 && firstSingle < firstCombo),
                "an AllowedForPairs combination appears at level " + (firstCombo + 1)
                + " before any AllowedForPairs single level (first at " + (firstSingle + 1) + ")");
        }

        [Test]
        public void MoreStructuralLevelsThanSlotsIsRefusedNotTruncated()
        {
            System.Random rng = new System.Random(5);
            List<LevelGenerator.Entry> structural = new List<LevelGenerator.Entry>();
            for (int i = 0; i < 12; i++)
            {
                structural.Add(new LevelGenerator.Entry { Data = MakeData(), Mechanic = "Bridge", Unranked = true, IsStructural = true });
            }
            bool ok = LevelGenerator.PlanAdvancedOrder(SinglePool(rng), new List<LevelGenerator.Entry>(), structural, 10, 0,
                out List<LevelGenerator.Entry> ordered, out string text);
            Assert.IsFalse(ok, text);
        }

        [Test]
        public void FirstOfAKindScoredCombinationSitsBelowTheRamp()
        {
            System.Random rng = new System.Random(4242);
            List<LevelGenerator.Entry> scored = SinglePool(rng);
            List<float> envelope = new List<float>();
            foreach (LevelGenerator.Entry e in scored) { envelope.Add(e.Score); }
            envelope.Sort();

            List<LevelGenerator.Entry> ordered = Plan(4);
            HashSet<string> families = new HashSet<string>();
            for (int i = 0; i < ordered.Count; i++)
            {
                LevelGenerator.Entry e = ordered[i];
                if (!IsCombo(e) || !families.Add(e.Family) || e.Unranked) { continue; }

                int idx = (int)System.Math.Round(i / 99.0 * (envelope.Count - 1));
                Assert.Less(e.Score, envelope[idx] - 2f,
                    "the first " + e.Family + " combination (level " + (i + 1) + ") should be easier than the ramp");
            }
        }
    }
}
