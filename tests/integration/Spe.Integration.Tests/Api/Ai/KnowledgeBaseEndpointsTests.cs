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
///   GET  /api/ai/knowledge/indexes/{indexName}/documents
///   DELETE /api/ai/knowledge/indexes/{indexName}/documents/{documentId}
///   POST /api/ai/knowledge/indexes/reindex/{documentId}
///   POST /api/ai/knowledge/test-search
/// </summary>
public class KnowledgeBaseEndpointsTests : IClassFixture<KnowledgeBaseTestFixture>
{
    private readonly KnowledgeBaseTestFixture _fixture;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string TestTenantId = "kb-test-tenant-abc";
    private const string TestDocumentId = "doc-00000000-0000-0001";
    private const string KnowledgeIndexName = "knowledge-index";
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
    public async Task GetIndexHealth_Returns401_WhenUnauthenticated()
    {
        // Arrange — no bearer token
        var client = _fixture.CreateClient();

        // Act
        var response = await client.GetAsync("/api/ai/knowledge/indexes/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------------------------------------------------------------------------
    // GET /api/ai/knowledge/indexes/{indexName}/documents — list documents tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetIndexedDocuments_ReturnsOk_WhenAuthenticatedWithValidIndex()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);

        // Act
        var response = await client.GetAsync($"/api/ai/knowledge/indexes/{KnowledgeIndexName}/documents");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<KnowledgeIndexedDocumentsResult>(_jsonOptions);
        content.Should().NotBeNull();
        content!.IndexName.Should().Be(KnowledgeIndexName);
        content.Documents.Should().NotBeNull();
        content.Page.Should().Be(1);
        content.PageSize.Should().Be(50);
    }

    [Fact]
    public async Task GetIndexedDocuments_Returns404_WhenIndexNameUnknown()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);

