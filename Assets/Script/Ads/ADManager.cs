using System;
using System.Collections;
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

    // Both conditions below must hold before an interstitial is due -- see
    // TryShowInterstitialIfDue. Tweakable here rather than hardcoded so cadence can be balanced
    // against retention without a code change.
    [Header("Interstitial Ad Gating")]
    [Tooltip("How many levels the player must complete before another interstitial is due.")]
    [SerializeField] private int levelsBetweenInterstitials = 3;
    [Tooltip("Minimum real-world seconds that must pass between two interstitials.")]
    [SerializeField] private float minSecondsBetweenInterstitials = 180f;

    private RewardedAd rewardedAd;
    private InterstitialAd interstitialAd;
    private bool isInitialized;
    private bool isInitializing;
    private bool isLoadingRewardedAd;
    private bool isLoadingInterstitialAd;

    private int levelsCompletedSinceLastInterstitial;
    // NegativeInfinity so the very first interstitial of a session is never held back by the
    // time gate -- there is no "last shown" yet to measure against.
    private float lastInterstitialShownRealtime = float.NegativeInfinity;


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

    /// <summary>Fires <paramref name="callback"/> two frames from now instead of immediately. The
    /// SDK's own OnAdFullScreenContentClosed/Failed callbacks land the moment the native ad
    /// overlay hands control back to Unity, before a frame has actually rendered on top of it --
    /// calling straight back into gameplay/UI code from there is calling it mid-transition. Two
    /// yield return nulls give Unity's own state (focus, rendering) a couple of frames to settle
    /// back to normal first.</summary>
    private IEnumerator InvokeAfterAdClosed(Action callback)
    {
        yield return null;
        yield return null;
        callback?.Invoke();
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

    /// <summary>Starts the Mobile Ads SDK. <paramref name="onComplete"/> fires immediately,
    /// before that has actually happened -- GameBootstrap waits for it before moving on to
    /// whatever's queued after this manager, and an ad SDK's own startup (a network round trip)
    /// is not worth blocking MainScene load over.
    ///
    /// Neither ad format is loaded here -- an ad sitting in memory this early is very likely
    /// stale (a rewarded ad the player never runs low on hints to redeem, an interstitial shown
    /// long after the level-count/time gate that justified fetching it). Each format is instead
    /// loaded on demand: see NotifyLevelStarted for rewarded, and NotifyLevelStarted /
    /// PreloadInterstitialAd for interstitial.</summary>
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

    /// <summary>Called once a level actually starts playing (not merely navigating to the
    /// gameplay page -- see GamePlayController.BeginAttempt). Preloads whichever ad format might
    /// be needed soon, so it is not started cold the moment it is actually asked for:
    /// - Rewarded, only once the player has no hints left to spend -- <paramref
    ///   name="hintsRemaining"/> is read at the call site rather than here, since ADManager has
    ///   no reason to know about ProfileManager/hint balances.
    /// - Interstitial, one level before it is due, i.e. this attempt is the last one that will
    ///   push levelsCompletedSinceLastInterstitial up to the threshold.</summary>
    public void NotifyLevelStarted(int hintsRemaining)
    {
        if (hintsRemaining <= 0)
        {
            PreloadRewardedAd();
        }

        if (levelsCompletedSinceLastInterstitial >= levelsBetweenInterstitials - 1)
        {
            PreloadInterstitialAd();
        }
    }

    /// <summary>Bumps the level-complete tally and, if both gates now pass and an interstitial
    /// is actually sitting loaded, shows it. Called once per completed attempt, right after the
    /// level-complete screen opens (see UIController.ActivateLevelCompleteScreen) so the
    /// interstitial always lands after that screen is already up, never before or instead of
    /// it.</summary>
    public void NotifyLevelCompleted()
    {
        levelsCompletedSinceLastInterstitial++;
        TryShowInterstitialIfDue();
    }

    /// <summary>Shows the interstitial only when both the level-count and time gates have been
    /// met AND an ad is actually ready. If it is due but nothing is loaded yet (a slow/failed
    /// fetch), this does NOT wait for one -- the player keeps playing, and the next completed
    /// level tries again; the gates are left exactly as they are so that retry happens
    /// immediately rather than waiting out a fresh cooldown window.</summary>
    private void TryShowInterstitialIfDue()
    {
        if (levelsCompletedSinceLastInterstitial < levelsBetweenInterstitials) { return; }
        if (Time.realtimeSinceStartup - lastInterstitialShownRealtime < minSecondsBetweenInterstitials) { return; }
        if (!isInitialized || interstitialAd == null || !interstitialAd.CanShowAd()) { return; }

        levelsCompletedSinceLastInterstitial = 0;
        lastInterstitialShownRealtime = Time.realtimeSinceStartup;
        ShowInterstitialAd(null, null);
    }

    // ---- rewarded ------------------------------------------------------------------------

    /// <summary>Starts a rewarded fetch if one is not already loaded or in flight. Safe to call
    /// repeatedly -- see NotifyLevelStarted, its only caller.</summary>
    public void PreloadRewardedAd()
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
        RewardedAd.Load(RewardedAdUnitId, new AdRequest(), (RewardedAd ad, LoadAdError error) =>
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

    /// <summary>Shows a rewarded ad if one is ready. <paramref name="onComplete"/> fires only
    /// once the player actually earned the reward; anything else -- none loaded, the SDK
    /// couldn't show it, or the player closed it before earning the reward -- calls
    /// <paramref name="onFailed"/> instead. Either way, the next ad starts loading right away.
    ///
    /// Once the ad actually closes, the callback is not called from the SDK's own closed/failed
    /// event -- see <see cref="InvokeAfterAdClosed"/> -- so it always lands a couple of frames
    /// after control is back with Unity. The "not initialized"/"none ready" failures above are
    /// synchronous, straight from this method's own call stack, and are not delayed.</summary>
    public void ShowRewardedAd(Action onComplete, Action onFailed)
    {
        // A caller can reach this before GameBootstrap's fire-and-forget Initialize() has
        // actually finished -- nothing here waits for it (see Initialize). Retrying the SDK's own
        // init instead of calling LoadRewardedAd directly matters: an ad load attempted before
        // MobileAds.Initialize completes is not something the SDK supports.
        if (!isInitialized)
        {
            Initialize(null);
            onFailed?.Invoke();
            return;
        }

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
            StartCoroutine(InvokeAfterAdClosed(earnedReward ? onComplete : onFailed));
        };

        adToShow.OnAdFullScreenContentFailed += (AdError error) =>
        {
            Debug.LogWarning("ADManager: rewarded ad failed to show: " + error);
            adToShow.Destroy();
            LoadRewardedAd();
            StartCoroutine(InvokeAfterAdClosed(onFailed));
        };

        adToShow.Show((Reward reward) => { earnedReward = true; });
    }

    // ---- interstitial ----------------------------------------------------------------------

    /// <summary>Starts an interstitial fetch if one is not already loaded or in flight. Safe to
    /// call repeatedly -- see NotifyLevelStarted (one level ahead of when it's due).</summary>
    public void PreloadInterstitialAd()
    {
        if (!isInitialized || isLoadingInterstitialAd || (interstitialAd != null && interstitialAd.CanShowAd())) { return; }
        LoadInterstitialAd();
    }

    private void LoadInterstitialAd()
    {
        if (interstitialAd != null)
        {
            interstitialAd.Destroy();
            interstitialAd = null;
        }

        isLoadingInterstitialAd = true;
        InterstitialAd.Load(InterstitialAdUnitId, new AdRequest(), (InterstitialAd ad, LoadAdError error) =>
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

    /// <summary>Shows an interstitial ad if one is ready. There's no reward to earn, so
    /// <paramref name="onComplete"/> and <paramref name="onFailed"/> only distinguish whether
    /// the ad actually showed.
    ///
    /// Same delayed-callback rule as ShowRewardedAd: once the ad actually closes, the callback
    /// runs a couple of frames later (see <see cref="InvokeAfterAdClosed"/>), not synchronously
    /// from the SDK's own event. The "not initialized"/"none ready" failures above are not
    /// delayed.</summary>
    public void ShowInterstitialAd(Action onComplete, Action onFailed)
    {
        // See the same check in ShowRewardedAd -- a caller can reach here before the
        // fire-and-forget Initialize() from GameBootstrap has actually finished.
        if (!isInitialized)
        {
            Initialize(null);
            onFailed?.Invoke();
            return;
        }

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
            StartCoroutine(InvokeAfterAdClosed(onComplete));
        };

        adToShow.OnAdFullScreenContentFailed += (AdError error) =>
        {
            Debug.LogWarning("ADManager: interstitial ad failed to show: " + error);
            adToShow.Destroy();
            LoadInterstitialAd();
            StartCoroutine(InvokeAfterAdClosed(onFailed));
        };

        adToShow.Show();
    }
}
