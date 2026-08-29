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

        private int levelButtonPerScreen = 30;

        public GameObject[] levelStages;

        private ObjectPool<LevelButton> objectPool;

        // only the current stage +/-1 adjacent are ever populated with real LevelButtons;
        // keyed by page index so a page's buttons can be returned to the pool once it
        // scrolls outside that window
        private Dictionary<int, List<LevelButton>> activePageButtons = new Dictionary<int, List<LevelButton>>();

        private int totalLevels;
        private float buttonWidth;
        private float horizontalSpacing;
        private float verticalSpacing;
        private float startX;
        private float startY;

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
        /// <param name="totalLevels">The total number of levels to create buttons for.</param>
        public void SpawnLevelButtons(int totalLevels)
        {
            this.totalLevels = totalLevels;
            currentstageOnScreen = 0;

            int pages = Mathf.CeilToInt((float) totalLevels / levelButtonPerScreen);

            levelStages = new GameObject[pages];

            Rect levelStageRect = levelStagePrefab.GetComponent<RectTransform>().rect;
            float totalButtonSize = levelStageRect.width * 0.7f;
            buttonWidth = totalButtonSize / 5;

            horizontalSpacing = (levelStageRect.width - totalButtonSize) / 6;
            verticalSpacing = (levelStageRect.height - (buttonWidth * 6)) / 7;

            startX = -((levelStageRect.width / 2) - (buttonWidth / 2) - horizontalSpacing);
            startY = (levelStageRect.height / 2) - (buttonWidth / 2) - verticalSpacing;

            stageScreenPosition = levelStagePrefab.transform.localPosition;

            // stage containers are cheap (one per 30 levels) and stay as before; the actual
            // LevelButtons are what get virtualized, populated on demand by RefreshVisibleButtons
            for (int i = 0; i < pages; i++)
            {
                GameObject levelStage = Instantiate(levelStagePrefab, transform);
                levelStage.SetActive(true);
                levelStages[i] = levelStage;

                levelStage.transform.localPosition = new Vector3(stageScreenPosition.x + (Screen.width) * i, stageScreenPosition.y, stageScreenPosition.z);
            }
        }

        /// <summary>
        /// Ensures LevelButtons exist (from the pool) only for <paramref name="centerStage"/> and
        /// its immediate neighbors, returning any page's buttons to the pool once that page falls
        /// outside the window. Keeps live LevelButton count bounded regardless of total level count.
        /// </summary>
        private void RefreshVisibleButtons(int centerStage)
        {
            if (levelStages == null || levelStages.Length == 0) { return; }

            if (objectPool == null)
            {
                objectPool = new ObjectPool<LevelButton>(levelButtonPrefab, levelButtonPerScreen, transform);
            }

            SaveData data = SavingSystem.Instance.Load();
            int completedLevels = data.completedLevel;

            HashSet<int> wantedPages = new HashSet<int>();
            for (int p = centerStage - 1; p <= centerStage + 1; p++)
            {
                if (p >= 0 && p < levelStages.Length) { wantedPages.Add(p); }
            }

            List<int> pagesToRelease = new List<int>();
            foreach (var kvp in activePageButtons)
            {
                if (!wantedPages.Contains(kvp.Key)) { pagesToRelease.Add(kvp.Key); }
            }
            foreach (int page in pagesToRelease)
            {
                foreach (LevelButton button in activePageButtons[page])
                {
                    objectPool.ReturnObject(button);
                }
                activePageButtons.Remove(page);
            }

            foreach (int page in wantedPages)
            {
                if (activePageButtons.ContainsKey(page)) { continue; }

                List<LevelButton> pageButtons = new List<LevelButton>();
                float currentX = startX;
                float currentY = startY;
                int firstLevelOnPage = page * levelButtonPerScreen + 1;

                for (int j = 0; j < 6; j++)
                {
                    for (int k = 0; k < 5; k++)
                    {
                        int level = firstLevelOnPage + (j * 5 + k);
                        if (level > totalLevels) { break; }

                        LevelButton button = objectPool.GetObject();
                        button.ThisTransform.SetParent(levelStages[page].transform, false);
                        button.ThisTransform.localScale = Vector3.one;
                        button.ThisTransform.sizeDelta = new Vector2(buttonWidth, buttonWidth);
                        button.ThisTransform.localPosition = new Vector3(currentX, currentY);

                        bool isCompleted = (level <= completedLevels);
                        int levelCompletionMoves = isCompleted ? data.completedlevelMoves[level - 1] : 0;
                        Color color = isCompleted ? completeLevel : unlockedLevel;

                        button.SetDetails(level, color, levelCompletionMoves);
                        pageButtons.Add(button);

                        currentX += buttonWidth + horizontalSpacing;
                    }

                    currentX = startX;
                    currentY -= buttonWidth + verticalSpacing;
                }

                activePageButtons[page] = pageButtons;
            }
        }

        private void SetButtons()
        {
            SaveData data = SavingSystem.Instance.Load();
            int nextLevel = data.completedLevel + 1;

            currentstageOnScreen = Mathf.CeilToInt((float)nextLevel / levelButtonPerScreen);
            currentstageOnScreen--;
            currentstageOnScreen = Mathf.Clamp(currentstageOnScreen, 0, Mathf.Max(0, levelStages.Length - 1));

            RefreshVisibleButtons(currentstageOnScreen);
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
                    RefreshVisibleButtons(currentstageOnScreen);
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
                RefreshVisibleButtons(currentstageOnScreen);
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
                RefreshVisibleButtons(currentstageOnScreen);
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
