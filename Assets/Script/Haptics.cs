using System.Runtime.InteropServices;
using UnityEngine;
using FreeFlow.Enums;

/// <summary>
/// The phone's vibration motor, behind the Settings screen's Vibration switch.
///
/// Static rather than a MonoBehaviour singleton: there is nothing to hold. It owns no scene object,
/// no coroutine and no state beyond the player's preference, and a haptic fires from gameplay code
/// that would otherwise have to find an instance first.
///
/// <see cref="Handheld.Vibrate"/> is deliberately not used. It is one fixed buzz -- about half a
/// second on Android, the old "you have a message" rattle -- which is far too heavy for a dot being
/// picked up, and offers no way to tell a confirmation apart from a refusal. Each platform's own
/// short-haptic API is used instead: VibrationEffect on Android, UIFeedbackGenerator on iOS.
/// </summary>
public static class Haptics
{
    // The player's setting, cached. Read once and then kept in step by SettingPage, because the
    // alternative is SettingsSystem.Load() -- a file read and a JSON parse -- on every dot picked up.
    private static bool? enabled;

#if UNITY_ANDROID && !UNITY_EDITOR
    private static AndroidJavaObject vibrator;
    private static AndroidJavaClass effectClass;
    private static int apiLevel;
    private static bool androidReady;
#endif

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void _freeflowHaptic(int type);
#endif

    /// <summary>Whether the player wants haptics at all. Read from the save the first time it is
    /// asked for; kept current after that by <see cref="SetEnabled"/>.</summary>
    public static bool Enabled
    {
        get
        {
            if (!enabled.HasValue)
            {
                enabled = SettingsSystem.Instance != null && SettingsSystem.Instance.Load().vibrationEnabled;
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
    /// Asks the phone for one tap. Does nothing when the player has haptics off, and nothing on a
    /// platform without a motor -- including the Editor, where there is no motor to ask and no
    /// warning worth logging for a tap the developer was never going to feel.
    /// </summary>
    public static void Play(HapticType type)
    {
        if (!Enabled) { return; }

#if UNITY_ANDROID && !UNITY_EDITOR
        PlayAndroid(type);
#elif UNITY_IOS && !UNITY_EDITOR
        _freeflowHaptic((int)type);
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// Every tap goes through VibrationEffect.createWaveform -- including Selection/Light/Medium,
    /// which used to be a single pulse (first via createOneShot(ms, amplitude), then via a
    /// one-entry waveform) and produced nothing on the device this was tested on, no exception and
    /// no vibration. Success and Warning, the only two that were ever felt, are also the only two
    /// with more than one pulse, so every tap here is a real on/off/on pattern now.
    ///
    /// This device also never honoured an explicit amplitude (createOneShot's own amplitude
    /// parameter did nothing either), and createWaveform's 2-arg overload used below has no
    /// amplitude parameter at all -- every pulse runs at the platform default. With amplitude off
    /// the table, pulse length is the only dial left for how strong a tap reads, which is why
    /// Selection/Light sit close to Success's own 18-34ms pulse widths rather than the much
    /// shorter ones tried first.
    /// </summary>
    private static void PlayAndroid(HapticType type)
    {
        if (!EnsureAndroid()) { return; }

        // VibrationEffect arrived in API 26. Below that the only option is the flat-out buzz, and a
        // constant heavy rattle is worse than no haptics -- so old devices simply get none.
        if (apiLevel < 26) { return; }

        try
        {
            switch (type)
            {
                case HapticType.Success:
                    VibratePattern(new long[] { 0, 18, 70, 34 });
                    break;
                case HapticType.Warning:
                    VibratePattern(new long[] { 0, 26, 50, 26 });
                    break;
                case HapticType.Medium:
                    VibratePattern(new long[] { 0, 25, 50, 35 });
                    break;
                case HapticType.Light:
                    VibratePattern(new long[] { 0, 20, 45, 30 });
                    break;
                default:
                    VibratePattern(new long[] { 0, 18, 40, 18 });
                    break;
            }
        }
        catch (AndroidJavaException e)
        {
            // A device that refuses the call is not a reason to take the game down with it.
            androidReady = false;
            Debug.LogWarning("Haptics: the device refused a vibration (" + e.Message + ") -- haptics off for this session.");
        }
    }

    /// <summary>A pattern of off/on pairs in milliseconds. -1 means "do not repeat" -- without it
    /// the pattern loops until something stops it, which on a phone in a pocket is memorable for
    /// the wrong reasons.</summary>
    private static void VibratePattern(long[] pattern)
    {
        using (AndroidJavaObject effect = effectClass.CallStatic<AndroidJavaObject>(
            "createWaveform", pattern, -1))
        {
            vibrator.Call("vibrate", effect);
        }
    }

    /// <summary>Resolves the system vibrator once. Everything here is a JNI lookup, which is far
    /// too expensive to repeat per tap.</summary>
    private static bool EnsureAndroid()
    {
        if (androidReady) { return true; }

        try
        {
            using (AndroidJavaClass version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                apiLevel = version.GetStatic<int>("SDK_INT");
            }

            using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }

            // Deliberately not cached as a permanent "never try again": a device can genuinely
            // report hasVibrator() false in a way that isn't going to change, but treating it as
            // permanent used to mean vibrator staying non-null from this lookup would make every
            // later call here return false without ever re-resolving, even after a transient
            // failure. Simplest fix is to just not remember a failed lookup at all.
            if (vibrator == null || !vibrator.Call<bool>("hasVibrator")) { return false; }

            effectClass = new AndroidJavaClass("android.os.VibrationEffect");
            androidReady = true;
            return true;
        }
        catch (AndroidJavaException e)
        {
            Debug.LogWarning("Haptics: no vibrator available (" + e.Message + ").");
            return false;
        }
    }
#endif
}
