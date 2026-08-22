using FreeFlow.Enums;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Represents a game block, single unit on the game board
    /// </summary>
    public class Block : MonoBehaviour
    {
        [SerializeField] private Image pairDotImage;
        [SerializeField] private Image blockBgHighlightImage;

        // The black inset sitting on top of the cell's white root Image. The white left
        // showing around it IS this cell's share of the grid line, so its inset per edge is
        // the line's half-width -- see ApplyGridLineInsets.
        [SerializeField] private RectTransform gridLineBackground;
        [SerializeField] private Image[] directionImages;

        // Edge bars, indexed like directionImages ((int)Direction - 1: Left=0, Right=1,
        // Up=2, Down=3). Reused for two mechanics that both mark a single edge of the cell:
        // dark for a wallMask bit, green for the one allowed entry edge on a OneWay cell.
        //
        // Each one is anchored to its own edge and centred ON it, so it straddles the boundary
        // into the neighbouring cell. A wall belongs to the edge between two cells, and both
        // cells draw it (BoardGenerator.NormalizeWalls), so the two copies land exactly on top of
        // each other and the player sees one rectangle in the gap -- not a box inside each cell,
        // which is what these looked like while all four were centred 100x100 squares.
        [SerializeField] private Image[] wallImages;

        // Center marker reused for the two mechanics that repurpose PairId as "which pair
        // this applies to": a plain square for Checkpoint, a 45-degree-rotated diamond for
        // ForbiddenForPair, tinted to that pair's color -- plus a neutral ring for Mixed (see
        // mixedMarkerSprite). A cell is exactly one BlockType, so those three uses can never
        // want the marker at the same time and one image serves all of them.
        [SerializeField] private Image specialMarkerImage;

        // Ring sprite swapped onto specialMarkerImage for a Mixed cell. Mixed shipped with no
        // art at all, so a rule the player cannot see read as a bug. Neutral-tinted and
        // ring-shaped so it can't be mistaken for either pair-colored marker, and it survives
        // having two paths drawn across it.
        [SerializeField] private Sprite mixedMarkerSprite;

        // Arrow glyph swapped onto specialMarkerImage for an Arrow cell, rotated to the forced
        // exit. The base sprite must point UP; MarkerRotationFor turns it from there.
        [SerializeField] private Sprite arrowMarkerSprite;

        // Crossing glyph swapped onto specialMarkerImage for a Bridge cell: two lanes meeting,
        // which is the rule stated as a shape. Distinct from the Mixed ring on purpose -- the two
        // shareable types look different because their terms are different.
        [SerializeField] private Sprite bridgeMarkerSprite;

        // Junction glyph for a Splitter cell: three stubs meeting. Must not look like the Mixed
        // ring or the Bridge crossing -- all three are "more than one path here" cells, and a
        // player who confuses them cannot reason about the board.
        [SerializeField] private Sprite splitterMarkerSprite;

        // Filled circle drawn inside the pair dot in a SECOND colour, marking a cell that is the
        // destination for two pairs at once. Uses the shared marker image rather than a new object:
        // a cell with two dot identities is never also a checkpoint, forbidden, mixed, bridge or
        // splitter cell, so the marker is free.
        [SerializeField] private Sprite secondDotSprite;

        // Shown on a pair dot when that pair has an exact-length requirement, so the
        // player knows which color it applies to and how many cells it needs.
        [SerializeField] private TextMeshProUGUI lengthLabel;

        // specialMarkerImage's authored sprite, captured before anything swaps it. Null in the
        // prefab today, which is exactly what draws the checkpoint square and forbidden
        // diamond (an Image with no sprite is a plain quad) -- captured rather than assumed so
        // ResetBlock can restore a pooled cell whatever the prefab ends up carrying.
        private Sprite defaultMarkerSprite;

        // Wall bars were rgb(0.05) against a pure-black cell background: the rule worked and
        // no player could see it. Light enough to read as a wall, dark enough that it doesn't
        // compete with a pair color.
        private static readonly Color WallColor = new Color(0.45f, 0.45f, 0.45f, 1f);
        private static readonly Color OneWayColor = new Color(0.2f, 0.8f, 0.3f, 1f);
        private static readonly Color MixedMarkerColor = new Color(0.85f, 0.85f, 0.85f, 0.9f);
        private static readonly Color ArrowMarkerColor = new Color(1f, 1f, 1f, 0.85f);
        private static readonly Color BridgeMarkerColor = new Color(0.6f, 0.72f, 0.85f, 0.85f);
        private static readonly Color SplitterMarkerColor = new Color(0.95f, 0.8f, 0.35f, 0.9f);
        private static readonly Color RotatorHintColor = new Color(0.85f, 0.68f, 0.2f, 0.55f);

        private int row_ID;
        private int coloum_ID;

        private bool isPairBlock;

        private PairColorType pairColorType;

        // Identity of the pair this block belongs to, independent of its display color.
        // 0 means "no pair" (mirrors PairColorType.None), matching pairColorType's None state.
        private int pairId;

        // A second pair this cell is also a dot for, or 0. Set on a shared destination: red's
        // source and blue's source each end here, so the cell answers to both identities.
        private int secondPairId;

        // Which pairs currently have a path through this cell, oldest first, and how each one
        // is drawn. This used to be a single (highlightedPairId, highlightedColorType) pair,
        // which was correct for every cell type except the one that exists to be shared:
        // BlockType.Mixed lets two pairs occupy a cell, but the second one to arrive silently
        // overwrote the first's identity, so the cell's wash showed the wrong colour, a reset
        // by the wrong pair cleared it, and every guard that asked "whose path is this?" got
        // the last writer rather than the truth.
        //
        // Three pairs, because occupancy is keyed by PAIR and the three cell types that share
        // differ in what they share between: a Mixed cell and a bridge hold two pairs, while a
        // splitter junction holds three segments of ONE pair (so one occupant) and may also be
        // crossed by another. The real constraint on how many PATHS fit is not this number, it is
        // the four direction slots below -- a path crossing a cell claims two of them, in and out.
        // See CanAcceptEntry, which is where that geometry is enforced.
        private const int MaxOccupants = 3;
        private int[] occupantPairId = new int[MaxOccupants];
        private PairColorType[] occupantColorType = new PairColorType[MaxOccupants];
        private int occupantCount;

        private BlockType blockType;

        // Edge-based, independent of blockType: bitmask of walled edges (Left=1, Right=2,
        // Up=4, Down=8). A wall belongs to the boundary between two cells, not to either
        // cell's type.
        private int wallMask;

        // Only meaningful when blockType == BlockType.OneWay: the only direction a path
        // may be moving in when entering this cell. Direction.None = no restriction.
        private Direction requiredEntryDirection;

        // Only meaningful when blockType == BlockType.Arrow: the direction a path must leave in,
        // whichever way it arrived. Direction.None = no restriction.
        private Direction forcedExitDirection;

        // Only meaningful when blockType == BlockType.Rotator: which elbow orientation this cell
        // is in right now. Runtime state, not level data -- SetBlock seeds it from the authored
        // initial rotation and ResetBlock clears it, so a pooled cell cannot carry a rotation
        // into the next level and the level asset is never written back to.
        private int currentRotation;

        // The two edges each rotation joins, clockwise from Up+Right. Edges, not travel
        // directions: a path entering through the Left edge is travelling Right.
        private static readonly Direction[][] RotatorEdges =
        {
            new[] { Direction.Up, Direction.Right },
            new[] { Direction.Right, Direction.Down },
            new[] { Direction.Down, Direction.Left },
            new[] { Direction.Left, Direction.Up }
        };

        // Which pair currently owns each direction-image slot (index = (int)dir - 1), 0 =
        // unowned. On a Normal cell there's only ever one occupant so this changes nothing
        // observable; on a Mixed cell it lets ResetAllHighlightDirection(pairId) clear only
        // the calling pair's own slots without touching another pair's.
        private int[] directionOwnerPairId = new int[4];

        // How much of each direction bar is drawn, 0-1. The bars are capsules (rounded cap at
        // both ends) grown by resizing their RectTransform, not by Image.fillAmount -- a
        // partial fill cuts the sprite with a straight edge and would slice the far cap clean
        // off, which is exactly why the dragged tip used to end in a hard square. Width is the
        // one growth mechanism that keeps a rounded end rounded at every length.
        private float[] directionBarFraction = new float[4];

        // Which end of each bar is pinned while it grows. False = pinned at the cell center,
        // growing outward (the cell being left). True = pinned at the outer edge, growing
        // inward (the cell being entered), so the seam with the previous cell lights up first.
        private bool[] directionBarFromFarEdge = new bool[4];

        // Runs once per pooled instance, before any SetBlock, so the authored marker sprite is
        // captured before a Mixed cell has a chance to swap the ring in over it.
        private void Awake()
        {
            defaultMarkerSprite = specialMarkerImage != null ? specialMarkerImage.sprite : null;
        }

        /// <summary>
        /// Registers <paramref name="occupantPair"/> as having a path through this cell, or
        /// refreshes its colour and moves it to most-recent if it is already here.
        /// </summary>
        private void AddOccupant(int occupantPair, PairColorType type)
        {
            if (occupantPair == 0) { return; }

            int existing = OccupantIndex(occupantPair);
            if (existing >= 0)
            {
                occupantColorType[existing] = type;
                MoveOccupantToMostRecent(existing);
                return;
            }

            if (occupantCount < MaxOccupants)
            {
                occupantPairId[occupantCount] = occupantPair;
                occupantColorType[occupantCount] = type;
                occupantCount++;
            }
            else
            {
                // No slot left (see MaxOccupants): drop the oldest, which is the same
                // last-writer-wins outcome this cell had before it tracked occupants at all.
                for (int i = 1; i < MaxOccupants; i++)
                {
                    occupantPairId[i - 1] = occupantPairId[i];
                    occupantColorType[i - 1] = occupantColorType[i];
                }
                occupantPairId[MaxOccupants - 1] = occupantPair;
                occupantColorType[MaxOccupants - 1] = type;
            }

            RefreshPathWash();
        }

        private void RemoveOccupant(int occupantPair)
        {
            int idx = OccupantIndex(occupantPair);
            if (idx < 0) { return; }

            for (int i = idx + 1; i < occupantCount; i++)
            {
                occupantPairId[i - 1] = occupantPairId[i];
                occupantColorType[i - 1] = occupantColorType[i];
            }

            occupantCount--;
            occupantPairId[occupantCount] = 0;
            occupantColorType[occupantCount] = PairColorType.None;
        }

        private void ClearOccupants()
        {
            for (int i = 0; i < MaxOccupants; i++)
            {
                occupantPairId[i] = 0;
                occupantColorType[i] = PairColorType.None;
            }
            occupantCount = 0;
        }

        private int OccupantIndex(int occupantPair)
        {
            for (int i = 0; i < occupantCount; i++)
            {
                if (occupantPairId[i] == occupantPair) { return i; }
            }
            return -1;
        }

        // Most-recent is the far end of the list, because that is what HighlightedPairId and
        // HighlightedColorType report and what a single-occupant cell has always reported.
        private void MoveOccupantToMostRecent(int idx)
        {
            int lastIdx = occupantCount - 1;
            if (idx >= lastIdx) { return; }

            int id = occupantPairId[idx];
            PairColorType type = occupantColorType[idx];

            for (int i = idx + 1; i <= lastIdx; i++)
            {
                occupantPairId[i - 1] = occupantPairId[i];
                occupantColorType[i - 1] = occupantColorType[i];
            }

            occupantPairId[lastIdx] = id;
            occupantColorType[lastIdx] = type;
        }

        private bool OwnsAnyDirection(int occupantPair)
        {
            for (int i = 0; i < directionOwnerPairId.Length; i++)
            {
                if (directionOwnerPairId[i] == occupantPair) { return true; }
            }
            return false;
        }

        /// <summary>
        /// One place decides what the full-cell path wash shows, because the answer depends on
        /// how many pairs are here: nobody, nothing; one, that pair's colour; two, nothing at
        /// all. Two washes at <see cref="PathHighlightAlpha"/> cannot both be seen and showing
        /// either one alone claims the cell for a pair that only half-owns it -- on a shared
        /// cell the direction bars carry the colour instead.
        ///
        /// Public because clearing a path one bar at a time (which is what a splitter branch
        /// needs, so it does not wipe another branch's bar from a shared junction) leaves the
        /// wash to be re-derived afterwards.
        /// </summary>
        public void RefreshPathWash()
        {
            if (occupantCount == 1 && OwnsAnyDirection(occupantPairId[0]))
            {
                HighlightBlockBg();
            }
            else
            {
                ResetHighlightBlockBg();
            }

            // A path leaving a rotator takes its bars with it, and the elbow underneath has to
            // come back -- the cell is still joined the same way whether anyone is using it.
            ApplyRotatorHint();
        }

        /// <summary>
        /// Whether <paramref name="occupantPair"/> has a path through this cell. This is the
        /// question almost every caller actually means; <see cref="HighlightedPairId"/> answers
        /// the narrower "who arrived last", which on a shared cell is not the same thing.
        /// </summary>
        public bool IsOccupiedBy(int occupantPair)
        {
            return OccupantIndex(occupantPair) >= 0;
        }

        public int OccupantCount { get { return occupantCount; } }

        /// <summary>Occupant at <paramref name="index"/>, oldest first. 0 when out of range.</summary>
        public int GetOccupantPairId(int index)
        {
            return (index >= 0 && index < occupantCount) ? occupantPairId[index] : 0;
        }

        /// <summary>
        /// How <paramref name="occupantPair"/> is drawn in this cell, or None if it isn't here.
        /// </summary>
        public PairColorType GetOccupantColorType(int occupantPair)
        {
            int idx = OccupantIndex(occupantPair);
            return idx >= 0 ? occupantColorType[idx] : PairColorType.None;
        }

        /// <summary>
        /// Sets the properties of the block, including its position, pair color type,
        /// </summary>
        /// <param name="type">The pair color type of the block.</param>
        /// <param name="pairId">The pair identity of the block, independent of display color.</param>
        /// <param name="secondPairId">A second pair this cell is a dot for, or 0.</param>
        /// <param name="blockType">The obstacle/mechanic type of this cell.</param>
        /// <param name="wallMask">Bitmask of walled edges (Left=1, Right=2, Up=4, Down=8).</param>
        /// <param name="requiredEntryDirection">Only used when blockType is OneWay.</param>
        /// <param name="forcedExitDirection">Only used when blockType is Arrow.</param>
        /// <param name="initialRotation">Only used when blockType is Rotator.</param>
        /// <param name="rowIndex">The row index of the block.</param>
        /// <param name="coloumIndex">The column index of the block.</param>
        public void SetBlock(PairColorType type, int pairId, int secondPairId, BlockType blockType, int wallMask, Direction requiredEntryDirection, Direction forcedExitDirection, int initialRotation, int rowIndex, int coloumIndex)
        {
            this.row_ID = rowIndex;
            this.coloum_ID = coloumIndex;

            ApplyGridLineInsets(rowIndex, coloumIndex);
            ApplyWallGeometry();
            pairColorType = type;
            this.pairId = pairId;
            this.secondPairId = secondPairId;
            this.blockType = blockType;
            this.wallMask = wallMask;
            this.requiredEntryDirection = requiredEntryDirection;
            this.forcedExitDirection = forcedExitDirection;
            this.currentRotation = ((initialRotation % 4) + 4) % 4;

            // Bars are kept active permanently and shown/hidden purely by their width
            // (0 = invisible), rather than toggling the GameObject active/inactive on every
            // drag update, which was popping the bars on and off.
            for (int i = 0; i < directionImages.Length; i++)
            {
                directionImages[i].gameObject.SetActive(true);
                directionImages[i].DOKill();
                SetBarFraction(i, 0f, false);
            }

            if (pairColorType != PairColorType.None)
            {
                isPairBlock = true;
                pairDotImage.gameObject.SetActive(true);
                pairDotImage.color = GamePlayController.Instance.GetColor(type);

                pairDotImage.transform.localScale = Vector3.zero;
                pairDotImage.transform.DOScale(1, 0.5f);

                if (secondPairId != 0) { ShowSecondDot(); }

                int requiredLength = GamePlayController.Instance.GetRequiredPathLength(pairId);
                if (requiredLength > 0)
                {
                    lengthLabel.gameObject.SetActive(true);
                    lengthLabel.text = requiredLength.ToString();
                }
            }

            // full-cell fill for obstacles that block/gate the whole cell
            if (blockType == BlockType.Blocked)
            {
                SetObstacleVisual(new Color(0.2f, 0.2f, 0.2f, 1));
            }
            else if (blockType == BlockType.Gate)
            {
                // gates always start locked -- nothing can be solved yet at level load
                SetObstacleVisual(new Color(0.8f, 0.5f, 0.1f, 1));
            }
            else if (blockType == BlockType.Checkpoint)
            {
                ShowSpecialMarker(pairId, 0f);
            }
            else if (blockType == BlockType.ForbiddenForPair)
            {
                ShowSpecialMarker(pairId, 45f);
            }
            else if (blockType == BlockType.Mixed)
            {
                ShowMixedMarker();
            }
            else if (blockType == BlockType.Arrow)
            {
                ShowArrowMarker();
            }
            else if (blockType == BlockType.Bridge)
            {
                ShowBridgeMarker();
            }
            else if (blockType == BlockType.Splitter)
            {
                ShowSplitterMarker();
            }

            ApplyRotatorHint();

            // wall bars: independent of blockType, one per walled edge
            Direction[] edges = { Direction.Left, Direction.Right, Direction.Up, Direction.Down };
            for (int i = 0; i < edges.Length; i++)
            {
                if (HasWall(edges[i]))
                {
                    int idx = (int)edges[i] - 1;
                    wallImages[idx].gameObject.SetActive(true);
                    wallImages[idx].color = WallColor;
                }
            }

            // one-way: mark the single edge a path may enter through, on the opposite side
            // of the direction it must be travelling (e.g. "must be moving Down" means it
            // enters via the cell's Up-facing edge)
            if (blockType == BlockType.OneWay && requiredEntryDirection != Direction.None)
            {
                Direction entryEdge = OppositeDirection(requiredEntryDirection);
                int idx = (int)entryEdge - 1;

                // The green bar and the wall bar are the same image, so a wall on the one-way's
                // entry edge would have its art eaten by the green. Leave the wall drawn
                // instead: the cell is unenterable either way (GetDirection refuses a walled
                // crossing before it ever asks CanEnterFrom), and painting it green would
                // advertise the one opening this cell doesn't have. The combination is a level
                // authoring error, and flagging it is validation's job, not rendering's.
                if (idx >= 0 && !HasWall(entryEdge))
                {
                    wallImages[idx].gameObject.SetActive(true);
                    wallImages[idx].color = OneWayColor;
                }
            }
        }

        // Total width every grid line should end up, inner and outer alike.
        private const float GridLineWidth = 2f;

        /// <summary>
        /// Sizes the black inset so this cell contributes exactly its share of each grid line.
        /// Every cell paints its own full-width line, and cells butt right up against each
        /// other, so an interior seam gets one from each side and came out twice the weight of
        /// the board's outer edge, which only ever has the one. Interior edges therefore inset
        /// by half a line and let the neighbour supply the other half; the four edges on the
        /// board's rim have no neighbour and inset by the whole width.
        /// </summary>
        // How thick a wall bar is, as a fraction of the cell. Proportional rather than fixed so a
        // wall reads the same weight on a 4x4 board and an 8x8 one.
        private const float WallThicknessFraction = 0.1f;

        /// <summary>
        /// Sizes the four edge bars from the cell's current size. Only the thickness is set here --
        /// each bar's anchors already pin it along its own edge and centre it on the boundary --
        /// and it is re-applied whenever the cell is resized, since the board measures itself at
        /// runtime and again on every rotation or window resize.
        /// </summary>
        private void ApplyWallGeometry()
        {
            if (wallImages == null) { return; }

            RectTransform cellRect = transform as RectTransform;
            if (cellRect == null) { return; }

            float cell = Mathf.Min(cellRect.rect.width, cellRect.rect.height);
            float thickness = Mathf.Max(2f, cell * WallThicknessFraction);

            for (int i = 0; i < wallImages.Length; i++)
            {
                if (wallImages[i] == null) { continue; }

                bool vertical = i == (int)Direction.Left - 1 || i == (int)Direction.Right - 1;
                wallImages[i].rectTransform.sizeDelta = vertical
                    ? new Vector2(thickness, 0f)
                    : new Vector2(0f, thickness);
            }
        }

        private void ApplyGridLineInsets(int rowIndex, int coloumIndex)
        {
            if (gridLineBackground == null) { return; }

            int lastRow = GamePlayController.Instance.gridRow - 1;
            int lastColoum = GamePlayController.Instance.gridCol - 1;
            float half = GridLineWidth * 0.5f;

            // rowIndex 0 is the TOP row -- BoardGenerator lays rows out downward from the top.
            float left = coloumIndex == 0 ? GridLineWidth : half;
            float right = coloumIndex == lastColoum ? GridLineWidth : half;
            float top = rowIndex == 0 ? GridLineWidth : half;
            float bottom = rowIndex == lastRow ? GridLineWidth : half;

            gridLineBackground.offsetMin = new Vector2(left, bottom);
            gridLineBackground.offsetMax = new Vector2(-right, -top);

            // The path/obstacle wash sits directly over the same area, so it has to follow the
            // same insets or it would leave a sliver of bare background along interior edges.
            RectTransform highlightRect = blockBgHighlightImage.rectTransform;
            highlightRect.offsetMin = gridLineBackground.offsetMin;
            highlightRect.offsetMax = gridLineBackground.offsetMax;
        }

        private void SetObstacleVisual(Color color)
        {
            blockBgHighlightImage.gameObject.SetActive(true);
            blockBgHighlightImage.color = color;
        }

        /// <summary>
        /// Shows the shared center marker tinted to <paramref name="targetPairId"/>'s color.
        /// Assumes pairId doubles as a valid PairColorType value (true for every level
        /// authored so far); levels that give a Checkpoint/ForbiddenForPair cell a pairId
        /// outside 1-9 would need a real pairId-to-color lookup instead.
        /// </summary>
        private void ShowSpecialMarker(int targetPairId, float rotationZ)
        {
            specialMarkerImage.gameObject.SetActive(true);
            specialMarkerImage.transform.localEulerAngles = new Vector3(0, 0, rotationZ);
            specialMarkerImage.transform.localScale = Vector3.one;
            specialMarkerImage.sprite = defaultMarkerSprite;

            Color color = GamePlayController.Instance.GetColor((PairColorType)targetPairId);
            color.a = 1f;
            specialMarkerImage.color = color;
        }

        /// <summary>
        /// Shows the shared center marker as a neutral ring, marking a cell more than one pair
        /// may occupy. Deliberately not tinted to any pair color: the cell belongs to whoever
        /// crosses it, and the two pair-colored marker shapes already mean "this rule is about
        /// that pair". Falls back to the plain marker quad, scaled down, when no ring sprite is
        /// assigned -- smaller and neutral still reads as "not a checkpoint".
        /// </summary>
        private void ShowMixedMarker()
        {
            specialMarkerImage.gameObject.SetActive(true);
            specialMarkerImage.transform.localEulerAngles = Vector3.zero;
            specialMarkerImage.transform.localScale = mixedMarkerSprite != null ? Vector3.one : Vector3.one * 0.4f;
            specialMarkerImage.sprite = mixedMarkerSprite;
            specialMarkerImage.color = MixedMarkerColor;
        }

        /// <summary>
        /// Shows the shared center marker as an arrow pointing the way a path is forced to leave.
        /// Neutral white: the rule applies to every pair, so tinting it to one would be a lie.
        /// This is the one marker whose meaning is its rotation, which is why the arrow glyph is
        /// reserved for it -- a OneWay cell marks its entry EDGE instead, so the two directional
        /// mechanics never look alike.
        /// </summary>
        private void ShowArrowMarker()
        {
            specialMarkerImage.gameObject.SetActive(true);
            specialMarkerImage.transform.localEulerAngles = new Vector3(0, 0, MarkerRotationFor(forcedExitDirection));
            specialMarkerImage.transform.localScale = arrowMarkerSprite != null ? Vector3.one : Vector3.one * 0.4f;
            specialMarkerImage.sprite = arrowMarkerSprite;
            specialMarkerImage.color = ArrowMarkerColor;
        }

        /// <summary>
        /// Shows the shared center marker as a crossing, marking a cell two pairs may share on
        /// strict terms. Cool neutral tint rather than the Mixed ring's warm grey, so the
        /// permissive and strict shareable cells read apart at a glance.
        /// </summary>
        private void ShowBridgeMarker()
        {
            specialMarkerImage.gameObject.SetActive(true);
            specialMarkerImage.transform.localEulerAngles = Vector3.zero;
            specialMarkerImage.transform.localScale = bridgeMarkerSprite != null ? Vector3.one : Vector3.one * 0.4f;
            specialMarkerImage.sprite = bridgeMarkerSprite;
            specialMarkerImage.color = BridgeMarkerColor;
        }

        /// <summary>
        /// Shows the shared center marker as a splitter junction. Warm gold, distinct from both
        /// shareable cells: this one is not about two pairs meeting, it is one pair branching.
        /// </summary>
        private void ShowSplitterMarker()
        {
            specialMarkerImage.gameObject.SetActive(true);
            specialMarkerImage.transform.localEulerAngles = Vector3.zero;
            specialMarkerImage.transform.localScale = splitterMarkerSprite != null ? Vector3.one : Vector3.one * 0.5f;
            specialMarkerImage.sprite = splitterMarkerSprite;
            specialMarkerImage.color = SplitterMarkerColor;
        }

        // Z rotation that turns an up-pointing sprite toward dir. Positive Z is counter-clockwise
        // in UI space, so Up -> Left is +90.
        private static float MarkerRotationFor(Direction dir)
        {
            switch (dir)
            {
                case Direction.Up: return 0f;
                case Direction.Left: return 90f;
                case Direction.Down: return 180f;
                case Direction.Right: return 270f;
                default: return 0f;
            }
        }

        /// <summary>
        /// Draws the second pair's colour as a smaller filled circle inside this cell's dot, so a
        /// shared destination reads as one dot wearing two colours rather than as any of the
        /// "more than one path here" markers.
        /// </summary>
        private void ShowSecondDot()
        {
            specialMarkerImage.gameObject.SetActive(true);
            specialMarkerImage.transform.localEulerAngles = Vector3.zero;
            specialMarkerImage.transform.localScale = Vector3.one * 0.5f;
            specialMarkerImage.sprite = secondDotSprite;

            // Same (PairColorType)pairId assumption the other markers make -- see ShowSpecialMarker.
            Color color = GamePlayController.Instance.GetColor((PairColorType)secondPairId);
            color.a = 1f;
            specialMarkerImage.color = color;
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

        /// <summary>
        /// Whether a path belonging to <paramref name="enteringPairId"/> is allowed to
        /// enter this cell at all, ignoring direction. See <see cref="CanEnterFrom"/> for
        /// the direction-dependent gate (walls, one-way), checked separately in GetDirection.
        /// </summary>
        public bool CanEnter(int enteringPairId)
        {
            if (blockType == BlockType.Blocked) { return false; }
            if (blockType == BlockType.ForbiddenForPair && pairId == enteringPairId) { return false; }
            if (blockType == BlockType.Gate && !GamePlayController.Instance.IsPairSolved(pairId)) { return false; }
            return true;
        }

        /// <summary>
        /// Updates this cell's visual to reflect whether its dependency pair is solved.
        /// No-op for non-Gate cells.
        /// </summary>
        public void RefreshGateVisual()
        {
            if (blockType != BlockType.Gate) { return; }

            if (GamePlayController.Instance.IsPairSolved(pairId))
            {
                blockBgHighlightImage.gameObject.SetActive(false);
            }
            else
            {
                SetObstacleVisual(new Color(0.8f, 0.5f, 0.1f, 1));
            }
        }

        /// <summary>
        /// Whether this cell may be entered while moving in <paramref name="incomingDirection"/>.
        /// </summary>
        public bool CanEnterFrom(Direction incomingDirection)
        {
            // An arrow entered head-on would be forced straight back out into the cell the path
            // just came from, which is an illegal self-overlap. Refusing the entry is both the
            // simplest resolution and the intuitive one: an arrow reads as a current, and you
            // cannot swim into it.
            if (blockType == BlockType.Arrow
                && forcedExitDirection != Direction.None
                && incomingDirection == OppositeDirection(forcedExitDirection))
            {
                return false;
            }

            // A rotator only joins two edges, so a path can only come in through one of those --
            // and a path entering through an edge is travelling the opposite way.
            if (blockType == BlockType.Rotator && !JoinsEdge(OppositeDirection(incomingDirection)))
            {
                return false;
            }

            return requiredEntryDirection == Direction.None || requiredEntryDirection == incomingDirection;
        }

        /// <summary>
        /// Whether a path in this cell may leave in <paramref name="exitDirection"/>. The third
        /// entry predicate, and the one <see cref="CanEnter"/> and <see cref="CanEnterFrom"/>
        /// cannot express: they see a cell and an approach, never where the path goes next.
        ///
        /// Inert everywhere except an Arrow cell. Normally an arrow's exit is committed the
        /// instant a path enters (GamePlayController follows the chain), so nothing gets the
        /// chance to leave the wrong way -- but a path trimmed by a mid-path reconnect can be
        /// left sitting on one, and then this is what holds the rule.
        /// </summary>
        public bool CanExit(Direction exitDirection, int exitingPairId)
        {
            return CanExitFrom(EntryDirectionOf(exitingPairId), exitDirection);
        }

        /// <summary>
        /// The exit rule as a pure function of the two directions, with no reference to what is
        /// currently drawn here. Same rule as <see cref="CanExit"/>, reachable by anything that
        /// knows a hypothetical entry — LevelValidator walks boards this way, and one rule with
        /// two entry points beats two implementations that can drift apart.
        ///
        /// <paramref name="entryDirection"/> may be None when the entry is unknown (a path that
        /// starts here), in which case only rules that ignore the entry can apply.
        /// </summary>
        public bool CanExitFrom(Direction entryDirection, Direction exitDirection)
        {
            if (blockType == BlockType.Arrow)
            {
                return forcedExitDirection == Direction.None || exitDirection == forcedExitDirection;
            }

            if (blockType == BlockType.Bridge)
            {
                // Straight through only: a bridge is two lanes crossing, and a path that turned
                // on one would be changing lanes in mid-air.
                return entryDirection == Direction.None || exitDirection == entryDirection;
            }

            if (blockType == BlockType.Rotator)
            {
                // In through one joined edge, out through the other. With an unknown entry, any
                // joined edge will do -- there is nothing yet to have come in through.
                if (entryDirection == Direction.None) { return JoinsEdge(exitDirection); }

                Direction entryEdge = OppositeDirection(entryDirection);
                return JoinsEdge(exitDirection) && exitDirection != entryEdge;
            }

            return true;
        }

        /// <summary>
        /// Whether this rotator's current elbow includes the given edge.
        /// </summary>
        private bool JoinsEdge(Direction edge)
        {
            Direction[] joined = RotatorEdges[currentRotation];
            return joined[0] == edge || joined[1] == edge;
        }

        /// <summary>
        /// Turns the elbow a quarter turn and redraws it. No-op on anything else.
        /// </summary>
        public void Rotate()
        {
            if (blockType != BlockType.Rotator) { return; }

            currentRotation = (currentRotation + 1) % 4;
            ApplyRotatorHint();
        }

        /// <summary>
        /// Draws the elbow: the two joined edges as dim gold bars in the cell's own direction
        /// slots. Reusing those slots rather than adding art is not a shortcut -- they are already
        /// exactly the right geometry (centre to edge), so the hint lines up with the path that
        /// will run through it, and gold marks the cell as board furniture the player can touch.
        ///
        /// Only ever writes to slots no pair owns, so a path drawn through the rotator covers the
        /// hint with its own colour; when that path leaves, RefreshPathWash brings the hint back.
        /// </summary>
        private void ApplyRotatorHint()
        {
            if (blockType != BlockType.Rotator) { return; }

            for (int i = 0; i < directionImages.Length; i++)
            {
                if (directionOwnerPairId[i] != 0) { continue; }

                bool joined = JoinsEdge((Direction)(i + 1));
                directionImages[i].DOKill();
                directionImages[i].color = RotatorHintColor;
                SetBarFraction(i, joined ? 1f : 0f, false);
            }
        }

        public int CurrentRotation { get { return currentRotation; } }

        /// <summary>
        /// Entry as it would be under SOME rotation, for level validation rather than play. A
        /// rotator's orientation is the player's to change, so asking whether a board is solvable
        /// has to consider every rotation, not the authored one -- otherwise a level whose whole
        /// point is "turn this" reads as unsolvable.
        /// </summary>
        public bool CanEnterFromUnderAnyRotation(Direction incomingDirection)
        {
            if (blockType == BlockType.Rotator) { return true; }
            return CanEnterFrom(incomingDirection);
        }

        /// <summary>
        /// Exit as it would be under SOME rotation -- see
        /// <see cref="CanEnterFromUnderAnyRotation"/>. Every rotation is an elbow, so a rotator
        /// always turns a path ninety degrees: straight through and doubling back are the two
        /// things no rotation can offer.
        /// </summary>
        public bool CanExitFromUnderAnyRotation(Direction entryDirection, Direction exitDirection)
        {
            if (blockType != BlockType.Rotator) { return CanExitFrom(entryDirection, exitDirection); }
            if (entryDirection == Direction.None) { return true; }

            return exitDirection != entryDirection
                && exitDirection != OppositeDirection(entryDirection);
        }

        /// <summary>
        /// Whether <paramref name="enteringPairId"/> may enter while moving in
        /// <paramref name="incomingDirection"/>, given who is already here. Only a Bridge says no:
        /// it carries one lane per axis, so a second pair crossing the same way has nowhere to go.
        /// Mixed, by contrast, shares freely — that is the difference between the two.
        /// </summary>
        public bool CanAcceptEntry(Direction incomingDirection, int enteringPairId)
        {
            if (blockType == BlockType.Mixed)
            {
                // A path crossing a cell claims two direction slots -- the edge it came in
                // through and the one it leaves by -- and there are only four. Two paths fill a
                // Mixed cell exactly, whether they run straight or turn.
                //
                // A third has to take a slot off one of them, and since each slot records its
                // owner, that quietly hands part of one pair's line to another: clearing the third
                // pair would then tear a hole in a line the player never touched. Refusing the
                // entry is the honest answer, and it is the same shape of rule as the bridge's --
                // that one limits by axis, this one by what can be drawn.
                if (IsOccupiedBy(enteringPairId)) { return true; }
                return FreeDirectionSlots() >= 2;
            }

            if (blockType != BlockType.Bridge) { return true; }

            bool horizontal = IsHorizontal(incomingDirection);

            for (int i = 0; i < occupantCount; i++)
            {
                int other = occupantPairId[i];
                if (other == enteringPairId) { continue; }
                if (OwnsAxis(other, horizontal)) { return false; }
            }

            return true;
        }

        /// <summary>
        /// Which way <paramref name="pairId"/> was travelling when it entered, read back off the
        /// one direction bar it owns here. None when it owns no bar (not here) or more than one
        /// (already crossed, so it is not the path's tip and nothing will ask).
        /// </summary>
        private Direction EntryDirectionOf(int pairId)
        {
            Direction owned = Direction.None;

            for (int i = 0; i < directionOwnerPairId.Length; i++)
            {
                if (directionOwnerPairId[i] != pairId) { continue; }
                if (owned != Direction.None) { return Direction.None; }
                owned = (Direction)(i + 1);
            }

            // The bar sits on the edge the path came through, so travel was the other way.
            return owned == Direction.None ? Direction.None : OppositeDirection(owned);
        }

        /// <summary>
        /// How many of the four direction slots no pair has claimed. A live drag preview does not
        /// claim one, so this counts committed bars only.
        /// </summary>
        private int FreeDirectionSlots()
        {
            int free = 0;

            for (int i = 0; i < directionOwnerPairId.Length; i++)
            {
                if (directionOwnerPairId[i] == 0) { free++; }
            }

            return free;
        }

        private bool OwnsAxis(int pairId, bool horizontal)
        {
            int first = horizontal ? (int)Direction.Left - 1 : (int)Direction.Up - 1;
            int second = horizontal ? (int)Direction.Right - 1 : (int)Direction.Down - 1;
            return directionOwnerPairId[first] == pairId || directionOwnerPairId[second] == pairId;
        }

        private static bool IsHorizontal(Direction dir)
        {
            return dir == Direction.Left || dir == Direction.Right;
        }

        /// <summary>
        /// Whether more than one path may be in this cell at once. The shareable types differ in
        /// their terms, not in whether they share, so cell-stealing asks this rather than comparing
        /// against a list of enum values that grows every time one is added.
        ///
        /// Splitter counts: a junction holds several segments of ONE pair, and a branch arriving at
        /// it must not be treated as stealing the cell from its own siblings. Without that, a dot
        /// sitting directly beside a junction breaks it -- the first step of that branch lands on
        /// the junction while the branch's own dot still has no bars, so the steal check reads the
        /// junction as another pair's cell and trims a sibling branch off it.
        /// </summary>
        public bool IsShareable
        {
            get
            {
                return blockType == BlockType.Mixed
                    || blockType == BlockType.Bridge
                    || blockType == BlockType.Splitter
                    || IsSharedGoal;
            }
        }

        /// <summary>
        /// Whether this cell has a wall on the edge facing <paramref name="dir"/>.
        /// </summary>
        public bool HasWall(Direction dir)
        {
            int bit = WallBit(dir);
            return bit != 0 && (wallMask & bit) != 0;
        }

        /// <summary>
        /// Adds a wall on <paramref name="dir"/> and draws it, if it isn't already there. Used by
        /// BoardGenerator to mirror a one-sided authored wall onto the neighbouring cell. A wall
        /// takes the shared edge image over a one-way marker, which is the same precedence
        /// SetBlock applies and for the same reason: the crossing is refused, so the art must not
        /// suggest an opening.
        /// </summary>
        public void AddWall(Direction dir)
        {
            int bit = WallBit(dir);
            if (bit == 0 || (wallMask & bit) != 0) { return; }

            wallMask |= bit;

            int idx = (int)dir - 1;
            wallImages[idx].gameObject.SetActive(true);
            wallImages[idx].color = WallColor;
        }

        private static int WallBit(Direction dir)
        {
            switch (dir)
            {
                case Direction.Left: return 1;
                case Direction.Right: return 2;
                case Direction.Up: return 4;
                case Direction.Down: return 8;
                default: return 0;
            }
        }

        /// <summary>
        /// Lays out one direction bar for its current fraction. Each bar is a capsule sprite
        /// (9-sliced so both caps stay circular however far it's stretched) laid along the
        /// cell's local +X, rotated per direction, pivot at the cell center end.
        ///
        /// Two bits of geometry make the joints work, and both depend on the cap radius being
        /// exactly half the bar's thickness:
        ///  - the near end starts half a thickness BEHIND the cell center, so its cap circle is
        ///    centred on the cell center. Two perpendicular bars then overlap as a full disc
        ///    there, which is precisely a round join: the outer corner of the elbow comes out as
        ///    a quarter arc. Start the bar at the center instead and the caps taper to a point,
        ///    pinching every straight-through cell at its waist.
        ///  - the far end runs half a thickness PAST the cell edge, so it overlaps the
        ///    neighbouring cell's own cap and the seam between two cells stays full thickness.
        ///    The authored 2px overhang isn't enough once the ends are round -- both caps would
        ///    be mid-taper at the seam, leaving a visible pinch on every straight run.
        /// </summary>
        private void ApplyBarGeometry(int idx)
        {
            RectTransform barRect = directionImages[idx].rectTransform;
            RectTransform cellRect = barRect.parent as RectTransform;
            if (cellRect == null) { return; }

            float thickness = (barRect.anchorMax.y - barRect.anchorMin.y) * cellRect.rect.height + barRect.sizeDelta.y;
            float capRadius = thickness * 0.5f;
            float centerToEdge = (barRect.anchorMax.x - barRect.anchorMin.x) * cellRect.rect.width;
            float fullLength = centerToEdge + capRadius * 2f;

            ApplyCapScale(idx, capRadius);

            float fraction = directionBarFraction[idx];
            if (fraction <= 0f)
            {
                barRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 0f);
                return;
            }

            float length = fullLength * fraction;

            // Where the bar's near end sits along its own local +X. Growing outward keeps it
            // capRadius behind the cell center (scaled by fraction so a bar at 0 has no length
            // and nothing pops in at the center); the entering case instead parks the far end
            // on the cell edge and walks the near end inward.
            float nearEnd = directionBarFromFarEdge[idx]
                ? (centerToEdge + capRadius) - length
                : -capRadius * fraction;

            // That offset has to be carried by the PIVOT, not anchoredPosition. Rotation
            // happens about the pivot and anchoredPosition is measured in the cell's frame, so
            // shifting there moves every bar the same way in cell space -- which is backwards
            // for the three rotated ones, pulling Left/Up/Down off the center they exist to
            // overlap. Putting it in the pivot keeps the offset along each bar's own axis, and
            // anchoredPosition is then whatever holds that pivot on the cell center:
            // pivotPos = anchoredPosition.x + pivot.x * anchorSpan, which must come to zero.
            float pivotX = -nearEnd / length;
            barRect.pivot = new Vector2(pivotX, 0.5f);
            barRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, length);
            barRect.anchoredPosition = new Vector2(-pivotX * centerToEdge, 0f);
        }

        /// <summary>
        /// Keeps the 9-sliced caps drawn at exactly <paramref name="capRadius"/> wide. Sliced
        /// borders are otherwise drawn at their authored pixel size, so the caps would be a
        /// fixed width regardless of how thick the bar is and only look circular at one
        /// specific grid size.
        /// </summary>
        private void ApplyCapScale(int idx, float capRadius)
        {
            Image image = directionImages[idx];
            Sprite sprite = image.sprite;
            if (sprite == null || capRadius <= 0f) { return; }

            float borderPixels = sprite.border.x;
            if (borderPixels <= 0f) { return; }

            Canvas canvas = image.canvas;
            float referencePixelsPerUnit = canvas != null ? canvas.referencePixelsPerUnit : 100f;
            float spriteUnits = sprite.pixelsPerUnit / referencePixelsPerUnit;

            image.pixelsPerUnitMultiplier = borderPixels / spriteUnits / capRadius;
        }

        /// <summary>
        /// Re-lays the bars whenever this cell's own rect changes size. Bar length used to be
        /// pure anchoring, which Unity re-solved for free; now that it's computed from the cell
        /// size, an already-drawn path would keep the previous cell's measurements after a
        /// resize -- a device rotation mid-level, or a pooled cell handed a different grid size.
        /// </summary>
        private void OnRectTransformDimensionsChange()
        {
            if (directionImages == null || directionBarFraction == null) { return; }

            for (int i = 0; i < directionImages.Length; i++)
            {
                if (directionImages[i] == null) { continue; }
                ApplyBarGeometry(i);
            }

            ApplyWallGeometry();
        }

        private void SetBarFraction(int idx, float fraction, bool fromFarEdge)
        {
            directionBarFraction[idx] = Mathf.Clamp01(fraction);
            directionBarFromFarEdge[idx] = fromFarEdge;
            ApplyBarGeometry(idx);
        }

        // How long a bar takes to finish filling once a step commits. The cell being left
        // already sits at fillAmount ~1 from the live drag preview, so this is a no-op there;
        // it only matters for the entered cell's incoming bar. 0.08s verified geometrically
        // correct (grows from the seam inward) but was too short to read as motion at all --
        // sub-100ms changes get perceived as an instant switch regardless of direction.
        private const float CommitFillDuration = 0.18f;

        /// <summary>
        /// Highlights the block in a specified direction with a given pair color type.
        /// Every direction bar is the same capsule (pivot at the cell-center end, tip at the
        /// edge), just rotated per direction, so growing it normally means "grow from this
        /// cell's center outward to the edge". That's correct for the bar on the cell being
        /// LEFT (it's already been growing that way all through the live preview), but wrong
        /// for the bar on the cell being ENTERED: growing center-to-edge means the part that
        /// actually touches the previous cell is the last sliver to appear, so the seam still
        /// looks like it pops in no matter how long the tween runs. Pass
        /// <paramref name="growFromFarEdge"/> true for that entering-cell call so it fills
        /// edge-to-center instead -- the seam lights up immediately and the fill finishes
        /// toward the dot, reading as the stroke continuing rather than a new bar switching on.
        /// </summary>
        /// <param name="dir">The direction in which to highlight the block.</param>
        /// <param name="type">The pair color type used for the highlight color.</param>
        /// <param name="pairId">The pair identity being highlighted, independent of display color.</param>
        /// <param name="growFromFarEdge">True when this call is highlighting the cell being
        /// entered (the far/incoming side of the new segment) rather than the cell being left.</param>
        public void HighlightBlockDirection(Direction dir, PairColorType type, int pairId, bool growFromFarEdge = false)
        {
            int idx = (int)dir - 1;
            directionOwnerPairId[idx] = pairId;

            // Registered after the slot is claimed, so RefreshPathWash inside AddOccupant can
            // see that this pair now owns a bar here.
            AddOccupant(pairId, type);

            Color color = GamePlayController.Instance.GetColor(type);
            directionImages[idx].color = color;
            directionImages[idx].DOKill();

            if (growFromFarEdge)
            {
                // Driven live, every frame, by GamePlayController's drag preview
                // (SetDirectionFillAmount) for as long as this stays the entry edge of the
                // current last selected block -- an autonomous tween can't track where the
                // pointer actually is, which is exactly what looked wrong: the bar would
                // finish filling on its own schedule regardless of how far the pointer had
                // actually moved into the cell.
                SetBarFraction(idx, 0f, true);
            }
            else
            {
                int tweened = idx;
                DOTween.To(() => directionBarFraction[tweened],
                           value => SetBarFraction(tweened, value, false),
                           1f,
                           CommitFillDuration)
                       .SetTarget(directionImages[tweened]);
            }
        }

        /// <summary>
        /// Directly sets how much of a direction bar is drawn, no animation. Used by
        /// GamePlayController's live drag preview to keep the entry bar (the edge this cell
        /// was entered through) tracking the pointer's actual position every frame, and to
        /// snap it to fully connected once the path advances past this cell.
        /// </summary>
        public void SetDirectionFillAmount(Direction dir, float fillAmount)
        {
            int idx = (int)dir - 1;
            SetBarFraction(idx, fillAmount, directionBarFromFarEdge[idx]);
        }

        /// <summary>
        /// Reads how much of a direction bar is drawn -- used by GamePlayController on pointer
        /// release to check how far the entry edge had actually filled before deciding whether
        /// this cell counts as a real step or should be undone.
        /// </summary>
        public float GetDirectionFillAmount(Direction dir)
        {
            int idx = (int)dir - 1;
            return directionBarFraction[idx];
        }

        /// <summary>
        /// Live drag preview: shows this cell's <paramref name="dir"/> bar growing from the
        /// cell center outward as <paramref name="fraction"/> (0-1) increases, tracking the
        /// pointer's actual progress toward the neighboring cell before the step commits.
        /// Never touches a slot ProcessBlockStep/HighlightBlockDirection has already
        /// committed for this drag (directionOwnerPairId != 0), so a live preview can't
        /// clobber an already-drawn segment. The image stays active at all times (see
        /// SetBlock) -- visibility is purely its width, since toggling the GameObject
        /// active/inactive every frame during a drag is what caused the old flicker.
        /// </summary>
        public void SetDirectionPreview(Direction dir, float fraction, PairColorType type)
        {
            int idx = (int)dir - 1;
            if (directionOwnerPairId[idx] != 0) { return; }

            fraction = Mathf.Clamp01(fraction);
            SetBarFraction(idx, fraction, false);
            if (fraction > 0f)
            {
                directionImages[idx].color = GamePlayController.Instance.GetColor(type);
            }
        }

        /// <summary>
        /// Full reset of all highlight state regardless of owner. Only safe when the whole
        /// cell is being repurposed (pooling between levels) -- during gameplay use the
        /// pair-scoped overload instead so one pair can't clear another pair's highlight off
        /// a Mixed cell they both occupy.
        /// </summary>
        public void ResetAllHighlightDirection()
        {
            for (int i = 0; i < directionImages.Length; i++)
            {
                directionImages[i].DOKill();
                SetBarFraction(i, 0f, false);
                directionOwnerPairId[i] = 0;
            }

            ClearOccupants();
            ResetHighlightBlockBg();
        }

        /// <summary>
        /// Resets only the highlight state owned by <paramref name="pairId"/>. On a Normal
        /// cell (only ever one occupant) this behaves exactly like the full reset; on a
        /// Mixed cell it leaves any other pair's direction images untouched.
        /// </summary>
        public void ResetAllHighlightDirection(int pairId)
        {
            for (int i = 0; i < directionImages.Length; i++)
            {
                if (directionOwnerPairId[i] == pairId)
                {
                    directionImages[i].DOKill();
                    SetBarFraction(i, 0f, false);
                    directionOwnerPairId[i] = 0;
                }
            }

            // Unconditional now. This used to clear the wash only when the leaving pair happened
            // to be the last one to write its identity here, which on a shared cell meant
            // clearing the earlier occupant left a wash in the wrong colour and clearing the
            // later one stripped a wash the survivor still deserved. RefreshPathWash decides
            // from what is actually left.
            RemoveOccupant(pairId);
            RefreshPathWash();
        }

        /// <summary>
        /// Resets the highlight direction of the block in a specific direction.
        /// </summary>
        /// <param name="dir">The direction to reset the highlight for.</param>
        public void ResetHighlightDirection(Direction dir)
        {
            int idx = (int)dir - 1;
            int owner = directionOwnerPairId[idx];

            directionImages[idx].DOKill();
            SetBarFraction(idx, 0f, false);
            directionOwnerPairId[idx] = 0;

            // A pair that no longer owns any bar here has left the cell. Without this a
            // retreating path leaves its occupancy entry behind, and the cell goes on answering
            // "that pair is here" to the wash and to every ownership guard.
            if (owner != 0 && !OwnsAnyDirection(owner))
            {
                RemoveOccupant(owner);
            }

            RefreshPathWash();
        }

        public void HighlightBlock()
        {
            pairDotImage.transform.DOScale(1.3f, 0.35f);
        }

        public void ResetHighlightBlock()
        {
            pairDotImage.transform.DOScale(1f, 0.35f);
        }

        // blockBgHighlightImage is shared with the obstacle visuals (Blocked/Gate force it
        // to full opacity), so this must set alpha explicitly rather than "preserve
        // whatever's currently on the image" -- a pooled Block that was previously an
        // obstacle would otherwise leave this wash stuck at full opacity forever.
        private const float PathHighlightAlpha = 0.2f;

        public void HighlightBlockBg()
        {
            // A shared cell gets no wash at all -- see RefreshPathWash. Guarded here too
            // because OnPointerUp washes every cell of a committed path directly, and the
            // second pair to commit across a Mixed cell would otherwise paint over the first.
            if (occupantCount >= 2)
            {
                ResetHighlightBlockBg();
                return;
            }

            blockBgHighlightImage.gameObject.SetActive(true);

            Color color = GamePlayController.Instance.GetColor(HighlightedColorType);
            color.a = PathHighlightAlpha;
            blockBgHighlightImage.color = color;
        }

        public void ResetHighlightBlockBg()
        {
            blockBgHighlightImage.gameObject.SetActive(false);
        }

        public bool IsPairBlock
        {
            get { return isPairBlock; }
        }

        public PairColorType PairColorType
        {
            get { return pairColorType; }
        }

        // The most recent occupant -- what this cell reported before it tracked more than one,
        // and still exactly right for any cell only one path can be in. On a shared cell prefer
        // IsOccupiedBy/GetOccupantColorType: "who arrived last" is rarely the question.
        public PairColorType HighlightedColorType
        {
            get { return occupantCount > 0 ? occupantColorType[occupantCount - 1] : PairColorType.None; }
        }

        public int PairId { get { return pairId; } }

        public int SecondPairId { get { return secondPairId; } }

        /// <summary>Whether this cell is a shared destination -- a dot for two pairs.</summary>
        public bool IsSharedGoal { get { return isPairBlock && secondPairId != 0; } }

        /// <summary>
        /// Whether this cell is a dot belonging to <paramref name="askingPairId"/>. Use this rather
        /// than comparing <see cref="PairId"/>: a shared destination answers to two pairs, and
        /// PairId can only name one of them.
        /// </summary>
        public bool IsDotFor(int askingPairId)
        {
            if (!isPairBlock || askingPairId == 0) { return false; }
            return pairId == askingPairId || secondPairId == askingPairId;
        }

        public int HighlightedPairId
        {
            get { return occupantCount > 0 ? occupantPairId[occupantCount - 1] : 0; }
        }

        public BlockType BlockType { get { return blockType; } }

        public Direction ForcedExitDirection { get { return forcedExitDirection; } }

        // Only meaningful on a OneWay cell, but readable anywhere: LevelValidator checks for the
        // column being authored on a cell whose type ignores it, which is otherwise an
        // invisible rule (CanEnterFrom enforces it regardless of blockType, but only a OneWay
        // cell draws the marker).
        public Direction RequiredEntryDirection { get { return requiredEntryDirection; } }

        public int Row_ID { get { return row_ID; } }
        public int Coloum_ID { get { return coloum_ID; } }

        /// <summary>
        /// Resets all properties of the block to their default values.
        /// </summary>
        public void ResetBlock()
        {
            this.row_ID = -1;
            this.coloum_ID = -1;
            pairColorType = PairColorType.None;
            pairId = 0;
            secondPairId = 0;
            blockType = BlockType.Normal;
            wallMask = 0;
            requiredEntryDirection = Direction.None;
            forcedExitDirection = Direction.None;
            currentRotation = 0;
            isPairBlock = false;
            pairDotImage.transform.localScale = Vector3.zero;

            pairDotImage.gameObject.SetActive(false);
            ResetAllHighlightDirection();

            for (int i = 0; i < wallImages.Length; i++)
            {
                wallImages[i].gameObject.SetActive(false);
            }
            specialMarkerImage.gameObject.SetActive(false);
            specialMarkerImage.transform.localEulerAngles = Vector3.zero;

            // A Mixed cell swaps in the ring sprite, scales the marker and tints it neutral, so
            // a pooled cell has to be put back to the authored state or the next level's
            // checkpoint inherits a ring.
            specialMarkerImage.transform.localScale = Vector3.one;
            specialMarkerImage.sprite = defaultMarkerSprite;
            specialMarkerImage.color = Color.white;

            lengthLabel.gameObject.SetActive(false);
        }
    }
}