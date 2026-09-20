using System;
using FreeFlow.Util;
using GoogleMobileAds.Api;
using UnityEngine;

/// <summary>Owns the AdMob (Google Mobile Ads) SDK: one-time initialization and the two ad
/// formats the game shows -- rewarded (the hint top-up) and interstitial. Callers never touch
/// GoogleMobileAds types directly; they get a plain success/failure callback, and for rewarded
/// that "success" only ever fires once the reward was actually earned, not just because the ad
/// closed.
///
/// Implements IInitializable so a scene's GameBootstrap can sequence this SDK's startup against
/// the others (analytics, auth, ...) instead of it racing them from its own Awake/Start.</summary>
public class ADManager : Singleton<ADManager>, IInitializable
{
    // Live and test IDs are separate fields, not one field swapped at build time, so a live ID
    // can be filled in and reviewed in the inspector long before FINAL_BUILD is ever defined --
    // see SelectAdUnitId for which pair actually gets used.
    // TODO: fill in this game's own live IDs from the AdMob console before shipping -- empty by
    // default so a FINAL_BUILD build fails loudly (an empty ad unit ID) rather than shipping with
    // Google's test IDs by accident.
    [Header("Ad Unit IDs -- Android (Live, used when FINAL_BUILD is defined)")]
    [SerializeField] private string androidLiveRewardedAdUnitId = "";
    [SerializeField] private string androidLiveInterstitialAdUnitId = "";

    [Header("Ad Unit IDs -- iOS (Live, used when FINAL_BUILD is defined)")]
    [SerializeField] private string iosLiveRewardedAdUnitId = "";
    [SerializeField] private string iosLiveInterstitialAdUnitId = "";

    // Google's public test IDs -- safe to ship in any non-FINAL_BUILD (dev/debug) build.
    [Header("Ad Unit IDs -- Android (Test, used otherwise)")]
    [SerializeField] private string androidTestRewardedAdUnitId = "ca-app-pub-3940256099942544/5224354917";
    [SerializeField] private string androidTestInterstitialAdUnitId = "ca-app-pub-3940256099942544/1033173712";

    [Header("Ad Unit IDs -- iOS (Test, used otherwise)")]
    [SerializeField] private string iosTestRewardedAdUnitId = "ca-app-pub-3940256099942544/1712485313";
    [SerializeField] private string iosTestInterstitialAdUnitId = "ca-app-pub-3940256099942544/4411468910";

    private RewardedAd rewardedAd;
    private InterstitialAd interstitialAd;
    private bool isInitialized;
    private bool isInitializing;


    private string RewardedAdUnitId
    {
        get
        {
#if UNITY_ANDROID
            return SelectAdUnitId(androidLiveRewardedAdUnitId, androidTestRewardedAdUnitId);
#elif UNITY_IOS
            return SelectAdUnitId(iosLiveRewardedAdUnitId, iosTestRewardedAdUnitId);
#else
            return SelectAdUnitId(androidLiveRewardedAdUnitId, androidTestRewardedAdUnitId);
#endif
        }
    }

    private string InterstitialAdUnitId
    {
        get
        {
#if UNITY_ANDROID
            return SelectAdUnitId(androidLiveInterstitialAdUnitId, androidTestInterstitialAdUnitId);
#elif UNITY_IOS
            return SelectAdUnitId(iosLiveInterstitialAdUnitId, iosTestInterstitialAdUnitId);
#else
            return SelectAdUnitId(androidLiveInterstitialAdUnitId, androidTestInterstitialAdUnitId);
#endif
        }
    }

    /// <summary>The same FINAL_BUILD symbol the rest of the project strips dev-only code with
    /// (see DeveloperPage) decides ad IDs too: a store build (FINAL_BUILD defined) serves real
    /// ads, any other build serves Google's test ads so development never risks invalid-traffic
    /// clicks/impressions on the live account.</summary>
    private static string SelectAdUnitId(string liveId, string testId)
    {
#if FINAL_BUILD
        return liveId;
#else
        return testId;
#endif
    }

