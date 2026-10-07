using Sprk.Bff.Api.Infrastructure.Dataverse;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// Integration tests for the Office-scoped sprk_communication endpoints added by
/// smart-todo-decoupling-r3 task 070a.
/// </summary>
/// <remarks>
/// <para>
/// Covers:
/// <list type="bullet">
///   <item><description><c>GET /api/office/communications/by-message-id/{internetMessageId}</c> —
///     401 unauth, 200 with match, 404 without match</description></item>
///   <item><description><c>GET /api/office/communications/{commId}/linked-todos</c> —
///     200 with 3 todos, 200 with empty array, 401 unauth</description></item>
/// </list>
/// </para>
/// <para>
/// Pattern mirrors <see cref="OfficeEndpointsTests"/> (WebApplicationFactory + test auth
/// handler + Mock&lt;IGenericEntityService&gt;). Per RB-T028-03/04/05/06 and §F-asymmetric
/// registration: registration MUST be unconditional (no feature flag guarding service
/// registration) since the endpoints map unconditionally.
/// </para>
/// </remarks>
[Trait("status", "repaired")]
public class CommunicationsEndpointsContractTests : IClassFixture<OfficeCommunicationsTestWebAppFactory>
{
    private readonly OfficeCommunicationsTestWebAppFactory _factory;
    private readonly HttpClient _client;

