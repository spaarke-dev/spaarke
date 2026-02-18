using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// Unit tests for SignalEvaluationService.
/// Tests threshold-based signal detection rules: BudgetExceeded, BudgetWarning, VelocitySpike.
/// </summary>
public class SignalEvaluationServiceTests : IDisposable
{
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly FinanceTelemetry _telemetry;
    private readonly Mock<ILogger<SignalEvaluationService>> _loggerMock;

    private readonly Guid _matterId = Guid.NewGuid();
    private readonly Guid _snapshotId = Guid.NewGuid();

    // Dataverse schema constants (mirrored from service for readability)
    private const int PeriodTypeMonth = 100000000;
    private const int PeriodTypeToDate = 100000003;

    public SignalEvaluationServiceTests()
    {
        _dataverseServiceMock = new Mock<IDataverseService>();
        _telemetry = new FinanceTelemetry();
        _loggerMock = new Mock<ILogger<SignalEvaluationService>>();
    }

    public void Dispose()
    {
        _telemetry.Dispose();
    }

    #region Test Helpers

    /// <summary>
    /// Create the service under test with specified options (or defaults).
    /// </summary>
    private SignalEvaluationService CreateService(FinanceOptions? options = null)
    {
        options ??= new FinanceOptions(); // Defaults: BudgetWarningPercentage=80, VelocitySpikePct=50
        var optionsMock = Options.Create(options);
        return new SignalEvaluationService(
            _dataverseServiceMock.Object,
            optionsMock,
            _telemetry,
            _loggerMock.Object);
    }

