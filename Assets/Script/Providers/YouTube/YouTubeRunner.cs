using System;
using System.Collections;
using UnityEngine;

namespace FreeFlow.Platform.YouTube
{
    /// <summary>A hidden, scene-independent object the YouTube services run their timers on (the
    /// cloud-load timeout, the save delay). The services are plain classes and cannot run coroutines
    /// themselves.</summary>
    public class YouTubeRunner : MonoBehaviour
    {
        private static YouTubeRunner instance;

        public static YouTubeRunner Instance
        {
            get
            {
                if (instance == null)
                {
                    GameObject host = new GameObject("YouTubeRunner");
                    host.hideFlags = HideFlags.HideInHierarchy;
                    DontDestroyOnLoad(host);
                    instance = host.AddComponent<YouTubeRunner>();
                }
                return instance;
            }
        }

        /// <summary>Runs <paramref name="action"/> after <paramref name="seconds"/> of real time.</summary>
        public void RunAfterRealtime(float seconds, Action action)
        {
            StartCoroutine(RunAfter(seconds, action));
        }

        private static IEnumerator RunAfter(float seconds, Action action)
        {
            yield return new WaitForSecondsRealtime(seconds);
            action?.Invoke();
        }
    }
}
