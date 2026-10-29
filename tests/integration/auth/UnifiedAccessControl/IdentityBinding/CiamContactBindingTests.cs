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

    /// <summary>
    /// Task 185: ContactA invited here the way the invite flow leaves it — the marker at <paramref name="invitedOn"/>
    /// (default now), the stored hash of a fresh code and the invite stamp for <paramref name="email"/>. Returns the code
    /// the invite email carries.
    /// </summary>
    private string InvitedContactA(DateTimeOffset? invitedOn = null, string email = Email)
    {
        var invite = InviteCode.Create(ContactA);
        _store.AddContact(ContactA, email: email, invitedOn: invitedOn ?? Now, inviteCodeHash: invite.StoredValue);
        _store.StampInvite(ContactA, email);
        return invite.Code;
    }

    [Fact]
    public async Task AContactBoundToTheOid_Resolves_EmailIsNeverConsulted()
    {
        _store.AddContact(ContactA, email: "other@firm.example", oid: CiamOid.ToString("D"), plane: IdentityPlaneMarker.External);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString().ToUpperInvariant(), Email, inviteCode: null, CancellationToken.None);

        result.ContactId.Should().Be(ContactA, "oids are compared as parsed Guids, not strings");
        _store.Reads.Should().NotContain("email");
    }

    [Fact]
    public async Task TheCiamPlaneNeverCreatesAContact()
    {
        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, inviteCode: null, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotFound);
        _store.Writes.Should().BeEmpty("external users arrive by invitation");
    }

    [Fact]
    public async Task TheInviteRepairPath_BindsTheOidOntoTheOneUnboundActiveContact_PlaneExternal()
    {
        // Task 184: the invite that this path repairs left its pending-invite marker (written before the CIAM account);
        // task 185: and the hash of the code its email carries.
        var code = InvitedContactA();

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email.ToUpperInvariant(), code, CancellationToken.None);

        result.ContactId.Should().Be(ContactA);
        _store.Contacts[ContactA].Oid.Should().Be(CiamOid.ToString("D"));
        _store.Contacts[ContactA].Plane.Should().Be((int)IdentityPlaneMarker.External);
    }

    // ── Task 184 (owner round 96): E11 binds only on THIS environment's intent ──────────────────────────────────
    // Any CIAM user can obtain a token for this customer's audience (provisioning spike S1), so an email match alone
    // is no evidence that this customer invited this person.

    /// <summary>
    /// Task 185 (owner round 98, option (c); design §6): a grant alone no longer binds; the grant flows send the invite
    /// instead. Before 185 this test was <c>AnUnboundContactWithAnActiveGrantHere_IsEmailBound</c>.
    /// </summary>
    [Fact]
    public async Task AnUnboundContactWithOnlyAnActiveGrantHere_IsDeniedNotInvited_AndNothingIsWritten()
    {
        _store.AddContact(ContactA, email: Email);
        _store.AddGrant(ContactA, expiresOn: DateOnly.FromDateTime(Now.UtcDateTime).AddDays(30));

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, inviteCode: null, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
        _store.Writes.Should().BeEmpty("no bind, and no flag: no other person is involved");
        _store.Contacts[ContactA].Oid.Should().BeNull();
    }

    [Fact]
    public async Task AnUnboundContactWithAPendingInviteHere_IsEmailBound()
    {
        var code = InvitedContactA(Now.AddDays(-2));

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None);

        result.Action.Should().Be(BindingAction.BindByEmail);
        _store.Contacts[ContactA].Plane.Should().Be((int)IdentityPlaneMarker.External);
    }

    /// <summary>
    /// Owner round 97, through the binder: the marker's age is measured at the binder's clock (the decision is pure). A
    /// marker exactly 30 days old binds; one a second older is denied <c>contact_not_invited</c>, with nothing written —
    /// even with the right code (task 185).
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public async Task AMarkerAtTheEdgeOfItsThirtyDays_IsMeasuredAtTheBindersClock(int extraSeconds, bool binds)
    {
        var code = InvitedContactA(Now - ContactBindingDecision.InviteMarkerWindow + TimeSpan.FromSeconds(extraSeconds));

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None);

        if (binds)
        {
            result.Action.Should().Be(BindingAction.BindByEmail);
        }
        else
        {
            result.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
            _store.Writes.Should().BeEmpty();
            _store.Contacts[ContactA].Oid.Should().BeNull();
        }
    }

    [Fact]
    public async Task AnUnboundContactThisEnvironmentNeverInvited_IsDeniedNotInvited_AndNothingIsWritten()
    {
        _store.AddContact(ContactA, email: Email);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, inviteCode: null, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
        _store.Writes.Should().BeEmpty("no bind, and no flag: no other person is involved");
        _store.Contacts[ContactA].Oid.Should().BeNull();
        _store.Contacts[ContactA].Flag.Should().BeNull();
    }

    [Theory]
    [InlineData(1, 30)]   // deactivated
    [InlineData(0, -1)]   // expired yesterday
    public async Task OnlyAnExpiredOrDeactivatedGrant_AndNoInvite_IsDenied(int stateCode, int expiresInDays)
    {
        _store.AddContact(ContactA, email: Email);
        _store.AddGrant(ContactA, stateCode, DateOnly.FromDateTime(Now.UtcDateTime).AddDays(expiresInDays));

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, inviteCode: null, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
        _store.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AnIntentReadThatThrows_NeverBinds()
    {
        var code = InvitedContactA();
        _store.ThrowOnIntentRead = true;

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactLookupFailed);
        _store.Writes.Should().BeEmpty();
    }

    /// <summary>
    /// Task 185 slice B2 (option (c), design §6): the bind-intent read is ONE marker read (marker + code hash) and no
    /// grant read, so a grant fault can no longer deny a real invitee and a grant can no longer bind. The E11 path reads
    /// exactly: the oid, the email, the systemuser links, the marker, and the masking probe before the write. Replaces
    /// <c>AFailedGrantRead_NeverBinds_EvenWithAnInviteMarker</c>: there is no grant read left to fail. The Web API side is
    /// pinned in <c>DataverseContactIdentityStoreTests.TheBindIntentRead_HasNoGrantLeg</c>.
    /// </summary>
    [Fact]
    public async Task TheBindIntentRead_MakesNoGrantRead_OnlyTheMarkerRead()
    {
        var code = InvitedContactA();
        _store.AddGrant(ContactA);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None);

        result.Action.Should().Be(BindingAction.BindByEmail);
        // C5: an email bind also reads the inactive contacts for the email, for a removal.
        _store.Reads.Should().Equal("oid", "email", "references", "intent", "inactive-email", "probe");
    }

    /// <summary>
    /// Task 185 (option (c)): a grant here changes no E11 outcome. With no marker, an expired marker or a hash column not
    /// provisioned yet, the right code with an ACTIVE grant is still denied <c>contact_not_invited</c>, nothing written.
    /// The binder-level twin of the decision tests whose grant input slice B2 removed.
    /// </summary>
    [Theory]
    [InlineData("no-marker")]
    [InlineData("expired-marker")]
    [InlineData("hash-column-missing")]
    public async Task AGrantHere_ChangesNoE11Outcome(string scenario)
    {
        var invite = InviteCode.Create(ContactA);
        _store.AddContact(ContactA, email: Email,
            invitedOn: scenario switch
            {
                "no-marker" => null,
                "expired-marker" => Now.AddDays(-90),
                _ => Now,
            },
            inviteCodeHash: invite.StoredValue);
        _store.InviteCodeHashColumnMissing = scenario == "hash-column-missing";
        _store.AddGrant(ContactA);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, invite.Code, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited, scenario);
        _store.Writes.Should().BeEmpty(scenario);
    }

    /// <summary>
    /// An environment whose invite columns (the marker, the code hash, or the invite stamp) are not provisioned reads the
    /// whole bind-intent lookup as <see cref="LookupStatus.ColumnMissing"/> (one query selects them all). No stamp can then
    /// be met, so the external (CIAM) email bind is refused too: <c>contact_not_invited</c>, nothing written, the contact
    /// still unbound. Rollout order: apply the contact security schema before a build that requires the stamp.
    /// MUST FIRE: an otherwise valid invite code and sign-in over a lookup that is ColumnMissing. MUST NOT FIRE: the same
    /// sign-in over a provisioned lookup binds.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AnInviteColumnNotProvisioned_RefusesTheCiamEmailBind_AndWritesNothing(bool columnMissing, bool binds)
    {
        var invite = InviteCode.Create(ContactA);
        _store.AddContact(ContactA, email: Email, invitedOn: Now, inviteCodeHash: invite.StoredValue);
        _store.StampInvite(ContactA, Email);
        _store.InviteMarkerStatus = columnMissing ? LookupStatus.ColumnMissing : LookupStatus.Read;

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, invite.Code, CancellationToken.None);

        if (binds)
        {
            result.Action.Should().Be(BindingAction.BindByEmail);
            return;
        }

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
        _store.Writes.Should().BeEmpty();
        _store.Contacts[ContactA].Oid.Should().BeNull();
    }

    /// <summary>
    /// The Workforce token plane email-binds a member only on the invite stamp (an invite to this address naming the
    /// caller's tenant), read in the intent query. Before the stamp this test was
    /// <c>TheWorkforcePlane_EmailBindsAMember_WithoutReadingAnyIntent</c>.
    /// </summary>
    [Fact]
    public async Task TheWorkforcePlane_EmailBindsAMember_OnTheInviteStamp()
    {
        var oid = Guid.NewGuid();
        _store.AddContact(ContactA, email: "person@customer.example");
        _store.StampInvite(ContactA, "person@customer.example", CustomerTenant);

        var result = await Binder(_store).ResolveWorkforceCallerAsync(
            WorkforceUser(oid, tid: CustomerTenant), oid, stampTenantId: null, CancellationToken.None);

        result.Action.Should().Be(BindingAction.BindByEmail);
        _store.Reads.Should().Contain("intent");
    }

    /// <summary>
    /// #2005 (supersedes task 185 design §7's "the workforce bind is the plain bind"): the workforce plane's email bind
    /// consumes a pending invite-code hash in its ONE conditional write — a granted contact is auto-invited (185 §11), so a
    /// B2B guest binding it would otherwise leave a live code on a bound contact. The same write clears the invite stamp.
    /// Where the invite columns are not provisioned no stamp can exist, so the Workforce email bind is refused
    /// (contact_not_invited) and nothing is written.
    /// MUTATION PROOF: the binder calling <c>BindOidAsync</c> here fails the provisioned case.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheWorkforcePlane_EmailBind_ConsumesAPendingInviteCodeHash_InTheSameWrite(bool hashColumnProvisioned)
    {
        var oid = Guid.NewGuid();
        var invite = InviteCode.Create(ContactA);
        _store.AddContact(ContactA, email: "person@customer.example", invitedOn: Now, inviteCodeHash: invite.StoredValue);
        _store.StampInvite(ContactA, "person@customer.example", CustomerTenant);
        _store.InviteCodeHashColumnMissing = !hashColumnProvisioned;

        var result = await Binder(_store).ResolveWorkforceCallerAsync(
            WorkforceUser(oid, tid: CustomerTenant), oid, stampTenantId: null, CancellationToken.None);

        if (!hashColumnProvisioned)
        {
            result.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
            _store.Writes.Should().BeEmpty();
            return;
        }

        result.Should().Be(new ContactBindingResult(ContactA, null, BindingAction.BindByEmail, Wrote: true));
        _store.Writes.Should().ContainSingle().Which.Should().Be(
            ("bind", (Guid?)ContactA, $"{oid:D}|{IdentityPlaneMarker.Workforce}|invite-code-hash=null"));
        var row = _store.Contacts[ContactA];
        row.Oid.Should().Be(oid.ToString("D"));
        row.InviteCodeHash.Should().BeNull();
        (row.InvitedEmailHash, row.InvitedTenantId).Should().Be(((string?)null, (string?)null), "the bind clears the stamp");
    }

    /// <summary>
    /// #2005, the reported scenario: a B2B guest email-binds a granted contact that was auto-invited, an operator unbinds
    /// it within the marker's 30 days, and someone holding the forwarded invite link signs up a CIAM account with the
    /// email. The code was consumed by the workforce bind, so E11 denies it and nothing is bound.
    /// MUTATION PROOF: the binder calling <c>BindOidAsync</c> on the workforce plane binds the CIAM account here.
    /// </summary>
    [Fact]
    public async Task AWorkforceBind_ThenAnOperatorUnbind_LeavesTheForwardedInviteCodeDenied()
    {
        var guestOid = Guid.NewGuid();
        var invite = InviteCode.Create(ContactA);
        _store.AddContact(ContactA, email: "person@customer.example", invitedOn: Now, inviteCodeHash: invite.StoredValue);
        _store.StampInvite(ContactA, "person@customer.example", CustomerTenant);
        (await Binder(_store).ResolveWorkforceCallerAsync(
                WorkforceUser(guestOid, tid: CustomerTenant), guestOid, stampTenantId: null, CancellationToken.None))
            .Action.Should().Be(BindingAction.BindByEmail);
        var row = _store.Contacts[ContactA];
        row.Oid = null;
        row.KeyMirror = null;
        row.Plane = null;
        _store.Touch(row);

        var replay = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), "person@customer.example",
            invite.Code, CancellationToken.None);

        replay.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited, "the workforce bind consumed the code");
        _store.Contacts[ContactA].Oid.Should().BeNull();
    }

    [Fact]
    public async Task TheBindRefusesAContactBoundOnTheWorkforcePlane()
    {
        _store.AddContact(ContactA, email: Email, oid: Guid.NewGuid().ToString("D"), plane: IdentityPlaneMarker.Workforce);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, inviteCode: null, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactBoundToDifferentOid);
        _store.Contacts[ContactA].Flag!.CollidingPlane.Should().Be(IdentityPlaneMarker.External);
    }

    [Fact]
    public async Task TheBindRefusesAContactASystemUserLinksTo()
    {
        _store.AddContact(ContactA, email: Email);
        _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), Email, primaryContactId: ContactA);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, inviteCode: null, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactLinkedToOtherUser);
        _store.Writes.Should().NotContain(w => w.Op == "bind");
    }

    [Fact]
    public async Task AnAmbiguousEmail_IsDenied_NotAFirstRowPick()
    {
        _store.AddContact(ContactA, email: Email);
        _store.AddContact(ContactB, email: Email);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, inviteCode: null, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactEmailAmbiguous);
        _store.Writes.Should().NotContain(w => w.Op == "bind");
    }

    [Fact]
    public async Task AnAmbiguousOid_IsDenied_NotAFirstRowPick()
    {
        // The second binding carries no mirror (written by hand), so the unique index never saw it.
        _store.AddContact(ContactA, oid: CiamOid.ToString("D"), plane: IdentityPlaneMarker.External);
        _store.AddContact(ContactB, oid: CiamOid.ToString("D"), plane: IdentityPlaneMarker.External, deriveKeyMirror: false);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, inviteCode: null, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactOidAmbiguous);
    }

    [Fact]
    public async Task AFailedOidRead_Denies_AndNeverFallsThroughToTheEmailBind()
    {
        // The defect: a failed oid read looked like "no contact", fell through to the email fallback, and the
        // email path could bind this oid onto a SECOND contact.
        _store.OidLookupStatus = LookupStatus.Failed;
        _store.AddContact(ContactA, email: Email);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, inviteCode: null, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactLookupFailed);
        _store.Reads.Should().NotContain("email");
        _store.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyActiveContactsResolve()
    {
        _store.AddContact(ContactA, oid: CiamOid.ToString("D"), plane: IdentityPlaneMarker.External, stateCode: 1);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, inviteCode: null, CancellationToken.None);

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

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, inviteCode: null, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactInactive);
        _store.Reads.Should().NotContain("email", "an oid match on an inactive contact never falls through to the email path");
        _store.Writes.Should().BeEmpty("no bind, no create, no flag");
        _store.Contacts[ContactB].Oid.Should().BeNull("the active contact sharing the email is never bound");
    }

    [Fact]
    public async Task AFailedBindWrite_IsNotResolvedAnyway()
    {
        var code = InvitedContactA(); // task 184/185: invited here with a code, so the bind is attempted
        _store.FailBind = true;

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None);

        result.IsResolved.Should().BeFalse("an unbound contact resolved without its binding keeps the hijack window open");
        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactBindFailed);
    }

    [Fact]
    public async Task ABindThatLosesARace_IsNotAnOverwrite_ItIsReDecidedAgainstFreshRows()
    {
        // Another identity binds ContactA between our read and our conditional (If-Match) write.
        var winner = Guid.NewGuid();
        var code = InvitedContactA(); // task 184/185: invited here with a code, so the bind is attempted
        _store.BeforeBind = c =>
        {
            c.Oid = winner.ToString("D");
            c.Plane = (int)IdentityPlaneMarker.External;
            _store.Touch(c);
        };

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactBoundToDifferentOid,
            "re-decided: the contact is now someone else's");
        _store.Contacts[ContactA].Oid.Should().Be(winner.ToString("D"), "the winning binding is never overwritten");
    }

    [Fact]
    public async Task TheRepairBind_AgainstASquattedMirror_Denies_AndFlagsTheHolder_NeverBinds()
    {
        // B2 (owner round 4 item 4): another contact holds this CIAM oid in the unsecured uniqueness mirror.
        var code = InvitedContactA(); // task 184/185: invited here with a code, so the bind is attempted
        _store.AddContact(ContactB, email: "elsewhere@firm.example", keyMirror: CiamOid.ToString("D"));

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactKeyConflict);
        _store.Contacts[ContactA].Oid.Should().BeNull("the repair bind is never made past the index");
        var flag = _store.Contacts[ContactB].Flag!;
        (flag.Reason, flag.CollidingOid, flag.CollidingPlane)
            .Should().Be((IdentityCollisionReason.KeyMirrorConflict, CiamOid, IdentityPlaneMarker.External));
    }

    [Fact]
    public async Task AnUnparseableOidClaim_IsUnidentifiable()
        => (await Binder(_store).ResolveCiamCallerAsync("not-a-guid", Email, inviteCode: null, CancellationToken.None))
            .DenyCode.Should().Be(ContactBindingDecision.DenyUnidentifiableCaller);

    // ── Task 185 (owner round 98): the code is required, and consumed by the bind ────────────────────────────────

    /// <summary>
    /// AC2: the invitee with the code binds, and ONE conditional write carries the oid, the mirror, the plane AND the hash
    /// cleared (the in-memory store records the write once, under the If-Match version the decision was made on). The
    /// marker is kept. Neither the code nor the hash reaches a log line.
    /// MUTATION PROOF (ii): the binder calling <c>BindOidAsync</c> instead (not consuming) fails this test.
    /// </summary>
    [Fact]
    public async Task TheInvitee_WithTheCode_Binds_AndOneWriteCarriesOidMirrorPlaneAndHashNull()
    {
        var invite = InviteCode.Create(ContactA);
        _store.AddContact(ContactA, email: Email, invitedOn: Now, inviteCodeHash: invite.StoredValue);
        _store.StampInvite(ContactA, Email);
        var log = new BindingLogCapture<ContactIdentityBinder>();

        var result = await Binder(_store, log).ResolveCiamCallerAsync(CiamOid.ToString(), Email, invite.Code, CancellationToken.None);

        result.Should().Be(new ContactBindingResult(ContactA, null, BindingAction.BindByEmail, Wrote: true));
        _store.Writes.Should().ContainSingle().Which.Should().Be(
            ("bind", (Guid?)ContactA, $"{CiamOid:D}|{IdentityPlaneMarker.External}|invite-code-hash=null"));
        var row = _store.Contacts[ContactA];
        (row.Oid, row.KeyMirror, row.Plane).Should().Be((CiamOid.ToString("D"), CiamOid.ToString("D"), (int)IdentityPlaneMarker.External));
        row.InviteCodeHash.Should().BeNull("the code is single-use: consumed by the write that binds");
        row.InvitedOn.Should().Be(Now, "the marker is never cleared");
        log.Messages.Should().NotContain(m => m.Contains(invite.Code) || m.Contains(invite.StoredValue));
    }

    /// <summary>
    /// Design note §5.1: a not-invited deny logs its operator-only reason (here <c>code-mismatch</c>), and that line carries
    /// neither the presented code nor the stored hash. The HTTP deny code is unchanged.
    /// </summary>
    [Fact]
    public async Task AWrongCode_IsDenied_AndTheLogNamesTheReason_NeverTheCodeOrTheHash()
    {
        var invite = InviteCode.Create(ContactA);
        var wrong = InviteCode.Create(ContactA).Code;
        _store.AddContact(ContactA, email: Email, invitedOn: Now, inviteCodeHash: invite.StoredValue);
        _store.StampInvite(ContactA, Email);
        var log = new BindingLogCapture<ContactIdentityBinder>();

        var result = await Binder(_store, log).ResolveCiamCallerAsync(CiamOid.ToString(), Email, wrong, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
        log.Messages.Should().Contain(m => m.Contains("code-mismatch") && m.Contains(ContactA.ToString()));
        log.Messages.Should().NotContain(m => m.Contains(wrong) || m.Contains(invite.Code) || m.Contains(invite.StoredValue));
    }

    /// <summary>AC1: a squatted CIAM account carrying the invitee's email presents no code — denied, nothing written or flagged.</summary>
    [Fact]
    public async Task ASquattedAccount_PresentingNoCode_IsDeniedNotInvited_NothingWritten_NothingFlagged()
    {
        var invite = InviteCode.Create(ContactA);
        _store.AddContact(ContactA, email: Email, invitedOn: Now, inviteCodeHash: invite.StoredValue);
        _store.StampInvite(ContactA, Email);

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, inviteCode: null, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
        _store.Writes.Should().BeEmpty();
        _store.Contacts[ContactA].Flag.Should().BeNull();
        _store.Contacts[ContactA].InviteCodeHash.Should().Be(invite.StoredValue, "the real invitee can still use the code");
    }

    [Fact]
    public async Task AWrongCode_IsDeniedNotInvited_AndTheRealCodeStillBinds()
    {
        var code = InvitedContactA();

        var wrong = await Binder(_store).ResolveCiamCallerAsync(
            CiamOid.ToString(), Email, InviteCode.Create(ContactA).Code, CancellationToken.None);
        wrong.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
        _store.Writes.Should().BeEmpty("no attempt counter: a wrong code writes nothing (design §5.4)");

        (await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None))
            .Action.Should().Be(BindingAction.BindByEmail);
    }

    /// <summary>
    /// AC2 race (design §5.3): two sign-ins present the same code and both decide on the same row version. The second
    /// (staged inside the first's write, before its If-Match check) binds; the first's write then gets 412 and re-decides on
    /// fresh rows. Exactly one bind lands. With the SAME oid the loser resolves by oid (D4); with a DIFFERENT oid the email
    /// row is now bound to someone else: E7, <c>contact_bound_to_different_oid</c>.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TwoRacingSignIns_WithOneCode_BindExactlyOne_TheLoserGets412AndReDecides(bool sameOid)
    {
        var code = InvitedContactA();
        var otherOid = sameOid ? CiamOid : Guid.NewGuid();
        ContactBindingResult? first = null;
        _store.BeforeBind = _ => first = Binder(_store)
            .ResolveCiamCallerAsync(otherOid.ToString(), Email, code, CancellationToken.None).GetAwaiter().GetResult();

        var loser = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None);

        first!.Action.Should().Be(BindingAction.BindByEmail, "the sign-in whose write landed first binds");
        _store.Writes.Count(w => w.Op == "bind").Should().Be(1, "exactly one sign-in binds");
        _store.BindPreconditionFailures.Should().Be(1, "the loser's write was made on the version both decided on: 412");
        _store.Contacts[ContactA].Oid.Should().Be(otherOid.ToString("D"));
        _store.Contacts[ContactA].InviteCodeHash.Should().BeNull();
        if (sameOid)
        {
            loser.Action.Should().Be(BindingAction.ResolveByOid);
            loser.ContactId.Should().Be(ContactA);
        }
        else
        {
            loser.Action.Should().Be(BindingAction.Collision);
            loser.DenyCode.Should().Be(ContactBindingDecision.DenyContactBoundToDifferentOid);
        }
    }

    /// <summary>
    /// Pass 1 F1 (design §5.3): the contact row is read BEFORE its marker and hash. A concurrent sign-in with the same code
    /// that binds in between (staged just before this call's intent read) leaves this call seeing the hash already cleared.
    /// It re-decides once against fresh rows instead of denying <c>contact_not_invited</c>: with the SAME oid it resolves
    /// by oid (D4, the SPA's simultaneous first-load requests); with a DIFFERENT oid it is E7, as in the 412 race.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ASameCodeBindBetweenTheRowReadAndTheIntentRead_IsReDecided_NotDeniedNotInvited(bool sameOid)
    {
        var code = InvitedContactA();
        var otherOid = sameOid ? CiamOid : Guid.NewGuid();
        ContactBindingResult? racer = null;
        _store.AfterRead = read =>
        {
            if (read != "intent") return;
            _store.AfterRead = null; // one-shot: the racer's own intent read runs unhooked
            racer = Binder(_store)
                .ResolveCiamCallerAsync(otherOid.ToString(), Email, code, CancellationToken.None).GetAwaiter().GetResult();
        };

        var late = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None);

        racer!.Action.Should().Be(BindingAction.BindByEmail, "the racer bound inside the window");
        _store.Writes.Count(w => w.Op == "bind").Should().Be(1, "exactly one sign-in binds");
        _store.Contacts[ContactA].Oid.Should().Be(otherOid.ToString("D"));
        if (sameOid)
        {
            late.Action.Should().Be(BindingAction.ResolveByOid, "the oid lookup finds the contact the racer bound (D4)");
            late.ContactId.Should().Be(ContactA);
        }
        else
        {
            late.Action.Should().Be(BindingAction.Collision);
            late.DenyCode.Should().Be(ContactBindingDecision.DenyContactBoundToDifferentOid);
        }
    }

    /// <summary>
    /// Pass 1 F1, the negative: a plain wrong code with no race is re-decided exactly ONCE (two intent reads, never a
    /// loop) and stays denied, with nothing written. A sign-in presenting no code is never re-decided.
    /// </summary>
    [Fact]
    public async Task AWrongCode_WithNoRace_IsReDecidedExactlyOnce_AndStaysDenied_NothingWritten()
    {
        var code = InvitedContactA();

        var wrong = await Binder(_store).ResolveCiamCallerAsync(
            CiamOid.ToString(), Email, InviteCode.Create(ContactA).Code, CancellationToken.None);

        wrong.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
        _store.Reads.Count(r => r == "intent").Should().Be(2, "one re-decision, never a loop");
        _store.Writes.Should().BeEmpty();
        _store.Contacts[ContactA].InviteCodeHash.Should().NotBeNull("the real code is still usable");

        _store.Reads.Clear();
        (await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, inviteCode: null, CancellationToken.None))
            .DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
        _store.Reads.Count(r => r == "intent").Should().Be(1, "no code presented: nothing a racer could have consumed");

        (await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None))
            .Action.Should().Be(BindingAction.BindByEmail);
    }

    /// <summary>AC2 replay: after the bind, the same code from another account is decided by E7, never by E11.</summary>
    [Fact]
    public async Task TheSameCode_FromAnotherAccount_AfterTheBind_IsDeniedAsBoundToDifferentOid()
    {
        var code = InvitedContactA();
        (await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None))
            .Action.Should().Be(BindingAction.BindByEmail);

        var replay = await Binder(_store).ResolveCiamCallerAsync(Guid.NewGuid().ToString(), Email, code, CancellationToken.None);

        replay.DenyCode.Should().Be(ContactBindingDecision.DenyContactBoundToDifferentOid);
        _store.Contacts[ContactA].Oid.Should().Be(CiamOid.ToString("D"), "the binding is never re-pointed");
    }

    /// <summary>
    /// AC2 consumed: if an operator later unbinds the contact by hand, the old code is already gone, so it binds nobody.
    /// MUTATION PROOF (ii): the binder calling <c>BindOidAsync</c> instead (the hash survives the bind) fails this test.
    /// </summary>
    [Fact]
    public async Task AnOperatorUnbind_AfterConsumption_LeavesTheOldCodeDenied()
    {
        var code = InvitedContactA();
        (await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None))
            .Action.Should().Be(BindingAction.BindByEmail);
        var row = _store.Contacts[ContactA];
        row.Oid = null;
        row.KeyMirror = null;
        row.Plane = null;
        _store.Touch(row);

        var replay = await Binder(_store).ResolveCiamCallerAsync(Guid.NewGuid().ToString(), Email, code, CancellationToken.None);

        replay.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited, "a consumed code never binds again");
        _store.Contacts[ContactA].Oid.Should().BeNull();
    }

    /// <summary>
    /// AC3 superseded, raced (design §5.3): a re-invite lands between the sign-in's decision (old hash, version v1) and its
    /// write. The write gets 412; the re-decision reads the NEW hash, so the old code is denied and nothing binds. The
    /// re-invite's own code then binds.
    /// </summary>
    [Fact]
    public async Task AReInvite_WhileSomeoneSignsIn_TheSupersededCodeFails_TheNewCodeBinds()
    {
        var oldCode = InvitedContactA();
        InviteMarkResult? reInvite = null;
        _store.BeforeBind = _ => reInvite = Binder(_store)
            .MarkInviteSentAsync(ContactA, etag: null, Email, invitedTenantId: null, CancellationToken.None).GetAwaiter().GetResult();

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, oldCode, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited, "re-decided against the re-invite's hash");
        _store.BindPreconditionFailures.Should().Be(1);
        _store.Contacts[ContactA].Oid.Should().BeNull("a superseded code never binds");
        reInvite!.Status.Should().Be(StoreWriteStatus.Written);

        (await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, reInvite.InviteCode, CancellationToken.None))
            .Action.Should().Be(BindingAction.BindByEmail);
    }

    // ── Task 185: the invite's marker write creates the code (MarkInviteSentAsync) ──────────────────────────────────

    /// <summary>
    /// The marker and the new code's hash land in ONE write; the plaintext code goes back to the caller only (the invite
    /// email), it matches the stored hash for this contact, and it is in no log line and not in the result's ToString.
    /// </summary>
    [Fact]
    public async Task MarkInviteSent_WritesTheMarkerAndTheHashInOneWrite_AndReturnsTheCodeToTheCallerOnly()
    {
        _store.AddContact(ContactA, email: Email);
        var log = new BindingLogCapture<ContactIdentityBinder>();

        var mark = await Binder(_store, log).MarkInviteSentAsync(ContactA, etag: null, Email, invitedTenantId: null, CancellationToken.None);

        mark.Status.Should().Be(StoreWriteStatus.Written);
        InviteCode.IsWellFormed(mark.InviteCode).Should().BeTrue();
        var row = _store.Contacts[ContactA];
        _store.Writes.Should().ContainSingle().Which.Op.Should().Be("invite-marker");
        row.InvitedOn.Should().Be(Now);
        InviteCode.Matches(row.InviteCodeHash, mark.InviteCode, ContactA).Should().BeTrue();
        row.InviteCodeHash.Should().NotContain(mark.InviteCode!, "only the salted hash is stored, never the code");
        mark.ToString().Should().NotContain(mark.InviteCode!);
        log.Messages.Should().NotContain(m => m.Contains(mark.InviteCode!) || m.Contains(row.InviteCodeHash!));
    }

    /// <summary>A re-invite replaces the hash with a new code's: the earlier code stops matching, the new one binds.</summary>
    [Fact]
    public async Task AReInvite_ReplacesTheHash_TheEarlierCodeFails_TheNewCodeBinds()
    {
        _store.AddContact(ContactA, email: Email);
        var first = await Binder(_store).MarkInviteSentAsync(ContactA, etag: null, Email, invitedTenantId: null, CancellationToken.None);
        var firstHash = _store.Contacts[ContactA].InviteCodeHash;

        var second = await Binder(_store).MarkInviteSentAsync(ContactA, etag: null, Email, invitedTenantId: null, CancellationToken.None);

        second.InviteCode.Should().NotBe(first.InviteCode);
        _store.Contacts[ContactA].InviteCodeHash.Should().NotBe(firstHash);
        (await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, first.InviteCode, CancellationToken.None))
            .DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
        (await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, second.InviteCode, CancellationToken.None))
            .Action.Should().Be(BindingAction.BindByEmail);
    }

    /// <summary>
    /// A stamp with the marker column but not the hash column (Deploy-Release Phase 2 before Phase 3): the combined write
    /// is ColumnMissing, nothing is written, and no code is handed out — a marker without its hash could never bind.
    /// </summary>
    [Fact]
    public async Task MarkInviteSent_WhenTheHashColumnIsMissing_IsColumnMissing_WritesNothing_AndReturnsNoCode()
    {
        _store.AddContact(ContactA, email: Email);
        _store.InviteCodeHashColumnMissing = true;

        var mark = await Binder(_store).MarkInviteSentAsync(ContactA, etag: null, Email, invitedTenantId: null, CancellationToken.None);

        mark.Status.Should().Be(StoreWriteStatus.ColumnMissing);
        mark.InviteCode.Should().BeNull();
        _store.Writes.Should().BeEmpty();
        _store.Contacts[ContactA].InvitedOn.Should().BeNull();
    }

    // ── The invite's own bind (BindInvitedContactAsync) ──────────────────────────────────────────────
    // The invite binds the new CIAM oid onto a contact it just CREATED, which carries no row version yet. It
    // used to send If-Match: * ("the row exists") — an unconditional write that would overwrite a binding a
    // concurrent first sign-in made in between. Now: re-read, bind only an active unbound row, under its version.

    [Fact]
    public async Task TheInviteBind_OfAJustCreatedContact_RereadsIt_AndBindsUnderItsRowVersion()
    {
        _store.AddContact(ContactA, email: Email);

        var write = await Binder(_store).BindInvitedContactAsync(ContactA, etag: null, CiamOid, consumeInviteCode: false, CancellationToken.None);

        write.Status.Should().Be(StoreWriteStatus.Written);
        _store.Contacts[ContactA].Oid.Should().Be(CiamOid.ToString("D"));
        _store.Contacts[ContactA].Plane.Should().Be((int)IdentityPlaneMarker.External);
    }

    /// <summary>
    /// Task 185 (owner Q5, design §7): when the invite's marker and hash landed, its own bind clears the now-inert hash in
    /// the SAME conditional write as the oid. Without it (the columns not provisioned), the bind is the plain bind.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheInviteBind_ClearsTheHashInTheSameWrite_OnlyWhenAsked(bool consume)
    {
        var invite = InviteCode.Create(ContactA);
        _store.AddContact(ContactA, email: Email, invitedOn: Now, inviteCodeHash: invite.StoredValue);

        var write = await Binder(_store).BindInvitedContactAsync(ContactA, etag: null, CiamOid, consume, CancellationToken.None);

        write.Status.Should().Be(StoreWriteStatus.Written);
        _store.Writes.Should().ContainSingle().Which.Detail.Should().Be(consume
            ? $"{CiamOid:D}|{IdentityPlaneMarker.External}|invite-code-hash=null"
            : $"{CiamOid:D}|{IdentityPlaneMarker.External}");
        _store.Contacts[ContactA].InviteCodeHash.Should().Be(consume ? null : invite.StoredValue);
    }

    [Fact]
    public async Task TheInviteBind_NeverOverwritesABindingMadeMeanwhile()
    {
        var employee = Guid.NewGuid();
        _store.AddContact(ContactA, email: Email, oid: employee.ToString("D"), plane: IdentityPlaneMarker.Workforce);

        var write = await Binder(_store).BindInvitedContactAsync(ContactA, etag: null, CiamOid, consumeInviteCode: false, CancellationToken.None);

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

        var write = await Binder(_store).BindInvitedContactAsync(ContactA, etag: null, CiamOid, consumeInviteCode: false, CancellationToken.None);

        write.Status.Should().Be(StoreWriteStatus.Failed);
        write.Error.Should().Be(ContactBindingDecision.DenyBindingColumnMasked);
        _store.Contacts[ContactA].Oid.Should().BeNull();
    }
}
