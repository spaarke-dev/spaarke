using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.IdentityBindingTestKit;

namespace Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

/// <summary>
/// unified-access-control-r2 task 141 — the CIAM plane through <see cref="ContactIdentityBinder"/>.
/// </summary>
/// <remarks>
/// What changed (defect C7 item 6): the CIAM lookups read <c>$top=1</c>, so two contacts carrying one oid or one
/// email resolved to whichever came first; a failed oid read fell through to the email path, which could then
/// bind the SAME oid onto a second contact; oids compared as strings; neither query filtered statecode; and the
/// bind wrote any oid onto any unbound contact — including an employee's. The CIAM plane keeps exactly one email
/// path: the repair of an invite whose oid write failed. It never creates a contact.
/// </remarks>
public class CiamContactBindingTests
{
    private static readonly Guid CiamOid = Guid.Parse("dddddddd-4444-4444-8444-000000000001");
    private static readonly Guid ContactA = Guid.Parse("eeeeeeee-5555-4555-8555-00000000000a");
    private static readonly Guid ContactB = Guid.Parse("eeeeeeee-5555-4555-8555-00000000000b");
    private const string Email = "counsel@firm.example";

    private readonly InMemoryContactIdentityStore _store = new();

    [Fact]
    public async Task AContactBoundToTheOid_Resolves_EmailIsNeverConsulted()
    {
        _store.AddContact(ContactA, email: "other@firm.example", oid: CiamOid.ToString("D"), plane: IdentityPlaneMarker.External);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString().ToUpperInvariant(), Email, CancellationToken.None);

