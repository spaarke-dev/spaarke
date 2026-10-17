using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// spaarke-SPA-external-access-platform-r3 task 036 (FR-07) — <c>/grant</c> to an already-onboarded external contact
/// emails them a portal deep link, once, and only when the grant GAVE access. Through the PRODUCTION grant handler, grant
/// core and notifier over the task-142 harness's doubles; the email service is the harness's recording double (no
/// mailbox). The notifier's own branches (no email address, not onboarded, the granter, a failed send, the link rules)
/// are the unit tests' (<c>GrantAccessNotifierExternalTests</c>); these are the endpoint's "access gained" decision —
/// same-level re-grant, expiry-only change, lower level, organization grant — and the failure leaving the grant.
/// KEEP path: <c>tests/integration/auth/**</c>.
/// </summary>
public class GrantExternalContactEmailTests
{
    private const string CallerOid = "0a0a0a0a-1111-2222-3333-444444444444";
    private const string MatterName = "Smith v. Smith";

    private readonly Harness _h = new();
    private readonly Guid _matter = Guid.NewGuid();

    public GrantExternalContactEmailTests()
    {
        _h.Store.Root(ExternalGrantRootType.Matter, _matter);
        _h.Grants.RootNames[_matter] = MatterName;
    }

    private static HttpContext Context() => new DefaultHttpContext
    {
        TraceIdentifier = "trace-036",
        User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("oid", CallerOid), new Claim("name", "Gina Granter") }, "Test")),
    };

    /// <summary>An active external contact that signed in before (a CIAM oid is bound) and has an email address.</summary>
    private Guid OnboardedContact(string email = "partner@outside.test")
    {
        var contact = _h.Contact(oid: Guid.NewGuid().ToString("D"));
        _h.Identities.Contacts[contact].Email = email;
        return contact;
    }

    private Task<IResult> Grant(
        Guid contactId, ExternalAccessLevel level = ExternalAccessLevel.Collaborate, DateOnly? expiry = null) =>
        GrantExternalAccessEndpoint.GrantAccessAsync(
            new GrantAccessRequest(contactId, Guid.Empty, level, expiry, null, "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, new TestInfrastructure.RecordOwnershipResolverDouble(), new TestInfrastructure.TeamPrivilegeCheckerDouble(), new EverythingProbe(),
            _h.Materializer, _h.Notifier, _h.Invites, Context(), NullLogger<Program>.Instance, _h.Time, CancellationToken.None);

    private static GrantAccessResponse OkBody(IResult result) =>
        result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!;

    private string Link => $"https://external.test/?customer=acme&record=matter:{_matter:D}";

    [Fact]
    public async Task Grant_ToAnOnboardedExternalContact_OnANewRecord_SendsExactlyOneEmail_WithThePortalLink()
    {
        var contact = OnboardedContact();

        var body = OkBody(await Grant(contact));

        body.NotificationFailed.Should().BeFalse();
        var sent = _h.Emails.Sent.Should().ContainSingle().Subject;
        sent.To.Should().Be("partner@outside.test");
        sent.LinkUrl.Should().Be(Link);
        sent.Subject.Should().Contain(MatterName);
        _h.SentNotifications.Should().BeEmpty("an external contact has no notification bell; the email is its notice");
    }

    [Fact]
    public async Task Grant_Again_AtTheSameLevel_SendsNoSecondEmail()
    {
        var contact = OnboardedContact();
        await Grant(contact);
        _h.Emails.Sent.Clear();

        OkBody(await Grant(contact)).NotificationFailed.Should().BeFalse();

        _h.Emails.Sent.Should().BeEmpty("they already held this level");
    }

    [Fact]
    public async Task Grant_ChangingOnlyTheExpiry_SendsNoEmail()
    {
        var contact = OnboardedContact();
        _h.Grants.Seed(ExternalGrantRootType.Matter, _matter, contact, null, (int)ExternalAccessLevel.Collaborate, Today.AddDays(60));

        OkBody(await Grant(contact, ExternalAccessLevel.Collaborate, Today.AddDays(80)));

        _h.Grants.ActiveRowsOf(_matter, contact).Single().ExpiresDate.Should().Be(Today.AddDays(80));
        _h.Emails.Sent.Should().BeEmpty("they already held this level; only the date moved");
    }

    [Fact]
    public async Task Grant_ALowerLevel_SendsNoEmail()
    {
        var contact = OnboardedContact();
        _h.Grants.Seed(ExternalGrantRootType.Matter, _matter, contact, null, (int)ExternalAccessLevel.FullAccess, Today.AddDays(60));

        await Grant(contact, ExternalAccessLevel.ViewOnly);

        _h.Emails.Sent.Should().BeEmpty("a lower level gains nothing");
    }

    [Fact]
    public async Task Grant_RaisingTheLevel_OrRestoringALapsedGrant_SendsOneEmailEach()
    {
        var raised = OnboardedContact("raised@outside.test");
        _h.Grants.Seed(ExternalGrantRootType.Matter, _matter, raised, null, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(60));
        var restored = OnboardedContact("restored@outside.test");
        _h.Grants.Seed(ExternalGrantRootType.Matter, _matter, restored, null, (int)ExternalAccessLevel.Collaborate, Today.AddDays(-1));

        await Grant(raised, ExternalAccessLevel.Collaborate);
        await Grant(restored, ExternalAccessLevel.Collaborate);

        _h.Emails.Sent.Select(s => s.To).Should().BeEquivalentTo(new[] { "raised@outside.test", "restored@outside.test" });
    }

    [Fact]
    public async Task Grant_ToAnOrganization_SendsNoEmail()
    {
        var organization = Guid.NewGuid();

        var result = await GrantExternalAccessEndpoint.GrantAccessAsync(
            new GrantAccessRequest(Guid.Empty, Guid.Empty, ExternalAccessLevel.Collaborate, null, organization, "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, new TestInfrastructure.RecordOwnershipResolverDouble(), new TestInfrastructure.TeamPrivilegeCheckerDouble(), new EverythingProbe(),
            _h.Materializer, _h.Notifier, _h.Invites, Context(), NullLogger<Program>.Instance, _h.Time, CancellationToken.None);

        OkBody(result).NotificationFailed.Should().BeFalse();
        _h.Grants.ActiveRowsOf(_matter, organizationId: organization).Should().ContainSingle();
        _h.Emails.Sent.Should().BeEmpty("an organization grant fans out to nobody");
    }

    [Fact]
    public async Task Grant_ToAContactNotYetOnboarded_SendsNoEmail_TheInvitePathHandlesIt()
    {
        var contact = _h.Contact(); // no oid bound
        _h.Identities.Contacts[contact].Email = "new@outside.test";

        var body = OkBody(await Grant(contact));

        body.NotificationFailed.Should().BeFalse();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        _h.Emails.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_WhenTheEmailCannotBeSent_KeepsTheGrant_AndSaysSo()
    {
        var contact = OnboardedContact();
        _h.Emails.Failure = new HttpRequestException("Simulated shared-mailbox failure.");

        var body = OkBody(await Grant(contact));

        body.NotificationFailed.Should().BeTrue();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("a failed email never undoes the grant")
            .Which.AccessLevel.Should().Be((int)ExternalAccessLevel.Collaborate);
        _h.Emails.Sent.Should().BeEmpty();
    }

    /// <summary>A caller who holds every right on the record, so the grant ceiling passes.</summary>
    private sealed class EverythingProbe : CallerRecordAccessProbe
    {
        public EverythingProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        public override Task<Spaarke.Dataverse.AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
            => Task.FromResult(Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.Write
                               | Spaarke.Dataverse.AccessRights.Append | Spaarke.Dataverse.AccessRights.AppendTo
                               | Spaarke.Dataverse.AccessRights.Share | Spaarke.Dataverse.AccessRights.Delete);
    }
}
