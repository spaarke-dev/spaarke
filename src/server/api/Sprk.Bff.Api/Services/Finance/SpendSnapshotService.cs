using Microsoft.Extensions.Options;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// Interface for spend snapshot aggregation operations.
/// </summary>
public interface ISpendSnapshotService
{
    /// <summary>
    /// Generate spend snapshots for a matter by aggregating BillingEvent records.
    /// Creates Month + ToDate period snapshots with budget variance and MoM velocity metrics.
    /// Upserts via 5-field alternate key for idempotency.
    /// </summary>
    /// <param name="matterId">The matter ID to generate snapshots for.</param>
    /// <param name="correlationId">Optional correlation ID for traceability.</param>
    /// <param name="ct">Cancellation token.</param>
    Task GenerateAsync(Guid matterId, string? correlationId = null, CancellationToken ct = default);

    /// <summary>
    /// Generate spend snapshots for a project by aggregating BillingEvent records.
    /// Creates Month + ToDate period snapshots with budget variance and MoM velocity metrics.
    /// Upserts via 5-field alternate key for idempotency.
    /// </summary>
    /// <param name="projectId">The project ID to generate snapshots for.</param>
    /// <param name="correlationId">Optional correlation ID for traceability.</param>
    /// <param name="ct">Cancellation token.</param>
    Task GenerateForProjectAsync(Guid projectId, string? correlationId = null, CancellationToken ct = default);
}

/// <summary>
/// Financial aggregation engine that computes spend snapshots from BillingEvent records.
/// Purely deterministic math -- no AI involved.
/// </summary>
/// <remarks>
/// Aggregation rules (MVP):
/// - Period types: Month + ToDate only (Quarter/Year are post-MVP)
/// - Velocity: MoM only (QoQ/YoY post-MVP)
/// - VelocityPct = (current - prior) / prior * 100; null when prior month has zero spend
/// - Budget variance = Budget amount - Invoiced amount (positive = under budget)
/// - SpendSnapshot alternate key: matterId + periodType + periodKey + bucketKey + visibilityFilter
///
/// Uses IDataverseService (resolved as DataverseServiceClientImpl) for direct SDK access
/// to query BillingEvents, BudgetBuckets, and upsert SpendSnapshot records via alternate key.
/// </remarks>
public class SpendSnapshotService : ISpendSnapshotService
{
    private readonly IDataverseService _dataverseService;
    private readonly FinanceOptions _options;
    private readonly ILogger<SpendSnapshotService> _logger;

    // ═══════════════════════════════════════════════════════════════════════════
    // Entity and Field Constants
    // ═══════════════════════════════════════════════════════════════════════════

    // BillingEvent entity
    private const string BillingEventEntity = "sprk_billingevent";
    private const string BillingEvent_Matter = "sprk_matter";
    private const string BillingEvent_Project = "sprk_project";
    private const string BillingEvent_VisibilityState = "sprk_visibilitystate";
    private const string BillingEvent_Amount = "sprk_amount";
    private const string BillingEvent_EventDate = "sprk_eventdate";
    private const string BillingEvent_CostType = "sprk_costtype";

    // BudgetBucket entity
    private const string BudgetBucketEntity = "sprk_budgetbucket";
    private const string BudgetBucket_Budget = "sprk_budget";
    private const string BudgetBucket_BucketKey = "sprk_bucketkey";
    private const string BudgetBucket_Amount = "sprk_amount";

    // Budget entity
    private const string BudgetEntity = "sprk_budget";
    private const string Budget_Matter = "sprk_matter";
    private const string Budget_Project = "sprk_project";
    private const string Budget_TotalBudget = "sprk_totalbudget";

    // SpendSnapshot entity
    private const string SnapshotEntity = "sprk_spendsnapshot";
    private const string Snapshot_Matter = "sprk_matter";
    private const string Snapshot_Project = "sprk_project";
    private const string Snapshot_PeriodType = "sprk_periodtype";
    private const string Snapshot_PeriodKey = "sprk_periodkey";
    private const string Snapshot_BucketKey = "sprk_bucketkey";
    private const string Snapshot_VisibilityFilter = "sprk_visibilityfilter";
    private const string Snapshot_InvoicedAmount = "sprk_invoicedamount";
    private const string Snapshot_BudgetAmount = "sprk_budgetamount";
    private const string Snapshot_BudgetVariance = "sprk_budgetvariance";
    private const string Snapshot_BudgetVariancePct = "sprk_budgetvariancepct";
    private const string Snapshot_VelocityPct = "sprk_velocitypct";
    private const string Snapshot_PriorPeriodAmount = "sprk_priorperiodamount";
    private const string Snapshot_PriorPeriodKey = "sprk_priorperiodkey";
    private const string Snapshot_GeneratedAt = "sprk_generatedat";
    private const string Snapshot_CorrelationId = "sprk_correlationid";
    private const string Snapshot_Name = "sprk_name";

