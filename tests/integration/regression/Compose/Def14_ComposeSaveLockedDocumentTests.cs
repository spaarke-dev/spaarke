// DEF-14 regression — Compose Save must surface a locked/checked-out SPE document as an
// actionable 423 (and a changed-under-you document as 412), NEVER an opaque 500 leaking
// "ODataError".
//
// Root cause (owner UAT): the SPE write in UploadSessionManager.ReplaceFileContentAsUserAsync
// PUTs via the Graph SDK v5 / Kiota client, which raises
// `Microsoft.Graph.Models.ODataErrors.ODataError` — but the catch handlers only caught the
// LEGACY `Microsoft.Graph.ServiceException` (which Kiota never throws). The 423→Locked and
// 412→PreconditionFailed translations were DEAD CODE, so a locked-document write bubbled up as
// a generic 500 ("Save failed: ODataError: The resource you are attempting to access is
// locked."). The fix revives the translation by catching ODataError in the write layer, and
// maps the resulting typed domain exceptions to 423/412 with friendly copy at the endpoint.
//
// KEEP-path classification (ADR-038 §2 + tests/CLAUDE.md): regression — "every bug = one
// regression file". Compiled into Sprk.Bff.Api.Tests via the
// `..\..\integration\regression\**\*.cs` glob in the test csproj.
//
// Two layers are proven here, matching the two-part fix:
//   (A) Route layer (WebApplicationFactory): a lock/precondition raised by the SPE facade
//       (ISpeFileOperations) crosses ComposeService and the real Save route and lands as
//       423/412 with actionable copy — NOT a 500. This is the surface the owner saw.
//   (B) Translation layer (UploadSessionManager): a REAL Kiota `ODataError` (produced by a
//       Graph response with HTTP 423 / 412) is translated into the typed domain exceptions
//       DocumentLockedByWordException / EtagPreconditionFailedException. This exercises the
//       exact dead-code path that was revived — the ADR-007 boundary where the Microsoft.Graph
//       type is caught and never leaks past the facade.
//
// Banned-pattern compliance (tests/CLAUDE.md B1-B17): no Mock<HttpMessageHandler>; the SPE
// boundary is mocked at the module seam (ISpeFileOperations) per ADR-038 §4; the translation
// tests drive a real GraphServiceClient over a concrete (non-Moq) handler.

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression.Compose;

// =====================================================================================
// (A) Route layer — WebApplicationFactory through the real /api/compose/{id}/save route
// =====================================================================================

[Trait("status", "repaired")]
public sealed class Def14_ComposeSaveLockedDocumentTests : IClassFixture<Def14ComposeSaveFixture>
{
    private readonly Def14ComposeSaveFixture _fixture;

    public Def14_ComposeSaveLockedDocumentTests(Def14ComposeSaveFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SaveDocument_WhenSpeItemLocked_Returns423WithActionableCopy_NotOpaque500()
    {
        // Arrange — the SPE facade raises the typed lock exception the write layer produces on a
        // Graph 423 / resourceLocked (see the translation tests below for the ODataError→typed step).
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DocumentLockedByWordException("spe-item-locked"));

        using var client = _fixture.CreateAuthenticatedClient();
        var body = new
        {
            driveId = "drive-lock-01",
            sessionId = Guid.NewGuid().ToString(),
            tenantId = "tenant-aad-lock",
            content = new byte[] { 0x50, 0x4B, 0x03, 0x04 }, // DOCX ZIP signature
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/compose/documents/spe-item-locked/save", body);

        // Assert — 423 Locked, actionable copy, and NO ODataError / 500 leak.
        response.StatusCode.Should().Be(HttpStatusCode.Locked,
            "a checked-out / open-in-Word SPE item must surface as 423, not the opaque 500 DEF-14 reported");
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("checked out", "the 423 copy tells the user how to recover");
        payload.Should().NotContain("ODataError", "the raw Graph error must never leak to the client");
        ((int)response.StatusCode).Should().NotBe(500);
    }

    [Fact]
    public async Task SaveDocument_WhenEtagPreconditionFailed_Returns412WithActionableCopy_NotOpaque500()
    {
        // Arrange
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EtagPreconditionFailedException("spe-item-moved", "\"stale-etag\""));

        using var client = _fixture.CreateAuthenticatedClient();
        var body = new
        {
            driveId = "drive-lock-02",
            sessionId = Guid.NewGuid().ToString(),
            tenantId = "tenant-aad-etag",
            content = new byte[] { 0x50, 0x4B, 0x03, 0x04 },
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/compose/documents/spe-item-moved/save", body);

        // Assert — 412 Precondition Failed, actionable copy, no 500 leak.
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed,
            "a document that changed under the caller must surface as 412, not a 500");
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("reload", "the 412 copy tells the user to reload and reapply");
        payload.Should().NotContain("ODataError");
        ((int)response.StatusCode).Should().NotBe(500);
    }
}

