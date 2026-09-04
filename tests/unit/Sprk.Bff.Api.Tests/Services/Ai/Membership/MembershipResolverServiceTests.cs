// R3 Part 1 — Task 033: MembershipResolverService unit tests
//
// Verifies the orchestration contract from
// design.md Part 1 § "Endpoint contract":
//   - Happy path: discovery + identity → FetchXml → MembershipResponse.
//   - byRole map populated correctly (multi-role per row supported).
//   - Cache hit on second call within 5min (Fake cache call-count probe).
//   - Roles filter narrows descriptors considered.
//   - IdentityTypes filter narrows descriptors considered.
//   - Empty memberships return empty Ids + Count=0 (NOT error).
//   - Cancellation propagates (throws OperationCanceledException).
//   - Input guards: empty Guid + empty entityType throw ArgumentException.
//
// Test fixtures use Moq for IMembershipFieldDiscoveryService,
// IIdentityNormalizationService, IDataverseService; FakeDistributedCache (Dictionary-
// backed) for IDistributedCache so cache hits are observable.
//
// Per docs/procedures/testing-and-code-quality.md — Arrange-Act-Assert + FluentAssertions.

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Membership;

public class MembershipResolverServiceTests
{
    private static readonly Guid TestSystemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestContactId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TestTeamA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TestTeamB = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TestBusinessUnit = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TestAccount = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid TestOrgA = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static readonly Guid MatterIdA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid MatterIdB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid MatterIdC = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private const string EntityType = "sprk_matter";

    // ─────────────────────────────────────────────────────────────────────
    // Happy path
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_HappyPath_ReturnsExpectedResponse()
    {
        // Arrange
        var discovery = BuildDiscoveryMock(
            Descriptor("ownerid", "owner", "SystemUser"),
            Descriptor("sprk_owningteam", "owningTeam", "Team"),
            Descriptor("sprk_assignedattorney1", "assignedAttorney", "Contact"));

        var identity = BuildIdentityMock(BuildFullIdentity());

        // Three matters returned. MatterA: ownerid=TestSystemUserId.
        // MatterB: sprk_owningteam=TeamA. MatterC: sprk_assignedattorney1=TestContactId.
        var dataverse = BuildDataverseMockReturning(
            MatterRow(MatterIdA, ("ownerid", new EntityReference("systemuser", TestSystemUserId))),
            MatterRow(MatterIdB, ("sprk_owningteam", new EntityReference("team", TestTeamA))),
            MatterRow(MatterIdC, ("sprk_assignedattorney1", new EntityReference("contact", TestContactId))));

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        // Act
        var result = await sut.ResolveAsync(TestSystemUserId, EntityType, options: null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.EntityType.Should().Be(EntityType);
        result.PersonIdentity.SystemUserId.Should().Be(TestSystemUserId);
        result.Count.Should().Be(3);
        result.Ids.Should().BeEquivalentTo(new[] { MatterIdA, MatterIdB, MatterIdC });
        result.Ids.Should().BeInAscendingOrder();
        result.ContinuationToken.Should().BeNull("3 results fit within default limit of 500");
        result.CacheExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Regression (r5 2026-07-09 — Daily Briefing completeness)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_GeneratedFetchXml_MustNotUseDistinct_SoRecordIdsAreReturned()
    {
        // REGRESSION: the resolver's FetchXml used distinct='true' but did NOT project the
        // primary key. In Dataverse, distinct='true' dedupes on the PROJECTED columns and does
        // not return the record id unless it is explicitly projected — so records sharing the
        // same descriptor-lookup values (e.g. every matter a user owns → same ownerid) collapsed
        // into a few rows WITH EMPTY IDS, which MaterializeResults then dropped (row.Id ==
        // Guid.Empty). Net effect: membership resolved to 0 for a user who owned 45 matters, and
        // the Daily Briefing silently omitted every membership-scoped record — a false "all
        // caught up". The generated query MUST NOT use distinct (MaterializeResults already
        // dedupes by id via a HashSet, and these single-entity queries have no link-entity that
        // could multiply rows). Verified live: rows 0 → 49 after removing distinct.
        var discovery = BuildDiscoveryMock(Descriptor("ownerid", "owner", "SystemUser"));
        var identity = BuildIdentityMock(BuildFullIdentity());

        FetchExpression? captured = null;
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Callback<FetchExpression, CancellationToken>((fe, _) => captured = fe)
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                MatterRow(MatterIdA, ("ownerid", new EntityReference("systemuser", TestSystemUserId))),
            }));

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        // Act
        var result = await sut.ResolveAsync(TestSystemUserId, EntityType, options: null, CancellationToken.None);

