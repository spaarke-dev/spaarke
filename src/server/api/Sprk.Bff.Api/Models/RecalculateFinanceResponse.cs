namespace Sprk.Bff.Api.Models;

/// <summary>
/// Response DTO for the finance recalculation endpoint.
/// Contains all denormalized financial metrics written to the parent Matter/Project record.
/// </summary>
public sealed record RecalculateFinanceResponse
{
    /// <summary>Lifetime total spend across all invoices (Currency).</summary>
    public decimal TotalSpendToDate { get; init; }

    /// <summary>Count of confirmed/extracted/processed invoices.</summary>
    public int InvoiceCount { get; init; }

    /// <summary>Spend for the current calendar month (Currency).</summary>
    public decimal MonthlySpendCurrent { get; init; }

    /// <summary>Sum of all Budget records for this entity (Currency).</summary>
    public decimal TotalBudget { get; init; }

    /// <summary>TotalBudget - TotalSpendToDate (Currency, negative = over budget).</summary>
    public decimal RemainingBudget { get; init; }

    /// <summary>Budget utilization percentage: (TotalSpendToDate / TotalBudget) * 100.</summary>
    public decimal BudgetUtilizationPercent { get; init; }

    /// <summary>Month-over-month velocity: ((currentMonth - priorMonth) / priorMonth) * 100.</summary>
    public decimal MonthOverMonthVelocity { get; init; }

    /// <summary>Average invoice amount: TotalSpendToDate / InvoiceCount.</summary>
    public decimal AverageInvoiceAmount { get; init; }

    /// <summary>JSON array of monthly spend data (last 12 months) for sparkline/timeline display.</summary>
    public string MonthlySpendTimeline { get; init; } = "[]";
}
