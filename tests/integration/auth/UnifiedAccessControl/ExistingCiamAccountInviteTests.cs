using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Graph.Models.ODataErrors;
using Moq;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Registration;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 184 (owner round 96) — the invite's pending-invite marker and its "already has a CIAM
/// account" branch, through the PRODUCTION invite core (<see cref="InviteExternalUserEndpoint.ProvisionAsync"/>, alone and
/// inside <see cref="InviteAndGrantExternalUserEndpoint"/>) over the task-142 harness's doubles and the REAL
/// <see cref="ContactIdentityBinder"/>.
/// </summary>
/// <remarks>
/// <para>Provisioning's spike S1 (ii) measured that a second Graph <c>POST /users</c> for the same email answers 400
/// <c>Request_BadRequest</c> / <c>ObjectConflict</c> on <c>proxyAddresses</c>. That is "the person already has a CIAM
/// account": the invite succeeds as <c>ExistingAccount</c>, the contact stays PENDING (marker set, no oid), the "sign in with
/// your existing account" email replaces the onboarding email, and the first CIAM sign-in binds the contact through E11's
/// intent gate. Any other Graph error keeps today's failure path.</para>
/// <para>The CIAM provisioner is a stub that throws the Graph SDK's own <see cref="ODataError"/> shape — no Graph, no Key
/// Vault. KEEP path: <c>tests/integration/auth/**</c>.</para>
/// </remarks>
public class ExistingCiamAccountInviteTests
{
    private const string Email = "partner@outside.test";
    private const string PortalUrl = "https://external.test";
    private const string CustomerKey = "acme";

    private readonly Harness _h = new();
    private readonly Guid _matter = Guid.NewGuid();
    private readonly StubCiamProvisioner _ciam = new();

    public ExistingCiamAccountInviteTests()
    {
        _h.Store.Root(ExternalGrantRootType.Matter, _matter);
        _h.Grants.RootNames[_matter] = "Smith v. Smith";
    }

    /// <summary>Graph's answer to a second POST /users with the same email (S1 ii).</summary>
    private static ODataError ObjectConflictOnProxyAddresses() => GraphError(400, "Request_BadRequest",
        "Another object with the same value for property proxyAddresses already exists.",
        ("ObjectConflict", "proxyAddresses"));

    private static ODataError GraphError(int status, string code, string message, params (string Code, string Target)[] details)
        => new()
        {
            ResponseStatusCode = status,
            Error = new MainError
            {
                Code = code,
                Message = message,
                Details = details.Select(d => new ErrorDetails { Code = d.Code, Target = d.Target, Message = message }).ToList(),
            },
        };

    /// <summary>An active contact with no CIAM account bound yet.</summary>
    private Guid UnboundContact()
    {
        var id = Guid.NewGuid();
        _h.Identities.AddContact(id, email: Email);
        return id;
    }

    private static InviteExternalUserRequest Request()
        => new(Email, Guid.Empty, (int)ExternalAccessLevel.Collaborate, "Pat", "Partner", null, null, "matter", Guid.Empty);

    private static IConfiguration PortalConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ExternalAccess:PortalUrl"] = PortalUrl })
        .Build();

    private Task<IResult> InviteAndGrant(
        ILogger<Program>? logger = null, ContactIdentityBinder? binder = null, string? invitedTenantId = null,
        InviteOrganizationResolver? organizations = null, string email = Email, bool? sendAsPortalAccount = null,
        CiamUserProvisioningService? ciam = null) =>
        InviteAndGrantExternalUserEndpoint.InviteAndGrantAsync(
            Request() with
            {
                RecordId = _matter, InvitedTenantId = invitedTenantId, Email = email, SendAsPortalAccount = sendAsPortalAccount,
            },
            _h.Grants, _h.Participations, _h.AccessibleRecords, new TestInfrastructure.RecordOwnershipResolverDouble(), new TestInfrastructure.TeamPrivilegeCheckerDouble(), new EverythingProbe(),
            ciam ?? _ciam, _h.Emails, binder ?? IdentityBindingTestKit.Binder(_h.Identities), organizations ?? InviteOrganizationTestKit.PortalOnly(),
            PortalConfig(), Sprk.Bff.Api.Tests.TestInfrastructure.TestSpeOwnership.Customer(CustomerKey),
            _h.Materializer, _h.Notifier, Context(), logger ?? NullLogger<Program>.Instance, _h.Time, CancellationToken.None);

    /// <summary>The /invite core, as the route runs it.</summary>
    private Task<InviteExternalUserEndpoint.ProvisionOutcome> Invite(
        ContactIdentityBinder? binder = null, ILogger? logger = null, Guid? workAccountTenantId = null) =>
        InviteExternalUserEndpoint.ProvisionAsync(
            Request(), _h.Grants, _ciam, _h.Emails, binder ?? IdentityBindingTestKit.Binder(_h.Identities), PortalUrl,
            CustomerKey, logger ?? NullLogger<Program>.Instance, resolution: null, CancellationToken.None, workAccountTenantId);

