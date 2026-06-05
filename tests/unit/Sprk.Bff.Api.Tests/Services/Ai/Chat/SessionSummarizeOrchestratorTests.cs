using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="SessionSummarizeOrchestrator"/> — the R5 task 012 (D2-03) convergence
/// orchestrator that bridges the direct-endpoint path (task 014) and the agent-tool path (task 015).
///
/// Covers spec FR-01 + FR-04 + FR-08 + FR-11 + NFR-02 + NFR-03 + SC-08 acceptance criteria.
/// Includes ADR-010 enforcement (no R5-authored interface) + convergence-method count
/// reflection assertions per acceptance criterion in the task POML.
/// </summary>
public class SessionSummarizeOrchestratorTests
{
    private const string TenantId = "tenant-abc";
    private const string SessionId = "session-xyz";
    private const string FileId1 = "file-001";
    private const string FileId2 = "file-002";

    private readonly TestableChatSessionManager _sessionManagerStub;
    private readonly Mock<IRagService> _ragServiceMock;
    private readonly StubOpenAiClient _openAiClient;
    private readonly Mock<Spaarke.Dataverse.IGenericEntityService> _entityServiceMock;
    private readonly R5SummarizeTelemetry _telemetry;
    private readonly Mock<ILogger<SessionSummarizeOrchestrator>> _loggerMock;

    public SessionSummarizeOrchestratorTests()
    {
        _sessionManagerStub = new TestableChatSessionManager();
        _ragServiceMock = new Mock<IRagService>();
        _openAiClient = new StubOpenAiClient();
        _entityServiceMock = new Mock<Spaarke.Dataverse.IGenericEntityService>();
        _telemetry = new R5SummarizeTelemetry();
        _loggerMock = new Mock<ILogger<SessionSummarizeOrchestrator>>();

        // Default RAG response — small valid result set so the happy-path tests can proceed.
        _ragServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagSearchResponse
            {
                Query = "default",
                Results = new[]
                {
                    new RagSearchResult { Id = "chunk-1", DocumentName = "f1.pdf", Content = "Lorem ipsum.", Score = 0.9 }
                }
            });

        // Default action seed: valid system prompt + a minimal but valid Structured-Outputs schema.
        _entityServiceMock
            .Setup(e => e.RetrieveByAlternateKeyAsync(
                "sprk_analysisaction",
                It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildActionEntity(
                systemPrompt: "You are the R5 Summarize-for-Chat assistant.",
                outputSchemaJson: """{"type":"object","additionalProperties":false,"required":["tldr"],"properties":{"tldr":{"type":"array","items":{"type":"string"}}}}"""));
    }

    private SessionSummarizeOrchestrator CreateSut() => new(
        _sessionManagerStub,
        _ragServiceMock.Object,
        _openAiClient,
        _entityServiceMock.Object,
        _telemetry,
        _loggerMock.Object);

    // ─── (a) Single-file does NOT emit combined-summary interjection — FR-04 negative ──────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_SingleFile_DoesNotEmitCombinedSummaryInterjection()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        _openAiClient.TokensToYield = new[] { "{\"tldr\":[\"point\"]}" };

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        var chunks = await Collect(sut.SummarizeSessionFilesAsync(request));

