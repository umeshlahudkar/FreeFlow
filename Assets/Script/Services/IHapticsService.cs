using FreeFlow.Enums;

namespace FreeFlow.Core.Services
{
    /// <summary>The device's vibration motor. Whether the player wants haptics is decided by
    /// Haptics (the Settings switch); a provider only plays the tap it is asked for.</summary>
    public interface IHapticsService
    {
        /// <summary>Whether this platform can vibrate at all. UI that offers the Vibration switch
        /// should hide itself when this is false.</summary>
        bool IsSupported { get; }

        void Play(HapticType type);
    }
}
