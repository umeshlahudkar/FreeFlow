using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FreeFlow.Input;

namespace FreeFlow.UI
{
    public enum LevelTileState
    {
        Locked,
        Current,
        Done
    }

    /// <summary>
    /// Used for loading different game levels.
    /// </summary>
    public class LevelButton : MonoBehaviour
    {
        [SerializeField] private RectTransform thisTransform;
        [SerializeField] private TextMeshProUGUI levelNumberText;
        [SerializeField] private Image buttonImg;
        [SerializeField] private Button button;

        [Header("State Sprites")]
        [SerializeField] private Sprite doneSprite;
        [SerializeField] private Sprite currentSprite;
        [SerializeField] private Sprite lockedSprite;

        [Header("State Markers")]
        [SerializeField] private GameObject checkIcon;
        [SerializeField] private GameObject lockIcon;

        [Header("Number Colors")]
        [SerializeField] private Color doneNumberColor = new Color(0.0588f, 0.6196f, 0.5333f, 1f);
        [SerializeField] private Color currentNumberColor = new Color(0.0588f, 0.6196f, 0.5333f, 1f);
        [SerializeField] private Color lockedNumberColor = new Color(0.4039f, 0.4706f, 0.5529f, 1f);

        private int levelNumber;

        public RectTransform ThisTransform { get { return thisTransform; } }

        /// <summary>
        /// Sets the details for the level button: its number and its unlocked/current/done state.
        /// Locked tiles are not interactable -- the new art's lock icon is a real gate, not just
        /// a decoration, so a level ahead of the player's progress can no longer be tapped into.
        /// </summary>
        public void SetDetails(int levelNumber, LevelTileState state)
        {
            this.levelNumber = levelNumber;
            levelNumberText.text = levelNumber.ToString();

            Sprite sprite = state == LevelTileState.Done ? doneSprite
                : state == LevelTileState.Current ? currentSprite
                : lockedSprite;
            if (sprite != null) { buttonImg.sprite = sprite; }

            levelNumberText.color = state == LevelTileState.Done ? doneNumberColor
                : state == LevelTileState.Current ? currentNumberColor
                : lockedNumberColor;

            if (checkIcon != null) { checkIcon.SetActive(state == LevelTileState.Done); }
            if (lockIcon != null) { lockIcon.SetActive(state == LevelTileState.Locked); }

            if (button != null) { button.interactable = state != LevelTileState.Locked; }
        }

        /// <summary>
        /// Called when the level button is clicked. Loads the corresponding game level
        /// </summary>
        public void OnButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                UIController.Instance.LoadLevel(levelNumber);
            }
        }
    }
}
