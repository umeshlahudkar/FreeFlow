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

        // The cell a rejected drag is currently blinking, so a pointer sitting still over a
        // blocked/restricted cell keeps blinking it on a loop instead of restarting the tween
        // every frame. Cleared -- and the blink explicitly stopped -- the moment the pointer
        // lands anywhere else, so the blink never outlives the touch that triggered it.
        private Block invalidFeedbackBlock;

        // Checkpoints currently blinking because the board is full but their colour is not
        // on them. Separate from invalidFeedbackBlock, which is one cell and lives only for
        // the duration of a drag: this is board state, and can be several cells at once.
        private readonly List<Block> unmetCheckpointBlocks = new List<Block>();

        // Set only while invalidFeedbackBlock is blinking a WALL rather than a cell: the block on
        // the other side of that wall, and invalidFeedbackBlock's own edge direction, so the
        // matching blink on both sides of the wall (see FlashInvalidStep) can be stopped together.
        private Block invalidFeedbackWallOther;
        private Direction invalidFeedbackWallDir;

        // Every pair's drawn path, as a SET of segments rather than one list: the player can
        // start from either dot, so a pair can hold two half-drawn segments before they meet. The
        // old shape was Dictionary<int, List<Block>> assigned wholesale on commit, so the second
        // one did not add -- it silently replaced the first.
        //
        // A segment always starts at one of its pair's dots (OnPointerDown only ever begins a
        // selection at a dot, and trimming only ever shortens from the far end), which is what
        // gives each branch a stable identity to be replaced or cleared by.
        private Dictionary<int, List<List<Block>>> pairSegments;

        private EventSystem eventSystem;
        private List<RaycastResult> raycastResults;
        private PointerEventData eventData;

        public Block[,] grid;
        public int gridRow;
        public int gridCol;
        // Dots scaled up when the player grabs a pair.
        private List<Block> highlightedBlock = new List<Block>();

        [SerializeField] private PairColorDataSO PairColorDataSO;
        [SerializeField] private Image touchPointer;

        private int moves;

        /// <summary>Unscaled time the current attempt began; see <see cref="BeginAttempt"/>.</summary>
        private float attemptStartTime;

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
            pairSegments = new Dictionary<int, List<List<Block>>>();

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
        /// Runs every level-data sanity check against the populated grid. Call once the board is
        /// fully generated. The checks themselves live in <see cref="LevelValidator"/>; this only
        /// hands it the board.
        /// </summary>
        public void ValidateLevelData()
        {
            LevelValidator.Validate(grid, gridRow, gridCol);
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
                Block block = BlockFromHit(raycastResults[0]);

                if (block != null)
                {
                    int grabbedPairId = ResolveGrabbedPairId(block);

                    // if click on pair dot and the block is already clicked before, clears all highlighted blocks
                    // for the pair and removed it from the completed pairs list
                    // A shared destination is never a starting point: it belongs to two pairs, so
                    // a press on it could not say which one was meant. Drags start at the sources.
                    if (block.IsPairBlock && !block.IsSharedGoal && ClearSegmentsTouching(block))
                    {
                        isClicked = true;
                        selectedBlocks.Add(block);
                    }

                    //if click on pair dot and block is not clicked before
                    else if (block.IsPairBlock && !block.IsSharedGoal && !selectedBlocks.Contains(block))
                    {
                        isClicked = true;
                        selectedBlocks.Add(block);

                        //HighlightSelectedColorTypeBlock(block);
                    }

                    // if the some blocks of pair highlighted, and clicked on the highlighted block
                    // a press on a drawn cell resumes THAT segment -- the pair may have others
                    else if (SegmentContaining(grabbedPairId, block) != null)
                    {
                        List<Block> blocks = SegmentContaining(grabbedPairId, block);

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

                        DetachSegment(grabbedPairId, blocks);
                        //selectedBlocks.Add(block);
                        //HighlightSelectedColorTypeBlock(block);
                    }

                    if(isClicked)
                    {
                        AudioManager.Instance.PlayBlockSelectSound();

                        HighlightSelectedColorTypeBlock(block, grabbedPairId);
                        Color clr = (block.IsPairBlock && !block.IsSharedGoal)
                            ? GetColor(block.PairColorType)
                            : GetColor(block.GetOccupantColorType(grabbedPairId));
                        MoveTouchPointer(UnityEngine.Input.mousePosition);
                        SetTouchPointerImage(clr);
                    }
                }
            }
        }

        /// <summary>
        /// Drops pairs left with no segments, so "has this pair drawn anything" stays a simple
        /// key lookup.
        /// </summary>
        private void PruneEmptyPairs()
        {
            List<int> empty = null;

            foreach (KeyValuePair<int, List<List<Block>>> pair in pairSegments)
            {
                if (pair.Value.Count == 0)
                {
                    if (empty == null) { empty = new List<int>(); }
                    empty.Add(pair.Key);
                }
            }

            if (empty == null) { return; }
            for (int i = 0; i < empty.Count; i++) { pairSegments.Remove(empty[i]); }
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
                    Block block = BlockFromHit(raycastResults[0]);

                    // A rejected cell/wall blinks on a loop only while the pointer keeps landing
                    // on it -- stop it the moment the pointer is somewhere else.
                    if (block != invalidFeedbackBlock) { StopInvalidFeedback(); }

                    // selected block is not select again, check for the new block
                    if(CanSelectToAdd(block))
                    {
                        Direction dir = GetDirection(selectedBlocks[selectedBlocks.Count - 1], block);

                        if (dir != Direction.None && CanTakeStep(selectedBlocks[selectedBlocks.Count - 1], block, dir))
                        {
                            ProcessBlockStep(block, dir);
                        }
                        else if (dir == Direction.None)
                        {
                            // fast swipes can raycast a cell that isn't exactly adjacent to the last
                            // selected block; walk the straight-line path between them instead of
                            // silently dropping the step
                            List<Block> path = GetStraightLinePath(selectedBlocks[selectedBlocks.Count - 1], block);
                            if (path != null)
                            {
                                foreach (Block cell in path)
                                {
                                    Block stepFrom = selectedBlocks[selectedBlocks.Count - 1];

                                    if (!CanSelectToAdd(cell)) { FlashInvalidStep(stepFrom, cell); break; }

                                    Direction stepDir = GetDirection(stepFrom, cell);
                                    if (stepDir == Direction.None) { FlashInvalidStep(stepFrom, cell); break; }
                                    if (!CanTakeStep(stepFrom, cell, stepDir)) { FlashInvalidStep(stepFrom, cell); break; }

                                    ProcessBlockStep(cell, stepDir);
                                }
                            }
                            else if (AdjacentDirection(selectedBlocks[selectedBlocks.Count - 1], block) != Direction.None)
                            {
                                // Genuinely the path head's neighbour, but a wall on the shared
                                // edge or a one-way's required entry direction refuses it --
                                // GetDirection folds both of those into None.
                                FlashInvalidStep(selectedBlocks[selectedBlocks.Count - 1], block);
                            }
                        }
                        else
                        {
                            // Adjacent across a legal edge, but the destination itself refuses the
                            // step: a bridge lane already taken, or an illegal forced-arrow chain.
                            FlashInvalidStep(selectedBlocks[selectedBlocks.Count - 1], block);
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
                    else if (block != null
                        && AdjacentDirection(selectedBlocks[selectedBlocks.Count - 1], block) != Direction.None)
                    {
                        // Adjacent to the path head but CanSelectToAdd refused it outright --
                        // blocked, wrong pair colour/permission, or it would cross the path's
                        // own line.
                        FlashInvalidStep(selectedBlocks[selectedBlocks.Count - 1], block);
                    }
                }
                else
                {
                    // Pointer has been dragged off every raycastable cell (past the board's
                    // edge) -- nothing is being landed on any more, so nothing should still be
                    // blinking.
                    StopInvalidFeedback();
                }

                UpdateDragPreview();
                RefreshUnmetCheckpointFeedback();
            }
        }

        /// <summary>
        /// Blinks the reason a step from <paramref name="from"/> to <paramref name="to"/> was
        /// rejected, looping for as long as the pointer keeps landing on <paramref name="to"/> --
        /// see <see cref="invalidFeedbackBlock"/>. Whatever was blinking before this call is
        /// stopped first, so at most one cell/wall blinks at a time. A wall on the shared edge
        /// blinks the wall itself rather than the cell, since the wall -- not the cell -- is what
        /// refused the step; every other rejection (blocked, forbidden pair, one-way, taken
        /// bridge lane, illegal arrow chain, self-crossing path) blinks the destination cell.
        ///
        /// Both cells sharing a wall draw their own copy of the bar on the same spot (see
        /// wallVisual's field comment on <see cref="Block"/>), and only one of the two copies
        /// ends up on top -- which one depends on board build order, not anything this method
        /// can see. Flashing only the "owning" side risks blinking the copy that's actually
        /// hidden behind the other, so both are flashed together; the hidden one blinking is
        /// invisible and harmless, and the visible one is guaranteed to be among them.
        /// </summary>
        private void FlashInvalidStep(Block from, Block to)
        {
            if (to == null || to == invalidFeedbackBlock) { return; }

            StopInvalidFeedback();
            invalidFeedbackBlock = to;

            Direction dir = AdjacentDirection(from, to);
            bool wallOnEdge = dir != Direction.None
                && (from.HasWall(dir) || to.HasWall(OppositeDirection(dir)));

            if (wallOnEdge)
            {
                invalidFeedbackWallOther = from;
                invalidFeedbackWallDir = OppositeDirection(dir);

                from.PlayInvalidWallFeedback(dir);
                to.PlayInvalidWallFeedback(invalidFeedbackWallDir);
            }
            else
            {
                to.PlayInvalidMoveFeedback();
            }
        }

        /// <summary>
        /// Stops whatever <see cref="FlashInvalidStep"/> last started blinking, if anything --
        /// called the moment the pointer stops landing on that cell/wall (a new target, a valid
        /// step, release, or the drag ending outright), so a rejected cell never keeps blinking
        /// after the touch that triggered it has moved on.
        /// </summary>
        private void StopInvalidFeedback()
        {
            if (invalidFeedbackBlock == null) { return; }

            if (invalidFeedbackWallOther != null)
            {
                invalidFeedbackBlock.StopInvalidWallFeedback(invalidFeedbackWallDir);
                invalidFeedbackWallOther.StopInvalidWallFeedback(OppositeDirection(invalidFeedbackWallDir));
                invalidFeedbackWallOther = null;
            }
            else
            {
                invalidFeedbackBlock.StopInvalidMoveFeedback();
            }

            invalidFeedbackBlock = null;
        }

        private void OnPointerUp()
        {
            StopInvalidFeedback();
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

                // AdjacentDirection, not GetDirection: this describes a step already taken, read
                // BACKWARDS (from the cell entered toward the cell left). Re-running the movement
                // rules on that reversed reading answers None whenever the previous cell refuses
                // to be entered that way -- a one-way or an arrow -- and then this
                // whole block was skipped: the final bar never got snapped to fully connected and
                // the step was never undone either, so the path counted as complete while the last
                // segment looked half-drawn or missing.
                Direction entryDir = AdjacentDirection(releasedLast, releasedPrev);

                if (entryDir != Direction.None)
                {
                    if (releasedLast.GetDirectionFillAmount(entryDir) < 0.5f)
                    {
                        // Nothing special for arrows any more: a path may rest on one, because
                        // entering it and leaving it are two moves the player makes, not one the
                        // game makes for them.
                        UndoLastStep();
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

                if (IsPairSatisfied(selectedBlocks[0].PairId))
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
            UIController.Instance.UpdateFilledCells();

            bool boardFull = IsBoardFullyCovered();

            if (count >= UIController.Instance.CurrentLevelGoal && boardFull)
            {
                ClearUnmetCheckpointFeedback();
                GameState = GameState.Ending;
                UIController.Instance.ActivateLevelCompleteScreen(moves);
                SaveLevelData();
            }
            else
            {
                RefreshUnmetCheckpointFeedback();
            }
        }

        /// <summary>
        /// Blinks every Checkpoint currently held by a colour that is not its own.
        ///
        /// A Checkpoint is the only mechanic not enforced as the player moves. Every other rule
        /// refuses the step itself -- <see cref="Block.CanEnter"/> turns a colour away from a
        /// Forbidden or Permitted cell, <see cref="Block.CanExitFrom"/> will not let a path turn on
        /// a Bridge -- so the player is told at once. Checkpoint stops nobody at the door: any
        /// colour may cross it, and the rule is consulted only at completion time by
        /// PairSatisfiesCheckpoints. Without this the player fills the board, joins every pair,
        /// reads "Cells : 44/44" and nothing happens, with no clue which cell is wrong. Levels
        /// 41-45 have eight such states between them, and they are not edge cases: the generator's
        /// necessity gate REQUIRES that routing the owner around its checkpoint stays otherwise
        /// solvable, since that is exactly what makes the checkpoint load-bearing.
        ///
        /// <b>Occupied by the wrong colour, not merely missing its own.</b> An empty checkpoint is
        /// unfinished, not wrong -- its colour may simply not be drawn yet -- and blinking through
        /// most of a normal solve is noise the player learns to ignore. A checkpoint holding
        /// someone else is wrong the instant it happens: the cell takes one occupant, so the rule
        /// cannot be met until that colour leaves. That reads off live occupancy rather than the
        /// committed segments the win condition uses, which is deliberate -- occupancy updates as
        /// the finger moves, so the warning arrives during the drag that causes it instead of after
        /// release.
        /// </summary>
        /// <summary>How many Checkpoint cells this board carries. 0 on levels without the
        /// mechanic, which is how the HUD knows to say nothing about them.</summary>
        public int CheckpointCellCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < gridRow; i++)
                {
                    for (int j = 0; j < gridCol; j++)
                    {
                        if (grid[i, j] != null && grid[i, j].BlockType == BlockType.Checkpoint) { total++; }
                    }
                }
                return total;
            }
        }

        /// <summary>
        /// How many Checkpoints currently have their own colour's path running through them.
        ///
        /// Asks the same question <see cref="PairSatisfiesCheckpoints"/> asks -- is this cell in
        /// that pair's committed segments -- rather than reading occupancy, so the counter can
        /// never read "2/2" while the level refuses to finish. That mismatch is exactly the defect
        /// the cells counter was introduced to fix; repeating it here would undo the lesson.
        /// </summary>
        public int SatisfiedCheckpointCount
        {
            get
            {
                int met = 0;
                for (int i = 0; i < gridRow; i++)
                {
                    for (int j = 0; j < gridCol; j++)
                    {
                        Block cell = grid[i, j];
                        if (cell == null || cell.BlockType != BlockType.Checkpoint) { continue; }
                        if (SegmentContaining(cell.PairId, cell) != null) { met++; }
                    }
                }
                return met;
            }
        }

        private void RefreshUnmetCheckpointFeedback()
        {
            ClearUnmetCheckpointFeedback();

            for (int i = 0; i < gridRow; i++)
            {
                for (int j = 0; j < gridCol; j++)
                {
                    Block cell = grid[i, j];
                    if (cell == null || cell.BlockType != BlockType.Checkpoint) { continue; }
                    if (cell.OccupantCount == 0) { continue; }        // unfinished, not wrong
                    if (cell.IsOccupiedBy(cell.PairId)) { continue; } // its own colour is here

                    cell.PlayInvalidMoveFeedback();
                    unmetCheckpointBlocks.Add(cell);
                }
            }
        }

        /// <summary>Stops every blink <see cref="RefreshUnmetCheckpointFeedback"/> started.</summary>
        private void ClearUnmetCheckpointFeedback()
        {
            for (int i = 0; i < unmetCheckpointBlocks.Count; i++)
            {
                if (unmetCheckpointBlocks[i] != null) { unmetCheckpointBlocks[i].StopInvalidMoveFeedback(); }
            }
            unmetCheckpointBlocks.Clear();
        }

        /// <summary>
        /// Whether every usable cell on the board currently carries a path. A Blocked cell is the
        /// only kind excluded from "usable" -- it is never part of the board's required coverage,
        /// by definition. Every other cell needs at least one occupant: a Bridge may carry two
        /// (one per lane) but is satisfied by either, and a shared destination is satisfied the
        /// same way a plain dot is, by whichever pair(s) actually reached it.
        ///
        /// This is the second half of "the level is solved", alongside <see cref="GetPairCompleteCount"/>:
        /// connecting every pair is necessary but not sufficient -- a Flow-style board is only
        /// complete once nothing empty is left on it either.
        /// </summary>
        /// <summary>
        /// Usable cells on the board -- everything except Blocked, matching what
        /// <see cref="IsBoardFullyCovered"/> counts. This is the denominator the player is filling.
        /// </summary>
        public int UsableCellCount
        {
            get
            {
                if (grid == null) { return 0; }

                int count = 0;
                for (int r = 0; r < gridRow; r++)
                {
                    for (int c = 0; c < gridCol; c++)
                    {
                        Block cell = grid[r, c];
                        if (cell != null && cell.BlockType != BlockType.Blocked) { count++; }
                    }
                }
                return count;
            }
        }

        /// <summary>
        /// Usable cells that currently carry a path. Same occupancy test
        /// <see cref="IsBoardFullyCovered"/> uses, so the two can never disagree: when this equals
        /// <see cref="UsableCellCount"/>, the board is covered.
        /// </summary>
        public int FilledCellCount
        {
            get
            {
                if (grid == null) { return 0; }

                int count = 0;
                for (int r = 0; r < gridRow; r++)
                {
                    for (int c = 0; c < gridCol; c++)
                    {
                        Block cell = grid[r, c];
                        if (cell == null || cell.BlockType == BlockType.Blocked) { continue; }
                        if (cell.OccupantCount > 0) { count++; }
                    }
                }
                return count;
            }
        }

        private bool IsBoardFullyCovered()
        {
            return BoardTopology.IsFullyCovered(grid, gridRow, gridCol, cell => cell.OccupantCount > 0);
        }

        /// <summary>
        /// Drops the last committed step: clears the entered cell's bars, clears the bar the
        /// previous cell had pointing at it, and shortens the selection.
        /// </summary>
        private void UndoLastStep()
        {
            if (selectedBlocks.Count < 2) { return; }

            Block last = selectedBlocks[selectedBlocks.Count - 1];
            Block prev = selectedBlocks[selectedBlocks.Count - 2];

            // Reset by the DRAGGING pair's id, not prev.PairId -- that is the block's own static
            // level-data id, which is 0 on any non-dot cell, so it would match nothing on a
            // mid-path cell and leave the bar stuck on screen.
            // Pure geometry: the step exists, so its direction is not a question about the rules.
            Direction forwardDir = AdjacentDirection(prev, last);
            last.ResetAllHighlightDirection(selectedBlocks[0].PairId);
            if (forwardDir != Direction.None)
            {
                prev.ResetHighlightDirection(forwardDir);
            }

            selectedBlocks.RemoveAt(selectedBlocks.Count - 1);
        }

        /// <summary>
        /// Records that the current level has been started once more, and starts its clock.
        /// Called from <see cref="ResetGameplay"/>, so a restart counts as a fresh attempt -- which
        /// is the point: attempts-per-completion is the industry's own difficulty signal, and a
        /// level nobody ever restarts is a level nobody found hard.
        ///
        /// Written straight to disk rather than held in memory, because the attempt that matters
        /// most is the one the player abandons, and an abandoned session never reaches a save.
        /// </summary>
        private void BeginAttempt()
        {
            attemptStartTime = Time.unscaledTime;

            if (UIController.Instance == null) { return; }
            int currentLevel = UIController.Instance.CurrentLevel;
            int totalLevelCount = UIController.Instance.TotalLevelCount;
            if (currentLevel < 1 || currentLevel > totalLevelCount) { return; }

            GameMode mode = UIController.Instance.CurrentMode;
            SaveData data = SavingSystem.Instance.Load();

            int[] attempts = EnsureLength(data.AttemptsFor(mode), totalLevelCount);
            attempts[currentLevel - 1]++;
            data.SetAttemptsFor(mode, attempts);

            SavingSystem.Instance.Save(data);
        }

        private static int[] EnsureLength(int[] source, int length)
        {
            if (source != null && source.Length >= length) { return source; }
            int[] resized = new int[length];
            if (source != null) { System.Array.Copy(source, resized, source.Length); }
            return resized;
        }

        private static float[] EnsureLength(float[] source, int length)
        {
            if (source != null && source.Length >= length) { return source; }
            float[] resized = new float[length];
            if (source != null) { System.Array.Copy(source, resized, source.Length); }
            return resized;
        }

        private void SaveLevelData()
        {
            SaveData data = SavingSystem.Instance.Load();
            int currentLevel = UIController.Instance.CurrentLevel;
            int totalLevelCount = UIController.Instance.TotalLevelCount;
            GameMode mode = UIController.Instance.CurrentMode;

            // Progress is kept per mode: Classic 1 and Advanced 1 are different boards, so one
            // shared array would have each campaign overwriting the other's move counts.
            int[] modeMoves = data.MovesFor(mode);

            // sized once to the total level count, rather than growing by one slot (and
            // copying the whole array) on every single level completion
            if (modeMoves == null || modeMoves.Length < totalLevelCount)
            {
                int[] resized = new int[totalLevelCount];
                if (modeMoves != null) { System.Array.Copy(modeMoves, resized, modeMoves.Length); }
                modeMoves = resized;
                data.SetMovesFor(mode, modeMoves);
            }

            modeMoves[currentLevel - 1] = moves;

            // Time on the attempt that actually finished. Pelánek's entire Sudoku evaluation
            // regresses difficulty metrics against exactly this number, so it is what any future
            // fitting of DifficultyModel's weights will need.
            float[] modeSeconds = EnsureLength(data.SecondsFor(mode), totalLevelCount);
            modeSeconds[currentLevel - 1] = Time.unscaledTime - attemptStartTime;
            data.SetSecondsFor(mode, modeSeconds);

            if (currentLevel > data.CompletedLevelFor(mode))
            {
                data.SetCompletedLevelFor(mode, currentLevel);
            }

            SavingSystem.Instance.Save(data);
        }

        /// <summary>
        /// Scales up both dots of the pair the player just grabbed. <paramref name="grabbedPairId"/>
        /// is which pair that is when the press landed on a mid-path cell rather than a dot --
        /// resolved by <see cref="ResolveGrabbedPairId"/>, because a shared cell has two
        /// occupants and the cell alone cannot say which one was meant.
        /// </summary>
        private void HighlightSelectedColorTypeBlock(Block selectedBlock, int grabbedPairId)
        {
            highlightedBlock.Clear();

            // Every dot of the grabbed pair.
            int pairId = (selectedBlock.IsPairBlock && !selectedBlock.IsSharedGoal)
                ? selectedBlock.PairId
                : grabbedPairId;
            if (pairId != 0)
            {
                highlightedBlock.AddRange(DotsOfPair(pairId));
            }

            for (int i = 0; i < highlightedBlock.Count; i++)
            {
                highlightedBlock[i].HighlightBlock();
            }
        }

        /// <summary>
        /// Which pair the player just grabbed by pressing <paramref name="block"/>. A cell only
        /// one path can be in has a single answer, but a shared cell has two occupants
        /// and recency does not decide it: prefer whichever pair's path actually ENDS here, since
        /// that is the one a drag can extend, and fall back to the most recent occupant when
        /// neither does. Returns 0 for an unoccupied cell.
        /// </summary>
        private int ResolveGrabbedPairId(Block block)
        {
            if (block == null) { return 0; }

            for (int i = 0; i < block.OccupantCount; i++)
            {
                int candidate = block.GetOccupantPairId(i);
                List<List<Block>> segments = SegmentsOf(candidate);
                if (segments == null) { continue; }

                for (int j = 0; j < segments.Count; j++)
                {
                    List<Block> path = segments[j];
                    if (path.Count > 0 && IsEqual(path[path.Count - 1], block)) { return candidate; }
                }
            }

            return block.HighlightedPairId;
        }

        private void ResetHighlightSelectedColorTypeBlock()
        {
            if(isClicked)
            {
                for (int i = 0; i < highlightedBlock.Count; i++)
                {
                    highlightedBlock[i].ResetHighlightBlock();
                }
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
                    (block.IsDotFor(selectedBlocks[0].PairId)) ||
                    (block.IsDotFor(selectedBlocks[0].HighlightedPairId)) ||
                    (!block.IsPairBlock && hasSelectExistingFromLast) ||
                    (hasSelectExistingFromLast && block.IsDotFor(selectedBlocks[0].HighlightedPairId))
                )
                {
                    if ((hasSelectExistingFromLast || hasSelectExistingFromMiddle) && IsHighlightedPairComplete(selectedBlocks[0], selectedBlocks[selectedBlocks.Count - 1]))
                    {
                        return false;
                    }

                    // A pair may not cross its own path.
                    if (block.HighlightedPairId == selectedBlocks[0].HighlightedPairId
                        && SegmentContaining(block.HighlightedPairId, block) != null)
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
            Direction dir = AdjacentDirection(block1, block2);

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

        /// <summary>
        /// Whether the path may actually take the step from <paramref name="from"/> to
        /// <paramref name="to"/>, on top of what <see cref="GetDirection"/> already checked.
        /// The rules that need something a two-block function cannot see -- where the path came
        /// from, where it will be forced to go next, and who else is already in the cell:
        ///  - the cell being LEFT may be an arrow, and then the only legal exit is its own
        ///    (normally impossible, since an arrow's exit commits on entry, but a mid-path
        ///    reconnect can leave the path parked on one);
        ///  - the cell being ENTERED may be an arrow whose forced exit is illegal, and a path
        ///    must never be committed onto a cell it cannot leave;
        ///  - the cell being ENTERED may be a bridge whose lane on this axis is already taken.
        ///
        /// A bridge also refuses turns, which is the same CanExit call as the arrow's: the exit
        /// rule is one predicate with two rules inside it, not two predicates.
        /// </summary>
        private bool CanTakeStep(Block from, Block to, Direction dir)
        {
            int pairId = selectedBlocks[0].PairId;
            return from.CanExit(dir, pairId) && to.CanAcceptEntry(dir, pairId) && ArrowChainIsLegal(to);
        }

        /// <summary>
        /// Walks the forced exits from <paramref name="entered"/> and reports whether the whole
        /// chain can be committed. Arrows can point into arrows, so this is a walk and not a
        /// single lookahead; it is deterministic (one exit per arrow) and bounded by the board,
        /// since every step must reach a cell the path is not already using.
        /// </summary>
        private bool ArrowChainIsLegal(Block entered)
        {
            Block current = entered;
            int steps = gridRow * gridCol;

            while (current.BlockType == BlockType.Arrow && steps-- > 0)
            {
                Block next = ArrowExitTarget(current, entered);
                if (next == null) { return false; }
                current = next;
            }

            return true;
        }

        /// <summary>
        /// The cell an arrow forces the path into, or null when that step cannot be taken --
        /// off the board, across a wall, into a cell this pair may not enter, into another
        /// pair's dot, or back onto the path itself. <paramref name="alsoTaken"/> is the cell
        /// that is about to be added but is not in <see cref="selectedBlocks"/> yet, so a
        /// two-cell loop is caught while the chain is still hypothetical.
        /// </summary>
        private Block ArrowExitTarget(Block arrow, Block alsoTaken)
        {
            Direction forced = arrow.ForcedExitDirection;
            if (forced == Direction.None) { return null; }

            Block next = GetNeighbor(arrow, forced);
            if (next == null) { return null; }

            // GetDirection folds in walls and one-way entry, so a mismatch means the step is
            // refused for one of those reasons.
            if (GetDirection(arrow, next) != forced) { return null; }

            int pairId = selectedBlocks[0].PairId;
            if (!next.CanEnter(pairId)) { return null; }
            if (next.IsPairBlock && !next.IsDotFor(pairId)) { return null; }
            if (next == alsoTaken || selectedBlocks.Contains(next)) { return null; }

            return next;
        }

        /// <summary>
        /// Pure geometry: which way <paramref name="block2"/> lies from <paramref name="block1"/>
        /// when they are exactly adjacent, ignoring every rule about whether a path may actually
        /// go that way. <see cref="GetDirection"/> is this plus the rules.
        ///
        /// Separate because describing an existing step is a different question from asking whether
        /// a new one is allowed. Re-running the rules on a step already taken answers None
        /// whenever the reading is reversed and the other cell refuses that direction -- a
        /// one-way or an arrow.
        ///
        /// Rule of thumb: <see cref="GetDirection"/> for a step the player is about to take,
        /// AdjacentDirection for one already in <see cref="selectedBlocks"/> or a stored segment.
        /// </summary>
        private Direction AdjacentDirection(Block block1, Block block2)
        {
            if (block1.Row_ID == block2.Row_ID && block2.Coloum_ID - block1.Coloum_ID == 1)
            {
                return Direction.Right;
            }
            if (block1.Row_ID == block2.Row_ID && block1.Coloum_ID - block2.Coloum_ID == 1)
            {
                return Direction.Left;
            }
            if (block1.Coloum_ID == block2.Coloum_ID && block1.Row_ID - block2.Row_ID == 1)
            {
                return Direction.Up;
            }
            if (block1.Coloum_ID == block2.Coloum_ID && block2.Row_ID - block1.Row_ID == 1)
            {
                return Direction.Down;
            }
            return Direction.None;
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
            // -- shareable cells (Bridge, shared destination) are exempt: they're meant to be
            // shared, not
            // stolen from another pair. Entry terms are enforced before the step gets here.
            if (!block.IsShareable && block.HighlightedPairId != selectedBlocks[0].HighlightedPairId)
            {
                List<Block> blocks = SegmentContaining(block.HighlightedPairId, block);

                if (blocks != null)
                {
                    int indexToRemove = GetBlockIndex(blocks, block);
                    indexToRemove--;
                    if (indexToRemove <= -1) { indexToRemove = -1; }

                    if (indexToRemove != -1)
                    {
                        ResetBlockToRemove(blocks, indexToRemove);
                    }
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
                Direction oldEntryDir = AdjacentDirection(oldLast, selectedBlocks[selectedBlocks.Count - 2]);
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

            // An arrow does NOT take its own exit. It used to: entering one committed the forced
            // step immediately, so the stroke "continued through in one motion". In play that
            // detaches the line from the finger, and every rule the drag loop has is written
            // around the head being the cell under the pointer -- position read as intent, fills
            // driven by pointer distance, retreat inferred from raycasting an earlier cell. Three
            // separate bugs came out of that one flourish.
            //
            // The rule loses nothing: Block.CanExitFrom already refuses every direction except
            // the printed one, so the path can only leave an arrow the way the arrow says. The
            // player makes the move; the arrow only decides which move is available.
        }

        /// <summary>
        /// Resolves which pair color/id the currently active drag is drawing with -- the
        /// same rule ProcessBlockStep uses to color a newly committed step, reused by the
        /// live drag preview so the preview bar matches the color it'll commit as.
        /// </summary>
        private void GetCurrentDragColorAndPairId(out PairColorType type, out int pairId)
        {
            // selectedBlocks[0] is always a dot: OnPointerDown only ever begins a selection at
            // one, and a resumed path is stored from its dot outward (ResetBlockToRemove trims
            // from the far end only). So the pair's own identity is right there, whether this is
            // a fresh drag or a resumed one. This used to read the LAST selected block's occupant
            // identity when resuming -- the same answer on any cell one path can be in, and
            // whichever pair happened to arrive last on a shared one.
            type = selectedBlocks[0].PairColorType;
            pairId = selectedBlocks[0].PairId;
        }

        /// <summary>
        /// Grows the last selected block's bar toward whichever neighbor the pointer is
        /// currently heading for, proportional to how far across that cell it's travelled --
        /// a live preview of the step that ProcessBlockStep will commit once the pointer
        /// actually lands on the neighbor. Purely visual: never touches selectedBlocks or
        /// pairSegments.
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
                entryDir = AdjacentDirection(lastBlock, selectedBlocks[selectedBlocks.Count - 2]);
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
            // Un-draw bar by bar rather than resetting whole cells by pair id, so a shared cell
            // keeps the other pair's bar. AdjacentDirection is used instead of GetDirection because
            // re-asking the movement rules can answer None for a step that was legal when it was
            // made -- a one-way or an arrow refuses the reverse reading of the same two cells,
            // which would leave the bar stuck on screen.
            for (int i = indexToRemove; i < blocks.Count - 1; i++)
            {
                Direction forward = AdjacentDirection(blocks[i], blocks[i + 1]);
                if (forward == Direction.None) { continue; }

                blocks[i].ResetHighlightDirection(forward);
                blocks[i + 1].ResetHighlightDirection(OppositeDirection(forward));
            }

            // Remove blocks from the list starting from indexToRemove + 1.
            blocks.RemoveRange(indexToRemove + 1, blocks.Count - indexToRemove - 1);
        }


        /// <summary>
        /// Performs a raycast using the event system to gather raycast results.
        /// </summary>
        /// <param name="eventData">The pointer event data for the raycast.</param>
        /// <param name="results">The list to store the raycast results.</param>
        /// <summary>
        /// The cell a raycast hit, resolved by walking UP from whatever graphic was actually hit.
        ///
        /// This used to read the component off the hit object directly, which quietly tied input to
        /// one particular graphic being the raycast target: a cell is a root object with the Block
        /// script on it and a dozen child images, so hitting a child answered null and the drag did
        /// nothing. Walking up means any raycastable graphic in the cell -- the hit area, or a child
        /// that gets raycastTarget turned on later -- finds the same cell.
        ///
        /// A cell still needs at least one raycast target to be hit at all. That is the invisible
        /// full-cell Image on the root: alpha 0, so it draws nothing, with the CanvasRenderer's
        /// transparent-mesh culling turned OFF, because GraphicRaycaster skips a graphic whose mesh
        /// has been culled and a fully transparent one would be. Delete that Image and the board
        /// stops responding to touch entirely.
        /// </summary>
        private Block BlockFromHit(RaycastResult hit)
        {
            return hit.gameObject != null ? hit.gameObject.GetComponentInParent<Block>() : null;
        }

        private void PerformRaycast(PointerEventData eventData, List<RaycastResult> results)
        {
            eventSystem.RaycastAll(eventData, results);
        }

        /// <summary>
        /// Stores the drag just finished as one of its pair's segments. A segment is identified by
        /// the dot it starts from, so re-drawing the same branch replaces it instead of piling up.
        /// </summary>
        private void AddSelectedBlocksToCompletedPairs()
        {
            int pairId = selectedBlocks[0].PairId;

            if (!pairSegments.TryGetValue(pairId, out List<List<Block>> segments))
            {
                segments = new List<List<Block>>();
                pairSegments[pairId] = segments;
            }

            List<Block> stored = new List<Block>(selectedBlocks);

            for (int i = 0; i < segments.Count; i++)
            {
                if (IsEqual(segments[i][0], stored[0]))
                {
                    segments[i] = stored;
                    return;
                }
            }

            segments.Add(stored);
        }

        /// <summary>
        /// Every segment of <paramref name="pairId"/>, or null when it has none drawn.
        /// </summary>
        private List<List<Block>> SegmentsOf(int pairId)
        {
            return pairSegments.TryGetValue(pairId, out List<List<Block>> segments) ? segments : null;
        }

        /// <summary>
        /// The segment of <paramref name="pairId"/> that <paramref name="cell"/> is part of, or
        /// null. The first match is returned, which is the one drawn earliest.
        /// </summary>
        private List<Block> SegmentContaining(int pairId, Block cell)
        {
            List<List<Block>> segments = SegmentsOf(pairId);
            if (segments == null) { return null; }

            for (int i = 0; i < segments.Count; i++)
            {
                if (GetBlockIndex(segments[i], cell) != -1) { return segments[i]; }
            }
            return null;
        }

        /// <summary>
        /// Drops every segment of <paramref name="dot"/>'s pair, clearing what they drew. Tapping
        /// a dot means "undo this pair".
        /// </summary>
        private bool ClearSegmentsTouching(Block dot)
        {
            List<List<Block>> segments = SegmentsOf(dot.PairId);
            if (segments == null) { return false; }

            // A pair holds one path, so pressing either of its dots clears whatever it had --
            // which is what redrawing a pair has always meant. Clearing only the segment touching
            // the pressed dot would leave the other half dangling and unjoinable, since
            // CanSelectToAdd refuses entry into your own pair's cells.
            bool cleared = false;

            for (int i = segments.Count - 1; i >= 0; i--)
            {
                List<Block> segment = segments[i];

                ClearSegmentVisuals(segment);
                segments.RemoveAt(i);
                cleared = true;
            }

            if (segments.Count == 0) { pairSegments.Remove(dot.PairId); }
            return cleared;
        }

        /// <summary>
        /// Un-draws one segment, bar by bar rather than cell by cell. Resetting whole cells by pair
        /// id would be wrong here: on a shared cell another pair owns bars in the same cell, and
        /// clearing this segment must leave those alone.
        /// </summary>
        private void ClearSegmentVisuals(List<Block> segment)
        {
            for (int i = 0; i < segment.Count - 1; i++)
            {
                Direction forward = AdjacentDirection(segment[i], segment[i + 1]);
                if (forward == Direction.None) { continue; }

                segment[i].ResetHighlightDirection(forward);
                segment[i + 1].ResetHighlightDirection(OppositeDirection(forward));
            }

            // A one-cell segment (a dot tapped and released) drew nothing, and a longer one may
            // still hold a wash it no longer earns.
            for (int i = 0; i < segment.Count; i++)
            {
                segment[i].RefreshPathWash();
            }
        }

        /// <summary>
        /// Removes <paramref name="segment"/> from its pair's set without clearing what it drew --
        /// used when a drag is about to take it over.
        /// </summary>
        private void DetachSegment(int pairId, List<Block> segment)
        {
            List<List<Block>> segments = SegmentsOf(pairId);
            if (segments == null) { return; }

            segments.Remove(segment);
            if (segments.Count == 0) { pairSegments.Remove(pairId); }
        }


        /// <summary>
        /// Counts the number of completed pairs
        /// </summary>
        /// <returns>The count of completed pairs.</returns>
        private int GetPairCompleteCount()
        {
            int count = 0;
            foreach (var kvp in pairSegments)
            {
                if (IsPairSatisfied(kvp.Key))
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
            // Asked as "is b2 a dot of b1's pair", not "are their PairIds equal": a shared
            // destination is a dot for two pairs and PairId names only the first of them.
            return !IsEqual(b1, b2)
                && b1.IsPairBlock
                && b2.IsPairBlock
                && (b2.IsDotFor(b1.PairId) || b1.IsDotFor(b2.PairId));
        }

        private bool IsHighlightedPairComplete(Block b1, Block b2)
        {
            return (!IsEqual(b1, b2) && b1.HighlightedPairId == b2.PairId);
        }

        /// <summary>
        /// Whether <paramref name="pairId"/> is solved: all of its dots lie in one connected
        /// component of the cells it occupies, and its checkpoint and length constraints hold.
        ///
        /// This replaced "the first and last cell of the one stored path are both dots of this
        /// pair". That test was positional, which is why it could not describe a pair with three
        /// dots at all. A two-dot pair drawn as a single segment is the trivial case of the
        /// general check, so nothing about the existing levels changes.
        /// </summary>
        private bool IsPairSatisfied(int pairId)
        {
            List<List<Block>> segments = SegmentsOf(pairId);
            if (segments == null || segments.Count == 0) { return false; }

            List<Block> dots = DotsOfPair(pairId);
            if (dots.Count < 2) { return false; }

            // Walk the drawn path as a graph: cells are nodes, and two cells are joined only when
            // a segment actually runs between them. Two segments of one pair meet by sharing a
            // cell, which is how a path drawn inward from both dots joins up.
            HashSet<Block> reached = new HashSet<Block>();
            Queue<Block> queue = new Queue<Block>();

            reached.Add(dots[0]);
            queue.Enqueue(dots[0]);

            while (queue.Count > 0)
            {
                Block current = queue.Dequeue();

                for (int i = 0; i < segments.Count; i++)
                {
                    List<Block> segment = segments[i];
                    int at = GetBlockIndex(segment, current);
                    if (at == -1) { continue; }

                    if (at > 0 && reached.Add(segment[at - 1])) { queue.Enqueue(segment[at - 1]); }
                    if (at < segment.Count - 1 && reached.Add(segment[at + 1])) { queue.Enqueue(segment[at + 1]); }
                }
            }

            for (int i = 1; i < dots.Count; i++)
            {
                if (!reached.Contains(dots[i])) { return false; }
            }

            return PairSatisfiesCheckpoints(pairId);
        }

        /// <summary>
        /// Every dot on the board belonging to <paramref name="pairId"/>.
        /// </summary>
        private List<Block> DotsOfPair(int pairId)
        {
            List<Block> dots = new List<Block>();

            for (int i = 0; i < gridRow; i++)
            {
                for (int j = 0; j < gridCol; j++)
                {
                    if (grid[i, j].IsDotFor(pairId)) { dots.Add(grid[i, j]); }
                }
            }

            return dots;
        }

        /// <summary>
        /// Every grid cell marked BlockType.Checkpoint for this pairId must be part of the pair's
        /// drawn path -- any segment of it.
        /// </summary>
        private bool PairSatisfiesCheckpoints(int pairId)
        {
            for (int i = 0; i < gridRow; i++)
            {
                for (int j = 0; j < gridCol; j++)
                {
                    Block cell = grid[i, j];
                    if (cell.BlockType != BlockType.Checkpoint || cell.PairId != pairId) { continue; }
                    if (SegmentContaining(pairId, cell) == null) { return false; }
                }
            }
            return true;
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
            BeginAttempt();

            gameState = GameState.Waiting;

            StopInvalidFeedback();
            ClearUnmetCheckpointFeedback();

            selectedBlocks.Clear();
            pairSegments.Clear();
            highlightedBlock.Clear();

            isClicked = false;
            hasSelectExistingFromLast = false;
            hasSelectExistingFromMiddle = false;
        }

        /// <summary>
        /// Destroys every cell of the current board and forgets the grid. Called before the next
        /// board is built.
        /// </summary>
        public void ResetBlocks()
        {
            if (grid == null) { return; }

            for (int i = 0; i < gridRow; i++)
            {
                for (int j = 0; j < gridCol; j++)
                {
                    if (grid[i, j] != null) { Destroy(grid[i, j].gameObject); }
                }
            }

            grid = null;
        }
    }
}