    /// <summary>
    /// Set up a single snapshot for the matter.
    /// </summary>
    private void SetupSingleSnapshot(Dictionary<string, object?> fields)
    {
        _dataverseServiceMock
            .Setup(d => d.QueryChildRecordIdsAsync(
                "sprk_spendsnapshot",
                "sprk_matter",
                _matterId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { _snapshotId });

        _dataverseServiceMock
            .Setup(d => d.RetrieveRecordFieldsAsync(
                "sprk_spendsnapshot",
                _snapshotId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(fields);
    }

    /// <summary>
    /// Set up multiple snapshots for the matter.
    /// </summary>
    private void SetupMultipleSnapshots(params (Guid id, Dictionary<string, object?> fields)[] snapshots)
    {
        var ids = snapshots.Select(s => s.id).ToArray();

        _dataverseServiceMock
            .Setup(d => d.QueryChildRecordIdsAsync(
                "sprk_spendsnapshot",
                "sprk_matter",
                _matterId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids);

        foreach (var (id, fields) in snapshots)
        {
            _dataverseServiceMock
                .Setup(d => d.RetrieveRecordFieldsAsync(
                    "sprk_spendsnapshot",
                    id,
                    It.IsAny<string[]>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(fields);
        }
    }

    /// <summary>
    /// Set up the matter with no snapshots.
    /// </summary>
    private void SetupNoSnapshots()
    {
        _dataverseServiceMock
            .Setup(d => d.QueryChildRecordIdsAsync(
                "sprk_spendsnapshot",
                "sprk_matter",
                _matterId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());
    }

    /// <summary>
    /// Build a ToDate snapshot field dictionary for budget evaluation.
    /// </summary>
    private static Dictionary<string, object?> BuildToDateSnapshot(
        decimal invoicedAmount,
        decimal? budgetAmount,
        string periodKey = "2025-todate")
    {
        var fields = new Dictionary<string, object?>
        {
            ["sprk_periodtype"] = PeriodTypeToDate,
            ["sprk_periodkey"] = periodKey,
            ["sprk_bucketkey"] = "total",
            ["sprk_invoicedamount"] = invoicedAmount,
            ["sprk_budgetamount"] = budgetAmount,
            ["sprk_velocitypct"] = null
        };
        return fields;
    }

    /// <summary>
    /// Build a Month snapshot field dictionary for velocity evaluation.
    /// </summary>
    private static Dictionary<string, object?> BuildMonthSnapshot(
        decimal invoicedAmount,
        decimal? velocityPct,
        decimal? budgetAmount = null,
        string periodKey = "2025-01")
    {
        var fields = new Dictionary<string, object?>
        {
            ["sprk_periodtype"] = PeriodTypeMonth,
            ["sprk_periodkey"] = periodKey,
            ["sprk_bucketkey"] = "total",
            ["sprk_invoicedamount"] = invoicedAmount,
            ["sprk_budgetamount"] = budgetAmount,
            ["sprk_velocitypct"] = velocityPct
        };
        return fields;
    }

    /// <summary>
    /// Verify that UpdateRecordFieldsAsync was called for a signal upsert with expected signal type.
    /// </summary>
    private void VerifySignalUpserted(int expectedSignalType, int expectedSeverity, Times? times = null)
    {
        _dataverseServiceMock.Verify(d => d.UpdateRecordFieldsAsync(
            "sprk_spendsignal",
            It.IsAny<Guid>(),
            It.Is<Dictionary<string, object?>>(f =>
                (int)f["sprk_signaltype"]! == expectedSignalType &&
                (int)f["sprk_severity"]! == expectedSeverity),
            It.IsAny<CancellationToken>()),
            times ?? Times.Once());
    }

    /// <summary>
    /// Verify no signal upserts were made.
    /// </summary>
    private void VerifyNoSignalsUpserted()
    {
        _dataverseServiceMock.Verify(d => d.UpdateRecordFieldsAsync(
            "sprk_spendsignal",
            It.IsAny<Guid>(),
            It.IsAny<Dictionary<string, object?>>(),
            It.IsAny<CancellationToken>()),
            Times.Never());
    }

    #endregion

    #region BudgetExceeded Tests

    [Fact]
    public async Task EvaluateAsync_SpendAt100Percent_CreatesBudgetExceededSignal()
    {
        // Arrange
        var fields = BuildToDateSnapshot(invoicedAmount: 10000m, budgetAmount: 10000m);
        SetupSingleSnapshot(fields);
        var sut = CreateService();

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert -- at exactly 100%, BudgetExceeded fires (ratio >= 1.0)
        result.Should().BeGreaterThanOrEqualTo(1);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeBudgetExceeded,
            expectedSeverity: SignalEvaluationService.SeverityCritical);
    }

    [Fact]
    public async Task EvaluateAsync_SpendAbove100Percent_CreatesBudgetExceededSignal()
    {
        // Arrange -- 120% of budget
        var fields = BuildToDateSnapshot(invoicedAmount: 12000m, budgetAmount: 10000m);
        SetupSingleSnapshot(fields);
        var sut = CreateService();

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert
        result.Should().BeGreaterThanOrEqualTo(1);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeBudgetExceeded,
            expectedSeverity: SignalEvaluationService.SeverityCritical);
    }

    [Fact]
    public async Task EvaluateAsync_SpendAt100Percent_DoesNotCreateBudgetWarningSignal()
    {
        // Arrange -- at 100%, BudgetWarning should NOT fire (it only fires below 100%)
        var fields = BuildToDateSnapshot(invoicedAmount: 10000m, budgetAmount: 10000m);
        SetupSingleSnapshot(fields);
        var sut = CreateService();

        // Act
        await sut.EvaluateAsync(_matterId);

        // Assert -- BudgetWarning should not fire when ratio >= 1.0
        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeBudgetWarning,
            expectedSeverity: SignalEvaluationService.SeverityWarning,
            times: Times.Never());
    }

    #endregion

    #region BudgetWarning Tests

    [Fact]
    public async Task EvaluateAsync_SpendAt80Percent_CreatesBudgetWarningSignal()
    {
        // Arrange -- exactly 80% (default threshold)
        var fields = BuildToDateSnapshot(invoicedAmount: 8000m, budgetAmount: 10000m);
        SetupSingleSnapshot(fields);
        var sut = CreateService();

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert
        result.Should().BeGreaterThanOrEqualTo(1);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeBudgetWarning,
            expectedSeverity: SignalEvaluationService.SeverityWarning);
    }

    [Fact]
    public async Task EvaluateAsync_SpendAt90Percent_CreatesBudgetWarningSignal()
    {
        // Arrange -- 90%, above 80% threshold but below 100%
        var fields = BuildToDateSnapshot(invoicedAmount: 9000m, budgetAmount: 10000m);
        SetupSingleSnapshot(fields);
        var sut = CreateService();

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert
        result.Should().BeGreaterThanOrEqualTo(1);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeBudgetWarning,
            expectedSeverity: SignalEvaluationService.SeverityWarning);
    }

    [Fact]
    public async Task EvaluateAsync_SpendJustBelow80Percent_DoesNotCreateBudgetWarningSignal()
    {
        // Arrange -- 79.99% spend, just under the 80% default threshold
        var fields = BuildToDateSnapshot(invoicedAmount: 7999m, budgetAmount: 10000m);
        SetupSingleSnapshot(fields);
        var sut = CreateService();

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert
        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeBudgetWarning,
            expectedSeverity: SignalEvaluationService.SeverityWarning,
            times: Times.Never());
    }

    #endregion

    #region VelocitySpike Tests

    [Fact]
    public async Task EvaluateAsync_VelocityIncrease50Percent_CreatesVelocitySpikeSignal()
    {
        // Arrange -- exactly 50% velocity increase (default threshold)
        var fields = BuildMonthSnapshot(invoicedAmount: 5000m, velocityPct: 50m);
        SetupSingleSnapshot(fields);
        var sut = CreateService();

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert
        result.Should().Be(1);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeVelocitySpike,
            expectedSeverity: SignalEvaluationService.SeverityWarning);
    }

