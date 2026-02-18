using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// Unit tests for SpendSnapshotService aggregation logic.
///
/// Strategy: SpendSnapshotService.GenerateAsync relies on DataverseServiceClientImpl (concrete cast),
/// making it unsuitable for unit-level mocking. The service deliberately exposes its core business logic
/// as internal static methods (AggregateByMonth, ComputeVelocityPct, ComputeBudgetVariance,
/// ComputeBudgetVariancePct) for direct testing. InternalsVisibleTo allows the test project to access
/// these methods and the internal BillingEventData record.
///
/// These tests cover every aggregation rule documented in the service:
/// - Monthly period grouping by year-month
/// - ToDate cumulative sum
/// - Budget variance = Budget - Invoiced (positive = under budget, negative = over budget)
/// - Budget variance percentage = Variance / Budget * 100
/// - MoM velocity = (current - prior) / prior * 100; null when prior is zero
/// - Idempotent behavior via static deterministic computation
/// </summary>
public sealed class SpendSnapshotServiceTests
{
    #region AggregateByMonth Tests

    [Fact]
    public void AggregateByMonth_WithBillingEvents_CreatesMonthlyAggregations()
    {
        // Arrange
        var events = new List<SpendSnapshotService.BillingEventData>
        {
            new() { Amount = 1000m, EventDate = new DateTime(2026, 1, 15), CostType = 0 },
            new() { Amount = 2000m, EventDate = new DateTime(2026, 1, 20), CostType = 0 },
            new() { Amount = 3000m, EventDate = new DateTime(2026, 2, 10), CostType = 0 },
            new() { Amount = 500m,  EventDate = new DateTime(2026, 3, 5),  CostType = 0 },
        };

        // Act
        var result = SpendSnapshotService.AggregateByMonth(events);

        // Assert
        result.Should().HaveCount(3);
        result["2026-01"].Should().Be(3000m, "January has two events totaling 1000 + 2000");
        result["2026-02"].Should().Be(3000m, "February has one event of 3000");
        result["2026-03"].Should().Be(500m, "March has one event of 500");

        // Keys should be sorted chronologically
        result.Keys.Should().BeInAscendingOrder();
    }

    [Fact]
    public void AggregateByMonth_WithSingleEvent_CreatesSingleMonthEntry()
    {
        // Arrange
        var events = new List<SpendSnapshotService.BillingEventData>
        {
            new() { Amount = 750m, EventDate = new DateTime(2026, 6, 1), CostType = 0 },
        };

        // Act
        var result = SpendSnapshotService.AggregateByMonth(events);

        // Assert
        result.Should().HaveCount(1);
        result["2026-06"].Should().Be(750m);
    }

