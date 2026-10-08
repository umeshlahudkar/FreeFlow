using System;
using FreeFlow.Core.Services;
using UnityEngine;

/// <summary>The game's analytics events: their names and parameters. Where they are recorded is this
/// manager's <see cref="IAnalyticsService"/>, built from the build's ServiceConfig (Firebase on
/// Android and iOS).</summary>
public class AnalyticsManager
{
    // This build's analytics backend, built once from the ServiceConfig the first time it is needed.
    private static IAnalyticsService service;

    private static IAnalyticsService Service
    {
        get
        {
            if (service == null) { service = ServiceConfig.Current.CreateAnalyticsService(); }
            return service;
        }
    }

    /// <summary>Starts the analytics backend -- one step of StartScene's GameBootstrap order (see
    /// ServicesInitializer). Calls <paramref name="onComplete"/> exactly once.</summary>
    public static void Initialize(Action onComplete)
    {
        Service.Initialize(onComplete);
    }

    public static void LogEvent(string eventName)
    {
        Debug.Log("[Analytics] " + eventName);
        Service.LogEvent(eventName);
    }

    public static void LogLevelStart(int level, string mode)
    {
        Debug.Log("[Analytics] level_start (level=" + level + ", mode=" + mode + ")");
        Service.LogEvent(
            "level_start",
            new AnalyticsParameter("level", level),
            new AnalyticsParameter("mode", mode)
        );
    }

    public static void LogLevelComplete(int level, string mode)
    {
        Debug.Log("[Analytics] level_complete (level=" + level + ", mode=" + mode + ")");
        Service.LogEvent(
            "level_complete",
            new AnalyticsParameter("level", level),
            new AnalyticsParameter("mode", mode)
        );
    }

    public static void LogHintUsed(int level)
    {
        Debug.Log("[Analytics] hint_used (level=" + level + ")");
        Service.LogEvent(
            "hint_used",
            new AnalyticsParameter("level", level)
        );
    }

    // Forgets the backend before each play session, so a project with domain reload turned off never
    // carries one session's SDK state into the next.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForNewSession()
    {
        service = null;
    }
}
