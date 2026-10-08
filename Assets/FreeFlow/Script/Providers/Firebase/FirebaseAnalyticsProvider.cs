using FreeFlow.Core.Services;
using UnityEngine;

namespace FreeFlow.Analytics.Firebase
{
    /// <summary>The Firebase provider asset (Analytics + Crashlytics). Referenced from the mobile
    /// ServiceConfig. Firebase reads its own project settings from google-services.json, so there
    /// is nothing to configure here.</summary>
    [CreateAssetMenu(fileName = "FirebaseAnalyticsProvider", menuName = "FreeFlow/Providers/Firebase Analytics")]
    public class FirebaseAnalyticsProvider : AnalyticsServiceProvider
    {
        public override IAnalyticsService Create()
        {
            return new FirebaseAnalyticsService();
        }
    }
}
