public interface IRequestCache
{
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, CancellationToken ct);
}

public sealed class RequestCache : IRequestCache
{
    private readonly IHttpContextAccessor _http;
    public RequestCache(IHttpContextAccessor http) => _http = http;

    public Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, CancellationToken ct)
    {
        var items = _http.HttpContext?.Items ?? throw new InvalidOperationException("No HttpContext");
        var dict = (ConcurrentDictionary<string, Lazy<Task<object?>>>) (items["__reqcache"] ??=
            new ConcurrentDictionary<string, Lazy<Task<object?>>>());
        var lazy = dict.GetOrAdd(key, _ => new Lazy<Task<object?>>(async () => await factory()));
        return lazy.Value.ContinueWith(t => (T)t.Result!, ct);
    }
}