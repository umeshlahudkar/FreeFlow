using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FreeFlow.UI
{
    /// <summary>
    /// The strip at the top of DailyChallengePage: a date tile in the day's own month colour, two
    /// lines saying what that day is and what it holds, and a pill on the right naming its state.
    ///
    /// It replaced a 380-tall hero card whose largest element was a ring showing how many days of
    /// the month were solved -- which on a fresh month read "0 OF 31" in the biggest type on the
    /// screen, and which repeated a date the header, the calendar and the Play button were already
    /// each stating. The count moved to a progress bar inside the calendar card, where it sits
    /// under the month it counts; the date is now said once here.
    ///
    /// Purely a display shell -- DailyChallengePage decides which day is chosen and hands over the
    /// strings and the state in one <see cref="SetDay"/> call. Every property is set on every call,
    /// since the same strip is reused for whichever day the player taps.
    /// </summary>
    public class DailyDayCard : MonoBehaviour
    {
        /// <summary>What the chosen day is, which is the one thing that changes every colour on
        /// this strip. Not a general "is it done" flag: <see cref="Missed"/> and <see cref="Today"/>
        /// are both unsolved and both playable, and they are told apart only so the pill can say
        /// which of the two it is.</summary>
        public enum DayState { Today, Solved, Missed }

        [Header("Card")]
        [SerializeField] private Image cardImage;
        // Soft shadow under the strip. No longer tinted from the month colour (the strip itself is
        // white now), so it is set once in the scene and never touched here.
        [SerializeField] private Image cardShadow;

        [Header("Date tile")]
        [SerializeField] private Image dateTileImage;
        [SerializeField] private TextMeshProUGUI dayNameText;
        [SerializeField] private TextMeshProUGUI dayNumText;

        [Header("Text")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Pill")]
        [SerializeField] private Image pillImage;
        [SerializeField] private TextMeshProUGUI pillText;

        // One pair per state. Authored rather than derived from the month colour: a pill that
        // tinted itself would say "this is October" a second time, when the only thing it is for
        // is saying whether the day is done.
        [SerializeField] private Color newPillColour = new Color32(255, 237, 225, 255);
        [SerializeField] private Color newPillTextColour = new Color32(140, 52, 6, 255);
        [SerializeField] private Color solvedPillColour = new Color32(225, 245, 238, 255);
        [SerializeField] private Color solvedPillTextColour = new Color32(11, 107, 82, 255);
        [SerializeField] private Color missedPillColour = new Color32(250, 238, 218, 255);
        [SerializeField] private Color missedPillTextColour = new Color32(120, 70, 7, 255);

        // What the date tile turns when its day is done. The month colour is identity -- which
        // month this day belongs to -- and this is achievement, the same teal the solved cells in
        // the calendar below are drawn in, so a solved day reads the same in both places.
        [SerializeField] private Color solvedTileColour = new Color32(15, 157, 135, 255);

        /// <param name="dayName">Short weekday, e.g. "SUN".</param>
        /// <param name="dayNumber">Day of the month, e.g. "25".</param>
        /// <param name="title">What this day IS -- "TODAY'S PUZZLE", "CATCH-UP PUZZLE".</param>
        /// <param name="status">What it holds and where it stands, one line.</param>
        /// <param name="pill">The state in a word -- "NEW", "SOLVED", "MISSED".</param>
        /// <param name="monthColour">The chosen day's month colour, used on the tile unless the
        /// day is solved.</param>
        public void SetDay(string dayName, string dayNumber, string title, string status,
            string pill, DayState state, Color monthColour)
        {
            if (cardImage != null) { cardImage.color = Color.white; }

            if (dateTileImage != null)
            {
                dateTileImage.color = state == DayState.Solved ? solvedTileColour : monthColour;
            }
            if (dayNameText != null) { dayNameText.text = dayName; }
            if (dayNumText != null) { dayNumText.text = dayNumber; }

            if (titleText != null) { titleText.text = title; }
            if (statusText != null) { statusText.text = status; }

            if (pillText != null)
            {
                pillText.text = pill;
                pillText.color = state == DayState.Solved ? solvedPillTextColour
                    : state == DayState.Missed ? missedPillTextColour
                    : newPillTextColour;
            }
            if (pillImage != null)
            {
                pillImage.color = state == DayState.Solved ? solvedPillColour
                    : state == DayState.Missed ? missedPillColour
                    : newPillColour;
            }
        }
    }
}
