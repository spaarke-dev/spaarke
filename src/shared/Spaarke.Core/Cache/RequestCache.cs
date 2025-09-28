namespace Spaarke.Core.Cache;

/// <summary>
/// Per-request cache to collapse duplicate loads within a single HTTP request.
/// Registered as Scoped to ensure one instance per request.
/// </summary>
public sealed class RequestCache
{
    private readonly Dictionary<string, object> _cache = new();

    /// <summary>
    /// Gets a cached value by key, or null if not found.
    /// </summary>
    public T? Get<T>(string key) where T : class
    {
        return _cache.TryGetValue(key, out var value) ? value as T : null;
    }

    /// <summary>
    /// Sets a value in the cache.
    /// </summary>
    public void Set<T>(string key, T value) where T : class
    {
        _cache[key] = value;
    }

    /// <summary>
    /// Gets a cached value, or creates and caches it if not found.
    /// </summary>
    public T GetOrCreate<T>(string key, Func<T> factory) where T : class
    {
        if (_cache.TryGetValue(key, out var existing))
        {
            return (T)existing;
        }

        var value = factory();
        _cache[key] = value;
        return value;
    }

    /// <summary>
    /// Gets a cached value, or creates and caches it asynchronously if not found.
    /// </summary>
    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory) where T : class
    {
        if (_cache.TryGetValue(key, out var existing))
        {
            return (T)existing;
        }

        var value = await factory();
        _cache[key] = value;
        return value;
    }

    /// <summary>
    /// Gets a cached value, or creates and caches it asynchronously if not found.
    /// Supports cancellation token propagation.
    /// </summary>
    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default) where T : class
    {
        if (_cache.TryGetValue(key, out var existing))
        {
            return (T)existing;
        }

        var value = await factory(ct);
        _cache[key] = value;
        return value;
    }
}