    // OptionSet values (from Create-FinanceSchema.ps1)
    private const int VisibilityState_Invoiced = 100000000;
    private const int PeriodType_Month = 100000000;
    private const int PeriodType_ToDate = 100000003;

    // MVP constants
    private const string DefaultBucketKey = "TOTAL";
    private const string DefaultVisibilityFilter = "ACTUAL_INVOICED";
    private const string ToDatePeriodKey = "TO_DATE";

    public SpendSnapshotService(
        IDataverseService dataverseService,
        IOptions<FinanceOptions> options,
        ILogger<SpendSnapshotService> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task GenerateAsync(Guid matterId, string? correlationId = null, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Generating spend snapshots for matter {MatterId}, CorrelationId: {CorrelationId}",
            matterId, correlationId ?? "none");

        var serviceClient = GetServiceClient();
        var generatedAt = DateTime.UtcNow;
        var effectiveCorrelationId = correlationId ?? Guid.NewGuid().ToString("N");

        // Step 1: Query BillingEvents for the matter where VisibilityState = Invoiced
        var billingEvents = await QueryBillingEventsAsync(serviceClient, matterId, ct);

        _logger.LogInformation(
            "Found {EventCount} invoiced BillingEvents for matter {MatterId}",
            billingEvents.Count, matterId);

        if (billingEvents.Count == 0)
        {
            _logger.LogInformation(
                "No invoiced BillingEvents found for matter {MatterId}. Skipping snapshot generation.",
                matterId);
            return;
        }

        // Step 2: Group by year-month for Monthly period aggregation
        var monthlyAggregations = AggregateByMonth(billingEvents);

        // Step 3: Compute ToDate aggregation (cumulative sum across all months)
        var toDateAmount = billingEvents.Sum(e => e.Amount);

        // Step 4: Look up BudgetBucket records for the matter to compute variance
        var budgetAmount = await GetBudgetAmountForMatterAsync(serviceClient, matterId, ct);

        // Step 5: Compute MoM velocity for each month
        var monthlySnapshots = ComputeMonthlySnapshots(
            monthlyAggregations, budgetAmount, matterId, generatedAt, effectiveCorrelationId);

        // Step 6: Create ToDate snapshot
        var toDateSnapshot = CreateToDateSnapshot(
            toDateAmount, budgetAmount, matterId, generatedAt, effectiveCorrelationId);

        // Step 7: Upsert all snapshots via alternate key
        var allSnapshots = new List<Entity>(monthlySnapshots) { toDateSnapshot };

        _logger.LogInformation(
            "Upserting {SnapshotCount} spend snapshots for matter {MatterId} ({MonthCount} monthly + 1 ToDate)",
            allSnapshots.Count, matterId, monthlySnapshots.Count);

        foreach (var snapshot in allSnapshots)
        {
            await UpsertSnapshotAsync(serviceClient, snapshot, ct);
        }

        _logger.LogInformation(
            "Spend snapshot generation complete for matter {MatterId}. " +
            "Monthly periods: {MonthCount}, ToDate amount: {ToDateAmount:C}",
            matterId, monthlySnapshots.Count, toDateAmount);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Query Operations
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Query BillingEvent records for a matter where VisibilityState = Invoiced.
    /// </summary>
    private async Task<List<BillingEventData>> QueryBillingEventsAsync(
        ServiceClient serviceClient, Guid matterId, CancellationToken ct)
    {
        var query = new QueryExpression(BillingEventEntity)
        {
            ColumnSet = new ColumnSet(
                BillingEvent_Amount,
                BillingEvent_EventDate,
                BillingEvent_CostType,
                BillingEvent_VisibilityState),
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression(BillingEvent_Matter, ConditionOperator.Equal, matterId),
                    new ConditionExpression(BillingEvent_VisibilityState, ConditionOperator.Equal, VisibilityState_Invoiced)
                }
            }
        };

        var results = await serviceClient.RetrieveMultipleAsync(query, ct);

