using UnityEngine;
using TMPro;
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
        [SerializeField] private GameObject gameplayScreen;

        [Header("Level Data SO")]
        [SerializeField] LevelDataSO levelDataSO;

        [Header("Pause screen")]
        [SerializeField] GameObject pauseScreen;

        [Header("Setting screen")]
        [SerializeField] GameObject settingScreen;

        private LevelData currentLevelData;
        private int currentLevel;

        public int CurrentLevel { get { return currentLevel; } }

        public int CurrentLevelGoal { get { return currentLevelData.pairCount; } }

        private void Start()
        {
            levelScreenController.SpawnLevelButtons(levelDataSO.levels.Length);
        }

        /// <summary>
        /// Loads the specified game level and initializes relevant UI elements.
        /// </summary>
        /// <param name="levelNumber">The number of the level to load.</param>
        public void LoadLevel(int levelNumber)
        {
            if (levelNumber <= levelDataSO.levels.Length)
            {
                GamePlayController.Instance.ResetGameplay();
                boardGenerator.ResetBoard();

                currentLevel = levelNumber;

                currentLevelData = levelDataSO.levels[levelNumber - 1];

                levelScreenController.gameObject.SetActive(false);
                gameplayScreen.SetActive(false);
                mainMenuScreen.SetActive(false);

                gameOverScreen.SetActive(false);
                gameplayScreen.SetActive(true);

                boardGenerator.GenerateBoard(currentLevelData);

                gameplaylevelText.text = "Level : " + levelNumber;
                UpdatePairCount(0);
                UpdateMovesCount(0);
            }
        }

        /// <summary>
        /// Gets called when next level button click from the lwvwl win screen,
        /// Handles the next level loading
        /// </summary>
        private void LoadNextLevel()
        {
            currentLevel++;
            if (currentLevel > levelDataSO.levels.Length) { currentLevel = 1; }
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