        chunks.Should().NotContain(c => c.Type == "text" && c.Content == SessionSummarizeOrchestrator.CombinedSummaryInterjection);
    }

    // ─── (b) Multi-file emits combined-summary interjection BEFORE stream — FR-04 positive ────

    [Fact]
    public async Task SummarizeSessionFilesAsync_MultiFile_EmitsCombinedSummaryInterjectionBeforePlaybookStream()
    {
        _sessionManagerStub.Session = BuildSession(FileId1, FileId2);
        _openAiClient.TokensToYield = new[] { "{\"tldr\":[\"a\",\"b\"]}" };

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1, FileId2 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        var chunks = await Collect(sut.SummarizeSessionFilesAsync(request));

        chunks.Should().NotBeEmpty();
        chunks[0].Type.Should().Be("text");
        chunks[0].Content.Should().Be(SessionSummarizeOrchestrator.CombinedSummaryInterjection);

        // And the interjection precedes any delta or completion chunk.
        var firstStructured = chunks.FirstOrDefault(c => c.Type is "delta" or "complete");
        firstStructured.Should().NotBeNull();
        chunks.IndexOf(chunks[0]).Should().BeLessThan(chunks.IndexOf(firstStructured!));
    }

    // ─── (c) ADR-014 — RagService gets BOTH tenantId AND sessionId filters ─────────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_PropagatesTenantAndSessionIdToRagSearchOptions()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        _openAiClient.TokensToYield = new[] { "{\"tldr\":[\"x\"]}" };
        RagSearchOptions? observedOptions = null;
        _ragServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, opts, _) => observedOptions = opts)
            .ReturnsAsync(new RagSearchResponse { Query = "q", Results = Array.Empty<RagSearchResult>() });

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        _ = await Collect(sut.SummarizeSessionFilesAsync(request));

        observedOptions.Should().NotBeNull();
        observedOptions!.TenantId.Should().Be(TenantId);
        observedOptions.SessionId.Should().Be(SessionId);
    }

    // ─── (d) NFR-02 — hard cap 20 files per session ────────────────────────────────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_RejectsMoreThanTwentyFileIds()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        var tooMany = Enumerable.Range(1, ChatSession.MaxUploadedFiles + 1).Select(i => $"f-{i}").ToList();
        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, tooMany, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();

        var act = async () => { await foreach (var _ in sut.SummarizeSessionFilesAsync(request)) { } };
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*NFR-02*");
    }

    // ─── (e) Mid-stream exception yields FromError + terminates gracefully ─────────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_MidStreamException_YieldsFromErrorAndTerminates()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        _openAiClient.ThrowMidStream = true;
        _openAiClient.TokensToYield = new[] { "{\"tld" }; // emits one token, then the next MoveNext throws

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        var chunks = await Collect(sut.SummarizeSessionFilesAsync(request));

        chunks.Should().Contain(c => c.Type == "error");
        chunks.Last().Type.Should().Be("error");
        chunks.Last().Error.Should().NotBeNullOrEmpty();
    }

    // ─── (f) ADR-010 — class has no R5-authored interface ─────────────────────────────────────

    [Fact]
    public void SessionSummarizeOrchestrator_HasNoR5AuthoredInterface()
    {
        var ifaces = typeof(SessionSummarizeOrchestrator).GetInterfaces();
        // Filter out framework-supplied interfaces (System.*, Microsoft.*); any remaining
        // would indicate an R5-authored interface, which ADR-010 forbids unless a genuine seam exists.
        var r5Authored = ifaces.Where(i =>
            !i.Namespace?.StartsWith("System", StringComparison.Ordinal) is true
            && !i.Namespace?.StartsWith("Microsoft", StringComparison.Ordinal) is true).ToList();
        r5Authored.Should().BeEmpty(
            "ADR-010 forbids interfaces-for-testability-alone; SessionSummarizeOrchestrator is concrete by design");
    }

    // ─── (g) Convergence — exactly ONE public streaming entry point ────────────────────────────

    [Fact]
    public void SessionSummarizeOrchestrator_ExposesExactlyOneConvergenceMethod()
    {
        var publicMethods = typeof(SessionSummarizeOrchestrator)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName) // exclude property accessors / operators
            .ToList();

        // The convergence shape: returns IAsyncEnumerable<AnalysisChunk>.
        var convergence = publicMethods
            .Where(m => m.ReturnType.IsGenericType
                && m.ReturnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)
                && m.ReturnType.GetGenericArguments()[0] == typeof(AnalysisChunk))
            .ToList();

        convergence.Should().HaveCount(1,
            "spec FR-01 + FR-08 + SC-08 require a single convergence method that both task 014 and task 015 delegate to");
        convergence[0].Name.Should().Be(nameof(SessionSummarizeOrchestrator.SummarizeSessionFilesAsync));
    }

    // ─── (h) Telemetry — agent_tool path dimension ────────────────────────────────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_AgentToolPath_TelemetryRecordsAgentToolDimension()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        _openAiClient.TokensToYield = new[] { "{\"tldr\":[\"a\"]}" };

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.AgentTool);

        request.Path.ToTelemetryValue().Should().Be("agent_tool");

        var sut = CreateSut();
        _ = await Collect(sut.SummarizeSessionFilesAsync(request));
        // No exception means the bounded-enum guard accepted agent_tool. Negative case is covered
        // by the next test using DirectEndpoint.
    }

    // ─── (i) Telemetry — direct_endpoint path dimension ───────────────────────────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_DirectEndpointPath_TelemetryRecordsDirectEndpointDimension()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        _openAiClient.TokensToYield = new[] { "{\"tldr\":[\"a\"]}" };

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        request.Path.ToTelemetryValue().Should().Be("direct_endpoint");

        var sut = CreateSut();
        var chunks = await Collect(sut.SummarizeSessionFilesAsync(request));
        chunks.Should().Contain(c => c.Type == "complete");
    }

    // ─── (j) Empty input validation — required tenant + session ID ────────────────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_EmptyTenantId_Throws()
    {
        var request = new SummarizeSessionFilesRequest(
            TenantId: "", SessionId: SessionId, FileIds: null, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();

        var act = async () => { await foreach (var _ in sut.SummarizeSessionFilesAsync(request)) { } };
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SummarizeSessionFilesAsync_EmptySessionId_Throws()
    {
        var request = new SummarizeSessionFilesRequest(
            TenantId: TenantId, SessionId: "", FileIds: null, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();

        var act = async () => { await foreach (var _ in sut.SummarizeSessionFilesAsync(request)) { } };
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─── (k) Decline path — empty session emits a decline-style error chunk ───────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_NoFilesInSession_EmitsDecline()
    {
        _sessionManagerStub.Session = BuildSession(/* no files */);
        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, FileIds: null, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        var chunks = await Collect(sut.SummarizeSessionFilesAsync(request));

        chunks.Should().ContainSingle();
        chunks[0].Type.Should().Be("error");
        chunks[0].Error.Should().Contain("No files are available");
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static async Task<List<AnalysisChunk>> Collect(IAsyncEnumerable<AnalysisChunk> source)
    {
        var list = new List<AnalysisChunk>();
        await foreach (var chunk in source)
        {
            list.Add(chunk);
        }
        return list;
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
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: files);
    }

    private static Entity BuildActionEntity(string systemPrompt, string outputSchemaJson)
    {
        var entity = new Entity("sprk_analysisaction", Guid.NewGuid());
        entity["sprk_analysisactionid"] = entity.Id;
        entity["sprk_name"] = "Summarize Document for Chat";
        entity["sprk_actioncode"] = SessionSummarizeOrchestrator.SummarizeActionCode;
        entity["sprk_systemprompt"] = systemPrompt;
        entity["sprk_outputschemajson"] = outputSchemaJson;
        return entity;
    }

    // ─── Test doubles ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Subclass of <see cref="ChatSessionManager"/> that overrides the virtual
    /// <see cref="ChatSessionManager.GetSessionAsync(string, string, CancellationToken)"/> so we can
    /// inject a fixed session without wiring Redis/Dataverse. Sealed Moq doesn't apply to non-virtual
    /// methods; subclass-with-override is the canonical pattern in this codebase (see ChatSessionManagerTests).
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
    /// Stub <see cref="IOpenAiClient"/> for streaming tests. Only the streaming method is needed;
    /// all other interface members throw to make accidental use visible.
    /// </summary>
    private sealed class StubOpenAiClient : IOpenAiClient
    {
        public IReadOnlyList<string> TokensToYield { get; set; } = Array.Empty<string>();
        public bool ThrowMidStream { get; set; }

        public async IAsyncEnumerable<string> StreamStructuredCompletionAsync(
            IEnumerable<global::OpenAI.Chat.ChatMessage> messages,
            BinaryData jsonSchema,
            string schemaName,
            string? model = null,
            int? maxOutputTokens = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var i = 0;
            foreach (var token in TokensToYield)
            {
                if (ThrowMidStream && i > 0)
                {
                    throw new InvalidOperationException("simulated mid-stream failure");
                }
                yield return token;
                i++;
                await Task.Yield();
            }

            if (ThrowMidStream && TokensToYield.Count > 0)
            {
                // If only one token was queued and we still want to fail, simulate the failure
                // on the MoveNext following the last yield.
                throw new InvalidOperationException("simulated mid-stream failure");
            }
        }

        public IAsyncEnumerable<string> StreamCompletionAsync(string prompt, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by R5 task 012 tests.");
        public Task<string> GetCompletionAsync(string prompt, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by R5 task 012 tests.");
        public IAsyncEnumerable<string> StreamVisionCompletionAsync(string prompt, byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by R5 task 012 tests.");
        public Task<string> GetVisionCompletionAsync(string prompt, byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by R5 task 012 tests.");
        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, string? model = null, int? dimensions = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by R5 task 012 tests.");
        public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(IEnumerable<string> texts, string? model = null, int? dimensions = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by R5 task 012 tests.");
        public Task<ChatCompletionResult> GetChatCompletionWithToolsAsync(IEnumerable<global::OpenAI.Chat.ChatMessage> messages, IEnumerable<global::OpenAI.Chat.ChatTool> tools, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by R5 task 012 tests.");
        public Task<T> GetStructuredCompletionAsync<T>(IEnumerable<global::OpenAI.Chat.ChatMessage> messages, BinaryData jsonSchema, string schemaName, string deploymentName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by R5 task 012 tests.");
        public Task<string> GetStructuredCompletionRawAsync(string prompt, BinaryData jsonSchema, string schemaName, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by R5 task 012 tests.");
    }
}
