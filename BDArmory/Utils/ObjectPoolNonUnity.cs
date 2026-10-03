using System.Collections.Generic;
using UnityEngine;

using BDArmory.Settings;

namespace BDArmory.Utils
{
    public class ObjectPoolEntry<T> where T : new()
    {
        public T value;
        public bool inUse = false; // Set this once you're done with the entry. Also, remember to set any reference fields in T to null.
        public ObjectPoolEntry() { value = new T(); }
    }

    public class ObjectPoolNonUnity<T> where T : new()
    {
        int lastIndex = 0;

        public List<ObjectPoolEntry<T>> pool = new List<ObjectPoolEntry<T>>();

        public ObjectPoolNonUnity(int size = 10)
        {
            if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.ObjectPoolNonUnity]: Creating object pool of size {size} for {typeof(T)}");
            AddObjectsToPool(size);
        }

        private void AddObjectsToPool(int count)
        {
            for (int i = 0; i < count; ++i)
            {
                pool.Add(new ObjectPoolEntry<T>());
            }
        }

        private void ReplacePoolObject(int index)
        {
            Debug.LogWarning($"[BDArmory.ObjectPoolNonUnity]: Object of type {typeof(T)} was null at position {index}, replacing it.");
            pool[index] = new ObjectPoolEntry<T>();
        }

        public ObjectPoolEntry<T> GetPooledObject()
        {
            // Start at the last index returned and cycle round for efficiency. This makes this a typically O(1) seek operation.
            for (int i = lastIndex + 1; i < pool.Count; ++i)
            {
                if (pool[i].value == null)
                {
                    ReplacePoolObject(i);
                }
                if (!pool[i].inUse)
                {
                    lastIndex = i;
                    pool[i].inUse = true;
                    return pool[i];
                }
            }
            for (int i = 0; i < lastIndex + 1; ++i)
            {
                if (pool[i].value == null)
                {
                    ReplacePoolObject(i);
                }
                if (!pool[i].inUse)
                {
                    lastIndex = i;
                    pool[i].inUse = true;
                    return pool[i];
                }
            }

            // The pool is full, increase it by 20%+1 and return the last entry.
            var size = (int)(pool.Count * 1.2) + 1;
            if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.ObjectPoolNonUnity]: Increasing pool size to {size} for {typeof(T)}");
            AddObjectsToPool(size - pool.Count);
            pool[pool.Count - 1].inUse = true;
            return pool[pool.Count - 1];
        }
    }
}
