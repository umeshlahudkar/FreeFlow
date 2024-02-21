using FreeFlow.Util;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using TMPro;
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

        [SerializeField] private TextMeshProUGUI stageText;

        [SerializeField] private Color completeLevel;
        [SerializeField] private Color unlockedLevel;
        [SerializeField] private Color lockedLevel;

        private int levelButtonPerScreen = 30;

        [SerializeField] private List<LevelButton> levelButtons = new List<LevelButton>();
        public GameObject[] levelStages;

        //private ObjectPool<LevelButton> objectPool;
        //private WaitForSeconds waitForSeconds;
        
        public int currentstageOnScreen;

        private Vector3 clickPosition;
        private Vector3 endPosition;
        private Vector3 prePosition;

        public float swipeThreshold = 50f; 
        public float swipeSpeed = 5f;

        private Vector3 stageScreenPosition;

       
        private void OnEnable()
        {
            SetButtons();
        }

        /// <summary>
        /// Prepares the level selection screen by instantiating and setting up level buttons.
        /// </summary>
        /// <param name="unlockedLevels">The number of levels that are unlocked.</param>
        public void SpawnLevelButtons(int totalLevels)
        {
            currentstageOnScreen = 0;

            int pages = Mathf.CeilToInt((float) totalLevels / levelButtonPerScreen);

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

            int spawnedLevelCount = 0;
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
                        spawnedLevelCount++;
                        LevelButton button = Instantiate<LevelButton>(levelButtonPrefab, levelStage.transform);
                        button.ThisTransform.SetParent(levelStage.transform);
                        button.ThisTransform.localScale = Vector3.one;
                        
                        button.gameObject.SetActive(true);
                        button.ThisTransform.sizeDelta = new Vector2(buttonWidth, buttonWidth);
                        button.ThisTransform.localPosition = new Vector3(currentX, currentY);

                        levelButtons.Add(button);

                        currentX += buttonWidth + horizontalSpacing;

                        if(spawnedLevelCount == totalLevels) { return; }
                    }

                    currentX = startX;
                    currentY -= buttonWidth + verticalSpacing;
                }

                currentX = startX;
                currentY = startY ;
            }
        }

        private void SetButtons()
        {
            SaveData data = SavingSystem.Instance.Load();
            int completedLevels = data.completedLevel;
            int currentUnlockedLevel = completedLevels + 1;

            for(int i = 0; i < levelButtons.Count; i++)
            {
                int level = i + 1;
                bool isUnlocked = (level <= currentUnlockedLevel);
                bool isCompleted = (level <= completedLevels);

                int levelCompletionMoves = 0;
                if(isCompleted)
                {
                    levelCompletionMoves = data.completedlevelMoves[level - 1];
                }

                Color color = isUnlocked ? (isCompleted ? completeLevel : unlockedLevel) : lockedLevel;
                levelButtons[i].SetDetails(level, isUnlocked, color, levelCompletionMoves);
            }

            currentstageOnScreen = Mathf.CeilToInt((float)currentUnlockedLevel / levelButtonPerScreen);
            currentstageOnScreen--;

            MoveLevelStages(currentstageOnScreen);
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
                    UpdateStageText();
                }
                else
                {
                    MoveLevelStages(currentstageOnScreen);
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

        private void UpdateStageText()
        {
            stageText.text = "STAGE - " + (currentstageOnScreen+1);
        }

        public void OnRightButtonClick()
        {
            currentstageOnScreen++;
            if(currentstageOnScreen < levelStages.Length)
            {
                MoveLevelStages(currentstageOnScreen);
                UpdateStageText();
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
                UpdateStageText();
            }
            else
            {
                currentstageOnScreen = 0;
            }
        }
    }
}
