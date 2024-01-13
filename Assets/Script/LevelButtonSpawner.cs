using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LevelButtonSpawner : MonoBehaviour
{
    [SerializeField] private LevelButton levelButtonPrefab;
    [SerializeField] private int maxLevelPerScreen = 20;

    private void Start()
    {
        PrepareLevelScreen(3, 0);
    }

    public void PrepareLevelScreen(int unlockedLevels, int totalLevels)
    {
        for(int i = 0; i < maxLevelPerScreen; i++)
        {
            LevelButton button = Instantiate(levelButtonPrefab, levelButtonPrefab.transform.parent);
            button.SetDetails((i + 1), (i + 1) <= unlockedLevels ? true : false);
            button.gameObject.SetActive(true);
        }
    }
}
