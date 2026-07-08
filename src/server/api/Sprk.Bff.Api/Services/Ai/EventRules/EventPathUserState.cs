using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.Cache;

namespace Sprk.Bff.Api.Services.Ai.EventRules;

/// <summary>
/// Per-user Event-path state: the daily execution budget counter (NFR-09 /
/// ADR-016) and the per-user opt-out marker (FR-P1-03 bound b). Module boundary
/// for <see cref="EventRulesService"/> tests per ADR-038.
/// </summary>
public interface IEventPathUserState
{
    /// <summary>True when the user has opted out of automatic Event-path runs.</summary>
    Task<bool> IsOptedOutAsync(string tenantId, string userOid, CancellationToken cancellationToken);

    /// <summary>Sets (or clears) the user's Event-path opt-out.</summary>
    Task SetOptOutAsync(string tenantId, string userOid, bool optedOut, CancellationToken cancellationToken);

    /// <summary>Capability executions the user has consumed today (UTC day) on the Event path.</summary>
    Task<int> GetTodayExecutionCountAsync(string tenantId, string userOid, CancellationToken cancellationToken);

    /// <summary>Adds <paramref name="count"/> executions to the user's UTC-day counter.</summary>
    Task AddExecutionsAsync(string tenantId, string userOid, int count, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IEventPathUserState"/> backed by <see cref="ITenantCache"/> (Redis,
/// ADR-009/ADR-014 tenant-scoped keys).
/// </summary>
/// <remarks>
/// <para>
/// <b>Keys</b> (final on-wire per ITenantCache convention):
/// <c>spaarke:tenant:{tenantId}:event-optout:{userOid}:v1</c> (TTL
/// <see cref="EventRulesOptions.OptOutTtlDays"/>) and
/// <c>spaarke:tenant:{tenantId}:event-budget:{userOid}:{yyyyMMdd}:v1</c> (TTL 48h —
/// the counter only matters for its UTC day; 48h absorbs clock skew).
/// </para>
/// <para>
/// <b>Documented decisions (task 022)</b>:
/// <list type="bullet">
///   <item><b>Opt-out durability</b>: Redis-backed with a long TTL at P1. A Redis
///   flush re-enables auto-runs for opted-out users (fail-open toward the product
///   default). A durable Dataverse per-user settings column is the P3+ upgrade
///   path if the trade-off proves wrong — deliberately NOT built now (CLAUDE.md
///   §11: no concrete failing contract requires durability today).</item>
///   <item><b>Non-atomic counter</b>: read-modify-write without a lock. The budget
///   is a soft cost guard (NFR-09), not an exact quota — a lost increment under
///   concurrent uploads under-counts by at most the concurrency degree. An atomic
///   INCR would require widening the ITenantCache contract for one consumer.</item>
///   <item><b>UTC day boundary</b> via <see cref="TimeProvider"/> (testable per
///   TEST-ARCHITECTURE TimeProvider rule).</item>
/// </list>
/// </para>
/// </remarks>
public sealed class EventPathUserState : IEventPathUserState
{
    internal const string OptOutResource = "event-optout";
    internal const string BudgetResource = "event-budget";
    internal const int CacheVersion = 1;
    private static readonly TimeSpan BudgetTtl = TimeSpan.FromHours(48);

    private readonly ITenantCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<EventRulesOptions> _options;

    public EventPathUserState(
        ITenantCache cache,
        IOptions<EventRulesOptions> options,
        TimeProvider? timeProvider = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<bool> IsOptedOutAsync(string tenantId, string userOid, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userOid);
        return await _cache
            .GetAsync<bool?>(tenantId, OptOutResource, userOid, CacheVersion, ct: cancellationToken)
            .ConfigureAwait(false) == true;
    }

    /// <inheritdoc />
    public Task SetOptOutAsync(string tenantId, string userOid, bool optedOut, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userOid);

        // Opted back in → remove the marker (absence == default-in). Opted out → long-TTL marker.
        return optedOut
            ? _cache.SetAsync(
                tenantId, OptOutResource, userOid, CacheVersion,
                value: (bool?)true,
                ttl: TimeSpan.FromDays(Math.Max(1, _options.Value.OptOutTtlDays)),
                ct: cancellationToken)
            : _cache.RemoveAsync(tenantId, OptOutResource, userOid, CacheVersion, ct: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> GetTodayExecutionCountAsync(string tenantId, string userOid, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userOid);
        return await _cache
            .GetAsync<int?>(tenantId, BudgetResource, BudgetId(userOid), CacheVersion, ct: cancellationToken)
            .ConfigureAwait(false) ?? 0;
    }

    /// <inheritdoc />
    public async Task AddExecutionsAsync(string tenantId, string userOid, int count, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userOid);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var id = BudgetId(userOid);
        var current = await _cache
            .GetAsync<int?>(tenantId, BudgetResource, id, CacheVersion, ct: cancellationToken)
            .ConfigureAwait(false) ?? 0;
        await _cache
            .SetAsync(tenantId, BudgetResource, id, CacheVersion, (int?)(current + count), BudgetTtl, ct: cancellationToken)
            .ConfigureAwait(false);
    }

    private string BudgetId(string userOid)
        => $"{userOid}:{_timeProvider.GetUtcNow():yyyyMMdd}";
}
