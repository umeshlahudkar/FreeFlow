using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Block : MonoBehaviour
{
    [SerializeField] private Image pairDotImage;
    [SerializeField] private Image[] directionImages;

    private int row_ID;
    private int coloum_ID;

    private bool isPairDotPresent;

    private PairColorType pairColorType;
    private PairColorType highlightedColorType;

    public void SetBlock(PairColorType type, int rowIndex, int coloumIndex)
    {
        this.row_ID = rowIndex;
        this.coloum_ID = coloumIndex;
        pairColorType = type;

        if(pairColorType != PairColorType.None)
        {
            isPairDotPresent = true;
            pairDotImage.gameObject.SetActive(true);
            pairDotImage.color = GamePlayController.Instance.GetColor(type);
        }
    }

    public void HighlightBlock(Direction dir, PairColorType type)
    {
        //DisableAllDirImages();
        highlightedColorType = type;
        directionImages[((int)dir - 1)].gameObject.SetActive(true);
        directionImages[((int)dir - 1)].color = GamePlayController.Instance.GetColor(type);
    }

    public void ResetAllHighlightDirection()
    {
        for(int i = 0; i < directionImages.Length; i++)
        {
            directionImages[i].gameObject.SetActive(false);
        }

        highlightedColorType = PairColorType.None;
    }

    public void ResetHighlightDirection(Direction dir)
    {
        directionImages[((int)dir - 1)].gameObject.SetActive(false);
    }

    public bool IsDotPresent
    {
        get { return isPairDotPresent; }
    }

    public PairColorType DotType
    {
        get { return pairColorType; }
    }

    public PairColorType HighlightedDotType
    {
        get { return highlightedColorType; }
    }

    public int Row_ID { get { return row_ID; } }
    public int Coloum_ID { get { return coloum_ID; } }

    public void ResetBlock()
    {
        this.row_ID = -1;
        this.coloum_ID = -1;
        pairColorType = PairColorType.None;
        highlightedColorType = PairColorType.None;
        isPairDotPresent = false;

        pairDotImage.gameObject.SetActive(false);
        ResetAllHighlightDirection();
    }
}
