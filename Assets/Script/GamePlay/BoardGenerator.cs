using FreeFlow.Util;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Generates and manages the game board by creating, positioning, and resetting blocks based on provided LevelData.
    /// </summary>
    public class BoardGenerator : MonoBehaviour
    {
        [SerializeField] private Block blockPrefab;
        [SerializeField] private RectTransform thisTransform;
        private List<Block> gridblocks;

        private ObjectPool<Block> objectPool;

        /// <summary>
        /// Initializes the object pool for blocks with the specified prefab, capacity, and parent transform.
        /// </summary>
        private void InitializePool()
        {
            objectPool = new ObjectPool<Block>(blockPrefab, 16, thisTransform);
        }


        /// <summary>
        /// Generate blocks based on level data
        /// </summary>
        /// <param name="data">The LevelData containing grid size, block size, block space, and grid rows</param>
        public void GenerateBoard(LevelData data)
        {
            if (objectPool == null) { InitializePool(); }

            gridblocks = new List<Block>();

            int rowSize = (int)data.gridSize;
            int coloumSize = (int)data.gridSize;

            float totalScreenWidth = thisTransform.rect.width;
            float totalScreenHeight = thisTransform.rect.height;

            float useableWidth = totalScreenWidth * 0.9f;

            int maxBlockInRowCol = rowSize > coloumSize ? rowSize : coloumSize;
            float blockSize = useableWidth / maxBlockInRowCol;

            float horizontalSpacing = (totalScreenWidth - (blockSize * coloumSize)) / 2;
            float verticalSpacing = (totalScreenHeight - (blockSize * rowSize)) / 2;

            float startPointX = -((totalScreenWidth / 2) - (blockSize / 2) - (horizontalSpacing));
            float startPointY = ((totalScreenHeight / 2) - (blockSize / 2) - (verticalSpacing));

            int blockSpace = 0;

            float currentPositionX = startPointX;
            float currentPositionY = startPointY;

            for (int i = 0; i < rowSize; i++)
            {
                for (int j = 0; j < coloumSize; j++)
                {
                    //Block block = Instantiate(blockPrefab, transform);
                    Block block = objectPool.GetObject();
                    block.gameObject.name = "Block_" + i + " " + j;
                    block.transform.localPosition = new Vector3(currentPositionX, currentPositionY, 0);
                    block.GetComponent<RectTransform>().sizeDelta = Vector3.one * blockSize;

                    block.SetBlock(data.gridRows[i].coloum[j], i, j);

                    currentPositionX += (blockSize + blockSpace);
                    gridblocks.Add(block);
                }
                currentPositionX = startPointX;
                currentPositionY -= (blockSize + blockSpace);
            }

            GamePlayController.Instance.GameState = Enums.GameState.Playing;
        }

        /// <summary>
        /// Calculates the starting X-coordinate for the grid based on block size, row size, and block space.
        /// </summary>
        /// <param name="blockSize">Size of each block.</param>
        /// <param name="rowSize">Number of rows in the grid.</param>
        /// <param name="blockSpace">Space between blocks.</param>
        /// <returns>The calculated starting X-coordinate.</returns>
        private float GetStartPointX(float blockSize, int rowSize, int blockSpace)
        {
            //float totalWidth = (blockSize * rowSize) + ((rowSize - 1) * blockSpace);
            float totalWidth = GetTotalWidth(blockSize, rowSize, blockSpace);
            return -((totalWidth / 2) - (blockSize / 2));
        }


        /// <summary>
        /// Calculates the starting Y-coordinate for the grid based on block size, column size, and block space.
        /// </summary>
        /// <param name="blockSize">Size of each block.</param>
        /// <param name="columnSize">Number of columns in the grid.</param>
        /// <param name="blockSpace">Space between blocks.</param>
        /// <returns>The calculated starting Y-coordinate.</returns>
        private float GetStartPointY(float blockSize, int columnSize, int blockSpace)
        {
            //float totalHeight = (blockSize * columnSize) + ((columnSize - 1) * blockSpace);
            float totalHeight = GetTotalHeight(blockSize, columnSize, blockSpace);
            return ((totalHeight / 2) - (blockSize / 2));
        }


        /// <summary>
        /// Calculates the total width of the grid based on block size, row size, and block space.
        /// </summary>
        /// <param name="blockSize">Size of each block.</param>
        /// <param name="rowSize">Number of rows in the grid.</param>
        /// <param name="blockSpace">Space between blocks.</param>
        /// <returns>The calculated total width of the grid.</returns>
        private float GetTotalWidth(float blockSize, int rowSize, int blockSpace)
        {
            return (blockSize * rowSize) + ((rowSize - 1) * blockSpace);
        }

        /// <summary>
        /// Calculates the total height of the grid based on block size, column size, and block space.
        /// </summary>
        /// <param name="blockSize">Size of each block.</param>
        /// <param name="columnSize">Number of columns in the grid.</param>
        /// <param name="blockSpace">Space between blocks.</param>
        /// <returns>The calculated total height of the grid.</returns>
        private float GetTotalHeight(float blockSize, int columnSize, int blockSpace)
        {
            return (blockSize * columnSize) + ((columnSize - 1) * blockSpace);
        }


        /// <summary>
        /// Resets the grid by deactivating and returning blocks to the object pool.
        /// </summary>
        public void ResetBoard()
        {
            if (gridblocks != null && gridblocks.Count > 0)
            {
                foreach (Block b in gridblocks)
                {
                    //Destroy(b.gameObject);
                    b.ResetBlock();
                    objectPool.ReturnObject(b);
                }
                gridblocks.Clear();
            }
        }
    }
}
