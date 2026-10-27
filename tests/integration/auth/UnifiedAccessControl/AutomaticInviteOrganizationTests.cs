using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Sprk.Bff.Api.Tests.Auth.SpeAdmin;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Slice B3 — an automatic invite (after a grant, <see cref="ExternalInviteService.InviteOnGrantAsync"/>, and after an
/// internal user's Assigned-To pass, <see cref="AssignedAccessMaterializer"/>) is decided from the email's domain by the
/// same <see cref="InviteOrganizationResolver.DecideAsync"/> as <c>/invite</c>: a customer-tenant domain gets a work-account
/// stamp, any other domain a portal-account invite, and a lookup with no definite answer writes no stamp and is reported.
/// Through the PRODUCTION invite service, binder and materializer over the task-142 harness's doubles; the discovery
/// endpoint is a scripted handler (no network). KEEP path: <c>tests/integration/auth/**</c>.
/// </summary>
public class AutomaticInviteOrganizationTests
{
    private const ExternalGrantRootType Matter = ExternalGrantRootType.Matter;
    private const string Attorney1 = "sprk_assignedattorney1";
    private const string Email = "person@customer.example";

    private static readonly Guid CustomerTenant = Guid.Parse("44444444-dddd-4ddd-8ddd-444444444444");

    private readonly Harness _h = new();
    private readonly Guid _matter = Guid.NewGuid();
    private readonly string _internalOid = Guid.NewGuid().ToString("D");
    private readonly InviteOrganizationTestKit.DiscoveryHandler _discovery = new();

    public AutomaticInviteOrganizationTests()
    {
        _h.Store.Root(Matter, _matter);
        _h.Grants.RootNames[_matter] = "Smith v. Smith";
        _h.Grants.SystemUserIdsByOid[Guid.Parse(_internalOid)] = Guid.NewGuid();
        _h.Organizations = InviteOrganizationTestKit.Resolver(
            _discovery, new InviteOrganizationTestKit.RecordingTenantCache(), new[] { CustomerTenant });
    }

    private Guid UnboundContact()
    {
        var contact = _h.Contact();
        _h.Identities.Contacts[contact].Email = Email;
        return contact;
    }

    private void DomainIsTheCustomerTenant() =>
        _discovery.Answer(HttpStatusCode.OK, InviteOrganizationTestKit.DiscoveryBody(CustomerTenant));

    private void DomainHasNoTenant() =>
        _discovery.Answer(HttpStatusCode.BadRequest, InviteOrganizationTestKit.TenantNotFoundBody);

    private void LookupFaults() => _discovery.Answer(HttpStatusCode.InternalServerError, "{}");

    private Task<AssignedAccessOutcome> Pass(AssignedAccessTrigger trigger, string? grantorOid)
        => _h.SyncAsync(Matter, _matter, revokeOnChange: true, trigger, grantorOid);

    // ── After a grant (InviteOnGrantAsync) ──────────────────────────────────────────────────────────────────────────

    /// <summary>MUST FIRE: a customer-tenant domain gets a work-account stamp, never a portal-account invite.</summary>
    [Fact]
    public async Task InviteOnGrant_CustomerTenantDomain_StampsAWorkAccount_AndNoPortalAccount()
    {
        var contact = UnboundContact();
        DomainIsTheCustomerTenant();

        var result = await _h.Invites.InviteOnGrantAsync(contact, Matter, _matter);

        result.Should().Be(new GrantInviteResult(GrantInviteStatus.WorkAccountInvited));
        var notice = _h.Emails.Sent.Should().ContainSingle("the work-account notice, after the stamp").Subject;
        notice.To.Should().Be(Email);
        notice.Subject.Should().Be("You've been given access to the matter \"Smith v. Smith\" in Spaarke");
        notice.LinkUrl.Should().Be("https://external.test/?customer=acme");
        var row = _h.Identities.Contacts[contact];
        row.InvitedTenantId.Should().Be(CustomerTenant.ToString("D"));
        row.InvitedEmailHash.Should().NotBeNull();
        row.InviteCodeHash.Should().BeNull("a work-account stamp carries no code");
        row.Oid.Should().BeNull();
        _h.Ciam.Attempts.Should().BeEmpty("no CIAM account for a work-account invite");
        _h.Emails.InviteUrls.Should().BeEmpty("no portal-account invite email");
        _discovery.Requests.Should().ContainSingle();
    }

    /// <summary>
    /// Slice C, MUST FIRE: the automatic invite after a grant never reaches a contact whose external access an administrator
    /// removed (bound or not), or whose marker cannot be read: Failed with the reinstate code, nothing looked up, written or
    /// sent. The grant response reports it like any failed invite.
    /// </summary>
    [Theory]
    [InlineData("removed-unbound")]
    [InlineData("removed-bound")]
    [InlineData("unreadable")]
    public async Task InviteOnGrant_RemovedContact_IsFailed_ContactRemovedAdminReinstate_AndNothingIsWritten(string how)
    {
        var contact = UnboundContact();
        switch (how)
        {
            case "removed-unbound":
                _h.Identities.Contacts[contact].RemovedOn = DateTimeOffset.UtcNow;
                break;
            case "removed-bound":
                _h.Identities.Contacts[contact].RemovedOn = DateTimeOffset.UtcNow;
                _h.Identities.Contacts[contact].Oid = Guid.NewGuid().ToString("D");
                _h.Identities.Contacts[contact].Plane = (int)IdentityPlaneMarker.External;
                break;
            default:
                _h.Identities.RemovedMarkerProof = ColumnReadProofOutcome.Unproven;
                break;
        }

        DomainIsTheCustomerTenant();

        var result = await _h.Invites.InviteOnGrantAsync(contact, Matter, _matter);

        result.Should().Be(new GrantInviteResult(GrantInviteStatus.Failed, ContactBindingDecision.InviteContactRemovedAdminReinstate));
        _discovery.Requests.Should().BeEmpty("the organisation is not even looked up");
        _h.Identities.Writes.Should().BeEmpty();
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Emails.Sent.Should().BeEmpty();
        _h.Emails.InviteUrls.Should().BeEmpty();
    }

    /// <summary>MUST NOT FIRE: any other domain gets the portal-account invite it got before.</summary>
    [Fact]
    public async Task InviteOnGrant_OtherDomain_SendsThePortalAccountInvite()
    {
        var contact = UnboundContact();
        DomainHasNoTenant();

        var result = await _h.Invites.InviteOnGrantAsync(contact);

        result.Should().Be(new GrantInviteResult(GrantInviteStatus.Sent));
        _h.Ciam.Created.Should().ContainSingle().Which.Email.Should().Be(Email);
        _h.Emails.Sent.Should().BeEmpty("a portal-account invite sends its own CIAM email, never the work-account notice");
        _h.Identities.Contacts[contact].InvitedTenantId.Should().BeNull("a portal-account invite names no tenant");
    }

    /// <summary>
    /// MUST FIRE: a lookup with no definite answer writes nothing (no stamp, no account, no email) and is answered "not
    /// sent: tenant_lookup_unavailable" in the grant response's invite result, so staff see it and retry.
    /// </summary>
    [Fact]
    public async Task InviteOnGrant_LookupFault_IsNotSent_TenantLookupUnavailable_AndNothingIsWritten()
    {
        var contact = UnboundContact();
        LookupFaults();

        var result = await _h.Invites.InviteOnGrantAsync(contact);

        result.Should().Be(new GrantInviteResult(GrantInviteStatus.Failed, ContactBindingDecision.InviteTenantLookupUnavailable));
        _h.Identities.Writes.Should().BeEmpty();
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Emails.InviteUrls.Should().BeEmpty();
        _h.Emails.Sent.Should().BeEmpty();
    }

    // ── After an internal user's Assigned-To pass ───────────────────────────────────────────────────────────────────

    /// <summary>MUST FIRE: the materializer's invite for a customer-tenant domain is a work-account stamp.</summary>
    [Fact]
    public async Task InternalAssignment_CustomerTenantDomain_StampsAWorkAccount()
    {
        var contact = UnboundContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        DomainIsTheCustomerTenant();

        var outcome = await Pass(AssignedAccessTrigger.Sync, _internalOid);

        outcome.Complete.Should().BeTrue();
        outcome.Entries.Should().ContainSingle(e => e.SubjectId == contact)
            .Which.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.WorkAccountInvited));
        _h.Identities.Contacts[contact].InvitedTenantId.Should().Be(CustomerTenant.ToString("D"));
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Emails.Sent.Should().ContainSingle().Which.Subject.Should().Contain("Smith v. Smith");
    }

    /// <summary>
    /// MUST FIRE: a notice that fails to send after an automatic work-account stamp keeps the stamp and is answered as a
    /// failed invite email (email_not_sent), so staff see it.
    /// </summary>
    [Fact]
    public async Task InviteOnGrant_WorkAccount_ANoticeThatFails_KeepsTheStamp_AndIsEmailNotSent()
    {
        var contact = UnboundContact();
        DomainIsTheCustomerTenant();
        _h.Emails.Failure = new InvalidOperationException("mailbox refused");

        var result = await _h.Invites.InviteOnGrantAsync(contact, Matter, _matter);

        result.Should().Be(new GrantInviteResult(GrantInviteStatus.Failed, GrantInviteStatus.EmailNotSentReasonCode));
        _h.Identities.Contacts[contact].InvitedTenantId.Should().Be(CustomerTenant.ToString("D"), "the stamp stays");
    }

    /// <summary>MUST NOT FIRE: the materializer's invite for any other domain is the portal-account invite.</summary>
    [Fact]
    public async Task InternalAssignment_OtherDomain_SendsThePortalAccountInvite()
    {
        var contact = UnboundContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        DomainHasNoTenant();

        var outcome = await Pass(AssignedAccessTrigger.Sync, _internalOid);

        outcome.Complete.Should().BeTrue();
        outcome.Entries.Should().ContainSingle(e => e.SubjectId == contact)
            .Which.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.Sent));
        _h.Ciam.Created.Should().ContainSingle();
        _h.Identities.Contacts[contact].InvitedTenantId.Should().BeNull();
    }

    /// <summary>
    /// MUST FIRE: on a lookup fault the grant is written, no stamp is written, the entry's invite says
    /// tenant_lookup_unavailable, and the root reports incomplete with the invite-deferred failure kind. Then two job passes
    /// over the same root make no lookup and report nothing new: a background pass never invites (I-9), so the owed invite
    /// waits for a staff Invite, not for the job.
    /// </summary>
    [Fact]
    public async Task InternalAssignment_LookupFault_WritesTheGrant_NoStamp_AndReportsInviteDeferred_WhichTheJobDoesNotRepeat()
    {
        var contact = UnboundContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        LookupFaults();
        var logs = new RecordingLoggerProvider();
        using var factory = new LoggerFactory(new[] { logs });
        _h.Logger = factory.CreateLogger<AssignedAccessMaterializer>();
        _h.InviteLogger = factory.CreateLogger<ExternalInviteService>();

        var outcome = await Pass(AssignedAccessTrigger.Sync, _internalOid);

        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("the grant stands");
        var row = _h.Identities.Contacts[contact];
        (row.InvitedOn, row.InvitedEmailHash, row.InvitedTenantId, row.InviteCodeHash)
            .Should().Be(((DateTimeOffset?)null, (string?)null, (string?)null, (string?)null), "no stamp is written");
        _h.Ciam.Attempts.Should().BeEmpty("never a portal-account fallback");
        outcome.Entries.Should().ContainSingle(e => e.SubjectId == contact).Which.Invite
            .Should().Be(new GrantInviteResult(GrantInviteStatus.Failed, ContactBindingDecision.InviteTenantLookupUnavailable));
        outcome.Complete.Should().BeFalse();
        outcome.Failures.Should().ContainSingle(f => f.Kind == AssignedAccessMaterializer.InviteDeferredFailure)
            .Which.SubjectId.Should().Be(contact);

        var linesAfterSync = logs.Lines.Count;
        var lookupsAfterSync = _discovery.Requests.Count;
        for (var tick = 0; tick < 2; tick++)
        {
            var job = await Pass(AssignedAccessTrigger.Job, grantorOid: null);
            job.Failures.Should().NotContain(f => f.Kind == AssignedAccessMaterializer.InviteDeferredFailure);
        }

        _discovery.Requests.Should().HaveCount(lookupsAfterSync, "a job pass never looks the organisation up");
        logs.Lines.Skip(linesAfterSync)
            .Should().NotContain(l => l.Level >= LogLevel.Warning && l.Message.Contains("not sent"),
                "the job repeats no failure line for the owed invite");
    }

    // ── Slice B3: a host without Ciam:* ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// MUST NOT FIRE: an automatic work-account invite needs no CIAM; MUST FIRE: an automatic portal-account invite on the
    /// same host is still Failed / ciam_not_configured with nothing written.
    /// </summary>
    [Fact]
    public async Task InviteOnGrant_NoCiamHost_SendsAWorkAccountInvite_ButNotAPortalAccountInvite()
    {
        var invites = _h.InvitesOver(_h.Identities, NoCiamProvisioner());

        var workContact = UnboundContact();
        DomainIsTheCustomerTenant();
        (await invites.InviteOnGrantAsync(workContact)).Should().Be(new GrantInviteResult(GrantInviteStatus.WorkAccountInvited));
        _h.Identities.Contacts[workContact].InvitedTenantId.Should().Be(CustomerTenant.ToString("D"));

        var portalContact = _h.Contact();
        _h.Identities.Contacts[portalContact].Email = "person@elsewhere.example";
        DomainHasNoTenant();
        (await invites.InviteOnGrantAsync(portalContact))
            .Should().Be(new GrantInviteResult(GrantInviteStatus.Failed, GrantInviteStatus.CiamNotConfiguredReasonCode));
        _h.Identities.Contacts[portalContact].InvitedOn.Should().BeNull();
    }
}
