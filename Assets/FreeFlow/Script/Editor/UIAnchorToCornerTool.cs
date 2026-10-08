using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

public static class UIAnchorToCornerTool
{
    private const string MenuPath = "Tools/UI/Anchor To Nearest Corner";

    [MenuItem(MenuPath)]
    [Shortcut(MenuPath, KeyCode.BackQuote, ShortcutModifiers.Action)]
    private static void AnchorToNearestCorner()
    {
        Undo.SetCurrentGroupName("Anchor UI To Corner");
        int undoGroup = Undo.GetCurrentGroup();

        foreach (GameObject go in Selection.gameObjects)
        {
            RectTransform rt = go.transform as RectTransform;
            RectTransform parent = rt != null ? rt.parent as RectTransform : null;
            if (rt == null || parent == null)
                continue;

            SnapToNearestCorner(rt, parent);
        }

        Undo.CollapseUndoOperations(undoGroup);
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateAnchorToNearestCorner()
    {
        foreach (GameObject go in Selection.gameObjects)
        {
            RectTransform rt = go.transform as RectTransform;
            if (rt != null && rt.parent as RectTransform != null)
                return true;
        }
        return false;
    }

    // Sets anchorMin/anchorMax to the element's own current corners (as a fraction of the
    // parent rect) and zeroes the offsets, so the rect doesn't move but is now anchored
    // exactly to its own bounds instead of a fixed pixel offset.
    private static void SnapToNearestCorner(RectTransform rt, RectTransform parent)
    {
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners); // 0 = bottom-left, 2 = top-right

        Vector2 localBL = parent.InverseTransformPoint(corners[0]);
        Vector2 localTR = parent.InverseTransformPoint(corners[2]);
        Rect parentRect = parent.rect;

        Vector2 anchorMin = new Vector2(
            Mathf.InverseLerp(parentRect.xMin, parentRect.xMax, localBL.x),
            Mathf.InverseLerp(parentRect.yMin, parentRect.yMax, localBL.y));
        Vector2 anchorMax = new Vector2(
            Mathf.InverseLerp(parentRect.xMin, parentRect.xMax, localTR.x),
            Mathf.InverseLerp(parentRect.yMin, parentRect.yMax, localTR.y));

        Undo.RecordObject(rt, "Anchor UI To Corner");

        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}

public static class UIAnchorToStretchTool
{
    private const string MenuPath = "Tools/UI/Anchor To Stretch (Own Rect)";

    [MenuItem(MenuPath)]
    [Shortcut(MenuPath, KeyCode.BackQuote, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
    private static void AnchorToStretch()
    {
        Undo.SetCurrentGroupName("Anchor UI To Stretch");
        int undoGroup = Undo.GetCurrentGroup();

        foreach (GameObject go in Selection.gameObjects)
        {
            RectTransform rt = go.transform as RectTransform;
            RectTransform parent = rt != null ? rt.parent as RectTransform : null;
            if (rt == null || parent == null)
                continue;

            StretchToOwnRect(rt, parent);
        }

        Undo.CollapseUndoOperations(undoGroup);
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateAnchorToStretch()
    {
        foreach (GameObject go in Selection.gameObjects)
        {
            RectTransform rt = go.transform as RectTransform;
            if (rt != null && rt.parent as RectTransform != null)
                return true;
        }
        return false;
    }

    private static void StretchToOwnRect(RectTransform rt, RectTransform parent)
    {
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners); // 0 = bottom-left, 2 = top-right

        Vector2 localBL = parent.InverseTransformPoint(corners[0]);
        Vector2 localTR = parent.InverseTransformPoint(corners[2]);
        Rect parentRect = parent.rect;

        float left = localBL.x - parentRect.xMin;
        float bottom = localBL.y - parentRect.yMin;
        float right = parentRect.xMax - localTR.x;
        float top = parentRect.yMax - localTR.y;

        Undo.RecordObject(rt, "Anchor UI To Stretch");

        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }
}
