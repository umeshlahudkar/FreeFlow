using System;

namespace FreeFlow.Core.Services
{
    /// <summary>Where ProfileManager's save slots (progress and settings) are kept. The game only
    /// ever talks to ProfileManager, so swapping the backend per platform changes nothing above it.
    ///
    /// <see cref="Load"/> is the one asynchronous step: a backend that has to fetch its data does it
    /// there, and ProfileManager.Initialize waits for it. Reads and writes after that are synchronous
    /// against whatever the backend holds.</summary>
    public interface ISaveStorage
    {
        /// <summary>Makes the slots readable. Calls <paramref name="onComplete"/> exactly once.</summary>
        void Load(Action onComplete);

        bool TryRead(string slot, out string json);
        void Write(string slot, string json);
        void Delete(string slot);

        /// <summary>Persists anything still pending right now -- called when the host pauses the game,
        /// which may be the last chance to save. Backends that write on every change do nothing.</summary>
        void Flush();
    }
}
