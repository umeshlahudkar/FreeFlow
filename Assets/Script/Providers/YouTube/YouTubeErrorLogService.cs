using System;
using FreeFlow.Core.Services;

namespace FreeFlow.Platform.YouTube
{
    /// <summary>The YouTube build's analytics: Playables allows no external calls, so there is no
    /// event tracking -- only errors, reported to YouTube's health page through Google's wrapper.</summary>
    public sealed class YouTubeErrorLogService : IAnalyticsService
    {
        public void Initialize(Action onComplete) { onComplete?.Invoke(); }

        public void LogEvent(string eventName, params AnalyticsParameter[] parameters) { }

        public void LogError(string message)
        {
            YouTubeSdk.LogError(message);
        }
    }
}
