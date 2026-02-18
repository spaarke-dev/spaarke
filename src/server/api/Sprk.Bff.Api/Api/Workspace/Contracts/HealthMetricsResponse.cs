namespace Sprk.Bff.Api.Api.Workspace.Contracts;

/// <summary>
/// Response DTO for the Portfolio Health Summary endpoint.
/// </summary>
/// <param name="MattersAtRisk">Count of matters where overdue events exist or utilization exceeds 85%.</param>
/// <param name="OverdueEvents">Total count of overdue events across all active matters.</param>
/// <param name="ActiveMatters">Count of matters with an active/open status.</param>
/// <param name="BudgetUtilizationPercent">Portfolio-level spend / budget expressed as a percentage (0 when budget is 0).</param>
/// <param name="PortfolioSpend">Sum of all invoiced amounts across active matters.</param>
/// <param name="PortfolioBudget">Sum of all budget amounts across active matters.</param>
/// <param name="Timestamp">UTC timestamp when this data was generated and cached.</param>
public record HealthMetricsResponse(
    int MattersAtRisk,
    int OverdueEvents,
    int ActiveMatters,
    decimal BudgetUtilizationPercent,
    decimal PortfolioSpend,
    decimal PortfolioBudget,
    DateTimeOffset Timestamp);
