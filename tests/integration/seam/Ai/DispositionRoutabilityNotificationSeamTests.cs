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
/// FR-14 (spaarke-notification-spine-r1 task 033) vertical-slice seam tests — the definition-of-done for
/// realizing the <see cref="BindingDisposition.Notification"/> routing leg through the ADR-043
/// <see cref="DispositionRoutability"/> registry. Proves the admit⇔route⇔store invariant the registry's
/// own doc comments describe as the contract for EVERY registered disposition, now that Notification was
/// flipped <c>Routable=false→true</c> and its <see cref="OutputRouter"/> leg added in the same change.
/// </summary>
/// <remarks>
/// <para>
/// <b>Real path, not mocked</b>: <see cref="SessionDispatchOrchestrator"/>, <see cref="ContextBinder"/>,
/// <see cref="ActionRunner"/>, <see cref="OutputRouter"/>, and <see cref="ChatSessionManager"/> (over the
/// in-memory tenant cache with production serialization) are the PRODUCTION types. Only the LLM boundary
/// (a recording <see cref="IOpenAiClient"/>), the catalog data boundaries, and the Layer-A
/// <see cref="IActionSeam"/> (the appnotification WRITE boundary — a Dataverse side effect the harness
/// legitimately doubles, exactly as <c>CodedWorkflowDispatchSeamTests</c> doubles
/// <see cref="IEmailDispositionSender"/>) are doubled. A router-unit mock would defeat the category
/// (ADR-043 governance — that is how the compose 422 shipped "done").
/// </para>
/// <para>
/// The <see cref="IActionSeam"/> double mirrors the real <c>ActionSeam.CreateNotificationAsync</c>
/// content-validation (title/body/recipientId required → typed failure) so the malformed-payload test
/// exercises the router's real <c>!Success → throw</c> loud-failure path.
/// </para>
/// </remarks>
public sealed class DispositionRoutabilityNotificationSeamTests
{
    private const string TenantId = "00000000-0000-0000-0000-0000000000dd";
    private const string SessionId = "22222222-2222-2222-2222-222222222222";
    private static readonly Guid BindingId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ActionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid RecipientId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid CreatedNotificationId = Guid.Parse("12121212-3434-5656-7878-909090909090");

    private const string SelectionInputSchema =
        """{"type":"object","required":["selectionText"],"properties":{"selectionText":{"type":"string"}}}""";

    // Permissive output schema: the capability's routed payload carries a `notification` envelope the
    // router parses (no `required`, so the missing-envelope test's payload also validates).
    private const string NotificationOutputSchema =
        """{"type":"object","additionalProperties":true,"properties":{"notification":{"type":"object"}}}""";

