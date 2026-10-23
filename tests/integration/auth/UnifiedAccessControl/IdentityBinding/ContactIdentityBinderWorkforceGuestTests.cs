using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.IdentityBindingTestKit;

namespace Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

/// <summary>
/// #2128 (owner 2026-10-20, option 1) — the FR-15 guest auto-create is retired. A customer-tenant guest on the workforce
/// token plane (<c>acct = 1</c>, <c>tid</c> = the stamp tenant, home tenant from <c>idp</c> on the customer list) is refused
/// <c>workforce_guest_use_home_signin</c> by <see cref="ContactIdentityBinder.ResolveWorkforceCallerAsync"/> before any
/// read: no create, no email bind, and an existing guest binding no longer resolves. Members (<c>acct = 0</c>) and guests
/// whose home tenant is not listed are unchanged.
/// </summary>
public class ContactIdentityBinderWorkforceGuestTests
{
    private const string UseHomeSignIn = "sdap.access.deny.workforce_guest_use_home_signin";
    private const string GuestEmail = "new.hire@customer.example";
    private static readonly Guid GuestOid = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid ContactA = Guid.Parse("eeeeeeee-5555-4555-8555-00000000000a");
    private static readonly Guid UnlistedTenant = Guid.Parse("77777777-cccc-4ccc-8ccc-777777777777");
    private static readonly string Stamp = SpaarkeTenant.ToString("D");

    private readonly InMemoryContactIdentityStore _store = new();

    private static ClaimsPrincipal Guest(Guid homeTenant, string? email = GuestEmail)
    {
        var user = WorkforceUser(GuestOid, SpaarkeTenant, acct: "1", email: email);
        ((ClaimsIdentity)user.Identity!).AddClaim(new Claim("idp", $"https://sts.windows.net/{homeTenant:D}/"));
        return user;
    }

    [Fact]
    public void TheDenyCode_IsExact()
        => WorkforceMembershipTest.DenyGuestUseHomeSignin.Should().Be(UseHomeSignIn);

