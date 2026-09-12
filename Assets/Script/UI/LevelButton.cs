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

        // Lets a caller (e.g. DailyChallengePage, reusing this same prefab for today's pick)
        // substitute what a tap does -- set once, right after Instantiate. Deliberately NOT a
        // second CanInput()/PlayButtonClickSound() gate of its own: OnButtonClick below already
        // gates once before invoking this, and a second gate in the override action would consume
        // CanInput()'s one-shot debounce a second time in the same call stack, always silently
        // no-op-ing (see PackSelectPage.OnPackSelected's own doc comment for the same bug shipped
        // here once already).
        private System.Action onClickOverride;

        public RectTransform ThisTransform { get { return thisTransform; } }

        /// <summary>Substitutes what OnButtonClick does after its own CanInput gate passes,
        /// instead of the default UIController.LoadLevel(levelNumber) call. The action itself must
        /// NOT re-check CanInput() -- see the field's own doc comment.</summary>
        public void SetClickOverride(System.Action action)
        {
            onClickOverride = action;
        }

        /// <summary>
        /// Sets the details for the level button: its number and its unlocked/current/done state.
        /// Locked tiles are not interactable -- the new art's lock icon is a real gate, not just
        /// a decoration, so a level ahead of the player's progress can no longer be tapped into.
        /// </summary>
        public void SetDetails(int levelNumber, LevelTileState state)
        {
            SetDetails(levelNumber, state, levelNumber);
        }

        /// <summary>
        /// As <see cref="SetDetails(int, LevelTileState)"/>, but the tile SHOWS
        /// <paramref name="displayNumber"/> rather than the level it loads.
        ///
        /// The two are the same thing everywhere except the Daily Challenge hub, where the day's
        /// challenges are picks from all over the packs (5x5 level 65, 6x6 level 59, ...). Showing
        /// those raw numbers reads as a jumble, and worse, as pack progress the player has not
        /// made -- what matters there is only which of the day's five this is, so the hub passes
        /// 1..5. Purely presentational: <paramref name="levelNumber"/> is still what a tap loads.
        /// </summary>
        public void SetDetails(int levelNumber, LevelTileState state, int displayNumber)
        {
            this.levelNumber = levelNumber;
            levelNumberText.text = displayNumber.ToString();

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
                if (onClickOverride != null) { onClickOverride(); }
                else { UIController.Instance.LoadLevel(levelNumber); }
            }
        }
    }
}
