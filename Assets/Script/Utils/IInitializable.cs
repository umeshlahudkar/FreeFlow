using System;

namespace FreeFlow.Util
{
    /// <summary>A manager with an explicit, possibly asynchronous startup step before the rest of
    /// the game can rely on it -- bringing up a third-party SDK, in practice (ads, analytics,
    /// auth, ...). Implemented only by managers registered with <see cref="GameBootstrap"/>;
    /// everything else in this project is ready the moment its own Awake runs and has no reason
    /// to implement it.</summary>
    public interface IInitializable
    {
        /// <summary>Starts this manager's own startup work and calls <paramref name="onComplete"/>
        /// exactly once, whether that work is actually asynchronous (an SDK's own init callback)
        /// or finishes within the call (nothing to do this session). GameBootstrap waits on that
        /// call before moving on to the next manager in its list, so a manager that never calls it
        /// back stalls every manager after it.</summary>
        void Initialize(Action onComplete);
    }
}
