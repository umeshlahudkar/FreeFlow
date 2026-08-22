using FreeFlow.Enums;

[System.Serializable]
public struct LevelData
{
    public GridSize gridSize;
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

    // Only meaningful on a cell with blockType == BlockType.Arrow: the direction a path is
    // forced to leave in, however it entered. Deliberately its own column rather than reusing
    // requiredEntryDirection -- a cell constrained on entry AND exit is a legitimate thing to
    // author later, and one column meaning two things is exactly how requiredEntryDirection
    // became enforceable on cells that never draw it. Optional: defaults to Direction.None.
    public Direction[] forcedExitDirection;

    // Only meaningful on a cell with blockType == BlockType.Rotator: which of the four elbow
    // orientations it starts in (0 = Up+Right, then clockwise). This is the STARTING rotation
    // only -- the current one is runtime state and is never written back here. Optional:
    // defaults to 0.
    public int[] initialRotation;

    // A SECOND pair this cell is a dot for, making it the shared destination of two colours: each
    // pair runs its own source to this one cell. Optional: 0 means the cell has at most the one
    // identity in `coloum`/`pairId`.
    //
    // One extra identity, not a list, because two colours sharing a goal is the shape that reads
    // on a board -- three would need a real set here and a dot that can show three colours.
    public int[] secondPairId;
}

[System.Serializable]
public struct PairConstraint
{
    public int pairId;

    // Path must use exactly this many cells to count as complete. 0 = no constraint.
    public int requiredPathLength;
}


