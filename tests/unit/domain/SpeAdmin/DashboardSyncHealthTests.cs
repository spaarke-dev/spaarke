using FluentAssertions;
using Sprk.Bff.Api.Services.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.SpeAdmin;

/// <summary>
/// Pure-logic tests (ADR-038 §2 path #6 — no mocks, no DI, no I/O) for the dashboard sync-health rule
/// introduced by <c>sdap-SPE-admin-app-r2</c> task 003 (spec FR-A03).
/// </summary>
/// <remarks>
/// What breaks if these are deleted: the SPE Admin Dashboard could once again report Sync Status "OK" while
/// a concern was failing. That is the single most direct instance of the spec §2.4 systemic defect — the app
/// reporting success when it is not succeeding — and it is what this project was raised to correct.
/// </remarks>
public class DashboardSyncHealthTests
{
    private static SpeDashboardSyncService.ConcernOutcome Ok(string name) =>
        new() { Concern = name, Succeeded = true };

    private static SpeDashboardSyncService.ConcernOutcome Failed(string name, string reason = "boom") =>
        new() { Concern = name, Succeeded = false, Reason = reason };

    [Fact]
    public void DeriveHealth_WhenEveryConcernSucceeded_ReturnsHealthy()
    {
        var concerns = new[] { Ok("Dataverse container-type configs"), Ok("Graph containers (config A)") };

        SpeDashboardSyncService.DeriveHealth(concerns)
            .Should().Be(SpeDashboardSyncService.SyncHealth.Healthy);
    }

    [Fact]
    public void DeriveHealth_WhenSomeSucceededAndSomeFailed_ReturnsDegraded()
    {
        var concerns = new[]
        {
            Ok("Dataverse container-type configs"),
            Failed("Graph containers (config B)", "403 accessDenied")
        };

        SpeDashboardSyncService.DeriveHealth(concerns)
            .Should().Be(SpeDashboardSyncService.SyncHealth.Degraded);
    }

    [Fact]
    public void DeriveHealth_WhenEveryConcernFailed_ReturnsFailed()
    {
        var concerns = new[] { Failed("Dataverse container-type configs"), Failed("Graph containers (config A)") };

        SpeDashboardSyncService.DeriveHealth(concerns)
            .Should().Be(SpeDashboardSyncService.SyncHealth.Failed);
    }

    [Fact]
    public void DeriveHealth_WhenExactlyOneConcernFailed_IsNeverHealthy()
    {
        // The regression guard. A single failing concern used to be invisible behind an "OK" tile.
        var concerns = new[] { Failed("Dataverse container-type configs", "Dataverse query failed.") };

        var health = SpeDashboardSyncService.DeriveHealth(concerns);

        health.Should().NotBe(SpeDashboardSyncService.SyncHealth.Healthy);
        health.Should().Be(SpeDashboardSyncService.SyncHealth.Failed);
    }

    [Fact]
    public void DeriveHealth_WhenNoConcernWasAttempted_ReturnsHealthy()
    {
        // Nothing was attempted, so nothing failed. Callers that attempt work always record a concern.
        SpeDashboardSyncService.DeriveHealth(Array.Empty<SpeDashboardSyncService.ConcernOutcome>())
            .Should().Be(SpeDashboardSyncService.SyncHealth.Healthy);
    }

    [Theory]
    [InlineData(1, 9, SpeDashboardSyncService.SyncHealth.Degraded)]
    [InlineData(9, 1, SpeDashboardSyncService.SyncHealth.Degraded)]
    [InlineData(10, 0, SpeDashboardSyncService.SyncHealth.Healthy)]
    [InlineData(0, 10, SpeDashboardSyncService.SyncHealth.Failed)]
    public void DeriveHealth_GivenMixOfOutcomes_ReturnsExpectedHealth(
        int okCount, int failedCount, SpeDashboardSyncService.SyncHealth expected)
    {
        var concerns = Enumerable.Range(0, okCount).Select(i => Ok($"ok-{i}"))
            .Concat(Enumerable.Range(0, failedCount).Select(i => Failed($"bad-{i}")))
            .ToList();

        SpeDashboardSyncService.DeriveHealth(concerns).Should().Be(expected);
    }
}
