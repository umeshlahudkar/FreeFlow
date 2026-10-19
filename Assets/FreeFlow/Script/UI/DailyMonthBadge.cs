using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FreeFlow.UI
{
    /// <summary>
    /// The big month dot at the top of DailyChallengePage: a dot in the month's own colour, a ring
    /// around it that fills by days solved / days in the month, the solved count inside the dot,
    /// and a ribbon with the month's name underneath.
    ///
    /// Purely a display shell -- DailyChallengePage decides which month is shown, its colour and
    /// how many of its days are solved, and hands all of that over in one <see cref="SetMonth"/>
    /// call. Every property is set explicitly on every call, since the same badge is reused for
    /// whichever month the player pages to.
    /// </summary>
    public class DailyMonthBadge : MonoBehaviour
    {
        [Header("Dot and ring")]
        // Image.Type = Filled, Radial360 from the top -- fillAmount is the month's solved fraction.
        [SerializeField] private Image ringFill;
        [SerializeField] private Image dot;
        [SerializeField] private TextMeshProUGUI countText;
        [SerializeField] private TextMeshProUGUI countSubText;
        // Shown in place of the count once every day of the month is solved.
        [SerializeField] private Image completeCheck;

        [Header("Ribbon")]
        [SerializeField] private Image ribbonBand;
        [SerializeField] private Image ribbonTails;
        [SerializeField] private TextMeshProUGUI ribbonText;

        [Header("Lines under the ribbon")]
        [SerializeField] private TextMeshProUGUI summaryText;
        [SerializeField] private TextMeshProUGUI hintText;

        // How much darker the ribbon's tails are than its band, so the ribbon reads as folded.
        [SerializeField] private float ribbonTailShade = 0.75f;

        /// <param name="monthName">Shown on the ribbon, e.g. "OCTOBER".</param>
        /// <param name="colour">The month's own colour, used for the dot, ring and ribbon.</param>
        /// <param name="textColour">Readable on <paramref name="colour"/> -- white for most months,
        /// dark for the light ones.</param>
        public void SetMonth(string monthName, Color colour, Color textColour, int solved, int daysInMonth)
        {
            bool complete = daysInMonth > 0 && solved >= daysInMonth;

            if (ringFill != null)
            {
                ringFill.color = colour;
                ringFill.fillAmount = daysInMonth > 0 ? Mathf.Clamp01((float)solved / daysInMonth) : 0f;
            }
            if (dot != null) { dot.color = colour; }

            if (countText != null)
            {
                countText.gameObject.SetActive(!complete);
                countText.text = solved.ToString();
                countText.color = textColour;
            }
            if (countSubText != null)
            {
                countSubText.gameObject.SetActive(!complete);
                countSubText.text = "OF " + daysInMonth + " DAYS";
                countSubText.color = textColour;
            }
            if (completeCheck != null)
            {
                completeCheck.gameObject.SetActive(complete);
                completeCheck.color = textColour;
            }

            if (ribbonBand != null) { ribbonBand.color = colour; }
            if (ribbonTails != null)
            {
                ribbonTails.color = new Color(colour.r * ribbonTailShade, colour.g * ribbonTailShade, colour.b * ribbonTailShade, colour.a);
            }
            if (ribbonText != null)
            {
                ribbonText.text = monthName;
                ribbonText.color = textColour;
            }

            if (summaryText != null)
            {
                summaryText.text = complete
                    ? "ALL " + daysInMonth + " DAYS SOLVED"
                    : solved + " OF " + daysInMonth + " DAYS SOLVED";
            }
            if (hintText != null)
            {
                hintText.text = complete ? "MONTH COMPLETE" : "SOLVE EVERY DAY TO FILL THE RING";
            }
        }
    }
}
