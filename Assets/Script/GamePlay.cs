using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class GamePlay : MonoBehaviour
{
    public static GamePlay instance;

    private void Awake()
    {
        instance = this;
    }

    private bool isClicked = false;

    private Color[] colors = new Color[] { Color.red, Color.blue, Color.yellow, Color.green };

    private List<Block> selectedBlocks = new List<Block>();
    private Block[,] grid;

    private Dictionary<DotType, List<Block>> completedPairs = new Dictionary<DotType, List<Block>>();

    private bool isLastBlockFromStartedDot = false;

    void Update()
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

    public void SetGridSize(int rowSize, int coloumSize)
    {
        grid = new Block[rowSize, coloumSize];
    }

    public void SetBlockAtGridIndex(int row, int coloum, Block block)
    {
        grid[row, coloum] = block;
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
        PointerEventData eventData = new PointerEventData(EventSystem.current);
        eventData.position = Input.mousePosition;

        List<RaycastResult> results = new List<RaycastResult>();

        EventSystem.current.RaycastAll(eventData, results);

        if (results.Count > 0)
        {
            Block block = results[0].gameObject.GetComponent<Block>();

            if (block != null && block.IsDotPresent && completedPairs.ContainsKey(block.DotType)) // if click on same dot, clear all highlighted blocks
            {
                List<Block> blocks = completedPairs[block.DotType];
                foreach (Block b in blocks)
                {
                    b.DisableAllDirImages();
                }
                completedPairs.Remove(block.DotType);
                isClicked = true;
                selectedBlocks.Add(block);
            }
            else if (block != null && completedPairs.ContainsKey(block.HighlightedDotType)) // to clear the completed block/highlighted block
            {
                List<Block> blocks = completedPairs[block.HighlightedDotType];
                int indexToRemove = -1;

                if(blocks[blocks.Count - 1].Row_ID == block.Row_ID && blocks[blocks.Count - 1].Coloum_ID == block.Coloum_ID)
                {
                    isLastBlockFromStartedDot = true;
                    isClicked = true;
                    selectedBlocks.Add(block);
                }
                else
                {
                    bool found = false;
                    bool disablePreviousBlock = false;

                    for (int i = 0; i < blocks.Count; i++)
                    {
                        Block b = blocks[i];

                        if (!found)
                        {
                            if (b.Row_ID == block.Row_ID && b.Coloum_ID == block.Coloum_ID)
                            {
                                indexToRemove = i;
                                found = true;
                            }
                        }
                        else
                        {
                            if (!disablePreviousBlock)
                            {
                                Direction dir = GetDirection(blocks[indexToRemove], b);
                                if (dir != Direction.None)
                                {
                                    blocks[indexToRemove].DisableDirImage(dir);
                                    disablePreviousBlock = true;
                                }
                            }

                            b.DisableAllDirImages();
                        }
                    }
                }
                /*
                if(indexToRemove == blocks.Count - 1)
                {
                    isLastBlockFromStartedDot = true;
                    isClicked = true;
                    selectedBlocks.Add(block);
                    Debug.Log("existing block found");
                    return;
                }
                */
                if (indexToRemove != -1)
                {
                    blocks.RemoveRange(indexToRemove + 1, blocks.Count - indexToRemove - 1);
                }
            }
            else if (block != null && block.IsDotPresent && !selectedBlocks.Contains(block))
            {
                isClicked = true;
                selectedBlocks.Add(block);
            }
        }
    }

    private void OnPointerMoved()
    {
        if (isClicked)
        {
            PointerEventData eventData = new PointerEventData(EventSystem.current);
            eventData.position = Input.mousePosition;

            List<RaycastResult> results = new List<RaycastResult>();

            EventSystem.current.RaycastAll(eventData, results);

            if (results.Count > 0)
            {
                Block block = results[0].gameObject.GetComponent<Block>();

                if (block != null && !block.IsDotPresent && selectedBlocks.Count > 0 && !selectedBlocks.Contains(block) ||
                    block != null && block.IsDotPresent && selectedBlocks.Count > 0 && block.DotType == selectedBlocks[0].DotType && !selectedBlocks.Contains(block)  ||
                    block != null && block.IsDotPresent && selectedBlocks.Count > 0 && block.DotType == selectedBlocks[0].HighlightedDotType && !selectedBlocks.Contains(block)  ||
                    block != null && isLastBlockFromStartedDot && !block.IsDotPresent && selectedBlocks.Count > 0 && !selectedBlocks.Contains(block)  ||
                    block != null && isLastBlockFromStartedDot && block.IsDotPresent && selectedBlocks.Count > 0 && block.DotType == selectedBlocks[0].HighlightedDotType && !selectedBlocks.Contains(block))

                {
                    if (completedPairs.ContainsKey(block.HighlightedDotType) && block.HighlightedDotType != selectedBlocks[0].HighlightedDotType)
                    {
                        List<Block> blocks = completedPairs[block.HighlightedDotType];

                        int indexToRemove = -1;

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
                            if(!flag)
                            {
                                Direction di = GetDirection(b, blocks[i + 1]);
                                if(di != Direction.None)
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
                    }
                   
                    Direction dir = GetDirection(selectedBlocks[selectedBlocks.Count - 1], block);

                    if (dir != Direction.None)
                    {
                        DotType type = selectedBlocks[0].DotType;
                        if (isLastBlockFromStartedDot)
                        {
                            type = selectedBlocks[selectedBlocks.Count - 1].HighlightedDotType;
                        }
                       
                        switch (dir)
                        {
                            case Direction.Left:
                                selectedBlocks[selectedBlocks.Count - 1].HighlightBlockDirection(dir, type);
                                block.HighlightBlockDirection(Direction.Right, type);
                                break;

                            case Direction.Right:
                                selectedBlocks[selectedBlocks.Count - 1].HighlightBlockDirection(dir, type);
                                block.HighlightBlockDirection(Direction.Left, type);
                                break;

                            case Direction.Up:
                                selectedBlocks[selectedBlocks.Count - 1].HighlightBlockDirection(dir, type);
                                block.HighlightBlockDirection(Direction.Down, type);
                                break;

                            case Direction.Down:
                                selectedBlocks[selectedBlocks.Count - 1].HighlightBlockDirection(dir, type);
                                block.HighlightBlockDirection(Direction.Up, type);
                                break;
                        }

                        selectedBlocks.Add(block);
                    }
                }
                else if (isLastBlockFromStartedDot && block != null && selectedBlocks.Contains(block)) // && block.HighlightedDotType == selectedBlocks[0].HighlightedDotType && selectedBlocks.Contains(block))
                {
                    List<Block> blocks = completedPairs[(block.HighlightedDotType)];

                    if (blocks.Count > 0 && blocks[blocks.Count - 1].Row_ID == block.Row_ID && blocks[blocks.Count - 1].Coloum_ID == block.Coloum_ID)
                    {
                        return;
                    }


                    if (blocks.Count > 0 && blocks.Contains(block)) // && !blocks[blocks.Count - 1].IsDotPresent)
                    {
                        Block b = blocks[blocks.Count - 1];
                        Direction dir = GetDirection(block, b);

                        if (dir != Direction.None)
                        {
                            b.DisableAllDirImages();
                            block.DisableDirImage(dir);

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
        isClicked = false;
        if (selectedBlocks.Count > 0)
        {
            if (isLastBlockFromStartedDot && completedPairs.ContainsKey(selectedBlocks[0].HighlightedDotType) && completedPairs[selectedBlocks[0].HighlightedDotType].Count > 0)
            {
                selectedBlocks.RemoveAt(0); // added two times

                if(selectedBlocks.Count > 0)
                {
                    completedPairs[selectedBlocks[0].HighlightedDotType].AddRange(new List<Block>(selectedBlocks));
                    Debug.Log("Storing value to existing list " + selectedBlocks[0].HighlightedDotType + " " + completedPairs[selectedBlocks[0].HighlightedDotType].Count);
                }
            }
            else
            {
                completedPairs[selectedBlocks[0].DotType] = new List<Block>(selectedBlocks);
                Debug.Log("Storing value to new list " + selectedBlocks[0].DotType + " " + completedPairs[selectedBlocks[0].DotType].Count);
            }
        }

        isLastBlockFromStartedDot = false;
        selectedBlocks.Clear();

        CheckForPairComplete();
    }

    private bool CheckForPairComplete()
    {

        foreach (var kvp in completedPairs)
        {
            DotType key = kvp.Key;
            List<Block> blockList = kvp.Value;

            if (blockList.Count > 0 && IsPairComplete(blockList[0], blockList[blockList.Count - 1]))
            {
                Debug.Log("Pair complete " + key);
            }
            else
            {
                Debug.Log("Pair not complete " + key);
            }
        }

        return false;
    }

    private bool IsPairComplete(Block b1, Block b2)
    {
        return (!IsEqual(b1, b2) && b1.IsDotPresent && b2.IsDotPresent && b1.DotType == b2.DotType);
    }

    private bool IsEqual(Block b1, Block b2)
    {
        return (b1.Row_ID == b2.Row_ID && b1.Coloum_ID == b2.Coloum_ID);
    }
}
