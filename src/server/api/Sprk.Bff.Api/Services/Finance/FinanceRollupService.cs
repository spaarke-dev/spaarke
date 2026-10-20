using System.Globalization;
using System.Text.Json;
using Microsoft.PowerPlatform.Dataverse.Client;
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
/// <c>CallerRecordAccessProbe</c> precedent. The reads unwrap a concrete <c>ServiceClient</c>, which no in-process
/// double can stand in for, so without the seam neither the recalculate routes' post-check 404 nor
/// SpendSnapshotGenerationJobHandler's deleted-matter skip could be tested (task 130).
/// </remarks>
public class FinanceRollupService
{
    // INTENTIONAL: Keeps IDataverseService — casts to ServiceClient for FetchXML queries.
    // Cannot use narrow interface until FetchXML support is added to IFieldMappingDataverseService.
    private readonly IDataverseService _dataverseService;

    // Writes go through the NARROW interface (→ DataverseWebApiService), NOT the composite.
    // The composite resolves to DataverseServiceClientImpl, whose field-mapping methods are stubs.
    // See docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md.
    private readonly IFieldMappingDataverseService _fieldMappingService;
    private readonly ILogger<FinanceRollupService> _logger;

    // Entity constants (same as FinancialCalculationToolHandler)
    // The spend/budget/utilization rules themselves (status set, sums, formula) live in FinanceBaseValues, which the Signal
    // evaluator also uses (task 025b, D-122): there is one formula, here and there.
    private const string InvoiceEntity = FinanceBaseValues.InvoiceEntity;
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
        ILogger<FinanceRollupService> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
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

        // Get ServiceClient for QueryExpression support
        var serviceClient = _dataverseService.UnwrapServiceClient(nameof(FinanceRollupService));

        // Step 1: Parallel queries — invoices and budgets
        var invoiceTask = QueryInvoicesAsync(serviceClient, parentId, invoiceLookupField, ct);
        var budgetTask = QueryBudgetTotalAsync(serviceClient, parentId, budgetLookupField, ct);

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

        // Pending Spend (D-104): In Review and not held, kept apart from actual spend; it feeds nothing else here.
        await WriteRollupAsync(
            _fieldMappingService, _logger, parentEntityName, parentId, fields,
            async token => FinanceBaseValues.SumSpend(
                await QueryPendingInvoicesAsync(serviceClient, parentId, invoiceLookupField, token)),
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
    /// Query all invoices for a parent entity (matter or project) with valid statuses.
    /// </summary>
    private static async Task<List<Entity>> QueryInvoicesAsync(
        ServiceClient serviceClient,
        Guid parentId,
        string lookupField,
        CancellationToken ct)
    {
        var query = BuildInvoiceQuery(parentId, lookupField);

        var results = await Task.Run(() => serviceClient.RetrieveMultiple(query), ct);
        return results.Entities.ToList();
    }

    /// <summary>Query the parent's PENDING invoices (D-104): In Review and not held.</summary>
    private static async Task<List<Entity>> QueryPendingInvoicesAsync(
        ServiceClient serviceClient,
        Guid parentId,
        string lookupField,
        CancellationToken ct)
    {
        var query = BuildPendingInvoiceQuery(parentId, lookupField);

        var results = await Task.Run(() => serviceClient.RetrieveMultiple(query), ct);
        return results.Entities.ToList();
    }

    /// <summary>The pending-spend invoice query (D-104): the parent's invoices whose status is one of
    /// <see cref="FinanceBaseValues.PendingInvoiceStatuses"/> and that are not held. The held exclusion is
    /// <see cref="FinanceBaseValues.HeldSubmissionConditions"/>, written as a LEFT OUTER link with the conditions inside the link
    /// and a null check on the linked key, so an invoice with several linked submissions is returned at most once. Internal so a
    /// test can pin it against the grouped query the Signal evaluator sends.</summary>
    internal static QueryExpression BuildPendingInvoiceQuery(Guid parentId, string lookupField)
    {
        var query = new QueryExpression(InvoiceEntity)
        {
            ColumnSet = InvoiceColumns(),
            Criteria = new FilterExpression(LogicalOperator.And)
        };
        query.Criteria.AddCondition(lookupField, ConditionOperator.Equal, parentId);
        query.Criteria.AddCondition(
            Invoice_StatusCode,
            ConditionOperator.In,
            FinanceBaseValues.PendingInvoiceStatuses.Cast<object>().ToArray());

        var held = query.AddLink(
            FinanceBaseValues.SubmissionEntity, FinanceBaseValues.InvoiceKeyColumn, FinanceBaseValues.SubmissionInvoiceLookup, JoinOperator.LeftOuter);
        held.EntityAlias = FinanceBaseValues.HeldAlias;
        held.Columns = new ColumnSet(false);
        foreach (var (column, values) in FinanceBaseValues.HeldSubmissionConditions)
        {
            held.LinkCriteria.AddCondition(column, ConditionOperator.In, values.ToArray());
        }

        query.Criteria.AddCondition(
            FinanceBaseValues.HeldAlias, FinanceBaseValues.SubmissionKeyColumn, ConditionOperator.Null);
        return query;
    }

    /// <summary>The invoice columns both spend queries read: the two amount columns of <see cref="FinanceBaseValues.SpendAmount"/>
    /// (a column left out would read as empty and fall back to the billed total without a sound), the date and the status.</summary>
    private static ColumnSet InvoiceColumns() =>
        new(FinanceBaseValues.SpendAmountColumns.Append(Invoice_InvoiceDate).Append(Invoice_StatusCode).ToArray());

    /// <summary>The invoice query: the parent's invoices whose status is one of <see cref="FinanceBaseValues.CountedInvoiceStatuses"/>.
    /// Internal so a test can pin it against the grouped query the Signal evaluator sends (same lookups, same status set).</summary>
    internal static QueryExpression BuildInvoiceQuery(Guid parentId, string lookupField)
    {
        var query = new QueryExpression(InvoiceEntity)
        {
            ColumnSet = InvoiceColumns(),
            Criteria = new FilterExpression(LogicalOperator.And)
        };
        query.Criteria.AddCondition(lookupField, ConditionOperator.Equal, parentId);
        query.Criteria.AddCondition(
            Invoice_StatusCode,
            ConditionOperator.In,
            FinanceBaseValues.CountedInvoiceStatuses.Cast<object>().ToArray());
        return query;
    }

    /// <summary>
    /// Query all Budget records for a parent entity and return the sum of sprk_totalbudget.
    /// </summary>
    private static async Task<decimal> QueryBudgetTotalAsync(
        ServiceClient serviceClient,
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

        var results = await Task.Run(() => serviceClient.RetrieveMultiple(query), ct);
        return FinanceBaseValues.SumMoney(results.Entities, Budget_TotalBudget);
    }
}
