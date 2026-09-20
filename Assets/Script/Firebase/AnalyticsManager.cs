using Firebase.Analytics;
using UnityEngine;

public class AnalyticsManager
{
    public static void LogEvent(string eventName)
    {
        Debug.Log("[Analytics] " + eventName);
        FirebaseAnalytics.LogEvent(eventName);
    }

    public static void LogLevelStart(int level, string mode)
    {
        Debug.Log("[Analytics] level_start (level=" + level + ", mode=" + mode + ")");
        FirebaseAnalytics.LogEvent(
            "level_start",
            new Parameter("level", level),
            new Parameter("mode", mode)
        );
    }

    public static void LogLevelComplete(int level, string mode)
    {
        Debug.Log("[Analytics] level_complete (level=" + level + ", mode=" + mode + ")");
        FirebaseAnalytics.LogEvent(
            "level_complete",
            new Parameter("level", level),
            new Parameter("mode", mode)
        );
    }

    public static void LogHintUsed(int level)
    {
        Debug.Log("[Analytics] hint_used (level=" + level + ")");
        FirebaseAnalytics.LogEvent(
            "hint_used",
            new Parameter("level", level)
        );
    }

    public static void LogDailyStreakComplete(int streak)
    {
        Debug.Log("[Analytics] daily_streak_complete (streak=" + streak + ")");
        FirebaseAnalytics.LogEvent(
            "daily_streak_complete",
            new Parameter("streak", streak)
        );
    }
}
