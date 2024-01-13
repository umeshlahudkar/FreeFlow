using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoardGenerator : MonoBehaviour
{
    [SerializeField] private Block blockPrefab;
    private List<Block> gridblocks;

    public void GenerateBoard(LevelData data)
    {
        gridblocks = new List<Block>();

        int rowSize = (int)data.gridSize;
        int coloumSize = (int)data.gridSize;

        int blockSize = data.blockSize;
        int blockSpace = data.blockSpace;

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

                currentPositionX += (blockSize + blockSpace);
                gridblocks.Add(block);
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

    public void ResetGrid()
    {
        foreach(Block b in gridblocks)
        {
            Destroy(b.gameObject);
        }
    }
}
