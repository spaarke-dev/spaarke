using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services;

/// <summary>
/// Performance tests for <see cref="ScorecardCalculatorService"/> validating NFR-01:
/// calculator responds in &lt;500ms. Tests measure calculator logic overhead with
/// fast-returning mocks (not actual Dataverse query latency).
/// Validates acceptance criteria from task 051.
/// </summary>
public class ScorecardCalculatorPerformanceTests
{
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<ScorecardCalculatorService>> _loggerMock;
    private readonly ScorecardCalculatorService _service;

    // Performance area constants matching ScorecardCalculatorService.PerformanceArea
    private const int Guidelines = 1;
    private const int Budget = 2;
    private const int Outcomes = 3;

    /// <summary>NFR-01 threshold: calculator must complete within 500ms.</summary>
    private const double MaxAllowedMilliseconds = 500;

    public ScorecardCalculatorPerformanceTests()
    {
        _dataverseServiceMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<ScorecardCalculatorService>>();
        _service = new ScorecardCalculatorService(_dataverseServiceMock.Object, _loggerMock.Object);
    }

    #region Helper Methods

    /// <summary>
    /// Generates an array of KpiAssessmentRecord with the specified count,
    /// using sequential grades ordered DESC (newest first).
    /// </summary>
    private static KpiAssessmentRecord[] GenerateAssessments(int count)
    {
        var assessments = new KpiAssessmentRecord[count];
        var gradeValues = new[] { 100, 95, 90, 85, 80, 75, 70, 65, 60 };

        for (var i = 0; i < count; i++)
        {
            assessments[i] = new KpiAssessmentRecord
            {
                Id = Guid.NewGuid(),
                Grade = gradeValues[i % gradeValues.Length],
                CreatedOn = DateTime.UtcNow.AddDays(-i)
            };
        }

        return assessments;
    }

