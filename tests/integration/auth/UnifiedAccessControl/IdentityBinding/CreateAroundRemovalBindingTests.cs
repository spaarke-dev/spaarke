using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.IdentityBindingTestKit;

namespace Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

/// <summary>
/// Slice C2b: no contact is created around a removal. Every contact create the binder makes (the first sign-in create, the
/// licensed-user link create and registration) is refused when an INACTIVE contact for the new contact's email is one whose
/// external access an administrator removed, or when that read fails; the report-only link pass reports the same refusal.
/// An unmarked inactive contact, and a create with no email, behave as before.
/// </summary>
public class CreateAroundRemovalBindingTests
{
    private static readonly Guid RemovedContact = Guid.Parse("eeeeeeee-8888-4888-8888-00000000000a");
    private static readonly DateTimeOffset RemovedOn = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private const string Email = "person@customer.example";

    private readonly InMemoryContactIdentityStore _store = new();

    private void InactiveContactForTheEmail(bool removed, string? oid = null)
    {
        _store.AddContact(RemovedContact, email: Email, oid: oid,
            plane: oid is null ? null : IdentityPlaneMarker.Workforce, stateCode: 1);
        if (removed) _store.Contacts[RemovedContact].RemovedOn = RemovedOn;
    }

    private Task<ContactBindingResult> FirstSignIn(Guid oid, string? email = Email)
        => Binder(_store).ResolveWorkforceCallerAsync(
            WorkforceUser(oid, tid: CustomerTenant, email: email), oid, stampTenantId: null, CancellationToken.None);

    // ── C5: the email bind onto an existing contact ─────────────────────────────────────────────────────────────

    private static readonly Guid InvitedContact = Guid.Parse("eeeeeeee-8888-4888-8888-00000000000b");

    /// <summary>An unbound ACTIVE contact for the email, invited for a work-account sign-in from the customer tenant.</summary>
    private void InvitedActiveContactForTheEmail()
    {
        _store.AddContact(InvitedContact, email: Email);
        _store.StampInvite(InvitedContact, Email, CustomerTenant);
    }

