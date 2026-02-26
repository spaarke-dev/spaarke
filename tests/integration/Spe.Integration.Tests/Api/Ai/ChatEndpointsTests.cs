using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

// Explicit alias to avoid ambiguity with domain ChatMessage
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Spe.Integration.Tests.Api.Ai;

/// <summary>
/// Integration tests for the SprkChat session API endpoints.
///
/// Tests HTTP request/response flow with mocked ChatSessionManager, ChatHistoryManager,
/// SprkChatAgentFactory, SprkChatAgent, and IChatClient to avoid external service calls.
///
/// Endpoints under test:
///   POST   /api/ai/chat/sessions
///   POST   /api/ai/chat/sessions/{id}/messages  (SSE)
///   POST   /api/ai/chat/sessions/{id}/refine    (SSE)
///   GET    /api/ai/chat/sessions/{id}/history
///   PATCH  /api/ai/chat/sessions/{id}/context
///   DELETE /api/ai/chat/sessions/{id}
/// </summary>
public class ChatEndpointsTests : IClassFixture<ChatEndpointsTestFixture>
{
    private readonly ChatEndpointsTestFixture _fixture;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string TestTenantId = "chat-test-tenant-abc";
    private const string TestSessionId = "test-session-123";
    private const string TestDocumentId = "doc-test-001";
    private static readonly Guid TestPlaybookId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public ChatEndpointsTests(ChatEndpointsTestFixture fixture)
    {
        _fixture = fixture;
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }

    // -------------------------------------------------------------------------
    // POST /api/ai/chat/sessions — create session
    // -------------------------------------------------------------------------

