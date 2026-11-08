using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Finance.Tools;

/// <summary>
/// Tool handler that calculates and aggregates financial metrics for matters or projects.
/// Queries Dataverse to compute total spend, invoice count, budget, and remaining budget.
/// Supports both Matter-level and Project-level financial calculations.
/// </summary>
public class FinancialCalculationToolHandler : IAiToolHandler
{
    // The application identity's IDataverseService, read through its IGenericEntityService members (task 165b-4).
    private readonly IDataverseService _dataverseService;

    // Task 165b-4: the source of the writer identity only (BillingDecisionRecorder.WriterUserIdAsync), as ApExportService takes it.
    private readonly BillingDecisionRecorder _recorder;
    private readonly FinanceTelemetry _telemetry;
    private readonly ILogger<FinancialCalculationToolHandler> _logger;

    public const string ToolNameConst = "FinancialCalculation";
    public string ToolName => ToolNameConst;

    // Entity and field constants
    private const string MatterEntity = "sprk_matter";
    private const string ProjectEntity = "sprk_project";

    private const string BudgetEntity = "sprk_budget";
    private const string Budget_Matter = "sprk_matter";
    private const string Budget_Project = "sprk_project";
    private const string Budget_TotalBudget = "sprk_totalbudget";

    // Which invoices count as spend is FinanceBaseValues.CountedInvoiceStatuses, filtered on statuscode (ebilling-r1 task 015,
    // D-41 / D-103). This handler once filtered the retired invoice-status choice column on 100000001-100000003, two of which it never had
    // (it had 100000000 ToReview and 100000001 Reviewed), so its totals counted only "Reviewed" invoices (#1771).

