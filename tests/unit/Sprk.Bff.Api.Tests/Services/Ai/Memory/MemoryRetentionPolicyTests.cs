using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Memory;

/// <summary>
/// Tests for <see cref="MemoryRetentionPolicy"/> (task AIR2-052, FR-B-03). Pure mapping — no mocks,
/// no I/O. Pins the retention-class → Cosmos ttl(seconds) mapping and its DOCUMENTED DEFAULT
/// (absent/unknown ⇒ no expiry). Cosmos performs the expiry; this map is the ONLY retention machinery.
/// </summary>
public class MemoryRetentionPolicyTests
{
    [Fact]
    public void ResolveTtlSeconds_Tier3UserOwned_ReturnsNullNoExpiry()
    {
        // User-owned Tier-3 memory persists until user/GDPR erasure — never auto-expires.
        MemoryRetentionPolicy.ResolveTtlSeconds(MemoryRetentionPolicy.Tier3UserOwned).Should().BeNull();
    }

    [Fact]
    public void ResolveTtlSeconds_Ephemeral_Returns30DaysInSeconds()
    {
        MemoryRetentionPolicy.ResolveTtlSeconds(MemoryRetentionPolicy.Ephemeral).Should().Be(30 * 24 * 60 * 60);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("some-future-unknown-class")]
    public void ResolveTtlSeconds_AbsentOrUnknownClass_ReturnsNullDocumentedDefault(string? retentionClass)
    {
        // Documented default: absent/empty/unrecognized ⇒ no TTL (never silently drop a Tier-3 fact).
        MemoryRetentionPolicy.ResolveTtlSeconds(retentionClass).Should().BeNull();
    }
}
