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
            _h.Grants, _h.Participations, _h.AccessibleRecords, new EverythingProbe(),
            _h.Materializer, _h.Notifier, Context(), NullLogger<Program>.Instance, _h.Time, CancellationToken.None);

    private Task<IResult> Share(Guid user, ExternalAccessLevel level = ExternalAccessLevel.Collaborate) =>
        InternalShareEndpoints.ShareAsync(
            new ShareRecordWithUserRequest("matter", _matter, user, level),
            _h.Shares, _h.Grants, _h.Participations, _h.Cache.Mock.Object, new EverythingProbe(), _h.Children, _h.Guard,
            Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.InheritanceOverNothing(), _h.Materializer,
            _h.Notifier, Context(), NullLogger<Program>.Instance, CancellationToken.None);

    private static T OkBody<T>(IResult result) => result.Should().BeOfType<Ok<T>>().Subject.Value!;

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
    public async Task Grant_ToAContactThatRepresentsAnInternalUser_TellsThatUserOnce_WithALinkToTheRecord()
    {
        var (contact, user) = _h.LinkedContact();

        var body = OkBody<GrantAccessResponse>(await Grant(contact));

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

    /// <summary>Beyond the named cases: the grant's "access gained" rule is separate code from the share's.</summary>
    [Fact]
    public async Task Grant_AgainAtTheSameLevel_DoesNotTellThemAgain()
    {
        var (contact, _) = _h.LinkedContact();
        await Grant(contact);

        OkBody<GrantAccessResponse>(await Grant(contact)).NotificationFailed.Should().BeFalse();

        _h.SentNotifications.Should().ContainSingle("the re-grant changed nothing");
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
    public async Task Grant_ToAContactWhoseUserIsWalledOffTheSecureRecord_DoesNotNameTheRecordToThem()
    {
        _h.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        var (contact, user) = _h.LinkedContact();
        _h.DenyList.DenySystemUserOnRecord(user, _matter);

        var body = OkBody<GrantAccessResponse>(await Grant(contact));

        body.NotificationFailed.Should().BeFalse("a walled user is not someone to be told, so nothing failed");
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
        return (contact, user);
    }

    [Fact]
    public async Task Grant_ToAContactBoundToTheOidOfAnUnlinkedUser_TellsThatUser()
    {
        var (contact, user) = OidOnlyContact(Guid.NewGuid());

        OkBody<GrantAccessResponse>(await Grant(contact)).NotificationFailed.Should().BeFalse();

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

    /// <summary>A leak path: the user's link names ANOTHER contact, so this contact's grant is not theirs, whatever the oid.</summary>
    [Fact]
    public async Task Grant_ToAContactCarryingTheOidOfAUserLinkedToAnotherContact_TellsNobody()
    {
        var (contact, _) = OidOnlyContact(Guid.NewGuid(), userLinkedTo: Guid.NewGuid());

        OkBody<GrantAccessResponse>(await Grant(contact)).NotificationFailed.Should().BeFalse();

        _h.SentNotifications.Should().BeEmpty();
    }

    // ── When a grant gives access ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_OverALapsedGrantAtTheSameLevel_RestoresIt_AndTellsThem()
    {
        var (contact, user) = _h.LinkedContact();
        _h.Grants.Seed(ExternalGrantRootType.Matter, _matter, contact, null, (int)ExternalAccessLevel.Collaborate,
            Today.AddDays(-1));

        OkBody<GrantAccessResponse>(await Grant(contact)).NotificationFailed.Should().BeFalse();

        OwnerOf(_h.SentNotifications.Should().ContainSingle("the lapsed grant gave nothing; the restore gives access").Subject)
            .Should().Be(user);
    }

    [Fact]
    public async Task Grant_RaisingTheLevel_TellsThem()
    {
        var (contact, user) = _h.LinkedContact();
        _h.Grants.Seed(ExternalGrantRootType.Matter, _matter, contact, null, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(60));

        await Grant(contact, ExternalAccessLevel.Collaborate);

        var sent = _h.SentNotifications.Should().ContainSingle().Subject;
        OwnerOf(sent).Should().Be(user);
        sent.GetAttributeValue<string>("body").Should().Be("Gina Granter gave you Collaborate access.");
    }

    [Fact]
    public async Task Grant_ChangingOnlyTheExpiry_TellsNobody()
    {
        var (contact, _) = _h.LinkedContact();
        _h.Grants.Seed(ExternalGrantRootType.Matter, _matter, contact, null, (int)ExternalAccessLevel.Collaborate, Today.AddDays(60));

        var result = await GrantExternalAccessEndpoint.GrantAccessAsync(
            new GrantAccessRequest(contact, Guid.Empty, ExternalAccessLevel.Collaborate, Today.AddDays(80), null, "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, new EverythingProbe(),
            _h.Materializer, _h.Notifier, Context(), NullLogger<Program>.Instance, _h.Time, CancellationToken.None);

        OkBody<GrantAccessResponse>(result);
        _h.Grants.ActiveRowsOf(_matter, contact).Single().ExpiresDate.Should().Be(Today.AddDays(80));
        _h.SentNotifications.Should().BeEmpty("they already held this level; only the date moved");
    }

    [Fact]
    public async Task Grant_ToAnOrganization_TellsNobody()
    {
        var organization = Guid.NewGuid();

        var result = await GrantExternalAccessEndpoint.GrantAccessAsync(
            new GrantAccessRequest(Guid.Empty, Guid.Empty, ExternalAccessLevel.Collaborate, null, organization, "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, new EverythingProbe(),
            _h.Materializer, _h.Notifier, Context(), NullLogger<Program>.Instance, _h.Time, CancellationToken.None);

        OkBody<GrantAccessResponse>(result).NotificationFailed.Should().BeFalse();
        _h.Grants.ActiveRowsOf(_matter, organizationId: organization).Should().ContainSingle();
        _h.SentNotifications.Should().BeEmpty("an organization grant fans out to nobody (round 2 item 9)");
    }

    // ── Best effort ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_WhenTheNotificationCannotBeWritten_KeepsTheGrant_AndSaysSo()
    {
        var (contact, _) = _h.LinkedContact();
        _h.NotificationWriteFailure = new HttpRequestException("Simulated appnotification create failure.");

        var body = OkBody<GrantAccessResponse>(await Grant(contact));

        body.NotificationFailed.Should().BeTrue();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("a failed notification never undoes the grant")
            .Which.AccessLevel.Should().Be((int)ExternalAccessLevel.Collaborate);
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
