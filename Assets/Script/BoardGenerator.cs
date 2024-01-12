using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoardGenerator : MonoBehaviour
{
    [SerializeField] private RectTransform gameplayTrans;
    public Block blockPrefab;

    public LevelDataSO levelDataSO;

    public int currentLevel;

    private void Start()
    {
        GenerateGrid();
    }

    private void GenerateGrid()
    {
        LevelData data = levelDataSO.levels[currentLevel];


        int rowSize = (int)data.gridSize;
        int coloumSize = (int)data.gridSize;

        int blockSize = data.blockSize;
        int blockSpace = data.blockSpace;

        GamePlay.instance.SetGridSize(rowSize, coloumSize);
        gameplayTrans.sizeDelta = new Vector2(GetTotalWidth(blockSize, coloumSize, blockSpace), GetTotalHeight(blockSize, rowSize, blockSpace));

        float startPointX = GetStartPointX(blockSize, coloumSize, blockSpace);
        float startPointY = GetStartPointY(blockSize, rowSize, blockSpace);

        float currentPositionX = startPointX;
        float currentPositionY = startPointY;

        for (int i = 0; i < rowSize; i++)
        {
            for(int j = 0; j < coloumSize; j++)
            {
                Block block = Instantiate(blockPrefab, transform);
                block.gameObject.name = "Block_" + i + " " + j;

                block.transform.localPosition = new Vector3(currentPositionX, currentPositionY, 0);
                
                block.GetComponent<RectTransform>().sizeDelta = Vector3.one * blockSize;

                if(data.gridRows[i].coloum[j] != DotType.None)
                {
                    block.SetBlock(data.gridRows[i].coloum[j], i, j);
                }

                block.SetBlock(data.gridRows[i].coloum[j], i, j);

                GamePlay.instance.SetBlockAtGridIndex(i, j, block);
                currentPositionX += (blockSize + blockSpace);
            }
            currentPositionX = startPointX;
            currentPositionY -= (blockSize + blockSpace);
        }
    }

    private float GetStartPointX(float blockSize, int rowSize, int blockSpace)
    {
        //float totalWidth = (blockSize * rowSize) + ((rowSize - 1) * blockSpace);
        float totalWidth = GetTotalWidth(blockSize, rowSize, blockSpace);
        return -((totalWidth / 2) - (blockSize / 2));
    }


    private float GetStartPointY(float blockSize, int columnSize, int blockSpace)
    {
        //float totalHeight = (blockSize * columnSize) + ((columnSize - 1) * blockSpace);
        float totalHeight = GetTotalHeight(blockSize, columnSize, blockSpace);
        return ((totalHeight / 2) - (blockSize / 2));
    }

    private float GetTotalWidth(float blockSize, int rowSize, int blockSpace)
    {
        return (blockSize * rowSize) + ((rowSize - 1) * blockSpace);
    }

    private float GetTotalHeight(float blockSize, int columnSize, int blockSpace)
    {
        return (blockSize * columnSize) + ((columnSize - 1) * blockSpace);
    }
}
