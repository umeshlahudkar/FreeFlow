using System;
using FreeFlow.UI;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// DailyChallengePage.FormatCountdown -- the "RESETS IN ..." label on the Daily Challenge hub.
    /// Pure arithmetic over a TimeSpan, so the boundaries that only occur around midnight can be
    /// checked without waiting for one.
    /// </summary>
    public class DailyCountdownTests
    {
        [Test]
        public void MostOfTheDay_ReadsInHoursAndMinutes()
        {
            Assert.AreEqual("4H 47M", DailyChallengePage.FormatCountdown(new TimeSpan(4, 47, 12)));
            Assert.AreEqual("23H 59M", DailyChallengePage.FormatCountdown(new TimeSpan(23, 59, 59)));
        }

        [Test]
        public void InsideTheLastHour_SwitchesToMinutesAndSeconds()
        {
            Assert.AreEqual("59M 30S", DailyChallengePage.FormatCountdown(new TimeSpan(0, 59, 30)));
            Assert.AreEqual("1M 5S", DailyChallengePage.FormatCountdown(new TimeSpan(0, 1, 5)));
        }

        [Test]
        public void InsideTheLastMinute_CountsSeconds()
        {
            // The case this formatting exists for: it used to read "0H 0M" for a solid minute.
            Assert.AreEqual("3S", DailyChallengePage.FormatCountdown(TimeSpan.FromSeconds(3)));
            Assert.AreEqual("45S", DailyChallengePage.FormatCountdown(TimeSpan.FromSeconds(45)));
            Assert.AreEqual("0S", DailyChallengePage.FormatCountdown(TimeSpan.Zero));
        }

        [Test]
        public void TheHourBoundary_FlipsExactlyAtSixtyMinutes()
        {
            Assert.AreEqual("1H 0M", DailyChallengePage.FormatCountdown(new TimeSpan(1, 0, 0)));
            Assert.AreEqual("59M 59S", DailyChallengePage.FormatCountdown(new TimeSpan(0, 59, 59)));
        }

        [Test]
        public void APastDeadline_ReadsZeroRatherThanANegativeCount()
        {
            // Only reachable if the device clock jumps backwards over the reset; the label should
            // not start counting up in negatives.
            Assert.AreEqual("0S", DailyChallengePage.FormatCountdown(TimeSpan.FromSeconds(-5)));
            Assert.AreEqual("0S", DailyChallengePage.FormatCountdown(TimeSpan.FromHours(-3)));
        }

        [Test]
        public void MoreThanADay_FoldsIntoHoursRatherThanDroppingTheDay()
        {
            // TimeSpan.Hours alone would report 2 here and silently lose the day.
            Assert.AreEqual("26H 30M", DailyChallengePage.FormatCountdown(new TimeSpan(1, 2, 30, 0)));
        }
    }
}
