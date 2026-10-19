using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FreeFlow.Input;
using FreeFlow.Enums;

namespace FreeFlow.UI
{
    /// <summary>
    /// One month on DailyChallengePage's shelf of recent months: a small ring that fills by that
    /// month's solved fraction around a dot in the month's colour, a check once every day is
    /// solved, and the month's short name. Tapping it shows that month in the calendar.
    ///
    /// Same shape as CalendarDayButton: a display shell whose tap action is supplied by the page
    /// (<see cref="SetClickOverride"/>), and every property is set explicitly on every
    /// <see cref="SetDetails"/> call because the same six chips are reused as the months move on.
    /// </summary>
    public class DailyMonthChip : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private Image background;
        [SerializeField] private Sprite normalBackground;
        [SerializeField] private Sprite selectedBackground;

        // Image.Type = Filled, Radial360 from the top -- fillAmount is the month's solved fraction.
        [SerializeField] private Image ringFill;
        [SerializeField] private Image dot;
        [SerializeField] private Image check;
        [SerializeField] private TextMeshProUGUI label;

        [SerializeField] private Color labelColour = new Color(0.42f, 0.5f, 0.57f, 1f);
        [SerializeField] private Color selectedLabelColour = new Color(0.06f, 0.62f, 0.53f, 1f);

        // Deliberately not a second CanInput() gate -- OnButtonClick already gates once before
        // invoking this, and gating twice in one tap always fails the second check.
        private System.Action onClickOverride;

        public void SetClickOverride(System.Action action)
        {
            onClickOverride = action;
        }

        public void SetDetails(string monthLabel, Color colour, Color textColour, int solved, int daysInMonth, bool selected)
        {
            bool complete = daysInMonth > 0 && solved >= daysInMonth;

            if (background != null)
            {
                Sprite sprite = selected ? selectedBackground : normalBackground;
                if (sprite != null) { background.sprite = sprite; }
            }
            if (ringFill != null)
            {
                ringFill.color = colour;
                ringFill.fillAmount = daysInMonth > 0 ? Mathf.Clamp01((float)solved / daysInMonth) : 0f;
            }
            if (dot != null) { dot.color = colour; }
            if (check != null)
            {
                check.gameObject.SetActive(complete);
                check.color = textColour;
            }
            if (label != null)
            {
                label.text = monthLabel;
                label.color = selected ? selectedLabelColour : labelColour;
            }
        }

        /// <summary>Wired to the Button's own onClick in the scene.</summary>
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
