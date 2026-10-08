using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Blends the CanvasScaler's match between "match width" and "match height" according to how the
/// current screen's aspect compares to the reference resolution's, so a tall phone keeps the
/// designed width and a squat tablet keeps the designed height instead of one of them being
/// cropped or padded.
///
/// Re-evaluated whenever the canvas changes size, not just once at Awake. The aspect is not fixed
/// for the life of the process -- a foldable unfolding or an Android app resized into split-screen
/// both change it -- and an Awake-only match left the canvas scaled for the shape the screen had
/// at startup.
/// </summary>
[RequireComponent(typeof(CanvasScaler))]
public class CanvasScalerMatchSetter : MonoBehaviour
{
    [Tooltip("Aspect ratio (width / height) of the CanvasScaler's Reference Resolution, e.g. 1080/1920.")]
    [SerializeField] private float referenceAspect = 1080f / 1920f;

    [Tooltip("Aspect ratio of the tallest/narrowest screen expected (e.g. 9/19.5). Match = 0 (match width) at or beyond this.")]
    [SerializeField] private float tallAspect = 9f / 19.5f;

    [Tooltip("Aspect ratio of the widest/squarest screen expected (e.g. 3/4). Match = 1 (match height) at or beyond this.")]
    [SerializeField] private float wideAspect = 3f / 4f;

    private CanvasScaler scaler;

    private void Awake()
    {
        scaler = GetComponent<CanvasScaler>();
        Apply();
    }

    // Fired when the canvas is resized, which is exactly when the aspect can have changed.
    private void OnRectTransformDimensionsChange()
    {
        Apply();
    }

    private void Apply()
    {
        if (scaler == null) { scaler = GetComponent<CanvasScaler>(); }
        if (scaler == null || Screen.height <= 0) { return; }

        float currentAspect = (float)Screen.width / Screen.height;

        float match = currentAspect <= referenceAspect
            ? Mathf.Lerp(0f, 0.5f, Mathf.InverseLerp(tallAspect, referenceAspect, currentAspect))
            : Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(referenceAspect, wideAspect, currentAspect));

        // Writing the match resizes the canvas, which fires OnRectTransformDimensionsChange again;
        // without this guard that is an endless rebuild loop rather than a one-off correction.
        if (Mathf.Abs(scaler.matchWidthOrHeight - match) > 0.0001f)
        {
            scaler.matchWidthOrHeight = match;
        }
    }
}
