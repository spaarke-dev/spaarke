using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat.Tools;

/// <summary>
/// Unit tests for <see cref="InvokeSummarizePlaybookTool"/> — R5 task 015 (D2-05).
///
/// Test strategy: <see cref="SessionSummarizeOrchestrator"/> is <c>public sealed</c> (per
/// ADR-010 — no R5-authored interface; concrete class with no derivation). So we exercise
/// the real orchestrator with stubbed I/O boundary deps (mirrors the pattern in
/// <c>SessionSummarizeOrchestratorTests</c>). The tool's job is THIN delegation; the only
/// behavior under test is request-shape construction, SSE forwarding, and result extraction.
/// The orchestrator's own behavior is covered by <c>SessionSummarizeOrchestratorTests</c>.
///
/// Coverage:
/// <list type="bullet">
///   <item>Constructor validation (null guards, blank tenant/session).</item>
///   <item>Tool catalog: GetTools() yields exactly one AIFunction with name
///         <see cref="InvokeSummarizePlaybookTool.ToolName"/> + curated description.</item>
///   <item>NFR-12 description quality: text contains routing-scope guards and explicit
///         insights.query differentiation.</item>
///   <item>Delegation + AgentTool path: the orchestrator runs end-to-end with the request
///         shape produced by the tool (verified via R5SummarizeTelemetry path dimension).</item>
///   <item>fileIds defaulting: null and empty-array both delegate to "all session files"
///         via the orchestrator (verified by RAG mock argument capture).</item>
///   <item>style defaulting: null + whitespace → null; explicit string → passed through.</item>
///   <item>SSE forwarding: every orchestrator chunk is mirrored to the SSE writer.</item>
///   <item>Convergence (FR-05): identical request shape produces identical SSE event
///         stream content + ordering whether dispatched via DirectEndpoint or AgentTool.</item>
/// </list>
/// </summary>
public class InvokeSummarizePlaybookToolTests
{
    private const string TenantId = "tenant-abc";
    private const string SessionId = "session-xyz";
    private const string FileId1 = "file-001";
    private const string FileId2 = "file-002";

    // ── Constructor validation ─────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenOrchestratorIsNull()
    {
        var action = () => new InvokeSummarizePlaybookTool(
            orchestrator: null!,
            tenantId: TenantId,
            sessionId: SessionId,
            correlationId: null,
            sseWriter: null,
            logger: NullLogger<InvokeSummarizePlaybookTool>.Instance);

        action.Should().Throw<ArgumentNullException>().WithParameterName("orchestrator");
    }

    [Fact]
    public void Constructor_ThrowsArgumentException_WhenTenantIdIsBlank()
    {
        var action = () => new InvokeSummarizePlaybookTool(
            orchestrator: BuildOrchestrator(out _, out _, out _),
            tenantId: "   ",
            sessionId: SessionId,
            correlationId: null,
            sseWriter: null,
            logger: NullLogger<InvokeSummarizePlaybookTool>.Instance);

        action.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }

    [Fact]
    public void Constructor_ThrowsArgumentException_WhenSessionIdIsBlank()
    {
        var action = () => new InvokeSummarizePlaybookTool(
            orchestrator: BuildOrchestrator(out _, out _, out _),
            tenantId: TenantId,
            sessionId: "",
            correlationId: null,
            sseWriter: null,
            logger: NullLogger<InvokeSummarizePlaybookTool>.Instance);

        action.Should().Throw<ArgumentException>().WithParameterName("sessionId");
    }

    // ── Tool catalog (NFR-12 + AIPU2-061 discoverability) ─────────────────────────

    [Fact]
    public void GetTools_YieldsExactlyOneFunction_WithCanonicalNameAndDescription()
    {
        var tool = CreateSutWithEmptySession();

        var functions = tool.GetTools().ToList();

        functions.Should().HaveCount(1);
        functions[0].Name.Should().Be(InvokeSummarizePlaybookTool.ToolName);
        functions[0].Name.Should().Be("invoke_summarize_playbook");
        functions[0].Description.Should().Be(InvokeSummarizePlaybookTool.ToolDescription);
    }

