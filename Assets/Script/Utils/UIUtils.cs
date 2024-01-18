using UnityEngine;
using DG.Tweening;

public static class UIUtils
{
    public static void Activate(this GameObject obj, float duration = 0.25f)
    {
        obj.transform.localScale = Vector3.zero;
        obj.SetActive(true);
        obj.transform.DOScale(Vector3.one, duration).SetEase(Ease.Linear);
    }

    public static void Deactivate(this GameObject obj, float duration = 0.1f)
    {
        obj.transform.DOScale(Vector3.zero, duration).SetEase(Ease.Linear)
            .OnComplete(() => obj.SetActive(false));
    }
}