    public CommunicationsEndpointsContractTests(OfficeCommunicationsTestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ────────────────────────────────────────────────────────────────────────
    // GET /api/office/communications/by-message-id/{internetMessageId}
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByMessageId_WithoutAuth_Returns401()
    {
        // Arrange
        using var anonFactory = OfficeCommunicationsTestWebAppFactory.CreateAnonymous();
        var anonClient = anonFactory.CreateClient();

        // Act
        var response = await anonClient.GetAsync(
            "/api/office/communications/by-message-id/abc123%40contoso.com");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetByMessageId_WithAuthAndKnownMessage_Returns200WithRecord()
    {
        // Arrange
        var expectedCommunicationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var expectedSubject = "Re: Contract terms";
        var messageId = "<abc@contoso.com>";

        // Programmed on the DELEGATED client (task 127 / #1020). The app-only service is no longer
        // on this path, so programming EntityServiceMock alone would now yield a 404.
        _factory.SetupUserQuery(
            // Discriminate on the message id: this fixture is SHARED across the class, so matching on
            // the entity set alone would leak this row into the not-captured tests below.
            Uri.EscapeDataString(messageId),
            "{\"value\":[{\"sprk_communicationid\":\"" + expectedCommunicationId + "\","
            + "\"sprk_subject\":\"" + expectedSubject + "\"}]}");

        // Act — note the client URL-encodes the messageId
        var response = await _client.GetAsync(
            $"/api/office/communications/by-message-id/{Uri.EscapeDataString(messageId)}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("communicationId").GetGuid().Should().Be(expectedCommunicationId);
        payload.GetProperty("subject").GetString().Should().Be(expectedSubject);
    }

    [Fact]
    public async Task GetByMessageId_WithAuthAndMissingMessage_Returns404()
    {
        // Arrange
        var messageId = "<does-not-exist@contoso.com>";

        _factory.EntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        // Act
        var response = await _client.GetAsync(
            $"/api/office/communications/by-message-id/{Uri.EscapeDataString(messageId)}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ────────────────────────────────────────────────────────────────────────
    // GET /api/office/communications/by-message-id/{internetMessageId}/suggestions
    // (task 042 / FR-B2 — engine-predicted pre-selection for the add-in picker)
    //
    // The 200 path runs the REAL engine evaluate path (CommunicationService.ReconstructEnvelopeAsync +
    // IncomingAssociationResolver.EvaluateAsync) — nothing in it is mocked. Task 161 drives it through the
    // engine's own DI plug-in point instead: one deterministic IAssociationRung (ScriptedRung) whose matches are
    // fixed, so the test can name which candidate the caller may read. That is a module boundary, not a mock of
    // the engine, and it is what proves the payload carries no candidate the caller cannot read.
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSuggestions_WithoutAuth_Returns401()
    {
        // Arrange
        using var anonFactory = OfficeCommunicationsTestWebAppFactory.CreateAnonymous();
        var anonClient = anonFactory.CreateClient();

        // Act
        var response = await anonClient.GetAsync(
            "/api/office/communications/by-message-id/abc123%40contoso.com/suggestions");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSuggestions_WhenEmailNotCaptured_Returns404()
    {
        // Arrange — no sprk_communication for this message id (email not yet captured).
        // FR-B2 fallback: the client opens the picker with NO pre-selection.
        var messageId = "<not-captured@contoso.com>";

        _factory.EntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        // Act
        var response = await _client.GetAsync(
            $"/api/office/communications/by-message-id/{Uri.EscapeDataString(messageId)}/suggestions");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("errorCode").GetString().Should().Be("OFFICE_COMM_NOT_FOUND");
    }

    [Fact]
    public async Task GetSuggestions_ACandidateTheCallerCannotRead_IsAbsentEverywhere_ReadableOnesKeepNamesAndFiling()
    {
        // unified-access-control-r2 task 161 — task 127's criterion ("no candidate the caller cannot read — neither
        // name nor id") was not met: the route dropped the NAME of an unreadable candidate but returned its id,
        // entity, confidence and provenance. It now shares SuggestionCandidateAccess with the suggest route.
        var messageId = "<trim-161@contoso.com>";
        var communicationId = Guid.NewGuid();
        var readable = Guid.NewGuid();
        var hidden = Guid.NewGuid();
        _factory.Rung.Reset();
        _factory.SetupUserQuery(
            Uri.EscapeDataString(messageId),
            "{\"value\":[{\"sprk_communicationid\":\"" + communicationId + "\",\"sprk_subject\":\"Re: Acme\"}]}");
        _factory.EntityServiceMock
            .Setup(s => s.RetrieveAsync("sprk_communication", communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communication", communicationId)
            {
                ["sprk_subject"] = "Re: Acme",
                ["sprk_from"] = "client@outside.com",
                ["sprk_to"] = "caller@contoso.com",
            });
        foreach (var target in new[] { readable, hidden })
        {
            _factory.Rung.Matches.Add(new Sprk.Bff.Api.Services.Communication.Engine.RungMatch
            {
                Rung = Sprk.Bff.Api.Services.Communication.Engine.RungKind.ExplicitReference,
                RegardingFieldName = "sprk_regardingmatter",
                Target = new EntityReference("sprk_matter", target),
                Confidence = 0.95,
                Provenance = $"explicit reference to matter {target}",
            });
        }
        _factory.Rung.Matches.Add(new Sprk.Bff.Api.Services.Communication.Engine.RungMatch
        {
            Rung = Sprk.Bff.Api.Services.Communication.Engine.RungKind.ExplicitReference,
            Category = "deadline",
            Confidence = 0.6,
            Provenance = $"deadline clause linked to {hidden}",
        });
        _factory.SetupUserQuery($"sprk_matters({readable})", "{\"sprk_mattername\":\"Acme v Widgets\"}");
        _factory.UserClientMock
            .Setup(c => c.GetAsync(It.Is<string>(p => p.Contains($"sprk_matters({hidden})")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "denied"));

        var response = await _client.GetAsync(
            $"/api/office/communications/by-message-id/{Uri.EscapeDataString(messageId)}/suggestions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain(hidden.ToString(), "neither the candidate, its name, its filing verdict nor a signal naming it");
        var payload = JsonDocument.Parse(text).RootElement;
        var suggestions = payload.GetProperty("suggestions");
        suggestions.GetProperty("candidates").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("targetId").GetString().Should().Be(readable.ToString());
        suggestions.GetProperty("status").GetString().Should().Be("Resolved",
            "decided without the hidden record — with both, the engine would answer Ambiguous");
        payload.GetProperty("names").GetProperty(readable.ToString()).GetString().Should().Be("Acme v Widgets");
        payload.GetProperty("filingAccess").TryGetProperty(readable.ToString(), out _).Should().BeTrue(
            "a readable, named candidate still carries its filing verdict (task 084)");
        _factory.Rung.Reset();
    }

    // ────────────────────────────────────────────────────────────────────────
    // GET /api/office/communications/{commId}/linked-todos
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLinkedTodos_WithoutAuth_Returns401()
    {
        // Arrange
        using var anonFactory = OfficeCommunicationsTestWebAppFactory.CreateAnonymous();
        var anonClient = anonFactory.CreateClient();
        var commId = Guid.NewGuid();

        // Act
        var response = await anonClient.GetAsync(
            $"/api/office/communications/{commId}/linked-todos");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetLinkedTodos_WithAuthAndThreeMatches_Returns200WithTodos()
    {
        // Arrange
        var commId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var todoIds = new[]
        {
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
        };

        // Programmed on the DELEGATED client (task 127 / #1020). Dataverse trims this query under
        // the caller's own context now, so the rows a test programs here stand in for "what this
        // caller is permitted to see" rather than "every row linked to the communication".
        var rows = string.Join(",", todoIds.Select((id, idx) =>
            "{\"sprk_todoid\":\"" + id + "\",\"sprk_name\":\"Todo " + (idx + 1)
            + "\",\"statecode\":0,\"statuscode\":1}"));
        // Discriminated on the commId for the same shared-fixture reason as above.
        _factory.SetupUserQuery(commId.ToString(), "{\"value\":[" + rows + "]}");

        // Act
        var response = await _client.GetAsync(
            $"/api/office/communications/{commId}/linked-todos");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("count").GetInt32().Should().Be(3);

        var todos = payload.GetProperty("todos");
        todos.GetArrayLength().Should().Be(3);

        // Wire-level field names are snake_case per the client contract — verify the
        // first projection serialises with the expected attributes.
        var first = todos[0];
        first.GetProperty("sprk_todoid").GetGuid().Should().Be(todoIds[0]);
        first.GetProperty("sprk_name").GetString().Should().Be("Todo 1");
        first.GetProperty("statecode").GetInt32().Should().Be(0);
        first.GetProperty("statuscode").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetLinkedTodos_WithAuthAndNoMatches_Returns200WithEmptyArray()
    {
        // Arrange
        var commId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        _factory.EntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        // Act
        var response = await _client.GetAsync(
            $"/api/office/communications/{commId}/linked-todos");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("count").GetInt32().Should().Be(0);
        payload.GetProperty("todos").GetArrayLength().Should().Be(0);
    }
}

/// <summary>
/// Custom WebApplicationFactory for OfficeCommunicationsEndpoints tests. Mirrors
/// <see cref="OfficeTestWebAppFactory"/> but exposes the
/// <see cref="IGenericEntityService"/> mock so tests can program Dataverse responses
/// per-test.
/// </summary>
public sealed class OfficeCommunicationsTestWebAppFactory : WebApplicationFactory<Program>
{
    public Mock<IGenericEntityService> EntityServiceMock { get; } = new();

    /// <summary>
    /// The DELEGATED (user-OBO) Dataverse client - the boundary these routes read through since
    /// unified-access-control-r2 task 127 (GitHub #1020). Mocking this interface is allowed by
    /// docs/standards/TEST-ARCHITECTURE.md mock-boundary rules: it is a module boundary, not an
    /// HttpMessageHandler (which ADR-038 bans).
    /// <para>
    /// <see cref="EntityServiceMock"/> is retained because the fixture still has to displace the
    /// app-only registration to keep the host off real Dataverse - but these three routes no longer
    /// read through it, and a test that programs it alone will now get a 404.
    /// </para>
    /// </summary>
    public Mock<IDataverseUserClient> UserClientMock { get; } = new();

    /// <summary>
    /// Task 161: the Association Engine's DI plug-in point, replaced by ONE deterministic rung so a test can name the
    /// candidates the engine proposes. Empty by default — the engine then proposes nothing.
    /// </summary>
    public Sprk.Bff.Api.Tests.Api.Communication.ScriptedRung Rung { get; } = new();

    /// <summary>
    /// Default for any delegated GET a test has not programmed: an empty, SUCCESSFUL page.
    ///
    /// <para>This is the honest default. Under the caller's own security context, "you may not see
    /// this record" and "this record does not exist" are meant to be indistinguishable — Dataverse
    /// returns no rows either way, and the handlers turn that into a 404. Defaulting to a FAILURE
    /// instead would make unprogrammed tests exercise the delegated-context-broken path, which is a
    /// different branch entirely.</para>
    /// </summary>
    private void SetupEmptyUserQueryDefault()
        => UserClientMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                using var doc = System.Text.Json.JsonDocument.Parse("{\"value\":[]}");
                return DataverseUserResponse.Ok(200, doc.RootElement.Clone());
            });

    /// <summary>Programs a delegated OData GET whose path contains <paramref name="pathFragment"/>.</summary>
    public void SetupUserQuery(string pathFragment, string json)
        => UserClientMock
            .Setup(c => c.GetAsync(
                It.Is<string>(pth => pth.Contains(pathFragment, StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                return DataverseUserResponse.Ok(200, doc.RootElement.Clone());
            });

    private readonly bool _disableAuth;

    public OfficeCommunicationsTestWebAppFactory() : this(disableAuth: false) { }

    private OfficeCommunicationsTestWebAppFactory(bool disableAuth)
    {
        SetupEmptyUserQueryDefault();
        _disableAuth = disableAuth;
    }

    public static OfficeCommunicationsTestWebAppFactory CreateAnonymous() => new(disableAuth: true);

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            // Reuse the same baseline config as OfficeTestWebAppFactory so the host
            // boots through SpeAdminModule + AiPersistenceModule + AgentService DI.
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
                ["Graph:ManagedIdentity:Enabled"] = "false",
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
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["OfficeRateLimit:Enabled"] = "false",
                ["Redis:Enabled"] = "false",
                // spaarke-redis-cache-remediation-r1 task 003 (FR-02): opt into in-memory fallback for tests.
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
            config.AddInMemoryCollection(dict!);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // spaarke-redis-cache-remediation-r1 task 003 (FR-02): switch to Development for in-memory
        // cache fallback; disable ValidateScopes to preserve pre-existing test behavior.
        builder.UseEnvironment("Development");
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            // Test hosts must not authenticate for real — see TestTokenCredential.
            services.UseStubTokenCredential();

            // Add the test authentication scheme. When _disableAuth is true, we register
            // a deny-all handler so that hitting an authenticated endpoint without a
            // bearer token results in a 401, exercising the .RequireAuthorization()
            // group filter.
            if (_disableAuth)
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, DenyAllAuthHandler>("Test", _ => { });
            }
            else
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, AllowAllAuthHandler>("Test", _ => { });
            }

            services.AddDistributedMemoryCache();

            services.Configure<OfficeRateLimitOptions>(o => o.Enabled = false);

            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = "Test";
                o.DefaultChallengeScheme = "Test";
            });

            services.RemoveAll<IHostedService>();
            services.AddSingleton<IOfficeRateLimitService, OfficeRateLimitService>();

            // Replace IGenericEntityService with the per-fixture mock so tests can
            // program Dataverse responses.
            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(EntityServiceMock.Object);

            // task 127 (#1020): the three Office communications routes read through the DELEGATED
            // client now, so the fixture must displace it too - otherwise the host resolves the real
            // typed HttpClient and every read fails closed against no Dataverse.
            services.RemoveAll<IDataverseUserClient>();
            services.AddSingleton(UserClientMock.Object);

            services.RemoveAll<Sprk.Bff.Api.Services.Communication.Engine.IAssociationRung>();
            services.AddSingleton<Sprk.Bff.Api.Services.Communication.Engine.IAssociationRung>(Rung);

            // Replace IDataverseService to avoid real Dataverse boot. This mirrors the
            // shape used by OfficeTestWebAppFactory.
            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);
        });
    }
}

/// <summary>
/// Always-authenticate handler used for the happy-path tests.
/// </summary>
internal sealed class AllowAllAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public AllowAllAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim("oid", "test-user-oid"),
            new Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(System.Security.Claims.ClaimTypes.Email, "test@example.com"),
            new Claim("tid", "test-tenant-id")
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Deny-all handler that returns <c>AuthenticateResult.NoResult</c> so
/// <c>RequireAuthorization()</c> triggers a 401 challenge.
/// </summary>
internal sealed class DenyAllAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public DenyAllAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(AuthenticateResult.NoResult());

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        return Task.CompletedTask;
    }
}
