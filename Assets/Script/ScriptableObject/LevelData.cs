using FreeFlow.Enums;

[System.Serializable]
public struct LevelData
{
    public GridSize gridSize;
    public int blockSize;
    public int blockSpace;
    public int pairCount;

    public GridRow[] gridRows;
}

[System.Serializable]
public struct GridRow
{
    public PairColorType[] coloum;
}


