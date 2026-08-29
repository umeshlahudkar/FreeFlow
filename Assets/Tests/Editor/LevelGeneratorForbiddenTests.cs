using System.Collections.Generic;
using System.Reflection;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers LevelGenerator's Forbidden placement, via reflection (see
    /// LevelGeneratorBlockedCellTests' doc comment for why). The invariant that matters is the
    /// first test: a Forbidden cell must never name the colour whose own path runs through it.
    /// Naming its own colour would forbid the intended solution, so every candidate would be
    /// unsolvable and the generator would spin forever producing nothing -- a failure that shows
    /// up only as "0 levels saved" after a long run, with nothing pointing at the cause.
    /// </summary>
    public class LevelGeneratorForbiddenTests
    {
        private static readonly MethodInfo PlaceForbiddenCellsMethod = typeof(LevelGenerator).GetMethod(
            "PlaceForbiddenCells", BindingFlags.NonPublic | BindingFlags.Static);

        private static List<(int Row, int Col, int ForbiddenPairId)> PlaceForbiddenCells(
            List<List<(int Row, int Col)>> paths, HashSet<(int Row, int Col)> excludedCells,
            List<PairColorType> palette, int count, System.Random rng)
        {
            return (List<(int, int, int)>)PlaceForbiddenCellsMethod.Invoke(
                null, new object[] { paths, excludedCells, palette, count, rng });
        }

        /// <summary>Two 4-cell paths, so both have genuine interior cells.</summary>
        private static List<List<(int Row, int Col)>> TwoPaths()
        {
            return new List<List<(int, int)>>
            {
                new List<(int, int)> { (0, 0), (0, 1), (0, 2), (0, 3) },
                new List<(int, int)> { (1, 0), (1, 1), (1, 2), (1, 3) }
            };
        }

        private static List<PairColorType> TwoColours()
        {
            return new List<PairColorType> { PairColorType.Red, PairColorType.Blue };
        }

        [Test]
        public void NeverForbidsTheColourThatOwnsTheCell()
        {
            List<List<(int Row, int Col)>> paths = TwoPaths();
            List<PairColorType> palette = TwoColours();

            // Ask for far more than exist so every interior cell gets exercised, over several
            // seeds so a lucky draw cannot hide a wrong choice.
            for (int seed = 0; seed < 25; seed++)
            {
                var placed = PlaceForbiddenCells(paths, null, palette, 10, new System.Random(seed));

                foreach (var f in placed)
                {
                    int owner = -1;
                    for (int p = 0; p < paths.Count; p++)
                    {
                        if (paths[p].Contains((f.Row, f.Col))) { owner = p; }
                    }

                    Assert.AreNotEqual(-1, owner, "placed cell must belong to some path");
                    Assert.AreNotEqual((int)palette[owner], f.ForbiddenPairId,
                        "a Forbidden cell must not bar the colour whose own path runs through it");
                }
            }
        }

        [Test]
        public void OnlyEverChoosesInteriorCells()
        {
            List<List<(int Row, int Col)>> paths = TwoPaths();
            var endpoints = new HashSet<(int, int)> { (0, 0), (0, 3), (1, 0), (1, 3) };

            var placed = PlaceForbiddenCells(paths, null, TwoColours(), 10, new System.Random(4));

            Assert.IsTrue(placed.Count > 0);
            foreach (var f in placed)
            {
                Assert.IsFalse(endpoints.Contains((f.Row, f.Col)),
                    "endpoints are pair dots -- a permission rule there would collide with the dot's own pairId");
            }
        }

        [Test]
        public void RespectsExcludedCells()
        {
            List<List<(int Row, int Col)>> paths = TwoPaths();
            // Simulates cells One-Way or Arrow already claimed: only (1,1) and (1,2) remain.
            var excluded = new HashSet<(int, int)> { (0, 1), (0, 2) };

            var placed = PlaceForbiddenCells(paths, excluded, TwoColours(), 10, new System.Random(5));

            Assert.AreEqual(2, placed.Count);
            foreach (var f in placed)
            {
                Assert.IsFalse(excluded.Contains((f.Row, f.Col)));
            }
        }

        [Test]
        public void WithZeroCount_ReturnsEmpty()
        {
            var placed = PlaceForbiddenCells(TwoPaths(), null, TwoColours(), 0, new System.Random(6));
            Assert.AreEqual(0, placed.Count);
        }

        [Test]
        public void WithASingleColour_PlacesNothing()
        {
            // Nothing to forbid: barring the only colour would bar the solution itself.
            var onePath = new List<List<(int, int)>>
            {
                new List<(int, int)> { (0, 0), (0, 1), (0, 2), (0, 3) }
            };
            var placed = PlaceForbiddenCells(onePath, null,
                new List<PairColorType> { PairColorType.Red }, 3, new System.Random(7));

            Assert.AreEqual(0, placed.Count);
        }
    }
}
