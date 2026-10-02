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
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;
using Sprk.Bff.Api.Services.Identity;

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
    // Task 152 verifier round 1 item 5: the portfolio's matters come from the people-targeting surface and are read AS
    // THE CALLER — there is no app-only Dataverse client on PortfolioService any more.
    private readonly Mock<IMembershipResolverService> _resolverMock = new(MockBehavior.Strict);
    private readonly RecordingCallerQuery _callerQuery = new();
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

        var matterId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        SetupPeopleMatters(matterId);
        _callerQuery.Matters.Add(MatterRow(matterId, "Test Matter", spend: 500m, budget: 1000m));

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
        _resolverMock.VerifyAll();
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

        // Pre-baked portfolio response (cache hit avoids touching the resolver and the caller-context reads).
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
    // Which matters, read how (unified-access-control-r2 task 152, verifier round 1 item 5; ADR-034 A3)
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Guid CallerSystemUserId = StubSystemUserIdentityResolver.SystemUserId;
    private static readonly Guid MatterA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid MatterB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    [Fact]
    public async Task GetPortfolioSummaryAsync_MattersComeFromThePeopleSurface_ReadAsTheCaller_WithRealColumns()
    {
        SetupCacheMiss();
        SetupPeopleMatters(MatterA, MatterB);
        _callerQuery.Matters.Add(MatterRow(MatterA, "Alpha", spend: 90m, budget: 100m)); // 90% → at risk
        _callerQuery.Matters.Add(MatterRow(MatterB, "Bravo", spend: 10m, budget: 100m));
        _callerQuery.OverdueTasks.Add(OverdueTask(MatterB));
        _callerQuery.OverdueTasks.Add(OverdueTask(MatterB));

        var result = await CreateSut().GetPortfolioSummaryAsync(TestUserId, CancellationToken.None);

        result.ActiveMatters.Should().Be(2);
        result.TotalSpend.Should().Be(100m);
        result.TotalBudget.Should().Be(200m);
        result.OverdueEvents.Should().Be(2, "overdue open tasks are COUNTED from sprk_event — sprk_matter stores no count");
        result.MattersAtRisk.Should().Be(2, "Alpha is over 85% utilized; Bravo has overdue tasks");

        _resolverMock.Verify(r => r.ResolveAsync(
                CallerSystemUserId, "sprk_matter",
                It.Is<MembershipResolveOptions?>(o => o != null && o.PeopleTargeting && !o.AccessConferringOnly
                    && o.Limit == MembershipResolveOptions.MaxLimit),
                It.IsAny<CancellationToken>()),
            Times.Once, "the portfolio is the matters FOR the user, read to completion — never an ownerid filter");
        _callerQuery.Calls.Should().NotBeEmpty().And.OnlyContain(c => c.Caller == CallerSystemUserId,
            "every row is read AS THE CALLER (MSCRMCallerID)");

        var matterQuery = _callerQuery.Calls.First(c => c.EntitySet == "sprk_matters").Query;
        matterQuery.Should().Contain("sprk_mattername").And.Contain("sprk_totalspendtodate").And.Contain("sprk_totalbudget");
        foreach (var absent in new[] { "sprk_name,", "sprk_totalspend,", "sprk_overdueeventcount", "ownerid", "owninguser" })
        {
            matterQuery.Should().NotContain(absent, $"'{absent.TrimEnd(',')}' does not exist on sprk_matter / is an ad-hoc owner condition");
        }
    }

    [Fact]
    public async Task ReadMattersForSystemUserAsync_MatterTheCallerCannotRead_IsAbsent()
    {
        SetupPeopleMatters(MatterA, MatterB);
        // Dataverse trims MatterB from the impersonated read: it names the caller, but the caller cannot open it.
        _callerQuery.Matters.Add(MatterRow(MatterA, "Alpha", 0m, 0m));

        var read = await CreateSut().ReadMattersForSystemUserAsync(CallerSystemUserId, CancellationToken.None);

        read.Unavailable.Should().BeFalse();
        read.Matters.Select(m => m.Id).Should().Equal(MatterA);
    }

    [Fact]
    public async Task ReadMattersForSystemUserAsync_NoMattersForTheUser_IsEmpty_NotUnavailable_AndReadsNothing()
    {
        SetupPeopleMatters();

        var read = await CreateSut().ReadMattersForSystemUserAsync(CallerSystemUserId, CancellationToken.None);

        read.Unavailable.Should().BeFalse("no matters for you is not a failure");
        read.Matters.Should().BeEmpty();
        _callerQuery.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadMattersForSystemUserAsync_CallerReadFails_Unavailable_NeverAShrunkList()
    {
        var ids = Enumerable.Range(1, PortfolioService.MaxIdsPerImpersonatedRequest + 5)
            .Select(i => Guid.Parse($"c0000000-0000-0000-0000-{i:D12}")).ToArray();
        SetupPeopleMatters(ids);
        foreach (var id in ids)
        {
            _callerQuery.Matters.Add(MatterRow(id, $"Matter {id}", 0m, 0m));
        }
        _callerQuery.FailOnMatterCallNumber = 2; // the SECOND chunk fails; the first chunk's rows must not be served

        var read = await CreateSut().ReadMattersForSystemUserAsync(CallerSystemUserId, CancellationToken.None);

        read.Unavailable.Should().BeTrue();
        read.Matters.Should().BeEmpty();
        _callerQuery.Calls.Count(c => c.EntitySet == "sprk_matters").Should().Be(2, "ids are chunked at 50 per caller-context read");
    }

    [Fact]
    public async Task ReadMattersForSystemUserAsync_PeopleSetLargerThanTheResolverCeiling_Unavailable()
    {
        const string NextPage = "page-2";
        _resolverMock
            .Setup(r => r.ResolveAsync(CallerSystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string _, MembershipResolveOptions? o, CancellationToken _) => o?.ContinuationToken == NextPage
                ? Response(new[] { MatterB })               // more rows exist past the first full page
                : Response(new[] { MatterA }, NextPage));

        var read = await CreateSut().ReadMattersForSystemUserAsync(CallerSystemUserId, CancellationToken.None);

        read.Unavailable.Should().BeTrue("an arbitrary subset of the user's matters is never aggregated");
        _callerQuery.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadMattersForSystemUserAsync_ResolverThrows_Unavailable()
    {
        _resolverMock
            .Setup(r => r.ResolveAsync(CallerSystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("resolver down"));

        var read = await CreateSut().ReadMattersForSystemUserAsync(CallerSystemUserId, CancellationToken.None);

        read.Unavailable.Should().BeTrue();
    }

    [Fact]
    public async Task GetPortfolioSummaryAsync_ReadFails_KeepsThePreExistingEmptyPortfolio()
    {
        SetupCacheMiss();
        SetupPeopleMatters(MatterA);
        _callerQuery.FailOnMatterCallNumber = 1;

        var result = await CreateSut().GetPortfolioSummaryAsync(TestUserId, CancellationToken.None);

        result.ActiveMatters.Should().Be(0, "the endpoint's graceful empty state on failure is unchanged");
        _callerQuery.Calls.Should().OnlyContain(c => c.Caller == CallerSystemUserId, "and nothing is read app-only");
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
        _resolverMock.Object,
        _callerQuery,
        StubSystemUserIdentityResolver.Instance,
        NullLogger<PortfolioService>.Instance,
        _timeProvider);

    private void SetupCacheMiss()
    {
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _cacheMock
            .Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupPeopleMatters(params Guid[] ids)
    {
        _resolverMock
            .Setup(r => r.ResolveAsync(CallerSystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ids));
    }

    private static MembershipResponse Response(Guid[] ids, string? continuationToken = null) => new(
        EntityType: "sprk_matter",
        PersonIdentity: new PersonIdentity(CallerSystemUserId),
        Ids: ids,
        ByRole: new Dictionary<string, IReadOnlyList<Guid>>(),
        Count: ids.Length,
        CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5),
        ContinuationToken: continuationToken);

    private static Dictionary<string, JsonElement> MatterRow(Guid id, string name, decimal spend, decimal budget) =>
        Row(new Dictionary<string, object?>
        {
            ["sprk_matterid"] = id.ToString("D"),
            ["sprk_mattername"] = name,
            ["sprk_totalspendtodate"] = spend,
            ["sprk_totalbudget"] = budget,
        });

    private static Dictionary<string, JsonElement> OverdueTask(Guid matterId) =>
        Row(new Dictionary<string, object?>
        {
            ["sprk_eventid"] = Guid.NewGuid().ToString("D"),
            ["_sprk_regardingmatter_value"] = matterId.ToString("D"),
        });

    private static Dictionary<string, JsonElement> Row(Dictionary<string, object?> values) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(values))!;

    /// <summary>
    /// Dataverse-under-impersonation stand-in: answers a matter or overdue-task read with the rows whose id the query
    /// names (so chunking and trimming are observable), and records every call.
    /// </summary>
    private sealed class RecordingCallerQuery : IImpersonatedCommunicationQuery
    {
        public List<Dictionary<string, JsonElement>> Matters { get; } = new();
        public List<Dictionary<string, JsonElement>> OverdueTasks { get; } = new();
        public List<(string EntitySet, string Query, Guid Caller)> Calls { get; } = new();
        public int FailOnMatterCallNumber { get; set; } = -1;

        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
            string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct)
        {
            var query = odataQuery ?? string.Empty;
            Calls.Add((entitySetName, query, callerSystemUserId));
            if (entitySetName == "sprk_matters" && Calls.Count(c => c.EntitySet == "sprk_matters") == FailOnMatterCallNumber)
            {
                throw new HttpRequestException("Dataverse refused the impersonated read");
            }

            var (source, key) = entitySetName == "sprk_matters"
                ? (Matters, "sprk_matterid")
                : (OverdueTasks, "_sprk_regardingmatter_value");
            IReadOnlyList<Dictionary<string, JsonElement>> rows = source
                .Where(r => query.Contains(r[key].GetString()!, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult(rows);
        }
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
