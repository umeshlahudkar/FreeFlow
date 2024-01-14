using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class UIController : MonoBehaviour
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

    public static UIController instance;

    private void Awake()
    {
        instance = this;
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

            GamePlayController.instance.ResetGameplay();
            GamePlayController.instance.GameState = GameState.Playing;
        }
    }

    public void OnNextLevelButtonClick()
    {
        currentLevel++;
        if(currentLevel > levelDataSO.levels.Length) { currentLevel = 1; }

        LoadLevel(currentLevel);
    }

    public void OnPlayButtonClick()
    {
        levelButtonSpawner.gameObject.SetActive(true);
        levelButtonSpawner.PrepareLevelScreen(levelDataSO.levels.Length);
    }

    public void OnGameplayBackButtonClick()
    {
        boardGenerator.ResetGrid();

        levelButtonSpawner.gameObject.SetActive(false);
        mainMenuCanvas.gameObject.SetActive(true);
        gamePlayCanvas.gameObject.SetActive(false);
    }

    public void OnLevelBackButtonClick()
    {
        levelButtonSpawner.gameObject.SetActive(false);
    }

    public void OnQuitButtonClick()
    {
        Application.Quit();
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
