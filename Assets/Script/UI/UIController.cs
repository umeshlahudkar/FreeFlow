using UnityEngine;
using TMPro;
using FreeFlow.GamePlay;
using FreeFlow.Input;
using FreeFlow.Util;
using DG.Tweening;

namespace FreeFlow.UI
{
    /// <summary>
    /// Manages the UI elements and controls the flow of the game
    /// </summary>
    public class UIController : Singleton<UIController>
    {
        [Header("Menu Screen")]
        [SerializeField] private LevelScreenController levelButtonSpawner;
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

        private LevelData currentLevelData;
        private int currentLevel;

        public int CurrentLevel { get { return currentLevel; } }

        public int CurrentLevelGoal { get { return currentLevelData.pairCount; } }

        private void Start()
        {
            levelButtonSpawner.PrepareLevelScreen(levelDataSO.levels.Length);
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

                currentLevel = levelNumber;

                currentLevelData = levelDataSO.levels[levelNumber - 1];

                levelButtonSpawner.gameObject.SetActive(false);
                gameplayScreen.SetActive(false);
                mainMenuScreen.SetActive(false);

                gameOverScreen.SetActive(false);
                gameplayScreen.SetActive(true);

                boardGenerator.ResetBoard();
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
                levelButtonSpawner.gameObject.Activate();
                mainMenuScreen.SetActive(false);
                levelButtonSpawner.LoadLevelScreen(levelDataSO.levels.Length);
            }
        }

        /// <summary>
        ///  Gets called when Main-Menu button click from the gameplay screen,
        ///  activates main menu screen
        /// </summary>
        public void OnGameplayBackButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                boardGenerator.ResetBoard();

                levelButtonSpawner.gameObject.SetActive(false);
                mainMenuScreen.SetActive(true);
                gameplayScreen.SetActive(false);
                gameOverScreen.SetActive(false);
            }
        }

        public void OnPauseButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                GamePlayController.Instance.GameState = Enums.GameState.Paused;

                pauseScreen.transform.localPosition += new Vector3(Screen.width, 0, 0);
                pauseScreen.SetActive(true);
                pauseScreen.transform.DOLocalMove(Vector3.zero, 0.25f);
            }
        }

        public void OnResumeButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                pauseScreen.transform.DOLocalMove(new Vector3(Screen.width, 0, 0), 0.25f).OnComplete( ()=> 
                {
                    GamePlayController.Instance.GameState = Enums.GameState.Playing;
                    pauseScreen.SetActive(false);
                    pauseScreen.transform.localPosition = Vector3.zero;
                });
            }
        }

        public void OnPauseScreenRetryButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                GamePlayController.Instance.ResetGameplay();
                boardGenerator.ResetBoard();

                pauseScreen.transform.DOLocalMove(new Vector3(Screen.width, 0, 0), 0.25f).OnComplete(() =>
                {
                    pauseScreen.SetActive(false);
                    pauseScreen.transform.localPosition = Vector3.zero;

                    LoadLevel(currentLevel);
                });
            }
        }

        public void OnPauseScreenHomeButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                gameOverScreen.SetActive(false);
                gameplayScreen.SetActive(false);

                pauseScreen.transform.DOLocalMove(new Vector3(Screen.width, 0, 0), 0.25f).OnComplete(() =>
                {
                    pauseScreen.SetActive(false);
                    pauseScreen.transform.localPosition = Vector3.zero;

                    mainMenuScreen.SetActive(true);
                });
            }
        }

        /// <summary>
        ///  Gets called when Back button click from the level screen,
        ///  activates main menu screen
        /// </summary>
        public void OnLevelBackButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                mainMenuScreen.SetActive(true);
                levelButtonSpawner.gameObject.Deactivate();
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

            gameOverScreen.transform.localPosition += new Vector3(Screen.width, 0, 0);
            gameOverScreen.transform.DOLocalMove(Vector3.zero, 0.25f);
        }

        public void OnGameOverScreenRetryButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                GamePlayController.Instance.ResetGameplay();
                boardGenerator.ResetBoard();

                gameOverScreen.transform.DOLocalMove(new Vector3(Screen.width, 0, 0), 0.25f).OnComplete(() =>
                {
                    gameOverScreen.SetActive(false);
                    gameOverScreen.transform.localPosition = Vector3.zero;

                    LoadLevel(currentLevel);
                });
            }
        }

        public void OnGameOverScreenHomeButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                pauseScreen.SetActive(false);
                gameplayScreen.SetActive(false);

                gameOverScreen.transform.DOLocalMove(new Vector3(Screen.width, 0, 0), 0.25f).OnComplete(() =>
                {
                    gameOverScreen.SetActive(false);
                    gameOverScreen.transform.localPosition = Vector3.zero;

                    mainMenuScreen.SetActive(true);
                });
            }
        }

        public void OnGameOverScreenNextButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                GamePlayController.Instance.ResetGameplay();
                boardGenerator.ResetBoard();

                gameOverScreen.transform.DOLocalMove(new Vector3(Screen.width, 0, 0), 0.25f).OnComplete(() =>
                {
                    gameOverScreen.SetActive(false);
                    gameOverScreen.transform.localPosition = Vector3.zero;

                    LoadNextLevel();
                });
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