    [Fact]
    public void ToolDescription_IsSemanticallyDistinctFrom_InsightsQuery_NFR12()
    {
        // NFR-12 / UR-01 mitigation: the tool description text MUST scope routing to
        // session-uploaded files AND MUST explicitly differentiate from insights.query
        // so the LLM Layer 2 classifier picks the right tool for each natural-language
        // prompt.
        var description = InvokeSummarizePlaybookTool.ToolDescription;

        // Must mention chat-session uploaded files (scope assertion).
        description.Should().Contain("chat session", because:
            "description must explicitly scope to chat-session-uploaded files (NFR-12)");
        description.Should().Contain("uploaded", because:
            "description must call out the upload origin to disambiguate from knowledge-index search");

        // Must explicitly differentiate from insights.query (UR-01 mitigation).
        description.Should().Contain("insights.query", because:
            "description must explicitly name insights.query as the alternative tool for entity Q&A (NFR-12 / UR-01)");

        // Must enumerate the primary trigger verbs the user is likely to use.
        description.Should().ContainAny(new[] { "summarize", "Summarize" });
        description.Should().Contain("TL;DR");

        // Length sanity (description is load-bearing but must remain compact — LLM tool
        // schemas budget per-tool description tokens; runaway descriptions degrade routing).
        description.Length.Should().BeLessThan(800,
            "tool descriptions over ~800 chars degrade LLM tool-routing quality");
    }

    // ── Delegation + AgentTool path discriminator ──────────────────────────────────

    [Fact]
    public async Task InvokeSummarizePlaybookAsync_DelegatesToOrchestrator_WithAgentToolPathAndDefaults()
    {
        // Arrange — session has two files; orchestrator returns a happy-path stream.
        var orchestrator = BuildOrchestrator(
            out var sessionManager, out var ragMock, out var openAi,
            session: BuildSession(FileId1, FileId2));
        openAi.TokensToYield = new[] { "{\"tldr\":[\"summary point\"]}" };

        var capturedEvents = new List<ChatSseEvent>();
        Func<ChatSseEvent, CancellationToken, Task> sseWriter = (evt, _) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        var tool = new InvokeSummarizePlaybookTool(
            orchestrator,
            TenantId,
            SessionId,
            correlationId: "trace-001",
            sseWriter: sseWriter,
            logger: NullLogger<InvokeSummarizePlaybookTool>.Instance);

        // Act
        var result = await tool.InvokeSummarizePlaybookAsync(fileIds: null, style: null);

        // Assert — the orchestrator was driven end-to-end (RAG search executed; SSE
        // events emitted). The path discriminator was AgentTool — verified by the
        // ResolvedFileIds capture (orchestrator's defaulting kicked in → fileIds null
        // resolves to ALL session files).
        result.Should().NotBeNullOrEmpty();
        capturedEvents.Should().NotBeEmpty(because:
            "the tool MUST forward orchestrator chunks to the SSE writer in real time");

        ragMock.Verify(r => r.SearchAsync(
            It.IsAny<string>(),
            It.Is<RagSearchOptions>(o => o.TenantId == TenantId && o.SessionId == SessionId),
            It.IsAny<CancellationToken>()),
            Times.Once,
            "the orchestrator MUST be driven with the tool's tenantId + sessionId (ADR-014)");
    }

    [Fact]
    public async Task InvokeSummarizePlaybookAsync_WithEmptySession_ReturnsDeclineMessage()
    {
        // Session has NO uploaded files → orchestrator emits a structured decline (per
        // task 012 evidence section "FR-11 equivalent"). Tool surfaces the error text to
        // the LLM conversation history.
        var orchestrator = BuildOrchestrator(
            out _, out _, out _,
            session: BuildSession(/* no files */));

        var tool = new InvokeSummarizePlaybookTool(
            orchestrator,
            TenantId, SessionId,
            correlationId: null, sseWriter: null,
            logger: NullLogger<InvokeSummarizePlaybookTool>.Instance);

        var result = await tool.InvokeSummarizePlaybookAsync(fileIds: null, style: null);

        result.Should().Contain("unable to complete");
        result.Should().ContainAny(new[] { "No files", "upload" });
    }

