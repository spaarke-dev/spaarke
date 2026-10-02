// Wave 28 / GitHub #229 closeout (2026-06-22): Unit tests for the
// ADR-034 wiring of BriefingService.GetTopPriorityMatterAsync.
//
// unified-access-control-r2 task 152 (2026-10-02): the candidates now come from the PEOPLE-TARGETING surface
// (MembershipResolveOptions.People — the matters FOR the user, never every team/BU-owned matter), and the detail rows
// + overdue-task counts are read AS THE CALLER through IImpersonatedCommunicationQuery, so a matter the user cannot
// open is absent. A failed people resolution or caller read surfaces as TopPriorityMatterUnavailable — never an
// app-only answer.
//
// Reference: docs/architecture/membership-resolution-pattern.md "Wiring + Consumer Inventory (AS-BUILT)".

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;
using Sprk.Bff.Api.Services.Identity;

namespace Sprk.Bff.Api.Tests.Services.Workspace;

/// <summary>
/// Unit tests for <see cref="BriefingService"/>'s top-priority matter: people-targeted candidates, caller-context
/// reads, the deterministic heuristic, and the failure semantics.
/// </summary>
public class BriefingServiceTests
{
    // ── Stable test fixtures ─────────────────────────────────────────────────
    private static readonly Guid TestAadObjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly string TestAadOidString = TestAadObjectId.ToString("D");
    private static readonly Guid TestSystemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MatterIdHighOverdue = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MatterIdLowOverdue = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid MatterIdMidOverdue = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly Mock<IDataverseService> _dataverseMock = new(MockBehavior.Strict);
    private readonly Mock<IMembershipResolverService> _resolverMock = new(MockBehavior.Strict);
    private readonly Mock<IImpersonatedCommunicationQuery> _callerQueryMock = new(MockBehavior.Strict);
    private readonly List<(string EntitySet, string Query, Guid Caller)> _callerReads = new();
    private readonly IDistributedCache _cache = new MemoryDistributedCache(
        Options.Create(new MemoryDistributedCacheOptions()));
    private readonly Mock<IDistributedCache> _portfolioCacheMock = new(MockBehavior.Loose);

