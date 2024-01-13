using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UIController : MonoBehaviour
{
    [SerializeField] private BoardGenerator boardGenerator;

    private LevelDataSO levelDataSO;

    public static UIController instance;

    private void Awake()
    {
        instance = this;
        levelDataSO = Resources.Load<LevelDataSO>("LevelData");
    }

    public void LoadLevel(int levelNumber)
    {

    }

    public void OnPlayButtonClick()
    {
        Debug.Log("play button click");
    }

    public void OnQuitButtonClick()
    {
        Debug.Log("quit button click");
    }
}
