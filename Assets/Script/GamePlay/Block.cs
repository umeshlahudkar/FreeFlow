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
        [SerializeField] private Image[] directionImages;

        // Edge bars, indexed like directionImages ((int)Direction - 1: Left=0, Right=1,
        // Up=2, Down=3). Reused for two mechanics that both mark a single edge of the cell:
        // dark for a wallMask bit, green for the one allowed entry edge on a OneWay cell.
        [SerializeField] private Image[] wallImages;

        // Center marker reused for the two mechanics that repurpose PairId as "which pair
        // this applies to": a plain square for Checkpoint, a 45-degree-rotated diamond for
        // ForbiddenForPair, tinted to that pair's color.
        [SerializeField] private Image specialMarkerImage;

        // Shown on a pair dot when that pair has an exact-length requirement, so the
        // player knows which color it applies to and how many cells it needs.
        [SerializeField] private TextMeshProUGUI lengthLabel;

        private int row_ID;
        private int coloum_ID;

        private bool isPairBlock;

        private PairColorType pairColorType;
        private PairColorType highlightedColorType;

        // Identity of the pair this block belongs to, independent of its display color.
        // 0 means "no pair" (mirrors PairColorType.None), matching pairColorType/
        // highlightedColorType's None state.
        private int pairId;
        private int highlightedPairId;

        private BlockType blockType;

        // Edge-based, independent of blockType: bitmask of walled edges (Left=1, Right=2,
        // Up=4, Down=8). A wall belongs to the boundary between two cells, not to either
        // cell's type.
        private int wallMask;

        // Only meaningful when blockType == BlockType.OneWay: the only direction a path
        // may be moving in when entering this cell. Direction.None = no restriction.
        private Direction requiredEntryDirection;

        // Which pair currently owns each direction-image slot (index = (int)dir - 1), 0 =
        // unowned. On a Normal cell there's only ever one occupant so this changes nothing
        // observable; on a Mixed cell it lets ResetAllHighlightDirection(pairId) clear only
        // the calling pair's own slots without touching another pair's.
        private int[] directionOwnerPairId = new int[4];

        /// <summary>
        /// Sets the properties of the block, including its position, pair color type,
        /// </summary>
        /// <param name="type">The pair color type of the block.</param>
        /// <param name="pairId">The pair identity of the block, independent of display color.</param>
        /// <param name="blockType">The obstacle/mechanic type of this cell.</param>
        /// <param name="wallMask">Bitmask of walled edges (Left=1, Right=2, Up=4, Down=8).</param>
        /// <param name="requiredEntryDirection">Only used when blockType is OneWay.</param>
        /// <param name="rowIndex">The row index of the block.</param>
        /// <param name="coloumIndex">The column index of the block.</param>
        public void SetBlock(PairColorType type, int pairId, BlockType blockType, int wallMask, Direction requiredEntryDirection, int rowIndex, int coloumIndex)
        {
            this.row_ID = rowIndex;
            this.coloum_ID = coloumIndex;
            pairColorType = type;
            this.pairId = pairId;
            this.blockType = blockType;
            this.wallMask = wallMask;
            this.requiredEntryDirection = requiredEntryDirection;

            // Direction bars are Image.Type.Filled: kept active permanently and shown/hidden
            // purely via fillAmount (0 = invisible), rather than toggling the GameObject
            // active/inactive on every drag update, which was popping the bars on and off.
            for (int i = 0; i < directionImages.Length; i++)
            {
                directionImages[i].gameObject.SetActive(true);
                directionImages[i].DOKill();
                directionImages[i].fillAmount = 0f;
                directionImages[i].fillOrigin = (int)Image.OriginHorizontal.Left;
            }

            if (pairColorType != PairColorType.None)
            {
                isPairBlock = true;
                pairDotImage.gameObject.SetActive(true);
                pairDotImage.color = GamePlayController.Instance.GetColor(type);

                pairDotImage.transform.localScale = Vector3.zero;
                pairDotImage.transform.DOScale(1, 0.5f);

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

            // wall bars: independent of blockType, one per walled edge
            Direction[] edges = { Direction.Left, Direction.Right, Direction.Up, Direction.Down };
            for (int i = 0; i < edges.Length; i++)
            {
                if (HasWall(edges[i]))
                {
                    int idx = (int)edges[i] - 1;
                    wallImages[idx].gameObject.SetActive(true);
                    wallImages[idx].color = new Color(0.05f, 0.05f, 0.05f, 1);
                }
            }

            // one-way: mark the single edge a path may enter through, on the opposite side
            // of the direction it must be travelling (e.g. "must be moving Down" means it
            // enters via the cell's Up-facing edge)
            if (blockType == BlockType.OneWay && requiredEntryDirection != Direction.None)
            {
                int idx = OppositeDirectionIndex(requiredEntryDirection);
                if (idx >= 0)
                {
                    wallImages[idx].gameObject.SetActive(true);
                    wallImages[idx].color = new Color(0.2f, 0.8f, 0.3f, 1);
                }
            }
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

            Color color = GamePlayController.Instance.GetColor((PairColorType)targetPairId);
            color.a = 1f;
            specialMarkerImage.color = color;
        }

        private static int OppositeDirectionIndex(Direction dir)
        {
            switch (dir)
            {
                case Direction.Left: return 1; // Right
                case Direction.Right: return 0; // Left
                case Direction.Up: return 3; // Down
                case Direction.Down: return 2; // Up
                default: return -1;
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
            return requiredEntryDirection == Direction.None || requiredEntryDirection == incomingDirection;
        }

        /// <summary>
        /// Whether this cell has a wall on the edge facing <paramref name="dir"/>.
        /// </summary>
        public bool HasWall(Direction dir)
        {
            int bit = WallBit(dir);
            return bit != 0 && (wallMask & bit) != 0;
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

        // How long a bar takes to finish filling once a step commits. The cell being left
        // already sits at fillAmount ~1 from the live drag preview, so this is a no-op there;
        // it only matters for the entered cell's incoming bar. 0.08s verified geometrically
        // correct (grows from the seam inward) but was too short to read as motion at all --
        // sub-100ms changes get perceived as an instant switch regardless of direction.
        private const float CommitFillDuration = 0.18f;

        /// <summary>
        /// Highlights the block in a specified direction with a given pair color type.
        /// Every direction bar is the same rect (pivot at the cell-center end, tip at the
        /// edge), just rotated per direction, so fillOrigin=Left always means "grow from
        /// this cell's center outward to the edge". That's correct for the bar on the cell
        /// being LEFT (it's already been growing that way all through the live preview), but
        /// wrong for the bar on the cell being ENTERED: growing center-to-edge means the part
        /// that actually touches the previous cell is the last sliver to appear, so the seam
        /// still looks like it pops in no matter how long the tween runs. Pass
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
            highlightedColorType = type;
            highlightedPairId = pairId;

            int idx = (int)dir - 1;
            directionOwnerPairId[idx] = pairId;

            Color color = GamePlayController.Instance.GetColor(type);
            directionImages[idx].color = color;
            directionImages[idx].fillOrigin = growFromFarEdge ? (int)Image.OriginHorizontal.Right : (int)Image.OriginHorizontal.Left;
            directionImages[idx].DOKill();

            if (growFromFarEdge)
            {
                // Driven live, every frame, by GamePlayController's drag preview
                // (SetDirectionFillAmount) for as long as this stays the entry edge of the
                // current last selected block -- an autonomous tween can't track where the
                // pointer actually is, which is exactly what looked wrong: the bar would
                // finish filling on its own schedule regardless of how far the pointer had
                // actually moved into the cell.
                directionImages[idx].fillAmount = 0f;
            }
            else
            {
                directionImages[idx].DOFillAmount(1f, CommitFillDuration);
            }
        }

        /// <summary>
        /// Directly sets a direction bar's fillAmount, no animation. Used by
        /// GamePlayController's live drag preview to keep the entry bar (the edge this cell
        /// was entered through) tracking the pointer's actual position every frame, and to
        /// snap it to fully connected once the path advances past this cell.
        /// </summary>
        public void SetDirectionFillAmount(Direction dir, float fillAmount)
        {
            int idx = (int)dir - 1;
            directionImages[idx].fillAmount = Mathf.Clamp01(fillAmount);
        }

        /// <summary>
        /// Reads a direction bar's current fillAmount -- used by GamePlayController on pointer
        /// release to check how far the entry edge had actually filled before deciding whether
        /// this cell counts as a real step or should be undone.
        /// </summary>
        public float GetDirectionFillAmount(Direction dir)
        {
            int idx = (int)dir - 1;
            return directionImages[idx].fillAmount;
        }

        /// <summary>
        /// Live drag preview: shows this cell's <paramref name="dir"/> bar growing from the
        /// cell center outward as <paramref name="fraction"/> (0-1) increases, tracking the
        /// pointer's actual progress toward the neighboring cell before the step commits.
        /// Never touches a slot ProcessBlockStep/HighlightBlockDirection has already
        /// committed for this drag (directionOwnerPairId != 0), so a live preview can't
        /// clobber an already-drawn segment. The image stays active at all times (see
        /// SetBlock) -- visibility is purely fillAmount, since toggling the GameObject
        /// active/inactive every frame during a drag is what caused the old flicker.
        /// </summary>
        public void SetDirectionPreview(Direction dir, float fraction, PairColorType type)
        {
            int idx = (int)dir - 1;
            if (directionOwnerPairId[idx] != 0) { return; }

            fraction = Mathf.Clamp01(fraction);
            directionImages[idx].fillAmount = fraction;
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
                directionImages[i].fillAmount = 0f;
                directionImages[i].fillOrigin = (int)Image.OriginHorizontal.Left;
                directionOwnerPairId[i] = 0;
            }

            ResetHighlightBlockBg();
            highlightedColorType = PairColorType.None;
            highlightedPairId = 0;
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
                    directionImages[i].fillAmount = 0f;
                    directionImages[i].fillOrigin = (int)Image.OriginHorizontal.Left;
                    directionOwnerPairId[i] = 0;
                }
            }

            if (highlightedPairId == pairId)
            {
                ResetHighlightBlockBg();
                highlightedColorType = PairColorType.None;
                highlightedPairId = 0;
            }
        }

        /// <summary>
        /// Resets the highlight direction of the block in a specific direction.
        /// </summary>
        /// <param name="dir">The direction to reset the highlight for.</param>
        public void ResetHighlightDirection(Direction dir)
        {
            int idx = (int)dir - 1;
            directionImages[idx].DOKill();
            directionImages[idx].fillAmount = 0f;
            directionImages[idx].fillOrigin = (int)Image.OriginHorizontal.Left;
            directionOwnerPairId[idx] = 0;

            int count = 0;
            for (int i = 0; i < directionImages.Length; i++)
            {
                if(directionImages[i].fillAmount > 0f) { break; }
                count++;
            }

            if(count >= directionImages.Length)
            {
                ResetHighlightBlockBg();
            }
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
            blockBgHighlightImage.gameObject.SetActive(true);

            Color color = GamePlayController.Instance.GetColor(highlightedColorType);
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

        public PairColorType HighlightedColorType
        {
            get { return highlightedColorType; }
        }

        public int PairId { get { return pairId; } }
        public int HighlightedPairId { get { return highlightedPairId; } }

        public BlockType BlockType { get { return blockType; } }

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
            highlightedColorType = PairColorType.None;
            pairId = 0;
            highlightedPairId = 0;
            blockType = BlockType.Normal;
            wallMask = 0;
            requiredEntryDirection = Direction.None;
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
            lengthLabel.gameObject.SetActive(false);
        }
    }
}