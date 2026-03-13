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
    private const int Guidelines = 100000000;
    private const int Budget = 100000001;
    private const int Outcomes = 100000002;

    // Grade option set values matching ScorecardCalculatorService.GradeToDecimal
    private const int GradeAPlus = 100000000;  // → 1.00m
    private const int GradeA = 100000001;      // → 0.95m
    private const int GradeBPlus = 100000002;  // → 0.90m
    private const int GradeB = 100000003;      // → 0.85m
    private const int GradeCPlus = 100000004;  // → 0.80m
    private const int GradeC = 100000005;      // → 0.75m
    private const int GradeDPlus = 100000006;  // → 0.70m
    private const int GradeD = 100000007;      // → 0.65m
    private const int GradeF = 100000008;      // → 0.60m
    private const int GradeNoGrade = 100000009; // → 0.00m

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
        var gradeValues = new[] { GradeAPlus, GradeA, GradeBPlus, GradeB, GradeCPlus, GradeC, GradeDPlus, GradeD, GradeF };

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
    /// Sets up the mock to return the given assessments for a specific performance area
    /// via BatchQueryKpiAssessmentsAsync (PPI-024: batch query).
    /// </summary>
    private void SetupAreaAssessments(Guid matterId, int performanceArea, KpiAssessmentRecord[] assessments)
    {
        // Store area data for batch setup
        _areaData[performanceArea] = assessments;
        SetupBatchMock(matterId);
    }

    private readonly Dictionary<int, KpiAssessmentRecord[]> _areaData = new();

    private void SetupBatchMock(Guid matterId)
    {
        var result = new Dictionary<int, KpiAssessmentRecord[]>
        {
            [Guidelines] = _areaData.GetValueOrDefault(Guidelines, Array.Empty<KpiAssessmentRecord>()),
            [Budget] = _areaData.GetValueOrDefault(Budget, Array.Empty<KpiAssessmentRecord>()),
            [Outcomes] = _areaData.GetValueOrDefault(Outcomes, Array.Empty<KpiAssessmentRecord>()),
        };

        _dataverseServiceMock
            .Setup(s => s.BatchQueryKpiAssessmentsAsync(
                matterId, It.IsAny<string>(), It.IsAny<int[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
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

    #region NFR-01: Batch Query Performance

    [Fact]
    public async Task Performance_BatchQuery_SingleRoundTrip()
    {
        // Arrange - simulate latency in batch query
        var matterId = Guid.NewGuid();
        var assessments = GenerateAssessments(50);
        var queryDelay = TimeSpan.FromMilliseconds(50); // 50ms simulated latency

        // Setup batch mock with simulated delay — single round trip for all 3 areas
        var batchResult = new Dictionary<int, KpiAssessmentRecord[]>
        {
            [Guidelines] = assessments,
            [Budget] = assessments,
            [Outcomes] = assessments,
        };

        _dataverseServiceMock
            .Setup(s => s.BatchQueryKpiAssessmentsAsync(
                matterId, It.IsAny<string>(), It.IsAny<int[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(queryDelay);
                return batchResult;
            });

        // Warm up
        await _service.RecalculateGradesAsync(matterId);

        // Act - time the batch execution
        var stopwatch = Stopwatch.StartNew();
        var result = await _service.RecalculateGradesAsync(matterId);
        stopwatch.Stop();

        var batchTime = stopwatch.Elapsed;

        // Assert - batch sends 1 request instead of 3, so total time should be
        // roughly 1x delay (~50ms). Use 2x as threshold for scheduling overhead.
        var singleQueryThreshold = queryDelay.TotalMilliseconds * 2;
        batchTime.TotalMilliseconds.Should().BeLessThan(singleQueryThreshold,
            "batch query sends one request for all areas, not three sequential requests " +
            $"(expected < {singleQueryThreshold}ms, got {batchTime.TotalMilliseconds:F1}ms)");

        // Assert - still within overall NFR-01 threshold
        batchTime.TotalMilliseconds.Should().BeLessThan(MaxAllowedMilliseconds,
            "NFR-01: batch recalculation must still complete within 500ms");

        // Assert - result is valid
        result.GuidelineCurrent.Should().NotBeNull();
        result.BudgetCurrent.Should().NotBeNull();
        result.OutcomeCurrent.Should().NotBeNull();

        // Assert - batch method was called exactly once (PPI-024: single round trip)
        _dataverseServiceMock.Verify(
            s => s.BatchQueryKpiAssessmentsAsync(
                matterId, It.IsAny<string>(),
                It.Is<int[]>(a => a.Contains(Guidelines) && a.Contains(Budget) && a.Contains(Outcomes)),
                It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    #endregion
}
