using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.EventRules;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

/// <summary>
/// ADR-043 (Move 1 / E-10) vertical-slice seam tests — the definition-of-done for the ContextBinder +
/// ActionRunner input-resolution cutover. Drives a real consumer input ALL THE WAY through the execution
/// spine: dispatch → <see cref="ContextBinder"/> input resolution → <see cref="ActionRunner"/> completion →
/// <see cref="OutputRouter"/> → stored <see cref="SessionOutput"/> (ADR-040) → rendered terminal frame.
/// </summary>
/// <remarks>
/// <para>
/// <b>Real path, not mocked</b>: <see cref="ContextBinder"/>, <see cref="ActionRunner"/>,
/// <see cref="PromptSchemaRenderer"/>, <see cref="OutputRouter"/>, and <see cref="ChatSessionManager"/>
/// (over the in-memory tenant cache with production System.Text.Json serialization) are the PRODUCTION
/// types. Only the external LLM boundary (a recording <see cref="IOpenAiClient"/> stub) and the catalog
/// data boundaries (<see cref="IConsumerRoutingService"/>/<see cref="IScopeResolverService"/>/
/// <see cref="ISessionFileTextSource"/>) are doubled. Mocking the binder/runner/router would defeat the
/// category (a contract-shape test is NOT sufficient — ADR-043 governance).
/// </para>
/// <para>
/// <b>Anti-stub</b>: each test asserts the RESOLVED operand reached the LLM prompt (the recorded prompt
/// contains the operand's value). If input resolution were stubbed / the operand never rendered, these
/// fail — exactly what the category guards.
/// </para>
/// </remarks>
public sealed class ContextBinderActionRunnerSeamTests
{
    private const string TenantId = "00000000-0000-0000-0000-0000000000aa";
    private const string SessionId = "11111111-1111-1111-1111-111111111111";
    private static readonly Guid BindingId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ActionId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // OpenAI structured-output schema (shared by the flat-text compose-shaped Actions below).
    private const string ComposeOutputSchema =
        """{"type":"object","additionalProperties":false,"required":["explanation"],"properties":{"explanation":{"type":"string"}}}""";

    private const string ComposeInputSchema =
        """{"type":"object","required":["selectionText"],"properties":{"selectionText":{"type":"string"}}}""";

    // SUM-CHAT-shaped input schema: declares fileIds/styleHint — NEITHER is in the structured-operand
    // vocabulary, so dispatch deterministically takes the file/`## Document` branch (non-regression).
    private const string SummarizeInputSchema =
        """{"type":"object","properties":{"fileIds":{"type":"array","items":{"type":"string"}},"styleHint":{"type":"string"}}}""";

    private const string SummarizeSystemPromptJps =
        """{"$schema":"https://spaarke.com/schemas/prompt/v1","instruction":{"role":"You are the Summarize-for-Chat assistant.","task":"Summarize the session file text in the ## Document section."},"output":{"fields":[{"name":"summary","type":"string"}],"structuredOutput":true}}""";

    private const string SummarizeOutputSchema =
        """{"type":"object","additionalProperties":false,"required":["summary"],"properties":{"summary":{"type":"string"}}}""";

