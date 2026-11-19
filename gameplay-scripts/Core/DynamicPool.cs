using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight pool for MonoBehaviour instances built via a factory function (no prefab needed).
/// </summary>
public class DynamicPool<T> where T : Component
{
    readonly Func<T>  _factory;
    readonly Stack<T> _pool = new Stack<T>();

    public int Count => _pool.Count;

    public DynamicPool(Func<T> factory, int initialSize = 0)
    {
        _factory = factory;
        for (int i = 0; i < initialSize; i++)
        {
            T obj = _factory();
            if (obj == null) continue;
            obj.gameObject.SetActive(false);
            _pool.Push(obj);
        }
    }

    public T Get(Vector3 position, Quaternion rotation)
    {
        T obj = null;
        while (obj == null && _pool.Count > 0)
            obj = _pool.Pop();

        if (obj == null) obj = _factory();
        if (obj == null) return null;

        obj.transform.SetPositionAndRotation(position, rotation);
        obj.gameObject.SetActive(true);
        if (obj is IPoolable poolable) poolable.OnSpawnedFromPool();
        return obj;
    }

    public void Return(T obj)
    {
        if (obj == null) return;
        if (obj is IPoolable poolable) poolable.OnReturnedToPool();
        obj.gameObject.SetActive(false);
        _pool.Push(obj);
    }
}
