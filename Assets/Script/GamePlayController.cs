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

    private Color[] colors;
    private List<Block> selectedBlocks;
    private Dictionary<DotType, List<Block>> completedPairs;
   
    private EventSystem eventSystem;
    private List<RaycastResult> raycastResults;
    private PointerEventData eventData;

    private int moves;

    private void Start()
    {
        gameState = GameState.Waiting;

        isClicked = false;
        hasSelectExistingFromLast = false;
        hasSelectExistingFromMiddle = false;

        colors = new Color[] { Color.red, Color.blue, Color.yellow, Color.green };
        selectedBlocks = new List<Block>();
        completedPairs = new Dictionary<DotType, List<Block>>();

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

    public Color GetColor(DotType type)
    {
        return colors[((int)type - 1)];
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
                if (block.IsDotPresent && completedPairs.ContainsKey(block.DotType)) // if click on same dot, clear all highlighted blocks
                {
                    List<Block> blocks = completedPairs[block.DotType];
                    foreach (Block b in blocks)
                    {
                        b.ResetAllHighlightDirection();
                    }
                    completedPairs.Remove(block.DotType);
                    isClicked = true;
                    selectedBlocks.Add(block);
                }
                else if (block.IsDotPresent && !selectedBlocks.Contains(block))
                {
                    isClicked = true;
                    selectedBlocks.Add(block);
                }
                else if (completedPairs.ContainsKey(block.HighlightedDotType)) // to clear the completed block/highlighted block
                {
                    List<Block> blocks = completedPairs[block.HighlightedDotType];

                    if (IsEqual(blocks[blocks.Count - 1], block))
                    {
                        hasSelectExistingFromLast = true;
                        isClicked = true;
                        selectedBlocks.Add(block);
                        return;
                    }


                    int indexToRemove = GetBlockIndex(blocks, block);
                    if (indexToRemove != -1)
                    {
                        ResetBlockToRemove(blocks, indexToRemove);
                    }

                    hasSelectExistingFromMiddle = true;
                    isClicked = true;
                    selectedBlocks.Add(block);
                }
            }
        }
    }

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

        blocks.RemoveRange(indexToRemove + 1, blocks.Count - indexToRemove - 1);
    }

    private void PerformRaycast(PointerEventData eventData, List<RaycastResult> results)
    {
        eventSystem.RaycastAll(eventData, results);
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


                // adds the new blocks to the selected list
                if (block != null && selectedBlocks.Count > 0 && !selectedBlocks.Contains(block))
                {
                    if ((!block.IsDotPresent) || // normal blocks - without any dot present
                        (block.IsDotPresent && block.DotType == selectedBlocks[0].DotType) || // if dot present, is it same as the selected
                        (block.IsDotPresent && block.DotType == selectedBlocks[0].HighlightedDotType) || // selected block from middle one, dreag to same dot type block
                        (!block.IsDotPresent && hasSelectExistingFromLast) || // keep adding to existing list
                        (block.IsDotPresent && hasSelectExistingFromLast && block.DotType == selectedBlocks[0].HighlightedDotType))
                    {

                        // if intersect the pair with the another pair, reset the another pait till intersection 
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
                            /*
                            for (int i = 0; i < blocks.Count; i++)
                            {
                                Block b = blocks[i];

                                if (b.Row_ID == block.Row_ID && b.Coloum_ID == block.Coloum_ID)
                                {
                                    indexToRemove = i - 1;
                                    break;
                                }
                            }
                            
                            bool flag = false;
                            for (int i = indexToRemove; i < blocks.Count; i++)
                            {
                                Block b = blocks[i];
                                if (!flag)
                                {
                                    Direction di = GetDirection(b, blocks[i + 1]);
                                    if (di != Direction.None)
                                    {
                                        b.DisableDirImage(di);
                                    }
                                    flag = true;
                                }
                                else
                                {
                                    b.DisableAllDirImages();
                                }
                            }

                            if (indexToRemove != -1)
                            {
                                blocks.RemoveRange(indexToRemove + 1, blocks.Count - indexToRemove - 1);
                            }
                            */
                        }

                        Direction dir = GetDirection(selectedBlocks[selectedBlocks.Count - 1], block);

                        if (dir != Direction.None)
                        {
                            DotType type = hasSelectExistingFromLast ? selectedBlocks[selectedBlocks.Count - 1].HighlightedDotType : selectedBlocks[0].DotType;
                            if (hasSelectExistingFromMiddle)
                            {
                                type = selectedBlocks[selectedBlocks.Count - 1].HighlightedDotType;
                            }

                            selectedBlocks[selectedBlocks.Count - 1].HighlightBlock(dir, type);

                            switch (dir)
                            {
                                case Direction.Left:
                                    block.HighlightBlock(Direction.Right, type);
                                    break;

                                case Direction.Right:
                                    //selectedBlocks[selectedBlocks.Count - 1].HighlightBlockDirection(dir, type);
                                    block.HighlightBlock(Direction.Left, type);
                                    break;

                                case Direction.Up:
                                    //selectedBlocks[selectedBlocks.Count - 1].HighlightBlockDirection(dir, type);
                                    block.HighlightBlock(Direction.Down, type);
                                    break;

                                case Direction.Down:
                                    //selectedBlocks[selectedBlocks.Count - 1].HighlightBlockDirection(dir, type);
                                    block.HighlightBlock(Direction.Up, type);
                                    break;
                            }

                            selectedBlocks.Add(block);
                        }
                    }
                }
                /*
                if (block != null && !block.IsDotPresent && selectedBlocks.Count > 0 && !selectedBlocks.Contains(block) ||
                    block != null && block.IsDotPresent && selectedBlocks.Count > 0 && block.DotType == selectedBlocks[0].DotType && !selectedBlocks.Contains(block)  ||
                    block != null && block.IsDotPresent && selectedBlocks.Count > 0 && block.DotType == selectedBlocks[0].HighlightedDotType && !selectedBlocks.Contains(block)  ||
                    block != null && isLastBlockFromStartedDot && !block.IsDotPresent && selectedBlocks.Count > 0 && !selectedBlocks.Contains(block)  ||
                    block != null && isLastBlockFromStartedDot && block.IsDotPresent && selectedBlocks.Count > 0 && block.DotType == selectedBlocks[0].HighlightedDotType && !selectedBlocks.Contains(block))

                {
                  */

                // reset the highlighted block
                else if (hasSelectExistingFromLast && block != null && selectedBlocks.Contains(block)) // && block.HighlightedDotType == selectedBlocks[0].HighlightedDotType && selectedBlocks.Contains(block))
                {
                    List<Block> blocks = completedPairs[(block.HighlightedDotType)];

                    if (blocks.Count > 0 && IsEqual(blocks[blocks.Count - 1], block)) // && blocks[blocks.Count - 1].Row_ID == block.Row_ID && blocks[blocks.Count - 1].Coloum_ID == block.Coloum_ID)
                    {
                        return;
                    }

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

    private void OnPointerUp()
    {
        if (selectedBlocks.Count > 0)
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
                completedPairs[selectedBlocks[0].DotType] = new List<Block>(selectedBlocks);
                //Debug.Log("Storing value to new list " + selectedBlocks[0].DotType + " " + completedPairs[selectedBlocks[0].DotType].Count);
            }

            if(selectedBlocks.Count > 1) 
            {
                moves++;
                UIController.Instance.UpdateMovesCount(moves);
            }
        }

        hasSelectExistingFromMiddle = false;
        isClicked = false;
        hasSelectExistingFromLast = false;
        selectedBlocks.Clear();

        CheckForPairComplete();
    }

    private void CheckForPairComplete()
    {
        int count = 0;
        foreach (var kvp in completedPairs)
        {
            DotType key = kvp.Key;
            List<Block> blockList = kvp.Value;


            if (blockList.Count > 0 && IsPairComplete(blockList[0], blockList[blockList.Count - 1]))
            {
                count++;
            }
            
        }

        UIController.Instance.UpdatePairCount(count);

        if (count >= UIController.Instance.CurrentLevelGoal)
        {
            GameState = GameState.Ending;
            UIController.Instance.ActivateLevelCompleteScreen(moves);
        }
    }

    private bool IsPairComplete(Block b1, Block b2)
    {
        return (!IsEqual(b1, b2) && b1.IsDotPresent && b2.IsDotPresent && b1.DotType == b2.DotType);
    }

    private bool IsEqual(Block b1, Block b2)
    {
        return (b1.Row_ID == b2.Row_ID && b1.Coloum_ID == b2.Coloum_ID);
    }

    public GameState GameState
    {
        get { return gameState; }
        set { gameState = value; }
    }

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
