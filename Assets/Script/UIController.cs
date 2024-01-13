using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UIController : MonoBehaviour
{
    [SerializeField] private BoardGenerator boardGenerator;
    [SerializeField] private LevelButtonSpawner levelButtonSpawner;
    [SerializeField] private GameObject mainMenuScreen;
    [SerializeField] private Canvas mainMenuCanvas;
    [SerializeField] private Canvas gamePlayCanvas;

    private LevelDataSO levelDataSO;
    private int currentLevel;

    public static UIController instance;

    private void Awake()
    {
        instance = this;
        levelDataSO = Resources.Load<LevelDataSO>("LevelData");
    }

    public void LoadLevel(int levelNumber)
    {
        if(levelNumber <= levelDataSO.levels.Length)
        {
            currentLevel = levelNumber;
            boardGenerator.GenerateBoard(levelDataSO.levels[levelNumber - 1]);

            levelButtonSpawner.gameObject.SetActive(false);
            mainMenuScreen.SetActive(false);
        }
    }

    public void OnPlayButtonClick()
    {
        levelButtonSpawner.gameObject.SetActive(true);
        levelButtonSpawner.PrepareLevelScreen(levelDataSO.levels.Length);
    }

    public void OnGameplayBackButtonClick()
    {
        mainMenuScreen.SetActive(false);
        boardGenerator.ResetGrid();
    }

    public void OnLevelBackButtonClick()
    {

    }

    public void OnQuitButtonClick()
    {
        Application.Quit();
    }
}
