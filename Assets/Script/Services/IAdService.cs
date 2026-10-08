using System;

namespace FreeFlow.Core.Services
{
    /// <summary>What happened when an ad was asked to show.</summary>
    public enum AdShowResult
    {
        /// <summary>Nothing was shown: the SDK is not up yet, or no ad is loaded. Reported straight
        /// from within the Show call, before it returns.</summary>
        NotReady,

        /// <summary>An ad was loaded but the SDK could not put it on screen.</summary>
        FailedToShow,

        /// <summary>The ad was shown and closed. For a rewarded ad: closed before the reward was
        /// earned.</summary>
        Closed,

        /// <summary>A rewarded ad was watched far enough to earn its reward.</summary>
        Rewarded
    }

    /// <summary>
    /// An ad network: loading and showing ads, nothing else. WHEN an ad is offered -- the
    /// interstitial cadence, which hint shortage triggers a rewarded preload, what a reward pays --
    /// is decided by the game (ADManager), so a new network only has to implement this.
    ///
    /// Every result other than <see cref="AdShowResult.NotReady"/> arrives after the ad overlay
    /// has handed control back to Unity, possibly before a frame has rendered; ADManager deals
    /// with that, not the provider.
    /// </summary>
    public interface IAdService
    {
        /// <summary>Starts the SDK. Calls <paramref name="onComplete"/> exactly once.</summary>
        void Initialize(Action onComplete);

        /// <summary>Starts loading a rewarded ad unless one is already loaded or loading.</summary>
        void PreloadRewarded();

        void ShowRewarded(Action<AdShowResult> onResult);

        /// <summary>Starts loading an interstitial unless one is already loaded or loading.</summary>
        void PreloadInterstitial();

        /// <summary>Whether an interstitial is loaded and can be shown right now.</summary>
        bool IsInterstitialReady { get; }

        void ShowInterstitial(Action<AdShowResult> onResult);
    }
}
