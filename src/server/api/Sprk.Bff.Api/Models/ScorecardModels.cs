namespace Sprk.Bff.Api.Models;

/// <summary>
/// Response DTO for the scorecard recalculation endpoint.
/// Contains current grades, averages, and trend data for each KPI dimension.
/// </summary>
public sealed record RecalculateGradesResponse
{
    /// <summary>Current guideline compliance grade (0-100 scale, null if insufficient data).</summary>
    public decimal? GuidelineCurrent { get; init; }

    /// <summary>Rolling average of guideline compliance grades.</summary>
    public decimal? GuidelineAverage { get; init; }

    /// <summary>Historical trend of guideline compliance grades (most recent last).</summary>
    public decimal?[] GuidelineTrend { get; init; } = [];

    /// <summary>Current budget adherence grade (0-100 scale, null if insufficient data).</summary>
    public decimal? BudgetCurrent { get; init; }

    /// <summary>Rolling average of budget adherence grades.</summary>
    public decimal? BudgetAverage { get; init; }

    /// <summary>Historical trend of budget adherence grades (most recent last).</summary>
    public decimal?[] BudgetTrend { get; init; } = [];

    /// <summary>Current outcome/result grade (0-100 scale, null if insufficient data).</summary>
    public decimal? OutcomeCurrent { get; init; }

    /// <summary>Rolling average of outcome grades.</summary>
    public decimal? OutcomeAverage { get; init; }

    /// <summary>Historical trend of outcome grades (most recent last).</summary>
    public decimal?[] OutcomeTrend { get; init; } = [];
}
