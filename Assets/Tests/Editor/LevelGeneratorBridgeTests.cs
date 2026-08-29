using System.Collections.Generic;
using System.Reflection;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers Bridge construction, via reflection (see LevelGeneratorBlockedCellTests' doc comment
    /// for why).
    ///
    /// Bridge is the one mechanic that is not a decoration applied to a finished solution. Every
    /// other mechanic reads a rule off a partition that already exists; a Bridge needs a SECOND
    /// path through a cell the partition has already given away, so it is built into the search
    /// instead -- the cell enters as two independent lane nodes. These tests pin the properties
    /// that node splitting is supposed to buy, because each one is a level the validator or the
    /// solver would reject much later and much less legibly:
    ///   - both lanes actually used, by two DIFFERENT paths (otherwise it is not a crossing)
    ///   - each lane traversed STRAIGHT through (Block.CanExitFrom refuses a turn on a bridge)
    ///   - never an endpoint (LevelValidator rejects a bridge that is also a pair dot)
    ///   - the no-bridge case unchanged, since five shipped level ranges depend on it
    /// </summary>
    public class LevelGeneratorBridgeTests
    {
        private static readonly MethodInfo PartitionMethod = typeof(LevelGenerator).GetMethod(
            "TryGeneratePathPartition",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[]
            {
                typeof(int), typeof(bool[,]), typeof(int), typeof(int),
                typeof(HashSet<(int, int)>), typeof(System.Random)
            },
            null);

        private static readonly MethodInfo ChooseBridgeCellsMethod = typeof(LevelGenerator).GetMethod(
            "ChooseBridgeCells", BindingFlags.NonPublic | BindingFlags.Static);

        private static List<List<(int Row, int Col)>> Partition(int size, bool[,] usable, int usableCount,
            int pathCount, HashSet<(int Row, int Col)> bridges, System.Random rng)
        {
            return (List<List<(int, int)>>)PartitionMethod.Invoke(
                null, new object[] { size, usable, usableCount, pathCount, bridges, rng });
        }

        private static HashSet<(int Row, int Col)> ChooseBridgeCells(int size, bool[,] usable, int count,
            System.Random rng)
        {
            return (HashSet<(int, int)>)ChooseBridgeCellsMethod.Invoke(
                null, new object[] { size, usable, count, rng });
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

        /// <summary>
        /// Runs the partition until it succeeds, since a stranded cell is an expected, cheap
        /// failure the real caller retries through rather than a defect.
        /// </summary>
        private static List<List<(int Row, int Col)>> PartitionEventually(int size, bool[,] usable,
            int usableCount, int pathCount, HashSet<(int Row, int Col)> bridges, int seedFrom = 0,
            int tries = 400)
        {
            for (int seed = seedFrom; seed < seedFrom + tries; seed++)
            {
                var paths = Partition(size, usable, usableCount, pathCount, bridges, new System.Random(seed));
                if (paths != null) { return paths; }
            }
            return null;
        }

        [Test]
        public void BothLanesAreUsed_ByTwoDifferentPaths()
        {
            var bridges = new HashSet<(int, int)> { (3, 3) };

            int produced = 0;
            for (int seed = 0; seed < 300; seed++)
            {
                var paths = Partition(7, FullBoard(7), 49, 5, bridges, new System.Random(seed));
                if (paths == null) { continue; }
                produced++;

                int owners = 0;
                foreach (var path in paths)
                {
                    if (path.Contains((3, 3))) { owners++; }
                }

                Assert.AreEqual(2, owners,
                    "a bridge carries exactly two paths -- one per lane; anything else is not a crossing");
            }

            Assert.Greater(produced, 0, "expected at least one successful partition across 300 seeds");
        }

        [Test]
        public void EachLaneRunsStraightThroughTheBridge()
        {
            var bridges = new HashSet<(int, int)> { (3, 3) };
            var paths = PartitionEventually(7, FullBoard(7), 49, 5, bridges);
            Assert.IsNotNull(paths);

            foreach (var path in paths)
            {
                int at = path.IndexOf((3, 3));
                if (at < 0) { continue; }

                Assert.Greater(at, 0, "a bridge is never a path endpoint");
                Assert.Less(at, path.Count - 1, "a bridge is never a path endpoint");

                (int Row, int Col) before = path[at - 1];
                (int Row, int Col) after = path[at + 1];

                bool straight = (before.Row == after.Row && before.Row == 3)
                    || (before.Col == after.Col && before.Col == 3);
                Assert.IsTrue(straight,
                    "Block.CanExitFrom refuses a turn on a bridge -- a path that entered on one lane "
                    + "and left on the other would be changing lanes in mid-air. Got "
                    + before + " -> (3,3) -> " + after);
            }
        }

        [Test]
        public void ABridgeIsNeverAPathEndpoint()
        {
            // LevelValidator.ValidateBridgeCells errors on a bridge that is also a pair dot, and
            // path ends become dots, so this has to hold for every path of every partition.
            var bridges = new HashSet<(int, int)> { (2, 4) };

            for (int seed = 0; seed < 300; seed++)
            {
                var paths = Partition(7, FullBoard(7), 49, 5, bridges, new System.Random(seed));
                if (paths == null) { continue; }

                foreach (var path in paths)
                {
                    Assert.AreNotEqual((2, 4), path[0]);
                    Assert.AreNotEqual((2, 4), path[path.Count - 1]);
                }
            }
        }

        [Test]
        public void EveryCellIsStillCoveredExactlyOnce_ExceptTheBridge()
        {
            var bridges = new HashSet<(int, int)> { (3, 3) };
            var paths = PartitionEventually(7, FullBoard(7), 49, 5, bridges);
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
        public void WithNoBridges_BehavesExactlyAsBefore()
        {
            // Five shipped level ranges go through this path, so the bridge-free case has to be
            // untouched -- same seed, same board, cell-for-cell.
            for (int seed = 0; seed < 50; seed++)
            {
                var withNull = Partition(6, FullBoard(6), 36, 4, null, new System.Random(seed));
                var withEmpty = Partition(6, FullBoard(6), 36, 4,
                    new HashSet<(int, int)>(), new System.Random(seed));

                Assert.AreEqual(withNull == null, withEmpty == null);
                if (withNull == null) { continue; }

                Assert.AreEqual(withNull.Count, withEmpty.Count);
                for (int p = 0; p < withNull.Count; p++)
                {
                    CollectionAssert.AreEqual(withNull[p], withEmpty[p]);
                }
            }
        }

        [Test]
        public void ChooseBridgeCells_OnlyPicksCellsWithAllFourNeighboursUsable()
        {
            bool[,] usable = FullBoard(7);
            usable[3, 3] = false;      // holes next to otherwise-fine candidates
            usable[1, 5] = false;

            for (int seed = 0; seed < 40; seed++)
            {
                var chosen = ChooseBridgeCells(7, usable, 2, new System.Random(seed));

                foreach ((int Row, int Col) cell in chosen)
                {
                    Assert.IsTrue(cell.Row >= 1 && cell.Row <= 5 && cell.Col >= 1 && cell.Col <= 5,
                        "an edge cell is missing two of the four neighbours a crossing needs");
                    Assert.IsTrue(usable[cell.Row, cell.Col]);
                    Assert.IsTrue(usable[cell.Row - 1, cell.Col] && usable[cell.Row + 1, cell.Col]
                        && usable[cell.Row, cell.Col - 1] && usable[cell.Row, cell.Col + 1],
                        "a lane with a blocked neighbour is a dead end, and LevelValidator rejects it");
                }
            }
        }

        [Test]
        public void ChooseBridgeCells_NeverPicksTwoAdjacentCells()
        {
            for (int seed = 0; seed < 60; seed++)
            {
                var chosen = ChooseBridgeCells(7, FullBoard(7), 3, new System.Random(seed));

                foreach ((int Row, int Col) cell in chosen)
                {
                    Assert.IsFalse(chosen.Contains((cell.Row + 1, cell.Col)));
                    Assert.IsFalse(chosen.Contains((cell.Row, cell.Col + 1)));
                }
            }
        }

        [Test]
        public void TwoBridgesOnOneBoardBothCross()
        {
            var bridges = new HashSet<(int, int)> { (2, 2), (4, 4) };
            var paths = PartitionEventually(7, FullBoard(7), 49, 5, bridges);
            Assert.IsNotNull(paths);

            foreach ((int Row, int Col) bridge in bridges)
            {
                int owners = 0;
                foreach (var path in paths)
                {
                    if (path.Contains(bridge)) { owners++; }
                }
                Assert.AreEqual(2, owners, "bridge " + bridge + " must carry two paths");
            }
        }
    }
}
