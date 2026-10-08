using System.Collections.Generic;
using System.Reflection;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers LevelGenerator's Arrow placement logic directly, via reflection (see
    /// LevelGeneratorBlockedCellTests' own doc comment for why: LevelGenerator is Editor tooling,
    /// not test code, so its private methods aren't otherwise reachable). Mirrors
    /// LevelGeneratorOneWayTests exactly, since PlaceArrowCells mirrors PlaceOneWayCells -- exit
    /// direction instead of entry direction, and the same reversedByCell handling (see
    /// TryBuildCandidate's own doc comment on why a segment can be walked in reverse of its own
    /// array order).
    /// </summary>
    public class LevelGeneratorArrowTests
    {
        private static readonly MethodInfo PlaceArrowCellsMethod = typeof(LevelGenerator).GetMethod(
            "PlaceArrowCells", BindingFlags.NonPublic | BindingFlags.Static);

        private static List<(int Row, int Col, Direction ExitDir)> PlaceArrowCells(
            List<List<(int Row, int Col)>> paths, HashSet<(int Row, int Col)> excludedCells,
            Dictionary<(int Row, int Col), bool> reversedByCell, int count, System.Random rng)
        {
            return (List<(int, int, Direction)>)PlaceArrowCellsMethod.Invoke(
                null, new object[] { paths, excludedCells, reversedByCell, count, rng });
        }

        private static List<(int Row, int Col)> StraightSnake()
        {
            // A 1x5 straight snake: (0,0)-(0,1)-(0,2)-(0,3)-(0,4).
            return new List<(int, int)> { (0, 0), (0, 1), (0, 2), (0, 3), (0, 4) };
        }

        /// <summary>Wraps a single colour's path as the list-of-paths the production code now
        /// takes. These tests deliberately use one path: the per-path indexing is what is under
        /// test, and one path exercises it without obscuring it.</summary>
        private static List<List<(int Row, int Col)>> AsPaths(List<(int Row, int Col)> single)
        {
            return new List<List<(int, int)>> { single };
        }

        private static Dictionary<(int Row, int Col), bool> NotReversed(List<(int Row, int Col)> snake)
        {
            var map = new Dictionary<(int, int), bool>();
            foreach (var cell in snake) { map[cell] = false; }
            return map;
        }

        private static Dictionary<(int Row, int Col), bool> AllReversed(List<(int Row, int Col)> snake)
        {
            var map = new Dictionary<(int, int), bool>();
            foreach (var cell in snake) { map[cell] = true; }
            return map;
        }

        [Test]
        public void PlaceArrowCells_NeverChoosesADotCellOrTheSnakesLastCell()
        {
            List<(int Row, int Col)> snake = StraightSnake();
            HashSet<(int Row, int Col)> dots = new HashSet<(int, int)> { (0, 0), (0, 4) };

            // Request more than could possibly be needed to exercise every remaining candidate.
            List<(int Row, int Col, Direction ExitDir)> placed =
                PlaceArrowCells(AsPaths(snake), dots, NotReversed(snake), 10, new System.Random(1));

            Assert.IsTrue(placed.Count > 0);
            foreach (var p in placed)
            {
                Assert.IsFalse(dots.Contains((p.Row, p.Col)), "must never choose a dot cell");
                Assert.AreNotEqual((0, 4), (p.Row, p.Col), "must never choose the snake's last cell");
            }
        }

        [Test]
        public void PlaceArrowCells_ExitDirectionMatchesActualTravelOutOfTheCell_WhenNotReversed()
        {
            List<(int Row, int Col)> snake = StraightSnake();
            HashSet<(int Row, int Col)> dots = new HashSet<(int, int)> { (0, 0), (0, 4) };

            List<(int Row, int Col, Direction ExitDir)> placed =
                PlaceArrowCells(AsPaths(snake), dots, NotReversed(snake), 3, new System.Random(7));

            foreach (var p in placed)
            {
                // Every interior cell on this straight snake exits by moving Right, when the
                // solver walks the segment in the same order as the snake's own array.
                Assert.AreEqual(Direction.Right, p.ExitDir);
            }
        }

        [Test]
        public void PlaceArrowCells_ExitDirectionIsReversed_WhenSegmentIsReversed()
        {
            // Simulates PuzzleSolver actually walking this segment backwards (its row-major-first
            // dot happens to be the snake's LAST cell, not its first) -- see TryBuildCandidate's
            // reversedByCell doc comment for why this can happen.
            List<(int Row, int Col)> snake = StraightSnake();
            HashSet<(int Row, int Col)> dots = new HashSet<(int, int)> { (0, 0), (0, 4) };

            List<(int Row, int Col, Direction ExitDir)> placed =
                PlaceArrowCells(AsPaths(snake), dots, AllReversed(snake), 3, new System.Random(7));

            foreach (var p in placed)
            {
                // Walking backwards along this straight snake means exiting by moving Left.
                Assert.AreEqual(Direction.Left, p.ExitDir);
            }
        }

        [Test]
        public void PlaceArrowCells_NeverChoosesACellAlreadyExcluded()
        {
            // Simulates a cell One-Way already claimed: (0,2) is excluded even though it's
            // otherwise a valid interior candidate.
            List<(int Row, int Col)> snake = StraightSnake();
            HashSet<(int Row, int Col)> excluded = new HashSet<(int, int)> { (0, 0), (0, 4), (0, 2) };

            List<(int Row, int Col, Direction ExitDir)> placed =
                PlaceArrowCells(AsPaths(snake), excluded, NotReversed(snake), 10, new System.Random(2));

            Assert.AreEqual(2, placed.Count); // only (0,1) and (0,3) remain
            foreach (var p in placed)
            {
                Assert.AreNotEqual((0, 2), (p.Row, p.Col));
            }
        }

        [Test]
        public void PlaceArrowCells_ReturnsFewerThanRequestedWhenNotEnoughInteriorCellsExist()
        {
            // Only 3 interior (non-dot) cells exist on this 5-cell snake -- (0,1),(0,2),(0,3).
            List<(int Row, int Col)> snake = StraightSnake();
            HashSet<(int Row, int Col)> dots = new HashSet<(int, int)> { (0, 0), (0, 4) };

            List<(int Row, int Col, Direction ExitDir)> placed =
                PlaceArrowCells(AsPaths(snake), dots, NotReversed(snake), 10, new System.Random(3));

            Assert.AreEqual(3, placed.Count);
        }

        [Test]
        public void PlaceArrowCells_WithZeroCount_ReturnsEmpty()
        {
            List<(int Row, int Col)> snake = StraightSnake();
            HashSet<(int Row, int Col)> dots = new HashSet<(int, int)> { (0, 0), (0, 4) };

            List<(int Row, int Col, Direction ExitDir)> placed =
                PlaceArrowCells(AsPaths(snake), dots, NotReversed(snake), 0, new System.Random(9));

            Assert.AreEqual(0, placed.Count);
        }
    }
}
