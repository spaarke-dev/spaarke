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
/// ADR-043 vertical-slice seam tests — the definition-of-done for
/// <c>ai-advanced-capabilities-nda-r1</c> task 023 (whole-document NDA review orchestration).
/// PROVES the ONE architectural claim of that task: a SINGLE NDA-REVIEW run yields a ledgered
/// <c>{overallRisk, flaggedSections[]}</c> result (ADR-040 store-before-render) from which BOTH
/// client dispositions derive — the concise cited SUMMARY payload (task 030 review panel /
/// Assistant) and the advisory-COMMENTS payload (task 031 client event) — with NO second LLM call.
/// </summary>
/// <remarks>
/// <para>
/// <b>Design decision proven here — single prompted Action + client-side disposition fan-out (NOT a
/// coded workflow).</b> The existing generic dispatch spine already expresses the fan-out: it runs a
/// prompted Action once (<see cref="ActionRunner"/>), writes the universal ledger entry BEFORE any
/// render (<see cref="OutputRouter"/> — ADR-040), and emits the full structured payload VERBATIM on
/// the terminal <c>complete</c> chunk (<c>SessionDispatchOrchestrator.BuildResultChunk</c> → the
/// <see cref="AnalysisChunk.CompletedRaw"/> pass-through, which explicitly preserves the
/// <c>overallRisk</c>/<c>flaggedSections</c> shape). NDA-REVIEW is a SINGLE capability with TWO client
/// views, not an ADR-037 composite (which composes N Action outputs) — so ADR-039's "author composites
/// as coded workflows" does not fire, and §11 reuse-first forbids adding a <c>NdaReviewWorkflow</c>
/// when the existing spine already carries the one-run→two-payloads contract. The advisory-comments
/// payload is a CLIENT-DERIVED projection of the same ledger entry (ADR-040 derived-views pattern),
/// materialized by a lightweight client event (task 031) — deliberately NOT a new routable server
/// disposition (the recommended Binding disposition is <c>informational</c>: read-only advisory).
/// </para>
/// <para>
/// <b>Real path, not mocked</b>: <see cref="SessionDispatchOrchestrator"/>, <see cref="ContextBinder"/>,
/// <see cref="ActionRunner"/>, <see cref="OutputRouter"/>, and <see cref="ChatSessionManager"/> (over
/// the in-memory tenant cache with production serialization) are the PRODUCTION types. Only the LLM
/// boundary (a call-counting <see cref="IOpenAiClient"/>) and the catalog data boundaries are doubled.
/// Mocking the router/orchestrator would defeat the category — a contract-shape test is NOT sufficient
/// (ADR-043 governance).
/// </para>
/// </remarks>
public sealed class NdaReviewFanOutSeamTests
{
    private const string TenantId = "00000000-0000-0000-0000-0000000000cc";
    private const string SessionId = "77777777-7777-7777-7777-777777777777";
    private static readonly Guid BindingId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid ActionId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    // NDA-REVIEW takes the WHOLE document as a structured operand (documentText) — the args-text branch,
    // no session files. This is the task-020 input surface (documentText, not selectionText/single-clause).
    private const string DocumentInputSchema =
        """{"type":"object","required":["documentText"],"properties":{"documentText":{"type":"string"}}}""";

    // The task-020 closed output contract: {overallRisk, flaggedSections[{sectionRef, quotedText,
    // riskLevel, explanation, standardRef}]}. The recording client ignores the schema; it is carried here
    // for fidelity to the real Action row.
    private const string NdaReviewOutputSchema =
        """
        {"type":"object","additionalProperties":false,"required":["overallRisk","flaggedSections"],
         "properties":{
           "overallRisk":{"type":"string","enum":["Low","Medium","High","Critical"]},
           "flaggedSections":{"type":"array","items":{"type":"object","additionalProperties":false,
             "required":["sectionRef","quotedText","riskLevel","explanation","standardRef"],
             "properties":{
               "sectionRef":{"type":"string"},"quotedText":{"type":"string"},
               "riskLevel":{"type":"string","enum":["Low","Medium","High","Critical"]},
               "explanation":{"type":"string"},"standardRef":{"type":"string"}}}}}}
        """;

