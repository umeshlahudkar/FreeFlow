using System;
using FreeFlow.Enums;

namespace FreeFlow.Core.Services
{
    // What a build gets for any service its config does not name: everything succeeds quietly and
    // nothing is offered. Kept beside the interfaces so a new interface and its "nothing here" come
    // in the same change.

    /// <summary>No ad network: nothing loads, every Show reports <see cref="AdShowResult.NotReady"/>.</summary>
    public sealed class NullAdService : IAdService
    {
        public void Initialize(Action onComplete) { onComplete?.Invoke(); }
        public void PreloadRewarded() { }
        public void ShowRewarded(Action<AdShowResult> onResult) { onResult?.Invoke(AdShowResult.NotReady); }
        public void PreloadInterstitial() { }
        public bool IsInterstitialReady { get { return false; } }
        public void ShowInterstitial(Action<AdShowResult> onResult) { onResult?.Invoke(AdShowResult.NotReady); }
    }

    /// <summary>No analytics backend: events are dropped.</summary>
    public sealed class NullAnalyticsService : IAnalyticsService
    {
        public void Initialize(Action onComplete) { onComplete?.Invoke(); }
        public void LogEvent(string eventName, params AnalyticsParameter[] parameters) { }
        public void LogError(string message) { }
    }

    /// <summary>No host to report to: never pauses, never mutes.</summary>
    public sealed class NullPlatformService : IPlatformService
    {
        public void Initialize(Action onComplete) { onComplete?.Invoke(); }
        public void SignalFirstFrameReady() { }
        public void SignalGameReady() { }
        public bool IsAudioEnabled { get { return true; } }
        public bool CanOpenExternalLinks { get { return true; } }

#pragma warning disable 0067 // never raised: this host never mutes or pauses the game
        public event Action<bool> AudioEnabledChanged;
        public event Action Paused;
        public event Action Resumed;
#pragma warning restore 0067
    }

    /// <summary>No share sheet.</summary>
    public sealed class NullShareService : IShareService
    {
        public bool IsAvailable { get { return false; } }
        public void Share(string subject, string text, string filePath) { }
    }

    /// <summary>No vibration motor.</summary>
    public sealed class NullHapticsService : IHapticsService
    {
        public bool IsSupported { get { return false; } }
        public void Play(HapticType type) { }
    }
}
