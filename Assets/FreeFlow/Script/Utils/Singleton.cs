using UnityEngine;

namespace FreeFlow.Util
{
    public class Singleton<T> : MonoBehaviour where T : Component
    {
        private static T instance;
        public static T Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindAnyObjectByType<T>();

                    if (instance == null)
                    {
                        GameObject obj = new GameObject("Controller");
                        instance = obj.AddComponent<T>();
                    }
                }
                return instance;
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                DestroyImmediate(gameObject);
            }
            else
            {
                instance = this as T;
            }
        }
    }
}
