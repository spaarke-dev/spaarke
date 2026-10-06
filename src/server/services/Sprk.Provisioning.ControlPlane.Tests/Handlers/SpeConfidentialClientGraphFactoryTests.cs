// -----------------------------------------------------------------------------
// SpeConfidentialClientGraphFactoryTests.cs
//
// Task 248 (G28, owner D16) — the SPE owning-app credential is the Worker UAMI's
// federated identity credential (MI-FIC), never a certificate or a secret.
//
//   F1  The production factory's credential is a ClientAssertionCredential built by
//       WorkerDataverseCredentialFactory (the single place the Worker mints the UAMI
//       assertion) — not a certificate or secret credential.
//   F2  THE assertion path, end to end through Azure.Identity/MSAL: asking that
//       credential for a Graph token asks the UAMI for `api://AzureADTokenExchange`
//       and posts that token to the owning app's tenant token endpoint as a
//       jwt-bearer client_assertion for the owning app's client id — and no
//       client_secret. The UAMI is a fake TokenCredential; Entra is a fake
//       HttpMessageHandler behind Azure.Core's HttpClientTransport (ADR-038 — the
//       SDK's own request marshaling runs; never Mock<HttpMessageHandler>).
//   F3  The T6 delegated-token trap phrase detector.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Web;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Provisioning.ControlPlane.Handlers.Credentials;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class SpeConfidentialClientGraphFactoryTests
{
    private const string TenantId = "11111111-2222-3333-4444-555555555555";
    private const string OwnerAppId = "77777777-8888-9999-aaaa-bbbbbbbbbbbb";
    private const string UamiClientId = "965a4a01-0000-0000-0000-000000000001";
    private const string UamiAssertion = "uami-assertion-for-token-exchange";

    // ---------- F1 — production credential is the UAMI-fed client assertion ----------

    [Fact]
    public void CreateCredential_Production_IsTheUamiClientAssertion_NeverACertificateOrSecret()
    {
        var factory = new SpeConfidentialClientGraphFactory(BuildWorkerCredentialFactory());

        var credential = factory.CreateCredential(TenantId, OwnerAppId);

        credential.Should().BeOfType<ClientAssertionCredential>(
            "the owning app trusts the Worker UAMI through a federated identity credential (ADR-028 A4, owner D16) — " +
            "never a certificate or secret credential");
    }

    // ---------- F2 — the assertion path ----------

    [Fact]
    public async Task OwnerCredential_GetToken_ExchangesTheUamiAssertionForAnOwningAppToken()
    {
        var uami = new FakeUamiCredential();
        var entra = new FakeEntraHandler();
        var credential = BuildWorkerCredentialFactory().CreateManagedIdentityFederatedCredential(
            TenantId, OwnerAppId, uami, new ClientAssertionCredentialOptions
            {
                Transport = new HttpClientTransport(new HttpClient(entra)),
                DisableInstanceDiscovery = true,
            });

        var token = await credential.GetTokenAsync(
            new TokenRequestContext(SpeConfidentialClientGraphFactory.GraphDefaultScope), CancellationToken.None);

        token.Token.Should().Be(FakeEntraHandler.IssuedToken);
        uami.RequestedScopes.Should().ContainSingle().Which.Should().Equal(
            WorkerDataverseCredentialFactory.FederatedTokenExchangeScope);
        entra.TokenRequestUri!.AbsolutePath.Should().Be($"/{TenantId}/oauth2/v2.0/token",
            "the token is requested in the run's tenant (§4D I5)");
        entra.TokenRequestForm!["client_id"].Should().Be(OwnerAppId);
        entra.TokenRequestForm["grant_type"].Should().Be("client_credentials");
        entra.TokenRequestForm["client_assertion_type"].Should().Be("urn:ietf:params:oauth:client-assertion-type:jwt-bearer");
        entra.TokenRequestForm["client_assertion"].Should().Be(UamiAssertion,
            "the UAMI's token IS the client assertion — no certificate signs it");
        entra.TokenRequestForm["scope"].Should().Contain("https://graph.microsoft.com/.default");
        entra.TokenRequestForm.AllKeys.Should().NotContain("client_secret");
    }

    // ---------- F3 — T6 delegated-token trap phrase detection ----------

    [Theory]
    [InlineData(403, "Public client not allowed for this resource.", true)]
    [InlineData(400, "Invalid request payload.", false)]
    public void IsDelegatedTokenTrapError_MatchesOnlyTheTrapPhrase(int status, string message, bool expected)
    {
        var ex = new ODataError { ResponseStatusCode = status, Error = new MainError { Message = message } };

        SpeConfidentialClientGraphFactory.IsDelegatedTokenTrapError(ex).Should().Be(expected);
    }

    // ---------- helpers ----------

    private static WorkerDataverseCredentialFactory BuildWorkerCredentialFactory() => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ManagedIdentity:ClientId"] = UamiClientId })
            .Build(),
        NullLogger<WorkerDataverseCredentialFactory>.Instance);

    /// <summary>Stands in for the Worker UAMI: hands out a fixed assertion and records the scopes asked for.</summary>
    private sealed class FakeUamiCredential : TokenCredential
    {
        public List<string[]> RequestedScopes { get; } = new();

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            RequestedScopes.Add(requestContext.Scopes);
            return new AccessToken(UamiAssertion, DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>Stands in for Entra ID's token endpoint; captures the client-credentials request.</summary>
    private sealed class FakeEntraHandler : HttpMessageHandler
    {
        public const string IssuedToken = "owner-app-graph-token";

        public Uri? TokenRequestUri { get; private set; }

        public System.Collections.Specialized.NameValueCollection? TokenRequestForm { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/oauth2/v2.0/token", StringComparison.Ordinal))
            {
                TokenRequestUri = request.RequestUri;
                TokenRequestForm = HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellationToken));
                return Json($$"""{"token_type":"Bearer","expires_in":3599,"ext_expires_in":3599,"access_token":"{{IssuedToken}}"}""");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
