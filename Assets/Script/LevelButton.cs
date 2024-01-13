using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LevelButton : MonoBehaviour
{
    [SerializeField] private Button thisButton;
    [SerializeField] private TextMeshProUGUI levelNumberText;

    private int levelNumber;

    public void SetDetails(int _levelNumber, bool interactable)
    {
        levelNumber = _levelNumber;
        thisButton.interactable = interactable;
        levelNumberText.text = levelNumber.ToString();
    }

    public void OnButtonClick()
    {
        UIController.instance.LoadLevel(levelNumber);
    }
}
