using System.Collections.Generic;
using UnityEngine;

namespace FreeFlow.Util
{
    /// <summary>
    /// Hides items on screens that are laid out by hand -- fixed positions, no layout group -- and
    /// moves what is left so no gap shows where they were.
    ///
    /// Only ever called when something is actually hidden (a service the build does not have), so
    /// a build that shows everything keeps exactly the positions authored in the scene.
    /// </summary>
    public static class ManualLayout
    {
        /// <summary>
        /// Hides <paramref name="hidden"/> -- children of <paramref name="card"/>, a vertical stack
        /// of top-anchored, top-pivoted items -- and closes the gaps. Each hidden item gives up its
        /// slot: the distance from its top to the top of the item below it. Everything below moves
        /// up by the slots given up so far, the card shrinks by their total, and every top-anchored
        /// sibling of the card that sits below it moves up by the same total.
        /// </summary>
        public static void CollapseStack(RectTransform card, ICollection<RectTransform> hidden)
        {
            if (card == null || hidden == null || hidden.Count == 0) { return; }

            List<RectTransform> items = new List<RectTransform>();
            foreach (Transform child in card) { items.Add((RectTransform)child); }

            // Top first: positions are measured down from the card's top edge, so higher is larger.
            items.Sort((a, b) => b.anchoredPosition.y.CompareTo(a.anchoredPosition.y));

            float removed = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                RectTransform item = items[i];
                if (hidden.Contains(item))
                {
                    float slot = i + 1 < items.Count
                        ? item.anchoredPosition.y - items[i + 1].anchoredPosition.y
                        : item.rect.height;
                    item.gameObject.SetActive(false);
                    removed += slot;
                }
                else if (removed > 0f)
                {
                    item.anchoredPosition += new Vector2(0f, removed);
                }
            }

            if (removed <= 0f) { return; }

            card.sizeDelta -= new Vector2(0f, removed);

            RectTransform parent = card.parent as RectTransform;
            if (parent == null) { return; }

            float cardTop = card.anchoredPosition.y;
            foreach (Transform child in parent)
            {
                RectTransform sibling = (RectTransform)child;
                bool topAnchored = Mathf.Approximately(sibling.anchorMin.y, 1f) && Mathf.Approximately(sibling.anchorMax.y, 1f);
                if (sibling != card && topAnchored && sibling.anchoredPosition.y < cardTop)
                {
                    sibling.anchoredPosition += new Vector2(0f, removed);
                }
            }
        }

        /// <summary>
        /// Hides <paramref name="hidden"/> from <paramref name="row"/> -- items side by side, all
        /// sharing the same anchors -- and spreads the rest evenly across the width the whole row
        /// used to span, keeping the same gap between them.
        /// </summary>
        public static void CollapseRow(IList<RectTransform> row, RectTransform hidden)
        {
            if (row == null || hidden == null || !row.Contains(hidden)) { return; }

            List<RectTransform> items = new List<RectTransform>(row);
            items.Sort((a, b) => a.anchoredPosition.x.CompareTo(b.anchoredPosition.x));

            float left = float.MaxValue;
            float right = float.MinValue;
            float totalWidth = 0f;
            foreach (RectTransform item in items)
            {
                float width = item.sizeDelta.x;
                left = Mathf.Min(left, item.anchoredPosition.x - width * item.pivot.x);
                right = Mathf.Max(right, item.anchoredPosition.x + width * (1f - item.pivot.x));
                totalWidth += width;
            }
            float gap = items.Count > 1 ? (right - left - totalWidth) / (items.Count - 1) : 0f;

            hidden.gameObject.SetActive(false);
            items.Remove(hidden);
            if (items.Count == 0) { return; }

            float newWidth = (right - left - gap * (items.Count - 1)) / items.Count;
            float x = left;
            foreach (RectTransform item in items)
            {
                item.sizeDelta = new Vector2(newWidth, item.sizeDelta.y);
                item.anchoredPosition = new Vector2(x + newWidth * item.pivot.x, item.anchoredPosition.y);
                x += newWidth + gap;
            }
        }
    }
}
