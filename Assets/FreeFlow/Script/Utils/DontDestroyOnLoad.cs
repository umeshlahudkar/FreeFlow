using UnityEngine;

namespace FreeFlow.Util
{
    /// <summary>Drop this on any root GameObject that must survive a scene load -- GameBootstrap
    /// itself, or a manager it initializes in StartScene before handing off to MainScene. Kept as
    /// its own component instead of being folded into GameBootstrap's Awake so which objects
    /// persist is a choice made once, in the Inspector, on each object that needs it -- not a list
    /// to keep in sync inside GameBootstrap's script as managers are added or removed.</summary>
    public class DontDestroyOnLoad : MonoBehaviour
    {
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }
    }
}
