using FluentAssertions;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 185 (design note §12 I-9; owner round 98 option (c), I-1 to I-4, I-7) — the Assigned-To
/// materializer follows the grant flows' invite rule: a grant alone no longer binds an account (§6 (b)), so an INTERNAL
/// user's assignment of an unbound contact with an email sends the CIAM invite, with its code, through the ONE shared invite
/// core. A partner contact's assignment never invites (I-4) and tells an internal user; a background pass never invites.
/// Through the PRODUCTION materializer, grant core, invite service (with the REAL binder) and notifier over the task-142
/// harness's doubles; the CIAM provisioner and the mailbox are recording doubles (no Graph, no Key Vault).
/// KEEP path: <c>tests/integration/auth/**</c>.
/// </summary>
public class AssignedAccessSendsInviteTests
{
    private const ExternalGrantRootType Matter = ExternalGrantRootType.Matter;
    private const string Attorney1 = "sprk_assignedattorney1";
    private const string Paralegal1 = "sprk_assignedparalegal1";
    private const string Email = "assignee@outside.test";
    private const string PortalUrl = "https://external.test";
    private const string CustomerKey = "acme";

    private readonly Harness _h = new();
    private readonly Guid _matter = Guid.NewGuid();

    /// <summary>The internal user whose save or sync asked for the pass: an oid that resolves to a systemuser.</summary>
    private readonly string _internalOid = Guid.NewGuid().ToString("D");

    public AssignedAccessSendsInviteTests()
    {
        _h.Store.Root(Matter, _matter);
        _h.Grants.RootNames[_matter] = "Smith v. Smith";
        _h.Grants.SystemUserIdsByOid[Guid.Parse(_internalOid)] = Guid.NewGuid();
    }

    /// <summary>An active contact typed in by an internal user: an email (or none), no CIAM account bound.</summary>
    private Guid UnboundContact(string? email = Email)
    {
        var contact = _h.Contact();
        _h.Identities.Contacts[contact].Email = email;
        return contact;
    }

    private Task<AssignedAccessOutcome> Pass(AssignedAccessTrigger trigger, string? grantorOid)
        => _h.SyncAsync(Matter, _matter, revokeOnChange: true, trigger, grantorOid);

    private Task<AssignedAccessOutcome> InternalSync() => Pass(AssignedAccessTrigger.Sync, _internalOid);

    private static AssignedAccessEntryOutcome EntryOf(AssignedAccessOutcome outcome, Guid contact)
        => outcome.Entries.Should().ContainSingle(e => e.SubjectId == contact).Subject;

    /// <summary>The code in the ONE invite email's link, after checking the link is the portal, the customer key and the code.</summary>
    private string TheOneInviteCode()
    {
        var url = _h.Emails.InviteUrls.Should().ContainSingle("exactly one invite email").Subject;
        const string prefix = PortalUrl + "/?customer=" + CustomerKey + "#invite=";
        url.Should().StartWith(prefix);
        var code = url[prefix.Length..];
        InviteCode.IsWellFormed(code).Should().BeTrue("the link carries a 43-character base64url code");
        return code;
    }

    // ── An internal assignment of an unbound contact with an email is invited ───────────────────────────────────────

