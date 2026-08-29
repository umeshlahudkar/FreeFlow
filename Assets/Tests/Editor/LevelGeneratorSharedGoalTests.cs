using System.Collections.Generic;
using System.Reflection;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers shared-destination construction, via reflection (see LevelGeneratorBlockedCellTests'
    /// doc comment for why).
    ///
    /// Shared Destination is the second mechanic (after Bridge) that changes the SHAPE of the
    /// partition instead of decorating one: a path's two ends become its colour's dots, so two
    /// colours sharing a destination means two paths ending on one cell, which no rule laid over a
    /// finished partition can produce. It is built with the same node splitting as Bridge but aimed
    /// the opposite way -- a bridge's two nodes must be INTERIOR to their paths, a shared goal's
    /// must be ENDPOINTS -- and it gets there by seeding the two paths at the cell and anchoring
    /// them so they can only grow from the tail.
    ///
    /// These tests pin the properties that anchoring is supposed to buy. Each corresponds to a
    /// level the game would accept and mis-render or mis-solve rather than reject outright:
    ///   - the cell really is an endpoint of both paths (otherwise it is not a dot at all)
    ///   - exactly two colours claim it, never one or three
    ///   - the round-trip through LevelData carries the SECOND colour's identity, which
    ///     BuildBlockGrid silently dropped until this mechanic needed it
    /// </summary>
    public class LevelGeneratorSharedGoalTests
    {
        private static MethodInfo PartitionMethod()
        {
            foreach (MethodInfo m in typeof(LevelGenerator).GetMethods(
                BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (m.Name == "TryGeneratePathPartition" && m.GetParameters().Length == 7) { return m; }
            }
            return null;
        }

        private static readonly MethodInfo ChooseSharedGoalCellsMethod = typeof(LevelGenerator).GetMethod(
            "ChooseSharedGoalCells", BindingFlags.NonPublic | BindingFlags.Static);

        private static List<List<(int Row, int Col)>> Partition(int size, bool[,] usable, int usableCount,
            int pathCount, HashSet<(int Row, int Col)> sharedGoals, System.Random rng)
        {
            return (List<List<(int, int)>>)PartitionMethod().Invoke(
                null, new object[] { size, usable, usableCount, pathCount, null, sharedGoals, rng });
        }

        private static HashSet<(int Row, int Col)> ChooseSharedGoalCells(int size, bool[,] usable,
            HashSet<(int Row, int Col)> bridges, int count, System.Random rng)
        {
            return (HashSet<(int, int)>)ChooseSharedGoalCellsMethod.Invoke(
                null, new object[] { size, usable, bridges, count, rng });
        }

        private static bool[,] FullBoard(int size)
        {
            bool[,] usable = new bool[size, size];
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++) { usable[r, c] = true; }
            }
            return usable;
        }

        [Test]
        public void TheSharedCellIsAnEndpointOfBothPathsThatClaimIt()
        {
            var goals = new HashSet<(int, int)> { (3, 3) };

            int produced = 0;
            for (int seed = 0; seed < 200; seed++)
            {
                var paths = Partition(7, FullBoard(7), 49, 6, goals, new System.Random(seed));
                if (paths == null) { continue; }
                produced++;

                foreach (var path in paths)
                {
                    int at = path.IndexOf((3, 3));
                    if (at < 0) { continue; }

                    bool isEndpoint = at == 0 || at == path.Count - 1;
                    Assert.IsTrue(isEndpoint,
                        "a shared destination is a DOT -- a path merely passing through it would "
                        + "leave the cell owned by nobody, and nothing downstream would notice");
                }
            }

            Assert.Greater(produced, 0, "expected at least one successful partition across 200 seeds");
        }

        [Test]
        public void ExactlyTwoColoursClaimTheSharedCell()
        {
            var goals = new HashSet<(int, int)> { (2, 4) };

            int produced = 0;
            for (int seed = 0; seed < 200; seed++)
            {
                var paths = Partition(7, FullBoard(7), 49, 6, goals, new System.Random(seed));
                if (paths == null) { continue; }
                produced++;

                int owners = 0;
                foreach (var path in paths)
                {
                    if (path.Contains((2, 4))) { owners++; }
                }

                Assert.AreEqual(2, owners, "a shared destination joins exactly two colours");
            }

            Assert.Greater(produced, 0);
        }

        [Test]
        public void EveryOtherCellIsStillCoveredExactlyOnce()
        {
            var goals = new HashSet<(int, int)> { (3, 3) };

            List<List<(int Row, int Col)>> paths = null;
            for (int seed = 0; seed < 200 && paths == null; seed++)
            {
                paths = Partition(7, FullBoard(7), 49, 6, goals, new System.Random(seed));
            }
            Assert.IsNotNull(paths);

            var counts = new Dictionary<(int, int), int>();
            foreach (var path in paths)
            {
                foreach (var cell in path)
                {
                    counts.TryGetValue(cell, out int n);
                    counts[cell] = n + 1;
                }
            }

            Assert.AreEqual(49, counts.Count, "every cell of the board must be covered");
            foreach (var kv in counts)
            {
                int expected = kv.Key == (3, 3) ? 2 : 1;
                Assert.AreEqual(expected, kv.Value, "cell " + kv.Key + " covered the wrong number of times");
            }
        }

        [Test]
        public void TheTwoSharersArriveOnDifferentEdges()
        {
            // LevelData caps sharing at four colours because a path ending in a cell claims the
            // edge it arrived through, and a cell has four. Two sharers must therefore differ.
            var goals = new HashSet<(int, int)> { (3, 3) };

            for (int seed = 0; seed < 120; seed++)
            {
                var paths = Partition(7, FullBoard(7), 49, 6, goals, new System.Random(seed));
                if (paths == null) { continue; }

                var arrivals = new List<(int, int)>();
                foreach (var path in paths)
                {
                    int at = path.IndexOf((3, 3));
                    if (at < 0) { continue; }
                    arrivals.Add(at == 0 ? path[1] : path[path.Count - 2]);
                }

                Assert.AreEqual(2, arrivals.Count);
                Assert.AreNotEqual(arrivals[0], arrivals[1],
                    "both colours would be claiming the same edge into the shared cell");
            }
        }

        [Test]
        public void ChooseSharedGoalCells_NeverPicksABridgeCell()
        {
            // A bridge is a cell paths pass THROUGH; a shared goal is one they END at.
            var bridges = new HashSet<(int, int)> { (3, 3), (1, 5) };

            for (int seed = 0; seed < 40; seed++)
            {
                var chosen = ChooseSharedGoalCells(7, FullBoard(7), bridges, 2, new System.Random(seed));

                foreach ((int Row, int Col) cell in chosen)
                {
                    Assert.IsFalse(bridges.Contains(cell), "a cell cannot be both a crossing and a dot");
                }
            }
        }

        [Test]
        public void ChooseSharedGoalCells_RequiresTwoOpenSides()
        {
            bool[,] usable = FullBoard(5);
            // Wall (0,0) in to a single open side by blocking one of its two neighbours.
            usable[0, 1] = false;

            for (int seed = 0; seed < 40; seed++)
            {
                var chosen = ChooseSharedGoalCells(5, usable, null, 3, new System.Random(seed));

                foreach ((int Row, int Col) cell in chosen)
                {
                    int open = 0;
                    if (cell.Row > 0 && usable[cell.Row - 1, cell.Col]) { open++; }
                    if (cell.Row < 4 && usable[cell.Row + 1, cell.Col]) { open++; }
                    if (cell.Col > 0 && usable[cell.Row, cell.Col - 1]) { open++; }
                    if (cell.Col < 4 && usable[cell.Row, cell.Col + 1]) { open++; }

                    Assert.GreaterOrEqual(open, 2,
                        "each colour ending here needs its own edge to arrive through");
                }
            }
        }

        [Test]
        public void ChooseSharedGoalCells_NeverPicksTwoAdjacentCells()
        {
            for (int seed = 0; seed < 60; seed++)
            {
                var chosen = ChooseSharedGoalCells(7, FullBoard(7), null, 3, new System.Random(seed));

                foreach ((int Row, int Col) cell in chosen)
                {
                    Assert.IsFalse(chosen.Contains((cell.Row + 1, cell.Col)));
                    Assert.IsFalse(chosen.Contains((cell.Row, cell.Col + 1)));
                }
            }
        }

        [Test]
        public void BuildBlockGrid_CarriesTheSecondColoursDotIdentity()
        {
            // The bug this mechanic exposed: BoardGenerator reads secondPairId/thirdPairId/
            // fourthPairId at runtime, but the generator's own BuildBlockGrid did not. A shared
            // dot therefore looked like an ordinary one-colour dot during offline validation, so
            // the second colour appeared to have a single dot and every candidate was checked
            // against a board the game would never build.
            var data = new LevelData
            {
                gridSize = GridSize.GridSize_4X4,
                pairCount = 2,
                gridRows = new GridRow[4]
            };

            for (int r = 0; r < 4; r++)
            {
                data.gridRows[r] = new GridRow
                {
                    coloum = new PairColorType[4],
                    pairId = new int[4],
                    blockType = new BlockType[4],
                    wallMask = new int[4],
                    requiredEntryDirection = new Direction[4],
                    forcedExitDirection = new Direction[4],
                    secondPairId = new int[4]
                };
            }

            // (0,0) is a shared destination: a dot for Red and for Blue.
            data.gridRows[0].coloum[0] = PairColorType.Red;
            data.gridRows[0].secondPairId[0] = (int)PairColorType.Blue;

            MethodInfo build = typeof(LevelGenerator).GetMethod(
                "BuildBlockGrid", BindingFlags.NonPublic | BindingFlags.Static);
            object[] args = { data, 0, 0 };
            Block[,] grid = (Block[,])build.Invoke(null, args);

            try
            {
                Block shared = grid[0, 0];
                Assert.IsTrue(shared.IsPairBlock);
                Assert.AreEqual((int)PairColorType.Blue, shared.SecondPairId,
                    "the second colour's dot identity must survive into the validated grid");
                Assert.IsTrue(shared.IsSharedGoal, "two named pairs on a dot make it a shared goal");
                Assert.IsTrue(shared.IsDotFor((int)PairColorType.Red));
                Assert.IsTrue(shared.IsDotFor((int)PairColorType.Blue),
                    "the solver reaches the second colour's dot through IsDotFor");
            }
            finally
            {
                typeof(LevelGenerator)
                    .GetMethod("DestroyBlockGrid", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { grid });
            }
        }
    }
}
