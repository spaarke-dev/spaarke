using System.Security.Claims;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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
/// spaarke-SPA-external-access-platform-r3 task 036 (FR-07), review F1 - the Manage Access modal's REAL path.
/// <c>AccessGrantModal</c> posts <c>/invite-and-grant</c> for every external contact with an email, and for a contact
/// that is ALREADY onboarded that route skips the CIAM onboarding email, so without the grant email the person is never
/// told. Through the PRODUCTION <see cref="InviteAndGrantExternalUserEndpoint"/> (binder, grant core, notifier) over the
/// task-142 harness's doubles; the email service is the harness's recording double (no mailbox) and the CIAM provisioner
/// is a stub that creates no account. Exactly one email per invite: an already-onboarded contact gets the GRANT email, a
/// contact onboarded by this very request gets only the CIAM onboarding email. KEEP path: <c>tests/integration/auth/**</c>.
/// </summary>
public class InviteAndGrantExternalContactEmailTests
{
    private const string CallerOid = "0a0a0a0a-1111-2222-3333-444444444444";
    private const string MatterName = "Smith v. Smith";
    private const string Email = "partner@outside.test";

    private readonly Harness _h = new();
    private readonly Guid _matter = Guid.NewGuid();
    private readonly StubCiamProvisioner _ciam = new();

    public InviteAndGrantExternalContactEmailTests()
    {
        _h.Store.Root(ExternalGrantRootType.Matter, _matter);
        _h.Grants.RootNames[_matter] = MatterName;
    }

    private static HttpContext Context() => new DefaultHttpContext
    {
        TraceIdentifier = "trace-036-review",
        User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("oid", CallerOid), new Claim("name", "Gina Granter") }, "Test")),
    };

    /// <summary>An active contact whose CIAM account already exists (oid bound, External plane).</summary>
    private Guid AlreadyOnboarded()
    {
        var id = Guid.NewGuid();
        _h.Identities.AddContact(id, email: Email, oid: Guid.NewGuid().ToString("D"), plane: IdentityPlaneMarker.External);
        return id;
    }

    /// <summary>An active contact with an email but no CIAM account yet (this invite creates it).</summary>
    private Guid NotYetOnboarded()
    {
        var id = Guid.NewGuid();
        _h.Identities.AddContact(id, email: Email);
        return id;
    }

    private Task<IResult> InviteAndGrant(ExternalAccessLevel level = ExternalAccessLevel.Collaborate) =>
        InviteAndGrantExternalUserEndpoint.InviteAndGrantAsync(
            new InviteExternalUserRequest(Email, Guid.Empty, (int)level, "Pat", "Partner", null, null, "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, new TestInfrastructure.RecordOwnershipResolverDouble(), new TestInfrastructure.TeamPrivilegeCheckerDouble(), new EverythingProbe(),
            _ciam, _h.Emails, IdentityBindingTestKit.Binder(_h.Identities), InviteOrganizationTestKit.PortalOnly(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ExternalAccess:PortalUrl"] = "https://external.test" })
                .Build(),
            Sprk.Bff.Api.Tests.TestInfrastructure.TestSpeOwnership.Customer("acme"),
            _h.Materializer, _h.Notifier, Context(), NullLogger<Program>.Instance, _h.Time, CancellationToken.None, _h.ContactShare);

    private static InviteAndGrantResponse OkBody(IResult result) =>
        result.Should().BeOfType<Ok<InviteAndGrantResponse>>().Subject.Value!;

    private string Link => $"https://external.test/?customer=acme&record=matter:{_matter:D}";

    [Fact]
    public async Task AlreadyOnboardedContact_GainingAccess_GetsExactlyOneGrantEmail_WithThePortalLink()
    {
        var contact = AlreadyOnboarded();

        var body = OkBody(await InviteAndGrant());

        body.OnboardStatus.Should().Be("AlreadyProvisioned");
        body.NotificationFailed.Should().BeFalse();
        var sent = _h.Emails.Sent.Should().ContainSingle("exactly one email per invite").Subject;
        sent.To.Should().Be(Email);
        sent.LinkUrl.Should().Be(Link);
        sent.Subject.Should().Contain(MatterName);
        _h.Emails.Onboarding.Should().BeEmpty("an already-onboarded contact is not sent the CIAM onboarding email");
        _ciam.Created.Should().BeEmpty();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
    }

    [Fact]
    public async Task AlreadyOnboardedContact_ReGrantedAtTheSameLevel_GetsNoSecondEmail()
    {
        AlreadyOnboarded();
        await InviteAndGrant();
        _h.Emails.Sent.Clear();

        OkBody(await InviteAndGrant()).NotificationFailed.Should().BeFalse();

        _h.Emails.Sent.Should().BeEmpty("they already held this level");
        _h.Emails.Onboarding.Should().BeEmpty();
    }

    [Fact]
    public async Task ContactOnboardedByThisRequest_GetsOnlyTheOnboardingEmail_NoGrantEmail()
    {
        var contact = NotYetOnboarded();

        var body = OkBody(await InviteAndGrant());

        body.OnboardStatus.Should().Be("Provisioned");
        body.NotificationFailed.Should().BeFalse();
        _ciam.Created.Should().ContainSingle();
        _h.Emails.Onboarding.Should().ContainSingle().Which.Should().Be(Email);
        _h.Emails.Sent.Should().BeEmpty("one email per invite: the CIAM onboarding email is this contact's notice");
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
    }

    [Fact]
    public async Task WhenTheGrantEmailCannotBeSent_TheGrantIsKept_AndTheResponseSaysSo()
    {
        var contact = AlreadyOnboarded();
        _h.Emails.Failure = new HttpRequestException("Simulated shared-mailbox failure.");

        var result = await InviteAndGrant();

        var body = OkBody(result);
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

    /// <summary>The CIAM provisioner seam: records each create and answers a fresh oid. No Graph, no Key Vault.</summary>
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

        public List<string> Created { get; } = new();

        public override Task<string> CreateCiamUserAsync(
            string email, string? firstName, string? lastName, CancellationToken ct = default)
        {
            Created.Add(email);
            return Task.FromResult(Guid.NewGuid().ToString("D"));
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
