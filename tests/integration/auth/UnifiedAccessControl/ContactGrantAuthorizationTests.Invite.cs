using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
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
        h.SentNotifications.Should().BeEmpty("a team-owned record has no owning user to tell");
        h.Ciam.Attempts.Should().BeEmpty();
    }
}
