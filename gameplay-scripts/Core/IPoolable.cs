/// <summary>
/// Optional hook for objects pooled via ObjectPool&lt;T&gt; / DynamicPool&lt;T&gt;.
/// Pools call OnSpawnedFromPool right after activating an instance, and
/// OnReturnedToPool right before deactivating it. Implement this if a class
/// needs to reset internal state per spawn (timers, particle emitters, trails)
/// without forcing every caller to remember.
/// </summary>
public interface IPoolable
{
    void OnSpawnedFromPool();
    void OnReturnedToPool();
}
