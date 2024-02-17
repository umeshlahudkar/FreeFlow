using FreeFlow.Util;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using System.Collections;

namespace FreeFlow.UI
{
    /// <summary>
    /// Handles the spawning and management of level buttons on a level selection screen
    /// </summary>
    public class LevelScreenController : MonoBehaviour
    {
        [SerializeField] private LevelButton levelButtonPrefab;
        [SerializeField] private GameObject levelStagePrefab;

        [SerializeField] private int levelButtonPerScreen = 30;

        [SerializeField] private Color completeLevel;
        [SerializeField] private Color unlockedLevel;
        [SerializeField] private Color lockedLevel;

        private int numberOfLevels = 100;

        private List<LevelButton> buttons = new List<LevelButton>();
        private ObjectPool<LevelButton> objectPool;
        private WaitForSeconds waitForSeconds;

        public GameObject[] levelStages;
        public int currentstageOnScreen;

        private Vector3 clickPosition;
        private Vector3 endPosition;
        private Vector3 prePosition;

        public float swipeThreshold = 50f; 
        public float swipeSpeed = 5f;

        private Vector3 stageScreenPosition;

        /// <summary>
        /// Initializes the object pool for level buttons
        /// </summary>
        private void InitializePool()
        {
            waitForSeconds = new WaitForSeconds(0.02f);
            objectPool = new ObjectPool<LevelButton>(levelButtonPrefab, levelButtonPerScreen, levelButtonPrefab.transform.parent);
        }


        /// <summary>
        /// Prepares the level selection screen by instantiating and setting up level buttons.
        /// </summary>
        /// <param name="unlockedLevels">The number of levels that are unlocked.</param>
        public void PrepareLevelScreen(int unlockedLevels)
        {
            currentstageOnScreen = 0;
            int pages = Mathf.CeilToInt((float)numberOfLevels / levelButtonPerScreen);

            levelStages = new GameObject[pages];

            Rect levelStageRect = levelStagePrefab.GetComponent<RectTransform>().rect;
            float totalButtonSize = levelStageRect.width * 0.7f;
            float buttonWidth = totalButtonSize / 5;

            float horizontalSpacing = (levelStageRect.width - totalButtonSize) / 6;
            float verticalSpacing = (levelStageRect.height - (buttonWidth * 6)) / 7;

            float startX = -((levelStageRect.width / 2) - (buttonWidth / 2) - horizontalSpacing);
            float startY = (levelStageRect.height / 2) - (buttonWidth / 2) - verticalSpacing;

            float currentX = startX;
            float currentY = startY;

            int levelNumber = 0;
            stageScreenPosition = levelStagePrefab.transform.localPosition;

            for (int i = 0; i < pages; i++)
            {
                GameObject levelStage = Instantiate(levelStagePrefab, transform);
                levelStage.SetActive(true);
                levelStages[i] = levelStage;

                levelStage.transform.localPosition = new Vector3(stageScreenPosition.x + (Screen.width) * i, stageScreenPosition.y, stageScreenPosition.z);

                for (int j = 0; j < 6; j++)
                {
                    for (int k = 0; k < 5; k++)
                    {
                        levelNumber++;
                        LevelButton button = Instantiate<LevelButton>(levelButtonPrefab, levelStage.transform);

                        Color color = levelNumber <= unlockedLevels ? (levelNumber == unlockedLevels ? unlockedLevel : completeLevel) : lockedLevel; 
                        button.SetDetails(levelNumber, levelNumber <= unlockedLevels, color);
                        //buttons.Add(button);
                        button.gameObject.SetActive(true);
                        button.ThisTransform.sizeDelta = new Vector2(buttonWidth, buttonWidth);
                        button.ThisTransform.localPosition = new Vector3(currentX, currentY);

                        currentX += buttonWidth + horizontalSpacing;
                    }

                    currentX = startX;
                    currentY -= buttonWidth + verticalSpacing;
                }

                currentX = startX;
                currentY = startY ;
            }
            //if (objectPool == null) { InitializePool(); }
            //StartCoroutine(InstantiateButton(unlockedLevels));
        }

