using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Audit;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Audit;

/// <summary>
/// Unit tests for <see cref="AuditPartitionKey"/> (task AIR2-074 / FR-D-05).
///
/// The audit container was re-keyed off bare <c>/tenantId</c> onto a synthetic
/// <c>{tenantId}|{yyyy-MM}</c> value so a customer-dedicated tenant's audit trail does not collapse
/// into one logical partition against the Cosmos 20 GB cap. These are pure-function tests (no mocks,
/// no I/O) — the right shape for domain logic per ADR-038.
/// </summary>
public class AuditPartitionKeyTests
{
    // =========================================================================
    // Format: {tenantId}|{yyyy-MM}
    // =========================================================================

    [Fact]
    public void Build_ComposesTenantAndMonthlyBucket_NotBareTenantId()
    {
        var ts = new DateTimeOffset(2026, 7, 10, 13, 45, 0, TimeSpan.Zero);

        var pk = AuditPartitionKey.Build("contoso", ts);

        pk.Should().Be("contoso|2026-07");
        pk.Should().NotBe("contoso", "bare /tenantId must no longer be the partition key (AIR2-074 re-key)");
    }

    [Fact]
    public void Build_BucketsByUtcMonth_RegardlessOfTimezoneOffset()
    {
        // 2026-03-31 23:30 -02:00 == 2026-04-01 01:30 UTC → bucket is the UTC month (April).
        var ts = new DateTimeOffset(2026, 3, 31, 23, 30, 0, TimeSpan.FromHours(-2));

        AuditPartitionKey.Build("t", ts).Should().Be("t|2026-04");
    }

    [Fact]
    public void Build_SameTenantDifferentMonths_ProducesDifferentPartitions()
    {
        var jan = AuditPartitionKey.Build("t", new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero));
        var feb = AuditPartitionKey.Build("t", new DateTimeOffset(2026, 2, 5, 0, 0, 0, TimeSpan.Zero));

        jan.Should().NotBe(feb, "monthly bucketing spreads a tenant's writes across logical partitions over time");
    }

    // =========================================================================
    // Tenant-id normalization (mirrors MemoryItemStore.NormalizeSubjectId — one convention)
    // =========================================================================

    [Fact]
    public void Build_NormalizesGuidTenant_StripsBracesAndLowercases()
    {
        var ts = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var upperBraced = "{2F1A9C3E-1111-2222-3333-444455556666}";
        var lowerPlain = "2f1a9c3e-1111-2222-3333-444455556666";

        // Same tenant expressed two ways must land in the SAME logical partition.
        AuditPartitionKey.Build(upperBraced, ts)
            .Should().Be(AuditPartitionKey.Build(lowerPlain, ts))
            .And.Be($"{lowerPlain}|2026-05");
    }

    [Fact]
    public void Build_NonGuidTenant_TrimsButPreservesCasing()
    {
        var ts = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        AuditPartitionKey.Build("  Contoso-Prod  ", ts).Should().Be("Contoso-Prod|2026-05");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_NullOrWhitespaceTenant_Throws(string? tenantId)
    {
        var ts = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        var act = () => AuditPartitionKey.Build(tenantId!, ts);

        act.Should().Throw<ArgumentException>();
    }

    // =========================================================================
    // AuditEntry.PartitionKey wires the derivation (doc property == write partition)
    // =========================================================================

    [Fact]
    public void AuditEntryPartitionKey_MatchesBuildOverTenantAndTimestamp()
    {
        var entry = new AuditEntry
        {
            TenantId = "tenant-abc",
            UserId = "u",
            SessionId = "s",
            Action = "chat_response",
            ResponseHash = "hash",
            Timestamp = new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero)
        };

        entry.PartitionKey.Should().Be(AuditPartitionKey.Build(entry.TenantId, entry.Timestamp));
        entry.PartitionKey.Should().Be("tenant-abc|2026-11");
    }
}
