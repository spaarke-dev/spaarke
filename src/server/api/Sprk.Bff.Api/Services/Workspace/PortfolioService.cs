using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Api.Workspace.Contracts;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Identity;

namespace Sprk.Bff.Api.Services.Workspace;

/// <summary>
/// Response DTO for portfolio aggregation.
/// </summary>
/// <param name="TotalSpend">Sum of all invoiced amounts across active matters.</param>
/// <param name="TotalBudget">Sum of all budget amounts across active matters.</param>
/// <param name="UtilizationPercent">TotalSpend / TotalBudget expressed as a percentage (0 when TotalBudget is 0).</param>
/// <param name="MattersAtRisk">Count of matters with at least one overdue open task, or utilization above 85%.</param>
/// <param name="OverdueEvents">Total count of overdue open tasks (<c>sprk_event</c>, type Task) across all active matters.</param>
/// <param name="ActiveMatters">Count of matters with an active/open status.</param>
/// <param name="CachedAt">Timestamp when this data was generated and cached.</param>
public record PortfolioSummaryResponse(
    decimal TotalSpend,
    decimal TotalBudget,
    decimal UtilizationPercent,
    int MattersAtRisk,
    int OverdueEvents,
    int ActiveMatters,
    DateTimeOffset CachedAt);

/// <summary>
/// Internal model representing an active matter the user's portfolio is built from (read as the caller).
/// </summary>
internal sealed class MatterRecord
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal InvoicedAmount { get; init; }
    public decimal BudgetAmount { get; init; }
    public int OverdueEventCount { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>
/// The active matters FOR a user, read under the user's Dataverse security — or the fact that they could not be
/// determined. <see cref="Unavailable"/> is never an empty list in disguise.
/// </summary>
internal sealed record PortfolioMatterRead(IReadOnlyList<MatterRecord> Matters, bool Unavailable)
{
    public static PortfolioMatterRead None { get; } = new(Array.Empty<MatterRecord>(), false);
    public static PortfolioMatterRead UnavailableRead { get; } = new(Array.Empty<MatterRecord>(), true);
}

/// <summary>
/// Aggregates portfolio data for the Legal Operations Workspace.
/// Selects the matters FOR the user, reads them as the user, and caches the aggregate in Redis.
/// </summary>
/// <remarks>
/// Follows ADR-009: Redis-first caching with a 5-minute TTL.
/// Cache key pattern: "workspace:{userId}:portfolio"
///
/// At-risk definition:
/// - Matter has at least one overdue open task, OR
/// - Matter's individual utilization (InvoicedAmount / BudgetAmount) > 85%
///
/// <para>
/// <b>Which matters (unified-access-control-r2 task 152, verifier round 1 item 5; ADR-034 Amendment A3).</b> The
/// portfolio is the matters FOR the user — the people-targeting surface (<see cref="MembershipResolveOptions.People"/>:
/// human Created By, personal ownership, "Assigned *" through the linked contact) — read to completion
/// (<see cref="PeopleTargetedSet"/>). Before task 152 it filtered app-only on <c>ownerid</c> = the caller: an ad-hoc
/// owner condition on an attention surface (the A1/D5 anti-pattern ADR-034 forbids), which also counted no team-owned
/// matter at all, and it selected <c>sprk_name</c>, <c>sprk_totalspend</c> and <c>sprk_overdueeventcount</c> — none of
/// which exist on <c>sprk_matter</c> (verified live, read-only, 2026-10-02) — so the query failed and every metric was
/// zero.
/// </para>
/// <para>
/// <b>What the user may see.</b> The detail rows and the overdue-task counts are read AS THE USER through the existing
/// <see cref="IImpersonatedCommunicationQuery"/> seam (MSCRMCallerID), chunked, so Dataverse drops any matter the user
/// cannot open. There is no app-only read and no app-only fallback.
/// </para>
/// </remarks>
public class PortfolioService
{
    private readonly IDistributedCache _cache;
    private readonly IMembershipResolverService _membershipResolver;
    private readonly IImpersonatedCommunicationQuery _callerQuery;
    // Translates the caller's Entra oid into the Dataverse systemuserid the people surface and the caller-context
    // reads take. Reused (already a registered singleton) rather than re-implemented — root CLAUDE.md section 11.
    private readonly ISystemUserIdentityResolver _systemUserIdentityResolver;
    private readonly ILogger<PortfolioService> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The most candidate ids bound into ONE caller-context GET (task 152) — keeps each query near 4 KB, far under the
    /// Web API URL limit. A failed chunk fails the whole read (it never shrinks the candidate set).
    /// </summary>
    internal const int MaxIdsPerImpersonatedRequest = 50;

    /// <summary>sprk_event type id for Task — same constant the Daily Briefing collector and TaskActionCore use.</summary>
    private const string EventTypeTask = "124f5fc9-98ff-f011-8406-7c1e525abd8b";

    /// <summary>The Dataverse logical name of the portfolio's entity (ADR-034 canonical name).</summary>
    private const string MatterEntityLogicalName = "sprk_matter";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Initializes a new instance of <see cref="PortfolioService"/>.
    /// </summary>
    /// <param name="cache">Distributed cache (Redis) for portfolio data.</param>
    /// <param name="membershipResolver">
    /// Canonical user-record membership resolver (ADR-034). Its people-targeting surface selects the matters FOR the
    /// user (task 152).
    /// </param>
    /// <param name="callerQuery">
    /// The existing caller-context read seam (MSCRMCallerID = the user): every matter row and overdue-task count is
    /// read AS THE USER, so Dataverse trims a matter the user cannot open (task 152).
    /// </param>
    /// <param name="systemUserIdentityResolver">Entra oid → Dataverse systemuserid.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="timeProvider">
    /// BCL clock abstraction (.NET 8 <see cref="TimeProvider"/>) used to stamp <c>CachedAt</c> and
    /// <c>Timestamp</c> fields on the returned response records. Defaults to
    /// <see cref="TimeProvider.System"/> when not injected (production); tests inject a
    /// deterministic provider (e.g., a hand-rolled subclass with a fixed <see cref="DateTimeOffset"/>)
    /// to assert against known timestamps. Introduced by Phase 4 Track C TestClock PoC
    /// (FR-13 / task 042) per <c>projects/sdap.bff.api-test-suite-repair-r2/design.md §5.5</c>.
    /// </param>
    public PortfolioService(
        IDistributedCache cache,
        IMembershipResolverService membershipResolver,
        IImpersonatedCommunicationQuery callerQuery,
        ISystemUserIdentityResolver systemUserIdentityResolver,
        ILogger<PortfolioService> logger,
        TimeProvider? timeProvider = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _membershipResolver = membershipResolver ?? throw new ArgumentNullException(nameof(membershipResolver));
        _callerQuery = callerQuery ?? throw new ArgumentNullException(nameof(callerQuery));
        _systemUserIdentityResolver = systemUserIdentityResolver ?? throw new ArgumentNullException(nameof(systemUserIdentityResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns the aggregated portfolio summary for the specified user.
    /// Results are cached in Redis for 5 minutes.
    /// </summary>
    /// <param name="userId">The Entra ID object ID of the authenticated user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aggregated portfolio summary.</returns>
    public async Task<PortfolioSummaryResponse> GetPortfolioSummaryAsync(string userId, CancellationToken ct)
    {
        var cacheKey = $"workspace:{userId}:portfolio";

        // 1. Check Redis cache
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug(
                "Portfolio cache hit. UserId={UserId}, CacheKey={CacheKey}",
                userId,
                cacheKey);

            var deserialized = JsonSerializer.Deserialize<PortfolioSummaryResponse>(cached, JsonOptions);
            if (deserialized is not null)
                return deserialized;

            _logger.LogWarning(
                "Portfolio cache entry deserialization returned null, falling through to Dataverse. " +
                "UserId={UserId}, CacheKey={CacheKey}",
                userId,
                cacheKey);
        }

        _logger.LogDebug(
            "Portfolio cache miss. UserId={UserId}, CacheKey={CacheKey}",
            userId,
            cacheKey);

        // 2. Cache miss — query Dataverse
        var matters = await QueryMattersFromDataverseAsync(userId, ct);

        // 3. Aggregate metrics
        var result = AggregatePortfolio(matters);

        // 4. Cache with 5-minute TTL
        var serialized = JsonSerializer.Serialize(result, JsonOptions);
        await _cache.SetStringAsync(
            cacheKey,
            serialized,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            },
            ct);

        _logger.LogDebug(
            "Portfolio cached. UserId={UserId}, CacheKey={CacheKey}, ActiveMatters={ActiveMatters}",
            userId,
            cacheKey,
            result.ActiveMatters);

        return result;
    }

    /// <summary>
    /// Returns focused health metrics for the Portfolio Health Summary UI.
    /// Derives metrics from portfolio data and caches with a separate Redis key.
    /// </summary>
    /// <param name="userId">The Entra ID object ID of the authenticated user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Health metrics response.</returns>
    /// <remarks>
    /// Cache key: "workspace:{userId}:health" (separate from "workspace:{userId}:portfolio").
    /// TTL: 5 minutes.
    /// </remarks>
    public async Task<HealthMetricsResponse> GetHealthMetricsAsync(string userId, CancellationToken ct)
    {
        var cacheKey = $"workspace:{userId}:health";

        // 1. Check Redis cache
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug(
                "Health metrics cache hit. UserId={UserId}, CacheKey={CacheKey}",
                userId,
                cacheKey);

            var deserialized = JsonSerializer.Deserialize<HealthMetricsResponse>(cached, JsonOptions);
            if (deserialized is not null)
                return deserialized;

            _logger.LogWarning(
                "Health metrics cache entry deserialization returned null, falling through to portfolio query. " +
                "UserId={UserId}, CacheKey={CacheKey}",
                userId,
                cacheKey);
        }

        _logger.LogDebug(
            "Health metrics cache miss. UserId={UserId}, CacheKey={CacheKey}",
            userId,
            cacheKey);

        // 2. Derive from portfolio data (reuses existing Dataverse query + aggregate logic)
        var portfolio = await GetPortfolioSummaryAsync(userId, ct);

        var result = new HealthMetricsResponse(
            MattersAtRisk: portfolio.MattersAtRisk,
            OverdueEvents: portfolio.OverdueEvents,
            ActiveMatters: portfolio.ActiveMatters,
            BudgetUtilizationPercent: portfolio.UtilizationPercent,
            PortfolioSpend: portfolio.TotalSpend,
            PortfolioBudget: portfolio.TotalBudget,
            Timestamp: _timeProvider.GetUtcNow());

        // 3. Cache with 5-minute TTL under its own key
        var serialized = JsonSerializer.Serialize(result, JsonOptions);
        await _cache.SetStringAsync(
            cacheKey,
            serialized,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            },
            ct);

        _logger.LogDebug(
            "Health metrics cached. UserId={UserId}, CacheKey={CacheKey}, MattersAtRisk={MattersAtRisk}",
            userId,
            cacheKey,
            result.MattersAtRisk);

        return result;
    }

    /// <summary>
    /// Aggregates raw matter records into a portfolio summary.
    /// </summary>
    /// <remarks>
    /// Instance method (not <c>static</c>) so the <c>CachedAt</c> timestamp can be sourced from
    /// the injected <see cref="TimeProvider"/> — required for deterministic test assertions per
    /// FR-13 (TestClock PoC). Pure-aggregate logic is otherwise side-effect free.
    /// </remarks>
    private PortfolioSummaryResponse AggregatePortfolio(IReadOnlyList<MatterRecord> matters)
    {
        var activeMatters = matters.Where(m => m.IsActive).ToList();

        var totalSpend = activeMatters.Sum(m => m.InvoicedAmount);
        var totalBudget = activeMatters.Sum(m => m.BudgetAmount);

        // Avoid division by zero (ADR constraint)
        var utilizationPercent = totalBudget == 0
            ? 0m
            : Math.Round(totalSpend / totalBudget * 100m, 2);

        var overdueEvents = activeMatters.Sum(m => m.OverdueEventCount);

        // At-risk: overdue events present OR individual matter utilization > 85%
        var mattersAtRisk = activeMatters.Count(m =>
            m.OverdueEventCount > 0
            || (m.BudgetAmount > 0 && m.InvoicedAmount / m.BudgetAmount * 100m > 85m));

        return new PortfolioSummaryResponse(
            TotalSpend: totalSpend,
            TotalBudget: totalBudget,
            UtilizationPercent: utilizationPercent,
            MattersAtRisk: mattersAtRisk,
            OverdueEvents: overdueEvents,
            ActiveMatters: activeMatters.Count,
            CachedAt: _timeProvider.GetUtcNow());
    }

    /// <summary>
    /// The active matters in the caller's portfolio, for the aggregate. A caller that cannot be resolved, or a read
    /// that fails, yields no matters — the pre-existing graceful empty state of this endpoint (logged) — and never an
    /// unfiltered or app-only answer.
    /// </summary>
    private async Task<IReadOnlyList<MatterRecord>> QueryMattersFromDataverseAsync(
        string userId,
        CancellationToken ct)
    {
        _logger.LogDebug(
            "Querying portfolio matters. UserId={UserId}",
            userId);

        // `userId` is the Entra oid; the people surface and the caller-context reads take the Dataverse systemuserid
        // (a different id space — see CallerResolution). Fail CLOSED: an unresolvable caller yields no rows.
        var systemUserId = await _systemUserIdentityResolver
            .ResolveSystemUserIdAsync(userId, ct)
            .ConfigureAwait(false);

        if (systemUserId is not { } callerSystemUserId)
        {
            _logger.LogWarning(
                "PortfolioService: caller {UserId} could not be resolved to a systemuser; returning no matters "
                + "rather than an unfiltered org-wide result.",
                userId);
            return Array.Empty<MatterRecord>();
        }

        var read = await ReadMattersForSystemUserAsync(callerSystemUserId, ct).ConfigureAwait(false);
        if (read.Unavailable)
        {
            // Return empty list on failure — graceful empty state (pre-existing endpoint constraint). The read above
            // already logged WHY; nothing here is answered app-only.
            _logger.LogError(
                "PortfolioService: the portfolio matters could not be determined for UserId={UserId}; returning the "
                + "empty portfolio.",
                userId);
            return Array.Empty<MatterRecord>();
        }

        _logger.LogDebug(
            "Read {Count} portfolio matters as the caller. UserId={UserId}",
            read.Matters.Count,
            userId);

        return read.Matters;
    }

    /// <summary>
    /// The active matters FOR <paramref name="systemUserId"/> (ADR-034 A3 people-targeting surface, read to
    /// completion), with their spend, budget and overdue open-task counts — every row read AS THE USER.
    /// </summary>
    /// <remarks>
    /// <para>Shared by the portfolio aggregate and <see cref="BriefingService"/>'s top-priority matter, so the two can
    /// never disagree about which matters are the user's.</para>
    /// <para>Failure semantics (ADR-003 fail closed): a failed people resolution, a people set larger than the
    /// resolver's ceiling, or any failed caller-context read (any chunk) returns
    /// <see cref="PortfolioMatterRead.Unavailable"/> = <see langword="true"/> — never a shrunk list and never an
    /// app-only answer. Cancellation is always propagated.</para>
    /// <para>Columns verified against live <c>sprk_matter</c> metadata (read-only, 2026-10-02): <c>sprk_mattername</c>,
    /// <c>sprk_totalspendtodate</c>, <c>sprk_totalbudget</c>. There is no stored overdue count; overdue open Task
    /// events (due on or before yesterday, UTC) are counted from <c>sprk_event</c>.</para>
    /// </remarks>
    internal async Task<PortfolioMatterRead> ReadMattersForSystemUserAsync(Guid systemUserId, CancellationToken ct)
    {
        PeopleTargetedSet.Result candidates;
        try
        {
            candidates = await PeopleTargetedSet
                .ResolveAsync(_membershipResolver, systemUserId, MatterEntityLogicalName, _logger, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PortfolioService: people-targeting resolution failed for SystemUserId={SystemUserId}. Unavailable "
                + "(fail closed, no app-only fallback).",
                systemUserId);
            return PortfolioMatterRead.UnavailableRead;
        }

        if (!candidates.Complete)
        {
            // PeopleTargetedSet logged people_targeting_set_incomplete — an arbitrary subset is never aggregated.
            return PortfolioMatterRead.UnavailableRead;
        }

        if (candidates.Ids.Count == 0)
        {
            return PortfolioMatterRead.None;
        }

        try
        {
            return new PortfolioMatterRead(
                await QueryMatterDetailsAsCallerAsync(candidates.Ids, systemUserId, ct).ConfigureAwait(false),
                Unavailable: false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PortfolioService: the caller-context read of matter details failed for SystemUserId={SystemUserId} "
                + "(candidate count={Count}). Unavailable — never answered app-only.",
                systemUserId, candidates.Ids.Count);
            return PortfolioMatterRead.UnavailableRead;
        }
    }

    /// <summary>
    /// Reads the candidate matters' detail rows and their overdue open-task counts AS THE CALLER, chunked. Matters the
    /// caller cannot read are absent from the result (Dataverse trims them). Throws on any failed read.
    /// </summary>
    private async Task<IReadOnlyList<MatterRecord>> QueryMatterDetailsAsCallerAsync(
        IReadOnlyList<Guid> matterIds,
        Guid callerSystemUserId,
        CancellationToken ct)
    {
        var details = new Dictionary<Guid, Dictionary<string, JsonElement>>();
        var overdue = new Dictionary<Guid, int>();
        // "Overdue" = due before today (on or before yesterday), UTC.
        var yesterday = _timeProvider.GetUtcNow().UtcDateTime.Date.AddDays(-1)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        foreach (var chunk in matterIds.Distinct().Chunk(MaxIdsPerImpersonatedRequest))
        {
            // Active candidate matters, as the caller.
            var matterClause = string.Join(" or ", chunk.Select(id => $"sprk_matterid eq {id:D}"));
            var matterRows = await _callerQuery.QueryAsync(
                "sprk_matters",
                "$select=sprk_matterid,sprk_mattername,sprk_totalspendtodate,sprk_totalbudget"
                + $"&$filter=statecode eq 0 and ({matterClause})",
                callerSystemUserId,
                ct).ConfigureAwait(false);

            foreach (var row in matterRows)
            {
                if (ReadGuid(row, "sprk_matterid") is { } id)
                {
                    details.TryAdd(id, row);
                }
            }

            // Open Task events on those matters whose due date has passed, as the caller. Counted here — sprk_matter
            // has no stored overdue count.
            var regardingClause = string.Join(" or ", chunk.Select(id => $"_sprk_regardingmatter_value eq {id:D}"));
            var eventRows = await _callerQuery.QueryAsync(
                "sprk_events",
                "$select=sprk_eventid,_sprk_regardingmatter_value"
                + $"&$filter=_sprk_eventtype_ref_value eq {EventTypeTask} and statecode eq 0"
                + $" and Microsoft.Dynamics.CRM.OnOrBefore(PropertyName='sprk_duedate',PropertyValue='{yesterday}')"
                + $" and ({regardingClause})",
                callerSystemUserId,
                ct).ConfigureAwait(false);

            foreach (var row in eventRows)
            {
                if (ReadGuid(row, "_sprk_regardingmatter_value") is { } matterId)
                {
                    overdue[matterId] = overdue.TryGetValue(matterId, out var n) ? n + 1 : 1;
                }
            }
        }

        var matters = new List<MatterRecord>(details.Count);
        foreach (var (id, row) in details)
        {
            matters.Add(new MatterRecord
            {
                Id = id,
                Name = ReadString(row, "sprk_mattername") ?? string.Empty,
                InvoicedAmount = ReadDecimal(row, "sprk_totalspendtodate"),
                BudgetAmount = ReadDecimal(row, "sprk_totalbudget"),
                OverdueEventCount = overdue.TryGetValue(id, out var count) ? count : 0,
                // The read filters statecode eq 0, so every returned matter is active.
                IsActive = true,
            });
        }

        return matters;
    }

    private static string? ReadString(Dictionary<string, JsonElement> row, string key) =>
        row.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static Guid? ReadGuid(Dictionary<string, JsonElement> row, string key) =>
        Guid.TryParse(ReadString(row, key), out var g) && g != Guid.Empty ? g : null;

    private static decimal ReadDecimal(Dictionary<string, JsonElement> row, string key) =>
        row.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d) ? d : 0m;
}
