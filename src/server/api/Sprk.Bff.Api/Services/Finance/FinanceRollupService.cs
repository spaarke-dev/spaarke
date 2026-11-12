using System.Globalization;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// Calculates and persists denormalized financial fields on Matter/Project records.
/// Called by the subgrid parent rollup web resource (sprk_subgrid_parent_rollup.js)
/// when Invoices or Budgets are added/changed via the form, and by
/// SpendSnapshotGenerationJobHandler after background invoice processing.
///
/// Follows the same pattern as ScorecardCalculatorService:
///   1. Parallel queries for child records
///   2. Compute derived values
///   3. Write back to parent via UpdateExistingRecordFieldsAsync (update-only, task 130)
/// </summary>
/// <remarks>
/// Concrete (ADR-010) with the two entry points <c>virtual</c> as its substitution seam — the
/// <c>CallerRecordAccessProbe</c> precedent (task 130: the recalculate routes' post-check 404 and
/// SpendSnapshotGenerationJobHandler's deleted-matter skip are tested through it).
/// <para>Task 165b-4: counted and pending spend come from the shared readers in <see cref="FinanceBaseValues"/>
/// (<see cref="FinanceBaseValues.ReadCountedInvoicesAsync"/>, <see cref="FinanceBaseValues.ReadPendingSpendAsync"/>), the same ones the
/// Signal evaluator and the finance tool use, so they decide only from server-written sources and cannot disagree. Every read goes through
/// the application identity's <see cref="IGenericEntityService"/> (the same connection the earlier <c>ServiceClient</c> unwrap reached).</para>
/// </remarks>
public class FinanceRollupService
{
    // The application identity's IDataverseService, read through its IGenericEntityService members (task 165b-4).
    private readonly IDataverseService _dataverseService;

    // Task 165b-4: the source of the writer identity only (BillingDecisionRecorder.WriterUserIdAsync), as ApExportService takes it.
    private readonly BillingDecisionRecorder _recorder;

    // Writes go through the NARROW interface (→ DataverseWebApiService), NOT the composite.
    // The composite resolves to DataverseServiceClientImpl, whose field-mapping methods are stubs.
    // See docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md.
    private readonly IFieldMappingDataverseService _fieldMappingService;
    private readonly ILogger<FinanceRollupService> _logger;

    // Entity constants (same as FinancialCalculationToolHandler)
    // The spend/budget/utilization rules themselves (status set, sums, formula) live in FinanceBaseValues, which the Signal
    // evaluator also uses (task 025b, D-122): there is one formula, here and there.
    private const string Invoice_Matter = FinanceBaseValues.MatterEntity;
    private const string Invoice_Project = FinanceBaseValues.ProjectEntity;
    private const string Invoice_StatusCode = FinanceBaseValues.InvoiceStatusCodeColumn;
    private const string Invoice_InvoiceDate = "sprk_invoicedate";

    private const string BudgetEntity = FinanceBaseValues.BudgetEntity;
    private const string Budget_Matter = FinanceBaseValues.MatterEntity;
    private const string Budget_Project = FinanceBaseValues.ProjectEntity;
    private const string Budget_TotalBudget = FinanceBaseValues.BudgetTotalBudget;

    // Target field names on sprk_matter / sprk_project (from Dataverse schema)
    private const string Field_TotalSpendToDate = "sprk_totalspendtodate";
    private const string Field_PendingSpend = FinanceBaseValues.PendingSpendColumn;
    private const string Field_InvoiceCount = "sprk_invoicecount";
    private const string Field_MonthlySpendCurrent = "sprk_monthlyspendcurrent";
    private const string Field_TotalBudget = "sprk_totalbudget";
    private const string Field_RemainingBudget = "sprk_remainingbudget";
    private const string Field_BudgetUtilizationPercent = "sprk_budgetutilizationpercent";
    private const string Field_MonthOverMonthVelocity = "sprk_monthovermonthvelocity";
    private const string Field_AverageInvoiceAmount = "sprk_averageinvoiceamount";
    private const string Field_MonthlySpendTimeline = "sprk_monthlyspendtimeline";

