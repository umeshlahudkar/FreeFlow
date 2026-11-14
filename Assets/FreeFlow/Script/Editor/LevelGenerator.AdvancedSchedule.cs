using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// How an Advanced pack is ORDERED once it has combination levels (GAME_EXPANSION_PLAN §6.46-6.47).
    ///
    /// Kept as a pure function over plain entries, apart from the pack builder, so the rules can be
    /// tested without Unity generating anything: <see cref="PlanAdvancedOrder"/> takes scored
    /// single-rule boards, combination boards and structural (Bridge / Shared Destination) levels
    /// and returns the 100-level order.
    ///
    /// The shape, from the research in §6.47 (isolate, develop, then combine; one new idea at a time;
    /// a sawtooth rather than a straight ramp):
    ///   - levels 1-35: warm-up, each rule taught alone (three levels per rule), single rules mixed;
    ///   - levels 36-100: 36 combination levels in six bands of rising density (3, 5, 6, 7, 7, 8),
    ///     among the remaining single-rule levels and the structural ones.
    ///
    /// A combination only appears once BOTH its rules have had their teaching run. The first level of
    /// each new KIND of combination is easier than the ramp wants there, and one breather per band of
    /// ten likewise. Bridge and Shared Destination combinations cannot be difficulty-scored
    /// (HumanSolver.CanRate refuses both), so those are placed by position and chosen by how hard the
    /// overlay rule is to read, not by score.
    /// </summary>
    public static partial class LevelGenerator
    {
        /// <summary>Combination levels in a 100-level pack (the user's choice, 2026-11-14).</summary>
        internal const int AdvancedComboLevels = 36;

        /// <summary>How far below the ramp the first level of each new kind of combination sits.</summary>
        internal const float FirstOfKindDip = 10f;

        /// <summary>How far below the ramp a breather (one per band of ten) sits.</summary>
        internal const float BreatherDip = 8f;

        /// <summary>The single-rule level just before an unscorable first combination of its kind.</summary>
        internal const float LeadInDip = 8f;

        /// <summary>A rule counts as taught once at least this many of its teaching-run levels were
        /// really placed (or the whole run, if the run itself was shorter).</summary>
        internal const int MinTaughtLevels = 2;

        internal const string FamilyRuleWall = "RuleWall";
        internal const string FamilyTwoRules = "TwoRules";
        internal const string FamilyShared = "Shared";
        internal const string FamilyBridge = "Bridge";

        internal enum SlotKind { Single, Combo, Structural }

        // Six bands as fractions of the pack: from level 36 of 100, then a band every ten levels.
        // Counts sum to 36. Rule + walls and two-rule boards lead (they can be scored, so the ramp and
        // dips can be exact); Shared Destination combinations come only after its own teaching level
        // (L56) and Bridge combinations are the hardest to read, so they thicken late.
        private static readonly float[] ComboBandEdges = { 0.35f, 0.50f, 0.60f, 0.70f, 0.80f, 0.90f, 1.00f };
        private static readonly int[] ComboBandWeights = { 3, 5, 6, 7, 7, 8 };
        private static readonly string[][] ComboBandFamilies =
        {
            new[] { FamilyRuleWall, FamilyTwoRules, FamilyRuleWall },
            new[] { FamilyRuleWall, FamilyTwoRules, FamilyBridge, FamilyTwoRules, FamilyShared },
            new[] { FamilyTwoRules, FamilyBridge, FamilyShared, FamilyRuleWall, FamilyShared, FamilyBridge },
            new[] { FamilyBridge, FamilyTwoRules, FamilyShared, FamilyBridge, FamilyRuleWall, FamilyShared, FamilyTwoRules },
            new[] { FamilyShared, FamilyTwoRules, FamilyBridge, FamilyRuleWall, FamilyBridge, FamilyShared, FamilyTwoRules },
            new[] { FamilyTwoRules, FamilyBridge, FamilyShared, FamilyTwoRules, FamilyBridge, FamilyShared, FamilyRuleWall, FamilyTwoRules },
        };

        private static readonly string[] AllFamilies =
            { FamilyTwoRules, FamilyRuleWall, FamilyBridge, FamilyShared };

        private struct ComboSlot
        {
            public int Pos;
            public int Band;
            public int J;
        }

        private sealed class ComboContext
        {
            public List<Entry> Combos;
            public HashSet<LevelData> Used;
            public Dictionary<string, int> TaughtAt;
            public Dictionary<string, int> RuleRank;
            public int MaxRank;
            public List<float> Envelope;
            public int Count;
            public int FirstComboPos;
            public int Span;
            public string PreviousPairing;
            public readonly List<string> Recent = new List<string>();
        }

        /// <summary>
        /// Orders a pack. Returns false, with the reason in <paramref name="planText"/>, if it cannot
        /// fill every slot. <paramref name="scored"/> are single-rule boards (and blocked-only boards),
        /// <paramref name="combos"/> are combination boards (Unranked ones carry no usable score), and
        /// <paramref name="structural"/> are the single Bridge / Shared Destination levels, placed at
        /// fixed even spacing exactly as before.
        /// </summary>
        internal static bool PlanAdvancedOrder(List<Entry> scored, List<Entry> combos, List<Entry> structural,
            int count, int comboTarget, out List<Entry> ordered, out string planText)
        {
            ordered = null;
            planText = string.Empty;
            StringBuilder plan = new StringBuilder();
            if (combos == null) { combos = new List<Entry>(); }
            if (structural == null) { structural = new List<Entry>(); }
            if (scored == null || count <= 0) { planText = "nothing to schedule"; return false; }
            if (structural.Count >= count)
            {
                planText = structural.Count + " structural levels cannot fit in a " + count + "-level pack";
                return false;
            }

            // ---- 1. which level is which kind -------------------------------------------------
            SlotKind[] slots = new SlotKind[count];
            int structuralCount = structural.Count;
            int spacing = Mathf.Max(1, count / (structuralCount + 1));
            int[] structuralAt = new int[structuralCount];
            for (int i = 0; i < structuralCount; i++)
            {
                int s = Mathf.Min(count - 1, (i + 1) * spacing);
                while (slots[s] == SlotKind.Structural && s < count - 1) { s++; }
                slots[s] = SlotKind.Structural;
                structuralAt[i] = s;
            }

            List<ComboSlot> comboSlots = PlanComboSlots(count, comboTarget, slots, combos.Count);
            for (int i = 0; i < comboSlots.Count; i++) { slots[comboSlots[i].Pos] = SlotKind.Combo; }

            List<int> singleSlots = new List<int>();
            for (int p = 0; p < count; p++) { if (slots[p] == SlotKind.Single) { singleSlots.Add(p); } }

            // ---- 2. the single-rule blocks, laid over the single slots ------------------------
            List<string> rules = new List<string>();
            foreach (Entry e in scored)
            {
                if (e.Mechanic != "BlockedOnly" && !rules.Contains(e.Mechanic)) { rules.Add(e.Mechanic); }
            }
            rules.Sort((a, b) => MedianOf(scored, a).CompareTo(MedianOf(scored, b)));

            int singlesTotal = singleSlots.Count;
            int singlesBeforeCombos = singlesTotal / 2;
            if (comboSlots.Count > 0)
            {
                singlesBeforeCombos = 0;
                foreach (int sp in singleSlots) { if (sp < comboSlots[0].Pos) { singlesBeforeCombos++; } }
            }

            int warmUp = Mathf.Min((count - structuralCount) / 25 + 2, CountOf(scored, "BlockedOnly"));
            warmUp = Mathf.Min(warmUp, singlesTotal);

            // Runs shrink rather than overflow when the pack is too short for three levels of every
            // rule, but never below one: a rule met zero times is worse than a rule met once.
            int practicePerRule = 3;
            while (practicePerRule > 1 && rules.Count * practicePerRule > singlesBeforeCombos - warmUp)
            {
                practicePerRule--;
            }

            int teachBlock = rules.Count * practicePerRule;
            int consolidation = Mathf.Max(0, singlesBeforeCombos - warmUp - teachBlock);
            int escalation = Mathf.Max(0, singlesTotal - warmUp - teachBlock - consolidation);

            int cursor = 0;
            List<int> warmPositions = Slice(singleSlots, ref cursor, warmUp);
            List<List<int>> runPositions = new List<List<int>>();
            for (int r = 0; r < rules.Count; r++) { runPositions.Add(Slice(singleSlots, ref cursor, practicePerRule)); }
            List<int> consolidationPositions = Slice(singleSlots, ref cursor, consolidation);
            List<int> escalationPositions = Slice(singleSlots, ref cursor, escalation);

            // When each rule has been TAUGHT: the last level of its run that was ACTUALLY FILLED, and
            // only if at least MinTaughtLevels were (set below, once the runs are placed). A run that
            // comes up short must not count as taught -- an earlier version counted the slots rather
            // than the boards, and a scarce rule then had combinations before its first single level.
            // Bridge and Shared Destination are taught by their first structural level.
            Dictionary<string, int> taughtAt = new Dictionary<string, int>();
            for (int i = 0; i < structuralCount; i++)
            {
                if (!taughtAt.ContainsKey(structural[i].Mechanic)) { taughtAt[structural[i].Mechanic] = structuralAt[i]; }
            }

            // ---- 3. the pack's own difficulty envelope -----------------------------------------
            List<float> envelope = new List<float>(scored.Count);
            foreach (Entry e in scored) { envelope.Add(e.Score); }
            envelope.Sort();

            HashSet<LevelData> used = new HashSet<LevelData>();
            Entry[] placed = new Entry[count];
            float[] dip = new float[count];
            for (int i = 0; i < structuralCount; i++)
            {
                placed[structuralAt[i]] = structural[i];
                used.Add(structural[i].Data);
            }

            // ---- 3b. the warm-up and the teaching runs, placed BEFORE any combination is chosen, so
            // "taught" reflects the boards that really are there. Neither block carries a dip: both end
            // well before the first breather (level 31) and the first combination (level 36).
            List<Entry> blocked = Where(scored, "BlockedOnly", 0, 9, used);
            List<Entry> warmPicked = FillAlongRamp(blocked, warmPositions, count, envelope, used, dip, placed);
            plan.Append("  warm-up: Blocked x").Append(warmPicked.Count).AppendLine();

            for (int r = 0; r < rules.Count; r++)
            {
                // Deficit 1-2 keeps the introduction gentle: at one colour down a board usually
                // needs a single cell of the rule, the clearest form it takes.
                List<Entry> practice = Where(scored, rules[r], 1, 2, used);
                List<Entry> chosen = FillAlongRamp(practice, runPositions[r], count, envelope, used, dip, placed);
                plan.Append("  run ").Append(r + 1).Append(": ").Append(rules[r])
                    .Append("  x").Append(chosen.Count).Append(Range(chosen)).AppendLine();

                if (chosen.Count >= Mathf.Min(MinTaughtLevels, runPositions[r].Count))
                {
                    taughtAt[rules[r]] = runPositions[r][chosen.Count - 1];
                }
                else
                {
                    plan.Append("    (").Append(rules[r]).Append(" has too few easy boards to count as taught: "
                        + "no combination will use it)").AppendLine();
                }
            }

            // ---- 4. which combination goes where -----------------------------------------------
            Dictionary<string, int> ruleRank = new Dictionary<string, int>();
            for (int i = 0; i < rules.Count; i++) { ruleRank[rules[i]] = i; }

            ComboContext ctx = new ComboContext
            {
                Combos = combos, Used = used, TaughtAt = taughtAt, RuleRank = ruleRank,
                MaxRank = Mathf.Max(1, rules.Count - 1), Envelope = envelope, Count = count,
                FirstComboPos = comboSlots.Count > 0 ? comboSlots[0].Pos : 0,
            };
            ctx.Span = Mathf.Max(1, count - 1 - ctx.FirstComboPos);

            HashSet<string> familiesSeen = new HashSet<string>();
            List<int> unfilledCombo = new List<int>();
            int rankedCombos = 0, unrankedCombos = 0;
            StringBuilder comboLines = new StringBuilder();

            foreach (ComboSlot cs in comboSlots)
            {
                string[] tokens = ComboBandFamilies[cs.Band];
                string wanted = tokens[cs.J % tokens.Length];

                Entry pick = null;
                bool pickedFirst = false;
                foreach (string family in FamilyOrder(wanted))
                {
                    bool first = !familiesSeen.Contains(family);
                    pick = ChooseCombo(ctx, family, cs.Pos, first);
                    if (pick != null) { pickedFirst = first; break; }
                }

                if (pick == null) { unfilledCombo.Add(cs.Pos); continue; }

                placed[cs.Pos] = pick;
                used.Add(pick.Data);
                familiesSeen.Add(pick.Family);
                ctx.PreviousPairing = pick.Pairing;
                ctx.Recent.Add(pick.Pairing);
                if (ctx.Recent.Count > 3) { ctx.Recent.RemoveAt(0); }

                if (pick.Unranked)
                {
                    unrankedCombos++;
                    // No score to dip, so ease the way in: the single level just before it.
                    if (pickedFirst && cs.Pos > 0 && slots[cs.Pos - 1] == SlotKind.Single)
                    {
                        dip[cs.Pos - 1] = Mathf.Max(dip[cs.Pos - 1], LeadInDip);
                    }
                }
                else { rankedCombos++; }

                comboLines.Append("    L").Append(cs.Pos + 1).Append(": ").Append(pick.Family).Append("  ")
                    .Append(pick.Pairing)
                    .Append(pick.Unranked ? "  (unranked)" : "  " + pick.Score.ToString("0"))
                    .Append(pickedFirst ? "  [first of its kind]" : string.Empty).AppendLine();
            }

            // Breathers: one single-rule level per band of ten from level 31, easier than the ramp.
            // The last band is left alone -- the finale should not open with a dip.
            for (int start = (int)(0.30f * count); start + 10 <= count - count / 10 + 1; start += 10)
            {
                int bestPos = -1, bestGap = int.MaxValue;
                for (int p = start; p < Mathf.Min(count, start + 10); p++)
                {
                    if (slots[p] != SlotKind.Single) { continue; }
                    int gap = Mathf.Abs(p - (start + 4));
                    if (gap < bestGap) { bestGap = gap; bestPos = p; }
                }
                if (bestPos >= 0) { dip[bestPos] = Mathf.Max(dip[bestPos], BreatherDip); }
            }

            // ---- 5. fill the rest of the single slots, block by block ---------------------------
            List<Entry> gentle = WhereAnyRule(scored, 1, 2, used);
            List<Entry> consolidated = FillAlongRamp(gentle, consolidationPositions, count, envelope, used, dip, placed);
            plan.Append("  consolidation: x").Append(consolidated.Count).Append(Range(consolidated)).AppendLine();

            List<Entry> hard = WhereAnyRule(scored, 2, 3, used);
            List<Entry> escalated = FillAlongRamp(hard, escalationPositions, count, envelope, used, dip, placed);
            plan.Append("  escalation: x").Append(escalated.Count).Append(Range(escalated)).AppendLine();

            // Anything unfilled -- a rule that ran out, or a combination slot with no combination
            // left -- is topped up from whatever remains, nearest the ramp, so the pack is always
            // the requested length.
            List<int> gaps = new List<int>();
            for (int p = 0; p < count; p++) { if (placed[p] == null) { gaps.Add(p); } }
            if (gaps.Count > 0)
            {
                List<Entry> spare = WhereAnyRule(scored, 0, 9, used);
                FillAlongRamp(spare, gaps, count, envelope, used, dip, placed);
                plan.Append("  topped up ").Append(gaps.Count).Append(" slot(s)")
                    .Append(unfilledCombo.Count > 0 ? ", " + unfilledCombo.Count + " of them combination slots with no combination left" : string.Empty)
                    .AppendLine();
            }

            plan.Append("  unranked: ").Append(structuralCount).Append(" structural levels, every ").Append(spacing).AppendLine();
            plan.Append("  combinations: ").Append(rankedCombos + unrankedCombos).Append(" (").Append(rankedCombos)
                .Append(" scored, ").Append(unrankedCombos).Append(" placed by position)").AppendLine();
            plan.Append(comboLines);

            for (int p = 0; p < count; p++)
            {
                if (placed[p] == null)
                {
                    planText = "schedule could not fill level " + (p + 1) + "\n" + plan;
                    return false;
                }
            }

            ordered = new List<Entry>(placed);
            planText = plan.ToString();
            return true;
        }

        /// <summary>Where the combination levels sit: evenly across the free levels of each band.
        /// If fewer combinations exist than slots, the EARLIEST bands lose theirs first.</summary>
        private static List<ComboSlot> PlanComboSlots(int count, int comboTarget, SlotKind[] slots, int available)
        {
            List<ComboSlot> result = new List<ComboSlot>();
            if (comboTarget <= 0 || available <= 0) { return result; }

            int bands = ComboBandWeights.Length;
            int[] n = new int[bands];
            int sum = 0;
            for (int b = 0; b < bands; b++)
            {
                n[b] = Mathf.RoundToInt(ComboBandWeights[b] * comboTarget / (float)AdvancedComboLevels);
                sum += n[b];
            }
            int excess = sum - available;
            for (int b = 0; b < bands && excess > 0; b++)
            {
                int cut = Mathf.Min(n[b], excess);
                n[b] -= cut;
                excess -= cut;
            }

            for (int b = 0; b < bands; b++)
            {
                int lo = Mathf.RoundToInt(ComboBandEdges[b] * count);
                int hi = b == bands - 1 ? count : Mathf.RoundToInt(ComboBandEdges[b + 1] * count);

                List<int> free = new List<int>();
                for (int p = lo; p < hi && p < count; p++) { if (slots[p] == SlotKind.Single) { free.Add(p); } }

                int take = Mathf.Min(n[b], free.Count);
                for (int j = 0; j < take; j++)
                {
                    int idx = Mathf.Min(free.Count - 1, (int)((j + 0.5f) * free.Count / take));
                    result.Add(new ComboSlot { Pos = free[idx], Band = b, J = j });
                }
            }
            return result;
        }

        private static IEnumerable<string> FamilyOrder(string wanted)
        {
            yield return wanted;
            foreach (string f in AllFamilies) { if (f != wanted) { yield return f; } }
        }

        private static List<int> Slice(List<int> all, ref int cursor, int n)
        {
            List<int> part = new List<int>();
            for (int i = 0; i < n && cursor < all.Count; i++) { part.Add(all[cursor]); cursor++; }
            return part;
        }

        /// <summary>
        /// The best unused combination of <paramref name="family"/> for level <paramref name="pos"/>, or
        /// null. Never the same pairing as the previous combination, and a pairing seen in the last three
        /// is penalised. Scored families pick the board nearest the ramp (less the first-of-kind dip);
        /// unscored ones pick by how hard the overlay rule is to read -- gentlest first, harder as the
        /// pack goes on.
        /// </summary>
        private static Entry ChooseCombo(ComboContext ctx, string family, int pos, bool firstOfKind)
        {
            float target = RampTargetAt(pos, ctx.Count, ctx.Envelope);
            Entry best = null;
            float bestCost = float.MaxValue;

            foreach (Entry c in ctx.Combos)
            {
                if (c.Family != family || ctx.Used.Contains(c.Data)) { continue; }
                if (!IsTaught(c.Mechanic, ctx.TaughtAt, pos) || !IsTaught(c.RuleB, ctx.TaughtAt, pos)) { continue; }

                string pairing = c.Pairing;
                if (pairing == ctx.PreviousPairing) { continue; }

                float cost;
                if (!c.Unranked)
                {
                    cost = Mathf.Abs(c.Score - (target - (firstOfKind ? FirstOfKindDip : 0f)));
                }
                else
                {
                    int rank;
                    if (!ctx.RuleRank.TryGetValue(c.RuleB, out rank)) { rank = ctx.MaxRank; }
                    int want = firstOfKind
                        ? 0
                        : Mathf.RoundToInt(ctx.MaxRank * Mathf.Clamp01((pos - ctx.FirstComboPos) / (float)ctx.Span));
                    cost = Mathf.Abs(rank - want) * 10f;
                }
                if (ctx.Recent.Contains(pairing)) { cost += 8f; }

                if (cost < bestCost) { bestCost = cost; best = c; }
            }
            return best;
        }

        private static bool IsTaught(string rule, Dictionary<string, int> taughtAt, int pos)
        {
            if (string.IsNullOrEmpty(rule)) { return true; }
            int at;
            return taughtAt.TryGetValue(rule, out at) && at < pos;
        }

        /// <summary>
        /// Fills <paramref name="positions"/> with the candidates nearest the ramp at each (less any dip),
        /// with a small penalty for repeating the rule of the level just before. Stops, leaving the rest
        /// unfilled, when the candidates run out.
        /// </summary>
        private static List<Entry> FillAlongRamp(List<Entry> candidates, List<int> positions, int packLength,
            List<float> envelope, HashSet<LevelData> used, float[] dip, Entry[] placed)
        {
            List<Entry> picked = new List<Entry>();
            if (candidates == null) { return picked; }

            foreach (int pos in positions)
            {
                float target = RampTargetAt(pos, packLength, envelope) - dip[pos];
                Entry prev = pos > 0 ? placed[pos - 1] : null;

                Entry best = null;
                float bestCost = float.MaxValue;
                foreach (Entry c in candidates)
                {
                    if (used.Contains(c.Data)) { continue; }
                    float cost = Mathf.Abs(c.Score - target) + RepeatPenalty(prev, c);
                    if (cost < bestCost) { bestCost = cost; best = c; }
                }

                if (best == null) { break; }
                placed[pos] = best;
                used.Add(best.Data);
                picked.Add(best);
            }
            return picked;
        }

        private static float RepeatPenalty(Entry prev, Entry c)
        {
            if (prev == null || prev.IsStructural) { return 0f; }
            if (prev.Family == null) { return prev.Mechanic == c.Mechanic ? 6f : 0f; }
            return (prev.Mechanic == c.Mechanic || prev.RuleB == c.Mechanic) ? 3f : 0f;
        }
    }
}
