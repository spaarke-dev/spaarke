using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Capabilities;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for SprkChatAgentFactory.
/// Verifies that the factory creates agents with correct context from IChatContextProvider.
/// </summary>
public class SprkChatAgentFactoryTests
{
    private static readonly Guid TestPlaybookId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string TestDocumentId = "doc-001";
    private const string TestTenantId = "tenant-abc";
    private const string TestSessionId = "session-xyz";

    [Fact]
    public async Task CreateAgentAsync_ReturnsSprkChatAgent_WithContextFromProvider()
    {
        // Arrange
        const string expectedSystemPrompt = "You are a contract analyst.";

        var expectedContext = new ChatContext(
            SystemPrompt: expectedSystemPrompt,
            DocumentSummary: "This is an NDA.",
            AnalysisMetadata: null,
            PlaybookId: TestPlaybookId);

        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(TestDocumentId, TestTenantId, TestPlaybookId, It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<ChatSessionFile>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedContext);

        var services = BuildServiceProvider(contextProviderMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act
        var agent = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId);

        // Assert — base prompt preserved at the start; SprkChatAgentFactory may
        // append additive directives (e.g. R6 Wave B-G10b compact-formatting
        // directive) so we assert StartWith rather than exact equality.
        agent.Should().NotBeNull();
        agent.Context.SystemPrompt.Should().StartWith(expectedSystemPrompt);
        agent.Context.DocumentSummary.Should().Be("This is an NDA.");
        agent.Context.PlaybookId.Should().Be(TestPlaybookId);
    }

    [Fact]
    public async Task CreateAgentAsync_CallsContextProvider_WithCorrectParameters()
    {
        // Arrange
        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<ChatHostContext?>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<IReadOnlyList<ChatSessionFile>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultContext());

