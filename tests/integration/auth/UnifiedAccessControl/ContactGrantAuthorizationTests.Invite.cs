using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 185 slice E, §12 I-4 (OWNER, round 98b) — a contact-side grant (task 140) NEVER sends an
/// invite. A partner's grant to an unbound colleague with an email is saved, answered <c>invite.status =
/// NeedsInternalInvite</c>, and the record's internal owner is told in-app (the grant notifier's appnotification path) that
/// an internal user must send the invite. Through the PRODUCTION contact grant handler, the shared invite service's
/// read-only assessment (over this fixture's identity rows) and the PRODUCTION notifier over the task-142 harness.
/// </summary>
public partial class ContactGrantAuthorizationTests
{
    private static readonly Guid RecordOwner = Guid.Parse("5a140000-0000-0000-0000-0000000000f1");

    /// <summary>The invite service over this fixture's contacts and a notifier whose record owner is <see cref="RecordOwner"/>.</summary>
    private Harness WireInviteSeams()
    {
        var h = new Harness();
        h.Grants.RootOwners[ProjectId] = RecordOwner;
        h.Grants.RootNames[ProjectId] = "Apollo";
        Invites = h.InvitesOver(_identities);
        Notifier = h.Notifier;
        return h;
    }

    [Fact]
    public async Task ContactGrant_ToAnUnboundColleagueWithAnEmail_NeverInvites_NeedsInternalInvite_TheOwnerIsTold()
    {
        var h = WireInviteSeams();

        var body = Ok<ContactGrantResponse>(await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate)));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.NeedsInternalInvite));
        _dataverse.RowsFor(Colleague).Should().ContainSingle("the partner's grant is saved");
        h.Ciam.Attempts.Should().BeEmpty("a contact never invites: no CIAM account is made");
        h.Emails.InviteUrls.Should().BeEmpty("no invite email");
        h.Emails.Sent.Should().BeEmpty();
        _identities.Contacts[Colleague].InvitedOn.Should().BeNull("no marker and no code are written");
        _identities.Contacts[Colleague].InviteCodeHash.Should().BeNull();

        var told = h.SentNotifications.Should().ContainSingle("the record's internal owner is told").Subject;
        told.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(RecordOwner);
        told.GetAttributeValue<string>("title").Should().Contain("needs an invite").And.Contain("Apollo");
    }

    /// <summary>Only a write that gave access tells the owner: the same grant posted again is still NeedsInternalInvite, and
    /// the owner is told once.</summary>
    [Fact]
    public async Task ContactGrant_PostedTwice_TellsTheOwnerOnce()
    {
        var h = WireInviteSeams();

        await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate));
        var again = Ok<ContactGrantResponse>(await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate)));

        again.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.NeedsInternalInvite));
        ToldUser(h).Should().Be(RecordOwner, "one notice, from the write that gave access");
    }

    [Fact]
    public async Task ContactGrant_ToABoundColleague_IsNotNeeded_AndNobodyIsTold()
    {
        var h = WireInviteSeams();
        _identities.Contacts[Colleague].Oid = Guid.NewGuid().ToString("D");
        _identities.Contacts[Colleague].Plane = (int)IdentityPlaneMarker.External;

        var body = Ok<ContactGrantResponse>(await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate)));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.NotNeeded));
        h.SentNotifications.Should().BeEmpty();
        h.Ciam.Attempts.Should().BeEmpty();
    }

    [Fact]
    public async Task ContactGrant_ToAColleagueWithNoEmail_IsNoEmail_AndNobodyIsTold()
    {
        var h = WireInviteSeams();
        _identities.Contacts[Colleague].Email = null;

        var body = Ok<ContactGrantResponse>(await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate)));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.NoEmail));
        h.SentNotifications.Should().BeEmpty();
        h.Ciam.Attempts.Should().BeEmpty();
    }

    [Fact]
    public async Task ContactGrant_ToAColleagueWithACurrentInvite_IsAlreadyPending_AndNobodyIsTold()
    {
        var h = WireInviteSeams();
        _identities.Contacts[Colleague].InvitedOn = h.Time.GetUtcNow().AddDays(-3);
        _identities.Contacts[Colleague].InviteCodeHash = "v1.pending";
        _identities.Contacts[Colleague].InvitedEmailHash = "v1:pending-stamp";

        var body = Ok<ContactGrantResponse>(await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate)));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.AlreadyPending));
        _identities.Contacts[Colleague].InviteCodeHash.Should().Be("v1.pending");
        h.SentNotifications.Should().BeEmpty();
    }

    [Fact]
    public async Task ContactGrant_OnATeamOwnedRecord_StillNeedsInternalInvite_AndTheGrantStands()
    {
        var h = WireInviteSeams();
        h.Grants.RootOwners.TryRemove(ProjectId, out _);

        var body = Ok<ContactGrantResponse>(await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate)));

        body.Invite!.Status.Should().Be(GrantInviteStatus.NeedsInternalInvite);
        _dataverse.RowsFor(Colleague).Should().ContainSingle();
        h.SentNotifications.Should().BeEmpty(
            "a team-owned record with no owning user, no granter on the partner's own grant and no creator has nobody to tell");
        h.Ciam.Attempts.Should().BeEmpty();
    }

    // ── Task 185 slice F (§12 I-4): a team-owned record (secure records are) tells a fallback internal user ──────────────

    private static readonly Guid PartnerGranter = Guid.Parse("5a140000-0000-0000-0000-0000000000f2");
    private static readonly Guid RecordCreator = Guid.Parse("5a140000-0000-0000-0000-0000000000f3");

    /// <summary>The invite seams over a TEAM-owned project (no owning user), with a capturing notifier log.</summary>
    private (Harness H, BindingLogCapture<GrantAccessNotifier> Log) WireTeamOwnedInviteSeams()
    {
        var h = WireInviteSeams();
        h.Grants.RootOwners.TryRemove(ProjectId, out _);
        var log = new BindingLogCapture<GrantAccessNotifier>();
        h.NotifierLogger = log;
        Notifier = h.Notifier;
        return (h, log);
    }

    /// <summary>The internal user who granted the granting partner contact its OWN access to the project (sprk_grantedby).</summary>
    private void PartnersOwnGrantIssuedBy(Guid systemUserId)
        => _dataverse.RowsFor(Grantor).Single().GrantedBySystemUserId = systemUserId;

    private static Guid ToldUser(Harness h)
        => h.SentNotifications.Should().ContainSingle("one internal user is told").Subject
            .GetAttributeValue<EntityReference>("ownerid").Id;

    [Fact]
    public async Task ContactGrant_OnATeamOwnedRecord_TellsTheInternalUserWhoGrantedThePartnerItsOwnAccess()
    {
        var (h, _) = WireTeamOwnedInviteSeams();
        h.Grants.Person(PartnerGranter);
        h.Grants.Person(RecordCreator);
        h.Grants.RootCreators[ProjectId] = RecordCreator;
        PartnersOwnGrantIssuedBy(PartnerGranter);

        var body = Ok<ContactGrantResponse>(await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate)));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.NeedsInternalInvite));
        ToldUser(h).Should().Be(PartnerGranter, "the partner's own granter comes before the record's creator");
        h.SentNotifications.Single().GetAttributeValue<string>("title").Should().Contain("needs an invite").And.Contain("Apollo");
        h.Ciam.Attempts.Should().BeEmpty();
    }

    [Fact]
    public async Task ContactGrant_OnATeamOwnedRecord_WithNoGranterRecorded_TellsTheRecordsCreator()
    {
        var (h, _) = WireTeamOwnedInviteSeams();
        h.Grants.Person(RecordCreator);
        h.Grants.RootCreators[ProjectId] = RecordCreator;

        var body = Ok<ContactGrantResponse>(await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate)));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.NeedsInternalInvite));
        ToldUser(h).Should().Be(RecordCreator);
    }

    [Fact]
    public async Task ContactGrant_OnATeamOwnedRecord_WhoseGranterIsDisabled_TellsTheRecordsCreator()
    {
        var (h, _) = WireTeamOwnedInviteSeams();
        h.Grants.Person(PartnerGranter);
        h.Grants.SystemUsers[PartnerGranter].IsDisabled = true;
        h.Grants.Person(RecordCreator);
        h.Grants.RootCreators[ProjectId] = RecordCreator;
        PartnersOwnGrantIssuedBy(PartnerGranter);

        await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate));

        ToldUser(h).Should().Be(RecordCreator, "a disabled granter is not a person who can be told");
    }

    [Fact]
    public async Task ContactGrant_OnATeamOwnedRecord_CreatedByTheApplicationUser_WithNoGranter_TellsNobody_AndLogsAWarning()
    {
        var (h, log) = WireTeamOwnedInviteSeams();
        h.Grants.Person(RecordCreator);
        h.Grants.SystemUsers[RecordCreator].ApplicationId = Guid.NewGuid();
        h.Grants.RootCreators[ProjectId] = RecordCreator;

        var body = Ok<ContactGrantResponse>(await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate)));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.NeedsInternalInvite), "the response is unchanged");
        _dataverse.RowsFor(Colleague).Should().ContainSingle("the grant stands");
        h.SentNotifications.Should().BeEmpty("the application user is not a person");
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning, "nobody could be told: a Warning, not a throw");
    }

    [Fact]
    public async Task ContactGrant_OnAUserOwnedRecord_StillTellsTheOwner_NotThePartnersGranter()
    {
        var h = WireInviteSeams();
        h.Grants.Person(PartnerGranter);
        PartnersOwnGrantIssuedBy(PartnerGranter);

        await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate));

        ToldUser(h).Should().Be(RecordOwner, "a user-owned record keeps telling its owner, as before");
    }
}
