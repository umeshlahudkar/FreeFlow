using System.Collections.Generic;
using UnityEngine;
using FreeFlow.Enums;
using FreeFlow.Util;

namespace FreeFlow.UI
{
    /// <summary>
    /// Single owner of screen navigation. Every page/overlay is a <see cref="Page"/> wired into
    /// <see cref="pages"/> in the Inspector; PageManager is the only thing allowed to Open/Close
    /// one -- callers go through OpenPage/ClosePage/OpenAsOverlay/CloseOverlay rather than
    /// touching a page's GameObject directly.
    ///
    /// <see cref="pageStack"/> drives Back-button navigation and holds ONLY full pages (MainMenu,
    /// PackSelect, Levels, DailyChallenge, Gameplay). Overlays (Pause, Setting, LevelComplete,
    /// Warning) are opened on top of whatever page is current and closed by their own explicit
    /// action -- they never go on the stack, per the user's own explicit call.
    ///
    /// It is also the one place a page reference lives. Anything needing to talk to a page asks
    /// <see cref="Get{T}"/> for it rather than serializing its own field -- see that method.
    /// </summary>
    public class PageManager : Singleton<PageManager>
    {
        [SerializeField] private Page[] pages;
        [SerializeField] private PageType startingPage = PageType.MainMenu;

        private readonly Dictionary<PageType, Page> pageMap = new Dictionary<PageType, Page>();
        private readonly Stack<Page> pageStack = new Stack<Page>();

        public PageType CurrentPage { get { return pageStack.Peek().PageType; } }

        private void Awake()
        {
            foreach (Page page in pages)
            {
                pageMap[page.PageType] = page;
            }

            // The starting page is already the one left active in the scene -- seed the stack
            // without re-opening (re-activating) it.
            pageStack.Push(pageMap[startingPage]);
        }

        /// <summary>
        /// Navigates to <paramref name="type"/>. If it is already the current page, it is simply
        /// cycled (Close then Open) rather than pushed again -- what Retry needs. If it is
        /// already further down the stack, everything above it is popped and closed instead of
        /// stacking a second copy -- what a Home button jumping back several pages needs, so a
        /// later Back from the root can never re-enter a page that was already left behind.
        /// Otherwise the current top is closed and <paramref name="type"/> is pushed and opened.
        /// </summary>
        public void OpenPage(PageType type)
        {
            if (!pageMap.TryGetValue(type, out Page target))
            {
                Debug.LogError("PageManager: no page registered for " + type);
                return;
            }

            if (pageStack.Count > 0 && pageStack.Peek() == target)
            {
                target.Close();
                target.Open();
                return;
            }

            if (pageStack.Contains(target))
            {
                while (pageStack.Peek() != target)
                {
                    pageStack.Pop().Close();
                }
                target.Open();
                return;
            }

            if (pageStack.Count > 0) { pageStack.Peek().Close(); }
            pageStack.Push(target);
            target.Open();
        }

        /// <summary>Generic Back action: closes the current page and reopens whatever is beneath
        /// it. No-ops on the root page (nothing left to go back to).</summary>
        public void ClosePage()
        {
            if (pageStack.Count <= 1) { return; }
            pageStack.Pop().Close();
            pageStack.Peek().Open();
        }

        /// <summary>
        /// The page registered for <paramref name="type"/>, typed. This is how anything that needs
        /// to TALK to a page -- fill in the level-complete sheet, put a message on the warning
        /// notifier, refresh the gameplay HUD -- reaches it.
        ///
        /// Nothing outside this class should serialize a second reference to a page: the pages
        /// array here is the registry, and a duplicate reference in another component is one that
        /// can be left pointing at a stale or missing object while this one still works (or the
        /// reverse), with nothing to say the two disagree.
        ///
        /// Returns null and complains rather than throwing, so a mis-wired Inspector surfaces as a
        /// named error instead of a NullReferenceException three frames later.
        /// </summary>
        public T Get<T>(PageType type) where T : Page
        {
            if (!pageMap.TryGetValue(type, out Page page))
            {
                Debug.LogError("PageManager: no page registered for " + type);
                return null;
            }

            T typed = page as T;
            if (typed == null)
            {
                Debug.LogError("PageManager: " + type + " is a " + page.GetType().Name
                    + ", not a " + typeof(T).Name + ".");
            }
            return typed;
        }

        /// <summary>Opens an overlay (Pause/Setting/LevelComplete/Warning) on top of the current
        /// page without touching the back-stack.</summary>
        public void OpenAsOverlay(PageType type)
        {
            if (pageMap.TryGetValue(type, out Page overlay)) { overlay.Open(); }
            else { Debug.LogError("PageManager: no page registered for " + type); }
        }

        /// <summary>Closes an overlay opened via <see cref="OpenAsOverlay"/>. Safe to call even if
        /// it is already closed.</summary>
        public void CloseOverlay(PageType type)
        {
            if (pageMap.TryGetValue(type, out Page overlay)) { overlay.Close(); }
        }
    }
}
