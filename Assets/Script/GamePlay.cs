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

            if (block != null && block.IsDotPresent && completedPairs.ContainsKey(block.DotType))
            {
                List<Block> blocks = completedPairs[block.DotType];
                foreach (Block b in blocks)
                {
                    b.DisableAllDirImages();
                    completedPairs.Remove(block.DotType);
                }
            }
            else if (block != null && completedPairs.ContainsKey(block.HighlightedDotType))
            {
                List<Block> blocks = completedPairs[block.HighlightedDotType];
                int indexToRemove = -1;
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
                        if(!disablePreviousBlock)
                        {
                            Direction dir = GetDirection(blocks[indexToRemove], b);
                            blocks[indexToRemove].DisableDirImage(dir);
                            disablePreviousBlock = true;
                        }

                        b.DisableAllDirImages();
                    }

                }

                if(indexToRemove == blocks.Count - 1)
                {
                    isLastBlockFromStartedDot = true;
                    isClicked = true;
                    selectedBlocks.Add(block);
                    return;
                }

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

                if (block != null && !block.IsDotPresent && !selectedBlocks.Contains(block) ||
                    block != null && block.IsDotPresent && block.DotType == selectedBlocks[0].DotType && !selectedBlocks.Contains(block) ||
                     block != null && block.IsDotPresent && block.DotType == selectedBlocks[0].HighlightedDotType && !selectedBlocks.Contains(block))
                {
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
            }
        }
    }

    private void OnPointerUp()
    {
        Debug.Log("Pointer up");
        isClicked = false;
        if (selectedBlocks.Count > 0)
        {
            if (isLastBlockFromStartedDot && completedPairs.ContainsKey(selectedBlocks[0].HighlightedDotType) && completedPairs[selectedBlocks[0].HighlightedDotType].Count > 0)
            {
                selectedBlocks.RemoveAt(0); // added two times

                if(selectedBlocks.Count > 0)
                {
                    completedPairs[selectedBlocks[0].HighlightedDotType].AddRange(new List<Block>(selectedBlocks));
                    Debug.Log("Adding to existing list" + completedPairs[selectedBlocks[0].HighlightedDotType].Count);
                }
            }
            else
            {
                completedPairs[selectedBlocks[0].DotType] = new List<Block>(selectedBlocks);
                Debug.Log("Adding to new  " + completedPairs[selectedBlocks[0].DotType].Count);
            }
        }

        isLastBlockFromStartedDot = false;
        selectedBlocks.Clear();
    }
}
