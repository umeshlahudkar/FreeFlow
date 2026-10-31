using System;
using UnityEngine;

namespace FreeFlow.Util
{
    /// <summary>
    /// The game's one clock: the device is read once per play session, and every fetch after that
    /// is that reading plus however long the app has been running.
    ///
    /// The counter is <see cref="Time.realtimeSinceStartupAsDouble"/>, which Unity documents as
    /// coming from the system timer rather than from frames -- it keeps counting while the app is
    /// backgrounded, and on mobile the value it returns includes time the system spent asleep.
    /// Checked on 6000.3: 16.3 seconds of wall clock passed with zero frames rendered and the two
    /// agreed to within a millisecond. That is what lets a single reading stand for a whole
    /// session, however long the player leaves the game sitting in the background.
    ///
    /// What one reading CANNOT see is the device clock changing underneath it -- a timezone flown
    /// into, a DST flip, or NTP correcting a clock that was wrong at launch. Those are rare, and
    /// they heal the next time the app is launched, so nothing re-reads the device here.
    /// </summary>
    public static class TimeManager
    {
        private static DateTime localAtSync;
        private static TimeSpan offsetAtSync;
        private static double monoAtSync;

        /// <summary>The player's local wall-clock time. Everything the game shows, and the thing
        /// that decides which calendar day it is.
        ///
        /// <see cref="DateTimeKind.Unspecified"/> deliberately: this is a reading off the wall, not
        /// an instant. Marking it Local would invite a ToUniversalTime() somewhere and apply the
        /// offset a second time.</summary>
        public static DateTime Now
        {
            get { return localAtSync.AddSeconds(Time.realtimeSinceStartupAsDouble - monoAtSync); }
        }

        /// <summary>The same moment in UTC -- for anything PERSISTED, since a timestamp means
        /// nothing without a timezone. Nothing stores one yet; the daily's day indices are date
        /// labels and belong in <see cref="Now"/>'s frame.</summary>
        public static DateTime UtcNow
        {
            get { return DateTime.SpecifyKind(Now - offsetAtSync, DateTimeKind.Utc); }
        }

        // Once per play session, before any scene loads. Also re-arms a project with domain reload
        // turned off, which would otherwise carry one session's reading into the next.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Sync()
        {
            DateTime utc = DateTime.UtcNow;

            // The offset is derived from the two readings rather than asked of
            // TimeZoneInfo.Local.GetUtcOffset. Both answer the same, DST included, but TimeZoneInfo
            // needs the platform's timezone database to be present and right -- and on WebGL, which
            // this branch ships, that is reported missing or an hour out often enough not to build
            // on. Rounded to the minute because the two reads are not atomic (measured
            // 05:29:59.9994 on a +05:30 device); every real offset is a whole number of minutes.
            offsetAtSync = TimeSpan.FromMinutes(Math.Round((DateTime.Now - utc).TotalMinutes));

            localAtSync = DateTime.SpecifyKind(utc + offsetAtSync, DateTimeKind.Unspecified);
            monoAtSync = Time.realtimeSinceStartupAsDouble;
        }
    }
}
