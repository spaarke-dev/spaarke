using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Telemetry;
using System.Text.Json;

namespace Sprk.Bff.Api.Services.Finance.Tools;

/// <summary>
/// Tool handler that calculates and aggregates financial metrics for matters.
/// Queries Dataverse to compute total spend, invoice count, budget, and remaining budget.
/// </summary>
public class FinancialCalculationToolHandler : IAiToolHandler
{
    private readonly IDataverseService _dataverseService;
    private readonly FinanceTelemetry _telemetry;
    private readonly ILogger<FinancialCalculationToolHandler> _logger;

    public const string ToolNameConst = "FinancialCalculation";
    public string ToolName => ToolNameConst;

    // Entity and field constants
    private const string InvoiceEntity = "sprk_invoice";
    private const string Invoice_Matter = "sprk_matter";
    private const string Invoice_TotalAmount = "sprk_totalamount";
    private const string Invoice_InvoiceStatus = "sprk_invoicestatus";

    private const string MatterEntity = "sprk_matter";

    private const string BudgetEntity = "sprk_budget";
    private const string Budget_Matter = "sprk_matter";
    private const string Budget_TotalBudget = "sprk_totalbudget";

    // Only count invoices that are not rejected/cancelled
    private const int InvoiceStatus_Confirmed = 100000001;
    private const int InvoiceStatus_Extracted = 100000002;
    private const int InvoiceStatus_Processed = 100000003;

    public FinancialCalculationToolHandler(
        IDataverseService dataverseService,
        FinanceTelemetry telemetry,
        ILogger<FinancialCalculationToolHandler> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes financial calculations for a matter.
    /// Queries all invoices for the matter and retrieves budget amount from Budget entity (direct lookup).
    /// </summary>
    /// <param name="parameters">Tool parameters: matterId (required)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>PlaybookToolResult with financial totals (totalSpend, invoiceCount, budget, remainingBudget)</returns>
    public async Task<PlaybookToolResult> ExecuteAsync(ToolParameters parameters, CancellationToken ct)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var matterId = parameters.GetGuid("matterId");

            _logger.LogInformation(
                "FinancialCalculation tool executing for matter {MatterId}",
                matterId);

            // Calculate financial totals
            var totals = await CalculateMatterFinancialTotalsAsync(matterId, ct);

            stopwatch.Stop();

            _logger.LogInformation(
                "FinancialCalculation completed for matter {MatterId}: " +
                "TotalSpend={TotalSpend:C}, InvoiceCount={InvoiceCount}, " +
                "Budget={Budget:C}, RemainingBudget={RemainingBudget:C} (duration={Duration}ms)",
                matterId, totals.TotalSpendToDate, totals.InvoiceCount,
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
    private async Task<MatterFinancialTotals> CalculateMatterFinancialTotalsAsync(
        Guid matterId,
        CancellationToken ct)
    {
        // Resolve to ServiceClient for QueryExpression support
        var serviceClient = _dataverseService as ServiceClient;
        if (serviceClient == null)
        {
            throw new InvalidOperationException(
                "FinancialCalculationToolHandler requires IDataverseService resolved as ServiceClient " +
                "for QueryExpression support. Ensure FinanceModule registers DataverseServiceClientImpl.");
        }

        // Query all invoices for this matter
        var invoiceQuery = new QueryExpression(InvoiceEntity)
        {
            ColumnSet = new ColumnSet(Invoice_TotalAmount, Invoice_InvoiceStatus),
            Criteria = new FilterExpression(LogicalOperator.And)
        };
        invoiceQuery.Criteria.AddCondition(Invoice_Matter, ConditionOperator.Equal, matterId);
        invoiceQuery.Criteria.AddCondition(
            Invoice_InvoiceStatus,
            ConditionOperator.In,
            InvoiceStatus_Confirmed,
            InvoiceStatus_Extracted,
            InvoiceStatus_Processed);

        var invoiceResults = await Task.Run(() => serviceClient.RetrieveMultiple(invoiceQuery), ct);

        decimal totalSpend = 0;
        int invoiceCount = invoiceResults.Entities.Count;

        foreach (var invoice in invoiceResults.Entities)
        {
            var amount = invoice.GetAttributeValue<Money>(Invoice_TotalAmount);
            if (amount != null)
            {
                totalSpend += amount.Value;
            }
        }

        _logger.LogDebug(
            "Matter {MatterId} has {InvoiceCount} invoices with total spend {TotalSpend:C}",
            matterId, invoiceCount, totalSpend);

        // Get budget amount using same pattern as SpendSnapshotService
        var budget = await GetBudgetAmountForMatterAsync(serviceClient, matterId, ct);
        var budgetValue = budget ?? 0m;

        var remainingBudget = budgetValue - totalSpend;
        var budgetUtilization = budgetValue > 0 ? (totalSpend / budgetValue) * 100 : 0;

        return new MatterFinancialTotals
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
    /// Look up budget amount for the matter from Budget entity.
    /// Matter can have multiple Budget records (spanning budget cycles/time periods).
    /// Returns the sum of all Budget.sprk_totalbudget values for this matter.
    /// </summary>
    private async Task<decimal?> GetBudgetAmountForMatterAsync(
        ServiceClient serviceClient,
        Guid matterId,
        CancellationToken ct)
    {
        // Query ALL Budget records for this matter
        var budgetQuery = new QueryExpression(BudgetEntity)
        {
            ColumnSet = new ColumnSet(Budget_TotalBudget),
            Criteria = new FilterExpression()
        };
        budgetQuery.Criteria.AddCondition(Budget_Matter, ConditionOperator.Equal, matterId);

        var budgetResults = await Task.Run(() => serviceClient.RetrieveMultiple(budgetQuery), ct);
        if (budgetResults.Entities.Count == 0)
        {
            _logger.LogDebug("No budget records found for matter {MatterId}.", matterId);
            return null;
        }

        // Sum all budget amounts (matter may span multiple budget cycles)
        decimal totalBudget = 0;
        foreach (var budget in budgetResults.Entities)
        {
            var amount = budget.GetAttributeValue<Money>(Budget_TotalBudget);
            if (amount != null)
            {
                totalBudget += amount.Value;
            }
        }

        _logger.LogDebug(
            "Budget amount for matter {MatterId}: {BudgetAmount:C} (from {BudgetCount} budget record(s))",
            matterId, totalBudget, budgetResults.Entities.Count);

        return totalBudget;
    }
}

/// <summary>
/// Financial totals calculated for a matter or project.
/// </summary>
public class MatterFinancialTotals
{
    public decimal TotalBudget { get; set; }
    public decimal TotalSpendToDate { get; set; }
    public decimal RemainingBudget { get; set; }
    public decimal BudgetUtilizationPercent { get; set; }
    public int InvoiceCount { get; set; }
    public decimal AverageInvoiceAmount { get; set; }
}
