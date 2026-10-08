using System;
using Firebase;
using Firebase.Analytics;
using Firebase.Crashlytics;
using Firebase.Extensions;
using FreeFlow.Core.Services;
using UnityEngine;

namespace FreeFlow.Analytics.Firebase
{
    /// <summary>
    /// Firebase behind <see cref="IAnalyticsService"/>: the one dependency check every Firebase
    /// product (Analytics, Crashlytics) needs before it is safe to touch, then event logging.
    /// Analytics needs nothing further -- FirebaseAnalytics.LogEvent works the moment the default
    /// app exists. Crashlytics gets one explicit setting; everything else about it is automatic once
    /// the plugin is present and the app has been created. Moved here unchanged from FirebaseManager
    /// and AnalyticsManager.
    /// </summary>
    public sealed class FirebaseAnalyticsService : IAnalyticsService
    {
        private bool isInitialized;
        private bool isInitializing;

        /// <summary>Whether Firebase actually came up. False also covers "Initialize never
        /// completed" -- a device with missing or outdated Google Play Services fails the dependency
        /// check without throwing.</summary>
        public bool IsAvailable { get; private set; }

        /// <summary>Reports done immediately -- Firebase's own dependency check (on some devices, an
        /// actual Play-Services repair/update prompt) is not worth blocking MainScene load over. The
        /// check still runs, and Crashlytics/Analytics still come up, just in the background.</summary>
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

        public void LogEvent(string eventName, params AnalyticsParameter[] parameters)
        {
            if (parameters == null || parameters.Length == 0)
            {
                FirebaseAnalytics.LogEvent(eventName);
                return;
            }

            Parameter[] firebaseParameters = new Parameter[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                firebaseParameters[i] = ToFirebase(parameters[i]);
            }
            FirebaseAnalytics.LogEvent(eventName, firebaseParameters);
        }

        /// <summary>Crashlytics already reports uncaught exceptions on its own (see Initialize), and
        /// the game has never sent non-fatal errors to it, so nothing is added here.</summary>
        public void LogError(string message) { }

        // Firebase parameters are typed: whole numbers as long, fractions as double, anything else
        // as its text -- the same overloads the game called directly before (level as an int,
        // mode as a string).
        private static Parameter ToFirebase(AnalyticsParameter parameter)
        {
            switch (parameter.Value)
            {
                case int i: return new Parameter(parameter.Name, i);
                case long l: return new Parameter(parameter.Name, l);
                case float f: return new Parameter(parameter.Name, f);
                case double d: return new Parameter(parameter.Name, d);
                default: return new Parameter(parameter.Name, parameter.Value == null ? "" : parameter.Value.ToString());
            }
        }
    }
}
