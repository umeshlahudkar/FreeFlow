using FreeFlow.Enums;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Represents a game block, single unit on the game board
    /// </summary>
    public class Block : MonoBehaviour
    {
        [SerializeField] private Image blockBgHighlightImage;

        [SerializeField] private Image[] directionImages;

        // Everything below is a MECHANIC-SPECIFIC visual: most cells on a board are Normal and
        // need none of them, so each is a source prefab instantiated as a child of this cell only
        // the first time a mechanic that needs it actually shows up (see the EnsureX() methods
        // below) rather than a pre-built, mostly-inactive child on every one of a board's cells.
        // The runtime instance fields below the prefab references start null and stay null on a
        // cell that never needs that visual.

        // The single pair dot. Not needed on any non-pair cell, and not needed on a shared
        // destination either -- that hides the dot and shows sharedDotVisual's cluster instead.
        [SerializeField] private GameObject pairDotVisual;
        private Image pairDotImage;

        // One per colour that can share a destination, laid out as a diamond (0 left, 1 right,
        // 2 top, 3 below) so enabling the first N gives a pair, then a triangle, then a full
        // diamond -- an arrangement authored by dragging in the editor, not computed from
        // trigonometry. Their anchors are fractions of the cell, so they still follow the board
        // through every size.
        [SerializeField] private GameObject sharedDotVisual;
        private RectTransform sharedDotGroup;
        private Image[] sharedDotImages;

        // Edge bars, indexed like directionImages ((int)Direction - 1: Left=0, Right=1,
        // Up=2, Down=3). Reused for two mechanics that both mark a single edge of the cell:
        // dark for a wallMask bit, green for the one allowed entry edge on a OneWay cell.
        //
        // Each one is anchored to its own edge and centred ON it, so it straddles the boundary
        // into the neighbouring cell. A wall belongs to the edge between two cells, and both
        // cells draw it (BoardGenerator.NormalizeWalls), so the two copies land exactly on top of
        // each other and the player sees one rectangle in the gap -- not a box inside each cell,
        // which is what these looked like while all four were centred 100x100 squares.
        [SerializeField] private GameObject wallVisual;
        private RectTransform wallGroup;
        private Image[] wallImages;

        // The one-way marker has its own Image because its art is directional: a bar with chevrons
        // pointing INTO the cell, i.e. the way a path must be travelling to enter. A wall bar is
        // symmetric and works in any of the four slots above; this one has to be positioned and
        // rotated per edge, which a slot pinned to a fixed edge cannot do.
        [SerializeField] private GameObject oneWayVisual;
        private Image oneWayImage;

        // Center markers for the mechanics that repurpose PairId as "which pair this applies
        // to" and want a glyph rather than a border: Checkpoint, Arrow, Bridge. A cell is
        // exactly one BlockType, so at most one of these three is ever instantiated. Each is its
        // own dedicated prefab with its own sprite already authored on it -- unlike the wall or
        // permission-border visuals, these three have nothing in common a shared prefab would
        // actually be reusing beyond "an Image", and sharing one meant swapping sprite, tint,
        // rotation and scale by hand every time the block type changed.
        [SerializeField] private GameObject checkpointMarkerVisual;
        private Image checkpointMarkerImage;

        [SerializeField] private GameObject arrowMarkerVisual;
        private Image arrowMarkerImage;

        [SerializeField] private GameObject bridgeMarkerVisual;
        private Image bridgeMarkerImage;

        // Rounded-rect border split into one angular slice per named pair colour, solid where
        // that colour may pass and dashed where it may not. Backs ForbiddenForPair and
        // AllowedForPairs in place of a center glyph, so the cell reads as "which colours get
        // through" rather than needing a legend, and the two named colours (pairId,
        // secondPairId) show as two mitred halves instead of a ring plus a hand-cut arc.
        [SerializeField] private GameObject permissionBorderVisual;
        private PermissionBorderView permissionBorderView;

        // Art for the cell types that paint the whole tile, and for the two edge bars.
        [SerializeField] private Sprite blockedSprite;
        // Two sprites, because a wall bar is stretched along the edge it sits on: the shading has
        // to run across the bar's THICKNESS, and that is the vertical axis for the Up/Down bars and
        // the horizontal one for Left/Right. One sprite would be correct for one pair and smeared
        // along the other. wallSpriteVertical is the exact transpose of wallSprite, which is also
        // what keeps the lighting consistent -- see Tools/make_wall_sprite.py.
        [SerializeField] private Sprite wallSprite;
        [SerializeField] private Sprite wallSpriteVertical;
        [SerializeField] private Sprite oneWaySprite;

        // Wall bars were rgb(0.05) against a pure-black cell background: the rule worked and
        // no player could see it. Light enough to read as a wall, dark enough that it doesn't
        // compete with a pair color.
        // A warm bone, and deliberately brighter than the board's grid lines rather than darker.
        // At 0.45 grey the bar was dimmer than the white line it straddles, so it read as a gap in
        // the grid -- a shadow between two cells -- instead of something built on top of it.
        private static readonly Color WallColor = new Color(0.93f, 0.89f, 0.81f, 1f);
        // The tint is a CEILING on how bright the blocked tile can get: the sprite is white with
        // its shape in alpha, so lowering alpha only fades a pixel toward the dark board behind,
        // never past the tint. At 0.28 the hatch stripes and the slab between them were separated
        // by too little to read as stripes at all; 0.42 opens that gap while keeping the cell
        // clearly deader than any path colour.
        private static readonly Color BlockedColor = new Color(0.42f, 0.42f, 0.42f, 1f);
        private static readonly Color ArrowMarkerColor = new Color(1f, 1f, 1f, 0.85f);

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
        private int thirdPairId;
        private int fourthPairId;

        // Which pairs currently have a path through this cell, oldest first, and how each one
        // is drawn. This used to be a single (highlightedPairId, highlightedColorType) pair,
        // which was correct for every cell type except the ones that are shared: a Bridge or a
        // shared destination holds two pairs, but the second one to arrive silently overwrote the
        // first's identity, so the cell's wash showed the wrong colour, a reset by the wrong pair
        // cleared it, and every guard that asked "whose path is this?" got the last writer rather
        // than the truth.
        //
        // Four pairs, which is the geometric ceiling rather than a taste call: a path that ENDS in
        // a cell claims the one edge it arrived through, and a cell has four edges, so at most four
        // pairs can finish in the same cell. That is exactly what a four-colour shared destination
        // asks for. It was three, sized for a bridge.
        //
        // Raising it does not loosen anything: a bridge is capped by axis ownership, which does not
        // consult this number. The real constraint on how many PATHS fit is still the four
        // direction slots below -- a path crossing a cell claims two of them, in and out. See
        // CanAcceptEntry, where that geometry is enforced.

        private const int MaxOccupants = 4;
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

        // Which pair currently owns each direction-image slot (index = (int)dir - 1), 0 =
        // unowned. On a Normal cell there's only ever one occupant so this changes nothing
        // observable; on a shared cell it lets ResetAllHighlightDirection(pairId) clear only
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

        // Lazily instantiates the mechanic-specific visuals, one per kind, the first time a cell
        // that actually needs one asks for it -- see the field block above. Every Ensure*
        // guards on its source-prefab reference rather than assuming it's wired, same defensive
        // posture the pre-instantiated fields had before this was on-demand.

        // Draw order is sibling order (later = on top). Mechanic visuals (wall, one-way, the
        // three center markers, the permission border) all insert right after BgHighlight --
        // the backdrop layer -- so they never depend on one another's creation order: whichever
        // of them exists, they end up adjacent, below the direction bars. The dot instead goes
        // to the very end via SetAsLastSibling, so it stays the frontmost thing on the cell
        // (the one landmark that should never be covered by a path or a mechanic) regardless of
        // what else gets added to this cell before or after it, including a wall arriving later
        // through AddWall.
        private const int MechanicSiblingIndex = 1;

        private Image EnsurePairDot()
        {
            if (pairDotImage == null && pairDotVisual != null)
            {
                pairDotImage = Instantiate(pairDotVisual, transform).GetComponent<Image>();
                pairDotImage.transform.SetAsLastSibling();
            }
            return pairDotImage;
        }

        private RectTransform EnsureSharedDotGroup()
        {
            if (sharedDotGroup == null && sharedDotVisual != null)
            {
                sharedDotGroup = Instantiate(sharedDotVisual, transform).GetComponent<RectTransform>();
                sharedDotGroup.SetAsLastSibling();
                sharedDotImages = new Image[4];
                for (int i = 0; i < sharedDotImages.Length; i++)
                {
                    Transform child = sharedDotGroup.Find("SharedDot" + i);
                    sharedDotImages[i] = child != null ? child.GetComponent<Image>() : null;
                }
            }
            return sharedDotGroup;
        }

        private RectTransform EnsureWallGroup()
        {
            if (wallGroup == null && wallVisual != null)
            {
                wallGroup = Instantiate(wallVisual, transform).GetComponent<RectTransform>();
                wallGroup.SetSiblingIndex(MechanicSiblingIndex);
                wallImages = new Image[4];
                wallImages[(int)Direction.Left - 1] = FindImage(wallGroup, "LeftWallImage");
                wallImages[(int)Direction.Right - 1] = FindImage(wallGroup, "RightWallImage");
                wallImages[(int)Direction.Up - 1] = FindImage(wallGroup, "UpWallImage");
                wallImages[(int)Direction.Down - 1] = FindImage(wallGroup, "DownWallImage");

                // Sized immediately rather than waiting for the next resize event -- a wall
                // added mid-game (AddWall, after this cell's own SetBlock already ran) has no
                // resize coming to size it for the first time.
                ApplyWallGeometry();
            }
            return wallGroup;
        }

        private static Image FindImage(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            return child != null ? child.GetComponent<Image>() : null;
        }

        private Image EnsureOneWayImage()
        {
            if (oneWayImage == null && oneWayVisual != null)
            {
                oneWayImage = Instantiate(oneWayVisual, transform).GetComponent<Image>();
                oneWayImage.transform.SetSiblingIndex(MechanicSiblingIndex);
            }
            return oneWayImage;
        }

        private Image EnsureCheckpointMarker()
        {
            if (checkpointMarkerImage == null && checkpointMarkerVisual != null)
            {
                checkpointMarkerImage = Instantiate(checkpointMarkerVisual, transform).GetComponent<Image>();
                checkpointMarkerImage.transform.SetSiblingIndex(MechanicSiblingIndex);
            }
            return checkpointMarkerImage;
        }

        private Image EnsureArrowMarker()
        {
            if (arrowMarkerImage == null && arrowMarkerVisual != null)
            {
                arrowMarkerImage = Instantiate(arrowMarkerVisual, transform).GetComponent<Image>();
                arrowMarkerImage.transform.SetSiblingIndex(MechanicSiblingIndex);
            }
            return arrowMarkerImage;
        }

        private Image EnsureBridgeMarker()
        {
            if (bridgeMarkerImage == null && bridgeMarkerVisual != null)
            {
                bridgeMarkerImage = Instantiate(bridgeMarkerVisual, transform).GetComponent<Image>();
                bridgeMarkerImage.transform.SetSiblingIndex(MechanicSiblingIndex);
            }
            return bridgeMarkerImage;
        }

        private PermissionBorderView EnsurePermissionBorder()
        {
            if (permissionBorderView == null && permissionBorderVisual != null)
            {
                permissionBorderView = Instantiate(permissionBorderVisual, transform).GetComponent<PermissionBorderView>();
                permissionBorderView.transform.SetSiblingIndex(MechanicSiblingIndex);
            }
            return permissionBorderView;
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
        /// Public because clearing a path one bar at a time -- so a shared cell keeps the other
        /// pair's bar -- leaves the wash to be re-derived afterwards.
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
        /// <param name="thirdPairId">A third pair this cell is a dot for, or 0.</param>
        /// <param name="fourthPairId">A fourth pair this cell is a dot for, or 0.</param>
        /// <param name="blockType">The obstacle/mechanic type of this cell.</param>
        /// <param name="wallMask">Bitmask of walled edges (Left=1, Right=2, Up=4, Down=8).</param>
        /// <param name="requiredEntryDirection">Only used when blockType is OneWay.</param>
        /// <param name="forcedExitDirection">Only used when blockType is Arrow.</param>
        /// <param name="rowIndex">The row index of the block.</param>
        /// <param name="coloumIndex">The column index of the block.</param>
        public void SetBlock(PairColorType type, int pairId, int secondPairId, int thirdPairId, int fourthPairId, BlockType blockType, int wallMask, Direction requiredEntryDirection, Direction forcedExitDirection, int rowIndex, int coloumIndex)
        {
            this.row_ID = rowIndex;
            this.coloum_ID = coloumIndex;

            ApplyHighlightGeometry();
            pairColorType = type;
            this.pairId = pairId;
            this.secondPairId = secondPairId;
            this.thirdPairId = thirdPairId;
            this.fourthPairId = fourthPairId;
            this.blockType = blockType;
            this.wallMask = wallMask;
            this.requiredEntryDirection = requiredEntryDirection;
            this.forcedExitDirection = forcedExitDirection;

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

                if (IsSharedGoal)
                {
                    // more than one colour finishes here, so the cluster of circles shows
                    // instead of the single dot -- which is simply never instantiated here
                    ShowSharedDotCluster();
                }
                else
                {
                    Image dot = EnsurePairDot();
                    if (dot != null)
                    {
                        dot.gameObject.SetActive(true);
                        dot.color = GamePlayController.Instance.GetColor(type);

                        dot.transform.localScale = Vector3.zero;
                        dot.transform.DOScale(1, 0.5f);
                    }
                }
            }

            // full-cell fill for an obstacle that blocks the whole cell
            if (blockType == BlockType.Blocked)
            {
                SetObstacleVisual(blockedSprite, BlockedColor);
            }
            else if (blockType == BlockType.Checkpoint)
            {
                ShowCheckpointMarker();
            }
            else if (blockType == BlockType.ForbiddenForPair)
            {
                ShowPermissionBorder(namedColoursAreAllowed: false);
            }
            else if (blockType == BlockType.AllowedForPairs)
            {
                ShowPermissionBorder(namedColoursAreAllowed: true);
            }
            else if (blockType == BlockType.Arrow)
            {
                ShowArrowMarker();
            }
            else if (blockType == BlockType.Bridge)
            {
                ShowBridgeMarker();
            }

            // wall bars: independent of blockType, one per walled edge. Instantiated only for a
            // cell that actually has one -- the overwhelming majority never do. The group carries
            // all four, so it goes on only for a cell that has any -- and each bar is set
            // explicitly either way, since ShowWallBar alone would leave a previously-shown edge on.
            if (wallMask != 0) { EnsureWallGroup(); }
            if (wallGroup != null) { wallGroup.gameObject.SetActive(wallMask != 0); }

            Direction[] edges = { Direction.Left, Direction.Right, Direction.Up, Direction.Down };
            for (int i = 0; i < edges.Length; i++)
            {
                if (HasWall(edges[i]))
                {
                    ShowWallBar(edges[i]);
                }
                else if (wallImages != null && i < wallImages.Length && wallImages[i] != null)
                {
                    wallImages[i].gameObject.SetActive(false);
                }
            }

            // one-way: mark the single edge a path may enter through, on the opposite side
            // of the direction it must be travelling (e.g. "must be moving Down" means it
            // enters via the cell's Up-facing edge)
            if (blockType == BlockType.OneWay && requiredEntryDirection != Direction.None)
            {
                ShowOneWayMarker();
            }
        }

        // How thick a wall bar is, as a fraction of the cell. Proportional rather than fixed so a
        // wall reads the same weight on a 4x4 board and an 8x8 one.
        private const float WallThicknessFraction = 0.08f;

        // The one-way bar carries chevrons, so it needs more room than a plain wall bar.
        private const float OneWayThicknessFraction = 0.3f;

        /// <summary>
        /// Sizes the four edge bars from the cell's current size. Only the thickness is set here --
        /// each bar's anchors already pin it along its own edge and centre it on the boundary --
        /// and it is re-applied whenever the cell is resized, since the board measures itself at
        /// runtime and again on every rotation or window resize.
        /// </summary>
        /// <summary>
        /// Turns on the bar for one walled edge and dresses it: the sprite for that edge's
        /// orientation, and the shared wall tint.
        ///
        /// <see cref="Image.Type.Simple"/> rather than Sliced. The bar's thickness is a fraction of
        /// the cell, so it changes with the board, while a 9-slice border is pinned to a fixed unit
        /// size -- the two disagree at every board size but one. The bevel is baked proportionally
        /// into the sprite instead, which is why the borders in the .meta are zero.
        /// </summary>
        private void ShowWallBar(Direction edge)
        {
            EnsureWallGroup();

            int idx = (int)edge - 1;
            if (wallImages == null || idx < 0 || idx >= wallImages.Length) { return; }
            if (wallImages[idx] == null) { return; }

            bool vertical = edge == Direction.Left || edge == Direction.Right;
            Sprite sprite = vertical ? wallSpriteVertical : wallSprite;

            wallImages[idx].gameObject.SetActive(true);
            wallImages[idx].sprite = sprite != null ? sprite : wallSprite;
            wallImages[idx].type = Image.Type.Simple;
            wallImages[idx].color = WallColor;
        }

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

        /// <summary>
        /// Stretches the path wash / obstacle fill to the whole cell.
        ///
        /// The cell's own background and the grid lines are not drawn here, and not drawn per
        /// cell at all: one sprite behind the whole board carries every line and fill, for every
        /// size the game ships (see BoardGenerator.gridSizeSprites). Cells used to own a
        /// background of their own as a fallback for a grid size with no board art, but every
        /// size from 4x4 to 12x12 has art, so that fallback never drew and has been removed.
        /// </summary>
        private void ApplyHighlightGeometry()
        {
            RectTransform highlightRect = blockBgHighlightImage.rectTransform;
            highlightRect.offsetMin = Vector2.zero;
            highlightRect.offsetMax = Vector2.zero;
        }

        private void SetObstacleVisual(Sprite sprite, Color color)
        {
            blockBgHighlightImage.gameObject.SetActive(true);
            blockBgHighlightImage.sprite = sprite;
            blockBgHighlightImage.color = color;
        }

        /// <summary>
        /// Shows the checkpoint flag, tinted to the checkpoint's own pair -- the only cue telling
        /// two checkpoints for two different pairs apart on the same board.
        /// Assumes pairId doubles as a valid PairColorType value (true for every level authored
        /// so far); a level giving a Checkpoint cell a pairId outside 1-9 would need a real
        /// pairId-to-color lookup instead.
        /// </summary>
        private void ShowCheckpointMarker()
        {
            if (EnsureCheckpointMarker() == null) { return; }

            checkpointMarkerImage.gameObject.SetActive(true);

            Color color = GamePlayController.Instance.GetColor((PairColorType)pairId);
            color.a = 1f;
            checkpointMarkerImage.color = color;
        }

        /// <summary>
        /// Shows the arrow marker, pointing the way a path is forced to leave. Neutral white: the
        /// rule applies to every pair, so tinting it to one would be a lie. This is the one marker
        /// whose meaning is its rotation, which is why the arrow glyph is reserved for it -- a
        /// OneWay cell marks its entry EDGE instead, so the two directional mechanics never look
        /// alike. The base sprite points UP; MarkerRotationFor turns it from there.
        /// </summary>
        private void ShowArrowMarker()
        {
            if (EnsureArrowMarker() == null) { return; }

            arrowMarkerImage.gameObject.SetActive(true);
            arrowMarkerImage.transform.localEulerAngles = new Vector3(0, 0, MarkerRotationFor(forcedExitDirection));
            arrowMarkerImage.color = ArrowMarkerColor;
        }

        /// <summary>
        /// Shows the bridge marker: a crossing, marking a cell two pairs may share on strict
        /// terms. Plain white -- the rule applies to whichever two pairs cross here, so tinting it
        /// to either would be a lie, same reasoning as the arrow.
        /// </summary>
        private void ShowBridgeMarker()
        {
            if (EnsureBridgeMarker() == null) { return; }

            bridgeMarkerImage.gameObject.SetActive(true);
            bridgeMarkerImage.color = Color.white;
        }

        /// <summary>
        /// Puts the one-way bar on the edge a path must enter through, with its chevrons pointing
        /// the way the path has to be travelling.
        ///
        /// The art is a horizontal bar authored for the TOP edge, so it is placed by moving the
        /// rect to the centre of the target edge and rotating it about its own centre. Position and
        /// rotation are independent in a RectTransform, which is what makes one image serve all
        /// four edges: rotating a bar 90 degrees turns it vertical, and the anchored position then
        /// decides which edge it sits on.
        ///
        /// This no longer shares the wall images. A wall bar is symmetric, so it can live in a slot
        /// pinned to a fixed edge; chevrons cannot. Sharing also meant a cell that was walled AND
        /// one-way on the same edge had to pick one sprite to lose.
        /// </summary>
        private void ShowOneWayMarker()
        {
            if (EnsureOneWayImage() == null) { return; }

            RectTransform cellRect = transform as RectTransform;
            RectTransform bar = oneWayImage.rectTransform;
            if (cellRect == null) { return; }

            float cell = Mathf.Min(cellRect.rect.width, cellRect.rect.height);
            float thickness = Mathf.Max(4f, cell * OneWayThicknessFraction);
            float half = (cell - thickness) * 0.5f;

            // the edge the path comes through is opposite the direction it must be travelling
            Direction entryEdge = OppositeDirection(requiredEntryDirection);

            Vector2 position = Vector2.zero;
            float rotation = 0f;

            switch (entryEdge)
            {
                case Direction.Up: position = new Vector2(0f, half); rotation = 0f; break;
                case Direction.Down: position = new Vector2(0f, -half); rotation = 180f; break;
                case Direction.Left: position = new Vector2(-half, 0f); rotation = 90f; break;
                case Direction.Right: position = new Vector2(half, 0f); rotation = 270f; break;
                default: return;
            }

            bar.sizeDelta = new Vector2(cell, thickness);
            bar.anchoredPosition = position;
            bar.localEulerAngles = new Vector3(0f, 0f, rotation);

            oneWayImage.gameObject.SetActive(true);
            oneWayImage.sprite = oneWaySprite;
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
        /// Draws the second pair's colour over HALF this cell's dot, so a shared destination reads
        /// as one dot split between two colours rather than as any of the "more than one path here"
        /// markers. The art is the left half of a disc; the dot underneath supplies the other.
        /// </summary>
        /// <summary>
        /// Whether <paramref name="askingPairId"/> is one of the one or two pairs this cell names
        /// through <see cref="pairId"/> and <see cref="secondPairId"/>.
        ///
        /// Shared by both permission rules, which read the same two ids and only differ in what
        /// they conclude: <see cref="BlockType.ForbiddenForPair"/> refuses the pairs it names,
        /// <see cref="BlockType.AllowedForPairs"/> refuses the ones it does not.
        ///
        /// A cell with no pairId names nobody, so a forbidden cell becomes a no-op and a permit
        /// cell becomes <see cref="BlockType.Blocked"/> by another name. Both are deliberate --
        /// each rule fails in its own safe direction -- and LevelValidator errors on either.
        /// </summary>
        private bool NamesPair(int askingPairId)
        {
            if (askingPairId == 0) { return false; }
            return askingPairId == pairId
                || (secondPairId != 0 && askingPairId == secondPairId);
        }

        /// <summary>
        /// Whether a block type reads <see cref="secondPairId"/> as "a second pair this rule is
        /// about" rather than "a second pair this cell is a dot for".
        ///
        /// The column carries both meanings, which has already caused two bugs -- a level label
        /// claiming a mechanic it did not have, and a validator rejecting a valid board. Any code
        /// that reads secondPairId without first checking isPairBlock has to ask this instead of
        /// naming the types inline, so a third rule using the column cannot be half-adopted.
        /// </summary>
        public static bool SecondIdNamesAPair(BlockType type)
        {
            return type == BlockType.ForbiddenForPair || type == BlockType.AllowedForPairs;
        }

        /// <summary>
        /// Draws the permission border: one slice per named colour (pairId, and secondPairId
        /// when the cell names two), solid if <paramref name="namedColoursAreAllowed"/> (an
        /// AllowedForPairs cell) or dashed if not (a ForbiddenForPair cell).
        /// </summary>
        private void ShowPermissionBorder(bool namedColoursAreAllowed)
        {
            if (EnsurePermissionBorder() == null) { return; }

            int count = secondPairId != 0 ? 2 : 1;
            Color[] colors = new Color[count];
            bool[] allowed = new bool[count];

            // Same (PairColorType)pairId assumption as every other marker -- see ShowSpecialMarker.
            colors[0] = GamePlayController.Instance.GetColor((PairColorType)pairId);
            allowed[0] = namedColoursAreAllowed;

            if (count == 2)
            {
                colors[1] = GamePlayController.Instance.GetColor((PairColorType)secondPairId);
                allowed[1] = namedColoursAreAllowed;
            }

            permissionBorderView.SetSegments(colors, allowed);
        }

        /// <summary>
        /// How many colours finish on this cell: 1 for an ordinary dot, up to
        /// <see cref="MaxOccupants"/> for a shared destination.
        /// </summary>
        private int SharedPairCount()
        {
            if (fourthPairId != 0) { return 4; }
            if (thirdPairId != 0) { return 3; }
            if (secondPairId != 0) { return 2; }
            return 1;
        }

        private int SharedPairIdAt(int index)
        {
            switch (index)
            {
                case 0: return pairId;
                case 1: return secondPairId;
                case 2: return thirdPairId;
                default: return fourthPairId;
            }
        }

        /// <summary>
        /// Shows a shared destination: the group, plus one circle per colour finishing here.
        ///
        /// Where the circles sit is AUTHORED, not computed -- this only decides how many are on.
        /// The four are laid out as a diamond in the prefab (0 left, 1 right, 2 top, 3 bottom), so
        /// taking the first N in order gives a horizontal pair at two, an upward triangle at three
        /// and the full diamond at four, and each arrangement is something you can see and drag
        /// rather than read out of trigonometry.
        ///
        /// The cost of authoring it is that the circles are one fixed size at every count, where
        /// the ring maths they replaced shrank them as colours were added to keep the cluster
        /// tangent. Their anchors are fractions of the cell, so they still follow the board through
        /// every size and resize. The sprite is taken from the pair dot so the circles are the same
        /// circle the rest of the board draws.
        /// </summary>
        private void ShowSharedDotCluster()
        {
            EnsureSharedDotGroup();
            if (sharedDotGroup != null) { sharedDotGroup.gameObject.SetActive(true); }
            if (sharedDotImages == null) { return; }

            int count = Mathf.Min(SharedPairCount(), sharedDotImages.Length);
            Sprite dotSprite = PairDotSprite();

            for (int i = 0; i < sharedDotImages.Length; i++)
            {
                Image circle = sharedDotImages[i];
                if (circle == null) { continue; }

                // Set on BOTH branches rather than only when showing one: a cell is not always
                // freshly instantiated (a resize re-runs this), so a dot left over from a
                // four-colour cell has to be switched off for a two-colour one.
                circle.gameObject.SetActive(i < count);
                if (i >= count) { continue; }

                circle.sprite = dotSprite != null ? dotSprite : circle.sprite;

                // Same (PairColorType)pairId assumption the markers make -- see ShowSpecialMarker.
                Color color = GamePlayController.Instance.GetColor((PairColorType)SharedPairIdAt(i));
                color.a = 1f;
                circle.color = color;

                circle.transform.localScale = Vector3.zero;
                circle.transform.DOScale(1f, 0.5f);
            }
        }

        /// <summary>
        /// The pair dot's authored sprite, read off the SOURCE prefab rather than a live
        /// instance -- a shared destination never instantiates its own single dot (see SetBlock),
        /// so there is no <see cref="pairDotImage"/> instance to read this off on that cell.
        /// </summary>
        private Sprite PairDotSprite()
        {
            if (pairDotVisual == null) { return null; }
            Image source = pairDotVisual.GetComponent<Image>();
            return source != null ? source.sprite : null;
        }

        private void ScaleSharedDotCluster(float target)
        {
            if (sharedDotImages == null) { return; }

            for (int i = 0; i < sharedDotImages.Length; i++)
            {
                if (sharedDotImages[i] == null) { continue; }
                if (!sharedDotImages[i].gameObject.activeSelf) { continue; }

                sharedDotImages[i].transform.DOScale(target, 0.35f);
            }
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
        /// the direction-dependent rules (walls, one-way), checked separately in GetDirection.
        /// </summary>
        public bool CanEnter(int enteringPairId)
        {
            if (blockType == BlockType.Blocked) { return false; }
            if (blockType == BlockType.ForbiddenForPair && NamesPair(enteringPairId)) { return false; }
            if (blockType == BlockType.AllowedForPairs && !NamesPair(enteringPairId)) { return false; }
            return true;
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

            return true;
        }

        /// <summary>
        /// Whether <paramref name="enteringPairId"/> may enter while moving in
        /// <paramref name="incomingDirection"/>, given who is already here. Only a Bridge says no:
        /// it carries one lane per axis, so a second pair crossing the same way has nowhere to go.
        /// </summary>
        public bool CanAcceptEntry(Direction incomingDirection, int enteringPairId)
        {
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
        /// </summary>
        public bool IsShareable
        {
            get
            {
                return blockType == BlockType.Bridge
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

            // If this cell's own level data named no wall at all, SetBlock never called
            // EnsureWallGroup, so the source prefab hasn't been instantiated for this cell yet --
            // ShowWallBar below is about to do that for the first time. SetBlock's own loop is
            // what normally turns off the three sides a cell doesn't use, right after every wall
            // group gets created; that loop already ran (and found nothing to show) before this
            // cell ever had a reason to instantiate one, so nothing has told the OTHER three bars
            // to be inactive -- they come up however the source prefab authored them, which is
            // exactly the "4 sides lit up when only 1 should be" bug this guards against.
            bool groupIsNew = wallGroup == null;

            wallMask |= bit;

            // Through ShowWallBar, not by hand: this used to set only the tint, so a wall added
            // during play came up as an untextured quad while a wall present at level load came up
            // textured. Two ways to draw the same thing is one too many.
            ShowWallBar(dir);

            if (groupIsNew)
            {
                Direction[] edges = { Direction.Left, Direction.Right, Direction.Up, Direction.Down };
                for (int i = 0; i < edges.Length; i++)
                {
                    if (edges[i] == dir || HasWall(edges[i])) { continue; }

                    int idx = (int)edges[i] - 1;
                    if (wallImages != null && idx >= 0 && idx < wallImages.Length && wallImages[idx] != null)
                    {
                        wallImages[idx].gameObject.SetActive(false);
                    }
                }
            }

            // This cell's own level data may have named no wall at all, in which case SetBlock
            // never turned the group on (or, now, never even instantiated it) -- a wall arriving
            // here afterward, mirrored from a neighbour by NormalizeWalls, still needs it visible.
            if (wallGroup != null) { wallGroup.gameObject.SetActive(true); }
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

            if (blockType == BlockType.OneWay && requiredEntryDirection != Direction.None)
            {
                ShowOneWayMarker();
            }
        }

        private void SetBarFraction(int idx, float fraction, bool fromFarEdge)
        {
            directionBarFraction[idx] = Mathf.Clamp01(fraction);
            directionBarFromFarEdge[idx] = fromFarEdge;
            ApplyBarGeometry(idx);
        }

        /// <summary>
        /// Highlights the block in a specified direction with a given pair color type.
        /// Every direction bar is the same capsule (pivot at the cell-center end, tip at the
        /// edge), just rotated per direction, so growing it normally means "grow from this
        /// cell's center outward to the edge". That's correct for the bar on the cell being
        /// LEFT (it's already been growing that way all through the live preview), but wrong
        /// for the bar on the cell being ENTERED: growing center-to-edge means the part that
        /// actually touches the previous cell is the last sliver to appear, so the seam would
        /// still look like it pops in. Pass <paramref name="growFromFarEdge"/> true for that
        /// entering-cell call so it fills edge-to-center instead -- the seam lights up
        /// immediately and the fill finishes toward the dot, reading as the stroke continuing
        /// rather than a new bar switching on.
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

            if (growFromFarEdge)
            {
                // Driven live, every frame, by GamePlayController's drag preview
                // (SetDirectionFillAmount) for as long as this stays the entry edge of the
                // current last selected block.
                SetBarFraction(idx, 0f, true);
            }
            else
            {
                SetBarFraction(idx, 1f, false);
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
        /// Full reset of all highlight state regardless of owner. During gameplay always use the
        /// pair-scoped overload instead, so one pair cannot clear another pair's highlight off a
        /// cell they both occupy.
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
        /// shared cell it leaves any other pair's direction images untouched.
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
            // a shared destination hides the single dot, so the pulse has to move to the cluster
            if (IsSharedGoal) { ScaleSharedDotCluster(1.3f); return; }

            pairDotImage.transform.DOScale(1.3f, 0.35f);
        }

        public void ResetHighlightBlock()
        {
            if (IsSharedGoal) { ScaleSharedDotCluster(1f); return; }

            pairDotImage.transform.DOScale(1f, 0.35f);
        }

        // blockBgHighlightImage is shared with the obstacle visual (Blocked forces it
        // to full opacity), so this must set alpha explicitly rather than "preserve
        // whatever's currently on the image" -- a pooled Block that was previously an
        // obstacle would otherwise leave this wash stuck at full opacity forever.
        private const float PathHighlightAlpha = 0.2f;

        public void HighlightBlockBg()
        {
            // A shared cell gets no wash at all -- see RefreshPathWash. Guarded here too
            // because OnPointerUp washes every cell of a committed path directly, and the
            // second pair to commit across a shared cell would otherwise paint over the first.
            if (occupantCount >= 2)
            {
                ResetHighlightBlockBg();
                return;
            }

            blockBgHighlightImage.gameObject.SetActive(true);

            // A plain fill: this image is shared with the obstacle art, so a cell that was drawn
            // as blocked has to drop that sprite before it can carry a path wash.
            blockBgHighlightImage.sprite = null;

            Color color = GamePlayController.Instance.GetColor(HighlightedColorType);
            color.a = PathHighlightAlpha;
            blockBgHighlightImage.color = color;
        }

        public void ResetHighlightBlockBg()
        {
            blockBgHighlightImage.gameObject.SetActive(false);
        }

        // A plain overlay built at runtime rather than a serialized/instantiated visual like the
        // mechanic art above: it has no sprite and no per-type variation, and every cell can hit
        // it regardless of BlockType, so there is nothing a source prefab would be reusing.
        // Kept separate from blockBgHighlightImage, which already carries obstacle art and the
        // path wash -- flashing that one would mean snapshotting and restoring whatever it was
        // showing, for a cell that may be a Blocked tile, a wall-adjacent Normal cell, or mid
        // path wash.
        private Image invalidMoveFlashImage;
        private static readonly Color InvalidMoveFlashColor = new Color(1f, 0.25f, 0.25f, 0f);
        private const float InvalidMoveFlashAlpha = 0.55f;

        private void EnsureInvalidMoveFlashImage()
        {
            if (invalidMoveFlashImage != null) { return; }

            GameObject go = new GameObject("InvalidMoveFlash", typeof(RectTransform), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            invalidMoveFlashImage = go.GetComponent<Image>();
            invalidMoveFlashImage.color = InvalidMoveFlashColor;
            invalidMoveFlashImage.raycastTarget = false;

            // Drawn above every mechanic marker, wall bar and the path wash so the blink reads
            // clearly no matter what the cell already looks like.
            rt.SetAsLastSibling();
        }

        /// <summary>
        /// Blinks the cell red on a loop to tell the player a drag onto it was rejected -- fully
        /// blocked, wrong pair colour/permission, a one-way entered from the wrong side, a taken
        /// bridge lane, an illegal arrow chain, or a self-crossing path. Not used for a wall on
        /// the shared edge -- see <see cref="PlayInvalidWallFeedback"/>, which blinks the wall
        /// itself, since the wall rather than either cell is what refused the step. Keeps looping
        /// until <see cref="StopInvalidMoveFeedback"/> is called; callers own that lifetime
        /// (start while the pointer sits on the rejected cell, stop the moment it leaves).
        /// </summary>
        public void PlayInvalidMoveFeedback()
        {
            EnsureInvalidMoveFlashImage();

            invalidMoveFlashImage.DOKill();
            invalidMoveFlashImage.color = InvalidMoveFlashColor;
            invalidMoveFlashImage.DOFade(InvalidMoveFlashAlpha, 0.09f).SetLoops(-1, LoopType.Yoyo);
        }

        public void StopInvalidMoveFeedback()
        {
            if (invalidMoveFlashImage == null) { return; }

            invalidMoveFlashImage.DOKill();
            invalidMoveFlashImage.color = InvalidMoveFlashColor;
        }

        private static readonly Color InvalidWallFlashColor = new Color(1f, 0.2f, 0.2f, 1f);

        /// <summary>
        /// Blinks the wall bar on <paramref name="edge"/> red on a loop -- a step was rejected
        /// because a wall sits on that shared edge, so the wall blinks rather than either cell
        /// either side of it. Both cells sharing a wall draw their own copy of the bar on top of
        /// each other (see wallVisual's field comment), and only one copy ends up visible, so
        /// callers flash both sides together rather than relying on this method alone to pick
        /// the right one. Keeps looping until <see cref="StopInvalidWallFeedback"/> is called.
        /// </summary>
        public void PlayInvalidWallFeedback(Direction edge)
        {
            EnsureWallGroup();

            Image wallImage = WallImageFor(edge);
            if (wallImage == null) { return; }

            wallImage.DOKill();
            wallImage.color = WallColor;
            wallImage.DOColor(InvalidWallFlashColor, 0.09f).SetLoops(-1, LoopType.Yoyo);
        }

        public void StopInvalidWallFeedback(Direction edge)
        {
            Image wallImage = WallImageFor(edge);
            if (wallImage == null) { return; }

            wallImage.DOKill();
            wallImage.color = WallColor;
        }

        private Image WallImageFor(Direction edge)
        {
            int idx = (int)edge - 1;
            if (wallImages == null || idx < 0 || idx >= wallImages.Length) { return null; }
            return wallImages[idx];
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
        public bool IsSharedGoal
        {
            get { return isPairBlock && (secondPairId != 0 || thirdPairId != 0 || fourthPairId != 0); }
        }

        public int ThirdPairId { get { return thirdPairId; } }

        public int FourthPairId { get { return fourthPairId; } }

        /// <summary>
        /// Whether this cell is a dot belonging to <paramref name="askingPairId"/>. Use this rather
        /// than comparing <see cref="PairId"/>: a shared destination answers to two pairs, and
        /// PairId can only name one of them.
        /// </summary>
        public bool IsDotFor(int askingPairId)
        {
            if (!isPairBlock || askingPairId == 0) { return false; }
            return pairId == askingPairId || secondPairId == askingPairId
                || thirdPairId == askingPairId || fourthPairId == askingPairId;
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

    }
}