    /// <summary>
    /// Acceptance criterion: POST /sessions creates a session and returns sessionId.
    /// </summary>
    [Fact]
    public async Task CreateSession_Returns201_WhenAuthenticated()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new { DocumentId = TestDocumentId, PlaybookId = TestPlaybookId };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/chat/sessions", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var content = await response.Content.ReadFromJsonAsync<ChatSessionCreatedResponse>(_jsonOptions);
        content.Should().NotBeNull();
        content!.SessionId.Should().NotBeNullOrEmpty("a new session ID must be returned");
        content.CreatedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task CreateSession_Returns401_WhenUnauthenticated()
    {
        // Arrange — no bearer token
        var client = _fixture.CreateClient();
        var request = new { DocumentId = TestDocumentId, PlaybookId = TestPlaybookId };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/chat/sessions", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------------------------------------------------------------------------
    // POST /api/ai/chat/sessions/{id}/messages — SSE streaming
    // -------------------------------------------------------------------------

    /// <summary>
    /// Acceptance criterion: POST /sessions/{id}/messages returns SSE stream with "token" and "done" events.
    /// </summary>
    [Fact]
    public async Task SendMessage_ReturnsSseStream_WithTokenAndDoneEvents()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new ChatSendMessageRequest("What are the key risks in this contract?");

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/messages", request, _jsonOptions);

        // Assert — SSE response
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"type\":\"token\"");
        body.Should().Contain("\"type\":\"done\"");
        body.Should().Contain("data: ");
    }

    [Fact]
    public async Task SendMessage_Returns401_WhenUnauthenticated()
    {
        // Arrange
        var client = _fixture.CreateClient();
        var request = new { Message = "test message" };

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/messages", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SendMessage_Returns404_WhenSessionNotFound()
    {
        // Arrange — use a session ID that the mock returns null for
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new { Message = "test message" };

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/ai/chat/sessions/nonexistent-session/messages", request, _jsonOptions);

        // Assert — 404 returned inline (SSE not set up for non-existent sessions)
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -------------------------------------------------------------------------
    // GET /api/ai/chat/sessions/{id}/history — message history
    // -------------------------------------------------------------------------

    /// <summary>
    /// Acceptance criterion: GET /sessions/{id}/history returns messages.
    /// </summary>
    [Fact]
    public async Task GetHistory_ReturnsMessages_WhenAuthenticated()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);

        // Act
        var response = await client.GetAsync($"/api/ai/chat/sessions/{TestSessionId}/history");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<ChatHistoryResponse>(_jsonOptions);
        content.Should().NotBeNull();
        content!.SessionId.Should().Be(TestSessionId);
        content.Messages.Should().NotBeNull();
        content.Messages.Length.Should().Be(2); // Session mock returns 2 messages
    }

    [Fact]
    public async Task GetHistory_Returns401_WhenUnauthenticated()
    {
        // Arrange
        var client = _fixture.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/ai/chat/sessions/{TestSessionId}/history");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------------------------------------------------------------------------
    // PATCH /api/ai/chat/sessions/{id}/context — context switch
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SwitchContext_Returns204_WhenAuthenticated()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new { DocumentId = "doc-new-001", PlaybookId = TestPlaybookId };

        // Act
        var response = await client.PatchAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/context", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SwitchContext_Returns401_WhenUnauthenticated()
    {
        // Arrange
        var client = _fixture.CreateClient();
        var request = new { DocumentId = "doc-new-001" };

        // Act
        var response = await client.PatchAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/context", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------------------------------------------------------------------------
    // DELETE /api/ai/chat/sessions/{id} — delete session
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteSession_Returns204_WhenAuthenticated()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);

        // Act
        var response = await client.DeleteAsync($"/api/ai/chat/sessions/{TestSessionId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteSession_Returns401_WhenUnauthenticated()
    {
        // Arrange
        var client = _fixture.CreateClient();

        // Act
        var response = await client.DeleteAsync($"/api/ai/chat/sessions/{TestSessionId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// =============================================================================
// Test Fixture
// =============================================================================

/// <summary>
/// WebApplicationFactory fixture for chat endpoint integration tests.
///
/// Strategy for sealed classes (ChatHistoryManager, SprkChatAgentFactory):
///   - These classes cannot be mocked via Moq (sealed).
///   - Real instances are constructed with mocked constructor dependencies.
///   - ChatHistoryManager uses the same MockSessionManager (not sealed) and a
///     MockDataverseRepository (interface) as constructor arguments.
///   - SprkChatAgentFactory is registered via DI factory delegate using the test
///     IServiceProvider, with a mock IChatContextProvider registered to satisfy
///     CreateAgentAsync's scope resolution.
///
/// ChatSessionManager (not sealed):
///   - Mocked with MockBehavior.Loose.
///   - InternalsVisibleTo("DynamicProxyGenAssembly2") in Sprk.Bff.Api.csproj allows
///     Moq to intercept the internal virtual UpdateSessionCacheAsync method.
///
/// Registers a test JWT authentication scheme matching the production JWT claims structure.
/// </summary>
public class ChatEndpointsTestFixture : WebApplicationFactory<Program>
{
    public const string CreatedSessionId = "created-session-001";
    private const string TestSessionId = "test-session-123";
    private const string TestDocumentId = "doc-test-001";
    private static readonly Guid TestPlaybookId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // IChatDataverseRepository is an interface — fully mockable.
    // Used as constructor argument for both real ChatSessionManager and ChatHistoryManager.
    public Mock<IChatDataverseRepository> MockDataverseRepository { get; } = new(MockBehavior.Loose);

    // IChatContextProvider is an interface — fully mockable.
    // Registered in test DI so SprkChatAgentFactory.CreateAgentAsync can resolve it.
    public Mock<IChatContextProvider> MockContextProvider { get; } = new(MockBehavior.Loose);

    // IChatClient is an interface — fully mockable.
    public Mock<IChatClient> MockChatClient { get; } = new(MockBehavior.Loose);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Inject test configuration so Program.cs startup guards (e.g. ServiceBus check) don't throw.
        // WebApplicationFactory uses the API project's configuration files, not the test project's.
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
            // ---------------------------------------------------------------
            // Remove real service registrations and replace with test doubles
            // ---------------------------------------------------------------
            services.RemoveAll<ChatSessionManager>();
            services.RemoveAll<ChatHistoryManager>();
            services.RemoveAll<SprkChatAgentFactory>();
            services.RemoveAll<IChatClient>();
            services.RemoveAll<IChatContextProvider>();
            services.RemoveAll<IChatDataverseRepository>();
            // NOTE: Do NOT remove IDistributedCache — it's used by many other services
            // (IndexingWorkerHostedService, etc.). The real in-memory cache from Program.cs is fine.
            // Our real ChatSessionManager will use the real in-memory IDistributedCache.

            SetupDataverseRepositoryMock();
            SetupContextProviderMock();
            SetupChatClientMock();

            // Register mocked IChatDataverseRepository (Dataverse calls are mocked)
            services.AddScoped(_ => MockDataverseRepository.Object);

            // Remove all background (hosted) services to prevent DI resolution failures from
            // services that are conditionally registered (DocumentIntelligence disabled in test mode)
            // but required by background workers. The chat/knowledge endpoints don't need workers.
            services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();

            // ---------------------------------------------------------------
            // Stub all conditionally-registered services (registered by Program.cs only
            // when Analysis:Enabled=true && DocumentIntelligence:Enabled=true).
            // The minimal API framework validates endpoint parameter bindings at startup —
            // if any service parameter is unresolvable, it infers it as "Body", causing
            // a startup failure. These stubs are Loose mocks returning null/defaults.
            // ---------------------------------------------------------------
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IScopeResolverService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped<Sprk.Bff.Api.Services.Ai.Builder.BuilderScopeImporter>();
            services.AddSingleton(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IRagService>(Moq.MockBehavior.Loose).Object);
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
            // SearchIndexClient is needed by KnowledgeBaseEndpoints
            services.AddSingleton(_ => new Azure.Search.Documents.Indexes.SearchIndexClient(
                new Uri("https://test-search.search.windows.net"),
                new Azure.AzureKeyCredential("test-api-key")));

            // IOpenAiClient is conditionally registered (DocumentIntelligence:Enabled=true only),
            // but FinanceModule services (InvoiceAnalysisService, InvoiceSearchService, etc.) always
            // depend on it. Register a stub to prevent InvalidOperationException during scope activation.
            services.RemoveAll<Sprk.Bff.Api.Services.Ai.IOpenAiClient>();
            services.AddSingleton(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IOpenAiClient>(Moq.MockBehavior.Loose).Object);

            // ITextExtractor is conditionally registered (DocumentIntelligence:Enabled=true only),
            // but WorkspaceModule (MatterPreFillService) always depends on it.
            // TextExtractorService (concrete) is also directly injected by some job handlers — register
            // both the interface mock AND the concrete type (uses IOptions<DocumentIntelligenceOptions>
            // which is always registered; will return NotSupported for all file types in test mode).
            services.RemoveAll<Sprk.Bff.Api.Services.Ai.ITextExtractor>();
            services.AddSingleton(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.ITextExtractor>(Moq.MockBehavior.Loose).Object);
            services.AddSingleton<Sprk.Bff.Api.Services.Ai.TextExtractorService>();

            // IDataverseService is a singleton registered via factory in Program.cs (line 362).
            // The factory calls DataverseServiceClientImpl constructor which reads TENANT_ID, API_APP_ID,
            // API_CLIENT_SECRET from config and then tries to connect to Dataverse — both fail in tests.
            // Replace with a Loose mock that returns null/default for all methods.
            services.RemoveAll<Spaarke.Dataverse.IDataverseService>();
            services.AddSingleton(_ => new Moq.Mock<Spaarke.Dataverse.IDataverseService>(Moq.MockBehavior.Loose).Object);

            // IAccessDataSource (used by IAiAuthorizationService and ResourceAccessHandler) also makes
            // real Dataverse calls. Replace with a Loose mock.
            services.RemoveAll<Spaarke.Dataverse.IAccessDataSource>();
            services.AddScoped(_ => new Moq.Mock<Spaarke.Dataverse.IAccessDataSource>(Moq.MockBehavior.Loose).Object);

            // IAiAuthorizationService (registered by SpaarkeCore) makes real Dataverse calls via
            // IAccessDataSource. Replace with a mock that approves all authenticated requests so the
            // AiAuthorizationFilter passes through to the endpoint handlers in tests.
            services.RemoveAll<Sprk.Bff.Api.Services.Ai.IAiAuthorizationService>();
            var mockAiAuthService = new Moq.Mock<Sprk.Bff.Api.Services.Ai.IAiAuthorizationService>(Moq.MockBehavior.Loose);
            mockAiAuthService
                .Setup(s => s.AuthorizeAsync(
                    Moq.It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                    Moq.It.IsAny<System.Collections.Generic.IReadOnlyList<Guid>>(),
                    Moq.It.IsAny<Microsoft.AspNetCore.Http.HttpContext>(),
                    Moq.It.IsAny<CancellationToken>()))
                .ReturnsAsync(Sprk.Bff.Api.Services.Ai.AuthorizationResult.Authorized(
                    System.Array.Empty<Guid>()));
            services.AddScoped(_ => mockAiAuthService.Object);

            // ChatSessionManager (not sealed, but CreateSessionAsync/DeleteSessionAsync are NOT virtual
            // so Moq cannot mock them). Use a real instance with:
            //   - Real IDistributedCache (in-memory) from DI — avoids breaking other services
            //   - Mocked IChatDataverseRepository — returns test data without Dataverse calls
            services.AddScoped(sp =>
            {
                var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
                var logger = NullLogger<ChatSessionManager>.Instance;
                return new ChatSessionManager(
                    cache,
                    MockDataverseRepository.Object,
                    logger);
            });

            // ChatHistoryManager (sealed): construct real instance with mocked dependencies.
            // GetHistoryAsync delegates to _sessionManager.GetSessionAsync, which calls our
            // MockDataverseRepository to return test session data.
            services.AddScoped(sp =>
            {
                var sessionManager = sp.GetRequiredService<ChatSessionManager>();
                var logger = NullLogger<ChatHistoryManager>.Instance;
                return new ChatHistoryManager(
                    sessionManager,
                    MockDataverseRepository.Object,
                    logger);
            });

            // SprkChatAgentFactory (sealed): construct real instance with test IServiceProvider.
            // The factory calls sp.CreateAsyncScope() then resolves IChatContextProvider and
            // ILogger<SprkChatAgent> from the scope — both are registered in test DI below.
            services.AddSingleton(sp =>
            {
                var chatClient = MockChatClient.Object;
                var logger = NullLogger<SprkChatAgentFactory>.Instance;
                return new SprkChatAgentFactory(chatClient, sp, logger);
            });

            // Register IChatClient mock for RefineText endpoint and SprkChatAgentFactory.
            services.AddSingleton(MockChatClient.Object);

            // Register IChatContextProvider mock so SprkChatAgentFactory.CreateAgentAsync can
            // resolve it from the DI scope during agent creation.
            services.AddScoped(_ => MockContextProvider.Object);

            // Register ILogger<SprkChatAgent> so SprkChatAgentFactory can resolve it from scope.
            services.AddSingleton<ILogger<SprkChatAgent>>(NullLogger<SprkChatAgent>.Instance);

            // Register test JWT authentication scheme (overrides production JWT bearer)
            services.AddAuthentication("Test")
                .AddScheme<TestChatAuthSchemeOptions, TestChatAuthHandler>("Test", _ => { });
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
    // Mock Setup Helpers
    // -------------------------------------------------------------------------

    private void SetupDataverseRepositoryMock()
    {
        var now = DateTimeOffset.UtcNow;

        // CreateSessionAsync — no-op (session is created in memory by ChatSessionManager)
        MockDataverseRepository
            .Setup(r => r.CreateSessionAsync(
                It.IsAny<ChatSession>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // GetSessionAsync — returns a session for TestSessionId (with 2 messages)
        MockDataverseRepository
            .Setup(r => r.GetSessionAsync(
                It.IsAny<string>(),
                TestSessionId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSession(
                SessionId: TestSessionId,
                TenantId: "chat-test-tenant-abc",
                DocumentId: TestDocumentId,
                PlaybookId: TestPlaybookId,
                CreatedAt: now,
                LastActivity: now,
                Messages: [
                    new Sprk.Bff.Api.Models.Ai.Chat.ChatMessage(
                        "msg-001", TestSessionId, ChatMessageRole.User,
                        "Hello", 5, now.AddMinutes(-2), 1),
                    new Sprk.Bff.Api.Models.Ai.Chat.ChatMessage(
                        "msg-002", TestSessionId, ChatMessageRole.Assistant,
                        "Hi there!", 10, now.AddMinutes(-1), 2)
                ]));

        // GetSessionAsync — returns null for any other session ID (triggers 404)
        MockDataverseRepository
            .Setup(r => r.GetSessionAsync(
                It.IsAny<string>(),
                It.Is<string>(s => s != TestSessionId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);

        // ArchiveSessionAsync — no-op (called by DeleteSessionAsync)
        MockDataverseRepository
            .Setup(r => r.ArchiveSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // AddMessageAsync — no-op (messages are persisted by ChatHistoryManager after SSE)
        MockDataverseRepository
            .Setup(r => r.AddMessageAsync(
                It.IsAny<Sprk.Bff.Api.Models.Ai.Chat.ChatMessage>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // UpdateSessionActivityAsync — no-op
        MockDataverseRepository
            .Setup(r => r.UpdateSessionActivityAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // UpdateSessionSummaryAsync — no-op
        MockDataverseRepository
            .Setup(r => r.UpdateSessionSummaryAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupContextProviderMock()
    {
        var testContext = new Sprk.Bff.Api.Models.Ai.Chat.ChatContext(
            SystemPrompt: "You are a helpful legal assistant.",
            DocumentSummary: null,
            AnalysisMetadata: null,
            PlaybookId: TestPlaybookId);

        MockContextProvider
            .Setup(p => p.GetContextAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<ChatHostContext?>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(testContext);
    }

    private void SetupChatClientMock()
    {
        // GetResponseAsync — returns a mock response for RefineText calls
        var chatCompletion = new ChatResponse(
            new List<AiChatMessage>
            {
                new(ChatRole.Assistant, "This is the refined text.")
            });

        MockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatCompletion);

        // GetStreamingResponseAsync — returns a single token update for SendMessage calls
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent("Test response")]
        };

        MockChatClient
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IList<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(MockAsyncEnumerable(update));
    }

    /// <summary>
    /// Wraps a single <see cref="ChatResponseUpdate"/> in an async enumerable.
    /// </summary>
    private static async IAsyncEnumerable<ChatResponseUpdate> MockAsyncEnumerable(
        ChatResponseUpdate item)
    {
        yield return item;
        await Task.CompletedTask;
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
// Test Authentication Handler (same pattern as KnowledgeBaseEndpointsTests)
// =============================================================================

/// <summary>
/// Test JWT authentication handler: reads the Bearer token from the Authorization header,
/// parses it without signature validation, and surfaces the claims as the authenticated user.
/// Mirrors the production handler structure so tenantId (tid claim) flows correctly.
/// </summary>
internal class TestChatAuthHandler
    : Microsoft.AspNetCore.Authentication.AuthenticationHandler<TestChatAuthSchemeOptions>
{
    public TestChatAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<TestChatAuthSchemeOptions> options,
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

internal class TestChatAuthSchemeOptions : Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions
{
}