        // Assert — the generated query must not use distinct…
        captured.Should().NotBeNull();
        captured!.Query.Should().NotContain(
            "distinct",
            "distinct='true' without projecting the primary key makes Dataverse return empty record ids, " +
            "which silently zeroes membership resolution (r5 briefing-completeness regression)");
        // …and the matched record's id must survive materialization (not be dropped as empty).
        result.Ids.Should().Contain(MatterIdA);
    }

    [Fact]
    public async Task ResolveAsync_ByRoleMap_PopulatedCorrectly()
    {
        // Arrange — single matter has BOTH ownerid AND sprk_assignedattorney1 populated
        // (the same user is both owner AND attorney — multi-role per row valid).
        var discovery = BuildDiscoveryMock(
            Descriptor("ownerid", "owner", "SystemUser"),
            Descriptor("sprk_owningteam", "owningTeam", "Team"),
            Descriptor("sprk_assignedattorney1", "assignedAttorney", "Contact"));

        var identity = BuildIdentityMock(BuildFullIdentity());

        var dataverse = BuildDataverseMockReturning(
            // Multi-role row: matterA has both owner=user AND assignedAttorney=contact
            MatterRow(MatterIdA,
                ("ownerid", new EntityReference("systemuser", TestSystemUserId)),
                ("sprk_assignedattorney1", new EntityReference("contact", TestContactId))),
            // owningTeam only
            MatterRow(MatterIdB, ("sprk_owningteam", new EntityReference("team", TestTeamA))));

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        // Act
        var result = await sut.ResolveAsync(TestSystemUserId, EntityType, options: null, CancellationToken.None);

        // Assert
        result.ByRole.Should().ContainKeys("owner", "owningTeam", "assignedAttorney");
        result.ByRole["owner"].Should().BeEquivalentTo(new[] { MatterIdA });
        result.ByRole["owningTeam"].Should().BeEquivalentTo(new[] { MatterIdB });
        result.ByRole["assignedAttorney"].Should().BeEquivalentTo(new[] { MatterIdA });
        // MatterA appears in BOTH owner AND assignedAttorney — multi-role per row.
    }

    [Fact]
    public async Task ResolveAsync_RolesWithZeroMatches_EmittedAsEmptyList()
    {
        // Empty buckets for queried roles helps clients distinguish "no matches"
        // from "not in query".
        var discovery = BuildDiscoveryMock(
            Descriptor("ownerid", "owner", "SystemUser"),
            Descriptor("sprk_assignedlawfirm1", "assignedLawFirm", "Organization"));

        // User has NO organizations → assignedLawFirm cannot match.
        var identity = BuildIdentityMock(new PersonIdentity(
            TestSystemUserId,
            ContactId: null,
            PrimaryEmail: null,
            TeamIds: Array.Empty<Guid>(),
            BusinessUnitId: null,
            AccountId: null,
            OrganizationIds: Array.Empty<Guid>()));

        var dataverse = BuildDataverseMockReturning(
            MatterRow(MatterIdA, ("ownerid", new EntityReference("systemuser", TestSystemUserId))));

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        var result = await sut.ResolveAsync(TestSystemUserId, EntityType, options: null, CancellationToken.None);

        result.ByRole.Should().ContainKey("owner");
        result.ByRole.Should().ContainKey("assignedLawFirm");
        result.ByRole["owner"].Should().BeEquivalentTo(new[] { MatterIdA });
        result.ByRole["assignedLawFirm"].Should().BeEmpty(
            "the role was queried but produced no matches — empty list, not absent key");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Cache behavior
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_CacheHit_OnSecondCallWithinTtl()
    {
        // Arrange
        var discovery = BuildDiscoveryMock(Descriptor("ownerid", "owner", "SystemUser"));
        var identity = BuildIdentityMock(BuildFullIdentity());
        var dataverse = BuildDataverseMockReturning(
            MatterRow(MatterIdA, ("ownerid", new EntityReference("systemuser", TestSystemUserId))));

        var fakeCache = new FakeDistributedCache();
        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object, fakeCache);

        // Act — first call: MISS → resolve → cache set
        var first = await sut.ResolveAsync(TestSystemUserId, EntityType, options: null, CancellationToken.None);
        // Second call (identical args): cache HIT → no further Dataverse calls
        var second = await sut.ResolveAsync(TestSystemUserId, EntityType, options: null, CancellationToken.None);

        // Assert
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        second.Ids.Should().BeEquivalentTo(first.Ids);
        second.ByRole.Should().BeEquivalentTo(first.ByRole);
        second.PersonIdentity.SystemUserId.Should().Be(first.PersonIdentity.SystemUserId);

        // Cache probes
        fakeCache.GetCallCount.Should().Be(2, "GetAsync called once per ResolveAsync invocation");
        fakeCache.SetCallCount.Should().Be(1, "Set only on the cache MISS");

        // Discovery + identity + Dataverse called ONLY ONCE (cache hit avoided second round-trip)
        discovery.Verify(d => d.DiscoverAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        identity.Verify(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        dataverse.Verify(d => d.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Filter behavior
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_RolesFilter_NarrowsDescriptors()
    {
        // Arrange — 3 discovered, but caller asks for "owner" ONLY.
        var discovery = BuildDiscoveryMock(
            Descriptor("ownerid", "owner", "SystemUser"),
            Descriptor("sprk_owningteam", "owningTeam", "Team"),
            Descriptor("sprk_assignedattorney1", "assignedAttorney", "Contact"));

        var identity = BuildIdentityMock(BuildFullIdentity());

        FetchExpression? capturedFetch = null;
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Callback<FetchExpression, CancellationToken>((fe, _) => capturedFetch = fe)
            .ReturnsAsync(new EntityCollection());

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        // Act
        var result = await sut.ResolveAsync(
            TestSystemUserId,
            EntityType,
            new MembershipResolveOptions(Roles: new[] { "owner" }),
            CancellationToken.None);

        // Assert — only "owner" descriptor used; ByRole has ONLY "owner"
        result.ByRole.Keys.Should().BeEquivalentTo(new[] { "owner" });

        // FetchXml should reference ownerid but NOT sprk_owningteam / sprk_assignedattorney1
        capturedFetch.Should().NotBeNull();
        capturedFetch!.Query.Should().Contain("ownerid");
        capturedFetch.Query.Should().NotContain("sprk_owningteam");
        capturedFetch.Query.Should().NotContain("sprk_assignedattorney1");
    }

    [Fact]
    public async Task ResolveAsync_IdentityTypesFilter_NarrowsDescriptors()
    {
        // Arrange — caller filters to identity types "SystemUser" only.
        // Only "owner" (SystemUser) survives — "owningTeam"/"assignedAttorney" dropped.
        var discovery = BuildDiscoveryMock(
            Descriptor("ownerid", "owner", "SystemUser"),
            Descriptor("sprk_owningteam", "owningTeam", "Team"),
            Descriptor("sprk_assignedattorney1", "assignedAttorney", "Contact"));

        var identity = BuildIdentityMock(BuildFullIdentity());

        FetchExpression? capturedFetch = null;
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Callback<FetchExpression, CancellationToken>((fe, _) => capturedFetch = fe)
            .ReturnsAsync(new EntityCollection());

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        // Act
        var result = await sut.ResolveAsync(
            TestSystemUserId,
            EntityType,
            new MembershipResolveOptions(IdentityTypes: new[] { "SystemUser" }),
            CancellationToken.None);

        // Assert
        result.ByRole.Keys.Should().BeEquivalentTo(new[] { "owner" });
        capturedFetch!.Query.Should().Contain("ownerid");
        capturedFetch.Query.Should().NotContain("sprk_owningteam");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Empty memberships
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_NoDiscoveredDescriptors_ReturnsEmptyNotError()
    {
        // Discovery returns zero fields (no Lookup → identity table on this entity).
        var discovery = BuildDiscoveryMock(/* no descriptors */);
        var identity = BuildIdentityMock(BuildFullIdentity());
        // Dataverse should NEVER be queried when there are no descriptors.
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        var result = await sut.ResolveAsync(TestSystemUserId, EntityType, options: null, CancellationToken.None);

        result.Should().NotBeNull();
        result.Count.Should().Be(0);
        result.Ids.Should().BeEmpty();
        result.ByRole.Should().BeEmpty();
        dataverse.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveAsync_ZeroIdentityValuesForDescriptors_ReturnsEmpty()
    {
        // Descriptors target Contact + Team, but user has NEITHER contact nor teams.
        var discovery = BuildDiscoveryMock(
            Descriptor("sprk_owningteam", "owningTeam", "Team"),
            Descriptor("sprk_assignedattorney1", "assignedAttorney", "Contact"));

        var identity = BuildIdentityMock(new PersonIdentity(
            TestSystemUserId,
            ContactId: null,
            PrimaryEmail: null,
            TeamIds: Array.Empty<Guid>(),
            BusinessUnitId: TestBusinessUnit,
            AccountId: null,
            OrganizationIds: Array.Empty<Guid>()));

        // Dataverse should NOT be queried — no conditions could be built.
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        var result = await sut.ResolveAsync(TestSystemUserId, EntityType, options: null, CancellationToken.None);

        result.Count.Should().Be(0);
        result.Ids.Should().BeEmpty();
        result.ByRole.Should().ContainKeys("owningTeam", "assignedAttorney");
        result.ByRole["owningTeam"].Should().BeEmpty();
        result.ByRole["assignedAttorney"].Should().BeEmpty();
        dataverse.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveAsync_DataverseReturnsZeroRows_ReturnsEmptyResponse()
    {
        // Descriptors + identity values are valid, but Dataverse query returns 0 rows.
        var discovery = BuildDiscoveryMock(Descriptor("ownerid", "owner", "SystemUser"));
        var identity = BuildIdentityMock(BuildFullIdentity());
        var dataverse = BuildDataverseMockReturning(/* zero rows */);

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        var result = await sut.ResolveAsync(TestSystemUserId, EntityType, options: null, CancellationToken.None);

        result.Count.Should().Be(0);
        result.Ids.Should().BeEmpty();
        result.ByRole.Should().ContainKey("owner");
        result.ByRole["owner"].Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────
    // R4 spec FR-11 / AC-11 — Contact-only `member_skipped` warning logging
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ContactDescriptorWithNullContactId_EmitsMemberSkippedWarning()
    {
        // R4 spec FR-11 / AC-11: when a Contact-typed membership descriptor is
        // present but the resolved identity has NO ContactId (no Contact↔SystemUser
        // cross-ref via azureactivedirectoryobjectid per ADR-028), the resolver
        // MUST emit a structured `member_skipped` warning so App Insights can
        // pivot on it. Behavior is unchanged — the descriptor is still skipped;
        // observability is added.
        //
        // Arrange
        var discovery = BuildDiscoveryMock(
            Descriptor("ownerid", "owner", "SystemUser"),
            Descriptor("sprk_assignedattorney1", "assignedAttorney", "Contact"));

        // Identity has NO ContactId — the Contact descriptor cannot match.
        var identity = BuildIdentityMock(new PersonIdentity(
            SystemUserId: TestSystemUserId,
            ContactId: null,
            PrimaryEmail: null,
            TeamIds: Array.Empty<Guid>(),
            BusinessUnitId: null,
            AccountId: null,
            OrganizationIds: Array.Empty<Guid>()));

        var dataverse = BuildDataverseMockReturning(
            MatterRow(MatterIdA, ("ownerid", new EntityReference("systemuser", TestSystemUserId))));

        var loggerMock = new Mock<ILogger<MembershipResolverService>>();
        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object, logger: loggerMock.Object);

        // Act
        var result = await sut.ResolveAsync(TestSystemUserId, EntityType, options: null, CancellationToken.None);

        // Assert — behavior unchanged: descriptor skipped, owner descriptor still resolves
        result.Should().NotBeNull();
        result.ByRole.Should().ContainKey("owner");
        result.ByRole.Should().ContainKey("assignedAttorney");
        result.ByRole["assignedAttorney"].Should().BeEmpty("the Contact descriptor was skipped — no match possible");

        // Assert — `member_skipped` warning emitted with required structured fields:
        // matter={EntityType}, contact={SystemUserId}, role={Role}, reason="no_systemuser_mapping"
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("member_skipped")
                                              && o.ToString()!.Contains(EntityType)
                                              && o.ToString()!.Contains("assignedAttorney")
                                              && o.ToString()!.Contains("no_systemuser_mapping")
                                              && o.ToString()!.Contains(TestSystemUserId.ToString())),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Contact-typed descriptor with no ContactId MUST emit exactly one `member_skipped` warning per FR-11");
    }

    [Fact]
    public async Task ResolveAsync_ContactDescriptorWithContactId_DoesNotEmitMemberSkipped()
    {
        // Inverse case: when ContactId IS present, no `member_skipped` warning fires.
        // Guards against false-positive emission.
        //
        // Arrange
        var discovery = BuildDiscoveryMock(
            Descriptor("sprk_assignedattorney1", "assignedAttorney", "Contact"));

        var identity = BuildIdentityMock(BuildFullIdentity()); // has ContactId

        var dataverse = BuildDataverseMockReturning(
            MatterRow(MatterIdA, ("sprk_assignedattorney1", new EntityReference("contact", TestContactId))));

        var loggerMock = new Mock<ILogger<MembershipResolverService>>();
        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object, logger: loggerMock.Object);

        // Act
        await sut.ResolveAsync(TestSystemUserId, EntityType, options: null, CancellationToken.None);

        // Assert — NO `member_skipped` warning emitted when ContactId is present.
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("member_skipped")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            "Contact-typed descriptor with valid ContactId MUST NOT emit `member_skipped`");
    }

    [Fact]
    public async Task ResolveAsync_MultipleContactDescriptorsAllNull_EmitsOneWarningPerDescriptor()
    {
        // Two Contact-typed descriptors both unresolvable → exactly two warnings.
        // Verifies per-descriptor emission semantics.
        var discovery = BuildDiscoveryMock(
            Descriptor("sprk_assignedattorney1", "assignedAttorney", "Contact"),
            Descriptor("sprk_secondarycontact", "secondaryContact", "Contact"));

        var identity = BuildIdentityMock(new PersonIdentity(
            SystemUserId: TestSystemUserId,
            ContactId: null,
            PrimaryEmail: null,
            TeamIds: Array.Empty<Guid>(),
            BusinessUnitId: TestBusinessUnit,
            AccountId: null,
            OrganizationIds: Array.Empty<Guid>()));

        // Dataverse not actually queried (no conditions buildable) — strict mock OK.
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);

        var loggerMock = new Mock<ILogger<MembershipResolverService>>();
        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object, logger: loggerMock.Object);

        await sut.ResolveAsync(TestSystemUserId, EntityType, options: null, CancellationToken.None);

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("member_skipped")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(2),
            "two Contact-typed descriptors with no ContactId MUST emit two `member_skipped` warnings");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Cancellation
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var discovery = new Mock<IMembershipFieldDiscoveryService>();
        var identity = new Mock<IIdentityNormalizationService>();
        var dataverse = new Mock<IDataverseService>();

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => sut.ResolveAsync(TestSystemUserId, EntityType, options: null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Input guards
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_EmptyGuid_ThrowsArgumentException()
    {
        var sut = CreateSut(
            new Mock<IMembershipFieldDiscoveryService>().Object,
            new Mock<IIdentityNormalizationService>().Object,
            new Mock<IDataverseService>().Object);

        Func<Task> act = () => sut.ResolveAsync(Guid.Empty, EntityType, options: null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("systemUserId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveAsync_EmptyEntityType_ThrowsArgumentException(string? entityType)
    {
        var sut = CreateSut(
            new Mock<IMembershipFieldDiscoveryService>().Object,
            new Mock<IIdentityNormalizationService>().Object,
            new Mock<IDataverseService>().Object);

        Func<Task> act = () => sut.ResolveAsync(TestSystemUserId, entityType!, options: null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("entityType");
    }

    // ─────────────────────────────────────────────────────────────────────
    // R3 Part 1D — task 054 — transitive includeRelated
    // ─────────────────────────────────────────────────────────────────────

    private static readonly Guid DocumentIdA = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd01");
    private static readonly Guid DocumentIdB = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd02");
    private static readonly Guid EventIdA = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeee01");

    [Fact]
    public async Task ResolveAsync_WithIncludeRelated_ReturnsTransitiveMemberships()
    {
        // AC-1D.1: includeRelated=documents returns documents on matters the user is on.
        // Primary: user owns MatterA. Related: DocumentA + DocumentB on MatterA via sprk_matter Lookup.
        var discovery = BuildDiscoveryMock(Descriptor("ownerid", "owner", "SystemUser"));
        // Back-ref lookups: sprk_document.sprk_matter → sprk_matter
        var discoveryMock = (Mock<IMembershipFieldDiscoveryService>)discovery;
        discoveryMock.Setup(d => d.DiscoverLookupsTargetingAsync(
                "sprk_document", "sprk_matter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "sprk_matter" });

        var identity = BuildIdentityMock(BuildFullIdentity());

        // Two FetchExpression calls expected (primary + transitive). Sequence them.
        var fetchCalls = new List<FetchExpression>();
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Callback<FetchExpression, CancellationToken>((fe, _) => fetchCalls.Add(fe))
            .ReturnsAsync(() =>
            {
                // First call → primary matter rows. Second call → documents.
                if (fetchCalls.Count == 1)
                {
                    return new EntityCollection(new List<Entity>
                    {
                        MatterRow(MatterIdA, ("ownerid", new EntityReference("systemuser", TestSystemUserId)))
                    });
                }
                var docA = new Entity("sprk_document") { Id = DocumentIdA };
                docA["sprk_matter"] = new EntityReference("sprk_matter", MatterIdA);
                var docB = new Entity("sprk_document") { Id = DocumentIdB };
                docB["sprk_matter"] = new EntityReference("sprk_matter", MatterIdA);
                return new EntityCollection(new List<Entity> { docA, docB });
            });

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        // Act
        var result = await sut.ResolveAsync(
            TestSystemUserId,
            EntityType,
            new MembershipResolveOptions(IncludeRelated: new[] { "sprk_document" }),
            CancellationToken.None);

        // Assert — primary surface unchanged
        result.Ids.Should().BeEquivalentTo(new[] { MatterIdA });
        result.ByRole["owner"].Should().BeEquivalentTo(new[] { MatterIdA });

        // R3 Part 1D — RelatedByRole populated with nested role → ids map
        result.RelatedByRole.Should().NotBeNull();
        result.RelatedByRole!.Should().ContainKey("sprk_document");
        var docs = result.RelatedByRole!["sprk_document"];
        docs.Should().ContainKey("matter"); // sprk_matter → "matter" via CamelCase strategy
        docs!["matter"].Should().BeEquivalentTo(new[] { DocumentIdA, DocumentIdB });
    }

    [Fact]
    public async Task ResolveAsync_WithMultipleIncludeRelated_ReturnsAllNestedKeys()
    {
        // includeRelated=sprk_document,sprk_event → both nested under RelatedByRole.
        var discovery = BuildDiscoveryMock(Descriptor("ownerid", "owner", "SystemUser"));
        var discoveryMock = (Mock<IMembershipFieldDiscoveryService>)discovery;
        discoveryMock.Setup(d => d.DiscoverLookupsTargetingAsync(
                "sprk_document", "sprk_matter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "sprk_matter" });
        discoveryMock.Setup(d => d.DiscoverLookupsTargetingAsync(
                "sprk_event", "sprk_matter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "sprk_matter" });

        var identity = BuildIdentityMock(BuildFullIdentity());

        var fetchCalls = new List<FetchExpression>();
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Callback<FetchExpression, CancellationToken>((fe, _) => fetchCalls.Add(fe))
            .ReturnsAsync(() =>
            {
                if (fetchCalls.Count == 1)
                {
                    return new EntityCollection(new List<Entity>
                    {
                        MatterRow(MatterIdA, ("ownerid", new EntityReference("systemuser", TestSystemUserId)))
                    });
                }
                if (fetchCalls.Count == 2)
                {
                    var docA = new Entity("sprk_document") { Id = DocumentIdA };
                    docA["sprk_matter"] = new EntityReference("sprk_matter", MatterIdA);
                    return new EntityCollection(new List<Entity> { docA });
                }
                var evt = new Entity("sprk_event") { Id = EventIdA };
                evt["sprk_matter"] = new EntityReference("sprk_matter", MatterIdA);
                return new EntityCollection(new List<Entity> { evt });
            });

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        var result = await sut.ResolveAsync(
            TestSystemUserId,
            EntityType,
            new MembershipResolveOptions(IncludeRelated: new[] { "sprk_document", "sprk_event" }),
            CancellationToken.None);

        result.RelatedByRole.Should().NotBeNull();
        result.RelatedByRole!.Should().ContainKeys("sprk_document", "sprk_event");
        result.RelatedByRole!["sprk_document"]["matter"].Should().BeEquivalentTo(new[] { DocumentIdA });
        result.RelatedByRole!["sprk_event"]["matter"].Should().BeEquivalentTo(new[] { EventIdA });
    }

    [Fact]
    public async Task ResolveAsync_WithoutIncludeRelated_RelatedByRoleIsNull()
    {
        // Absence-of-request guarantees null (so JsonIgnore omits the key).
        var discovery = BuildDiscoveryMock(Descriptor("ownerid", "owner", "SystemUser"));
        var identity = BuildIdentityMock(BuildFullIdentity());
        var dataverse = BuildDataverseMockReturning(
            MatterRow(MatterIdA, ("ownerid", new EntityReference("systemuser", TestSystemUserId))));

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        var result = await sut.ResolveAsync(TestSystemUserId, EntityType, options: null, CancellationToken.None);

        result.RelatedByRole.Should().BeNull("absence of includeRelated must yield null RelatedByRole");
    }

    [Fact]
    public async Task ResolveAsync_WithExplicitChainSyntax_ThrowsDepthExceeded()
    {
        // FR-1D.2 / Q3: dot syntax (e.g., "documents.events") rejected immediately.
        var discovery = BuildDiscoveryMock(Descriptor("ownerid", "owner", "SystemUser"));
        var identity = BuildIdentityMock(BuildFullIdentity());
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        Func<Task> act = () => sut.ResolveAsync(
            TestSystemUserId,
            EntityType,
            new MembershipResolveOptions(IncludeRelated: new[] { "sprk_document.sprk_event" }),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<MembershipDepthExceededException>();
        ex.Which.ReasonTag.Should().Be("explicit-chain-syntax");
        ex.Which.OffendingEntry.Should().Be("sprk_document.sprk_event");
        dataverse.VerifyNoOtherCalls(); // Validation runs before any I/O.
    }

    [Fact]
    public async Task ResolveAsync_WithUnknownRelatedEntity_ThrowsDepthExceeded()
    {
        // Related entity's metadata fetch fails → 1-hop verification cannot succeed.
        var discovery = BuildDiscoveryMock(Descriptor("ownerid", "owner", "SystemUser"));
        var discoveryMock = (Mock<IMembershipFieldDiscoveryService>)discovery;
        discoveryMock.Setup(d => d.DiscoverLookupsTargetingAsync(
                "sprk_unknown", "sprk_matter", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Entity 'sprk_unknown' not found in Dataverse metadata."));

        var identity = BuildIdentityMock(BuildFullIdentity());
        var dataverse = BuildDataverseMockReturning(
            MatterRow(MatterIdA, ("ownerid", new EntityReference("systemuser", TestSystemUserId))));

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        Func<Task> act = () => sut.ResolveAsync(
            TestSystemUserId,
            EntityType,
            new MembershipResolveOptions(IncludeRelated: new[] { "sprk_unknown" }),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<MembershipDepthExceededException>();
        ex.Which.ReasonTag.Should().Be("unknown-entity");
        ex.Which.OffendingEntry.Should().Be("sprk_unknown");
    }

    [Fact]
    public async Task ResolveAsync_WithRelatedEntityLackingBackReference_ThrowsDepthExceeded()
    {
        // FR-1D.2: requested related entity has no 1-hop Lookup to the primary entity.
        // Discovery returns empty → reject as "not-a-direct-lookup-target" → endpoint 400.
        var discovery = BuildDiscoveryMock(Descriptor("ownerid", "owner", "SystemUser"));
        var discoveryMock = (Mock<IMembershipFieldDiscoveryService>)discovery;
        discoveryMock.Setup(d => d.DiscoverLookupsTargetingAsync(
                "sprk_unrelated", "sprk_matter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        var identity = BuildIdentityMock(BuildFullIdentity());
        var dataverse = BuildDataverseMockReturning(
            MatterRow(MatterIdA, ("ownerid", new EntityReference("systemuser", TestSystemUserId))));

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        Func<Task> act = () => sut.ResolveAsync(
            TestSystemUserId,
            EntityType,
            new MembershipResolveOptions(IncludeRelated: new[] { "sprk_unrelated" }),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<MembershipDepthExceededException>();
        ex.Which.ReasonTag.Should().Be("not-a-direct-lookup-target");
        ex.Which.OffendingEntry.Should().Be("sprk_unrelated");
    }

    [Fact]
    public async Task ResolveAsync_WithIncludeRelatedAndNoPrimaryMatches_ReturnsEmptyNested()
    {
        // Primary returns zero rows → transitive still validates entity but inner map empty.
        var discovery = BuildDiscoveryMock(Descriptor("ownerid", "owner", "SystemUser"));
        var discoveryMock = (Mock<IMembershipFieldDiscoveryService>)discovery;
        discoveryMock.Setup(d => d.DiscoverLookupsTargetingAsync(
                "sprk_document", "sprk_matter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "sprk_matter" });

        var identity = BuildIdentityMock(BuildFullIdentity());
        var dataverse = BuildDataverseMockReturning(/* zero matter rows */);

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        var result = await sut.ResolveAsync(
            TestSystemUserId,
            EntityType,
            new MembershipResolveOptions(IncludeRelated: new[] { "sprk_document" }),
            CancellationToken.None);

        result.Count.Should().Be(0);
        result.RelatedByRole.Should().NotBeNull("requested entity must appear with empty inner map, not be absent");
        result.RelatedByRole!.Should().ContainKey("sprk_document");
        result.RelatedByRole!["sprk_document"].Should().ContainKey("matter");
        result.RelatedByRole!["sprk_document"]["matter"].Should().BeEmpty();
    }

    [Fact]
    public void MembershipResponse_NestedRelatedByRole_SerializesAsRelatedByRoleCamelCase()
    {
        // FR-1D.3: response shape extends byRole with nested relatedByRole map.
        // JSON shape: { "relatedByRole": { "sprk_document": { "matter": [guid, ...] } } }
        var systemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var doc = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd01");
        var related = (IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Guid>>>)
            new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Guid>>>
            {
                ["sprk_document"] = new Dictionary<string, IReadOnlyList<Guid>>
                {
                    ["matter"] = new[] { doc },
                },
            };

        var response = new MembershipResponse(
            EntityType: "sprk_matter",
            PersonIdentity: new PersonIdentity(systemUserId),
            Ids: Array.Empty<Guid>(),
            ByRole: new Dictionary<string, IReadOnlyList<Guid>>(),
            Count: 0,
            CacheExpiresAt: DateTimeOffset.UtcNow,
            ContinuationToken: null,
            RelatedByRole: related);

        var json = System.Text.Json.JsonSerializer.Serialize(response);

        json.Should().Contain("\"relatedByRole\":");
        json.Should().Contain("\"sprk_document\":");
        json.Should().Contain("\"matter\":");
        json.Should().Contain(doc.ToString("D"));
    }

    [Fact]
    public void MembershipResponse_NullRelatedByRole_OmittedFromJson()
    {
        // Absence of transitive request → null → not emitted (clients see no key).
        var response = new MembershipResponse(
            EntityType: "sprk_matter",
            PersonIdentity: new PersonIdentity(Guid.NewGuid()),
            Ids: Array.Empty<Guid>(),
            ByRole: new Dictionary<string, IReadOnlyList<Guid>>(),
            Count: 0,
            CacheExpiresAt: DateTimeOffset.UtcNow,
            ContinuationToken: null,
            RelatedByRole: null);

        var json = System.Text.Json.JsonSerializer.Serialize(response);

        json.Should().NotContain("\"relatedByRole\"",
            "JsonIgnore(WhenWritingNull) must omit the key when caller did not request includeRelated");
    }

    // ─────────────────────────────────────────────────────────────────────
    // teams-app-r1 task 021 — Contact-anchored entry (ResolveByContactAsync)
    // role-allowlist filtering (NFR-05, security-load-bearing)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveByContactAsync_AllowlistedAssignedContactRole_ReturnsMatchingRecords()
    {
        // (a) A contactId on an allowlisted sprk_assigned* contact-role lookup
        // returns the record — WITHOUT a systemuser, and WITHOUT touching
        // IIdentityNormalizationService (strict mock proves it is never called).
        var discovery = BuildDiscoveryMock(
            Descriptor("ownerid", "owner", "SystemUser"),                        // not contact
            Descriptor("sprk_owningteam", "owningTeam", "Team"),                 // not contact
            Descriptor("sprk_assignedattorney1", "assignedAttorney", "Contact")); // allowlisted

        var dataverse = BuildDataverseMockReturning(
            MatterRow(MatterIdC, ("sprk_assignedattorney1", new EntityReference("contact", TestContactId))));

        var strictIdentity = new Mock<IIdentityNormalizationService>(MockBehavior.Strict);
        var sut = CreateSut(discovery.Object, strictIdentity.Object, dataverse.Object);

        // Act
        var result = await sut.ResolveByContactAsync(TestContactId, EntityType, options: null, CancellationToken.None);

        // Assert — contact-only principal; only the allowlisted role resolves.
        result.Should().NotBeNull();
        result.PersonIdentity.ContactId.Should().Be(TestContactId);
        result.PersonIdentity.SystemUserId.Should().Be(Guid.Empty, "contact-only principal has no systemuser");
        result.Ids.Should().BeEquivalentTo(new[] { MatterIdC });
        result.ByRole.Should().ContainKey("assignedAttorney");
        result.ByRole["assignedAttorney"].Should().BeEquivalentTo(new[] { MatterIdC });
        result.ByRole.Should().NotContainKey("owner", "SystemUser lookups are not access-conferring on the contact path");
        result.ByRole.Should().NotContainKey("owningTeam", "Team lookups are not access-conferring on the contact path");
        result.RelatedByRole.Should().BeNull("the contact path performs no transitive expansion");

        // The systemuser identity-normalization path is never invoked.
        strictIdentity.Verify(
            i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveByContactAsync_AdverseOrNonAllowlistedContactFields_NeverConferAccess()
    {
        // (b) Adverse / informational fields MUST NEVER match:
        //  - sprk_opposingcounsel: Contact-typed but fails the sprk_assigned* convention.
        //  - sprk_regardingrecordid: polymorphic regarding lookup resolved to a
        //    NON-contact target → excluded by the contact-type gate.
        // A single allowlisted role is included to prove the filter keeps the right field only.
        var discovery = BuildDiscoveryMock(
            Descriptor("sprk_opposingcounsel", "opposingCounsel", "Contact"),
            Descriptor("sprk_regardingrecordid", "regardingRecord", "SystemUser"),
            Descriptor("sprk_assignedparalegal1", "assignedParalegal", "Contact"));

        FetchExpression? captured = null;
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Callback<FetchExpression, CancellationToken>((fe, _) => captured = fe)
            .ReturnsAsync(new EntityCollection());

        var sut = CreateSut(discovery.Object, new Mock<IIdentityNormalizationService>(MockBehavior.Strict).Object, dataverse.Object);

        // Act
        var result = await sut.ResolveByContactAsync(TestContactId, EntityType, options: null, CancellationToken.None);

        // Assert — only the allowlisted paralegal role is queried; adverse fields
        // never even reach the FetchXml.
        result.ByRole.Keys.Should().BeEquivalentTo(new[] { "assignedParalegal" });
        captured.Should().NotBeNull();
        captured!.Query.Should().Contain("sprk_assignedparalegal1");
        captured.Query.Should().NotContain("sprk_opposingcounsel",
            "an adverse contact lookup that fails the sprk_assigned* convention must never confer access");
        captured.Query.Should().NotContain("sprk_regardingrecordid",
            "a polymorphic regarding lookup resolved to a non-contact target must never confer access");
    }

    [Fact]
    public async Task ResolveByContactAsync_FieldNotInRegistry_DoesNotQualify_EvenIfNameMatchesRetiredConvention()
    {
        // FR-24 registry-lock test — INVERTS the retired convention-lock test this replaces
        // (ResolveByContactAsync_NewlyAddedAssignedConventionField_AutoQualifiesWithoutCodeChange,
        // which asserted the OPPOSITE: that naming a field sprk_assigned* was sufficient). Conferral is
        // now registry membership ONLY (ADR-034 Amendment A1) — a brand-new sprk_assigned*-named
        // contact lookup that nobody has reviewed onto the registry must NOT confer access purely by
        // matching the old naming pattern. Uses the seeded migration-default registry (SeededOptions()
        // via CreateSut's default), which has no entry for this never-before-seen field.
        var discovery = BuildDiscoveryMock(
            Descriptor("sprk_assignedguardianadlitem", "assignedGuardianAdLitem", "Contact"));

        var dataverse = BuildDataverseMockReturning(
            MatterRow(MatterIdA, ("sprk_assignedguardianadlitem", new EntityReference("contact", TestContactId))));

        var sut = CreateSut(discovery.Object, new Mock<IIdentityNormalizationService>(MockBehavior.Strict).Object, dataverse.Object);

        // Act
        var result = await sut.ResolveByContactAsync(TestContactId, EntityType, options: null, CancellationToken.None);

        // Assert — the never-before-seen field confers NOTHING despite matching the retired convention.
        result.Ids.Should().BeEmpty();
        result.ByRole.Should().NotContainKey("assignedGuardianAdLitem");
    }

    [Fact]
    public async Task ResolveByContactAsync_FieldAddedToRegistryViaConfigOnly_Qualifies()
    {
        // The other half of the FR-24 inversion (spec acceptance criterion, literal claim): adding the
        // SAME field to the registry — config only, no code change — makes it qualify.
        var discovery = BuildDiscoveryMock(
            Descriptor("sprk_assignedguardianadlitem", "assignedGuardianAdLitem", "Contact"));

        var dataverse = BuildDataverseMockReturning(
            MatterRow(MatterIdA, ("sprk_assignedguardianadlitem", new EntityReference("contact", TestContactId))));

        var membershipOptions = new MembershipOptions();
        membershipOptions.AccessConferringRoles.Entities[EntityType] = new List<AccessConferringColumn>
        {
            new() { Field = "sprk_assignedguardianadlitem", IdentityType = "Contact" },
        };

        var sut = CreateSut(
            discovery.Object,
            new Mock<IIdentityNormalizationService>(MockBehavior.Strict).Object,
            dataverse.Object,
            membershipOptions: membershipOptions);

        // Act
        var result = await sut.ResolveByContactAsync(TestContactId, EntityType, options: null, CancellationToken.None);

        // Assert — the SAME field now confers access, purely via the registry edit.
        result.Ids.Should().BeEquivalentTo(new[] { MatterIdA });
        result.ByRole.Should().ContainKey("assignedGuardianAdLitem");
        result.ByRole["assignedGuardianAdLitem"].Should().BeEquivalentTo(new[] { MatterIdA });
    }

    [Fact]
    public async Task ResolveByContactAsync_FieldOmittedFromRegistry_IsSuppressed_EvenWhenDiscoveryStillFindsIt()
    {
        // FR-24: exclusion is now suppression-BY-OMISSION, not a separate ExcludedFields mechanism
        // (that config surface is DELETED — the registry itself IS the allow-list; see
        // MembershipOptions.AccessConferringRegistry). A field discovery still classifies as a
        // Contact-typed lookup is suppressed simply by NOT appearing in the entity's registry list,
        // while another registry-listed field on the same entity still confers access.
        var discovery = BuildDiscoveryMock(
            Descriptor("sprk_assignedtoexternal", "assignedToExternal", "Contact"),
            Descriptor("sprk_assignedattorney1", "assignedAttorney", "Contact"));

        FetchExpression? captured = null;
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Callback<FetchExpression, CancellationToken>((fe, _) => captured = fe)
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                MatterRow(MatterIdB, ("sprk_assignedattorney1", new EntityReference("contact", TestContactId))),
            }));

        // Registry lists ONLY sprk_assignedattorney1 for sprk_matter — sprk_assignedtoexternal is
        // deliberately omitted, even though discovery still finds it as a Contact-typed lookup.
        var membershipOptions = new MembershipOptions();
        membershipOptions.AccessConferringRoles.Entities[EntityType] = new List<AccessConferringColumn>
        {
            new() { Field = "sprk_assignedattorney1", IdentityType = "Contact" },
        };

        var sut = CreateSut(
            discovery.Object,
            new Mock<IIdentityNormalizationService>(MockBehavior.Strict).Object,
            dataverse.Object,
            membershipOptions: membershipOptions);

        // Act
        var result = await sut.ResolveByContactAsync(TestContactId, EntityType, options: null, CancellationToken.None);

        // Assert — the omitted field is suppressed; the registry-listed field still confers access.
        result.ByRole.Keys.Should().BeEquivalentTo(new[] { "assignedAttorney" });
        result.ByRole.Should().NotContainKey("assignedToExternal");
        result.Ids.Should().BeEquivalentTo(new[] { MatterIdB });
        captured.Should().NotBeNull();
        captured!.Query.Should().Contain("sprk_assignedattorney1");
        captured.Query.Should().NotContain("sprk_assignedtoexternal",
            "a field omitted from the registry must never confer access, regardless of discovery output");
    }

    [Fact]
    public async Task ResolveByContactAsync_EmptyContactId_ThrowsArgumentException()
    {
        var sut = CreateSut(
            new Mock<IMembershipFieldDiscoveryService>().Object,
            new Mock<IIdentityNormalizationService>().Object,
            new Mock<IDataverseService>().Object);

        Func<Task> act = () => sut.ResolveByContactAsync(Guid.Empty, EntityType, options: null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("contactId");
    }

    // ─────────────────────────────────────────────────────────────────────
    // ADR-034 Amendment A1 / spec FR-24 (unified-access-control-r2 task 041) — the access-conferring
    // column registry: systemuser-plane opt-in gate (AccessConferringOnly), org-typed coverage,
    // fail-closed defaults, malformed-entry handling.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_AccessConferringOnlyTrue_FiltersToRegistryListedColumnsOnly()
    {
        // AC (systemuser opt-in gate): AccessConferringOnly=true applies the SAME registry filter
        // ResolveByContactAsync always applies, on the systemuser (ResolveAsync) path. A registry-listed
        // Contact column resolves; a non-registry Contact column (sprk_opposingcounsel — discovery still
        // finds it) never reaches the emitted FetchXml; a non-Contact/Organization descriptor
        // (SystemUser-typed "owner") is excluded regardless, same as the contact path always was.
        var discovery = BuildDiscoveryMock(
            Descriptor("ownerid", "owner", "SystemUser"),
            Descriptor("sprk_assignedattorney1", "assignedAttorney", "Contact"),
            Descriptor("sprk_opposingcounsel", "opposingCounsel", "Contact"));

        var identity = BuildIdentityMock(BuildFullIdentity());

        FetchExpression? captured = null;
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Callback<FetchExpression, CancellationToken>((fe, _) => captured = fe)
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                MatterRow(MatterIdA, ("sprk_assignedattorney1", new EntityReference("contact", TestContactId))),
            }));

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        // Act
        var result = await sut.ResolveAsync(
            TestSystemUserId,
            EntityType,
            new MembershipResolveOptions(AccessConferringOnly: true),
            CancellationToken.None);

        // Assert — only the registry-listed Contact column resolves.
        result.ByRole.Keys.Should().BeEquivalentTo(new[] { "assignedAttorney" });
        result.ByRole.Should().NotContainKey("owner");
        result.ByRole.Should().NotContainKey("opposingCounsel");
        captured.Should().NotBeNull();
        captured!.Query.Should().Contain("sprk_assignedattorney1");
        captured.Query.Should().NotContain("ownerid",
            "AccessConferringOnly excludes non-Contact/Organization descriptors even on the systemuser plane");
        captured.Query.Should().NotContain("sprk_opposingcounsel",
            "AccessConferringOnly excludes a Contact-typed field that is not in the registry");
    }

    [Fact]
    public async Task ResolveAsync_AccessConferringOnlyDefaultsFalse_ScopingOutputByteIdentical()
    {
        // AC pin (the OTHER half of the opt-in gate): omitting AccessConferringOnly (default false)
        // leaves ResolveAsync's pre-task-041 behavior unchanged — EVERY discovered descriptor still
        // confers, including a field that is NOT in the access-conferring registry. Uses the SAME
        // discovery shape as ResolveAsync_AccessConferringOnlyTrue_FiltersToRegistryListedColumnsOnly so
        // the two tests are a direct true/false contrast pair.
        var discovery = BuildDiscoveryMock(
            Descriptor("ownerid", "owner", "SystemUser"),
            Descriptor("sprk_assignedattorney1", "assignedAttorney", "Contact"),
            Descriptor("sprk_opposingcounsel", "opposingCounsel", "Contact"));

        var identity = BuildIdentityMock(BuildFullIdentity());

        var dataverse = BuildDataverseMockReturning(
            MatterRow(MatterIdA,
                ("ownerid", new EntityReference("systemuser", TestSystemUserId)),
                ("sprk_assignedattorney1", new EntityReference("contact", TestContactId)),
                ("sprk_opposingcounsel", new EntityReference("contact", TestContactId))));

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        // Act — options: null is the common case; AccessConferringOnly defaults false either way.
        var result = await sut.ResolveAsync(TestSystemUserId, EntityType, options: null, CancellationToken.None);

        // Assert — ALL THREE descriptors still confer; nothing is filtered by the registry.
        result.ByRole.Keys.Should().BeEquivalentTo(new[] { "owner", "assignedAttorney", "opposingCounsel" });
    }

    [Fact]
    public async Task ResolveAsync_AccessConferringOnly_OrgTypedRegistryColumn_SurvivesFilterAndResolves()
    {
        // AC (org half): a registry-listed org-typed column (sprk_assignedlawfirm1, seeded per ADR-034
        // M4) produces an Organization descriptor that SURVIVES the registry filter and resolves a real
        // row when the caller's identity carries organization affiliations (BuildFullIdentity() sets
        // OrganizationIds: [TestOrgA]).
        var discovery = BuildDiscoveryMock(
            Descriptor("sprk_assignedlawfirm1", "assignedLawFirm", "Organization"));

        var identity = BuildIdentityMock(BuildFullIdentity());

        FetchExpression? captured = null;
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Callback<FetchExpression, CancellationToken>((fe, _) => captured = fe)
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                MatterRow(MatterIdA, ("sprk_assignedlawfirm1", new EntityReference("sprk_organization", TestOrgA))),
            }));

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        // Act
        var result = await sut.ResolveAsync(
            TestSystemUserId,
            EntityType,
            new MembershipResolveOptions(AccessConferringOnly: true),
            CancellationToken.None);

        // Assert
        result.ByRole.Should().ContainKey("assignedLawFirm");
        result.ByRole["assignedLawFirm"].Should().BeEquivalentTo(new[] { MatterIdA });
        captured.Should().NotBeNull();
        captured!.Query.Should().Contain("sprk_assignedlawfirm1");
    }

    [Fact]
    public async Task ResolveAsync_AccessConferringOnly_OrganizationFieldNotInRegistry_NeverReachesFetchXml()
    {
        // AC (FR-24 negative, org half): an organization referenced via a NON-registry lookup confers
        // nothing — the field never reaches the emitted FetchXml. Mirrors the existing Contact-typed
        // adverse-field assertion shape (sprk_opposingcounsel) for the Organization identity type — this
        // is the disclosure ADR-034 Amendment A1 closes: unrestricted org expansion would otherwise
        // confer access from ANY organization referenced on the record, including opposing counsel.
        var discovery = BuildDiscoveryMock(
            Descriptor("sprk_assignedlawfirm1", "assignedLawFirm", "Organization"),          // registered
            Descriptor("sprk_opposingcounselfirm", "opposingCounselFirm", "Organization"));  // NOT registered

        var identity = BuildIdentityMock(BuildFullIdentity());

        FetchExpression? captured = null;
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Callback<FetchExpression, CancellationToken>((fe, _) => captured = fe)
            .ReturnsAsync(new EntityCollection());

        var sut = CreateSut(discovery.Object, identity.Object, dataverse.Object);

        // Act
        var result = await sut.ResolveAsync(
            TestSystemUserId,
            EntityType,
            new MembershipResolveOptions(AccessConferringOnly: true),
            CancellationToken.None);

        // Assert
        result.ByRole.Keys.Should().BeEquivalentTo(new[] { "assignedLawFirm" });
        captured.Should().NotBeNull();
        captured!.Query.Should().Contain("sprk_assignedlawfirm1");
        captured.Query.Should().NotContain("sprk_opposingcounselfirm",
            "an organization-typed lookup not in the access-conferring registry must never confer access " +
            "— unrestricted org expansion would disclose access from ANY organization on the record, " +
            "including opposing counsel");
    }

    [Fact]
    public async Task ResolveByContactAsync_EntityWithNoRegistryEntries_YieldsZeroConferringDescriptors()
    {
        // AC (fail-closed default, spec NFR-01): an entity with ZERO registry entries confers nothing
        // via the derived-member term — even when discovery finds a plausible-looking Contact lookup.
        // "sprk_document" (verified live 2026-09-04 against Dataverse metadata) has no
        // sprk_assigned*-prefixed lookups and is deliberately absent from the migration seed
        // (SeededOptions(), used by CreateSut's default).
        var discovery = BuildDiscoveryMock(
            Descriptor("sprk_relatedcontact", "relatedContact", "Contact"));

        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);

        var sut = CreateSut(discovery.Object, new Mock<IIdentityNormalizationService>(MockBehavior.Strict).Object, dataverse.Object);

        // Act
        var result = await sut.ResolveByContactAsync(TestContactId, "sprk_document", options: null, CancellationToken.None);

        // Assert
        result.Ids.Should().BeEmpty();
        result.ByRole.Should().BeEmpty();
        dataverse.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveByContactAsync_MalformedRegistryEntry_IsIgnoredAndLogged_NeverWidened()
    {
        // AC (malformed entry, spec NFR-01): a registry entry whose IdentityType is neither Contact nor
        // Organization is logged and ignored — never widened. A well-formed sibling entry on the same
        // entity still confers access, proving the malformed entry does not poison the whole filter.
        var discovery = BuildDiscoveryMock(
            Descriptor("sprk_assignedattorney1", "assignedAttorney", "Contact"),
            Descriptor("sprk_owningteam", "owningTeam", "Team"));

        var dataverse = BuildDataverseMockReturning(
            MatterRow(MatterIdA, ("sprk_assignedattorney1", new EntityReference("contact", TestContactId))));

        var membershipOptions = new MembershipOptions();
        membershipOptions.AccessConferringRoles.Entities[EntityType] = new List<AccessConferringColumn>
        {
            new() { Field = "sprk_assignedattorney1", IdentityType = "Contact" },
            new() { Field = "sprk_owningteam", IdentityType = "Team" }, // malformed: Team is not Contact/Organization
        };

        var loggerMock = new Mock<ILogger<MembershipResolverService>>();
        var sut = CreateSut(
            discovery.Object,
            new Mock<IIdentityNormalizationService>(MockBehavior.Strict).Object,
            dataverse.Object,
            membershipOptions: membershipOptions,
            logger: loggerMock.Object);

        // Act
        var result = await sut.ResolveByContactAsync(TestContactId, EntityType, options: null, CancellationToken.None);

        // Assert — the malformed entry is dropped; the well-formed sibling entry still resolves.
        result.ByRole.Keys.Should().BeEquivalentTo(new[] { "assignedAttorney" });
        result.ByRole.Should().NotContainKey("owningTeam");

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("malformed")
                                              && o.ToString()!.Contains("sprk_owningteam")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce,
            "a malformed registry entry (unsupported IdentityType) MUST be logged, per spec NFR-01");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static MembershipResolverService CreateSut(
        IMembershipFieldDiscoveryService discovery,
        IIdentityNormalizationService identity,
        IDataverseService dataverse,
        ITenantCache? cache = null,
        ILogger<MembershipResolverService>? logger = null,
        MembershipOptions? membershipOptions = null)
    {
        return new MembershipResolverService(
            discovery,
            identity,
            dataverse,
            cache ?? new FakeDistributedCache(),
            Options.Create(membershipOptions ?? SeededOptions()),
            logger ?? NullLogger<MembershipResolverService>.Instance);
    }

    /// <summary>
    /// The default <see cref="MembershipOptions"/> for tests that don't construct a custom registry.
    /// Applies the SAME post-configure seeding production DI uses (<see cref="MembershipOptionsDefaults"/>),
    /// so the contact-path allowlist tests exercise the REAL migration-seeded access-conferring registry
    /// (ADR-034 Amendment A1 / spec FR-24) rather than an empty one. Raw <c>new MembershipOptions()</c>
    /// has an EMPTY registry — Dictionary/List-typed seeding must go through post-configure, same
    /// reasoning already documented on <see cref="MembershipOptions.IncludedIdentityTables"/>
    /// (IConfiguration.Bind APPENDS to List-typed values, so a property-level default would double up
    /// an operator's own entries).
    /// </summary>
    private static MembershipOptions SeededOptions()
    {
        var options = new MembershipOptions();
        new MembershipOptionsDefaults().PostConfigure(name: null, options);
        return options;
    }

    private static Mock<IMembershipFieldDiscoveryService> BuildDiscoveryMock(
        params MembershipDescriptor[] descriptors)
    {
        var result = new DiscoveryResult(
            EntityType: EntityType,
            DiscoveredAt: DateTimeOffset.UtcNow,
            DiscoveredFields: descriptors,
            ExcludedFields: Array.Empty<IgnoredField>(),
            IgnoredFields: Array.Empty<IgnoredField>());

        var mock = new Mock<IMembershipFieldDiscoveryService>();
        mock.Setup(d => d.DiscoverAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    private static Mock<IIdentityNormalizationService> BuildIdentityMock(PersonIdentity identity)
    {
        var mock = new Mock<IIdentityNormalizationService>();
        mock.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity);
        return mock;
    }

    private static Mock<IDataverseService> BuildDataverseMockReturning(params Entity[] rows)
    {
        var ec = new EntityCollection(rows.ToList());
        var mock = new Mock<IDataverseService>();
        mock.Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ec);
        return mock;
    }

    private static MembershipDescriptor Descriptor(string field, string role, string identityType)
        => new(Field: field, Role: role, IdentityType: identityType,
               TargetTable: identityType.ToLowerInvariant(), Source: "auto");

    private static PersonIdentity BuildFullIdentity() => new(
        SystemUserId: TestSystemUserId,
        ContactId: TestContactId,
        PrimaryEmail: "ada@spaarke.dev",
        TeamIds: new[] { TestTeamA, TestTeamB },
        BusinessUnitId: TestBusinessUnit,
        AccountId: TestAccount,
        OrganizationIds: new[] { TestOrgA });

    private static Entity MatterRow(Guid id, params (string attr, object value)[] attributes)
    {
        var entity = new Entity("sprk_matter") { Id = id };
        foreach (var (attr, value) in attributes)
        {
            entity[attr] = value;
        }
        return entity;
    }

    /// <summary>
    /// Tiny in-memory <see cref="ITenantCache"/> for unit-test isolation.
    /// Tracks Get/Set call counts so tests can verify cache hit/miss behavior
    /// without a Redis dependency. Named <c>FakeDistributedCache</c> for
    /// backward-compatibility with pre-migration test bodies.
    /// </summary>
    private sealed class FakeDistributedCache : ITenantCache
    {
        private readonly Dictionary<string, object?> _store = new(StringComparer.Ordinal);
        public int GetCallCount { get; private set; }
        public int SetCallCount { get; private set; }

        private static string BuildKey(string tenantId, string resource, string id, int version)
            => $"tenant:{tenantId}:{resource}:{id}:v{version}";

        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
        {
            GetCallCount++;
            var key = BuildKey(tenantId, resource, id, version);
            return Task.FromResult(_store.TryGetValue(key, out var v) ? (T?)v : default);
        }

        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
        {
            SetCallCount++;
            var key = BuildKey(tenantId, resource, id, version);
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
        {
            var key = BuildKey(tenantId, resource, id, version);
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public async Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
        {
            var existing = await GetAsync<T>(tenantId, resource, id, version, cacheInstance, ct);
            if (existing is not null)
            {
                return existing;
            }
            var produced = await factory(ct);
            if (produced is not null)
            {
                await SetAsync(tenantId, resource, id, version, produced, ttl, cacheInstance, ct);
            }
            return produced!;
        }
    }
}
