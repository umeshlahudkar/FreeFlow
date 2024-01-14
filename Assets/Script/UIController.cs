using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class UIController : Singleton<UIController>
{
    [SerializeField] private BoardGenerator boardGenerator;
    [SerializeField] private LevelButtonSpawner levelButtonSpawner;

    [SerializeField] private GameObject mainMenuScreen;
    [SerializeField] private Canvas mainMenuCanvas;
    [SerializeField] private Canvas gamePlayCanvas;

    [Header("Level Complete Screen")]
    [SerializeField] private GameObject levelCompleteScreen;
    [SerializeField] private TextMeshProUGUI levelCompleteMovesCount;

    [Header("Gameplay")]
    [SerializeField] private TextMeshProUGUI gameplaylevelText;
    [SerializeField] private TextMeshProUGUI gameplayPairText;
    [SerializeField] private TextMeshProUGUI gameplayMoveText;


    private LevelDataSO levelDataSO;
    private LevelData currentLevelData;
    private int currentLevel;

    private void Awake()
    {
        levelDataSO = Resources.Load<LevelDataSO>("LevelData");
    }

    private void Start()
    {
         if(!mainMenuCanvas.gameObject.activeSelf)
         {
            mainMenuCanvas.gameObject.SetActive(true);
         }
    }

    public int CurrentLevel { get { return currentLevel; } }
    
    public int CurrentLevelGoal { get { return currentLevelData.pairCount; } }

    public void LoadLevel(int levelNumber)
    {
        if(levelNumber <= levelDataSO.levels.Length)
        {
            currentLevel = levelNumber;

            currentLevelData = levelDataSO.levels[levelNumber - 1];

            boardGenerator.GenerateBoard(currentLevelData);

            levelButtonSpawner.gameObject.SetActive(false);
            mainMenuCanvas.gameObject.SetActive(false);
            gamePlayCanvas.gameObject.SetActive(true);
            levelCompleteScreen.SetActive(false);

            gameplaylevelText.text = "Level : " + levelNumber;
            UpdatePairCount(0);
            UpdateMovesCount(0);

            GamePlayController.Instance.ResetGameplay();
            GamePlayController.Instance.GameState = GameState.Playing;
        }
    }

    public void OnNextLevelButtonClick()
    {
        if(InputManager.Instance.CanInput())
        {
            currentLevel++;
            if (currentLevel > levelDataSO.levels.Length) { currentLevel = 1; }

            LoadLevel(currentLevel);
        }
    }

    public void OnPlayButtonClick()
    {
        if (InputManager.Instance.CanInput())
        {
            levelButtonSpawner.gameObject.SetActive(true);
            levelButtonSpawner.PrepareLevelScreen(levelDataSO.levels.Length);
        }
    }

    public void OnGameplayBackButtonClick()
    {
        if (InputManager.Instance.CanInput())
        {
            boardGenerator.ResetGrid();

            levelButtonSpawner.gameObject.SetActive(false);
            mainMenuCanvas.gameObject.SetActive(true);
            gamePlayCanvas.gameObject.SetActive(false);
        }
    }

    public void OnLevelBackButtonClick()
    {
        if (InputManager.Instance.CanInput())
        {
            levelButtonSpawner.gameObject.SetActive(false);
        }
    }

    public void OnQuitButtonClick()
    {
        if (InputManager.Instance.CanInput())
        {
            Application.Quit();
        }
    }

    public void ActivateLevelCompleteScreen(int movesCount)
    {
        levelCompleteScreen.SetActive(true);
        levelCompleteMovesCount.text = "You Completed the level in " + movesCount + " moves.";
    }

    public void UpdatePairCount(int completePair)
    {
        gameplayPairText.text = "Pair : " + completePair + "/" + currentLevelData.pairCount;
    }

    public void UpdateMovesCount(int moves)
    {
        gameplayMoveText.text = "Moves : " + moves;
    }
}