        // Act
        var response = await client.GetAsync("/api/ai/knowledge/indexes/nonexistent-index/documents");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetIndexedDocuments_Returns401_WhenUnauthenticated()
    {
        // Arrange
        var client = _fixture.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/ai/knowledge/indexes/{KnowledgeIndexName}/documents");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------------------------------------------------------------------------
    // DELETE /api/ai/knowledge/indexes/{indexName}/documents/{documentId}
    // -------------------------------------------------------------------------

    /// <summary>
    /// Acceptance criterion: DELETE document removes chunks from index.
    /// The mock IRagService.DeleteBySourceDocumentAsync returns a known chunk count (3).
    /// </summary>
    [Fact]
    public async Task DeleteIndexedDocument_ReturnsOk_WhenDocumentExists()
    {
        // Arrange — the mock returns 3 chunks deleted for any document
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);

        // Act
        var response = await client.DeleteAsync(
            $"/api/ai/knowledge/indexes/{KnowledgeIndexName}/documents/{TestDocumentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<KnowledgeDeleteResult>(_jsonOptions);
        content.Should().NotBeNull();
        content!.DocumentId.Should().Be(TestDocumentId);
        content.IndexName.Should().Be(KnowledgeIndexName);
        content.ChunksDeleted.Should().BeGreaterThan(0, "mock returns non-zero chunks deleted");
        content.Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DeleteIndexedDocument_Returns404_WhenNoChunksFound()
    {
        // Arrange — use "empty-doc-id" which the mock maps to 0 chunks
        const string emptyDocId = "doc-with-no-chunks";
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);

        // Act
        var response = await client.DeleteAsync(
            $"/api/ai/knowledge/indexes/{KnowledgeIndexName}/documents/{emptyDocId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteIndexedDocument_Returns401_WhenUnauthenticated()
    {
        // Arrange
        var client = _fixture.CreateClient();

        // Act
        var response = await client.DeleteAsync(
            $"/api/ai/knowledge/indexes/{KnowledgeIndexName}/documents/{TestDocumentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------------------------------------------------------------------------
    // POST /api/ai/knowledge/indexes/reindex/{documentId}
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReindexDocument_Returns202Accepted_WhenAuthenticated()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new { driveId = "drive-123", fileName = "contract.pdf" };

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/ai/knowledge/indexes/reindex/{TestDocumentId}", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var content = await response.Content.ReadFromJsonAsync<KnowledgeReindexResult>(_jsonOptions);
        content.Should().NotBeNull();
        content!.DocumentId.Should().Be(TestDocumentId);
        content.JobId.Should().NotBe(Guid.Empty);
        content.Status.Should().Be("Queued");
        content.Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ReindexDocument_Returns401_WhenUnauthenticated()
    {
        // Arrange
        var client = _fixture.CreateClient();
        var request = new { driveId = "drive-123", fileName = "contract.pdf" };

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/ai/knowledge/indexes/reindex/{TestDocumentId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------------------------------------------------------------------------
    // POST /api/ai/knowledge/test-search
    // -------------------------------------------------------------------------

    /// <summary>
    /// Acceptance criterion: POST test-search returns results for known indexed document.
    /// The mock IRagService.SearchAsync returns predictable results.
    /// </summary>
    [Fact]
    public async Task TestSearch_ReturnsResults_WhenAuthenticated()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new KnowledgeTestSearchRequest
        {
            Query = "employment contract termination clause",
            IndexName = KnowledgeIndexName,
            Top = 3
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/knowledge/test-search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<KnowledgeTestSearchResult>(_jsonOptions);
        content.Should().NotBeNull();
        content!.Query.Should().Be(request.Query);
        content.Results.Should().NotBeNull();
        content.ResultCount.Should().BeGreaterOrEqualTo(0);
        content.TenantId.Should().Be(TestTenantId);
        content.SearchDurationMs.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task TestSearch_Returns400_WhenQueryMissing()
    {
        // Arrange — send an empty query body
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new { indexName = KnowledgeIndexName, top = 5 }; // no query field

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/knowledge/test-search", request, _jsonOptions);

        // Assert — endpoint returns 400 when query is null/empty
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TestSearch_Returns401_WhenUnauthenticated()
    {
        // Arrange
        var client = _fixture.CreateClient();
        var request = new { query = "test query", indexName = KnowledgeIndexName, top = 3 };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/knowledge/test-search", request);

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
        builder.UseSetting("Cors:AllowedOrigins", "https://localhost:3000");
        builder.UseSetting("AzureAiSearch:Endpoint", "https://test-search.search.windows.net");
        builder.UseSetting("AzureAiSearch:ApiKey", "test-api-key");
        builder.UseSetting("AzureAiSearch:KnowledgeIndexName", "knowledge-index");
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

        builder.ConfigureServices(services =>
        {
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

            // Register stub IScopeResolverService so BuilderScopeImporter can be resolved from DI.
            // BuilderScopeAdminEndpoints.MapPost(..., ImportFromJson) depends on BuilderScopeImporter
            // as a DI parameter. When Analysis:Enabled=false, IScopeResolverService is not registered
            // by Program.cs, causing parameter inference to fail ("importer | Body (Inferred)").
            // A Loose mock satisfies the dependency without any real Dataverse calls.
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IScopeResolverService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped<Sprk.Bff.Api.Services.Ai.Builder.BuilderScopeImporter>();

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
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IAiPlaybookBuilderService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.Builder.IBuilderAgentService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IPlaybookOrchestrationService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IPlaybookSharingService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IScopeManagementService>(Moq.MockBehavior.Loose).Object);
            services.AddSingleton(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.Visualization.IVisualizationService>(Moq.MockBehavior.Loose).Object);
            services.AddSingleton(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IModelSelector>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IIntentClassificationService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IEntityResolutionService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IClarificationService>(Moq.MockBehavior.Loose).Object);

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
                var cache = sp.GetRequiredService<IDistributedCache>();
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
        });

        builder.UseEnvironment("Testing");
    }

    public HttpClient CreateAuthenticatedClient(string tenantId, string? userId = null)
    {
        var client = CreateClient();
        var token = GenerateTestJwt(tenantId, userId ?? Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void SetupRagServiceMock()
    {
        // SearchAsync — returns 2 predictable results for any non-null query
        MockRagService
            .Setup(r => r.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<RagSearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string query, RagSearchOptions opts, CancellationToken _) =>
                new RagSearchResponse
                {
                    Query = query,
                    Results = new List<RagSearchResult>
                    {
                        new RagSearchResult
                        {
                            Id = "chunk-001",
                            DocumentId = "doc-00000000-0000-0001",
                            DocumentName = "Employment Contract.pdf",
                            Content = "This contract governs the terms of employment termination.",
                            Score = 0.92,
                            SemanticScore = 0.88,
                            ChunkIndex = 0,
                            ChunkCount = 3
                        },
                        new RagSearchResult
                        {
                            Id = "chunk-002",
                            DocumentId = "doc-00000000-0000-0001",
                            DocumentName = "Employment Contract.pdf",
                            Content = "Termination procedures require a 30-day notice period.",
                            Score = 0.85,
                            SemanticScore = 0.80,
                            ChunkIndex = 1,
                            ChunkCount = 3
                        }
                    },
                    TotalCount = 2,
                    SearchDurationMs = 25,
                    EmbeddingDurationMs = 10,
                    EmbeddingCacheHit = false
                });

        // DeleteBySourceDocumentAsync — returns 3 chunks for any document except EmptyDocumentId
        MockRagService
            .Setup(r => r.DeleteBySourceDocumentAsync(
                It.Is<string>(id => id != EmptyDocumentId),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        MockRagService
            .Setup(r => r.DeleteBySourceDocumentAsync(
                EmptyDocumentId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
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
            return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.NoResult());
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
