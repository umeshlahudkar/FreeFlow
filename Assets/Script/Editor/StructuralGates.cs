using System.Collections.Generic;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Structural well-formedness checks, applied to a board's SOLUTION rather than to its
    /// difficulty.
    ///
    /// <b>Why gates and not a score.</b> PuzzleMadness, who publish Numberlink commercially, build
    /// puzzles by laying random links, filling isolated regions and merging adjacent ones -- which
    /// is recognisably our own refinement-and-merge-down pipeline. What they do differently is that
    /// they <b>do not rate difficulty at all</b>. They gate on structure, and they say the rules
    /// were arrived at by building puzzles, playing them, and adding a constraint for each thing
    /// they disliked. That is a cheaper and more reliable instrument than a difficulty model, and
    /// it is orthogonal to one: a board can be structurally sound and still trivial.
    ///
    /// Their published rules, and what each means here:
    ///
    ///   - <b>every link at least 3 cells</b>, a single 2-cell link restarting the whole board. A
    ///     2-cell link is two adjacent dots -- a pair the player connects without a decision, and
    ///     exactly the kind of free colour that makes a board collapse. We had no such rule.
    ///   - <b>a deliberate spread of link lengths</b>, both short and long. Uniform-length paths
    ///     read as a tiling rather than a puzzle. We had no such rule either.
    ///   - <b>total link length between 85% and 115% of the grid's cells.</b> This one is already
    ///     satisfied by construction and is checked only as an assertion: we require FULL coverage,
    ///     which pins the ratio at exactly 100%. It is in their list because they do not.
    ///
    /// <b>A fourth gate, not from PuzzleMadness but from the genre's own definition</b>, is that
    /// NO LINK MAY TOUCH ITSELF -- thomasahle's reference Numberlink generator states a
    /// well-formed puzzle as "the solution uses 100% of the paper and no link touches itself".
    /// §6.31 recorded that we already satisfied this. That was wrong, and wrong in the way §0's
    /// first gotcha warns about: it was checked on three levels, read 0, 1 and 0, and generalised
    /// from a sample that contained a violation. Measured across a proper spread, five of ten
    /// Classic levels self-touch.
    ///
    /// This is not cosmetic. A self-touching solution invalidates the corner dual law outright, so
    /// <see cref="HumanSolver"/> cannot use its strongest technique on such a board -- the two are
    /// wired together through <see cref="Report.SelfTouches"/>.
    /// </summary>
    public static class StructuralGates
    {
        /// <summary>Two adjacent dots are a colour the player gets for free.</summary>
        public const int MinPathCells = 3;

        /// <summary>
        /// (longest - shortest) / mean. A starting value, not a measured one: it asks that the
        /// range of path lengths span at least three quarters of the mean, which admits 4-and-11
        /// on a mean of 8 but rejects 7-and-9. Tune it against play before trusting it.
        /// </summary>
        public const float DefaultMinLengthSpread = 0.75f;

        public sealed class Report
        {
            public bool Passed;
            public int ShortestPath;
            public int LongestPath;
            public float MeanPath;

            /// <summary>(longest - shortest) / mean.</summary>
            public float LengthSpread;

            /// <summary>Distinct covered cells as a fraction of usable ones. Should be 1.0.</summary>
            public float Coverage;

            /// <summary>Pairs of cells on the SAME path that are adjacent without being consecutive
            /// -- the link doubling back alongside itself. Must be 0.</summary>
            public int SelfTouches;

            /// <summary>Empty when <see cref="Passed"/>; otherwise which gate failed and why.</summary>
            public string FailureReason = string.Empty;

            public override string ToString()
            {
                return (Passed ? "pass" : "FAIL: " + FailureReason)
                    + "  paths " + ShortestPath + ".." + LongestPath
                    + "  mean=" + MeanPath.ToString("0.0")
                    + "  spread=" + LengthSpread.ToString("0.00")
                    + "  coverage=" + Coverage.ToString("0.00")
                    + "  selfTouch=" + SelfTouches;
            }
        }

        public static Report Evaluate(PuzzleSolver.SolveResult solved, int usableCells,
            float minLengthSpread = DefaultMinLengthSpread)
        {
            Report report = new Report();

            if (solved == null || solved.Solutions == null || solved.Solutions.Count == 0)
            {
                report.FailureReason = "no solution to inspect";
                return report;
            }

            int shortest = int.MaxValue, longest = 0, total = 0;
            HashSet<int> covered = new HashSet<int>();

            for (int i = 0; i < solved.Solutions.Count; i++)
            {
                List<(int Row, int Col)> cells = solved.Solutions[i].Cells;
                int length = cells.Count;
                if (length < shortest) { shortest = length; }
                if (length > longest) { longest = length; }
                total += length;

                for (int j = 0; j < cells.Count; j++)
                {
                    // A shared destination and a bridge are both covered by more than one path, so
                    // the distinct-cell set is the only honest coverage count.
                    covered.Add(cells[j].Row * 1024 + cells[j].Col);
                }
            }

            report.ShortestPath = shortest;
            report.LongestPath = longest;
            report.MeanPath = total / (float)solved.Solutions.Count;
            report.LengthSpread = report.MeanPath > 0f ? (longest - shortest) / report.MeanPath : 0f;
            report.Coverage = usableCells > 0 ? covered.Count / (float)usableCells : 0f;
            report.SelfTouches = CountSelfTouches(solved);

            if (report.SelfTouches > 0)
            {
                report.FailureReason = report.SelfTouches + " self-touch"
                    + (report.SelfTouches == 1 ? "" : "es") + " (a link running alongside itself)";
                return report;
            }
            if (shortest < MinPathCells)
            {
                report.FailureReason = "a " + shortest + "-cell link (minimum " + MinPathCells + ")";
                return report;
            }
            if (report.LengthSpread < minLengthSpread)
            {
                report.FailureReason = "path lengths too uniform (spread "
                    + report.LengthSpread.ToString("0.00") + " < " + minLengthSpread.ToString("0.00") + ")";
                return report;
            }
            if (report.Coverage < 0.999f)
            {
                report.FailureReason = "coverage " + report.Coverage.ToString("0.000") + ", expected 1.0";
                return report;
            }

            report.Passed = true;
            return report;
        }

        /// <summary>
        /// Adjacent-but-not-consecutive cell pairs within one path. Counted on a per-path index map
        /// rather than by comparing every cell against every other, so the cost stays linear in
        /// path length instead of quadratic.
        /// </summary>
        private static int CountSelfTouches(PuzzleSolver.SolveResult solved)
        {
            int touches = 0;

            for (int p = 0; p < solved.Solutions.Count; p++)
            {
                List<(int Row, int Col)> cells = solved.Solutions[p].Cells;
                Dictionary<int, int> indexAt = new Dictionary<int, int>(cells.Count);
                for (int i = 0; i < cells.Count; i++)
                {
                    indexAt[cells[i].Row * 1024 + cells[i].Col] = i;
                }

                for (int i = 0; i < cells.Count; i++)
                {
                    // Right and down only: looking at all four neighbours would count each
                    // touching pair from both ends.
                    CountIfTouching(indexAt, cells[i].Row, cells[i].Col + 1, i, ref touches);
                    CountIfTouching(indexAt, cells[i].Row + 1, cells[i].Col, i, ref touches);
                }
            }
            return touches;
        }

        private static void CountIfTouching(Dictionary<int, int> indexAt, int row, int col,
            int fromIndex, ref int touches)
        {
            if (!indexAt.TryGetValue(row * 1024 + col, out int other)) { return; }
            int gap = other - fromIndex;
            if (gap < 0) { gap = -gap; }
            if (gap > 1) { touches++; }
        }
    }
}
