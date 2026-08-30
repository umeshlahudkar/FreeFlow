using System.Collections.Generic;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers the difficulty instruments added after the second research pass: the structural
    /// gates, the relaxation metrics, and the blend that combines them.
    ///
    /// Every test here is a regression for a bug that actually happened. The instruments were
    /// written, run against ten shipped levels, and each of the four defects below showed up in
    /// that first run -- two of them making a malformed board score ABOVE the Flow Free reference.
    /// The boards they are checked on are deliberately tiny and rigid, because these are checks on
    /// the measuring code, not on the puzzle.
    /// </summary>
    public class DifficultyModelTests
    {
        /// <summary>
        /// Four colours, one per row, dots on the left and right edges. The solution is four
        /// straight lines and it is unique: any path that dips into a neighbouring row steals the
        /// only two interior cells that row's own colour could use, and strands it.
        ///
        /// Small and rigid on purpose -- these are checks on the measuring code, not on the puzzle.
        /// </summary>
        private static LevelData FourByFour()
        {
            LevelData data = new LevelData
            {
                gridSize = GridSize.GridSize_4X4,
                pairCount = 4,
                gridRows = new GridRow[4]
            };

            PairColorType[] colours =
            {
                PairColorType.Red, PairColorType.Blue, PairColorType.Green, PairColorType.Yellow
            };

            for (int r = 0; r < 4; r++)
            {
                data.gridRows[r] = new GridRow
                {
                    coloum = new PairColorType[4],
                    // pairId is left EMPTY on purpose -- see the relaxation test below.
                    pairId = new int[4],
                    blockType = new BlockType[4],
                    wallMask = new int[4],
                    requiredEntryDirection = new Direction[4],
                    forcedExitDirection = new Direction[4],
                    secondPairId = new int[4]
                };
                data.gridRows[r].coloum[0] = colours[r];
                data.gridRows[r].coloum[3] = colours[r];
            }
            return data;
        }

        private static PuzzleSolver.SolveResult ResultWithPaths(params int[][] flatCells)
        {
            PuzzleSolver.SolveResult result = new PuzzleSolver.SolveResult
            {
                Status = PuzzleSolver.SolveStatus.Solved,
                Solutions = new List<PuzzleSolver.PairSolution>()
            };

            for (int i = 0; i < flatCells.Length; i++)
            {
                List<(int Row, int Col)> cells = new List<(int, int)>();
                for (int j = 0; j + 1 < flatCells[i].Length; j += 2)
                {
                    cells.Add((flatCells[i][j], flatCells[i][j + 1]));
                }
                result.Solutions.Add(new PuzzleSolver.PairSolution { PairId = i + 1, Cells = cells });
            }
            return result;
        }

        [Test]
        public void AShortLink_FailsTheGate()
        {
            // PuzzleMadness restart a whole board over a single 2-cell link, because two adjacent
            // dots are a colour the player connects without making a decision.
            PuzzleSolver.SolveResult solved = ResultWithPaths(
                new[] { 0, 0, 0, 1 },                                // 2 cells -- the offender
                new[] { 1, 0, 1, 1, 1, 2, 1, 3, 1, 4, 1, 5 });       // 6, straight, no self-touch

            StructuralGates.Report report = StructuralGates.Evaluate(solved, 8);

            Assert.IsFalse(report.Passed);
            Assert.AreEqual(2, report.ShortestPath);
            StringAssert.Contains("2-cell link", report.FailureReason);
        }

        [Test]
        public void ALinkRunningAlongsideItself_IsCounted()
        {
            // A U-turn in a 2-wide corridor: cells 0 and 3 are adjacent but three apart along the
            // path. This is the shape the corner dual law forbids, and the shape our generator was
            // quietly producing on 26 of 50 shipped levels.
            PuzzleSolver.SolveResult solved = ResultWithPaths(
                new[] { 0, 0, 1, 0, 1, 1, 0, 1 });

            StructuralGates.Report report = StructuralGates.Evaluate(solved, 4);

            Assert.AreEqual(1, report.SelfTouches, "one non-consecutive adjacency, counted once");
            Assert.IsFalse(report.Passed);
            StringAssert.Contains("self-touch", report.FailureReason);
        }

        [Test]
        public void AStraightPath_HasNoSelfTouch()
        {
            PuzzleSolver.SolveResult solved = ResultWithPaths(new[] { 0, 0, 0, 1, 0, 2, 0, 3 });

            Assert.AreEqual(0, StructuralGates.Evaluate(solved, 4).SelfTouches);
        }

        [Test]
        public void Relaxation_FindsColoursWhenThePairIdColumnIsEmpty()
        {
            // The bug this guards: the first version read LevelData.pairId, but BuildBlockGrid does
            // not use that column for a cell's primary identity -- it derives the id from the
            // COLOUR. On every level whose pairId column is blank (which is all of them), the
            // relaxation found no colours, removed none, and reported a confident zero for every
            // metric rather than admitting it had measured nothing.
            RelaxationMetrics.Result result = RelaxationMetrics.Measure(FourByFour());

            int variantsAttempted = result.Variants + result.UnsolvableVariants
                + result.InconclusiveVariants;
            Assert.AreEqual(4, variantsAttempted, "every colour should have been relaxed away in turn");
        }

        [Test]
        public void Blend_DropsFixednessWhenItWasNeverMeasured()
        {
            // On a board where removing ANY colour makes coverage impossible, every relaxed variant
            // is unsolvable and Fixedness stays 0 -- meaning "no measurement", not "nothing stayed
            // put". Feeding that 0 through the inversion scored it as maximally hard, which is how
            // a malformed 7x7 came out above the Flow Free reference on the first calibration run.
            DifficultyModel.Profile unmeasured = new DifficultyModel.Profile
            {
                Human = new HumanSolver.Rating { Assumptions = 0, Dependency = DifficultyModel.DependencyEasyFloor },
                Relaxation = new RelaxationMetrics.Result { Fixedness = 0f, Variants = 0 },
                Tangle = 0f
            };

            // Every measured term is at its EASIEST, so an honest blend has to return 0. If the
            // absent fixedness were counted it would contribute its full weight as "hardest".
            Assert.AreEqual(0f, DifficultyModel.Blend(unmeasured), 0.001f);
        }

        [Test]
        public void Blend_CountsFixednessWhenItWasMeasured()
        {
            DifficultyModel.Profile measured = new DifficultyModel.Profile
            {
                Human = new HumanSolver.Rating { Assumptions = 0, Dependency = DifficultyModel.DependencyEasyFloor },
                Relaxation = new RelaxationMetrics.Result { Fixedness = 0f, Variants = 3 },
                Tangle = 0f
            };

            Assert.Greater(DifficultyModel.Blend(measured), 0f,
                "a real fixedness of 0 means nothing held its colour, which is the hard end");
        }

        [Test]
        public void DependencyIsSampledOnABoardThatCanBeReasonedThrough()
        {
            LevelData data = FourByFour();
            Block[,] grid = LevelGenerator.BuildBlockGrid(data, out int rows, out int cols);
            try
            {
                HumanSolver.Rating rating = HumanSolver.Rate(grid, rows, cols);

                Assert.IsTrue(rating.Solved);
                Assert.AreEqual(0, rating.Assumptions, "both paths are forced from the start");
                Assert.Greater(rating.Dependency, 0f,
                    "a board with no opening deduction at all is Nikoli's failure mode, not a hard board");
            }
            finally { LevelGenerator.DestroyBlockGrid(grid); }
        }

        /// <summary>Invokes LevelGenerator's private stratified picker.</summary>
        private static List<(float Score, LevelData Data)> Stratify(float[] scores, int count)
        {
            List<(float Score, LevelData Data)> input = new List<(float, LevelData)>();
            for (int i = 0; i < scores.Length; i++) { input.Add((scores[i], new LevelData())); }

            System.Reflection.MethodInfo m = typeof(LevelGenerator).GetMethod("SelectStratified",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return (List<(float Score, LevelData Data)>)m.Invoke(null, new object[] { input, count });
        }

        [Test]
        public void Stratified_KeepsBothEndsOfTheRange()
        {
            float[] scores = new float[200];
            for (int i = 0; i < scores.Length; i++) { scores[i] = 40f + i * 0.25f; }   // 40..89.75

            List<(float Score, LevelData Data)> picked = Stratify(scores, 10);

            Assert.AreEqual(10, picked.Count);
            Assert.AreEqual(40f, picked[0].Score, 0.001f, "the pack has to open at the easy end");
            Assert.AreEqual(89.75f, picked[picked.Count - 1].Score, 0.001f, "and finish at the hard end");
        }

        [Test]
        public void Stratified_RampsMonotonically()
        {
            float[] scores = new float[200];
            for (int i = 0; i < scores.Length; i++) { scores[i] = 40f + (i % 50); }

            List<(float Score, LevelData Data)> picked = Stratify(scores, 12);

            for (int i = 1; i < picked.Count; i++)
            {
                Assert.GreaterOrEqual(picked[i].Score, picked[i - 1].Score,
                    "difficulty must not go backwards inside a pack");
            }
        }

        [Test]
        public void Stratified_SpreadsWhereTopNWouldBunch()
        {
            // 180 boards clustered at the easy end, 20 spread up to hard -- the shape a real pool
            // has. Top-N would take the 10 hardest and open the pack at 78; stratifying has to
            // cover the whole range instead.
            List<float> scores = new List<float>();
            for (int i = 0; i < 180; i++) { scores.Add(50f + (i % 5)); }
            for (int i = 0; i < 20; i++) { scores.Add(60f + i); }

            List<(float Score, LevelData Data)> picked = Stratify(scores.ToArray(), 10);

            float span = picked[picked.Count - 1].Score - picked[0].Score;
            Assert.Greater(span, 25f, "a stratified pack should span most of the available range");
        }

        [Test]
        public void Stratified_ReturnsEverythingWhenAskedForMoreThanItHas()
        {
            List<(float Score, LevelData Data)> picked = Stratify(new float[] { 3f, 1f, 2f }, 10);

            Assert.AreEqual(3, picked.Count);
            Assert.AreEqual(1f, picked[0].Score, 0.001f, "and still sorted");
        }

        [Test]
        public void StageOne_SkipsRelaxationButStillScores()
        {
            DifficultyModel.Profile full = DifficultyModel.Measure(FourByFour());
            DifficultyModel.Profile cheap = DifficultyModel.Measure(FourByFour(), 14, 2000000, false);

            Assert.IsNotNull(full.Relaxation, "the full model runs relaxation");
            Assert.IsNull(cheap.Relaxation, "stage one must not pay for it");

            Assert.IsTrue(cheap.Valid);
            Assert.AreEqual(full.Human.Assumptions, cheap.Human.Assumptions,
                "the cheap terms are identical -- only the relaxation term is absent");
            Assert.AreEqual(full.Structure.Passed, cheap.Structure.Passed);
        }

        [Test]
        public void AGeneratedBoard_CarriesItsOwnAnswer()
        {
            // The hint system reads solutionPairId instead of solving on device, because making the
            // levels harder took "find one solution" from 2.6 ms to 49.5 ms average and 771 ms at
            // worst -- on desktop. This checks the stored answer is the answer, which is the failure
            // that would otherwise be silent: wrong hints forever, with nothing else noticing.
            System.Reflection.MethodInfo build = typeof(LevelGenerator).GetMethod("BuildPlainLevelData",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            bool[,] usable = new bool[4, 4];
            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 4; c++) { usable[r, c] = true; }
            }

            // Four straight rows -- the same rigid partition FourByFour() describes.
            List<List<(int Row, int Col)>> paths = new List<List<(int, int)>>();
            for (int r = 0; r < 4; r++)
            {
                paths.Add(new List<(int, int)> { (r, 0), (r, 1), (r, 2), (r, 3) });
            }

            LevelData data = (LevelData)build.Invoke(null,
                new object[] { 4, usable, paths, new System.Random(7) });

            for (int r = 0; r < 4; r++)
            {
                int[] row = data.gridRows[r].solutionPairId;
                Assert.IsNotNull(row, "every generated row must carry its slice of the answer");

                int expected = (int)data.gridRows[r].coloum[0];   // the pair id IS the colour
                Assert.AreNotEqual(0, expected);
                for (int c = 0; c < 4; c++)
                {
                    Assert.AreEqual(expected, row[c],
                        "cell (" + r + "," + c + ") should be covered by its own row's colour");
                }
            }
        }

        [Test]
        public void TurningTheCornerDualOff_DoesNotChangeACleanBoardsVerdict()
        {
            // The law is only valid where no link touches itself. Switching it off must weaken the
            // rating, never break it -- the first version banned a whole CELL to a colour rather
            // than the single forbidden LINK, and that over-restriction made five provably solvable
            // boards report UNSOLVED.
            LevelData data = FourByFour();
            Block[,] grid = LevelGenerator.BuildBlockGrid(data, out int rows, out int cols);
            try
            {
                Assert.IsTrue(HumanSolver.Rate(grid, rows, cols, 6, true).Solved);
                Assert.IsTrue(HumanSolver.Rate(grid, rows, cols, 6, false).Solved);
            }
            finally { LevelGenerator.DestroyBlockGrid(grid); }
        }
    }
}
