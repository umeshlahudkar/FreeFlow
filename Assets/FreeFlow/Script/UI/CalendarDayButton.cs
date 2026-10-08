using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FreeFlow.Input;
using FreeFlow.Enums;

namespace FreeFlow.UI
{
    /// <summary>
    /// One day-cell tile in DailyChallengePage's month calendar. Deliberately its own component,
    /// not a reuse of LevelButton (the pack-select Levels screen's tile) -- the two are shown in
    /// different contexts (a calendar day vs. a pack level number) and styling one should never
    /// risk changing the other.
    ///
    /// Purely a display shell with no notion of "state" of its own -- DailyChallengePage owns
    /// that (done/open/selected/locked), decides which of its own daySolvedSprite/dayTodaySprite
    /// fields (or no override at all) represents it, and just hands this cell the resolved sprite,
    /// whether it's completed, and whether it should be tappable.
    /// </summary>
    public class CalendarDayButton : MonoBehaviour
    {
        [SerializeField] private RectTransform thisTransform;
        [SerializeField] private TextMeshProUGUI dayNumberText;
        [SerializeField] private Image buttonImg;
        [SerializeField] private Button button;

        // Lets DailyChallengePage substitute what a tap does -- set fresh every time a cell is
        // (re)assigned a date, since which date a given grid slot represents changes as the player
        // pages between months. Deliberately NOT a second CanInput() gate of its own -- see
        // OnButtonClick, which already gates once before invoking this (same reasoning as
        // LevelButton's own onClickOverride field).
        private System.Action onClickOverride;

        // The text colour this cell was originally authored with, captured once before SetDetails
        // ever touches it. The same 35 cell instances are reused for every month the player pages
        // to, so SetDetails must actively restore this baseline whenever a state doesn't call for
        // an override -- leaving it untouched only "works" for a cell that has never shown that
        // override before; any cell that HAD would keep showing it forever after, on every later
        // month reusing that grid slot. The button's background never gets an equivalent RGB
        // baseline -- only its alpha is ever touched (always hard-set to 0 or 1, never restored
        // from some earlier authored value), so there's nothing to capture for it.
        private Color authoredDayNumberColor;
        private bool authoredColorCaptured;

        [SerializeField] private Color lockedTextColor = new Color(0.65f, 0.65f, 0.65f, 1f);

        public RectTransform ThisTransform { get { return thisTransform; } }

        public void SetClickOverride(System.Action action)
        {
            onClickOverride = action;
        }

        /// <summary>Sets this cell's day number, background sprite, whether it can be tapped, and
        /// text colour -- completed is white, locked (non-interactable) is <see cref="lockedTextColor"/>,
        /// anything else (available or the currently selected day) is left at whatever colour was
        /// originally authored on this cell. The button's background never gets a colour (RGB) of
        /// its own, only its ALPHA is toggled to show/hide <paramref name="sprite"/> -- it stays
        /// `enabled` at all times, because this same Image IS the Button's own targetGraphic and its
        /// only raycastable Graphic; disabling it (as an earlier version of this method did) also
        /// stops the whole cell from receiving clicks at all, which is what silently broke tapping
        /// available days. <paramref name="sprite"/> null means an available/locked day: alpha 0, no
        /// background painted, just the bare number, but still fully clickable underneath. Every
        /// property is resolved explicitly on every call, never conditionally skipped, since the
        /// same 35 cell instances are reused for a different date each time the player pages to a
        /// different month.</summary>
        public void SetDetails(int dayNumber, Sprite sprite, bool interactable, bool completed)
        {
            if (dayNumberText != null)
            {
                if (!authoredColorCaptured) { authoredDayNumberColor = dayNumberText.color; authoredColorCaptured = true; }

                dayNumberText.text = dayNumber.ToString();
                dayNumberText.color = completed ? Color.white : interactable ? authoredDayNumberColor : lockedTextColor;
            }

            if (buttonImg != null)
            {
                buttonImg.enabled = true;
                if (sprite != null) { buttonImg.sprite = sprite; }
                Color bgColor = buttonImg.color;
                bgColor.a = sprite != null ? 1f : 0f;
                buttonImg.color = bgColor;
            }

            if (button != null)
            {
                button.interactable = interactable;
                // Selectable's built-in colour-tint transition auto-fades its targetGraphic
                // whenever interactable is false -- since that IS buttonImg here, a merely
                // non-selectable completed day (which still has a sprite showing) would get
                // dimmed for no reason. All visual state is already handled explicitly above, so
                // the transition itself must do nothing.
                button.transition = Selectable.Transition.None;
            }
        }

        /// <summary>Renders this tile fully blank and non-interactable WITHOUT deactivating its
        /// GameObject -- for the calendar's leading/trailing cells (the days before the 1st, or
        /// after the month's last day), which still have to occupy a GRID SLOT so every later cell
        /// lands in the right column. A GridLayoutGroup skips inactive children entirely when
        /// positioning cells, so deactivating a "blank" cell collapses the whole grid by one slot
        /// instead of leaving a gap for it (see LevelButton.SetBlank, where this was first found).
        /// Turns the Image's rendering off directly rather than touching its colour, so this needs
        /// no colour fields of its own at all.</summary>
        public void SetBlank()
        {
            if (dayNumberText != null) { dayNumberText.text = ""; }
            if (buttonImg != null) { buttonImg.enabled = false; }
            if (button != null) { button.interactable = false; }
        }

        /// <summary>Wired to the Button's own onClick -- see the build script that constructs each
        /// cell for where that wiring happens.</summary>
        public void OnButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
                if (onClickOverride != null) { onClickOverride(); }
            }
        }
    }
}
