using Spaarke.Dataverse;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Services;

/// <summary>
/// Calculates performance scorecard grades for matters and projects by querying Dataverse
/// for KPI assessment records across three performance areas.
/// </summary>
/// <remarks>
/// Follows ADR-010: Registered as concrete type (no interface) unless a seam is required.
/// Implements tasks 011 (current grade), 012 (historical average), 013 (trend data).
/// Supports both sprk_matter and sprk_project entities via parentLookupField parameter.
/// NFR-06: Calculator logic kept under 250 lines of code.
/// </remarks>
public sealed class ScorecardCalculatorService
{
    /// <summary>Performance area choice values from sprk_performancearea (Dataverse local option set).</summary>
    private static class PerformanceArea
    {
        public const int Guidelines = 100000000;
        public const int Budget = 100000001;
        public const int Outcomes = 100000002;
    }

    /// <summary>Number of recent assessments to include in trend data for sparkline graphs.</summary>
    private const int TrendCount = 5;

    private readonly IDataverseService _dataverseService;
    private readonly ILogger<ScorecardCalculatorService> _logger;

    public ScorecardCalculatorService(
        IDataverseService dataverseService,
        ILogger<ScorecardCalculatorService> logger)
    {
        _dataverseService = dataverseService;
        _logger = logger;
    }

    /// <summary>
    /// Recalculates all performance grades for the specified matter.
    /// </summary>
    public Task<RecalculateGradesResponse> RecalculateGradesAsync(
        Guid matterId,
        CancellationToken ct = default)
    {
        return RecalculateGradesForEntityAsync(matterId, "sprk_matter", "sprk_matter", ct);
    }

