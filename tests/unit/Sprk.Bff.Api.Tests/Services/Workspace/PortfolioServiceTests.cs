using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Workspace.Contracts;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Workspace;

/// <summary>
/// Phase 4 Track C — TestClock + seeded-Guid PoC against <see cref="PortfolioService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose</b>: demonstrate the determinism pattern introduced by Task 042 (FR-13) for the
/// <c>Services/Workspace/*</c> surface. <see cref="PortfolioService"/> was chosen as the PoC
/// target because it (a) had two direct <see cref="DateTimeOffset.UtcNow"/> call sites with
/// observable outputs (<c>CachedAt</c> + <c>Timestamp</c> response fields), (b) had no
/// pre-existing test class to disturb, and (c) consumes <see cref="System.TimeProvider"/>
/// through an optional constructor parameter so DI default behavior is preserved
/// (per <c>projects/sdap.bff.api-test-suite-repair-r2/design.md §5.5 Track C</c>).
/// </para>
/// <para>
/// <b>Pattern shown here</b>:
/// <list type="number">
///   <item><description>A hand-rolled <see cref="FixedTimeProvider"/> subclass (BCL approach)
///     stamps the returned record at a known UTC instant — same shape as
///     <c>PrecedentProjectionSyncTests.FixedTimeProvider</c> already in the codebase, so no
///     new NuGet package is required.</description></item>
///   <item><description>A <see cref="FakeGuidProvider"/> returning a seeded sequence demonstrates
///     the second seam (currently unused by <see cref="PortfolioService"/> — the abstraction
///     itself is the deliverable per FR-13, with consumer migration following in r3).</description></item>
///   <item><description>Strict Mock + <see cref="EntityCollection"/> fixtures avoid any direct
///     I/O so the test stays under the 100 ms per-test budget from
///     <c>.claude/constraints/testing.md</c>.</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Reference</b>: <c>tests/unit/Sprk.Bff.Api.Tests/Services/Insights/Precedents/PrecedentProjectionSyncTests.cs</c>
/// shows the same <see cref="System.TimeProvider"/>-subclass approach in a different domain. We
/// reuse the shape here intentionally so the pattern is uniform across the test suite — Phase 5
/// task 080 will codify it in <c>docs/procedures/testing-and-code-quality.md</c>.
/// </para>
/// </remarks>
public class PortfolioServiceTests
{
    // ── Deterministic seeds ──────────────────────────────────────────────────
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Guid SeededId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SeededId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SeededId3 = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const string TestUserId = "44444444-4444-4444-4444-444444444444";

    // ── Strict mocks (boundary-only, per testing.md MUST rules) ──────────────
    private readonly Mock<IDistributedCache> _cacheMock = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _entityServiceMock = new(MockBehavior.Strict);
    private readonly FixedTimeProvider _timeProvider = new(FixedNow);
    private readonly FakeGuidProvider _guidProvider = new(SeededId1, SeededId2, SeededId3);

