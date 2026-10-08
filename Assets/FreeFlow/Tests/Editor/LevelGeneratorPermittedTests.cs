using System.Collections.Generic;
using System.Reflection;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers LevelGenerator's Permitted placement, via reflection (see
    /// LevelGeneratorBlockedCellTests' doc comment for why).
    ///
    /// Permitted and Forbidden read the same two id columns and are trivially easy to confuse, but
    /// their placement invariants are exact opposites: a Forbidden cell refuses the colour it
    /// NAMES, so it must name a colour that stays away; a Permitted cell refuses every colour it
    /// does NOT name, so it must name the colour whose path runs through it. Get it backwards and
    /// the intended solution is barred from a cell it needs -- every candidate becomes unsolvable
    /// and the generator produces nothing, with no clue as to why. That is what the first test
    /// pins, and it is the mirror of LevelGeneratorForbiddenTests' first test on purpose.
    /// </summary>
    public class LevelGeneratorPermittedTests
    {
        private static readonly MethodInfo PlacePermittedCellsMethod = typeof(LevelGenerator).GetMethod(
            "PlacePermittedCells", BindingFlags.NonPublic | BindingFlags.Static);

        private static List<(int Row, int Col, int AllowedPairId)> PlacePermittedCells(
            List<List<(int Row, int Col)>> paths, HashSet<(int Row, int Col)> excludedCells,
            List<PairColorType> palette, int count, System.Random rng)
        {
            return (List<(int, int, int)>)PlacePermittedCellsMethod.Invoke(
                null, new object[] { paths, excludedCells, palette, count, rng });
        }

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
        public void AlwaysPermitsTheColourThatOwnsTheCell()
        {
            List<List<(int Row, int Col)>> paths = TwoPaths();
            List<PairColorType> palette = TwoColours();

            for (int seed = 0; seed < 25; seed++)
            {
                var placed = PlacePermittedCells(paths, null, palette, 10, new System.Random(seed));

                foreach (var p in placed)
                {
                    int owner = -1;
                    for (int i = 0; i < paths.Count; i++)
                    {
                        if (paths[i].Contains((p.Row, p.Col))) { owner = i; }
                    }

                    Assert.AreNotEqual(-1, owner, "placed cell must belong to some path");
                    Assert.AreEqual((int)palette[owner], p.AllowedPairId,
                        "a Permitted cell must admit the colour whose own path runs through it -- "
                        + "naming any other colour bars the intended solution from a cell it needs");
                }
            }
        }

        [Test]
        public void AlwaysNamesSomeColour()
        {
            // Block's own doc: a permit cell naming nobody is BlockType.Blocked under another
            // name, which LevelValidator rejects outright.
            var placed = PlacePermittedCells(TwoPaths(), null, TwoColours(), 10, new System.Random(3));

            Assert.IsTrue(placed.Count > 0);
            foreach (var p in placed)
            {
                Assert.AreNotEqual(0, p.AllowedPairId, "a Permitted cell that names nobody blocks everyone");
            }
        }

        [Test]
        public void OnlyEverChoosesInteriorCells()
        {
            var endpoints = new HashSet<(int, int)> { (0, 0), (0, 3), (1, 0), (1, 3) };

            var placed = PlacePermittedCells(TwoPaths(), null, TwoColours(), 10, new System.Random(4));

            Assert.IsTrue(placed.Count > 0);
            foreach (var p in placed)
            {
                Assert.IsFalse(endpoints.Contains((p.Row, p.Col)),
                    "endpoints are pair dots -- a permission rule there would collide with the dot's own pairId");
            }
        }

        [Test]
        public void RespectsExcludedCells()
        {
            // Simulates cells another mechanic already claimed: only (1,1) and (1,2) remain.
            var excluded = new HashSet<(int, int)> { (0, 1), (0, 2) };

            var placed = PlacePermittedCells(TwoPaths(), excluded, TwoColours(), 10, new System.Random(5));

            Assert.AreEqual(2, placed.Count);
            foreach (var p in placed)
            {
                Assert.IsFalse(excluded.Contains((p.Row, p.Col)));
            }
        }

        [Test]
        public void WithZeroCount_ReturnsEmpty()
        {
            var placed = PlacePermittedCells(TwoPaths(), null, TwoColours(), 0, new System.Random(6));
            Assert.AreEqual(0, placed.Count);
        }
    }
}
