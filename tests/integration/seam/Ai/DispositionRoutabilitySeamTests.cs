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
/// ADR-043 (Move 2 / E-20) vertical-slice seam tests — the definition-of-done for the
/// <see cref="DispositionRoutability"/> single-source registry. Disposition capability used to be
/// triplicated across three hand-maintained lists (the dispatch admit-gate, the
/// <see cref="OutputRouter"/> switch, and <c>ToLedgerValue</c>) that drifted — the exact defect that
/// half-landed compose's routing promotion (router case added, admit-gate un-widened → a live 422).
/// These tests prove the three collapse to ONE registry: a disposition registered ONCE
/// (<see cref="DispositionRoutability"/>) dispatches end-to-end (admit → route → store → render) with
/// NO three-list edit, and the admit-gate can never disagree with the router.
/// </summary>
/// <remarks>
/// <para>
/// <b>Real path, not mocked</b>: <see cref="SessionDispatchOrchestrator"/>, <see cref="ContextBinder"/>,
/// <see cref="ActionRunner"/>, <see cref="OutputRouter"/>, and <see cref="ChatSessionManager"/> (over
/// the in-memory tenant cache with production serialization) are the PRODUCTION types. Only the LLM
/// boundary (a recording <see cref="IOpenAiClient"/>) and the catalog data boundaries are doubled.
/// Mocking the router/orchestrator would defeat the category (a contract-shape test is NOT sufficient
/// — ADR-043 governance; that is how the compose 422 shipped "done").
/// </para>
/// </remarks>
public sealed class DispositionRoutabilitySeamTests
{
    private const string TenantId = "00000000-0000-0000-0000-0000000000bb";
    private const string SessionId = "44444444-4444-4444-4444-444444444444";
    private static readonly Guid BindingId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ActionId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    // A structured-operand (selectionText) input schema so dispatch takes the args-text branch — no
    // session files required. Same shape the ContextBinder+ActionRunner seam suite uses.
    private const string SelectionInputSchema =
        """{"type":"object","required":["selectionText"],"properties":{"selectionText":{"type":"string"}}}""";

    private const string PassThroughOutputSchema =
        """{"type":"object","additionalProperties":false,"required":["new_text"],"properties":{"new_text":{"type":"string"}}}""";

