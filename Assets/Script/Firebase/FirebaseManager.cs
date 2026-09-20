using System;
using FreeFlow.Util;
using Firebase;
using Firebase.Crashlytics;
using Firebase.Extensions;
using UnityEngine;

/// <summary>Owns Firebase startup: the one dependency check every Firebase product (Analytics,
/// Crashlytics, and whatever else joins them) needs before it is safe to touch. Analytics needs
/// nothing further from here -- FirebaseAnalytics.LogEvent works the moment the default app
/// exists. Crashlytics gets one explicit setting below; everything else about it is automatic
/// once the plugin is present and the app has been created.
///
/// Implements IInitializable so a scene's GameBootstrap can sequence this against the project's
/// other startup work (ads, auth, ...) instead of it racing them from its own Awake/Start.</summary>
public class FirebaseManager : Singleton<FirebaseManager>, IInitializable
{
    private bool isInitialized;
    private bool isInitializing;

    /// <summary>Whether Firebase actually came up. False also covers "Initialize never
    /// completed" -- a device with missing or outdated Google Play Services fails the dependency
    /// check without throwing, so this is what callers should check before assuming Analytics or
    /// Crashlytics are live.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Reports done immediately, same as ADManager.Initialize -- GameBootstrap waits for
    /// this call before moving on to whatever's queued after this manager, and Firebase's own
    /// dependency check (on some devices, an actual Play-Services repair/update prompt) is not
    /// worth blocking MainScene load over. The check still runs, and Crashlytics/Analytics still
    /// come up, just in the background rather than gating the rest of startup on it.</summary>
    public void Initialize(Action onComplete)
    {
        onComplete?.Invoke();

        if (isInitialized || isInitializing) { return; }
        isInitializing = true;

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            isInitializing = false;
            DependencyStatus status = task.Result;
            IsAvailable = status == DependencyStatus.Available;

            Debug.Log("FirebaseManager: " + status);

            if (IsAvailable)
            {
                isInitialized = true;

                FirebaseApp app = FirebaseApp.DefaultInstance;
                Crashlytics.ReportUncaughtExceptionsAsFatal = true;
            }
            else
            {
                Debug.LogWarning("FirebaseManager: dependencies unavailable (" + status +
                    ") -- Analytics/Crashlytics disabled for this session.");
            }
        });
    }
}
