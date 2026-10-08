using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using FreeFlow.Enums;
using FreeFlow.GamePlay;

namespace FreeFlow.UI
{
    /// <summary>
    /// One animated mechanic demo: a small board built from real <see cref="Block"/> cells, with a
    /// <see cref="MechanicIntroSO"/>'s runs traced across it on a loop.
    ///
    /// A component rather than part of a page, because two different screens show these and one of
    /// them shows SEVERAL at once: the first-encounter card has a single demo, while the guide the
    /// info button opens lists every mechanic on the board, each with its own. Sharing the class
    /// means the board-building, the layout arithmetic and the playback exist once.
    ///
    /// It sits ON the board area it measures -- the same arrangement BoardGenerator has with its
    /// own area -- so Unity delivers OnRectTransformDimensionsChange to the rect that actually
    /// changed and no relay is needed to carry it.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class MechanicDemoView : MonoBehaviour
    {
        // The same cell prefab the real board uses -- see MechanicIntroSO.demoBoard for why the
        // demo is a real board rather than an illustration.
        [SerializeField] private Block blockPrefab;

        // Breathing room inside the area, per side, so the grid never runs flush to its edge.
        [SerializeField] private float boardPadding = 12f;

        // The grid behind the cells: dark cells and light lines baked into one sprite, exactly as
        // the real board draws it. Optional -- leave it unassigned and the demo is just cells.
        //
        // Sized to the CELLS rather than stretched over the area, for the reason
        // BoardGenerator.LayoutBoardGrid gives: cell size is snapped down to a whole even number,
        // so the grid the cells actually occupy is a little smaller than the space available. A
        // sprite stretched to the area instead puts its baked lines a fraction off every cell
        // boundary, and the error accumulates across the board.
        [SerializeField] private Image boardGridImage;

        // Indexed from 4x4, the same way BoardGenerator indexes its own. Optional: with none
        // supplied, whatever sprite boardGridImage already carries is kept and only its size is
        // corrected -- which is all a 4x4-only set of demos needs.
        [SerializeField] private Sprite[] gridSizeSprites;

        [Header("Timing")]
        // Matched to the hint's own trace (GamePlayController.HintDrawSeconds and its per-step
        // bounds) so a demonstrated path and a hinted one move at the same speed -- they are the
        // same gesture being shown, and two different speeds would read as two different things.
        [SerializeField] private float runDrawSeconds = 1.1f;
        [SerializeField] private float minStepSeconds = 0.08f;
        [SerializeField] private float maxStepSeconds = 0.22f;

        // How long the refusal flash is held. Longer than a step: it is the point of the run, and
        // it has to survive being looked at rather than merely occurring.
        [SerializeField] private float refusedHoldSeconds = 0.9f;

        // The nudge into the edge that cannot be crossed, and the retreat back out of it. Slower
        // than an ordinary step on purpose: a step that is going to be taken can be quick, but one
        // that gets turned back has to be seen happening or the path just looks like it stopped.
        [SerializeField] private float refusedPushSeconds = 0.28f;

        // Beats either side of a run, and the longer one before the whole sequence repeats.
        [SerializeField] private float beforeRunSeconds = 0.35f;
        [SerializeField] private float afterRunSeconds = 0.8f;
        [SerializeField] private float loopGapSeconds = 0.6f;

        private const int SmallestGridSize = 4;

        private RectTransform boardArea;
        private Block[,] cells;

        // Which mechanic the cells were built for, so re-showing the same one does not rebuild the
        // board underneath a running animation.
        private string builtKey;

        // The area size the current cells were laid out for, so a dimensions change that did not
        // actually resize it is a cheap comparison rather than a second pass over every cell.
        private Vector2 laidOutSize;

        private MechanicIntroSO current;
        private Coroutine playRoutine;

        private RectTransform Area
        {
            get
            {
                if (boardArea == null) { boardArea = (RectTransform)transform; }
                return boardArea;
            }
        }

        /// <summary>Builds <paramref name="intro"/>'s board if it is not already up, and starts
        /// tracing its runs. A card with no runs is refused here rather than shown as an empty
        /// board -- see <see cref="MechanicIntroSO.HasDemo"/>.</summary>
        public void Play(MechanicIntroSO intro)
        {
            if (intro == null || !intro.HasDemo) { Stop(); return; }

            current = intro;

            if (builtKey != intro.mechanicKey)
            {
                BuildBoard(intro.demoBoard);
                builtKey = intro.mechanicKey;
            }
            else if (cells != null)
            {
                // Kept cells, but the space may have resized while this view was away.
                LayoutBoard(cells.GetLength(0));
            }

            StopLoop();
            ClearPath();

            // StartCoroutine throws on an inactive object, and a view that is off has nothing to
            // animate anyway -- it plays when it is switched back on.
            if (isActiveAndEnabled) { playRoutine = StartCoroutine(PlayLoop()); }
        }

        /// <summary>Stops the trace and un-draws it, leaving the cells and their markers.</summary>
        public void Stop()
        {
            StopLoop();
            ClearPath();
        }

        private void StopLoop()
        {
            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
                playRoutine = null;
            }
        }

