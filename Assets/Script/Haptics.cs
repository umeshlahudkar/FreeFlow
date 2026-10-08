using FreeFlow.Core.Services;
using FreeFlow.Enums;
using UnityEngine;

/// <summary>
/// The phone's vibration motor, behind the Settings screen's Vibration switch. This owns the
/// player's on/off preference; the vibration itself is this class's <see cref="IHapticsService"/>,
/// built from the build's ServiceConfig (native vibration on Android and iOS).
///
/// Static rather than a MonoBehaviour singleton: there is nothing to hold. It owns no scene object,
/// no coroutine and no state beyond the player's preference, and a haptic fires from gameplay code
/// that would otherwise have to find an instance first.
/// </summary>
public static class Haptics
{
    // The player's setting, cached. Read once and then kept in step by SettingPage -- ProfileManager
    // itself caches Settings.json in memory too, but this skips even that lookup on every dot picked up.
    private static bool? enabled;

    // This build's vibration motor, built once from the ServiceConfig the first time it is needed.
    private static IHapticsService service;

    private static IHapticsService Service
    {
        get
        {
            if (service == null) { service = ServiceConfig.Current.CreateHapticsService(); }
            return service;
        }
    }

    /// <summary>Whether this build has a vibration motor to drive. The Settings screen hides its
    /// Vibration row when it does not (the YouTube build).</summary>
    public static bool IsSupported { get { return Service.IsSupported; } }

    /// <summary>Whether the player wants haptics at all. Read from the save the first time it is
    /// asked for; kept current after that by <see cref="SetEnabled"/>.</summary>
    public static bool Enabled
    {
        get
        {
            if (!enabled.HasValue)
            {
                enabled = ProfileManager.Instance.LoadSettings().vibrationEnabled;
            }
            return enabled.Value;
        }
    }

    /// <summary>Called by the Settings screen when the switch moves, so the cached preference never
    /// disagrees with the save -- and so a haptic never has to re-read the file to find out.</summary>
    public static void SetEnabled(bool value)
    {
        enabled = value;
    }

    /// <summary>
    /// Asks for one tap. Does nothing when the player has haptics off; on a platform without a motor
    /// -- including the Editor -- the haptics service itself does nothing.
    /// </summary>
    public static void Play(HapticType type)
    {
        if (!Enabled) { return; }
        Service.Play(type);
    }

    // Forgets the cached preference and the service before each play session, so a project with
    // domain reload turned off never carries one session's state into the next.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForNewSession()
    {
        enabled = null;
        service = null;
    }
}
