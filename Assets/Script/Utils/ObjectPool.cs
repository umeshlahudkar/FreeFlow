using System.Collections.Generic;
using UnityEngine;

namespace FreeFlow.Util
{
    public class ObjectPool<T> where T : Component
    {
        private T prefab;
        private Transform parentTransform;
        private Queue<T> objectQueue = new Queue<T>();

        public ObjectPool(T prefab, int initialSize, Transform parentTransform = null)
        {
            this.prefab = prefab;
            this.parentTransform = parentTransform;

            InitializePool(initialSize);
        }

        private void InitializePool(int initialSize)
        {
            for (int i = 0; i < initialSize; i++)
            {
                T obj = CreateNewObject();
                objectQueue.Enqueue(obj);
            }
        }

        private T CreateNewObject()
        {
            T obj = Object.Instantiate(prefab, parentTransform);
            obj.gameObject.SetActive(false);
            return obj;
        }

        public T GetObject()
        {
            if (objectQueue.Count == 0)
            {
                T newObj = CreateNewObject();
                objectQueue.Enqueue(newObj);
            }

            T obj = objectQueue.Dequeue();
            obj.gameObject.SetActive(true);

            return obj;
        }

        public void ReturnObject(T obj)
        {
            obj.gameObject.SetActive(false);
            objectQueue.Enqueue(obj);
        }
    }
}