using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
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

    // ─────────────────────────────────────────────────────────────────────────
    // (v) ai-advanced-capabilities-nda-r1 task 010 — model-tier last-mile. Proves
    //     ActionRunner no longer hardcodes model:null: the Action's sprk_modeltier
    //     resolves to the CONFIGURED deployment for that tier via
    //     ModelTierDeploymentResolver, and an unspecified tier defaults to Standard.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_ActionWithReasoningTier_ResolvesToConfiguredReasoningDeployment()
    {
        var h = new Harness();
        await h.SeedSessionAsync(BuildSession());
        h.GivenBinding(inputSchema: ComposeInputSchema);
        h.GivenFlatTextAction("ROLE: Explain the selected clause.", tier: AiModelTier.Reasoning);
        h.OpenAi.RawJsonToReturn = """{"explanation":"ok"}""";

        var chunks = await h.DispatchAsync(new { selectionText = "Liability is capped." });

        chunks.Should().NotContain(c => c.Type == "error");
        h.OpenAi.LastModel.Should().Be(
            Harness.ReasoningDeploymentName,
            "an Action with sprk_modeltier=Reasoning MUST execute against the configured Reasoning " +
            "deployment (not gpt-4o-mini) — the model:null hardcode this task removes");
    }

    [Fact]
    public async Task DispatchAsync_ActionWithUnspecifiedTier_ResolvesToStandardDeployment()
    {
        var h = new Harness();
        await h.SeedSessionAsync(BuildSession());
        h.GivenBinding(inputSchema: ComposeInputSchema);
        // No tier passed — mirrors the vast majority of pre-existing Actions whose
        // sprk_modeltier column has never been set.
        h.GivenFlatTextAction("ROLE: Explain the selected clause.");
        h.OpenAi.RawJsonToReturn = """{"explanation":"ok"}""";

        var chunks = await h.DispatchAsync(new { selectionText = "Liability is capped." });

        chunks.Should().NotContain(c => c.Type == "error");
        h.OpenAi.LastModel.Should().Be(
            Harness.StandardDeploymentName,
            "an Action with no sprk_modeltier MUST default deterministically to the Standard tier");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (v) DECLARED COMPANION INPUTS — spaarkeai-compose-r8 task 051, FR-C03 "link 3".
    //
    //     The operand channel is SINGLE-VALUED by construction: TryFindDeclaredOperandField
    //     returns on the FIRST vocabulary match and ResolveOperand builds a one-key object. That
    //     answers "what content do I run over?" — it was never able to answer "and WHERE did that
    //     content come from?". So a deterministic anchor (a w14:paraId captured at selection time)
    //     had nowhere to ride: the client sent it, the server accepted it, and it was silently
    //     dropped before the prompt. The model was then asked to name its edit target and could
    //     only do so by QUOTING PROSE BACK — which is a generation step, and generation is lossy.
    //     That is the root of Compose's "wording differs slightly" dead end.
    //
    //     The fix is not to widen the operand vocabulary (adding a 4th name would make the anchor
    //     COMPETE with selectionText, not accompany it — first match wins, so one would vanish).
    //     It is to make the Action's declared input schema mean what it says: a property the Action
    //     DECLARES and the caller SUPPLIES reaches the model, alongside the operand.
    //
    //     These three tests pin the whole contract: it arrives, undeclared args still do not, and
    //     the operand itself is unchanged.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Input schema declaring the operand AND a companion identifier (the compose edit shape).</summary>
    private const string ComposeAnchoredInputSchema =
        """{"type":"object","required":["selectionText"],"properties":{"selectionText":{"type":"string"},"targetParaId":{"type":"string"}}}""";

    [Fact]
    public async Task DispatchAsync_DeclaredCompanionInput_ReachesThePromptAlongsideTheOperand()
    {
        var h = new Harness();
        await h.SeedSessionAsync(BuildSession());
        h.GivenBinding(inputSchema: ComposeAnchoredInputSchema);
        h.GivenFlatTextAction("ROLE: Rewrite the selected clause.");
        h.OpenAi.RawJsonToReturn = """{"explanation":"ok"}""";

        const string clause = "The Provider total liability is capped at fees paid.";
        const string paraId = "A1B2C3D4";

        var chunks = await h.DispatchAsync(new { selectionText = clause, targetParaId = paraId });

        chunks.Should().NotContain(c => c.Type == "error");
        h.OpenAi.LastPrompt.Should().Contain("## Input").And.Contain(clause);
        h.OpenAi.LastPrompt.Should().Contain(paraId,
            "a DECLARED, SUPPLIED input MUST reach the model — otherwise the model cannot echo an "
            + "identifier and is forced to quote prose back, which is the lossy step this removes");
    }

    [Fact]
    public async Task DispatchAsync_UndeclaredArg_DoesNotReachThePrompt()
    {
        // The bound on the rule above: DECLARATION is the contract, not "everything in args".
        // Without this, any caller-supplied field would silently become prompt content.
        var h = new Harness();
        await h.SeedSessionAsync(BuildSession());
        h.GivenBinding(inputSchema: ComposeInputSchema); // declares selectionText ONLY
        h.GivenFlatTextAction("ROLE: Explain the selected clause.");
        h.OpenAi.RawJsonToReturn = """{"explanation":"ok"}""";

        var chunks = await h.DispatchAsync(
            new { selectionText = "Liability is capped.", targetParaId = "SHOULDNOTAPPEAR" });

        chunks.Should().NotContain(c => c.Type == "error");
        h.OpenAi.LastPrompt.Should().NotContain("SHOULDNOTAPPEAR",
            "an arg the Action never declared is still accepted-and-ignored");
    }

    [Fact]
    public async Task DispatchAsync_CompanionInput_DoesNotDisplaceTheOperand()
    {
        // Non-regression for the single-valued operand: the companion rides ALONGSIDE, and the
        // resolved operand kind is still SelectionText. A 4th vocabulary entry would have failed here.
        var h = new Harness();
        await h.SeedSessionAsync(BuildSession());
        h.GivenBinding(inputSchema: ComposeAnchoredInputSchema);
        h.GivenFlatTextAction("ROLE: Rewrite the selected clause.");
        h.OpenAi.RawJsonToReturn = """{"explanation":"ok"}""";

        const string clause = "Liability is capped at fees paid.";
        await h.DispatchAsync(new { selectionText = clause, targetParaId = "A1B2C3D4" });

        h.OpenAi.LastPrompt.Should().Contain(clause,
            "the operand is still the content the completion runs over — the companion does not replace it");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (vi) The WHOLE-DOCUMENT compose shape (spaarkeai-compose-r8 task 054, FR-C03)
    //
    //      The whole-document revise pass dispatches into a session that HAS a registered file. Before
    //      task 054 its args were `{revisionIntent}` alone — no operand-vocabulary member — so
    //      HasStructuredOperand was false and the dispatch took the FILE-operand path. That path builds
    //      its ContextBindingRequest from the resolved DocumentText only: it passes NO Args and NO
    //      InputSchemaJson, so `revisionIntent` was accepted at the endpoint and dropped before the
    //      prompt. The Action's four INSTRUCTIONS-BY-INTENT branches could not be selected, and
    //      flag-risks (comments-only, empty edits by contract) was indistinguishable from
    //      improve-clarity.
    //
    //      Task 054 supplies `documentText` — the editor's own text with each paragraph's paraId
    //      prefixed, which is simultaneously the operand and the CLOSED SET the model must choose an id
    //      from. That flips the branch. These tests pin the flip itself, because it is the link that
    //      makes every other part of the whole-document anchor chain reachable.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>The whole-document Action's declared inputs: the operand plus its two companions.</summary>
    private const string ReviseDocumentInputSchema =
        """{"type":"object","required":["revisionIntent","documentText"],"properties":{"documentText":{"type":"string"},"revisionIntent":{"type":"string"},"instruction":{"type":"string"}}}""";

    [Fact]
    public async Task DispatchAsync_WholeDocumentOperand_WinsOverTheSessionFile_AndCarriesTheIntent()
    {
        // The session has a file registered — exactly the real condition. The supplied operand must
        // still win, because the file path would silently discard both the closed set and the intent.
        var h = new Harness();
        await h.SeedSessionAsync(BuildSession(fileId: "file-1"));
        h.GivenBinding(inputSchema: ReviseDocumentInputSchema);
        h.GivenFlatTextAction("ROLE: Revise the whole document per the supplied revisionIntent.");
        h.GivenSessionFileText("RAG-EXTRACTED TEXT — a different projection of the same file.");
        h.OpenAi.RawJsonToReturn = """{"explanation":"ok"}""";

        const string annotated = "[AAAA0001] 1. Definitions\n[AAAA0002] The receiving party shall indemnify.";
        var chunks = await h.DispatchAsync(new { revisionIntent = "flag-risks", documentText = annotated });

        chunks.Should().NotContain(c => c.Type == "error");

        // The closed set reached the model, ids beside the content they name.
        h.OpenAi.LastPrompt.Should().Contain("## Input").And.Contain("[AAAA0002] The receiving party shall indemnify");

        // The intent reached the model as a declared companion. Without this the model cannot tell
        // flag-risks (comments only) from improve-clarity, and task 055's comments[] half has nothing
        // reliable to place.
        h.OpenAi.LastPrompt.Should().Contain("flag-risks",
            "the declared intent must reach the prompt — the file-operand path drops it entirely");

        // And the RAG extract did NOT come along: one document, one coordinate system. Sending both
        // would let the model quote from a text the editor cannot place an edit into.
        h.OpenAi.LastPrompt.Should().NotContain("RAG-EXTRACTED TEXT",
            "the supplied operand replaces the file projection — the model must quote from the same "
            + "text the redline is placed into, which is the editor's, not the search index's");
    }

    [Fact]
    public async Task DispatchAsync_WholeDocumentWithoutOperand_StillTakesTheFilePath_Unregressed()
    {
        // The degradation path: no Compose tab open / no stamped ids ⇒ the client omits documentText and
        // the dispatch is exactly what it was before task 054. Pinned so the fallback stays a KNOWN
        // shape rather than an accident.
        var h = new Harness();
        await h.SeedSessionAsync(BuildSession(fileId: "file-1"));
        h.GivenBinding(inputSchema: ReviseDocumentInputSchema);
        h.GivenJpsAction(SummarizeSystemPromptJps, SummarizeOutputSchema);
        h.GivenSessionFileText("RAG-EXTRACTED TEXT — a different projection of the same file.");
        h.OpenAi.RawJsonToReturn = """{"summary":"ok"}""";

        var chunks = await h.DispatchAsync(new { revisionIntent = "flag-risks" });

        chunks.Should().NotContain(c => c.Type == "error");
        h.OpenAi.LastPrompt.Should().Contain("## Document").And.Contain("RAG-EXTRACTED TEXT");
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
            UploadedFiles: files) { OwnerOid = TestSessionOwner.Oid };
    }

    private sealed class Harness
    {
        // Distinct per-tier deployment names (task 010) — deliberately NOT real Azure OpenAI deployment
        // names, so a test failure that resolves to the wrong tier's value is unambiguous at a glance.
        public const string FastDeploymentName = "seam-fast-deploy";
        public const string StandardDeploymentName = "seam-standard-deploy";
        public const string ReasoningDeploymentName = "seam-reasoning-deploy";

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

            var modelOptions = Options.Create(new DocumentIntelligenceOptions
            {
                FastModel = FastDeploymentName,
                StandardModel = StandardDeploymentName,
                ReasoningModel = ReasoningDeploymentName,
            });

            var renderer = new PromptSchemaRenderer(Mock.Of<ILogger<PromptSchemaRenderer>>());
            var runner = new ActionRunner(OpenAi, renderer, Mock.Of<ILogger<ActionRunner>>(), modelOptions);
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

        public void GivenFlatTextAction(string systemPrompt, AiModelTier? tier = null) =>
            Scope
                .Setup(s => s.GetActionAsync(ActionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AnalysisAction
                {
                    Id = ActionId,
                    Name = "Seam Test Action",
                    SystemPrompt = systemPrompt,
                    OutputSchemaJson = ComposeOutputSchema,
                    Temperature = 0.2m,
                    ModelTier = tier,
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

        /// <summary>The <c>model</c> deployment name ActionRunner passed on the most recent call — the
        /// task 010 assertion point (proves the resolved deployment reached the LLM boundary, not just
        /// the resolver's return value in isolation).</summary>
        public string? LastModel { get; private set; }

        public Task<string> GetStructuredCompletionRawAsync(
            string prompt, BinaryData jsonSchema, string schemaName, string? model = null,
            int? maxOutputTokens = null, float? temperature = null, CancellationToken cancellationToken = default)
        {
            LastPrompt = prompt;
            LastModel = model;
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