    // ─────────────────────────────────────────────────────────────────────────
    // (1) REGISTER-ONCE vertical slice + compose-stays-green. A compose-disposition
    //     binding dispatches end-to-end: admit (previously 422'd here) → route
    //     (pass-through) → store (ledger "compose") → render (terminal frame). The
    //     ONLY thing that makes this pass is the ONE DispositionRoutability entry —
    //     admit-gate, ToLedgerValue, and the router all read it.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_ComposeDisposition_Admits_Routes_Stores_AndRenders()
    {
        var h = new Harness();
        await h.SeedSessionAsync(BuildSession());
        h.GivenBinding(BindingDisposition.Compose, SelectionInputSchema);
        h.GivenFlatTextAction("ROLE: Propose a redline for the selected clause.", PassThroughOutputSchema);
        h.OpenAi.RawJsonToReturn = """{"new_text":"sixty (60) days' written notice"}""";

        var chunks = await h.DispatchAsync(new { selectionText = "thirty (30) days notice" });

        // ADMIT: the compose disposition was admitted (before single-sourcing this 422'd at the
        // admit-gate — routable-but-unadmitted). RENDER: a terminal complete frame, no error.
        chunks.Should().NotContain(c => c.Type == "error",
            "compose is routable ⇒ admissible (ADR-043 §3) — the admit-gate no longer rejects it");
        chunks.Should().Contain(c => c.Type == "complete", "the dispatch yields a terminal complete frame");

        // STORE: the compose SessionOutput is durably in the ledger BEFORE render, with the ledger
        // vocabulary member the registry maps (ToLedgerValue derives from the SAME registry).
        var stored = await h.GetStoredOutputAsync();
        stored.Should().NotBeNull("store precedes render — ADR-040");
        stored!.Disposition.Should().Be(DispositionRoutability.ToLedgerValue(BindingDisposition.Compose))
            .And.Be(ComposeDisposition.DispositionValue);
        stored.Payload.GetProperty("new_text").GetString().Should().Be("sixty (60) days' written notice");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (2) LOUD rejection through the registry — a not-yet-routable disposition is
    //     rejected PRE-RUN at the admit-gate (never a silent drop, never
    //     admittable-but-unroutable). No SessionOutput is stored (rejected before run).
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BindingDisposition.Overlay)]
    [InlineData(BindingDisposition.Record)]
    [InlineData(BindingDisposition.Notification)]
    public async Task DispatchAsync_NotYetRoutableDisposition_RejectedPreRun_NeverSilentDrop(
        BindingDisposition disposition)
    {
        var h = new Harness();
        await h.SeedSessionAsync(BuildSession());
        h.GivenBinding(disposition, SelectionInputSchema);
        h.GivenFlatTextAction("ROLE: unused — rejected before the LLM.", PassThroughOutputSchema);

        Func<Task> act = () => h.DispatchToCompletionAsync(new { selectionText = "x" });

        var ex = await act.Should().ThrowAsync<DispatchRejectedException>(
            "a disposition the registry marks not-routable is rejected LOUDLY, never silently dropped");
        ex.Which.ErrorCode.Should().Be(DispatchRejectedException.DispositionUnsupported);
        ex.Which.StatusCode.Should().Be(422);

        // Rejected PRE-RUN: no LLM call, no ledger write.
        h.OpenAi.LastPrompt.Should().BeNull("the disposition gate rejects BEFORE the LLM spend");
        (await h.GetStoredOutputAsync()).Should().BeNull("no output is stored for a rejected dispatch");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (3) Non-regression: an informational dispatch still admits/routes/stores, and
    //     the router's stored ledger value derives from the registry (ties
    //     ToLedgerValue to the real router path — no separate mapping list).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_InformationalDisposition_Stores_LedgerValueDerivedFromRegistry()
    {
        var h = new Harness();
        await h.SeedSessionAsync(BuildSession());
        h.GivenBinding(BindingDisposition.Informational, SelectionInputSchema);
        h.GivenFlatTextAction("ROLE: Explain the clause.", PassThroughOutputSchema);
        h.OpenAi.RawJsonToReturn = """{"new_text":"An explanation."}""";

        var chunks = await h.DispatchAsync(new { selectionText = "a clause" });

        chunks.Should().Contain(c => c.Type == "complete").And.NotContain(c => c.Type == "error");
        var stored = await h.GetStoredOutputAsync();
        stored!.Disposition.Should().Be(DispositionRoutability.ToLedgerValue(BindingDisposition.Informational))
            .And.Be("informational");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (4) STRUCTURAL invariant — the three-list divergence is impossible by
    //     construction: admission IS routability for EVERY disposition, and every
    //     disposition has a stable ledger value. This is the "cannot disagree" proof.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Registry_AdmissionEqualsRoutability_ForEveryDisposition_NoThreeListDivergencePossible()
    {
        foreach (var disposition in DispositionRoutability.All)
        {
            DispositionRoutability.IsAdmissible(disposition)
                .Should().Be(DispositionRoutability.IsRoutable(disposition),
                    $"admission derives from routability (ADR-043 §3) — '{disposition}' must not be " +
                    "admittable-but-unroutable or routable-but-unadmitted");

            DispositionRoutability.ToLedgerValue(disposition)
                .Should().NotBeNullOrWhiteSpace($"'{disposition}' must map to a ledger vocabulary member (store precedes routing)");
        }
    }

    [Fact]
    public void Registry_RoutableSet_IsExactlyTheRealizedLegs()
    {
        var routable = DispositionRoutability.All
            .Where(DispositionRoutability.IsRoutable)
            .ToHashSet();

        routable.Should().BeEquivalentTo(new[]
        {
            BindingDisposition.Informational,
            BindingDisposition.WorkProduct,
            BindingDisposition.Email,
            BindingDisposition.Compose,
        }, "these four legs are realized in OutputRouter; overlay/record/notification are registered " +
           "as loud not-yet-routable (they need a new side-effect mechanism — out of E-20 scope)");
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
        UploadedFiles: Array.Empty<ChatSessionFile>());

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

        public void GivenBinding(BindingDisposition disposition, string inputSchema) =>
            Routing
                .Setup(c => c.GetBindingByIdAsync(BindingId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Binding
                {
                    BindingId = BindingId,
                    ConsumerType = "disposition-seam-test",
                    ActionId = ActionId,
                    ActionKind = ActionKind.Prompted,
                    Disposition = disposition,
                    InputSchemaJson = inputSchema,
                });

        public void GivenFlatTextAction(string systemPrompt, string outputSchema) =>
            Scope
                .Setup(s => s.GetActionAsync(ActionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AnalysisAction
                {
                    Id = ActionId,
                    Name = "Disposition Seam Action",
                    SystemPrompt = systemPrompt,
                    OutputSchemaJson = outputSchema,
                    Temperature = 0.2m,
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

        /// <summary>Drives the dispatch to completion (or the pre-stream throw) discarding chunks — for the rejection assertions.</summary>
        public async Task DispatchToCompletionAsync(object args)
        {
            var argsElement = JsonSerializer.SerializeToElement(args);
            await foreach (var _ in Orchestrator.DispatchAsync(
                new SessionDispatchRequest(TenantId, SessionId, BindingId, argsElement)))
            {
            }
        }

        public async Task<SessionOutput?> GetStoredOutputAsync()
        {
            var session = await Sessions.GetSessionAsync(TenantId, SessionId);
            return session?.Outputs?.LastOrDefault(o => o.BindingId == BindingId.ToString());
        }
    }

    /// <summary>
    /// Deterministic recording <see cref="IOpenAiClient"/> — captures the assembled prompt so a test can
    /// assert the LLM was (or was NOT) called. Only <see cref="GetStructuredCompletionRawAsync"/> (the
    /// executor's boundary) is used; the rest throw.
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
