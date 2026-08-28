using FreeFlow.Enums;
using FreeFlow.Util;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Generates and manages the game board by creating, positioning, and resetting blocks based on provided LevelData.
    /// </summary>
    public class BoardGenerator : MonoBehaviour
    {
        [SerializeField] private Block blockPrefab;
        // The board area: a flexible-height row in GameplayScreen's VerticalLayoutGroup, so
        // it is handed whatever vertical space the header, stats row and footer leave over.
        // Cell size is measured off this at runtime rather than authored per level.
        [SerializeField] private RectTransform thisTransform;

        // The square play area: a child of the board area carrying an AspectRatioFitter set
        // to FitInParent at 1:1, so it is always the largest centred square that fits whatever
        // the layout group hands its parent. Cell size is measured off THIS, which is why the
        // grid stays square without any min(width, height) arithmetic here -- and it gives the
        // board a real rect to parent blocks to and to hang a frame or background on.
        [SerializeField] private RectTransform boardArea;

        // Breathing room kept clear inside the square, per side, so the grid never runs flush
        // into the screen edge on a narrow phone.
        [SerializeField] private float boardPadding = 40f;

        // One image behind the whole board, carrying the grid for the current size: dark cells and
        // light lines baked into a single sprite. This is the ONLY thing that draws the grid --
        // cells used to draw their own lines as well, four thin rects each, and no longer do.
        [SerializeField] private Image boardGridImage;

        // Indexed by grid size, starting at GridSize_4X4, and every size in GridSize must have an
        // entry: a cell no longer has a fallback grid of its own to fall back to, so a missing
        // sprite means a board with no lines at all. A new grid size needs its art before it ships.
        [SerializeField] private Sprite[] gridSizeSprites;

        private const int SmallestGridSize = 4;
        //private List<Block> gridblocks;


        /// <summary>
        /// Generate blocks based on level data
        /// </summary>
        /// <param name="data">The LevelData containing grid size and grid rows</param>
        public void GenerateBoard(LevelData data)
        {
            int rowSize = (int)data.gridSize;
            int coloumSize = (int)data.gridSize;

            GamePlayController.Instance.InitGrid(rowSize, coloumSize);

            for (int i = 0; i < rowSize; i++)
            {
                for (int j = 0; j < coloumSize; j++)
                {
                    Block block = Instantiate(blockPrefab, BoardArea);
                    block.gameObject.name = "Block_" + i + " " + j;

                    PairColorType colorType = data.gridRows[i].coloum[j];
                    int[] pairIds = data.gridRows[i].pairId;
                    BlockType[] blockTypes = data.gridRows[i].blockType;
                    int[] wallMasks = data.gridRows[i].wallMask;
                    Direction[] requiredEntryDirections = data.gridRows[i].requiredEntryDirection;
                    Direction[] forcedExitDirections = data.gridRows[i].forcedExitDirection;
                    int[] secondPairIds = data.gridRows[i].secondPairId;
                    int[] thirdPairIds = data.gridRows[i].thirdPairId;
                    int[] fourthPairIds = data.gridRows[i].fourthPairId;

                    // explicit pairId wins (needed for >9 simultaneous pairs); otherwise fall
                    // back to the color's own value so existing hand-authored levels (which
                    // never set pairId) behave exactly as before
                    int pairId = (pairIds != null && j < pairIds.Length && pairIds[j] != 0)
                        ? pairIds[j]
                        : (int)colorType;

                    BlockType blockType = (blockTypes != null && j < blockTypes.Length)
                        ? blockTypes[j]
                        : BlockType.Normal;

                    int wallMask = (wallMasks != null && j < wallMasks.Length) ? wallMasks[j] : 0;

                    Direction requiredEntryDirection = (requiredEntryDirections != null && j < requiredEntryDirections.Length)
                        ? requiredEntryDirections[j]
                        : Direction.None;

                    Direction forcedExitDirection = (forcedExitDirections != null && j < forcedExitDirections.Length)
                        ? forcedExitDirections[j]
                        : Direction.None;

                    int secondPairId = (secondPairIds != null && j < secondPairIds.Length)
                        ? secondPairIds[j]
                        : 0;

                    // levels authored before these columns existed simply have no array here
                    int thirdPairId = (thirdPairIds != null && j < thirdPairIds.Length)
                        ? thirdPairIds[j]
                        : 0;

                    int fourthPairId = (fourthPairIds != null && j < fourthPairIds.Length)
                        ? fourthPairIds[j]
                        : 0;

                    block.SetBlock(colorType, pairId, secondPairId, thirdPairId, fourthPairId, blockType, wallMask, requiredEntryDirection, forcedExitDirection, i, j);

                    GamePlayController.Instance.grid[i, j] = block;
                }
            }

            NormalizeWalls(rowSize, coloumSize);

            // Placement is a second pass: every block has to exist before the board can be
            // measured and laid out, and LayoutBoard is the same code path a screen resize
            // goes through, so there is only one copy of the arithmetic.
            LayoutBoard();

            GamePlayController.Instance.ValidateLevelData();
            GamePlayController.Instance.GameState = Enums.GameState.Playing;
        }

        /// <summary>
        /// The grid sprite for an n x n board, or null when there is not one -- which is the signal
        /// to let the cells draw their own lines instead.
        /// </summary>
        private Sprite GridSpriteFor(int gridSize)
        {
            if (gridSizeSprites == null) { return null; }

            int index = gridSize - SmallestGridSize;
            if (index < 0 || index >= gridSizeSprites.Length) { return null; }

            return gridSizeSprites[index];
        }

        /// <summary>
        /// Sizes the board image to the grid the cells actually occupy -- NOT to the board area.
        /// The two are different: the area is padded, and cell size is snapped down to whole units,
        /// so the drawn grid is a little smaller than the space available and sits centred in it.
        /// Stretching the sprite over the area instead would put its baked lines slightly off from
        /// every cell boundary, and the error grows with the board.
        /// </summary>
        private void LayoutBoardGrid(float blockSize, int rowSize, int coloumSize)
        {
            if (boardGridImage == null) { return; }

            Sprite sprite = GridSpriteFor(rowSize);
            if (sprite == null)
            {
                boardGridImage.gameObject.SetActive(false);
                return;
            }

            boardGridImage.gameObject.SetActive(true);
            boardGridImage.sprite = sprite;
            boardGridImage.rectTransform.sizeDelta =
                new Vector2(blockSize * coloumSize, blockSize * rowSize);
        }

        /// <summary>
        /// A wall belongs to the edge between two cells, but wallMask is stored per cell, so
        /// level data can declare one side and not the other. Movement already handles that --
        /// GetDirection refuses the crossing if *either* side declares it -- but the art does
        /// not: only the declaring cell draws a bar, so the same wall looks solid from one side
        /// and open from the other. Mirroring at load lets a level be authored either way and
        /// still look right from both.
        /// </summary>
        private void NormalizeWalls(int rowSize, int coloumSize)
        {
            Block[,] grid = GamePlayController.Instance.grid;
            if (grid == null) { return; }

            Direction[] edges = { Direction.Left, Direction.Right, Direction.Up, Direction.Down };

            for (int i = 0; i < rowSize; i++)
            {
                for (int j = 0; j < coloumSize; j++)
                {
                    Block block = grid[i, j];
                    if (block == null) { continue; }

                    for (int e = 0; e < edges.Length; e++)
                    {
                        if (!block.HasWall(edges[e])) { continue; }

                        int r = i, c = j;
                        switch (edges[e])
                        {
                            case Direction.Left: c--; break;
                            case Direction.Right: c++; break;
                            case Direction.Up: r--; break;
                            case Direction.Down: r++; break;
                        }

                        if (r < 0 || r >= rowSize || c < 0 || c >= coloumSize) { continue; }
                        if (grid[r, c] != null) { grid[r, c].AddWall(OppositeEdge(edges[e])); }
                    }
                }
            }
        }

        private static Direction OppositeEdge(Direction dir)
        {
            switch (dir)
            {
                case Direction.Left: return Direction.Right;
                case Direction.Right: return Direction.Left;
                case Direction.Up: return Direction.Down;
                case Direction.Down: return Direction.Up;
                default: return Direction.None;
            }
        }

        /// <summary>
        /// Sizes and positions every block from the square play area's current rect. The
        /// AspectRatioFitter has already reduced "how much room is there" to a single number --
        /// a tall phone leaves a square limited by width, a tablet one limited by height --
        /// so cell size is just that width divided by the grid. This is the only place cell
        /// size is decided; it used to be a per-level authored number and then a hardcoded
        /// fraction of the 1080-unit reference width, neither of which knew anything about the
        /// space actually available.
        /// </summary>
        private void LayoutBoard()
        {
            Block[,] grid = GamePlayController.Instance.grid;
            if (grid == null) { return; }

            int rowSize = GamePlayController.Instance.gridRow;
            int coloumSize = GamePlayController.Instance.gridCol;
            if (rowSize <= 0 || coloumSize <= 0) { return; }

            // Two rebuilds, outer first: the layout group above may not have run yet on the
            // frame a level loads, and the fitter only reacts to its parent's new size after
            // that. Rebuilding just the group would leave the square a frame behind, since the
            // fitter's own response is normally deferred to the end of frame.
            RectTransform layoutRoot = thisTransform.parent as RectTransform;
            if (layoutRoot != null)
            {
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(layoutRoot);
            }
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(BoardArea);

            float usable = BoardArea.rect.width - (boardPadding * 2f);
            if (usable <= 0f) { return; }

            int maxBlockInRowCol = rowSize > coloumSize ? rowSize : coloumSize;

            // Snapped to the largest EVEN whole number of units that fits. Cells butt against each
            // other, so a fractional cell size puts every boundary on a fractional coordinate, and
            // anything centred on that boundary -- a wall bar, and the board sprite's own baked
            // lines once it is stretched to this size -- straddles half pixels, rasterising 4 px on
            // one seam and 5 px on the next. Even, so that half a cell is a whole number too and
            // the centred start position below stays on the integer lattice.
            float blockSize = Mathf.Floor(usable / maxBlockInRowCol * 0.5f) * 2f;
            if (blockSize < 2f) { blockSize = 2f; }

            // centre the grid in the board area; row 0 is the top row
            float startPointX = -((blockSize * coloumSize) / 2f) + (blockSize / 2f);
            float startPointY = ((blockSize * rowSize) / 2f) - (blockSize / 2f);

            LayoutBoardGrid(blockSize, rowSize, coloumSize);

            for (int i = 0; i < rowSize; i++)
            {
                for (int j = 0; j < coloumSize; j++)
                {
                    Block block = grid[i, j];
                    if (block == null) { continue; }

                    RectTransform blockRect = (RectTransform)block.transform;
                    blockRect.sizeDelta = new Vector2(blockSize, blockSize);
                    blockRect.localPosition = new Vector3(startPointX + (blockSize * j),
                                                          startPointY - (blockSize * i),
                                                          0f);
                }
            }
        }

        /// <summary>
        /// Re-lays the board when the area it lives in changes size -- a device rotation, or a
        /// resized player window. Nothing needs regenerating, only measuring again.
        /// </summary>
        private void OnRectTransformDimensionsChange()
        {
            if (thisTransform == null || GamePlayController.Instance == null) { return; }
            LayoutBoard();
        }

        /// <summary>
        /// The square play area, falling back to the board area itself if the child was never
        /// wired up -- blocks laid out in a slightly non-square area beats a null reference.
        /// </summary>
        private RectTransform BoardArea
        {
            get { return boardArea != null ? boardArea : thisTransform; }
        }

        /// <summary>
        /// Clears the board by destroying every cell. A board is built fresh each time rather than
        /// recycled: the cells were pooled, which meant every visual a mechanic touches had to be
        /// put back by hand before the object could be handed to the next level, and a single
        /// missed field showed up as a mechanic bleeding across levels. A new board is 25-64 UI
        /// objects on a level change, which is not a cost worth that class of bug.
        /// </summary>
        public void ResetBoard()
        {
            GamePlayController.Instance.ResetBlocks();
        }
    }
}
