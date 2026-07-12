using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// DEF-11 (spaarkeai-compose-r2) — document-session routing for TEXT/agent-path capability
/// dispatch. <see cref="BindingCapabilityTool.InvokeAsync"/> normally dispatches into the CHAT
/// session that hosts the tool-call loop; a <c>compose</c>-disposition capability's ledger output
/// must instead land in the SEPARATE, coordinated Compose "document session" the client registers
/// via <c>ChatSession.ActiveDocument.DocumentSessionId</c> — mirroring the DEF-09 Click-path
/// precedent ("a Compose edit belongs to the document session", two sessions deliberately kept
/// separate). These tests exercise the REAL <see cref="ChatSessionManager"/> + a recording
/// <see cref="SessionDispatchOrchestrator"/> subclass (the same module-boundary pattern used by
/// <c>LoopElicitationTests</c>) — no <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI-registration
/// assertions (ADR-038).
/// </summary>
public class ComposeDocumentSessionRoutingTests
{
    private const string TenantId = "tenant-compose-routing-tests";

    private const string ComposeReviseSchema =
        """{"type":"object","properties":{"revisionIntent":{"type":"string","enum":["align-clauses","flag-risks","improve-clarity","custom"]}},"required":["revisionIntent"]}""";

    private readonly InMemoryTenantCache _cache = new();
    private readonly ChatSessionManager _sessionManager;

    public ComposeDocumentSessionRoutingTests()
    {
        _sessionManager = new ChatSessionManager(
            _cache,
            new Mock<IChatDataverseRepository>().Object,
            new Mock<ILogger<ChatSessionManager>>().Object);
    }

    [Fact]
    public async Task InvokeAsync_ComposeDispositionBindingWithRegisteredDocumentSession_DispatchesToDocumentSession()
    {
        var chatSessionId = Guid.NewGuid().ToString("N");
        var documentSessionId = Guid.NewGuid().ToString("N");
        await SeedSessionAsync(chatSessionId, new ActiveDocumentIdentity(
            Source: ActiveDocumentIdentity.SourceComposeDirect,
            SessionFileId: "file-1",
            DocumentSessionId: documentSessionId));

        var dispatcher = new RecordingDispatchOrchestrator();
        var tool = CreateTool(chatSessionId, dispatcher, BindingDisposition.Compose);

        await tool.InvokeAsync(new AIFunctionArguments { ["revisionIntent"] = "improve-clarity" });

        dispatcher.DispatchCount.Should().Be(1);
        dispatcher.LastRequest!.SessionId.Should().Be(documentSessionId,
            "DEF-11: a compose-disposition capability's ledger write must land in the DOCUMENT " +
            "session (DEF-09 precedent) — the Compose editor materializes redlines/comments by " +
            "reading THAT session, not the chat session that hosted the tool-call loop");
        dispatcher.LastRequest.SessionId.Should().NotBe(chatSessionId,
            "the document session and the chat session are deliberately kept separate (DEF-09) — " +
            "the compose dispatch must NOT land in the chat session when a document session is registered");
    }

    [Fact]
    public async Task InvokeAsync_ComposeDispositionBindingWithNoDocumentSessionRegistered_FailsSoftToChatSession()
    {
        var chatSessionId = Guid.NewGuid().ToString("N");
        await SeedSessionAsync(chatSessionId, activeDocument: null);

        var dispatcher = new RecordingDispatchOrchestrator();
        var tool = CreateTool(chatSessionId, dispatcher, BindingDisposition.Compose);

        await tool.InvokeAsync(new AIFunctionArguments { ["revisionIntent"] = "improve-clarity" });

        dispatcher.DispatchCount.Should().Be(1);
        dispatcher.LastRequest!.SessionId.Should().Be(chatSessionId,
            "fail-soft: no ActiveDocument (or no DocumentSessionId on it) leaves dispatch targeting " +
            "the chat session — the pre-DEF-11 behavior for every existing compose capability");
    }