    // ─────────────────────────────────────────────────────────────────────────
    // (i) The compose-B2 shape: an args-text (non-file) operand Action, end-to-end
    //     on the REAL path — input → dispatch → stored SessionOutput → render.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_ArgsTextSelectionOperand_ResolvesRunsStoresAndRenders()
    {
        var h = new Harness();
        await h.SeedSessionAsync(BuildSession());
        h.GivenBinding(inputSchema: ComposeInputSchema);
        h.GivenFlatTextAction("ROLE: Explain the selected clause in plain language.");
        h.OpenAi.RawJsonToReturn = """{"explanation":"It caps the provider's liability at fees paid."}""";

        // NB: apostrophe-free so the substring check is not confused by STJ JSON escaping of "'"
        // (the operand is JSON-serialized into `## Input`; the resolution assertion below is the point).
        const string clause = "The Provider total liability is capped at fees paid in the prior twelve months.";
        var chunks = await h.DispatchAsync(new { selectionText = clause });

        // Rendered frame: a terminal complete chunk, no error (ADR-041 OutcomeCard / render-follows-store).
        chunks.Should().NotContain(c => c.Type == "error", "the args-text operand path runs end-to-end");
        chunks.Should().Contain(c => c.Type == "complete", "the dispatch yields a terminal complete frame");

        // Anti-stub: the RESOLVED operand reached the LLM prompt through the single-source `## Input`
        // producer. If ContextBinder had not resolved selectionText, the clause would be absent.
        h.OpenAi.LastPrompt.Should().Contain("## Input").And.Contain(clause,
            "ContextBinder MUST resolve the declared selectionText arg into the `## Input` operand");

        // Stored SessionOutput (ADR-040): the LLM payload is durably in the ledger, addressable, before render.
        var stored = await h.GetStoredOutputAsync();
        stored.Should().NotBeNull();
        stored!.BindingId.Should().Be(BindingId.ToString());
        stored.Payload.GetProperty("explanation").GetString().Should().Contain("caps the provider's liability");

        // ContextBinder is the fingerprint writer (task-038 dark seam → live; store-before-render).
        var session = await h.Sessions.GetSessionAsync(TenantId, SessionId);
        session!.ContextFingerprints.Should().ContainSingle("ContextBinder writes the ContextEnvelope fingerprint per turn");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (ii) NON-REGRESSION: the shipped file-input summarize path still resolves,
    //      runs, and stores — the file renders under `## Document`, not `## Input`.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_FileInputSummarizePath_IsUnregressed()
    {
        var h = new Harness();
        await h.SeedSessionAsync(BuildSession(fileId: "file-1"));
        h.GivenBinding(inputSchema: SummarizeInputSchema);
        h.GivenJpsAction(SummarizeSystemPromptJps, SummarizeOutputSchema);
        h.GivenSessionFileText("This contract governs the sale of goods between the parties.");
        h.OpenAi.RawJsonToReturn = """{"summary":"A sale-of-goods agreement."}""";

        var chunks = await h.DispatchAsync(new { fileIds = new[] { "file-1" } });

        chunks.Should().NotContain(c => c.Type == "error");
        chunks.Should().Contain(c => c.Type == "complete");

        // Non-regression: the file text renders under `## Document` (the shipped channel), NOT `## Input`.
        h.OpenAi.LastPrompt.Should().Contain("## Document")
            .And.Contain("This contract governs the sale of goods")
            .And.NotContain("## Input", "the file-grounding document stays on the `## Document` channel");

        var stored = await h.GetStoredOutputAsync();
        stored!.Payload.GetProperty("summary").GetString().Should().Be("A sale-of-goods agreement.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (iii) ADR-040 read-by-reference: a ledger_resolution operand resolves a
    //       prior SessionOutput by key and feeds it as the `## Input` operand.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_LedgerResolutionOperand_ResolvesPriorSessionOutputByReference()
    {
        var h = new Harness();
        var priorPayload = JsonSerializer.SerializeToElement(new { clause = "Termination requires 30 days written notice." });
        var priorOutput = new SessionOutput
        {
            Key = SessionLedger.BuildOutputKey("priorbinding", 1),
            BindingId = "priorbinding",
            UcId = "UC-PRIOR",
            Turn = 1,
            Disposition = "informational",
            Payload = priorPayload,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await h.SeedSessionAsync(BuildSession() with { Outputs = new[] { priorOutput } });

        h.GivenBinding(inputSchema: ComposeInputSchema);
        h.GivenFlatTextAction("ROLE: Explain the referenced clause.");
        h.OpenAi.RawJsonToReturn = """{"explanation":"A termination-for-convenience notice clause."}""";

        var chunks = await h.DispatchAsync(new { ledger_resolution = new { key = "priorbinding@t1" } });

        chunks.Should().NotContain(c => c.Type == "error");
        chunks.Should().Contain(c => c.Type == "complete");

        // The prior output's payload became THIS turn's `## Input` operand (ADR-040 read-by-reference).
        h.OpenAi.LastPrompt.Should().Contain("## Input")
            .And.Contain("Termination requires 30 days written notice",
                "ledger_resolution MUST resolve the referenced prior SessionOutput's payload as the operand");

        // A NEW SessionOutput was stored for the dispatching binding at the next turn ordinal.
        var session = await h.Sessions.GetSessionAsync(TenantId, SessionId);
        session!.Outputs.Should().Contain(o => o.BindingId == BindingId.ToString() && o.Turn == 2);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (iv) Golden `## Input`-format assertion — the frozen single-source contract.
    //      Any drift (indentation, key order, header, trailing newline) fails the build,
    //      protecting the DailyBriefingNarrator replica until E-12 retires it.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PromptInputSection_Render_ProducesTheFrozenGoldenFormat()
    {
        var operand = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["selectionText"] = "X" });

        var rendered = PromptInputSection.Render(operand);

        // FROZEN CONTRACT (LF): header, blank line, 2-space-indented JSON, single trailing newline.
        rendered.Should().Be("## Input\n\n{\n  \"selectionText\": \"X\"\n}\n");
    }

    [Fact]
    public async Task DispatchAsync_ArgsTextOperand_RendersTheGoldenInputSectionOnTheRealPath()
    {
        var h = new Harness();
        await h.SeedSessionAsync(BuildSession());
        h.GivenBinding(inputSchema: ComposeInputSchema);
        h.GivenFlatTextAction("ROLE: Explain the selected clause.");
        h.OpenAi.RawJsonToReturn = """{"explanation":"ok"}""";

        await h.DispatchAsync(new { selectionText = "Liability is capped." });

        // The real dispatch path emits the byte-identical frozen `## Input` block (single-source producer).
        h.OpenAi.LastPrompt.Should().Contain(
            "## Input\n\n{\n  \"selectionText\": \"Liability is capped.\"\n}\n");
    }

    // ─── Harness ─────────────────────────────────────────────────────────────

    private static ChatSession BuildSession(string? fileId = null)
    {
        var files = fileId is null
            ? Array.Empty<ChatSessionFile>()
            : new[]
            {
                new ChatSessionFile(
                    FileId: fileId, FileName: $"{fileId}.pdf", ContentType: "application/pdf",
                    SizeBytes: 1024, SearchDocumentIdsCsv: $"doc-{fileId}-1", UploadedAt: DateTimeOffset.UtcNow),
            };

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

    private sealed class Harness
    {
        public ChatSessionManager Sessions { get; }
        public RecordingOpenAiClient OpenAi { get; } = new();
        public Mock<IConsumerRoutingService> Routing { get; } = new();
        public Mock<IScopeResolverService> Scope { get; } = new();
        public Mock<ISessionFileTextSource> TextSource { get; } = new();
        public SessionDispatchOrchestrator Orchestrator { get; }

        public Harness()
        {
            Sessions = new ChatSessionManager(
                new InMemoryTenantCache(),
                Mock.Of<IChatDataverseRepository>(),
                Mock.Of<ILogger<ChatSessionManager>>());

            var renderer = new PromptSchemaRenderer(Mock.Of<ILogger<PromptSchemaRenderer>>());
            var runner = new ActionRunner(OpenAi, renderer, Mock.Of<ILogger<ActionRunner>>());
            var binder = new ContextBinder(Sessions, Mock.Of<ILogger<ContextBinder>>());
            var router = new OutputRouter(Sessions, Mock.Of<ILogger<OutputRouter>>());
            var pending = new PendingPlanManager(
                new InMemoryTenantCache(), Sessions, Mock.Of<ILogger<PendingPlanManager>>());

            Orchestrator = new SessionDispatchOrchestrator(
                Sessions, Routing.Object, Scope.Object, runner, binder, TextSource.Object, router, pending,
                Options.Create(new EventRulesOptions { ReadinessProbeAttempts = 1, ReadinessProbeDelayMs = 0 }),
                new Sprk.Bff.Api.Telemetry.AiTelemetry(),
                Mock.Of<ILogger<SessionDispatchOrchestrator>>());
        }

        public Task SeedSessionAsync(ChatSession session) => Sessions.UpdateSessionCacheAsync(session);

        public void GivenBinding(string? inputSchema) =>
            Routing
                .Setup(c => c.GetBindingByIdAsync(BindingId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Binding
                {
                    BindingId = BindingId,
                    ConsumerType = "seam-test",
                    ActionId = ActionId,
                    ActionKind = ActionKind.Prompted,
                    Disposition = BindingDisposition.Informational,
                    InputSchemaJson = inputSchema,
                });

        public void GivenFlatTextAction(string systemPrompt) =>
            Scope
                .Setup(s => s.GetActionAsync(ActionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AnalysisAction
                {
                    Id = ActionId,
                    Name = "Seam Test Action",
                    SystemPrompt = systemPrompt,
                    OutputSchemaJson = ComposeOutputSchema,
                    Temperature = 0.2m,
                });

        public void GivenJpsAction(string systemPromptJps, string outputSchema) =>
            Scope
                .Setup(s => s.GetActionAsync(ActionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AnalysisAction
                {
                    Id = ActionId,
                    Name = "Seam Summarize Action",
                    SystemPrompt = systemPromptJps,
                    OutputSchemaJson = outputSchema,
                    Temperature = 0.0m,
                });

        public void GivenSessionFileText(string extractedText) =>
            TextSource
                .Setup(t => t.FetchAsync(
                    It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<ChatSessionFile>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SessionFileText
                {
                    ExtractedText = extractedText,
                    DisplayName = "f.pdf",
                    ChunkCount = 1,
                });

        public async Task<IReadOnlyList<AnalysisChunk>> DispatchAsync(object args)
        {
            var argsElement = JsonSerializer.SerializeToElement(args);
            var chunks = new List<AnalysisChunk>();
            await foreach (var chunk in Orchestrator.DispatchAsync(
                new SessionDispatchRequest(TenantId, SessionId, BindingId, argsElement)))
            {
                chunks.Add(chunk);
            }
            return chunks;
        }

        public async Task<SessionOutput?> GetStoredOutputAsync()
        {
            var session = await Sessions.GetSessionAsync(TenantId, SessionId);
            return session?.Outputs?.LastOrDefault(o => o.BindingId == BindingId.ToString());
        }
    }

    /// <summary>
    /// Deterministic recording <see cref="IOpenAiClient"/> — captures the assembled prompt so the tests can
    /// assert the RESOLVED operand actually reached the LLM (the anti-stub guarantee). Only
    /// <see cref="GetStructuredCompletionRawAsync"/> (the executor's boundary) is used; the rest throw.
    /// </summary>
    private sealed class RecordingOpenAiClient : IOpenAiClient
    {
        public string RawJsonToReturn { get; set; } = "{}";
        public string? LastPrompt { get; private set; }

        public Task<string> GetStructuredCompletionRawAsync(
            string prompt, BinaryData jsonSchema, string schemaName, string? model = null,
            int? maxOutputTokens = null, float? temperature = null, CancellationToken cancellationToken = default)
        {
            LastPrompt = prompt;
            return Task.FromResult(RawJsonToReturn);
        }

        public IAsyncEnumerable<string> StreamStructuredCompletionAsync(
            IEnumerable<global::OpenAI.Chat.ChatMessage> messages, BinaryData jsonSchema, string schemaName,
            string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamCompletionAsync(string prompt, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<string> GetCompletionAsync(string prompt, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamVisionCompletionAsync(string prompt, byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<string> GetVisionCompletionAsync(string prompt, byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, string? model = null, int? dimensions = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(IEnumerable<string> texts, string? model = null, int? dimensions = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<ChatCompletionResult> GetChatCompletionWithToolsAsync(IEnumerable<global::OpenAI.Chat.ChatMessage> messages, IEnumerable<global::OpenAI.Chat.ChatTool> tools, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<T> GetStructuredCompletionAsync<T>(IEnumerable<global::OpenAI.Chat.ChatMessage> messages, BinaryData jsonSchema, string schemaName, string deploymentName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
