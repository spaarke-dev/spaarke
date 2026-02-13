using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services;

/// <summary>
/// Integration tests for <see cref="ScorecardCalculatorService"/> validating end-to-end flows:
/// create assessments, run calculator, verify grades, averages, trends, and matter updates.
/// Validates acceptance criteria from task 050.
/// </summary>
public class ScorecardCalculatorIntegrationTests
{
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<ScorecardCalculatorService>> _loggerMock;
    private readonly ScorecardCalculatorService _service;

    // Performance area constants matching ScorecardCalculatorService.PerformanceArea
    private const int Guidelines = 1;
    private const int Budget = 2;
    private const int Outcomes = 3;

    public ScorecardCalculatorIntegrationTests()
    {
        _dataverseServiceMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<ScorecardCalculatorService>>();
        _service = new ScorecardCalculatorService(_dataverseServiceMock.Object, _loggerMock.Object);
    }

    #region Helper Methods

    /// <summary>
    /// Creates a KpiAssessmentRecord with the given grade and optional created date.
    /// </summary>
    private static KpiAssessmentRecord CreateAssessment(int grade, DateTime? createdOn = null)
    {
        return new KpiAssessmentRecord
        {
            Id = Guid.NewGuid(),
            Grade = grade,
            CreatedOn = createdOn ?? DateTime.UtcNow
        };
    }

