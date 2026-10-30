using System;
using UnityEngine;

namespace FreeFlow.Util
{
    /// <summary>Starts the platform and analytics services as one step in GameBootstrap's
    /// initialization order, through their owners (PlatformManager, AnalyticsManager). Took over
    /// FirebaseManager's place in StartScene: the Firebase startup itself now lives in the Firebase
    /// analytics provider, and a build with a different provider (or none) starts that instead.
    ///
    /// Ads are started by ADManager and saves by ProfileManager, each in its own place in the
    /// order, exactly as before.</summary>
    public class ServicesInitializer : MonoBehaviour, IInitializable
    {
        public void Initialize(Action onComplete)
        {
            // Its own line, not another link in the chain below: reminders depend on neither the
            // host nor analytics, and nesting it inside them would say they did.
            ReminderScheduler.Initialize();

            PlatformManager.Initialize(() => AnalyticsManager.Initialize(onComplete));
        }
    }
}
