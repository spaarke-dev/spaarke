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
    // R3 FR-15 (#1563) — registered workforce-contact guest. Stamp-shaped configuration (NFR-07, never dev's):
    // CustomerTenantIds = { CustomerTenant }, stamp tenant (AzureAd:TenantId) = SpaarkeTenant, which the list
    // does NOT hold. Live guest token shape (2026-10-08): tid = Spaarke, acct = 1, idp = home-tenant issuer.
    // ─────────────────────────────────────────────────────────────────────

    private static readonly Guid GuestOid = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid UnlistedTenant = Guid.Parse("77777777-cccc-4ccc-8ccc-777777777777");
    private static readonly Guid CiamTenant = Guid.Parse("88888888-dddd-4ddd-8ddd-888888888888");
    private const string GuestEmail = "new.hire@customer.example";

    private static string V1Issuer(Guid tenant) => $"https://sts.windows.net/{tenant:D}/";

    [Theory]
    [InlineData("https://sts.windows.net/11111111-aaaa-4aaa-8aaa-111111111111/")]          // v1 issuer (live shape)
    [InlineData("https://login.microsoftonline.com/11111111-aaaa-4aaa-8aaa-111111111111/v2.0")] // v2 issuer
    [InlineData("11111111-aaaa-4aaa-8aaa-111111111111")]                                     // bare tenant id
    public async Task ResolveAsync_RegisteredGuestFirstSignIn_IsAMemberAndCreatesExactlyOneContactKeyedByTheGuestOid(
        string idp)
    {
        var result = await ResolveGuestAsync(SpaarkeTenant, idp);

        result.IsResolved.Should().BeTrue(result.DenyCode);
        result.Principal!.Kind.Should().Be(WorkforcePrincipalKind.ContactOnly);
        var created = _store.Contacts.Values.Should().ContainSingle().Subject;
        result.Principal.ContactId.Should().Be(created.Id);
        created.Oid.Should().Be(GuestOid.ToString("D"), "the contact is keyed by the guest oid, never client input");
        created.Plane.Should().Be((int)IdentityPlaneMarker.Workforce);
        created.Email.Should().Be(GuestEmail);
        _store.Writes.Should().ContainSingle(w => w.Op == "create");
    }

    [Fact]
    public async Task ResolveAsync_RegisteredGuestFirstSignIn_EmailBindsTheOneUnboundContactAndCreatesNone()
    {
        _store.AddContact(TestContactId, email: GuestEmail);

        var result = await ResolveGuestAsync(SpaarkeTenant, V1Issuer(CustomerTenant));

        result.Principal!.ContactId.Should().Be(TestContactId);
        _store.Contacts.Should().ContainSingle();
        _store.Contacts[TestContactId].Oid.Should().Be(GuestOid.ToString("D"));
        _store.Writes.Should().NotContain(w => w.Op == "create");
    }

    [Fact]
    public async Task ResolveAsync_RegisteredGuestSecondSignIn_ResolvesTheSameContactAndCreatesNone()
    {
        var first = await ResolveGuestAsync(SpaarkeTenant, V1Issuer(CustomerTenant));
        var writesAfterFirst = _store.Writes.Count;

        var second = await ResolveGuestAsync(SpaarkeTenant, V1Issuer(CustomerTenant));

        second.IsResolved.Should().BeTrue(second.DenyCode);
        second.Principal!.ContactId.Should().Be(first.Principal!.ContactId);
        _store.Contacts.Should().ContainSingle("a second sign-in never creates a second contact");
        _store.Writes.Should().HaveCount(writesAfterFirst, "the second sign-in resolves by oid and writes nothing");
    }

    [Theory]
    [InlineData("77777777-cccc-4ccc-8ccc-777777777777")] // a home tenant that is not on the list
    [InlineData("88888888-dddd-4ddd-8ddd-888888888888")] // the CIAM tenant (startup validation keeps it off the list)
    [InlineData("22222222-bbbb-4bbb-8bbb-222222222222")] // the stamp tenant itself (an internal guest of Spaarke)
    public async Task ResolveAsync_GuestWhoseHomeTenantIsNotListed_IsDeniedTenantNotCustomer(string homeTenant)
    {
        var result = await ResolveGuestAsync(SpaarkeTenant, V1Issuer(Guid.Parse(homeTenant)));

        AssertDeniedWithNoWrites(result, WorkforceMembershipTest.DenyForeignTenant);
    }

    [Theory]
    [InlineData(null)]                                                                        // no idp claim
    [InlineData("")]
    [InlineData("live.com")]                                                                  // MSA
    [InlineData("mail")]                                                                      // email one-time passcode
    [InlineData("google.com")]
    [InlineData("https://sts.windows.net/not-a-tenant/")]
    [InlineData("https://sts.windows.net/00000000-0000-0000-0000-000000000000/")]
    [InlineData("http://sts.windows.net/11111111-aaaa-4aaa-8aaa-111111111111/")]              // not https
    [InlineData("https://sts.windows.net:8443/11111111-aaaa-4aaa-8aaa-111111111111/")]        // a port
    [InlineData("https://evil.example/11111111-aaaa-4aaa-8aaa-111111111111/")]                // another host
    [InlineData("https://sts.windows.net.evil.example/11111111-aaaa-4aaa-8aaa-111111111111/")]
    [InlineData("https://user@sts.windows.net/11111111-aaaa-4aaa-8aaa-111111111111/")]        // user info
    [InlineData("https://sts.windows.net/11111111-aaaa-4aaa-8aaa-111111111111/?x=1")]         // a query
    [InlineData("https://sts.windows.net/11111111-aaaa-4aaa-8aaa-111111111111/extra")]        // an extra segment
    [InlineData("https://sts.windows.net//11111111-aaaa-4aaa-8aaa-111111111111/")]            // an empty segment
    [InlineData("https://sts.windows.net/x/11111111-aaaa-4aaa-8aaa-111111111111/")]
    [InlineData("https://login.microsoftonline.com/11111111-aaaa-4aaa-8aaa-111111111111/")]   // v2 host, no /v2.0
    [InlineData("https://login.microsoftonline.com/11111111-aaaa-4aaa-8aaa-111111111111/v1.0")]
    [InlineData("https://sts.windows.net/11111111-aaaa-4aaa-8aaa-111111111111/v2.0")]         // v1 host, v2 path
    [InlineData("{11111111-aaaa-4aaa-8aaa-111111111111}")]                                    // non-D GUID format
    [InlineData("11111111aaaa4aaa8aaa111111111111")]
    public async Task ResolveAsync_GuestWithAMissingOrUnparsableIdp_IsDenied(string? idp)
    {
        var result = await ResolveGuestAsync(SpaarkeTenant, idp);

        AssertDeniedWithNoWrites(result, WorkforceMembershipTest.DenyForeignTenant);
    }

    [Fact]
    public async Task ResolveAsync_GuestWithTwoDifferentIdpClaims_IsDenied()
    {
        // Ambiguity is never resolved by picking one: the short and the mapped claim name disagree.
        var user = BuildUser(GuestOid, SpaarkeTenant, email: GuestEmail, acct: "1", idp: V1Issuer(CustomerTenant));
        ((ClaimsIdentity)user.Identity!).AddClaim(new Claim(
            "http://schemas.microsoft.com/identity/claims/identityprovider", V1Issuer(UnlistedTenant)));

        var result = await ResolveAsync(user);

        AssertDeniedWithNoWrites(result, WorkforceMembershipTest.DenyForeignTenant);
    }

    [Fact]
    public async Task ResolveAsync_RegisteredGuestUnderTheMappedIdpClaimName_IsAMember()
    {
        // MapInboundClaims=true renames idp; the claim is read under both names, like tid.
        var claims = new List<Claim>
        {
            new("scp", "user_impersonation"), new("acct", "1"), new("sub", "pairwise"),
            new("oid", GuestOid.ToString("D")), new("tid", SpaarkeTenant.ToString("D")), new("email", GuestEmail),
            new("http://schemas.microsoft.com/identity/claims/identityprovider", V1Issuer(CustomerTenant)),
        };

        var result = await ResolveAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, "TestWorkforce")));

        result.IsResolved.Should().BeTrue(result.DenyCode);
        _store.Contacts.Should().ContainSingle();
    }

    [Theory]
    [InlineData("77777777-cccc-4ccc-8ccc-777777777777")] // a guest of another tenant
    [InlineData("88888888-dddd-4ddd-8ddd-888888888888")] // the CIAM tenant
    public async Task ResolveAsync_GuestWhoseTidIsNotTheStampTenant_IsDeniedAsBefore(string tid)
    {
        var result = await ResolveGuestAsync(Guid.Parse(tid), V1Issuer(CustomerTenant));

        AssertDeniedWithNoWrites(result, WorkforceMembershipTest.DenyForeignTenant);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-tenant")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task ResolveAsync_StampTenantNotConfigured_GuestIsDenied(string? stampTenant)
    {
        var result = await ResolveGuestAsync(SpaarkeTenant, V1Issuer(CustomerTenant), stampTenant: stampTenant);

        AssertDeniedWithNoWrites(result, WorkforceMembershipTest.DenyForeignTenant);
    }

    [Theory]
    [InlineData("0")]  // Spaarke staff: a member of the stamp tenant
    [InlineData(null)] // acct missing
    [InlineData("2")]  // acct unrecognized
    public async Task ResolveAsync_NonGuestInTheStampTenant_ResolvesExactlyAsBefore_EvenWithAListedIdp(string? acct)
    {
        // Spaarke staff with no systemuser: tid = Spaarke is not a customer tenant → denied, nothing created —
        // the guest branch requires acct = 1, so a listed idp changes nothing.
        var result = await ResolveGuestAsync(SpaarkeTenant, V1Issuer(CustomerTenant), acct: acct);

        AssertDeniedWithNoWrites(result, WorkforceMembershipTest.DenyForeignTenant);
    }

    [Fact]
    public async Task ResolveAsync_SpaarkeStaffWithASystemUser_ResolveAsASystemUserAndNeverReachTheBinding()
    {
        // Licensed staff (and licensed U2 guests) resolve through the systemuser branch BEFORE the member test.
        var dataverse = new Mock<IDataverseService>();
        SetupSystemUserLookup(dataverse, GuestOid, TestSystemUserId);
        var identity = new Mock<IIdentityNormalizationService>();
        identity
            .Setup(x => x.ResolveAsync(TestSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(TestSystemUserId, ContactId: TestContactId));

        var result = await CreateSut(dataverse.Object, identity.Object).ResolveAsync(
            BuildUser(GuestOid, SpaarkeTenant, email: GuestEmail, acct: "1", idp: V1Issuer(CustomerTenant)),
            CancellationToken.None);

        result.Principal!.Kind.Should().Be(WorkforcePrincipalKind.SystemUser);
        result.Principal.SystemUserId.Should().Be(TestSystemUserId);
        _store.Reads.Should().BeEmpty();
        _store.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("11111111-aaaa-4aaa-8aaa-111111111111", true)]
    [InlineData("77777777-cccc-4ccc-8ccc-777777777777", false)]
    public async Task ResolveAsync_ADeploymentListingItsOwnTenant_AdmitsOnlyAGuestWithAListedHomeTenant(
        string homeTenant, bool admitted)
    {
        // Covers the acct switch's guest arm, reached only when the stamp tenant is ON the list (dev's shape,
        // NFR-07). A guest with an unlisted home tenant keeps today's workforce_guest code there.
        var result = await ResolveGuestAsync(SpaarkeTenant, V1Issuer(Guid.Parse(homeTenant)),
            listedTenants: new[] { CustomerTenant, SpaarkeTenant });

        if (admitted)
        {
            result.IsResolved.Should().BeTrue(result.DenyCode);
            _store.Contacts.Should().ContainSingle();
        }
        else
        {
            AssertDeniedWithNoWrites(result, WorkforceMembershipTest.DenyGuest);
        }
    }

    private Task<WorkforcePrincipalResolution> ResolveGuestAsync(
        Guid tid, string? idp, string? acct = "1", string? stampTenant = "22222222-bbbb-4bbb-8bbb-222222222222",
        Guid[]? listedTenants = null)
        => ResolveAsync(BuildUser(GuestOid, tid, email: GuestEmail, acct: acct, idp: idp), stampTenant, listedTenants);

    /// <summary>No systemuser for the caller: the contact-only branch runs (the path FR-15 changes).</summary>
    private Task<WorkforcePrincipalResolution> ResolveAsync(
        ClaimsPrincipal user, string? stampTenant = "22222222-bbbb-4bbb-8bbb-222222222222", Guid[]? listedTenants = null)
    {
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        return CreateSut(dataverse.Object, new Mock<IIdentityNormalizationService>().Object, stampTenant, listedTenants)
            .ResolveAsync(user, CancellationToken.None);
    }

    private void AssertDeniedWithNoWrites(WorkforcePrincipalResolution result, string denyCode)
    {
        result.IsResolved.Should().BeFalse();
        result.Principal.Should().BeNull();
        result.DenyReason.Should().Be(WorkforceDenyReason.PrincipalNotResolved);
        result.DenyCode.Should().Be(denyCode);
        _store.Writes.Should().BeEmpty("a denied guest is never bound, created or flagged");
        _store.Contacts.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <remarks>
    /// Link writes ON, so the inline link runs (it is gated on the rollout switch — verifier finding 4). Stamp-shaped
    /// by default (R3 NFR-07): the stamp tenant is Spaarke's and the customer list holds only the customer tenant.
    /// </remarks>
    private WorkforcePrincipalResolver CreateSut(
        IDataverseService dataverse,
        IIdentityNormalizationService identity,
        string? stampTenant = "22222222-bbbb-4bbb-8bbb-222222222222",
        Guid[]? listedTenants = null)
        => new(identity, dataverse, new FakeTenantCache(), Binder(_store, null, listedTenants ?? new[] { CustomerTenant }),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ContactIdentityBinder.LinkWritesEnabledConfigKey] = "true",
                ["AzureAd:TenantId"] = stampTenant,
            }).Build(),
            NullLogger<WorkforcePrincipalResolver>.Instance);

    private static ClaimsPrincipal BuildUser(
        Guid? oid, Guid? tid = null, string? email = null, string? preferredUsername = null, string? acct = "0",
        string? idp = null)
    {
        var claims = new List<Claim> { new("scp", "user_impersonation"), new("sub", "pairwise") };
        if (acct is not null) claims.Add(new Claim("acct", acct));
        if (oid is { } o) claims.Add(new Claim("oid", o.ToString("D")));
        if (tid is { } t) claims.Add(new Claim("tid", t.ToString("D")));
        if (email is not null) claims.Add(new Claim("email", email));
        if (preferredUsername is not null) claims.Add(new Claim("preferred_username", preferredUsername));
        if (idp is not null) claims.Add(new Claim("idp", idp));
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
