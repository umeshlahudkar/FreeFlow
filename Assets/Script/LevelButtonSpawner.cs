using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LevelButtonSpawner : MonoBehaviour
{
    [SerializeField] private LevelButton levelButtonPrefab;
    [SerializeField] private int maxLevelPerScreen = 20;

    private List<LevelButton> buttons = new List<LevelButton>();

    private void OnEnable()
    {
       
    }

    public void PrepareLevelScreen(int unlockedLevels)
    {
        
        for(int i = 0; i < maxLevelPerScreen; i++)
        {
            LevelButton button = Instantiate(levelButtonPrefab, levelButtonPrefab.transform.parent);
            button.SetDetails((i + 1), (i + 1) <= unlockedLevels ? true : false);
            button.gameObject.SetActive(true);

            buttons.Add(button);
        }
    }

    private void OnDisable()
    {
        if (buttons != null && buttons.Count > 0)
        {
            foreach (LevelButton button in buttons)
            {
                Destroy(button.gameObject);
            }
            buttons.Clear();
        }
    }
}
