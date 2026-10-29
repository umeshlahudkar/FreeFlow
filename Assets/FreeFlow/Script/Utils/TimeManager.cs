using System;
using UnityEngine;

namespace FreeFlow.Util
{
    /// <summary>
    /// The game's one clock. Everything that asks "what time is it" asks here, so the local-vs-UTC
    /// decision lives in exactly one place instead of being retyped at every call site.
    ///
    /// It reads the device clock ONCE per sync and advances from there on
    /// <see cref="Time.realtimeSinceStartupAsDouble"/>, which Unity documents as coming from the
    /// system timer rather than from frames: it keeps counting while the app is backgrounded, and
    /// on desktop, web, XR and mobile the value it returns includes time the system spent asleep.
    /// (Console platforms differ -- see Unity's platform notes if this game ever ships to one.)
    /// Verified on 6000.3: 16.3 seconds of wall clock passed with zero frames rendered and the two
    /// agreed to within a millisecond.
    ///
    /// The point of the snapshot is NOT speed -- a direct DateTime.UtcNow read is about 14ns
    /// against this path's 35ns, measured in this project. It is that the clock does not jump
    /// mid-session when the device's wall clock moves under it. What the player sees stays
    /// monotonic.
    ///
    /// The cost of that is drift: anything the device clock learns after a sync (a timezone the
    /// player flew into, a DST flip, NTP correcting a wrong clock) is invisible until the next
    /// one. So <see cref="Resync"/> runs on every resume -- see PlatformManager.OnResumed -- which
    /// is also the moment Android's AlarmManager comparison matters, since a scheduled
    /// notification fires against the DEVICE clock, not this one. The two have to agree.
    /// </summary>
    public static class TimeManager
    {
        // The device clock and the monotonic counter, read in the same breath. Everything after a
        // sync is these two plus elapsed.
        private static DateTime utcAtSync;
        private static double monoAtSync;
        private static TimeSpan offsetAtSync;

        private static bool synced;

        // Unity documents a known issue where realtimeSinceStartup(AsDouble) returns incorrect
        // values during OnDestroy. Teardown is exactly when the save is flushed, so a wrong value
        // here would be written into the player's file and kept. Past this flag the snapshot is
        // abandoned and the device clock is read directly -- a jump no longer matters when the
        // process is ending, but a garbage timestamp would.
        private static bool shuttingDown;

        /// <summary>
        /// Re-reads the device clock, its UTC offset and the monotonic counter together. Called at
        /// first use and on every resume; safe to call at any time.
        /// </summary>
        public static void Resync()
        {
            DateTime utc = DateTime.UtcNow;

            // The offset is DERIVED from the two clock readings rather than asked of
            // TimeZoneInfo.Local.GetUtcOffset. Both give the same answer, summer time included --
            // but TimeZoneInfo is the one that needs the platform's timezone database to be
            // present and correct, and on WebGL (this project ships a YouTube Playables build)
            // that is reported missing or an hour out often enough not to build on. DateTime.Now
            // works wherever the platform has a local clock at all, which is everywhere.
            //
            // Rounded to the minute because the two reads are not atomic -- measured at
            // 05:29:59.9994 for a +05:30 device. Every real-world offset is a whole number of
            // minutes (+05:30, +05:45, +12:45 and so on), so rounding loses nothing and drops the
            // microseconds between the reads.
            TimeSpan rawOffset = DateTime.Now - utc;
            offsetAtSync = TimeSpan.FromMinutes(Math.Round(rawOffset.TotalMinutes));

            utcAtSync = utc;
            monoAtSync = Time.realtimeSinceStartupAsDouble;
            synced = true;
        }

        /// <summary>
        /// UTC now. This is what gets PERSISTED -- a moment in time means nothing without a
        /// timezone, so anything written to a save file or handed to a server is this, never
        /// <see cref="Now"/>.
        /// </summary>
        public static DateTime UtcNow
        {
            get
            {
                if (shuttingDown) { return DateTime.UtcNow; }
                if (!synced) { Resync(); }

                return utcAtSync.AddSeconds(Time.realtimeSinceStartupAsDouble - monoAtSync);
            }
        }

        /// <summary>
        /// The player's local wall-clock time -- what the game SHOWS, and what decides which
        /// calendar day it is.
        ///
        /// Deliberately <see cref="DateTimeKind.Unspecified"/>: this is a reading off the wall,
        /// not an instant. Marking it Local would invite ToUniversalTime() somewhere and apply the
        /// offset a second time.
        /// </summary>
        public static DateTime Now
        {
            get { return DateTime.SpecifyKind(UtcNow + UtcOffset, DateTimeKind.Unspecified); }
        }

        /// <summary>The device's UTC offset as of the last sync, DST included.</summary>
        public static TimeSpan UtcOffset
        {
            get
            {
                if (!synced) { Resync(); }
                return offsetAtSync;
            }
        }

        // Re-armed for each play session so a project with domain reload turned off cannot carry a
        // previous session's snapshot -- or a second copy of the quit handler -- into the next one.
        // The -= before the += is what keeps the subscription at exactly one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession()
        {
            synced = false;
            shuttingDown = false;

            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        private static void OnQuitting()
        {
            shuttingDown = true;
        }
    }
}
