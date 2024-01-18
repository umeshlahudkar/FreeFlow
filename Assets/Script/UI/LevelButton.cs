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
        [SerializeField] private Transform thisTransform;
        [SerializeField] private Button thisButton;
        [SerializeField] private TextMeshProUGUI levelNumberText;

        private int levelNumber;

        public Transform ThisTransform { get { return thisTransform; } }

        /// <summary>
        /// Sets the details for the level button, including the level number and its interactability.
        /// </summary>
        /// <param name="_levelNumber">The number associated with this level button.</param>
        /// <param name="interactable">Whether the button should be interactable.</param>
        public void SetDetails(int _levelNumber, bool interactable)
        {
            levelNumber = _levelNumber;
            thisButton.interactable = interactable;
            levelNumberText.text = levelNumber.ToString();
        }

        /// <summary>
        /// Called when the level button is clicked. Loads the corresponding game level
        /// </summary>
        public void OnButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                UIController.Instance.LoadLevel(levelNumber);
            }
        }
    }
}
