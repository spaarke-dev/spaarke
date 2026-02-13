using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ScorecardCalculatorService"/> covering current grade,
/// historical average, trend data, edge cases, and matter update verification.
/// Validates acceptance criteria from tasks 011, 012, and 013.
/// </summary>
public class ScorecardCalculatorServiceTests
{
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<ScorecardCalculatorService>> _loggerMock;
    private readonly ScorecardCalculatorService _service;

    // Performance area constants matching ScorecardCalculatorService.PerformanceArea
    private const int Guidelines = 1;
    private const int Budget = 2;
    private const int Outcomes = 3;

    public ScorecardCalculatorServiceTests()
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

    #endregion

    #region Current Grade Tests (Task 011)

    [Fact]
    public async Task RecalculateGradesAsync_SingleAssessment_ReturnsGradeAsDecimal()
    {
        // Arrange
        var matterId = Guid.NewGuid();
        SetupAreaAssessments(matterId, Guidelines, CreateAssessment(95));
        SetupAreaAssessments(matterId, Budget);
        SetupAreaAssessments(matterId, Outcomes);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - grade 95 should become 0.95 (95 / 100.0)
        result.GuidelineCurrent.Should().Be(0.95m);
    }

    [Fact]
    public async Task RecalculateGradesAsync_MultipleAssessments_ReturnsLatestGrade()
    {
        // Arrange - assessments are ordered DESC (latest first)
        var matterId = Guid.NewGuid();
        var assessments = new[]
        {
            CreateAssessment(90, DateTime.UtcNow),               // Latest (index 0)
            CreateAssessment(85, DateTime.UtcNow.AddDays(-30)),  // Older
            CreateAssessment(80, DateTime.UtcNow.AddDays(-60))   // Oldest
        };
        SetupAreaAssessments(matterId, Guidelines, assessments);
        SetupAreaAssessments(matterId, Budget);
        SetupAreaAssessments(matterId, Outcomes);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - should return the first element (latest), grade 90 -> 0.90
        result.GuidelineCurrent.Should().Be(0.90m);
    }

    [Fact]
    public async Task RecalculateGradesAsync_NoAssessments_ReturnsNullCurrentGrade()
    {
        // Arrange
        var matterId = Guid.NewGuid();
        SetupAllAreasEmpty(matterId);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert
        result.GuidelineCurrent.Should().BeNull();
        result.BudgetCurrent.Should().BeNull();
        result.OutcomeCurrent.Should().BeNull();
    }

    [Fact]
    public async Task RecalculateGradesAsync_AllGradeValues_ConvertsCorrectly()
    {
        // Arrange - test all defined grade choice values
        var matterId = Guid.NewGuid();

        // Guidelines: A+ (100)
        SetupAreaAssessments(matterId, Guidelines, CreateAssessment(100));
        // Budget: C (75)
        SetupAreaAssessments(matterId, Budget, CreateAssessment(75));
        // Outcomes: F (60)
        SetupAreaAssessments(matterId, Outcomes, CreateAssessment(60));

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert
        result.GuidelineCurrent.Should().Be(1.00m);
        result.BudgetCurrent.Should().Be(0.75m);
        result.OutcomeCurrent.Should().Be(0.60m);
    }

    #endregion

    #region Historical Average Tests (Task 012)

    [Fact]
    public async Task RecalculateGradesAsync_MultipleAssessments_ReturnsCorrectAverage()
    {
        // Arrange - assessments [100, 85, 90] -> average = (1.00 + 0.85 + 0.90) / 3 = 0.9166... -> 0.92
        var matterId = Guid.NewGuid();
        var assessments = new[]
        {
            CreateAssessment(100),
            CreateAssessment(85),
            CreateAssessment(90)
        };
        SetupAreaAssessments(matterId, Guidelines, assessments);
        SetupAreaAssessments(matterId, Budget);
        SetupAreaAssessments(matterId, Outcomes);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - (1.00 + 0.85 + 0.90) / 3 = 0.916666... rounded to 0.92
        result.GuidelineAverage.Should().Be(0.92m);
    }

    [Fact]
    public async Task RecalculateGradesAsync_SingleAssessment_AverageEqualsCurrent()
    {
        // Arrange - single assessment [95] -> average = 0.95
        var matterId = Guid.NewGuid();
        SetupAreaAssessments(matterId, Guidelines, CreateAssessment(95));
        SetupAreaAssessments(matterId, Budget);
        SetupAreaAssessments(matterId, Outcomes);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert
        result.GuidelineAverage.Should().Be(0.95m);
    }

