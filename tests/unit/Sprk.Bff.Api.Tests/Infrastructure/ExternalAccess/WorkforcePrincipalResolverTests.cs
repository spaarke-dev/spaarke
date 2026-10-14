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
using Microsoft.Extensions.Logging;
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

    // ─────────────────────────────────────────────────────────────────────
    // R3 task 012 review round — disabled systemuser, app-only hardening, diagnostics, #EXT# email.
    // ─────────────────────────────────────────────────────────────────────

    private const string Stamp = "22222222-bbbb-4bbb-8bbb-222222222222";

    /// <summary>
    /// Dataverse as it behaves for a DISABLED systemuser: the one lookup by oid returns the row with
    /// <c>isdisabled = true</c>.
    /// </summary>
    private static Mock<IDataverseService> DisabledSystemUserDataverse(Guid oid)
        => SystemUserRowsDataverse(oid, (TestSystemUserId, true));

    /// <summary>Dataverse returning exactly the supplied systemuser rows (id, isdisabled) for the oid lookup.</summary>
    private static Mock<IDataverseService> SystemUserRowsDataverse(Guid oid, params (Guid Id, bool Disabled)[] rows)
    {
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                var result = new EntityCollection();
                var byOid = q.Criteria.Conditions.Any(c =>
                    c.AttributeName == "azureactivedirectoryobjectid" && c.Values.Count == 1 && Equals(c.Values[0], oid));
                if (q.EntityName == "systemuser" && byOid)
                {
                    foreach (var (id, disabled) in rows.Take(q.TopCount ?? int.MaxValue))
                    {
                        result.Entities.Add(new Entity("systemuser") { Id = id, ["isdisabled"] = disabled });
                    }
                }

                return result;
            });
        return dataverse;
    }

    [Theory]
    [InlineData("1", true)]  // a licensed B2B guest whose home tenant FR-15 admits: the escalation this closes
    [InlineData("0", false)] // the pre-existing Model-2 analogue: a disabled systemuser with a member token
    public async Task ResolveAsync_DisabledSystemUser_IsDeniedAndNeverReachesTheBinder(string acct, bool guest)
    {
        var dataverse = DisabledSystemUserDataverse(GuestOid);
        var user = guest
            ? BuildUser(GuestOid, SpaarkeTenant, email: GuestEmail, acct: acct, idp: V1Issuer(CustomerTenant))
            : BuildUser(GuestOid, CustomerTenant, email: GuestEmail, acct: acct);

        var result = await CreateSut(dataverse.Object, new Mock<IIdentityNormalizationService>().Object)
            .ResolveAsync(user, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.Principal.Should().BeNull();
        result.DenyReason.Should().Be(WorkforceDenyReason.PrincipalNotResolved);
        result.DenyCode.Should().Be(WorkforcePrincipalResolver.DenySystemUserDisabled)
            .And.Be("sdap.access.deny.systemuser_disabled");
        _store.Reads.Should().BeEmpty("the binder is never called for a disabled systemuser");
        _store.Writes.Should().BeEmpty();
        _store.Contacts.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_SystemUserLookupThrows_DeniesFailClosed()
    {
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("dataverse down"));

        var result = await CreateSut(dataverse.Object, new Mock<IIdentityNormalizationService>().Object)
            .ResolveAsync(BuildUser(GuestOid, SpaarkeTenant, email: GuestEmail, acct: "1", idp: V1Issuer(CustomerTenant)),
                CancellationToken.None);

        AssertDeniedWithNoWrites(result, WorkforcePrincipalResolver.DenyPrincipalNotResolved);
        _store.Reads.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_LookupCancelled_PropagatesAndIsNotADeny()
    {
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => CreateSut(dataverse.Object, new Mock<IIdentityNormalizationService>().Object)
            .ResolveAsync(BuildUser(TestOid, CustomerTenant, email: TestEmail), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ResolveAsync_ContactOnlyCaller_IssuesExactlyOneSystemUserQueryPerUncachedResolve()
    {
        // R3 task 012 re-check K2: the enabled-only lookup plus a separate existence check cost a contact-only
        // caller two uncached systemuser reads per request. One query by oid (no isdisabled filter) answers both.
        _store.AddContact(TestContactId, oid: TestOid.ToString("D"), plane: IdentityPlaneMarker.Workforce);
        var dataverse = new Mock<IDataverseService>();
        var queries = new List<QueryExpression>();
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback((QueryExpression q, CancellationToken _) => queries.Add(q))
            .ReturnsAsync(new EntityCollection());
        var sut = CreateSut(dataverse.Object, new Mock<IIdentityNormalizationService>().Object);
        var user = BuildUser(TestOid, CustomerTenant, email: TestEmail, acct: "0");

        var first = await sut.ResolveAsync(user, CancellationToken.None);
        first.IsResolved.Should().BeTrue("the oid-bound contact resolves through the binder");
        first.Principal!.Kind.Should().Be(WorkforcePrincipalKind.ContactOnly);
        queries.Should().ContainSingle(q => q.EntityName == "systemuser");

        queries.Clear();
        await sut.ResolveAsync(user, CancellationToken.None);
        queries.Should().ContainSingle(q => q.EntityName == "systemuser",
            "a null result is never cached, so each uncached resolve is exactly one read - not two");
        queries[0].Criteria.Conditions.Should().NotContain(c => c.AttributeName == "isdisabled",
            "the one query must see disabled rows too");
        queries[0].ColumnSet.Columns.Should().Contain("isdisabled");
        queries[0].NoLock.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_EnabledAndDisabledDuplicateRows_ResolvesTheEnabledOne_InEitherRowOrder()
    {
        var enabledId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var disabledId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        foreach (var rows in new[]
                 {
                     new[] { (disabledId, true), (enabledId, false) },
                     new[] { (enabledId, false), (disabledId, true) },
                 })
        {
            var dataverse = SystemUserRowsDataverse(TestOid, rows);
            var identity = new Mock<IIdentityNormalizationService>();
            identity
                .Setup(x => x.ResolveAsync(enabledId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PersonIdentity(enabledId, ContactId: TestContactId));

            var result = await CreateSut(dataverse.Object, identity.Object)
                .ResolveAsync(BuildUser(TestOid, CustomerTenant, email: TestEmail), CancellationToken.None);

            result.IsResolved.Should().BeTrue();
            result.Principal!.Kind.Should().Be(WorkforcePrincipalKind.SystemUser);
            result.Principal.SystemUserId.Should().Be(enabledId, "an enabled row wins over a disabled duplicate");
            _store.Reads.Should().BeEmpty("a systemuser never reaches the contact binder");
        }
    }

    [Fact]
    public async Task ResolveAsync_OnlyDisabledRows_DeniesSystemUserDisabledAndNeverCallsTheBinder()
    {
        var dataverse = SystemUserRowsDataverse(
            GuestOid,
            (Guid.Parse("33333333-3333-3333-3333-333333333333"), true),
            (Guid.Parse("44444444-4444-4444-4444-444444444444"), true));

        var result = await CreateSut(dataverse.Object, new Mock<IIdentityNormalizationService>().Object)
            .ResolveAsync(BuildUser(GuestOid, CustomerTenant, email: GuestEmail, acct: "0"), CancellationToken.None);

        AssertDeniedWithNoWrites(result, WorkforcePrincipalResolver.DenySystemUserDisabled);
        _store.Reads.Should().BeEmpty("the binder is never called when only disabled rows exist");
        dataverse.Verify(
            x => x.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_AppOnlyTokenWhoseOidIsAnEnabledSystemUser_IsDeniedNotUserTokenWithNoLookups()
    {
        // A Dataverse application user is a systemuser row keyed by the app's service-principal oid; an app-only
        // token carries that oid. It must be refused BEFORE the systemuser lookup can resolve it as a person.
        var dataverse = new Mock<IDataverseService>();
        SetupSystemUserLookup(dataverse, TestOid, TestSystemUserId);
        var identity = new Mock<IIdentityNormalizationService>();

        var result = await CreateSut(dataverse.Object, identity.Object)
            .ResolveAsync(AppOnlyToken(TestOid, CustomerTenant), CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.DenyReason.Should().Be(WorkforceDenyReason.PrincipalNotResolved);
        result.DenyCode.Should().Be(WorkforceMembershipTest.DenyNotUserToken);
        dataverse.Verify(
            x => x.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
        identity.Verify(x => x.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Reads.Should().BeEmpty();
        _store.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("1", Stamp)]                                    // acct=1, tid=stamp, listed idp: the FR-15 shape, app-only
    [InlineData("0", "11111111-aaaa-4aaa-8aaa-111111111111")]  // acct=0 in the listed customer tenant, app-only
    public async Task ResolveAsync_AppOnlyTokenWithTheMemberOrGuestShape_IsDeniedNotUserTokenWithNoWrites(
        string acct, string tid)
    {
        var token = AppOnlyToken(GuestOid, Guid.Parse(tid), acct);
        ((ClaimsIdentity)token.Identity!).AddClaim(new Claim("idp", V1Issuer(CustomerTenant)));
        ((ClaimsIdentity)token.Identity!).AddClaim(new Claim("email", GuestEmail));

        var result = await ResolveAsync(token);

        AssertDeniedWithNoWrites(result, WorkforceMembershipTest.DenyNotUserToken);
        _store.Reads.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_GuestDenied_LogsOneWarningWithTheReasonStampParseAndIdpHost_AndNoOtherClaimValues()
    {
        var log = new BindingLogCapture<WorkforcePrincipalResolver>();

        var result = await ResolveAsync(
            BuildUser(GuestOid, SpaarkeTenant, email: GuestEmail, acct: "1", idp: V1Issuer(UnlistedTenant)), logger: log);

        result.DenyCode.Should().Be(WorkforceMembershipTest.DenyForeignTenant);
        var line = log.Entries.Should().ContainSingle(e => e.Message.Contains("Guest (acct=1)")).Subject;
        line.Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning);
        line.Message.Should().Contain(WorkforceMembershipTest.DenyForeignTenant)
            .And.Contain("stampTenantParsed=True")
            .And.Contain("idpHost=sts.windows.net")
            .And.Contain($"idpHomeTenant={UnlistedTenant:D}");
        log.Messages.Should().NotContain(m => m.Contains(GuestEmail), "no email, token or other claim value is logged");
    }

    [Fact]
    public async Task ResolveAsync_GuestDeniedWithUnparsableIdpAndNoStampTenant_LogsTheTwoFactsAndNoRawIdp()
    {
        var log = new BindingLogCapture<WorkforcePrincipalResolver>();

        await ResolveAsync(
            BuildUser(GuestOid, SpaarkeTenant, email: GuestEmail, acct: "1", idp: "https://evil.example/x"),
            stampTenant: "not-a-guid", logger: log);

        var line = log.Messages.Should().ContainSingle(m => m.Contains("Guest (acct=1)")).Subject;
        line.Should().Contain("stampTenantParsed=False").And.Contain("idpHost=unparsed")
            .And.Contain("idpHomeTenant=unparsed").And.NotContain("evil.example");
    }

    [Fact]
    public async Task ResolveAsync_NonGuestDeny_DoesNotWriteTheGuestDiagnosticLine()
    {
        var log = new BindingLogCapture<WorkforcePrincipalResolver>();

        await ResolveAsync(BuildUser(GuestOid, SpaarkeTenant, email: GuestEmail, acct: "0"), logger: log);

        log.Messages.Should().NotContain(m => m.Contains("Guest (acct=1)"));
    }

    [Fact]
    public async Task ResolveAsync_RegisteredGuestResolved_LogsTheParsedHomeTenant()
    {
        var log = new BindingLogCapture<WorkforcePrincipalResolver>();

        var result = await ResolveAsync(
            BuildUser(GuestOid, SpaarkeTenant, email: GuestEmail, acct: "1", idp: V1Issuer(CustomerTenant)), logger: log);

        result.IsResolved.Should().BeTrue(result.DenyCode);
        log.Messages.Should().Contain(m => m.Contains("Resolved workforce caller") && m.Contains($"homeTenant: {CustomerTenant:D}"));
    }

    [Fact]
    public void Validator_StampTenantNotANonZeroGuid_LogsOneStartupWarning_AndStartupIsNotFailed()
    {
        foreach (var value in new string?[] { null, "", "not-a-guid", "00000000-0000-0000-0000-000000000000" })
        {
            var log = new BindingLogCapture<WorkforceIdentityOptionsValidator>();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AzureAd:TenantId"] = value }).Build();

            var validator = new WorkforceIdentityOptionsValidator(config, log);

            log.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning)
                .Which.Message.Should().Contain("FR-15 registered-guest admission disabled");
            validator.Validate(null, new WorkforceIdentityOptions()).Succeeded
                .Should().BeTrue("a missing stamp tenant must never fail startup");
        }
    }

    [Fact]
    public void Validator_StampTenantIsAGuid_LogsNothing()
    {
        var log = new BindingLogCapture<WorkforceIdentityOptionsValidator>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AzureAd:TenantId"] = Stamp }).Build();

        _ = new WorkforceIdentityOptionsValidator(config, log);

        log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_GuestWithOnlyAnExtUpn_CreatesTheContactWithoutAnEmail()
    {
        // preferred_username / upn of a B2B guest is "name_home.example#EXT#@spaarke.onmicrosoft.com": not the person's
        // address. With no real email claim the contact is created WITHOUT one, never with the UPN.
        const string ExtUpn = "new.hire_customer.example#EXT#@spaarke.onmicrosoft.com";
        var user = BuildUser(GuestOid, SpaarkeTenant, preferredUsername: ExtUpn, acct: "1", idp: V1Issuer(CustomerTenant));
        ((ClaimsIdentity)user.Identity!).AddClaim(new Claim("upn", ExtUpn));

        var result = await ResolveAsync(user);

        result.IsResolved.Should().BeTrue(result.DenyCode);
        var created = _store.Contacts.Values.Should().ContainSingle().Subject;
        created.Email.Should().BeNull();
        WorkforcePrincipalResolver.ExtractTokenEmail(user).Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_GuestWithARealEmailAndAnExtUpn_UsesTheRealEmail()
    {
        var user = BuildUser(GuestOid, SpaarkeTenant, email: GuestEmail,
            preferredUsername: "new.hire_customer.example#EXT#@spaarke.onmicrosoft.com", acct: "1",
            idp: V1Issuer(CustomerTenant));

        var result = await ResolveAsync(user);

        result.IsResolved.Should().BeTrue(result.DenyCode);
        _store.Contacts.Values.Should().ContainSingle().Which.Email.Should().Be(GuestEmail);
    }

    [Fact]
    public async Task ResolveAsync_GuestWithOnlyAnExtUpn_NeverEmailBindsAnUnboundContactNamedLikeTheUpn()
    {
        // An unbound contact whose address equals the UPN would be a bind target if the UPN were used as the email.
        const string ExtUpn = "new.hire_customer.example#EXT#@spaarke.onmicrosoft.com";
        _store.AddContact(TestContactId, email: ExtUpn);

        var result = await ResolveAsync(
            BuildUser(GuestOid, SpaarkeTenant, preferredUsername: ExtUpn, acct: "1", idp: V1Issuer(CustomerTenant)));

        result.Principal!.ContactId.Should().NotBe(TestContactId);
        _store.Contacts[TestContactId].Oid.Should().BeNull("the UPN was not used as the bind email");
    }

    [Fact]
    public void TokenEmail_ForAMember_IsUnchanged_EvenWhenItContainsTheExtMarker()
    {
        const string Upn = "someone#EXT#@customer.example";

        WorkforceCallerClaims.TokenEmail(BuildUser(TestOid, CustomerTenant, preferredUsername: Upn, acct: "0"))
            .Should().Be(Upn, "only acct = 1 tokens skip the #EXT# UPN");
    }

    [Fact]
    public void TokenEmail_ReadsTheMappedUpnClaim_WhenNoOtherEmailClaimIsPresent()
    {
        // A v1 token validated with MapInboundClaims = true carries "upn" as ClaimTypes.Upn (#1798).
        const string Upn = "member@customer.example";
        var user = BuildUser(TestOid, CustomerTenant, acct: "0");
        ((ClaimsIdentity)user.Identity!).AddClaim(new Claim(System.Security.Claims.ClaimTypes.Upn, Upn));

        WorkforceCallerClaims.TokenEmail(user).Should().Be(Upn);
    }

    [Fact]
    public void TokenEmail_ForAGuest_SkipsAMappedExtUpn()
    {
        var user = BuildUser(GuestOid, SpaarkeTenant, acct: "1", idp: V1Issuer(CustomerTenant));
        ((ClaimsIdentity)user.Identity!).AddClaim(
            new Claim(System.Security.Claims.ClaimTypes.Upn, "new.hire_customer.example#EXT#@spaarke.onmicrosoft.com"));

        WorkforceCallerClaims.TokenEmail(user).Should().BeNull();
    }

    private Task<WorkforcePrincipalResolution> ResolveGuestAsync(
        Guid tid, string? idp, string? acct = "1", string? stampTenant = "22222222-bbbb-4bbb-8bbb-222222222222",
        Guid[]? listedTenants = null)
        => ResolveAsync(BuildUser(GuestOid, tid, email: GuestEmail, acct: acct, idp: idp), stampTenant, listedTenants);

    /// <summary>No systemuser for the caller: the contact-only branch runs (the path FR-15 changes).</summary>
    private Task<WorkforcePrincipalResolution> ResolveAsync(
        ClaimsPrincipal user, string? stampTenant = "22222222-bbbb-4bbb-8bbb-222222222222", Guid[]? listedTenants = null,
        ILogger<WorkforcePrincipalResolver>? logger = null)
    {
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        return CreateSut(dataverse.Object, new Mock<IIdentityNormalizationService>().Object, stampTenant, listedTenants, logger)
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
        Guid[]? listedTenants = null,
        ILogger<WorkforcePrincipalResolver>? logger = null)
        => new(identity, dataverse, new FakeTenantCache(), Binder(_store, null, listedTenants ?? new[] { CustomerTenant }),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ContactIdentityBinder.LinkWritesEnabledConfigKey] = "true",
                ["AzureAd:TenantId"] = stampTenant,
            }).Build(),
            logger ?? NullLogger<WorkforcePrincipalResolver>.Instance);

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
            collection.Entities.Add(new Entity("systemuser") { Id = suid, ["isdisabled"] = false });
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
