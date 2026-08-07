// teams-app-r1 Task 020 — WorkforcePrincipalResolver tests.
//
// Verifies the ADR-028 A2 / FR-04 contract — a validated workforce token resolves to EXACTLY one of:
//   (a) systemuser principal (systemuserId + derived contactId),
//   (b) contact-only principal (contactId, by AAD oid OR verified-email fallback),
//   (c) explicit DENY — never a silent pass-through with an unscoped principal.
//
// Module-boundary mocks only (IDataverseService, IIdentityNormalizationService) per tests/CLAUDE.md
// Do's; these tests protect the auth-spine principal contract that tasks 021/022 compose on.
// (The WorkforceCallerAuthorizationFilter deny→401/403 endpoint-filter tests were removed with that
//  transitional filter in R2 task 018; the resolver — still consumed by CallerPrincipalResolver — stays.)

using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public class WorkforcePrincipalResolverTests
{
    private static readonly Guid TestOid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TestSystemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestContactId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string TestTenantId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string TestEmail = "mike@customer.example";

    // ─────────────────────────────────────────────────────────────────────
    // (a) systemuser branch — AC-1
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_CallerHasSystemUser_ReturnsSystemUserPrincipalWithDerivedContact()
    {
        // Arrange
        var dataverse = new Mock<IDataverseService>();
        SetupSystemUserLookup(dataverse, TestOid, TestSystemUserId);

        var identity = new Mock<IIdentityNormalizationService>();
        identity
            .Setup(x => x.ResolveAsync(TestSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(TestSystemUserId, ContactId: TestContactId));

        var sut = CreateSut(dataverse.Object, identity.Object);

        // Act
        var result = await sut.ResolveAsync(BuildUser(oid: TestOid, tid: TestTenantId), CancellationToken.None);

        // Assert
        result.IsResolved.Should().BeTrue();
        result.Principal!.Kind.Should().Be(WorkforcePrincipalKind.SystemUser);
        result.Principal.SystemUserId.Should().Be(TestSystemUserId);
        result.Principal.ContactId.Should().Be(TestContactId, "the systemuser's contactId is derived");
        result.Principal.TenantId.Should().Be(TestTenantId);

        // Contact-only conversion MUST NOT be consulted once a systemuser is found (no silent double-path).
        identity.Verify(
            x => x.TryResolveContactByWorkforceIdentityAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_SystemUserWithNoLinkedContact_StillReturnsSystemUserPrincipal()
    {
        // Arrange — systemuser found, but derived contact is null (no sprk_primarycontact / no cross-ref).
        var dataverse = new Mock<IDataverseService>();
        SetupSystemUserLookup(dataverse, TestOid, TestSystemUserId);

        var identity = new Mock<IIdentityNormalizationService>();
        identity
            .Setup(x => x.ResolveAsync(TestSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(TestSystemUserId, ContactId: null));

        var sut = CreateSut(dataverse.Object, identity.Object);

        // Act
        var result = await sut.ResolveAsync(BuildUser(oid: TestOid, tid: TestTenantId), CancellationToken.None);

        // Assert — a systemuser with no contact is still a valid systemuser principal (ADR-034 plane).
        result.IsResolved.Should().BeTrue();
        result.Principal!.Kind.Should().Be(WorkforcePrincipalKind.SystemUser);
        result.Principal.SystemUserId.Should().Be(TestSystemUserId);
        result.Principal.ContactId.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────
    // (b) contact-only branch — AC-2
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_NoSystemUserButContactByOid_ReturnsContactOnlyPrincipal()
    {
        // Arrange — no systemuser row; contact resolves (by oid or verified email).
        var dataverse = new Mock<IDataverseService>();
        SetupSystemUserLookup(dataverse, TestOid, systemUserId: null); // 0 rows

        var identity = new Mock<IIdentityNormalizationService>();
        identity
            .Setup(x => x.TryResolveContactByWorkforceIdentityAsync(TestOid, TestEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestContactId);

        var sut = CreateSut(dataverse.Object, identity.Object);

        // Act
        var result = await sut.ResolveAsync(
            BuildUser(oid: TestOid, tid: TestTenantId, email: TestEmail), CancellationToken.None);

        // Assert
        result.IsResolved.Should().BeTrue();
        result.Principal!.Kind.Should().Be(WorkforcePrincipalKind.ContactOnly);
        result.Principal.ContactId.Should().Be(TestContactId);
        result.Principal.SystemUserId.Should().BeNull("a contact-only principal has no systemuser");
        result.Principal.TenantId.Should().Be(TestTenantId);
    }

    [Fact]
    public async Task ResolveAsync_ContactResolver_ReceivesTheVerifiedEmailFromTheToken()
    {
        // Arrange — proves the verified-email fallback is threaded from the token to the resolver.
        var dataverse = new Mock<IDataverseService>();
        SetupSystemUserLookup(dataverse, TestOid, systemUserId: null);

        var identity = new Mock<IIdentityNormalizationService>();
        identity
            .Setup(x => x.TryResolveContactByWorkforceIdentityAsync(TestOid, TestEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestContactId);

        var sut = CreateSut(dataverse.Object, identity.Object);

        // Act — email supplied via preferred_username only (no explicit 'email' claim).
        var result = await sut.ResolveAsync(
            BuildUser(oid: TestOid, tid: TestTenantId, preferredUsername: TestEmail), CancellationToken.None);

        // Assert
        result.Principal!.ContactId.Should().Be(TestContactId);
        identity.Verify(
            x => x.TryResolveContactByWorkforceIdentityAsync(TestOid, TestEmail, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────
    // (c) deny branch — AC-3
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_NeitherSystemUserNorContact_DeniesWithPrincipalNotResolved()
    {
        // Arrange — no systemuser, no contact.
        var dataverse = new Mock<IDataverseService>();
        SetupSystemUserLookup(dataverse, TestOid, systemUserId: null);

        var identity = new Mock<IIdentityNormalizationService>();
        identity
            .Setup(x => x.TryResolveContactByWorkforceIdentityAsync(TestOid, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var sut = CreateSut(dataverse.Object, identity.Object);

        // Act
        var result = await sut.ResolveAsync(
            BuildUser(oid: TestOid, tid: TestTenantId, email: TestEmail), CancellationToken.None);

        // Assert — explicit deny, NOT a resolved-but-unscoped principal.
        result.IsResolved.Should().BeFalse();
        result.Principal.Should().BeNull();
        result.DenyReason.Should().Be(WorkforceDenyReason.PrincipalNotResolved);
        result.DenyCode.Should().Be(WorkforcePrincipalResolver.DenyPrincipalNotResolved);
    }

    [Fact]
    public async Task ResolveAsync_TokenMissingOidClaim_DeniesWithMissingIdentityClaims()
    {
        // Arrange — token with no oid claim at all.
        var dataverse = new Mock<IDataverseService>();
        var identity = new Mock<IIdentityNormalizationService>();
        var sut = CreateSut(dataverse.Object, identity.Object);

        // Act
        var result = await sut.ResolveAsync(BuildUser(oid: null, tid: TestTenantId), CancellationToken.None);

        // Assert
        result.IsResolved.Should().BeFalse();
        result.DenyReason.Should().Be(WorkforceDenyReason.MissingIdentityClaims);
        result.DenyCode.Should().Be(WorkforcePrincipalResolver.DenyMissingIdentityClaims);

        // No Dataverse / identity work is attempted when the caller cannot be identified.
        dataverse.Verify(
            x => x.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static WorkforcePrincipalResolver CreateSut(
        IDataverseService dataverse,
        IIdentityNormalizationService identity)
        => new(
            identity,
            dataverse,
            new FakeTenantCache(),
            NullLogger<WorkforcePrincipalResolver>.Instance);

    private static ClaimsPrincipal BuildUser(
        Guid? oid,
        string? tid = null,
        string? email = null,
        string? preferredUsername = null)
    {
        var claims = new List<Claim>();
        if (oid is { } o) claims.Add(new Claim("oid", o.ToString("D")));
        if (tid is not null) claims.Add(new Claim("tid", tid));
        if (email is not null) claims.Add(new Claim("email", email));
        if (preferredUsername is not null) claims.Add(new Claim("preferred_username", preferredUsername));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestWorkforce"));
    }

    private static void SetupSystemUserLookup(
        Mock<IDataverseService> dataverse,
        Guid oid,
        Guid? systemUserId)
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

    /// <summary>Dictionary-backed <see cref="ITenantCache"/> — resolves systemuser-lookup cache
    /// misses to a live query (returns default Guid on Get).</summary>
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
