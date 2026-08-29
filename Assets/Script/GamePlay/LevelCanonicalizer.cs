using System;
using System.Collections.Generic;
using System.Text;
using FreeFlow.Enums;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Produces a string key for a board that is identical for any board that is "the same
    /// puzzle" under rotation, reflection, or pair-colour relabeling -- the normalization spec §15
    /// asks duplicate detection to use. Two boards are duplicates exactly when this key matches.
    ///
    /// Approach: render the board under all 8 ways a rectangle can be rotated/reflected onto
    /// itself (the dihedral group of the square, generalized to non-square boards -- a 90-degree
    /// turn swaps a board's row and column counts, so it can only ever match another board with
    /// swapped dimensions, which is exactly correct), relabel pair identities canonically for each
    /// (first pair id encountered scanning the transformed board in raster order becomes 1, and so
    /// on -- color is never part of the key at all, since it's cosmetic), serialize each of the 8
    /// results, and keep whichever string sorts first. Two boards that are the same puzzle produce
    /// the same 8-way set of strings (just permuted, and possibly re-derived under a different one
    /// of the two boards' own 8 transforms), so their lexicographically-smallest member is
    /// identical too.
    /// </summary>
    public static class LevelCanonicalizer
    {
        private enum TransformKind
        {
            Identity, Rot90, Rot180, Rot270, FlipHorizontal, FlipVertical, Transpose, AntiTranspose
        }

        private static readonly TransformKind[] AllTransforms =
        {
            TransformKind.Identity, TransformKind.Rot90, TransformKind.Rot180, TransformKind.Rot270,
            TransformKind.FlipHorizontal, TransformKind.FlipVertical, TransformKind.Transpose,
            TransformKind.AntiTranspose
        };

        private struct CellRecord
        {
            public BlockType BlockType;
            public int WallMask;
            public int PairId;
            public int SecondPairId;
            public int ThirdPairId;
            public int FourthPairId;
            public Direction RequiredEntryDirection;
            public Direction ForcedExitDirection;
        }

        public static string ComputeCanonicalKey(Block[,] grid, int rowCount, int colCount)
        {
            string best = null;
            for (int i = 0; i < AllTransforms.Length; i++)
            {
                string candidate = Serialize(grid, rowCount, colCount, AllTransforms[i]);
                if (best == null || string.CompareOrdinal(candidate, best) < 0) { best = candidate; }
            }
            return best;
        }

        public static bool AreDuplicates(Block[,] gridA, int rowsA, int colsA, Block[,] gridB, int rowsB, int colsB)
        {
            return ComputeCanonicalKey(gridA, rowsA, colsA) == ComputeCanonicalKey(gridB, rowsB, colsB);
        }

        private static string Serialize(Block[,] grid, int rowCount, int colCount, TransformKind kind)
        {
            bool swaps = SwapsDimensions(kind);
            int newRows = swaps ? colCount : rowCount;
            int newCols = swaps ? rowCount : colCount;

            CellRecord[,] mapped = new CellRecord[newRows, newCols];
            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < colCount; c++)
                {
                    Block cell = grid[r, c];
                    (int nr, int nc) = MapCoord(kind, r, c, rowCount, colCount);

                    mapped[nr, nc] = new CellRecord
                    {
                        BlockType = cell.BlockType,
                        WallMask = RemapWallMask(cell, kind),
                        PairId = cell.PairId,
                        SecondPairId = cell.SecondPairId,
                        ThirdPairId = cell.ThirdPairId,
                        FourthPairId = cell.FourthPairId,
                        RequiredEntryDirection = MapDirection(kind, cell.RequiredEntryDirection),
                        ForcedExitDirection = MapDirection(kind, cell.ForcedExitDirection)
                    };
                }
            }

            // Canonical pair relabeling: first pair id encountered scanning the TRANSFORMED board
            // in raster order becomes 1, and so on. Doing this per-transform (rather than once on
            // the original board) is what makes a rotated copy produce the same string as the
            // original once both are canonically relabeled -- a fixed original-board relabeling
            // would carry the original scan order's bias into every transform instead.
            Dictionary<int, int> relabel = new Dictionary<int, int>();
            int nextId = 1;
            for (int r = 0; r < newRows; r++)
            {
                for (int c = 0; c < newCols; c++)
                {
                    CellRecord rec = mapped[r, c];
                    RegisterForRelabel(relabel, ref nextId, rec.PairId);
                    RegisterForRelabel(relabel, ref nextId, rec.SecondPairId);
                    RegisterForRelabel(relabel, ref nextId, rec.ThirdPairId);
                    RegisterForRelabel(relabel, ref nextId, rec.FourthPairId);
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(newRows).Append('x').Append(newCols).Append('|');
            for (int r = 0; r < newRows; r++)
            {
                for (int c = 0; c < newCols; c++)
                {
                    CellRecord rec = mapped[r, c];
                    sb.Append((int)rec.BlockType).Append(':')
                      .Append(rec.WallMask).Append(':')
                      .Append(Relabel(relabel, rec.PairId)).Append(':')
                      .Append(Relabel(relabel, rec.SecondPairId)).Append(':')
                      .Append(Relabel(relabel, rec.ThirdPairId)).Append(':')
                      .Append(Relabel(relabel, rec.FourthPairId)).Append(':')
                      .Append((int)rec.RequiredEntryDirection).Append(':')
                      .Append((int)rec.ForcedExitDirection).Append(';');
                }
            }
            return sb.ToString();
        }

        private static void RegisterForRelabel(Dictionary<int, int> relabel, ref int nextId, int originalId)
        {
            if (originalId == 0 || relabel.ContainsKey(originalId)) { return; }
            relabel[originalId] = nextId;
            nextId++;
        }

        private static int Relabel(Dictionary<int, int> relabel, int originalId)
        {
            if (originalId == 0) { return 0; }
            return relabel.TryGetValue(originalId, out int canonical) ? canonical : 0;
        }

        private static bool SwapsDimensions(TransformKind kind)
        {
            return kind == TransformKind.Rot90 || kind == TransformKind.Rot270
                || kind == TransformKind.Transpose || kind == TransformKind.AntiTranspose;
        }

        private static (int Row, int Col) MapCoord(TransformKind kind, int r, int c, int rows, int cols)
        {
            switch (kind)
            {
                case TransformKind.Identity: return (r, c);
                case TransformKind.Rot90: return (c, rows - 1 - r);
                case TransformKind.Rot180: return (rows - 1 - r, cols - 1 - c);
                case TransformKind.Rot270: return (cols - 1 - c, r);
                case TransformKind.FlipHorizontal: return (r, cols - 1 - c);
                case TransformKind.FlipVertical: return (rows - 1 - r, c);
                case TransformKind.Transpose: return (c, r);
                case TransformKind.AntiTranspose: return (cols - 1 - c, rows - 1 - r);
                default: return (r, c);
            }
        }

        private static Direction MapDirection(TransformKind kind, Direction dir)
        {
            if (dir == Direction.None) { return Direction.None; }

            switch (kind)
            {
                case TransformKind.Identity:
                    return dir;
                case TransformKind.Rot90:
                    switch (dir)
                    {
                        case Direction.Up: return Direction.Right;
                        case Direction.Right: return Direction.Down;
                        case Direction.Down: return Direction.Left;
                        default: return Direction.Up; // Direction.Left
                    }
                case TransformKind.Rot180:
                    switch (dir)
                    {
                        case Direction.Up: return Direction.Down;
                        case Direction.Down: return Direction.Up;
                        case Direction.Left: return Direction.Right;
                        default: return Direction.Left; // Direction.Right
                    }
                case TransformKind.Rot270:
                    switch (dir)
                    {
                        case Direction.Up: return Direction.Left;
                        case Direction.Left: return Direction.Down;
                        case Direction.Down: return Direction.Right;
                        default: return Direction.Up; // Direction.Right
                    }
                case TransformKind.FlipHorizontal:
                    switch (dir)
                    {
                        case Direction.Left: return Direction.Right;
                        case Direction.Right: return Direction.Left;
                        default: return dir;
                    }
                case TransformKind.FlipVertical:
                    switch (dir)
                    {
                        case Direction.Up: return Direction.Down;
                        case Direction.Down: return Direction.Up;
                        default: return dir;
                    }
                case TransformKind.Transpose:
                    switch (dir)
                    {
                        case Direction.Up: return Direction.Left;
                        case Direction.Left: return Direction.Up;
                        case Direction.Down: return Direction.Right;
                        default: return Direction.Down; // Direction.Right
                    }
                case TransformKind.AntiTranspose:
                    switch (dir)
                    {
                        case Direction.Up: return Direction.Right;
                        case Direction.Right: return Direction.Up;
                        case Direction.Down: return Direction.Left;
                        default: return Direction.Down; // Direction.Left
                    }
                default:
                    return dir;
            }
        }

        private static int RemapWallMask(Block cell, TransformKind kind)
        {
            int result = 0;
            if (cell.HasWall(Direction.Left)) { result |= WallBit(MapDirection(kind, Direction.Left)); }
            if (cell.HasWall(Direction.Right)) { result |= WallBit(MapDirection(kind, Direction.Right)); }
            if (cell.HasWall(Direction.Up)) { result |= WallBit(MapDirection(kind, Direction.Up)); }
            if (cell.HasWall(Direction.Down)) { result |= WallBit(MapDirection(kind, Direction.Down)); }
            return result;
        }

        private static int WallBit(Direction dir)
        {
            switch (dir)
            {
                case Direction.Left: return 1;
                case Direction.Right: return 2;
                case Direction.Up: return 4;
                case Direction.Down: return 8;
                default: return 0;
            }
        }
    }
}