        var services = BuildServiceProvider(contextProviderMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act
        await factory.CreateAgentAsync(TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId);

        // Assert
        contextProviderMock.Verify(
            p => p.GetContextAsync(TestDocumentId, TestTenantId, TestPlaybookId, It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<ChatSessionFile>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAgentAsync_ReturnsNewAgentInstance_OnEachCall()
    {
        // Arrange
        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<ChatSessionFile>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultContext());

        var services = BuildServiceProvider(contextProviderMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act
        var agent1 = await factory.CreateAgentAsync(TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId);
        var agent2 = await factory.CreateAgentAsync(TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId);

        // Assert — each call must return a distinct instance (context switching support)
        agent1.Should().NotBeSameAs(agent2);
    }

    [Fact]
    public async Task CreateAgentAsync_HandlesContextSwitching_ByCreatingNewAgentWithDifferentDocument()
    {
        // Arrange
        const string doc1 = "doc-001";
        const string doc2 = "doc-002";
        const string prompt1 = "Analyze doc 1.";
        const string prompt2 = "Analyze doc 2.";

        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(doc1, It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<ChatSessionFile>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatContext(prompt1, null, null, TestPlaybookId));
        contextProviderMock
            .Setup(p => p.GetContextAsync(doc2, It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<ChatSessionFile>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatContext(prompt2, null, null, TestPlaybookId));

        var services = BuildServiceProvider(contextProviderMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act — simulate context switch by creating two agents for different documents
        var agentDoc1 = await factory.CreateAgentAsync(TestSessionId, doc1, TestPlaybookId, TestTenantId);
        var agentDoc2 = await factory.CreateAgentAsync(TestSessionId, doc2, TestPlaybookId, TestTenantId);

        // Assert — base prompt preserved at the start; additive directives
        // (e.g. R6 Wave B-G10b compact-formatting) may follow.
        agentDoc1.Context.SystemPrompt.Should().StartWith(prompt1);
        agentDoc2.Context.SystemPrompt.Should().StartWith(prompt2);
    }

    // ── AIPU2-061: Per-turn tool injection tests ──────────────────────────────

    /// <summary>
    /// When the CapabilityRouter returns a confident result for a simple greeting-like
    /// message that matches no capability hints, the factory falls back to the full
    /// playbook capability set (backward compatible).
    ///
    /// Verifies: factory returns an agent and does not throw when the router is
    /// configured but routing produces an uncertain result for low-complexity intent.
    /// </summary>
    [Fact]
    public async Task CreateAgentAsync_WithRouter_LowComplexityIntent_UsesFullCapabilitySet()
    {
        // Arrange
        // Router returns "uncertain" for a simple hello message — no capability matched.
        var routerMock = new Mock<ICapabilityRouter>();
        routerMock
            .Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(CapabilityRoutingResult.Uncertain(0.0, layer: 1, latencyMs: 0));

        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<ChatSessionFile>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultContext());

        var services = BuildServiceProvider(contextProviderMock.Object, routerMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act — low-complexity greeting-style message
        var agent = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId,
            latestUserMessage: "Hello");

        // Assert — agent created successfully, router was called
        agent.Should().NotBeNull();
        routerMock.Verify(
            r => r.RouteAsync("Hello", It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()),
            Times.Once);
    }

    /// <summary>
    /// When the CapabilityRouter returns a confident result with a specific capability
    /// selected for a document-analysis-style message, the factory passes the routing
    /// result to ResolveTools. Verifies the agent is returned and the router was called
    /// with the user message text.
    /// </summary>
    [Fact]
    public async Task CreateAgentAsync_WithRouter_DocumentAnalysisIntent_CallsRouterWithUserMessage()
    {
        // Arrange
        const string documentAnalysisMessage = "analyze this contract for risk clauses";

        var routerMock = new Mock<ICapabilityRouter>();
        routerMock
            .Setup(r => r.RouteAsync(documentAnalysisMessage, It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(CapabilityRoutingResult.Confident(
                selectedCapabilities: ["analyze"],
                confidence: 0.92,
                layer: 1,
                latencyMs: 3));

        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<ChatSessionFile>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultContext());

        var services = BuildServiceProvider(contextProviderMock.Object, routerMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act
        var agent = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId,
            latestUserMessage: documentAnalysisMessage);

        // Assert — agent created successfully, router was called with the exact user message
        agent.Should().NotBeNull();
        routerMock.Verify(
            r => r.RouteAsync(documentAnalysisMessage, It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()),
            Times.Once);
    }

    /// <summary>
    /// When no user message is provided (latestUserMessage is null), the CapabilityRouter
    /// is NOT called — the factory falls through to the existing full-capability-set path.
    /// </summary>
    [Fact]
    public async Task CreateAgentAsync_WithRouter_NullUserMessage_RouterNotCalled()
    {
        // Arrange
        var routerMock = new Mock<ICapabilityRouter>();

        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<ChatSessionFile>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultContext());

        var services = BuildServiceProvider(contextProviderMock.Object, routerMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act — no user message = initial session creation
        var agent = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId,
            latestUserMessage: null);

        // Assert — router never called for null message
        agent.Should().NotBeNull();
        routerMock.Verify(
            r => r.RouteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()),
            Times.Never);
    }

    /// <summary>
    /// When the tool set changes between turns (previous turn had different tools than current),
    /// capability_change SSE events are emitted for added and removed tools.
    ///
    /// Verifies the FR-801 capability_change contract: the factory emits events so the
    /// client can update UI affordances when the active capability profile changes.
    /// </summary>
    [Fact]
    public async Task CreateAgentAsync_EmitsCapabilityChange_WhenToolSetDiffers()
    {
        // Arrange
        var routerMock = new Mock<ICapabilityRouter>();
        // Router returns a confident result — routing WILL happen but manifest is empty,
        // so tool filtering leaves full set in place. The capability_change event is
        // triggered by comparing current tools against previousTurnToolNames.
        routerMock
            .Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(CapabilityRoutingResult.Uncertain(0.0, layer: 1, latencyMs: 0));

        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<ChatSessionFile>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultContext());

        // Previous turn had a tool called "OldTool" that no longer appears in the current set.
        // The current agent will have no tools (no DI services = no tools resolved).
        // So the factory should emit "unavailable" for "OldTool".
        var capturedEvents = new List<ChatSseEvent>();
        Func<ChatSseEvent, CancellationToken, Task> sseWriter = (evt, _) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        var services = BuildServiceProvider(contextProviderMock.Object, routerMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act — previous turn had "OldTool", current turn will have no tools
        await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId,
            sseWriter: sseWriter,
            latestUserMessage: "Hello",
            previousTurnToolNames: ["OldTool"]);

        // Assert — at least one capability_change event was emitted for "OldTool"
        capturedEvents.Should().Contain(e =>
            e.Type == "capability_change",
            "factory must emit capability_change when tool set changes between turns");

        var changeEvent = capturedEvents.First(e => e.Type == "capability_change");
        changeEvent.Data.Should().NotBeNull();
    }

    /// <summary>
    /// When the router is not registered in DI (null ICapabilityRouter), the factory
    /// behaves exactly as before AIPU2-061 — no routing call, full tool set resolved.
    /// </summary>
    [Fact]
    public async Task CreateAgentAsync_WithoutRouter_FallsBackToFullCapabilitySet()
    {
        // Arrange — no router registered (null capabilityRouter)
        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<ChatSessionFile>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultContext());

        // Use the original service provider (no router registration)
        var services = BuildServiceProvider(contextProviderMock.Object, capabilityRouter: null);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act — any user message; router is absent so no routing occurs
        var agent = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId,
            latestUserMessage: "summarize this document");

        // Assert — agent created normally; no exception thrown
        agent.Should().NotBeNull();
    }

