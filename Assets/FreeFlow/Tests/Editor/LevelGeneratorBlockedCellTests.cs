using System.Reflection;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers LevelGenerator's Blocked-cell placement and connectivity check directly, via
    /// reflection (LevelGenerator is Editor tooling, not test code, so its private methods aren't
    /// otherwise reachable -- same reasoning as keeping BlockTestHarness and the generator's own
    /// headless Block[,] factory independent of each other). The rest of a generated candidate's
    /// correctness (solvability, coverage, mechanic necessity) is already covered by the full
    /// pipeline it runs through (LevelValidator/PuzzleSolver/RequiredMechanicValidator, all
    /// tested elsewhere); this file is specifically about the new graph logic those don't exercise.
    /// </summary>
    public class LevelGeneratorBlockedCellTests
    {
        private static readonly MethodInfo PlaceBlockedCellsMethod =
            typeof(LevelGenerator).GetMethod("PlaceBlockedCells", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo IsSingleConnectedRegionMethod =
            typeof(LevelGenerator).GetMethod("IsSingleConnectedRegion", BindingFlags.NonPublic | BindingFlags.Static);

        private static bool[,] PlaceBlockedCells(int size, int blockedCount, System.Random rng,
            bool interiorOnly = false)
        {
            return (bool[,])PlaceBlockedCellsMethod.Invoke(null,
                new object[] { size, blockedCount, interiorOnly, rng });
        }

        private static bool IsSingleConnectedRegion(bool[,] usable, int size)
        {
            return (bool)IsSingleConnectedRegionMethod.Invoke(null, new object[] { usable, size });
        }

        [Test]
        public void PlaceBlockedCells_WithZeroCount_MarksEveryCellUsable()
        {
            bool[,] usable = PlaceBlockedCells(5, 0, new System.Random(1));

            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < 5; c++)
                {
                    Assert.IsTrue(usable[r, c]);
                }
            }
        }

        /// <summary>
        /// Interior-only placement is a gameplay guarantee, not a tuning detail: a blocked cell on
        /// the outer ring is indistinguishable in play from the board simply being smaller there,
        /// so a level introducing the mechanic that way can be finished without the player ever
        /// noticing it exists. Every blocked cell on Levels 6-10 must be one the player has to
        /// route around.
        /// </summary>
        [Test]
        public void PlaceBlockedCells_InteriorOnly_NeverTouchesTheOuterRing()
        {
            System.Random rng = new System.Random(7);
            for (int trial = 0; trial < 20; trial++)
            {
                bool[,] usable = PlaceBlockedCells(5, 3, rng, interiorOnly: true);

                for (int r = 0; r < 5; r++)
                {
                    for (int c = 0; c < 5; c++)
                    {
                        bool onOuterRing = r == 0 || c == 0 || r == 4 || c == 4;
                        if (onOuterRing)
                        {
                            Assert.IsTrue(usable[r, c],
                                "blocked cell landed on the outer ring at (" + r + "," + c + ")");
                        }
                    }
                }
            }
        }

        [Test]
        public void PlaceBlockedCells_InteriorOnly_RejectsACountThatCannotFit()
        {
            // A 4x4 board has only a 2x2 interior, so 5 interior blocked cells is an authoring
            // error -- it must fail loudly rather than silently placing fewer than asked for.
            Assert.Throws<TargetInvocationException>(
                () => PlaceBlockedCells(4, 5, new System.Random(1), interiorOnly: true));
        }

        [Test]
        public void PlaceBlockedCells_AlwaysReturnsAConnectedRegionOfTheRightSize()
        {
            System.Random rng = new System.Random(42);
            for (int trial = 0; trial < 20; trial++)
            {
                bool[,] usable = PlaceBlockedCells(5, 3, rng);

                int usableCount = 0;
                for (int r = 0; r < 5; r++)
                {
                    for (int c = 0; c < 5; c++)
                    {
                        if (usable[r, c]) { usableCount++; }
                    }
                }

                Assert.AreEqual(22, usableCount, "trial " + trial);
                Assert.IsTrue(IsSingleConnectedRegion(usable, 5), "trial " + trial);
            }
        }

        [Test]
        public void IsSingleConnectedRegion_DetectsADisconnectedBoard()
        {
            // Walling off the middle column of a 3x3 board splits it into two isolated halves.
            bool[,] usable = new bool[3, 3];
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    usable[r, c] = c != 1;
                }
            }

            Assert.IsFalse(IsSingleConnectedRegion(usable, 3));
        }

        [Test]
        public void IsSingleConnectedRegion_AcceptsAFullyOpenBoard()
        {
            bool[,] usable = new bool[3, 3];
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    usable[r, c] = true;
                }
            }

            Assert.IsTrue(IsSingleConnectedRegion(usable, 3));
        }
    }
}
