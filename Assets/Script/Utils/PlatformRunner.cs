using UnityEngine;

namespace FreeFlow.Util
{
    /// <summary>A hidden, scene-independent object PlatformManager runs its coroutines on (the
    /// game-ready delay, re-enabling input after a resume). PlatformManager is static and cannot run
    /// coroutines itself.</summary>
    public class PlatformRunner : MonoBehaviour
    {
        private static PlatformRunner instance;

        public static PlatformRunner Instance
        {
            get
            {
                if (instance == null)
                {
                    GameObject host = new GameObject("PlatformRunner");
                    host.hideFlags = HideFlags.HideInHierarchy;
                    DontDestroyOnLoad(host);
                    instance = host.AddComponent<PlatformRunner>();
                }
                return instance;
            }
        }
    }
}