    /// <summary>
    /// Recalculates all performance grades for the specified project.
    /// </summary>
    public Task<RecalculateGradesResponse> RecalculateProjectGradesAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        return RecalculateGradesForEntityAsync(projectId, "sprk_project", "sprk_project", ct);
    }

    /// <summary>
    /// Core recalculation logic shared by matter and project endpoints.
    /// Queries KPI assessments per area, computes current grade, historical average,
    /// and trend data, then persists current/average grades back to the parent record.
    /// </summary>
    private async Task<RecalculateGradesResponse> RecalculateGradesForEntityAsync(
        Guid parentId,
        string parentLookupField,
        string parentEntityName,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Recalculating scorecard grades for {EntityName} {ParentId}",
            parentEntityName, parentId);

        // Query all assessments per area in parallel for efficiency (NFR-01: < 500ms)
        var guidelinesTask = _dataverseService.QueryKpiAssessmentsAsync(parentId, parentLookupField, PerformanceArea.Guidelines, ct: ct);
        var budgetTask = _dataverseService.QueryKpiAssessmentsAsync(parentId, parentLookupField, PerformanceArea.Budget, ct: ct);
        var outcomesTask = _dataverseService.QueryKpiAssessmentsAsync(parentId, parentLookupField, PerformanceArea.Outcomes, ct: ct);

        await Task.WhenAll(guidelinesTask, budgetTask, outcomesTask);

        var guidelineAssessments = guidelinesTask.Result;
        var budgetAssessments = budgetTask.Result;
        var outcomeAssessments = outcomesTask.Result;

        // Task 011: Current grades (latest assessment per area)
        var guidelineCurrent = GetCurrentGrade(guidelineAssessments);
        var budgetCurrent = GetCurrentGrade(budgetAssessments);
        var outcomeCurrent = GetCurrentGrade(outcomeAssessments);

        // Task 012: Historical averages (mean of all assessments per area)
        var guidelineAverage = GetHistoricalAverage(guidelineAssessments);
        var budgetAverage = GetHistoricalAverage(budgetAssessments);
        var outcomeAverage = GetHistoricalAverage(outcomeAssessments);

        // Task 013: Trend data (last 5 assessments, chronological order)
        var guidelineTrend = GetTrendData(guidelineAssessments);
        var budgetTrend = GetTrendData(budgetAssessments);
        var outcomeTrend = GetTrendData(outcomeAssessments);

        // Persist current and average grades to the parent record
        await UpdateEntityGradesAsync(parentId, parentEntityName, guidelineCurrent, guidelineAverage,
            budgetCurrent, budgetAverage, outcomeCurrent, outcomeAverage, ct);

        _logger.LogInformation(
            "Scorecard recalculation complete for {EntityName} {ParentId}: " +
            "Guidelines={GuidelineCurrent}/{GuidelineAvg}, " +
            "Budget={BudgetCurrent}/{BudgetAvg}, " +
            "Outcomes={OutcomeCurrent}/{OutcomeAvg}",
            parentEntityName, parentId,
            guidelineCurrent, guidelineAverage,
            budgetCurrent, budgetAverage,
            outcomeCurrent, outcomeAverage);

        return new RecalculateGradesResponse
        {
            GuidelineCurrent = guidelineCurrent,
            GuidelineAverage = guidelineAverage,
            GuidelineTrend = guidelineTrend,
            BudgetCurrent = budgetCurrent,
            BudgetAverage = budgetAverage,
            BudgetTrend = budgetTrend,
            OutcomeCurrent = outcomeCurrent,
            OutcomeAverage = outcomeAverage,
            OutcomeTrend = outcomeTrend
        };
    }

    /// <summary>
    /// Task 011: Gets the current (most recent) grade for a performance area.
    /// Assessments are already ordered by createdon DESC, so index 0 is the latest.
    /// </summary>
    private static decimal? GetCurrentGrade(KpiAssessmentRecord[] assessments)
    {
        if (assessments.Length == 0)
            return null;

        return GradeToDecimal(assessments[0].Grade);
    }

    /// <summary>
    /// Task 012: Calculates the arithmetic mean of all assessment grades for a performance area.
    /// Rounds to 2 decimal places for display precision.
    /// </summary>
    private static decimal? GetHistoricalAverage(KpiAssessmentRecord[] assessments)
    {
        if (assessments.Length == 0)
            return null;

        var average = assessments.Average(a => GradeToDecimal(a.Grade));
        return Math.Round(average, 2);
    }

    /// <summary>
    /// Task 013: Gets the last N assessment grades in chronological order (oldest to newest)
    /// for sparkline visualization. Assessments arrive ordered DESC, so we take the first
    /// TrendCount and reverse to chronological order.
    /// </summary>
    private static decimal?[] GetTrendData(KpiAssessmentRecord[] assessments)
    {
        if (assessments.Length == 0)
            return [];

        return assessments
            .Take(TrendCount)
            .Reverse()
            .Select(a => (decimal?)GradeToDecimal(a.Grade))
            .ToArray();
    }

    /// <summary>
    /// Converts a Dataverse grade choice value to a decimal (0.00-1.00).
    /// Dataverse stores sequential option set values (100000000-100000009).
    /// Maps: 100000000=A+(1.00), 100000001=A(0.95), 100000002=B+(0.90),
    ///       100000003=B(0.85), 100000004=C+(0.80), 100000005=C(0.75),
    ///       100000006=D+(0.70), 100000007=D(0.65), 100000008=F(0.60),
    ///       100000009=No Grade(0.00).
    /// </summary>
    private static decimal GradeToDecimal(int gradeChoiceValue)
    {
        return gradeChoiceValue switch
        {
            100000000 => 1.00m,  // A+
            100000001 => 0.95m,  // A
            100000002 => 0.90m,  // B+
            100000003 => 0.85m,  // B
            100000004 => 0.80m,  // C+
            100000005 => 0.75m,  // C
            100000006 => 0.70m,  // D+
            100000007 => 0.65m,  // D
            100000008 => 0.60m,  // F
            100000009 => 0.00m,  // No Grade
            _ => 0.00m           // Unknown value fallback
        };
    }

    /// <summary>
    /// Persists the computed current and average grades to the parent entity (matter or project).
    /// Both sprk_matter and sprk_project use the same 6 grade field logical names.
    /// </summary>
    private async Task UpdateEntityGradesAsync(
        Guid parentId,
        string entityName,
        decimal? guidelineCurrent, decimal? guidelineAverage,
        decimal? budgetCurrent, decimal? budgetAverage,
        decimal? outcomeCurrent, decimal? outcomeAverage,
        CancellationToken ct)
    {
        var fields = new Dictionary<string, object?>
        {
            ["sprk_guidelinecompliancegrade_current"] = guidelineCurrent,
            ["sprk_guidelinecompliancegrade_average"] = guidelineAverage,
            ["sprk_budgetcompliancegrade_current"] = budgetCurrent,
            ["sprk_budgetcompliancegrade_average"] = budgetAverage,
            ["sprk_outcomecompliancegrade_current"] = outcomeCurrent,
            ["sprk_outcomecompliancegrade_average"] = outcomeAverage
        };

        await _dataverseService.UpdateRecordFieldsAsync(entityName, parentId, fields, ct);

        _logger.LogDebug("Updated {EntityName} {ParentId} grade fields", entityName, parentId);
    }
}