    /// <summary>
    /// MUST FIRE: a sign-in whose email bind would land on an invited active contact is denied while an inactive contact for
    /// the same email is one whose external access an administrator removed; nothing is bound.
    /// </summary>
    [Fact]
    public async Task AWorkforceEmailBind_WithTheEmailOfARemovedInactiveContact_IsDenied_AndNothingIsBound()
    {
        InactiveContactForTheEmail(removed: true);
        InvitedActiveContactForTheEmail();
        var oid = Guid.NewGuid();

        var result = await FirstSignIn(oid);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactRemoved);
        result.IsResolved.Should().BeFalse();
        _store.ContactsBoundTo(oid).Should().BeEmpty();
        _store.Contacts[InvitedContact].Oid.Should().BeNull();
        _store.Writes.Should().BeEmpty();
    }

    /// <summary>MUST FIRE: the CIAM plane's email bind (an invite code) is denied the same way.</summary>
    [Fact]
    public async Task ACiamEmailBind_WithTheEmailOfARemovedInactiveContact_IsDenied_AndNothingIsBound()
    {
        InactiveContactForTheEmail(removed: true);
        var invite = InviteCode.Create(InvitedContact);
        _store.AddContact(InvitedContact, email: Email, invitedOn: Now, inviteCodeHash: invite.StoredValue);
        _store.StampInvite(InvitedContact, Email);
        var oid = Guid.NewGuid();

        var result = await Binder(_store).ResolveCiamCallerAsync(oid.ToString(), Email, invite.Code, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactRemoved);
        _store.Contacts[InvitedContact].Oid.Should().BeNull();
        _store.Contacts[InvitedContact].InviteCodeHash.Should().Be(invite.StoredValue, "a refused bind consumes nothing");
        _store.Writes.Should().BeEmpty();
    }

    /// <summary>MUST FIRE: an email bind is denied, retryably, while the inactive contacts cannot be read.</summary>
    [Fact]
    public async Task AnEmailBind_WhenTheInactiveContactsCannotBeRead_IsDenied_AndNothingIsBound()
    {
        InvitedActiveContactForTheEmail();
        _store.FailInactiveEmailLookup = true;

        var result = await FirstSignIn(Guid.NewGuid());

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactLookupFailed);
        _store.Contacts[InvitedContact].Oid.Should().BeNull();
        _store.Writes.Should().BeEmpty();
    }

    /// <summary>MUST NOT FIRE: an unmarked inactive contact for the email does not stop the bind.</summary>
    [Fact]
    public async Task AnEmailBind_WithTheEmailOfAnUnmarkedInactiveContact_StillBinds()
    {
        InactiveContactForTheEmail(removed: false);
        InvitedActiveContactForTheEmail();
        var oid = Guid.NewGuid();

        var result = await FirstSignIn(oid);

        result.Action.Should().Be(BindingAction.BindByEmail);
        result.ContactId.Should().Be(InvitedContact);
        _store.Contacts[InvitedContact].Oid.Should().Be(oid.ToString("D"));
    }

    // ── The first sign-in create ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FirstSignInCreate_WithTheEmailOfARemovedInactiveContact_IsDenied_AndNoContactIsCreated()
    {
        InactiveContactForTheEmail(removed: true);

        var result = await FirstSignIn(Guid.NewGuid());

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactRemoved);
        result.ContactId.Should().BeNull();
        _store.Contacts.Should().ContainSingle("no contact is created around a removal");
        _store.CreatePayloads.Should().BeEmpty();
    }

    [Fact]
    public async Task FirstSignInCreate_WithTheEmailOfAnUnmarkedInactiveContact_StillCreates()
    {
        InactiveContactForTheEmail(removed: false);
        var oid = Guid.NewGuid();

        var result = await FirstSignIn(oid);

        result.Action.Should().Be(BindingAction.CreateByOid, "MUST NOT FIRE: an unmarked inactive contact is decided as before");
        result.IsResolved.Should().BeTrue();
        _store.ContactsBoundTo(oid).Should().ContainSingle();
    }

    [Fact]
    public async Task FirstSignInCreate_WhenTheInactiveContactsCannotBeRead_IsDenied_AndNothingIsCreated()
    {
        _store.FailInactiveEmailLookup = true;

        var result = await FirstSignIn(Guid.NewGuid());

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactLookupFailed);
        _store.Contacts.Should().BeEmpty("a create is never reached while a removal cannot be ruled out");
    }

    [Fact]
    public async Task FirstSignInCreate_WithNoEmail_HasNothingToMatch_AndCreates()
    {
        InactiveContactForTheEmail(removed: true);
        var oid = Guid.NewGuid();

        var result = await FirstSignIn(oid, email: null);

        result.Action.Should().Be(BindingAction.CreateByOid, "MUST NOT FIRE: an email-less create matches no contact");
        _store.Reads.Should().NotContain("inactive-email");
    }

    [Fact]
    public async Task ARemovedPerson_SigningInWithTheSameOidAndNoEmail_IsDenied_AndNoContactIsCreated()
    {
        // Removal keeps the binding, so the oid path denies the removed carrier before any create is considered.
        var oid = Guid.NewGuid();
        InactiveContactForTheEmail(removed: true, oid: oid.ToString("D"));
        _store.Contacts[RemovedContact].StateCode = 0;

        var result = await FirstSignIn(oid, email: null);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactRemoved);
        _store.Contacts.Should().ContainSingle();
        _store.CreatePayloads.Should().BeEmpty();
    }

    // ── The licensed-user link create (write pass and report-only pass) ────────────────────────────────────────

    private Guid LicensedUserWithTheEmail()
    {
        var userId = Guid.NewGuid();
        _store.AddSystemUser(userId, Guid.NewGuid(), Email);
        return userId;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LicensedUserLinkCreate_WithTheEmailOfARemovedInactiveContact_IsDenied_InBothPasses(bool applyWrites)
    {
        InactiveContactForTheEmail(removed: true);
        var userId = LicensedUserWithTheEmail();

        var result = await Binder(_store).EnsureSystemUserLinkAsync(userId, applyWrites, CancellationToken.None);

        result.Outcome.Should().Be(SystemUserLinkOutcome.Denied,
            "the report-only pass must not promise a create (CreatedAndLinked) the write pass refuses");
        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactRemoved);
        result.IsLinked.Should().BeFalse();
        result.Changes.Should().BeEmpty();
        _store.Contacts.Should().ContainSingle();
        _store.SystemUsers[userId].PrimaryContactId.Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LicensedUserLinkCreate_WithTheEmailOfAnUnmarkedInactiveContact_StillCreates(bool applyWrites)
    {
        InactiveContactForTheEmail(removed: false);
        var userId = LicensedUserWithTheEmail();

        var result = await Binder(_store).EnsureSystemUserLinkAsync(userId, applyWrites, CancellationToken.None);

        result.Outcome.Should().Be(SystemUserLinkOutcome.CreatedAndLinked, "MUST NOT FIRE: unchanged");
        _store.Contacts.Should().HaveCount(applyWrites ? 2 : 1);
    }

    [Fact]
    public async Task LicensedUserLinkCreate_WhenTheInactiveContactsCannotBeRead_IsDeniedInTheReportOnlyPass()
    {
        _store.FailInactiveEmailLookup = true;
        var userId = LicensedUserWithTheEmail();

        var result = await Binder(_store).EnsureSystemUserLinkAsync(userId, applyWrites: false, CancellationToken.None);

        result.Outcome.Should().Be(SystemUserLinkOutcome.Denied);
        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactLookupFailed);
    }

    // ── The inactive read: a removal beyond the first two rows is still seen ────────────────────────────────────

    [Fact]
    public async Task ARemovedContact_AmongSeveralInactiveContactsForTheEmail_StillRefusesTheCreate()
    {
        _store.AddContact(Guid.NewGuid(), email: Email, stateCode: 1);
        _store.AddContact(Guid.NewGuid(), email: Email, stateCode: 1);
        InactiveContactForTheEmail(removed: true);

        var result = await FirstSignIn(Guid.NewGuid());

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactRemoved);
        _store.CreatePayloads.Should().BeEmpty();
    }
}