    // ─────────────────────────────────────────────────────────────────────────
    // (1) The admit⇔route⇔store invariant for Notification. A notification-disposition binding
    //     dispatches end-to-end: admit (would 422 before task 033 — the exact gap Compose/SurfaceLaunch
    //     hit) → route (creates an appnotification via IActionSeam.CreateNotificationAsync) → store
    //     (ledger "notification"). The ONLY thing that makes admit pass is the ONE DispositionRoutability
    //     entry now marked Routable=true.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_NotificationDisposition_Admits_Routes_Stores_CreatesAppnotification()
    {
        var h = new Harness();
        await h.SeedSessionAsync();
        h.GivenBinding(BindingDisposition.Notification, SelectionInputSchema);
        h.GivenFlatTextAction("ROLE: Draft the notification.", NotificationOutputSchema);
        h.OpenAi.RawJsonToReturn =
            "{\"notification\":{\"title\":\"Document ready\",\"body\":\"Your NDA summary is ready to review.\"," +
            "\"recipientId\":\"" + RecipientId + "\",\"category\":\"chat-notification\",\"actionUrl\":\"/main.aspx?id=1\"," +
            "\"priority\":300000000,\"toastType\":200000000,\"regardingId\":\"" + ActionId + "\",\"regardingType\":\"sprk_matter\"}}";

        var chunks = await h.DispatchAsync(new { selectionText = "notify me when the summary is ready" });

        // ADMIT: notification is routable ⇒ admissible (ADR-043 §3) — the admit-gate no longer 422s.
        // RENDER: a terminal complete frame, no error.
        chunks.Should().NotContain(c => c.Type == "error",
            "notification is routable ⇒ admissible (ADR-043 §3) after the FR-14 flip — the admit-gate admits it");
        chunks.Should().Contain(c => c.Type == "complete", "the dispatch yields a terminal complete frame");

        // STORE: the notification SessionOutput is durably in the ledger BEFORE the side effect, with the
        // ledger vocabulary member the registry maps (ToLedgerValue derives from the SAME registry).
        var stored = await h.GetStoredOutputAsync();
        stored.Should().NotBeNull("store precedes render — ADR-040");
        stored!.Disposition.Should().Be(DispositionRoutability.ToLedgerValue(BindingDisposition.Notification))
            .And.Be("notification");

        // ROUTE: the Layer-A seam created exactly one appnotification, with the fields parsed from the
        // stored payload's `notification` envelope + the dispatch/router provenance.
        h.ActionSeam.Verify(
            s => s.CreateNotificationAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        h.Captured.Should().NotBeNull();
        h.Captured!.Title.Should().Be("Document ready");
        h.Captured.Body.Should().Be("Your NDA summary is ready to review.");
        h.Captured.RecipientId.Should().Be(RecipientId);
        h.Captured.Category.Should().Be("chat-notification");
        h.Captured.ActionUrl.Should().Be("/main.aspx?id=1");
        h.Captured.Priority.Should().Be(300000000);
        h.Captured.ToastType.Should().Be(200000000);
        h.Captured.RegardingId.Should().Be(ActionId);
        h.Captured.RegardingType.Should().Be("sprk_matter");
        h.Captured.Source.Should().Be("dispatch", "a dispatch/router-originated notification is not 'playbook'");
        h.Captured.CorrelationId.Should().Be(stored.Key, "the router correlates the appnotification to the ledger key");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (2) Loud failure AFTER the ledger store when the seam rejects the content (missing recipientId) —
    //     mirrors the Email leg's failure contract (store precedes render; a delivery/creation failure
    //     propagates loudly, never a silent skip).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_NotificationDisposition_SeamRejectsContent_ThrowsLoud_AfterLedgerStore()
    {
        var h = new Harness();
        await h.SeedSessionAsync();
        h.GivenBinding(BindingDisposition.Notification, SelectionInputSchema);
        h.GivenFlatTextAction("ROLE: Draft the notification.", NotificationOutputSchema);
        // A `notification` envelope MISSING the required recipientId — the seam's content validation fails.
        h.OpenAi.RawJsonToReturn = """{"notification":{"title":"No recipient","body":"orphaned"}}""";

        Func<Task> act = () => h.DispatchToCompletionAsync(new { selectionText = "x" });

        (await act.Should().ThrowAsync<InvalidOperationException>(
                "a seam-rejected notification fails LOUDLY after the ledger write — never a silent skip"))
            .WithMessage("*notification creation failed*");

        // STORE PRECEDED THE THROW (ADR-040): the ledger entry is durably present even though the side
        // effect failed — the entry stays addressable.
        var stored = await h.GetStoredOutputAsync();
        stored.Should().NotBeNull("the ledger write precedes the routing leg — the entry survives a leg failure");
        stored!.Disposition.Should().Be("notification");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (3) Loud failure when the routed payload carries NO `notification` envelope — the router's own
    //     STRUCTURE validation (mirrors DeliverEmailAsync's missing-`email`-envelope check).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_NotificationDisposition_MissingEnvelope_ThrowsLoud_AfterLedgerStore()
    {
        var h = new Harness();
        await h.SeedSessionAsync();
        h.GivenBinding(BindingDisposition.Notification, SelectionInputSchema);
        h.GivenFlatTextAction("ROLE: Draft the notification.", NotificationOutputSchema);
        // No `notification` object at all — the router rejects the structure before the seam is called.
        h.OpenAi.RawJsonToReturn = """{"somethingElse":"not a notification envelope"}""";

        Func<Task> act = () => h.DispatchToCompletionAsync(new { selectionText = "x" });

        (await act.Should().ThrowAsync<InvalidOperationException>(
                "a payload with no 'notification' envelope fails LOUDLY — the router validates the structure"))
            .WithMessage("*notification*envelope*");

        h.ActionSeam.Verify(
            s => s.CreateNotificationAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()),
            Times.Never, "the router rejects a structurally-invalid payload before ever calling the seam");
        (await h.GetStoredOutputAsync()).Should().NotBeNull("the ledger write precedes the routing leg (ADR-040)");
    }

    // ─── Harness ─────────────────────────────────────────────────────────────

    private sealed class Harness
    {
        public ChatSessionManager Sessions { get; }
        public RecordingOpenAiClient OpenAi { get; } = new();
        public Mock<IConsumerRoutingService> Routing { get; } = new();
        public Mock<IScopeResolverService> Scope { get; } = new();
        public Mock<ISessionFileTextSource> TextSource { get; } = new();
        public Mock<IActionSeam> ActionSeam { get; } = new();
        public CreateNotificationRequest? Captured { get; private set; }
        public SessionDispatchOrchestrator Orchestrator { get; }

        public Harness()
        {
            Sessions = new ChatSessionManager(
                new InMemoryTenantCache(),
                Mock.Of<IChatDataverseRepository>(),
                Mock.Of<ILogger<ChatSessionManager>>());

            // Mirror the real ActionSeam.CreateNotificationAsync content-validation so the router's
            // !Success → throw path is exercised faithfully; capture the request for the happy-path asserts.
            ActionSeam
                .Setup(s => s.CreateNotificationAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CreateNotificationRequest req, CancellationToken _) =>
                {
                    Captured = req;
                    if (req.RecipientId is null || req.RecipientId == Guid.Empty)
                        return new CreateNotificationResult(false, null, false, "recipientId is required");
                    if (string.IsNullOrWhiteSpace(req.Title))
                        return new CreateNotificationResult(false, null, false, "title is required");
                    if (string.IsNullOrWhiteSpace(req.Body))
                        return new CreateNotificationResult(false, null, false, "body is required");
                    return new CreateNotificationResult(true, CreatedNotificationId, false, null);
                });

            var renderer = new PromptSchemaRenderer(Mock.Of<ILogger<PromptSchemaRenderer>>());
            var runner = new ActionRunner(OpenAi, renderer, Mock.Of<ILogger<ActionRunner>>());
            var binder = new ContextBinder(Sessions, Mock.Of<ILogger<ContextBinder>>());
            var router = new OutputRouter(Sessions, Mock.Of<ILogger<OutputRouter>>(), actionSeam: ActionSeam.Object);
            var pending = new PendingPlanManager(
                new InMemoryTenantCache(), Sessions, Mock.Of<ILogger<PendingPlanManager>>());

            Orchestrator = new SessionDispatchOrchestrator(
                Sessions, Routing.Object, Scope.Object, runner, binder,
                Mock.Of<ICodedWorkflowRegistry>(), TextSource.Object, router, pending,
                Options.Create(new EventRulesOptions { ReadinessProbeAttempts = 1, ReadinessProbeDelayMs = 0 }),
                new Sprk.Bff.Api.Telemetry.AiTelemetry(),
                Mock.Of<ILogger<SessionDispatchOrchestrator>>());
        }

        public Task SeedSessionAsync() => Sessions.UpdateSessionCacheAsync(new ChatSession(
            SessionId: SessionId,
            TenantId: TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: Array.Empty<ChatSessionFile>()));

        public void GivenBinding(BindingDisposition disposition, string inputSchema) =>
            Routing
                .Setup(c => c.GetBindingByIdAsync(BindingId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Binding
                {
                    BindingId = BindingId,
                    ConsumerType = "notification-seam-test",
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
                    Name = "Notification Seam Action",
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
    /// Deterministic recording <see cref="IOpenAiClient"/> — returns a canned raw JSON payload (the
    /// capability's routed output carrying the `notification` envelope). Only the executor boundary
    /// (<see cref="GetStructuredCompletionRawAsync"/>) is used; the rest throw.
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
