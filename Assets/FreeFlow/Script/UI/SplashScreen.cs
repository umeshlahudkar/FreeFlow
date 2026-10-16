using UnityEngine;

namespace FreeFlow.UI
{
    /// <summary>
    /// StartScene's splash: the PlayEasy logo fades in over the game background, holds, fades
    /// out, and then -- only if MainScene is still not ready to show -- a spinning loader takes
    /// its place until it is. The background matches MainScene's, so the switch to the menu
    /// does not flash.
    ///
    /// The splash never loads anything itself. GameBootstrap loads MainScene in the background
    /// while the logo is up, tells this when the load is ready (<see cref="SceneReady"/>), and
    /// holds the switch until <see cref="LogoFinished"/>, so the logo is never cut short and the
    /// loader only appears when there is real waiting left to cover.
    ///
    /// Timed by hand rather than with tweens, on unscaled time with each frame's step capped:
    /// the first frames after launch can take most of a second each, and a tween fed that delta
    /// would skip the fade-in entirely. Unscaled because the host (YouTube Playables) can pause
    /// the game during startup, which sets timeScale to 0.
    /// </summary>
    public class SplashScreen : MonoBehaviour
    {
        // The most time one frame may advance the splash: a slow frame slows it down rather than
        // skipping part of it.
        private const float MaxFrameStepSeconds = 1f / 20f;

        [Header("Logo")]
        [SerializeField] private CanvasGroup logoGroup;
        [SerializeField] private float logoFadeInSeconds = 0.35f;
        [SerializeField] private float logoHoldSeconds = 1.2f;
        [SerializeField] private float logoFadeOutSeconds = 0.25f;

        [Header("Loader")]
        [SerializeField] private CanvasGroup loaderGroup;
        [SerializeField] private RectTransform loaderSpinner;
        [Tooltip("Degrees per second, clockwise.")]
        [SerializeField] private float spinDegreesPerSecond = 360f;
        // A load that finishes just after the logo fades would otherwise flash the loader for a
        // frame or two before the scene switches.
        [SerializeField] private float loaderDelaySeconds = 0.15f;
        [SerializeField] private float loaderFadeInSeconds = 0.2f;

        private float elapsed;
        private bool loaderShown;

        /// <summary>True once the logo has faded out; MainScene may be shown from then on.</summary>
        public bool LogoFinished { get; private set; }

        /// <summary>Set by GameBootstrap once MainScene has loaded and only waits to be shown.</summary>
        public bool SceneReady { get; set; }

        private void Awake()
        {
            logoGroup.alpha = 0f;
            loaderGroup.alpha = 0f;
        }

        private void Update()
        {
            float step = Mathf.Min(Time.unscaledDeltaTime, MaxFrameStepSeconds);
            elapsed += step;

            float fadeOutAt = logoFadeInSeconds + logoHoldSeconds;
            float logoEndAt = fadeOutAt + logoFadeOutSeconds;
            if (elapsed < logoFadeInSeconds)
            {
                logoGroup.alpha = Mathf.SmoothStep(0f, 1f, elapsed / logoFadeInSeconds);
            }
            else if (elapsed < fadeOutAt)
            {
                logoGroup.alpha = 1f;
            }
            else if (elapsed < logoEndAt)
            {
                logoGroup.alpha = Mathf.SmoothStep(1f, 0f, (elapsed - fadeOutAt) / logoFadeOutSeconds);
            }
            else
            {
                logoGroup.alpha = 0f;
                LogoFinished = true;
            }

            // Once shown, the loader stays until the scene switch takes it away.
            if (!loaderShown && LogoFinished && !SceneReady && elapsed >= logoEndAt + loaderDelaySeconds)
            {
                loaderShown = true;
            }
            if (loaderShown)
            {
                loaderGroup.alpha = Mathf.Min(1f, loaderGroup.alpha + step / loaderFadeInSeconds);
                loaderSpinner.Rotate(0f, 0f, -spinDegreesPerSecond * step);
            }
        }
    }
}
