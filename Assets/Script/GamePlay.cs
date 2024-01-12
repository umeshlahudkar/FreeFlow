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

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            PointerEventData eventData = new PointerEventData(EventSystem.current);
            eventData.position = Input.mousePosition;

            List<RaycastResult> results = new List<RaycastResult>();

            EventSystem.current.RaycastAll(eventData, results);

            if (results.Count > 0)
            {
                Block block = results[0].gameObject.GetComponent<Block>();
                if (block != null && block.IsDotPresent && !selectedBlocks.Contains(block))
                {
                    isClicked = true;
                    selectedBlocks.Add(block);
                }
            }
        }
        else if(Input.GetMouseButton(0))
        {
            if(isClicked)
            {
                PointerEventData eventData = new PointerEventData(EventSystem.current);
                eventData.position = Input.mousePosition;

                List<RaycastResult> results = new List<RaycastResult>();

                EventSystem.current.RaycastAll(eventData, results);

                if (results.Count > 0)
                {
                    Block block = results[0].gameObject.GetComponent<Block>();

                    if (block != null && !block.IsDotPresent && !selectedBlocks.Contains(block))
                    {
                        Direction dir = GetDirection(selectedBlocks[selectedBlocks.Count - 1], block);
                        if (dir != Direction.None)
                        {
                            DotType type = selectedBlocks[0].DotType;
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
        else if(Input.GetMouseButtonUp(0))
        {
            isClicked = false;
            selectedBlocks.Clear();
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
}
