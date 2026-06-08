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
public class CommunicationsEndpointsTests : IClassFixture<OfficeCommunicationsTestWebAppFactory>
{
    private readonly OfficeCommunicationsTestWebAppFactory _factory;
    private readonly HttpClient _client;

    public CommunicationsEndpointsTests(OfficeCommunicationsTestWebAppFactory factory)
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
        using var anonFactory = new OfficeCommunicationsTestWebAppFactory(disableAuth: true);
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

        _factory.EntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q =>
                    q.EntityName == "sprk_communication"
                    && q.Criteria.Conditions.Count == 1
                    && q.Criteria.Conditions[0].AttributeName == "sprk_internetmessageid"
                    && (string)q.Criteria.Conditions[0].Values[0] == messageId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var entity = new Entity("sprk_communication", expectedCommunicationId);
                entity["sprk_communicationid"] = expectedCommunicationId;
                entity["sprk_subject"] = expectedSubject;
                entity["sprk_internetmessageid"] = messageId;
                var collection = new EntityCollection(new[] { entity });
                return collection;
            });

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
    // GET /api/office/communications/{commId}/linked-todos
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLinkedTodos_WithoutAuth_Returns401()
    {
        // Arrange
        using var anonFactory = new OfficeCommunicationsTestWebAppFactory(disableAuth: true);
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

        _factory.EntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q =>
                    q.EntityName == "sprk_todo"
                    && q.TopCount == 10
                    && q.Criteria.Conditions.Count == 1
                    && q.Criteria.Conditions[0].AttributeName == "sprk_regardingcommunication"
                    && (Guid)q.Criteria.Conditions[0].Values[0] == commId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var entities = todoIds.Select((id, idx) =>
                {
                    var e = new Entity("sprk_todo", id);
                    e["sprk_todoid"] = id;
                    e["sprk_name"] = $"Todo {idx + 1}";
                    e["statecode"] = new OptionSetValue(0);
                    e["statuscode"] = new OptionSetValue(1);
                    return e;
                }).ToArray();
                return new EntityCollection(entities);
            });

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

    private readonly bool _disableAuth;

    public OfficeCommunicationsTestWebAppFactory() : this(disableAuth: false) { }

    public OfficeCommunicationsTestWebAppFactory(bool disableAuth)
    {
        _disableAuth = disableAuth;
    }

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
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["OfficeRateLimit:Enabled"] = "false",
                ["Redis:Enabled"] = "false",
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
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
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
