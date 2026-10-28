using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 181 (owner round 89 item 3) — an internal user is told, in the model-driven app's bell
/// and with a link to the record, when <c>/grant</c> (to a contact that represents them) or <c>/share-user</c> gives them
/// access. Through the PRODUCTION handlers, grant core, share route, notifier, No Access guard and task 100's
/// NotificationService, over the task-142 harness's doubles (ADR-038: no HTTP doubles).
/// </summary>
/// <remarks>
/// The cases are the POML's: one notification each for a linked-contact grant and a user share, linked to the record; a
/// failed notification leaves the grant and is reported; a same-level re-share does not notify; an external grant does not
/// notify. Beyond them, each justified where it stands: the same-level re-GRANT (its "gained" rule is separate code from
/// the share's), the granter (the POML's "not sent to the person granting"), a walled user (the POML's security
/// constraint), and the share route's own failure report (separate wiring from the grant's). KEEP path:
/// <c>tests/integration/auth/**</c>.
/// </remarks>
public class GrantAccessNotificationTests
{
    private const string CallerOid = "0a0a0a0a-1111-2222-3333-444444444444";
    private const string MatterName = "Smith v. Smith";

    private readonly Harness _h = new();
    private readonly Guid _matter = Guid.NewGuid();

    public GrantAccessNotificationTests()
    {
        _h.Store.Root(ExternalGrantRootType.Matter, _matter);
        _h.Grants.RootNames[_matter] = MatterName;
    }

