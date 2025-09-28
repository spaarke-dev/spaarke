public sealed class DataverseAccessDataSource : IAccessDataSource
{
    private readonly IDistributedCache _cache;
    private readonly IRequestCache _req;
    private readonly IDataverseClient _dv;

    public DataverseAccessDataSource(IDistributedCache cache, IRequestCache req, IDataverseClient dv)
    { _cache = cache; _req = req; _dv = dv; }

    public async Task<UserAccessSnapshot> GetUserSnapshotAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var tenant = user.FindFirst("tid")?.Value ?? "default";
        var userId = /* resolve */ Guid.Empty;
        var ver = await _req.GetOrCreateAsync($"uacver:{tenant}:{userId}", () => _dv.GetUserUacVersionAsync(userId, ct), ct);
        var key = $"uac:{tenant}:{userId}:{ver}";
        return await _cache.GetOrCreateAsync(key, TimeSpan.FromMinutes(2), _ => _dv.GetUserAccessSnapshotAsync(userId, ct), ct);
    }
}