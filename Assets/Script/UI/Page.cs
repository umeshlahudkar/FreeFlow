using UnityEngine;
using FreeFlow.Enums;
using DG.Tweening;

namespace FreeFlow.UI
{
    /// <summary>Base for every navigable screen and overlay. PageManager is the only thing that
    /// calls Open/Close -- a page must not activate/deactivate itself or a sibling page directly.</summary>
    public abstract class Page : MonoBehaviour
    {
        [SerializeField] private PageType pageType;

        // One shared fade length for every page rather than per-page tuning -- a screen that faded
        // at a different speed than its neighbour would make navigation feel uneven depending on
        // which one happened to be entered next.
        [SerializeField] private float fadeSeconds = 0.2f;

        // Added lazily rather than required on every page's prefab: most pages never needed one
        // before this, and a runtime CanvasGroup costs nothing a page is not already paying for as
        // a UI root.
        private CanvasGroup fadeGroup;

        public PageType PageType { get { return pageType; } }

        /// <summary>Whether PageManager currently has this page/overlay on screen. Stays true for
        /// the whole fade-OUT, not just the fade-in: Close() only deactivates the GameObject once
        /// its own fade finishes, so anything gating on this -- GamePlayController's mechanic-
        /// overlay input block, for one -- keeps treating a fading-out overlay as still up for as
        /// long as it is still visible and could still catch a stray tap.</summary>
        public bool IsOpen { get { return gameObject.activeInHierarchy; } }

        private CanvasGroup FadeGroup
        {
            get
            {
                if (fadeGroup == null)
                {
                    fadeGroup = GetComponent<CanvasGroup>();
                    if (fadeGroup == null) { fadeGroup = gameObject.AddComponent<CanvasGroup>(); }
                }
                return fadeGroup;
            }
        }

        public virtual void Open()
        {
            gameObject.Activate();

            CanvasGroup group = FadeGroup;

            // Kills whatever this page's own fade-OUT was still mid-flight -- PageManager cycles a
            // page already on top by calling Close() then straight back into Open() (OpenPage's
            // "already current" branch), and without this the earlier fade-out's OnComplete would
            // still be pending and deactivate the very page this call is reopening.
            group.DOKill();
            group.interactable = true;
            group.blocksRaycasts = true;
            group.alpha = 0f;
            group.DOFade(1f, fadeSeconds).SetUpdate(true);
        }

        public virtual void Close()
        {
            CanvasGroup group = FadeGroup;
            group.DOKill();

            // Off immediately, not once the fade finishes: a page on its way out must stop taking
            // taps the moment Close() is called, not stay clickable for the length of its own
            // fade-out.
            group.interactable = false;
            group.blocksRaycasts = false;

            group.DOFade(0f, fadeSeconds).SetUpdate(true).OnComplete(() => gameObject.Deactivate());
        }
    }
}
