using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class GamePlayController : Singleton<GamePlayController>
{
    private GameState gameState;

    private bool isClicked;
    private bool hasSelectExistingFromLast;
    private bool hasSelectExistingFromMiddle;

    private List<Block> selectedBlocks;
    private Dictionary<PairColorType, List<Block>> completedPairs;
   
    private EventSystem eventSystem;
    private List<RaycastResult> raycastResults;
    private PointerEventData eventData;

    [SerializeField] private PairColorDataSO PairColorDataSO;

    private int moves;

    private void Start()
    {
        gameState = GameState.Waiting;

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
        if(gameState == GameState.Playing)
        {
            if (Input.GetMouseButtonDown(0))
            {
                OnPointerDown();
            }
            else if (Input.GetMouseButton(0))
            {
                OnPointerMoved();
            }
            else if (Input.GetMouseButtonUp(0))
            {
                OnPointerUp();
            }
        }
    }

    private void OnPointerDown()
    {
        eventData.position = Input.mousePosition;
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
                else if (completedPairs.ContainsKey(block.HighlightedDotType))
                {
                    List<Block> blocks = completedPairs[block.HighlightedDotType];

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
                    selectedBlocks.Add(block);
                }
            }
        }
    }

    private void OnPointerMoved()
    {
        if (isClicked)
        {
            eventData.position = Input.mousePosition;
            raycastResults.Clear();
            PerformRaycast(eventData, raycastResults);

            if (raycastResults.Count > 0)
            {
                Block block = raycastResults[0].gameObject.GetComponent<Block>();

                // selected block is not select again, check for the new block
                if (block != null && selectedBlocks.Count > 0 && !selectedBlocks.Contains(block))
                {
                    if ((!block.IsPairBlock) || //if not the normal blocks - without pair block
                        (block.IsPairBlock && block.PairColorType == selectedBlocks[0].PairColorType) || // if dot present, is it same as the selected
                        (block.IsPairBlock && block.PairColorType == selectedBlocks[0].HighlightedDotType) || // selected block from middle one, dreag to same dot type block
                        (!block.IsPairBlock && hasSelectExistingFromLast) || // keep adding to existing list
                        (block.IsPairBlock && hasSelectExistingFromLast && block.PairColorType == selectedBlocks[0].HighlightedDotType))
                    {

                        // checks for the selected block is intersect with the another highlighted blocks (completed or incompleted highlighted pair)
                        if (completedPairs.ContainsKey(block.HighlightedDotType) && block.HighlightedDotType != selectedBlocks[0].HighlightedDotType)
                        {
                            List<Block> blocks = completedPairs[block.HighlightedDotType];

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
                            PairColorType type = hasSelectExistingFromLast ? selectedBlocks[selectedBlocks.Count - 1].HighlightedDotType : selectedBlocks[0].PairColorType;
                            if (hasSelectExistingFromMiddle)
                            {
                                type = selectedBlocks[selectedBlocks.Count - 1].HighlightedDotType;
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
                }
                // if selected block is already highlighted pair blocks, resets the block (unhighlight it)
                else if (hasSelectExistingFromLast && block != null && selectedBlocks.Contains(block))
                {
                    List<Block> blocks = completedPairs[(block.HighlightedDotType)];

                    //last highlighted block selected
                    if (blocks.Count > 0 && IsEqual(blocks[blocks.Count - 1], block))
                    {
                        return;
                    }

                    // selected somewhere between first and last pair, resets the blocks
                    if (blocks.Count > 0 && blocks.Contains(block))
                    {
                        Block b = blocks[blocks.Count - 1];
                        Direction dir = GetDirection(block, b);

                        if (dir != Direction.None)
                        {
                            b.ResetAllHighlightDirection();
                            block.ResetHighlightDirection(dir);

                            blocks.RemoveAt(blocks.Count - 1);
                            selectedBlocks.Clear();
                            selectedBlocks.Add(block);
                        }
                    }
                }
            }
        }
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
            if(type == PairColorDataSO.pairColorDatas[i].pairColorType)
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

  

    private void OnPointerUp()
    {
        if (selectedBlocks.Count > 0)
        {
            AddSelectedBlocksToCompletedPairs();

            if(selectedBlocks.Count > 1) 
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
    /// Adds the selected blocks to the completed pairs
    /// </summary>
    private void AddSelectedBlocksToCompletedPairs()
    {
        if ((hasSelectExistingFromLast || hasSelectExistingFromMiddle) && completedPairs.ContainsKey(selectedBlocks[0].HighlightedDotType) && completedPairs[selectedBlocks[0].HighlightedDotType].Count > 0)
        {
            selectedBlocks.RemoveAt(0); // added two times

            if (selectedBlocks.Count > 0)
            {
                completedPairs[selectedBlocks[0].HighlightedDotType].AddRange(new List<Block>(selectedBlocks));
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
