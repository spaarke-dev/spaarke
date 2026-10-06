using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Spe.Integration.Tests.Api.Ai;

/// <summary>
/// Integration tests for knowledge base management API endpoints.
/// Tests HTTP request/response flow with mocked Azure AI Search and RAG service dependencies.
///
/// Endpoints under test:
///   GET  /api/ai/knowledge/indexes/health
///
/// The list / delete / reindex / test-search routes this file also covered were DELETED by
/// unified-access-control-r2 task 163 (owner round 10 item 1: no caller, not published); their absence
/// is pinned by tests/integration/regression/KnowledgeAndRagRouteRetirementTests.cs.
/// </summary>
public class KnowledgeBaseEndpointsTests : IClassFixture<KnowledgeBaseTestFixture>
{
    private readonly KnowledgeBaseTestFixture _fixture;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string TestTenantId = "kb-test-tenant-abc";
    private const string TestDocumentId = "doc-00000000-0000-0001";
    private const string KnowledgeIndexName = "spaarke-knowledge-index-v2";
    private const string DiscoveryIndexName = "discovery-index";

    public KnowledgeBaseEndpointsTests(KnowledgeBaseTestFixture fixture)
    {
        _fixture = fixture;
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }

    // -------------------------------------------------------------------------
    // GET /api/ai/knowledge/indexes/health — document count tests
    // -------------------------------------------------------------------------

