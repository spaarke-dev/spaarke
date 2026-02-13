using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services;

/// <summary>
/// Error scenario tests for <see cref="ScorecardCalculatorService"/>.
/// Validates NFR-03: graceful error handling in the calculator service.
/// Covers exception propagation, cancellation, partial failures, and invalid data resilience.
/// </summary>
public class ScorecardCalculatorErrorTests
{
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<ScorecardCalculatorService>> _loggerMock;
    private readonly ScorecardCalculatorService _service;

    // Performance area constants matching ScorecardCalculatorService.PerformanceArea
    private const int Guidelines = 1;
    private const int Budget = 2;
    private const int Outcomes = 3;

    public ScorecardCalculatorErrorTests()
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
            .Setup(s => s.QueryKpiAssessmentsAsync(matterId, It.IsAny<string>(), performanceArea, It.IsAny<int>(), It.IsAny<CancellationToken>()))
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

    #region Error Scenario 1: Dataverse Query Throws

    [Fact]
    public async Task Error_DataverseQueryThrows_ServicePropagatesException()
    {
        // Arrange - mock QueryKpiAssessmentsAsync to throw HttpRequestException
        // simulating a Dataverse connectivity or service error
        var matterId = Guid.NewGuid();
        _dataverseServiceMock
            .Setup(s => s.QueryKpiAssessmentsAsync(matterId, It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Dataverse service unavailable"));

        // Act & Assert - service propagates the exception since queries run via Task.WhenAll
        var act = () => _service.RecalculateGradesAsync(matterId);
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Dataverse service unavailable*");
    }

    #endregion

    #region Error Scenario 2: Dataverse Update Throws

    [Fact]
    public async Task Error_DataverseUpdateThrows_ServicePropagatesException()
    {
        // Arrange - queries succeed but UpdateRecordFieldsAsync throws
        var matterId = Guid.NewGuid();
        SetupAreaAssessments(matterId, Guidelines, CreateAssessment(90));
        SetupAreaAssessments(matterId, Budget, CreateAssessment(85));
        SetupAreaAssessments(matterId, Outcomes, CreateAssessment(80));

        _dataverseServiceMock
            .Setup(s => s.UpdateRecordFieldsAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Dataverse update failed"));

        // Act & Assert - the service currently propagates the update exception.
        // Calculations succeed internally but the update failure surfaces to the caller.
        var act = () => _service.RecalculateGradesAsync(matterId);
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Dataverse update failed*");
    }

    [Fact]
    public async Task Error_DataverseUpdateThrows_QueriesStillExecuteSuccessfully()
    {
        // Arrange - validate that all three area queries still execute before the update fails
        var matterId = Guid.NewGuid();
        SetupAreaAssessments(matterId, Guidelines, CreateAssessment(90));
        SetupAreaAssessments(matterId, Budget, CreateAssessment(85));
        SetupAreaAssessments(matterId, Outcomes, CreateAssessment(80));

        _dataverseServiceMock
            .Setup(s => s.UpdateRecordFieldsAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Update failed"));

        // Act - ignore the exception to verify queries executed
        try { await _service.RecalculateGradesAsync(matterId); } catch { /* expected */ }

        // Assert - all three area queries were invoked despite update failure
        _dataverseServiceMock.Verify(
            s => s.QueryKpiAssessmentsAsync(matterId, It.IsAny<string>(), Guidelines, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _dataverseServiceMock.Verify(
            s => s.QueryKpiAssessmentsAsync(matterId, It.IsAny<string>(), Budget, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _dataverseServiceMock.Verify(
            s => s.QueryKpiAssessmentsAsync(matterId, It.IsAny<string>(), Outcomes, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Error Scenario 3: Invalid Matter ID

    [Fact]
    public async Task Error_InvalidMatterId_ReturnsEmptyGrades()
    {
        // Arrange - pass Guid.Empty as matterId; mock returns empty arrays
        var matterId = Guid.Empty;
        SetupAllAreasEmpty(matterId);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - all current grades should be null when no assessments found
        result.GuidelineCurrent.Should().BeNull();
        result.BudgetCurrent.Should().BeNull();
        result.OutcomeCurrent.Should().BeNull();

        // Assert - all averages should be null
        result.GuidelineAverage.Should().BeNull();
        result.BudgetAverage.Should().BeNull();
        result.OutcomeAverage.Should().BeNull();

        // Assert - all trends should be empty
        result.GuidelineTrend.Should().BeEmpty();
        result.BudgetTrend.Should().BeEmpty();
        result.OutcomeTrend.Should().BeEmpty();
    }

    #endregion

    #region Error Scenario 4: Cancellation Requested

    [Fact]
    public async Task Error_CancellationRequested_PropagatesCancellation()
    {
        // Arrange - create a pre-cancelled token
        var matterId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _dataverseServiceMock
            .Setup(s => s.QueryKpiAssessmentsAsync(matterId, It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, string _, int? _, int _, CancellationToken ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(Array.Empty<KpiAssessmentRecord>());
            });

        // Act & Assert - should throw OperationCanceledException
        var act = () => _service.RecalculateGradesAsync(matterId, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Error_CancellationDuringQuery_PropagatesTaskCanceledException()
    {
        // Arrange - cancellation occurs during Dataverse query execution
        var matterId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        _dataverseServiceMock
            .Setup(s => s.QueryKpiAssessmentsAsync(matterId, It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The operation was cancelled"));

        // Act & Assert
        var act = () => _service.RecalculateGradesAsync(matterId, cts.Token);
        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    #endregion

    #region Error Scenario 5: Partial Area Failure

    [Fact]
    public async Task Error_PartialAreaFailure_PropagatesException()
    {
        // Arrange - Guidelines query throws, Budget and Outcomes succeed.
        // Since Task.WhenAll is used, the aggregate exception propagates.
        var matterId = Guid.NewGuid();

        _dataverseServiceMock
            .Setup(s => s.QueryKpiAssessmentsAsync(matterId, It.IsAny<string>(), Guidelines, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Guidelines query failed"));

        SetupAreaAssessments(matterId, Budget, CreateAssessment(85));
        SetupAreaAssessments(matterId, Outcomes, CreateAssessment(90));

        // Act & Assert - Task.WhenAll surfaces the first faulted task's exception
        var act = () => _service.RecalculateGradesAsync(matterId);
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Guidelines query failed*");
    }

    [Fact]
    public async Task Error_PartialAreaFailure_OtherQueriesStillInvoked()
    {
        // Arrange - Guidelines throws, but Budget and Outcomes should still be invoked
        // because Task.WhenAll waits for all tasks before throwing
        var matterId = Guid.NewGuid();

        _dataverseServiceMock
            .Setup(s => s.QueryKpiAssessmentsAsync(matterId, It.IsAny<string>(), Guidelines, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Guidelines query failed"));

        SetupAreaAssessments(matterId, Budget, CreateAssessment(85));
        SetupAreaAssessments(matterId, Outcomes, CreateAssessment(90));

        // Act - ignore the exception
        try { await _service.RecalculateGradesAsync(matterId); } catch { /* expected */ }

        // Assert - all three area queries were invoked (Task.WhenAll runs them all)
        _dataverseServiceMock.Verify(
            s => s.QueryKpiAssessmentsAsync(matterId, It.IsAny<string>(), Guidelines, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _dataverseServiceMock.Verify(
            s => s.QueryKpiAssessmentsAsync(matterId, It.IsAny<string>(), Budget, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _dataverseServiceMock.Verify(
            s => s.QueryKpiAssessmentsAsync(matterId, It.IsAny<string>(), Outcomes, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Error Scenario 6: Invalid Grade Values

    [Fact]
    public async Task Error_DataverseReturnsNegativeGrade_HandlesGracefully()
    {
        // Arrange - grade value -1 is outside expected range but should not crash
        var matterId = Guid.NewGuid();
        SetupAreaAssessments(matterId, Guidelines, CreateAssessment(-1));
        SetupAreaAssessments(matterId, Budget);
        SetupAreaAssessments(matterId, Outcomes);

        // Act - should not throw
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - grade -1 / 100.0 = -0.01 (service doesn't validate range, just converts)
        result.GuidelineCurrent.Should().Be(-0.01m);
    }

    [Fact]
    public async Task Error_DataverseReturnsExcessiveGrade_HandlesGracefully()
    {
        // Arrange - grade value 999 is far beyond expected range
        var matterId = Guid.NewGuid();
        SetupAreaAssessments(matterId, Guidelines, CreateAssessment(999));
        SetupAreaAssessments(matterId, Budget);
        SetupAreaAssessments(matterId, Outcomes);

        // Act - should not throw
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - 999 / 100.0 = 9.99 (converted but not validated)
        result.GuidelineCurrent.Should().Be(9.99m);
    }

    [Fact]
    public async Task Error_DataverseReturnsMixedInvalidGrades_AverageCalculatesWithoutCrash()
    {
        // Arrange - mix of valid and out-of-range grades
        var matterId = Guid.NewGuid();
        var assessments = new[]
        {
            CreateAssessment(90),   // Valid
            CreateAssessment(-5),   // Invalid negative
            CreateAssessment(200)   // Invalid high
        };
        SetupAreaAssessments(matterId, Guidelines, assessments);
        SetupAreaAssessments(matterId, Budget);
        SetupAreaAssessments(matterId, Outcomes);

        // Act - should not throw
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - average of (0.90, -0.05, 2.00) / 3 = 0.95
        result.GuidelineAverage.Should().Be(0.95m);
        result.GuidelineCurrent.Should().Be(0.90m); // First element (latest)
        result.GuidelineTrend.Should().HaveCount(3);
    }

    [Fact]
    public async Task Error_DataverseReturnsZeroGrade_HandlesGracefully()
    {
        // Arrange - grade value 0 is the "No Grade" sentinel
        var matterId = Guid.NewGuid();
        SetupAreaAssessments(matterId, Guidelines, CreateAssessment(0));
        SetupAreaAssessments(matterId, Budget);
        SetupAreaAssessments(matterId, Outcomes);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - 0 / 100.0 = 0.00
        result.GuidelineCurrent.Should().Be(0.00m);
        result.GuidelineAverage.Should().Be(0.00m);
    }

    #endregion

    #region Error Scenario 7: Empty Grade Arrays with Mixed Data

    [Fact]
    public async Task Error_EmptyGradeArray_OneAreaPopulatedOthersEmpty()
    {
        // Arrange - only Budget has assessments, Guidelines and Outcomes are empty
        var matterId = Guid.NewGuid();
        SetupAreaAssessments(matterId, Guidelines);
        SetupAreaAssessments(matterId, Budget,
            CreateAssessment(95, DateTime.UtcNow),
            CreateAssessment(85, DateTime.UtcNow.AddDays(-30)));
        SetupAreaAssessments(matterId, Outcomes);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - Guidelines: null/empty (no data)
        result.GuidelineCurrent.Should().BeNull();
        result.GuidelineAverage.Should().BeNull();
        result.GuidelineTrend.Should().BeEmpty();

        // Assert - Budget: populated correctly
        result.BudgetCurrent.Should().Be(0.95m);
        result.BudgetAverage.Should().Be(0.90m); // (0.95 + 0.85) / 2
        result.BudgetTrend.Should().HaveCount(2);
        result.BudgetTrend.Should().ContainInOrder(0.85m, 0.95m);

        // Assert - Outcomes: null/empty (no data)
        result.OutcomeCurrent.Should().BeNull();
        result.OutcomeAverage.Should().BeNull();
        result.OutcomeTrend.Should().BeEmpty();
    }

    [Fact]
    public async Task Error_EmptyGradeArray_UpdateStillCalledWithNullsForEmptyAreas()
    {
        // Arrange - Guidelines has data, Budget and Outcomes are empty
        var matterId = Guid.NewGuid();
        SetupAreaAssessments(matterId, Guidelines, CreateAssessment(90));
        SetupAreaAssessments(matterId, Budget);
        SetupAreaAssessments(matterId, Outcomes);

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

        // Assert - update was called with correct mix of values and nulls
        capturedFields.Should().NotBeNull();
        capturedFields.Should().HaveCount(6);
        capturedFields!["sprk_guidelinecompliancegrade_current"].Should().Be(0.90m);
        capturedFields["sprk_guidelinecompliancegrade_average"].Should().Be(0.90m);
        capturedFields["sprk_budgetcompliancegrade_current"].Should().BeNull();
        capturedFields["sprk_budgetcompliancegrade_average"].Should().BeNull();
        capturedFields["sprk_outcomecompliancegrade_current"].Should().BeNull();
        capturedFields["sprk_outcomecompliancegrade_average"].Should().BeNull();
    }

    [Fact]
    public async Task Error_AllAreasEmpty_ResponseObjectFullyInitialized()
    {
        // Arrange - no assessments for any area
        var matterId = Guid.NewGuid();
        SetupAllAreasEmpty(matterId);

        // Act
        var result = await _service.RecalculateGradesAsync(matterId);

        // Assert - response should be fully initialized (not null), with null/empty fields
        result.Should().NotBeNull();
        result.GuidelineTrend.Should().NotBeNull().And.BeEmpty();
        result.BudgetTrend.Should().NotBeNull().And.BeEmpty();
        result.OutcomeTrend.Should().NotBeNull().And.BeEmpty();
    }

    #endregion
}
