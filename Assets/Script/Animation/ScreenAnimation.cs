using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class ScreenAnimation : MonoBehaviour
{
    [SerializeField] private float sizeIncrementSpeed = 0.5f;
    [SerializeField] private float objectActivationSpeed = 0.5f;
    [SerializeField] private List<Transform> objects;
    private WaitForSeconds waitforSeconds;


    private void OnEnable()
    {
        if(waitforSeconds == null) { waitforSeconds = new WaitForSeconds(objectActivationSpeed); }
        StartCoroutine(StartAnimation());
    }
   
    private IEnumerator StartAnimation()
    {
        foreach (Transform obj in objects)
        {
            obj.localScale = Vector3.zero;
        }

        foreach (Transform obj in objects)
        {
            obj.localScale = Vector3.zero;
            obj.DOScale(1, sizeIncrementSpeed);

            yield return waitforSeconds;
        }
    }

    public float TotolTime
    {
        get
        {
            return (objectActivationSpeed * objects.Count + objectActivationSpeed);
        }
    }
}
