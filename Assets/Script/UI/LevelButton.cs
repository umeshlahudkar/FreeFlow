using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FreeFlow.Input;

namespace FreeFlow.UI
{
    /// <summary>
    /// Used for loading different game levels.
    /// </summary>
    public class LevelButton : MonoBehaviour
    {
        [SerializeField] private RectTransform thisTransform;
        [SerializeField] private Button thisButton;
        [SerializeField] private TextMeshProUGUI levelNumberText;
        [SerializeField] private Image buttonImg;

        [SerializeField] private GameObject move;
        [SerializeField] private TextMeshProUGUI movesText;

        private int levelNumber;

        public RectTransform ThisTransform { get { return thisTransform; } }

        /// <summary>
        /// Sets the details for the level button, including the level number and its interactability.
        /// </summary>
        /// <param name="_levelNumber">The number associated with this level button.</param>
        /// <param name="interactable">Whether the button should be interactable.</param>
        public void SetDetails(int _levelNumber, bool interactable, Color imgColor, int movesCount = 0)
        {
            levelNumber = _levelNumber;
            thisButton.interactable = interactable;
            levelNumberText.text = levelNumber.ToString();
            buttonImg.color = imgColor;

            if(movesCount > 0)
            {
                move.SetActive(true);
                movesText.text = "Moves : " + movesCount;
            }
            else
            {
                move.SetActive(false);
            }
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
