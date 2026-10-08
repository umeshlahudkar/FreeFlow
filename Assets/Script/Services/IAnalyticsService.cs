using System;

namespace FreeFlow.Core.Services
{
    /// <summary>One named value attached to an analytics event.</summary>
    public readonly struct AnalyticsParameter
    {
        public readonly string Name;
        public readonly object Value;

        public AnalyticsParameter(string name, object value)
        {
            Name = name;
            Value = value;
        }
    }

    /// <summary>An analytics and error-reporting backend. The game's event names and parameters
    /// are decided by AnalyticsManager; a provider only records them.</summary>
    public interface IAnalyticsService
    {
        /// <summary>Starts the backend. Calls <paramref name="onComplete"/> exactly once.</summary>
        void Initialize(Action onComplete);

        void LogEvent(string eventName, params AnalyticsParameter[] parameters);

        /// <summary>Reports a non-fatal error. Backends that already capture errors on their own
        /// may treat this as a no-op.</summary>
        void LogError(string message);
    }
}