// =====================================================================================
// (B) Translation layer — UploadSessionManager turns a REAL Kiota ODataError into the
//     typed domain exceptions. Proves the revived dead-code path directly.
// =====================================================================================

[Trait("status", "repaired")]
public sealed class Def14_UploadSessionManagerOdataTranslationTests
{
    [Fact]
    public async Task ReplaceFileContentAsUser_WhenGraphReturns423_ThrowsDocumentLockedByWordException()
    {
        var sut = BuildManager(HttpStatusCode.Locked, errorCode: "resourceLocked");

        var act = () => sut.ReplaceFileContentAsUserAsync(
            new DefaultHttpContext(), "drive-1", "item-1",
            new MemoryStream(new byte[] { 1, 2, 3 }), CancellationToken.None);

        await act.Should().ThrowAsync<DocumentLockedByWordException>(
            "the Kiota ODataError (HTTP 423 / resourceLocked) must translate to the typed lock exception, not leak");
    }

    [Fact]
    public async Task ReplaceFileContentAsUser_WhenGraphReturns412_ThrowsEtagPreconditionFailedException()
    {
        var sut = BuildManager(HttpStatusCode.PreconditionFailed, errorCode: "preconditionFailed");

        var act = () => sut.ReplaceFileContentAsUserAsync(
            new DefaultHttpContext(), "drive-1", "item-1",
            new MemoryStream(new byte[] { 1, 2, 3 }), ifMatch: "\"stale\"", CancellationToken.None);

        await act.Should().ThrowAsync<EtagPreconditionFailedException>(
            "the Kiota ODataError (HTTP 412) must translate to the typed precondition exception");
    }

    private static UploadSessionManager BuildManager(HttpStatusCode status, string errorCode)
    {
        var handler = new StatusThrowingGraphHandler(status, errorCode);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") };
        var graph = new GraphServiceClient(httpClient);
        var factory = new StubGraphClientFactory(graph);
        return new UploadSessionManager(factory, Mock.Of<IHttpClientFactory>(), NullLogger<UploadSessionManager>.Instance);
    }
}

/// <summary>
/// Concrete (non-Moq, non-HttpMessageHandler-mock) handler that returns a Graph OData error
/// response for every request. The Graph SDK v5 / Kiota request adapter deserializes this into
/// a real <c>Microsoft.Graph.Models.ODataErrors.ODataError</c> and throws it — exactly the type
/// the production write path faces — so the translation under test is exercised for real.
/// </summary>
internal sealed class StatusThrowingGraphHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _errorCode;

    public StatusThrowingGraphHandler(HttpStatusCode status, string errorCode)
    {
        _status = status;
        _errorCode = errorCode;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var json = "{\"error\":{\"code\":\"" + _errorCode +
                   "\",\"message\":\"The resource you are attempting to access is locked.\"}}";
        var response = new HttpResponseMessage(_status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }
}

/// <summary>Returns a pre-built GraphServiceClient regardless of user/app context.</summary>
internal sealed class StubGraphClientFactory : IGraphClientFactory
{
    private readonly GraphServiceClient _client;
    public StubGraphClientFactory(GraphServiceClient client) => _client = client;
    public Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default) => Task.FromResult(_client);
    public GraphServiceClient ForApp() => _client;
}

