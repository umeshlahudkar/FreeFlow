using FreeFlow.Enums;
using FreeFlow.UI;
using FreeFlow.Util;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;


namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Manages the gameplay logic and user interactions for a matching pairs game
    /// </summary>
    public class GamePlayController : Singleton<GamePlayController>
    {
        [SerializeField] private GameState gameState = GameState.Waiting;

        private bool isClicked;
        private bool hasSelectExistingFromLast;
        private bool hasSelectExistingFromMiddle;

        [SerializeField] private List<Block> selectedBlocks;
        private Dictionary<PairColorType, List<Block>> completedPairs;

        private EventSystem eventSystem;
        private List<RaycastResult> raycastResults;
        private PointerEventData eventData;

        [SerializeField] private PairColorDataSO PairColorDataSO;

        private int moves;

        private void Start()
        {
            isClicked = false;
            hasSelectExistingFromLast = false;
            hasSelectExistingFromMiddle = false;

            selectedBlocks = new List<Block>();
            completedPairs = new Dictionary<PairColorType, List<Block>>();

            eventSystem = EventSystem.current;
            raycastResults = new List<RaycastResult>();
            eventData = new PointerEventData(eventSystem);

            moves = 0;
        }

        void Update()
        {
            if (gameState == GameState.Playing)
            {
                if (UnityEngine.Input.GetMouseButtonDown(0))
                {
                    OnPointerDown();
                }
                else if (UnityEngine.Input.GetMouseButton(0))
                {
                    OnPointerMoved();
                }
                else if (UnityEngine.Input.GetMouseButtonUp(0))
                {
                    OnPointerUp();
                }
            }
        }

        private void OnPointerDown()
        {
            eventData.position = UnityEngine.Input.mousePosition;
            raycastResults.Clear();
            PerformRaycast(eventData, raycastResults);

            if (raycastResults.Count > 0)
            {
                Block block = raycastResults[0].gameObject.GetComponent<Block>();

                if (block != null)
                {
                    // if click on pair dot and the block is already clicked before, clears all highlighted blocks
                    // for the pair and removed it from the completed pairs list
                    if (block.IsPairBlock && completedPairs.ContainsKey(block.PairColorType))
                    {
                        List<Block> blocks = completedPairs[block.PairColorType];
                        foreach (Block b in blocks)
                        {
                            b.ResetAllHighlightDirection();
                        }
                        completedPairs.Remove(block.PairColorType);

                        isClicked = true;
                        selectedBlocks.Add(block);
                    }

                    //if click on pair dot and block is not clicked before
                    else if (block.IsPairBlock && !selectedBlocks.Contains(block))
                    {
                        isClicked = true;
                        selectedBlocks.Add(block);
                    }

                    // if the some blocks of pair highlighted, and clicked on the highlighted block
                    else if (completedPairs.ContainsKey(block.HighlightedColorType))
                    {
                        List<Block> blocks = completedPairs[block.HighlightedColorType];

                        //check if the selected block is the last block of highlighted blocks list
                        if (IsEqual(blocks[blocks.Count - 1], block))
                        {
                            hasSelectExistingFromLast = true;
                           
                        }

                        //if selected block is the somewhere between first and last highlighted block,
                        //clears the highlighted blocks till the selected block
                        else
                        {
                            int indexToRemove = GetBlockIndex(blocks, block);
                            if (indexToRemove != -1)
                            {
                                ResetBlockToRemove(blocks, indexToRemove);
                            }
                            hasSelectExistingFromMiddle = true;
                        }

                        isClicked = true;
                        selectedBlocks.Clear();
                        selectedBlocks.AddRange(blocks);

                        completedPairs.Remove(block.HighlightedColorType);
                        //selectedBlocks.Add(block);
                    }
                }
            }
        }

        private void OnPointerMoved()
        {
            if (isClicked)
            {
                eventData.position = UnityEngine.Input.mousePosition;
                raycastResults.Clear();
                PerformRaycast(eventData, raycastResults);

                if (raycastResults.Count > 0)
                {
                    Block block = raycastResults[0].gameObject.GetComponent<Block>();

                    // selected block is not select again, check for the new block
                    if(CanSelectToAdd(block))
                    { 
                        // checks for the selected block is intersect with the another highlighted blocks (completed or incompleted highlighted pair)
                        if (completedPairs.ContainsKey(block.HighlightedColorType) && block.HighlightedColorType != selectedBlocks[0].HighlightedColorType)
                        {
                            List<Block> blocks = completedPairs[block.HighlightedColorType];

                            int indexToRemove = GetBlockIndex(blocks, block);
                            indexToRemove--;
                            if (indexToRemove <= -1) { indexToRemove = -1; }

                            if (indexToRemove != -1)
                            {
                                ResetBlockToRemove(blocks, indexToRemove);
                            }
                        }

                        // gets the direction of new selected block with respect to last selected blocks
                        Direction dir = GetDirection(selectedBlocks[selectedBlocks.Count - 1], block);

                        // highlighted the new selected block and last selected block based on direction
                        if (dir != Direction.None)
                        {
                            PairColorType type = hasSelectExistingFromLast ? selectedBlocks[selectedBlocks.Count - 1].HighlightedColorType : selectedBlocks[0].PairColorType;
                            if (hasSelectExistingFromMiddle)
                            {
                                type = selectedBlocks[selectedBlocks.Count - 1].HighlightedColorType;
                            }

                            //highlighting last selected block
                            selectedBlocks[selectedBlocks.Count - 1].HighlightBlock(dir, type);

                            //highlight new selected block
                            switch (dir)
                            {
                                case Direction.Left:
                                    block.HighlightBlock(Direction.Right, type);
                                    break;

                                case Direction.Right:
                                    block.HighlightBlock(Direction.Left, type);
                                    break;

                                case Direction.Up:
                                    block.HighlightBlock(Direction.Down, type);
                                    break;

                                case Direction.Down:
                                    block.HighlightBlock(Direction.Up, type);
                                    break;
                            }

                            selectedBlocks.Add(block);
                        }
                    }
                    // if selected block is already highlighted pair blocks, resets the block (unhighlight it)
                    //else if (hasSelectExistingFromLast && block != null && selectedBlocks.Contains(block))
                    //{
                    //    List<Block> blocks = completedPairs[(block.HighlightedColorType)];

                    //    //last highlighted block selected
                    //    if (blocks.Count > 0 && IsEqual(blocks[blocks.Count - 1], block))
                    //    {
                    //        return;
                    //    }

                    //    // selected somewhere between first and last pair, resets the blocks
                    //    if (blocks.Count > 0 && blocks.Contains(block))
                    //    {
                    //        Block b = blocks[blocks.Count - 1];
                    //        Direction dir = GetDirection(block, b);

                    //        if (dir != Direction.None)
                    //        {
                    //            b.ResetAllHighlightDirection();
                    //            block.ResetHighlightDirection(dir);

                    //            blocks.RemoveAt(blocks.Count - 1);
                    //            selectedBlocks.Clear();
                    //            selectedBlocks.Add(block);
                    //        }
                    //        Debug.Log("from last");
                    //    }
                    //}
                    else if(block != null && selectedBlocks.Contains(block))
                    {
                        if (!IsEqual(selectedBlocks[selectedBlocks.Count - 1], block) && 
                            IsEqual(selectedBlocks[selectedBlocks.Count - 2], block))
                        {
                            // selected somewhere between first and last pair, resets the blocks
                            //if (blocks.Count > 0 && blocks.Contains(block))
                            {
                                Block b = selectedBlocks[selectedBlocks.Count - 1];
                                Direction dir = GetDirection(block, b);

                                if (dir != Direction.None)
                                {
                                    b.ResetAllHighlightDirection();
                                    block.ResetHighlightDirection(dir);

                                    selectedBlocks.Remove(b);
                                    //selectedBlocks.Add(block);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void OnPointerUp()
        {
            if (selectedBlocks.Count > 0)
            {
                AddSelectedBlocksToCompletedPairs();

                if (selectedBlocks.Count > 1)
                {
                    moves++;
                    UIController.Instance.UpdateMovesCount(moves);
                }
            }

            isClicked = false;
            hasSelectExistingFromMiddle = false;
            hasSelectExistingFromLast = false;
            selectedBlocks.Clear();

            int count = GetPairCompleteCount();
            UIController.Instance.UpdatePairCount(count);

            if (count >= UIController.Instance.CurrentLevelGoal)
            {
                GameState = GameState.Ending;
                UIController.Instance.ActivateLevelCompleteScreen(moves);
            }
        }

        /// <summary>
        /// Check can the block is added to to list by checking if it's not alredy added
        /// </summary>
        /// <param name="block">the block to check</param>
        /// <returns></returns>
        private bool CanSelectToAdd(Block block)
        {
            if (block == null || selectedBlocks.Count <= 0 || selectedBlocks.Contains(block))
            {
                return false;
            }

            bool isPairBlockComplete = selectedBlocks[0].IsPairBlock && IsPairComplete(selectedBlocks[0], selectedBlocks[selectedBlocks.Count - 1]);

            if (!isPairBlockComplete)
            {
                if
                (
                    (!block.IsPairBlock) ||
                    (block.IsPairBlock && block.PairColorType == selectedBlocks[0].PairColorType) ||
                    (block.IsPairBlock && block.PairColorType == selectedBlocks[0].HighlightedColorType) ||
                    (!block.IsPairBlock && hasSelectExistingFromLast) ||
                    (block.IsPairBlock && hasSelectExistingFromLast && block.PairColorType == selectedBlocks[0].HighlightedColorType)
                )
                {
                    if ((hasSelectExistingFromLast || hasSelectExistingFromMiddle) && IsPairComplete(selectedBlocks[0], selectedBlocks[selectedBlocks.Count - 1], selectedBlocks[0].HighlightedColorType))
                    {
                        return false;
                    }

                    if(completedPairs.ContainsKey(block.HighlightedColorType) && completedPairs[block.HighlightedColorType].Contains(block) && block.HighlightedColorType == selectedBlocks[0].HighlightedColorType)
                    {
                        return false;
                    }

                    return true;
                }
            }

            return false;
        }


        /// <summary>
        /// Determines the direction of block2 with respect to block1
        /// </summary>
        /// <param name="block1">The first block.</param>
        /// <param name="block2">The second block.</param>
        /// <returns>The direction of block2(Right, Left, Up, Down) from block1, or None if they are not adjacent.</returns>
        private Direction GetDirection(Block block1, Block block2)
        {
            if (block1.Row_ID == block2.Row_ID && block1.Coloum_ID < block2.Coloum_ID && block2.Coloum_ID - block1.Coloum_ID == 1)
            {
                return Direction.Right;
            }

            if (block1.Row_ID == block2.Row_ID && block1.Coloum_ID > block2.Coloum_ID && block1.Coloum_ID - block2.Coloum_ID == 1)
            {
                return Direction.Left;
            }

            if (block1.Coloum_ID == block2.Coloum_ID && block1.Row_ID > block2.Row_ID && block1.Row_ID - block2.Row_ID == 1)
            {
                return Direction.Up;
            }

            if (block1.Coloum_ID == block2.Coloum_ID && block1.Row_ID < block2.Row_ID && block2.Row_ID - block1.Row_ID == 1)
            {
                return Direction.Down;
            }
            return Direction.None;
        }


        /// <summary>
        /// Retrieves the color for PairColorType from a ColorDataSO.
        /// </summary>
        /// <param name="type">The PairColorType for which to retrieve the color.</param>
        /// <returns>The color associated with the specified PairColorType, defaulting to black if not found.</returns>
        public Color GetColor(PairColorType type)
        {
            Color color = Color.black;
            for (int i = 0; i < PairColorDataSO.pairColorDatas.Length; i++)
            {
                if (type == PairColorDataSO.pairColorDatas[i].pairColorType)
                {
                    color = PairColorDataSO.pairColorDatas[i].color;
                    break;
                }
            }
            return color;
        }

        /// <summary>
        /// Gets the index of a specific block within a list
        /// </summary>
        /// <param name="blocks">The list of blocks to search within.</param>
        /// <param name="block">The block to find in the list.</param>
        /// <returns>The index of the block if found, or -1 if not found.</returns>
        private int GetBlockIndex(List<Block> blocks, Block block)
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                Block b = blocks[i];
                if (IsEqual(b, block))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Resets the highlight direction of a block and removes subsequent blocks from the list.
        /// </summary>
        /// <param name="blocks">The list of blocks to modify.</param>
        /// <param name="indexToRemove">The index of the block to start resetting and removing from.</param>
        private void ResetBlockToRemove(List<Block> blocks, int indexToRemove)
        {
            for (int i = indexToRemove; i < blocks.Count; i++)
            {
                Block b = blocks[i];
                if (i == indexToRemove)
                {
                    Direction dir = GetDirection(b, blocks[indexToRemove + 1]);
                    if (dir != Direction.None)
                    {
                        blocks[indexToRemove].ResetHighlightDirection(dir);
                    }
                }
                else
                {
                    b.ResetAllHighlightDirection();
                }
            }

            // Remove blocks from the list starting from indexToRemove + 1.
            blocks.RemoveRange(indexToRemove + 1, blocks.Count - indexToRemove - 1);
        }


        /// <summary>
        /// Performs a raycast using the event system to gather raycast results.
        /// </summary>
        /// <param name="eventData">The pointer event data for the raycast.</param>
        /// <param name="results">The list to store the raycast results.</param>
        private void PerformRaycast(PointerEventData eventData, List<RaycastResult> results)
        {
            eventSystem.RaycastAll(eventData, results);
        }

        /// <summary>
        /// Adds the selected blocks to the completed pairs
        /// </summary>
        private void AddSelectedBlocksToCompletedPairs()
        {
            if ((hasSelectExistingFromLast || hasSelectExistingFromMiddle) && completedPairs.ContainsKey(selectedBlocks[0].HighlightedColorType) && completedPairs[selectedBlocks[0].HighlightedColorType].Count > 0)
            {
                selectedBlocks.RemoveAt(0); // added two times

                if (selectedBlocks.Count > 0)
                {
                    completedPairs[selectedBlocks[0].HighlightedColorType].AddRange(new List<Block>(selectedBlocks));
                    //Debug.Log("Storing value to existing list " + selectedBlocks[0].HighlightedDotType + " " + completedPairs[selectedBlocks[0].HighlightedDotType].Count);
                }
            }
            else
            {
                completedPairs[selectedBlocks[0].PairColorType] = new List<Block>(selectedBlocks);
                //Debug.Log("Storing value to new list " + selectedBlocks[0].DotType + " " + completedPairs[selectedBlocks[0].DotType].Count);
            }
        }


        /// <summary>
        /// Counts the number of completed pairs
        /// </summary>
        /// <returns>The count of completed pairs.</returns>
        private int GetPairCompleteCount()
        {
            int count = 0;
            foreach (var kvp in completedPairs)
            {
                PairColorType key = kvp.Key;
                List<Block> blockList = kvp.Value;

                if (blockList.Count > 0 && IsPairComplete(blockList[0], blockList[blockList.Count - 1]))
                {
                    count++;
                }
            }
            return count;
        }


        /// <summary>
        /// Checks whether a pair of blocks represents a complete pair
        /// </summary>
        /// <param name="b1">The first block of the pair.</param>
        /// <param name="b2">The second block of the pair.</param>
        /// <returns>True if the pair is complete, false otherwise.</returns>
        private bool IsPairComplete(Block b1, Block b2)
        {
            return (!IsEqual(b1, b2) && b1.IsPairBlock && b2.IsPairBlock && b1.PairColorType == b2.PairColorType);
        }

        private bool IsPairComplete(Block b1, Block b2, PairColorType type)
        {
            return (!IsEqual(b1, b2) && b1.HighlightedColorType == b2.PairColorType);
        }


        /// <summary>
        /// Checks whether two blocks are equal based on their row and column positions.
        /// </summary>
        /// <param name="b1">The first block for comparison.</param>
        /// <param name="b2">The second block for comparison.</param>
        /// <returns>True if the blocks are equal, false otherwise.</returns>
        private bool IsEqual(Block b1, Block b2)
        {
            return (b1.Row_ID == b2.Row_ID && b1.Coloum_ID == b2.Coloum_ID);
        }

        public GameState GameState
        {
            get { return gameState; }
            set { gameState = value; }
        }


        /// <summary>
        /// Resets the gameplay state to its initial conditions
        /// </summary>
        public void ResetGameplay()
        {
            moves = 0;

            gameState = GameState.Waiting;

            selectedBlocks.Clear();
            completedPairs.Clear();

            isClicked = false;
            hasSelectExistingFromLast = false;
            hasSelectExistingFromMiddle = false;
        }
    }
}
