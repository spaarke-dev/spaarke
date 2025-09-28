public static class DistributedCacheExtensions
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static async Task<T?> GetAsync<T>(this IDistributedCache cache, string key, CancellationToken ct)
    {
        var bytes = await cache.GetAsync(key, ct);
        return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes, JsonOpts);
    }

    public static async Task SetAsync<T>(this IDistributedCache cache, string key, T value, TimeSpan ttl, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value!, JsonOpts);
        var opts = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl };
        await cache.SetAsync(key, bytes, opts, ct);
    }

    public static async Task<T> GetOrCreateAsync<T>(this IDistributedCache cache, string key, TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory, CancellationToken ct)
    {
        var cached = await cache.GetAsync<T>(key, ct);
        if (cached is not null) return cached;
        var result = await factory(ct);
        await cache.SetAsync(key, result!, ttl, ct);
        return result!;
    }
}