// =====================================================================================
// Fixture — real ComposeService + real Save route; ISpeFileOperations mocked at the seam.
// Mirrors ComposeContractFixture's canonical config-key set (bff-extensions.md §F.2) but,
// crucially, does NOT replace IComposeService — the real service + real endpoint run so the
// exception genuinely crosses the wire.
// =====================================================================================

public sealed class Def14ComposeSaveFixture : WebApplicationFactory<Program>
{
    public Mock<ISpeFileOperations> SpeMock { get; } = new(MockBehavior.Loose);

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["Cors:AllowedOrigins:0"] = "https://localhost:5173",
                ["UAMI_CLIENT_ID"] = "test-client-id",
                ["TENANT_ID"] = "test-tenant-id",
                ["API_APP_ID"] = "test-app-id",
                ["API_CLIENT_SECRET"] = "test-secret",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-app-id",
                ["AzureAd:Audience"] = "api://test-app-id",
                ["Graph:TenantId"] = "test-tenant-id",
                ["Graph:ClientId"] = "test-client-id",
                ["Graph:ClientSecret"] = "test-client-secret",
                ["Graph:UseManagedIdentity"] = "false",
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",
                ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
                ["Dataverse:ClientSecret"] = "test-client-secret",
                ["Dataverse:TenantId"] = "test-tenant-id",
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["ServiceBus:QueueName"] = "sdap-jobs",
                ["DocumentIntelligence:Enabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "test-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
                ["Analysis:Enabled"] = "true",
                ["Analysis:UseStubResolver"] = "true",
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["OfficeRateLimit:Enabled"] = "false",
                ["Redis:Enabled"] = "false",
                ["Redis:AllowInMemoryFallback"] = "true",
                ["ModelSelector:DefaultModel"] = "gpt-4o",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",
                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",
                ["SpeAdmin:KeyVaultUri"] = "https://test.vault.azure.net/",
                ["ManagedIdentity:ClientId"] = "test-managed-identity-client-id",
                ["CosmosPersistence:Endpoint"] = "https://test.documents.azure.com:443/",
                ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",
                ["AgentService:Enabled"] = "false",
                ["AgentService:Endpoint"] = "https://test.services.ai.azure.com/api/projects/test-project",
                ["AgentService:AgentId"] = "test-agent-id",
                ["AgentService:MaxConcurrency"] = "4",
                ["AgentService:ThreadCacheExpiryMinutes"] = "60",
            };
            config.AddInMemoryCollection(dict);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options =>
            {
                options.ThrowOnBadRequest = false;
            });

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = Def14FakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = Def14FakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, Def14FakeAuthHandler>(Def14FakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = Def14FakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = Def14FakeAuthHandler.SchemeName;
            });

            // Graph factory stub so incidental OBO paths don't hit real MSAL.
            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            // No background workers in tests.
            services.RemoveAll<IHostedService>();

            // Dataverse stub (never reached before the SPE throw, but keeps the graph resolvable).
            var dataverseMock = new Mock<IDataverseService>();
            dataverseMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseMock.Object);

            // The SPE facade is the module boundary we drive — real ComposeService + real Save
            // route stay in place so the exception genuinely crosses the wire (DEF-14 E2E DoD).
            services.RemoveAll<ISpeFileOperations>();
            services.AddSingleton(SpeMock.Object);
        });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }
}

internal sealed class Def14FakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Def14FakeAuth";

    public Def14FakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));
        }

        var oid = Request.Headers["X-Test-User"].ToString();
        if (string.IsNullOrWhiteSpace(oid))
        {
            oid = Guid.NewGuid().ToString();
        }

        var claims = new List<Claim>
        {
            new("oid", oid),
            new(ClaimTypes.NameIdentifier, oid),
            new(ClaimTypes.Name, $"DEF14 Test User {oid}"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
