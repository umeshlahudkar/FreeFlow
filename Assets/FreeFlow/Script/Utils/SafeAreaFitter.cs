using UnityEngine;

/// <summary>
/// Keeps this RectTransform inside the screen's safe area -- the region not covered by a notch,
/// punch-hole or home indicator.
///
/// Re-applied whenever the safe area or the screen changes, not just once at Start. The safe area
/// is not fixed for the life of the process: it changes when the device rotates, when a foldable
/// unfolds, and when an Android app is resized into split-screen or a free-form window. Applying
/// it only at Start left the insets describing whatever shape the screen had when this object
/// first woke up.
///
/// This cannot be caught in the Editor: Screen.safeArea there is always the full Game view rect,
/// so the inset is always zero and a stale one looks identical to a correct one. It only shows up
/// on a real device.
/// </summary>
public class SafeAreaFitter : MonoBehaviour
{
    private RectTransform rectTransform;
    private Rect lastSafeArea;
    private ScreenOrientation lastOrientation;
    private Vector2Int lastScreenSize;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        // Forces the first Apply rather than trusting the zero-initialised fields to differ --
        // a page that is enabled, disabled and enabled again must re-measure, since the screen
        // may well have changed while it was off.
        lastSafeArea = new Rect(-1f, -1f, -1f, -1f);
        Apply();
    }

    private void Update()
    {
        if (Screen.safeArea != lastSafeArea
            || Screen.orientation != lastOrientation
            || Screen.width != lastScreenSize.x
            || Screen.height != lastScreenSize.y)
        {
            Apply();
        }
    }

    private void Apply()
    {
        if (rectTransform == null) { rectTransform = GetComponent<RectTransform>(); }

        int screenWidth = Screen.width;
        int screenHeight = Screen.height;
        if (screenWidth <= 0 || screenHeight <= 0) { return; }

        Rect safeArea = Screen.safeArea;

        // Remember the RAW reading, so that when a lagging safeArea finally catches up it counts
        // as a change and the correct insets get applied.
        lastSafeArea = safeArea;
        lastOrientation = Screen.orientation;
        lastScreenSize = new Vector2Int(screenWidth, screenHeight);

        // Screen.safeArea can be measured against the PREVIOUS screen size for a frame after a
        // resolution change (in the Editor Game view it can stay stale indefinitely). Dividing
        // that by the current size yields anchors outside 0..1 -- a 1536-wide safe area over a
        // 1080-wide screen gives anchorMax.x = 1.42, which stretches the whole page 42% past the
        // right edge. An inconsistent rect means "no reliable inset", so fall back to the full
        // screen rather than trusting it.
        if (safeArea.width <= 0f || safeArea.height <= 0f
            || safeArea.xMin < -0.5f || safeArea.yMin < -0.5f
            || safeArea.xMax > screenWidth + 0.5f || safeArea.yMax > screenHeight + 0.5f)
        {
            safeArea = new Rect(0f, 0f, screenWidth, screenHeight);
        }

        Vector2 minAnchor = new Vector2(safeArea.xMin / screenWidth, safeArea.yMin / screenHeight);
        Vector2 maxAnchor = new Vector2(safeArea.xMax / screenWidth, safeArea.yMax / screenHeight);

        // Belt and braces: anchors outside the canvas are never a valid safe area.
        minAnchor.x = Mathf.Clamp01(minAnchor.x);
        minAnchor.y = Mathf.Clamp01(minAnchor.y);
        maxAnchor.x = Mathf.Clamp01(maxAnchor.x);
        maxAnchor.y = Mathf.Clamp01(maxAnchor.y);

        rectTransform.anchorMin = minAnchor;
        rectTransform.anchorMax = maxAnchor;

        // The anchors alone only describe the safe area if nothing is inset from them; a leftover
        // offset would shift the whole page off the region just computed.
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }
}
