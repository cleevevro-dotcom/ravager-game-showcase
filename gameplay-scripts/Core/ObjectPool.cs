using System.Collections.Generic;
using UnityEngine;

public class ObjectPool<T> where T : MonoBehaviour
{
    readonly T _prefab;
    readonly Transform _parent;
    readonly Stack<T> _pool = new Stack<T>();

    public int Count => _pool.Count;

    public ObjectPool(T prefab, int initialSize, Transform parent = null)
    {
        _prefab = prefab;
        _parent = parent;
        for (int i = 0; i < initialSize; i++)
        {
            T obj = Object.Instantiate(prefab, parent);
            obj.gameObject.SetActive(false);
            _pool.Push(obj);
        }
    }

    public T Get(Vector3 position, Quaternion rotation)
    {
        T obj = null;
        while (obj == null && _pool.Count > 0)
            obj = _pool.Pop();

        if (obj == null)
        {
            obj = Object.Instantiate(_prefab, _parent);
            obj.gameObject.SetActive(false);
        }

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
