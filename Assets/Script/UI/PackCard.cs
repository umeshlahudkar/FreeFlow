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
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI sizeText;
        [SerializeField] private TextMeshProUGUI subtitleText;
        [SerializeField] private TextMeshProUGUI fractionText;
        [SerializeField] private TextMeshProUGUI percentText;
        [SerializeField] private Slider progressSlider;
        [SerializeField] private Image actionImage;
        [SerializeField] private GameObject actionIconPlay;
        [SerializeField] private GameObject actionIconDone;
        [SerializeField] private Button actionButton;

        [Header("Action Sprites")]
        [SerializeField] private Sprite actionPlaySprite;
        [SerializeField] private Sprite actionDoneSprite;

        // Indexed by packSize - MinThumbSize (5x5 .. 9x9, matching the pack_thumb_*.png set --
        // there is no thumbnail art for any other board size, so ThumbSpriteFor clamps into range
        // rather than indexing out of bounds if a size outside 5-9 is ever added).
        private const int MinThumbSize = 5;
        [Header("Pack Icon Thumbnails (index 0 = 5x5 .. index 4 = 9x9)")]
        [SerializeField] private Sprite[] thumbSprites;

        private int packSize;

        public void SetDetails(int packSize, int completed, int total)
        {
            this.packSize = packSize;

            if (iconImage != null) { iconImage.sprite = ThumbSpriteFor(packSize); }
            if (sizeText != null) { sizeText.text = packSize + "×" + packSize; }
            if (subtitleText != null) { subtitleText.text = (packSize * packSize) + " cells"; }

            if (fractionText != null) { fractionText.text = completed + "/" + total; }
            int percent = total > 0 ? Mathf.RoundToInt(100f * completed / total) : 0;
            if (percentText != null) { percentText.text = percent + "%"; }
            SetProgress(total > 0 ? (float)completed / total : 0f);

            bool isDone = completed >= total;

            if (actionImage != null) { actionImage.sprite = isDone ? actionDoneSprite : actionPlaySprite; }
            if (actionIconPlay != null) { actionIconPlay.SetActive(!isDone); }
            if (actionIconDone != null) { actionIconDone.SetActive(isDone); }

            if (actionButton != null) { actionButton.interactable = true; }
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
