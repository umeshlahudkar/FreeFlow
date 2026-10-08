using System;
using System.Collections;
using FreeFlow.Core.Services;
using FreeFlow.Util;
using UnityEngine;

/// <summary>The game's ad rules: WHEN an ad is offered and what happens after it. The ad network
/// itself -- loading and showing -- is this manager's <see cref="IAdService"/>, built from the
/// build's ServiceConfig (AdMob on Android and iOS). Callers get a plain success/failure callback, and for
/// rewarded that "success" only ever fires once the reward was actually earned, not just because
/// the ad closed.
///
/// Implements IInitializable so a scene's GameBootstrap can sequence the ad SDK's startup against
/// the others (analytics, auth, ...) instead of it racing them from its own Awake/Start.</summary>
public class ADManager : Singleton<ADManager>, IInitializable
{
    // This build's ad network, built once from the ServiceConfig the first time it is needed.
    private IAdService adService;

    private IAdService AdService
    {
        get
        {
            if (adService == null) { adService = ServiceConfig.Current.CreateAdService(); }
            return adService;
        }
    }

    private int levelsCompletedSinceLastInterstitial;
    // NegativeInfinity so the very first interstitial of a session is never held back by the
    // time gate -- there is no "last shown" yet to measure against.
    private float lastInterstitialShownRealtime = float.NegativeInfinity;

    // The interstitial cadence lives in the ServiceConfig, beside the provider choice, so it can be
    // balanced against retention without a code change.
    private static int LevelsBetweenInterstitials
    {
        get { return ServiceConfig.Current.LevelsBetweenInterstitials; }
    }

    private static float MinSecondsBetweenInterstitials
    {
        get { return ServiceConfig.Current.MinSecondsBetweenInterstitials; }
    }

    /// <summary>Fires <paramref name="callback"/> two frames from now instead of immediately. The
    /// SDK's own closed/failed callbacks land the moment the native ad overlay hands control back to
    /// Unity, before a frame has actually rendered on top of it -- calling straight back into
    /// gameplay/UI code from there is calling it mid-transition. Two yield return nulls give Unity's
    /// own state (focus, rendering) a couple of frames to settle back to normal first.</summary>
    private IEnumerator InvokeAfterAdClosed(Action callback)
    {
        yield return null;
        yield return null;
        callback?.Invoke();
    }

    /// <summary>Starts the ad network. <paramref name="onComplete"/> fires immediately, before
    /// that has actually happened -- an ad SDK's own startup (a network round trip) is not worth
    /// blocking MainScene load over. Neither ad format is loaded here; see NotifyLevelStarted.</summary>
    public void Initialize(Action onComplete)
    {
        AdService.Initialize(onComplete);
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

        if (levelsCompletedSinceLastInterstitial >= LevelsBetweenInterstitials - 1)
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
        if (levelsCompletedSinceLastInterstitial < LevelsBetweenInterstitials) { return; }
        if (Time.realtimeSinceStartup - lastInterstitialShownRealtime < MinSecondsBetweenInterstitials) { return; }
        if (!AdService.IsInterstitialReady) { return; }

        levelsCompletedSinceLastInterstitial = 0;
        lastInterstitialShownRealtime = Time.realtimeSinceStartup;
        ShowInterstitialAd(null, null);
    }

    // ---- rewarded ------------------------------------------------------------------------

    /// <summary>Starts a rewarded fetch if one is not already loaded or in flight. Safe to call
    /// repeatedly -- see NotifyLevelStarted, its only caller.</summary>
    public void PreloadRewardedAd()
    {
        AdService.PreloadRewarded();
    }

    /// <summary>Shows a rewarded ad if one is ready. <paramref name="onComplete"/> fires only
    /// once the player actually earned the reward; anything else -- none loaded, the network
    /// couldn't show it, or the player closed it before earning the reward -- calls
    /// <paramref name="onFailed"/> instead.
    ///
    /// Once an ad actually showed, the callback runs a couple of frames later (see
    /// <see cref="InvokeAfterAdClosed"/>). "Not ready" failures come straight from this method's
    /// own call stack and are not delayed.</summary>
    public void ShowRewardedAd(Action onComplete, Action onFailed)
    {
        AdService.ShowRewarded(result =>
        {
            if (result == AdShowResult.NotReady)
            {
                onFailed?.Invoke();
                return;
            }

            StartCoroutine(InvokeAfterAdClosed(result == AdShowResult.Rewarded ? onComplete : onFailed));
        });
    }

    // ---- interstitial ----------------------------------------------------------------------

    /// <summary>Starts an interstitial fetch if one is not already loaded or in flight. Safe to
    /// call repeatedly -- see NotifyLevelStarted (one level ahead of when it's due).</summary>
    public void PreloadInterstitialAd()
    {
        AdService.PreloadInterstitial();
    }

    /// <summary>Shows an interstitial ad if one is ready. There's no reward to earn, so
    /// <paramref name="onComplete"/> and <paramref name="onFailed"/> only distinguish whether
    /// the ad actually showed. Same delayed-callback rule as ShowRewardedAd.</summary>
    public void ShowInterstitialAd(Action onComplete, Action onFailed)
    {
        AdService.ShowInterstitial(result =>
        {
            if (result == AdShowResult.NotReady)
            {
                onFailed?.Invoke();
                return;
            }

            StartCoroutine(InvokeAfterAdClosed(result == AdShowResult.Closed ? onComplete : onFailed));
        });
    }
}