    /// <summary>
    /// The /invite ROUTE handler itself (private; reached by reflection so the test reads the response the route returns,
    /// not one it rebuilt).
    /// </summary>
    private async Task<IResult> InviteRoute(
        ILogger<Program>? logger = null, string? invitedTenantId = null, InviteOrganizationResolver? organizations = null,
        string email = Email, bool? sendAsPortalAccount = null, CiamUserProvisioningService? ciam = null)
    {
        var handler = typeof(InviteExternalUserEndpoint).GetMethod("InviteExternalUserAsync", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("The /invite handler was not found.");
        var task = (Task<IResult>)handler.Invoke(null, new object[]
        {
            Request() with
            {
                RecordId = _matter, InvitedTenantId = invitedTenantId, Email = email, SendAsPortalAccount = sendAsPortalAccount,
            },
            _h.Grants, _h.Participations, ciam ?? _ciam, _h.Emails,
            IdentityBindingTestKit.Binder(_h.Identities), organizations ?? InviteOrganizationTestKit.PortalOnly(), PortalConfig(),
            Sprk.Bff.Api.Tests.TestInfrastructure.TestSpeOwnership.Customer(CustomerKey), Context(),
            logger ?? NullLogger<Program>.Instance, CancellationToken.None,
        })!;
        return await task;
    }

    /// <summary>
    /// Task 185 slice D: the code the LAST invite email carried, read from its link. Asserts the link's shape
    /// (<c>{portal}/?customer={key}#invite={code}</c>, §12 I-12) and that the code matches the hash the invite stored.
    /// </summary>
    private string InviteEmailCode(Guid contact, string? storedHash = null)
    {
        var code = CodeIn(_h.Emails.InviteUrls.Should().NotBeEmpty("an invite email was sent").And.Subject.Last());
        var stored = storedHash ?? _h.Identities.Contacts[contact].InviteCodeHash;
        stored.Should().NotBeNull("the invite writes the code's hash with the marker")
            .And.HaveLength(InviteCode.StoredValueLength);
        InviteCode.Matches(stored, code, contact).Should().BeTrue("the emailed code is the one whose hash was stored");
        return code;
    }

    /// <summary>The code in an invite link, after checking the link is exactly the portal, the customer key and the code.</summary>
    private static string CodeIn(string inviteUrl)
    {
        const string prefix = PortalUrl + "/?customer=" + CustomerKey + "#invite=";
        inviteUrl.Should().StartWith(prefix);
        var code = inviteUrl[prefix.Length..];
        InviteCode.IsWellFormed(code).Should().BeTrue("the link carries a 43-character base64url code");
        return code;
    }

    /// <summary>The pieces of a stored value no log line may carry: the whole value and its salt and digest.</summary>
    private static IEnumerable<string> HashParts(string stored)
        => new[] { stored }.Concat(stored.Split(':', '.').Where(p => p.Length >= 16));

    private static HttpContext Context() => new DefaultHttpContext
    {
        TraceIdentifier = "trace-184",
        User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("oid", "0a0a0a0a-1111-2222-3333-444444444444"), new Claim("name", "Gina Granter") }, "Test")),
    };

    // ── The ExistingAccount branch ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// §12 I-10 (owner round 98c): the inviter is answered "Provisioned", the new-account status, so the response never tells
    /// an inviter that the email already has a CIAM account. The operator log records ExistingAccount. MUTATION: answering
    /// the branch in <c>onboardStatus</c> fails this test.
    /// </summary>
    [Fact]
    public async Task InviteAndGrant_OfAPersonWithACiamAccount_SucceedsAsExistingAccount_PendingContact_SignInEmailOnly()
    {
        var contact = UnboundContact();
        _ciam.Failure = ObjectConflictOnProxyAddresses();
        var log = new BindingLogCapture<Program>();

        var body = (await InviteAndGrant(log)).Should().BeOfType<Ok<InviteAndGrantResponse>>().Subject.Value!;

        body.OnboardStatus.Should().Be(InviteExternalUserEndpoint.ProvisionedStatus, "the inviter sees the new-account answer");
        log.Messages.Should().Contain(m => m.Contains(InviteExternalUserEndpoint.ExistingAccountStatus),
            "the operator log keeps the branch");
        body.ContactId.Should().Be(contact);
        _h.Identities.Contacts[contact].Oid.Should().BeNull("the existing account's oid is never looked up at invite time");
        _h.Identities.Contacts[contact].InvitedOn.Should().NotBeNull("the contact stays pending: marker set, no oid");
        _h.Emails.ExistingAccount.Should().ContainSingle().Which.Should().Be(Email);
        _h.Emails.Onboarding.Should().BeEmpty("there is no new account and no password to set");
        _h.Emails.Sent.Should().BeEmpty("one email per invite: the sign-in email is this person's notice");
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();

        // The first CIAM sign-in binds the pending contact (E11, under the intent gate; task 185: with the email's code).
        var code = InviteEmailCode(contact);
        var existingOid = Guid.NewGuid();
        var signIn = await IdentityBindingTestKit.Binder(_h.Identities)
            .ResolveCiamCallerAsync(existingOid.ToString(), Email, code, CancellationToken.None);
        signIn.ContactId.Should().Be(contact);
        _h.Identities.Contacts[contact].Oid.Should().Be(existingOid.ToString("D"));
    }

    [Fact]
    public async Task Invite_OfAPersonWithACiamAccount_SucceedsAsExistingAccount_WithNoGrant()
    {
        var contact = UnboundContact();
        _ciam.Failure = ObjectConflictOnProxyAddresses();

        var outcome = await Invite();

        outcome.Status.Should().Be(InviteExternalUserEndpoint.ExistingAccountStatus);
        outcome.Refusal.Should().BeNull();
        outcome.Failure.Should().BeNull();
        _h.Identities.Contacts[contact].InvitedOn.Should().NotBeNull();
        _h.Identities.Contacts[contact].Oid.Should().BeNull();
        _h.Emails.ExistingAccount.Should().ContainSingle();
        _h.Emails.Onboarding.Should().BeEmpty();
    }

    /// <summary>
    /// §12 I-10 (owner round 98c), the /invite ROUTE: a person who already has a CIAM account is answered "Provisioned", the
    /// same as a new account, while the existing-account email is sent and the operator log names ExistingAccount (never the
    /// code or its hash). MUTATION: returning the branch in the response's <c>status</c> fails this test.
    /// </summary>
    [Fact]
    public async Task InviteRoute_OfAPersonWithACiamAccount_AnswersLikeANewAccount_OnlyTheLogSaysExistingAccount()
    {
        var contact = UnboundContact();
        _ciam.Failure = ObjectConflictOnProxyAddresses();
        var log = new BindingLogCapture<Program>();

        var body = (await InviteRoute(log)).Should().BeOfType<Ok<InviteExternalUserResponse>>().Subject.Value!;

        body.Status.Should().Be(InviteExternalUserEndpoint.ProvisionedStatus, "the inviter is never told the account existed");
        _h.Emails.ExistingAccount.Should().ContainSingle("the person still gets the existing-account email");
        log.Messages.Should().Contain(m => m.Contains(InviteExternalUserEndpoint.ExistingAccountStatus));
        var code = InviteEmailCode(contact);
        var stored = _h.Identities.Contacts[contact].InviteCodeHash!;
        log.Messages.Should().NotContain(m => m.Contains(code, StringComparison.Ordinal) || m.Contains(stored, StringComparison.Ordinal));
    }

    [Theory]
    // MUTATION (treat every 400 as ExistingAccount): the first three rows fail.
    [InlineData(400, "Request_BadRequest", "ObjectConflict", "userPrincipalName")]
    [InlineData(400, "Request_BadRequest", "InvalidValue", "mail")]
    [InlineData(400, "Request_BadRequest", null, null)]
    [InlineData(503, "serviceUnavailable", null, null)]
    [InlineData(500, "generalException", null, null)]
    public async Task AnyOtherGraphError_KeepsTodaysFailurePath(int status, string code, string? detailCode, string? target)
    {
        var contact = UnboundContact();
        _ciam.Failure = detailCode is null
            ? GraphError(status, code, "Something went wrong.")
            : GraphError(status, code, "Something went wrong.", (detailCode, target!));

        var result = await InviteAndGrant();

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        _h.Emails.ExistingAccount.Should().BeEmpty();
        _h.Emails.Onboarding.Should().BeEmpty();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty("a failed onboarding writes no grant");
    }

    // ── The marker ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ANewAccountInvite_StampsTheMarkerBeforeTheCiamAccount_AndStillBindsTheOid()
    {
        var contact = UnboundContact();
        DateTimeOffset? markerAtCreate = null;
        _ciam.OnCreate = () => markerAtCreate = _h.Identities.Contacts[contact].InvitedOn;

        var outcome = await Invite();

        markerAtCreate.Should().NotBeNull("the marker is written BEFORE the CIAM account is created");
        outcome.Status.Should().Be("Provisioned");
        _h.Identities.Contacts[contact].Oid.Should().Be(_ciam.Created.Single().Oid.ToString("D"),
            "the marker moved the row version; the bind re-reads and still lands");
        _h.Emails.Onboarding.Should().ContainSingle();
    }

    /// <summary>
    /// Task 185 (owner Q5, design §7): the new-account path is unchanged — same request, no code presented, same status —
    /// except that its bind PATCH also clears the now-inert code hash, in the same write as the oid. Nothing is left on a
    /// bound contact for a later unbind to reopen.
    /// </summary>
    [Fact]
    public async Task ANewAccountInvite_ClearsTheCodeHashInItsOwnBindPatch()
    {
        var contact = UnboundContact();
        string? hashAtCreate = null;
        _ciam.OnCreate = () => hashAtCreate = _h.Identities.Contacts[contact].InviteCodeHash;

        var outcome = await Invite();

        outcome.Status.Should().Be("Provisioned");
        hashAtCreate.Should().NotBeNull("the marker write carried the hash, before the CIAM account");
        var oid = _ciam.Created.Single().Oid;
        _h.Identities.Writes.Where(w => w.RowId == contact).Select(w => w.Op)
            .Should().Equal("invite-marker", "bind");
        _h.Identities.Writes.Single(w => w.Op == "bind").Detail
            .Should().Be($"{oid:D}|{IdentityPlaneMarker.External}|invite-code-hash=null");
        _h.Identities.Contacts[contact].InviteCodeHash.Should().BeNull();
        _h.Identities.Contacts[contact].InvitedOn.Should().NotBeNull("the marker is never cleared");
    }

    /// <summary>The retryable 503 of an invite whose contact needed the marker and did not get it (fix round 1, F4-2).</summary>
    private static void ShouldBeMarkerNotWritten(IResult result)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable, "retryable — never a 200, never a generic 500");
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ContactBindingDecision.InviteMarkerNotWritten);
    }

    [Fact]
    public async Task AMarkerThatCannotBeWritten_StopsTheInvite_BeforeAnyCiamAccount_WithARetryable503()
    {
        var contact = UnboundContact();
        _h.Identities.MarkerWriteFailure = StoreWriteStatus.Failed;

        ShouldBeMarkerNotWritten(await InviteAndGrant());

        _ciam.Created.Should().BeEmpty("no CIAM account without the invite's own evidence");
        _h.Emails.Onboarding.Should().BeEmpty();
        _h.Emails.ExistingAccount.Should().BeEmpty();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty();
    }

    /// <summary>
    /// Customer stamps run Deploy-Release Phase 2 (the BFF) before Phase 3 (the SpaarkeMaster import that brings the
    /// column). In that window the NORMAL new-account path needs no marker — the CIAM create and the oid bind happen in
    /// the same request — so it still succeeds.
    /// </summary>
    [Theory]
    [InlineData("marker-column")]
    [InlineData("hash-column")] // task 185: the marker column is there, the hash column is not (either one missing)
    public async Task BeforeTheSchema_ANewAccountInvite_StillSucceeds_ItsOidIsBoundInTheSameRequest(string missing)
    {
        var contact = UnboundContact();
        if (missing == "marker-column")
        {
            _h.Identities.InviteMarkerStatus = LookupStatus.ColumnMissing;
        }
        else
        {
            _h.Identities.InviteCodeHashColumnMissing = true;
        }

        var body = (await InviteAndGrant()).Should().BeOfType<Ok<InviteAndGrantResponse>>().Subject.Value!;

        body.OnboardStatus.Should().Be("Provisioned");
        _ciam.Created.Should().ContainSingle();
        _h.Identities.Contacts[contact].InvitedOn.Should().BeNull();
        _h.Identities.Contacts[contact].Oid.Should().Be(_ciam.Created.Single().Oid.ToString("D"));
        _h.Identities.Writes.Single(w => w.Op == "bind").Detail.Should().NotContain("invite-code-hash",
            "task 185: with the columns missing the bind payload is exactly the plain bind");
        _h.Emails.Onboarding.Should().ContainSingle();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
    }

    /// <summary>
    /// The same window, ExistingAccount: the marker is the contact's only evidence, so an invite without it is not a
    /// success — a 200 would leave a contact no sign-in can bind, even after the column arrives. 503, no email, no grant.
    /// MUTATION PROOF: going back to "log and report ExistingAccount" fails this test and the /invite one below.
    /// </summary>
    [Fact]
    public async Task BeforeTheSchema_InviteAndGrant_OfAPersonWithACiamAccount_Is503_NoEmail_NoGrant()
    {
        var contact = UnboundContact();
        _h.Identities.InviteMarkerStatus = LookupStatus.ColumnMissing;
        _ciam.Failure = ObjectConflictOnProxyAddresses();

        ShouldBeMarkerNotWritten(await InviteAndGrant());

        _h.Emails.ExistingAccount.Should().BeEmpty("nothing to sign in to yet: the first sign-in would be refused");
        _h.Emails.Onboarding.Should().BeEmpty();
        _h.Emails.Sent.Should().BeEmpty();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty();
        _h.Identities.Contacts[contact].Oid.Should().BeNull("nothing new is bound");
    }

    [Fact]
    public async Task BeforeTheSchema_Invite_OfAPersonWithACiamAccount_IsAFailure_NotExistingAccount()
    {
        var contact = UnboundContact();
        _h.Identities.InviteMarkerStatus = LookupStatus.ColumnMissing;
        _ciam.Failure = ObjectConflictOnProxyAddresses();

        var outcome = await Invite();

        outcome.Status.Should().NotBe(InviteExternalUserEndpoint.ExistingAccountStatus);
        outcome.Failure.Should().NotBeNull();
        outcome.Failure!.ReasonCode.Should().Be(ContactBindingDecision.InviteMarkerNotWritten);
        _h.Emails.ExistingAccount.Should().BeEmpty();
        _h.Identities.Contacts[contact].Oid.Should().BeNull();
    }

    /// <summary>
    /// The oid-write-failed repair path relies on the marker too (the first sign-in email-binds the contact). With no
    /// marker and the bind failed, there is no evidence and no grant follows: 503, no email, no grant.
    /// </summary>
    [Fact]
    public async Task BeforeTheSchema_AFailedOidWrite_Is503_NoEmail_NoGrant()
    {
        var contact = UnboundContact();
        _h.Identities.InviteMarkerStatus = LookupStatus.ColumnMissing;
        _h.Identities.FailBind = true;

        ShouldBeMarkerNotWritten(await InviteAndGrant());

        _h.Emails.Onboarding.Should().BeEmpty();
        _h.Emails.ExistingAccount.Should().BeEmpty();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty();
        _h.Identities.Contacts[contact].Oid.Should().BeNull();
    }

    /// <summary>
    /// With the marker landed, a failed oid write keeps today's 500. Task 185 (owner Q6, design §6): no email was sent, so
    /// nobody holds a code and the first sign-in is denied <c>contact_not_invited</c>; a RETRIED invite answers
    /// ExistingAccount (the CIAM account exists) with a NEW code, and that code repairs the binding. Before 185 this test was
    /// <c>AFailedOidWrite_WithTheMarker_KeepsThe500_AndTheFirstSignInRepairsIt</c>. Slice D: the 500's logged reason says
    /// "retry the invite" (it said "The first CIAM sign-in repairs the binding").
    /// </summary>
    [Fact]
    public async Task AFailedOidWrite_WithTheMarker_KeepsThe500_AndARetriedInviteWithANewCodeRepairsIt()
    {
        var contact = UnboundContact();
        _h.Identities.FailBind = true;

        var result = await InviteAndGrant();

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        _h.Identities.Contacts[contact].InvitedOn.Should().NotBeNull();
        _h.Emails.Onboarding.Should().BeEmpty("no email was sent, so no code is in anyone's hands");
        _h.Identities.FailBind = false;
        var created = _ciam.Created.Single().Oid;
        var firstHash = _h.Identities.Contacts[contact].InviteCodeHash;

        var withoutCode = await IdentityBindingTestKit.Binder(_h.Identities)
            .ResolveCiamCallerAsync(created.ToString(), Email, inviteCode: null, CancellationToken.None);
        withoutCode.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited, "the first sign-in no longer repairs it");

        // The retry: the CIAM account now exists, so the invite answers ExistingAccount and sends a new code.
        _ciam.Failure = ObjectConflictOnProxyAddresses();
        (await Invite()).Status.Should().Be(InviteExternalUserEndpoint.ExistingAccountStatus);
        _h.Identities.Contacts[contact].InviteCodeHash.Should().NotBe(firstHash, "the retried invite carries a new code");
        _h.Emails.ExistingAccount.Should().ContainSingle();

        var signIn = await IdentityBindingTestKit.Binder(_h.Identities)
            .ResolveCiamCallerAsync(created.ToString(), Email, InviteEmailCode(contact), CancellationToken.None);
        signIn.ContactId.Should().Be(contact);
        _h.Identities.Contacts[contact].Oid.Should().Be(created.ToString("D"));
    }

    [Fact]
    public async Task AReInvite_OverwritesTheMarkerWithTheLatestTime()
    {
        // Coordinator design (round 96): a re-invite overwrites the marker; nothing clears it.
        var contact = UnboundContact();
        _ciam.Failure = ObjectConflictOnProxyAddresses();
        var clock = new FakeTimeProvider(IdentityBindingTestKit.Now);
        var binder = new ContactIdentityBinder(_h.Identities, IdentityBindingTestKit.Tenants(IdentityBindingTestKit.CustomerTenant),
            clock, NullLogger<ContactIdentityBinder>.Instance, IdentityBindingTestKit.NoOwnerUnderTest);

        await Invite(binder);
        var firstHash = _h.Identities.Contacts[contact].InviteCodeHash;
        clock.Advance(TimeSpan.FromDays(3));
        await Invite(binder);

        _h.Identities.Contacts[contact].InvitedOn.Should().Be(IdentityBindingTestKit.Now.AddDays(3));
        // Task 185: the re-invite replaced the code's hash in the same write (the earlier code is superseded).
        _h.Identities.Contacts[contact].InviteCodeHash.Should().NotBeNull().And.NotBe(firstHash);
    }

    // ── Task 185 slice D: the invite emails carry the code ──────────────────────────────────────────────────

    /// <summary>
    /// Both invite emails open the invite link, <c>{portal}/?customer={key}#invite={code}</c>, and its code is the one
    /// whose hash the invite stored. The new-account bind clears the hash, so that case reads the hash as it stood when the
    /// CIAM account was created (after the marker write, before the bind).
    /// MUTATION: passing the base portal URL (or no code) into either email fails this test.
    /// </summary>
    [Theory]
    [InlineData("onboarding")]
    [InlineData("existing-account")]
    public async Task BothInviteEmails_CarryALinkWhoseCodeMatchesTheStoredHash(string email)
    {
        var contact = UnboundContact();
        string? hashAtCreate = null;
        _ciam.OnCreate = () => hashAtCreate = _h.Identities.Contacts[contact].InviteCodeHash;
        if (email == "existing-account")
        {
            _ciam.Failure = ObjectConflictOnProxyAddresses();
        }

        var outcome = await Invite();

        outcome.Status.Should().Be(email == "existing-account" ? InviteExternalUserEndpoint.ExistingAccountStatus : "Provisioned");
        (email == "existing-account" ? _h.Emails.ExistingAccount : _h.Emails.Onboarding).Should().ContainSingle();
        _h.Emails.InviteUrls.Should().ContainSingle();
        InviteEmailCode(contact, hashAtCreate);
    }

    /// <summary>
    /// A re-invite sends a NEW code and the earlier link stops matching (superseded): each email's code matches only the
    /// hash its own invite stored.
    /// </summary>
    [Fact]
    public async Task AReInvite_EmailsANewCode_AndTheEarlierCodeNoLongerMatches()
    {
        var contact = UnboundContact();
        _ciam.Failure = ObjectConflictOnProxyAddresses();

        await Invite();
        var first = InviteEmailCode(contact);
        await Invite();
        var second = InviteEmailCode(contact);

        second.Should().NotBe(first);
        InviteCode.Matches(_h.Identities.Contacts[contact].InviteCodeHash, first, contact).Should().BeFalse();
    }

    /// <summary>
    /// The invite API responses never carry the code: /invite (the route handler itself) and /invite-and-grant answer the
    /// BASE portal URL, and the serialized JSON does not contain the code. Owner decision: the code goes only into the email.
    /// </summary>
    [Fact]
    public async Task TheInviteResponses_NeverCarryTheCode()
    {
        var contact = UnboundContact();
        _ciam.Failure = ObjectConflictOnProxyAddresses();

        var invite = (await InviteRoute()).Should().BeOfType<Ok<InviteExternalUserResponse>>().Subject.Value!;
        var inviteCode = InviteEmailCode(contact);
        invite.InviteRedeemUrl.Should().Be(PortalUrl, "the response carries the base portal URL, not the invite link");
        var inviteJson = JsonSerializer.Serialize(invite, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        inviteJson.Should().NotContain(inviteCode).And.NotContain("invite=");

        var grant = (await InviteAndGrant()).Should().BeOfType<Ok<InviteAndGrantResponse>>().Subject.Value!;
        var grantCode = InviteEmailCode(contact);
        grant.PortalUrl.Should().Be(PortalUrl);
        var grantJson = JsonSerializer.Serialize(grant, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        grantJson.Should().NotContain(grantCode).And.NotContain(inviteCode).And.NotContain("invite=");
    }

    /// <summary>
    /// No log line written while inviting (the endpoint core, the binder) carries the code or its stored hash, on the
    /// new-account path, the ExistingAccount path and through /invite-and-grant.
    /// </summary>
    [Theory]
    [InlineData("new-account")]
    [InlineData("existing-account")]
    [InlineData("invite-and-grant")]
    public async Task NoLogLine_CarriesTheCodeOrItsHash(string path)
    {
        var endpointLog = new BindingLogCapture<Program>();
        var binderLog = new BindingLogCapture<ContactIdentityBinder>();
        var binder = IdentityBindingTestKit.Binder(_h.Identities, binderLog);
        var contact = UnboundContact();
        string? hashAtCreate = null;
        _ciam.OnCreate = () => hashAtCreate = _h.Identities.Contacts[contact].InviteCodeHash;
        if (path != "new-account")
        {
            _ciam.Failure = ObjectConflictOnProxyAddresses();
        }

        if (path == "invite-and-grant")
        {
            (await InviteAndGrant(endpointLog, binder)).Should().BeOfType<Ok<InviteAndGrantResponse>>();
        }
        else
        {
            await Invite(binder, endpointLog);
        }

        var stored = hashAtCreate ?? _h.Identities.Contacts[contact].InviteCodeHash!;
        var secrets = HashParts(stored).Append(InviteEmailCode(contact, stored)).ToList();
        var lines = endpointLog.Messages.Concat(binderLog.Messages).ToList();
        lines.Should().NotBeEmpty("the invite logs its steps");
        foreach (var secret in secrets)
        {
            lines.Should().NotContain(line => line.Contains(secret, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Owner Q6: a failed oid write after the marker landed keeps the 500, and its reason now says "retry the invite"
    /// (no email was sent, so nobody holds a code; the first sign-in no longer repairs it).
    /// </summary>
    [Fact]
    public async Task AFailedOidWrite_WithTheMarker_ItsReasonSaysRetryTheInvite()
    {
        UnboundContact();
        _h.Identities.FailBind = true;

        var thrown = await FluentActions.Invoking(() => Invite()).Should().ThrowAsync<InvalidOperationException>();

        thrown.Which.Message.Should().Contain("retry the invite").And.NotContain("first CIAM sign-in repairs");
        _h.Emails.InviteUrls.Should().BeEmpty("no email, so no code was sent");
    }

    /// <summary>A portal URL the invite link cannot be built from (not https) stops the invite before anything is written.</summary>
    [Theory]
    [InlineData("http://external.test")]
    [InlineData("https://user:pass@external.test")]
    [InlineData("not a url")]
    public async Task AnUnusablePortalUrl_StopsTheInvite_BeforeAnyWrite(string portalUrl)
    {
        var contact = UnboundContact();

        var invoke = () => InviteExternalUserEndpoint.ProvisionAsync(
            Request(), _h.Grants, _ciam, _h.Emails, IdentityBindingTestKit.Binder(_h.Identities), portalUrl,
            CustomerKey, NullLogger<Program>.Instance, resolution: null, CancellationToken.None);

        await invoke.Should().ThrowAsync<InvalidOperationException>();
        _h.Identities.Contacts[contact].InvitedOn.Should().BeNull();
        _ciam.Created.Should().BeEmpty();
        _h.Emails.InviteUrls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://external.test", "acme", "CODE", "https://external.test/?customer=acme#invite=CODE")]
    [InlineData("https://external.test/portal/?x=1#f", "acme", "CODE", "https://external.test/portal/?customer=acme#invite=CODE")]
    [InlineData("https://external.test", null, "CODE", "https://external.test/#invite=CODE")]
    [InlineData("https://external.test", "a b", null, "https://external.test/?customer=a%20b")]
    [InlineData("https://external.test", null, null, "https://external.test/")]
    [InlineData("http://localhost:5173", "acme", "CODE", "http://localhost:5173/?customer=acme#invite=CODE")]
    [InlineData("http://external.test", "acme", "CODE", null)]
    public void BuildInviteLink_UsesOnlyTrustedConfig_TheKeyAndTheCode(string portal, string? key, string? code, string? expected)
        => InviteExternalUserEndpoint.BuildInviteLink(portal, key, code).Should().Be(expected);

    // ── Task 185 adversarial pass 2, F2: an invite email that did not send is reported ───────────────────────

    /// <summary>The shared mailbox refusing the send (a Graph 429).</summary>
    private static HttpRequestException MailboxThrottled()
        => new("Simulated mailbox throttling.", null, System.Net.HttpStatusCode.TooManyRequests);

    /// <summary>The code hash the invite stored, read when the CIAM account is created (a new account's bind then clears it).</summary>
    private string? _hashAtCreate;

    /// <summary>Sets up one of the two onboarding branches: a new CIAM account, or a person who already has one.</summary>
    private Guid ContactOnBranch(string branch)
    {
        var contact = UnboundContact();
        _ciam.OnCreate = () => _hashAtCreate = _h.Identities.Contacts[contact].InviteCodeHash;
        if (branch == "existing-account")
            _ciam.Failure = ObjectConflictOnProxyAddresses();
        return contact;
    }

    /// <summary>
    /// The /invite ROUTE, when the invite email fails to send: nobody holds the code, so the response says so —
    /// <c>emailSent: false</c> and <c>email_not_sent</c> — and §12 I-10: the WHOLE response (status code and every field) is the
    /// same on both branches. MUTATION: dropping the report on either branch fails that row.
    /// </summary>
    [Theory]
    [InlineData("new-account")]
    [InlineData("existing-account")]
    public async Task InviteRoute_WhenTheInviteEmailFails_ReportsEmailNotSent_IdenticallyOnBothBranches(string branch)
    {
        var contact = ContactOnBranch(branch);
        _h.Emails.InviteEmailFailure = MailboxThrottled();
        var log = new BindingLogCapture<Program>();

        var body = (await InviteRoute(log)).Should().BeOfType<Ok<InviteExternalUserResponse>>().Subject.Value!;

        body.Should().Be(new InviteExternalUserResponse(
            contact, PortalUrl, InviteExternalUserEndpoint.ProvisionedStatus,
            EmailSent: false, EmailErrorCode: GrantInviteStatus.EmailNotSentReasonCode));
        _h.Emails.InviteUrls.Should().BeEmpty("the send failed");
        _hashAtCreate.Should().NotBeNull("the marker and the code's hash landed before the account step");
        foreach (var part in HashParts(_hashAtCreate!))
            log.Messages.Should().NotContain(m => m.Contains(part, StringComparison.Ordinal), "never log a hash");
    }

    /// <summary>
    /// /invite-and-grant, when the invite email fails to send: the grant is written (200), and the response reports the email
    /// the same way on both branches (§12 I-10). MUTATION: dropping the report on either branch fails that row.
    /// </summary>
    [Theory]
    [InlineData("new-account")]
    [InlineData("existing-account")]
    public async Task InviteAndGrant_WhenTheInviteEmailFails_ReportsEmailNotSent_IdenticallyOnBothBranches(string branch)
    {
        var contact = ContactOnBranch(branch);
        _h.Emails.InviteEmailFailure = MailboxThrottled();
        var log = new BindingLogCapture<Program>();

        var body = (await InviteAndGrant(log)).Should().BeOfType<Ok<InviteAndGrantResponse>>().Subject.Value!;

        body.Should().Be(new InviteAndGrantResponse(
            contact, InviteExternalUserEndpoint.ProvisionedStatus, body.AccessRecordId, PortalUrl,
            GrantedAccessLevel: ExternalAccessLevel.Collaborate, Narrowed: false, NotificationFailed: false,
            EmailSent: false, EmailErrorCode: GrantInviteStatus.EmailNotSentReasonCode));
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("the grant stands");
        _h.Emails.InviteUrls.Should().BeEmpty("the send failed");
        _hashAtCreate.Should().NotBeNull("the marker and the code's hash landed before the account step");
        foreach (var part in HashParts(_hashAtCreate!))
            log.Messages.Should().NotContain(m => m.Contains(part, StringComparison.Ordinal), "never log a hash");
    }

    /// <summary>A sent invite email is reported as sent, with no error code, on both routes and both branches.</summary>
    [Theory]
    [InlineData("new-account")]
    [InlineData("existing-account")]
    public async Task InviteRoute_WhenTheInviteEmailIsSent_ReportsNoFailure_IdenticallyOnBothBranches(string branch)
    {
        var contact = ContactOnBranch(branch);

        var body = (await InviteRoute()).Should().BeOfType<Ok<InviteExternalUserResponse>>().Subject.Value!;

        body.Should().Be(new InviteExternalUserResponse(
            contact, PortalUrl, InviteExternalUserEndpoint.ProvisionedStatus, EmailSent: true, EmailErrorCode: null));
        _h.Emails.InviteUrls.Should().ContainSingle();
    }

    [Theory]
    [InlineData("new-account")]
    [InlineData("existing-account")]
    public async Task InviteAndGrant_WhenTheInviteEmailIsSent_ReportsNoFailure_IdenticallyOnBothBranches(string branch)
    {
        var contact = ContactOnBranch(branch);

        var body = (await InviteAndGrant()).Should().BeOfType<Ok<InviteAndGrantResponse>>().Subject.Value!;

        body.Should().Be(new InviteAndGrantResponse(
            contact, InviteExternalUserEndpoint.ProvisionedStatus, body.AccessRecordId, PortalUrl,
            GrantedAccessLevel: ExternalAccessLevel.Collaborate, Narrowed: false, NotificationFailed: false,
            EmailSent: true, EmailErrorCode: null));
        _h.Emails.InviteUrls.Should().ContainSingle();
    }

    // ── #2005 K1: a contact with no row version is re-read before its mark, never marked under If-Match: * ──────────

    /// <summary>
    /// The invite core over a contact that carries NO row version (the state of a contact the invite just created; also a
    /// row read without one). The resolution says it was an unbound contact to provision.
    /// </summary>
    private Task<InviteExternalUserEndpoint.ProvisionOutcome> InviteWithoutARowVersion(Guid contact) =>
        InviteExternalUserEndpoint.ProvisionAsync(
            Request(), _h.Grants, _ciam, _h.Emails, IdentityBindingTestKit.Binder(_h.Identities), PortalUrl, CustomerKey,
            NullLogger<Program>.Instance,
            new InviteContactResolution(InviteContactAction.ProvisionExisting, contact, ETag: null, null, null),
            CancellationToken.None);

    /// <summary>Nothing was marked, nothing was created or sent, and no mark was even attempted without a version.</summary>
    private void ShouldBeUnmarked(Guid contact)
    {
        _h.Identities.Contacts[contact].InvitedOn.Should().BeNull("a contact that is not unbound is never marked");
        _h.Identities.Contacts[contact].InviteCodeHash.Should().BeNull("a contact that is not unbound never holds a code hash");
        _h.Identities.Writes.Should().NotContain(w => w.Op == "invite-marker" || w.Op == "refused-unversioned-invite-marker",
            "the mark is made only under the re-read row's version, never unconditionally");
        _ciam.Created.Should().BeEmpty("no CIAM account is created for a refused invite");
        _h.Emails.InviteUrls.Should().BeEmpty("no invite email, so nobody holds a code");
    }

    /// <summary>
    /// #2005 K1: between the invite's contact create and its mark, a workforce sign-in email-binds the contact (or an
    /// internal user is linked to it). The workforce plane reads no invite marker, so a mark under <c>If-Match: *</c> put the
    /// marker and the hash on a BOUND contact, the ExistingAccount path emailed the code, and an operator unbind revived the
    /// link. Now the contact is re-read and refused exactly as an invite that resolved to it is: same reason code, same flag.
    /// MUTATION PROOF: marking under <c>*</c> (or without the re-read) fails every case.
    /// </summary>
    [Theory]
    [InlineData("workforce-bound", ContactBindingDecision.InviteWorkforceBoundContact)]
    [InlineData("linked-to-internal-user", ContactBindingDecision.InviteContactLinkedToInternalUser)]
    public async Task AContactBoundBeforeItsMark_IsNotMarked_AndTheInviteIsRefusedAsForABoundContact(string how, string reasonCode)
    {
        var contact = Guid.NewGuid();
        var workforceOid = Guid.NewGuid();
        if (how == "workforce-bound")
        {
            _h.Identities.AddContact(contact, email: Email, oid: workforceOid.ToString("D"), plane: IdentityPlaneMarker.Workforce);
        }
        else
        {
            _h.Identities.AddContact(contact, email: Email);
            _h.Identities.AddSystemUser(Guid.NewGuid(), workforceOid, Email, primaryContactId: contact);
        }

        var outcome = await InviteWithoutARowVersion(contact);

        outcome.Refusal.Should().NotBeNull("the invite is refused, as an invite of an already-bound contact is");
        outcome.Refusal!.ReasonCode.Should().Be(reasonCode);
        outcome.Failure.Should().BeNull();
        ShouldBeUnmarked(contact);
        _h.Identities.Contacts[contact].Flag.Should().NotBeNull("the collision is flagged for an operator, as today");
        _h.Identities.Contacts[contact].Flag!.Reason.Should().Be(IdentityCollisionReason.InviteMatchesWorkforceContact);
        if (how == "workforce-bound")
        {
            _h.Identities.Contacts[contact].Oid.Should().Be(workforceOid.ToString("D"), "the workforce binding is untouched");
        }
    }

    /// <summary>
    /// #2005 K1: the contact is unbound at the re-read, but changes before the conditional mark lands: the 412 refuses the
    /// mark (the invite's existing retryable 503; a retry re-resolves the email and judges the contact as it now is).
    /// </summary>
    [Fact]
    public async Task AContactChangedBetweenItsReReadAndItsMark_Is412_NotMarked_Retryable503()
    {
        var contact = UnboundContact();
        _h.Identities.AfterRead = read =>
        {
            if (read == "references") _h.Identities.Touch(_h.Identities.Contacts[contact]);
        };

        var outcome = await InviteWithoutARowVersion(contact);

        outcome.Failure.Should().NotBeNull();
        outcome.Failure!.ReasonCode.Should().Be(ContactBindingDecision.InviteMarkerNotWritten);
        _h.Identities.Contacts[contact].InvitedOn.Should().BeNull();
        _h.Identities.Contacts[contact].InviteCodeHash.Should().BeNull();
        _h.Identities.Writes.Should().NotContain(w => w.Op == "refused-unversioned-invite-marker");
        _ciam.Created.Should().BeEmpty();
        _h.Emails.InviteUrls.Should().BeEmpty();
    }

    /// <summary>#2005 K1: the re-read cannot be read: fail closed with the invite's own lookup-failure 503, nothing marked.</summary>
    [Fact]
    public async Task AReReadThatFails_FailsClosed_WithTheInviteLookupFailure_NothingMarked()
    {
        var contact = UnboundContact();
        _h.Identities.FailGetContact = true;

        var outcome = await InviteWithoutARowVersion(contact);

        outcome.Failure.Should().NotBeNull();
        outcome.Failure!.ReasonCode.Should().Be(ContactBindingDecision.InviteContactLookupFailed);
        outcome.Refusal.Should().BeNull();
        ShouldBeUnmarked(contact);
    }

    /// <summary>
    /// #2005 K1: the normal path. A contact with no row version that is still active and unbound is marked under the
    /// re-read row's version, then provisioned and bound as before (the bind consumes the hash).
    /// </summary>
    [Fact]
    public async Task AnUnboundContactWithoutARowVersion_IsMarkedUnderItsReReadVersion_AndProvisioned()
    {
        var contact = UnboundContact();
        DateTimeOffset? markerAtCreate = null;
        string? hashAtCreate = null;
        _ciam.OnCreate = () =>
        {
            markerAtCreate = _h.Identities.Contacts[contact].InvitedOn;
            hashAtCreate = _h.Identities.Contacts[contact].InviteCodeHash;
        };

        var outcome = await InviteWithoutARowVersion(contact);

        outcome.Status.Should().Be(InviteExternalUserEndpoint.ProvisionedStatus);
        markerAtCreate.Should().NotBeNull("the marker landed before the CIAM account");
        hashAtCreate.Should().NotBeNull("the hash landed with the marker");
        _h.Identities.Writes.Should().ContainSingle(w => w.Op == "invite-marker");
        _h.Identities.Writes.Should().NotContain(w => w.Op == "refused-unversioned-invite-marker");
        _h.Identities.Contacts[contact].Oid.Should().Be(_ciam.Created.Should().ContainSingle().Subject.Oid.ToString("D"));
        _h.Identities.Contacts[contact].InviteCodeHash.Should().BeNull("the bind consumed the code");
        InviteCode.Matches(hashAtCreate!, CodeIn(_h.Emails.InviteUrls.Should().ContainSingle().Subject), contact)
            .Should().BeTrue("the emailed code is the one marked");
    }

    // ── The invite stamp: written by /invite and /invite-and-grant, independent of the CIAM account branch ──────────

    private static readonly Guid PartnerTenant = IdentityBindingTestKit.CustomerTenant;

    /// <summary>
    /// A portal-account invite writes the invite stamp in the marker's write: the invited address, hashed (never the address),
    /// and NO tenant (only a work-account invite names one). With the CIAM account already existing the contact stays
    /// unbound and keeps its code and stamp, and a CIAM sign-in presenting the code then binds it.
    /// </summary>
    [Fact]
    public async Task APortalAccountInvite_WritesTheStamp_WithANullTenant_AndTheCode()
    {
        var contact = UnboundContact();
        _ciam.Failure = ObjectConflictOnProxyAddresses();

        var outcome = await Invite();

        outcome.Status.Should().Be(InviteExternalUserEndpoint.ExistingAccountStatus);
        var row = _h.Identities.Contacts[contact];
        InviteEmailStamp.Matches(row.InvitedEmailHash, Email).Should().BeTrue("the stamp records the invited address");
        row.InvitedEmailHash.Should().NotContain(Email, "only the salted hash is stored");
        row.InvitedTenantId.Should().BeNull("a portal-account invite names no tenant");
        row.InviteCodeHash.Should().NotBeNullOrEmpty("a portal-account invite carries its code");
        _h.Identities.InviteStampWrites.Should().ContainSingle().Which.InvitedTenantId.Should().BeNull();
    }

    /// <summary>/invite-and-grant writes the same portal-account stamp: no tenant.</summary>
    [Fact]
    public async Task InviteAndGrant_APortalAccountInvite_WritesTheStamp_WithANullTenant()
    {
        var contact = UnboundContact();
        _ciam.Failure = ObjectConflictOnProxyAddresses();

        (await InviteAndGrant()).Should().BeOfType<Ok<InviteAndGrantResponse>>();

        var row = _h.Identities.Contacts[contact];
        InviteEmailStamp.Matches(row.InvitedEmailHash, Email).Should().BeTrue();
        row.InvitedTenantId.Should().BeNull();
    }

    /// <summary>
    /// The stamp write does not depend on the CIAM account branch: a new-account invite writes it with the marker BEFORE the
    /// account is created, and the invite's own bind of the new oid then clears it.
    /// </summary>
    [Fact]
    public async Task ANewAccountInvite_WritesTheStampBeforeTheAccount_AndItsOwnBindClearsIt()
    {
        var contact = UnboundContact();
        string? stampAtCreate = null;
        _ciam.OnCreate = () => stampAtCreate = _h.Identities.Contacts[contact].InvitedEmailHash;

        var outcome = await Invite();

        outcome.Status.Should().Be(InviteExternalUserEndpoint.ProvisionedStatus);
        InviteEmailStamp.Matches(stampAtCreate, Email).Should().BeTrue("the stamp landed with the marker, before the account");
        _h.Identities.InviteStampWrites.Should().ContainSingle();
        var row = _h.Identities.Contacts[contact];
        row.Oid.Should().NotBeNull();
        (row.InvitedEmailHash, row.InvitedTenantId).Should().Be(((string?)null, (string?)null), "the bind clears the stamp");
    }

    /// <summary>
    /// A request never supplies the tenant: any non-blank InvitedTenantId is a 400 in each route's validation shape, before
    /// any read, write or lookup (a GUID included). MUST-NOT-FIRE: blank is accepted.
    /// </summary>
    [Theory]
    [InlineData("11111111-aaaa-4aaa-8aaa-111111111111", true)]
    [InlineData(" 11111111-AAAA-4AAA-8AAA-111111111111 ", true)]
    [InlineData("not-a-guid", true)]
    [InlineData("tenant.onmicrosoft.com", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public async Task ARequestSuppliedTenant_IsRefused400(string raw, bool refused)
    {
        UnboundContact();
        _ciam.Failure = ObjectConflictOnProxyAddresses();
        var handler = new InviteOrganizationTestKit.DiscoveryHandler();
        var organizations = InviteOrganizationTestKit.Resolver(handler, new InviteOrganizationTestKit.RecordingTenantCache(),
            new[] { PartnerTenant });
        if (!refused)
        {
            handler.Answer(HttpStatusCode.BadRequest, InviteOrganizationTestKit.TenantNotFoundBody)
                .Answer(HttpStatusCode.BadRequest, InviteOrganizationTestKit.TenantNotFoundBody);
        }

        foreach (var result in new[]
                 {
                     await InviteRoute(invitedTenantId: raw, organizations: organizations),
                     await InviteAndGrant(invitedTenantId: raw, organizations: organizations),
                 })
        {
            if (refused)
            {
                var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
                problem.StatusCode.Should().Be(400);
                problem.ProblemDetails.Detail.Should().Contain("InvitedTenantId");
            }
            else
            {
                result.Should().NotBeOfType<ProblemHttpResult>();
            }
        }

        if (refused)
        {
            _h.Identities.Writes.Should().BeEmpty("a refused request writes nothing");
            _h.Identities.Reads.Should().BeEmpty("a refused request reads nothing");
            handler.Requests.Should().BeEmpty("a refused request looks nothing up");
        }
    }

    // ── Slice B2: the server decides work account vs portal account from the email's domain ─────────────────────

    private readonly InviteOrganizationTestKit.RecordingTenantCache _orgCache = new();

    private InviteOrganizationResolver Organizations(InviteOrganizationTestKit.DiscoveryHandler handler, params Guid[] customers)
        => InviteOrganizationTestKit.Resolver(handler, _orgCache, customers.Length == 0 ? new[] { PartnerTenant } : customers,
            timeout: TimeSpan.FromMilliseconds(200));

    /// <summary>
    /// MUST FIRE: the invited email's domain belongs to a configured customer tenant, so /invite is a work-account invite:
    /// the stamp names that tenant and carries no code; no CIAM account, no oid, no plane, no email. The lookup is one GET of
    /// the fixed discovery URL with no credentials. A Workforce sign-in from that tenant with that email then binds the
    /// contact once (plane Workforce) and clears the stamp, so a second identity cannot use it.
    /// </summary>
    [Fact]
    public async Task Invite_ACustomerTenantDomain_IsAWorkAccountInvite_AndAWorkforceSignInBindsItOnce()
    {
        var contact = UnboundContact();
        var handler = new InviteOrganizationTestKit.DiscoveryHandler()
            .Answer(HttpStatusCode.OK, InviteOrganizationTestKit.DiscoveryBody(PartnerTenant));
        var creates = 0;
        _ciam.OnCreate = () => creates++;

        var result = await InviteRoute(organizations: Organizations(handler));

        var body = result.Should().BeOfType<Ok<InviteExternalUserResponse>>().Subject.Value!;
        body.Status.Should().Be(InviteExternalUserEndpoint.WorkAccountInvitedStatus);
        (body.ContactId, body.EmailSent, body.EmailErrorCode).Should().Be((contact, (bool?)true, (string?)null),
            "slice B3: the work-account notice was sent");
        var row = _h.Identities.Contacts[contact];
        row.InvitedTenantId.Should().Be(PartnerTenant.ToString("D"));
        InviteEmailStamp.Matches(row.InvitedEmailHash, Email).Should().BeTrue();
        row.InviteCodeHash.Should().BeNull("a work-account invite carries no code");
        (row.Oid, row.Plane).Should().Be(((string?)null, (int?)null), "no account is bound");
        creates.Should().Be(0, "no CIAM account is created");
        _h.Emails.InviteUrls.Should().BeEmpty("no CIAM invite email is sent");

        var call = handler.Requests.Should().ContainSingle().Subject;
        call.Method.Should().Be(HttpMethod.Get);
        call.RequestUri.Should().Be(new Uri("https://login.microsoftonline.com/outside.test/v2.0/.well-known/openid-configuration"));
        call.Headers.Authorization.Should().BeNull();
        call.Headers.Contains("Cookie").Should().BeFalse();

        // A CIAM sign-in never meets a work-account invite, even for the same address.
        var ciamCaller = Guid.NewGuid();
        (await IdentityBindingTestKit.Binder(_h.Identities).ResolveCiamCallerAsync(ciamCaller.ToString("D"), Email, inviteCode: null, CancellationToken.None))
            .DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);

        var oid = Guid.NewGuid();
        var signIn = await IdentityBindingTestKit.Binder(_h.Identities).ResolveWorkforceCallerAsync(
            IdentityBindingTestKit.WorkforceUser(oid, tid: PartnerTenant, email: Email), oid, stampTenantId: null,
            CancellationToken.None);

        signIn.ContactId.Should().Be(contact);
        (row.Oid, row.Plane, row.InvitedEmailHash, row.InvitedTenantId)
            .Should().Be((oid.ToString("D"), (int?)(int)IdentityPlaneMarker.Workforce, (string?)null, (string?)null));
    }

    /// <summary>
    /// MUST FIRE: /invite-and-grant to a customer-tenant domain is a work-account invite AND the grant is written; no CIAM
    /// account and no CIAM email.
    /// </summary>
    [Fact]
    public async Task InviteAndGrant_ACustomerTenantDomain_IsAWorkAccountInvite_AndTheGrantIsWritten()
    {
        var contact = UnboundContact();
        var handler = new InviteOrganizationTestKit.DiscoveryHandler()
            .Answer(HttpStatusCode.OK, InviteOrganizationTestKit.DiscoveryBody(PartnerTenant));

        var body = (await InviteAndGrant(organizations: Organizations(handler)))
            .Should().BeOfType<Ok<InviteAndGrantResponse>>().Subject.Value!;

        body.OnboardStatus.Should().Be(InviteExternalUserEndpoint.WorkAccountInvitedStatus);
        (body.EmailSent, body.EmailErrorCode).Should().Be(((bool?)true, (string?)null), "slice B3: the notice was sent");
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        _h.Identities.Contacts[contact].InvitedTenantId.Should().Be(PartnerTenant.ToString("D"));
        _ciam.Created.Should().BeEmpty();
        _h.Emails.InviteUrls.Should().BeEmpty();
    }

    // ── Slice B3: the work-account notice ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// MUST FIRE: /invite to a customer-tenant domain sends ONE notice to the stamped address, after the stamp landed: fixed
    /// text (no grant exists, so no record), and one link — the portal with the customer key. No code, no contact id, no
    /// tenant id anywhere in it.
    /// </summary>
    [Fact]
    public async Task Invite_WorkAccount_SendsTheFixedNotice_ToTheStampedAddress_WithNoCodeOrIds()
    {
        var contact = UnboundContact();
        var handler = new InviteOrganizationTestKit.DiscoveryHandler()
            .Answer(HttpStatusCode.OK, InviteOrganizationTestKit.DiscoveryBody(PartnerTenant));

        (await InviteRoute(organizations: Organizations(handler))).Should().BeOfType<Ok<InviteExternalUserResponse>>();

        var sent = _h.Emails.Sent.Should().ContainSingle("one notice").Subject;
        sent.To.Should().Be(Email);
        sent.Subject.Should().Be("You've been invited to Spaarke");
        sent.LinkUrl.Should().Be($"{PortalUrl}/?customer={CustomerKey}", "the portal and the customer key only");
        var message = _h.Emails.Messages.Single();
        message.Should().Contain("work account");
        foreach (var text in new[] { sent.Subject, sent.LinkUrl, message })
        {
            text.Should().NotContain(contact.ToString("D")).And.NotContain(contact.ToString("N"))
                .And.NotContain(PartnerTenant.ToString("D")).And.NotContain("#").And.NotContain("invite=");
        }

        _h.Identities.Contacts[contact].InvitedEmailHash.Should().NotBeNull("the notice follows a stamp that landed");
        _h.Emails.InviteUrls.Should().BeEmpty("no portal-account invite email");
    }

    /// <summary>
    /// MUST FIRE: the response says "email not sent" in its existing emailSent / emailErrorCode fields when the notice fails
    /// to send. Slice C2b: a pending invite exists only if its email went out, so the stamp is cleared.
    /// </summary>
    [Fact]
    public async Task Invite_WorkAccount_ANoticeThatFails_ClearsTheStamp_AndIsReportedEmailNotSent()
    {
        var contact = UnboundContact();
        _h.Emails.Failure = new InvalidOperationException("mailbox refused");
        var handler = new InviteOrganizationTestKit.DiscoveryHandler()
            .Answer(HttpStatusCode.OK, InviteOrganizationTestKit.DiscoveryBody(PartnerTenant));

        var body = (await InviteRoute(organizations: Organizations(handler)))
            .Should().BeOfType<Ok<InviteExternalUserResponse>>().Subject.Value!;

        body.Status.Should().Be(InviteExternalUserEndpoint.WorkAccountInvitedStatus);
        (body.EmailSent, body.EmailErrorCode).Should().Be(((bool?)false, (string?)GrantInviteStatus.EmailNotSentReasonCode));
        _h.Identities.Contacts[contact].InvitedTenantId.Should().BeNull("the stamp of an invite whose notice did not send is cleared");
        _h.Identities.Contacts[contact].InvitedEmailHash.Should().BeNull();
    }

    /// <summary>MUST FIRE: /invite-and-grant's notice is sent after the grant is written and names the granted record.</summary>
    [Fact]
    public async Task InviteAndGrant_WorkAccount_SendsTheNotice_NamingTheGrantedRecord()
    {
        UnboundContact();
        var handler = new InviteOrganizationTestKit.DiscoveryHandler()
            .Answer(HttpStatusCode.OK, InviteOrganizationTestKit.DiscoveryBody(PartnerTenant));

        (await InviteAndGrant(organizations: Organizations(handler))).Should().BeOfType<Ok<InviteAndGrantResponse>>();

        var sent = _h.Emails.Sent.Should().ContainSingle().Subject;
        sent.To.Should().Be(Email);
        sent.Subject.Should().Be("You've been given access to the matter \"Smith v. Smith\" in Spaarke");
        sent.LinkUrl.Should().Be($"{PortalUrl}/?customer={CustomerKey}");
    }

    /// <summary>
    /// MUST NOT FIRE: when the grant fails after the work-account stamp, /invite-and-grant sends no notice: never a record
    /// name the person cannot open.
    /// </summary>
    [Fact]
    public async Task InviteAndGrant_WorkAccount_AGrantThatFails_SendsNoNotice()
    {
        UnboundContact();
        _h.Grants.GrantCreateFailure = new HttpRequestException("grant write refused");
        var handler = new InviteOrganizationTestKit.DiscoveryHandler()
            .Answer(HttpStatusCode.OK, InviteOrganizationTestKit.DiscoveryBody(PartnerTenant));

        var result = await InviteAndGrant(organizations: Organizations(handler));

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(500);
        _h.Emails.Sent.Should().BeEmpty();
    }

    /// <summary>
    /// MUST NOT FIRE: a portal-account invite sends no work-account notice (it keeps its own CIAM email), on both routes.
    /// </summary>
    [Fact]
    public async Task APortalAccountInvite_SendsNoWorkAccountNotice()
    {
        UnboundContact();
        var handler = new InviteOrganizationTestKit.DiscoveryHandler()
            .Answer(HttpStatusCode.BadRequest, InviteOrganizationTestKit.TenantNotFoundBody);

        (await InviteRoute(organizations: Organizations(handler))).Should().BeOfType<Ok<InviteExternalUserResponse>>();

        _h.Emails.Onboarding.Should().ContainSingle("the portal-account invite keeps its CIAM email");
        _h.Emails.Sent.Should().BeEmpty("no work-account notice");
    }

    // ── Slice B3: a host without Ciam:* ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// MUST NOT FIRE: a work-account invite creates no CIAM account, so a host without <c>Ciam:*</c> still sends it on both
    /// routes: the stamp is written and the notice sent.
    /// </summary>
    [Fact]
    public async Task NoCiamHost_AWorkAccountInvite_IsSent_OnBothRoutes()
    {
        var contact = UnboundContact();
        var handler = new InviteOrganizationTestKit.DiscoveryHandler()
            .Answer(HttpStatusCode.OK, InviteOrganizationTestKit.DiscoveryBody(PartnerTenant))
            .Answer(HttpStatusCode.OK, InviteOrganizationTestKit.DiscoveryBody(PartnerTenant));
        var organizations = Organizations(handler);
        var noCiam = NoCiamProvisioner();

        (await InviteRoute(organizations: organizations, ciam: noCiam)).Should().BeOfType<Ok<InviteExternalUserResponse>>()
            .Which.Value!.Status.Should().Be(InviteExternalUserEndpoint.WorkAccountInvitedStatus);
        (await InviteAndGrant(organizations: organizations, ciam: noCiam)).Should().BeOfType<Ok<InviteAndGrantResponse>>()
            .Which.Value!.OnboardStatus.Should().Be(InviteExternalUserEndpoint.WorkAccountInvitedStatus);

        _h.Identities.Contacts[contact].InvitedTenantId.Should().Be(PartnerTenant.ToString("D"));
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        _h.Emails.Sent.Should().HaveCount(2, "one notice per invite");
    }

    /// <summary>
    /// MUST FIRE: on a host without <c>Ciam:*</c>, a portal-account invite is still refused on both routes with 503
    /// ciam_not_configured, and nothing is read, written or granted.
    /// </summary>
    [Fact]
    public async Task NoCiamHost_APortalAccountInvite_IsStill503CiamNotConfigured()
    {
        var contact = UnboundContact();
        var handler = new InviteOrganizationTestKit.DiscoveryHandler()
            .Answer(HttpStatusCode.BadRequest, InviteOrganizationTestKit.TenantNotFoundBody)
            .Answer(HttpStatusCode.BadRequest, InviteOrganizationTestKit.TenantNotFoundBody);
        var organizations = Organizations(handler);
        var noCiam = NoCiamProvisioner();

        foreach (var result in new[]
                 {
                     await InviteRoute(organizations: organizations, ciam: noCiam),
                     await InviteAndGrant(organizations: organizations, ciam: noCiam),
                 })
        {
            var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
            problem.StatusCode.Should().Be(503);
            problem.ProblemDetails.Extensions["reasonCode"].Should().Be(GrantInviteStatus.CiamNotConfiguredReasonCode);
        }

        _h.Identities.Writes.Should().BeEmpty();
        _h.Identities.Reads.Should().BeEmpty();
        _h.Grants.Rows.Should().BeEmpty();
        _h.Identities.Contacts[contact].InvitedOn.Should().BeNull();
    }

    /// <summary>
    /// MUST NOT FIRE: every definite answer that is not a configured customer tenant is a portal-account invite, exactly as
    /// before: another Entra tenant, a domain no tenant owns (AADSTS90002), the Microsoft account (consumer) tenant, and this
    /// deployment's CIAM tenant (the last two refused even if a misconfigured list named them). Not found is never an error.
    /// </summary>
    [Theory]
    [InlineData("another-tenant")]
    [InlineData("not-found")]
    [InlineData("consumer")]
    [InlineData("ciam")]
    public async Task EveryOtherDefiniteAnswer_IsAPortalAccountInvite(string answer)
    {
        var contact = UnboundContact();
        _ciam.Failure = ObjectConflictOnProxyAddresses();
        var handler = new InviteOrganizationTestKit.DiscoveryHandler();
        _ = answer switch
        {
            "another-tenant" => handler.Answer(HttpStatusCode.OK, InviteOrganizationTestKit.DiscoveryBody(Guid.NewGuid())),
            "not-found" => handler.Answer(HttpStatusCode.BadRequest, InviteOrganizationTestKit.TenantNotFoundBody),
            "consumer" => handler.Answer(HttpStatusCode.OK,
                InviteOrganizationTestKit.DiscoveryBody(InviteOrganizationResolver.ConsumerTenantId)),
            _ => handler.Answer(HttpStatusCode.OK, InviteOrganizationTestKit.DiscoveryBody(InviteOrganizationTestKit.CiamTenant)),
        };

        var result = await InviteRoute(organizations: Organizations(handler,
            PartnerTenant, InviteOrganizationResolver.ConsumerTenantId, InviteOrganizationTestKit.CiamTenant));

        result.Should().BeOfType<Ok<InviteExternalUserResponse>>()
            .Which.Value!.Status.Should().Be(InviteExternalUserEndpoint.ProvisionedStatus, answer);
        var row = _h.Identities.Contacts[contact];
        row.InvitedTenantId.Should().BeNull(answer);
        row.InviteCodeHash.Should().NotBeNullOrEmpty(answer);
        _h.Emails.InviteUrls.Should().ContainSingle(answer);
    }

    /// <summary>
    /// MUST FIRE: a lookup with no definite answer is a 503 tenant_lookup_unavailable on both routes, whose message offers a
    /// retry only. Nothing is read about the contact, nothing is written or granted, no account, no email —
    /// and nothing is cached, so the retry looks up again.
    /// </summary>
    [Theory]
    [InlineData("timeout")]
    [InlineData("http-500")]
    [InlineData("http-429")]
    [InlineData("redirect")]
    [InlineData("unparsable-200")]
    [InlineData("issuer-without-tenant")]
    [InlineData("other-400")]
    [InlineData("network")]
    public async Task ALookupWithNoDefiniteAnswer_Is503_AndNothingIsWritten(string fault)
    {
        UnboundContact();
        var handler = new InviteOrganizationTestKit.DiscoveryHandler();
        for (var i = 0; i < 2; i++)
        {
            _ = fault switch
            {
                "timeout" => handler.Hang(),
                "http-500" => handler.Answer(HttpStatusCode.InternalServerError, "{}"),
                "http-429" => handler.Answer(HttpStatusCode.TooManyRequests, "{}"),
                "redirect" => handler.Answer(() => new HttpResponseMessage(HttpStatusCode.Found)
                {
                    Headers = { Location = new Uri("https://elsewhere.test/") },
                    Content = new StringContent(string.Empty),
                }),
                "unparsable-200" => handler.Answer(HttpStatusCode.OK, "<html>not json</html>"),
                "issuer-without-tenant" => handler.Answer(HttpStatusCode.OK,
                    "{\"issuer\":\"https://login.microsoftonline.com/{tenantid}/v2.0\"}"),
                "other-400" => handler.Answer(HttpStatusCode.BadRequest, "{\"error\":\"invalid_request\",\"error_codes\":[900144]}"),
                _ => handler.Throw(new HttpRequestException("connection refused")),
            };
        }

        var organizations = Organizations(handler);
        var creates = 0;
        _ciam.OnCreate = () => creates++;

        foreach (var result in new[] { await InviteRoute(organizations: organizations), await InviteAndGrant(organizations: organizations) })
        {
            var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
            problem.StatusCode.Should().Be(503, fault);
            problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ContactBindingDecision.InviteTenantLookupUnavailable);
            problem.ProblemDetails.Detail.Should().Contain("Try again").And.NotContain("portal account",
                "the only remedy is a retry: the kind of sign-in is never chosen by staff");
        }

        handler.Requests.Should().HaveCount(2, "a fault is never cached: the second request looks up again");
        _orgCache.Writes.Should().BeEmpty("a fault is never cached");
        _h.Identities.Writes.Should().BeEmpty();
        _h.Identities.Reads.Should().BeEmpty("the lookup runs before the contact is read");
        _h.Grants.Rows.Should().BeEmpty();
        creates.Should().Be(0);
        _h.Emails.InviteUrls.Should().BeEmpty();
    }

    /// <summary>
    /// An email whose domain is not a host name is a 400 at invite time on both routes; it never reaches the network and
    /// nothing is read or written.
    /// </summary>
    [Theory]
    [InlineData("partner@")]
    [InlineData("partner@localhost")]
    [InlineData("partner@-outside.test")]
    [InlineData("partner@outside..test")]
    [InlineData("partner@out side.test")]
    [InlineData("partner@outside.test/v2.0")]
    [InlineData("partner@outside.test?x=1")]
    [InlineData("partner@outside.test#x")]
    [InlineData("partner@out%2fside.test")]
    [InlineData("partner@out\\side.test")]
    [InlineData("a@b@outside.test")]
    public async Task AMalformedDomain_Is400_AndNeverReachesTheNetwork(string email)
    {
        var handler = new InviteOrganizationTestKit.DiscoveryHandler();
        var organizations = Organizations(handler);

        foreach (var result in new[]
                 {
                     await InviteRoute(organizations: organizations, email: email),
                     await InviteAndGrant(organizations: organizations, email: email),
                 })
        {
            result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(400, email);
        }

        handler.Requests.Should().BeEmpty();
        _h.Identities.Writes.Should().BeEmpty();
        _h.Identities.Reads.Should().BeEmpty();
    }

    /// <summary>
    /// MUST FIRE: the request never chooses the kind of sign-in. Any supplied SendAsPortalAccount, false included, is a 400 on
    /// both routes, before any read, write or lookup.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ASuppliedSendAsPortalAccount_IsRefused400_WhateverItsValue(bool value)
    {
        UnboundContact();
        var handler = new InviteOrganizationTestKit.DiscoveryHandler();

        foreach (var result in new[]
                 {
                     await InviteRoute(organizations: Organizations(handler), sendAsPortalAccount: value),
                     await InviteAndGrant(organizations: Organizations(handler), sendAsPortalAccount: value),
                 })
        {
            var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
            problem.StatusCode.Should().Be(400);
            problem.ProblemDetails.Detail.Should().Contain("SendAsPortalAccount");
        }

        handler.Requests.Should().BeEmpty("a refused request looks nothing up");
        _h.Identities.Writes.Should().BeEmpty("a refused request writes nothing");
        _h.Identities.Reads.Should().BeEmpty("a refused request reads nothing");
        _h.Grants.Rows.Should().BeEmpty();
    }

    /// <summary>MUST NOT FIRE: a deployment with no customer tenants never calls the discovery endpoint; the invite is as before.</summary>
    [Fact]
    public async Task NoCustomerTenantsConfigured_MakesNoLookup_AndInvitesAPortalAccount()
    {
        var contact = UnboundContact();
        _ciam.Failure = ObjectConflictOnProxyAddresses();
        var handler = new InviteOrganizationTestKit.DiscoveryHandler();
        var organizations = InviteOrganizationTestKit.Resolver(handler, _orgCache, Array.Empty<Guid>());

        (await InviteRoute(organizations: organizations)).Should().BeOfType<Ok<InviteExternalUserResponse>>();
        (await InviteAndGrant(organizations: organizations)).Should().BeOfType<Ok<InviteAndGrantResponse>>();

        handler.Requests.Should().BeEmpty();
        _h.Identities.Contacts[contact].InvitedTenantId.Should().BeNull();
    }

    /// <summary>
    /// MUST FIRE: a work-account invite to a contact that already carries a portal-account sign-in is refused 409
    /// contact_already_bound on both routes; nothing is written or granted.
    /// </summary>
    [Fact]
    public async Task AWorkAccountInvite_ToAContactBoundToAPortalAccount_IsRefused409()
    {
        var contact = Guid.NewGuid();
        _h.Identities.AddContact(contact, email: Email, oid: Guid.NewGuid().ToString("D"), plane: IdentityPlaneMarker.External);
        var handler = new InviteOrganizationTestKit.DiscoveryHandler()
            .Answer(HttpStatusCode.OK, InviteOrganizationTestKit.DiscoveryBody(PartnerTenant))
            .Answer(HttpStatusCode.OK, InviteOrganizationTestKit.DiscoveryBody(PartnerTenant));
        var organizations = Organizations(handler);
        _orgCache.Fault = new InvalidOperationException("cache down"); // a cache outage is skipped, never a fault

        foreach (var result in new[] { await InviteRoute(organizations: organizations), await InviteAndGrant(organizations: organizations) })
        {
            var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
            problem.StatusCode.Should().Be(409);
            problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ContactBindingDecision.InviteContactAlreadyBound);
        }

        _h.Emails.Sent.Should().BeEmpty("a bound contact gets no work-account notice");
        _h.Identities.Writes.Should().BeEmpty();
        _h.Grants.Rows.Should().BeEmpty();
    }

    /// <summary>
    /// Slice C, MUST FIRE: an ordinary invite (/invite and /invite-and-grant) to a contact whose external access an
    /// administrator removed is refused with the endpoint's existing refusal shape (409 + reason code), active or inactive,
    /// and an unreadable marker refuses too. Nothing is created, written, granted or sent. MUST NOT FIRE: the unmarked twin
    /// is invited as before.
    /// </summary>
    /// <remarks>Slice C2c: an unproven marker is not a removal: 503 <c>contact_removed_state_unavailable</c> (retryable),
    /// with nothing written either.</remarks>
    [Theory]
    [InlineData("active-removed", 409, ContactBindingDecision.InviteContactRemovedAdminReinstate)]
    [InlineData("inactive-removed", 409, ContactBindingDecision.InviteContactRemovedAdminReinstate)]
    [InlineData("active-unreadable", 503, ContactBindingDecision.InviteContactRemovedStateUnavailable)]
    public async Task AnInvite_ToARemovedContact_IsRefused_SetMarker409_UnprovenMarker503(string how, int status, string code)
    {
        var contact = Guid.NewGuid();
        _h.Identities.AddContact(contact, email: Email, stateCode: how.StartsWith("inactive", StringComparison.Ordinal) ? 1 : 0);
        if (how.EndsWith("removed", StringComparison.Ordinal))
        {
            _h.Identities.Contacts[contact].RemovedOn = DateTimeOffset.UtcNow;
        }
        else
        {
            _h.Identities.RemovedMarkerProof = ColumnReadProofOutcome.Unproven;
        }

        foreach (var result in new[] { await InviteRoute(), await InviteAndGrant() })
        {
            var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
            problem.StatusCode.Should().Be(status);
            problem.ProblemDetails.Extensions["reasonCode"].Should().Be(code);
        }

        _h.Identities.Contacts.Should().ContainSingle("no contact is created around a removal");
        _h.Identities.Writes.Should().BeEmpty();
        _h.Grants.Rows.Should().BeEmpty();
        _ciam.Created.Should().BeEmpty();
        _h.Emails.InviteUrls.Should().BeEmpty();
    }

    /// <summary>Slice C, MUST NOT FIRE: the unmarked twin of the test above is invited exactly as before.</summary>
    [Fact]
    public async Task AnInvite_ToAnUnmarkedContact_IsNotRefused()
    {
        UnboundContact();

        (await InviteRoute()).Should().BeOfType<Ok<InviteExternalUserResponse>>();
    }

    /// <summary>MUST FIRE: a work-account invite to a contact bound on a workforce plane is refused with the existing code.</summary>
    [Fact]
    public async Task AWorkAccountInvite_ToAWorkforceBoundContact_IsRefused_WithTheExistingCode()
    {
        var contact = Guid.NewGuid();
        _h.Identities.AddContact(contact, email: Email, oid: Guid.NewGuid().ToString("D"), plane: IdentityPlaneMarker.Workforce);
        var handler = new InviteOrganizationTestKit.DiscoveryHandler()
            .Answer(HttpStatusCode.OK, InviteOrganizationTestKit.DiscoveryBody(PartnerTenant));

        var problem = (await InviteRoute(organizations: Organizations(handler))).Should().BeOfType<ProblemHttpResult>().Subject;

        problem.StatusCode.Should().Be(409);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ContactBindingDecision.InviteWorkforceBoundContact);
        _h.Identities.Contacts[contact].InvitedEmailHash.Should().BeNull();
    }

    /// <summary>A work-account invite whose stamp cannot land is the retryable 503 invite_marker_not_written; nothing else.</summary>
    [Fact]
    public async Task AWorkAccountInvite_WhoseStampDoesNotLand_Is503_AndCreatesNoAccount()
    {
        UnboundContact();
        _h.Identities.MarkerWriteFailure = StoreWriteStatus.Failed;
        var creates = 0;
        _ciam.OnCreate = () => creates++;

        var outcome = await Invite(workAccountTenantId: PartnerTenant);

        outcome.Failure!.ReasonCode.Should().Be(ContactBindingDecision.InviteMarkerNotWritten);
        creates.Should().Be(0);
        _h.Emails.InviteUrls.Should().BeEmpty();
    }

    // ── Doubles ─────────────────────────────────────────────────────────────────────────────────────────────

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

    /// <summary>The CIAM provisioner seam: answers a fresh oid, or throws <see cref="Failure"/> translated as the real service
    /// translates it. No Graph, no Key Vault.</summary>
    // ── Slice C2b: /invite to a person who already has an account ───────────────────────────────────────────────

    /// <summary>
    /// MUST FIRE: /invite on a contact already bound to a portal account sends nothing and writes nothing. emailSent is false
    /// (never null) with its own code, the staff text, and the portal sign-in link the work-account notice carries (no code).
    /// </summary>
    [Fact]
    public async Task InviteRoute_ToAPersonWhoAlreadyHasAnAccount_SendsNothing_WritesNothing_AndSaysSo()
    {
        var contact = Guid.NewGuid();
        _h.Identities.AddContact(contact, email: Email, oid: Guid.NewGuid().ToString("D"), plane: IdentityPlaneMarker.External);

        var body = (await InviteRoute()).Should().BeOfType<Ok<InviteExternalUserResponse>>().Subject.Value!;

        body.Status.Should().Be(InviteExternalUserEndpoint.AlreadyProvisionedStatus);
        body.EmailSent.Should().Be(false, "emailSent is never null on /invite");
        body.EmailErrorCode.Should().Be(ContactBindingDecision.InviteContactAlreadyHasAccount);
        body.StaffMessage.Should().Contain("already has an account");
        body.SignInUrl.Should().Be($"{PortalUrl}/?customer={CustomerKey}");
        body.SignInUrl.Should().NotContain("invite=", "the sign-in link carries no code");
        _h.Identities.Writes.Should().BeEmpty("nothing is written");
        _h.Emails.InviteUrls.Should().BeEmpty("no invite email");
        _h.Emails.Sent.Should().BeEmpty("no notice");
        _ciam.Created.Should().BeEmpty();
    }

    /// <summary>MUST NOT FIRE: an unbound contact is invited as before (no already-has-account answer).</summary>
    [Fact]
    public async Task InviteRoute_ToAnUnboundContact_IsInvitedAsBefore()
    {
        UnboundContact();

        var body = (await InviteRoute()).Should().BeOfType<Ok<InviteExternalUserResponse>>().Subject.Value!;

        body.EmailSent.Should().Be(true);
        body.EmailErrorCode.Should().BeNull();
        body.StaffMessage.Should().BeNull();
        body.SignInUrl.Should().BeNull();
    }

    private sealed class StubCiamProvisioner : CiamUserProvisioningService
    {
        public StubCiamProvisioner()
            : base(
                new CiamGraphClientFactory(
                    new SecretClient(new Uri("https://vault.test.invalid/"), new NoCredential()),
                    Mock.Of<IHttpClientFactory>(),
                    Config(),
                    NullLogger<CiamGraphClientFactory>.Instance),
                new PasswordGenerator(),
                Config(),
                NullLogger<CiamUserProvisioningService>.Instance)
        {
        }

        public List<(string Email, Guid Oid)> Created { get; } = new();

        /// <summary>Thrown by every create, as the Graph SDK throws a Graph error.</summary>
        public Exception? Failure { get; set; }

        /// <summary>Runs at the start of every create (to observe what was written before it).</summary>
        public Action? OnCreate { get; set; }

        public override Task<string> CreateCiamUserAsync(
            string email, string? firstName, string? lastName, CancellationToken ct = default)
        {
            OnCreate?.Invoke();
            if (Failure is { } failure)
            {
                // The real service's translation (its CreateCiamUserAsync catch filter): the production classifier
                // decides whether a Graph error is "the email already has a CIAM account"; anything else propagates.
                if (failure is ODataError graph && IsExistingAccountConflict(graph))
                {
                    throw new CiamAccountAlreadyExistsException(email, graph);
                }

                throw failure;
            }
            var oid = Guid.NewGuid();
            Created.Add((email, oid));
            return Task.FromResult(oid.ToString("D"));
        }

        private static IConfiguration Config() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ciam:Instance"] = "https://test.ciamlogin.com",
                ["Ciam:TenantId"] = "00000000-0000-0000-0000-0000000000c1",
                ["Ciam:Domain"] = "test.onmicrosoft.com",
                ["Ciam:GraphProvisioner:ClientId"] = "provisioner",
                ["Ciam:GraphProvisioner:CertificateName"] = "cert",
            })
            .Build();

        private sealed class NoCredential : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
                => throw new NotSupportedException("No Key Vault call is made.");

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
                => throw new NotSupportedException("No Key Vault call is made.");
        }
    }
}
