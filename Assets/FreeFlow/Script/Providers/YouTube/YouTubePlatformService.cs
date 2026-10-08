using System;
using FreeFlow.Core.Services;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace FreeFlow.Platform.YouTube
{
    /// <summary>
    /// YouTube Playables as the host, through Google's YTGameWrapper: the first-frame and game-ready
    /// signals, YouTube's mute, and pause/resume. What the game does about a pause or a mute is
    /// PlatformManager's business; this only reports them.
    ///
    /// On pause, PlatformManager's handlers run first (freeze, save), then Unity's frame loop is
    /// stopped -- Google's wrapper cannot do that part, and Playables requires all execution,
    /// rendering included, to stop. On resume the loop restarts before the handlers run.
    /// </summary>
    public sealed class YouTubePlatformService : IPlatformService
    {
        public event Action<bool> AudioEnabledChanged;
        public event Action Paused;
        public event Action Resumed;

        public void Initialize(Action onComplete)
        {
            if (YouTubeSdk.Wrapper == null)
            {
                YouTubeSdk.LogError("No YTGameWrapper object found; YouTube pause, mute and ready signals are off.");
            }
            else
            {
                YouTubeSdk.Wrapper.SetOnPauseCallback(OnPause);
                YouTubeSdk.Wrapper.SetOnResumeCallback(OnResume);
                YouTubeSdk.Wrapper.SetOnAudioEnabledChangeCallback(OnAudioEnabledChange);
            }
            onComplete?.Invoke();
        }

        public void SignalFirstFrameReady()
        {
            if (YouTubeSdk.Wrapper != null) { YouTubeSdk.Wrapper.SendGameFirstFrameReady(); }
        }

        public void SignalGameReady()
        {
            if (YouTubeSdk.Wrapper != null) { YouTubeSdk.Wrapper.SendGameIsReady(); }
        }

        public bool IsAudioEnabled
        {
            get { return YouTubeSdk.Wrapper == null || YouTubeSdk.Wrapper.IsYTGameAudioEnabled(); }
        }

        /// <summary>Never: Playables certification forbids any way off the page.</summary>
        public bool CanOpenExternalLinks { get { return false; } }

        private void OnPause()
        {
            Paused?.Invoke();
#if UNITY_WEBGL && !UNITY_EDITOR
            FreeFlowPauseMainLoop();
#endif
        }

        private void OnResume()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            FreeFlowResumeMainLoop();
#endif
            Resumed?.Invoke();
        }

        private void OnAudioEnabledChange(bool isAudioEnabled)
        {
            AudioEnabledChanged?.Invoke(isAudioEnabled);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void FreeFlowPauseMainLoop();
        [DllImport("__Internal")] private static extern void FreeFlowResumeMainLoop();
#endif
    }
}