    // ── R5 task 015 / R5 task 024 — REMOVED in R6 Wave 10 / task 023 (Pillar 3 cleanup) ──
    // The InvokeSummarizePlaybookTool + InvokeInsightsQueryTool bridges were deleted in
    // favor of the generic InvokePlaybookHandler (R6 Pillar 3 / task 021). The factory no
    // longer registers `invoke_summarize_playbook` or `insights.query` AIFunctions; the
    // generic `invoke_playbook` tool is surfaced via the data-driven block (FR-11) from the
    // SYS-Invoke Playbook Dataverse seed row. Capability gating moves from C# constants
    // (PlaybookCapabilities.Summarize / .InsightsQuery) to the playbook-visibility lookup
    // inside InvokePlaybookHandler.IsTenantVisibleAsync (per task 021). Test coverage for
    // the generic dispatcher lives in InvokePlaybookHandlerTests; integration coverage for
    // the data-driven block lives in SprkChatAgentFactoryDataDrivenToolsTests.
    //
    // Removed tests:
    //   - CreateAgentAsync_WithSummarizeCapability_RegistersInvokeSummarizePlaybookTool
    //   - CreateAgentAsync_WithInsightsQueryCapability_RegistersInsightsQueryTool
    //   - BuildServiceProviderWithInsightsQueryTool (helper)
    //   - BuildServiceProviderWithSummarizeOrchestrator (helper)
    // Recoverable via `git show HEAD~1:tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryTests.cs`.

    // ── R5 task 033 — Session Files manifest suffix on system prompt ──────────