    // A representative advisory review result: one High finding (the attorney-review machine signal) and
    // one Medium finding, each fully grounded (sectionRef + quotedText + standardRef).
    private const string NdaReviewLlmOutput =
        """
        {"overallRisk":"High",
         "flaggedSections":[
           {"sectionRef":"Section 3.1, para 1 (p. 2)",
            "quotedText":"Confidential Information means information marked 'Confidential'.",
            "riskLevel":"High",
            "explanation":"The clause defines Confidential Information only as marked information (grounded fact); this is materially narrower than the standard, which risks losing protection for oral and unmarked disclosures (advisory judgment).",
            "standardRef":"B3 - Definition of Confidential Information"},
           {"sectionRef":"Section 7 (p. 4)",
            "quotedText":"This Agreement shall remain in effect for one (1) year.",
            "riskLevel":"Medium",
            "explanation":"The clause sets a single one-year term (grounded fact); the standard distinguishes a disclosure window from a longer confidentiality survival period, so sensitive information may lose protection too soon (advisory judgment).",
            "standardRef":"B8 - Term & confidentiality period"}]}
        """;

    // ─────────────────────────────────────────────────────────────────────────
    // (1) THE task-023 DoD: one NDA-REVIEW run → ONE ledgered result → BOTH payloads
    //     (summary + comments) derive from it, ledgered BEFORE render, NO second LLM call.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispatch_NdaReview_OneRun_LedgersOnce_BothPayloadsDeriveFromStoredResult_NoSecondLlmCall()
    {
        var h = new Harness();
        await h.SeedSessionAsync(BuildSession());
        // Recommended Binding shape for task 022: read-only advisory ⇒ informational disposition
        // (NOT compose/edit; NOT overlay/not-yet-routable). Comments are a client-derived view, not a
        // second server disposition.
        h.GivenBinding(BindingDisposition.Informational, DocumentInputSchema, consumerType: "nda-review");
        h.GivenNdaReviewAction();
        h.OpenAi.RawJsonToReturn = NdaReviewLlmOutput;

        var chunks = await h.DispatchAsync(new { documentText = "MUTUAL NON-DISCLOSURE AGREEMENT ... (full NDA text)" });

        // NO error; a terminal complete frame.
        chunks.Should().NotContain(c => c.Type == "error");
        var complete = chunks.Should().ContainSingle(c => c.Type == "complete").Subject;

        // ── ONE RUN, ONE LEDGER WRITE (both payloads from one result — the core project constraint):
        //    the LLM was called EXACTLY once. The summary and comments payloads are NOT two model calls.
        h.OpenAi.CallCount.Should().Be(1,
            "both the summary and the advisory-comments payloads derive from the ONE ledgered result — " +
            "there is no second LLM call to produce comments vs summary (project constraint / ADR-040)");

        // ── STORE-BEFORE-RENDER (ADR-040): the {overallRisk, flaggedSections[]} result is durably in the
        //    ledger, addressable by {bindingId}@t{n}, and the terminal chunk RENDERS FROM the stored entry.
        var stored = await h.GetStoredOutputAsync();
        stored.Should().NotBeNull("store precedes render — ADR-040");
        stored!.Key.Should().Be($"{BindingId}@t1", "the result is addressable (ADR-040 {bindingId}@t{n})");
        stored.Disposition.Should().Be("informational", "read-only advisory review — the single server disposition");

        // The terminal chunk's payload IS the stored payload (render-follows-store, enforced in production
        // by ProgressiveRenderGuard.EnsureStored → render from storedEntry.Payload).
        var summaryPayload = complete.Result.Should().BeOfType<JsonElement>().Subject;
        JsonSerializer.Serialize(summaryPayload).Should().Be(JsonSerializer.Serialize(stored.Payload),
            "the summary payload the client renders is the STORED ledger entry, not pre-store state");

        // ── (a) SUMMARY payload (task 030 review panel / Assistant) — derives from the terminal chunk:
        //    the full {overallRisk, flaggedSections[]} survives verbatim on the wire.
        summaryPayload.GetProperty("overallRisk").GetString().Should().Be("High");
        summaryPayload.GetProperty("flaggedSections").GetArrayLength().Should().Be(2);
        complete.Disposition.Should().Be("informational");
        complete.ConsumerType.Should().Be("nda-review",
            "the terminal chunk carries the Binding consumer-type so the client routes BOTH views");

        // ── (b) COMMENTS payload (task 031 client event) — derives from the SAME stored ledger entry:
        //    each flaggedSection carries the five citation fields the client materializes as one comment.
        var flagged = stored.Payload.GetProperty("flaggedSections");
        flagged.GetArrayLength().Should().Be(2, "one advisory comment per grounded finding");
        var first = flagged[0];
        first.GetProperty("sectionRef").GetString().Should().Be("Section 3.1, para 1 (p. 2)");
        first.GetProperty("quotedText").GetString().Should().Contain("marked 'Confidential'");
        first.GetProperty("riskLevel").GetString().Should().Be("High");
        first.GetProperty("standardRef").GetString().Should().Be("B3 - Definition of Confidential Information");
        first.GetProperty("explanation").GetString().Should().NotBeNullOrWhiteSpace();

        // ── BOTH VIEWS, ONE SOURCE: the comments payload and the summary payload are byte-identical
        //    projections of the SAME stored flaggedSections[] — the fan-out is client-side derivation of
        //    one ledgered result, never divergent server outputs.
        JsonSerializer.Serialize(summaryPayload.GetProperty("flaggedSections"))
            .Should().Be(JsonSerializer.Serialize(flagged),
                "summary panel (030) and advisory comments (031) derive from the SAME ledgered flaggedSections[]");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (2) The attorney-review machine signal survives the ledger round-trip: a High/Critical finding
    //     stored under flaggedSections[].riskLevel is exactly what task 022's classification + task 030's
    //     panel read. This is the grounded advisory contract (ADR-039 amendment — advisory mode) landing
    //     in the store unchanged.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispatch_NdaReview_HighAndCriticalFindings_SurviveLedger_AsAttorneyReviewSignal()
    {
        var h = new Harness();
        await h.SeedSessionAsync(BuildSession());
        h.GivenBinding(BindingDisposition.Informational, DocumentInputSchema, consumerType: "nda-review");
        h.GivenNdaReviewAction();
        h.OpenAi.RawJsonToReturn =
            """
            {"overallRisk":"Critical",
             "flaggedSections":[
               {"sectionRef":"Section 11 (p. 6)","quotedText":"Recipient shall not solicit any employee for five (5) years.",
                "riskLevel":"Critical","explanation":"A hidden five-year non-solicit (grounded fact) is a restrictive covenant the standard says an NDA must not smuggle in (advisory judgment).",
                "standardRef":"B11 - Restrictive covenants"}]}
            """;

        var chunks = await h.DispatchAsync(new { documentText = "... NDA with a hidden non-solicit ..." });

        chunks.Should().NotContain(c => c.Type == "error").And.ContainSingle(c => c.Type == "complete");
        h.OpenAi.CallCount.Should().Be(1);

        var stored = await h.GetStoredOutputAsync();
        stored!.Payload.GetProperty("overallRisk").GetString().Should().Be("Critical",
            "overallRisk High/Critical is the machine signal that routes the NDA to counsel");
        stored.Payload.GetProperty("flaggedSections")[0].GetProperty("riskLevel").GetString()
            .Should().Be("Critical", "the per-finding attorney-review signal survives the ledger round-trip");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (3) A clean NDA (empty flaggedSections, Low overall) still ledgers-once and renders — the
    //     no-findings path is not a special case: the same one-run→ledger→both-views contract holds.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispatch_NdaReview_CleanNda_EmptyFindings_StillLedgersOnce_AndRenders()
    {
        var h = new Harness();
        await h.SeedSessionAsync(BuildSession());
        h.GivenBinding(BindingDisposition.Informational, DocumentInputSchema, consumerType: "nda-review");
        h.GivenNdaReviewAction();
        h.OpenAi.RawJsonToReturn = """{"overallRisk":"Low","flaggedSections":[]}""";

        var chunks = await h.DispatchAsync(new { documentText = "A clean, standard mutual NDA." });

        chunks.Should().NotContain(c => c.Type == "error").And.ContainSingle(c => c.Type == "complete");
        h.OpenAi.CallCount.Should().Be(1);

        var stored = await h.GetStoredOutputAsync();
        stored!.Payload.GetProperty("overallRisk").GetString().Should().Be("Low");
        stored.Payload.GetProperty("flaggedSections").GetArrayLength().Should().Be(0,
            "a clean NDA yields an empty comments payload AND an empty summary findings list — one ledgered result, both views empty");
    }

    // ─── Harness ─────────────────────────────────────────────────────────────

    private static ChatSession BuildSession() => new(
        SessionId: SessionId,
        TenantId: TenantId,
        DocumentId: null,
        PlaybookId: null,
        CreatedAt: DateTimeOffset.UtcNow,
        LastActivity: DateTimeOffset.UtcNow,
        Messages: Array.Empty<ChatMessage>(),
        HostContext: null,
        AdditionalDocumentIds: null,
        UploadedFiles: Array.Empty<ChatSessionFile>()) { OwnerOid = TestSessionOwner.Oid };

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
                Sessions, Routing.Object, Scope.Object, runner, binder,
                Mock.Of<Sprk.Bff.Api.Services.Ai.ICodedWorkflowRegistry>(), TextSource.Object, router, pending,
                Options.Create(new EventRulesOptions { ReadinessProbeAttempts = 1, ReadinessProbeDelayMs = 0 }),
                new Sprk.Bff.Api.Telemetry.AiTelemetry(),
                Mock.Of<ILogger<SessionDispatchOrchestrator>>());
        }

