using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Metering;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai.Metering;

/// <summary>
/// Pure-domain tests (ADR-038 §2 path #6) for <see cref="InMemoryTenantTokenLedger"/> — the
/// month-to-date USD spend tracker used by <see cref="TenantBudgetPolicy"/> to make pre-call gate
/// decisions (customer-provisioning-orchestration-r1 task 077).
/// </summary>
/// <remarks>
/// Uses <see cref="FakeTimeProvider"/> per tests/CLAUDE.md "TimeProvider over Stopwatch" rule —
/// month-boundary behavior is deterministic without real clock.
/// </remarks>
public sealed class InMemoryTenantTokenLedgerTests
{
    [Fact]
    public void GetMonthToDateSpendUsd_ForUnseenTenant_ReturnsZero()
    {
        var ledger = new InMemoryTenantTokenLedger();

        ledger.GetMonthToDateSpendUsd("never-seen-tenant").Should().Be(0m);
    }

    [Fact]
    public void AddSpend_ThenGet_ReturnsAccruedTotal()
    {
        var ledger = new InMemoryTenantTokenLedger();
        var tenantId = Guid.NewGuid().ToString();

        ledger.AddSpend(tenantId, 1.25m);
        ledger.AddSpend(tenantId, 0.75m);
        ledger.AddSpend(tenantId, 2.50m);

        ledger.GetMonthToDateSpendUsd(tenantId).Should().Be(4.50m);
    }

    [Fact]
    public void AddSpend_IsTenantScoped_DoesNotBleedAcrossTenants()
    {
        var ledger = new InMemoryTenantTokenLedger();
        var tenantA = Guid.NewGuid().ToString();
        var tenantB = Guid.NewGuid().ToString();

        ledger.AddSpend(tenantA, 10.00m);
        ledger.AddSpend(tenantB, 3.00m);

        ledger.GetMonthToDateSpendUsd(tenantA).Should().Be(10.00m);
        ledger.GetMonthToDateSpendUsd(tenantB).Should().Be(3.00m);
    }

    [Fact]
    public void AddSpend_IsCaseInsensitiveByTenantId()
    {
        var ledger = new InMemoryTenantTokenLedger();
        var tenant = "AABBCCDD-1234-5678-9ABC-DEFAABBCCDDE";

        ledger.AddSpend(tenant.ToUpperInvariant(), 5.00m);
        ledger.AddSpend(tenant.ToLowerInvariant(), 3.00m);

        ledger.GetMonthToDateSpendUsd(tenant).Should().Be(8.00m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void AddSpend_WithZeroOrNegativeDelta_IsIgnored(decimal delta)
    {
        var ledger = new InMemoryTenantTokenLedger();
        var tenantId = Guid.NewGuid().ToString();

        ledger.AddSpend(tenantId, 5.00m);
        ledger.AddSpend(tenantId, delta);

        ledger.GetMonthToDateSpendUsd(tenantId).Should().Be(5.00m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddSpend_WithMissingTenantId_IsIgnored(string? tenantId)
    {
        var ledger = new InMemoryTenantTokenLedger();

        // Must not throw; must not accrue to any bucket.
        ledger.AddSpend(tenantId!, 5.00m);
        ledger.GetMonthToDateSpendUsd(Guid.NewGuid().ToString()).Should().Be(0m);
    }

    [Fact]
    public void GetMonthToDateSpendUsd_ResetsOnMonthBoundary()
    {
        // Local mutable TimeProvider crosses a month boundary deterministically (per
        // tests/CLAUDE.md "TimeProvider over Stopwatch" rule — same local-fake pattern used by
        // Services/Ai/Memory/RecentlyDiscussedTrackerTests).
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-01-15T12:00:00Z"));
        var ledger = new InMemoryTenantTokenLedger(time);
        var tenantId = Guid.NewGuid().ToString();

        // January accrual
        ledger.AddSpend(tenantId, 25.00m);
        ledger.GetMonthToDateSpendUsd(tenantId).Should().Be(25.00m);

        // Advance to February — new bucket, MTD resets to zero
        time.Set(DateTimeOffset.Parse("2026-02-01T00:00:01Z"));
        ledger.GetMonthToDateSpendUsd(tenantId).Should().Be(0m,
            "monthly resets are the load-bearing semantic for FR-13 §M1 tokenBudgetMonthlyUSD");

        // February accrual is independent
        ledger.AddSpend(tenantId, 3.50m);
        ledger.GetMonthToDateSpendUsd(tenantId).Should().Be(3.50m);
    }

    /// <summary>
    /// Minimal mutable <see cref="TimeProvider"/> for month-boundary tests. Same shape as the
    /// local <c>FakeTimeProvider</c> in <c>RecentlyDiscussedTrackerTests</c>; both projects
    /// intentionally avoid the <c>Microsoft.Extensions.TimeProvider.Testing</c> package (not
    /// referenced in the test csproj) in favor of a 3-line local double.
    /// </summary>
    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        public MutableTimeProvider(DateTimeOffset initial) => _utcNow = initial;
        public void Set(DateTimeOffset next) => _utcNow = next;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
