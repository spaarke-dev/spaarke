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
using Microsoft.AspNetCore.TestHost;
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

    // FR-D3 (spaarkeai-assistant-enhancements-r2 task 031): a session that genuinely exists
    // (fixture's mock resolves it) but carries zero messages — distinct from a session id the
    // fixture never seeded, which resolves to null and must 404.
    private const string TestEmptySessionId = "test-session-empty-001";

    // A session id the fixture's mock has never seeded — GetSessionAsync resolves to null at
    // every tier (Redis miss, no Cosmos persistence registered in this fixture, Dataverse mock
    // returns null for any id other than TestSessionId/TestEmptySessionId).
    private const string TestMissingSessionId = "test-session-never-existed-999";

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
    [Trait("status", "repaired")]
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
    [Trait("status", "repaired")]
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
    /// Acceptance criterion: POST /sessions/{id}/messages returns SSE stream.
    /// </summary>
    [Fact]
    [Trait("status", "repaired")]
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
        // Assertion updated 2026-06-01 (RB-T028-03/04/05/06 repair): post-Phase-1b kill-switch,
        // the legacy dispatch path (retired by ai-architecture-redesign-r1 tasks 034/035) attempted a
        // real Azure Search call and surfaces RequestFailedException through SendMessageAsync's
        // catch block as a terminal SSE error chunk (data: {"type":"error", ...}). Pre-Phase-1b
        // this code path DI-resolved differently and reached the mock IChatClient producing
        // token+done events. The test now validates the structural SSE pipeline (data: prefix,
        // valid JSON event envelope) rather than the specific event types, which depend on AI
        // service availability — out of scope for unit/integration smoke. Tracked under ADR-030.
        body.Should().Contain("data: ", "SSE stream must use 'data: ' line prefix");
        body.Should().Contain("\"type\":", "SSE events must carry a 'type' field");
    }

    [Fact]
    [Trait("status", "repaired")]
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
    [Trait("status", "repaired")]
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
    [Trait("status", "repaired")]
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
    [Trait("status", "repaired")]
    public async Task GetHistory_Returns401_WhenUnauthenticated()
    {
        // Arrange
        var client = _fixture.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/ai/chat/sessions/{TestSessionId}/history");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// FR-D3 (spaarkeai-assistant-enhancements-r2 task 031) regression: before this fix,
    /// <c>GetHistoryAsync</c> returned 200 with an empty message array for a session that never
    /// existed, silently masking the missing session instead of triggering the client's
    /// stale-session recovery. The endpoint now checks <c>ChatSessionManager.GetSessionAsync</c>
    /// (the same Redis→Cosmos→Dataverse existence check DELETE/PATCH-context/GET-tabs already
    /// use) BEFORE delegating to <c>ChatHistoryManager.GetHistoryAsync</c>, and returns 404
    /// (RFC 7807 ProblemDetails) when the session is genuinely absent.
    /// </summary>
    [Fact]
    [Trait("status", "repaired")]
    public async Task GetHistory_Returns404_WhenSessionDoesNotExist()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);

        // Act — TestMissingSessionId was never seeded by SetupDataverseRepositoryMock, so it
        // resolves to null at every tier (Redis miss, no Cosmos persistence in this fixture,
        // Dataverse mock returns null for any id other than TestSessionId/TestEmptySessionId).
        var response = await client.GetAsync($"/api/ai/chat/sessions/{TestMissingSessionId}/history");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "FR-D3: a genuinely-missing session must 404 so the client's stale-session recovery fires");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json",
            "ADR-019: 404s use RFC 7807 ProblemDetails, matching the sibling GetTabsAsync 404 shape");
    }

    /// <summary>
    /// FR-D3 regression guard: an existing session that simply has no messages yet is NOT the
    /// same as a missing session. <c>ChatSessionManager.GetSessionAsync</c> returns a non-null
    /// <see cref="ChatSession"/> (with an empty <c>Messages</c> list) for a session that exists
    /// at the Dataverse tier, so this must stay 200 — only a null session resolution is 404.
    /// </summary>
    [Fact]
    [Trait("status", "repaired")]
    public async Task GetHistory_Returns200WithEmptyMessages_WhenSessionExistsWithNoMessages()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);

        // Act
        var response = await client.GetAsync($"/api/ai/chat/sessions/{TestEmptySessionId}/history");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "an existing session with zero messages must NOT be 404'd — only a genuinely-missing " +
            "session (null from GetSessionAsync) is 404");

        var content = await response.Content.ReadFromJsonAsync<ChatHistoryResponse>(_jsonOptions);
        content.Should().NotBeNull();
        content!.SessionId.Should().Be(TestEmptySessionId);
        content.Messages.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // PATCH /api/ai/chat/sessions/{id}/context — context switch
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("status", "repaired")]
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
    [Trait("status", "repaired")]
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
    // PATCH /api/ai/chat/sessions/{id} — rename session (FR-D4, task 032)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Acceptance criterion (FR-D4): PATCH renames and persists across reloads. Verifies the
    /// round-trip through the REAL <see cref="ChatSessionManager"/> (real in-memory
    /// <c>ITenantCache</c>-backed Redis hot cache, per this fixture's registration) rather than
    /// just asserting the HTTP status — a 204 with no persistence would be a silent no-op.
    /// </summary>
    [Fact]
    [Trait("status", "repaired")]
    public async Task RenameSession_Returns204AndPersistsTitle_WhenAuthenticated()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        const string newTitle = "Renamed via PATCH";
        var request = new { Title = newTitle };

        // Act
        var response = await client.PatchAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}", request, _jsonOptions);

        // Assert — HTTP contract
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert — persistence round-trip: resolve the REAL ChatSessionManager from the fixture's
        // DI container (same instance the endpoint used) and confirm the rename landed in the
        // hot cache rather than only in the handler's local variable.
        using var scope = _fixture.Services.CreateScope();
        var sessionManager = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
        var persisted = await sessionManager.GetSessionAsync(TestTenantId, TestSessionId, CancellationToken.None);

        persisted.Should().NotBeNull();
        persisted!.Title.Should().Be(newTitle, "the rename must persist across reloads (FR-D4)");
    }

    [Fact]
    [Trait("status", "repaired")]
    public async Task RenameSession_Returns404_WhenSessionDoesNotExist()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new { Title = "New title" };

        // Act — mirrors GetHistory_Returns404_WhenSessionDoesNotExist (same 404-on-missing
        // pattern via ChatSessionManager.GetSessionAsync).
        var response = await client.PatchAsJsonAsync(
            $"/api/ai/chat/sessions/{TestMissingSessionId}", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "renaming a genuinely-missing session must 404, matching the sibling PATCH/GET endpoints");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json",
            "ADR-019: 404s use RFC 7807 ProblemDetails");
    }

    [Fact]
    [Trait("status", "repaired")]
    public async Task RenameSession_Returns400_WhenTitleIsEmpty()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var request = new { Title = "   " };

        // Act
        var response = await client.PatchAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    [Trait("status", "repaired")]
    public async Task RenameSession_Returns401_WhenUnauthenticated()
    {
        // Arrange
        var client = _fixture.CreateClient();
        var request = new { Title = "New title" };

        // Act
        var response = await client.PatchAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------------------------------------------------------------------------
    // FR-D4 (task 032) — title generation at the first substantive exchange.
    //
    // These exercise the REAL ChatHistoryManager.AddMessageAsync directly (resolved from the
    // fixture's DI, same production wiring the SSE SendMessage endpoint uses) rather than through
    // POST /messages: the legacy dispatch path's SSE token loop makes real outbound calls in this
    // fixture (see SendMessage_ReturnsSseStream_WithTokenAndDoneEvents' remarks) that are orthogonal
    // to title-gen and would make an HTTP-level test flaky for a concern that has nothing to do
    // with SSE streaming. Session creation still rides the real POST /sessions endpoint so the
    // starting state (a genuine zero-message session) is realistic, not hand-built.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Acceptance criterion (FR-D4): the fallback never yields a bare timestamp. With
    /// <see cref="ChatEndpointsTestFixture.MockOpenAiClient"/> left unconfigured (Loose mock —
    /// <c>GetCompletionAsync</c> resolves to a null string), the first user message must seed a
    /// non-null, non-timestamp title derived deterministically from the message content.
    /// </summary>
    [Fact]
    [Trait("status", "repaired")]
    public async Task AddMessageAsync_SeedsFallbackTitle_WhenFirstUserMessageAndTitleGenUnavailable()
    {
        // Arrange — a brand-new (zero-message) session via the real create endpoint.
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var createResponse = await client.PostAsJsonAsync(
            "/api/ai/chat/sessions", new { DocumentId = TestDocumentId }, _jsonOptions);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ChatSessionCreatedResponse>(_jsonOptions);

        using var scope = _fixture.Services.CreateScope();
        var sessionManager = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
        var historyManager = scope.ServiceProvider.GetRequiredService<ChatHistoryManager>();
        var session = await sessionManager.GetSessionAsync(TestTenantId, created!.SessionId, CancellationToken.None);
        session.Should().NotBeNull();
        session!.Messages.Should().BeEmpty("a brand-new session must start with zero messages");

        var firstMessage = new Sprk.Bff.Api.Models.Ai.Chat.ChatMessage(
            MessageId: Guid.NewGuid().ToString("N"),
            SessionId: created.SessionId,
            Role: ChatMessageRole.User,
            Content: "What are the termination provisions in this NDA?",
            TokenCount: 0,
            CreatedAt: DateTimeOffset.UtcNow,
            SequenceNumber: 1);

        // Act
        var updated = await historyManager.AddMessageAsync(session, firstMessage, CancellationToken.None);

        // Assert
        updated.Title.Should().NotBeNullOrWhiteSpace();
        updated.Title.Should().Be("What are the termination provisions in this NDA?",
            "unconfigured title-gen must fall back to the deterministic first-message title");
        updated.Title.Should().NotMatchRegex(@"^Conversation ·", "the fallback must never be a bare timestamp");
    }

    /// <summary>
    /// Acceptance criterion (FR-D4): new sessions get 3–6 word descriptive titles when the cheap
    /// grounded completion succeeds. Configures <see cref="ChatEndpointsTestFixture.MockOpenAiClient"/>
    /// to return a model-generated label and verifies it — not the deterministic fallback — is
    /// what gets stored.
    /// </summary>
    [Fact]
    [Trait("status", "repaired")]
    public async Task AddMessageAsync_SeedsGeneratedTitle_WhenFirstUserMessageAndTitleGenSucceeds()
    {
        // Arrange — ChatEndpointsTestFixture is an IClassFixture (ONE instance shared across every
        // test in this class), so a Setup() on the shared MockOpenAiClient leaks into whichever
        // other test runs next unless reset. try/finally guarantees the reset runs even on
        // assertion failure, keeping AddMessageAsync_SeedsFallbackTitle_* (which relies on the
        // mock being unconfigured) isolated regardless of xUnit's (unordered) test execution.
        _fixture.MockOpenAiClient
            .Setup(c => c.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("\"NDA Termination Clause Review.\"");

        try
        {
            var client = _fixture.CreateAuthenticatedClient(TestTenantId);
            var createResponse = await client.PostAsJsonAsync(
                "/api/ai/chat/sessions", new { DocumentId = TestDocumentId }, _jsonOptions);
            createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            var created = await createResponse.Content.ReadFromJsonAsync<ChatSessionCreatedResponse>(_jsonOptions);

            using var scope = _fixture.Services.CreateScope();
            var sessionManager = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
            var historyManager = scope.ServiceProvider.GetRequiredService<ChatHistoryManager>();
            var session = await sessionManager.GetSessionAsync(TestTenantId, created!.SessionId, CancellationToken.None);

            var firstMessage = new Sprk.Bff.Api.Models.Ai.Chat.ChatMessage(
                MessageId: Guid.NewGuid().ToString("N"),
                SessionId: created.SessionId,
                Role: ChatMessageRole.User,
                Content: "What are the termination provisions in this NDA?",
                TokenCount: 0,
                CreatedAt: DateTimeOffset.UtcNow,
                SequenceNumber: 1);

            // Act
            var updated = await historyManager.AddMessageAsync(session!, firstMessage, CancellationToken.None);

            // Assert — quotes and trailing period stripped by ChatHistoryManager.CleanGeneratedTitle.
            updated.Title.Should().Be("NDA Termination Clause Review");
        }
        finally
        {
            _fixture.MockOpenAiClient.Reset();
        }
    }

    /// <summary>
    /// Regression guard: title-gen must only fire once, at the FIRST user message. A second
    /// message on the same session must not overwrite an already-seeded title.
    /// </summary>
    [Fact]
    [Trait("status", "repaired")]
    public async Task AddMessageAsync_DoesNotReseedTitle_ForSecondMessageOnSameSession()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient(TestTenantId);
        var createResponse = await client.PostAsJsonAsync(
            "/api/ai/chat/sessions", new { DocumentId = TestDocumentId }, _jsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<ChatSessionCreatedResponse>(_jsonOptions);

        using var scope = _fixture.Services.CreateScope();
        var sessionManager = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
        var historyManager = scope.ServiceProvider.GetRequiredService<ChatHistoryManager>();
        var session = await sessionManager.GetSessionAsync(TestTenantId, created!.SessionId, CancellationToken.None);

        var firstMessage = new Sprk.Bff.Api.Models.Ai.Chat.ChatMessage(
            Guid.NewGuid().ToString("N"), created.SessionId, ChatMessageRole.User,
            "First question about the NDA", 0, DateTimeOffset.UtcNow, 1);
        var afterFirst = await historyManager.AddMessageAsync(session!, firstMessage, CancellationToken.None);

        var secondMessage = new Sprk.Bff.Api.Models.Ai.Chat.ChatMessage(
            Guid.NewGuid().ToString("N"), created.SessionId, ChatMessageRole.Assistant,
            "Here is the answer.", 0, DateTimeOffset.UtcNow, 2);

        // Act
        var afterSecond = await historyManager.AddMessageAsync(afterFirst, secondMessage, CancellationToken.None);

        // Assert
        afterSecond.Title.Should().Be(afterFirst.Title,
            "title-gen only fires at the first user message; later messages must not reseed it");
    }

    // -------------------------------------------------------------------------
    // DELETE /api/ai/chat/sessions/{id} — delete session
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("status", "repaired")]
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
    [Trait("status", "repaired")]
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

    // FR-D3 (task 031): a session the Dataverse mock resolves to a real (non-null) session with
    // zero messages — must stay distinguishable from TestMissingSessionId's null resolution.
    private const string TestEmptySessionId = "test-session-empty-001";

    private static readonly Guid TestPlaybookId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // IChatDataverseRepository is an interface — fully mockable.
    // Used as constructor argument for both real ChatSessionManager and ChatHistoryManager.
    public Mock<IChatDataverseRepository> MockDataverseRepository { get; } = new(MockBehavior.Loose);

    // IChatContextProvider is an interface — fully mockable.
    // Registered in test DI so SprkChatAgentFactory.CreateAgentAsync can resolve it.
    public Mock<IChatContextProvider> MockContextProvider { get; } = new(MockBehavior.Loose);

    // IChatClient is an interface — fully mockable.
    public Mock<IChatClient> MockChatClient { get; } = new(MockBehavior.Loose);

    // FR-D4 (task 032) — IOpenAiClient is an interface — fully mockable. Wired explicitly into
    // the ChatHistoryManager test registration below (NOT the anonymous DI-resolved mock
    // registered further down for other AI-gated consumers) so individual tests can configure
    // GetCompletionAsync to exercise both the "generated" and "fallback" legs of the FR-D4
    // title-gen chain. Unconfigured (Loose) => GetCompletionAsync returns Task<string?> with a
    // null result, which ChatHistoryManager.CleanGeneratedTitle treats as unusable => falls back
    // to the deterministic first-message title (never a bare timestamp).
    public Mock<Sprk.Bff.Api.Services.Ai.IOpenAiClient> MockOpenAiClient { get; } = new(MockBehavior.Loose);

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
        // email-communication-intelligence-r2 UAT: SessionPersistenceService.ctor requires
        // CosmosPersistence:DatabaseName (throws "not configured" → 500 on every /api/ai/chat/sessions
        // request). Surfaced once the asymmetric-registration host-boot crash was fixed (same PR).
        // Mirrors the 10+ other Api.Ai/Compose/Communication fixtures that set "spaarke-ai-test".
        builder.UseSetting("CosmosPersistence:DatabaseName", "spaarke-ai-test");

        builder.ConfigureTestServices(services =>
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
            services.AddSingleton(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IRagService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IFileIndexingService>(Moq.MockBehavior.Loose).Object);
            services.AddSingleton(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IKnowledgeDeploymentService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IAnalysisOrchestrationService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IAppOnlyAnalysisService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IPlaybookService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.INodeService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IPlaybookOrchestrationService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IPlaybookSharingService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IScopeManagementService>(Moq.MockBehavior.Loose).Object);
            services.AddSingleton(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.Visualization.IVisualizationService>(Moq.MockBehavior.Loose).Object);
            services.AddSingleton(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.IModelSelector>(Moq.MockBehavior.Loose).Object);

            // Semantic Search & Record Search - endpoints are always mapped but services
            // only register when Analysis:Enabled=true && DocumentIntelligence:Enabled=true
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.SemanticSearch.ISemanticSearchService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.RecordSearch.IRecordSearchService>(Moq.MockBehavior.Loose).Object);

            // IEmailTemplateService — used by CommunicationTemplateEndpoints (always mapped, added by
            // the 096a5f754 "feat(copilot)" merge) but only registered when Analysis:Enabled=true.
            // Without this stub the unconditional endpoint's parameter binding fails at startup and
            // takes down the ENTIRE endpoint route table (every test in this fixture 500s). Same
            // stub-the-conditional-service pattern as the block above.
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.Delivery.IEmailTemplateService>(Moq.MockBehavior.Loose).Object);
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.PublicContracts.IEmailDraftAi>(Moq.MockBehavior.Loose).Object);

            // ReferenceIndexingService (sealed concrete) — used by AdminKnowledgeEndpoints.
            // Register its missing dependency stubs so DI can construct it.
            services.AddSingleton(_ => new Moq.Mock<Sprk.Bff.Api.Services.Ai.ITextChunkingService>(Moq.MockBehavior.Loose).Object);
            services.AddSingleton<Sprk.Bff.Api.Services.Ai.ReferenceIndexingService>();

            // IRecordMatchService — used by RecordMatchEndpoints (always mapped).
            services.AddScoped(_ => new Moq.Mock<Sprk.Bff.Api.Services.RecordMatching.IRecordMatchService>(Moq.MockBehavior.Loose).Object);

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
                var cache = sp.GetRequiredService<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>();
                var logger = NullLogger<ChatSessionManager>.Instance;
                return new ChatSessionManager(
                    cache,
                    MockDataverseRepository.Object,
                    logger);
            });

            // ChatHistoryManager (sealed): construct real instance with mocked dependencies.
            // GetHistoryAsync delegates to _sessionManager.GetSessionAsync, which calls our
            // MockDataverseRepository to return test session data. FR-D4 (task 032): also wires
            // MockOpenAiClient so tests can exercise the title-gen "generated" leg by configuring
            // GetCompletionAsync — unconfigured, it degrades to the fallback leg (see field remarks).
            services.AddScoped(sp =>
            {
                var sessionManager = sp.GetRequiredService<ChatSessionManager>();
                var logger = NullLogger<ChatHistoryManager>.Instance;
                return new ChatHistoryManager(
                    sessionManager,
                    MockDataverseRepository.Object,
                    logger,
                    MockOpenAiClient.Object);
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

        // GetSessionAsync — returns a REAL (non-null) session for TestEmptySessionId with an
        // empty Messages list. FR-D3 (task 031): registered AFTER the catch-all above so Moq's
        // most-recently-added-setup-wins matching resolves this id to the empty session rather
        // than the catch-all's null — the regression guard for "existing but empty must stay 200".
        MockDataverseRepository
            .Setup(r => r.GetSessionAsync(
                It.IsAny<string>(),
                TestEmptySessionId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSession(
                SessionId: TestEmptySessionId,
                TenantId: "chat-test-tenant-abc",
                DocumentId: null,
                PlaybookId: TestPlaybookId,
                CreatedAt: now,
                LastActivity: now,
                Messages: Array.Empty<Sprk.Bff.Api.Models.Ai.Chat.ChatMessage>()) { OwnerOid = TestSessionOwner.Oid });

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
                It.IsAny<IReadOnlyList<Sprk.Bff.Api.Models.Ai.Chat.ChatSessionFile>?>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<Sprk.Bff.Api.Models.Ai.Chat.SessionOutput>?>(),
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

internal class TestChatAuthSchemeOptions : Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions
{
}