    /// <summary>Boots the Mobile Ads SDK once, then starts the first load of each ad format so
    /// one is usually already sitting in memory by the time a player asks for it.
    /// <paramref name="onComplete"/> fires once initialization and both loads have been kicked
    /// off -- not once an ad has actually finished loading, since a slow or failed load shouldn't
    /// hold up every manager queued behind this one in GameBootstrap.</summary>
    public void Initialize(Action onComplete)
    {
        if (isInitialized)
        {
            onComplete?.Invoke();
            return;
        }

        if (isInitializing)
            return;

        isInitializing = true;

        MobileAds.Initialize(initStatus =>
        {
            isInitializing = false;
            isInitialized = true;

            LoadRewardedAd();
            LoadInterstitialAd();

            onComplete?.Invoke();
        });
    }

    // ---- rewarded ------------------------------------------------------------------------

    private void LoadRewardedAd()
    {
        if (rewardedAd != null)
        {
            rewardedAd.Destroy();
            rewardedAd = null;
        }

        RewardedAd.Load(RewardedAdUnitId, new AdRequest(), (RewardedAd ad, LoadAdError error) =>
        {
            if (error != null || ad == null)
            {
                Debug.LogWarning("ADManager: rewarded ad failed to load: " + error);
                return;
            }

            rewardedAd = ad;
        });
    }

    /// <summary>Shows a rewarded ad if one is ready. <paramref name="onComplete"/> fires only
    /// once the player actually earned the reward; anything else -- none loaded, the SDK
    /// couldn't show it, or the player closed it before earning the reward -- calls
    /// <paramref name="onFailed"/> instead. Either way, the next ad starts loading right
    /// away.</summary>
    public void ShowRewardedAd(Action onComplete, Action onFailed)
    {
        if (rewardedAd == null || !rewardedAd.CanShowAd())
        {
            onFailed?.Invoke();
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
            if (earnedReward) { onComplete?.Invoke(); }
            else { onFailed?.Invoke(); }
        };

        adToShow.OnAdFullScreenContentFailed += (AdError error) =>
        {
            Debug.LogWarning("ADManager: rewarded ad failed to show: " + error);
            adToShow.Destroy();
            LoadRewardedAd();
            onFailed?.Invoke();
        };

        adToShow.Show((Reward reward) => { earnedReward = true; });
    }

    // ---- interstitial ----------------------------------------------------------------------

    private void LoadInterstitialAd()
    {
        if (interstitialAd != null)
        {
            interstitialAd.Destroy();
            interstitialAd = null;
        }

        InterstitialAd.Load(InterstitialAdUnitId, new AdRequest(), (InterstitialAd ad, LoadAdError error) =>
        {
            if (error != null || ad == null)
            {
                Debug.LogWarning("ADManager: interstitial ad failed to load: " + error);
                return;
            }

            interstitialAd = ad;
        });
    }

    /// <summary>Shows an interstitial ad if one is ready. There's no reward to earn, so
    /// <paramref name="onComplete"/> and <paramref name="onFailed"/> only distinguish whether
    /// the ad actually showed.</summary>
    public void ShowInterstitialAd(Action onComplete, Action onFailed)
    {
        if (interstitialAd == null || !interstitialAd.CanShowAd())
        {
            onFailed?.Invoke();
            LoadInterstitialAd();
            return;
        }

        InterstitialAd adToShow = interstitialAd;
        interstitialAd = null;

        adToShow.OnAdFullScreenContentClosed += () =>
        {
            adToShow.Destroy();
            LoadInterstitialAd();
            onComplete?.Invoke();
        };

        adToShow.OnAdFullScreenContentFailed += (AdError error) =>
        {
            Debug.LogWarning("ADManager: interstitial ad failed to show: " + error);
            adToShow.Destroy();
            LoadInterstitialAd();
            onFailed?.Invoke();
        };

        adToShow.Show();
    }
}
