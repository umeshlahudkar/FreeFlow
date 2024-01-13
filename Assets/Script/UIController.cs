using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UIController : MonoBehaviour
{
    [SerializeField] private BoardGenerator boardGenerator;

    private LevelDataSO levelDataSO;

    private void Awake()
    {
        levelDataSO = Resources.Load<LevelDataSO>("LevelData");
        if(levelDataSO != null)
        {
            Debug.Log(".........");
        }
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
