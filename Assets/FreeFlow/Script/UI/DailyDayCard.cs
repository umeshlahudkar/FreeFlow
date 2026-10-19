using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FreeFlow.UI
{
    /// <summary>
    /// The card at the top of DailyChallengePage: the chosen day's date, whether it is today's
    /// puzzle or a catch-up, its board size, and on the right a ring filled by how many days of
    /// that month are solved. Tinted with the month's colour, in the same style as the main menu's
    /// Daily card.
    ///
    /// Purely a display shell -- DailyChallengePage decides which day is chosen and hands over the
    /// strings and colours in one <see cref="SetDay"/> call. Every property is set on every call,
    /// since the same card is reused for whichever day the player taps.
    /// </summary>
    public class DailyDayCard : MonoBehaviour
    {
        [Header("Card")]
        [SerializeField] private Image cardImage;
        // Coloured shadow under the card: a darker shade of the month colour at this alpha.
        [SerializeField] private Image cardShadow;
        [SerializeField] private float shadowShade = 0.55f;
        [SerializeField] private float shadowAlpha = 0.45f;

        [Header("Text")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI pillText;
        [SerializeField] private TextMeshProUGUI boardText;
        [SerializeField] private Color solvedPillTextColour = new Color32(11, 122, 105, 255);

        [Header("Month ring")]
        // Image.Type = Filled, Radial360 from the top -- fillAmount is the month's solved fraction.
        [SerializeField] private Image ringFill;
        [SerializeField] private TextMeshProUGUI countText;
        [SerializeField] private TextMeshProUGUI countSubText;

        /// <param name="colour">The chosen day's month colour.</param>
        /// <param name="textColour">Readable on <paramref name="colour"/> -- white for most months,
        /// dark for the light ones.</param>
        /// <param name="lightColour">Whether <paramref name="colour"/> is one of the light months,
        /// which need darker shades for the pill and ring numbers to read on white.</param>
        public void SetDay(string title, string status, string pill, bool solved, string board,
            Color colour, Color textColour, bool lightColour, int solvedInMonth, int daysInMonth)
        {
            if (cardImage != null) { cardImage.color = colour; }
            if (cardShadow != null)
            {
                Color shadow = DailyMonthPalette.Shade(colour, shadowShade);
                shadow.a = shadowAlpha;
                cardShadow.color = shadow;
            }

            if (titleText != null) { titleText.text = title; titleText.color = textColour; }
            if (statusText != null) { statusText.text = status; statusText.color = textColour; }
            if (boardText != null) { boardText.text = board; boardText.color = textColour; }
            if (pillText != null)
            {
                pillText.text = pill;
                pillText.color = solved ? solvedPillTextColour : DailyMonthPalette.Shade(colour, lightColour ? 0.55f : 0.8f);
            }

            if (ringFill != null) { ringFill.fillAmount = daysInMonth > 0 ? Mathf.Clamp01((float)solvedInMonth / daysInMonth) : 0f; }
            if (countText != null)
            {
                countText.text = solvedInMonth.ToString();
                countText.color = DailyMonthPalette.Shade(colour, lightColour ? 0.6f : 0.9f);
            }
            if (countSubText != null)
            {
                countSubText.text = "OF " + daysInMonth + " DAYS";
                countSubText.color = DailyMonthPalette.Shade(colour, lightColour ? 0.45f : 0.7f);
            }
        }
    }
}
