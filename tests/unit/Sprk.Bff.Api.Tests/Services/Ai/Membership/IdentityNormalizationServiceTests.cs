// R3 Part 1 — Task 031: IdentityNormalizationService unit tests
//
// Verifies the six identity-type-path contract from
// design.md Part 1 § Identity normalization contract:
//   - Happy path: all six paths populate PersonIdentity correctly.
//   - User without contact: ContactId == null, no exception thrown.
//   - Multi-team user: TeamIds populates from teammembership rows.
//   - Cache hit: second call within TTL skips Dataverse round-trips.
//   - Failure isolation: a thrown exception on one identity-type path
//     produces null/empty on that field but does NOT fail other paths.
//   - Cancellation: token propagates to in-flight queries (throws OCE).
//   - Empty input guard: Guid.Empty throws ArgumentException.
//   - Empty resolvers: zero registered IIdentityOrganizationResolver yields
//     an empty OrganizationIds list (acceptable per task 031 contract).
//
// Test fixtures use Moq for IDataverseService + IIdentityOrganizationResolver
// and a tiny FakeDistributedCache (Dictionary-backed) for IDistributedCache.
// Per docs/procedures/testing-and-code-quality.md — Arrange-Act-Assert + FluentAssertions.

using FluentAssertions;
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

[Trait("status", "repaired")]
public class IdentityNormalizationServiceTests
{
    private static readonly Guid TestSystemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestAadObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TestContactId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TestBusinessUnitId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TestAccountId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TestTeamIdA = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid TestTeamIdB = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid TestOrgIdA = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid TestOrgIdB = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private const string EmailAddress = "ada@spaarke.dev";

