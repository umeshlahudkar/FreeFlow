using FreeFlow.Enums;

[System.Serializable]
public struct LevelData
{
    public GridSize gridSize;
    public int blockSize;
    public int blockSpace;
    public int pairCount;

    public GridRow[] gridRows;

    // Per-pair path-length requirements. Optional: absent/empty means no pair on this
    // level has a length constraint.
    public PairConstraint[] pairConstraints;
}

[System.Serializable]
public struct GridRow
{
    public PairColorType[] coloum;

    // Identity of a pair, independent of its display color. Optional: when left empty
    // (all existing hand-authored levels), BoardGenerator falls back to deriving it from
    // the cell's PairColorType, so no migration of existing LevelData assets is needed.
    // Author this explicitly to have more than 9 simultaneous pairs on one board.
    public int[] pairId;

    // Obstacle/mechanic type per cell. Optional: when left empty, every cell defaults to
    // BlockType.Normal (matching all existing hand-authored levels).
    public BlockType[] blockType;

    // Per-cell bitmask of blocked edges (Left=1, Right=2, Up=4, Down=8), independent of
    // BlockType -- a wall is a property of an edge between two cells, not of a cell's
    // type. Optional: when left empty, no cell has any walls.
    public int[] wallMask;

    // Only meaningful on a cell with blockType == BlockType.OneWay: the only direction a
    // path may be moving in when it enters this cell. Optional: defaults to Direction.None
    // (no restriction) when left empty.
    public Direction[] requiredEntryDirection;
}

[System.Serializable]
public struct PairConstraint
{
    public int pairId;

    // Path must use exactly this many cells to count as complete. 0 = no constraint.
    public int requiredPathLength;
}