    [Fact]
    public async Task EvaluateAsync_VelocityAbove50Percent_CreatesVelocitySpikeSignal()
    {
        // Arrange -- 75% velocity spike, above default 50% threshold
        var fields = BuildMonthSnapshot(invoicedAmount: 5000m, velocityPct: 75m);
        SetupSingleSnapshot(fields);
        var sut = CreateService();

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert
        result.Should().Be(1);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeVelocitySpike,
            expectedSeverity: SignalEvaluationService.SeverityWarning);
    }

    [Fact]
    public async Task EvaluateAsync_VelocityJustBelow50Percent_DoesNotCreateVelocitySpikeSignal()
    {
        // Arrange -- 49.9% velocity, just under the 50% default threshold
        var fields = BuildMonthSnapshot(invoicedAmount: 5000m, velocityPct: 49.9m);
        SetupSingleSnapshot(fields);
        var sut = CreateService();

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert
        result.Should().Be(0);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeVelocitySpike,
            expectedSeverity: SignalEvaluationService.SeverityWarning,
            times: Times.Never());
    }

    #endregion

    #region Within All Thresholds (No Signals)

    [Fact]
    public async Task EvaluateAsync_SpendWithinAllThresholds_CreatesNoSignals()
    {
        // Arrange -- ToDate snapshot at 50% budget (well under 80% warning), no velocity spike
        var toDateSnapshot = BuildToDateSnapshot(invoicedAmount: 5000m, budgetAmount: 10000m);
        var monthSnapshot = BuildMonthSnapshot(invoicedAmount: 3000m, velocityPct: 10m);

        var toDateId = Guid.NewGuid();
        var monthId = Guid.NewGuid();

        SetupMultipleSnapshots(
            (toDateId, toDateSnapshot),
            (monthId, monthSnapshot));

        var sut = CreateService();

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert
        result.Should().Be(0);
        VerifyNoSignalsUpserted();
    }

    [Fact]
    public async Task EvaluateAsync_NoSnapshots_ReturnsZeroSignals()
    {
        // Arrange
        SetupNoSnapshots();
        var sut = CreateService();

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert
        result.Should().Be(0);
        VerifyNoSignalsUpserted();
    }

    #endregion

    #region Custom Threshold Tests

    [Fact]
    public async Task EvaluateAsync_CustomBudgetWarningThreshold_UsesConfiguredValue()
    {
        // Arrange -- set threshold to 60% and spend at 65%
        var options = new FinanceOptions { BudgetWarningPercentage = 60m };
        var fields = BuildToDateSnapshot(invoicedAmount: 6500m, budgetAmount: 10000m);
        SetupSingleSnapshot(fields);
        var sut = CreateService(options);

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert -- 65% exceeds the custom 60% threshold
        result.Should().BeGreaterThanOrEqualTo(1);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeBudgetWarning,
            expectedSeverity: SignalEvaluationService.SeverityWarning);
    }

    [Fact]
    public async Task EvaluateAsync_CustomBudgetWarningThreshold_DoesNotFireBelowConfiguredValue()
    {
        // Arrange -- set threshold to 90% and spend at 85%
        var options = new FinanceOptions { BudgetWarningPercentage = 90m };
        var fields = BuildToDateSnapshot(invoicedAmount: 8500m, budgetAmount: 10000m);
        SetupSingleSnapshot(fields);
        var sut = CreateService(options);

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert -- 85% is below the custom 90% threshold, so no warning
        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeBudgetWarning,
            expectedSeverity: SignalEvaluationService.SeverityWarning,
            times: Times.Never());
    }

    [Fact]
    public async Task EvaluateAsync_CustomVelocitySpikeThreshold_UsesConfiguredValue()
    {
        // Arrange -- set threshold to 30% and velocity at 35%
        var options = new FinanceOptions { VelocitySpikePct = 30m };
        var fields = BuildMonthSnapshot(invoicedAmount: 5000m, velocityPct: 35m);
        SetupSingleSnapshot(fields);
        var sut = CreateService(options);

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert -- 35% exceeds the custom 30% threshold
        result.Should().Be(1);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeVelocitySpike,
            expectedSeverity: SignalEvaluationService.SeverityWarning);
    }

    [Fact]
    public async Task EvaluateAsync_CustomVelocitySpikeThreshold_DoesNotFireBelowConfiguredValue()
    {
        // Arrange -- set threshold to 75% and velocity at 60%
        var options = new FinanceOptions { VelocitySpikePct = 75m };
        var fields = BuildMonthSnapshot(invoicedAmount: 5000m, velocityPct: 60m);
        SetupSingleSnapshot(fields);
        var sut = CreateService(options);

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert -- 60% is below the custom 75% threshold
        result.Should().Be(0);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeVelocitySpike,
            expectedSeverity: SignalEvaluationService.SeverityWarning,
            times: Times.Never());
    }

    #endregion

    #region NoBudget Tests

    [Fact]
    public async Task EvaluateAsync_NoBudget_SkipsBudgetSignals()
    {
        // Arrange -- ToDate snapshot with null budget
        var fields = BuildToDateSnapshot(invoicedAmount: 10000m, budgetAmount: null);
        SetupSingleSnapshot(fields);
        var sut = CreateService();

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert -- neither BudgetExceeded nor BudgetWarning should fire
        result.Should().Be(0);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeBudgetExceeded,
            expectedSeverity: SignalEvaluationService.SeverityCritical,
            times: Times.Never());

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeBudgetWarning,
            expectedSeverity: SignalEvaluationService.SeverityWarning,
            times: Times.Never());
    }

    [Fact]
    public async Task EvaluateAsync_ZeroBudget_SkipsBudgetSignals()
    {
        // Arrange -- ToDate snapshot with zero budget (treated same as no budget)
        var fields = BuildToDateSnapshot(invoicedAmount: 10000m, budgetAmount: 0m);
        SetupSingleSnapshot(fields);
        var sut = CreateService();

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert -- budget <= 0 should be treated as no budget
        result.Should().Be(0);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeBudgetExceeded,
            expectedSeverity: SignalEvaluationService.SeverityCritical,
            times: Times.Never());
    }

    #endregion

    #region Velocity Edge Cases

    [Fact]
    public async Task EvaluateAsync_NullVelocity_SkipsVelocitySpikeSignal()
    {
        // Arrange -- Month snapshot with null velocity
        var fields = BuildMonthSnapshot(invoicedAmount: 5000m, velocityPct: null);
        SetupSingleSnapshot(fields);
        var sut = CreateService();

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert
        result.Should().Be(0);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeVelocitySpike,
            expectedSeverity: SignalEvaluationService.SeverityWarning,
            times: Times.Never());
    }

    [Fact]
    public async Task EvaluateAsync_VelocityOnToDateSnapshot_DoesNotFireVelocitySpike()
    {
        // Arrange -- ToDate snapshot has velocity data but VelocitySpike only checks Month
        var fields = new Dictionary<string, object?>
        {
            ["sprk_periodtype"] = PeriodTypeToDate,
            ["sprk_periodkey"] = "2025-todate",
            ["sprk_bucketkey"] = "total",
            ["sprk_invoicedamount"] = 5000m,
            ["sprk_budgetamount"] = 50000m, // well within budget
            ["sprk_velocitypct"] = 100m // high velocity, but on ToDate snapshot
        };
        SetupSingleSnapshot(fields);
        var sut = CreateService();

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert -- velocity rule ignores ToDate snapshots
        result.Should().Be(0);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeVelocitySpike,
            expectedSeverity: SignalEvaluationService.SeverityWarning,
            times: Times.Never());
    }

    #endregion

    #region Multiple Signals Tests

    [Fact]
    public async Task EvaluateAsync_BudgetExceededAndVelocitySpike_CreatesBothSignals()
    {
        // Arrange -- one ToDate at 110% budget, one Month at 60% velocity
        var toDateId = Guid.NewGuid();
        var monthId = Guid.NewGuid();

        var toDateFields = BuildToDateSnapshot(invoicedAmount: 11000m, budgetAmount: 10000m);
        var monthFields = BuildMonthSnapshot(invoicedAmount: 5000m, velocityPct: 60m);

        SetupMultipleSnapshots(
            (toDateId, toDateFields),
            (monthId, monthFields));

        var sut = CreateService();

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert -- BudgetExceeded + VelocitySpike = 2 signals
        result.Should().Be(2);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeBudgetExceeded,
            expectedSeverity: SignalEvaluationService.SeverityCritical);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeVelocitySpike,
            expectedSeverity: SignalEvaluationService.SeverityWarning);
    }

    [Fact]
    public async Task EvaluateAsync_BudgetWarningAndVelocitySpike_CreatesBothSignals()
    {
        // Arrange -- one ToDate at 85% budget (warning), one Month at 55% velocity
        var toDateId = Guid.NewGuid();
        var monthId = Guid.NewGuid();

        var toDateFields = BuildToDateSnapshot(invoicedAmount: 8500m, budgetAmount: 10000m);
        var monthFields = BuildMonthSnapshot(invoicedAmount: 5000m, velocityPct: 55m);

        SetupMultipleSnapshots(
            (toDateId, toDateFields),
            (monthId, monthFields));

        var sut = CreateService();

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert -- BudgetWarning + VelocitySpike = 2 signals
        result.Should().Be(2);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeBudgetWarning,
            expectedSeverity: SignalEvaluationService.SeverityWarning);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeVelocitySpike,
            expectedSeverity: SignalEvaluationService.SeverityWarning);
    }

    #endregion

    #region Empty/Missing Snapshot Fields

    [Fact]
    public async Task EvaluateAsync_SnapshotWithEmptyFields_SkipsSnapshot()
    {
        // Arrange -- snapshot returns empty field dictionary
        _dataverseServiceMock
            .Setup(d => d.QueryChildRecordIdsAsync(
                "sprk_spendsnapshot",
                "sprk_matter",
                _matterId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { _snapshotId });

        _dataverseServiceMock
            .Setup(d => d.RetrieveRecordFieldsAsync(
                "sprk_spendsnapshot",
                _snapshotId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object?>());

        var sut = CreateService();

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert
        result.Should().Be(0);
        VerifyNoSignalsUpserted();
    }

    #endregion

    #region Deterministic ID Tests

    [Fact]
    public void GenerateDeterministicId_SameMatterAndSignalType_ReturnsSameId()
    {
        // Arrange
        var matterId = Guid.NewGuid();

        // Act
        var id1 = SignalEvaluationService.GenerateDeterministicId(matterId, SignalEvaluationService.SignalTypeBudgetExceeded);
        var id2 = SignalEvaluationService.GenerateDeterministicId(matterId, SignalEvaluationService.SignalTypeBudgetExceeded);

        // Assert
        id1.Should().Be(id2);
    }

    [Fact]
    public void GenerateDeterministicId_DifferentSignalTypes_ReturnsDifferentIds()
    {
        // Arrange
        var matterId = Guid.NewGuid();

        // Act
        var budgetExceededId = SignalEvaluationService.GenerateDeterministicId(matterId, SignalEvaluationService.SignalTypeBudgetExceeded);
        var budgetWarningId = SignalEvaluationService.GenerateDeterministicId(matterId, SignalEvaluationService.SignalTypeBudgetWarning);
        var velocitySpikeId = SignalEvaluationService.GenerateDeterministicId(matterId, SignalEvaluationService.SignalTypeVelocitySpike);

        // Assert
        budgetExceededId.Should().NotBe(budgetWarningId);
        budgetExceededId.Should().NotBe(velocitySpikeId);
        budgetWarningId.Should().NotBe(velocitySpikeId);
    }

    [Fact]
    public void GenerateDeterministicId_DifferentMatters_ReturnsDifferentIds()
    {
        // Arrange
        var matterId1 = Guid.NewGuid();
        var matterId2 = Guid.NewGuid();

        // Act
        var id1 = SignalEvaluationService.GenerateDeterministicId(matterId1, SignalEvaluationService.SignalTypeBudgetExceeded);
        var id2 = SignalEvaluationService.GenerateDeterministicId(matterId2, SignalEvaluationService.SignalTypeBudgetExceeded);

        // Assert
        id1.Should().NotBe(id2);
    }

    #endregion

    #region Period Type Filtering

    [Fact]
    public async Task EvaluateAsync_MonthSnapshot_DoesNotTriggerBudgetRules()
    {
        // Arrange -- Month snapshot with budget data; budget rules only apply to ToDate
        var fields = BuildMonthSnapshot(invoicedAmount: 10000m, velocityPct: null, budgetAmount: 5000m);
        SetupSingleSnapshot(fields);
        var sut = CreateService();

        // Act
        var result = await sut.EvaluateAsync(_matterId);

        // Assert -- budget rules ignore Month snapshots
        result.Should().Be(0);

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeBudgetExceeded,
            expectedSeverity: SignalEvaluationService.SeverityCritical,
            times: Times.Never());

        VerifySignalUpserted(
            expectedSignalType: SignalEvaluationService.SignalTypeBudgetWarning,
            expectedSeverity: SignalEvaluationService.SeverityWarning,
            times: Times.Never());
    }

    #endregion
}