    // ─────────────────────────────────────────────────────────────────────
    // Happy path
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_HappyPath_PopulatesAllSixIdentityPaths()
    {
        // Arrange
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        SetupSystemUserRow(dataverse, TestSystemUserId,
            email: EmailAddress, businessUnitId: TestBusinessUnitId, aadOid: TestAadObjectId);
        SetupContactCrossRefRow(dataverse, TestAadObjectId, TestContactId);
        SetupTeamMembershipRows(dataverse, TestSystemUserId, TestTeamIdA, TestTeamIdB);
        SetupContactWithAccountParent(dataverse, TestContactId, TestAccountId);

        var orgResolver = new Mock<IIdentityOrganizationResolver>();
        orgResolver
            .Setup(x => x.ResolveOrganizationsAsync(TestSystemUserId, TestContactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { TestOrgIdA, TestOrgIdB });

        var sut = CreateSut(dataverse.Object, organizationResolvers: new[] { orgResolver.Object });

        // Act
        var result = await sut.ResolveAsync(TestSystemUserId, CancellationToken.None);

        // Assert — every field populated per design.md Part 1 contract
        result.SystemUserId.Should().Be(TestSystemUserId);
        result.ContactId.Should().Be(TestContactId);
        result.PrimaryEmail.Should().Be(EmailAddress);
        result.TeamIds.Should().BeEquivalentTo(new[] { TestTeamIdA, TestTeamIdB });
        result.BusinessUnitId.Should().Be(TestBusinessUnitId);
        result.AccountId.Should().Be(TestAccountId);
        result.OrganizationIds.Should().BeEquivalentTo(new[] { TestOrgIdA, TestOrgIdB });
    }

    // ─────────────────────────────────────────────────────────────────────
    // Edge cases — user without contact, contact without account, etc.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_UserWithoutContact_ContactIdIsNullNoException()
    {
        // Arrange — systemuser exists but cross-ref query returns zero contacts
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        SetupSystemUserRow(dataverse, TestSystemUserId,
            email: EmailAddress, businessUnitId: TestBusinessUnitId, aadOid: TestAadObjectId);
        SetupContactCrossRefRow(dataverse, TestAadObjectId, contactId: null); // 0 rows
        SetupTeamMembershipRows(dataverse, TestSystemUserId); // no teams

        var sut = CreateSut(dataverse.Object);

        // Act
        var result = await sut.ResolveAsync(TestSystemUserId, CancellationToken.None);

        // Assert
        result.SystemUserId.Should().Be(TestSystemUserId);
        result.ContactId.Should().BeNull();
        result.AccountId.Should().BeNull(); // account path depends on contact — skipped
        result.PrimaryEmail.Should().Be(EmailAddress);
        result.BusinessUnitId.Should().Be(TestBusinessUnitId);
        result.TeamIds.Should().BeEmpty();
        result.OrganizationIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ContactExistsButHasNoAccountParent_AccountIdIsNull()
    {
        // Arrange — contact resolves but parentcustomerid is missing / Contact-typed
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        SetupSystemUserRow(dataverse, TestSystemUserId,
            email: EmailAddress, businessUnitId: TestBusinessUnitId, aadOid: TestAadObjectId);
        SetupContactCrossRefRow(dataverse, TestAadObjectId, TestContactId);
        SetupTeamMembershipRows(dataverse, TestSystemUserId);
        // Contact row WITHOUT parentcustomerid attribute → AccountId stays null
        var contactEntity = new Entity("contact") { Id = TestContactId };
        dataverse
            .Setup(x => x.RetrieveAsync(
                "contact",
                TestContactId,
                It.Is<string[]>(c => c.Contains("parentcustomerid")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(contactEntity);

        var sut = CreateSut(dataverse.Object);

        // Act
        var result = await sut.ResolveAsync(TestSystemUserId, CancellationToken.None);

        // Assert
        result.ContactId.Should().Be(TestContactId);
        result.AccountId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_MultiTeamUser_PopulatesAllTeamIds()
    {
        // Arrange — teammembership returns 3 rows
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        SetupSystemUserRow(dataverse, TestSystemUserId,
            email: EmailAddress, businessUnitId: TestBusinessUnitId, aadOid: TestAadObjectId);
        SetupContactCrossRefRow(dataverse, TestAadObjectId, contactId: null);

        var extraTeamId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SetupTeamMembershipRows(dataverse, TestSystemUserId, TestTeamIdA, TestTeamIdB, extraTeamId);

        var sut = CreateSut(dataverse.Object);

        // Act
        var result = await sut.ResolveAsync(TestSystemUserId, CancellationToken.None);

        // Assert
        result.TeamIds.Should().HaveCount(3);
        result.TeamIds.Should().BeEquivalentTo(new[] { TestTeamIdA, TestTeamIdB, extraTeamId });
    }

    // ─────────────────────────────────────────────────────────────────────
    // Failure isolation per path (spec FR-1A.5)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_SystemUserQueryThrows_OtherPathsStillResolve()
    {
        // Arrange — systemuser RetrieveAsync throws; teams path independent and succeeds
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(x => x.RetrieveAsync(
                "systemuser",
                TestSystemUserId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated Dataverse failure"));

        SetupTeamMembershipRows(dataverse, TestSystemUserId, TestTeamIdA);

        var orgResolver = new Mock<IIdentityOrganizationResolver>();
        orgResolver
            .Setup(x => x.ResolveOrganizationsAsync(TestSystemUserId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { TestOrgIdA });

        var sut = CreateSut(dataverse.Object, organizationResolvers: new[] { orgResolver.Object });

        // Act
        var result = await sut.ResolveAsync(TestSystemUserId, CancellationToken.None);

        // Assert — systemuser path failed → email/BU null; team + org paths still populated
        result.SystemUserId.Should().Be(TestSystemUserId);
        result.PrimaryEmail.Should().BeNull();
        result.BusinessUnitId.Should().BeNull();
        result.ContactId.Should().BeNull(); // depends on AAD oid from systemuser path
        result.TeamIds.Should().ContainSingle().Which.Should().Be(TestTeamIdA);
        result.OrganizationIds.Should().ContainSingle().Which.Should().Be(TestOrgIdA);
    }

    [Fact]
    public async Task ResolveAsync_OrganizationResolverThrows_OtherResolversStillMerge()
    {
        // Arrange — two resolvers; the first throws, second returns ids → result has the second's ids
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        SetupSystemUserRow(dataverse, TestSystemUserId, EmailAddress, TestBusinessUnitId, TestAadObjectId);
        SetupContactCrossRefRow(dataverse, TestAadObjectId, contactId: null);
        SetupTeamMembershipRows(dataverse, TestSystemUserId);

        var throwingResolver = new Mock<IIdentityOrganizationResolver>();
        throwingResolver
            .Setup(x => x.ResolveOrganizationsAsync(TestSystemUserId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("first resolver failed"));

        var goodResolver = new Mock<IIdentityOrganizationResolver>();
        goodResolver
            .Setup(x => x.ResolveOrganizationsAsync(TestSystemUserId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { TestOrgIdA, TestOrgIdB });

        var sut = CreateSut(dataverse.Object,
            organizationResolvers: new[] { throwingResolver.Object, goodResolver.Object });

        // Act
        var result = await sut.ResolveAsync(TestSystemUserId, CancellationToken.None);

        // Assert
        result.OrganizationIds.Should().BeEquivalentTo(new[] { TestOrgIdA, TestOrgIdB });
    }

    [Fact]
    public async Task ResolveAsync_NoOrganizationResolvers_ReturnsEmptyOrganizationIds()
    {
        // Arrange — happy systemuser/teams path, but ZERO IIdentityOrganizationResolver registered
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        SetupSystemUserRow(dataverse, TestSystemUserId, EmailAddress, TestBusinessUnitId, TestAadObjectId);
        SetupContactCrossRefRow(dataverse, TestAadObjectId, contactId: null);
        SetupTeamMembershipRows(dataverse, TestSystemUserId);

        var sut = CreateSut(dataverse.Object); // empty resolvers

        // Act
        var result = await sut.ResolveAsync(TestSystemUserId, CancellationToken.None);

        // Assert — explicitly empty (not null) — matches contract docs
        result.OrganizationIds.Should().NotBeNull();
        result.OrganizationIds.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Cache hit (ADR-009: 10-min TTL)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_SecondCallWithinTtl_HitsCacheAndSkipsDataverseRoundTrip()
    {
        // Arrange — minimal happy path; count Dataverse calls
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        SetupSystemUserRow(dataverse, TestSystemUserId, EmailAddress, TestBusinessUnitId, TestAadObjectId);
        SetupContactCrossRefRow(dataverse, TestAadObjectId, TestContactId);
        SetupTeamMembershipRows(dataverse, TestSystemUserId, TestTeamIdA);
        SetupContactWithAccountParent(dataverse, TestContactId, TestAccountId);

        var cache = new FakeDistributedCache();
        var sut = CreateSut(dataverse.Object, cache: cache);

        // Act — call twice
        var first = await sut.ResolveAsync(TestSystemUserId, CancellationToken.None);
        var second = await sut.ResolveAsync(TestSystemUserId, CancellationToken.None);

        // Assert — same content; the second call MUST have come from cache
        // (Dataverse mocks would throw on second invocation if not — see VerifyOnce below.)
        second.Should().BeEquivalentTo(first);

        dataverse.Verify(
            x => x.RetrieveAsync("systemuser", TestSystemUserId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "second ResolveAsync call should hit cache, not Dataverse");

        dataverse.Verify(
            x => x.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "expect two RetrieveMultipleAsync calls on first invocation (contact cross-ref + teammembership); none on second");

        // Cache was populated exactly once
        cache.SetCallCount.Should().Be(1);
        // And read twice (once miss, once hit)
        cache.GetCallCount.Should().BeGreaterThanOrEqualTo(2);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Cancellation
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var dataverse = new Mock<IDataverseService>();
        var sut = CreateSut(dataverse.Object);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => sut.ResolveAsync(TestSystemUserId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Input guards
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_EmptyGuid_ThrowsArgumentException()
    {
        var dataverse = new Mock<IDataverseService>();
        var sut = CreateSut(dataverse.Object);

        Func<Task> act = () => sut.ResolveAsync(Guid.Empty, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("systemUserId");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static IdentityNormalizationService CreateSut(
        IDataverseService dataverse,
        ITenantCache? cache = null,
        IEnumerable<IIdentityOrganizationResolver>? organizationResolvers = null)
    {
        return new IdentityNormalizationService(
            dataverse,
            cache ?? new FakeDistributedCache(),
            organizationResolvers ?? Array.Empty<IIdentityOrganizationResolver>(),
            Options.Create(new MembershipOptions()),
            NullLogger<IdentityNormalizationService>.Instance);
    }

    private static void SetupSystemUserRow(
        Mock<IDataverseService> dataverse,
        Guid systemUserId,
        string? email,
        Guid? businessUnitId,
        Guid? aadOid)
    {
        var entity = new Entity("systemuser") { Id = systemUserId };
        if (email is not null)
        {
            entity["internalemailaddress"] = email;
        }
        if (businessUnitId is { } buId)
        {
            entity["businessunitid"] = new EntityReference("businessunit", buId);
        }
        if (aadOid is { } oid)
        {
            entity["azureactivedirectoryobjectid"] = oid;
        }

        dataverse
            .Setup(x => x.RetrieveAsync(
                "systemuser",
                systemUserId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
    }

    private static void SetupContactCrossRefRow(
        Mock<IDataverseService> dataverse,
        Guid aadOid,
        Guid? contactId)
    {
        var collection = new EntityCollection();
        if (contactId is { } cid)
        {
            collection.Entities.Add(new Entity("contact") { Id = cid });
        }

        dataverse
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q =>
                    q.EntityName == "contact" &&
                    q.Criteria.Conditions.Any(c =>
                        c.AttributeName == "azureactivedirectoryobjectid" &&
                        c.Values.Count == 1 &&
                        Equals(c.Values[0], aadOid))),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
    }

    private static void SetupTeamMembershipRows(
        Mock<IDataverseService> dataverse,
        Guid systemUserId,
        params Guid[] teamIds)
    {
        var collection = new EntityCollection();
        foreach (var tid in teamIds)
        {
            var row = new Entity("teammembership");
            row["teamid"] = tid;
            collection.Entities.Add(row);
        }

        dataverse
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q =>
                    q.EntityName == "teammembership" &&
                    q.Criteria.Conditions.Any(c =>
                        c.AttributeName == "systemuserid" &&
                        c.Values.Count == 1 &&
                        Equals(c.Values[0], systemUserId))),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
    }

    private static void SetupContactWithAccountParent(
        Mock<IDataverseService> dataverse,
        Guid contactId,
        Guid accountId)
    {
        var entity = new Entity("contact") { Id = contactId };
        entity["parentcustomerid"] = new EntityReference("account", accountId);

        dataverse
            .Setup(x => x.RetrieveAsync(
                "contact",
                contactId,
                It.Is<string[]>(c => c.Contains("parentcustomerid")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
    }

    /// <summary>
    /// Tiny in-memory <see cref="ITenantCache"/> for unit-test isolation.
    /// Tracks Get/Set call counts so tests can verify cache hit/miss behavior
    /// without a Redis dependency.
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