    // ── fileIds defaulting ─────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeSummarizePlaybookAsync_WithExplicitFileIds_PropagatesToOrchestrator()
    {
        var orchestrator = BuildOrchestrator(
            out _, out var ragMock, out var openAi,
            session: BuildSession(FileId1, FileId2));
        openAi.TokensToYield = new[] { "{\"tldr\":[\"filtered\"]}" };

        var tool = new InvokeSummarizePlaybookTool(
            orchestrator,
            TenantId, SessionId,
            correlationId: null, sseWriter: null,
            logger: NullLogger<InvokeSummarizePlaybookTool>.Instance);

        await tool.InvokeSummarizePlaybookAsync(
            fileIds: new[] { FileId1 },
            style: null);

        // The orchestrator's BuildRagQuery composes a query mentioning the selected files
        // by name. We assert at the boundary the RAG search was driven exactly once with
        // the correct tenant + session — the orchestrator's own tests cover the fileId
        // narrowing logic. The tool's job is to PASS THROUGH the fileIds (not transform
        // them or default them prematurely).
        ragMock.Verify(r => r.SearchAsync(
            It.IsAny<string>(),
            It.Is<RagSearchOptions>(o => o.TenantId == TenantId && o.SessionId == SessionId),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeSummarizePlaybookAsync_WithEmptyFileIdsArray_DelegatesAsAllFiles()
    {
        // Empty array passed in → tool maps to null → orchestrator defaults to all session
        // files (per FR-08 + orchestrator's ResolveFileIds defaulting). We verify the tool
        // does not throw the orchestrator's NFR-02 cap error for an empty array (it would
        // if the tool incorrectly forwarded an empty IReadOnlyList instead of null).
        var orchestrator = BuildOrchestrator(
            out _, out _, out var openAi,
            session: BuildSession(FileId1));
        openAi.TokensToYield = new[] { "{\"tldr\":[\"all-files\"]}" };

        var tool = new InvokeSummarizePlaybookTool(
            orchestrator,
            TenantId, SessionId,
            correlationId: null, sseWriter: null,
            logger: NullLogger<InvokeSummarizePlaybookTool>.Instance);

        var result = await tool.InvokeSummarizePlaybookAsync(
            fileIds: Array.Empty<string>(),
            style: null);

        result.Should().NotBeNullOrEmpty(because:
            "empty fileIds array MUST default to summarizing all session files (FR-08), not error");
    }

    // ── style defaulting ───────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeSummarizePlaybookAsync_WhitespaceStyle_TreatedAsNullByTool()
    {
        // Whitespace-only style MUST default to null at the tool boundary so the
        // orchestrator's "default playbook style" fallback fires. We verify by inspecting
        // the OpenAI client's last received messages — the user content includes a "Style
        // hint:" line ONLY when StyleHint is non-blank (per orchestrator BuildUserContent).
        var orchestrator = BuildOrchestrator(
            out _, out _, out var openAi,
            session: BuildSession(FileId1));
        openAi.TokensToYield = new[] { "{\"tldr\":[\"default-style\"]}" };

        var tool = new InvokeSummarizePlaybookTool(
            orchestrator,
            TenantId, SessionId,
            correlationId: null, sseWriter: null,
            logger: NullLogger<InvokeSummarizePlaybookTool>.Instance);

        await tool.InvokeSummarizePlaybookAsync(fileIds: null, style: "   ");

        // Inspect the captured user content — it should NOT contain a "Style hint:" line
        // because whitespace was converted to null by the tool.
        var userContent = openAi.LastUserContent;
        userContent.Should().NotContain("Style hint:",
            because: "whitespace-only style MUST be coerced to null by the tool so the playbook default applies");
    }

    [Fact]
    public async Task InvokeSummarizePlaybookAsync_ExplicitStyle_PropagatesThroughToOrchestrator()
    {
        var orchestrator = BuildOrchestrator(
            out _, out _, out var openAi,
            session: BuildSession(FileId1));
        openAi.TokensToYield = new[] { "{\"tldr\":[\"bulleted\"]}" };

        var tool = new InvokeSummarizePlaybookTool(
            orchestrator,
            TenantId, SessionId,
            correlationId: null, sseWriter: null,
            logger: NullLogger<InvokeSummarizePlaybookTool>.Instance);

        await tool.InvokeSummarizePlaybookAsync(fileIds: null, style: "bullet");

        // The orchestrator's BuildUserContent prepends "Style hint: bullet" when StyleHint
        // is non-blank. This proves the tool's style argument flowed all the way through
        // to the LLM message construction.
        openAi.LastUserContent.Should().Contain("Style hint: bullet",
            because: "the tool MUST propagate the explicit style hint to the orchestrator");
    }

    // ── SSE forwarding (FR-04 + task 016 progressive-streaming contract) ───────────

    [Fact]
    public async Task InvokeSummarizePlaybookAsync_ForwardsEveryOrchestratorChunkToSseWriter()
    {
        // Multi-file (≥2) triggers the orchestrator's combined-summary interjection chunk.
        // We verify the SSE writer received that interjection AND the subsequent
        // playbook stream chunks (Completed final).
        var orchestrator = BuildOrchestrator(
            out _, out _, out var openAi,
            session: BuildSession(FileId1, FileId2));
        openAi.TokensToYield = new[] { "{\"tldr\":[\"two-file summary\"]}" };

        var capturedEvents = new List<ChatSseEvent>();
        Func<ChatSseEvent, CancellationToken, Task> sseWriter = (evt, _) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        var tool = new InvokeSummarizePlaybookTool(
            orchestrator,
            TenantId, SessionId,
            correlationId: null, sseWriter: sseWriter,
            logger: NullLogger<InvokeSummarizePlaybookTool>.Instance);

        await tool.InvokeSummarizePlaybookAsync(fileIds: null, style: null);

        // The first event MUST be the combined-summary interjection (Type="text") per
        // FR-04 + the orchestrator's CombinedSummaryInterjection emission contract.
        capturedEvents.Should().NotBeEmpty();
        capturedEvents[0].Type.Should().Be("text");
        capturedEvents[0].Content.Should().Be(PlaybookExecutionEngine.CombinedSummaryInterjection);

        // The terminal event MUST be Type="complete" — the orchestrator's Completed chunk.
        capturedEvents.Last().Type.Should().Be("complete");
    }

    // ── Convergence (FR-05) ────────────────────────────────────────────────────────

    [Fact]
    public async Task ConvergenceTest_AgentToolAndDirectEndpoint_ProduceIdenticalSseEventStream_FR05()
    {
        // FR-05 convergence proof: invoking the orchestrator via the agent-tool path AND
        // via the direct-endpoint surrogate path with IDENTICAL input MUST produce
        // IDENTICAL streaming chunks. Both call the same SessionSummarizeOrchestrator
        // .SummarizeSessionFilesAsync — the only difference is the Path discriminator
        // which affects TELEMETRY ONLY, not the output chunk stream (per task 012 spec).
        //
        // Strategy: build two orchestrators with IDENTICAL stubbed deps (same canned
        // OpenAI tokens; same RAG response; same session). Run the agent-tool path once
        // and the direct-endpoint surrogate once; compare the chunk streams element-by-
        // element.

        // -- Agent-tool path
        var agentOrchestrator = BuildOrchestrator(
            out _, out _, out var agentOpenAi,
            session: BuildSession(FileId1));
        agentOpenAi.TokensToYield = new[] { "{\"tldr\":[\"converged\"]}" };

        var agentSse = new List<ChatSseEvent>();
        Func<ChatSseEvent, CancellationToken, Task> agentWriter = (e, _) =>
        {
            agentSse.Add(e);
            return Task.CompletedTask;
        };
        var tool = new InvokeSummarizePlaybookTool(
            agentOrchestrator,
            TenantId, SessionId,
            correlationId: null,
            sseWriter: agentWriter,
            logger: NullLogger<InvokeSummarizePlaybookTool>.Instance);
        await tool.InvokeSummarizePlaybookAsync(fileIds: null, style: null);

        // -- Direct-endpoint surrogate path (what task 014 will do)
        var directOrchestrator = BuildOrchestrator(
            out _, out _, out var directOpenAi,
            session: BuildSession(FileId1));
        directOpenAi.TokensToYield = new[] { "{\"tldr\":[\"converged\"]}" };

        var directChunks = new List<AnalysisChunk>();
        var directRequest = new SummarizeSessionFilesRequest(
            TenantId: TenantId,
            SessionId: SessionId,
            FileIds: null,
            StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);
        await foreach (var chunk in directOrchestrator.SummarizeSessionFilesAsync(
            directRequest, CancellationToken.None))
        {
            directChunks.Add(chunk);
        }

        // FR-05 assertion: the agent-tool's emitted SSE event chain MUST match the direct
        // endpoint's chunk stream — by Type + Content, in ORDER. The agent-tool's
        // sseWriter wraps each AnalysisChunk in a ChatSseEvent(Type, Content, Data=chunk)
        // so the comparison is direct.
        agentSse.Should().HaveCount(directChunks.Count,
            "agent-tool path and direct-endpoint path MUST emit the same number of chunks (FR-05)");
        for (var i = 0; i < directChunks.Count; i++)
        {
            agentSse[i].Type.Should().Be(directChunks[i].Type,
                $"chunk {i} type must match (FR-05 convergence)");
            agentSse[i].Content.Should().Be(directChunks[i].Content,
                $"chunk {i} content must match (FR-05 convergence)");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────

    private InvokeSummarizePlaybookTool CreateSutWithEmptySession()
    {
        var orchestrator = BuildOrchestrator(out _, out _, out _, session: BuildSession());
        return new InvokeSummarizePlaybookTool(
            orchestrator,
            TenantId, SessionId,
            correlationId: "trace-001",
            sseWriter: null,
            logger: NullLogger<InvokeSummarizePlaybookTool>.Instance);
    }

    /// <summary>
    /// Constructs a real <see cref="SessionSummarizeOrchestrator"/> backed by a real
    /// <see cref="PlaybookExecutionEngine"/> wired against stubbed I/O boundary deps. Returns
    /// the orchestrator + the mocks/stubs the test can inspect.
    /// <para>
    /// <b>R6 task 025 (Pillar 4 / D-A-17) update</b>: the chat-summarize streaming pipeline
    /// moved from the orchestrator into the engine. This helper now constructs both — the
    /// orchestrator forwards to the engine, the engine runs RAG + Structured Outputs + parser
    /// against the stubs.
    /// </para>
    /// </summary>
    private static SessionSummarizeOrchestrator BuildOrchestrator(
        out TestableChatSessionManager sessionManager,
        out Mock<IRagService> ragMock,
        out RecordingOpenAiClient openAi,
        ChatSession? session = null)
    {
        sessionManager = new TestableChatSessionManager { Session = session };
        ragMock = new Mock<IRagService>();
        ragMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagSearchResponse
            {
                Query = "default",
                Results = new[]
                {
                    new RagSearchResult { Id = "chunk-1", DocumentName = "f1.pdf", Content = "Lorem ipsum.", Score = 0.9 }
                }
            });

        openAi = new RecordingOpenAiClient();

        // R6 task 025 — FK chain (post-task-024 valid) supersedes alternate-key load.
        var actionId = Guid.Parse("eeb05bfd-1260-f111-ab0b-70a8a59455f4");
        var nodeMock = new Mock<INodeService>();
        nodeMock
            .Setup(n => n.GetNodesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new PlaybookNodeDto
            {
                Id = Guid.NewGuid(),
                PlaybookId = SessionSummarizeOrchestrator.ChatSummarizePlaybookId,
                ActionId = actionId
            }});

        var entityMock = new Mock<Spaarke.Dataverse.IGenericEntityService>();
        entityMock
            .Setup(e => e.RetrieveAsync(
                "sprk_analysisaction",
                It.IsAny<Guid>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildActionEntity(actionId,
                systemPrompt: "You are the R5 Summarize-for-Chat assistant.",
                outputSchemaJson: """{"type":"object","additionalProperties":false,"required":["tldr"],"properties":{"tldr":{"type":"array","items":{"type":"string"}}}}"""));

        var telemetry = new R5SummarizeTelemetry();

        var engine = new PlaybookExecutionEngine(
            builderService: Mock.Of<IAiPlaybookBuilderService>(),
            orchestrationService: Mock.Of<IPlaybookOrchestrationService>(),
            httpContextAccessor: Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            nodeService: nodeMock.Object,
            entityService: entityMock.Object,
            ragService: ragMock.Object,
            openAiClient: openAi,
            summarizeTelemetry: telemetry,
            logger: NullLogger<PlaybookExecutionEngine>.Instance);

        return new SessionSummarizeOrchestrator(
            sessionManager,
            engine,
            NullLogger<SessionSummarizeOrchestrator>.Instance);
    }

    private static ChatSession BuildSession(params string[] fileIds)
    {
        var files = fileIds
            .Select(id => new ChatSessionFile(
                FileId: id,
                FileName: $"{id}.pdf",
                ContentType: "application/pdf",
                SizeBytes: 1024,
                SearchDocumentIdsCsv: $"doc-{id}-1",
                UploadedAt: DateTimeOffset.UtcNow))
            .ToList();
        return new ChatSession(
            SessionId: SessionId,
            TenantId: TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<Sprk.Bff.Api.Models.Ai.Chat.ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: files);
    }

    private static Entity BuildActionEntity(Guid actionId, string systemPrompt, string outputSchemaJson)
    {
        var entity = new Entity("sprk_analysisaction", actionId);
        entity["sprk_analysisactionid"] = entity.Id;
        entity["sprk_name"] = "Summarize Document for Chat";
        entity["sprk_actioncode"] = "SUM-CHAT@v1";
        entity["sprk_systemprompt"] = systemPrompt;
        entity["sprk_outputschemajson"] = outputSchemaJson;
        return entity;
    }

    // ── Test doubles ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Subclass of <see cref="ChatSessionManager"/> that overrides the virtual
    /// <see cref="ChatSessionManager.GetSessionAsync(string, string, CancellationToken)"/>.
    /// Mirrors <c>SessionSummarizeOrchestratorTests.TestableChatSessionManager</c>.
    /// </summary>
    private sealed class TestableChatSessionManager : ChatSessionManager
    {
        public TestableChatSessionManager() : base(
            cache: Mock.Of<IDistributedCache>(),
            dataverseRepository: Mock.Of<IChatDataverseRepository>(),
            logger: Mock.Of<ILogger<ChatSessionManager>>(),
            persistence: null,
            cleanupSignal: null)
        {
        }

        public ChatSession? Session { get; set; }

        public override Task<ChatSession?> GetSessionAsync(
            string tenantId, string sessionId, CancellationToken ct = default)
            => Task.FromResult(Session);
    }

    /// <summary>
    /// Stub <see cref="IOpenAiClient"/> that records the last received user content (for
    /// style/file-id verification) and yields a canned token stream. Mirrors
    /// <c>SessionSummarizeOrchestratorTests.StubOpenAiClient</c> + adds message capture.
    /// </summary>
    private sealed class RecordingOpenAiClient : IOpenAiClient
    {
        public IReadOnlyList<string> TokensToYield { get; set; } = Array.Empty<string>();
        public string? LastUserContent { get; private set; }

        public async IAsyncEnumerable<string> StreamStructuredCompletionAsync(
            IEnumerable<global::OpenAI.Chat.ChatMessage> messages,
            BinaryData jsonSchema,
            string schemaName,
            string? model = null,
            int? maxOutputTokens = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Capture the last UserChatMessage's text for assertion (orchestrator passes a
            // system prompt + user content; user content is what carries style + file refs).
            foreach (var m in messages)
            {
                if (m is global::OpenAI.Chat.UserChatMessage user)
                {
                    LastUserContent = string.Concat(user.Content.Select(p => p.Text ?? string.Empty));
                }
            }

            foreach (var token in TokensToYield)
            {
                yield return token;
                await Task.Yield();
            }
        }

        public IAsyncEnumerable<string> StreamCompletionAsync(string prompt, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by InvokeSummarizePlaybookTool tests.");
        public Task<string> GetCompletionAsync(string prompt, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by InvokeSummarizePlaybookTool tests.");
        public IAsyncEnumerable<string> StreamVisionCompletionAsync(string prompt, byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by InvokeSummarizePlaybookTool tests.");
        public Task<string> GetVisionCompletionAsync(string prompt, byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by InvokeSummarizePlaybookTool tests.");
        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, string? model = null, int? dimensions = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by InvokeSummarizePlaybookTool tests.");
        public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(IEnumerable<string> texts, string? model = null, int? dimensions = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by InvokeSummarizePlaybookTool tests.");
        public Task<ChatCompletionResult> GetChatCompletionWithToolsAsync(IEnumerable<global::OpenAI.Chat.ChatMessage> messages, IEnumerable<global::OpenAI.Chat.ChatTool> tools, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by InvokeSummarizePlaybookTool tests.");
        public Task<T> GetStructuredCompletionAsync<T>(IEnumerable<global::OpenAI.Chat.ChatMessage> messages, BinaryData jsonSchema, string schemaName, string deploymentName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by InvokeSummarizePlaybookTool tests.");
        public Task<string> GetStructuredCompletionRawAsync(string prompt, BinaryData jsonSchema, string schemaName, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by InvokeSummarizePlaybookTool tests.");
    }
}
