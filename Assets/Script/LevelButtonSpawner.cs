using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LevelButtonSpawner : MonoBehaviour
{
    [SerializeField] private LevelButton levelButtonPrefab;
    [SerializeField] private int maxLevelPerScreen = 20;

    private List<LevelButton> buttons = new List<LevelButton>();
    private ObjectPool<LevelButton> objectPool;

    private void InitializePool()
    {
        objectPool = new ObjectPool<LevelButton>(levelButtonPrefab, maxLevelPerScreen, levelButtonPrefab.transform.parent);
    }
   
    public void PrepareLevelScreen(int unlockedLevels)
    {
        if(objectPool == null) { InitializePool(); }

        for (int i = 0; i < maxLevelPerScreen; i++)
        {
            LevelButton button = objectPool.GetObject();
            button.SetDetails((i + 1), (i + 1) <= unlockedLevels);

            buttons.Add(button);
        }
    }

    private void OnDisable()
    {
        if (buttons != null && buttons.Count > 0)
        {
            foreach (LevelButton button in buttons)
            {
                objectPool.ReturnObject(button);
            }
            buttons.Clear();
        }
    }
}
