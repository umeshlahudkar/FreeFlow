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
    // alternative is SavingSystem.Load() -- a file read and a JSON parse -- on every dot picked up.
    private static bool? enabled;

#if UNITY_ANDROID && !UNITY_EDITOR
    // How long each tap runs and how hard, in milliseconds and 1-255 amplitude. Short: a haptic
    // that outlasts the touch that caused it reads as a fault rather than as feedback.
    private const int SelectionMs = 8;
    private const int LightMs = 14;
    private const int MediumMs = 24;

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
                enabled = SavingSystem.Instance != null && SavingSystem.Instance.Load().vibrationEnabled;
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
    /// One-shots through VibrationEffect, which is what gives a duration AND an amplitude -- the
    /// older vibrate(long) runs the motor flat out and cannot produce a light tick.
    ///
    /// Patterns are used for Success and Warning: two pulses with a gap reads as an event rather
    /// than as a longer version of the same tap, which is the whole point of those two being
    /// distinguishable by feel.
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
                    VibrateOnce(MediumMs, 180);
                    break;
                case HapticType.Light:
                    VibrateOnce(LightMs, 140);
                    break;
                default:
                    VibrateOnce(SelectionMs, 90);
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

    private static void VibrateOnce(int milliseconds, int amplitude)
    {
        using (AndroidJavaObject effect = effectClass.CallStatic<AndroidJavaObject>(
            "createOneShot", (long)milliseconds, amplitude))
        {
            vibrator.Call("vibrate", effect);
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
        if (vibrator != null) { return false; }   // resolved once already and failed

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
