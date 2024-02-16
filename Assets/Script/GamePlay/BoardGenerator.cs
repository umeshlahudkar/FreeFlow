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
        [SerializeField] private Transform thisTransform;
        private List<Block> gridblocks;

        private ObjectPool<Block> objectPool;
        private WaitForSeconds waitForSeconds;


        /// <summary>
        /// Initializes the object pool for blocks with the specified prefab, capacity, and parent transform.
        /// </summary>
        private void InitializePool()
        {
            waitForSeconds = new WaitForSeconds(0.1f);
            objectPool = new ObjectPool<Block>(blockPrefab, 16, thisTransform);
        }


        /// <summary>
        /// Generate blocks based on level data
        /// </summary>
        /// <param name="data">The LevelData containing grid size, block size, block space, and grid rows</param>
        public void GenerateBoard(LevelData data)
        {
            if (objectPool == null) { InitializePool(); }

            StartCoroutine(GenerateBoardCo(data));
        }

        private IEnumerator GenerateBoardCo(LevelData data)
        {
            ScreenAnimation anim = gameObject.GetComponentInParent<ScreenAnimation>();
            if (anim != null)
            {
                yield return new WaitForSeconds(anim.TotolTime);
            }

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

                    //yield return waitForSeconds;
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