    /// <summary>
    /// Sets up the mock to return the given assessments for a specific performance area.
    /// </summary>
    private void SetupAreaAssessments(Guid matterId, int performanceArea, params KpiAssessmentRecord[] assessments)
    {
        _dataverseServiceMock
            .Setup(s => s.QueryKpiAssessmentsAsync(matterId, performanceArea, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessments);
    }

    /// <summary>
    /// Sets up the mock to return empty arrays for all three performance areas.
    /// </summary>
    private void SetupAllAreasEmpty(Guid matterId)
    {
        SetupAreaAssessments(matterId, Guidelines);
        SetupAreaAssessments(matterId, Budget);
        SetupAreaAssessments(matterId, Outcomes);
    }

    /// <summary>
    /// Generates an array of assessments with sequential grades, ordered DESC (newest first).
    /// </summary>
    private static KpiAssessmentRecord[] GenerateAssessments(int count, int startGrade = 80, int gradeStep = 5)
    {
        var assessments = new KpiAssessmentRecord[count];
        for (var i = 0; i < count; i++)
        {
            // Index 0 = newest (highest grade), index N = oldest (lowest grade)
            var grade = startGrade + ((count - 1 - i) * gradeStep);
            grade = Math.Min(grade, 100); // Cap at 100
            assessments[i] = CreateAssessment(grade, DateTime.UtcNow.AddDays(-i * 10));
        }
        return assessments;
    }

    #endregion

    #region End-to-End: Single Assessment Per Area

    [Fact]
    public async Task EndToEnd_SingleAssessment_ReturnsCorrectGrades()
    {
        // Arrange - one assessment per area with different grades
        var matterId = Guid.NewGuid();
        SetupAreaAssessments(matterId, Guidelines, CreateAssessment(95));
        SetupAreaAssessments(matterId, Budget, CreateAssessment(85));
        SetupAreaAssessments(matterId, Outcomes, CreateAssessment(100));

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - current grades match the single assessment per area
        result.GuidelineCurrent.Should().Be(0.95m, "single Guidelines assessment of 95 should yield 0.95");
        result.BudgetCurrent.Should().Be(0.85m, "single Budget assessment of 85 should yield 0.85");
        result.OutcomeCurrent.Should().Be(1.00m, "single Outcomes assessment of 100 should yield 1.00");

        // Assert - averages equal current when there is only one assessment
        result.GuidelineAverage.Should().Be(0.95m);
        result.BudgetAverage.Should().Be(0.85m);
        result.OutcomeAverage.Should().Be(1.00m);

        // Assert - trend contains single data point per area
        result.GuidelineTrend.Should().ContainSingle().Which.Should().Be(0.95m);
        result.BudgetTrend.Should().ContainSingle().Which.Should().Be(0.85m);
        result.OutcomeTrend.Should().ContainSingle().Which.Should().Be(1.00m);
    }

    #endregion

    #region End-to-End: Multiple Assessments with Averages and Trends

    [Fact]
    public async Task EndToEnd_MultipleAssessments_ReturnsAveragesAndTrends()
    {
        // Arrange - 5 assessments per area, ordered DESC (newest first)
        var matterId = Guid.NewGuid();

        // Guidelines: grades 95, 90, 85, 80, 75 (newest to oldest)
        var guidelineAssessments = new[]
        {
            CreateAssessment(95, DateTime.UtcNow),
            CreateAssessment(90, DateTime.UtcNow.AddDays(-10)),
            CreateAssessment(85, DateTime.UtcNow.AddDays(-20)),
            CreateAssessment(80, DateTime.UtcNow.AddDays(-30)),
            CreateAssessment(75, DateTime.UtcNow.AddDays(-40))
        };
        SetupAreaAssessments(matterId, Guidelines, guidelineAssessments);

        // Budget: grades 100, 90, 80, 70, 60 (newest to oldest)
        var budgetAssessments = new[]
        {
            CreateAssessment(100, DateTime.UtcNow),
            CreateAssessment(90, DateTime.UtcNow.AddDays(-10)),
            CreateAssessment(80, DateTime.UtcNow.AddDays(-20)),
            CreateAssessment(70, DateTime.UtcNow.AddDays(-30)),
            CreateAssessment(60, DateTime.UtcNow.AddDays(-40))
        };
        SetupAreaAssessments(matterId, Budget, budgetAssessments);

        // Outcomes: grades 85, 85, 85, 85, 85 (all same, newest to oldest)
        var outcomeAssessments = new[]
        {
            CreateAssessment(85, DateTime.UtcNow),
            CreateAssessment(85, DateTime.UtcNow.AddDays(-10)),
            CreateAssessment(85, DateTime.UtcNow.AddDays(-20)),
            CreateAssessment(85, DateTime.UtcNow.AddDays(-30)),
            CreateAssessment(85, DateTime.UtcNow.AddDays(-40))
        };
        SetupAreaAssessments(matterId, Outcomes, outcomeAssessments);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - current grades (first/newest assessment)
        result.GuidelineCurrent.Should().Be(0.95m);
        result.BudgetCurrent.Should().Be(1.00m);
        result.OutcomeCurrent.Should().Be(0.85m);

        // Assert - averages
        // Guidelines: (0.95+0.90+0.85+0.80+0.75)/5 = 0.85
        result.GuidelineAverage.Should().Be(0.85m);
        // Budget: (1.00+0.90+0.80+0.70+0.60)/5 = 0.80
        result.BudgetAverage.Should().Be(0.80m);
        // Outcomes: all 0.85 -> average = 0.85
        result.OutcomeAverage.Should().Be(0.85m);

        // Assert - trend data (reversed to chronological: oldest to newest)
        result.GuidelineTrend.Should().HaveCount(5);
        result.GuidelineTrend.Should().ContainInOrder(0.75m, 0.80m, 0.85m, 0.90m, 0.95m);

        result.BudgetTrend.Should().HaveCount(5);
        result.BudgetTrend.Should().ContainInOrder(0.60m, 0.70m, 0.80m, 0.90m, 1.00m);

        result.OutcomeTrend.Should().HaveCount(5);
        result.OutcomeTrend.Should().ContainInOrder(0.85m, 0.85m, 0.85m, 0.85m, 0.85m);
    }

    #endregion

    #region End-to-End: No Assessments

    [Fact]
    public async Task EndToEnd_NoAssessments_ReturnsNullGrades()
    {
        // Arrange - no assessments for any area
        var matterId = Guid.NewGuid();
        SetupAllAreasEmpty(matterId);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - all current grades are null
        result.GuidelineCurrent.Should().BeNull("no Guidelines assessments should yield null current grade");
        result.BudgetCurrent.Should().BeNull("no Budget assessments should yield null current grade");
        result.OutcomeCurrent.Should().BeNull("no Outcomes assessments should yield null current grade");

        // Assert - all averages are null
        result.GuidelineAverage.Should().BeNull();
        result.BudgetAverage.Should().BeNull();
        result.OutcomeAverage.Should().BeNull();

        // Assert - all trend arrays are empty
        result.GuidelineTrend.Should().BeEmpty();
        result.BudgetTrend.Should().BeEmpty();
        result.OutcomeTrend.Should().BeEmpty();
    }

    #endregion

    #region End-to-End: Verify Matter Update Called

    [Fact]
    public async Task EndToEnd_VerifiesMatterUpdateCalled()
    {
        // Arrange - set up assessments so we can verify the update values
        var matterId = Guid.NewGuid();
        SetupAreaAssessments(matterId, Guidelines, CreateAssessment(90));
        SetupAreaAssessments(matterId, Budget, CreateAssessment(80));
        SetupAreaAssessments(matterId, Outcomes, CreateAssessment(100));

        Dictionary<string, object?>? capturedFields = null;
        _dataverseServiceMock
            .Setup(s => s.UpdateRecordFieldsAsync(
                "sprk_matter",
                matterId,
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken>(
                (_, _, fields, _) => capturedFields = fields)
            .Returns(Task.CompletedTask);

        // Act
        await _service.RecalculateGradesAsync(matterId);

        // Assert - UpdateRecordFieldsAsync was called exactly once
        _dataverseServiceMock.Verify(
            s => s.UpdateRecordFieldsAsync(
                "sprk_matter",
                matterId,
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert - captured fields contain correct current and average values
        capturedFields.Should().NotBeNull();
        capturedFields.Should().HaveCount(6);
        capturedFields!["sprk_guidelinecompliancegrade_current"].Should().Be(0.90m);
        capturedFields["sprk_guidelinecompliancegrade_average"].Should().Be(0.90m);
        capturedFields["sprk_budgetcompliancegrade_current"].Should().Be(0.80m);
        capturedFields["sprk_budgetcompliancegrade_average"].Should().Be(0.80m);
        capturedFields["sprk_outcomecompliancegrade_current"].Should().Be(1.00m);
        capturedFields["sprk_outcomecompliancegrade_average"].Should().Be(1.00m);
    }

    #endregion

    #region End-to-End: Mixed Areas Calculated Independently

    [Fact]
    public async Task EndToEnd_MixedAreas_CalculatesIndependently()
    {
        // Arrange - different assessment counts per area to verify independent calculation
        var matterId = Guid.NewGuid();

        // Guidelines: 5 assessments (full trend window)
        SetupAreaAssessments(matterId, Guidelines,
            CreateAssessment(100, DateTime.UtcNow),
            CreateAssessment(95, DateTime.UtcNow.AddDays(-10)),
            CreateAssessment(90, DateTime.UtcNow.AddDays(-20)),
            CreateAssessment(85, DateTime.UtcNow.AddDays(-30)),
            CreateAssessment(80, DateTime.UtcNow.AddDays(-40)));

        // Budget: 2 assessments (partial trend)
        SetupAreaAssessments(matterId, Budget,
            CreateAssessment(75, DateTime.UtcNow),
            CreateAssessment(70, DateTime.UtcNow.AddDays(-30)));

        // Outcomes: 0 assessments (empty)
        SetupAreaAssessments(matterId, Outcomes);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - Guidelines: full data
        result.GuidelineCurrent.Should().Be(1.00m);
        // (1.00+0.95+0.90+0.85+0.80)/5 = 0.90
        result.GuidelineAverage.Should().Be(0.90m);
        result.GuidelineTrend.Should().HaveCount(5);
        result.GuidelineTrend.Should().ContainInOrder(0.80m, 0.85m, 0.90m, 0.95m, 1.00m);

        // Assert - Budget: partial data
        result.BudgetCurrent.Should().Be(0.75m);
        // (0.75+0.70)/2 = 0.725 -> Math.Round uses banker's rounding -> 0.72
        result.BudgetAverage.Should().Be(0.72m);
        result.BudgetTrend.Should().HaveCount(2);
        result.BudgetTrend.Should().ContainInOrder(0.70m, 0.75m);

        // Assert - Outcomes: no data, completely independent from other areas
        result.OutcomeCurrent.Should().BeNull();
        result.OutcomeAverage.Should().BeNull();
        result.OutcomeTrend.Should().BeEmpty();

        // Verify all three areas were queried independently
        _dataverseServiceMock.Verify(
            s => s.QueryKpiAssessmentsAsync(matterId, Guidelines, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _dataverseServiceMock.Verify(
            s => s.QueryKpiAssessmentsAsync(matterId, Budget, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _dataverseServiceMock.Verify(
            s => s.QueryKpiAssessmentsAsync(matterId, Outcomes, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}
