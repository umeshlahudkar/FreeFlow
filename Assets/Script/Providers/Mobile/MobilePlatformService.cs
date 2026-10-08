using System;
using FreeFlow.Core.Services;

namespace FreeFlow.Platform.Mobile
{
    /// <summary>The Play Store and App Store host: nothing to report to and nothing that pauses or
    /// mutes the game from outside, so every member is a no-op -- the game behaves exactly as it did
    /// before this service existed.</summary>
    public sealed class MobilePlatformService : IPlatformService
    {
        public void Initialize(Action onComplete) { onComplete?.Invoke(); }
        public void SignalFirstFrameReady() { }
        public void SignalGameReady() { }
        public bool IsAudioEnabled { get { return true; } }
        public bool CanOpenExternalLinks { get { return true; } }

#pragma warning disable 0067 // never raised: a phone OS does not pause or mute the game through this
        public event Action<bool> AudioEnabledChanged;
        public event Action Paused;
        public event Action Resumed;
#pragma warning restore 0067
    }
}
