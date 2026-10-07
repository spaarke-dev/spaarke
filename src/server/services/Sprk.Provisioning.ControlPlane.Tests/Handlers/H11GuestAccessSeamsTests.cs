// -----------------------------------------------------------------------------
// H11GuestAccessSeamsTests.cs
//
// Task 232 (D2, G10). HTTP-level tests of the three H11 seams that make a Model 1
// guest usable, over a real HttpClient wrapping a hand-rolled fake
// HttpMessageHandler (NOT Mock<HttpMessageHandler> — banned per testing.md),
// with the credential injected through each class's internal test constructor.
//
// COVERAGE (task 232 acceptance criteria):
//   GraphRestB2BInvitationClient
//     - an existing guest is reused: no POST /invitations (no second email);
//     - an unknown address is invited (POST /invitations, mail sent);
//     - an address that belongs to a Member is refused, nothing sent;
//     - a lookup failure fails closed (no invitation);
//     - the OData literal doubles a quote in the address.
//   GraphRestEnvironmentSecurityGroupClient
//     - read returns displayName + securityEnabled; a 404 is a Failure;
//     - add member: 204 → success; 400 "already exist" → success (idempotent);
//       403 → Failure.
//   DataverseWebApiGuestUserWriter
//     - every role resolved before any write; a missing role writes nothing;
//     - the systemuser is read by the azureactivedirectoryobjectid alternate key
//       (which adds a group member on demand) — no POST systemusers;
//     - roles already held are not associated again (second run writes nothing);
//     - a refused alternate-key read is a Failure naming the group rule;
//     - restrictguestuseraccess true/false/unreadable.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H11GuestAccessSeamsTests
{
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string GroupId = "6f1c2b3a-4d5e-4f60-8a7b-9c0d1e2f3a4b";
    private const string GuestId = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string EnvUrl = "https://spaarke-acme.crm.dynamics.com/";
    private const string RootBuId = "bbbbbbbb-1111-2222-3333-444444444444";
    private const string RoleId = "cccccccc-1111-2222-3333-444444444444";
    private const string SystemUserId = "dddddddd-1111-2222-3333-444444444444";

    private static readonly UserProvisioningEntry Ada = new("Ada", "Lovelace", "ada@customer.com", null);

    // ---------------- invitation client ----------------

    [Fact]
    public async Task InviteAsync_AnExistingGuest_IsReusedWithoutASecondInvitation()
    {
        var http = new FakeHttp(req => req.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, $$"""{"value":[{"id":"{{GuestId}}","userType":"Guest"}]}""")
            : throw new InvalidOperationException("no POST expected"));

        var outcome = await Invitations(http).InviteAsync(Ada, TenantId, CancellationToken.None);

        outcome.Should().Be(new B2BInvitationOutcome.Success(GuestId, InvitationId: null));
        http.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Get);
        http.Requests[0].Uri.Should().Contain("mail%20eq%20%27ada%40customer.com%27");
    }

    [Fact]
    public async Task InviteAsync_AnUnknownAddress_IsInvitedWithTheMailSent()
    {
        var http = new FakeHttp(req => req.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, """{"value":[]}""")
            : Json(HttpStatusCode.Created, $$$"""{"id":"inv-1","invitedUser":{"id":"{{{GuestId}}}"}}"""));

        var outcome = await Invitations(http).InviteAsync(Ada, TenantId, CancellationToken.None);

        outcome.Should().Be(new B2BInvitationOutcome.Success(GuestId, "inv-1"));
        var post = http.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post).Subject;
        post.Uri.Should().EndWith("/v1.0/invitations");
        post.Body.Should().Contain("\"sendInvitationMessage\":true").And.Contain("\"invitedUserEmailAddress\":\"ada@customer.com\"");
    }

    [Fact]
    public async Task InviteAsync_AnAddressOfATenantMember_IsRefusedAndNothingIsSent()
    {
        var http = new FakeHttp(_ => Json(HttpStatusCode.OK, $$"""{"value":[{"id":"{{GuestId}}","userType":"Member"}]}"""));

        var outcome = await Invitations(http).InviteAsync(Ada, TenantId, CancellationToken.None);

        outcome.Should().BeOfType<B2BInvitationOutcome.Failure>().Which.Diagnostic.Should().Contain("Member");
        http.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task InviteAsync_ALookupFailure_FailsClosedWithoutInviting()
    {
        var http = new FakeHttp(_ => Json(HttpStatusCode.Forbidden, """{"error":{"code":"Authorization_RequestDenied"}}"""));

        var outcome = await Invitations(http).InviteAsync(Ada, TenantId, CancellationToken.None);

        outcome.Should().BeOfType<B2BInvitationOutcome.Failure>().Which.Diagnostic.Should().Contain("403");
        http.Requests.Should().NotContain(r => r.Method == HttpMethod.Post,
            "inviting without knowing whether the user exists would mail an existing guest again");
    }

    [Fact]
    public async Task InviteAsync_AQuoteInTheAddress_IsDoubledInTheODataLiteral()
    {
        var http = new FakeHttp(req => req.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, """{"value":[]}""")
            : Json(HttpStatusCode.Created, $$$"""{"id":"inv-1","invitedUser":{"id":"{{{GuestId}}}"}}"""));

        await Invitations(http).InviteAsync(Ada with { Email = "o'brien@customer.com" }, TenantId, CancellationToken.None);

        http.Requests[0].Uri.Should().Contain("o%27%27brien%40customer.com");
    }

    // ---------------- security group client ----------------

    [Fact]
    public async Task ReadAsync_ReturnsTheGroupsNameAndWhetherItIsASecurityGroup()
    {
        var http = new FakeHttp(_ => Json(HttpStatusCode.OK, """{"displayName":"sprk-acme-users","securityEnabled":true}"""));

        var outcome = await Groups(http).ReadAsync(GroupId, TenantId, CancellationToken.None);

        outcome.Should().Be(new SecurityGroupReadOutcome.Found("sprk-acme-users", SecurityEnabled: true));
        http.Requests.Single().Uri.Should().Contain($"/v1.0/groups/{GroupId}?$select=displayName,securityEnabled");
    }

    [Fact]
    public async Task ReadAsync_AMissingGroup_IsAFailure()
    {
        var http = new FakeHttp(_ => Json(HttpStatusCode.NotFound, """{"error":{"code":"Request_ResourceNotFound"}}"""));

        var outcome = await Groups(http).ReadAsync(GroupId, TenantId, CancellationToken.None);

        outcome.Should().BeOfType<SecurityGroupReadOutcome.Failure>().Which.Diagnostic.Should().Contain("404");
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, "", true)]
    [InlineData(HttpStatusCode.BadRequest,
        """{"error":{"code":"Request_BadRequest","message":"One or more added object references already exist for the following modified properties: 'members'."}}""",
        true)]   // already a member — idempotent
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"code":"Authorization_RequestDenied"}}""", false)]
    public async Task AddMemberAsync_MapsGraphsAnswer(HttpStatusCode status, string body, bool expectSuccess)
    {
        var http = new FakeHttp(_ => Json(status, body));

        var outcome = await Groups(http).AddMemberAsync(GroupId, GuestId, TenantId, CancellationToken.None);

        (outcome is SecurityGroupMembershipOutcome.Success).Should().Be(expectSuccess);
        var post = http.Requests.Single();
        post.Uri.Should().EndWith($"/v1.0/groups/{GroupId}/members/$ref");
        post.Body.Should().Contain($"https://graph.microsoft.com/v1.0/directoryObjects/{GuestId}");
    }

    // ---------------- Dataverse guest-user writer ----------------

    [Fact]
    public async Task EnsureGuestUserAsync_ReadsTheUserByItsObjectId_AndAssociatesTheRole()
    {
        var http = DataverseFake(roleFound: true, roleHeld: false);

        var outcome = await Writer(http).EnsureGuestUserAsync(Request(), CancellationToken.None);

        outcome.Should().Be(new DataverseGuestUserOutcome.Success(SystemUserId));
        http.Requests.Should().Contain(r => r.Method == HttpMethod.Get
            && r.Uri.Contains($"/systemusers(azureactivedirectoryobjectid={GuestId})"));
        http.Requests.Should().NotContain(r => r.Method == HttpMethod.Post && r.Uri.EndsWith("/api/data/v9.2/systemusers"),
            "a plain POST systemusers needs domainname and has no documented guest behaviour — the alternate key adds the user");
        var associate = http.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post).Subject;
        associate.Uri.Should().EndWith($"/systemusers({SystemUserId})/systemuserroles_association/$ref");
        associate.Body.Should().Contain($"https://spaarke-acme.crm.dynamics.com/api/data/v9.2/roles({RoleId})");
    }

    [Fact]
    public async Task EnsureGuestUserAsync_ARoleAlreadyHeld_WritesNothing()
    {
        var http = DataverseFake(roleFound: true, roleHeld: true);

        var outcome = await Writer(http).EnsureGuestUserAsync(Request(), CancellationToken.None);

        outcome.Should().Be(new DataverseGuestUserOutcome.Success(SystemUserId));
        http.Requests.Should().NotContain(r => r.Method == HttpMethod.Post, "a second run writes nothing new");
    }

    [Fact]
    public async Task EnsureGuestUserAsync_AMissingRole_TouchesNoUser()
    {
        var http = DataverseFake(roleFound: false, roleHeld: false);

        var outcome = await Writer(http).EnsureGuestUserAsync(Request(), CancellationToken.None);

        outcome.Should().Be(new DataverseGuestUserOutcome.RoleNotFound("Spaarke Basic User"));
        http.Requests.Should().NotContain(r => r.Uri.Contains("/systemusers"),
            "every role is resolved before the user is read (and so added)");
    }

    [Fact]
    public async Task EnsureGuestUserAsync_ARefusedUserRead_IsAFailureNamingTheGroupRule()
    {
        var http = DataverseFake(roleFound: true, roleHeld: false,
            userRead: () => Json(HttpStatusCode.Forbidden,
                """{"error":{"code":"0x80072560","message":"The user is not a member of the organization."}}"""));

        var outcome = await Writer(http).EnsureGuestUserAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<DataverseGuestUserOutcome.Failure>().Which.Diagnostic
            .Should().Contain("DIRECT member of the environment security group");
        http.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData("""{"value":[{"restrictguestuseraccess":false}]}""", typeof(GuestAccessOutcome.Allowed))]
    [InlineData("""{"value":[{"restrictguestuseraccess":true}]}""", typeof(GuestAccessOutcome.Restricted))]
    [InlineData("""{"value":[{}]}""", typeof(GuestAccessOutcome.Failure))]
    public async Task ReadGuestAccessAsync_MapsTheOrganizationSetting(string body, Type expected)
    {
        var http = new FakeHttp(_ => Json(HttpStatusCode.OK, body));

        var outcome = await Writer(http).ReadGuestAccessAsync(EnvUrl, TenantId, CancellationToken.None);

        outcome.Should().BeOfType(expected);
        http.Requests.Single().Uri.Should().EndWith("/api/data/v9.2/organizations?$select=restrictguestuseraccess");
    }

    // ---------------- helpers ----------------

    private static DataverseGuestUserRequest Request()
        => new(EnvUrl, TenantId, GuestId, ["Spaarke Basic User"]);

    private static FakeHttp DataverseFake(bool roleFound, bool roleHeld, Func<HttpResponseMessage>? userRead = null)
        => new(req =>
        {
            var uri = req.RequestUri!.AbsoluteUri;
            if (req.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (uri.Contains("/businessunits?"))
            {
                return Json(HttpStatusCode.OK, $$"""{"value":[{"businessunitid":"{{RootBuId}}"}]}""");
            }
            if (uri.Contains("/roles?"))
            {
                uri.Should().Contain(RootBuId, "roles are looked up in the root business unit");
                return Json(HttpStatusCode.OK, roleFound ? $$"""{"value":[{"roleid":"{{RoleId}}"}]}""" : """{"value":[]}""");
            }
            if (uri.Contains("/systemuserroles_association?"))
            {
                return Json(HttpStatusCode.OK, roleHeld ? $$"""{"value":[{"roleid":"{{RoleId}}"}]}""" : """{"value":[]}""");
            }
            if (uri.Contains("/systemusers(azureactivedirectoryobjectid="))
            {
                return userRead?.Invoke() ?? Json(HttpStatusCode.OK, $$"""{"systemuserid":"{{SystemUserId}}"}""");
            }
            throw new InvalidOperationException($"unexpected request {req.Method} {uri}");
        });

    private static GraphRestB2BInvitationClient Invitations(FakeHttp http)
        => new(new HttpClient(http), Options.Create(new H11UserProvisioningOptions()),
            NullLogger<GraphRestB2BInvitationClient>.Instance, _ => new FakeCredential());

    private static GraphRestEnvironmentSecurityGroupClient Groups(FakeHttp http)
        => new(new HttpClient(http), Options.Create(new H11UserProvisioningOptions()), _ => new FakeCredential());

    private static DataverseWebApiGuestUserWriter Writer(FakeHttp http)
        => new(new HttpClient(http), Options.Create(new H11UserProvisioningOptions()), _ => new FakeCredential());

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>Hand-rolled fake handler (NOT Mock&lt;HttpMessageHandler&gt;) recording each request.</summary>
    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Uri, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add((request.Method, request.RequestUri!.AbsoluteUri, body));
            return respond(request);
        }
    }
}
