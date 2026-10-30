using System;

namespace FreeFlow.Core.Services
{
    /// <summary>
    /// Local reminders: something queued now to appear later, whether or not the game is running
    /// when it does.
    ///
    /// The thing to keep in mind about every method here is that NONE of our code runs at the
    /// moment a reminder appears. The OS shows the text it was handed, to a player who may have
    /// solved today's puzzle an hour ago. So everything a reminder claims has to be true when it
    /// is SCHEDULED, and the way it stays honest is that opening the game cancels the lot --
    /// see <see cref="CancelAll"/>.
    ///
    /// ReminderScheduler decides what to queue and when; a provider only carries it out.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>Registers whatever the platform needs before anything can be posted. No
        /// callback: unlike the ad or analytics SDKs, there is nothing asynchronous to wait
        /// for.</summary>
        void Initialize();

        /// <summary>Whether this platform can post notifications at all. False on the web build,
        /// where the whole feature is absent rather than merely switched off.</summary>
        bool IsSupported { get; }

        /// <summary>Whether the player has allowed them. On Android 13+ this is false until asked,
        /// and stays false if they refused.</summary>
        bool HasPermission { get; }

        /// <summary>Asks once, if the OS will still show the prompt. The callback carries the
        /// answer, and is invoked immediately with false where there is nothing to ask -- including
        /// when the player has refused often enough that only the system settings screen can
        /// change it.</summary>
        void RequestPermission(Action<bool> onGranted);

        /// <summary>
        /// Queues one reminder. <paramref name="fireAtDeviceLocal"/> is read on the DEVICE clock,
        /// not TimeManager's -- the alarm fires against the device's own notion of the time, so a
        /// caller that computed the moment from the game clock can be off by whatever that clock
        /// has drifted.
        /// </summary>
        void Schedule(int id, string title, string body, DateTime fireAtDeviceLocal);

        /// <summary>Drops every reminder, both the ones still pending and any already sitting in
        /// the tray. The player opening the game is what makes all of them wrong at once.</summary>
        void CancelAll();
    }
}
