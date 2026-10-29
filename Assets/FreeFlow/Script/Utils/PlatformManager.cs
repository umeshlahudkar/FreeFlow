using System;
using System.Collections;
using FreeFlow.Core.Services;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FreeFlow.Util
{
    /// <summary>
    /// The host the game runs inside -- the phone OS, or a web portal such as YouTube Playables --
    /// through this manager's <see cref="IPlatformService"/>, built from the build's ServiceConfig.
    /// The service only talks to the host; what the GAME does about it lives here:
    ///
    /// - Ready signals: first frame on startup, game ready once the main menu can be used.
    /// - Mute: the host's audio setting sits above every in-game control (it sets the listener's
    ///   volume; the music and sound sliders set their own sources').
    /// - Pause: freeze time, audio and input and save straight away; on resume undo it, with input
    ///   coming back a frame later so taps made while paused are not replayed.
    ///
    /// On Android and iOS the service never pauses or mutes and the signals go nowhere, so none of
    /// this changes how those builds behave.
    /// </summary>
    public static class PlatformManager
    {
        // gameReady must wait until nothing non-interactable is on screen. MainMenuPage pops its
        // cards in over ~0.5s (3 cards, 0.08s stagger, 0.3s each), so wait that out first.
        private const string MainSceneName = "MainScene";
        private const float GameReadyDelaySeconds = 0.6f;

        // This build's platform service, built once from the ServiceConfig the first time it is needed.
        private static IPlatformService service;
        private static bool initialized;
        private static bool gameReadySent;
        private static bool paused;
        private static float timeScaleBeforePause = 1f;

        private static IPlatformService Service
        {
            get
            {
                if (service == null) { service = ServiceConfig.Current.CreatePlatformService(); }
                return service;
            }
        }

        /// <summary>True between the host's pause and resume.</summary>
        public static bool IsPaused { get { return paused; } }

        /// <summary>Whether this host lets the game link to a page outside it (the privacy policy).</summary>
        public static bool CanOpenExternalLinks { get { return Service.CanOpenExternalLinks; } }

        /// <summary>Starts the platform connection -- one step of StartScene's GameBootstrap order
        /// (see ServicesInitializer) -- and tells the host the first frame is up. Calls
        /// <paramref name="onComplete"/> exactly once.</summary>
        public static void Initialize(Action onComplete)
        {
            Service.Initialize(() =>
            {
                if (!initialized)
                {
                    initialized = true;
                    Service.Paused += OnPaused;
                    Service.Resumed += OnResumed;
                    Service.AudioEnabledChanged += ApplyAudioEnabled;
                    SceneManager.sceneLoaded += OnSceneLoaded;
                    ApplyAudioEnabled(Service.IsAudioEnabled);
                    Service.SignalFirstFrameReady();
                }
                onComplete?.Invoke();
            });
        }

        private static void ApplyAudioEnabled(bool enabled)
        {
            AudioListener.volume = enabled ? 1f : 0f;
        }

        // ---- game ready ------------------------------------------------------------------------

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (gameReadySent || scene.name != MainSceneName) { return; }
            PlatformRunner.Instance.StartCoroutine(SignalGameReadyWhenInteractive());
        }

        private static IEnumerator SignalGameReadyWhenInteractive()
        {
            yield return new WaitForSecondsRealtime(GameReadyDelaySeconds);
            if (gameReadySent) { yield break; }
            gameReadySent = true;
            Service.SignalGameReady();
        }

        // ---- pause / resume --------------------------------------------------------------------

        private static void OnPaused()
        {
            if (paused) { return; }
            paused = true;

            timeScaleBeforePause = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;

            // Found rather than reached through Instance: the host can pause during startup, before
            // MainScene exists, and Instance would create a stray manager.
            FreeFlow.Input.InputManager input = UnityEngine.Object.FindAnyObjectByType<FreeFlow.Input.InputManager>();
            if (input != null) { input.DisableInput(); }

            // The host may never resume: this is the moment to save.
            ProfileManager profile = UnityEngine.Object.FindAnyObjectByType<ProfileManager>();
            if (profile != null) { profile.FlushSave(); }
        }

        private static void OnResumed()
        {
            if (!paused) { return; }
            paused = false;

            Time.timeScale = timeScaleBeforePause;
            AudioListener.pause = false;
            PlatformRunner.Instance.StartCoroutine(EnableInputNextFrame());
        }

        private static IEnumerator EnableInputNextFrame()
        {
            yield return null;
            if (paused) { yield break; }
            FreeFlow.Input.InputManager input = UnityEngine.Object.FindAnyObjectByType<FreeFlow.Input.InputManager>();
            if (input != null) { input.EnableInput(); }
        }

        // Forgets everything before each play session, so a project with domain reload turned off
        // never carries one session's state into the next.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession()
        {
            service = null;
            initialized = false;
            gameReadySent = false;
            paused = false;
            timeScaleBeforePause = 1f;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }
}
