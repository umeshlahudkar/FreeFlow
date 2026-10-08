using System.Collections.Generic;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers LevelCanonicalizer.ComputeCanonicalKey against hand-derived transforms: for each
    /// case, the "rotated"/"flipped"/"relabeled" grid below was worked out by hand from the same
    /// coordinate and direction mappings LevelCanonicalizer itself uses, so a passing test means
    /// the two independently-derived results actually agree -- not just that the algorithm agrees
    /// with itself. Cases with walls/One-Way/Arrow specifically exercise direction remapping,
    /// which a dot-only board can't: a plain grid of dots looks the same under every transform
    /// regardless of whether the remapping code is even correct.
    /// </summary>
    public class LevelCanonicalizerTests
    {
        private readonly List<Block> created = new List<Block>();

        [TearDown]
        public void TearDown()
        {
            foreach (Block block in created) { BlockTestHarness.Destroy(block); }
            created.Clear();
        }

        private Block[,] CreateGrid(int rows, int cols)
        {
            Block[,] grid = new Block[rows, cols];
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    grid[i, j] = BlockTestHarness.CreateBlock(i, j);
                    created.Add(grid[i, j]);
                }
            }
            return grid;
        }

        [Test]
        public void IdenticalLevels_ProduceTheSameKey()
        {
            Block[,] a = CreateGrid(1, 2);
            BlockTestHarness.SetDot(a[0, 0], pairId: 1);
            BlockTestHarness.SetDot(a[0, 1], pairId: 1);

            Block[,] b = CreateGrid(1, 2);
            BlockTestHarness.SetDot(b[0, 0], pairId: 1);
            BlockTestHarness.SetDot(b[0, 1], pairId: 1);

            Assert.AreEqual(
                LevelCanonicalizer.ComputeCanonicalKey(a, 1, 2),
                LevelCanonicalizer.ComputeCanonicalKey(b, 1, 2));
        }

        [Test]
        public void Rotated180Copy_WithWallAndOneWay_ProducesTheSameKey()
        {
            // Original: dots at the two opposite corners of a 2x3 board, a One-Way cell that must
            // be entered moving Right, and a wall on the Right edge of (1,0).
            Block[,] original = CreateGrid(2, 3);
            BlockTestHarness.SetDot(original[0, 0], pairId: 1);
            BlockTestHarness.SetDot(original[1, 2], pairId: 1);
            BlockTestHarness.SetOneWay(original[0, 1], Direction.Right);
            BlockTestHarness.SetWall(original[1, 0], Direction.Right);

            // Rot180 maps (r,c) -> (1-r, 2-c) and swaps Left<->Right, Up<->Down: the dots swap
            // corners, the One-Way's required direction flips to Left at (1,1), and the wall
            // becomes a Left-edge wall at (0,2).
            Block[,] rotated = CreateGrid(2, 3);
            BlockTestHarness.SetDot(rotated[1, 2], pairId: 1);
            BlockTestHarness.SetDot(rotated[0, 0], pairId: 1);
            BlockTestHarness.SetOneWay(rotated[1, 1], Direction.Left);
            BlockTestHarness.SetWall(rotated[0, 2], Direction.Left);

            Assert.AreEqual(
                LevelCanonicalizer.ComputeCanonicalKey(original, 2, 3),
                LevelCanonicalizer.ComputeCanonicalKey(rotated, 2, 3));
        }

        [Test]
        public void HorizontallyFlippedCopy_WithArrow_ProducesTheSameKey()
        {
            Block[,] original = CreateGrid(1, 3);
            BlockTestHarness.SetDot(original[0, 0], pairId: 1);
            BlockTestHarness.SetDot(original[0, 2], pairId: 1);
            BlockTestHarness.SetArrow(original[0, 1], Direction.Right);

            // Mirrored left-right: the dots swap ends, and the forced exit direction flips.
            Block[,] flipped = CreateGrid(1, 3);
            BlockTestHarness.SetDot(flipped[0, 2], pairId: 1);
            BlockTestHarness.SetDot(flipped[0, 0], pairId: 1);
            BlockTestHarness.SetArrow(flipped[0, 1], Direction.Left);

            Assert.AreEqual(
                LevelCanonicalizer.ComputeCanonicalKey(original, 1, 3),
                LevelCanonicalizer.ComputeCanonicalKey(flipped, 1, 3));
        }

        [Test]
        public void Rotated90Copy_SwapsDimensionsButProducesTheSameKey()
        {
            Block[,] original = CreateGrid(2, 3);
            BlockTestHarness.SetDot(original[0, 0], pairId: 1);
            BlockTestHarness.SetDot(original[1, 2], pairId: 1);

            // Rot90 maps (r,c) -> (c, 1-r) on a 2-row board, landing in a 3x2 board.
            Block[,] rotated = CreateGrid(3, 2);
            BlockTestHarness.SetDot(rotated[0, 1], pairId: 1);
            BlockTestHarness.SetDot(rotated[2, 0], pairId: 1);

            Assert.AreEqual(
                LevelCanonicalizer.ComputeCanonicalKey(original, 2, 3),
                LevelCanonicalizer.ComputeCanonicalKey(rotated, 3, 2));
        }

        [Test]
        public void RelabeledPairIds_ProduceTheSameKey()
        {
            // Same structure, same positions, but the two pairs are authored with different raw
            // ids -- color/id is cosmetic, so canonical relabeling (first pair id seen in raster
            // order becomes 1, next becomes 2) should erase the difference entirely.
            Block[,] a = CreateGrid(1, 4);
            BlockTestHarness.SetDot(a[0, 0], pairId: 5);
            BlockTestHarness.SetDot(a[0, 1], pairId: 5);
            BlockTestHarness.SetDot(a[0, 2], pairId: 9);
            BlockTestHarness.SetDot(a[0, 3], pairId: 9);

            Block[,] b = CreateGrid(1, 4);
            BlockTestHarness.SetDot(b[0, 0], pairId: 7);
            BlockTestHarness.SetDot(b[0, 1], pairId: 7);
            BlockTestHarness.SetDot(b[0, 2], pairId: 3);
            BlockTestHarness.SetDot(b[0, 3], pairId: 3);

            Assert.AreEqual(
                LevelCanonicalizer.ComputeCanonicalKey(a, 1, 4),
                LevelCanonicalizer.ComputeCanonicalKey(b, 1, 4));
        }

        [Test]
        public void StructurallyDifferentLevels_ProduceDifferentKeys()
        {
            Block[,] plain = CreateGrid(1, 3);
            BlockTestHarness.SetDot(plain[0, 0], pairId: 1);
            BlockTestHarness.SetDot(plain[0, 2], pairId: 1);

            Block[,] walled = CreateGrid(1, 3);
            BlockTestHarness.SetDot(walled[0, 0], pairId: 1);
            BlockTestHarness.SetDot(walled[0, 2], pairId: 1);
            BlockTestHarness.SetWall(walled[0, 0], Direction.Right);

            Assert.AreNotEqual(
                LevelCanonicalizer.ComputeCanonicalKey(plain, 1, 3),
                LevelCanonicalizer.ComputeCanonicalKey(walled, 1, 3));
        }
    }
}
