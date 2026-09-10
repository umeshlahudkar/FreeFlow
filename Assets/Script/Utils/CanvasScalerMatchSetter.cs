using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasScaler))]
public class CanvasScalerMatchSetter : MonoBehaviour
{
    [Tooltip("Aspect ratio (width / height) of the CanvasScaler's Reference Resolution, e.g. 1080/1920.")]
    [SerializeField] private float referenceAspect = 1080f / 1920f;

    [Tooltip("Aspect ratio of the tallest/narrowest screen expected (e.g. 9/19.5). Match = 0 (match width) at or beyond this.")]
    [SerializeField] private float tallAspect = 9f / 19.5f;

    [Tooltip("Aspect ratio of the widest/squarest screen expected (e.g. 3/4). Match = 1 (match height) at or beyond this.")]
    [SerializeField] private float wideAspect = 3f / 4f;

    private void Awake()
    {
        CanvasScaler scaler = GetComponent<CanvasScaler>();
        float currentAspect = (float)Screen.width / Screen.height;

        float match = currentAspect <= referenceAspect
            ? Mathf.Lerp(0f, 0.5f, Mathf.InverseLerp(tallAspect, referenceAspect, currentAspect))
            : Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(referenceAspect, wideAspect, currentAspect));

        scaler.matchWidthOrHeight = match;
    }
}
