// teams-app-r1 Task 020 — WorkforcePrincipalResolver tests; rewired by unified-access-control-r2 task 141.
//
// Verifies the ADR-028 A2 / FR-04 contract — a validated workforce token resolves to EXACTLY one of:
//   (a) systemuser principal (systemuserId + derived contactId),
//   (b) contact-only principal (the contact BOUND to the caller's oid — task 141),
//   (c) explicit DENY carrying the binding decision's own code — never an unscoped principal.
//
// Module-boundary doubles only: IDataverseService (the systemuser lookup), IIdentityNormalizationService, and
// the in-memory identity store behind the REAL ContactIdentityBinder (no HTTP doubles — ADR-038 B1).

using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.IdentityBindingTestKit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public class WorkforcePrincipalResolverTests
{
    private static readonly Guid TestOid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TestSystemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestContactId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string TestEmail = "mike@customer.example";

    private readonly InMemoryContactIdentityStore _store = new();

    // ─────────────────────────────────────────────────────────────────────
    // (a) systemuser branch — AC-1
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_CallerHasSystemUser_ReturnsSystemUserPrincipalWithDerivedContact()
    {
        var dataverse = new Mock<IDataverseService>();
        SetupSystemUserLookup(dataverse, TestOid, TestSystemUserId);

        var identity = new Mock<IIdentityNormalizationService>();
        identity
            .Setup(x => x.ResolveAsync(TestSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(TestSystemUserId, ContactId: TestContactId));

        var result = await CreateSut(dataverse.Object, identity.Object)
            .ResolveAsync(BuildUser(TestOid, CustomerTenant), CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        result.Principal!.Kind.Should().Be(WorkforcePrincipalKind.SystemUser);
        result.Principal.SystemUserId.Should().Be(TestSystemUserId);
        result.Principal.ContactId.Should().Be(TestContactId, "the systemuser's contactId is derived");
        result.Principal.TenantId.Should().Be(CustomerTenant.ToString("D"));

        // A linked systemuser costs the binding nothing: no contact read, no write (no silent double-path).
        _store.Reads.Should().BeEmpty();
        _store.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_SystemUserWhoseLinkCannotBeMade_StillReturnsSystemUserPrincipal()
    {
        // The systemuser exists in the identity store with an email bound to someone else → the inline link is
        // refused (and flagged) — but a systemuser with no contact is still a valid ADR-034 principal.
        _store.AddSystemUser(TestSystemUserId, TestOid, TestEmail);
        _store.AddContact(TestContactId, email: TestEmail, oid: Guid.NewGuid().ToString("D"));
        var dataverse = new Mock<IDataverseService>();
        SetupSystemUserLookup(dataverse, TestOid, TestSystemUserId);
        var identity = new Mock<IIdentityNormalizationService>();
        identity
            .Setup(x => x.ResolveAsync(TestSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(TestSystemUserId, ContactId: null));

        var result = await CreateSut(dataverse.Object, identity.Object)
            .ResolveAsync(BuildUser(TestOid, CustomerTenant), CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        result.Principal!.Kind.Should().Be(WorkforcePrincipalKind.SystemUser);
        result.Principal.ContactId.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────
    // (b) contact-only branch — AC-2
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_NoSystemUserButAContactBoundToTheOid_ReturnsContactOnlyPrincipal()
    {
        _store.AddContact(TestContactId, oid: TestOid.ToString("D"), plane: IdentityPlaneMarker.Workforce);
        var dataverse = new Mock<IDataverseService>();
        SetupSystemUserLookup(dataverse, TestOid, systemUserId: null);

        var result = await CreateSut(dataverse.Object, new Mock<IIdentityNormalizationService>().Object)
            .ResolveAsync(BuildUser(TestOid, CustomerTenant, email: TestEmail), CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        result.Principal!.Kind.Should().Be(WorkforcePrincipalKind.ContactOnly);
        result.Principal.ContactId.Should().Be(TestContactId);
        result.Principal.SystemUserId.Should().BeNull("a contact-only principal has no systemuser");
    }

    [Fact]
    public async Task ResolveAsync_TheBindingReceivesTheEmailFromTheToken_PreferredUsernameIncluded()
    {
        // The email claim chain is threaded from the token to the first-sign-in bind.
        _store.AddContact(TestContactId, email: TestEmail);
        var dataverse = new Mock<IDataverseService>();
        SetupSystemUserLookup(dataverse, TestOid, systemUserId: null);

        var result = await CreateSut(dataverse.Object, new Mock<IIdentityNormalizationService>().Object)
            .ResolveAsync(BuildUser(TestOid, CustomerTenant, preferredUsername: TestEmail), CancellationToken.None);

        result.Principal!.ContactId.Should().Be(TestContactId);
        _store.Contacts[TestContactId].Oid.Should().Be(TestOid.ToString("D"));
    }

    // ─────────────────────────────────────────────────────────────────────
    // (c) deny branch — AC-3
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ANonMemberWithNoBinding_DeniesWithTheMemberTestCode()
    {
        // No systemuser, no bound contact, and a GUEST token — no creation, no bind, explicit deny.
        var dataverse = new Mock<IDataverseService>();
        SetupSystemUserLookup(dataverse, TestOid, systemUserId: null);

        var result = await CreateSut(dataverse.Object, new Mock<IIdentityNormalizationService>().Object)
            .ResolveAsync(BuildUser(TestOid, CustomerTenant, email: TestEmail, acct: "1"), CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.Principal.Should().BeNull();
        result.DenyReason.Should().Be(WorkforceDenyReason.PrincipalNotResolved);
        result.DenyCode.Should().Be(WorkforceMembershipTest.DenyGuest);
    }

    [Fact]
    public async Task ResolveAsync_TokenMissingOidClaim_DeniesWithMissingIdentityClaims()
    {
        var dataverse = new Mock<IDataverseService>();
        var sut = CreateSut(dataverse.Object, new Mock<IIdentityNormalizationService>().Object);

        var result = await sut.ResolveAsync(BuildUser(oid: null, CustomerTenant), CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.DenyReason.Should().Be(WorkforceDenyReason.MissingIdentityClaims);
        result.DenyCode.Should().Be(WorkforcePrincipalResolver.DenyMissingIdentityClaims);
        dataverse.Verify(
            x => x.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _store.Reads.Should().BeEmpty("no binding work is attempted when the caller cannot be identified");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <remarks>Link writes ON, so the inline link runs (it is gated on the rollout switch — verifier finding 4).</remarks>
    private WorkforcePrincipalResolver CreateSut(IDataverseService dataverse, IIdentityNormalizationService identity)
        => new(identity, dataverse, new FakeTenantCache(), Binder(_store),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ContactIdentityBinder.LinkWritesEnabledConfigKey] = "true",
            }).Build(),
            NullLogger<WorkforcePrincipalResolver>.Instance);

    private static ClaimsPrincipal BuildUser(
        Guid? oid, Guid? tid = null, string? email = null, string? preferredUsername = null, string acct = "0")
    {
        var claims = new List<Claim> { new("scp", "user_impersonation"), new("acct", acct), new("sub", "pairwise") };
        if (oid is { } o) claims.Add(new Claim("oid", o.ToString("D")));
        if (tid is { } t) claims.Add(new Claim("tid", t.ToString("D")));
        if (email is not null) claims.Add(new Claim("email", email));
        if (preferredUsername is not null) claims.Add(new Claim("preferred_username", preferredUsername));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestWorkforce"));
    }

    private static void SetupSystemUserLookup(Mock<IDataverseService> dataverse, Guid oid, Guid? systemUserId)
    {
        var collection = new EntityCollection();
        if (systemUserId is { } suid)
        {
            collection.Entities.Add(new Entity("systemuser") { Id = suid });
        }

        dataverse
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q =>
                    q.EntityName == "systemuser" &&
                    q.Criteria.Conditions.Any(c =>
                        c.AttributeName == "azureactivedirectoryobjectid" &&
                        c.Values.Count == 1 &&
                        Equals(c.Values[0], oid))),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
    }

    /// <summary>Dictionary-backed <see cref="ITenantCache"/>.</summary>
    private sealed class FakeTenantCache : ITenantCache
    {
        private readonly Dictionary<string, object?> _store = new(StringComparer.Ordinal);

        private static string Key(string tenantId, string resource, string id, int version)
            => $"tenant:{tenantId}:{resource}:{id}:v{version}";

        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
            => Task.FromResult(_store.TryGetValue(Key(tenantId, resource, id, version), out var v) ? (T?)v : default);

        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
        {
            _store[Key(tenantId, resource, id, version)] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
        {
            _store.Remove(Key(tenantId, resource, id, version));
            return Task.CompletedTask;
        }

        public async Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
        {
            var existing = await GetAsync<T>(tenantId, resource, id, version, cacheInstance, ct);
            if (existing is not null) return existing;
            var produced = await factory(ct);
            if (produced is not null) await SetAsync(tenantId, resource, id, version, produced, ttl, cacheInstance, ct);
            return produced!;
        }
    }
}
