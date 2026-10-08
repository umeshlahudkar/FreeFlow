using UnityEngine;
using YTGameSDK;

namespace FreeFlow.Platform.YouTube
{
    /// <summary>Finds Google's YTGameWrapper -- the scene object in StartScene, kept alive across
    /// scenes by the wrapper itself -- for the YouTube services, and reports errors through it.</summary>
    internal static class YouTubeSdk
    {
        private static YTGameWrapper wrapper;

        public static YTGameWrapper Wrapper
        {
            get
            {
                if (wrapper == null) { wrapper = Object.FindAnyObjectByType<YTGameWrapper>(); }
                return wrapper;
            }
        }

        /// <summary>Logs to the console and to YouTube's health reporting (best-effort, rate-limited
        /// on YouTube's side).</summary>
        public static void LogError(string message)
        {
            Debug.LogError("[YouTube] " + message);
            if (Wrapper != null) { Wrapper.SendYTGameError(message); }
        }
    }
}
