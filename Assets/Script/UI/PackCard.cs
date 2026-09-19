using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FreeFlow.Input;
using FreeFlow.Enums;

namespace FreeFlow.UI
{
    /// <summary>
    /// One row on the pack-select screen (02_level_select) -- one board size within the current
    /// mode. Every pack is always open; tapping it hands off to UIController, which owns the
    /// actual SetPack + screen-switch (see UIController.OnPackSelected) -- this only renders the
    /// row and reports the tap, the same division of responsibility LevelButton already uses for
    /// LoadLevel.
    /// </summary>
    public class PackCard : MonoBehaviour
    {
        [SerializeField] private Image cardBackground;
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI sizeText;
        [SerializeField] private TextMeshProUGUI subtitleText;
        [SerializeField] private TextMeshProUGUI fractionText;
        [SerializeField] private TextMeshProUGUI percentText;
        [SerializeField] private Slider progressSlider;
        [SerializeField] private GameObject actionIconPlay;
        [SerializeField] private GameObject actionIconDone;

        // Indexed by packSize - MinThumbSize (5x5 .. 9x9, matching the pack_thumb_*.png set --
        // there is no thumbnail art for any other board size, so ThumbSpriteFor clamps into range
        // rather than indexing out of bounds if a size outside 5-9 is ever added).
        private const int MinThumbSize = 5;
        [Header("Pack Icon Thumbnails (index 0 = 5x5 .. index 4 = 9x9)")]
        [SerializeField] private Sprite[] thumbSprites;

        [Header("Difficulty Tag")]
        // Breathing room each side of the label, and the pill's fixed height. The pill sprite is a
        // stadium (its 9-slice border spans the full sprite height), so the ends stay semicircular
        // at any height and only the width has to be computed.
        private const float TagSidePadding = 34f;
        private const float TagHeight = 46f;

        [SerializeField] private PackMetadataSO packMetadata;
        [SerializeField] private GameObject difficultyTag;
        [SerializeField] private Image difficultyTagBackground;
        [SerializeField] private TextMeshProUGUI difficultyTagLabel;

        private int packSize;

        public void SetDetails(GameMode mode, int packSize, int completed, int total)
        {
            this.packSize = packSize;

            if (iconImage != null) { iconImage.sprite = ThumbSpriteFor(packSize); }
            if (sizeText != null) { sizeText.text = packSize + "×" + packSize; }

            // Writes the subtitle as well as the tag -- the colour range lives in the same
            // authored entry as the tier, so both are set in one place or neither is.
            ApplyDifficultyTag(mode, packSize);

            if (fractionText != null) { fractionText.text = completed + "/" + total; }
            int percent = total > 0 ? Mathf.RoundToInt(100f * completed / total) : 0;
            if (percentText != null) { percentText.text = percent + "%"; }
            SetProgress(total > 0 ? (float)completed / total : 0f);

            bool isDone = completed >= total;

            if (actionIconPlay != null) { actionIconPlay.SetActive(!isDone); }
            if (actionIconDone != null) { actionIconDone.SetActive(isDone); }
        }

        /// <summary>
        /// The tier tab across the card's top edge.
        ///
        /// The tier is read from PackMetadataSO rather than derived: it is deliberately authored,
        /// because the measured LevelData.difficultyScore comes out nearly flat and non-monotonic
        /// across the shipped packs -- see that asset's own doc comment.
        ///
        /// A pack with no authored entry hides the tab rather than showing a tier it was never
        /// given: a board size added to UIController before it is authored here should read as
        /// unlabelled, not as "EASY".
        ///
        /// The entry also carries the pack's pair-count range. Nothing draws it at the moment --
        /// the card was cut back to icon, size, tag, bar and count -- but it stays authored so it
        /// costs nothing to put back.
        /// </summary>
        private void ApplyDifficultyTag(GameMode mode, int packSize)
        {
            PackMetadataSO.Entry entry = packMetadata != null ? packMetadata.For(mode, packSize) : null;

            if (entry == null)
            {
                if (difficultyTag != null) { difficultyTag.SetActive(false); }
                return;
            }

            if (difficultyTag != null) { difficultyTag.SetActive(true); }

            PackMetadataSO.TierStyle style = packMetadata.StyleFor(entry.tier);
            if (style == null) { return; }

            if (cardBackground != null) { cardBackground.color = style.cardBackground; }
            if (difficultyTagBackground != null) { difficultyTagBackground.color = style.badgeBackground; }
            if (difficultyTagLabel == null) { return; }

            difficultyTagLabel.text = style.label;
            difficultyTagLabel.color = style.badgeText;

            // The pill hugs its own label, measured here rather than by a ContentSizeFitter and a
            // HorizontalLayoutGroup. Those cost a layout rebuild on a screen that otherwise needs
            // none, and they measured this label wrong anyway -- the group reported a preferred
            // width of 448 for text 67 wide. ForceMeshUpdate is required because preferredValues
            // reads the mesh TMP has generated, and the text was assigned on the line above: with
            // no update the first card would be sized from whatever label the template carried.
            difficultyTagLabel.ForceMeshUpdate();
            float labelWidth = difficultyTagLabel.preferredWidth;

            RectTransform pill = difficultyTagBackground != null
                ? difficultyTagBackground.rectTransform
                : null;
            if (pill != null)
            {
                pill.sizeDelta = new Vector2(labelWidth + TagSidePadding * 2f, TagHeight);
            }
        }

        /// <summary>The size-appropriate grid-preview thumbnail. Clamps into the 5x5-9x9 range
        /// the art actually covers rather than indexing out of bounds.</summary>
        private Sprite ThumbSpriteFor(int size)
        {
            if (thumbSprites == null || thumbSprites.Length == 0) { return null; }
            int index = Mathf.Clamp(size - MinThumbSize, 0, thumbSprites.Length - 1);
            return thumbSprites[index];
        }

        private void SetProgress(float fraction)
        {
            if (progressSlider == null) { return; }
            progressSlider.value = Mathf.Clamp01(fraction);
        }

        public void OnCardClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
                GetComponentInParent<PackSelectPage>(true).OnPackSelected(packSize);
            }
        }
    }
}
