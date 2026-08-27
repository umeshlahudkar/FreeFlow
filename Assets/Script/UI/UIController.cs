using UnityEngine;
using TMPro;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using FreeFlow.Input;
using FreeFlow.Util;

namespace FreeFlow.UI
{
    /// <summary>
    /// Manages the UI elements and controls the flow of the game
    /// </summary>
    public class UIController : Singleton<UIController>
    {
        [Header("Menu Screen")]
        [SerializeField] private LevelScreenController levelScreenController;
        [SerializeField] private GameObject mainMenuScreen;
        [SerializeField] private BoardGenerator boardGenerator;

        [Header("Game over Screen")]
        [SerializeField] private GameObject gameOverScreen;
        [SerializeField] private TextMeshProUGUI gameOverMsgText;
        [SerializeField] private TextMeshProUGUI gameOverLevelText;

        [Header("Gameplay")]
        [SerializeField] private TextMeshProUGUI gameplaylevelText;
        [SerializeField] private TextMeshProUGUI gameplayPairText;
        [SerializeField] private TextMeshProUGUI gameplayMoveText;

        // Names the mechanic(s) the current level actually contains. Derived from the level
        // data every load rather than authored per level, so it cannot drift out of step with
        // the board -- see DescribeMechanics.
        [SerializeField] private TextMeshProUGUI gameplayMechanicText;

        [SerializeField] private GameObject gameplayScreen;

        [Header("Level Data")]
        // total level count is authored metadata, not derived from a loaded array --
        // each level's grid data now lives in its own SingleLevelDataSO under
        // Resources/Levels/, loaded on demand so memory scales with levels visited,
        // not levels that exist. Keep this in sync when adding new level assets.
        [SerializeField] private int totalLevelCount;

        [Header("Pause screen")]
        [SerializeField] GameObject pauseScreen;

        [Header("Setting screen")]
        [SerializeField] GameObject settingScreen;

        private LevelData currentLevelData;
        private SingleLevelDataSO currentLevelDataAsset;
        private int currentLevel;

        public int CurrentLevel { get { return currentLevel; } }
        public int TotalLevelCount { get { return totalLevelCount; } }

        public int CurrentLevelGoal { get { return currentLevelData.pairCount; } }

        private void Start()
        {
            levelScreenController.SpawnLevelButtons(totalLevelCount);
        }

        /// <summary>
        /// Loads the specified game level and initializes relevant UI elements.
        /// </summary>
        /// <param name="levelNumber">The number of the level to load.</param>
        public void LoadLevel(int levelNumber)
        {
            if (levelNumber <= totalLevelCount)
            {
                GamePlayController.Instance.ResetGameplay();
                boardGenerator.ResetBoard();

                currentLevel = levelNumber;

                if (currentLevelDataAsset != null)
                {
                    Resources.UnloadAsset(currentLevelDataAsset);
                }
                currentLevelDataAsset = Resources.Load<SingleLevelDataSO>("Levels/Level_" + levelNumber);
                currentLevelData = currentLevelDataAsset.levelData;

                levelScreenController.gameObject.SetActive(false);
                gameplayScreen.SetActive(false);
                mainMenuScreen.SetActive(false);

                gameOverScreen.SetActive(false);
                gameplayScreen.SetActive(true);

                boardGenerator.GenerateBoard(currentLevelData);

                gameplaylevelText.text = "Level : " + levelNumber;
                UpdateMechanicLabel(currentLevelData);
                UpdatePairCount(0);
                UpdateMovesCount(0);
            }
        }

        private void UpdateMechanicLabel(LevelData data)
        {
            if (gameplayMechanicText == null) { return; }
            gameplayMechanicText.text = "Mechanic : " + DescribeMechanics(data);
        }

        /// <summary>
        /// Names the mechanics present in <paramref name="data"/>, read straight off the level's
        /// own cells and constraints. Derived rather than authored on purpose: a hand-written
        /// label would be one more field to forget when a board changes, and this cannot
        /// disagree with what the player is looking at. Levels with no mechanic read "Basic",
        /// and a board carrying more than one lists them all.
        /// </summary>
        private static string DescribeMechanics(LevelData data)
        {
            bool blocked = false, walls = false, checkpoint = false, forbidden = false;
            bool oneWay = false;
            bool arrow = false, bridge = false;
            bool sharedGoal = false, permitted = false;

            if (data.gridRows != null)
            {
                for (int i = 0; i < data.gridRows.Length; i++)
                {
                    GridRow row = data.gridRows[i];

                    if (row.blockType != null)
                    {
                        for (int j = 0; j < row.blockType.Length; j++)
                        {
                            switch (row.blockType[j])
                            {
                                case BlockType.Blocked: blocked = true; break;
                                case BlockType.Checkpoint: checkpoint = true; break;
                                case BlockType.ForbiddenForPair: forbidden = true; break;
                                case BlockType.AllowedForPairs: permitted = true; break;
                                case BlockType.OneWay: oneWay = true; break;
                                case BlockType.Arrow: arrow = true; break;
                                case BlockType.Bridge: bridge = true; break;
                            }
                        }
                    }

                    // secondPairId is the only one of the three a permission rule also reads, so
                    // it is the only one that needs the guard -- see Block.SecondIdNamesAPair.
                    if (row.secondPairId != null)
                    {
                        for (int j = 0; j < row.secondPairId.Length; j++)
                        {
                            if (row.secondPairId[j] == 0) { continue; }

                            bool namesAPair = row.blockType != null
                                           && j < row.blockType.Length
                                           && Block.SecondIdNamesAPair(row.blockType[j]);
                            if (!namesAPair) { sharedGoal = true; }
                        }
                    }

                    if (HasAnyNonZero(row.thirdPairId) || HasAnyNonZero(row.fourthPairId))
                    {
                        sharedGoal = true;
                    }

                    if (row.wallMask != null)
                    {
                        for (int j = 0; j < row.wallMask.Length; j++)
                        {
                            if (row.wallMask[j] != 0) { walls = true; }
                        }
                    }
                }
            }

            string description = string.Empty;
            AppendMechanic(ref description, blocked, "Blocked cell");
            AppendMechanic(ref description, walls, "Wall");
            AppendMechanic(ref description, oneWay, "One-way");
            AppendMechanic(ref description, arrow, "Arrow");
            AppendMechanic(ref description, forbidden, "Forbidden cell");
            AppendMechanic(ref description, permitted, "Permitted colours");
            AppendMechanic(ref description, bridge, "Bridge");
            AppendMechanic(ref description, sharedGoal, "Shared destination");
            AppendMechanic(ref description, checkpoint, "Checkpoint");

            return description.Length > 0 ? description : "Basic";
        }