    public FinancialCalculationToolHandler(
        IDataverseService dataverseService,
        BillingDecisionRecorder recorder,
        FinanceTelemetry telemetry,
        ILogger<FinancialCalculationToolHandler> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes financial calculations for a matter or project.
    /// Queries all invoices and retrieves budget amount from Budget entity (direct lookup).
    /// </summary>
    /// <param name="parameters">Tool parameters: matterId OR projectId (mutually exclusive)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>PlaybookToolResult with financial totals (totalSpend, invoiceCount, budget, remainingBudget)</returns>
    public async Task<PlaybookToolResult> ExecuteAsync(ToolParameters parameters, CancellationToken ct)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Check for matterId or projectId (mutually exclusive)
            var hasMatter = parameters.TryGetGuid("matterId", out var matterId);
            var hasProject = parameters.TryGetGuid("projectId", out var projectId);

            if (!hasMatter && !hasProject)
            {
                return PlaybookToolResult.CreateError("Either 'matterId' or 'projectId' parameter is required.");
            }

            if (hasMatter && hasProject)
            {
                return PlaybookToolResult.CreateError("Cannot specify both 'matterId' and 'projectId'. Use one or the other.");
            }

            FinancialTotals totals;
            string entityType;
            Guid entityId;

            if (hasMatter)
            {
                entityType = "Matter";
                entityId = matterId;
                _logger.LogInformation("FinancialCalculation tool executing for matter {MatterId}", matterId);
                totals = await CalculateMatterFinancialTotalsAsync(matterId, ct);
            }
            else
            {
                entityType = "Project";
                entityId = projectId;
                _logger.LogInformation("FinancialCalculation tool executing for project {ProjectId}", projectId);
                totals = await CalculateProjectFinancialTotalsAsync(projectId, ct);
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "FinancialCalculation completed for {EntityType} {EntityId}: " +
                "TotalSpend={TotalSpend:C}, InvoiceCount={InvoiceCount}, " +
                "Budget={Budget:C}, RemainingBudget={RemainingBudget:C} (duration={Duration}ms)",
                entityType, entityId, totals.TotalSpendToDate, totals.InvoiceCount,
                totals.TotalBudget, totals.RemainingBudget, stopwatch.ElapsedMilliseconds);

            // Return as JSON for playbook consumption
            var resultJson = JsonSerializer.Serialize(new
            {
                totalSpend = totals.TotalSpendToDate,
                invoiceCount = totals.InvoiceCount,
                budget = totals.TotalBudget,
                remainingBudget = totals.RemainingBudget,
                budgetUtilization = totals.BudgetUtilizationPercent
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            return PlaybookToolResult.CreateSuccess(resultJson);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Financial calculation failed");
            return PlaybookToolResult.CreateError($"Financial calculation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Calculate financial totals for a matter by querying invoices and budget.
    /// </summary>
    private Task<FinancialTotals> CalculateMatterFinancialTotalsAsync(Guid matterId, CancellationToken ct) =>
        CalculateFinancialTotalsAsync(MatterEntity, Budget_Matter, matterId, ct);

    /// <summary>
    /// Calculate financial totals for a project by querying invoices and budget.
    /// </summary>
    private Task<FinancialTotals> CalculateProjectFinancialTotalsAsync(Guid projectId, CancellationToken ct) =>
        CalculateFinancialTotalsAsync(ProjectEntity, Budget_Project, projectId, ct);

    /// <summary>
    /// The parent's totals. Task 165b-4: the counted invoices come from the shared reader
    /// (<see cref="FinanceBaseValues.ReadCountedInvoicesAsync"/>: counted status AND the approve record), the same one the rollup and the Signal
    /// evaluator use, so the tool's totals agree with the matter form and decide only from server-written sources. Every read is app-only
    /// through <see cref="IGenericEntityService"/>. A fault (the writer id or any read) propagates and <see cref="ExecuteAsync"/> answers an error.
    /// </summary>
    private async Task<FinancialTotals> CalculateFinancialTotalsAsync(string parentEntity, string budgetLookup, Guid parentId, CancellationToken ct)
    {
        var writerUserId = await _recorder.WriterUserIdAsync(ct).ConfigureAwait(false);
        var invoices = await FinanceBaseValues.ReadCountedInvoicesAsync(_dataverseService, parentEntity, [parentId], writerUserId, ct).ConfigureAwait(false);

        var totalSpend = FinanceBaseValues.SumSpend(invoices);   // D-130: approved total, else billed total
        var invoiceCount = invoices.Count;

        _logger.LogDebug(
            "{ParentEntity} {ParentId} has {InvoiceCount} counted invoices with total spend {TotalSpend:C}",
            parentEntity, parentId, invoiceCount, totalSpend);

        var budget = await GetBudgetAmountAsync(parentEntity, budgetLookup, parentId, ct).ConfigureAwait(false);
        var budgetValue = budget ?? 0m;

        var remainingBudget = budgetValue - totalSpend;
        var budgetUtilization = budgetValue > 0 ? (totalSpend / budgetValue) * 100 : 0;

        return new FinancialTotals
        {
            TotalBudget = budgetValue,
            TotalSpendToDate = totalSpend,
            RemainingBudget = remainingBudget,
            BudgetUtilizationPercent = budgetUtilization,
            InvoiceCount = invoiceCount,
            AverageInvoiceAmount = invoiceCount > 0 ? totalSpend / invoiceCount : 0
        };
    }

    /// <summary>
    /// The sum of the parent's Budget.sprk_totalbudget values (a parent may span several budget cycles); null when it has no budget row.
    /// </summary>
    private async Task<decimal?> GetBudgetAmountAsync(string parentEntity, string budgetLookup, Guid parentId, CancellationToken ct)
    {
        var budgetQuery = new QueryExpression(BudgetEntity)
        {
            ColumnSet = new ColumnSet(Budget_TotalBudget),
            Criteria = new FilterExpression()
        };
        budgetQuery.Criteria.AddCondition(budgetLookup, ConditionOperator.Equal, parentId);

        var budgetResults = await _dataverseService.RetrieveMultipleAsync(budgetQuery, ct).ConfigureAwait(false);
        if (budgetResults.Entities.Count == 0)
        {
            _logger.LogDebug("No budget records found for {ParentEntity} {ParentId}.", parentEntity, parentId);
            return null;
        }

        var totalBudget = FinanceBaseValues.SumMoney(budgetResults.Entities, Budget_TotalBudget);
        _logger.LogDebug(
            "Budget amount for {ParentEntity} {ParentId}: {BudgetAmount:C} (from {BudgetCount} budget record(s))",
            parentEntity, parentId, totalBudget, budgetResults.Entities.Count);
        return totalBudget;
    }
}

/// <summary>
/// Financial totals calculated for a matter or project.
/// </summary>
public class FinancialTotals
{
    public decimal TotalBudget { get; set; }
    public decimal TotalSpendToDate { get; set; }
    public decimal RemainingBudget { get; set; }
    public decimal BudgetUtilizationPercent { get; set; }
    public int InvoiceCount { get; set; }
    public decimal AverageInvoiceAmount { get; set; }
}
