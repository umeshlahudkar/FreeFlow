using FreeFlow.Enums;
using FreeFlow.UI;
using FreeFlow.Util;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
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

        private List<Block> selectedBlocks;
        private Dictionary<int, List<Block>> completedPairs;

        private EventSystem eventSystem;
        private List<RaycastResult> raycastResults;
        private PointerEventData eventData;

        public Block[,] grid;
        public int gridRow;
        public int gridCol;
        private Block[] highlightedBlock = new Block[2];

        [SerializeField] private PairColorDataSO PairColorDataSO;
        [SerializeField] private Image touchPointer;

        private int moves;

        private PairConstraint[] pairConstraints;

        // Which direction (if any) is showing a live, not-yet-committed drag-progress
        // preview, and the block it's drawn on. The block has to be tracked too: the preview
        // lives on whichever cell was last when it was drawn, and a committed step moves
        // "last" on to the next cell before the preview gets cleared. Clearing by direction
        // alone then wipes that direction on the NEW last block -- which never had a preview --
        // and strands the real one on the previous cell for the rest of the level.
        private Direction activePreviewDirection = Direction.None;
        private Block activePreviewBlock;

        private void Start()
        {
            isClicked = false;
            hasSelectExistingFromLast = false;
            hasSelectExistingFromMiddle = false;

            selectedBlocks = new List<Block>();
            completedPairs = new Dictionary<int, List<Block>>();

            eventSystem = EventSystem.current;
            raycastResults = new List<RaycastResult>();
            eventData = new PointerEventData(eventSystem);

            moves = 0;

            // must stay off: if enabled, this Image sits over the grid every frame during a
            // drag and the raycast in OnPointerMoved/OnPointerUp would hit it instead of the
            // Block underneath, stalling the drag entirely.
            touchPointer.raycastTarget = false;
        }

        public void InitGrid(int row, int col)
        {
            gridRow = row;
            gridCol = col;
            grid = new Block[gridRow, gridCol];
        }

        /// <summary>
        /// Per-pair path-length requirements for the level currently being generated.
        /// Pass null/empty when the level has none.
        /// </summary>
        public void SetLevelConstraints(PairConstraint[] constraints)
        {
            pairConstraints = constraints;
        }

        /// <summary>
        /// Verifies every pair-block's PairId appears exactly twice on the current grid.
        /// Call once the grid is fully populated. Logs an error per malformed id rather than
        /// throwing, since bad level content shouldn't crash the game outright.
        /// </summary>
        public void ValidateLevelPairs()
        {
            Dictionary<int, int> pairIdCounts = new Dictionary<int, int>();

            for (int i = 0; i < gridRow; i++)
            {
                for (int j = 0; j < gridCol; j++)
                {
                    Block block = grid[i, j];
                    if (block.IsPairBlock)
                    {
                        int id = block.PairId;
                        pairIdCounts[id] = pairIdCounts.TryGetValue(id, out int existing) ? existing + 1 : 1;
                    }
                }
            }

            foreach (var kvp in pairIdCounts)
            {
                if (kvp.Value != 2)
                {
                    Debug.LogError("FreeFlow level data error: pair id " + kvp.Key + " appears " + kvp.Value + " time(s) on this board, expected exactly 2.");
                }
            }
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
                    if (block.IsPairBlock && completedPairs.ContainsKey(block.PairId))
                    {
                        List<Block> blocks = completedPairs[block.PairId];
                        foreach (Block b in blocks)
                        {
                            b.ResetAllHighlightDirection(block.PairId);
                        }
                        completedPairs.Remove(block.PairId);

                        isClicked = true;
                        selectedBlocks.Add(block);

                    }

                    //if click on pair dot and block is not clicked before
                    else if (block.IsPairBlock && !selectedBlocks.Contains(block))
                    {
                        isClicked = true;
                        selectedBlocks.Add(block);

                        //HighlightSelectedColorTypeBlock(block);
                    }

                    // if the some blocks of pair highlighted, and clicked on the highlighted block
                    else if (completedPairs.ContainsKey(block.HighlightedPairId))
                    {
                        List<Block> blocks = completedPairs[block.HighlightedPairId];

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

                        completedPairs.Remove(block.HighlightedPairId);
                        //selectedBlocks.Add(block);
                        //HighlightSelectedColorTypeBlock(block);
                    }

                    if(isClicked)
                    {
                        AudioManager.Instance.PlayBlockSelectSound();

                        HighlightSelectedColorTypeBlock(block);
                        Color clr = (block.IsPairBlock ? GetColor(block.PairColorType) : GetColor(block.HighlightedColorType));
                        MoveTouchPointer(UnityEngine.Input.mousePosition);
                        SetTouchPointerImage(clr);
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

                MoveTouchPointer(UnityEngine.Input.mousePosition);

                if (raycastResults.Count > 0)
                {
                    Block block = raycastResults[0].gameObject.GetComponent<Block>();

                    // selected block is not select again, check for the new block
                    if(CanSelectToAdd(block))
                    {
                        Direction dir = GetDirection(selectedBlocks[selectedBlocks.Count - 1], block);

                        if (dir != Direction.None)
                        {
                            ProcessBlockStep(block, dir);
                        }
                        else
                        {
                            // fast swipes can raycast a cell that isn't exactly adjacent to the last
                            // selected block; walk the straight-line path between them instead of
                            // silently dropping the step
                            List<Block> path = GetStraightLinePath(selectedBlocks[selectedBlocks.Count - 1], block);
                            if (path != null)
                            {
                                foreach (Block cell in path)
                                {
                                    if (!CanSelectToAdd(cell)) { break; }

                                    Direction stepDir = GetDirection(selectedBlocks[selectedBlocks.Count - 1], cell);
                                    if (stepDir == Direction.None) { break; }

                                    ProcessBlockStep(cell, stepDir);
                                }
                            }
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
                    else if(block != null && selectedBlocks.Contains(block)
                        && !IsEqual(selectedBlocks[selectedBlocks.Count - 1], block))
                    {
                        // Pointer landed back on an earlier point already in the path -- could be
                        // exactly one step back, or several at once (e.g. a fast drag straight
                        // back toward the start skips over the in-between cells' raycasts
                        // entirely). Retreat all the way to wherever this block actually sits,
                        // not just the immediately-previous one, otherwise a multi-step jump back
                        // matches neither this branch's old exact Count-2 check nor the forward
                        // CanSelectToAdd branch, and the last selected block's bars are never
                        // reset -- they just keep getting live-updated against a pointer that's
                        // no longer anywhere near that cell, leaving them stuck on-screen.
                        int targetIndex = GetBlockIndex(selectedBlocks, block);
                        if (targetIndex != -1)
                        {
                            // Still selectedBlocks[Count-1] at this point, so this clears whatever
                            // direction the live preview (including an in-progress turn) was
                            // showing on it. ResetBlockToRemove below only clears COMMITTED bars
                            // (directionOwnerPairId == pairId) -- a preview that never got fully
                            // committed has no owner, so without this it's left stuck on-screen.
                            ClearDragPreview();
                            ResetBlockToRemove(selectedBlocks, targetIndex);
                        }
                    }
                }

                UpdateDragPreview();
            }
        }

        private void OnPointerUp()
        {
            ClearDragPreview();

            // The last selected block may have only just been entered when the pointer was
            // released -- its entry bar could still be sitting well under fully connected.
            // Logically the step is already committed either way, but visually that reads as
            // "half-drawn yet somehow highlighted as part of the path" once HighlightBlockBg
            // washes the whole cell below. Apply the same 50% rule used for a mid-cell turn:
            // below half-connected, treat the step as not really taken and undo it; at or
            // above half, snap it to fully connected so the visual matches the logical state.
            if (selectedBlocks.Count >= 2)
            {
                Block releasedLast = selectedBlocks[selectedBlocks.Count - 1];
                Block releasedPrev = selectedBlocks[selectedBlocks.Count - 2];
                Direction entryDir = GetDirection(releasedLast, releasedPrev);

                if (entryDir != Direction.None)
                {
                    if (releasedLast.GetDirectionFillAmount(entryDir) < 0.5f)
                    {
                        // releasedPrev.PairId is that BLOCK's own static level-data id -- 0 for
                        // any non-dot cell. The bar was actually marked owned under the pair
                        // that's dragging (selectedBlocks[0].PairId), so resetting by
                        // releasedPrev.PairId silently matches nothing on a mid-path cell and
                        // leaves the bar stuck.
                        Direction forwardDir = GetDirection(releasedPrev, releasedLast);
                        releasedLast.ResetAllHighlightDirection(selectedBlocks[0].PairId);
                        if (forwardDir != Direction.None)
                        {
                            releasedPrev.ResetHighlightDirection(forwardDir);
                        }
                        selectedBlocks.RemoveAt(selectedBlocks.Count - 1);
                    }
                    else
                    {
                        releasedLast.SetDirectionFillAmount(entryDir, 1f);
                    }
                }
            }

            if (selectedBlocks.Count > 0)
            {
                AddSelectedBlocksToCompletedPairs();

                if (selectedBlocks.Count > 1)
                {
                    moves++;
                    UIController.Instance.UpdateMovesCount(moves);
                }

                if (selectedBlocks.Count > 1)
                {
                    for (int i = 0; i < selectedBlocks.Count; i++)
                    {
                        selectedBlocks[i].HighlightBlockBg();
                    }
                }

                if (IsPathFullyComplete(selectedBlocks))
                {
                    AudioManager.Instance.PlayPairCompleteSound();
                }
            }

            ResetTouchPointer();
            ResetHighlightSelectedColorTypeBlock();

            isClicked = false;
            hasSelectExistingFromMiddle = false;
            hasSelectExistingFromLast = false;
            selectedBlocks.Clear();

            int count = GetPairCompleteCount();
            UIController.Instance.UpdatePairCount(count);
            RefreshGateVisuals();

            if (count >= UIController.Instance.CurrentLevelGoal)
            {
                GameState = GameState.Ending;
                UIController.Instance.ActivateLevelCompleteScreen(moves);
                SaveLevelData();
            }
        }

        private void SaveLevelData()
        {
            SaveData data = SavingSystem.Instance.Load();
            int currentLevel = UIController.Instance.CurrentLevel;
            int totalLevelCount = UIController.Instance.TotalLevelCount;

            // sized once to the total level count, rather than growing by one slot (and
            // copying the whole array) on every single level completion
            if (data.completedlevelMoves == null || data.completedlevelMoves.Length < totalLevelCount)
            {
                int[] resized = new int[totalLevelCount];
                if (data.completedlevelMoves != null)
                {
                    System.Array.Copy(data.completedlevelMoves, resized, data.completedlevelMoves.Length);
                }
                data.completedlevelMoves = resized;
            }

            data.completedlevelMoves[currentLevel - 1] = moves;

            if (currentLevel > data.completedLevel)
            {
                data.completedLevel = currentLevel;
            }

            SavingSystem.Instance.Save(data);
        }

        private void HighlightSelectedColorTypeBlock(Block selectedBlock)
        {
            highlightedBlock[0] = null;
            highlightedBlock[1] = null;

            if(selectedBlock.IsPairBlock)
            {
                highlightedBlock[0] = selectedBlock;
                bool blockFound = false;
                for (int i = 0; i < gridRow; i++)
                {
                    for (int j = 0; j < gridCol; j++)
                    {
                        if (grid[i, j].IsPairBlock && grid[i, j] != selectedBlock && grid[i, j].PairId == selectedBlock.PairId)
                        {
                            highlightedBlock[1] = grid[i, j];
                            blockFound = true;
                            break;
                        }
                    }
                    if (blockFound) { break; }
                }
            }
            else
            {
                int blockCount = 0;
                for (int i = 0; i < gridRow; i++)
                {
                    for (int j = 0; j < gridCol; j++)
                    {
                        if (grid[i, j].IsPairBlock && grid[i, j].PairId == selectedBlock.HighlightedPairId)
                        {
                            blockCount++;
                            highlightedBlock[blockCount-1] = grid[i, j];
                            if (blockCount >= 2) { break; }
                        }
                    }
                    if (blockCount >= 2) { break; }
                }
            }

            // guarded: level-data validation (ValidateLevelPairs) should catch a color/id
            // appearing without its partner before play starts, but don't NRE on it here
            if (highlightedBlock[0] != null) { highlightedBlock[0].HighlightBlock(); }
            if (highlightedBlock[1] != null) { highlightedBlock[1].HighlightBlock(); }
        }

        private void ResetHighlightSelectedColorTypeBlock()
        {
            if(isClicked)
            {
                if (highlightedBlock[0] != null) { highlightedBlock[0].ResetHighlightBlock(); }
                if (highlightedBlock[1] != null) { highlightedBlock[1].ResetHighlightBlock(); }
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

            if (!block.CanEnter(selectedBlocks[0].PairId))
            {
                return false;
            }

            bool isPairBlockComplete = selectedBlocks[0].IsPairBlock && IsPairComplete(selectedBlocks[0], selectedBlocks[selectedBlocks.Count - 1]);

            if (!isPairBlockComplete)
            {
                if
                (
                    (!block.IsPairBlock) ||
                    (block.IsPairBlock && block.PairId == selectedBlocks[0].PairId) ||
                    (block.IsPairBlock && block.PairId == selectedBlocks[0].HighlightedPairId) ||
                    (!block.IsPairBlock && hasSelectExistingFromLast) ||
                    (block.IsPairBlock && hasSelectExistingFromLast && block.PairId == selectedBlocks[0].HighlightedPairId)
                )
                {
                    if ((hasSelectExistingFromLast || hasSelectExistingFromMiddle) && IsHighlightedPairComplete(selectedBlocks[0], selectedBlocks[selectedBlocks.Count - 1]))
                    {
                        return false;
                    }

                    if(completedPairs.ContainsKey(block.HighlightedPairId) && completedPairs[block.HighlightedPairId].Contains(block) && block.HighlightedPairId == selectedBlocks[0].HighlightedPairId)
                    {
                        return false;
                    }

                    return true;
                }
            }

            return false;
        }


        /// <summary>
        /// Determines the direction of block2 with respect to block1. Returns None both when
        /// they aren't adjacent AND when they are adjacent but the move is mechanically
        /// disallowed (a wall on the shared edge, or block2 is a one-way cell requiring a
        /// different entry direction) -- callers already treat "None" as "can't step there,"
        /// so folding these checks in here means nothing downstream needs to know about walls
        /// or one-way cells at all.
        /// </summary>
        /// <param name="block1">The first block.</param>
        /// <param name="block2">The second block.</param>
        /// <returns>The direction of block2(Right, Left, Up, Down) from block1, or None if they are not adjacent or the move is disallowed.</returns>
        private Direction GetDirection(Block block1, Block block2)
        {
            Direction dir = Direction.None;

            if (block1.Row_ID == block2.Row_ID && block1.Coloum_ID < block2.Coloum_ID && block2.Coloum_ID - block1.Coloum_ID == 1)
            {
                dir = Direction.Right;
            }
            else if (block1.Row_ID == block2.Row_ID && block1.Coloum_ID > block2.Coloum_ID && block1.Coloum_ID - block2.Coloum_ID == 1)
            {
                dir = Direction.Left;
            }
            else if (block1.Coloum_ID == block2.Coloum_ID && block1.Row_ID > block2.Row_ID && block1.Row_ID - block2.Row_ID == 1)
            {
                dir = Direction.Up;
            }
            else if (block1.Coloum_ID == block2.Coloum_ID && block1.Row_ID < block2.Row_ID && block2.Row_ID - block1.Row_ID == 1)
            {
                dir = Direction.Down;
            }

            if (dir == Direction.None)
            {
                return Direction.None;
            }

            if (block1.HasWall(dir) || block2.HasWall(OppositeDirection(dir)))
            {
                return Direction.None;
            }

            if (!block2.CanEnterFrom(dir))
            {
                return Direction.None;
            }

            return dir;
        }

        private Direction OppositeDirection(Direction dir)
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
        /// Applies one drag step onto <paramref name="block"/>: resolves cell-stealing against
        /// another highlighted pair, highlights both ends in <paramref name="dir"/>, and appends
        /// the block to <see cref="selectedBlocks"/>. Shared by the single-adjacent-cell case and
        /// the multi-cell interpolation used when a fast swipe skips over intermediate cells.
        /// </summary>
        private void ProcessBlockStep(Block block, Direction dir)
        {
            // checks for the selected block is intersect with the another highlighted blocks (completed or incompleted highlighted pair)
            // -- Mixed cells are exempt: they're meant to be shared, not stolen from another pair
            if (block.BlockType != BlockType.Mixed && completedPairs.ContainsKey(block.HighlightedPairId) && block.HighlightedPairId != selectedBlocks[0].HighlightedPairId)
            {
                List<Block> blocks = completedPairs[block.HighlightedPairId];

                int indexToRemove = GetBlockIndex(blocks, block);
                indexToRemove--;
                if (indexToRemove <= -1) { indexToRemove = -1; }

                if (indexToRemove != -1)
                {
                    ResetBlockToRemove(blocks, indexToRemove);
                }
            }

            PairColorType type;
            int pairId;
            GetCurrentDragColorAndPairId(out type, out pairId);

            Block oldLast = selectedBlocks[selectedBlocks.Count - 1];

            // The block we're leaving stops receiving live entry-fill updates the moment it's
            // no longer the last selected block, so snap its own entry edge (if any) to fully
            // connected now -- normally it's already ~1 from the live per-frame tracking, but
            // a sharp turn can trigger this step before the pointer passed dead center.
            if (selectedBlocks.Count >= 2)
            {
                Direction oldEntryDir = GetDirection(oldLast, selectedBlocks[selectedBlocks.Count - 2]);
                if (oldEntryDir != Direction.None)
                {
                    oldLast.SetDirectionFillAmount(oldEntryDir, 1f);
                }
            }

            //highlighting last selected block
            oldLast.HighlightBlockDirection(dir, type, pairId);

            //highlight new selected block -- growFromFarEdge: this is the cell being entered,
            //so its bar should fill from the shared edge inward, not from its own center outward
            switch (dir)
            {
                case Direction.Left:
                    block.HighlightBlockDirection(Direction.Right, type, pairId, growFromFarEdge: true);
                    break;

                case Direction.Right:
                    block.HighlightBlockDirection(Direction.Left, type, pairId, growFromFarEdge: true);
                    break;

                case Direction.Up:
                    block.HighlightBlockDirection(Direction.Down, type, pairId, growFromFarEdge: true);
                    break;

                case Direction.Down:
                    block.HighlightBlockDirection(Direction.Up, type, pairId, growFromFarEdge: true);
                    break;
            }

            selectedBlocks.Add(block);
        }

        /// <summary>
        /// Resolves which pair color/id the currently active drag is drawing with -- the
        /// same rule ProcessBlockStep uses to color a newly committed step, reused by the
        /// live drag preview so the preview bar matches the color it'll commit as.
        /// </summary>
        private void GetCurrentDragColorAndPairId(out PairColorType type, out int pairId)
        {
            type = hasSelectExistingFromLast ? selectedBlocks[selectedBlocks.Count - 1].HighlightedColorType : selectedBlocks[0].PairColorType;
            pairId = hasSelectExistingFromLast ? selectedBlocks[selectedBlocks.Count - 1].HighlightedPairId : selectedBlocks[0].PairId;
            if (hasSelectExistingFromMiddle)
            {
                type = selectedBlocks[selectedBlocks.Count - 1].HighlightedColorType;
                pairId = selectedBlocks[selectedBlocks.Count - 1].HighlightedPairId;
            }
        }

        /// <summary>
        /// Grows the last selected block's bar toward whichever neighbor the pointer is
        /// currently heading for, proportional to how far across that cell it's travelled --
        /// a live preview of the step that ProcessBlockStep will commit once the pointer
        /// actually lands on the neighbor. Purely visual: never touches selectedBlocks or
        /// completedPairs.
        /// </summary>
        private void UpdateDragPreview()
        {
            UpdateDragPreview(UnityEngine.Input.mousePosition);
        }

        /// <summary>
        /// Testable core of the live drag preview: takes the pointer's screen position
        /// explicitly instead of reading Input.mousePosition directly, so the geometry can
        /// be exercised deterministically without real hardware input.
        /// </summary>
        private void UpdateDragPreview(Vector2 pointerScreenPosition)
        {
            if (selectedBlocks.Count == 0) { return; }

            Block lastBlock = selectedBlocks[selectedBlocks.Count - 1];
            RectTransform lastRect = lastBlock.transform as RectTransform;
            if (lastRect == null) { return; }

            Vector3 blockScreenCenter = RectTransformUtility.WorldToScreenPoint(null, lastRect.position);
            Vector3 edgeWorld = lastRect.TransformPoint(new Vector3(lastRect.rect.width / 2f, 0f, 0f));
            Vector3 edgeScreen = RectTransformUtility.WorldToScreenPoint(null, edgeWorld);
            float cellHalfSizeScreen = Vector3.Distance(blockScreenCenter, edgeScreen);

            Vector2 offset = pointerScreenPosition - (Vector2)blockScreenCenter;

            // entryDir/entryFraction track the edge this cell was entered through, growing
            // from 0 (just crossed the edge) to 1 (reached this cell's center) as the pointer
            // moves -- replaces the old fixed-duration commit tween, which finished on its own
            // schedule regardless of where the pointer actually was. Defaults to "no entry
            // edge, already complete" for the very first block of a drag (the starting dot).
            Direction entryDir = Direction.None;
            float entryFraction = 1f;
            if (selectedBlocks.Count >= 2 && cellHalfSizeScreen > 0.01f)
            {
                entryDir = GetDirection(lastBlock, selectedBlocks[selectedBlocks.Count - 2]);
                if (entryDir != Direction.None)
                {
                    entryFraction = ComputeEdgeToCenterFraction(entryDir, offset, cellHalfSizeScreen);
                }
            }

            Direction candidate = Direction.None;
            float fraction = 0f;

            if (cellHalfSizeScreen > 0.01f)
            {
                if (entryDir != Direction.None && entryFraction < 1f)
                {
                    // Still connecting from the entry edge: a perpendicular direction only
                    // counts as a deliberate turn once the pointer has pushed more than halfway
                    // from center toward that edge. Below that, ordinary wobble while still
                    // approaching this cell's center would otherwise flash an unrelated bar
                    // (e.g. Down) even though the drag is really just moving straight through.
                    // Once it crosses that point, finish the entry edge immediately and switch
                    // to filling the turn, rather than blending the two together.
                    bool enteredHorizontally = entryDir == Direction.Left || entryDir == Direction.Right;
                    float turnAxisOffset = enteredHorizontally ? offset.y : offset.x;
                    float turnFraction = Mathf.Clamp01(Mathf.Abs(turnAxisOffset) / cellHalfSizeScreen);

                    if (turnFraction > 0.5f)
                    {
                        entryFraction = 1f;
                        candidate = enteredHorizontally
                            ? (offset.y > 0 ? Direction.Up : Direction.Down)
                            : (offset.x > 0 ? Direction.Right : Direction.Left);
                        fraction = turnFraction;
                    }
                }
                else
                {
                    // No entry edge to protect (starting dot) or already past this cell's
                    // center -- normal dominant-axis preview toward the next step.
                    if (Mathf.Abs(offset.x) >= Mathf.Abs(offset.y))
                    {
                        candidate = offset.x > 0 ? Direction.Right : Direction.Left;
                        fraction = Mathf.Abs(offset.x) / cellHalfSizeScreen;
                    }
                    else
                    {
                        candidate = offset.y > 0 ? Direction.Up : Direction.Down;
                        fraction = Mathf.Abs(offset.y) / cellHalfSizeScreen;
                    }
                    fraction = Mathf.Clamp01(fraction);
                }
            }

            if (entryDir != Direction.None)
            {
                lastBlock.SetDirectionFillAmount(entryDir, entryFraction);
            }

            Block neighbor = candidate != Direction.None ? GetNeighbor(lastBlock, candidate) : null;
            bool candidateIsLegal = neighbor != null
                && CanSelectToAdd(neighbor)
                && GetDirection(lastBlock, neighbor) == candidate;

            if (!candidateIsLegal)
            {
                candidate = Direction.None;
                fraction = 0f;
            }

            // Clear the outgoing preview whenever EITHER the direction or the block it sits
            // on has moved on. Testing the direction alone misses the case where the drag
            // commits a step and then keeps heading the same way -- candidate is unchanged, so
            // the stale bar on the previous cell would never be taken down. Committed bars are
            // safe from this: SetDirectionPreview ignores any slot that already has an owner.
            if (activePreviewBlock != null
                && (activePreviewBlock != lastBlock || activePreviewDirection != candidate))
            {
                activePreviewBlock.SetDirectionPreview(activePreviewDirection, 0f, PairColorType.None);
            }

            if (candidate != Direction.None)
            {
                PairColorType type;
                int pairId;
                GetCurrentDragColorAndPairId(out type, out pairId);
                lastBlock.SetDirectionPreview(candidate, fraction, type);
            }

            activePreviewDirection = candidate;
            activePreviewBlock = candidate != Direction.None ? lastBlock : null;
        }

        /// <summary>
        /// Fraction (0-1) of how far the pointer has travelled from the cell edge facing
        /// <paramref name="entryDir"/> toward this cell's center: 0 right at the edge (just
        /// crossed into the cell), 1 once the pointer reaches the center (fully connected).
        /// <paramref name="offsetFromCenter"/> and <paramref name="cellHalfSizeScreen"/> are
        /// the same screen-space values UpdateDragPreview already computes for the outgoing
        /// candidate, reused here for the incoming edge instead.
        /// </summary>
        private float ComputeEdgeToCenterFraction(Direction entryDir, Vector2 offsetFromCenter, float cellHalfSizeScreen)
        {
            float axisValue;
            float edgeSign;

            switch (entryDir)
            {
                case Direction.Right: axisValue = offsetFromCenter.x; edgeSign = 1f; break;
                case Direction.Left: axisValue = offsetFromCenter.x; edgeSign = -1f; break;
                case Direction.Up: axisValue = offsetFromCenter.y; edgeSign = 1f; break;
                case Direction.Down: axisValue = offsetFromCenter.y; edgeSign = -1f; break;
                default: return 1f;
            }

            return Mathf.Clamp01(1f - edgeSign * axisValue / cellHalfSizeScreen);
        }

        /// <summary>
        /// Clears any live preview left on the last selected block. Call before a drag ends
        /// or its "last block" reference is about to change/clear.
        /// </summary>
        private void ClearDragPreview()
        {
            if (activePreviewDirection == Direction.None || activePreviewBlock == null) { return; }

            activePreviewBlock.SetDirectionPreview(activePreviewDirection, 0f, PairColorType.None);
            activePreviewDirection = Direction.None;
            activePreviewBlock = null;
        }

        private Block GetNeighbor(Block from, Direction dir)
        {
            int r = from.Row_ID;
            int c = from.Coloum_ID;
            switch (dir)
            {
                case Direction.Left: c--; break;
                case Direction.Right: c++; break;
                case Direction.Up: r--; break;
                case Direction.Down: r++; break;
                default: return null;
            }
            if (r < 0 || r >= gridRow || c < 0 || c >= gridCol) { return null; }
            return grid[r, c];
        }

        /// <summary>
        /// Returns the cells strictly between <paramref name="from"/> and <paramref name="to"/>
        /// (exclusive of <paramref name="from"/>, inclusive of <paramref name="to"/>) when they
        /// share a row or column, in travel order. Returns null when they aren't aligned (e.g. a
        /// diagonal jump), since that can't be walked as a sequence of orthogonal grid steps.
        /// </summary>
        private List<Block> GetStraightLinePath(Block from, Block to)
        {
            List<Block> path = new List<Block>();

            if (from.Row_ID == to.Row_ID && from.Coloum_ID != to.Coloum_ID)
            {
                int step = to.Coloum_ID > from.Coloum_ID ? 1 : -1;
                for (int c = from.Coloum_ID + step; ; c += step)
                {
                    path.Add(grid[from.Row_ID, c]);
                    if (c == to.Coloum_ID) { break; }
                }
                return path;
            }

            if (from.Coloum_ID == to.Coloum_ID && from.Row_ID != to.Row_ID)
            {
                int step = to.Row_ID > from.Row_ID ? 1 : -1;
                for (int r = from.Row_ID + step; ; r += step)
                {
                    path.Add(grid[r, from.Coloum_ID]);
                    if (r == to.Row_ID) { break; }
                }
                return path;
            }

            return null;
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
            // index 0 of any stored path is always a dot, so this is the pair's own identity
            int pairId = blocks[0].PairId;

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
                    b.ResetAllHighlightDirection(pairId);
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
            completedPairs[selectedBlocks[0].PairId] = new List<Block>(selectedBlocks);
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
                if (IsPathFullyComplete(kvp.Value))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Whether the given pair's currently drawn path is a fully valid completion.
        /// The dependency-tracking hook a Gate cell checks before opening. Re-evaluates
        /// live from current path state, so a gate opens/re-locks immediately as its
        /// dependency pair's solved state changes.
        /// </summary>
        public bool IsPairSolved(int pairId)
        {
            return completedPairs.TryGetValue(pairId, out List<Block> path) && IsPathFullyComplete(path);
        }

        /// <summary>
        /// Updates every Gate cell's visual to reflect whether its dependency pair is
        /// currently solved. Called after any change to pair-completion state.
        /// </summary>
        private void RefreshGateVisuals()
        {
            for (int i = 0; i < gridRow; i++)
            {
                for (int j = 0; j < gridCol; j++)
                {
                    grid[i, j].RefreshGateVisual();
                }
            }
        }


        /// <summary>
        /// Checks whether a pair of blocks represents a complete pair
        /// </summary>
        /// <param name="b1">The first block of the pair.</param>
        /// <param name="b2">The second block of the pair.</param>
        /// <returns>True if the pair is complete, false otherwise.</returns>
        private bool IsPairComplete(Block b1, Block b2)
        {
            return (!IsEqual(b1, b2) && b1.IsPairBlock && b2.IsPairBlock && b1.PairId == b2.PairId);
        }

        private bool IsHighlightedPairComplete(Block b1, Block b2)
        {
            return (!IsEqual(b1, b2) && b1.HighlightedPairId == b2.PairId);
        }

        /// <summary>
        /// Whether a drawn path counts as a fully valid completion: its endpoints connect
        /// AND every checkpoint/length constraint for that pair is satisfied.
        /// </summary>
        private bool IsPathFullyComplete(List<Block> path)
        {
            if (path.Count == 0 || !IsPairComplete(path[0], path[path.Count - 1]))
            {
                return false;
            }

            int pairId = path[0].PairId;
            return PathSatisfiesCheckpoints(path, pairId) && PathSatisfiesLength(path, pairId);
        }

        /// <summary>
        /// Every grid cell marked BlockType.Checkpoint for this pairId must be part of the path.
        /// </summary>
        private bool PathSatisfiesCheckpoints(List<Block> path, int pairId)
        {
            for (int i = 0; i < gridRow; i++)
            {
                for (int j = 0; j < gridCol; j++)
                {
                    Block cell = grid[i, j];
                    if (cell.BlockType == BlockType.Checkpoint && cell.PairId == pairId && !path.Contains(cell))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private bool PathSatisfiesLength(List<Block> path, int pairId)
        {
            int requiredLength = GetRequiredPathLength(pairId);
            return requiredLength <= 0 || path.Count == requiredLength;
        }

        /// <summary>
        /// The exact path length required for this pairId to count as complete, or 0 if
        /// that pair has no length constraint. Public so Block can show it on the dot.
        /// </summary>
        public int GetRequiredPathLength(int pairId)
        {
            if (pairConstraints == null) { return 0; }

            for (int i = 0; i < pairConstraints.Length; i++)
            {
                if (pairConstraints[i].pairId == pairId) { return pairConstraints[i].requiredPathLength; }
            }
            return 0;
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

        private void SetTouchPointerImage(Color color)
        {
            touchPointer.gameObject.SetActive(true);
            color.a = touchPointer.color.a;
            touchPointer.color = color;
        }

        private void ResetTouchPointer()
        {
            touchPointer.gameObject.SetActive(false);
        }

        private void MoveTouchPointer(Vector3 position)
        {
            touchPointer.transform.position = position;
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
            pairConstraints = null;

            isClicked = false;
            hasSelectExistingFromLast = false;
            hasSelectExistingFromMiddle = false;
        }

        public void ResetBlocks(ObjectPool<Block> blockPool)
        {
            if(grid != null)
            {
                for (int i = 0; i < gridRow; i++)
                {
                    for (int j = 0; j < gridCol; j++)
                    {
                        grid[i, j].ResetBlock();
                        blockPool.ReturnObject(grid[i, j]);
                    }
                }
                grid = null;
            }
        }
    }
}
