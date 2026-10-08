using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FreeFlow.Util
{
    /// <summary>Runs every registered manager's startup step in a fixed order before anything
    /// else in the game touches them. Managers with an SDK to bring up (ads, analytics, auth, and
    /// whatever else joins them) do it here instead of racing each other from their own Awake --
    /// one manager's Initialize is free to depend on another having already finished (analytics
    /// tagging events with the id auth just signed in with, for instance) only because this
    /// decides the order, not whichever Awake happens to run first.
    ///
    /// Managers themselves stay Singleton&lt;T&gt; and still lazily self-create on first Instance
    /// access, exactly as before -- this only sequences the ones that opt in by implementing
    /// IInitializable and appearing in initializationOrder. A manager that does neither is simply
    /// not this class's concern.
    ///
    /// Lives in its own StartScene: a scene with nothing to show yet is exactly where SDK
    /// startup belongs, rather than blocking or half-running underneath MainScene's own UI and
    /// gameplay while it waits on the same managers.</summary>
    public class GameBootstrap : Singleton<GameBootstrap>
    {
        [Tooltip("Initialized in this order, top to bottom. Each entry must implement IInitializable.")]
        [SerializeField] private MonoBehaviour[] initializationOrder;

        [Tooltip("Loaded once every entry above has finished initializing.")]
        [SerializeField] private string mainSceneName = "MainScene";

        /// <summary>Whether every entry in <see cref="initializationOrder"/> has finished. False
        /// for the entire lifetime of a build that never wires this component into a scene.</summary>
        public bool IsReady { get; private set; }

        /// <summary>Fires once, after the last manager in <see cref="initializationOrder"/> calls
        /// its Initialize back. Subscribing after it has already fired does not replay it -- check
        /// <see cref="IsReady"/> first for that case.</summary>
        public event Action OnReady;

        private void Start()
        {
            StartCoroutine(InitializeInOrder());
        }

        private IEnumerator InitializeInOrder()
        {
            foreach (MonoBehaviour step in initializationOrder)
            {
                if (step is not IInitializable initializable)
                {
                    Debug.LogWarning("GameBootstrap: " +
                        (step == null ? "a null entry" : step.name + " does not implement IInitializable") +
                        " -- skipped.");
                    continue;
                }

                bool done = false;
                initializable.Initialize(() => done = true);
                yield return new WaitUntil(() => done);
            }

            IsReady = true;
            OnReady?.Invoke();
            SceneManager.LoadScene(mainSceneName);
        }
    }
}
