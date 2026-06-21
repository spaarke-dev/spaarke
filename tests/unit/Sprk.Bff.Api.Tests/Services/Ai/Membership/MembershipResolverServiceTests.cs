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
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Membership;

public class MembershipResolverServiceTests
{
    private static readonly Guid TestSystemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestContactId    = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TestTeamA        = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TestTeamB        = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TestBusinessUnit = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TestAccount      = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid TestOrgA         = Guid.Parse("77777777-7777-7777-7777-777777777777");

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
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static MembershipResolverService CreateSut(
        IMembershipFieldDiscoveryService discovery,
        IIdentityNormalizationService identity,
        IDataverseService dataverse,
        IDistributedCache? cache = null)
    {
        return new MembershipResolverService(
            discovery,
            identity,
            dataverse,
            cache ?? new FakeDistributedCache(),
            Options.Create(new MembershipOptions()),
            NullLogger<MembershipResolverService>.Instance);
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
    /// Tiny in-memory <see cref="IDistributedCache"/> for unit-test isolation.
    /// Tracks Get/Set call counts so tests can verify cache hit/miss behavior
    /// without a Redis dependency.
    /// </summary>
    private sealed class FakeDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _store = new(StringComparer.Ordinal);
        public int GetCallCount { get; private set; }
        public int SetCallCount { get; private set; }

        public byte[]? Get(string key)
        {
            GetCallCount++;
            return _store.TryGetValue(key, out var v) ? v : null;
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Task.FromResult(Get(key));

        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _store.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            SetCallCount++;
            _store[key] = value;
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }
}
