using System.Security.Claims;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
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

    private Task<IResult> InviteAndGrant() =>
        InviteAndGrantExternalUserEndpoint.InviteAndGrantAsync(
            Request() with { RecordId = _matter },
            _h.Grants, _h.Participations, _h.AccessibleRecords, new EverythingProbe(),
            _ciam, _h.Emails, IdentityBindingTestKit.Binder(_h.Identities),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ExternalAccess:PortalUrl"] = PortalUrl })
                .Build(),
            _h.Materializer, _h.Notifier, Context(), NullLogger<Program>.Instance, _h.Time, CancellationToken.None);

    /// <summary>The /invite core, as the route runs it.</summary>
    private Task<InviteExternalUserEndpoint.ProvisionOutcome> Invite(ContactIdentityBinder? binder = null) =>
        InviteExternalUserEndpoint.ProvisionAsync(
            Request(), _h.Grants, _ciam, _h.Emails, binder ?? IdentityBindingTestKit.Binder(_h.Identities), PortalUrl,
            NullLogger<Program>.Instance, resolution: null, CancellationToken.None);

    private static HttpContext Context() => new DefaultHttpContext
    {
        TraceIdentifier = "trace-184",
        User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("oid", "0a0a0a0a-1111-2222-3333-444444444444"), new Claim("name", "Gina Granter") }, "Test")),
    };

    // ── The ExistingAccount branch ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InviteAndGrant_OfAPersonWithACiamAccount_SucceedsAsExistingAccount_PendingContact_SignInEmailOnly()
    {
        var contact = UnboundContact();
        _ciam.Failure = ObjectConflictOnProxyAddresses();

        var body = (await InviteAndGrant()).Should().BeOfType<Ok<InviteAndGrantResponse>>().Subject.Value!;

        body.OnboardStatus.Should().Be("ExistingAccount");
        body.ContactId.Should().Be(contact);
        _h.Identities.Contacts[contact].Oid.Should().BeNull("the existing account's oid is never looked up at invite time");
        _h.Identities.Contacts[contact].InvitedOn.Should().NotBeNull("the contact stays pending: marker set, no oid");
        _h.Emails.ExistingAccount.Should().ContainSingle().Which.Should().Be(Email);
        _h.Emails.Onboarding.Should().BeEmpty("there is no new account and no password to set");
        _h.Emails.Sent.Should().BeEmpty("one email per invite: the sign-in email is this person's notice");
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();

        // The first CIAM sign-in binds the pending contact (E11, under the intent gate).
        var existingOid = Guid.NewGuid();
        var signIn = await IdentityBindingTestKit.Binder(_h.Identities)
            .ResolveCiamCallerAsync(existingOid.ToString(), Email, CancellationToken.None);
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
    [Fact]
    public async Task BeforeTheSchema_ANewAccountInvite_StillSucceeds_ItsOidIsBoundInTheSameRequest()
    {
        var contact = UnboundContact();
        _h.Identities.InviteMarkerStatus = LookupStatus.ColumnMissing;

        var body = (await InviteAndGrant()).Should().BeOfType<Ok<InviteAndGrantResponse>>().Subject.Value!;

        body.OnboardStatus.Should().Be("Provisioned");
        _ciam.Created.Should().ContainSingle();
        _h.Identities.Contacts[contact].InvitedOn.Should().BeNull();
        _h.Identities.Contacts[contact].Oid.Should().Be(_ciam.Created.Single().Oid.ToString("D"));
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

    /// <summary>With the marker landed, a failed oid write keeps today's 500, and the first sign-in repairs the binding.</summary>
    [Fact]
    public async Task AFailedOidWrite_WithTheMarker_KeepsThe500_AndTheFirstSignInRepairsIt()
    {
        var contact = UnboundContact();
        _h.Identities.FailBind = true;

        var result = await InviteAndGrant();

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        _h.Identities.Contacts[contact].InvitedOn.Should().NotBeNull();
        _h.Identities.FailBind = false;
        var created = _ciam.Created.Single().Oid;
        var signIn = await IdentityBindingTestKit.Binder(_h.Identities)
            .ResolveCiamCallerAsync(created.ToString(), Email, CancellationToken.None);
        signIn.ContactId.Should().Be(contact);
    }

    [Fact]
    public async Task AReInvite_OverwritesTheMarkerWithTheLatestTime()
    {
        // Coordinator design (round 96): a re-invite overwrites the marker; nothing clears it.
        var contact = UnboundContact();
        _ciam.Failure = ObjectConflictOnProxyAddresses();
        var clock = new FakeTimeProvider(IdentityBindingTestKit.Now);
        var binder = new ContactIdentityBinder(_h.Identities, IdentityBindingTestKit.Tenants(IdentityBindingTestKit.CustomerTenant),
            clock, NullLogger<ContactIdentityBinder>.Instance);

        await Invite(binder);
        clock.Advance(TimeSpan.FromDays(3));
        await Invite(binder);

        _h.Identities.Contacts[contact].InvitedOn.Should().Be(IdentityBindingTestKit.Now.AddDays(3));
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
