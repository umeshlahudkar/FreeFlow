using FreeFlow.Core.Services;
using UnityEngine;

namespace FreeFlow.Ads.AdMob
{
    /// <summary>The AdMob (Google Mobile Ads) provider asset: holds the ad unit IDs and builds an
    /// <see cref="AdMobAdService"/> from them. Referenced from the mobile ServiceConfig.</summary>
    [CreateAssetMenu(fileName = "AdMobAdServiceProvider", menuName = "FreeFlow/Providers/AdMob Ads")]
    public class AdMobAdServiceProvider : AdServiceProvider
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

        public override IAdService Create()
        {
            return new AdMobAdService(RewardedAdUnitId, InterstitialAdUnitId);
        }

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
    }
}
