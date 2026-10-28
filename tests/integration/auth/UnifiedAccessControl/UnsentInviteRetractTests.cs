using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Slice C2b: a pending invite exists only if its email went out. When the invite's email (the existing-account email of a
/// portal-account invite, or the work-account notice) does not send, the invite the request wrote (marker, code hash and
/// stamp) is cleared, conditional on the row version read back with that same stamp, so the next invite writes a new one
/// and sends it. A newer invite written meanwhile is never cleared; a clear that fails is logged at Error and the invite is
/// still reported "email not sent". Through the PRODUCTION invite service and binder over the task-142 harness's doubles.
/// </summary>
public class UnsentInviteRetractTests
{
    private const ExternalGrantRootType Matter = ExternalGrantRootType.Matter;
    private const string Email = "person@customer.example";
    private static readonly Guid CustomerTenant = Guid.Parse("44444444-dddd-4ddd-8ddd-444444444444");

    private readonly Harness _h = new();
    private readonly Guid _matter = Guid.NewGuid();
    private readonly InviteOrganizationTestKit.DiscoveryHandler _discovery = new();

    public UnsentInviteRetractTests()
    {
        _h.Store.Root(Matter, _matter);
        _h.Grants.RootNames[_matter] = "Smith v. Smith";
    }

    private Guid UnboundContact()
    {
        var contact = _h.Contact();
        _h.Identities.Contacts[contact].Email = Email;
        return contact;
    }

    /// <summary>The person already has a CIAM account: the invite is ExistingAccount, its email carries the code.</summary>
    private void PersonHasACiamAccount() => _h.Ciam.Failure = new ODataError
    {
        ResponseStatusCode = 400,
        Error = new MainError
        {
            Code = "Request_BadRequest",
            Message = "Another object with the same value for property proxyAddresses already exists.",
            Details = new List<ErrorDetails>
            {
                new() { Code = "ObjectConflict", Target = "proxyAddresses", Message = "conflict" },
            },
        },
    };

    private void EmailsAreWorkAccounts()
    {
        _h.Organizations = InviteOrganizationTestKit.Resolver(
            _discovery, new InviteOrganizationTestKit.RecordingTenantCache(), new[] { CustomerTenant });
        // One answer per invite: each automatic invite decides the organisation again.
        _discovery.Answer(HttpStatusCode.OK, InviteOrganizationTestKit.DiscoveryBody(CustomerTenant))
            .Answer(HttpStatusCode.OK, InviteOrganizationTestKit.DiscoveryBody(CustomerTenant));
    }

    private static void ShouldHaveNoPendingInvite(InMemoryContactIdentityStore.Contact row)
    {
        row.InvitedOn.Should().BeNull();
        row.InviteCodeHash.Should().BeNull();
        row.InvitedEmailHash.Should().BeNull();
        row.InvitedTenantId.Should().BeNull();
    }

    // ── Must fire: a failed send leaves no pending invite, so the next automatic invite sends ─────────────────────

    [Fact]
    public async Task PortalInvite_ExistingAccountEmailFails_TheInviteIsCleared_AndTheNextAutomaticInviteSends()
    {
        var contact = UnboundContact();
        PersonHasACiamAccount();
        _h.Emails.InviteEmailFailure = new HttpRequestException("mailbox unavailable");

        var first = await _h.Invites.InviteOnGrantAsync(contact, Matter, _matter);

        first.Should().Be(new GrantInviteResult(GrantInviteStatus.Failed, GrantInviteStatus.EmailNotSentReasonCode));
        ShouldHaveNoPendingInvite(_h.Identities.Contacts[contact]);

        _h.Emails.InviteEmailFailure = null;
        var second = await _h.Invites.InviteOnGrantAsync(contact, Matter, _matter);

        second.Should().Be(new GrantInviteResult(GrantInviteStatus.Sent),
            "the failed invite was not left pending, so the next invite writes a new code and sends it");
        _h.Emails.ExistingAccount.Should().ContainSingle();
        _h.Identities.Contacts[contact].InviteCodeHash.Should().NotBeNull();
    }

    [Fact]
    public async Task WorkAccountInvite_NoticeFails_TheStampIsCleared_AndTheNextAutomaticInviteStampsAndSends()
    {
        var contact = UnboundContact();
        EmailsAreWorkAccounts();
        _h.Emails.Failure = new TimeoutException("the send timed out; it may or may not have gone");

        var first = await _h.Invites.InviteOnGrantAsync(contact, Matter, _matter);

        first.Should().Be(new GrantInviteResult(GrantInviteStatus.Failed, GrantInviteStatus.EmailNotSentReasonCode));
        ShouldHaveNoPendingInvite(_h.Identities.Contacts[contact]);

        _h.Emails.Failure = null;
        var second = await _h.Invites.InviteOnGrantAsync(contact, Matter, _matter);

        second.Should().Be(new GrantInviteResult(GrantInviteStatus.WorkAccountInvited));
        _h.Emails.Sent.Should().ContainSingle("the notice is sent on the next invite");
        _h.Identities.Contacts[contact].InvitedTenantId.Should().Be(CustomerTenant.ToString("D"));
    }

