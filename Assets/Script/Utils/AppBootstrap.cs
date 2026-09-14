using UnityEngine;

/// <summary>
/// Process-wide runtime settings that have to be applied before anything is drawn.
///
/// These are deliberately NOT on a MonoBehaviour in the scene. A component's Awake runs after the
/// first scene has already been loaded and laid out, and anything that forgets to add the component
/// to a new scene silently loses the setting. A RuntimeInitializeOnLoadMethod runs before the first
/// scene loads, needs no GameObject, and cannot be unwired by someone editing the hierarchy.
/// </summary>
public static class AppBootstrap
{
    // 60 rather than the display's own refresh rate: a 120Hz phone would otherwise render twice as
    // many frames for a board that only changes while a finger is down, and the battery cost of
    // that is not worth a difference no one can see on a puzzle grid.
    private const int TargetFrameRate = 60;

    /// <summary>
    /// Unity's default frame rate on mobile is 30fps, and nothing in this project ever overrode it.
    /// That is the single biggest reason the drag felt heavy on a device: at 30fps the path line is
    /// up to 33ms behind the finger that is drawing it, which reads as the line lagging rather than
    /// as a low frame rate -- and it looks identical in the Editor, which is not capped, so it
    /// could never be reproduced on a desktop.
    ///
    /// vSyncCount is cleared because it takes precedence over targetFrameRate wherever it is
    /// honoured: the quality level this project ships for mobile (Medium) sets vSyncCount to 1, and
    /// a non-zero vSyncCount makes targetFrameRate a no-op rather than an override.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyFrameRate()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = TargetFrameRate;
    }
}
