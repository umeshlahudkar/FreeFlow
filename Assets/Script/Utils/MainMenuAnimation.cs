using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using DG.Tweening;

public class MainMenuAnimation : MonoBehaviour
{
    [SerializeField] private RectTransform gameLabel;
    [SerializeField] private RectTransform lowerScreen;
    [SerializeField] private RectTransform playButton;

    private Vector3 gameLabelOriginalPos;
    private Vector3 lowerScreenOriginalPos;

    private bool screenAnimPlaying = false;

    private float timer = 0f;
    private float scaleInterval = 2f;
    private float scaleUpTime = 1f;
    private float scaleDownTime = 0.5f;
    private float minScale = 0.8f;
    private float maxScale = 1f;
    private float startDelay = 2f;


    private void Awake()
    {
        gameLabelOriginalPos = gameLabel.position;
        lowerScreenOriginalPos = lowerScreen.position;
    }

    private void OnEnable()
    {
        timer = 0;
        startDelay = 3f;
        PlayScreenOpenAnimation();
    }

    private float GetGameLabelPositionOutsideScreen()
    {
        return (Screen.height - gameLabelOriginalPos.y) + gameLabel.rect.height / 2;
    }

    private float GetLowerScreenPositionOutsideScreen()
    {
        return (lowerScreenOriginalPos.y + lowerScreen.rect.height / 2);
    }

    public void PlayScreenOpenAnimation()
    {
        screenAnimPlaying = true;
        gameLabel.position += new Vector3(0, GetGameLabelPositionOutsideScreen(), 0);
        lowerScreen.position += new Vector3(0, -GetLowerScreenPositionOutsideScreen(), 0);

        playButton.localScale = Vector3.zero;

        gameLabel.DOMove(gameLabelOriginalPos, 0.5f);
        lowerScreen.DOMove(lowerScreenOriginalPos, 0.5f);
        playButton.DOScale(new Vector3(1.25f, 1.25f, 1.25f), 0.5f).OnComplete(() =>
        {
            playButton.DOScale(Vector3.one, 0.5f).OnComplete(()=>screenAnimPlaying = false);
        });
    }

    public void PlayScreenCloseAnimation(UnityAction onAnimationComplete = null)
    {
        screenAnimPlaying = true;
        gameLabel.DOMove(gameLabelOriginalPos + new Vector3(0, GetGameLabelPositionOutsideScreen(), 0), 0.5f);
        lowerScreen.DOMove(lowerScreenOriginalPos + new Vector3(0, -GetLowerScreenPositionOutsideScreen(), 0), 0.5f);
        playButton.DOScale(Vector3.zero, 0.5f).OnComplete(() =>
        {
            onAnimationComplete?.Invoke();
            screenAnimPlaying = false;
        });
    }

    private void Update()
    {
        if(screenAnimPlaying)
        {
            startDelay = 3.0f;
            return;
        }

        startDelay -= Time.deltaTime;
        if(startDelay <= 0)
        {
            timer += Time.deltaTime;
            if (timer >= scaleInterval)
            {
                timer = 0f;

                playButton.DOScale(minScale, scaleDownTime).OnComplete(() =>
                {
                    playButton.DOScale(maxScale, scaleUpTime);
                });
            }

            startDelay = 0;
        }
    }
}
