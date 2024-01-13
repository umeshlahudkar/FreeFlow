using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Block : MonoBehaviour
{
    [SerializeField] private Image dotImage;
    [SerializeField] private Image[] directionImages;

    private int rowIndex;
    private int coloumIndex;
    private DotType dotType;
    private bool isDotPresent;
    private DotType highlightedDotType;

    public void SetBlock(DotType type, int rowIndex, int coloumIndex)
    {
        this.rowIndex = rowIndex;
        this.coloumIndex = coloumIndex;
        dotType = type;

        if(dotType != DotType.None)
        {
            isDotPresent = true;
            dotImage.gameObject.SetActive(true);
            dotImage.color = GamePlay.instance.GetColor(type);
        }
    }

    public void HighlightBlock(Direction dir, DotType type)
    {
        //DisableAllDirImages();
        highlightedDotType = type;
        directionImages[((int)dir - 1)].gameObject.SetActive(true);
        directionImages[((int)dir - 1)].color = GamePlay.instance.GetColor(type);
    }

    public void ResetAllHighlightDirection()
    {
        for(int i = 0; i < directionImages.Length; i++)
        {
            directionImages[i].gameObject.SetActive(false);
        }

        highlightedDotType = DotType.None;
    }

    public void ResetHighlightDirection(Direction dir)
    {
        directionImages[((int)dir - 1)].gameObject.SetActive(false);
    }

    public bool IsDotPresent
    {
        get { return isDotPresent; }
    }

    public DotType DotType
    {
        get { return dotType; }
    }

    public DotType HighlightedDotType
    {
        get { return highlightedDotType; }
    }

    public int Row_ID { get { return rowIndex; } }
    public int Coloum_ID { get { return coloumIndex; } }
}