    // -------------------------------------------------------------------------
    // Happy path — people-targeted candidates, read as the caller, heuristic applied
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBriefing_HappyPath_SelectsMatterWithMostOverdueTasks_ReadAsTheCaller()
    {
        SetupAadOidLookup(returnUserId: TestSystemUserId);
        SetupPeopleMatters(MatterIdHighOverdue, MatterIdLowOverdue, MatterIdMidOverdue);
        SetupCallerReads(
            matters: new[]
            {
                MatterRow(MatterIdHighOverdue, "Matter Alpha", spend: 80_000m, budget: 100_000m),
                MatterRow(MatterIdLowOverdue, "Matter Bravo", spend: 30_000m, budget: 50_000m),
                MatterRow(MatterIdMidOverdue, "Matter Charlie", spend: 95_000m, budget: 100_000m),
            },
            overdueTasks: Overdue(MatterIdHighOverdue, 5).Concat(Overdue(MatterIdMidOverdue, 2)).ToArray());

        var result = await CreateSut().GetBriefingAsync(TestAadOidString, CancellationToken.None);

        result.TopPriorityMatter.Should().NotBeNull();
        result.TopPriorityMatter!.MatterId.Should().Be(MatterIdHighOverdue);
        result.TopPriorityMatter.Name.Should().Be("Matter Alpha");
        result.TopPriorityMatter.Reason.Should().Contain("5", "the reason cites the overdue task count");
        result.TopPriorityMatter.Deadline.Should().BeNull("sprk_matter has no deadline column");
        result.TopPriorityMatterUnavailable.Should().BeFalse();

        // Every detail read runs as the caller; the app-only client is used for the oid lookup only.
        _callerReads.Should().NotBeEmpty();
        _callerReads.Should().OnlyContain(r => r.Caller == TestSystemUserId);
        _dataverseMock.Verify(
            d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName != "systemuser"), It.IsAny<CancellationToken>()),
            Times.Never,
            "no matter or event row is ever read app-only");
    }

    [Fact]
    public async Task GetBriefing_ResolvesCandidatesThroughThePeopleTargetingSurface()
    {
        SetupAadOidLookup(returnUserId: TestSystemUserId);
        SetupPeopleMatters(MatterIdHighOverdue);
        SetupCallerReads(new[] { MatterRow(MatterIdHighOverdue, "Matter Alpha", 0m, 0m) }, Array.Empty<Dictionary<string, JsonElement>>());

        await CreateSut().GetBriefingAsync(TestAadOidString, CancellationToken.None);

        _resolverMock.Verify(r => r.ResolveAsync(
            TestSystemUserId, "sprk_matter",
            It.Is<MembershipResolveOptions?>(o => o != null && o.PeopleTargeting && !o.AccessConferringOnly),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetBriefing_TieOnOverdue_SelectsHighestUtilization()
    {
        SetupAadOidLookup(returnUserId: TestSystemUserId);
        SetupPeopleMatters(MatterIdHighOverdue, MatterIdMidOverdue);
        SetupCallerReads(
            matters: new[]
            {
                MatterRow(MatterIdHighOverdue, "Matter Alpha", spend: 30_000m, budget: 100_000m),   // 30%
                MatterRow(MatterIdMidOverdue, "Matter Charlie", spend: 95_000m, budget: 100_000m), // 95%
            },
            overdueTasks: Overdue(MatterIdHighOverdue, 2).Concat(Overdue(MatterIdMidOverdue, 2)).ToArray());

        var result = await CreateSut().GetBriefingAsync(TestAadOidString, CancellationToken.None);

        result.TopPriorityMatter!.MatterId.Should().Be(MatterIdMidOverdue);
    }

    // -------------------------------------------------------------------------
    // Readability (criterion 10): a candidate Dataverse denies the caller is absent
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBriefing_CandidateTheCallerCannotRead_IsNeverTheTopMatter()
    {
        SetupAadOidLookup(returnUserId: TestSystemUserId);
        SetupPeopleMatters(MatterIdHighOverdue, MatterIdLowOverdue);
        // Dataverse trims MatterIdHighOverdue from the impersonated read (the caller cannot open it), even though
        // its overdue tasks would have made it the winner.
        SetupCallerReads(
            matters: new[] { MatterRow(MatterIdLowOverdue, "Matter Bravo", 0m, 0m) },
            overdueTasks: Overdue(MatterIdHighOverdue, 9).ToArray());

        var result = await CreateSut().GetBriefingAsync(TestAadOidString, CancellationToken.None);

        result.TopPriorityMatter!.MatterId.Should().Be(MatterIdLowOverdue);
    }

    [Fact]
    public async Task GetBriefing_LargeCandidateSet_IsChunked_EveryChunkReadAsTheCaller()
    {
        var ids = Enumerable.Range(1, PortfolioService.MaxIdsPerImpersonatedRequest + 10)
            .Select(i => Guid.Parse($"55555555-5555-5555-5555-{i:D12}"))
            .ToArray();
        SetupAadOidLookup(returnUserId: TestSystemUserId);
        SetupPeopleMatters(ids);
        SetupCallerReads(new[] { MatterRow(ids[^1], "Last Matter", 0m, 0m) }, Array.Empty<Dictionary<string, JsonElement>>());

        var result = await CreateSut().GetBriefingAsync(TestAadOidString, CancellationToken.None);

        _callerReads.Count(r => r.EntitySet == "sprk_matters").Should().Be(2);
        _callerReads.Count(r => r.EntitySet == "sprk_events").Should().Be(2);
        _callerReads.Where(r => r.EntitySet == "sprk_matters")
            .Should().OnlyContain(r => CountOf(r.Query, "sprk_matterid eq") <= PortfolioService.MaxIdsPerImpersonatedRequest);
        result.TopPriorityMatter!.MatterId.Should().Be(ids[^1]);
    }

    // -------------------------------------------------------------------------
    // Empty / degrade paths
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBriefing_NoMattersForTheUser_ReturnsNullTopMatter_NotUnavailable()
    {
        SetupAadOidLookup(returnUserId: TestSystemUserId);
        SetupPeopleMatters();

        var result = await CreateSut().GetBriefingAsync(TestAadOidString, CancellationToken.None);

        result.TopPriorityMatter.Should().BeNull();
        result.TopPriorityMatterUnavailable.Should().BeFalse("no matters for you is not a failure");
        _callerReads.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBriefing_NonGuidUserId_ReturnsNullTopMatterWithoutCrashing()
    {
        const string nonGuidUserId = "test-user-00000000-0000-0000-0000-000000000001";

        var result = await CreateSut().GetBriefingAsync(nonGuidUserId, CancellationToken.None);

        result.TopPriorityMatter.Should().BeNull();
        _resolverMock.Verify(
            r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetBriefing_AadOidNotProvisioned_ReturnsNullTopMatter()
    {
        SetupAadOidLookup(returnUserId: null);

        var result = await CreateSut().GetBriefingAsync(TestAadOidString, CancellationToken.None);

        result.TopPriorityMatter.Should().BeNull();
        _resolverMock.Verify(
            r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -------------------------------------------------------------------------
    // Fail closed (criterion 11): failures are UNAVAILABLE, never answered app-only
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBriefing_ResolverThrows_TopMatterUnavailable_FullBriefingStillServed()
    {
        SetupAadOidLookup(returnUserId: TestSystemUserId);
        _resolverMock
            .Setup(r => r.ResolveAsync(TestSystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated resolver failure"));

        var result = await CreateSut().GetBriefingAsync(TestAadOidString, CancellationToken.None);

        result.TopPriorityMatter.Should().BeNull();
        result.TopPriorityMatterUnavailable.Should().BeTrue();
        result.Narrative.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetBriefing_CallerContextReadFails_TopMatterUnavailable_NoAppOnlyFallback()
    {
        SetupAadOidLookup(returnUserId: TestSystemUserId);
        SetupPeopleMatters(MatterIdHighOverdue);
        _callerQueryMock
            .Setup(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("403 — prvActOnBehalfOfAnotherUser missing"));

        var result = await CreateSut().GetBriefingAsync(TestAadOidString, CancellationToken.None);

        result.TopPriorityMatter.Should().BeNull();
        result.TopPriorityMatterUnavailable.Should().BeTrue("a failed caller read is 'could not be determined', not 'no matters'");
        _dataverseMock.Verify(
            d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_matter"), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetBriefing_PeopleSetLargerThanTheResolverCeiling_TopMatterUnavailable_NeverAnArbitrarySubset()
    {
        // Verifier round 1 item 4: the candidate set is read to completion; a first FULL page (continuation token)
        // whose follow-up still names new matters is "could not be determined", never the GUID-ordered first page.
        SetupAadOidLookup(returnUserId: TestSystemUserId);
        _resolverMock
            .Setup(r => r.ResolveAsync(TestSystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string _, MembershipResolveOptions? o, CancellationToken _) =>
            {
                var ids = o?.ContinuationToken == "page-2" ? new[] { MatterIdLowOverdue } : new[] { MatterIdHighOverdue };
                return new MembershipResponse(
                    EntityType: "sprk_matter",
                    PersonIdentity: new PersonIdentity(TestSystemUserId),
                    Ids: ids,
                    ByRole: new Dictionary<string, IReadOnlyList<Guid>>(),
                    Count: ids.Length,
                    CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5),
                    ContinuationToken: o?.ContinuationToken == "page-2" ? null : "page-2");
            });

        var result = await CreateSut().GetBriefingAsync(TestAadOidString, CancellationToken.None);

        result.TopPriorityMatter.Should().BeNull();
        result.TopPriorityMatterUnavailable.Should().BeTrue();
        _callerReads.Should().BeEmpty("an incomplete candidate set is never read or ranked");
    }

    [Fact]
    public async Task GetBriefing_CancellationDuringResolver_PropagatesCancellation()
    {
        SetupAadOidLookup(returnUserId: TestSystemUserId);

        using var cts = new CancellationTokenSource();
        _resolverMock
            .Setup(r => r.ResolveAsync(TestSystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, MembershipResolveOptions?, CancellationToken>((_, _, _, _) => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        await CreateSut().Invoking(s => s.GetBriefingAsync(TestAadOidString, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    // =========================================================================
    // ── Helpers ──────────────────────────────────────────────────────────────
    // =========================================================================

    private BriefingService CreateSut()
    {
        // The portfolio METRICS are a cache hit here, so these tests observe only the top-priority-matter read. That
        // read is PortfolioService.ReadMattersForSystemUserAsync (task 152 verifier round 1 item 5: one shared,
        // people-targeted, caller-context read for the metrics and the top matter), driven by the resolver and
        // caller-query fakes below. PortfolioServiceTests pins the metrics side.
        _portfolioCacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new PortfolioSummaryResponse(
                TotalSpend: 0m, TotalBudget: 0m, UtilizationPercent: 0m, MattersAtRisk: 0, OverdueEvents: 0,
                ActiveMatters: 0, CachedAt: DateTimeOffset.UtcNow))));

        var portfolio = new PortfolioService(
            _portfolioCacheMock.Object,
            _resolverMock.Object,
            _callerQueryMock.Object,
            StubSystemUserIdentityResolver.Instance,
            NullLogger<PortfolioService>.Instance);

        return new BriefingService(
            portfolioService: portfolio,
            cache: _cache,
            dataverse: _dataverseMock.Object,
            logger: NullLogger<BriefingService>.Instance,
            briefingAi: null);
    }

    private void SetupAadOidLookup(Guid? returnUserId)
    {
        var systemUserCollection = returnUserId.HasValue
            ? new EntityCollection(new List<Entity> { new Entity("systemuser", returnUserId.Value) })
            : new EntityCollection();

        _dataverseMock
            .Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemUserCollection);
    }

    private void SetupPeopleMatters(params Guid[] ids)
    {
        _resolverMock
            .Setup(r => r.ResolveAsync(TestSystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MembershipResponse(
                EntityType: "sprk_matter",
                PersonIdentity: new PersonIdentity(TestSystemUserId),
                Ids: ids,
                ByRole: new Dictionary<string, IReadOnlyList<Guid>>(),
                Count: ids.Length,
                CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5)));
    }

    /// <summary>
    /// The caller-context read fake: returns only the rows whose id the query actually names, so chunking and
    /// trimming are both observable.
    /// </summary>
    private void SetupCallerReads(
        IReadOnlyList<Dictionary<string, JsonElement>> matters,
        IReadOnlyList<Dictionary<string, JsonElement>> overdueTasks)
    {
        _callerQueryMock
            .Setup(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string set, string? query, Guid caller, CancellationToken _) =>
            {
                _callerReads.Add((set, query ?? string.Empty, caller));
                IReadOnlyList<Dictionary<string, JsonElement>> source = set == "sprk_matters" ? matters : overdueTasks;
                var key = set == "sprk_matters" ? "sprk_matterid" : "_sprk_regardingmatter_value";
                return source.Where(r => (query ?? string.Empty).Contains(r[key].GetString()!, StringComparison.OrdinalIgnoreCase)).ToList();
            });
    }

    private static Dictionary<string, JsonElement> MatterRow(Guid id, string name, decimal spend, decimal budget) =>
        Row(new Dictionary<string, object?>
        {
            ["sprk_matterid"] = id.ToString("D"),
            ["sprk_mattername"] = name,
            ["sprk_totalspendtodate"] = spend,
            ["sprk_totalbudget"] = budget,
        });

    private static IEnumerable<Dictionary<string, JsonElement>> Overdue(Guid matterId, int count) =>
        Enumerable.Range(0, count).Select(_ => Row(new Dictionary<string, object?>
        {
            ["sprk_eventid"] = Guid.NewGuid().ToString("D"),
            ["_sprk_regardingmatter_value"] = matterId.ToString("D"),
        }));

    private static Dictionary<string, JsonElement> Row(Dictionary<string, object?> values) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(values))!;

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + 1, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