    [Fact]
    public async Task ACustomerTenantGuestWithNoContact_IsRefused_WithNoCreateAndNoLookup()
    {
        var store = new Mock<IContactIdentityStore>();
        var binder = new ContactIdentityBinder(
            store.Object, Tenants(CustomerTenant), new FakeTimeProvider(Now), new BindingLogCapture<ContactIdentityBinder>());

        var result = await binder.ResolveWorkforceCallerAsync(Guest(CustomerTenant), GuestOid, Stamp, CancellationToken.None);

        result.Should().Be(new ContactBindingResult(null, UseHomeSignIn, BindingAction.Deny));
        store.Verify(s => s.CreateContactForOidAsync(
            It.IsAny<Guid>(), It.IsAny<IdentityPlaneMarker>(), It.IsAny<NewContactDetails>(), It.IsAny<CancellationToken>()),
            Times.Never);
        store.Verify(s => s.FindActiveContactsByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.FindContactsByOidAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Invocations.Should().BeEmpty("the refusal comes before every store call");
    }

    [Fact]
    public async Task ACustomerTenantGuestWhoseEmailMatchesAnExistingContact_IsRefused_AndNotBound()
    {
        _store.AddContact(ContactA, email: GuestEmail);

        var result = await Binder(_store).ResolveWorkforceCallerAsync(
            Guest(CustomerTenant), GuestOid, Stamp, CancellationToken.None);

        result.DenyCode.Should().Be(UseHomeSignIn);
        result.Wrote.Should().BeFalse();
        _store.Contacts[ContactA].Oid.Should().BeNull("a refused guest is never email-bound");
        _store.Writes.Should().BeEmpty();
        _store.Reads.Should().BeEmpty();
    }

    [Fact]
    public async Task AContactAlreadyBoundToTheGuestOidOnTheWorkforcePlane_IsRefused_NotResolved_AndLeftUntouched()
    {
        // The retired path's own output (dev: three such contacts on 2026-10-20). The owner's intent is one door: the
        // binding must not keep granting access. It is not cleared or flagged here (no data change).
        var bound = _store.AddContact(ContactA, email: GuestEmail, oid: GuestOid.ToString("D"),
            plane: IdentityPlaneMarker.Workforce);

        var result = await Binder(_store).ResolveWorkforceCallerAsync(
            Guest(CustomerTenant), GuestOid, Stamp, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.ContactId.Should().BeNull();
        result.DenyCode.Should().Be(UseHomeSignIn);
        bound.Oid.Should().Be(GuestOid.ToString("D"));
        bound.Plane.Should().Be((int)IdentityPlaneMarker.Workforce);
        _store.Writes.Should().BeEmpty();
        _store.Reads.Should().BeEmpty("the oid lookup that would resolve the binding never runs");
    }

    [Fact]
    public async Task AGuestWhoseHomeTenantIsNotListed_IsDeniedExactlyAsBefore()
    {
        // Pin: not the FR-15 conjunction, so not the new refusal — the member test's own code, nothing written.
        _store.AddContact(ContactA, email: GuestEmail);

        var result = await Binder(_store).ResolveWorkforceCallerAsync(
            Guest(UnlistedTenant), GuestOid, Stamp, CancellationToken.None);

        result.DenyCode.Should().Be(WorkforceMembershipTest.DenyForeignTenant);
        _store.Writes.Should().BeEmpty();
        _store.Contacts[ContactA].Oid.Should().BeNull();
    }

    [Fact]
    public async Task AnUnlistedTenantGuestAlreadyBoundOnTheWorkforcePlane_IsRefused_WithNoStoreCall()
    {
        // #2128 round 1: a stored binding does not outlive the member test. Before, the oid lookup resolved it.
        var bound = _store.AddContact(ContactA, email: GuestEmail, oid: GuestOid.ToString("D"),
            plane: IdentityPlaneMarker.Workforce);

        var result = await Binder(_store).ResolveWorkforceCallerAsync(
            Guest(UnlistedTenant), GuestOid, Stamp, CancellationToken.None);

        result.Should().Be(new ContactBindingResult(null, WorkforceMembershipTest.DenyForeignTenant, BindingAction.Deny));
        bound.Oid.Should().Be(GuestOid.ToString("D"), "the row is left unchanged");
        _store.Reads.Should().BeEmpty("the oid lookup never runs");
        _store.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AMemberAlreadyBoundWhoseTenantWasSinceDelisted_IsRefused_WithNoStoreCall(bool listStillHasOthers)
    {
        // The member bound while its tenant was listed; an operator has since removed it (or emptied the list).
        var oid = Guid.NewGuid();
        var store = new Mock<IContactIdentityStore>();
        store.Setup(s => s.FindContactsByOidAsync(oid, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("the oid lookup must not run for a non-member"));
        var tenants = listStillHasOthers ? Tenants(UnlistedTenant) : Tenants();
        var binder = new ContactIdentityBinder(
            store.Object, tenants, new FakeTimeProvider(Now), new BindingLogCapture<ContactIdentityBinder>());

        var result = await binder.ResolveWorkforceCallerAsync(
            WorkforceUser(oid, CustomerTenant, acct: "0", email: GuestEmail), oid, Stamp, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.DenyCode.Should().Be(listStillHasOthers
            ? WorkforceMembershipTest.DenyForeignTenant
            : WorkforceMembershipTest.DenyTenantListEmpty);
        store.Invocations.Should().BeEmpty("the refusal comes before every store call");
    }

    [Fact]
    public async Task AMemberAlreadyBoundOnTheWorkforcePlane_StillResolvesByOid()
    {
        // Must-not-fire pin for #2128 round 1: an acct = 0 member of a listed tenant keeps resolving through its binding.
        var oid = Guid.NewGuid();
        _store.AddContact(ContactA, email: GuestEmail, oid: oid.ToString("D"), plane: IdentityPlaneMarker.Workforce);

        var result = await Binder(_store).ResolveWorkforceCallerAsync(
            WorkforceUser(oid, CustomerTenant, acct: "0", email: GuestEmail), oid, Stamp, CancellationToken.None);

        result.Should().Be(ContactBindingResult.Resolved(ContactA, BindingAction.ResolveByOid));
        _store.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AMemberWithNoContact_StillGetsOneCreated()
    {
        var oid = Guid.NewGuid();

        var result = await Binder(_store).ResolveWorkforceCallerAsync(
            WorkforceUser(oid, CustomerTenant, acct: "0"), oid, Stamp, CancellationToken.None);

        result.Action.Should().Be(BindingAction.CreateByOid);
        result.IsResolved.Should().BeTrue();
        _store.Contacts.Values.Should().ContainSingle().Which.Oid.Should().Be(oid.ToString("D"));
    }

    [Fact]
    public async Task AMemberWhoseEmailMatchesAnUnboundContact_IsStillEmailBound()
    {
        var oid = Guid.NewGuid();
        _store.AddContact(ContactA, email: GuestEmail);

        var result = await Binder(_store).ResolveWorkforceCallerAsync(
            WorkforceUser(oid, CustomerTenant, acct: "0", email: GuestEmail), oid, Stamp, CancellationToken.None);

        result.Should().Be(new ContactBindingResult(ContactA, null, BindingAction.BindByEmail, Wrote: true));
        _store.Contacts[ContactA].Oid.Should().Be(oid.ToString("D"));
    }

    [Fact]
    public async Task TheRefusal_IsLoggedWithTheMembershipLabelHomeTenantAndOidPrefix_AndNoEmailOrFullOid()
    {
        var log = new BindingLogCapture<ContactIdentityBinder>();

        await Binder(_store, log).ResolveWorkforceCallerAsync(Guest(CustomerTenant), GuestOid, Stamp, CancellationToken.None);

        var line = log.Messages.Should().ContainSingle(m => m.Contains("[ID-BIND]")).Subject;
        line.Should().Contain($"DENIED ({UseHomeSignIn})")
            .And.Contain("Member (customer-tenant guest")
            .And.Contain($"homeTenant={CustomerTenant:D}")
            .And.Contain($"oid={GuestOid.ToString("D")[..8]}")
            .And.NotContain(GuestOid.ToString("D"))
            .And.NotContain(GuestEmail)
            .And.NotContain("@");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ANonMemberRefusal_IsLoggedAsADenyWithItsMemberTestCode_AndNoEmail(bool unlistedGuest)
    {
        // #2128 round 2: the round-1 refusal before the oid lookup is the operator's only trace of a non-member sign-in.
        // It uses the binder's existing [ID-BIND] deny line (plane, oid, deny code, action, facts) — never the email.
        var log = new BindingLogCapture<ContactIdentityBinder>();
        var user = unlistedGuest
            ? Guest(UnlistedTenant)
            : WorkforceUser(GuestOid, CustomerTenant, acct: null, email: GuestEmail);
        var expected = unlistedGuest ? WorkforceMembershipTest.DenyForeignTenant : WorkforceMembershipTest.DenyAcctMissing;

        var result = await Binder(_store, log).ResolveWorkforceCallerAsync(user, GuestOid, Stamp, CancellationToken.None);

        result.DenyCode.Should().Be(expected);
        var entry = log.Entries.Should().ContainSingle(e => e.Message.Contains("[ID-BIND]")).Subject;
        entry.Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning);
        entry.Message.Should().Contain($"DENIED ({expected})")
            .And.Contain("refused before the oid lookup (#2128)")
            .And.NotContain(GuestEmail)
            .And.NotContain("@");
    }
}