    // ─────────────────────────────────────────────────────────────────────────
    // GetPortfolioSummaryAsync — cache miss path stamps CachedAt deterministically
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPortfolioSummaryAsync_StampsCachedAtFromTimeProvider_OnCacheMiss()
    {
        // Arrange
        const string cacheKey = $"workspace:{TestUserId}:portfolio";

        _cacheMock
            .Setup(c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeOneActiveMatter());

        // The Set call uses the entry-options form — verify it's invoked once with the cache key.
        _cacheMock
            .Setup(c => c.SetAsync(
                cacheKey,
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();

        // Act
        var result = await sut.GetPortfolioSummaryAsync(TestUserId, CancellationToken.None);

        // Assert — deterministic timestamp from the injected TimeProvider
        result.Should().NotBeNull();
        result.CachedAt.Should().Be(FixedNow);
        result.ActiveMatters.Should().Be(1);
        _cacheMock.VerifyAll();
        _entityServiceMock.VerifyAll();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetHealthMetricsAsync — derives from Portfolio, stamps its own Timestamp
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHealthMetricsAsync_StampsTimestampFromTimeProvider_OnCacheMiss()
    {
        // Arrange — health cache miss + portfolio cache hit (so we hit ONLY the Timestamp site)
        const string healthKey = $"workspace:{TestUserId}:health";
        const string portfolioKey = $"workspace:{TestUserId}:portfolio";

        // Pre-baked portfolio response (cache hit avoids touching IGenericEntityService).
        var prebakedPortfolio = new PortfolioSummaryResponse(
            TotalSpend: 1000m,
            TotalBudget: 2000m,
            UtilizationPercent: 50m,
            MattersAtRisk: 0,
            OverdueEvents: 0,
            ActiveMatters: 2,
            CachedAt: FixedNow.AddMinutes(-1));

        var portfolioBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(prebakedPortfolio));

        _cacheMock
            .Setup(c => c.GetAsync(healthKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _cacheMock
            .Setup(c => c.GetAsync(portfolioKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolioBytes);
        _cacheMock
            .Setup(c => c.SetAsync(
                healthKey,
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();

        // Act
        var result = await sut.GetHealthMetricsAsync(TestUserId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Timestamp.Should().Be(FixedNow);
        result.MattersAtRisk.Should().Be(0);
        result.PortfolioBudget.Should().Be(2000m);
        _cacheMock.VerifyAll();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IGuidProvider PoC: the provider itself is greenfield — exercise its
    // seeded sequence here so the second seam has at least one regression
    // guard. Production consumer migration is r3 scope (testclock-pattern-draft.md).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FakeGuidProvider_ReturnsSeededSequence_InOrder()
    {
        // Act
        var first = _guidProvider.NewGuid();
        var second = _guidProvider.NewGuid();
        var third = _guidProvider.NewGuid();

        // Assert
        first.Should().Be(SeededId1);
        second.Should().Be(SeededId2);
        third.Should().Be(SeededId3);
    }

    [Fact]
    public void FakeGuidProvider_ThrowsWhenExhausted_SoTestsFailLoudly()
    {
        // Arrange
        var provider = new FakeGuidProvider(SeededId1);
        provider.NewGuid();

        // Act + Assert
        var act = provider.NewGuid;
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*FakeGuidProvider exhausted*");
    }

    [Fact]
    public void DefaultGuidProvider_ProducesUniqueGuids()
    {
        // Arrange
        var provider = new DefaultGuidProvider();

        // Act
        var a = provider.NewGuid();
        var b = provider.NewGuid();

        // Assert
        a.Should().NotBe(Guid.Empty);
        b.Should().NotBe(Guid.Empty);
        a.Should().NotBe(b);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private PortfolioService CreateSut() => new(
        _cacheMock.Object,
        _entityServiceMock.Object,
        NullLogger<PortfolioService>.Instance,
        _timeProvider);

    private static EntityCollection MakeOneActiveMatter()
    {
        var entity = new Entity("sprk_matter", Guid.NewGuid());
        entity["sprk_name"] = "Test Matter";
        entity["sprk_totalspend"] = new Money(500m);
        entity["sprk_totalbudget"] = new Money(1000m);
        entity["sprk_overdueeventcount"] = 0;
        entity["statecode"] = new OptionSetValue(0); // Active

        var collection = new EntityCollection(new List<Entity> { entity });
        return collection;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Determinism scaffolding — kept inline for PoC readability. Phase 5 task
    // 080 may promote these to a shared test-helper assembly when other
    // Workspace test classes adopt the pattern (r3).
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal <see cref="System.TimeProvider"/> subclass returning a fixed UTC instant.
    /// Same shape as <c>PrecedentProjectionSyncTests.FixedTimeProvider</c> — kept inline
    /// (vs. the <c>Microsoft.Extensions.TimeProvider.Testing</c> NuGet) per ADR-029 publish-
    /// hygiene + BFF-extensions §B (no new package references without justification).
    /// </summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _fixedNow;
        public FixedTimeProvider(DateTimeOffset fixedNow) => _fixedNow = fixedNow;
        public override DateTimeOffset GetUtcNow() => _fixedNow;
    }

    /// <summary>
    /// Deterministic <see cref="IGuidProvider"/> for tests — returns the seeded values in
    /// the order supplied to the constructor; throws when exhausted so missing seed values
    /// surface immediately as test failures (rather than silently degrading to
    /// <see cref="Guid.Empty"/>).
    /// </summary>
    private sealed class FakeGuidProvider : IGuidProvider
    {
        private readonly Queue<Guid> _seeded;
        public FakeGuidProvider(params Guid[] seeded) => _seeded = new Queue<Guid>(seeded);
        public Guid NewGuid()
        {
            if (_seeded.Count == 0)
            {
                throw new InvalidOperationException(
                    "FakeGuidProvider exhausted — supply more seeded values for this test.");
            }
            return _seeded.Dequeue();
        }
    }
}