        return results.Entities.Select(e => new BillingEventData
        {
            Amount = e.GetAttributeValue<Money>(BillingEvent_Amount)?.Value ?? 0m,
            EventDate = e.GetAttributeValue<DateTime>(BillingEvent_EventDate),
            CostType = e.GetAttributeValue<OptionSetValue>(BillingEvent_CostType)?.Value ?? 0
        }).ToList();
    }

    /// <summary>
    /// Look up budget amount for the matter from Budget entity.
    /// Matter can have multiple Budget records (spanning budget cycles/time periods).
    /// Returns the sum of all Budget.sprk_totalbudget values for this matter.
    /// Returns null if no budget is configured.
    /// </summary>
    private async Task<decimal?> GetBudgetAmountForMatterAsync(
        ServiceClient serviceClient, Guid matterId, CancellationToken ct)
    {
        // Query ALL Budget records for this matter
        var budgetQuery = new QueryExpression(BudgetEntity)
        {
            ColumnSet = new ColumnSet(Budget_TotalBudget),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression(Budget_Matter, ConditionOperator.Equal, matterId)
                }
            }
        };

        var budgetResults = await serviceClient.RetrieveMultipleAsync(budgetQuery, ct);

        if (budgetResults.Entities.Count == 0)
        {
            _logger.LogDebug("No budget records found for matter {MatterId}.", matterId);
            return null;
        }

        // Sum all budget amounts (matter may span multiple budget cycles)
        var totalBudget = budgetResults.Entities
            .Sum(e => e.GetAttributeValue<Money>(Budget_TotalBudget)?.Value ?? 0m);

        _logger.LogDebug(
            "Budget amount for matter {MatterId}: {BudgetAmount:C} (from {BudgetCount} budget record(s))",
            matterId, totalBudget, budgetResults.Entities.Count);

        return totalBudget;
    }

    /// <summary>
    /// Look up budget amount for the project from Budget entity.
    /// Project can have multiple Budget records (spanning budget cycles/time periods).
    /// Returns the sum of all Budget.sprk_totalbudget values for this project.
    /// Returns null if no budget is configured.
    /// </summary>
    private async Task<decimal?> GetBudgetAmountForProjectAsync(
        ServiceClient serviceClient, Guid projectId, CancellationToken ct)
    {
        // Query ALL Budget records for this project
        var budgetQuery = new QueryExpression(BudgetEntity)
        {
            ColumnSet = new ColumnSet(Budget_TotalBudget),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression(Budget_Project, ConditionOperator.Equal, projectId)
                }
            }
        };

        var budgetResults = await serviceClient.RetrieveMultipleAsync(budgetQuery, ct);

        if (budgetResults.Entities.Count == 0)
        {
            _logger.LogDebug("No budget records found for project {ProjectId}.", projectId);
            return null;
        }

        // Sum all budget amounts (project may span multiple budget cycles)
        var totalBudget = budgetResults.Entities
            .Sum(e => e.GetAttributeValue<Money>(Budget_TotalBudget)?.Value ?? 0m);

        _logger.LogDebug(
            "Budget amount for project {ProjectId}: {BudgetAmount:C} (from {BudgetCount} budget record(s))",
            projectId, totalBudget, budgetResults.Entities.Count);

        return totalBudget;
    }

    /// <inheritdoc />
    public async Task GenerateForProjectAsync(Guid projectId, string? correlationId = null, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Generating spend snapshots for project {ProjectId}, CorrelationId: {CorrelationId}",
            projectId, correlationId ?? "none");

        // TODO: Implement project-level snapshot generation
        // This will mirror GenerateAsync(matterId) but query:
        // - BillingEvents WHERE sprk_project = projectId
        // - Budget WHERE sprk_project = projectId
        // - Upsert SpendSnapshot with sprk_project = projectId
        // See GenerateAsync(Guid matterId) for full implementation pattern

        throw new NotImplementedException(
            "Project-level spend snapshot generation is planned but not yet implemented. " +
            "Implementation will mirror Matter-level generation using Project lookups.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Aggregation Logic (Pure Math)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Group billing events by year-month and sum amounts per bucket (TOTAL for MVP).
    /// Returns sorted dictionary: "2026-01" => totalAmount.
    /// </summary>
    internal static SortedDictionary<string, decimal> AggregateByMonth(List<BillingEventData> events)
    {
        var monthly = new SortedDictionary<string, decimal>();

        foreach (var evt in events)
        {
            var periodKey = evt.EventDate.ToString("yyyy-MM");

            if (monthly.TryGetValue(periodKey, out var existing))
            {
                monthly[periodKey] = existing + evt.Amount;
            }
            else
            {
                monthly[periodKey] = evt.Amount;
            }
        }

        return monthly;
    }

    /// <summary>
    /// Compute MoM velocity: (current - prior) / prior * 100.
    /// Returns null when prior month has zero spend.
    /// </summary>
    internal static decimal? ComputeVelocityPct(decimal currentAmount, decimal priorAmount)
    {
        if (priorAmount == 0m)
            return null;

        return (currentAmount - priorAmount) / priorAmount * 100m;
    }

    /// <summary>
    /// Compute budget variance: Budget - Invoiced.
    /// Positive = under budget, negative = over budget.
    /// Returns null if no budget is configured.
    /// </summary>
    internal static decimal? ComputeBudgetVariance(decimal invoicedAmount, decimal? budgetAmount)
    {
        if (!budgetAmount.HasValue)
            return null;

        return budgetAmount.Value - invoicedAmount;
    }

    /// <summary>
    /// Compute budget variance percentage: Variance / Budget * 100.
    /// Returns null if budget is null or zero.
    /// </summary>
    internal static decimal? ComputeBudgetVariancePct(decimal? variance, decimal? budgetAmount)
    {
        if (!variance.HasValue || !budgetAmount.HasValue || budgetAmount.Value == 0m)
            return null;

        return variance.Value / budgetAmount.Value * 100m;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Snapshot Creation
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Build monthly snapshot entities with MoM velocity.
    /// </summary>
    private List<Entity> ComputeMonthlySnapshots(
        SortedDictionary<string, decimal> monthlyAggregations,
        decimal? budgetAmount,
        Guid matterId,
        DateTime generatedAt,
        string correlationId)
    {
        var snapshots = new List<Entity>();
        var months = monthlyAggregations.Keys.ToList();

        for (var i = 0; i < months.Count; i++)
        {
            var periodKey = months[i];
            var currentAmount = monthlyAggregations[periodKey];

            // Compute MoM velocity
            decimal? priorAmount = null;
            string? priorPeriodKey = null;
            decimal? velocityPct = null;

            if (i > 0)
            {
                priorPeriodKey = months[i - 1];
                priorAmount = monthlyAggregations[priorPeriodKey];
                velocityPct = ComputeVelocityPct(currentAmount, priorAmount.Value);
            }

            // Compute budget variance (per-month: compare monthly spend vs total budget)
            // Note: For monthly snapshots, variance is against total budget for visibility
            var variance = ComputeBudgetVariance(currentAmount, budgetAmount);
            var variancePct = ComputeBudgetVariancePct(variance, budgetAmount);

            var snapshot = CreateSnapshotEntity(
                matterId: matterId,
                periodType: PeriodType_Month,
                periodKey: periodKey,
                invoicedAmount: currentAmount,
                budgetAmount: budgetAmount,
                variance: variance,
                variancePct: variancePct,
                velocityPct: velocityPct,
                priorPeriodAmount: priorAmount,
                priorPeriodKey: priorPeriodKey,
                generatedAt: generatedAt,
                correlationId: correlationId);

            snapshots.Add(snapshot);
        }

        return snapshots;
    }

    /// <summary>
    /// Build ToDate snapshot entity with cumulative totals.
    /// </summary>
    private Entity CreateToDateSnapshot(
        decimal toDateAmount,
        decimal? budgetAmount,
        Guid matterId,
        DateTime generatedAt,
        string correlationId)
    {
        var variance = ComputeBudgetVariance(toDateAmount, budgetAmount);
        var variancePct = ComputeBudgetVariancePct(variance, budgetAmount);

        // ToDate has no velocity (no prior period comparison)
        return CreateSnapshotEntity(
            matterId: matterId,
            periodType: PeriodType_ToDate,
            periodKey: ToDatePeriodKey,
            invoicedAmount: toDateAmount,
            budgetAmount: budgetAmount,
            variance: variance,
            variancePct: variancePct,
            velocityPct: null,
            priorPeriodAmount: null,
            priorPeriodKey: null,
            generatedAt: generatedAt,
            correlationId: correlationId);
    }

    /// <summary>
    /// Create a SpendSnapshot entity with alternate key attributes for upsert.
    /// Alternate key: sprk_matter + sprk_periodtype + sprk_periodkey + sprk_bucketkey + sprk_visibilityfilter
    /// </summary>
    private static Entity CreateSnapshotEntity(
        Guid matterId,
        int periodType,
        string periodKey,
        decimal invoicedAmount,
        decimal? budgetAmount,
        decimal? variance,
        decimal? variancePct,
        decimal? velocityPct,
        decimal? priorPeriodAmount,
        string? priorPeriodKey,
        DateTime generatedAt,
        string correlationId)
    {
        // Create entity with alternate key for upsert
        var keyAttributes = new KeyAttributeCollection
        {
            { Snapshot_Matter, matterId },
            { Snapshot_PeriodType, periodType },
            { Snapshot_PeriodKey, periodKey },
            { Snapshot_BucketKey, DefaultBucketKey },
            { Snapshot_VisibilityFilter, DefaultVisibilityFilter }
        };

        var snapshot = new Entity(SnapshotEntity, keyAttributes);

        // Primary name field (human-readable identifier)
        var periodTypeLabel = periodType == PeriodType_Month ? "Month" : "ToDate";
        snapshot[Snapshot_Name] = $"{periodTypeLabel}:{periodKey}:{DefaultBucketKey}";

        // Required fields
        snapshot[Snapshot_PeriodType] = new OptionSetValue(periodType);
        snapshot[Snapshot_PeriodKey] = periodKey;
        snapshot[Snapshot_BucketKey] = DefaultBucketKey;
        snapshot[Snapshot_VisibilityFilter] = DefaultVisibilityFilter;
        snapshot[Snapshot_InvoicedAmount] = new Money(invoicedAmount);
        snapshot[Snapshot_GeneratedAt] = generatedAt;
        snapshot[Snapshot_CorrelationId] = correlationId;

        // Matter lookup (also part of alternate key)
        snapshot[Snapshot_Matter] = new EntityReference("sprk_matter", matterId);

        // Budget fields (nullable)
        if (budgetAmount.HasValue)
            snapshot[Snapshot_BudgetAmount] = new Money(budgetAmount.Value);
        else
            snapshot[Snapshot_BudgetAmount] = null;

        if (variance.HasValue)
            snapshot[Snapshot_BudgetVariance] = new Money(variance.Value);
        else
            snapshot[Snapshot_BudgetVariance] = null;

        if (variancePct.HasValue)
            snapshot[Snapshot_BudgetVariancePct] = variancePct.Value;
        else
            snapshot[Snapshot_BudgetVariancePct] = null;

        // Velocity fields (nullable)
        if (velocityPct.HasValue)
            snapshot[Snapshot_VelocityPct] = velocityPct.Value;
        else
            snapshot[Snapshot_VelocityPct] = null;

        if (priorPeriodAmount.HasValue)
            snapshot[Snapshot_PriorPeriodAmount] = new Money(priorPeriodAmount.Value);
        else
            snapshot[Snapshot_PriorPeriodAmount] = null;

        snapshot[Snapshot_PriorPeriodKey] = priorPeriodKey;

        return snapshot;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Upsert Operations
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Upsert a SpendSnapshot entity via alternate key.
    /// Uses UpsertRequest for idempotent write -- creates if not found, updates if exists.
    /// </summary>
    private async Task UpsertSnapshotAsync(ServiceClient serviceClient, Entity snapshot, CancellationToken ct)
    {
        var request = new UpsertRequest { Target = snapshot };

        var response = (UpsertResponse)await serviceClient.ExecuteAsync(request, ct);

        var action = response.RecordCreated ? "Created" : "Updated";
        _logger.LogDebug(
            "Snapshot {Action}: {PeriodKey} (Matter: {MatterId})",
            action,
            snapshot.GetAttributeValue<string>(Snapshot_PeriodKey),
            snapshot.KeyAttributes.ContainsKey(Snapshot_Matter) ? snapshot.KeyAttributes[Snapshot_Matter] : "unknown");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ServiceClient Access
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Get the underlying ServiceClient from the IDataverseService implementation.
    /// Required for generic SDK operations (QueryExpression, UpsertRequest) not exposed
    /// on the IDataverseService interface.
    /// </summary>
    private ServiceClient GetServiceClient()
    {
        if (_dataverseService is DataverseServiceClientImpl impl)
            return impl.OrganizationService;

        throw new InvalidOperationException(
            $"SpendSnapshotService requires IDataverseService to be backed by DataverseServiceClientImpl. " +
            $"Actual type: {_dataverseService.GetType().Name}. " +
            $"For unit testing, mock IDataverseService and use the internal static aggregation methods directly.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Internal Data Transfer Types
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Internal DTO for BillingEvent data used in aggregation.
    /// Keeps the aggregation logic independent of Xrm.Sdk Entity objects.
    /// </summary>
    internal record BillingEventData
    {
        public decimal Amount { get; init; }
        public DateTime EventDate { get; init; }
        public int CostType { get; init; }
    }
}