    [Fact]
    public async Task InvokeAsync_ComposeDispositionBindingWithActiveDocumentButNoDocumentSessionId_FailsSoftToChatSession()
    {
        var chatSessionId = Guid.NewGuid().ToString("N");
        // An ActiveDocument IS registered (task 113 pointer) but its DocumentSessionId is null —
        // the pre-DEF-11 shape every existing active-document registration produces.
        await SeedSessionAsync(chatSessionId, new ActiveDocumentIdentity(
            Source: ActiveDocumentIdentity.SourceStored,
            SprkDocumentId: Guid.NewGuid().ToString("D")));

        var dispatcher = new RecordingDispatchOrchestrator();
        var tool = CreateTool(chatSessionId, dispatcher, BindingDisposition.Compose);

        await tool.InvokeAsync(new AIFunctionArguments { ["revisionIntent"] = "flag-risks" });

        dispatcher.LastRequest!.SessionId.Should().Be(chatSessionId,
            "a registered ActiveDocument with no DocumentSessionId (additive field, older/partial " +
            "registrations) must not crash or misroute — dispatch falls back to the chat session");
    }

    [Fact]
    public async Task InvokeAsync_NonComposeDispositionBinding_AlwaysDispatchesToChatSession_EvenWithDocumentSessionRegistered()
    {
        var chatSessionId = Guid.NewGuid().ToString("N");
        var documentSessionId = Guid.NewGuid().ToString("N");
        await SeedSessionAsync(chatSessionId, new ActiveDocumentIdentity(
            Source: ActiveDocumentIdentity.SourceComposeDirect,
            SessionFileId: "file-1",
            DocumentSessionId: documentSessionId));

        var dispatcher = new RecordingDispatchOrchestrator();
        var tool = CreateTool(chatSessionId, dispatcher, BindingDisposition.Informational);

        await tool.InvokeAsync(new AIFunctionArguments());

        dispatcher.DispatchCount.Should().Be(1);
        dispatcher.LastRequest!.SessionId.Should().Be(chatSessionId,
            "the document-session routing rule is scoped to compose-disposition bindings only — " +
            "an informational (or any non-compose) capability always dispatches in the chat session " +
            "that invoked it, even when a document session happens to be registered");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task SeedSessionAsync(string sessionId, ActiveDocumentIdentity? activeDocument)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new ChatSession(
            SessionId: sessionId,
            TenantId: TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: now,
            LastActivity: now,
            Messages: new List<Sprk.Bff.Api.Models.Ai.Chat.ChatMessage>())
        {
            ActiveDocument = activeDocument,
        };
        await _sessionManager.UpdateSessionCacheAsync(session);
    }

    private BindingCapabilityTool CreateTool(
        string sessionId,
        SessionDispatchOrchestrator dispatcher,
        BindingDisposition disposition)
    {
        var binding = new Binding
        {
            BindingId = Guid.NewGuid(),
            ConsumerType = disposition == BindingDisposition.Compose ? "compose-revise-document" : "compose-explain-clause",
            ActionId = Guid.NewGuid(),
            ToolDescription = "Test capability.",
            InputSchemaJson = disposition == BindingDisposition.Compose
                ? ComposeReviseSchema
                : """{"type":"object","properties":{"fileIds":{"type":"array","items":{"type":"string"}}}}""",
            Disposition = disposition,
            CaptureMode = BindingCaptureMode.LoopElicitation,
        };

        var services = new ServiceCollection();
        services.AddScoped(_ => dispatcher);
        services.AddScoped(_ => _sessionManager);

        return new BindingCapabilityTool(
            binding,
            services.BuildServiceProvider(),
            TenantId,
            sessionId,
            Mock.Of<ILogger>());
    }

    /// <summary>
    /// Recording subclass over the virtual dispatch seam (protected logger-only ctor — the same
    /// ADR-038 module-boundary pattern <c>LoopElicitationTests</c> uses) that captures the FULL
    /// <see cref="SessionDispatchRequest"/>, so the test can assert which session id the routing
    /// decision actually targeted.
    /// </summary>
    private sealed class RecordingDispatchOrchestrator : SessionDispatchOrchestrator
    {
        public RecordingDispatchOrchestrator()
            : base(Mock.Of<ILogger<SessionDispatchOrchestrator>>())
        {
        }

        public int DispatchCount { get; private set; }

        public SessionDispatchRequest? LastRequest { get; private set; }

        public override async IAsyncEnumerable<AnalysisChunk> DispatchAsync(
            SessionDispatchRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            DispatchCount++;
            LastRequest = request;
            await Task.Yield();
            yield return AnalysisChunk.Completed("test output");
        }
    }
}