        public Task SeedSessionAsync(ChatSession session) => Sessions.UpdateSessionCacheAsync(session);

        public void GivenBinding(
            BindingDisposition disposition,
            string inputSchema,
            string consumerType) =>
            Routing
                .Setup(c => c.GetBindingByIdAsync(BindingId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Binding
                {
                    BindingId = BindingId,
                    ConsumerType = consumerType,
                    Ucid = null,
                    ActionId = ActionId,
                    ActionKind = ActionKind.Prompted,
                    Disposition = disposition,
                    InputSchemaJson = inputSchema,
                    Risk = BindingRisk.None,
                });

        public void GivenNdaReviewAction() =>
            Scope
                .Setup(s => s.GetActionAsync(ActionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AnalysisAction
                {
                    Id = ActionId,
                    Name = "NDA Review",
                    SystemPrompt = "ROLE: You are the Spaarke NDA Review advisor. Review the whole NDA and emit {overallRisk, flaggedSections[]}.",
                    OutputSchemaJson = NdaReviewOutputSchema,
                    Temperature = 0.3m,
                    ModelTier = AiModelTier.Reasoning,
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
    /// Deterministic, CALL-COUNTING <see cref="IOpenAiClient"/> — <see cref="CallCount"/> is the proof
    /// that both payloads derive from ONE model call (no second call for comments vs summary). Only
    /// <see cref="GetStructuredCompletionRawAsync"/> (the prompted executor's boundary) is used.
    /// </summary>
    private sealed class RecordingOpenAiClient : IOpenAiClient
    {
        public string RawJsonToReturn { get; set; } = "{}";
        public string? LastPrompt { get; private set; }
        public int CallCount { get; private set; }

        public Task<string> GetStructuredCompletionRawAsync(
            string prompt, BinaryData jsonSchema, string schemaName, string? model = null,
            int? maxOutputTokens = null, float? temperature = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
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