    [Fact]
    public async Task RecalculateGradesAsync_NoAssessments_ReturnsNullAverage()
    {
        // Arrange
        var matterId = Guid.NewGuid();
        SetupAllAreasEmpty(matterId);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert
        result.GuidelineAverage.Should().BeNull();
        result.BudgetAverage.Should().BeNull();
        result.OutcomeAverage.Should().BeNull();
    }

    [Fact]
    public async Task RecalculateGradesAsync_AverageRoundsToTwoDecimalPlaces()
    {
        // Arrange - grades that produce a long decimal average
        // [100, 90, 80] -> (1.00 + 0.90 + 0.80) / 3 = 0.9000 -> 0.90
        var matterId = Guid.NewGuid();
        var assessments = new[]
        {
            CreateAssessment(100),
            CreateAssessment(90),
            CreateAssessment(80)
        };
        SetupAreaAssessments(matterId, Guidelines, assessments);
        SetupAreaAssessments(matterId, Budget);
        SetupAreaAssessments(matterId, Outcomes);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert
        result.GuidelineAverage.Should().Be(0.90m);
    }

    #endregion

    #region Trend Data Tests (Task 013)

    [Fact]
    public async Task RecalculateGradesAsync_FiveAssessments_ReturnsAllInChronologicalOrder()
    {
        // Arrange - 5 assessments ordered DESC (newest first), should be reversed for trend
        var matterId = Guid.NewGuid();
        var assessments = new[]
        {
            CreateAssessment(95, DateTime.UtcNow),                // Newest
            CreateAssessment(90, DateTime.UtcNow.AddDays(-10)),
            CreateAssessment(85, DateTime.UtcNow.AddDays(-20)),
            CreateAssessment(80, DateTime.UtcNow.AddDays(-30)),
            CreateAssessment(75, DateTime.UtcNow.AddDays(-40))   // Oldest
        };
        SetupAreaAssessments(matterId, Guidelines, assessments);
        SetupAreaAssessments(matterId, Budget);
        SetupAreaAssessments(matterId, Outcomes);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - reversed to chronological: [75, 80, 85, 90, 95] -> [0.75, 0.80, 0.85, 0.90, 0.95]
        result.GuidelineTrend.Should().HaveCount(5);
        result.GuidelineTrend.Should().ContainInOrder(
            0.75m, 0.80m, 0.85m, 0.90m, 0.95m);
    }

    [Fact]
    public async Task RecalculateGradesAsync_ThreeAssessments_ReturnsThreeTrendPoints()
    {
        // Arrange - fewer than TrendCount (5), should return all available
        var matterId = Guid.NewGuid();
        var assessments = new[]
        {
            CreateAssessment(90, DateTime.UtcNow),
            CreateAssessment(85, DateTime.UtcNow.AddDays(-10)),
            CreateAssessment(80, DateTime.UtcNow.AddDays(-20))
        };
        SetupAreaAssessments(matterId, Guidelines, assessments);
        SetupAreaAssessments(matterId, Budget);
        SetupAreaAssessments(matterId, Outcomes);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - reversed: [80, 85, 90] -> [0.80, 0.85, 0.90]
        result.GuidelineTrend.Should().HaveCount(3);
        result.GuidelineTrend.Should().ContainInOrder(0.80m, 0.85m, 0.90m);
    }

    [Fact]
    public async Task RecalculateGradesAsync_NoAssessments_ReturnsEmptyTrend()
    {
        // Arrange
        var matterId = Guid.NewGuid();
        SetupAllAreasEmpty(matterId);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert
        result.GuidelineTrend.Should().BeEmpty();
        result.BudgetTrend.Should().BeEmpty();
        result.OutcomeTrend.Should().BeEmpty();
    }

