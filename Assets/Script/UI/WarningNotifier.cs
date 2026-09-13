using System.Collections;
using UnityEngine;
using TMPro;
using FreeFlow.Enums;

namespace FreeFlow.UI
{
    /// <summary>A short message explaining why something the player just tapped did not happen --
    /// "No More Hints" being the first of them. Opened with PageManager.OpenAsOverlay, like Pause
    /// and the level-complete sheet, so it is never on the back-stack.
    ///
    /// Unlike those two it carries no button and takes no input at all (its panel and label are
    /// both raycast-transparent): it puts itself away after <see cref="visibleSeconds"/>, and the
    /// board behind it stays playable the whole time. A notice about a tap that did nothing should
    /// not itself demand a tap to get rid of.
    ///
    /// The text is supplied per showing rather than authored once in the scene -- see
    /// UIController.ShowWarning -- so one notifier serves every message.</summary>
    public class WarningNotifier : Page
    {
        [SerializeField] private TextMeshProUGUI messageText;

        // Long enough to read a few words at a glance, short enough that it is gone before the
        // player has finished deciding what to do instead.
        [SerializeField] private float visibleSeconds = 1.5f;

        // Held so a second warning arriving while this one is still up restarts the countdown
        // rather than inheriting what was left of it -- otherwise a message shown a moment before
        // the old timer expired would flash and vanish.
        private Coroutine hideRoutine;

        /// <summary>The message this notifier shows when it is next opened. Set before opening it,
        /// not instead of opening it: only PageManager puts a page on screen.</summary>
        public void SetMessage(string message)
        {
            if (messageText != null) { messageText.text = message; }
        }

        public override void Open()
        {
            base.Open();

            // After base.Open(), which is what activates the GameObject -- StartCoroutine on an
            // inactive one throws, and silently would have left the notice on screen forever.
            if (hideRoutine != null) { StopCoroutine(hideRoutine); }
            hideRoutine = StartCoroutine(HideAfterDelay());
        }

        public override void Close()
        {
            hideRoutine = null;
            base.Close();
        }

        /// <summary>Closes through PageManager rather than deactivating directly, the same way
        /// PausePage's own Resume button does -- a page must not put itself away behind the
        /// manager's back.</summary>
        private IEnumerator HideAfterDelay()
        {
            // Unscaled: the pause overlay and the level-complete sheet both sit on top of a board
            // that may have stopped, and a notice that freezes with it would sit there until play
            // resumed.
            yield return new WaitForSecondsRealtime(visibleSeconds);

            hideRoutine = null;
            PageManager.Instance.CloseOverlay(PageType.Warning);
        }
    }
}
