using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using System.Text.Json;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// Interface for financial summary aggregation operations.
/// </summary>
public interface IFinanceSummaryService
{
    /// <summary>
    /// Get financial summary for a matter, aggregating snapshots, signals, and recent invoices.
    /// Results are cached in Redis for configured TTL (default: 5 minutes).
    /// </summary>
    /// <param name="matterId">The matter ID to retrieve summary for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Financial summary DTO, or null if no financial data exists for the matter.</returns>
    Task<FinanceSummaryDto?> GetSummaryAsync(Guid matterId, CancellationToken ct = default);

    /// <summary>
    /// Invalidate cached summary for a matter.
    /// Call this after snapshot generation or signal updates.
    /// </summary>
    /// <param name="matterId">The matter ID to invalidate cache for.</param>
    /// <param name="ct">Cancellation token.</param>
    Task InvalidateSummaryAsync(Guid matterId, CancellationToken ct = default);
}

/// <summary>
/// Aggregates financial data for a matter: latest snapshots, active signals, and recent invoices.
/// Results are cached in Redis following ADR-009 Redis-First Caching patterns.
/// </summary>
/// <remarks>
/// Summary composition:
/// - Latest ToDate snapshot: current spend, budget variance, velocity
/// - Active signals: BudgetExceeded, BudgetWarning, VelocitySpike (last 30 days, not dismissed)
/// - Recent invoices: last 5 invoices ordered by date descending
///
/// Cache key pattern: finance:summary:{matterId}
/// Cache TTL: Configured via FinanceOptions.FinanceSummaryCacheTtlMinutes (default: 5 minutes)
/// Cache invalidation: Explicit via InvalidateSummaryAsync after snapshot generation
///
/// Per ADR-009: Redis-first caching with graceful degradation (cache failures don't break functionality)
/// Per ADR-015: No content logging - only IDs, statuses, and metrics
/// </remarks>
public class FinanceSummaryService : IFinanceSummaryService
{
    private readonly IDataverseService _dataverseService;
    private readonly IDistributedCache _cache;
    private readonly FinanceOptions _options;
    private readonly ILogger<FinanceSummaryService> _logger;

    // Cache key prefix following SDAP naming convention
    private const string CacheKeyPrefix = "finance:summary:";

    // ═══════════════════════════════════════════════════════════════════════════
    // Entity and Field Constants
    // ═══════════════════════════════════════════════════════════════════════════

    // SpendSnapshot entity
    private const string SnapshotEntity = "sprk_spendsnapshot";
    private const string Snapshot_Matter = "sprk_matter";
    private const string Snapshot_PeriodType = "sprk_periodtype";
    private const string Snapshot_InvoicedAmount = "sprk_invoicedamount";
    private const string Snapshot_BudgetAmount = "sprk_budgetamount";
    private const string Snapshot_BudgetVariance = "sprk_budgetvariance";
    private const string Snapshot_VelocityPct = "sprk_velocitypct";
    private const string Snapshot_GeneratedAt = "sprk_generatedat";

    // SpendSignal entity
    private const string SignalEntity = "sprk_spendsignal";
    private const string Signal_Matter = "sprk_matter";
    private const string Signal_SignalType = "sprk_signaltype";
    private const string Signal_Severity = "sprk_severity";
    private const string Signal_Message = "sprk_message";
    private const string Signal_GeneratedAt = "sprk_generatedat";
    private const string Signal_IsActive = "sprk_isactive";

    // Invoice entity
    private const string InvoiceEntity = "sprk_invoice";
    private const string Invoice_Matter = "sprk_matter";
    private const string Invoice_InvoiceNumber = "sprk_invoicenumber";
    private const string Invoice_InvoiceDate = "sprk_invoicedate";
    private const string Invoice_TotalAmount = "sprk_totalamount";
    private const string Invoice_VendorOrg = "sprk_vendororg";
    private const string Invoice_InvoiceStatus = "sprk_invoicestatus";

    // Period type option set values
    private const int PeriodType_ToDate = 100000003;

    // Signal type option set values
    private const int SignalType_BudgetExceeded = 100000000;
    private const int SignalType_BudgetWarning = 100000001;
    private const int SignalType_VelocitySpike = 100000002;

    // Severity option set values
    private const int Severity_Info = 100000000;
    private const int Severity_Warning = 100000001;
    private const int Severity_Critical = 100000002;