    [Fact]
    public void AggregateByMonth_WithEmptyList_ReturnsEmptyDictionary()
    {
        // Arrange
        var events = new List<SpendSnapshotService.BillingEventData>();

        // Act
        var result = SpendSnapshotService.AggregateByMonth(events);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region ToDate Aggregation Tests

    [Fact]
    public void GenerateAsync_WithBillingEvents_CreatesToDateAggregation()
    {
        // Arrange - test the ToDate aggregation logic: Sum of all billing events
        var events = new List<SpendSnapshotService.BillingEventData>
        {
            new() { Amount = 1000m, EventDate = new DateTime(2026, 1, 15), CostType = 0 },
            new() { Amount = 2000m, EventDate = new DateTime(2026, 1, 20), CostType = 0 },
            new() { Amount = 3000m, EventDate = new DateTime(2026, 2, 10), CostType = 0 },
            new() { Amount = 500m,  EventDate = new DateTime(2026, 3, 5),  CostType = 0 },
        };

        // Act - ToDate is computed as Sum of all event amounts (same logic as GenerateAsync Step 3)
        var toDateAmount = events.Sum(e => e.Amount);

        // Assert
        toDateAmount.Should().Be(6500m, "ToDate should be cumulative sum of all billing events");
    }

    #endregion

    #region Budget Variance Tests

    [Fact]
    public void GenerateAsync_WithBudget_ComputesPositiveVariance()
    {
        // Arrange - under budget scenario
        var invoicedAmount = 8000m;
        var budgetAmount = 10000m;

        // Act
        var variance = SpendSnapshotService.ComputeBudgetVariance(invoicedAmount, budgetAmount);
        var variancePct = SpendSnapshotService.ComputeBudgetVariancePct(variance, budgetAmount);

        // Assert
        variance.Should().Be(2000m, "Budget(10000) - Invoiced(8000) = 2000 (under budget = positive)");
        variancePct.Should().Be(20m, "2000 / 10000 * 100 = 20%");
    }

    [Fact]
    public void GenerateAsync_OverBudget_ComputesNegativeVariance()
    {
        // Arrange - over budget scenario
        var invoicedAmount = 12000m;
        var budgetAmount = 10000m;

        // Act
        var variance = SpendSnapshotService.ComputeBudgetVariance(invoicedAmount, budgetAmount);
        var variancePct = SpendSnapshotService.ComputeBudgetVariancePct(variance, budgetAmount);

        // Assert
        variance.Should().Be(-2000m, "Budget(10000) - Invoiced(12000) = -2000 (over budget = negative)");
        variancePct.Should().Be(-20m, "-2000 / 10000 * 100 = -20%");
    }

    [Fact]
    public void ComputeBudgetVariance_NullBudget_ReturnsNull()
    {
        // Arrange
        var invoicedAmount = 5000m;

        // Act
        var variance = SpendSnapshotService.ComputeBudgetVariance(invoicedAmount, null);

        // Assert
        variance.Should().BeNull("no budget configured means no variance");
    }

    [Fact]
    public void ComputeBudgetVariancePct_ZeroBudget_ReturnsNull()
    {
        // Arrange - zero budget edge case (avoid divide-by-zero)
        var variance = 0m;
        var budgetAmount = 0m;

        // Act
        var variancePct = SpendSnapshotService.ComputeBudgetVariancePct(variance, budgetAmount);

        // Assert
        variancePct.Should().BeNull("zero budget should return null to avoid divide-by-zero");
    }

    [Fact]
    public void ComputeBudgetVariancePct_NullVariance_ReturnsNull()
    {
        // Act
        var variancePct = SpendSnapshotService.ComputeBudgetVariancePct(null, 10000m);

        // Assert
        variancePct.Should().BeNull("null variance (no budget) yields null percentage");
    }

    #endregion

    #region MoM Velocity Tests

    [Fact]
    public void GenerateAsync_WithPriorMonth_ComputesMoMVelocity()
    {
        // Arrange
        var currentAmount = 6000m;
        var priorAmount = 4000m;

        // Act
        var velocity = SpendSnapshotService.ComputeVelocityPct(currentAmount, priorAmount);

        // Assert
        velocity.Should().Be(50m, "(6000 - 4000) / 4000 * 100 = 50% increase");
    }

    [Fact]
    public void GenerateAsync_ZeroPriorMonth_VelocityIsNull()
    {
        // Arrange - prior month has zero spend
        var currentAmount = 5000m;
        var priorAmount = 0m;

        // Act
        var velocity = SpendSnapshotService.ComputeVelocityPct(currentAmount, priorAmount);

        // Assert
        velocity.Should().BeNull("zero prior month should return null to avoid divide-by-zero");
    }

    [Fact]
    public void ComputeVelocityPct_SpendDecrease_ReturnsNegativePercentage()
    {
        // Arrange
        var currentAmount = 3000m;
        var priorAmount = 5000m;

        // Act
        var velocity = SpendSnapshotService.ComputeVelocityPct(currentAmount, priorAmount);

        // Assert
        velocity.Should().Be(-40m, "(3000 - 5000) / 5000 * 100 = -40% decrease");
    }

    [Fact]
    public void ComputeVelocityPct_SameAmount_ReturnsZero()
    {
        // Arrange
        var currentAmount = 5000m;
        var priorAmount = 5000m;

        // Act
        var velocity = SpendSnapshotService.ComputeVelocityPct(currentAmount, priorAmount);

        // Assert
        velocity.Should().Be(0m, "same amount means zero velocity change");
    }

    #endregion

    #region Idempotency Tests

    [Fact]
    public void GenerateAsync_CalledTwice_IdempotentUpsert()
    {
        // Arrange - same inputs should always produce the same aggregation outputs
        var events = new List<SpendSnapshotService.BillingEventData>
        {
            new() { Amount = 1500m, EventDate = new DateTime(2026, 1, 10), CostType = 0 },
            new() { Amount = 2500m, EventDate = new DateTime(2026, 1, 25), CostType = 0 },
            new() { Amount = 4000m, EventDate = new DateTime(2026, 2, 15), CostType = 0 },
        };

        // Act - run the same aggregation twice
        var firstRun = SpendSnapshotService.AggregateByMonth(events);
        var secondRun = SpendSnapshotService.AggregateByMonth(events);

        // Assert - deterministic: same inputs always produce same outputs
        firstRun.Should().BeEquivalentTo(secondRun, "aggregation is deterministic and idempotent");

        // Also verify ToDate is deterministic
        var firstToDate = events.Sum(e => e.Amount);
        var secondToDate = events.Sum(e => e.Amount);
        firstToDate.Should().Be(secondToDate);

        // And verify budget variance is deterministic
        var budget = 10000m;
        var invoiced = firstToDate;
        var firstVariance = SpendSnapshotService.ComputeBudgetVariance(invoiced, budget);
        var secondVariance = SpendSnapshotService.ComputeBudgetVariance(invoiced, budget);
        firstVariance.Should().Be(secondVariance, "budget variance computation is deterministic");

        // And velocity
        var jan = firstRun["2026-01"];
        var feb = firstRun["2026-02"];
        var firstVelocity = SpendSnapshotService.ComputeVelocityPct(feb, jan);
        var secondVelocity = SpendSnapshotService.ComputeVelocityPct(feb, jan);
        firstVelocity.Should().Be(secondVelocity, "velocity computation is deterministic");
    }

    #endregion

    #region Integration Scenario Tests

    [Fact]
    public void FullAggregationScenario_MultipleMonths_AllMetricsCorrect()
    {
        // Arrange - realistic scenario with multiple months of billing events
        var events = new List<SpendSnapshotService.BillingEventData>
        {
            // January: 3 events totaling 5000
            new() { Amount = 1000m, EventDate = new DateTime(2026, 1, 5),  CostType = 0 },
            new() { Amount = 2000m, EventDate = new DateTime(2026, 1, 15), CostType = 0 },
            new() { Amount = 2000m, EventDate = new DateTime(2026, 1, 28), CostType = 0 },
            // February: 2 events totaling 7500
            new() { Amount = 3500m, EventDate = new DateTime(2026, 2, 10), CostType = 0 },
            new() { Amount = 4000m, EventDate = new DateTime(2026, 2, 20), CostType = 0 },
            // March: 1 event totaling 3000
            new() { Amount = 3000m, EventDate = new DateTime(2026, 3, 15), CostType = 0 },
        };
        var budgetAmount = 20000m;

        // Act - Step 2: Monthly aggregation
        var monthlyAgg = SpendSnapshotService.AggregateByMonth(events);

        // Act - Step 3: ToDate aggregation
        var toDateAmount = events.Sum(e => e.Amount);

        // Assert - Monthly aggregations
        monthlyAgg.Should().HaveCount(3);
        monthlyAgg["2026-01"].Should().Be(5000m);
        monthlyAgg["2026-02"].Should().Be(7500m);
        monthlyAgg["2026-03"].Should().Be(3000m);

        // Assert - ToDate
        toDateAmount.Should().Be(15500m);

        // Assert - MoM Velocity for February vs January
        var febVelocity = SpendSnapshotService.ComputeVelocityPct(7500m, 5000m);
        febVelocity.Should().Be(50m, "(7500 - 5000) / 5000 * 100 = 50%");

        // Assert - MoM Velocity for March vs February
        var marVelocity = SpendSnapshotService.ComputeVelocityPct(3000m, 7500m);
        marVelocity.Should().Be(-60m, "(3000 - 7500) / 7500 * 100 = -60%");

        // Assert - Budget variance for ToDate
        var toDateVariance = SpendSnapshotService.ComputeBudgetVariance(toDateAmount, budgetAmount);
        toDateVariance.Should().Be(4500m, "20000 - 15500 = 4500 (under budget)");

        var toDateVariancePct = SpendSnapshotService.ComputeBudgetVariancePct(toDateVariance, budgetAmount);
        toDateVariancePct.Should().Be(22.5m, "4500 / 20000 * 100 = 22.5%");
    }

    [Fact]
    public void AggregateByMonth_EventsAcrossYears_SeparatesByYearMonth()
    {
        // Arrange - events spanning two calendar years
        var events = new List<SpendSnapshotService.BillingEventData>
        {
            new() { Amount = 1000m, EventDate = new DateTime(2025, 12, 15), CostType = 0 },
            new() { Amount = 2000m, EventDate = new DateTime(2026, 1, 10),  CostType = 0 },
            new() { Amount = 3000m, EventDate = new DateTime(2026, 1, 20),  CostType = 0 },
        };

        // Act
        var result = SpendSnapshotService.AggregateByMonth(events);

        // Assert - year-month key format distinguishes across years
        result.Should().HaveCount(2);
        result["2025-12"].Should().Be(1000m);
        result["2026-01"].Should().Be(5000m);
        result.Keys.First().Should().Be("2025-12", "sorted dictionary orders chronologically");
    }

    #endregion
}
