using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models;
using System.Globalization;
using System.Text.Json;

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
///   3. Write back to parent via UpdateRecordFieldsAsync
/// </summary>
public sealed class FinanceRollupService
{
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<FinanceRollupService> _logger;

    // Entity constants (same as FinancialCalculationToolHandler)
    private const string InvoiceEntity = "sprk_invoice";
    private const string Invoice_Matter = "sprk_matter";
    private const string Invoice_Project = "sprk_project";
    private const string Invoice_TotalAmount = "sprk_totalamount";
    private const string Invoice_InvoiceStatus = "sprk_invoicestatus";
    private const string Invoice_InvoiceDate = "sprk_invoicedate";

    private const string BudgetEntity = "sprk_budget";
    private const string Budget_Matter = "sprk_matter";
    private const string Budget_Project = "sprk_project";
    private const string Budget_TotalBudget = "sprk_totalbudget";

    private const int InvoiceStatus_Confirmed = 100000001;
    private const int InvoiceStatus_Extracted = 100000002;
    private const int InvoiceStatus_Processed = 100000003;

    // Target field names on sprk_matter / sprk_project (from Dataverse schema)
    private const string Field_TotalSpendToDate = "sprk_totalspendtodate";
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
        ILogger<FinanceRollupService> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Recalculate and persist financial fields for a matter.</summary>
    public Task<RecalculateFinanceResponse> RecalculateMatterAsync(Guid matterId, CancellationToken ct = default)
        => RecalculateForEntityAsync(matterId, Invoice_Matter, Budget_Matter, "sprk_matter", ct);

    /// <summary>Recalculate and persist financial fields for a project.</summary>
    public Task<RecalculateFinanceResponse> RecalculateProjectAsync(Guid projectId, CancellationToken ct = default)
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
        var serviceClient = GetServiceClient();

        // Step 1: Parallel queries — invoices and budgets
        var invoiceTask = QueryInvoicesAsync(serviceClient, parentId, invoiceLookupField, ct);
        var budgetTask = QueryBudgetTotalAsync(serviceClient, parentId, budgetLookupField, ct);

        await Task.WhenAll(invoiceTask, budgetTask);

        var invoices = invoiceTask.Result;
        var totalBudget = budgetTask.Result;

        // Step 2: Compute all fields
        var now = DateTime.UtcNow;
        var currentMonthKey = now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var priorMonthKey = now.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);

        decimal totalSpend = 0m;
        decimal currentMonthSpend = 0m;
        decimal priorMonthSpend = 0m;
        int invoiceCount = invoices.Count;

        // Monthly buckets for timeline (last 12 months)
        var monthlyBuckets = new SortedDictionary<string, decimal>();
        for (int i = 11; i >= 0; i--)
        {
            var key = now.AddMonths(-i).ToString("yyyy-MM", CultureInfo.InvariantCulture);
            monthlyBuckets[key] = 0m;
        }

        foreach (var invoice in invoices)
        {
            var amount = invoice.GetAttributeValue<Money>(Invoice_TotalAmount)?.Value ?? 0m;
            totalSpend += amount;

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

        var remainingBudget = totalBudget - totalSpend;
        var utilization = totalBudget > 0 ? (totalSpend / totalBudget) * 100m : 0m;
        var velocity = SpendSnapshotService.ComputeVelocityPct(currentMonthSpend, priorMonthSpend);
        var averageInvoice = invoiceCount > 0 ? totalSpend / invoiceCount : 0m;

        // Build timeline JSON array
        var timelineEntries = monthlyBuckets.Select(kvp => new { month = kvp.Key, spend = kvp.Value });
        var timelineJson = JsonSerializer.Serialize(timelineEntries);

        // Step 3: Write back to parent entity
        var fields = new Dictionary<string, object?>
        {
            [Field_TotalSpendToDate] = new Money(totalSpend),
            [Field_InvoiceCount] = invoiceCount,
            [Field_MonthlySpendCurrent] = new Money(currentMonthSpend),
            [Field_TotalBudget] = new Money(totalBudget),
            [Field_RemainingBudget] = new Money(remainingBudget),
            [Field_BudgetUtilizationPercent] = utilization,
            [Field_MonthOverMonthVelocity] = velocity ?? 0m,
            [Field_AverageInvoiceAmount] = new Money(averageInvoice),
            [Field_MonthlySpendTimeline] = timelineJson
        };

        await _dataverseService.UpdateRecordFieldsAsync(parentEntityName, parentId, fields, ct);

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

    /// <summary>
    /// Query all invoices for a parent entity (matter or project) with valid statuses.
    /// </summary>
    private static async Task<List<Entity>> QueryInvoicesAsync(
        ServiceClient serviceClient,
        Guid parentId,
        string lookupField,
        CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceEntity)
        {
            ColumnSet = new ColumnSet(Invoice_TotalAmount, Invoice_InvoiceDate, Invoice_InvoiceStatus),
            Criteria = new FilterExpression(LogicalOperator.And)
        };
        query.Criteria.AddCondition(lookupField, ConditionOperator.Equal, parentId);
        query.Criteria.AddCondition(
            Invoice_InvoiceStatus,
            ConditionOperator.In,
            InvoiceStatus_Confirmed,
            InvoiceStatus_Extracted,
            InvoiceStatus_Processed);

        var results = await Task.Run(() => serviceClient.RetrieveMultiple(query), ct);
        return results.Entities.ToList();
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
        return results.Entities.Sum(e => e.GetAttributeValue<Money>(Budget_TotalBudget)?.Value ?? 0m);
    }

    /// <summary>
    /// Get the underlying ServiceClient from IDataverseService for QueryExpression support.
    /// Same pattern as FinancialCalculationToolHandler.
    /// </summary>
    private ServiceClient GetServiceClient()
    {
        if (_dataverseService is ServiceClient sc)
            return sc;

        throw new InvalidOperationException(
            "FinanceRollupService requires IDataverseService resolved as ServiceClient " +
            "for QueryExpression support.");
    }
}
