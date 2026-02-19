using System.Text;

namespace Sprk.Bff.Api.Services.Workspace;

public record PriorityScoreInput(
    int OverdueDays,
    decimal BudgetUtilizationPercent,
    int GradesBelowC,
    int? DaysToDeadline,
    string MatterValueTier,
    int PendingInvoiceCount);

public record PriorityScoreResult(
    int Score,
    string Level,
    IReadOnlyList<PriorityFactorResult> Factors,
    string ReasonString);

public record PriorityFactorResult(
    string FactorName,
    int Points,
    string Explanation);

/// <summary>
/// Deterministic priority scoring engine for Legal Operations Workspace matters.
/// All scoring is table-driven; same inputs always produce identical outputs.
/// Registered as a concrete type per ADR-010 (DI minimalism).
/// </summary>
public class PriorityScoringService
{
    // ──────────────────────────────────────────────────────────────────────────
    // Scoring tables
    // ──────────────────────────────────────────────────────────────────────────

    private static int ScoreOverdueDaysPoints(int overdueDays) => overdueDays switch
    {
        0 => 0,
        >= 15 => 30,
        >= 8 => 20,
        >= 1 => 10,
        _ => 0
    };

    private static int ScoreBudgetUtilizationPoints(decimal utilization) => utilization switch
    {
        > 85m => 20,
        >= 65m => 10,
        _ => 0
    };

    private static int ScoreGradesBelowCPoints(int gradesBelowC) =>
        Math.Min(gradesBelowC, 3) * 5;

    private static int ScoreDeadlineProximityPoints(int? daysToDeadline) => daysToDeadline switch
    {
        null => 0,
        > 30 => 0,
        >= 14 => 5,
        >= 7 => 10,
        _ => 15    // 0-6 days
    };

    private static int ScoreMatterValueTierPoints(string tier) => tier?.ToUpperInvariant() switch
    {
        "HIGH" => 10,
        "MEDIUM" => 5,
        _ => 0      // "Low" or any unknown value
    };

    private static int ScorePendingInvoicesPoints(int invoiceCount) => invoiceCount switch
    {
        0 => 0,
        <= 2 => 5,
        _ => 10
    };

    // ──────────────────────────────────────────────────────────────────────────
    // Factor builders (produce human-readable explanations)
    // ──────────────────────────────────────────────────────────────────────────

    private static PriorityFactorResult ScoreOverdueDays(int overdueDays)
    {
        var points = ScoreOverdueDaysPoints(overdueDays);
        var explanation = overdueDays == 0
            ? "Not overdue"
            : $"{overdueDays} day{(overdueDays == 1 ? "" : "s")} overdue";
        return new PriorityFactorResult("Overdue Days", points, explanation);
    }

    private static PriorityFactorResult ScoreBudgetUtilization(decimal utilization)
    {
        var points = ScoreBudgetUtilizationPoints(utilization);
        var explanation = $"Budget at {utilization:0.#}%";
        return new PriorityFactorResult("Budget Utilization", points, explanation);
    }

    private static PriorityFactorResult ScoreGradesBelowC(int gradesBelowC)
    {
        var points = ScoreGradesBelowCPoints(gradesBelowC);
        var explanation = gradesBelowC == 0
            ? "No grades below C"
            : $"{gradesBelowC} grade{(gradesBelowC == 1 ? "" : "s")} below C";
        return new PriorityFactorResult("Grades Below C", points, explanation);
    }

    private static PriorityFactorResult ScoreDeadlineProximity(int? daysToDeadline)
    {
        var points = ScoreDeadlineProximityPoints(daysToDeadline);
        var explanation = daysToDeadline.HasValue
            ? $"{daysToDeadline.Value} day{(daysToDeadline.Value == 1 ? "" : "s")} to deadline"
            : "No deadline";
        return new PriorityFactorResult("Deadline Proximity", points, explanation);
    }

    private static PriorityFactorResult ScoreMatterValueTier(string tier)
    {
        var points = ScoreMatterValueTierPoints(tier);
        var explanation = $"{(string.IsNullOrWhiteSpace(tier) ? "Unknown" : tier)} value matter";
        return new PriorityFactorResult("Matter Value Tier", points, explanation);
    }

    private static PriorityFactorResult ScorePendingInvoices(int invoiceCount)
    {
        var points = ScorePendingInvoicesPoints(invoiceCount);
        var explanation = invoiceCount == 0
            ? "No pending invoices"
            : $"{invoiceCount} pending invoice{(invoiceCount == 1 ? "" : "s")}";
        return new PriorityFactorResult("Pending Invoices", points, explanation);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Level and reason string
    // ──────────────────────────────────────────────────────────────────────────

    private static string GetPriorityLevel(int score) => score switch
    {
        >= 80 => "Critical",
        >= 60 => "High",
        >= 30 => "Medium",
        _ => "Low"
    };

    private static string BuildReasonString(
        IEnumerable<PriorityFactorResult> factors,
        int totalScore,
        string level)
    {
        var nonZero = factors
            .Where(f => f.Points > 0)
            .Select(f => $"{f.Explanation} (+{f.Points})")
            .ToList();

        if (nonZero.Count == 0)
            return $"No contributing factors = {totalScore} ({level})";

        return $"{string.Join(", ", nonZero)} = {totalScore} ({level})";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────────────

    public PriorityScoreResult CalculatePriorityScore(PriorityScoreInput input)
    {
        var factors = new List<PriorityFactorResult>
        {
            ScoreOverdueDays(input.OverdueDays),
            ScoreBudgetUtilization(input.BudgetUtilizationPercent),
            ScoreGradesBelowC(input.GradesBelowC),
            ScoreDeadlineProximity(input.DaysToDeadline),
            ScoreMatterValueTier(input.MatterValueTier),
            ScorePendingInvoices(input.PendingInvoiceCount)
        };

        var totalScore = Math.Min(factors.Sum(f => f.Points), 100);
        var level = GetPriorityLevel(totalScore);
        var reasonString = BuildReasonString(factors, totalScore, level);

        return new PriorityScoreResult(totalScore, level, factors, reasonString);
    }
}