    private static HttpContext Context() => new DefaultHttpContext
    {
        TraceIdentifier = "trace-181",
        User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("oid", CallerOid), new Claim("name", "Gina Granter") }, "Test")),
    };

    private Task<IResult> Grant(Guid contactId, ExternalAccessLevel level = ExternalAccessLevel.Collaborate) =>
        GrantExternalAccessEndpoint.GrantAccessAsync(
            new GrantAccessRequest(contactId, Guid.Empty, level, null, null, "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, new TestInfrastructure.RecordOwnershipResolverDouble(), new TestInfrastructure.TeamPrivilegeCheckerDouble(), new EverythingProbe(),
            _h.Materializer, _h.Notifier, _h.Invites, Context(), NullLogger<Program>.Instance, _h.Time, CancellationToken.None, _h.ContactShare);

    private Task<IResult> Share(Guid user, ExternalAccessLevel level = ExternalAccessLevel.Collaborate) =>
        InternalShareEndpoints.ShareAsync(
            new ShareRecordWithUserRequest("matter", _matter, user, level),
            _h.Shares, _h.Grants, _h.Participations, _h.Cache.Mock.Object, new EverythingProbe(), _h.Children, _h.Guard,
            Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.InheritanceOverNothing(), _h.Materializer,
            _h.Notifier, Context(), NullLogger<Program>.Instance, CancellationToken.None);

    private static T OkBody<T>(IResult result) => result.Should().BeOfType<Ok<T>>().Subject.Value!;

    /// <summary>
    /// Task 193: a contact linked to an internal user, and that user as the share core reads them. Since task 193 /grant
    /// SHARES with the user a contact represents (no contact grant), so the user is told through the share's notification.
    /// </summary>
    private (Guid ContactId, Guid UserId) Colleague()
    {
        var (contact, user) = _h.LinkedContact();
        _h.SystemUser(isExternal: false, id: user);
        return (contact, user);
    }

    private const int CollaborateMask = 262167; // Read + Write + Append + AppendTo + Share

    private static Guid OwnerOf(Entity notification) => notification.GetAttributeValue<EntityReference>("ownerid").Id;

    /// <summary>The URL of the notification card's one action — what the bell turns into a link.</summary>
    private static string ActionUrlOf(Entity notification)
    {
        using var data = JsonDocument.Parse(notification.GetAttributeValue<string>("data"));
        var action = data.RootElement.GetProperty("actions").EnumerateArray().Single();
        action.GetProperty("title").GetString().Should().Be("Open matter");
        return action.GetProperty("data").GetProperty("url").GetString()!;
    }

    private string RecordLink => $"/main.aspx?etn=sprk_matter&id={_matter:D}&pagetype=entityrecord";

    // ── Who is told ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_ToAContactThatRepresentsAnInternalUser_SharesWithThatUser_AndTellsThemOnce_WithALinkToTheRecord()
    {
        var (contact, user) = Colleague();

        var body = OkBody<GrantAccessResponse>(await Grant(contact));

        body.SharedWithUser!.SystemUserId.Should().Be(user, "task 193: a represented contact is shared with, not granted");
        _h.Grants.Rows.Should().BeEmpty();
        body.NotificationFailed.Should().BeFalse();
        var sent = _h.SentNotifications.Should().ContainSingle().Subject;
        OwnerOf(sent).Should().Be(user);
        sent.GetAttributeValue<string>("title").Should().Be($"You were given access to the matter \"{MatterName}\"");
        sent.GetAttributeValue<string>("body").Should().Be("Gina Granter gave you Collaborate access.");
        ActionUrlOf(sent).Should().Be(RecordLink);
    }

    [Fact]
    public async Task Share_WithAnInternalUser_TellsThatUserOnce_WithALinkToTheRecord()
    {
        var user = _h.SystemUser(isExternal: false);

        var body = OkBody<ShareRecordWithUserResponse>(await Share(user, ExternalAccessLevel.ViewOnly));

        body.NotificationFailed.Should().BeFalse();
        var sent = _h.SentNotifications.Should().ContainSingle().Subject;
        OwnerOf(sent).Should().Be(user);
        sent.GetAttributeValue<string>("title").Should().Be($"You were given access to the matter \"{MatterName}\"");
        sent.GetAttributeValue<string>("body").Should().Be("Gina Granter gave you View Only access.");
        ActionUrlOf(sent).Should().Be(RecordLink);
    }

    // ── Who is not ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_ToAnExternalContact_TellsNobody()
    {
        var contact = _h.Contact(); // represents no systemuser

        var body = OkBody<GrantAccessResponse>(await Grant(contact));

        body.NotificationFailed.Should().BeFalse();
        _h.SentNotifications.Should().BeEmpty();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
    }

    [Fact]
    public async Task Share_AgainAtTheSameLevel_DoesNotTellThemAgain()
    {
        var user = _h.SystemUser(isExternal: false);
        await Share(user);

        var again = OkBody<ShareRecordWithUserResponse>(await Share(user));

        again.Outcome.Should().Be(InternalShareEndpoints.OutcomeUnchanged);
        _h.SentNotifications.Should().ContainSingle("the second share changed nothing");
    }

    /// <summary>
    /// Beyond the named cases: a same-level re-share returns before the write, so the share's "rights gained" check is
    /// reached only by a change — a lower level is a change that gives nothing, and is not "you were given access".
    /// </summary>
    [Fact]
    public async Task Share_ToALowerLevel_DoesNotTellThem()
    {
        var user = _h.SystemUser(isExternal: false);
        await Share(user, ExternalAccessLevel.FullAccess);

        var lowered = OkBody<ShareRecordWithUserResponse>(await Share(user, ExternalAccessLevel.ViewOnly));

        lowered.Outcome.Should().Be(InternalShareEndpoints.OutcomeUpdated);
        _h.SentNotifications.Should().ContainSingle("only the first share gave access");
    }

    /// <summary>
    /// Beyond the named cases. Since task 193 a represented contact is shared with, so a same-level /grant again is the
    /// share's "unchanged" rule reached through /grant: nothing written, nobody told again.
    /// </summary>
    [Fact]
    public async Task Grant_ToARepresentedContact_AgainAtTheSameLevel_DoesNotTellThemAgain()
    {
        var (contact, _) = Colleague();
        await Grant(contact);

        OkBody<GrantAccessResponse>(await Grant(contact)).NotificationFailed.Should().BeFalse();

        _h.SentNotifications.Should().ContainSingle("the second share changed nothing");
    }

    /// <summary>Beyond the named cases: the POML's "not sent to the person granting access".</summary>
    [Fact]
    public async Task Share_WithTheGranterThemselves_TellsNobody()
    {
        var user = _h.SystemUser(isExternal: false);
        _h.Grants.SystemUserIdsByOid[Guid.Parse(CallerOid)] = user;

        OkBody<ShareRecordWithUserResponse>(await Share(user)).Outcome.Should().Be(InternalShareEndpoints.OutcomeCreated);

        _h.SentNotifications.Should().BeEmpty();
    }

    /// <summary>
    /// Beyond the named cases: the POML's security constraint. The contact grant checks the contact's No Access entries,
    /// not the user's, and the read path vetoes a walled user — so naming the record to them would be the leak.
    /// </summary>
    [Fact]
    public async Task Grant_ToAContactWhoseUserIsWalledOffTheSecureRecord_IsRefused_AndDoesNotNameTheRecordToThem()
    {
        // Task 193: the contact represents the user, so /grant shares, and the share core's No Access guard refuses a
        // walled user before anything is written (before task 193 a contact grant was written and the user not told).
        _h.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        var (contact, user) = Colleague();
        _h.DenyList.DenySystemUserOnRecord(user, _matter);

        var result = await Grant(contact);

        result.Should().BeOfType<ProblemHttpResult>();
        _h.Shares.Writes.Should().BeEmpty();
        _h.Grants.Rows.Should().BeEmpty();
        _h.SentNotifications.Should().BeEmpty();
    }

    // ── Who a contact represents: the READ path's rule (fix round, reviewers' F1) ──────────────────

    /// <summary>A user with no link, whose oid the granted contact (alone) carries, is that contact on the read path.</summary>
    private (Guid ContactId, Guid UserId) OidOnlyContact(Guid oid, Guid? userLinkedTo = null, int contactState = 0)
    {
        var contact = _h.Contact(oid: oid.ToString("D"), stateCode: contactState);
        var user = Guid.NewGuid();
        _h.Store.UsersByOid.GetOrAdd(oid, _ => new List<AssignedLinkCandidate>())
            .Add(new AssignedLinkCandidate(user, userLinkedTo, oid, false, 0, null, false));
        _h.SystemUser(isExternal: false, id: user); // task 193: as the share core reads them
        return (contact, user);
    }

    [Fact]
    public async Task Grant_ToAContactBoundToTheOidOfAnUnlinkedUser_SharesWithThatUser_AndTellsThem()
    {
        var (contact, user) = OidOnlyContact(Guid.NewGuid());

        var body = OkBody<GrantAccessResponse>(await Grant(contact));

        body.SharedWithUser!.SystemUserId.Should().Be(user, "task 193: a confirmed oid binding represents the user too");
        body.NotificationFailed.Should().BeFalse();

        OwnerOf(_h.SentNotifications.Should().ContainSingle().Subject).Should().Be(user);
    }

    [Fact]
    public async Task Grant_WhenTwoContactsCarryTheOid_TellsNobody_AndIsNotAFailure()
    {
        var oid = Guid.NewGuid();
        var (contact, _) = OidOnlyContact(oid);
        _h.Contact(oid: oid.ToString("D")); // a second contact on the same oid: ambiguous, resolves to no contact

        OkBody<GrantAccessResponse>(await Grant(contact)).NotificationFailed.Should().BeFalse();

        _h.SentNotifications.Should().BeEmpty("an ambiguous binding resolves to no contact on the read path");
    }

    [Fact]
    public async Task Grant_ToAnInactiveBoundContact_TellsNobody_AndIsNotAFailure()
    {
        var (contact, _) = OidOnlyContact(Guid.NewGuid(), contactState: 1);

        await Grant(contact);

        _h.SentNotifications.Should().BeEmpty("an inactive contact resolves for nobody");
    }

    /// <summary>The link is honoured as it is, but an inactive granted contact is nobody's: its linked user is not told.</summary>
    [Fact]
    public async Task Grant_ToAnInactiveContactWithALinkedUser_TellsNobody()
    {
        var contact = _h.Contact(stateCode: 1);
        _h.Store.UsersByLink.GetOrAdd(contact, _ => new List<AssignedLinkCandidate>())
            .Add(new AssignedLinkCandidate(Guid.NewGuid(), contact, Guid.NewGuid(), false, 0, null, false));

        await Grant(contact);

        _h.SentNotifications.Should().BeEmpty();
    }

    /// <summary>
    /// Where a binding column is not provisioned (the contact read answers ColumnMissing), the contact's state is still
    /// read the read path's way: an inactive contact's linked user is not told.
    /// </summary>
    [Fact]
    public async Task Grant_WhenTheBindingColumnIsMissing_AndTheLinkedContactIsInactive_TellsNobody()
    {
        var (contact, _) = _h.LinkedContact();
        _h.Identities.GetContactStatus = LookupStatus.ColumnMissing;
        _h.Participations.ContactStates[contact] = ContactRecordState.Inactive;

        await Grant(contact);

        _h.SentNotifications.Should().BeEmpty();
    }

    /// <summary>The positive twin, so the case above is not vacuous: an active contact's linked user is told.</summary>
    [Fact]
    public async Task Grant_WhenTheBindingColumnIsMissing_AndTheLinkedContactIsActive_SharesWithTheUser_AndTellsThem()
    {
        var (contact, user) = Colleague();
        _h.Identities.GetContactStatus = LookupStatus.ColumnMissing;

        OkBody<GrantAccessResponse>(await Grant(contact)).SharedWithUser!.SystemUserId.Should().Be(user);

        OwnerOf(_h.SentNotifications.Should().ContainSingle().Subject).Should().Be(user);
    }

    /// <summary>A leak path: the user's link names ANOTHER contact, so this contact's grant is not theirs, whatever the oid.</summary>
    [Fact]
    public async Task Grant_ToAContactCarryingTheOidOfAUserLinkedToAnotherContact_TellsNobody()
    {
        var (contact, _) = OidOnlyContact(Guid.NewGuid(), userLinkedTo: Guid.NewGuid());

        OkBody<GrantAccessResponse>(await Grant(contact)).NotificationFailed.Should().BeFalse();

        _h.SentNotifications.Should().BeEmpty();
    }

    // ── When a grant gives access ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Task 193: a represented contact holding a LAPSED contact grant is shared with instead (the share gives access, so the
    /// user is told); the old row is left as it was, never restored.
    /// </summary>
    [Fact]
    public async Task Grant_ToARepresentedContactWithALapsedGrant_SharesInstead_AndTellsThem()
    {
        var (contact, user) = Colleague();
        var lapsed = _h.Grants.Seed(ExternalGrantRootType.Matter, _matter, contact, null, (int)ExternalAccessLevel.Collaborate,
            Today.AddDays(-1));

        OkBody<GrantAccessResponse>(await Grant(contact)).SharedWithUser!.SystemUserId.Should().Be(user);

        OwnerOf(_h.SentNotifications.Should().ContainSingle("the share gives access").Subject).Should().Be(user);
        _h.Shares.MaskOf("sprk_matter", _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask);
        _h.Grants.Rows.Should().ContainSingle().Which.ExpiresDate.Should().Be(lapsed.ExpiresDate, "the lapsed row is untouched");
    }

    /// <summary>
    /// Task 193: a represented contact already holding a View Only contact grant is shared with at the asked level, and told;
    /// the contact grant row is left as it was.
    /// </summary>
    [Fact]
    public async Task Grant_ToARepresentedContactHoldingAContactGrant_SharesAtTheLevel_AndTellsThem()
    {
        var (contact, user) = Colleague();
        _h.Grants.Seed(ExternalGrantRootType.Matter, _matter, contact, null, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(60));

        await Grant(contact, ExternalAccessLevel.Collaborate);

        var sent = _h.SentNotifications.Should().ContainSingle().Subject;
        OwnerOf(sent).Should().Be(user);
        sent.GetAttributeValue<string>("body").Should().Be("Gina Granter gave you Collaborate access.");
        _h.Shares.MaskOf("sprk_matter", _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask);
        _h.Grants.Rows.Should().ContainSingle().Which.AccessLevel.Should().Be((int)ExternalAccessLevel.ViewOnly);
    }

    /// <summary>
    /// Task 193 (expiry ruling (c)): a represented contact is shared with, with no end date, whatever expiry the request
    /// carried (the answer says expiryNotApplied); the user gains access, so they are told. The old contact grant row and its
    /// date are left as they were.
    /// </summary>
    [Fact]
    public async Task Grant_ToARepresentedContact_WithAnExpiry_SharesWithNoEndDate_AndTellsThem()
    {
        var (contact, _) = Colleague();
        _h.Grants.Seed(ExternalGrantRootType.Matter, _matter, contact, null, (int)ExternalAccessLevel.Collaborate, Today.AddDays(60));

        var result = await GrantExternalAccessEndpoint.GrantAccessAsync(
            new GrantAccessRequest(contact, Guid.Empty, ExternalAccessLevel.Collaborate, Today.AddDays(80), null, "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, new TestInfrastructure.RecordOwnershipResolverDouble(), new TestInfrastructure.TeamPrivilegeCheckerDouble(), new EverythingProbe(),
            _h.Materializer, _h.Notifier, _h.Invites, Context(), NullLogger<Program>.Instance, _h.Time, CancellationToken.None, _h.ContactShare);

        OkBody<GrantAccessResponse>(result).ExpiryNotApplied.Should().BeTrue();
        _h.Grants.ActiveRowsOf(_matter, contact).Single().ExpiresDate.Should().Be(Today.AddDays(60), "the old row is untouched");
        _h.Shares.Writes.Should().NotBeEmpty();
        _h.SentNotifications.Should().ContainSingle();
    }

    [Fact]
    public async Task Grant_ToAnOrganization_TellsNobody()
    {
        var organization = Guid.NewGuid();

        var result = await GrantExternalAccessEndpoint.GrantAccessAsync(
            new GrantAccessRequest(Guid.Empty, Guid.Empty, ExternalAccessLevel.Collaborate, null, organization, "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, new TestInfrastructure.RecordOwnershipResolverDouble(), new TestInfrastructure.TeamPrivilegeCheckerDouble(), new EverythingProbe(),
            _h.Materializer, _h.Notifier, _h.Invites, Context(), NullLogger<Program>.Instance, _h.Time, CancellationToken.None, _h.ContactShare);

        OkBody<GrantAccessResponse>(result).NotificationFailed.Should().BeFalse();
        _h.Grants.ActiveRowsOf(_matter, organizationId: organization).Should().ContainSingle();
        _h.SentNotifications.Should().BeEmpty("an organization grant fans out to nobody (round 2 item 9)");
    }

    // ── Best effort ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_ToARepresentedContact_WhenTheNotificationCannotBeWritten_KeepsTheShare_AndSaysSo()
    {
        var (contact, user) = Colleague();
        _h.NotificationWriteFailure = new HttpRequestException("Simulated appnotification create failure.");

        var body = OkBody<GrantAccessResponse>(await Grant(contact));

        body.NotificationFailed.Should().BeTrue();
        _h.Shares.MaskOf("sprk_matter", _matter, DataversePrincipalRef.User(user))
            .Should().Be(CollaborateMask, "a failed notification never undoes the share");
    }

    /// <summary>Beyond the named cases: the share route reports the failure through its own wiring.</summary>
    [Fact]
    public async Task Share_WhenTheNotificationCannotBeWritten_KeepsTheShare_AndSaysSo()
    {
        var user = _h.SystemUser(isExternal: false);
        _h.NotificationWriteFailure = new HttpRequestException("Simulated appnotification create failure.");

        var body = OkBody<ShareRecordWithUserResponse>(await Share(user));

        body.NotificationFailed.Should().BeTrue();
        body.Outcome.Should().Be(InternalShareEndpoints.OutcomeCreated);
        _h.Shares.Writes.Should().ContainSingle("a failed notification never undoes the share");
    }

    /// <summary>A caller who holds every right on the record, so the grant ceiling and the share intersection pass.</summary>
    private sealed class EverythingProbe : CallerRecordAccessProbe
    {
        public EverythingProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
            => Task.FromResult(AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo
                               | AccessRights.Share | AccessRights.Delete);
    }
}
