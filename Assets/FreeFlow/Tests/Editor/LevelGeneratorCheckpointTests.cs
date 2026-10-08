using System.Collections.Generic;
using System.Reflection;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers LevelGenerator's Checkpoint placement, via reflection (see
    /// LevelGeneratorBlockedCellTests' doc comment for why).
    ///
    /// Checkpoint shares its placement body with Permitted -- both answer "which colour owns this
    /// cell" -- but the two rules do opposite things with the answer, and the reason the owner is
    /// the right colour differs in kind:
    ///   - Permitted names the owner because naming anyone else would BAR the intended solution.
    ///   - Checkpoint names the owner because the cell belongs to exactly one path, so requiring
    ///     any other colour to pass through it demands a second visit that full coverage forbids.
    ///     Such a level is not merely worse, it is unsolvable.
    /// Sharing the body means one bug would silently break both mechanics at once, so this suite
    /// pins Checkpoint's own invariants rather than trusting the Permitted tests to cover them.
    /// </summary>
    public class LevelGeneratorCheckpointTests
    {
        private static readonly MethodInfo PlaceCheckpointCellsMethod = typeof(LevelGenerator).GetMethod(
            "PlaceCheckpointCells", BindingFlags.NonPublic | BindingFlags.Static);

        private static List<(int Row, int Col, int CheckpointPairId)> PlaceCheckpointCells(
            List<List<(int Row, int Col)>> paths, HashSet<(int Row, int Col)> excludedCells,
            List<PairColorType> palette, int count, System.Random rng)
        {
            return (List<(int, int, int)>)PlaceCheckpointCellsMethod.Invoke(
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
        public void AlwaysRequiresTheColourThatOwnsTheCell()
        {
            List<List<(int Row, int Col)>> paths = TwoPaths();
            List<PairColorType> palette = TwoColours();

            for (int seed = 0; seed < 25; seed++)
            {
                var placed = PlaceCheckpointCells(paths, null, palette, 10, new System.Random(seed));

                foreach (var p in placed)
                {
                    int owner = -1;
                    for (int i = 0; i < paths.Count; i++)
                    {
                        if (paths[i].Contains((p.Row, p.Col))) { owner = i; }
                    }

                    Assert.AreNotEqual(-1, owner, "placed cell must belong to some path");
                    Assert.AreEqual((int)palette[owner], p.CheckpointPairId,
                        "a Checkpoint must name the colour whose path already runs through it -- "
                        + "any other colour would have to visit a cell that is not its own, which "
                        + "full coverage forbids, making the level unsolvable rather than merely worse");
                }
            }
        }

        [Test]
        public void AlwaysNamesSomeColour()
        {
            // LevelValidator.ValidateRuleCells: a rule cell whose pairId is missing or unknown
            // makes the rule a silent no-op.
            var placed = PlaceCheckpointCells(TwoPaths(), null, TwoColours(), 10, new System.Random(3));

            Assert.IsTrue(placed.Count > 0);
            foreach (var p in placed)
            {
                Assert.AreNotEqual(0, p.CheckpointPairId, "a Checkpoint naming nobody constrains nobody");
            }
        }

        [Test]
        public void OnlyEverChoosesInteriorCells()
        {
            var endpoints = new HashSet<(int, int)> { (0, 0), (0, 3), (1, 0), (1, 3) };

            var placed = PlaceCheckpointCells(TwoPaths(), null, TwoColours(), 10, new System.Random(4));

            Assert.IsTrue(placed.Count > 0);
            foreach (var p in placed)
            {
                Assert.IsFalse(endpoints.Contains((p.Row, p.Col)),
                    "a dot cell already carries its own pairId, which a rule cell would collide with -- "
                    + "and a checkpoint on a pair's own dot is satisfied before the player moves");
            }
        }

        [Test]
        public void RespectsExcludedCells()
        {
            // Simulates cells another mechanic already claimed: only (1,1) and (1,2) remain.
            var excluded = new HashSet<(int, int)> { (0, 1), (0, 2) };

            var placed = PlaceCheckpointCells(TwoPaths(), excluded, TwoColours(), 10, new System.Random(5));

            Assert.AreEqual(2, placed.Count);
            foreach (var p in placed)
            {
                Assert.IsFalse(excluded.Contains((p.Row, p.Col)));
            }
        }

        [Test]
        public void WithZeroCount_ReturnsEmpty()
        {
            var placed = PlaceCheckpointCells(TwoPaths(), null, TwoColours(), 0, new System.Random(6));
            Assert.AreEqual(0, placed.Count);
        }

        [Test]
        public void PlacedCheckpointIsSatisfiedByTheIntendedSolution()
        {
            // The property that matters end to end: the path the generator built must already
            // satisfy every checkpoint it placed. If this fails, every candidate is rejected as
            // unsolvable and the range produces nothing, with no indication why.
            var paths = TwoPaths();
            var palette = TwoColours();

            var placed = PlaceCheckpointCells(paths, null, palette, 4, new System.Random(11));
            Assert.IsTrue(placed.Count > 0);

            foreach (var p in placed)
            {
                List<(int Row, int Col)> named = null;
                for (int i = 0; i < paths.Count; i++)
                {
                    if ((int)palette[i] == p.CheckpointPairId) { named = paths[i]; }
                }

                Assert.IsNotNull(named, "the named pair must exist on this board");
                Assert.IsTrue(named.Contains((p.Row, p.Col)),
                    "the named pair's own path must already pass through the checkpoint");
            }
        }
    }
}