    [Fact]
    public async Task RecalculateGradesAsync_SevenAssessments_ReturnsOnlyLastFive()
    {
        // Arrange - 7 assessments, Take(5) should select only the 5 most recent
        var matterId = Guid.NewGuid();
        var assessments = new[]
        {
            CreateAssessment(100, DateTime.UtcNow),                // Most recent
            CreateAssessment(95, DateTime.UtcNow.AddDays(-5)),
            CreateAssessment(90, DateTime.UtcNow.AddDays(-10)),
            CreateAssessment(85, DateTime.UtcNow.AddDays(-15)),
            CreateAssessment(80, DateTime.UtcNow.AddDays(-20)),    // 5th most recent
            CreateAssessment(75, DateTime.UtcNow.AddDays(-25)),    // Excluded from trend
            CreateAssessment(70, DateTime.UtcNow.AddDays(-30))     // Excluded from trend
        };
        SetupAreaAssessments(matterId, Guidelines, assessments);
        SetupAreaAssessments(matterId, Budget);
        SetupAreaAssessments(matterId, Outcomes);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - Take(5) gets [100,95,90,85,80], Reverse -> [0.80, 0.85, 0.90, 0.95, 1.00]
        result.GuidelineTrend.Should().HaveCount(5);
        result.GuidelineTrend.Should().ContainInOrder(
            0.80m, 0.85m, 0.90m, 0.95m, 1.00m);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task RecalculateGradesAsync_AllAreasNoData_AllFieldsNull()
    {
        // Arrange
        var matterId = Guid.NewGuid();
        SetupAllAreasEmpty(matterId);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - all current grades null
        result.GuidelineCurrent.Should().BeNull();
        result.BudgetCurrent.Should().BeNull();
        result.OutcomeCurrent.Should().BeNull();

        // Assert - all averages null
        result.GuidelineAverage.Should().BeNull();
        result.BudgetAverage.Should().BeNull();
        result.OutcomeAverage.Should().BeNull();

        // Assert - all trends empty
        result.GuidelineTrend.Should().BeEmpty();
        result.BudgetTrend.Should().BeEmpty();
        result.OutcomeTrend.Should().BeEmpty();
    }

    [Fact]
    public async Task RecalculateGradesAsync_MixedAreas_OneWithDataOthersEmpty()
    {
        // Arrange - only Guidelines has data, Budget and Outcomes are empty
        var matterId = Guid.NewGuid();
        SetupAreaAssessments(matterId, Guidelines, CreateAssessment(90));
        SetupAreaAssessments(matterId, Budget);
        SetupAreaAssessments(matterId, Outcomes);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - Guidelines has values
        result.GuidelineCurrent.Should().Be(0.90m);
        result.GuidelineAverage.Should().Be(0.90m);
        result.GuidelineTrend.Should().HaveCount(1);

        // Assert - Budget is null/empty
        result.BudgetCurrent.Should().BeNull();
        result.BudgetAverage.Should().BeNull();
        result.BudgetTrend.Should().BeEmpty();

        // Assert - Outcomes is null/empty
        result.OutcomeCurrent.Should().BeNull();
        result.OutcomeAverage.Should().BeNull();
        result.OutcomeTrend.Should().BeEmpty();
    }

    [Fact]
    public async Task RecalculateGradesAsync_GradeValueZero_ReturnsDecimalZero()
    {
        // Arrange - Grade 0 (No Grade) should map to 0.00m
        var matterId = Guid.NewGuid();
        SetupAreaAssessments(matterId, Guidelines, CreateAssessment(0));
        SetupAreaAssessments(matterId, Budget);
        SetupAreaAssessments(matterId, Outcomes);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert
        result.GuidelineCurrent.Should().Be(0.00m);
        result.GuidelineAverage.Should().Be(0.00m);
        result.GuidelineTrend.Should().ContainSingle().Which.Should().Be(0.00m);
    }

    #endregion

    #region Matter Update Verification

    [Fact]
    public async Task RecalculateGradesAsync_UpdatesMatterWithCorrectFields()
    {
        // Arrange
        var matterId = Guid.NewGuid();
        SetupAreaAssessments(matterId, Guidelines, CreateAssessment(95));
        SetupAreaAssessments(matterId, Budget, CreateAssessment(85));
        SetupAreaAssessments(matterId, Outcomes, CreateAssessment(90));

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

        // Assert - UpdateRecordFieldsAsync was called
        _dataverseServiceMock.Verify(
            s => s.UpdateRecordFieldsAsync(
                "sprk_matter",
                matterId,
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert - all 6 fields are present with correct values
        capturedFields.Should().NotBeNull();
        capturedFields.Should().HaveCount(6);
        capturedFields!["sprk_guidelinecompliancegrade_current"].Should().Be(0.95m);
        capturedFields["sprk_guidelinecompliancegrade_average"].Should().Be(0.95m);
        capturedFields["sprk_budgetcompliancegrade_current"].Should().Be(0.85m);
        capturedFields["sprk_budgetcompliancegrade_average"].Should().Be(0.85m);
        capturedFields["sprk_outcomecompliancegrade_current"].Should().Be(0.90m);
        capturedFields["sprk_outcomecompliancegrade_average"].Should().Be(0.90m);
    }

    [Fact]
    public async Task RecalculateGradesAsync_UpdatesMatterEntityName()
    {
        // Arrange
        var matterId = Guid.NewGuid();
        SetupAllAreasEmpty(matterId);

        // Act
        await _service.RecalculateGradesAsync(matterId);

        // Assert - verifies entity name is "sprk_matter"
        _dataverseServiceMock.Verify(
            s => s.UpdateRecordFieldsAsync(
                "sprk_matter",
                matterId,
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RecalculateGradesAsync_NoData_UpdatesMatterWithNullValues()
    {
        // Arrange
        var matterId = Guid.NewGuid();
        SetupAllAreasEmpty(matterId);

        Dictionary<string, object?>? capturedFields = null;
        _dataverseServiceMock
            .Setup(s => s.UpdateRecordFieldsAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken>(
                (_, _, fields, _) => capturedFields = fields)
            .Returns(Task.CompletedTask);

        // Act
        await _service.RecalculateGradesAsync(matterId);

        // Assert - all 6 fields should be null when no assessments exist
        capturedFields.Should().NotBeNull();
        capturedFields.Should().HaveCount(6);
        capturedFields!["sprk_guidelinecompliancegrade_current"].Should().BeNull();
        capturedFields["sprk_guidelinecompliancegrade_average"].Should().BeNull();
        capturedFields["sprk_budgetcompliancegrade_current"].Should().BeNull();
        capturedFields["sprk_budgetcompliancegrade_average"].Should().BeNull();
        capturedFields["sprk_outcomecompliancegrade_current"].Should().BeNull();
        capturedFields["sprk_outcomecompliancegrade_average"].Should().BeNull();
    }

    #endregion

    #region Parallel Query Verification

    [Fact]
    public async Task RecalculateGradesAsync_QueriesAllThreePerformanceAreas()
    {
        // Arrange
        var matterId = Guid.NewGuid();
        SetupAllAreasEmpty(matterId);

        // Act
        await _service.RecalculateGradesAsync(matterId);

        // Assert - all three area queries were invoked
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

    #region Full Integration Scenario

    [Fact]
    public async Task RecalculateGradesAsync_AllAreasWithData_ReturnsCompleteResponse()
    {
        // Arrange - realistic scenario with data in all three areas
        var matterId = Guid.NewGuid();

        // Guidelines: 3 assessments
        SetupAreaAssessments(matterId, Guidelines,
            CreateAssessment(95, DateTime.UtcNow),
            CreateAssessment(90, DateTime.UtcNow.AddDays(-30)),
            CreateAssessment(85, DateTime.UtcNow.AddDays(-60)));

        // Budget: 2 assessments
        SetupAreaAssessments(matterId, Budget,
            CreateAssessment(80, DateTime.UtcNow),
            CreateAssessment(75, DateTime.UtcNow.AddDays(-30)));

        // Outcomes: 1 assessment
        SetupAreaAssessments(matterId, Outcomes,
            CreateAssessment(100, DateTime.UtcNow));

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - Guidelines
        result.GuidelineCurrent.Should().Be(0.95m);
        result.GuidelineAverage.Should().Be(0.90m); // (0.95 + 0.90 + 0.85) / 3 = 0.90
        result.GuidelineTrend.Should().HaveCount(3);
        result.GuidelineTrend.Should().ContainInOrder(0.85m, 0.90m, 0.95m);

        // Assert - Budget
        result.BudgetCurrent.Should().Be(0.80m);
        result.BudgetAverage.Should().Be(0.78m); // (0.80 + 0.75) / 2 = 0.775 -> 0.78
        result.BudgetTrend.Should().HaveCount(2);
        result.BudgetTrend.Should().ContainInOrder(0.75m, 0.80m);

        // Assert - Outcomes
        result.OutcomeCurrent.Should().Be(1.00m);
        result.OutcomeAverage.Should().Be(1.00m);
        result.OutcomeTrend.Should().ContainSingle().Which.Should().Be(1.00m);
    }

    #endregion
}
