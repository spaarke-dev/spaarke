# Request Cache Pattern

> **Domain**: Caching / Per-Request Memoization
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-009

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/shared/Spaarke.Core/Cache/RequestCache.cs` | Request-scoped cache |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SpaarkeCore.cs` | DI registration |

---

## Purpose

Collapse duplicate data loads within a single HTTP request. Prevents redundant database/API calls when the same data is accessed multiple times during request processing.

---

## Implementation

```csharp
public sealed class RequestCache
{
    private readonly Dictionary<string, object> _cache = new();

    public T GetOrCreate<T>(string key, Func<T> factory) where T : class
    {
        if (_cache.TryGetValue(key, out var cached))
            return (T)cached;

        var value = factory();
        _cache[key] = value;
        return value;
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory) where T : class
    {
        if (_cache.TryGetValue(key, out var cached))
            return (T)cached;

        var value = await factory();
        _cache[key] = value;
        return value;
    }
}
```

---

## Registration

```csharp
// Register as Scoped - new instance per HTTP request
services.AddScoped<RequestCache>();
```

---

## Usage Example

```csharp
public class AuthorizationService
{
    private readonly RequestCache _requestCache;
    private readonly IAccessDataSource _dataSource;

    public async Task<UacSnapshot> GetSnapshotAsync(string userId, CancellationToken ct)
    {
        // Same snapshot reused throughout the request
        return await _requestCache.GetOrCreateAsync(
            $"uac:{userId}",
            () => _dataSource.GetUserAccessAsync(userId, ct)
        );
    }
}
```

---

## When to Use

| Scenario | Use Request Cache |
|----------|-------------------|
| Same user data needed multiple times | ✅ Yes |
| Authorization checks on multiple resources | ✅ Yes |
| Data shared across middleware and endpoint | ✅ Yes |
| Data that changes during request | ❌ No |
| Data shared across requests | ❌ No (use distributed) |

---

## Key Points

1. **Scoped lifetime** - New instance per HTTP request
2. **Automatic cleanup** - Disposed with request scope
3. **No TTL needed** - Lives only for request duration
4. **Thread-safe** - Single request context
5. **String keys** - Use consistent key format

---

## Related Patterns

- [Distributed Cache](distributed-cache.md) - Cross-request caching

---

**Lines**: ~80