    // Active signal threshold (signals created in last 30 days)
    private static readonly TimeSpan ActiveSignalThreshold = TimeSpan.FromDays(30);

    // JSON serialization options for cache
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FinanceSummaryService(
        IDataverseService dataverseService,
        IDistributedCache cache,
        IOptions<FinanceOptions> options,
        ILogger<FinanceSummaryService> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<FinanceSummaryDto?> GetSummaryAsync(Guid matterId, CancellationToken ct = default)
    {
        var cacheKey = $"{CacheKeyPrefix}{matterId}";

        try
        {
            // Try cache first
            var cachedData = await _cache.GetStringAsync(cacheKey, ct);
            if (cachedData != null)
            {
                _logger.LogDebug("Finance summary cache HIT for matter {MatterId}", matterId);
                return JsonSerializer.Deserialize<FinanceSummaryDto>(cachedData, JsonOptions);
            }

            _logger.LogDebug("Finance summary cache MISS for matter {MatterId}", matterId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error retrieving finance summary from cache for matter {MatterId}, will generate new", matterId);
            // Continue to generate - cache failure shouldn't break functionality
        }

        // Generate summary from Dataverse
        var summary = await GenerateSummaryAsync(matterId, ct);

        if (summary == null)
        {
            _logger.LogInformation("No financial data found for matter {MatterId}", matterId);
            return null;
        }

        // Cache the result
        try
        {
            var serialized = JsonSerializer.Serialize(summary, JsonOptions);
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.FinanceSummaryCacheTtlMinutes)
            };

            await _cache.SetStringAsync(cacheKey, serialized, cacheOptions, ct);

            _logger.LogDebug(
                "Cached finance summary for matter {MatterId}, TTL={TTL} minutes",
                matterId, _options.FinanceSummaryCacheTtlMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error caching finance summary for matter {MatterId}", matterId);
            // Don't throw - caching is optimization, not requirement
        }

        return summary;
    }

    /// <inheritdoc />
    public async Task InvalidateSummaryAsync(Guid matterId, CancellationToken ct = default)
    {
        var cacheKey = $"{CacheKeyPrefix}{matterId}";

        try
        {
            await _cache.RemoveAsync(cacheKey, ct);
            _logger.LogDebug("Invalidated finance summary cache for matter {MatterId}", matterId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error invalidating finance summary cache for matter {MatterId}", matterId);
            // Don't throw - cache invalidation failure is not critical
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Summary Generation
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generate financial summary by aggregating Dataverse data.
    /// Returns null if no financial data exists for the matter.
    /// </summary>
    private async Task<FinanceSummaryDto?> GenerateSummaryAsync(Guid matterId, CancellationToken ct)
    {
        _logger.LogInformation("Generating finance summary for matter {MatterId}", matterId);

        var serviceClient = GetServiceClient();

        // Query latest ToDate snapshot
        var snapshot = await QueryLatestToDateSnapshotAsync(serviceClient, matterId, ct);
        if (snapshot == null)
        {
            // No snapshot means no financial data yet
            return null;
        }

        // Query active signals (last 30 days, not dismissed)
        var signals = await QueryActiveSignalsAsync(serviceClient, matterId, ct);

        // Query recent invoices (last 5)
        var recentInvoices = await QueryRecentInvoicesAsync(serviceClient, matterId, ct);

        var summary = new FinanceSummaryDto
        {
            MatterId = matterId,
            CurrentSpend = snapshot.InvoicedAmount,
            Budget = snapshot.BudgetAmount ?? 0m,
            BudgetVariance = snapshot.BudgetVariance ?? 0m,
            VelocityPct = snapshot.VelocityPct,
            ActiveSignals = signals,
            RecentInvoices = recentInvoices,
            GeneratedAt = DateTime.UtcNow
        };

        _logger.LogInformation(
            "Finance summary generated for matter {MatterId}. CurrentSpend={CurrentSpend:C}, " +
            "ActiveSignals={SignalCount}, RecentInvoices={InvoiceCount}",
            matterId, summary.CurrentSpend, signals.Count, recentInvoices.Count);

        return summary;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Dataverse Queries
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Query the latest ToDate snapshot for a matter.
    /// Returns null if no ToDate snapshot exists.
    /// </summary>
    private async Task<SpendSnapshotData?> QueryLatestToDateSnapshotAsync(
        ServiceClient serviceClient,
        Guid matterId,
        CancellationToken ct)
    {
        var query = new QueryExpression(SnapshotEntity)
        {
            ColumnSet = new ColumnSet(
                Snapshot_InvoicedAmount,
                Snapshot_BudgetAmount,
                Snapshot_BudgetVariance,
                Snapshot_VelocityPct,
                Snapshot_GeneratedAt),
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression(Snapshot_Matter, ConditionOperator.Equal, matterId),
                    new ConditionExpression(Snapshot_PeriodType, ConditionOperator.Equal, PeriodType_ToDate)
                }
            },
            Orders =
            {
                new OrderExpression(Snapshot_GeneratedAt, OrderType.Descending)
            },
            TopCount = 1
        };

        var results = await serviceClient.RetrieveMultipleAsync(query, ct);
        var entity = results.Entities.FirstOrDefault();

        if (entity == null)
        {
            return null;
        }

        return new SpendSnapshotData
        {
            InvoicedAmount = entity.GetAttributeValue<Microsoft.Xrm.Sdk.Money>(Snapshot_InvoicedAmount)?.Value ?? 0m,
            BudgetAmount = entity.GetAttributeValue<Microsoft.Xrm.Sdk.Money>(Snapshot_BudgetAmount)?.Value,
            BudgetVariance = entity.GetAttributeValue<Microsoft.Xrm.Sdk.Money>(Snapshot_BudgetVariance)?.Value,
            VelocityPct = entity.Contains(Snapshot_VelocityPct) ? entity.GetAttributeValue<decimal?>(Snapshot_VelocityPct) : null
        };
    }

    /// <summary>
    /// Query active signals for a matter (created in last 30 days, IsActive = true).
    /// </summary>
    private async Task<List<SpendSignalDto>> QueryActiveSignalsAsync(
        ServiceClient serviceClient,
        Guid matterId,
        CancellationToken ct)
    {
        var cutoffDate = DateTime.UtcNow.Subtract(ActiveSignalThreshold);

        var query = new QueryExpression(SignalEntity)
        {
            ColumnSet = new ColumnSet(
                Signal_SignalType,
                Signal_Severity,
                Signal_Message,
                Signal_GeneratedAt),
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression(Signal_Matter, ConditionOperator.Equal, matterId),
                    new ConditionExpression(Signal_IsActive, ConditionOperator.Equal, true),
                    new ConditionExpression(Signal_GeneratedAt, ConditionOperator.GreaterEqual, cutoffDate)
                }
            },
            Orders =
            {
                new OrderExpression(Signal_GeneratedAt, OrderType.Descending)
            }
        };

        var results = await serviceClient.RetrieveMultipleAsync(query, ct);

        return results.Entities.Select(e => new SpendSignalDto
        {
            SignalType = MapSignalType(e.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>(Signal_SignalType)?.Value ?? 0),
            Severity = MapSeverity(e.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>(Signal_Severity)?.Value ?? 0),
            Message = e.GetAttributeValue<string>(Signal_Message) ?? string.Empty,
            GeneratedAt = e.GetAttributeValue<DateTime>(Signal_GeneratedAt)
        }).ToList();
    }

    /// <summary>
    /// Query recent invoices for a matter (last 5, ordered by invoice date descending).
    /// </summary>
    private async Task<List<RecentInvoiceDto>> QueryRecentInvoicesAsync(
        ServiceClient serviceClient,
        Guid matterId,
        CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceEntity)
        {
            ColumnSet = new ColumnSet(
                Invoice_InvoiceNumber,
                Invoice_InvoiceDate,
                Invoice_TotalAmount,
                Invoice_VendorOrg,
                Invoice_InvoiceStatus),
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression(Invoice_Matter, ConditionOperator.Equal, matterId)
                }
            },
            Orders =
            {
                new OrderExpression(Invoice_InvoiceDate, OrderType.Descending)
            },
            TopCount = 5
        };

        var results = await serviceClient.RetrieveMultipleAsync(query, ct);

        return results.Entities.Select(e => new RecentInvoiceDto
        {
            InvoiceId = e.Id,
            InvoiceNumber = e.GetAttributeValue<string>(Invoice_InvoiceNumber) ?? string.Empty,
            InvoiceDate = e.GetAttributeValue<DateTime?>(Invoice_InvoiceDate),
            TotalAmount = e.GetAttributeValue<Microsoft.Xrm.Sdk.Money>(Invoice_TotalAmount)?.Value ?? 0m,
            VendorOrgId = e.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>(Invoice_VendorOrg)?.Id,
            Status = e.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>(Invoice_InvoiceStatus)?.Value ?? 0
        }).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helper Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Map signal type option set value to string name.
    /// </summary>
    private static string MapSignalType(int signalType) => signalType switch
    {
        SignalType_BudgetExceeded => "BudgetExceeded",
        SignalType_BudgetWarning => "BudgetWarning",
        SignalType_VelocitySpike => "VelocitySpike",
        _ => "Unknown"
    };

    /// <summary>
    /// Map severity option set value to string name.
    /// </summary>
    private static string MapSeverity(int severity) => severity switch
    {
        Severity_Info => "Info",
        Severity_Warning => "Warning",
        Severity_Critical => "Critical",
        _ => "Unknown"
    };

    /// <summary>
    /// Get the underlying ServiceClient from the IDataverseService implementation.
    /// Required for QueryExpression operations not exposed on the IDataverseService interface.
    /// </summary>
    private ServiceClient GetServiceClient()
    {
        if (_dataverseService is DataverseServiceClientImpl impl)
            return impl.OrganizationService;

        throw new InvalidOperationException(
            $"FinanceSummaryService requires IDataverseService to be backed by DataverseServiceClientImpl. " +
            $"Actual type: {_dataverseService.GetType().Name}. " +
            $"For unit testing, mock IFinanceSummaryService directly.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Internal Data Transfer Types
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Internal DTO for snapshot data used in summary composition.
    /// </summary>
    private record SpendSnapshotData
    {
        public decimal InvoicedAmount { get; init; }
        public decimal? BudgetAmount { get; init; }
        public decimal? BudgetVariance { get; init; }
        public decimal? VelocityPct { get; init; }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Public DTOs (returned to API consumers)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Financial summary for a matter, aggregating snapshots, signals, and recent invoices.
/// </summary>
public record FinanceSummaryDto
{
    /// <summary>The matter ID this summary is for.</summary>
    public Guid MatterId { get; init; }

    /// <summary>Current spend amount (from latest ToDate snapshot).</summary>
    public decimal CurrentSpend { get; init; }

    /// <summary>Budget amount (from latest ToDate snapshot).</summary>
    public decimal Budget { get; init; }

    /// <summary>Budget variance: Budget - CurrentSpend (positive = under budget).</summary>
    public decimal BudgetVariance { get; init; }

    /// <summary>Velocity percentage (month-over-month change, nullable).</summary>
    public decimal? VelocityPct { get; init; }

    /// <summary>Active spend signals (last 30 days, not dismissed).</summary>
    public List<SpendSignalDto> ActiveSignals { get; init; } = new();

    /// <summary>Recent invoices (last 5, ordered by date descending).</summary>
    public List<RecentInvoiceDto> RecentInvoices { get; init; } = new();

    /// <summary>Timestamp when this summary was generated.</summary>
    public DateTime GeneratedAt { get; init; }
}

/// <summary>
/// Spend signal DTO for summary response.
/// </summary>
public record SpendSignalDto
{
    /// <summary>Signal type: BudgetExceeded, BudgetWarning, VelocitySpike.</summary>
    public string SignalType { get; init; } = string.Empty;

    /// <summary>Severity: Info, Warning, Critical.</summary>
    public string Severity { get; init; } = string.Empty;

    /// <summary>Human-readable signal message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>When the signal was generated.</summary>
    public DateTime GeneratedAt { get; init; }
}

/// <summary>
/// Recent invoice DTO for summary response.
/// </summary>
public record RecentInvoiceDto
{
    /// <summary>Invoice record ID.</summary>
    public Guid InvoiceId { get; init; }

    /// <summary>Invoice number.</summary>
    public string InvoiceNumber { get; init; } = string.Empty;

    /// <summary>Invoice date.</summary>
    public DateTime? InvoiceDate { get; init; }

    /// <summary>Total invoice amount.</summary>
    public decimal TotalAmount { get; init; }

    /// <summary>Vendor organization ID.</summary>
    public Guid? VendorOrgId { get; init; }

    /// <summary>Invoice status (option set value).</summary>
    public int Status { get; init; }
}
