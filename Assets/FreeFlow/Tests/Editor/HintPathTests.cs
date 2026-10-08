using System.Collections.Generic;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers <see cref="HintPath"/>: turning a level's stored answer -- one pair id per cell, with
    /// no ordering in it -- back into the route a hint draws.
    ///
    /// The cases that matter are the ones a greedy "walk to whichever neighbour is also mine"
    /// reconstruction gets wrong, because that is the implementation this would otherwise have been:
    /// two dots that happen to be adjacent, and a route that runs alongside itself. Both appear
    /// constantly in real boards, and both fail silently -- the hint would draw a short-cut, joining
    /// the pair while leaving cells of its own uncovered, which no other check would catch.
    /// </summary>
    public class HintPathTests
    {
        private Block[,] grid;

        [TearDown]
        public void TearDown()
        {
            if (grid == null) { return; }
            foreach (Block block in grid) { BlockTestHarness.Destroy(block); }
            grid = null;
        }

        private Block[,] CreateGrid(int rows, int cols)
        {
            grid = new Block[rows, cols];
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    grid[i, j] = BlockTestHarness.CreateBlock(i, j);
                }
            }
            return grid;
        }

        /// <summary>Cell coordinates of <paramref name="path"/>, for readable assertions.</summary>
        private static List<(int, int)> Coords(List<Block> path)
        {
            List<(int, int)> cells = new List<(int, int)>();
            if (path == null) { return cells; }
            for (int i = 0; i < path.Count; i++) { cells.Add((path[i].Row_ID, path[i].Coloum_ID)); }
            return cells;
        }

        // -- Ordering ---------------------------------------------------------------------------

        [Test]
        public void AStraightPair_IsOrderedFromOneDotToTheOther()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);

            int[,] solution = { { 1, 1, 1 } };

            List<Block> path = HintPath.Build(grid, 1, 3, solution, 1);

            CollectionAssert.AreEqual(new[] { (0, 0), (0, 1), (0, 2) }, Coords(path));
        }

        [Test]
        public void AdjacentDots_DoNotEndTheRouteWhileTheirOwnCellsAreUncovered()
        {
            // The two dots are one step apart, so the shortest join is a single move -- and wrong:
            // the answer gives this pair all six cells, and a hint that drew the short-cut would
            // leave four of them empty on a board that only completes when every cell is covered.
            CreateGrid(2, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[1, 0], pairId: 1);

            int[,] solution =
            {
                { 1, 1, 1 },
                { 1, 1, 1 }
            };

            List<Block> path = HintPath.Build(grid, 2, 3, solution, 1);

            CollectionAssert.AreEqual(
                new[] { (0, 0), (0, 1), (0, 2), (1, 2), (1, 1), (1, 0) }, Coords(path));
        }

        [Test]
        public void ARouteRunningAlongsideItself_StaysAWellFormedRoute()
        {
            // A single pair over a whole 3x3 has to snake, so most of its cells are adjacent to a
            // cell they are several steps from along the route. More than one such snake exists here
            // and any of them is a correct answer, so this asserts the PROPERTIES a route must have
            // rather than one particular ordering: every cell used once, each step to a neighbour,
            // both ends on the dots.
            CreateGrid(3, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 3);
            BlockTestHarness.SetDot(grid[2, 2], pairId: 3);

            int[,] solution =
            {
                { 3, 3, 3 },
                { 3, 3, 3 },
                { 3, 3, 3 }
            };

            List<Block> path = HintPath.Build(grid, 3, 3, solution, 3);

            Assert.IsNotNull(path);
            Assert.AreEqual(9, path.Count, "every cell the answer gave the pair is on the route");
            CollectionAssert.AllItemsAreUnique(path);
            Assert.AreSame(grid[0, 0], path[0]);
            Assert.AreSame(grid[2, 2], path[path.Count - 1]);

            for (int i = 0; i < path.Count - 1; i++)
            {
                int stepped = System.Math.Abs(path[i].Row_ID - path[i + 1].Row_ID)
                            + System.Math.Abs(path[i].Coloum_ID - path[i + 1].Coloum_ID);
                Assert.AreEqual(1, stepped, "consecutive cells of a route are adjacent");
            }
        }

        [Test]
        public void OnePairsRoute_IgnoresCellsTheAnswerGaveAnother()
        {
            CreateGrid(3, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);
            BlockTestHarness.SetDot(grid[1, 0], pairId: 2);
            BlockTestHarness.SetDot(grid[2, 0], pairId: 2);

            int[,] solution =
            {
                { 1, 1, 1 },
                { 2, 2, 2 },
                { 2, 2, 2 }
            };

            CollectionAssert.AreEqual(new[] { (0, 0), (0, 1), (0, 2) },
                Coords(HintPath.Build(grid, 3, 3, solution, 1)));

            CollectionAssert.AreEqual(new[] { (1, 0), (1, 1), (1, 2), (2, 2), (2, 1), (2, 0) },
                Coords(HintPath.Build(grid, 3, 3, solution, 2)));
        }

        // -- The board's own movement rules ------------------------------------------------------

        [Test]
        public void AWallOnTheDirectEdge_ForcesTheLongWayRound()
        {
            CreateGrid(2, 2);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 1], pairId: 1);
            BlockTestHarness.SetWall(grid[0, 0], Direction.Right);

            int[,] solution =
            {
                { 1, 1 },
                { 1, 1 }
            };

            List<Block> path = HintPath.Build(grid, 2, 2, solution, 1);

            CollectionAssert.AreEqual(new[] { (0, 0), (1, 0), (1, 1), (0, 1) }, Coords(path));
        }

        [Test]
        public void AnArrowsForcedExit_ShapesTheRoute()
        {
            // The arrow at (1,0) may only be left downward, so the route cannot turn east across the
            // middle row; it has to run down the first column and back along the bottom.
            CreateGrid(3, 2);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 1], pairId: 1);
            BlockTestHarness.SetArrow(grid[1, 0], Direction.Down);

            int[,] solution =
            {
                { 1, 1 },
                { 1, 1 },
                { 1, 1 }
            };

            List<Block> path = HintPath.Build(grid, 3, 2, solution, 1);

            CollectionAssert.AreEqual(
                new[] { (0, 0), (1, 0), (2, 0), (2, 1), (1, 1), (0, 1) }, Coords(path));
        }

        // -- Bridges: the answer records one colour per cell, and a crossing has two --------------

        [Test]
        public void APairCrossesABridgeTheAnswerGaveTheOtherPair()
        {
            // The stored column holds one pair id per cell, so at a crossing only one of the two
            // paths is recorded there. The other pair's cells are then split in two by a cell it
            // does in fact pass straight through, and its route has to cross anyway.
            CreateGrid(3, 3);
            BlockTestHarness.SetDot(grid[1, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[1, 2], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 1], pairId: 2);
            BlockTestHarness.SetDot(grid[2, 1], pairId: 2);
            BlockTestHarness.SetBridge(grid[1, 1]);

            // (1,1) is recorded as pair 2's; pair 1 gets no entry for it at all.
            int[,] solution =
            {
                { 0, 2, 0 },
                { 1, 2, 1 },
                { 0, 2, 0 }
            };

            CollectionAssert.AreEqual(new[] { (1, 0), (1, 1), (1, 2) },
                Coords(HintPath.Build(grid, 3, 3, solution, 1)));

            CollectionAssert.AreEqual(new[] { (0, 1), (1, 1), (2, 1) },
                Coords(HintPath.Build(grid, 3, 3, solution, 2)));
        }

        [Test]
        public void ABridgeCannotBeTurnedOn()
        {
            // The answer gives this pair the bridge at (1,1), but joining its two dots through it
            // would mean entering southbound and leaving westbound -- a lane change in mid-air. The
            // only way round runs through (0,0), which the answer gave to someone else, so there is
            // no route at all and saying so is better than drawing an illegal one.
            CreateGrid(2, 2);
            BlockTestHarness.SetDot(grid[0, 1], pairId: 1);
            BlockTestHarness.SetDot(grid[1, 0], pairId: 1);
            BlockTestHarness.SetBridge(grid[1, 1]);

            int[,] solution =
            {
                { 2, 1 },
                { 1, 1 }
            };

            Assert.IsNull(HintPath.Build(grid, 2, 2, solution, 1));
        }

        // -- Shared destinations -----------------------------------------------------------------

        [Test]
        public void ARouteEndingOnASharedDestination_StartsAtThePlainDot()
        {
            // A drag may only begin at a plain dot -- a shared cell belongs to two pairs and a press
            // on it cannot say which was meant -- and the colour a segment draws in comes from its
            // first cell, which on a shared cell is the wrong pair's.
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1, secondPairId: 2);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 2);

            int[,] solution = { { 1, 2, 2 } };

            List<Block> path = HintPath.Build(grid, 1, 3, solution, 2);

            CollectionAssert.AreEqual(new[] { (0, 2), (0, 1), (0, 0) }, Coords(path));
        }

        // -- Declining rather than guessing ------------------------------------------------------

        [Test]
        public void APairTheAnswerNeverMentions_HasNoRoute()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);

            int[,] solution = { { 0, 0, 0 } };

            // Only the two dots are the pair's, and they are not adjacent, so no route covers them.
            Assert.IsNull(HintPath.Build(grid, 1, 3, solution, 1));
        }

        // -- Reading the answer off level data ---------------------------------------------------

        [Test]
        public void ReadSolution_AnswersNullForALevelThatRecordedNoAnswer()
        {
            Assert.IsNull(HintPath.ReadSolution(FourByFour(null)),
                "a level generated before the column existed carries no answer");

            Assert.IsNull(HintPath.ReadSolution(FourByFour(0)),
                "an all-zero column is a level that never recorded one, not one covered by nobody");
        }

        [Test]
        public void ReadSolution_ReadsTheColumnBackAsAGrid()
        {
            int[,] solution = HintPath.ReadSolution(FourByFour(7));

            Assert.IsNotNull(solution);
            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 4; c++) { Assert.AreEqual(7, solution[r, c]); }
            }
        }

        /// <summary>
        /// A 4x4 level whose only interesting column is its stored answer: every cell covered by
        /// <paramref name="pairId"/>, or the column left unwritten when it is null.
        /// </summary>
        private static LevelData FourByFour(int? pairId)
        {
            LevelData data = new LevelData
            {
                gridSize = GridSize.GridSize_4X4,
                pairCount = 1,
                gridRows = new GridRow[4]
            };

            for (int r = 0; r < 4; r++)
            {
                int[] row = null;
                if (pairId.HasValue)
                {
                    row = new int[4];
                    for (int c = 0; c < 4; c++) { row[c] = pairId.Value; }
                }

                data.gridRows[r] = new GridRow
                {
                    coloum = new PairColorType[4],
                    solutionPairId = row
                };
            }

            return data;
        }
    }
}
