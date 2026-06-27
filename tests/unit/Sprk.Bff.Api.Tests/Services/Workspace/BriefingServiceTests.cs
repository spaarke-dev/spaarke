// Wave 28 / GitHub #229 closeout (2026-06-22): Unit tests for the
// ADR-034 wiring of BriefingService.GetTopPriorityMatterAsync.
//
// Prior to this change the method returned hardcoded mock data (STUB). The
// replacement routes through IMembershipResolverService (canonical user-record
// membership per ADR-034) and IDataverseService (AAD-oid → systemuserid
// cross-reference per ADR-028 + matter detail retrieval).
//
// Reference: docs/architecture/membership-resolution-pattern.md "Wiring +
// Consumer Inventory (AS-BUILT)" — Daily Briefing has moved from GAPS to
// Confirmed Consumers.

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
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Workspace;

/// <summary>
/// Unit tests for <see cref="BriefingService"/> covering the membership-driven
/// top-priority-matter resolution introduced as the closeout for GitHub #229
/// (Wave 28). Validates the happy path, empty memberships, non-Guid oid degrade
/// case, AAD-oid cross-reference miss, resolver-failure failure-soft path, and
/// cancellation propagation.
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
    private readonly IDistributedCache _cache = new MemoryDistributedCache(
        Options.Create(new MemoryDistributedCacheOptions()));
    private readonly Mock<IDistributedCache> _portfolioCacheMock = new(MockBehavior.Loose);
    private readonly Mock<IGenericEntityService> _portfolioEntityServiceMock = new(MockBehavior.Loose);

    // -------------------------------------------------------------------------
    // Happy path — full membership resolution + heuristic application
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBriefing_HappyPath_SelectsMatterWithMostOverdueEvents()
    {
        // Arrange — AAD-oid lookup returns the systemuser row.
        SetupAadOidLookup(returnUserId: TestSystemUserId);

        // Resolver returns 3 matter IDs.
        var memberships = new MembershipResponse(
            EntityType: "sprk_matter",
            PersonIdentity: new PersonIdentity(TestSystemUserId, null, null, null, null, null, null),
            Ids: new[] { MatterIdHighOverdue, MatterIdLowOverdue, MatterIdMidOverdue },
            ByRole: new Dictionary<string, IReadOnlyList<Guid>>(),
            Count: 3,
            CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5));
        _resolverMock
            .Setup(r => r.ResolveAsync(
                TestSystemUserId,
                "sprk_matter",
                It.IsAny<MembershipResolveOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(memberships);

        // Matter-detail query — 3 matters with varying overdue counts.
        var matterCollection = new EntityCollection(new List<Entity>
        {
            BuildMatterEntity(MatterIdHighOverdue, "Matter Alpha", overdue: 5, spend: 80_000m, budget: 100_000m, deadline: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)),
            BuildMatterEntity(MatterIdLowOverdue, "Matter Bravo", overdue: 0, spend: 30_000m, budget: 50_000m, deadline: null),
            BuildMatterEntity(MatterIdMidOverdue, "Matter Charlie", overdue: 2, spend: 95_000m, budget: 100_000m, deadline: new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc))
        });
        _dataverseMock
            .Setup(d => d.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_matter"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(matterCollection);

        var sut = CreateSut();

        // Act
        var result = await sut.GetBriefingAsync(TestAadOidString, CancellationToken.None);

        // Assert — top matter is the one with highest overdue count (Matter Alpha = 5).
        result.TopPriorityMatter.Should().NotBeNull("happy path returns a top-priority matter");
        result.TopPriorityMatter!.MatterId.Should().Be(MatterIdHighOverdue);
        result.TopPriorityMatter.Name.Should().Be("Matter Alpha");
        result.TopPriorityMatter.Reason.Should().Contain("5", "reason cites the overdue event count");
        result.TopPriorityMatter.Deadline.Should().Be(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

        _resolverMock.VerifyAll();
        // No specific call-count assertion on dataverseMock — strict mock would have
        // thrown on any unexpected call already.
    }

    // -------------------------------------------------------------------------
    // Tie-break — equal overdue counts → highest utilization wins
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBriefing_TieOnOverdue_SelectsHighestUtilization()
    {
        // Arrange — both matters have overdue=2; differ only by utilization.
        SetupAadOidLookup(returnUserId: TestSystemUserId);

        var memberships = new MembershipResponse(
            EntityType: "sprk_matter",
            PersonIdentity: new PersonIdentity(TestSystemUserId, null, null, null, null, null, null),
            Ids: new[] { MatterIdHighOverdue, MatterIdMidOverdue },
            ByRole: new Dictionary<string, IReadOnlyList<Guid>>(),
            Count: 2,
            CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5));
        _resolverMock
            .Setup(r => r.ResolveAsync(
                TestSystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(memberships);

        var matterCollection = new EntityCollection(new List<Entity>
        {
            BuildMatterEntity(MatterIdHighOverdue, "Matter Alpha", overdue: 2, spend: 30_000m, budget: 100_000m, deadline: null),   // 30% util
            BuildMatterEntity(MatterIdMidOverdue,  "Matter Charlie", overdue: 2, spend: 95_000m, budget: 100_000m, deadline: null), // 95% util
        });
        _dataverseMock
            .Setup(d => d.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_matter"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(matterCollection);

        var sut = CreateSut();

        // Act
        var result = await sut.GetBriefingAsync(TestAadOidString, CancellationToken.None);

        // Assert — Matter Charlie wins on the utilization tie-break.
        result.TopPriorityMatter.Should().NotBeNull();
        result.TopPriorityMatter!.MatterId.Should().Be(MatterIdMidOverdue);
    }

    // -------------------------------------------------------------------------
    // Empty memberships — returns null TopMatterSummary gracefully
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBriefing_EmptyMemberships_ReturnsNullTopMatter()
    {
        // Arrange
        SetupAadOidLookup(returnUserId: TestSystemUserId);

        var emptyMemberships = new MembershipResponse(
            EntityType: "sprk_matter",
            PersonIdentity: new PersonIdentity(TestSystemUserId, null, null, null, null, null, null),
            Ids: Array.Empty<Guid>(),
            ByRole: new Dictionary<string, IReadOnlyList<Guid>>(),
            Count: 0,
            CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5));
        _resolverMock
            .Setup(r => r.ResolveAsync(
                TestSystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyMemberships);

        // The matter-detail query MUST NOT be invoked when memberships are empty.
        // (Strict mock will throw on any unexpected RetrieveMultipleAsync call beyond
        // the one configured by SetupAadOidLookup for the systemuser query.)

        var sut = CreateSut();

        // Act
        var result = await sut.GetBriefingAsync(TestAadOidString, CancellationToken.None);

        // Assert
        result.Should().NotBeNull("the briefing response is always populated");
        result.TopPriorityMatter.Should().BeNull("zero memberships → null top-priority matter");
        result.Narrative.Should().NotBeNullOrWhiteSpace("template narrative is always generated");
        result.IsAiEnhanced.Should().BeFalse("no AI facade in this test fixture");
    }

    // -------------------------------------------------------------------------
    // Non-Guid AAD oid — degrades to null (no crash; test-fixture compatible)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBriefing_NonGuidUserId_ReturnsNullTopMatterWithoutCrashing()
    {
        // Arrange — the strict mocks would throw if either was invoked. The Guid
        // pre-check inside GetTopPriorityMatterAsync MUST short-circuit before
        // reaching Dataverse or the resolver.
        const string nonGuidUserId = "test-user-00000000-0000-0000-0000-000000000001";

        var sut = CreateSut();

        // Act
        var result = await sut.GetBriefingAsync(nonGuidUserId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.TopPriorityMatter.Should().BeNull("non-Guid oid → null top-priority matter (failure-soft)");

        // Strict mocks would have thrown if invoked — verify they were not.
        _dataverseMock.Verify(
            d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "non-Guid oid must short-circuit before any Dataverse call");
        _resolverMock.Verify(
            r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "non-Guid oid must short-circuit before the resolver call");
    }

    // -------------------------------------------------------------------------
    // AAD-oid cross-reference miss — user not provisioned → null gracefully
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBriefing_AadOidNotProvisioned_ReturnsNullTopMatter()
    {
        // Arrange — systemuser query returns an empty collection.
        SetupAadOidLookup(returnUserId: null);

        // Resolver MUST NOT be invoked when systemuserid resolution fails.
        var sut = CreateSut();

        // Act
        var result = await sut.GetBriefingAsync(TestAadOidString, CancellationToken.None);

        // Assert
        result.TopPriorityMatter.Should().BeNull("unprovisioned user → null top-priority matter");
        _resolverMock.Verify(
            r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "missing systemuser → no resolver call");
    }

    // -------------------------------------------------------------------------
    // Resolver throws — failure-soft (return null TopMatter, full briefing still served)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBriefing_ResolverThrows_ReturnsNullTopMatterWithFullBriefing()
    {
        // Arrange
        SetupAadOidLookup(returnUserId: TestSystemUserId);

        _resolverMock
            .Setup(r => r.ResolveAsync(
                TestSystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated resolver failure"));

        var sut = CreateSut();

        // Act
        var result = await sut.GetBriefingAsync(TestAadOidString, CancellationToken.None);

        // Assert — full briefing response still served; only TopPriorityMatter is null.
        result.Should().NotBeNull();
        result.TopPriorityMatter.Should().BeNull("resolver failure must be swallowed");
        result.Narrative.Should().NotBeNullOrWhiteSpace("template narrative is always generated");
    }

    // -------------------------------------------------------------------------
    // Cancellation — OperationCanceledException must propagate (never swallowed)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBriefing_CancellationDuringResolver_PropagatesCancellation()
    {
        // Arrange
        SetupAadOidLookup(returnUserId: TestSystemUserId);

        using var cts = new CancellationTokenSource();
        _resolverMock
            .Setup(r => r.ResolveAsync(
                TestSystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, MembershipResolveOptions?, CancellationToken>((_, _, _, _) => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var sut = CreateSut();

        // Act + Assert
        await sut.Invoking(s => s.GetBriefingAsync(TestAadOidString, cts.Token))
            .Should()
            .ThrowAsync<OperationCanceledException>("cancellation MUST propagate, never be swallowed");
    }

    // =========================================================================
    // ── Helpers ──────────────────────────────────────────────────────────────
    // =========================================================================

    /// <summary>
    /// Builds the SUT with the strict IDataverseService + IMembershipResolverService
    /// mocks, a real <see cref="MemoryDistributedCache"/> for the AAD-oid cache, and
    /// a loose <see cref="PortfolioService"/> wired to return an empty matter set
    /// (the portfolio fields are exercised by other tests; this fixture focuses on
    /// the membership-driven TopMatter logic).
    /// </summary>
    private BriefingService CreateSut()
    {
        // Portfolio service with loose mocks — returns empty portfolio (0 matters).
        // The class-under-test still aggregates and serializes a response; only the
        // TopPriorityMatter slot is the focus of these tests.
        _portfolioCacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _portfolioCacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _portfolioEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        var portfolio = new PortfolioService(
            _portfolioCacheMock.Object,
            _portfolioEntityServiceMock.Object,
            NullLogger<PortfolioService>.Instance);

        return new BriefingService(
            portfolioService: portfolio,
            cache: _cache,
            membershipResolver: _resolverMock.Object,
            dataverse: _dataverseMock.Object,
            logger: NullLogger<BriefingService>.Instance,
            briefingAi: null);
    }

    /// <summary>
    /// Configures the strict IDataverseService mock to satisfy the AAD-oid → systemuserid
    /// cross-reference call. Pass <c>null</c> to simulate "user not provisioned" (empty
    /// EntityCollection).
    /// </summary>
    private void SetupAadOidLookup(Guid? returnUserId)
    {
        var systemUserCollection = returnUserId.HasValue
            ? new EntityCollection(new List<Entity>
            {
                new Entity("systemuser", returnUserId.Value)
            })
            : new EntityCollection();

        _dataverseMock
            .Setup(d => d.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "systemuser"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemUserCollection);
    }

    /// <summary>
    /// Builds a Dataverse <see cref="Entity"/> shaped like a sprk_matter row with the
    /// columns BriefingService.QueryMatterDetailsAsync reads.
    /// </summary>
    private static Entity BuildMatterEntity(
        Guid id,
        string name,
        int overdue,
        decimal spend,
        decimal budget,
        DateTime? deadline)
    {
        var entity = new Entity("sprk_matter", id);
        entity["sprk_name"] = name;
        entity["sprk_overdueeventcount"] = overdue;
        entity["sprk_totalspend"] = new Money(spend);
        entity["sprk_totalbudget"] = new Money(budget);
        entity["statecode"] = new OptionSetValue(0);
        if (deadline.HasValue)
        {
            entity["sprk_duedate"] = deadline.Value;
        }
        return entity;
    }
}