    public FinanceRollupService(
        IDataverseService dataverseService,
        IFieldMappingDataverseService fieldMappingService,
        BillingDecisionRecorder recorder,
        ILogger<FinanceRollupService> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _fieldMappingService = fieldMappingService ?? throw new ArgumentNullException(nameof(fieldMappingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Builds the rollup write payload. Values MUST be OData Web API primitives — plain
    /// <see cref="decimal"/> / <see cref="int"/> / <see cref="string"/> — never SDK Entity-model
    /// wrappers (<c>Money</c>, <c>OptionSetValue</c>, <c>EntityReference</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Regression guard.</b> <c>UpdateRecordFieldsAsync</c> PATCHes this dictionary to the Dataverse
    /// <b>Web API</b>, which takes currency as a bare number. Serializing a <c>Money</c> yields
    /// <c>{"Value":1234.56,"ExtensionData":null}</c> — an object — and Dataverse rejects the PATCH with
    /// HTTP 400. This method previously wrapped the five currency fields in <c>Money</c>, left behind when
    /// <c>b7b0d4011</c> (2026-03-03) converted the impl from the SDK Entity model to OData PATCH; every
    /// recalculate has failed at the write since. Extracted here so the contract is unit-testable without a
    /// live Dataverse connection — see <c>FinanceRollupPayloadTests</c>.
    /// </para>
    /// </remarks>
    internal static Dictionary<string, object?> BuildRollupFields(
        decimal totalSpend,
        int invoiceCount,
        decimal currentMonthSpend,
        decimal totalBudget,
        decimal remainingBudget,
        decimal utilization,
        decimal velocity,
        decimal averageInvoice,
        string timelineJson) => new()
        {
            [Field_TotalSpendToDate] = totalSpend,
            [Field_InvoiceCount] = invoiceCount,
            [Field_MonthlySpendCurrent] = currentMonthSpend,
            [Field_TotalBudget] = totalBudget,
            [Field_RemainingBudget] = remainingBudget,
            [Field_BudgetUtilizationPercent] = utilization,
            [Field_MonthOverMonthVelocity] = velocity,
            [Field_AverageInvoiceAmount] = averageInvoice,
            [Field_MonthlySpendTimeline] = timelineJson
        };

    /// <summary>The Pending Spend payload (D-104), written in its own PATCH after <see cref="BuildRollupFields"/>'s: see
    /// <see cref="WriteRollupAsync"/>.</summary>
    internal static Dictionary<string, object?> BuildPendingSpendFields(decimal pendingSpend) => new()
    {
        [Field_PendingSpend] = pendingSpend
    };

    /// <summary>
    /// Writes the counted rollup, then Pending Spend (D-104) separately. The counted write is the established contract and its
    /// failures propagate as before. Pending Spend is new: its column and its query's join to <c>sprk_invoicesubmission</c> may not
    /// exist yet on an environment the BFF reaches before the schema script runs, and Dataverse refuses a whole PATCH that names
    /// an unknown column. So a failure to read or write it is logged as an Error and leaves the stored value as it was (never 0);
    /// it never stops total spend, budget, velocity or the timeline from updating (ontology-r1 review F1, 2026-10-19).
    /// </summary>
    /// <returns>Whether Pending Spend was written.</returns>
    internal static async Task<bool> WriteRollupAsync(
        IFieldMappingDataverseService writer,
        ILogger logger,
        string parentEntityName,
        Guid parentId,
        Dictionary<string, object?> countedFields,
        Func<CancellationToken, Task<decimal>> readPendingSpend,
        CancellationToken ct)
    {
        // UPDATE-ONLY (task 130): never create. A plain PATCH upserts, so a parent deleted after the caller's
        // authorization check (or a background job's stale id) would be recreated as an empty row carrying
        // only these rollup fields. A missing parent surfaces as KeyNotFoundException → the endpoint's uniform 404.
        await writer.UpdateExistingRecordFieldsAsync(parentEntityName, parentId, countedFields, ct);

        try
        {
            var pendingSpend = await readPendingSpend(ct);
            await writer.UpdateExistingRecordFieldsAsync(parentEntityName, parentId, BuildPendingSpendFields(pendingSpend), ct);
            return true;
        }
        // Not the caller's cancellation; an HttpClient timeout (TaskCanceledException with ct not cancelled) is a pending failure too.
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(
                ex,
                "Pending Spend (D-104) not updated for {Entity} {EntityId}; the counted rollup was written and the stored pending value is unchanged",
                parentEntityName, parentId);
            return false;
        }
    }

    /// <summary>Recalculate and persist financial fields for a matter.</summary>
    public virtual Task<RecalculateFinanceResponse> RecalculateMatterAsync(Guid matterId, CancellationToken ct = default)
        => RecalculateForEntityAsync(matterId, Invoice_Matter, Budget_Matter, "sprk_matter", ct);

    /// <summary>Recalculate and persist financial fields for a project.</summary>
    public virtual Task<RecalculateFinanceResponse> RecalculateProjectAsync(Guid projectId, CancellationToken ct = default)
        => RecalculateForEntityAsync(projectId, Invoice_Project, Budget_Project, "sprk_project", ct);

    private async Task<RecalculateFinanceResponse> RecalculateForEntityAsync(
        Guid parentId,
        string invoiceLookupField,
        string budgetLookupField,
        string parentEntityName,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Recalculating finance for {Entity} {EntityId}",
            parentEntityName, parentId);

        // Step 1: the counted invoices (task 165b-4: the shared reader; an invoice counts only with its approve record) and the budgets.
        // The writer id is asked for only when the parent has counted invoice rows (round 2): with none, counted spend is 0 and the rollup
        // writes without it. With some and no usable writer id, counted spend is unknown: the read throws (and logs a Warning naming the
        // writer setting) before anything is written, so the WHOLE write is skipped (total budget too, it shares the counted PATCH) and
        // pending is unchanged. Any other read fault does the same.
        var invoiceTask = FinanceBaseValues.ReadCountedInvoicesAsync(
            _dataverseService, parentEntityName, [parentId], _recorder.WriterUserIdAsync, ct, [Invoice_InvoiceDate, Invoice_StatusCode], _logger);
        var budgetTask = QueryBudgetTotalAsync(_dataverseService, parentId, budgetLookupField, ct);

        await Task.WhenAll(invoiceTask, budgetTask);

        var invoices = invoiceTask.Result;
        var totalBudget = budgetTask.Result;

        // Step 2: Compute all fields
        var figures = ComputeSpendFigures(invoices, DateTime.UtcNow);
        var totalSpend = figures.TotalSpend;
        var currentMonthSpend = figures.CurrentMonthSpend;
        var priorMonthSpend = figures.PriorMonthSpend;
        var invoiceCount = figures.InvoiceCount;
        var monthlyBuckets = figures.MonthlyBuckets;

        var remainingBudget = totalBudget - totalSpend;
        var utilization = FinanceBaseValues.UtilizationPercent(totalSpend, totalBudget) ?? 0m;
        var velocity = SpendSnapshotService.ComputeVelocityPct(currentMonthSpend, priorMonthSpend);
        var averageInvoice = invoiceCount > 0 ? totalSpend / invoiceCount : 0m;

        // Build timeline JSON array
        var timelineEntries = monthlyBuckets.Select(kvp => new { month = kvp.Key, spend = kvp.Value });
        var timelineJson = JsonSerializer.Serialize(timelineEntries);

        // Step 3: Write back to parent entity — through the NARROW interface (→ DataverseWebApiService).
        var fields = BuildRollupFields(
            totalSpend: totalSpend,
            invoiceCount: invoiceCount,
            currentMonthSpend: currentMonthSpend,
            totalBudget: totalBudget,
            remainingBudget: remainingBudget,
            utilization: utilization,
            velocity: velocity ?? 0m,
            averageInvoice: averageInvoice,
            timelineJson: timelineJson);

        // Pending Spend (D-104): In Review and not held, kept apart from actual spend; it feeds nothing else here. Task 165b-4: the one
        // pending reader (InvoiceBlockedHold's hold, read in a batch), which logs the In Review invoices it leaves out as unlinked.
        await WriteRollupAsync(
            _fieldMappingService, _logger, parentEntityName, parentId, fields,
            async token => (await FinanceBaseValues.ReadPendingSpendAsync(_dataverseService, parentEntityName, [parentId], token, _logger))
                .GetValueOrDefault(parentId),
            ct);

        _logger.LogInformation(
            "Finance rollup complete for {Entity} {EntityId}: " +
            "TotalSpend={TotalSpend:C}, Invoices={InvoiceCount}, Budget={Budget:C}, Utilization={Utilization:F1}%",
            parentEntityName, parentId, totalSpend, invoiceCount, totalBudget, utilization);

        return new RecalculateFinanceResponse
        {
            TotalSpendToDate = totalSpend,
            InvoiceCount = invoiceCount,
            MonthlySpendCurrent = currentMonthSpend,
            TotalBudget = totalBudget,
            RemainingBudget = remainingBudget,
            BudgetUtilizationPercent = utilization,
            MonthOverMonthVelocity = velocity ?? 0m,
            AverageInvoiceAmount = averageInvoice,
            MonthlySpendTimeline = timelineJson
        };
    }

    /// <summary>The figures the rollup derives from the parent's counted invoices alone (no budget).</summary>
    internal sealed record SpendFigures(
        decimal TotalSpend, decimal CurrentMonthSpend, decimal PriorMonthSpend, int InvoiceCount, SortedDictionary<string, decimal> MonthlyBuckets);

    /// <summary>
    /// Total, current-month, prior-month and 12-month timeline spend over <paramref name="invoices"/> (the counted invoices).
    /// D-130: every figure uses each invoice's <see cref="FinanceBaseValues.SpendAmount"/> (approved total, else billed total), so a
    /// reviewer's disallowance lowers the total, the month it belongs to, the velocity and the average alike. Internal so a test can
    /// run it without a live <c>ServiceClient</c>.
    /// </summary>
    internal static SpendFigures ComputeSpendFigures(IReadOnlyCollection<Entity> invoices, DateTime now)
    {
        var currentMonthKey = now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var priorMonthKey = now.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);

        decimal currentMonthSpend = 0m;
        decimal priorMonthSpend = 0m;

        // Monthly buckets for timeline (last 12 months)
        var monthlyBuckets = new SortedDictionary<string, decimal>();
        for (int i = 11; i >= 0; i--)
        {
            var key = now.AddMonths(-i).ToString("yyyy-MM", CultureInfo.InvariantCulture);
            monthlyBuckets[key] = 0m;
        }

        foreach (var invoice in invoices)
        {
            var amount = FinanceBaseValues.SpendAmount(invoice);

            var invoiceDate = invoice.GetAttributeValue<DateTime?>(Invoice_InvoiceDate);
            if (invoiceDate.HasValue)
            {
                var monthKey = invoiceDate.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture);

                if (monthKey == currentMonthKey)
                    currentMonthSpend += amount;
                else if (monthKey == priorMonthKey)
                    priorMonthSpend += amount;

                if (monthlyBuckets.ContainsKey(monthKey))
                    monthlyBuckets[monthKey] += amount;
            }
        }

        return new SpendFigures(FinanceBaseValues.SumSpend(invoices), currentMonthSpend, priorMonthSpend, invoices.Count, monthlyBuckets);
    }

    /// <summary>
    /// Query all Budget records for a parent entity and return the sum of sprk_totalbudget (app-only, through
    /// <see cref="IGenericEntityService"/>; task 165b-4: no <c>ServiceClient</c> unwrap).
    /// </summary>
    private static async Task<decimal> QueryBudgetTotalAsync(
        IGenericEntityService entities,
        Guid parentId,
        string lookupField,
        CancellationToken ct)
    {
        var query = new QueryExpression(BudgetEntity)
        {
            ColumnSet = new ColumnSet(Budget_TotalBudget),
            Criteria = new FilterExpression()
        };
        query.Criteria.AddCondition(lookupField, ConditionOperator.Equal, parentId);

        var results = await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return FinanceBaseValues.SumMoney(results.Entities, Budget_TotalBudget);
    }
}
