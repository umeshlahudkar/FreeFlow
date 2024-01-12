using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[System.Serializable]
public struct LevelData 
{
    public GridSize gridSize;
    public int blockSize;
    public int blockSpace;

    public GridRow[] gridRows;
}


