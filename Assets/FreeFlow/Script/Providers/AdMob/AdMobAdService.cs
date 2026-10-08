using System;
using FreeFlow.Core.Services;
using GoogleMobileAds.Api;
using UnityEngine;

namespace FreeFlow.Ads.AdMob
{
    /// <summary>
    /// The AdMob (Google Mobile Ads) SDK behind <see cref="IAdService"/>: one-time initialization,
    /// loading and showing the two formats the game uses. Moved here unchanged from ADManager, which
    /// keeps the game's rules (when an ad is due, what a reward pays, the delay after an ad closes).
    ///
    /// For rewarded, <see cref="AdShowResult.Rewarded"/> is reported only once the reward was
    /// actually earned, not just because the ad closed.
    /// </summary>
    public sealed class AdMobAdService : IAdService
    {
        private readonly string rewardedAdUnitId;
        private readonly string interstitialAdUnitId;

        private RewardedAd rewardedAd;
        private InterstitialAd interstitialAd;
        private bool isInitialized;
        private bool isInitializing;
        private bool isLoadingRewardedAd;
        private bool isLoadingInterstitialAd;

        public AdMobAdService(string rewardedAdUnitId, string interstitialAdUnitId)
        {
            this.rewardedAdUnitId = rewardedAdUnitId;
            this.interstitialAdUnitId = interstitialAdUnitId;
        }

        /// <summary>Starts the Mobile Ads SDK. <paramref name="onComplete"/> fires immediately,
        /// before that has actually happened -- GameBootstrap waits for it before moving on to
        /// whatever's queued after this manager, and an ad SDK's own startup (a network round trip)
        /// is not worth blocking MainScene load over.
        ///
        /// Neither ad format is loaded here -- an ad sitting in memory this early is very likely
        /// stale. Each format is instead loaded on demand (see ADManager.NotifyLevelStarted).</summary>
        public void Initialize(Action onComplete)
        {
            onComplete?.Invoke();

            if (isInitialized || isInitializing) { return; }
            isInitializing = true;

            MobileAds.Initialize(initStatus =>
            {
                isInitializing = false;
                isInitialized = true;
            });
        }

        // ---- rewarded ------------------------------------------------------------------------

        /// <summary>Starts a rewarded fetch if one is not already loaded or in flight. Safe to call
        /// repeatedly.</summary>
        public void PreloadRewarded()
        {
            if (!isInitialized || isLoadingRewardedAd || (rewardedAd != null && rewardedAd.CanShowAd())) { return; }
            LoadRewardedAd();
        }

        private void LoadRewardedAd()
        {
            if (rewardedAd != null)
            {
                rewardedAd.Destroy();
                rewardedAd = null;
            }

            isLoadingRewardedAd = true;
            RewardedAd.Load(rewardedAdUnitId, new AdRequest(), (RewardedAd ad, LoadAdError error) =>
            {
                isLoadingRewardedAd = false;
                if (error != null || ad == null)
                {
                    Debug.LogWarning("ADManager: rewarded ad failed to load: " + error);
                    return;
                }

                rewardedAd = ad;
            });
        }

        /// <summary>Shows a rewarded ad if one is ready. Reports <see cref="AdShowResult.Rewarded"/>
        /// only once the player actually earned the reward; anything else -- none loaded, the SDK
        /// couldn't show it, or the player closed it early -- reports the matching failure. Either
        /// way, the next ad starts loading right away.</summary>
        public void ShowRewarded(Action<AdShowResult> onResult)
        {
            // A caller can reach this before the fire-and-forget Initialize() has actually
            // finished. Retrying the SDK's own init instead of loading directly matters: an ad load
            // attempted before MobileAds.Initialize completes is not something the SDK supports.
            if (!isInitialized)
            {
                Initialize(null);
                onResult?.Invoke(AdShowResult.NotReady);
                return;
            }

            if (rewardedAd == null || !rewardedAd.CanShowAd())
            {
                onResult?.Invoke(AdShowResult.NotReady);
                LoadRewardedAd();
                return;
            }

            bool earnedReward = false;
            RewardedAd adToShow = rewardedAd;
            rewardedAd = null;

            adToShow.OnAdFullScreenContentClosed += () =>
            {
                adToShow.Destroy();
                LoadRewardedAd();
                onResult?.Invoke(earnedReward ? AdShowResult.Rewarded : AdShowResult.Closed);
            };

            adToShow.OnAdFullScreenContentFailed += (AdError error) =>
            {
                Debug.LogWarning("ADManager: rewarded ad failed to show: " + error);
                adToShow.Destroy();
                LoadRewardedAd();
                onResult?.Invoke(AdShowResult.FailedToShow);
            };

            adToShow.Show((Reward reward) => { earnedReward = true; });
        }

        // ---- interstitial ----------------------------------------------------------------------

        /// <summary>Starts an interstitial fetch if one is not already loaded or in flight. Safe to
        /// call repeatedly.</summary>
        public void PreloadInterstitial()
        {
            if (!isInitialized || isLoadingInterstitialAd || (interstitialAd != null && interstitialAd.CanShowAd())) { return; }
            LoadInterstitialAd();
        }

        public bool IsInterstitialReady
        {
            get { return isInitialized && interstitialAd != null && interstitialAd.CanShowAd(); }
        }

        private void LoadInterstitialAd()
        {
            if (interstitialAd != null)
            {
                interstitialAd.Destroy();
                interstitialAd = null;
            }

            isLoadingInterstitialAd = true;
            InterstitialAd.Load(interstitialAdUnitId, new AdRequest(), (InterstitialAd ad, LoadAdError error) =>
            {
                isLoadingInterstitialAd = false;
                if (error != null || ad == null)
                {
                    Debug.LogWarning("ADManager: interstitial ad failed to load: " + error);
                    return;
                }

                interstitialAd = ad;
            });
        }

        /// <summary>Shows an interstitial ad if one is ready. There's no reward to earn, so the
        /// result only says whether the ad actually showed (<see cref="AdShowResult.Closed"/>).</summary>
        public void ShowInterstitial(Action<AdShowResult> onResult)
        {
            if (!isInitialized)
            {
                Initialize(null);
                onResult?.Invoke(AdShowResult.NotReady);
                return;
            }

            if (interstitialAd == null || !interstitialAd.CanShowAd())
            {
                onResult?.Invoke(AdShowResult.NotReady);
                LoadInterstitialAd();
                return;
            }

            InterstitialAd adToShow = interstitialAd;
            interstitialAd = null;

            adToShow.OnAdFullScreenContentClosed += () =>
            {
                adToShow.Destroy();
                LoadInterstitialAd();
                onResult?.Invoke(AdShowResult.Closed);
            };

            adToShow.OnAdFullScreenContentFailed += (AdError error) =>
            {
                Debug.LogWarning("ADManager: interstitial ad failed to show: " + error);
                adToShow.Destroy();
                LoadInterstitialAd();
                onResult?.Invoke(AdShowResult.FailedToShow);
            };

            adToShow.Show();
        }
    }
}