        private static bool HasAnyNonZero(int[] values)
        {
            if (values == null) { return false; }

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != 0) { return true; }
            }

            return false;
        }

        private static void AppendMechanic(ref string description, bool present, string name)
        {
            if (!present) { return; }
            description = description.Length > 0 ? description + " + " + name : name;
        }

        /// <summary>
        /// Gets called when next level button click from the lwvwl win screen,
        /// Handles the next level loading
        /// </summary>
        private void LoadNextLevel()
        {
            currentLevel++;
            if (currentLevel > totalLevelCount) { currentLevel = 1; }
            LoadLevel(currentLevel);
        }

        /// <summary>
        /// Gets called when Play button click from main menu,
        /// activates level screen
        /// </summary>
        public void OnPlayButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                mainMenuScreen.SetActive(false);
                //levelScreenController.LoadLevelScreen(levelDataSO.levels.Length);

                levelScreenController.gameObject.Activate();
            }
        }

        public void OnLevelScreenBackButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                levelScreenController.gameObject.Deactivate(0.25f, () => mainMenuScreen.SetActive(true));
            }
        }

        public void OnPauseButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                GamePlayController.Instance.GameState = Enums.GameState.Paused;
                pauseScreen.Activate();
            }
        }

        public void OnResumeButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                pauseScreen.Deactivate(0.25f, () => GamePlayController.Instance.GameState = Enums.GameState.Playing);
            }
        }

        public void OnPauseScreenRetryButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                GamePlayController.Instance.ResetGameplay();
                boardGenerator.ResetBoard();
                pauseScreen.Deactivate(0.25f, () => LoadLevel(currentLevel));
            }
        }

        public void OnPauseScreenHomeButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                GamePlayController.Instance.ResetGameplay();
                boardGenerator.ResetBoard();

                gameOverScreen.SetActive(false);
                gameplayScreen.SetActive(false);

                pauseScreen.Deactivate(0.25f, () => mainMenuScreen.SetActive(true));
            }
        }

        /// <summary>
        ///  Gets called when Quit button click from the Main menu screen,
        ///  closes the game
        /// </summary>
        public void OnQuitButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                Application.Quit();
            }
        }

        /// <summary>
        /// Activates level complete screen,
        /// Updates move count on level screen
        /// </summary>
        /// <param name="movesCount"></param>
        public void ActivateLevelCompleteScreen(int movesCount)
        {
            gameOverScreen.SetActive(true);
            gameOverMsgText.text = "Congrats!, You Completed the level in " + movesCount + " moves.";
            gameOverLevelText.text = "Level " + currentLevel;

            gameOverScreen.Activate();
        }

        public void OnGameOverScreenRetryButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                GamePlayController.Instance.ResetGameplay();
                boardGenerator.ResetBoard();

                gameOverScreen.Deactivate(0.25f, () => LoadLevel(currentLevel));
            }
        }

        public void OnGameOverScreenHomeButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                GamePlayController.Instance.ResetGameplay();
                boardGenerator.ResetBoard();

                pauseScreen.SetActive(false);
                gameplayScreen.SetActive(false);

                gameOverScreen.Deactivate(0.25f, () => mainMenuScreen.SetActive(true));
            }
        }

        public void OnGameOverScreenNextButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                GamePlayController.Instance.ResetGameplay();
                boardGenerator.ResetBoard();

                gameOverScreen.Deactivate(0.25f, ()=>LoadNextLevel());
            }
        }

        public void OnSeetingButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                settingScreen.Activate();
            }
        }

        /// <summary>
        /// Update and shows the completed pair count, basically on game screen
        /// </summary>
        /// <param name="completePair">Count of completed pairs</param>
        public void UpdatePairCount(int completePair)
        {
            gameplayPairText.text = "Pair : " + completePair + "/" + currentLevelData.pairCount;
        }

        /// <summary>
        /// Update and shows the completed moves count, basically on game screen
        /// </summary>
        /// <param name="moves">Number of moves</param>
        public void UpdateMovesCount(int moves)
        {
            gameplayMoveText.text = "Moves : " + moves;
        }
    }
}
