using System;

namespace FreeFlow.Core.Services
{
    /// <summary>
    /// The host the game runs inside: a phone OS, or a web portal such as YouTube Playables that
    /// wants to be told when the game is ready, can pause it, and can mute it. Hosts with none of
    /// that (the Play Store and App Store builds) implement every member as a no-op.
    /// </summary>
    public interface IPlatformService
    {
        /// <summary>Starts the platform connection. Calls <paramref name="onComplete"/> exactly once.</summary>
        void Initialize(Action onComplete);

        /// <summary>A loading or splash screen is on screen.</summary>
        void SignalFirstFrameReady();

        /// <summary>The game is ready for the player to interact with.</summary>
        void SignalGameReady();

        /// <summary>Whether the host currently allows sound.</summary>
        bool IsAudioEnabled { get; }

        event Action<bool> AudioEnabledChanged;

        /// <summary>Whether the game may send the player to a page outside it, such as the privacy
        /// policy. YouTube Playables forbids any way off the page.</summary>
        bool CanOpenExternalLinks { get; }

        /// <summary>The host asked the game to stop everything, and to save while it can.</summary>
        event Action Paused;

        event Action Resumed;
    }
}
