using UnityEngine;
using DG.Tweening;
using UnityEngine.Events;

public static class UIUtils
{
    public static void Activate(this GameObject obj, float duration = 0.25f, UnityAction action = null)
    {
        obj.transform.localPosition = new Vector3(Screen.width, 0, 0);
        obj.SetActive(true);
        obj.transform.DOLocalMove(Vector3.zero, duration).OnComplete(() =>
        {
            action?.Invoke();
        });
    }

    public static void Deactivate(this GameObject obj, float duration = 0.25f, UnityAction action = null)
    {
        obj.transform.DOLocalMove(new Vector3(Screen.width, 0, 0), duration).OnComplete(() =>
        {
            obj.SetActive(false);
            obj.transform.localPosition = Vector3.zero;
            action?.Invoke();
        });
    }
}