        private void OnDisable()
        {
            // Unity kills the coroutine when the object goes inactive; without clearing the handle
            // the stale one would still read as a running trace when it comes back.
            playRoutine = null;
            ClearPath();
        }

        private void OnEnable()
        {
            if (current != null && cells != null && playRoutine == null)
            {
                playRoutine = StartCoroutine(PlayLoop());
            }
        }

        /// <summary>
        /// Re-lays the demo when the space it lives in changes size -- a device rotation, a
        /// resized window, or a layout that settles a frame late.
        ///
        /// The real board does exactly this, for the same reason: cell size is measured off the
        /// area rather than authored, so a board measured once keeps a rotated phone's old cell
        /// size until something rebuilds it. Nothing is regenerated -- only measured again.
        /// </summary>
        private void OnRectTransformDimensionsChange()
        {
            if (cells == null) { return; }
            if (Area.rect.size == laidOutSize) { return; }

            LayoutBoard(cells.GetLength(0));
        }

        // ---- the demo board ---------------------------------------------------------------

        /// <summary>
        /// Builds the demo grid from <paramref name="data"/>, cell for cell, the same way
        /// BoardGenerator builds the real one -- including its pairId fallback, so a demo board
        /// can be authored with colours alone like every hand-authored level.
        ///
        /// Deliberately NOT routed through BoardGenerator itself: that one writes the grid it
        /// builds into GamePlayController, which owns exactly one board -- the one being played.
        /// A demo board that registered itself there would replace the level behind it.
        /// </summary>
        private void BuildBoard(LevelData data)
        {
            ClearBoard();

            if (blockPrefab == null || data.gridRows == null) { return; }

            int size = (int)data.gridSize;
            if (size <= 0 || data.gridRows.Length < size) { return; }

            cells = new Block[size, size];

            for (int i = 0; i < size; i++)
            {
                GridRow row = data.gridRows[i];

                for (int j = 0; j < size; j++)
                {
                    Block block = Instantiate(blockPrefab, Area);
                    block.gameObject.name = "DemoCell_" + i + "_" + j;

                    PairColorType colorType = At(row.coloum, j, PairColorType.None);

                    // Same rule as BoardGenerator: an explicit pairId wins, otherwise the colour's
                    // own value stands in for it.
                    int explicitPairId = At(row.pairId, j, 0);
                    int pairId = explicitPairId != 0 ? explicitPairId : (int)colorType;

                    block.SetBlock(
                        colorType, pairId,
                        At(row.secondPairId, j, 0), At(row.thirdPairId, j, 0), At(row.fourthPairId, j, 0),
                        At(row.blockType, j, BlockType.Normal),
                        At(row.wallMask, j, 0),
                        At(row.requiredEntryDirection, j, Direction.None),
                        At(row.forcedExitDirection, j, Direction.None),
                        i, j);

                    cells[i, j] = block;
                }
            }

            LayoutBoard(size);
        }

