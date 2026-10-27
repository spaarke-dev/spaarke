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
using Sprk.Bff.Api.Infrastructure.Authentication;
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
    // R3 FR-15 (#1563) — the customer-tenant guest, refused "use home sign-in" since #2128 (owner 2026-10-20).
    // Stamp-shaped configuration (NFR-07, never dev's):
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
    public async Task ResolveAsync_CustomerTenantGuestFirstSignIn_IsRefusedUseHomeSignIn_AndNothingIsReadOrCreated(
        string idp)
    {
        // #2128 (inverts FR-15's "creates exactly one contact keyed by the guest oid"): the guest is pointed at home
        // sign-in. No contact is created; the binder reads nothing (no oid lookup, no email lookup).
        var result = await ResolveGuestAsync(SpaarkeTenant, idp);

        AssertDeniedWithNoWrites(result, "sdap.access.deny.workforce_guest_use_home_signin");
        _store.Reads.Should().BeEmpty("the refusal comes before every contact read");
    }

    [Fact]
    public async Task ResolveAsync_CustomerTenantGuestWhoseEmailMatchesAContact_IsRefused_AndTheContactStaysUnbound()
    {
        // #2128 (inverts FR-15's "email-binds the one unbound contact").
        _store.AddContact(TestContactId, email: GuestEmail);

        var result = await ResolveGuestAsync(SpaarkeTenant, V1Issuer(CustomerTenant));

        result.IsResolved.Should().BeFalse();
        result.DenyCode.Should().Be(WorkforceMembershipTest.DenyGuestUseHomeSignin);
        _store.Contacts.Should().ContainSingle();
        _store.Contacts[TestContactId].Oid.Should().BeNull("a refused guest is never email-bound");
        _store.Writes.Should().BeEmpty();
        _store.Reads.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ContactAlreadyBoundToTheCustomerTenantGuestOid_IsRefused_NotResolved()
    {
        // #2128 (inverts FR-15's "second sign-in resolves the same contact"): a binding the retired path wrote (dev had
        // three on 2026-10-20) grants nothing any more. The contact is left as it is: nothing is cleared or flagged.
        _store.AddContact(TestContactId, email: GuestEmail, oid: GuestOid.ToString("D"), plane: IdentityPlaneMarker.Workforce);

        var result = await ResolveGuestAsync(SpaarkeTenant, V1Issuer(CustomerTenant));

        result.IsResolved.Should().BeFalse();
        result.Principal.Should().BeNull();
        result.DenyCode.Should().Be(WorkforceMembershipTest.DenyGuestUseHomeSignin);
        _store.Contacts[TestContactId].Oid.Should().Be(GuestOid.ToString("D"), "the stored binding is not modified");
        _store.Writes.Should().BeEmpty();
        _store.Reads.Should().BeEmpty("the oid lookup that would resolve the binding never runs");
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
    public async Task ResolveAsync_CustomerTenantGuestUnderTheMappedIdpClaimName_IsRefusedUseHomeSignIn()
    {
        // MapInboundClaims=true renames idp; the claim is read under both names, like tid — so the mapped form is the
        // same customer-tenant guest and gets the same #2128 refusal (was: admitted, one contact created).
        var claims = new List<Claim>
        {
            new("scp", "user_impersonation"), new("acct", "1"), new("sub", "pairwise"),
            new("oid", GuestOid.ToString("D")), new("tid", SpaarkeTenant.ToString("D")), new("email", GuestEmail),
            new("http://schemas.microsoft.com/identity/claims/identityprovider", V1Issuer(CustomerTenant)),
        };

        var result = await ResolveAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, "TestWorkforce")));

        AssertDeniedWithNoWrites(result, WorkforceMembershipTest.DenyGuestUseHomeSignin);
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
    public async Task ResolveAsync_ADeploymentListingItsOwnTenant_RefusesAGuestWithAListedHomeTenantUseHomeSignIn(
        string homeTenant, bool listedHome)
    {
        // Covers the acct switch's guest arm, reached only when the stamp tenant is ON the list (dev's shape,
        // NFR-07). #2128: a listed home tenant is the customer-tenant guest — refused, pointed at home sign-in (was:
        // admitted). A guest with an unlisted home tenant keeps today's workforce_guest code there.
        var result = await ResolveGuestAsync(SpaarkeTenant, V1Issuer(Guid.Parse(homeTenant)),
            listedTenants: new[] { CustomerTenant, SpaarkeTenant });

        AssertDeniedWithNoWrites(
            result, listedHome ? WorkforceMembershipTest.DenyGuestUseHomeSignin : WorkforceMembershipTest.DenyGuest);
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

    // ─────────────────────────────────────────────────────────────────────
    // R3 task 046 — ADR-028 A9 home principals (authenticated by the WorkforceHome scheme). Design §3/§4/§5:
    // missing oid/tid → 401; stored link (secured pair) → systemuser; WorkforceHome contact by (tid, oid);
    // else 403 registration_required. Nothing is created, updated or bound; the oid → systemuser lookup and the
    // binder's workforce path never run (finding (d)).
    // ─────────────────────────────────────────────────────────────────────

    private static readonly Guid HomeTenant = Guid.Parse("99999999-eeee-4eee-8eee-999999999999");
    private static readonly Guid OtherHomeTenant = Guid.Parse("12121212-ffff-4fff-8fff-121212121212");
    private static readonly Guid HomeOid = Guid.Parse("13131313-1313-4313-8313-131313131313");

    /// <summary>The A9 home-token shape: customer tid, acct = 0, a user token, authenticated by WorkforceHome.</summary>
    private static ClaimsPrincipal HomeUser(Guid? oid = null, Guid? tid = null, bool omitOid = false, bool omitTid = false)
        => HomePrincipal(BuildUser(omitOid ? null : oid ?? HomeOid, omitTid ? null : tid ?? HomeTenant, email: TestEmail));

    private InMemoryContactIdentityStore.Contact AddHomeContact(
        Guid id, Guid? tid, Guid oid, int? plane = ContactBindingDecision.WorkforceHomePlaneValue, int stateCode = 0)
    {
        var c = _store.AddContact(id, oid: oid.ToString("D"), stateCode: stateCode);
        c.Plane = plane;
        c.ExternalTenantId = tid?.ToString("D");
        return c;
    }

    private InMemoryContactIdentityStore.SystemUser AddLinkedSystemUser(
        Guid id, Guid? tid, Guid? oid, bool disabled = false, Guid? aadOid = null)
    {
        var u = _store.AddSystemUser(id, aadOid ?? Guid.NewGuid(), "licensed@customer.example", disabled: disabled);
        u.HomeTenantId = tid?.ToString("D");
        u.HomeObjectId = oid?.ToString("D");
        u.HomeTenantIdKey = u.HomeTenantId;
        u.HomeObjectIdKey = u.HomeObjectId;
        return u;
    }

    private (WorkforcePrincipalResolver Sut, Mock<IDataverseService> Dataverse, Mock<IIdentityNormalizationService> Identity)
        HomeSut(Guid? derivedContact = null)
    {
        var dataverse = new Mock<IDataverseService>();
        var identity = new Mock<IIdentityNormalizationService>();
        identity
            .Setup(x => x.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid suid, CancellationToken _) => new PersonIdentity(suid, ContactId: derivedContact));
        return (CreateSut(dataverse.Object, identity.Object), dataverse, identity);
    }

    /// <summary>AC "no Dataverse write occurs during resolution": nothing written, created, bound, linked or invalidated.</summary>
    private void AssertNothingWritten(Mock<IDataverseService> dataverse, Mock<IIdentityNormalizationService> identity)
    {
        _store.Writes.Should().BeEmpty("nothing is created, updated or bound at sign-in (047/048 do that)");
        dataverse.Invocations.Should().BeEmpty(
            "finding (d): the oid → systemuser lookup (ResolveSystemUserAsync) never runs for a home principal");
        identity.Verify(x => x.InvalidateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Reads.Should().NotContain(new[] { "oid", "email", "references", "intent", "probe", "systemuser" },
            "the binder's workforce path (bind, create, inline link) is never reached");
    }

    [Fact]
    public async Task Home_TwoWorkforceHomeContactsWithTheSameTidAndOid_AreDeniedAmbiguous()
    {
        // UAC review F3: two ACTIVE WorkforceHome contacts for one (tid, oid) must never resolve to either of them.
        AddHomeContact(TestContactId, HomeTenant, HomeOid);
        AddHomeContact(Guid.Parse("55555555-5555-4555-8555-555555555555"), HomeTenant, HomeOid);
        var (sut, dataverse, identity) = HomeSut();

        var result = await sut.ResolveAsync(HomeUser(), CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.Principal.Should().BeNull();
        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactOidAmbiguous);
        AssertNothingWritten(dataverse, identity);
    }

    [Theory]
    [InlineData("no-oid")]
    [InlineData("no-tid")]
    [InlineData("tid-not-a-guid")]
    public async Task Home_MissingOidOrTid_Is401MissingIdentityClaims_BeforeAnyLookup(string shape)
    {
        var (sut, dataverse, identity) = HomeSut();
        var user = shape switch
        {
            "no-oid" => HomeUser(omitOid: true),
            "no-tid" => HomeUser(omitTid: true),
            _ => HomePrincipal(new ClaimsPrincipal(new ClaimsIdentity(
                BuildUser(HomeOid, null, email: TestEmail).Claims.Append(new Claim("tid", "not-a-tenant")), "x"))),
        };

        var result = await sut.ResolveAsync(user, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.DenyReason.Should().Be(WorkforceDenyReason.MissingIdentityClaims);
        result.DenyCode.Should().Be(WorkforcePrincipalResolver.DenyMissingIdentityClaims);
        _store.Reads.Should().BeEmpty();
        AssertNothingWritten(dataverse, identity);
    }

    [Fact]
    public async Task Home_StoredLinkToAnEnabledSystemUser_ResolvesThatSystemUser_WithItsDerivedContact()
    {
        AddLinkedSystemUser(TestSystemUserId, HomeTenant, HomeOid);
        var (sut, dataverse, identity) = HomeSut(derivedContact: TestContactId);

        var result = await sut.ResolveAsync(HomeUser(), CancellationToken.None);

        result.IsResolved.Should().BeTrue(result.DenyCode);
        result.Principal!.Kind.Should().Be(WorkforcePrincipalKind.SystemUser);
        result.Principal.SystemUserId.Should().Be(TestSystemUserId);
        result.Principal.ContactId.Should().Be(TestContactId);
        result.Principal.Oid.Should().Be(HomeOid.ToString("D"));
        result.Principal.TenantId.Should().Be(HomeTenant.ToString("D"));
        _store.Reads.Should().Equal(new[] { "home-link" }, "a linked caller never reaches the contact lookup");
        AssertNothingWritten(dataverse, identity);
    }

    [Fact]
    public async Task Home_StoredLinkToADisabledSystemUser_IsDeniedSystemUserDisabled_AndNeverReachesTheContactLookup()
    {
        AddLinkedSystemUser(TestSystemUserId, HomeTenant, HomeOid, disabled: true);
        AddHomeContact(TestContactId, HomeTenant, HomeOid);
        var (sut, dataverse, identity) = HomeSut();

        var result = await sut.ResolveAsync(HomeUser(), CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.DenyReason.Should().Be(WorkforceDenyReason.PrincipalNotResolved);
        result.DenyCode.Should().Be(WorkforcePrincipalResolver.DenySystemUserDisabled);
        _store.Reads.Should().NotContain("home-contact");
        AssertNothingWritten(dataverse, identity);
    }

    [Fact]
    public async Task Home_WorkforceHomeContactWithTheSameTidAndOid_ResolvesThatContact()
    {
        AddHomeContact(TestContactId, HomeTenant, HomeOid);
        var (sut, dataverse, identity) = HomeSut();

        var result = await sut.ResolveAsync(HomeUser(), CancellationToken.None);

        result.IsResolved.Should().BeTrue(result.DenyCode);
        result.Principal!.Kind.Should().Be(WorkforcePrincipalKind.ContactOnly);
        result.Principal.ContactId.Should().Be(TestContactId);
        result.Principal.SystemUserId.Should().BeNull();
        result.Principal.Oid.Should().Be(HomeOid.ToString("D"));
        result.Principal.TenantId.Should().Be(HomeTenant.ToString("D"));
        _store.Reads.Should().Equal("home-link", "home-contact");
        AssertNothingWritten(dataverse, identity);
    }

    [Fact]
    public async Task Home_ClaimsShapedLikeACustomerTenantGuest_StillResolveTheWorkforceHomeContact_NeverTheGuestRefusal()
    {
        // #2128 pin: the WorkforceHome guard runs first, so the binder's guest refusal can never reach an A9 principal,
        // whatever acct / idp claims it carries.
        AddHomeContact(TestContactId, HomeTenant, HomeOid);
        var (sut, dataverse, identity) = HomeSut();
        var claims = BuildUser(HomeOid, HomeTenant, email: TestEmail, acct: "1", idp: V1Issuer(CustomerTenant)).Claims;

        var result = await sut.ResolveAsync(HomePrincipal(new ClaimsPrincipal(new ClaimsIdentity(claims, "x"))),
            CancellationToken.None);

        result.IsResolved.Should().BeTrue(result.DenyCode);
        result.Principal!.ContactId.Should().Be(TestContactId);
        _store.Reads.Should().Equal("home-link", "home-contact");
        AssertNothingWritten(dataverse, identity);
    }

    [Theory]
    [InlineData("other-tenant")]      // same oid, a WorkforceHome contact of ANOTHER customer directory
    [InlineData("no-tenant")]         // same oid, WorkforceHome plane, no sprk_externaltenantid
    [InlineData("plane-external")]    // same oid and tid, but a CIAM partner contact
    [InlineData("plane-workforce")]   // same oid and tid, but the Spaarke-tenant workforce plane (A8)
    [InlineData("no-plane")]          // same oid and tid, a pre-141 binding with no plane marker
    [InlineData("nothing")]           // no contact and no link at all
    public async Task Home_NoMatchingContact_IsRegistrationRequired_AndNothingIsCreated(string seed)
    {
        switch (seed)
        {
            case "other-tenant": AddHomeContact(TestContactId, OtherHomeTenant, HomeOid); break;
            case "no-tenant": AddHomeContact(TestContactId, null, HomeOid); break;
            case "plane-external": AddHomeContact(TestContactId, HomeTenant, HomeOid, (int)IdentityPlaneMarker.External); break;
            case "plane-workforce": AddHomeContact(TestContactId, HomeTenant, HomeOid, (int)IdentityPlaneMarker.Workforce); break;
            case "no-plane": AddHomeContact(TestContactId, HomeTenant, HomeOid, plane: null); break;
        }

        var (sut, dataverse, identity) = HomeSut();

        var result = await sut.ResolveAsync(HomeUser(), CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.Principal.Should().BeNull();
        result.DenyReason.Should().Be(WorkforceDenyReason.PrincipalNotResolved);
        result.DenyCode.Should().Be(WorkforcePrincipalResolver.DenyRegistrationRequired)
            .And.Be("sdap.access.registration_required");
        AssertNothingWritten(dataverse, identity);
        _store.Contacts.Should().HaveCount(seed == "nothing" ? 0 : 1, "no contact is created at sign-in");
    }

    [Fact]
    public async Task Home_LinkedInAnotherDirectory_IsNotThisCallersLink_IsRegistrationRequired()
    {
        // The same home oid stored with ANOTHER directory's tid: the pair is (tid, oid), never the oid alone.
        AddLinkedSystemUser(TestSystemUserId, OtherHomeTenant, HomeOid);
        var (sut, dataverse, identity) = HomeSut();

        var result = await sut.ResolveAsync(HomeUser(), CancellationToken.None);

        result.DenyCode.Should().Be(WorkforcePrincipalResolver.DenyRegistrationRequired);
        AssertNothingWritten(dataverse, identity);
    }

    [Fact]
    public async Task Home_OidEqualToASystemUsersAadObjectId_WithNoStoredLink_IsRegistrationRequired_NeverThatSystemUser()
    {
        // Finding (d): a systemuser whose azureactivedirectoryobjectid IS the home oid, with no stored link. The Spaarke-tenant
        // lookup would resolve it; a home principal must never reach that lookup.
        _store.AddSystemUser(TestSystemUserId, HomeOid, "someone@spaarke.example");
        var (sut, dataverse, identity) = HomeSut(derivedContact: TestContactId);
        SetupSystemUserLookup(dataverse, HomeOid, TestSystemUserId);

        var result = await sut.ResolveAsync(HomeUser(), CancellationToken.None);

        result.IsResolved.Should().BeFalse("a home oid is never compared with systemuser.azureactivedirectoryobjectid");
        result.Principal.Should().BeNull();
        result.DenyCode.Should().Be(WorkforcePrincipalResolver.DenyRegistrationRequired);
        AssertNothingWritten(dataverse, identity);
    }

    [Fact]
    public async Task Home_AKeyMirrorEdit_NeverRedirectsASignIn()
    {
        // Design §5: someone with systemuser Write sets ANOTHER user's unsecured mirrors to this caller's (tid, oid). The
        // secured pair is untouched (null), so that user is not this caller's link.
        var u = AddLinkedSystemUser(TestSystemUserId, tid: null, oid: null);
        u.HomeTenantIdKey = HomeTenant.ToString("D");
        u.HomeObjectIdKey = HomeOid.ToString("D");
        var (sut, dataverse, identity) = HomeSut(derivedContact: TestContactId);

        var result = await sut.ResolveAsync(HomeUser(), CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.DenyCode.Should().Be(WorkforcePrincipalResolver.DenyRegistrationRequired);
        AssertNothingWritten(dataverse, identity);
    }

    // Beyond the named cases (one-line justifications in notes/task-046-notes.md): the fail-closed arms of the new code.

    [Theory]
    [InlineData("link", "Failed")]
    [InlineData("link", "ColumnMissing")]
    [InlineData("contact", "Failed")]
    [InlineData("contact", "ColumnMissing")]
    public async Task Home_AReadThatFails_Denies_NeverRegistrationRequired(string read, string status)
    {
        var lookupStatus = Enum.Parse<LookupStatus>(status);
        if (read == "link") _store.HomeLinkLookupStatus = lookupStatus;
        else _store.HomeContactLookupStatus = lookupStatus;
        var (sut, dataverse, identity) = HomeSut();

        var result = await sut.ResolveAsync(HomeUser(), CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.DenyCode.Should().Be(lookupStatus == LookupStatus.ColumnMissing
            ? ContactBindingDecision.DenyBindingColumnMissing
            : ContactBindingDecision.DenyContactLookupFailed,
            "an unreadable answer is never 'not registered' — that would send a registered user to the form");
        AssertNothingWritten(dataverse, identity);
    }

    [Fact]
    public async Task Home_TwoEnabledSystemUsersWithTheSameStoredLink_AreDeniedAmbiguous()
    {
        AddLinkedSystemUser(TestSystemUserId, HomeTenant, HomeOid);
        AddLinkedSystemUser(Guid.Parse("44444444-4444-4444-4444-444444444444"), HomeTenant, HomeOid);
        var (sut, dataverse, identity) = HomeSut();

        var result = await sut.ResolveAsync(HomeUser(), CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyHomeLinkAmbiguous);
        AssertNothingWritten(dataverse, identity);
    }

    [Fact]
    public async Task Home_ADisabledAndTwoEnabledSystemUsersWithTheSameStoredLink_AreDeniedAmbiguous_NotResolvedToAnEnabledOne()
    {
        // Seeded disabled-first: without the enabled-first ordering the two-row sample is {disabled, enabled}, which resolves.
        AddLinkedSystemUser(Guid.Parse("33333333-3333-3333-3333-333333333333"), HomeTenant, HomeOid, disabled: true);
        AddLinkedSystemUser(TestSystemUserId, HomeTenant, HomeOid);
        AddLinkedSystemUser(Guid.Parse("44444444-4444-4444-4444-444444444444"), HomeTenant, HomeOid);
        var (sut, dataverse, identity) = HomeSut();

        var result = await sut.ResolveAsync(HomeUser(), CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.DenyCode.Should().Be(ContactBindingDecision.DenyHomeLinkAmbiguous);
        AssertNothingWritten(dataverse, identity);
    }

    [Fact]
    public async Task Home_TheMatchingContactIsInactive_IsDeniedContactInactive_NotRegistrationRequired()
    {
        AddHomeContact(TestContactId, HomeTenant, HomeOid, stateCode: 1);
        var (sut, dataverse, identity) = HomeSut();

        var result = await sut.ResolveAsync(HomeUser(), CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactInactive);
        AssertNothingWritten(dataverse, identity);
    }

    [Fact]
    public async Task Home_AnAppOnlyToken_IsDeniedNotUserToken_BeforeAnyLookup()
    {
        AddLinkedSystemUser(TestSystemUserId, HomeTenant, HomeOid);
        var (sut, dataverse, identity) = HomeSut();

        var result = await sut.ResolveAsync(HomePrincipal(AppOnlyToken(HomeOid, HomeTenant)), CancellationToken.None);

        result.DenyCode.Should().Be(WorkforceMembershipTest.DenyNotUserToken);
        _store.Reads.Should().BeEmpty();
        AssertNothingWritten(dataverse, identity);
    }

    [Fact]
    public async Task ResolveAsync_BearerWorkforcePrincipal_IsUnaffectedByTheWorkforceHomeGuard()
    {
        // Regression control for the guard above: a Bearer (default scheme) workforce principal is unaffected.
        var dataverse = new Mock<IDataverseService>();
        SetupSystemUserLookup(dataverse, TestOid, TestSystemUserId);
        var identity = new Mock<IIdentityNormalizationService>();
        identity
            .Setup(x => x.ResolveAsync(TestSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(TestSystemUserId, ContactId: TestContactId));
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            BuildUser(TestOid, CustomerTenant).Claims, authenticationType: Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme));

        var result = await CreateSut(dataverse.Object, identity.Object).ResolveAsync(user, CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        result.Principal!.SystemUserId.Should().Be(TestSystemUserId);
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
    public async Task ResolveAsync_CustomerTenantGuestRefused_LogsTheGuestDenyLineWithTheParsedHomeTenant()
    {
        // #2128 (was: "registered guest resolved, logs the parsed home tenant"): the refusal reaches the operator's
        // guest diagnostic line with the new code and the home tenant, and no email.
        var log = new BindingLogCapture<WorkforcePrincipalResolver>();

        var result = await ResolveAsync(
            BuildUser(GuestOid, SpaarkeTenant, email: GuestEmail, acct: "1", idp: V1Issuer(CustomerTenant)), logger: log);

        result.DenyCode.Should().Be(WorkforceMembershipTest.DenyGuestUseHomeSignin);
        log.Messages.Should().ContainSingle(m => m.Contains("Guest (acct=1)"))
            .Which.Should().Contain(WorkforceMembershipTest.DenyGuestUseHomeSignin)
            .And.Contain($"idpHomeTenant={CustomerTenant:D}");
        log.Messages.Should().NotContain(m => m.Contains("Resolved workforce caller"));
        log.Messages.Should().NotContain(m => m.Contains(GuestEmail));
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
                .Which.Message.Should().Contain("not workforce_guest_use_home_signin")
                .And.Contain("FR-15 guest admission is retired")
                .And.Contain("acct=1 workforce caller with no enabled systemuser is denied",
                    "an enabled systemuser, a licensed B2B guest included, is admitted before the binder (#2128 round 2)")
                .And.NotContain("every acct=1 workforce caller")
                .And.NotContain("admission disabled", "FR-15 guest admission is retired, not switchable (#2128)");
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
    public async Task ResolveAsync_GuestWithOnlyAnExtUpn_IsRefused_AndTheUpnIsNeverTheTokenEmail()
    {
        // preferred_username / upn of a B2B guest is "name_home.example#EXT#@spaarke.onmicrosoft.com": not the person's
        // address. #2128: no contact is created for the guest at all (was: created WITHOUT an email); the token email
        // still skips the UPN.
        const string ExtUpn = "new.hire_customer.example#EXT#@spaarke.onmicrosoft.com";
        var user = BuildUser(GuestOid, SpaarkeTenant, preferredUsername: ExtUpn, acct: "1", idp: V1Issuer(CustomerTenant));
        ((ClaimsIdentity)user.Identity!).AddClaim(new Claim("upn", ExtUpn));

        var result = await ResolveAsync(user);

        AssertDeniedWithNoWrites(result, WorkforceMembershipTest.DenyGuestUseHomeSignin);
        WorkforcePrincipalResolver.ExtractTokenEmail(user).Should().BeNull();
    }

    [Fact]
    public void TokenEmail_ForAGuestWithARealEmailAndAnExtUpn_IsTheRealEmail()
    {
        // Was ResolveAsync_GuestWithARealEmailAndAnExtUpn_UsesTheRealEmail, which read the email off the contact the
        // retired FR-15 create made (#2128). The claim rule it proved is kept, read directly.
        var user = BuildUser(GuestOid, SpaarkeTenant, email: GuestEmail,
            preferredUsername: "new.hire_customer.example#EXT#@spaarke.onmicrosoft.com", acct: "1",
            idp: V1Issuer(CustomerTenant));

        WorkforceCallerClaims.TokenEmail(user).Should().Be(GuestEmail);
    }

    [Fact]
    public async Task ResolveAsync_GuestWithOnlyAnExtUpn_NeverEmailBindsAnUnboundContactNamedLikeTheUpn()
    {
        // An unbound contact whose address equals the UPN would be a bind target if the UPN were used as the email.
        const string ExtUpn = "new.hire_customer.example#EXT#@spaarke.onmicrosoft.com";
        _store.AddContact(TestContactId, email: ExtUpn);

        var result = await ResolveAsync(
            BuildUser(GuestOid, SpaarkeTenant, preferredUsername: ExtUpn, acct: "1", idp: V1Issuer(CustomerTenant)));

        result.DenyCode.Should().Be(WorkforceMembershipTest.DenyGuestUseHomeSignin);
        _store.Contacts[TestContactId].Oid.Should().BeNull("the UPN was not used as the bind email");
        _store.Writes.Should().BeEmpty();
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

    /// <summary>The same claims, authenticated by the WorkforceHome scheme (the handler stamps this AuthenticationType).</summary>
    private static ClaimsPrincipal HomePrincipal(ClaimsPrincipal source)
        => new(new ClaimsIdentity(source.Claims, authenticationType: AuthSchemes.WorkforceHome));

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
