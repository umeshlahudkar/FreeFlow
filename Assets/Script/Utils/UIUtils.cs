using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class UIUtils 
{
    public static void Activate(this GameObject obj, float activateTime = 0.2f)
    {
        obj.SetActive(true);
        obj.transform.localScale = Vector3.zero;

        MonoBehaviour monoBehaviour = obj.GetComponent<MonoBehaviour>();
        if (monoBehaviour != null)
        {
            monoBehaviour.StartCoroutine(ScaleOverTime(obj.transform, Vector3.one, activateTime));
        }
    }

    public static void Deactivate(this GameObject obj, float deactivateTime = 0.2f)
    {
        MonoBehaviour monoBehaviour = obj.GetComponent<MonoBehaviour>();
        if (monoBehaviour != null)
        {
            monoBehaviour.StartCoroutine(ScaleOverTime(obj.transform, Vector3.zero, deactivateTime, () => obj.SetActive(false)));
        }
       
    }

    private static IEnumerator ScaleOverTime(Transform transform, Vector3 targetScale, float duration, System.Action onComplete = null)
    {
        float elapsedTime = 0f;
        Vector3 initialScale = transform.localScale;

        while (elapsedTime < duration)
        {
            transform.localScale = Vector3.Lerp(initialScale, targetScale, elapsedTime / duration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        transform.localScale = targetScale;

        onComplete?.Invoke();
    }
}