        result.ContactId.Should().Be(ContactA, "oids are compared as parsed Guids, not strings");
        _store.Reads.Should().NotContain("email");
    }

    [Fact]
    public async Task TheCiamPlaneNeverCreatesAContact()
    {
        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotFound);
        _store.Writes.Should().BeEmpty("external users arrive by invitation");
    }

    [Fact]
    public async Task TheInviteRepairPath_BindsTheOidOntoTheOneUnboundActiveContact_PlaneExternal()
    {
        _store.AddContact(ContactA, email: Email);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email.ToUpperInvariant(), CancellationToken.None);

        result.ContactId.Should().Be(ContactA);
        _store.Contacts[ContactA].Oid.Should().Be(CiamOid.ToString("D"));
        _store.Contacts[ContactA].Plane.Should().Be((int)IdentityPlaneMarker.External);
    }

    [Fact]
    public async Task TheBindRefusesAContactBoundOnTheWorkforcePlane()
    {
        _store.AddContact(ContactA, email: Email, oid: Guid.NewGuid().ToString("D"), plane: IdentityPlaneMarker.Workforce);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactBoundToDifferentOid);
        _store.Contacts[ContactA].Flag!.CollidingPlane.Should().Be(IdentityPlaneMarker.External);
    }

    [Fact]
    public async Task TheBindRefusesAContactASystemUserLinksTo()
    {
        _store.AddContact(ContactA, email: Email);
        _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), Email, primaryContactId: ContactA);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactLinkedToOtherUser);
        _store.Writes.Should().NotContain(w => w.Op == "bind");
    }

    [Fact]
    public async Task AnAmbiguousEmail_IsDenied_NotAFirstRowPick()
    {
        _store.AddContact(ContactA, email: Email);
        _store.AddContact(ContactB, email: Email);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactEmailAmbiguous);
        _store.Writes.Should().NotContain(w => w.Op == "bind");
    }

    [Fact]
    public async Task AnAmbiguousOid_IsDenied_NotAFirstRowPick()
    {
        // The second binding carries no mirror (written by hand), so the unique index never saw it.
        _store.AddContact(ContactA, oid: CiamOid.ToString("D"), plane: IdentityPlaneMarker.External);
        _store.AddContact(ContactB, oid: CiamOid.ToString("D"), plane: IdentityPlaneMarker.External, deriveKeyMirror: false);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactOidAmbiguous);
    }

    [Fact]
    public async Task AFailedOidRead_Denies_AndNeverFallsThroughToTheEmailBind()
    {
        // The defect: a failed oid read looked like "no contact", fell through to the email fallback, and the
        // email path could bind this oid onto a SECOND contact.
        _store.OidLookupStatus = LookupStatus.Failed;
        _store.AddContact(ContactA, email: Email);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactLookupFailed);
        _store.Reads.Should().NotContain("email");
        _store.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyActiveContactsResolve()
    {
        _store.AddContact(ContactA, oid: CiamOid.ToString("D"), plane: IdentityPlaneMarker.External, stateCode: 1);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactInactive);
    }

    /// <summary>
    /// Task 137 (C5) verifies task 141's sign-in rule on the CIAM plane, at its hardest: an oid bound to an INACTIVE
    /// contact denies with the inactive code even when an ACTIVE, unbound contact carries the caller's email — no email
    /// lookup, no oid bind, no contact creation. Falling through to the email path here would re-bind the caller onto
    /// the other contact.
    /// </summary>
    [Fact]
    public async Task AnOidBoundToAnInactiveContact_Denies_EvenWhenAnActiveContactSharesTheEmail_NoEmailReadNoBindNoCreate()
    {
        _store.AddContact(ContactA, email: Email, oid: CiamOid.ToString("D"), plane: IdentityPlaneMarker.External, stateCode: 1);
        _store.AddContact(ContactB, email: Email);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactInactive);
        _store.Reads.Should().NotContain("email", "an oid match on an inactive contact never falls through to the email path");
        _store.Writes.Should().BeEmpty("no bind, no create, no flag");
        _store.Contacts[ContactB].Oid.Should().BeNull("the active contact sharing the email is never bound");
    }

    [Fact]
    public async Task AFailedBindWrite_IsNotResolvedAnyway()
    {
        _store.AddContact(ContactA, email: Email);
        _store.FailBind = true;

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, CancellationToken.None);

        result.IsResolved.Should().BeFalse("an unbound contact resolved without its binding keeps the hijack window open");
        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactBindFailed);
    }

    [Fact]
    public async Task ABindThatLosesARace_IsNotAnOverwrite_ItIsReDecidedAgainstFreshRows()
    {
        // Another identity binds ContactA between our read and our conditional (If-Match) write.
        var winner = Guid.NewGuid();
        _store.AddContact(ContactA, email: Email);
        _store.BeforeBind = c =>
        {
            c.Oid = winner.ToString("D");
            c.Plane = (int)IdentityPlaneMarker.External;
            _store.Touch(c);
        };

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactBoundToDifferentOid,
            "re-decided: the contact is now someone else's");
        _store.Contacts[ContactA].Oid.Should().Be(winner.ToString("D"), "the winning binding is never overwritten");
    }

    [Fact]
    public async Task TheRepairBind_AgainstASquattedMirror_Denies_AndFlagsTheHolder_NeverBinds()
    {
        // B2 (owner round 4 item 4): another contact holds this CIAM oid in the unsecured uniqueness mirror.
        _store.AddContact(ContactA, email: Email);
        _store.AddContact(ContactB, email: "elsewhere@firm.example", keyMirror: CiamOid.ToString("D"));

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactKeyConflict);
        _store.Contacts[ContactA].Oid.Should().BeNull("the repair bind is never made past the index");
        var flag = _store.Contacts[ContactB].Flag!;
        (flag.Reason, flag.CollidingOid, flag.CollidingPlane)
            .Should().Be((IdentityCollisionReason.KeyMirrorConflict, CiamOid, IdentityPlaneMarker.External));
    }

    [Fact]
    public async Task AnUnparseableOidClaim_IsUnidentifiable()
        => (await Binder(_store).ResolveCiamCallerAsync("not-a-guid", Email, CancellationToken.None))
            .DenyCode.Should().Be(ContactBindingDecision.DenyUnidentifiableCaller);

    // ── The invite's own bind (BindInvitedContactAsync) ──────────────────────────────────────────────
    // The invite binds the new CIAM oid onto a contact it just CREATED, which carries no row version yet. It
    // used to send If-Match: * ("the row exists") — an unconditional write that would overwrite a binding a
    // concurrent first sign-in made in between. Now: re-read, bind only an active unbound row, under its version.

    [Fact]
    public async Task TheInviteBind_OfAJustCreatedContact_RereadsIt_AndBindsUnderItsRowVersion()
    {
        _store.AddContact(ContactA, email: Email);

        var write = await Binder(_store).BindInvitedContactAsync(ContactA, etag: null, CiamOid, CancellationToken.None);

        write.Status.Should().Be(StoreWriteStatus.Written);
        _store.Contacts[ContactA].Oid.Should().Be(CiamOid.ToString("D"));
        _store.Contacts[ContactA].Plane.Should().Be((int)IdentityPlaneMarker.External);
    }

    [Fact]
    public async Task TheInviteBind_NeverOverwritesABindingMadeMeanwhile()
    {
        var employee = Guid.NewGuid();
        _store.AddContact(ContactA, email: Email, oid: employee.ToString("D"), plane: IdentityPlaneMarker.Workforce);

        var write = await Binder(_store).BindInvitedContactAsync(ContactA, etag: null, CiamOid, CancellationToken.None);

        write.Status.Should().NotBe(StoreWriteStatus.Written);
        _store.Contacts[ContactA].Oid.Should().Be(employee.ToString("D"), "the concurrent binding stands");
        _store.Writes.Should().NotContain(w => w.Op == "bind");
    }

    [Fact]
    public async Task TheInviteBind_RunsTheMaskingProbe_LikeEveryOtherBind()
    {
        // A bound row elsewhere makes masking detectable; then the column is masked from this identity.
        _store.AddContact(ContactB, email: "someone@firm.example", oid: Guid.NewGuid().ToString("D"), plane: IdentityPlaneMarker.External);
        _store.AddContact(ContactA, email: Email);
        _store.MaskBindingColumn = true;

        var write = await Binder(_store).BindInvitedContactAsync(ContactA, etag: null, CiamOid, CancellationToken.None);

        write.Status.Should().Be(StoreWriteStatus.Failed);
        write.Error.Should().Be(ContactBindingDecision.DenyBindingColumnMasked);
        _store.Contacts[ContactA].Oid.Should().BeNull();
    }
}
