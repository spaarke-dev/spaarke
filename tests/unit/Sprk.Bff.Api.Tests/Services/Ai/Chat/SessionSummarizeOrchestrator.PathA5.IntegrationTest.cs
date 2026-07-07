using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Catalog-path integration tests for <see cref="SessionSummarizeOrchestrator"/> (FR-P1-01,
/// ai-architecture-redesign-r1 task 020). These exercise the orchestrator end-to-end through
/// the REAL prompted executor — <see cref="ActionRunner"/> + <see cref="PromptSchemaRenderer"/>
/// rendering the REAL SUM-CHAT@v1 JPS — with mocks only at the module boundaries
/// (<see cref="IConsumerRoutingService"/>, <see cref="IScopeResolverService"/>,
/// <see cref="ISessionFileTextSource"/>, <see cref="IOpenAiClient"/>) per ADR-038.
///
/// <para>
/// <b>KEEP-protected per ADR-038 + tests/CLAUDE.md</b>: each [Fact] anchors a concrete
/// contract behavior — catalog dispatch through the Binding row (FR-P1-01 / ADR-039),
/// SUM-CHAT@v1 JPS render through PromptSchemaRenderer (the jps-validate render test),
/// kill-switch fail-fast (ADR-030 P3), and the LLM-failure FromError terminator (the chat
/// client requires an explicit error chunk, not a silent disconnect).
/// </para>
///
/// <para>
/// <b>Why orchestrator-level integration (not WebApplicationFactory)</b>: the chat-summarize
/// execution lives entirely below the endpoint boundary
/// (<see cref="Api.Ai.SummarizeSessionEndpoint"/> is a thin SSE writer that calls into the
/// orchestrator + writes its <see cref="AnalysisChunk"/> output verbatim to the wire, covered
/// by <c>SummarizeSessionEndpointContractTests</c>). Exercising the orchestrator with the real
/// executor stack + a stub LLM boundary covers the integration contract per ADR-038 §1
/// (integration-heavy pyramid) with no transport-level mocks (ban B1).
/// </para>
/// </summary>
public class SessionSummarizeOrchestratorPathA5IntegrationTest
{
    private const string TenantId = "tenant-integration";
    private const string SessionId = "session-integration";
    private const string FileId1 = "file-int-001";

    private static readonly Guid BindingId = Guid.Parse("651194cd-3670-f111-ab0e-70a8a590c51c");
    private static readonly Guid ActionId = Guid.Parse("eeb05bfd-1260-f111-ab0b-70a8a59455f4");

    /// <summary>
    /// The REAL SUM-CHAT@v1 JPS as authored by task 020 (canonical artifact:
    /// <c>projects/spaarke-ai-architecture-redesign-r1/notes/jps/SUM-CHAT-v1.jps.json</c>;
    /// deployed to <c>sprk_analysisaction.sprk_systemprompt</c> on the SUM-CHAT@v1 row).
    /// Embedded verbatim (minus examples, which don't affect format detection) so this test
    /// is the executable render test: PromptSchemaRenderer MUST detect JPS format and render
    /// the instruction sections + the ## Document section.
    /// </summary>
    private const string SumChatJps = """
        {
          "$schema": "https://spaarke.com/schemas/prompt/v1",
          "$version": 1,
          "instruction": {
            "role": "You are the Spaarke Summarize-for-Chat assistant, an expert legal-operations document summarizer.",
            "task": "Read the session file text supplied in the ## Document section (1-N uploaded files, concatenated in file-then-chunk order) and produce a structured summary — TL;DR bullets, narrative summary, keywords, and named entities — suitable for progressive rendering in the Spaarke Assistant Workspace pane.",
            "constraints": [
              "Emit a JSON object matching the configured output schema EXACTLY; additionalProperties is false — do not invent fields.",
              "STREAMING-AWARE EMISSION ORDER (LOAD-BEARING): emit fields in EXACTLY this order: tldr, then summary, then keywords, then entities.",
              "tldr: 1-3 concise bullets, each 140 characters or fewer. Emit FIRST.",
              "summary: at most 2 paragraphs of prose and 2000 characters total. Emit SECOND.",
              "keywords: a single comma-separated string (NOT an array), 5-15 keywords. Emit THIRD.",
              "entities: an object of shape { organizations: string[], persons: string[] }. Emit LAST.",
              "Do NOT include rawResponse, parsedSuccessfully, or emailMetadata fields.",
              "Do NOT fabricate content."
            ]
          },
          "input": {
            "document": { "required": true, "maxLength": 100000, "placeholder": "{{document.extractedText}}" }
          },
          "output": {
            "fields": [
              { "name": "tldr", "type": "array", "description": "1-3 concise bullet takeaways." },
              { "name": "summary", "type": "string", "description": "Narrative summary." },
              { "name": "keywords", "type": "string", "description": "Comma-separated keywords." },
              { "name": "entities", "type": "object", "description": "Named entities." }
            ],
            "structuredOutput": true
          },
          "metadata": {
            "description": "SUM-CHAT@v1 — chat-session file summarization (UC-A-1).",
            "tags": ["chat-summarize", "UC-A-1", "prompted"]
          }
        }
        """;

    private const string SumChatOutputSchema =
        """{"type":"object","additionalProperties":false,"required":["tldr","summary","keywords","entities"],"properties":{"tldr":{"type":"array","items":{"type":"string"}},"summary":{"type":"string"},"keywords":{"type":"string"},"entities":{"type":"object","additionalProperties":false,"required":["organizations","persons"],"properties":{"organizations":{"type":"array","items":{"type":"string"}},"persons":{"type":"array","items":{"type":"string"}}}}}}""";

    private const string LlmResultJson =
        """{"tldr":["Engagement letter for Acme Corporation","Fees billed at $450/hour"],"summary":"Integration test summary of the engagement letter.","keywords":"engagement letter, Acme Corporation, fees","entities":{"organizations":["Acme Corporation"],"persons":["Jane Smith"]}}""";

    /// <summary>
    /// Scenario 1 — catalog HIT end-to-end (FR-P1-01 / ADR-039). With the chat-summarize
    /// Binding row resolved via <see cref="IConsumerRoutingService.ResolveBindingAsync"/>, the
    /// orchestrator loads the SUM-CHAT@v1 Action and executes it through the REAL
    /// <see cref="ActionRunner"/> + <see cref="PromptSchemaRenderer"/>: (a) the JPS is
    /// detected + rendered (role text + ## Document section with the session file text in the
    /// prompt sent to the LLM boundary), and (b) the structured completion surfaces as the
    /// terminal <see cref="AnalysisChunk.Completed(DocumentAnalysisResult)"/> wire chunk.
    /// </summary>
    [Fact]
    public async Task CatalogPath_BindingHit_RendersJpsAndCompletesWithStructuredResult()
    {
        // Arrange — real executor stack, stub LLM boundary.
        var openAi = new StubStructuredOpenAiClient { RawJsonToReturn = LlmResultJson };
        var sut = CreateSut(openAi);

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: "executive",
            Path: SummarizeInvocationPath.DirectEndpoint,
            CorrelationId: "integration-corr-001");

        // Act
        var chunks = await Collect(sut.SummarizeSessionFilesAsync(request));

        // Assert — (a) SUM-CHAT@v1 JPS rendered through PromptSchemaRenderer (render test).
        openAi.CapturedPrompt.Should().NotBeNull("the prompted executor must reach the LLM boundary");
        openAi.CapturedPrompt.Should().Contain("Spaarke Summarize-for-Chat assistant",
            "instruction.role from the SUM-CHAT@v1 JPS renders as the prompt opening");
        openAi.CapturedPrompt.Should().Contain("## Constraints",
            "instruction.constraints render as a numbered section — proof the JPS format was " +
            "detected (a flat-text fallback would have echoed raw JSON)");
        openAi.CapturedPrompt.Should().Contain("## Document",
            "PromptSchemaRenderer embeds the session file text in the ## Document section");
        openAi.CapturedPrompt.Should().Contain("uploaded engagement letter text",
            "the fetched session-file text is what the LLM summarizes");
        openAi.CapturedSchemaJson.Should().Contain("\"tldr\"",
            "constrained decoding uses the Action row's OutputSchemaJson (SUM-CHAT@v1)");

        // Assert — (b) wire shape: terminal complete chunk with the parsed structured result.
        chunks.Should().HaveCount(1, "single-file request: no interjection, one terminal chunk");
        chunks[0].Type.Should().Be("complete");
        chunks[0].Done.Should().BeTrue();
        chunks[0].Result.Should().NotBeNull();
        chunks[0].Result!.Summary.Should().Be("Integration test summary of the engagement letter.");
        chunks[0].Result!.TlDr.Should().BeEquivalentTo(
            new[] { "Engagement letter for Acme Corporation", "Fees billed at $450/hour" });
        chunks[0].Result!.Keywords.Should().Contain("Acme Corporation");
        chunks[0].Result!.ParsedSuccessfully.Should().BeTrue();
    }

    /// <summary>
    /// Scenario 4 — AI kill-switch OFF (compound-AI feature disabled).
    /// <see cref="NullSessionSummarizeOrchestrator"/> short-circuits at the first
    /// <c>MoveNextAsync()</c> with <see cref="FeatureDisabledException"/> per ADR-030 P3.
    /// The endpoint catches this BEFORE setting SSE headers and emits a 503 ProblemDetails.
    /// Contract preserved unchanged across the FR-P1-01 catalog cutover — the Null subclass
    /// continues to throw without dereferencing any of the catalog-path dependencies.
    /// </summary>
    [Fact]
    public async Task NullKillSwitchSubclass_ThrowsFeatureDisabledOnFirstMoveNext()
    {
        var loggerMock = new Mock<ILogger<SessionSummarizeOrchestrator>>();
        var sut = new NullSessionSummarizeOrchestrator(loggerMock.Object);

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var act = async () => { await foreach (var _ in sut.SummarizeSessionFilesAsync(request)) { } };
        var thrown = await act.Should().ThrowAsync<FeatureDisabledException>();
        thrown.Which.ErrorCode.Should().Be("ai.summarize.disabled",
            "ADR-030 P3 contract — error code drives ProblemDetails errorCode extension in the endpoint");
        thrown.Which.Message.Should().Contain("Analysis:Enabled",
            "operator diagnostic must reference the gating config keys");
    }

    /// <summary>
    /// Scenario 7 — LLM failure at the executor boundary. The orchestrator MUST emit a
    /// terminal <see cref="AnalysisChunk.FromError"/> rather than letting the exception kill
    /// the stream mid-flight. The chat client relies on the explicit error chunk to render a
    /// failure-state UX — a silent disconnect would leave the user staring at a spinner.
    /// </summary>
    [Fact]
    public async Task LlmFailure_EmitsTerminalFromErrorChunk()
    {
        var openAi = new StubStructuredOpenAiClient
        {
            ExceptionToThrow = new InvalidOperationException(
                "Azure OpenAI service returned HTTP 503 after retries exhausted.")
        };
        var sut = CreateSut(openAi);

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var chunks = await Collect(sut.SummarizeSessionFilesAsync(request));

        chunks.Should().HaveCount(1, "the failure must reach the chat client as a chunk");
        chunks[0].Type.Should().Be("error");
        chunks[0].Error.Should().Contain("AI summarization failed",
            "friendly wire message; the raw exception detail stays in server logs (ADR-019)");
        chunks[0].Done.Should().BeTrue("error chunks are terminal per the AnalysisChunk envelope contract");
    }

    // ─── Wiring ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs the orchestrator with the REAL prompted executor
    /// (<see cref="ActionRunner"/> + <see cref="PromptSchemaRenderer"/>) over the supplied
    /// LLM stub, a catalog boundary returning the chat-summarize Binding + SUM-CHAT@v1
    /// Action, and a session containing one file.
    /// </summary>
    private static SessionSummarizeOrchestrator CreateSut(StubStructuredOpenAiClient openAi)
    {
        var sessionManager = new TestableChatSessionManager
        {
            Session = BuildSession(FileId1)
        };

        var routing = new Mock<IConsumerRoutingService>();
        routing
            .Setup(r => r.ResolveBindingAsync(
                ConsumerTypes.ChatSummarize,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Binding
            {
                BindingId = BindingId,
                ConsumerType = ConsumerTypes.ChatSummarize,
                ConsumerCode = "default",
                Environment = "*",
                ActionId = ActionId,
                ActionKind = ActionKind.Prompted,
                Ucid = "UC-A-1",
                Disposition = BindingDisposition.Informational,
            });

        var scopeResolver = new Mock<IScopeResolverService>();
        scopeResolver
            .Setup(s => s.GetActionAsync(ActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction
            {
                Id = ActionId,
                Name = "Summarize Document for Chat",
                SystemPrompt = SumChatJps,
                OutputSchemaJson = SumChatOutputSchema,
                Temperature = 0.0m,
            });

        var textSource = new Mock<ISessionFileTextSource>();
        textSource
            .Setup(t => t.FetchAsync(
                TenantId, SessionId,
                It.IsAny<IReadOnlyList<ChatSessionFile>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionFileText
            {
                ExtractedText = "uploaded engagement letter text between Acme Corporation and Smith Legal Group",
                DisplayName = $"{FileId1}.pdf",
                ChunkCount = 1
            });

        // REAL prompted executor: ActionRunner + PromptSchemaRenderer (module-boundary mock
        // is the IOpenAiClient only, per ADR-038).
        var actionRunner = new ActionRunner(
            openAi,
            new PromptSchemaRenderer(Mock.Of<ILogger<PromptSchemaRenderer>>()),
            Mock.Of<ILogger<ActionRunner>>());

        return new SessionSummarizeOrchestrator(
            sessionManager,
            routing.Object,
            scopeResolver.Object,
            actionRunner,
            textSource.Object,
            // REAL OutputRouter over the same session manager (FR-P1-02, task 021) — the
            // catalog-path integration now exercises the live ledger-write-before-render seam.
            new OutputRouter(sessionManager, Mock.Of<ILogger<OutputRouter>>()),
            Mock.Of<ILogger<SessionSummarizeOrchestrator>>());
    }

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

    private sealed class TestableChatSessionManager : ChatSessionManager
    {
        public TestableChatSessionManager() : base(
            cache: Mock.Of<ITenantCache>(),
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
    /// Stub <see cref="IOpenAiClient"/> covering the single method the prompted executor uses
    /// (<see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/>). Captures the rendered
    /// prompt + schema so tests can assert the SUM-CHAT@v1 JPS render. All other members
    /// throw to make accidental use visible.
    /// </summary>
    private sealed class StubStructuredOpenAiClient : IOpenAiClient
    {
        public string RawJsonToReturn { get; set; } = "{}";
        public Exception? ExceptionToThrow { get; set; }
        public string? CapturedPrompt { get; private set; }
        public string? CapturedSchemaJson { get; private set; }

        public Task<string> GetStructuredCompletionRawAsync(
            string prompt, BinaryData jsonSchema, string schemaName, string? model = null,
            int? maxOutputTokens = null, float? temperature = null,
            CancellationToken cancellationToken = default)
        {
            CapturedPrompt = prompt;
            CapturedSchemaJson = jsonSchema.ToString();
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }
            return Task.FromResult(RawJsonToReturn);
        }

        public IAsyncEnumerable<string> StreamStructuredCompletionAsync(
            IEnumerable<global::OpenAI.Chat.ChatMessage> messages, BinaryData jsonSchema,
            string schemaName, string? model = null, int? maxOutputTokens = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by catalog-path tests.");
        public IAsyncEnumerable<string> StreamCompletionAsync(string prompt, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by catalog-path tests.");
        public Task<string> GetCompletionAsync(string prompt, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by catalog-path tests.");
        public IAsyncEnumerable<string> StreamVisionCompletionAsync(string prompt, byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by catalog-path tests.");
        public Task<string> GetVisionCompletionAsync(string prompt, byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by catalog-path tests.");
        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, string? model = null, int? dimensions = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by catalog-path tests.");
        public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(IEnumerable<string> texts, string? model = null, int? dimensions = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by catalog-path tests.");
        public Task<ChatCompletionResult> GetChatCompletionWithToolsAsync(IEnumerable<global::OpenAI.Chat.ChatMessage> messages, IEnumerable<global::OpenAI.Chat.ChatTool> tools, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by catalog-path tests.");
        public Task<T> GetStructuredCompletionAsync<T>(IEnumerable<global::OpenAI.Chat.ChatMessage> messages, BinaryData jsonSchema, string schemaName, string deploymentName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by catalog-path tests.");
    }
}
