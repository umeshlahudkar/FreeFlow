using UnityEngine;

namespace FreeFlow.UI
{
    /// <summary>
    /// One colour per calendar month, shared by DailyChallengePage's month badge and the main
    /// menu's Daily card so the two screens always show the same month in the same colour. The
    /// defaults are the game's own 12 pair colours (PairColorData), so each month's dot looks like
    /// one of the board's dots.
    /// </summary>
    [CreateAssetMenu(fileName = "DailyMonthPalette", menuName = "FreeFlow/Daily Month Palette")]
    public class DailyMonthPalette : ScriptableObject
    {
        // January first.
        [SerializeField] private Color[] monthColours =
        {
            new Color(0.108f, 0.5019f, 0.9426f, 1f),  // Jan, blue
            new Color(1f, 0.5333f, 0.6657f, 1f),      // Feb, pink
            new Color(0.0704f, 0.5689f, 0.0216f, 1f), // Mar, green
            new Color(0.6405f, 0.9973f, 0.097f, 1f),  // Apr, lime
            new Color(1f, 0.7632f, 0.0906f, 1f),      // May, yellow
            new Color(0.1129f, 0.7212f, 0.7257f, 1f), // Jun, cyan
            new Color(0.7509f, 0f, 0.2232f, 1f),      // Jul, red
            new Color(0.7522f, 0.0262f, 1f, 1f),      // Aug, purple
            new Color(0.052f, 0.3842f, 0.3769f, 1f),  // Sep, dark teal
            new Color(0.9984f, 0.3961f, 0.04f, 1f),   // Oct, orange
            new Color(0.4246f, 0.2804f, 0.0238f, 1f), // Nov, brown
            new Color(0.0432f, 0.1229f, 0.5012f, 1f), // Dec, navy
        };

        // A month colour brighter than this (relative luminance) gets darkText on it instead of
        // white -- white on lime or yellow is unreadable.
        [SerializeField] private float lightColourLuminance = 0.7f;
        [SerializeField] private Color darkText = new Color(0.06f, 0.12f, 0.18f, 1f);

        /// <param name="month">1 = January.</param>
        public Color Colour(int month)
        {
            if (monthColours == null || month < 1 || month > monthColours.Length) { return Color.white; }
            return monthColours[month - 1];
        }

        public bool IsLight(Color colour)
        {
            return 0.2126f * colour.r + 0.7152f * colour.g + 0.0722f * colour.b > lightColourLuminance;
        }

        /// <summary>Text colour that reads on <paramref name="colour"/>.</summary>
        public Color TextOn(Color colour)
        {
            return IsLight(colour) ? darkText : Color.white;
        }

        /// <summary><paramref name="colour"/> darkened by <paramref name="factor"/> (1 = unchanged),
        /// alpha kept.</summary>
        public static Color Shade(Color colour, float factor)
        {
            return new Color(colour.r * factor, colour.g * factor, colour.b * factor, colour.a);
        }

        /// <summary>How long until the next daily puzzle unlocks, as something to read -- "Next in
        /// 7h 12m". Shown wherever a player has already solved today and the only answer to "what
        /// now" is a wait: the main menu's Daily card, and the Daily page's own footer and
        /// today-solved banner.
        ///
        /// Lives here rather than on either page because both say it, and two copies would be two
        /// chances to word or round it differently. Minutes are floored by TimeSpan, so the last
        /// minute would otherwise read "Next in 0m" -- it is clamped to 1 instead, since a
        /// countdown that has reached zero while the puzzle is still locked reads as broken.
        ///
        /// Takes the time rather than reading the clock itself, so a caller ticking this once a
        /// second measures every line it draws against one instant.</summary>
        public static string NextPuzzleText(System.DateTime now)
        {
            System.TimeSpan left = FreeFlow.GamePlay.DailyChallengeSelector.TimeUntilNextDay(now);
            int hours = (int)left.TotalHours;
            return hours > 0
                ? "Next in " + hours + "h " + left.Minutes + "m"
                : "Next in " + Mathf.Max(1, left.Minutes) + "m";
        }
    }
}
