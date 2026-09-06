using UnityEngine;
using UnityEngine.Events;

public static class UIUtils
{
    // duration is intentionally unused now (kept only so every existing call site across the
    // screen controllers -- which all pass it -- keeps compiling): the slide-from-off-screen
    // transition every screen used to play on open/close was removed project-wide per explicit
    // request. Activate/Deactivate now just show/hide instantly.
    public static void Activate(this GameObject obj, float duration = 0.25f, UnityAction action = null)
    {
        obj.transform.localPosition = Vector3.zero;
        obj.SetActive(true);
        action?.Invoke();
    }

    public static void Deactivate(this GameObject obj, float duration = 0.25f, UnityAction action = null)
    {
        obj.SetActive(false);
        obj.transform.localPosition = Vector3.zero;
        action?.Invoke();
    }
}