        private void Update()
        {
            if(UnityEngine.Input.GetMouseButtonDown(0))
            {
                clickPosition = (UnityEngine.Input.mousePosition);
                prePosition = clickPosition;
            }
            else if(UnityEngine.Input.GetMouseButton(0))
            {
                endPosition = UnityEngine.Input.mousePosition;
                Vector3 direction = endPosition - prePosition;
                float dragDistance = Mathf.Abs(direction.x);

                if (dragDistance > 0.1)
                {
                    int directionMultiplier = (direction.x < 0) ? -1 : 1;
                    for (int i = 0; i < levelStages.Length; i++)
                    {
                        levelStages[i].transform.localPosition += new Vector3((dragDistance/2) * directionMultiplier, 0, 0);
                    }
                }
                prePosition = endPosition;
            }
            else if (UnityEngine.Input.GetMouseButtonUp(0))
            {
                Vector3 direction = endPosition - clickPosition;
                float dragDistancee = direction.magnitude;

                if (dragDistancee > swipeThreshold)
                {
                    if (direction.x < 0)
                    {
                        currentstageOnScreen++;
                    }
                    else
                    {
                        currentstageOnScreen--;
                    }

                    currentstageOnScreen = Mathf.Clamp(currentstageOnScreen, 0, levelStages.Length - 1);
                    MoveLevelStages(currentstageOnScreen);
                    //for (int i = 0; i < levelStages.Length; i++)
                    //{
                    //    if (i == currentScreen)
                    //    {
                    //        levelStages[i].transform.DOLocalMove(Vector3.zero, 0.2f);
                    //    }
                    //    else if (i < currentScreen)
                    //    {
                    //        int index = currentScreen - i;
                    //        Vector3 position = levelStages[currentScreen].transform.localPosition;
                    //        levelStages[i].transform.DOLocalMove(new Vector3(-(Screen.width * index), position.y, position.z), 0.2f);
                    //    }
                    //    else
                    //    {
                    //        int index = i - currentScreen;
                    //        Vector3 position = levelStages[currentScreen].transform.localPosition;
                    //        levelStages[i].transform.DOLocalMove(new Vector3((Screen.width * index), position.y, position.z), 0.2f);
                    //    }
                    //}
                }
                else
                {
                    MoveLevelStages(currentstageOnScreen);
                    //for (int i = 0; i < levelStages.Length; i++)
                    //{
                    //    if (i == currentScreen)
                    //    {
                    //        levelStages[i].transform.DOLocalMove(Vector3.zero, 0.2f);
                    //    }
                    //    else if (i < currentScreen)
                    //    {
                    //        int index = currentScreen - i;
                    //        levelStages[i].transform.DOLocalMove(new Vector3(-(Screen.width * index), levelStages[currentScreen].transform.localPosition.y, levelStages[currentScreen].transform.localPosition.z), 0.2f);
                    //    }
                    //    else
                    //    {
                    //        int index = i - currentScreen;
                    //        levelStages[i].transform.DOLocalMove(new Vector3((Screen.width * index), levelStages[currentScreen].transform.localPosition.y, levelStages[currentScreen].transform.localPosition.z), 0.2f);
                    //    }
                    //}
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stageScreenIndex"> current screen index which is on screen</param>
        private void MoveLevelStages(int stageScreenIndex)
        {
            for (int i = 0; i < levelStages.Length; i++)
            {
                if (i == stageScreenIndex)
                {
                    levelStages[i].transform.DOLocalMove(stageScreenPosition, 0.2f);
                }
                else if (i < stageScreenIndex)
                {
                    int index = stageScreenIndex - i;
                    //Vector3 position = levelStages[currentScreen].transform.localPosition;
                    levelStages[i].transform.DOLocalMove(new Vector3(-(Screen.width * index), stageScreenPosition.y, stageScreenPosition.z), 0.2f);
                }
                else
                {
                    int index = i - stageScreenIndex;
                    //Vector3 position = levelStages[currentScreen].transform.localPosition;
                    levelStages[i].transform.DOLocalMove(new Vector3((Screen.width * index), stageScreenPosition.y, stageScreenPosition.z), 0.2f);
                }
            }
        }

        public void OnRightButtonClick()
        {
            currentstageOnScreen++;
            if(currentstageOnScreen < levelStages.Length)
            {
                MoveLevelStages(currentstageOnScreen);
            }
            else
            {
                currentstageOnScreen = levelStages.Length - 1;
            }
        }

        public void OnLeftButtonClick()
        {
            currentstageOnScreen--;
            if (currentstageOnScreen >= 0)
            {
                MoveLevelStages(currentstageOnScreen);
            }
            else
            {
                currentstageOnScreen = 0;
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
