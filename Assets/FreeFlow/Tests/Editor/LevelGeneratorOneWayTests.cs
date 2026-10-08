using System.Collections.Generic;
using System.Reflection;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers LevelGenerator's One-Way placement logic directly, via reflection (see
    /// LevelGeneratorBlockedCellTests' own doc comment for why: LevelGenerator is Editor tooling,
    /// not test code, so its private methods aren't otherwise reachable). The rest of a generated
    /// candidate's correctness rides on the already-tested solve/validate/canonicalize/mechanic-
    /// necessity pipeline; this file is specifically about the new interior-cell selection and
    /// direction-of-travel logic those don't exercise.
    /// </summary>
    public class LevelGeneratorOneWayTests
    {
        private static readonly MethodInfo PlaceOneWayCellsMethod = typeof(LevelGenerator).GetMethod(
            "PlaceOneWayCells", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo DirectionOfTravelMethod = typeof(LevelGenerator).GetMethod(
            "DirectionOfTravel", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo IsRowMajorBeforeMethod = typeof(LevelGenerator).GetMethod(
            "IsRowMajorBefore", BindingFlags.NonPublic | BindingFlags.Static);

        private static List<(int Row, int Col, Direction EntryDir)> PlaceOneWayCells(
            List<List<(int Row, int Col)>> paths, HashSet<(int Row, int Col)> excludedCells,
            Dictionary<(int Row, int Col), bool> reversedByCell, int count, System.Random rng)
        {
            return (List<(int, int, Direction)>)PlaceOneWayCellsMethod.Invoke(
                null, new object[] { paths, excludedCells, reversedByCell, count, rng });
        }

        private static Direction DirectionOfTravel((int Row, int Col) from, (int Row, int Col) to)
        {
            return (Direction)DirectionOfTravelMethod.Invoke(null, new object[] { from, to });
        }

        private static bool IsRowMajorBefore((int Row, int Col) a, (int Row, int Col) b)
        {
            return (bool)IsRowMajorBeforeMethod.Invoke(null, new object[] { a, b });
        }

        [Test]
        public void DirectionOfTravel_ComputesEachDirectionCorrectly()
        {
            Assert.AreEqual(Direction.Right, DirectionOfTravel((2, 2), (2, 3)));
            Assert.AreEqual(Direction.Left, DirectionOfTravel((2, 2), (2, 1)));
            Assert.AreEqual(Direction.Down, DirectionOfTravel((2, 2), (3, 2)));
            Assert.AreEqual(Direction.Up, DirectionOfTravel((2, 2), (1, 2)));
        }

        [Test]
        public void IsRowMajorBefore_ComparesRowFirstThenColumn()
        {
            Assert.IsTrue(IsRowMajorBefore((0, 4), (3, 0)), "smaller row wins regardless of column");
            Assert.IsFalse(IsRowMajorBefore((3, 0), (0, 4)));
            Assert.IsTrue(IsRowMajorBefore((2, 1), (2, 3)), "same row -- smaller column wins");
            Assert.IsFalse(IsRowMajorBefore((2, 3), (2, 1)));
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
        public void PlaceOneWayCells_NeverChoosesADotCellOrTheSnakesFirstCell()
        {
            List<(int Row, int Col)> snake = StraightSnake();
            HashSet<(int Row, int Col)> dots = new HashSet<(int, int)> { (0, 0), (0, 4) };

            // Request more than could possibly be needed to exercise every remaining candidate.
            List<(int Row, int Col, Direction EntryDir)> placed =
                PlaceOneWayCells(AsPaths(snake), dots, NotReversed(snake), 10, new System.Random(1));

            Assert.IsTrue(placed.Count > 0);
            foreach (var p in placed)
            {
                Assert.IsFalse(dots.Contains((p.Row, p.Col)), "must never choose a dot cell");
                Assert.AreNotEqual((0, 0), (p.Row, p.Col), "must never choose the snake's first cell");
            }
        }

        [Test]
        public void PlaceOneWayCells_EntryDirectionMatchesActualTravelIntoTheCell_WhenNotReversed()
        {
            List<(int Row, int Col)> snake = StraightSnake();
            HashSet<(int Row, int Col)> dots = new HashSet<(int, int)> { (0, 0), (0, 4) };

            List<(int Row, int Col, Direction EntryDir)> placed =
                PlaceOneWayCells(AsPaths(snake), dots, NotReversed(snake), 3, new System.Random(7));

            foreach (var p in placed)
            {
                // Every interior cell on this straight snake is entered while moving Right, when
                // the solver walks the segment in the same order as the snake's own array.
                Assert.AreEqual(Direction.Right, p.EntryDir);
            }
        }

        [Test]
        public void PlaceOneWayCells_EntryDirectionIsReversed_WhenSegmentIsReversed()
        {
            // Simulates PuzzleSolver actually walking this segment backwards (its row-major-first
            // dot happens to be the snake's LAST cell, not its first) -- see TryBuildCandidate's
            // reversedByCell doc comment for why this can happen.
            List<(int Row, int Col)> snake = StraightSnake();
            HashSet<(int Row, int Col)> dots = new HashSet<(int, int)> { (0, 0), (0, 4) };

            List<(int Row, int Col, Direction EntryDir)> placed =
                PlaceOneWayCells(AsPaths(snake), dots, AllReversed(snake), 3, new System.Random(7));

            foreach (var p in placed)
            {
                // Walking backwards along this straight snake means entering while moving Left.
                Assert.AreEqual(Direction.Left, p.EntryDir);
            }
        }

        [Test]
        public void PlaceOneWayCells_ReturnsFewerThanRequestedWhenNotEnoughInteriorCellsExist()
        {
            // Only 3 interior (non-dot) cells exist on this 5-cell snake -- (0,1),(0,2),(0,3).
            List<(int Row, int Col)> snake = StraightSnake();
            HashSet<(int Row, int Col)> dots = new HashSet<(int, int)> { (0, 0), (0, 4) };

            List<(int Row, int Col, Direction EntryDir)> placed =
                PlaceOneWayCells(AsPaths(snake), dots, NotReversed(snake), 10, new System.Random(3));

            Assert.AreEqual(3, placed.Count);
        }

        [Test]
        public void PlaceOneWayCells_WithZeroCount_ReturnsEmpty()
        {
            List<(int Row, int Col)> snake = StraightSnake();
            HashSet<(int Row, int Col)> dots = new HashSet<(int, int)> { (0, 0), (0, 4) };

            List<(int Row, int Col, Direction EntryDir)> placed =
                PlaceOneWayCells(AsPaths(snake), dots, NotReversed(snake), 0, new System.Random(9));

            Assert.AreEqual(0, placed.Count);
        }
    }
}