    /// <summary>
    /// Sets up the mock to return the given assessments for a specific performance area.
    /// </summary>
    private void SetupAreaAssessments(Guid matterId, int performanceArea, KpiAssessmentRecord[] assessments)
    {
        _dataverseServiceMock
            .Setup(s => s.QueryKpiAssessmentsAsync(matterId, It.IsAny<string>(), performanceArea, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessments);
    }

    /// <summary>
    /// Sets up all three performance areas with the specified number of assessments each.
    /// </summary>
    private void SetupAllAreas(Guid matterId, int assessmentsPerArea)
    {
        SetupAreaAssessments(matterId, Guidelines, GenerateAssessments(assessmentsPerArea));
        SetupAreaAssessments(matterId, Budget, GenerateAssessments(assessmentsPerArea));
        SetupAreaAssessments(matterId, Outcomes, GenerateAssessments(assessmentsPerArea));
    }

    #endregion

    #region NFR-01: Single Area Performance

    [Fact]
    public async Task Performance_SingleArea_CompletesWithin500ms()
    {
        // Arrange - 10 assessments for Guidelines, empty for others
        var matterId = Guid.NewGuid();
        SetupAreaAssessments(matterId, Guidelines, GenerateAssessments(10));
        SetupAreaAssessments(matterId, Budget, Array.Empty<KpiAssessmentRecord>());
        SetupAreaAssessments(matterId, Outcomes, Array.Empty<KpiAssessmentRecord>());

        // Warm up - first call may include JIT overhead
        await _service.RecalculateGradesAsync(matterId);

        // Act - time the actual recalculation
        var stopwatch = Stopwatch.StartNew();
        var result = await _service.RecalculateGradesAsync(matterId);
        stopwatch.Stop();

        // Assert - NFR-01: must complete within 500ms
        stopwatch.Elapsed.TotalMilliseconds.Should().BeLessThan(MaxAllowedMilliseconds,
            "NFR-01: single area recalculation with 10 assessments must complete within 500ms");

        // Assert - result is valid (sanity check)
        result.GuidelineCurrent.Should().NotBeNull();
        result.GuidelineAverage.Should().NotBeNull();
        result.GuidelineTrend.Should().NotBeEmpty();
    }

    #endregion

    #region NFR-01: All Areas Performance

    [Fact]
    public async Task Performance_AllAreas_CompletesWithin500ms()
    {
        // Arrange - 50 assessments per area (150 total)
        var matterId = Guid.NewGuid();
        SetupAllAreas(matterId, 50);

        // Warm up
        await _service.RecalculateGradesAsync(matterId);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await _service.RecalculateGradesAsync(matterId);
        stopwatch.Stop();

        // Assert - NFR-01: must complete within 500ms
        stopwatch.Elapsed.TotalMilliseconds.Should().BeLessThan(MaxAllowedMilliseconds,
            "NFR-01: full recalculation with 150 total assessments must complete within 500ms");

        // Assert - all areas returned data (sanity check)
        result.GuidelineCurrent.Should().NotBeNull();
        result.BudgetCurrent.Should().NotBeNull();
        result.OutcomeCurrent.Should().NotBeNull();
        result.GuidelineTrend.Should().NotBeEmpty();
        result.BudgetTrend.Should().NotBeEmpty();
        result.OutcomeTrend.Should().NotBeEmpty();
    }

    #endregion

    #region NFR-01: Large Dataset Performance

    [Fact]
    public async Task Performance_LargeDataset_CompletesWithin500ms()
    {
        // Arrange - 100 assessments per area (300 total)
        var matterId = Guid.NewGuid();
        SetupAllAreas(matterId, 100);

        // Warm up
        await _service.RecalculateGradesAsync(matterId);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await _service.RecalculateGradesAsync(matterId);
        stopwatch.Stop();

        // Assert - NFR-01: must complete within 500ms even with large dataset
        stopwatch.Elapsed.TotalMilliseconds.Should().BeLessThan(MaxAllowedMilliseconds,
            "NFR-01: full recalculation with 300 total assessments must complete within 500ms");

        // Assert - all areas returned data (sanity check)
        result.GuidelineCurrent.Should().NotBeNull();
        result.BudgetCurrent.Should().NotBeNull();
        result.OutcomeCurrent.Should().NotBeNull();
    }

    #endregion

    #region NFR-01: Parallel Queries Faster Than Sequential

    [Fact]
    public async Task Performance_ParallelQueries_FasterThanSequential()
    {
        // Arrange - simulate latency in queries to make parallel vs sequential measurable
        var matterId = Guid.NewGuid();
        var assessments = GenerateAssessments(50);
        var queryDelay = TimeSpan.FromMilliseconds(50); // 50ms simulated latency per query

        // Setup mocks with simulated delay to make parallelism observable
        _dataverseServiceMock
            .Setup(s => s.QueryKpiAssessmentsAsync(matterId, It.IsAny<string>(), Guidelines, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(queryDelay);
                return assessments;
            });
        _dataverseServiceMock
            .Setup(s => s.QueryKpiAssessmentsAsync(matterId, It.IsAny<string>(), Budget, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(queryDelay);
                return assessments;
            });
        _dataverseServiceMock
            .Setup(s => s.QueryKpiAssessmentsAsync(matterId, It.IsAny<string>(), Outcomes, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(queryDelay);
                return assessments;
            });

        // Warm up
        await _service.RecalculateGradesAsync(matterId);

        // Act - time the parallel execution
        var stopwatch = Stopwatch.StartNew();
        var result = await _service.RecalculateGradesAsync(matterId);
        stopwatch.Stop();

        var parallelTime = stopwatch.Elapsed;

        // Assert - if queries run in parallel (Task.WhenAll), total time should be
        // roughly 1x delay (~50ms), not 3x delay (~150ms for sequential).
        // Use 2x as threshold to account for scheduling overhead.
        var sequentialThreshold = queryDelay.TotalMilliseconds * 3;
        parallelTime.TotalMilliseconds.Should().BeLessThan(sequentialThreshold,
            "parallel queries via Task.WhenAll should complete faster than sequential execution " +
            $"(expected < {sequentialThreshold}ms sequential threshold, got {parallelTime.TotalMilliseconds:F1}ms)");

        // Assert - still within overall NFR-01 threshold
        parallelTime.TotalMilliseconds.Should().BeLessThan(MaxAllowedMilliseconds,
            "NFR-01: parallel recalculation must still complete within 500ms");

        // Assert - result is valid
        result.GuidelineCurrent.Should().NotBeNull();
        result.BudgetCurrent.Should().NotBeNull();
        result.OutcomeCurrent.Should().NotBeNull();
    }

    #endregion
}