    // ── Must not fire: a sent invite stays pending ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PortalInvite_ExistingAccountEmailSent_StaysPending_AndTheNextAutomaticInviteSendsNothing()
    {
        var contact = UnboundContact();
        PersonHasACiamAccount();

        (await _h.Invites.InviteOnGrantAsync(contact, Matter, _matter)).Status.Should().Be(GrantInviteStatus.Sent);
        var second = await _h.Invites.InviteOnGrantAsync(contact, Matter, _matter);

        second.Should().Be(new GrantInviteResult(GrantInviteStatus.AlreadyPending));
        _h.Emails.ExistingAccount.Should().ContainSingle();
        _h.Identities.Contacts[contact].InviteCodeHash.Should().NotBeNull();
        _h.Identities.Writes.Should().NotContain(w => w.Op == "invite-retract");
    }

    [Fact]
    public async Task WorkAccountInvite_NoticeSent_StaysPending()
    {
        var contact = UnboundContact();
        EmailsAreWorkAccounts();

        (await _h.Invites.InviteOnGrantAsync(contact, Matter, _matter)).Status.Should().Be(GrantInviteStatus.WorkAccountInvited);
        var second = await _h.Invites.InviteOnGrantAsync(contact, Matter, _matter);

        second.Should().Be(new GrantInviteResult(GrantInviteStatus.AlreadyPending));
        _h.Identities.Contacts[contact].InvitedEmailHash.Should().NotBeNull();
        _h.Identities.Writes.Should().NotContain(w => w.Op == "invite-retract");
    }

    // ── A newer invite is never cleared ────────────────────────────────────────────────────────────────────────

    private async Task<(ContactIdentityBinder Binder, Guid Contact, string Written)> AnInviteWasWritten(
        BindingLogCapture<ContactIdentityBinder>? log = null)
    {
        var contact = UnboundContact();
        var binder = IdentityBindingTestKit.Binder(_h.Identities, log);
        var row = _h.Identities.Contacts[contact];
        var mark = await binder.MarkInviteSentAsync(contact, $"W/\"{row.Version}\"", Email, null, CancellationToken.None);
        mark.Status.Should().Be(StoreWriteStatus.Written);
        return (binder, contact, mark.WrittenEmailHash!);
    }

    [Fact]
    public async Task Retract_AfterANewerInviteWasWritten_ClearsNothing()
    {
        var (binder, contact, written) = await AnInviteWasWritten();
        var row = _h.Identities.Contacts[contact];
        var newer = await binder.MarkInviteSentAsync(contact, $"W/\"{row.Version}\"", Email, null, CancellationToken.None);

        var outcome = await binder.RetractUnsentInviteAsync(contact, written, CancellationToken.None);

        outcome.Should().Be(InviteRetractOutcome.Superseded);
        row.InvitedEmailHash.Should().Be(newer.WrittenEmailHash, "the newer invite survives");
        row.InviteCodeHash.Should().NotBeNull();
    }

    [Fact]
    public async Task Retract_RaceLost_ANewerInviteLandsBetweenTheReadAndTheClear_TheNewerInviteSurvives()
    {
        var (binder, contact, written) = await AnInviteWasWritten();
        _h.Identities.BeforePendingInviteClear = c =>
        {
            // A concurrent invite writes its own stamp after the retract read the row.
            c.InvitedEmailHash = "v1:newer-invite";
            c.Version += 1000;
            _h.Identities.BeforePendingInviteClear = null;
        };

        var outcome = await binder.RetractUnsentInviteAsync(contact, written, CancellationToken.None);

        outcome.Should().Be(InviteRetractOutcome.Superseded);
        _h.Identities.Contacts[contact].InvitedEmailHash.Should().Be("v1:newer-invite");
        _h.Identities.Writes.Should().NotContain(w => w.Op == "invite-retract");
    }

    [Fact]
    public async Task Retract_OfTheInviteItself_ClearsIt()
    {
        var (binder, contact, written) = await AnInviteWasWritten();

        var outcome = await binder.RetractUnsentInviteAsync(contact, written, CancellationToken.None);

        outcome.Should().Be(InviteRetractOutcome.Retracted, "MUST NOT FIRE: an unchanged invite is this call's own");
        ShouldHaveNoPendingInvite(_h.Identities.Contacts[contact]);
    }

    // ── A clear that fails ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Retract_WhenTheClearFails_IsFailed_AndLoggedAtError()
    {
        var log = new BindingLogCapture<ContactIdentityBinder>();
        var (binder, contact, written) = await AnInviteWasWritten(log);
        _h.Identities.PendingInviteClearFailure = StoreWriteStatus.Failed;

        var outcome = await binder.RetractUnsentInviteAsync(contact, written, CancellationToken.None);

        outcome.Should().Be(InviteRetractOutcome.Failed);
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains(contact.ToString()));
    }

    [Fact]
    public async Task ExistingAccountEmailFails_AndTheClearFails_TheInviteIsStillReportedEmailNotSent()
    {
        var contact = UnboundContact();
        PersonHasACiamAccount();
        _h.Emails.InviteEmailFailure = new HttpRequestException("mailbox unavailable");
        _h.Identities.PendingInviteClearFailure = StoreWriteStatus.Failed;

        var result = await _h.Invites.InviteOnGrantAsync(contact, Matter, _matter);

        result.Should().Be(new GrantInviteResult(GrantInviteStatus.Failed, GrantInviteStatus.EmailNotSentReasonCode));
    }
}
