using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Api.Workspace.Contracts;

namespace Sprk.Bff.Api.Services.Workspace;

/// <summary>
/// Response DTO for portfolio aggregation.
/// </summary>
/// <param name="TotalSpend">Sum of all invoiced amounts across active matters.</param>
/// <param name="TotalBudget">Sum of all budget amounts across active matters.</param>
/// <param name="UtilizationPercent">TotalSpend / TotalBudget expressed as a percentage (0 when TotalBudget is 0).</param>
/// <param name="MattersAtRisk">Count of matters where overdueeventcount > 0 or utilizationpercent > 85.</param>
/// <param name="OverdueEvents">Total count of overdue events across all active matters.</param>
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
/// Internal model representing a matter record returned from Dataverse.
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
/// Aggregates portfolio data for the Legal Operations Workspace.
/// Queries Dataverse for matter records and caches results in Redis.
/// </summary>
/// <remarks>
/// Follows ADR-009: Redis-first caching with a 5-minute TTL.
/// Cache key pattern: "workspace:{userId}:portfolio"
///
/// At-risk definition:
/// - Matter has OverdueEventCount > 0, OR
/// - Matter's individual utilization (InvoicedAmount / BudgetAmount) > 85%
/// </remarks>
public class PortfolioService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<PortfolioService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Initializes a new instance of <see cref="PortfolioService"/>.
    /// </summary>
    /// <param name="cache">Distributed cache (Redis) for portfolio data.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public PortfolioService(IDistributedCache cache, ILogger<PortfolioService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            Timestamp: DateTimeOffset.UtcNow);

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
    private static PortfolioSummaryResponse AggregatePortfolio(IReadOnlyList<MatterRecord> matters)
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
            CachedAt: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Queries matter records from Dataverse for the specified user.
    /// </summary>
    /// <remarks>
    /// TODO: Replace mock implementation with actual Dataverse query.
    ///
    /// Query should retrieve matters where the user is the responsible attorney or
    /// is a member of the matter team. Use OData filter:
    ///   $filter=sprk_responsibleattorneyid/systemuserid eq '{userId}'
    ///          or sprk_MatterTeamMembers/any(t: t/systemuserid eq '{userId}')
    ///
    /// Fields to select:
    ///   sprk_matterid, sprk_name, sprk_invoicedamount, sprk_budgetamount,
    ///   sprk_overdueeventcount, statecode
    ///
    /// Active filter: statecode eq 0 (Active)
    ///
    /// Example implementation using IOrganizationServiceAsync2 or HttpClient to
    /// Dataverse Web API:
    ///   var url = $"sprk_matters?$select=sprk_matterid,sprk_name,sprk_invoicedamount," +
    ///             $"sprk_budgetamount,sprk_overdueeventcount,statecode" +
    ///             $"&$filter=statecode eq 0";
    /// </remarks>
    private Task<IReadOnlyList<MatterRecord>> QueryMattersFromDataverseAsync(
        string userId,
        CancellationToken ct)
    {
        // TODO: Inject IDataverseService or HttpClient and execute the actual query.
        // The Dataverse client will be wired in once the matter entity schema is finalized.
        // See: projects/home-corporate-workspace-r1/spec.md § Dataverse Schema

        _logger.LogInformation(
            "TODO: Querying matters from Dataverse. UserId={UserId} — returning mock data.",
            userId);

        // Mock data — replace with real Dataverse query
        var mockMatters = new List<MatterRecord>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Matter A",
                InvoicedAmount = 125_000m,
                BudgetAmount = 150_000m,
                OverdueEventCount = 0,
                IsActive = true
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Matter B (at risk — over budget)",
                InvoicedAmount = 92_000m,
                BudgetAmount = 100_000m,
                OverdueEventCount = 0,
                IsActive = true
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Matter C (at risk — overdue events)",
                InvoicedAmount = 40_000m,
                BudgetAmount = 200_000m,
                OverdueEventCount = 3,
                IsActive = true
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Matter D (closed — excluded from metrics)",
                InvoicedAmount = 500_000m,
                BudgetAmount = 450_000m,
                OverdueEventCount = 0,
                IsActive = false
            }
        };

        return Task.FromResult<IReadOnlyList<MatterRecord>>(mockMatters);
    }
}