    /// <summary>
    /// Acceptance criterion: GET /health returns KnowledgeDocCount and DiscoveryDocCount.
    /// The mock SearchIndexClient returns deterministic counts for each index.
    /// </summary>
    [Fact]
    [Trait("status", "repaired")]
    public async Task GetIndexHealth_ReturnsDocCounts_WhenAuthenticated()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);

        // Act
        var response = await client.GetAsync("/api/ai/knowledge/indexes/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<KnowledgeIndexHealthResult>(_jsonOptions);
        content.Should().NotBeNull();
        content!.KnowledgeDocCount.Should().BeGreaterOrEqualTo(0);
        content.DiscoveryDocCount.Should().BeGreaterOrEqualTo(0);
        content.LastUpdated.Should().NotBe(default);
        content.KnowledgeIndexName.Should().Be(KnowledgeIndexName);
        content.DiscoveryIndexName.Should().Be(DiscoveryIndexName);
    }

    [Fact]
    [Trait("status", "repaired")]
    public async Task GetIndexHealth_Returns401_WhenUnauthenticated()
    {
        // Arrange — no bearer token
        var client = _fixture.CreateClient();

        // Act
        var response = await client.GetAsync("/api/ai/knowledge/indexes/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// =============================================================================
// Test Fixture
// =============================================================================

/// <summary>
/// WebApplicationFactory fixture for knowledge base endpoint integration tests.
/// Mocks Azure AI Search (SearchIndexClient) and IRagService to avoid external service calls.
/// Registers a test JWT authentication handler matching the production JWT claims structure.
/// </summary>
public class KnowledgeBaseTestFixture : WebApplicationFactory<Program>
{
    // The mock IRagService — public so tests can access/verify calls if needed
    public Mock<IRagService> MockRagService { get; } = new();

    // Sentinel doc ID that the mock maps to 0 deleted chunks (triggers 404)
    public const string EmptyDocumentId = "doc-with-no-chunks";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Inject test configuration so Program.cs startup guards (e.g. ServiceBus check) don't throw.
        // UseSetting directly patches the underlying IWebHostBuilder before Program.cs reads config.
        builder.UseSetting(
            "ConnectionStrings:ServiceBus",
            "Endpoint=sb://test-namespace.servicebus.windows.net/;" +
            "SharedAccessKeyName=test;SharedAccessKey=dGVzdC1rZXktZm9yLWludGVncmF0aW9uLXRlc3Rpbmc=");
        builder.UseSetting("AzureAd:TenantId", "test-tenant-id");
        builder.UseSetting("AzureAd:ClientId", "test-client-id");
        builder.UseSetting("AzureAd:ClientSecret", "test-secret");
        builder.UseSetting("Dataverse:ServiceUrl", "https://test.crm.dynamics.com");
        builder.UseSetting("Dataverse:ClientId", "test-client-id");
        builder.UseSetting("Dataverse:ClientSecret", "test-secret");
        builder.UseSetting("Graph:TenantId", "test-tenant-id");
        builder.UseSetting("Graph:ClientId", "test-client-id");
        builder.UseSetting("Graph:ClientSecret", "test-secret");
        builder.UseSetting("Cors:AllowedOrigins:0", "https://localhost:3000");
        builder.UseSetting("AzureAiSearch:Endpoint", "https://test-search.search.windows.net");
        builder.UseSetting("AzureAiSearch:ApiKey", "test-api-key");
        builder.UseSetting("AzureAiSearch:KnowledgeIndexName", "spaarke-knowledge-index-v2");
        builder.UseSetting("AzureAiSearch:DiscoveryIndexName", "discovery-index");
        builder.UseSetting("AzureOpenAI:Endpoint", "https://test.openai.azure.com/");
        builder.UseSetting("AzureOpenAI:ApiKey", "test-api-key");
        builder.UseSetting("AzureOpenAI:DeploymentName", "gpt-4");
        builder.UseSetting("AzureOpenAI:EmbeddingsDeploymentName", "text-embedding-3-small");

        // Graph options — requires at least one scope
        builder.UseSetting("Graph:Scopes:0", "https://graph.microsoft.com/.default");
        builder.UseSetting("Graph:Instance", "https://login.microsoftonline.com/");

        // Dataverse options validation
        builder.UseSetting("Dataverse:EnvironmentUrl", "https://test.crm.dynamics.com");
        builder.UseSetting("Dataverse:TenantId", "test-tenant-id");

        // ServiceBus options validation
        builder.UseSetting(
            "ServiceBus:ConnectionString",
            "Endpoint=sb://test-namespace.servicebus.windows.net/;" +
            "SharedAccessKeyName=test;SharedAccessKey=dGVzdC1rZXktZm9yLWludGVncmF0aW9uLXRlc3Rpbmc=");
        builder.UseSetting("ServiceBus:QueueName", "sdap-jobs");

        // Disable DocumentIntelligence features to avoid validation of OpenAI keys
        builder.UseSetting("DocumentIntelligence:Enabled", "false");
        builder.UseSetting("DocumentIntelligence:RecordMatchingEnabled", "false");
        builder.UseSetting("Analysis:Enabled", "false");

        // SpeAdmin — required by SpeAdminModule (KeyVault SecretClient).
        // Per sdap-bff.api-test-suite-repair task 027 (sibling-fixture absorption).
        // Mirrors IntegrationTestFixture.cs line 74 (canonical fix in task 062).
        builder.UseSetting("SpeAdmin:KeyVaultUri", "https://test-keyvault.vault.azure.net/");

        // CosmosPersistence — required by AiPersistenceModule (raw config read).
        // Per sdap-bff.api-test-suite-repair task 027 (sibling-fixture absorption).
        // Mirrors IntegrationTestFixture.cs line 81 (canonical fix in task 062).
        builder.UseSetting("CosmosPersistence:Endpoint", "https://test.documents.azure.com:443/");

        builder.ConfigureTestServices(services =>
        {
            // Test hosts must not authenticate for real — see TestTokenCredential.
            services.UseStubTokenCredential();

            // Replace IRagService with a controllable mock
            services.RemoveAll<IRagService>();
            SetupRagServiceMock();
            services.AddSingleton(MockRagService.Object);

            // Replace SearchIndexClient with a mock that returns predictable data
            services.RemoveAll<SearchIndexClient>();
            services.AddSingleton(CreateMockSearchIndexClient());

            // Remove all background (hosted) services to prevent DI resolution failures.
            // Background workers depend on conditionally-registered services (disabled in test mode).
            services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();

            // as a DI parameter. When Analysis:Enabled=false, IScopeResolverService is not registered
            // by Program.cs, causing parameter inference to fail ("importer | Body (Inferred)").
            // A Loose mock satisfies the dependency without any real Dataverse calls.
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IScopeResolverService>(Moq.MockBehavior.Loose).Object);

            // ---------------------------------------------------------------
            // Stub all conditionally-registered services (registered by Program.cs only
            // when Analysis:Enabled=true && DocumentIntelligence:Enabled=true).
            // The minimal API framework validates endpoint parameter bindings at startup —
            // if any service parameter is unresolvable, it infers it as "Body", causing
            // a startup failure. These stubs are Loose mocks returning null/defaults.
            // ---------------------------------------------------------------
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IFileIndexingService>(Moq.MockBehavior.Loose).Object);
            services.AddSingleton(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IKnowledgeDeploymentService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IAnalysisOrchestrationService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IAppOnlyAnalysisService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IPlaybookService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.INodeService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IPlaybookOrchestrationService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IPlaybookSharingService>(Moq.MockBehavior.Loose).Object);
            services.AddSingleton(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.Visualization.IVisualizationService>(Moq.MockBehavior.Loose).Object);
            services.AddSingleton(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IModelSelector>(Moq.MockBehavior.Loose).Object);

            // Semantic Search & Record Search - endpoints are always mapped but services
            // only register when Analysis:Enabled=true && DocumentIntelligence:Enabled=true
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.SemanticSearch.ISemanticSearchService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.RecordSearch.IRecordSearchService>(Moq.MockBehavior.Loose).Object);

            // ITextChunkingService stub — kept for any chunking consumer DI may construct in this host.
            services.AddSingleton(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.ITextChunkingService>(Moq.MockBehavior.Loose).Object);

            // IRecordMatchService — used by RecordMatchEndpoints (always mapped).
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.RecordMatching.IRecordMatchService>(Moq.MockBehavior.Loose).Object);

            // IOpenAiClient is conditionally registered (DocumentIntelligence:Enabled=true only),
            // but FinanceModule services (InvoiceAnalysisService, InvoiceSearchService, etc.) always
            // depend on it. Register a stub to prevent InvalidOperationException during scope activation.
            services.RemoveAll<Sprk.Bff.Api.Services.Ai.IOpenAiClient>();
            services.AddSingleton(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IOpenAiClient>(Moq.MockBehavior.Loose).Object);

            // ITextExtractor is conditionally registered. Register both interface mock and concrete type
            // (TextExtractorService is directly injected by some job handlers).
            services.RemoveAll<Sprk.Bff.Api.Services.Ai.ITextExtractor>();
            services.AddSingleton(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.ITextExtractor>(Moq.MockBehavior.Loose).Object);
            services.AddSingleton<Sprk.Bff.Api.Services.Ai.TextExtractorService>();

            // IDataverseService factory in Program.cs calls DataverseServiceClientImpl constructor
            // which reads TENANT_ID/API_APP_ID/API_CLIENT_SECRET and tries to connect to Dataverse.
            // Replace with a Loose mock to prevent connection failures.
            services.RemoveAll<Spaarke.Dataverse.IDataverseService>();
            services.AddSingleton(_ => new Moq.Mock<Spaarke.Dataverse.IDataverseService>(Moq.MockBehavior.Loose).Object);

            // IAccessDataSource (used by IAiAuthorizationService and ResourceAccessHandler) makes real
            // Dataverse calls. Replace with a Loose mock.
            services.RemoveAll<Spaarke.Dataverse.IAccessDataSource>();
            services.AddScoped(_ => new Moq.Mock<Spaarke.Dataverse.IAccessDataSource>(Moq.MockBehavior.Loose).Object);

            // ServiceBusClient — registered unconditionally in Program.cs. When JobSubmissionService
            // calls CreateSender().SendMessageAsync(), it tries to connect to the real Service Bus
            // (causing 19s timeout). Replace with a mock sender that no-ops.
            services.RemoveAll<Azure.Messaging.ServiceBus.ServiceBusClient>();
            var mockSbSender = new Moq.Mock<Azure.Messaging.ServiceBus.ServiceBusSender>(Moq.MockBehavior.Loose);
            mockSbSender
                .Setup(s => s.SendMessageAsync(
                    Moq.It.IsAny<Azure.Messaging.ServiceBus.ServiceBusMessage>(),
                    Moq.It.IsAny<CancellationToken>()))
                .Returns(System.Threading.Tasks.Task.CompletedTask);
            var mockSbClient = new Moq.Mock<Azure.Messaging.ServiceBus.ServiceBusClient>(Moq.MockBehavior.Loose);
            mockSbClient
                .Setup(c => c.CreateSender(Moq.It.IsAny<string>()))
                .Returns(mockSbSender.Object);
            services.AddSingleton(mockSbClient.Object);

            // Chat services — registered by AiModule (only when Analysis:Enabled=true &&
            // DocumentIntelligence:Enabled=true). ChatEndpoints are always mapped in Program.cs so
            // parameter inference fails at startup if these aren't registered.
            services.AddScoped(_ => new Mock<IChatDataverseRepository>(MockBehavior.Loose).Object);
            services.AddScoped(_ => new Mock<IChatContextProvider>(MockBehavior.Loose).Object);
            services.AddSingleton(_ => new Mock<IChatClient>(MockBehavior.Loose).Object);
            services.AddSingleton(sp =>
            {
                var chatClient = sp.GetRequiredService<IChatClient>();
                var logger = NullLogger<SprkChatAgentFactory>.Instance;
                return new SprkChatAgentFactory(chatClient, sp, logger);
            });
            services.AddScoped(sp =>
            {
                var cache = sp.GetRequiredService<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>();
                var repo = sp.GetRequiredService<IChatDataverseRepository>();
                var logger = NullLogger<ChatSessionManager>.Instance;
                return new ChatSessionManager(cache, repo, logger);
            });
            services.AddScoped(sp =>
            {
                var sessionManager = sp.GetRequiredService<ChatSessionManager>();
                var repo = sp.GetRequiredService<IChatDataverseRepository>();
                var logger = NullLogger<ChatHistoryManager>.Instance;
                return new ChatHistoryManager(sessionManager, repo, logger);
            });
            services.AddSingleton<ILogger<SprkChatAgent>>(NullLogger<SprkChatAgent>.Instance);

            // Register test JWT authentication scheme (overrides production JWT bearer)
            services.AddAuthentication("Test")
                .AddScheme<TestAuthSchemeOptions, TestKbAuthHandler>("Test", _ => { });

            // Override Microsoft Identity Web's PostConfigure which replaces our
            // DefaultAuthenticateScheme/DefaultChallengeScheme.
            services.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
            });
        });

        builder.UseEnvironment("Testing");
    }

    public HttpClient CreateAuthenticatedClient(string tenantId, string? userId = null)
    {
        var client = CreateClient();
        var token = GenerateTestJwt(tenantId, userId ?? IntegrationTestConstants.TestUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void SetupRagServiceMock()
    {
        // GetIndexHealthAsync — returns a non-null KnowledgeIndexHealth with deterministic counts.
        // (Task 163 deleted the list / delete / reindex / test-search routes this fixture also served;
        // their IRagService setups went with them. GET /indexes/health is the surviving route.)
        MockRagService
            .Setup(r => r.GetIndexHealthAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeIndexHealth(
                KnowledgeDocCount: 42,
                DiscoveryDocCount: 17,
                LastUpdated: DateTimeOffset.UtcNow,
                KnowledgeIndexName: "spaarke-knowledge-index-v2",
                DiscoveryIndexName: "discovery-index"));
    }

    /// <summary>
    /// Creates a mock SearchIndexClient where GetSearchClient returns a mock SearchClient.
    /// The mock SearchClient uses SearchModelFactory to return valid (empty) SearchResults
    /// so endpoints can call response.Value.TotalCount and response.Value.GetResultsAsync()
    /// without a NullReferenceException.
    /// </summary>
    private static SearchIndexClient CreateMockSearchIndexClient()
    {
        // Build a valid empty SearchResults<KnowledgeDocument> via SearchModelFactory.
        // SearchResults<T> has internal constructors — SearchModelFactory is the approved way
        // to construct test instances (same pattern used in unit tests).
        var emptySearchResults = Azure.Search.Documents.Models.SearchModelFactory.SearchResults<KnowledgeDocument>(
            values: new List<Azure.Search.Documents.Models.SearchResult<KnowledgeDocument>>(),
            totalCount: 0,
            facets: null,
            coverage: null,
            rawResponse: null!);

        var mockSearchClient = new Mock<SearchClient>(MockBehavior.Loose);

        // Setup SearchAsync to return a valid Response<SearchResults<T>> with 0 results.
        // This prevents NullReferenceException in endpoints that call response.Value.TotalCount
        // or response.Value.GetResultsAsync().
        mockSearchClient
            .Setup(c => c.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Azure.Response.FromValue(emptySearchResults, null!));

        var mockIndexClient = new Mock<SearchIndexClient>(MockBehavior.Loose);
        mockIndexClient
            .Setup(c => c.GetSearchClient(It.IsAny<string>()))
            .Returns(mockSearchClient.Object);

        return mockIndexClient.Object;
    }

    private static string GenerateTestJwt(string tenantId, string userId)
    {
        var claims = new[]
        {
            new Claim("tid", tenantId),
            new Claim("oid", userId),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("sub", userId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("test-secret-key-for-jwt-token-generation-minimum-32-chars"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "https://test.spaarke.local",
            audience: "api://spaarke-test",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

// =============================================================================
// Test Authentication Handler
// (Same pattern as SemanticSearchIntegrationTests.TestAuthHandler)
// =============================================================================

/// <summary>
/// Test JWT authentication handler: reads the Bearer token from the Authorization header,
/// parses it without signature validation, and surfaces the claims as the authenticated user.
/// Mirrors the production handler structure so that tenantId (tid claim) flows correctly.
/// </summary>
internal class TestKbAuthHandler
    : Microsoft.AspNetCore.Authentication.AuthenticationHandler<TestAuthSchemeOptions>
{
    public TestKbAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<TestAuthSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Fail("No Authorization header"));
        }

        var token = authHeader["Bearer ".Length..].Trim();

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);

            var claims = jwtToken.Claims.ToList();
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal, "Test");

            return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(ticket));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Fail(ex));
        }
    }
}

internal class TestAuthSchemeOptions : Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions
{
}