    [Theory]
    [InlineData(AssignedAccessTrigger.Sync)]
    [InlineData(AssignedAccessTrigger.Inline)]
    public async Task InternalAssignment_OfAnUnboundContactWithAnEmail_SendsOneInvite_WithAValidCode_AndWritesTheMarkerAndHash(
        AssignedAccessTrigger trigger)
    {
        var contact = UnboundContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        _h.Ciam.Failure = ExistingAccountConflict();

        var outcome = await Pass(trigger, _internalOid);

        outcome.Complete.Should().BeTrue();
        EntryOf(outcome, contact).Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.Sent),
            "§12 I-10: the materializer's invite field never says the account existed");
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("the grant is written first");
        _h.Emails.ExistingAccount.Should().ContainSingle().Which.Should().Be(Email);
        var code = TheOneInviteCode();
        var stored = _h.Identities.Contacts[contact].InviteCodeHash;
        stored.Should().NotBeNull().And.HaveLength(InviteCode.StoredValueLength);
        InviteCode.Matches(stored, code, contact).Should().BeTrue("the emailed code is the one whose hash was stored");
        _h.Identities.Contacts[contact].InvitedOn.Should().NotBeNull("the marker is written with the hash");
        _h.Emails.Sent.Should().BeEmpty("I-3: the invite is the contact's only email");

        // The first CIAM sign-in from that email, with the emailed code, binds the contact (E11's marker leg).
        var signIn = await IdentityBindingTestKit.Binder(_h.Identities)
            .ResolveCiamCallerAsync(Guid.NewGuid().ToString(), Email, code, CancellationToken.None);
        signIn.ContactId.Should().Be(contact);
    }

    [Fact]
    public async Task InternalAssignment_NewAccount_IsSent_OneOnboardingEmail_AndTheOidIsBound()
    {
        var contact = UnboundContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var outcome = await InternalSync();

        EntryOf(outcome, contact).Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.Sent));
        _h.Ciam.Created.Should().ContainSingle().Which.Email.Should().Be(Email);
        _h.Emails.Onboarding.Should().ContainSingle().Which.Should().Be(Email);
        TheOneInviteCode();
        _h.Identities.Writes.Should().Contain(w => w.Op == "invite-marker" && w.RowId == contact);
        _h.Identities.Contacts[contact].Oid.Should().NotBeNull();
    }

    // ── No email, bound, current invite ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InternalAssignment_OfAnUnboundContactWithNoEmail_IsNoEmail_AndSendsNoInvite()
    {
        var contact = UnboundContact(email: null);
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var outcome = await InternalSync();

        EntryOf(outcome, contact).Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.NoEmail));
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Emails.InviteUrls.Should().BeEmpty();
        _h.Identities.Contacts[contact].InvitedOn.Should().BeNull();
    }

    [Fact]
    public async Task InternalAssignment_OfABoundContact_IsNotNeeded_SendsNoInvite_AndNoOtherEmail()
    {
        var contact = _h.Contact(oid: Guid.NewGuid().ToString("D"));
        _h.Identities.Contacts[contact].Email = "partner@outside.test";
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var outcome = await InternalSync();

        EntryOf(outcome, contact).Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.NotNeeded));
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Emails.InviteUrls.Should().BeEmpty();
        _h.Emails.Sent.Should().BeEmpty("the materializer's behaviour for a bound contact is unchanged: it sends no email");
    }

    [Fact]
    public async Task InternalAssignment_OfAContactWithACurrentInvite_IsAlreadyPending_TheHashIsUnchanged()
    {
        var contact = UnboundContact();
        var invitedOn = _h.Time.GetUtcNow().AddDays(-29);
        _h.Identities.Contacts[contact].InvitedOn = invitedOn;
        _h.Identities.Contacts[contact].InviteCodeHash = "v1.pending-hash-of-the-earlier-invite";
        _h.Identities.Contacts[contact].InvitedEmailHash = "v1:pending-stamp-of-the-earlier-invite";
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var outcome = await InternalSync();

        EntryOf(outcome, contact).Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.AlreadyPending));
        _h.Identities.Contacts[contact].InviteCodeHash.Should().Be("v1.pending-hash-of-the-earlier-invite");
        _h.Identities.Contacts[contact].InvitedOn.Should().Be(invitedOn);
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Emails.InviteUrls.Should().BeEmpty();
    }

    // ── A failed invite never undoes the grant (I-1) ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InternalAssignment_WhenTheInviteFails_StillWritesTheGrant_AndSaysFailed_LoggingNoCodeOrHash()
    {
        var contact = UnboundContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        var inviteLog = new BindingLogCapture<ExternalInviteService>();
        var materializerLog = new BindingLogCapture<AssignedAccessMaterializer>();
        _h.InviteLogger = inviteLog;
        _h.Logger = materializerLog;
        _h.Ciam.Failure = new HttpRequestException("Simulated Graph outage.");

        // C5: the throw after the marker retracts the invite this call wrote; the hash it wrote is captured as it is cleared.
        string? stored = null;
        _h.Identities.BeforePendingInviteClear = c => stored = c.InviteCodeHash;

        var outcome = await InternalSync();

        outcome.Complete.Should().BeTrue("the grant is the assignment's; the invite's failure is reported on the entry");
        var entry = EntryOf(outcome, contact);
        entry.State.Should().Be(nameof(AssignedAccessState.Granted));
        entry.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.Failed, GrantInviteStatus.InviteFailedReasonCode));
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("I-1: the grant stands");
        _h.Emails.InviteUrls.Should().BeEmpty();
        inviteLog.Entries.Should().Contain(e => e.Level == LogLevel.Warning);

        stored.Should().NotBeNull("the invite wrote a code hash, then retracted it");
        _h.Identities.Contacts[contact].InviteCodeHash.Should().BeNull("a pending invite exists only if its email went out");
        var messages = inviteLog.Messages.Concat(materializerLog.Messages).ToList();
        foreach (var part in new[] { stored! }.Concat(stored!.Split(':', '.').Where(p => p.Length >= 16)))
            messages.Should().NotContain(m => m.Contains(part, StringComparison.Ordinal), "never log a hash");
    }

    // ── A partner contact's assignment never invites (I-4, I-7) ─────────────────────────────────────────────────────

    [Fact]
    public async Task PartnerAssignment_OfAnUnboundColleague_SendsNoInvite_AndTheRecordsInternalOwnerIsTold()
    {
        var partnerOid = Guid.NewGuid();
        _h.Identities.AddContact(Guid.NewGuid(), email: "partner@outside.test", oid: partnerOid.ToString("D"),
            plane: IdentityPlaneMarker.External);
        var owner = Guid.NewGuid();
        _h.Grants.RootOwners[_matter] = owner;
        var colleague = UnboundContact();
        _h.Store.Assign(Matter, _matter, Attorney1, colleague);

        var outcome = await Pass(AssignedAccessTrigger.Sync, partnerOid.ToString("D"));

        EntryOf(outcome, colleague).Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.NeedsInternalInvite));
        _h.Grants.ActiveRowsOf(_matter, colleague).Should().ContainSingle("the partner's assignment is still saved");
        _h.Ciam.Attempts.Should().BeEmpty("a contact never invites");
        _h.Emails.InviteUrls.Should().BeEmpty();
        _h.Identities.Contacts[colleague].InvitedOn.Should().BeNull();
        _h.SentNotifications.Should().ContainSingle("the internal owner is told the colleague needs an invite")
            .Which.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>("ownerid").Id.Should().Be(owner);
    }

    // ── A disabled internal user is nobody — never a partner (issue #1576) ─────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledInternalUsersAssignment_SendsNoInvite_AndIsNeverClassedAsAPartner(bool aContactCarriesTheSameOid)
    {
        // A disabled systemuser keeps its oid. Resolved, it would invite as an internal user; filtered out naively, it would
        // fall through to the contact lookup and — when a contact carries the same oid — be classed as a PARTNER (told to
        // the record's owner as "needs an internal invite"). It must be neither: nobody is behind the pass.
        var disabledOid = Guid.NewGuid();
        _h.Grants.SystemUserIdsByOid[disabledOid] = Guid.NewGuid();
        _h.Grants.DisabledSystemUserOids[disabledOid] = true;
        if (aContactCarriesTheSameOid)
        {
            _h.Identities.AddContact(Guid.NewGuid(), email: "same-oid@outside.test", oid: disabledOid.ToString("D"),
                plane: IdentityPlaneMarker.External);
        }

        _h.Grants.RootOwners[_matter] = Guid.NewGuid();
        var contact = UnboundContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var outcome = await Pass(AssignedAccessTrigger.Sync, disabledOid.ToString("D"));

        outcome.Complete.Should().BeTrue();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("the grant stands; only the invite depends on who asked");
        EntryOf(outcome, contact).Invite.Should().BeNull("nobody is behind a disabled user's pass");
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Emails.InviteUrls.Should().BeEmpty();
        _h.Identities.Contacts[contact].InvitedOn.Should().BeNull();
        _h.SentNotifications.Should().BeEmpty("a partner's 'needs an internal invite' notice would mean it was classed a partner");
    }

    // ── A background pass never invites ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AssignedAccessTrigger.Job, false)]
    [InlineData(AssignedAccessTrigger.Job, true)]
    [InlineData(AssignedAccessTrigger.Inline, false)]
    public async Task BackgroundPass_OfAnUnboundContactWithAnEmail_WritesTheGrant_ButSendsNoInvite(
        AssignedAccessTrigger trigger, bool withAnInternalOid)
    {
        // The job is never a person, even if an oid reached it; an inline pass with no caller (the secure-root inheritance,
        // a field mapping) has nobody behind it.
        var contact = UnboundContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var outcome = await Pass(trigger, withAnInternalOid ? _internalOid : null);

        outcome.Complete.Should().BeTrue();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        EntryOf(outcome, contact).Invite.Should().BeNull();
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Emails.InviteUrls.Should().BeEmpty();
        _h.Identities.Contacts[contact].InvitedOn.Should().BeNull("a background pass writes no marker and no code");
        _h.SentNotifications.Should().BeEmpty();
    }

    [Fact]
    public async Task ReMaterialization_OfAnExistingGrant_NeverReInvites_EvenOnceTheMarkerHasExpired()
    {
        var contact = UnboundContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        _h.Ciam.Failure = ExistingAccountConflict();
        await InternalSync();
        _h.Emails.InviteUrls.Should().ContainSingle();
        var firstHash = _h.Identities.Contacts[contact].InviteCodeHash;

        // 31 days later the marker is no longer current. Neither the job nor another internal sync re-invites: the grant
        // already exists, so no pass gives access fresh, and only an explicit Invite replaces the code.
        _h.Identities.Contacts[contact].InvitedOn = _h.Time.GetUtcNow().AddDays(-31);
        var job = await Pass(AssignedAccessTrigger.Job, null);
        var sync = await InternalSync();

        EntryOf(job, contact).Invite.Should().BeNull();
        EntryOf(sync, contact).Invite.Should().BeNull();
        _h.Emails.InviteUrls.Should().ContainSingle("no pass over an existing grant sends a second invite");
        _h.Ciam.Attempts.Should().ContainSingle();
        _h.Identities.Contacts[contact].InviteCodeHash.Should().Be(firstHash, "the code is not replaced");
    }

    // ── No contact gets two emails (I-3) ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AContactAssignedInTwoColumns_AndSyncedTwice_GetsExactlyOneEmail()
    {
        var contact = UnboundContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        _h.Store.Assign(Matter, _matter, Paralegal1, contact);

        await InternalSync();
        await InternalSync();

        _h.Emails.InviteUrls.Should().ContainSingle("one invite for the contact, whatever names it");
        _h.Emails.Onboarding.Should().ContainSingle();
        _h.Emails.Sent.Should().BeEmpty("no \"you were given access\" email on top of the invite");
        _h.Ciam.Attempts.Should().ContainSingle();
    }

    private static Microsoft.Graph.Models.ODataErrors.ODataError ExistingAccountConflict() => new()
    {
        ResponseStatusCode = 400,
        Error = new Microsoft.Graph.Models.ODataErrors.MainError
        {
            Code = "Request_BadRequest",
            Message = "Another object with the same value for property proxyAddresses already exists.",
            Details = new List<Microsoft.Graph.Models.ODataErrors.ErrorDetails>
            {
                new() { Code = "ObjectConflict", Target = "proxyAddresses", Message = "conflict" },
            },
        },
    };
}
