using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 185 slice E (owner round 98, option (c); design note §12 I-1 to I-3, I-5) — an internal
/// user's <c>/grant</c> to an UNBOUND contact with an email sends the CIAM invite, with its code, through the ONE shared invite
/// core. Through the PRODUCTION grant handler, grant core, invite service (with the REAL binder) and notifier over the
/// task-142 harness's doubles; the CIAM provisioner and the mailbox are recording doubles (no Graph, no Key Vault).
/// KEEP path: <c>tests/integration/auth/**</c>.
/// </summary>
public class GrantSendsInviteTests
{
    private const string Email = "new@outside.test";
    private const string PortalUrl = "https://external.test";
    private const string CustomerKey = "acme";

    private readonly Harness _h = new();
    private readonly Guid _matter = Guid.NewGuid();

    public GrantSendsInviteTests()
    {
        _h.Store.Root(ExternalGrantRootType.Matter, _matter);
        _h.Grants.RootNames[_matter] = "Smith v. Smith";
    }

    private static HttpContext Context() => new DefaultHttpContext
    {
        TraceIdentifier = "trace-185e",
        User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("oid", "0a0a0a0a-1111-2222-3333-444444444444"), new Claim("name", "Gina Granter") }, "Test")),
    };

    /// <summary>An active contact typed in by an internal user: an email, no CIAM account bound.</summary>
    private Guid UnboundContact(string? email = Email)
    {
        var contact = _h.Contact();
        _h.Identities.Contacts[contact].Email = email;
        return contact;
    }

    private Task<IResult> Grant(Guid contactId, ExternalAccessLevel level = ExternalAccessLevel.Collaborate) =>
        GrantExternalAccessEndpoint.GrantAccessAsync(
            new GrantAccessRequest(contactId, Guid.Empty, level, null, null, "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, new TestInfrastructure.RecordOwnershipResolverDouble(), new TestInfrastructure.TeamPrivilegeCheckerDouble(), new EverythingProbe(),
            _h.Materializer, _h.Notifier, _h.Invites, Context(), NullLogger<Program>.Instance, _h.Time, CancellationToken.None);

    private static GrantAccessResponse OkBody(IResult result) =>
        result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!;

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

    private static ODataError ObjectConflictOnProxyAddresses() => new()
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

    // ── An unbound contact with an email is invited ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_ToAnUnboundContactWithAnEmail_SendsTheInvite_NewAccount_OneEmailWithAValidCode()
    {
        var contact = UnboundContact();

        var body = OkBody(await Grant(contact));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.Sent));
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("the grant is written first");
        _h.Ciam.Created.Should().ContainSingle().Which.Email.Should().Be(Email);
        _h.Emails.Onboarding.Should().ContainSingle().Which.Should().Be(Email);
        TheOneInviteCode();
        _h.Identities.Writes.Should().Contain(w => w.Op == "invite-marker" && w.RowId == contact,
            "the marker and the code's hash are written in one write before the account is made");
        _h.Identities.Contacts[contact].InvitedOn.Should().NotBeNull();
        _h.Identities.Contacts[contact].Oid.Should().NotBeNull("the new account's oid is bound in the same request");
        _h.Emails.Sent.Should().BeEmpty("I-3: the invite replaces the \"you were given access\" email — one email per person");
    }

    /// <summary>
    /// §12 I-10 (owner round 98c): the grant response answers <c>Sent</c>, exactly as for a new account, so the inviter cannot
    /// learn that the email already has a CIAM account. The operator log still records the ExistingAccount branch, without
    /// the code or its hash. MUTATION: answering the branch in <c>invite.status</c> fails this test.
    /// </summary>
    [Fact]
    public async Task Grant_ToAnUnboundContactWithAnExistingCiamAccount_SendsTheExistingAccountEmail_CodeMatchesTheStoredHash()
    {
        var contact = UnboundContact();
        var log = new BindingLogCapture<ExternalInviteService>();
        _h.InviteLogger = log;
        _h.Ciam.Failure = ObjectConflictOnProxyAddresses();

        var body = OkBody(await Grant(contact));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.Sent), "the inviter sees the new-account answer");
        log.Messages.Should().Contain(m => m.Contains("ExistingAccount") && m.Contains(contact.ToString()),
            "the operator log keeps the branch");
        _h.Emails.ExistingAccount.Should().ContainSingle().Which.Should().Be(Email);
        _h.Emails.Onboarding.Should().BeEmpty();
        var code = TheOneInviteCode();
        var stored = _h.Identities.Contacts[contact].InviteCodeHash;
        stored.Should().NotBeNull().And.HaveLength(InviteCode.StoredValueLength);
        InviteCode.Matches(stored, code, contact).Should().BeTrue("the emailed code is the one whose hash was stored");
        log.Messages.Should().NotContain(m => m.Contains(code, StringComparison.Ordinal) || m.Contains(stored!, StringComparison.Ordinal),
            "never log a code or a hash");
        _h.Identities.Contacts[contact].Oid.Should().BeNull("the contact stays pending until its first sign-in");
        _h.Emails.Sent.Should().BeEmpty("one email per person");

        // The first CIAM sign-in from that email, with the emailed code, binds the contact (E11's marker leg).
        var oid = Guid.NewGuid();
        var signIn = await IdentityBindingTestKit.Binder(_h.Identities)
            .ResolveCiamCallerAsync(oid.ToString(), Email, code, CancellationToken.None);
        signIn.ContactId.Should().Be(contact);
    }

    // ── No email, bound, current invite ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_ToAnUnboundContactWithNoEmail_IsNoEmail_AndSendsNoInvite()
    {
        var contact = UnboundContact(email: null);

        var body = OkBody(await Grant(contact));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.NoEmail));
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Emails.InviteUrls.Should().BeEmpty();
        _h.Identities.Contacts[contact].InvitedOn.Should().BeNull("no marker is written for a contact nobody can invite");
    }

    [Fact]
    public async Task Grant_ToABoundContact_IsNotNeeded_AndKeepsTheExistingNotification()
    {
        var contact = _h.Contact(oid: Guid.NewGuid().ToString("D"));
        _h.Identities.Contacts[contact].Email = "partner@outside.test";

        var body = OkBody(await Grant(contact));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.NotNeeded));
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Emails.InviteUrls.Should().BeEmpty();
        _h.Emails.Sent.Should().ContainSingle("a bound contact keeps the \"you were given access\" email")
            .Which.To.Should().Be("partner@outside.test");
    }

    [Fact]
    public async Task Grant_ToAContactAnInternalUserLinksTo_IsNotNeeded_AndTheUserIsToldInApp()
    {
        var (contact, user) = _h.LinkedContact();
        _h.Identities.Contacts[contact].Email = "person@firm.example";

        var body = OkBody(await Grant(contact));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.NotNeeded));
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Identities.Contacts[contact].Flag.Should().BeNull("a grant never flags a contact; only an explicit Invite does");
        _h.SentNotifications.Should().ContainSingle().Which.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>("ownerid").Id
            .Should().Be(user);
    }

    [Fact]
    public async Task Grant_ToAContactWithACurrentInvite_IsAlreadyPending_TheHashIsUnchanged()
    {
        var contact = UnboundContact();
        var invitedOn = _h.Time.GetUtcNow().AddDays(-29);
        _h.Identities.Contacts[contact].InvitedOn = invitedOn;
        _h.Identities.Contacts[contact].InviteCodeHash = "v1.pending-hash-of-the-earlier-invite";
        _h.Identities.Contacts[contact].InvitedEmailHash = "v1:pending-stamp-of-the-earlier-invite";

        var body = OkBody(await Grant(contact));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.AlreadyPending));
        _h.Identities.Contacts[contact].InviteCodeHash.Should().Be("v1.pending-hash-of-the-earlier-invite",
            "I-2: a new code would break the link the person may be about to open");
        _h.Identities.Contacts[contact].InvitedOn.Should().Be(invitedOn);
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Emails.InviteUrls.Should().BeEmpty();
    }

    /// <summary>
    /// Slice B2, MUST FIRE: a current work-account invite (it names its organisation's tenant and carries no code) is
    /// pending too. A grant keeps it: no portal-account invite replaces it, no CIAM account, no code, the tenant stays.
    /// MUTATION: requiring a code hash alone in <c>AssessAsync</c> sends a portal-account invite here.
    /// </summary>
    [Fact]
    public async Task Grant_ToAContactWithACurrentWorkAccountInvite_IsAlreadyPending_AndKeepsIt()
    {
        var contact = UnboundContact();
        var invitedOn = _h.Time.GetUtcNow().AddDays(-2);
        var row = _h.Identities.Contacts[contact];
        row.InvitedOn = invitedOn;
        row.InvitedEmailHash = InviteEmailStamp.Create("partner@outside.test");
        row.InvitedTenantId = IdentityBindingTestKit.CustomerTenant.ToString("D");

        var body = OkBody(await Grant(contact));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.AlreadyPending));
        (row.InviteCodeHash, row.InvitedTenantId, row.InvitedOn)
            .Should().Be(((string?)null, IdentityBindingTestKit.CustomerTenant.ToString("D"), (DateTimeOffset?)invitedOn));
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Emails.InviteUrls.Should().BeEmpty();
    }

    /// <summary>
    /// #2005 F2: every bind consumes the code hash and leaves the marker, so a contact an operator unbinds after a bind holds a
    /// current marker with no code anyone can use. That is not a pending invite: the grant sends a new code, and the first
    /// CIAM sign-in with it binds. MUTATION: dropping the hash check from <c>AssessAsync</c> answers AlreadyPending, sends no
    /// code, and the person is denied <c>contact_not_invited</c> for up to 30 days.
    /// </summary>
    [Fact]
    public async Task Grant_ToAContactWithACurrentMarkerWhoseCodeWasConsumed_InvitesAgain_WithANewCode()
    {
        var contact = UnboundContact();
        var invitedOn = _h.Time.GetUtcNow().AddDays(-2);
        _h.Identities.Contacts[contact].InvitedOn = invitedOn;
        _h.Identities.Contacts[contact].InviteCodeHash = null;
        _h.Ciam.Failure = ObjectConflictOnProxyAddresses();

        var body = OkBody(await Grant(contact));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.Sent));
        var code = TheOneInviteCode();
        var stored = _h.Identities.Contacts[contact].InviteCodeHash;
        stored.Should().NotBeNull("a new code's hash is stored");
        InviteCode.Matches(stored, code, contact).Should().BeTrue("the emailed code is the one whose hash was stored");
        _h.Identities.Contacts[contact].InvitedOn.Should().NotBe(invitedOn, "the re-invite restarts the marker");

        var oid = Guid.NewGuid();
        var signIn = await IdentityBindingTestKit.Binder(_h.Identities)
            .ResolveCiamCallerAsync(oid.ToString(), Email, code, CancellationToken.None);
        signIn.ContactId.Should().Be(contact, "the new code binds at the first sign-in");
    }

    /// <summary>
    /// A current invite written before the invite stamp existed (marker and code, no email hash) binds nobody, so it is not
    /// a pending invite: the grant sends a new invite, which writes the stamp, and its code then binds.
    /// </summary>
    [Fact]
    public async Task Grant_ToAContactWithACurrentInviteButNoStamp_InvitesAgain_AndStampsIt()
    {
        var contact = UnboundContact();
        _h.Identities.Contacts[contact].InvitedOn = _h.Time.GetUtcNow().AddDays(-2);
        _h.Identities.Contacts[contact].InviteCodeHash = "v1.pending-hash-of-an-invite-before-the-stamp";
        _h.Identities.Contacts[contact].InvitedEmailHash = null;
        _h.Ciam.Failure = ObjectConflictOnProxyAddresses();

        var body = OkBody(await Grant(contact));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.Sent));
        InviteEmailStamp.Matches(_h.Identities.Contacts[contact].InvitedEmailHash, Email).Should().BeTrue();
        var code = TheOneInviteCode();
        var signIn = await IdentityBindingTestKit.Binder(_h.Identities)
            .ResolveCiamCallerAsync(Guid.NewGuid().ToString(), Email, code, CancellationToken.None);
        signIn.ContactId.Should().Be(contact);
    }

    [Fact]
    public async Task Grant_ToAContactWhoseInviteHasExpired_InvitesAgain_WithANewCode()
    {
        var contact = UnboundContact();
        _h.Identities.Contacts[contact].InvitedOn = _h.Time.GetUtcNow().AddDays(-31);
        _h.Identities.Contacts[contact].InviteCodeHash = "v1.expired";
        _h.Ciam.Failure = ObjectConflictOnProxyAddresses();

        var body = OkBody(await Grant(contact));

        body.Invite!.Status.Should().Be(GrantInviteStatus.Sent, "I-10: the inviter is never told the account existed");
        _h.Identities.Contacts[contact].InviteCodeHash.Should().NotBe("v1.expired");
        TheOneInviteCode();
    }

    [Fact]
    public async Task Grant_Twice_ToTheSameUnboundContact_InvitesOnce_NoContactGetsTwoEmails()
    {
        var contact = UnboundContact();
        _h.Ciam.Failure = ObjectConflictOnProxyAddresses();

        OkBody(await Grant(contact, ExternalAccessLevel.ViewOnly)).Invite!.Status.Should().Be(GrantInviteStatus.Sent);
        OkBody(await Grant(contact, ExternalAccessLevel.Collaborate)).Invite!.Status.Should().Be(GrantInviteStatus.AlreadyPending);

        _h.Emails.InviteUrls.Should().ContainSingle();
        _h.Emails.Sent.Should().BeEmpty();
    }

    /// <summary>
    /// The modal's Retry for a <c>/grant</c> whose invite did not complete: the grant already exists, so Retry calls
    /// <c>/invite</c> for the person, which sends the invite and writes no grant. MUST FIRE for each way the invite fails
    /// and leaves the contact unbound: the organisation check, the CIAM call, and the existing-account email.
    /// </summary>
    [Theory]
    [InlineData("organisation-lookup")]
    [InlineData("ciam-outage")]
    [InlineData("existing-account-email-not-sent")]
    public async Task Invite_AfterAGrantWhoseInviteFailed_SendsTheInvite_AndWritesNoGrant(string failure)
    {
        var contact = UnboundContact();
        var discovery = FailTheInvite(failure);

        OkBody(await Grant(contact)).Invite!.Status.Should().Be(GrantInviteStatus.Failed);
        var grantWrites = _h.Grants.WriteCount;
        _h.Emails.InviteUrls.Should().BeEmpty();

        // The cause clears; staff press Retry.
        ClearTheFailure(discovery, failure);
        var retried = await InviteRoute();

        retried.Should().BeOfType<Ok<InviteExternalUserResponse>>().Subject.Value!.EmailSent.Should().BeTrue(
            "Retry is what sends the invite");
        _h.Grants.WriteCount.Should().Be(grantWrites, "the grant already exists, and /invite writes none");
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle()
            .Which.AccessLevel.Should().Be((int)ExternalAccessLevel.Collaborate);
        TheOneInviteCode();
    }

    /// <summary>
    /// The same Retry as a second <c>/grant</c> at the same level: it writes no grant, and it sends the invite only when the
    /// failure wrote nothing (the organisation check). That is why the modal's Retry calls <c>/invite</c> instead.
    /// </summary>
    [Fact]
    public async Task Grant_Again_AfterItsOrganisationCheckFailed_WritesNoGrant_AndSendsTheInvite()
    {
        var contact = UnboundContact();
        var discovery = FailTheInvite("organisation-lookup");

        OkBody(await Grant(contact)).Invite!.Status.Should().Be(GrantInviteStatus.Failed);
        var grantWrites = _h.Grants.WriteCount;

        ClearTheFailure(discovery, "organisation-lookup");
        OkBody(await Grant(contact)).Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.Sent));

        _h.Grants.WriteCount.Should().Be(grantWrites, "a same-level re-grant on an existing grant writes nothing");
        TheOneInviteCode();
    }

    private InviteOrganizationTestKit.DiscoveryHandler FailTheInvite(string failure)
    {
        // The organisation decision a customer-tenant deployment makes; a scripted discovery endpoint answers each lookup.
        var discovery = new InviteOrganizationTestKit.DiscoveryHandler();
        _h.Organizations = InviteOrganizationTestKit.Resolver(
            discovery, new InviteOrganizationTestKit.RecordingTenantCache(), new[] { Guid.NewGuid() });
        switch (failure)
        {
            case "organisation-lookup":
                discovery.Answer(System.Net.HttpStatusCode.InternalServerError, "{}");
                break;
            case "ciam-outage":
                discovery.Answer(System.Net.HttpStatusCode.BadRequest, InviteOrganizationTestKit.TenantNotFoundBody);
                _h.Ciam.Failure = new HttpRequestException("Simulated Graph outage.");
                break;
            default:
                discovery.Answer(System.Net.HttpStatusCode.BadRequest, InviteOrganizationTestKit.TenantNotFoundBody);
                _h.Ciam.Failure = ObjectConflictOnProxyAddresses();
                _h.Emails.InviteEmailFailure = new HttpRequestException(
                    "Simulated mailbox throttling.", null, System.Net.HttpStatusCode.TooManyRequests);
                break;
        }

        return discovery;
    }

    /// <summary>The cause clears. A person who already has a CIAM account still has it.</summary>
    private void ClearTheFailure(InviteOrganizationTestKit.DiscoveryHandler discovery, string failure)
    {
        discovery.Answer(System.Net.HttpStatusCode.BadRequest, InviteOrganizationTestKit.TenantNotFoundBody);
        if (failure == "ciam-outage")
            _h.Ciam.Failure = null;
        _h.Emails.InviteEmailFailure = null;
    }

    /// <summary>The /invite ROUTE handler (private; reached by reflection, as ExistingCiamAccountInviteTests does).</summary>
    private async Task<IResult> InviteRoute()
    {
        var handler = typeof(InviteExternalUserEndpoint).GetMethod(
                          "InviteExternalUserAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                      ?? throw new InvalidOperationException("The /invite handler was not found.");
        var portal = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ExternalAccess:PortalUrl"] = PortalUrl })
            .Build();
        var task = (Task<IResult>)handler.Invoke(null, new object[]
        {
            new InviteExternalUserRequest(
                Email, Guid.Empty, (int)ExternalAccessLevel.Collaborate, null, null, null, null, "matter", _matter),
            _h.Grants, _h.Participations, _h.Ciam, _h.Emails, IdentityBindingTestKit.Binder(_h.Identities), _h.Organizations,
            portal, Sprk.Bff.Api.Tests.TestInfrastructure.TestSpeOwnership.Customer(CustomerKey), Context(),
            NullLogger<Program>.Instance, CancellationToken.None,
        })!;
        return await task;
    }

    // ── A failed invite never undoes the grant ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_WhenTheInviteFails_StillCommitsTheGrant_AndSaysFailed_LoggingNoCodeOrHash()
    {
        var contact = UnboundContact();
        var log = new BindingLogCapture<ExternalInviteService>();
        _h.InviteLogger = log;
        _h.Ciam.Failure = new HttpRequestException("Simulated Graph outage.");

        var body = OkBody(await Grant(contact));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.Failed, GrantInviteStatus.InviteFailedReasonCode));
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("I-1: the grant stands")
            .Which.AccessLevel.Should().Be((int)ExternalAccessLevel.Collaborate);
        _h.Emails.InviteUrls.Should().BeEmpty();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning);

        var stored = _h.Identities.Contacts[contact].InviteCodeHash!;
        foreach (var part in new[] { stored }.Concat(stored.Split(':', '.').Where(p => p.Length >= 16)))
            log.Messages.Should().NotContain(m => m.Contains(part, StringComparison.Ordinal), "never log a hash");
    }

    /// <summary>
    /// Task 185 adversarial pass 2, F2 (pins the grant flow's existing behaviour): an invite email that did not send is a
    /// failed invite — <c>Failed</c> + <c>email_not_sent</c> — on both branches; the grant stands.
    /// </summary>
    [Theory]
    [InlineData("new-account")]
    [InlineData("existing-account")]
    public async Task Grant_WhenTheInviteEmailFails_StillCommitsTheGrant_AndSaysFailed_EmailNotSent(string branch)
    {
        var contact = UnboundContact();
        if (branch == "existing-account")
            _h.Ciam.Failure = ObjectConflictOnProxyAddresses();
        _h.Emails.InviteEmailFailure = new HttpRequestException(
            "Simulated mailbox throttling.", null, System.Net.HttpStatusCode.TooManyRequests);

        var body = OkBody(await Grant(contact));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.Failed, GrantInviteStatus.EmailNotSentReasonCode));
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("I-1: the grant stands");
        _h.Emails.InviteUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_WhenTheMarkerCannotBeWritten_StillCommitsTheGrant_AndSaysFailed_WithTheInvitesCode()
    {
        var contact = UnboundContact();
        _h.Identities.MarkerWriteFailure = StoreWriteStatus.Failed;

        var body = OkBody(await Grant(contact));

        body.Invite.Should().Be(new GrantInviteResult(GrantInviteStatus.Failed, ContactBindingDecision.InviteMarkerNotWritten));
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        _h.Ciam.Attempts.Should().BeEmpty("the marker comes first; nothing is created without it");
        _h.Emails.InviteUrls.Should().BeEmpty();
    }

    /// <summary>
    /// Task 185 slice F (ADR-010 path C): a host with no <c>Ciam:*</c> configuration. The REAL provisioner and CIAM Graph client
    /// factory are built over that empty configuration — they construct (validation moved to first use) — and the invite
    /// service is given the provisioner directly. The grant is still written; the invite is answered Failed with
    /// <c>ciam_not_configured</c>, and nothing is marked, coded or emailed.
    /// </summary>
    [Fact]
    public async Task Grant_OnAHostWithNoCiamConfiguration_StillCommitsTheGrant_AndSaysFailed_CiamNotConfigured()
    {
        var contact = UnboundContact();
        var invites = _h.InvitesOver(_h.Identities, NoCiamProvisioner());

        var result = await GrantExternalAccessEndpoint.GrantAccessAsync(
            new GrantAccessRequest(contact, Guid.Empty, ExternalAccessLevel.Collaborate, null, null, "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, new TestInfrastructure.RecordOwnershipResolverDouble(), new TestInfrastructure.TeamPrivilegeCheckerDouble(), new EverythingProbe(),
            _h.Materializer, _h.Notifier, invites, Context(), NullLogger<Program>.Instance, _h.Time, CancellationToken.None);

        OkBody(result).Invite.Should().Be(
            new GrantInviteResult(GrantInviteStatus.Failed, GrantInviteStatus.CiamNotConfiguredReasonCode));
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("I-1: the grant stands");
        _h.Emails.InviteUrls.Should().BeEmpty();
        _h.Identities.Contacts[contact].InvitedOn.Should().BeNull("nothing is marked before the CIAM check");
        _h.Identities.Contacts[contact].InviteCodeHash.Should().BeNull("no code is made");
    }

    [Fact]
    public async Task Grant_ToAnOrganization_CarriesNoInvite()
    {
        var organization = Guid.NewGuid();

        var result = await GrantExternalAccessEndpoint.GrantAccessAsync(
            new GrantAccessRequest(Guid.Empty, Guid.Empty, ExternalAccessLevel.Collaborate, null, organization, "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, new TestInfrastructure.RecordOwnershipResolverDouble(), new TestInfrastructure.TeamPrivilegeCheckerDouble(), new EverythingProbe(),
            _h.Materializer, _h.Notifier, _h.Invites, Context(), NullLogger<Program>.Instance, _h.Time, CancellationToken.None);

        OkBody(result).Invite.Should().BeNull();
        _h.Ciam.Attempts.Should().BeEmpty();
    }

    // ── /invite-and-grant keeps inviting once ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task InviteAndGrant_ToAnUnboundContact_InvitesExactlyOnce_NoSecondInviteFromTheGrant()
    {
        var contact = UnboundContact();

        var result = await InviteAndGrantExternalUserEndpoint.InviteAndGrantAsync(
            new InviteExternalUserRequest(Email, Guid.Empty, (int)ExternalAccessLevel.Collaborate, "Pat", "Partner", null, null,
                "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, new TestInfrastructure.RecordOwnershipResolverDouble(), new TestInfrastructure.TeamPrivilegeCheckerDouble(), new EverythingProbe(),
            _h.Ciam, _h.Emails, IdentityBindingTestKit.Binder(_h.Identities), InviteOrganizationTestKit.PortalOnly(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ExternalAccess:PortalUrl"] = PortalUrl })
                .Build(),
            Sprk.Bff.Api.Tests.TestInfrastructure.TestSpeOwnership.Customer(CustomerKey),
            _h.Materializer, _h.Notifier, Context(), NullLogger<Program>.Instance, _h.Time, CancellationToken.None);

        result.Should().BeOfType<Ok<InviteAndGrantResponse>>().Subject.Value!.ContactId.Should().Be(contact);
        _h.Ciam.Attempts.Should().ContainSingle();
        TheOneInviteCode();
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