        /// <summary>
        /// Same arithmetic as BoardGenerator.LayoutBoard: an even whole-number cell size so every
        /// boundary lands on the integer lattice, and the grid centred in whatever space is left.
        ///
        /// Centred on the area's OWN centre rather than on (0, 0), which is the one thing this
        /// cannot borrow. A child's localPosition is measured from its parent's PIVOT, and the real
        /// board's area is pivoted in the middle, so there the two are the same point. A view
        /// anchored into a card or a list row may be pivoted anywhere, and centring on (0, 0) then
        /// hangs the grid outside the box. Reading the centre off the rect holds for any pivot.
        /// </summary>
        private void LayoutBoard(int size)
        {
            if (cells == null) { return; }

            // Two rebuilds, outer first -- the same order and reason as BoardGenerator.LayoutBoard:
            // whatever lays this view out may not have run yet on the frame it appears, and the
            // area only takes its new size after that.
            RectTransform outer = Area.parent as RectTransform;
            if (outer != null) { LayoutRebuilder.ForceRebuildLayoutImmediate(outer); }
            LayoutRebuilder.ForceRebuildLayoutImmediate(Area);

            Rect area = Area.rect;

            float usable = Mathf.Min(area.width, area.height) - (boardPadding * 2f);
            if (usable <= 0f) { return; }

            float blockSize = Mathf.Floor(usable / size * 0.5f) * 2f;
            if (blockSize < 2f) { blockSize = 2f; }

            float startX = area.center.x - ((blockSize * size) / 2f) + (blockSize / 2f);
            float startY = area.center.y + ((blockSize * size) / 2f) - (blockSize / 2f);

            LayoutBoardGrid(blockSize, size, area);

            laidOutSize = area.size;

            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    Block block = cells[i, j];
                    if (block == null) { continue; }

                    RectTransform rect = (RectTransform)block.transform;
                    rect.sizeDelta = new Vector2(blockSize, blockSize);
                    rect.localPosition = new Vector3(startX + (blockSize * j), startY - (blockSize * i), 0f);
                }
            }
        }

        /// <summary>Sizes and centres the grid image on exactly the cells that were just laid out.
        /// Mirrors BoardGenerator.LayoutBoardGrid, plus the centring that one does not need.</summary>
        private void LayoutBoardGrid(float blockSize, int size, Rect area)
        {
            if (boardGridImage == null) { return; }

            Sprite sprite = GridSpriteFor(size);
            if (sprite != null) { boardGridImage.sprite = sprite; }

            if (boardGridImage.sprite == null)
            {
                boardGridImage.gameObject.SetActive(false);
                return;
            }

            boardGridImage.gameObject.SetActive(true);
            boardGridImage.rectTransform.sizeDelta = new Vector2(blockSize * size, blockSize * size);

            if (boardGridImage.rectTransform.parent == Area)
            {
                boardGridImage.rectTransform.localPosition = new Vector3(area.center.x, area.center.y, 0f);
            }
        }

        /// <summary>The grid sprite for an n x n demo, or null when none is supplied -- which means
        /// "keep the one already assigned" rather than "draw no grid".</summary>
        private Sprite GridSpriteFor(int size)
        {
            if (gridSizeSprites == null) { return null; }

            int index = size - SmallestGridSize;
            if (index < 0 || index >= gridSizeSprites.Length) { return null; }

            return gridSizeSprites[index];
        }

        private void ClearBoard()
        {
            for (int i = Area.childCount - 1; i >= 0; i--)
            {
                GameObject child = Area.GetChild(i).gameObject;

                // The grid lives inside the area and is NOT a cell -- it is authored in the scene,
                // not spawned here, so clearing the board must leave it alone. Without this the
                // first rebuild silently deletes the background.
                if (boardGridImage != null && child == boardGridImage.gameObject) { continue; }

                // Hidden before it is destroyed: Destroy is deferred to the end of the frame, so an
                // outgoing board would otherwise be drawn underneath the incoming one for a frame
                // while the caller is already laying the new cells out.
                child.SetActive(false);
                Destroy(child);
            }

            cells = null;
            builtKey = null;
        }

        /// <summary>Un-draws every bar and stops any refusal flash, leaving the dots and markers.
        /// Between runs, and whenever the view goes away.</summary>
        private void ClearPath()
        {
            if (cells == null) { return; }

            Direction[] edges = { Direction.Left, Direction.Right, Direction.Up, Direction.Down };

            foreach (Block cell in cells)
            {
                if (cell == null) { continue; }

                cell.StopInvalidMoveFeedback();

                // Every edge, not only the one a refusal used: which edge was blinking is the run's
                // business, and a blink left running would outlive the run that started it.
                for (int i = 0; i < edges.Length; i++) { cell.StopInvalidWallFeedback(edges[i]); }

                cell.ResetAllHighlightDirection();
            }
        }

        // ---- playback ----------------------------------------------------------------------

        private IEnumerator PlayLoop()
        {
            // Unscaled throughout: the board behind this may be stopped, and a demo that froze with
            // it would sit there as a still image of a half-drawn path.
            while (current != null && current.runs != null && current.runs.Length > 0)
            {
                for (int i = 0; i < current.runs.Length; i++)
                {
                    // A run that keeps the previous one is building a picture the runs cannot make
                    // separately -- two paths sharing a bridge. See DemoRun.keepPrevious.
                    if (!current.runs[i].keepPrevious) { ClearPath(); }

                    yield return new WaitForSecondsRealtime(beforeRunSeconds);

                    yield return PlayRun(current.runs[i]);

                    yield return new WaitForSecondsRealtime(afterRunSeconds);
                }

                ClearPath();
                yield return new WaitForSecondsRealtime(loopGapSeconds);
            }

            playRoutine = null;
        }

        /// <summary>
        /// Traces one run: each step grows the leaving cell's bar out to the shared edge, then the
        /// entered cell's bar in from it -- the same two halves GamePlayController's hint draws, so
        /// a demonstrated path and a drawn one look identical.
        ///
        /// A refused run pushes at the edge it cannot cross, flashes whatever is refusing it, and
        /// retracts -- which is precisely what the board does to a player who tries it.
        /// </summary>
        private IEnumerator PlayRun(DemoRun run)
        {
            if (run.cells == null || run.cells.Length < 2) { yield break; }

            Block start = CellAt(run.cells[0]);
            if (start == null) { yield break; }

            PairColorType type = start.PairColorType;
            int pairId = start.PairId;

            int steps = run.cells.Length - 1;
            float perStep = Mathf.Clamp(runDrawSeconds / steps, minStepSeconds, maxStepSeconds);
            float halfStep = perStep * 0.5f;

            for (int i = 0; i < steps; i++)
            {
                Block from = CellAt(run.cells[i]);
                Block to = CellAt(run.cells[i + 1]);
                if (from == null || to == null) { yield break; }

                Direction dir = DirectionBetween(from, to);
                if (dir == Direction.None) { yield break; }

                if (run.endsRefused && i == steps - 1)
                {
                    // The ATTEMPT has to be visible, or a refusal reads as the path simply stopping
                    // for no reason: the bar pushes out of the cell being left, right up to the
                    // shared edge, whatever refuses it blinks, and then the push is retracted.
                    //
                    // Nothing is ever drawn INTO the refused cell. That state cannot happen in play
                    // -- the board refuses the step before anything is committed -- and a demo that
                    // showed it would be teaching a move the game does not allow.
                    from.HighlightBlockDirection(dir, type, pairId);
                    from.SetDirectionFillAmount(dir, 0f);
                    yield return GrowBar(from, dir, refusedPushSeconds);

                    // A wall belongs to the EDGE, not to either cell, so it is the wall that blinks
                    // -- flashing the cell beyond it would blame a cell that is perfectly enterable
                    // from any other side. Both sides are flashed together because each cell draws
                    // its own copy of the bar and only one ends up visible. Exactly what
                    // GamePlayController.FlashInvalidStep does to a player who tries it.
                    Direction entryEdge = OppositeDirection(dir);
                    bool wallOnEdge = from.HasWall(dir) || to.HasWall(entryEdge);

                    if (wallOnEdge)
                    {
                        from.PlayInvalidWallFeedback(dir);
                        to.PlayInvalidWallFeedback(entryEdge);
                    }
                    else
                    {
                        to.PlayInvalidMoveFeedback();
                    }

                    yield return new WaitForSecondsRealtime(refusedHoldSeconds);

                    if (wallOnEdge)
                    {
                        from.StopInvalidWallFeedback(dir);
                        to.StopInvalidWallFeedback(entryEdge);
                    }
                    else
                    {
                        to.StopInvalidMoveFeedback();
                    }

                    yield return ShrinkBar(from, dir, refusedPushSeconds);

                    // Hands the bar's slot back, so the retracted push leaves nothing claimed on
                    // the cell it came from.
                    from.ResetHighlightDirection(dir);
                    yield break;
                }

                from.HighlightBlockDirection(dir, type, pairId);
                from.SetDirectionFillAmount(dir, 0f);
                yield return GrowBar(from, dir, halfStep);

                Direction entry = OppositeDirection(dir);
                to.HighlightBlockDirection(entry, type, pairId, growFromFarEdge: true);
                yield return GrowBar(to, entry, halfStep);
            }
        }

        private IEnumerator GrowBar(Block cell, Direction dir, float duration)
        {
            float elapsed = 0f;

            while (elapsed < duration && cell != null)
            {
                elapsed += Time.unscaledDeltaTime;
                cell.SetDirectionFillAmount(dir, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            if (cell != null) { cell.SetDirectionFillAmount(dir, 1f); }
        }

        /// <summary>The push being turned back: the same bar <see cref="GrowBar"/> drew, drained to
        /// nothing from the edge it could not cross.</summary>
        private IEnumerator ShrinkBar(Block cell, Direction dir, float duration)
        {
            float elapsed = 0f;

            while (elapsed < duration && cell != null)
            {
                elapsed += Time.unscaledDeltaTime;
                cell.SetDirectionFillAmount(dir, 1f - Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            if (cell != null) { cell.SetDirectionFillAmount(dir, 0f); }
        }

        private Block CellAt(Vector2Int cell)
        {
            if (cells == null) { return null; }
            if (cell.x < 0 || cell.x >= cells.GetLength(0)) { return null; }
            if (cell.y < 0 || cell.y >= cells.GetLength(1)) { return null; }
            return cells[cell.x, cell.y];
        }

        /// <summary>Pure geometry, like GamePlayController.AdjacentDirection -- a demo path is
        /// authored, so the movement rules are not re-asked here. Asking them would refuse the very
        /// step a refused run exists to show.</summary>
        private static Direction DirectionBetween(Block a, Block b)
        {
            if (a.Row_ID == b.Row_ID && b.Coloum_ID - a.Coloum_ID == 1) { return Direction.Right; }
            if (a.Row_ID == b.Row_ID && a.Coloum_ID - b.Coloum_ID == 1) { return Direction.Left; }
            if (a.Coloum_ID == b.Coloum_ID && a.Row_ID - b.Row_ID == 1) { return Direction.Up; }
            if (a.Coloum_ID == b.Coloum_ID && b.Row_ID - a.Row_ID == 1) { return Direction.Down; }
            return Direction.None;
        }

        private static Direction OppositeDirection(Direction dir)
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

        private static T At<T>(T[] values, int index, T fallback)
        {
            return values != null && index < values.Length ? values[index] : fallback;
        }
    }
}
