using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Insights;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights;

/// <summary>
/// Unit tests for <see cref="InsightsPlaybookCacheKey"/> covering the key-stability
/// invariants on which the D-P13 cache contract depends.
/// </summary>
public class InsightsPlaybookCacheKeyTests
{
    private static readonly Guid SamplePlaybookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string SampleSubject = "matter:M-1234";
    private const string SampleScopeHash = "scopehash-abc";
    private const string SampleScopeHashOther = "scopehash-def";

    [Fact]
    public void Compose_SameInputs_ProducesSameKey()
    {
        // Arrange
        var p = new Dictionary<string, string> { ["jurisdiction"] = "CA", ["year"] = "2025" };

        // Act
        var k1 = InsightsPlaybookCacheKey.Compose(SamplePlaybookId, SampleSubject, p, SampleScopeHash);
        var k2 = InsightsPlaybookCacheKey.Compose(SamplePlaybookId, SampleSubject, p, SampleScopeHash);

        // Assert
        k1.Should().Be(k2);
        k1.Should().StartWith(InsightsPlaybookCacheKey.Prefix);
    }

    [Fact]
    public void Compose_DifferentParameterInsertionOrder_ProducesSameKey()
    {
        // The cache key MUST be stable under parameter dictionary insertion order —
        // otherwise two semantically-identical calls would miss the cache.
        var p1 = new Dictionary<string, string> { ["jurisdiction"] = "CA", ["year"] = "2025" };
        var p2 = new Dictionary<string, string> { ["year"] = "2025", ["jurisdiction"] = "CA" };

        var k1 = InsightsPlaybookCacheKey.Compose(SamplePlaybookId, SampleSubject, p1, SampleScopeHash);
        var k2 = InsightsPlaybookCacheKey.Compose(SamplePlaybookId, SampleSubject, p2, SampleScopeHash);

        k1.Should().Be(k2);
    }

    [Fact]
    public void Compose_NullVsEmptyParameters_ProducesSameKey()
    {
        var k1 = InsightsPlaybookCacheKey.Compose(SamplePlaybookId, SampleSubject, null, SampleScopeHash);
        var k2 = InsightsPlaybookCacheKey.Compose(
            SamplePlaybookId, SampleSubject, new Dictionary<string, string>(), SampleScopeHash);

        k1.Should().Be(k2);
    }

    [Fact]
    public void Compose_DifferentPlaybookId_ProducesDifferentKey()
    {
        var other = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var k1 = InsightsPlaybookCacheKey.Compose(SamplePlaybookId, SampleSubject, null, SampleScopeHash);
        var k2 = InsightsPlaybookCacheKey.Compose(other, SampleSubject, null, SampleScopeHash);

        k1.Should().NotBe(k2);
    }

    [Fact]
    public void Compose_DifferentSubject_ProducesDifferentKey()
    {
        var k1 = InsightsPlaybookCacheKey.Compose(SamplePlaybookId, "matter:M-1234", null, SampleScopeHash);
        var k2 = InsightsPlaybookCacheKey.Compose(SamplePlaybookId, "matter:M-5678", null, SampleScopeHash);

        k1.Should().NotBe(k2);
    }

    [Fact]
    public void Compose_DifferentParameterValue_ProducesDifferentKey()
    {
        var p1 = new Dictionary<string, string> { ["year"] = "2025" };
        var p2 = new Dictionary<string, string> { ["year"] = "2026" };

        var k1 = InsightsPlaybookCacheKey.Compose(SamplePlaybookId, SampleSubject, p1, SampleScopeHash);
        var k2 = InsightsPlaybookCacheKey.Compose(SamplePlaybookId, SampleSubject, p2, SampleScopeHash);

        k1.Should().NotBe(k2);
    }

    [Fact]
    public void Compose_DifferentAccessibleScopeHash_ProducesDifferentKey()
    {
        // DEP-3 anchor test: within-tenant access changes (different scope hash)
        // MUST partition the cache so two users with different access surfaces never
        // share a cached InsightArtifact.
        var k1 = InsightsPlaybookCacheKey.Compose(SamplePlaybookId, SampleSubject, null, SampleScopeHash);
        var k2 = InsightsPlaybookCacheKey.Compose(SamplePlaybookId, SampleSubject, null, SampleScopeHashOther);

        k1.Should().NotBe(k2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Compose_NullOrWhitespaceSubject_Throws(string? subject)
    {
        var act = () => InsightsPlaybookCacheKey.Compose(SamplePlaybookId, subject!, null, SampleScopeHash);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Compose_NullOrWhitespaceAccessibleScopeHash_Throws(string? hash)
    {
        var act = () => InsightsPlaybookCacheKey.Compose(SamplePlaybookId, SampleSubject, null, hash!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Compose_KeyStartsWithInsightsPlaybookPrefix()
    {
        var key = InsightsPlaybookCacheKey.Compose(SamplePlaybookId, SampleSubject, null, SampleScopeHash);
        key.Should().StartWith("insights:playbook:");
    }
}
