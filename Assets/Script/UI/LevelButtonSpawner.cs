using FreeFlow.Util;
using System.Collections.Generic;
using UnityEngine;


namespace FreeFlow.UI
{
    /// <summary>
    /// Handles the spawning and management of level buttons on a level selection screen
    /// </summary>
    public class LevelButtonSpawner : MonoBehaviour
    {
        [SerializeField] private LevelButton levelButtonPrefab;
        [SerializeField] private int maxLevelPerScreen = 20;

        private List<LevelButton> buttons = new List<LevelButton>();
        private ObjectPool<LevelButton> objectPool;

        /// <summary>
        /// Initializes the object pool for level buttons
        /// </summary>
        private void InitializePool()
        {
            objectPool = new ObjectPool<LevelButton>(levelButtonPrefab, maxLevelPerScreen, levelButtonPrefab.transform.parent);
        }


        /// <summary>
        /// Prepares the level selection screen by instantiating and setting up level buttons.
        /// </summary>
        /// <param name="unlockedLevels">The number of levels that are unlocked.</param>
        public void PrepareLevelScreen(int unlockedLevels)
        {
            if (objectPool == null) { InitializePool(); }

            for (int i = 0; i < maxLevelPerScreen; i++)
            {
                LevelButton button = objectPool.GetObject();
                button.SetDetails((i + 1), (i + 1) <= unlockedLevels);

                buttons.Add(button);
            }
        }

        /// <summary>
        /// On level screen closed, Disables the buttons and returns all buttons to the object pool
        /// </summary>
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
}
