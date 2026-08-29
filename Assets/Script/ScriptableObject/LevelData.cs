using FreeFlow.Enums;

[System.Serializable]
public struct LevelData
{
    public GridSize gridSize;
    public int pairCount;

    public GridRow[] gridRows;

    // DifficultyAnalyzer's 0-100 score for this level's solution, recorded at generation time.
    // Optional: 0 on any level nothing has scored (all hand-authored levels, and anything
    // generated before DifficultyAnalyzer existed) -- 0 is indistinguishable from "genuinely
    // trivial" and "never scored", which is fine for now since nothing reads this yet except the
    // generator that writes it; a real consumer (level-select UI, daily-challenge selection) will
    // need to tell those apart later.
    public float difficultyScore;
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

    // Further pairs this cell is a dot for, making it the shared destination of up to FOUR
    // colours: each pair runs its own source to this one cell. Optional, and filled in order --
    // 0 means the cell has no identity beyond the one in `coloum`/`pairId`, and a level should
    // never skip a slot.
    //
    // Four is the ceiling for a real reason rather than a chosen one: a path that ENDS in a cell
    // claims the single edge it arrived through, and a cell has four edges. A fifth colour could
    // not reach the cell without reusing an edge another path already owns. Block.MaxOccupants is
    // the same four.
    //
    // Named columns rather than one jagged array per cell, to match how every other per-cell
    // datum in this file is stored -- Unity serialises parallel arrays cleanly and a jagged one
    // awkwardly. secondPairId is also read by the two permission rules (ForbiddenForPair,
    // AllowedForPairs) for a completely different purpose; see Block.SecondIdNamesAPair. Those
    // rules do NOT read the third or fourth, which are dot identities only.
    public int[] secondPairId;
    public int[] thirdPairId;
    public int[] fourthPairId;
}