    /// <summary>
    /// When <see cref="ChatContext.UploadedFiles"/> is non-empty, the factory MUST append a
    /// compact "Session Files" manifest suffix to <see cref="ChatContext.SystemPrompt"/>
    /// so the LLM's tool-call reasoning sees the files. Verifies the binding contract for
    /// the Summarize convergence path (FR-01 + FR-08).
    /// </summary>
    [Fact]
    public async Task CreateAgentAsync_AppendsSessionFilesNoteToSystemPrompt_WhenUploadedFilesPresent()
    {
        // Arrange
        const string basePrompt = "You are Spaarke AI.";
        var manifest = new List<ChatSessionFile>
        {
            new("file-001", "contract.pdf", "application/pdf", 1234, "idx-001", DateTimeOffset.UtcNow),
            new("file-002", "schedule.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 5678, "idx-002", DateTimeOffset.UtcNow)
        };

        var contextWithFiles = new ChatContext(
            SystemPrompt: basePrompt,
            DocumentSummary: null,
            AnalysisMetadata: null,
            PlaybookId: TestPlaybookId,
            KnowledgeScope: null,
            UploadedFiles: manifest);

        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<IReadOnlyList<ChatSessionFile>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(contextWithFiles);

        var services = BuildServiceProvider(contextProviderMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act
        var agent = await factory.CreateAgentAsync(
            TestSessionId,
            TestDocumentId,
            TestPlaybookId,
            TestTenantId,
            uploadedFiles: manifest);

        // Assert — suffix appended; tool-name binding present; both file names + IDs visible.
        agent.Context.SystemPrompt.Should().StartWith(basePrompt,
            because: "the original system prompt must be preserved (suffix is additive)");
        agent.Context.SystemPrompt.Should().Contain("Session Files:",
            because: "the manifest suffix is keyed by 'Session Files:' so the LLM can recognize it");
        agent.Context.SystemPrompt.Should().Contain("contract.pdf");
        agent.Context.SystemPrompt.Should().Contain("schedule.docx");
        agent.Context.SystemPrompt.Should().Contain("file-001");
        agent.Context.SystemPrompt.Should().Contain("file-002");
        agent.Context.SystemPrompt.Should().Contain("invoke_playbook",
            because: "post-task-023 the suffix MUST name the generic invoke_playbook tool (the chat-summarize playbook ID is resolved at LLM call time)");
        agent.Context.SystemPrompt.Should().Contain("2 uploaded file",
            because: "the suffix announces the file count so the LLM can reason about cardinality");
    }

    /// <summary>
    /// When <see cref="ChatContext.UploadedFiles"/> is null or empty, the factory MUST NOT
    /// append any manifest suffix — backward compatible with pre-R5 sessions.
    /// </summary>
    [Fact]
    public async Task CreateAgentAsync_DoesNotAppendSessionFilesNote_WhenUploadedFilesEmpty()
    {
        // Arrange
        const string basePrompt = "You are Spaarke AI.";

        var contextWithoutFiles = new ChatContext(
            SystemPrompt: basePrompt,
            DocumentSummary: null,
            AnalysisMetadata: null,
            PlaybookId: TestPlaybookId,
            KnowledgeScope: null,
            UploadedFiles: null);

        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<IReadOnlyList<ChatSessionFile>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(contextWithoutFiles);

        var services = BuildServiceProvider(contextProviderMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act
        var agent = await factory.CreateAgentAsync(
            TestSessionId,
            TestDocumentId,
            TestPlaybookId,
            TestTenantId);

        // Assert — base prompt preserved at the start; no Session Files suffix.
        // (R6 Wave B-G10b appends a compact-formatting directive regardless of
        // file presence — the backward-compat contract this test owns is
        // specifically about the Session Files manifest, asserted via the
        // NotContain checks below.)
        agent.Context.SystemPrompt.Should().StartWith(basePrompt,
            because: "the original base prompt must be preserved at the start (any additive directives follow it)");
        agent.Context.SystemPrompt.Should().NotContain("Session Files:");
        // Post-task-023: the legacy `invoke_summarize_playbook` tool name was deleted; the
        // generic `invoke_playbook` is now the canonical name. With no uploaded files, the
        // manifest suffix is absent — so neither name should appear.
        agent.Context.SystemPrompt.Should().NotContain("invoke_playbook",
            because: "the tool-name binding only appears via the manifest suffix; with no uploaded files there is no suffix");
    }

    /// <summary>
    /// ADR-015 + R5 task 033 invariant: the manifest suffix MUST NOT include any extracted
    /// text content, chunk text, MIME, or size info — only fileId + fileName + count.
    /// Pinning this as a regression guard so future refactors can't accidentally leak.
    /// </summary>
    [Fact]
    public async Task CreateAgentAsync_SystemPromptSuffixDoesNotIncludeExtractedTextContent()
    {
        // Arrange — a sentinel value that a buggy implementation might leak.
        const string sentinelContentType = "application/x-leak-sentinel";
        const long sentinelSize = 999_999_999L;

        var manifest = new List<ChatSessionFile>
        {
            new(
                FileId: "file-leak-001",
                FileName: "leak.pdf",
                ContentType: sentinelContentType,
                SizeBytes: sentinelSize,
                SearchDocumentIdsCsv: "idx-leak-sentinel-chunk-csv",
                UploadedAt: DateTimeOffset.UtcNow)
        };

        var contextWithFile = new ChatContext(
            SystemPrompt: "Base.",
            DocumentSummary: null,
            AnalysisMetadata: null,
            PlaybookId: TestPlaybookId,
            KnowledgeScope: null,
            UploadedFiles: manifest);

        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<IReadOnlyList<ChatSessionFile>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(contextWithFile);

        var services = BuildServiceProvider(contextProviderMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act
        var agent = await factory.CreateAgentAsync(
            TestSessionId,
            TestDocumentId,
            TestPlaybookId,
            TestTenantId,
            uploadedFiles: manifest);

        // Assert — fileId + fileName are present (manifest); content-type, size, and
        // search-document IDs MUST NOT be in the prompt (ADR-015 no-leakage).
        agent.Context.SystemPrompt.Should().Contain("file-leak-001",
            because: "fileId is part of the canonical manifest");
        agent.Context.SystemPrompt.Should().Contain("leak.pdf",
            because: "fileName is part of the canonical manifest");

        agent.Context.SystemPrompt.Should().NotContain(sentinelContentType,
            because: "MIME content type MUST NOT leak into the system prompt (ADR-015)");
        agent.Context.SystemPrompt.Should().NotContain(sentinelSize.ToString(),
            because: "raw byte size MUST NOT leak into the system prompt (ADR-015)");
        agent.Context.SystemPrompt.Should().NotContain("idx-leak-sentinel-chunk-csv",
            because: "AI Search document/chunk IDs MUST NOT leak into the system prompt (ADR-015)");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static CapabilityManifestEntry MakeManifestEntry(
        string name,
        string[] toolNames,
        string[] keywordHints) =>
        new CapabilityManifestEntry(
            CapabilityName: name,
            Description: $"Description for {name}",
            KeywordHints: keywordHints,
            PlaybookId: null,
            ToolNames: toolNames,
            IsEnabled: true,
            TenantRestrictions: Array.Empty<string>());

    #region Private helpers

    private static ServiceProvider BuildServiceProvider(
        IChatContextProvider contextProvider,
        ICapabilityRouter? capabilityRouter = null)
    {
        var services = new ServiceCollection();

        // Register IChatClient mock (standard chat client)
        var chatClientMock = new Mock<IChatClient>();
        services.AddSingleton(chatClientMock.Object);

        // Register raw IChatClient as keyed service "raw" (task 071 — compound intent detection)
        var rawChatClientMock = new Mock<IChatClient>();
        services.AddKeyedSingleton<IChatClient>("raw", rawChatClientMock.Object);

        // Register IChatContextProvider (scoped — factory will resolve from scope)
        services.AddScoped(_ => contextProvider);

        // Register loggers
        services.AddLogging();

        // Register factory (singleton — matches ADR-010 constraint)
        // AIPU2-061: inject the router (may be null for backward-compat tests).
        if (capabilityRouter is not null)
        {
            services.AddSingleton<SprkChatAgentFactory>(sp =>
                new SprkChatAgentFactory(
                    sp.GetRequiredService<IChatClient>(),
                    sp.GetRequiredKeyedService<IChatClient>("raw"),
                    sp,
                    sp.GetRequiredService<ILogger<SprkChatAgentFactory>>(),
                    capabilityRouter));
        }
        else
        {
            services.AddSingleton<SprkChatAgentFactory>();
        }

        return services.BuildServiceProvider();
    }

    private static ChatContext CreateDefaultContext()
        => new ChatContext(
            SystemPrompt: "Default system prompt.",
            DocumentSummary: null,
            AnalysisMetadata: null,
            PlaybookId: TestPlaybookId);

    #endregion
}
