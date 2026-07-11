// Task 101 / 084 (E2E-R2) — Compose AI-action dispatch vertical-slice contract test
// (KEEP path: endpoint-contract). THE anti-recurrence forcing function.
//
// WHY THIS FILE EXISTS (non-waivable per project CLAUDE.md §"E2E Definition-of-Done" +
// notes/e2e-gap-register.md "THE ANTI-RECURRENCE FIX" + Cluster 2):
//   NO through-the-wire test dispatched a REAL COMPOSE action through the REAL `/dispatch`
//   route. The 3 existing surfaces each stop one layer short:
//     - DispatchSessionEndpointContractTests — real HTTP /dispatch, but a generic
//       *informational chat-summarize* binding (file-operand path), never a compose action.
//     - DispositionRoutabilitySeamTests — compose, but orchestrator-level with mocked routing
//       and a synthetic binding (no real HTTP /dispatch route).
//     - ComposeDispositionContractTests — pure, no WebApplicationFactory / no HTTP.
//   That gap is exactly why compose's routing promotion shipped "31 tests green" while the
//   disposition admit-gate still 422'd a `compose` dispatch end-to-end (the half-landed E-20
//   defect). This file adds the two dimensions none of the others have TOGETHER: a real HTTP
//   `/dispatch` route AND a `compose`-disposition binding — dispatching the EXACT wire body the
//   client `dispatchConsumer(bindingId, { slots })` helper POSTs from the ComposeAiToolbar
//   (`{ bindingId, args: { selectionText, selectionAnchor*, documentSpeId, documentRecordId,
//   sessionId } }`), and asserting admit → route → store "compose" → terminal render.
//
//   It proves the slice IN-PROCESS: the compose Binding is seeded at the WebApplicationFactory
//   MODULE BOUNDARY (the IConsumerRoutingService.GetBindingByIdAsync seam — the SAME boundary
//   tasks 100/102 mock Dataverse at), driving the REAL SessionDispatchOrchestrator + REAL
//   ActionRunner + REAL ContextBinder + REAL OutputRouter. It does NOT require the live-env
//   catalog deploy (task 047) — that deploy mints the per-environment row GUIDs the client
//   toolbar needs to ACTIVATE its buttons (gap 2.2, E2E-pending on 047); this test seeds the
//   GUID→binding resolution at the module boundary instead.
//
// KEEP-path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `endpoint-contract` · Path: `tests/integration/contract/Api/Ai/**`.
//   - "Every changed endpoint contract => >=1 integration test": anchors the compose dimension
//     of POST /api/ai/chat/sessions/{id}/dispatch (compose disposition admitted; structured
//     selectionText operand path; ledger store-before-render with disposition "compose").
//
// Banned-pattern compliance (ADR-038 §4 + tests/CLAUDE.md): NO Mock<HttpMessageHandler>; the
// orchestrator + ActionRunner + ContextBinder + OutputRouter under test are REAL; mocks live
// ONLY at the module boundaries (IConsumerRoutingService catalog / IScopeResolverService action
// / IOpenAiClient LLM). Assertions are HTTP-observable + persisted ledger side-effects.

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Through-the-wire (anti-recurrence) contract tests for compose AI-action dispatch (task 101,
/// closing the task-084 forcing function / Cluster 2). Reuses <see cref="DispatchSessionEndpointTestFixture"/>
/// — the in-process WebApplication mapping the REAL <c>/dispatch</c> route over the REAL
/// <see cref="SessionDispatchOrchestrator"/> + <see cref="Sprk.Bff.Api.Services.Ai.LinearConsumers.ActionRunner"/>
/// + <see cref="Sprk.Bff.Api.Services.Ai.Context.ContextBinder"/> + recording <see cref="OutputRouter"/> —
/// and seeds a COMPOSE binding at the <see cref="IConsumerRoutingService"/> module boundary.
/// </summary>
public sealed class ComposeDispatchEndpointContractTests : IClassFixture<DispatchSessionEndpointTestFixture>
{
    private readonly DispatchSessionEndpointTestFixture _fx;

    // A stable, arbitrary GUID standing in for the seeded compose Binding row. In production this
    // GUID is minted per-environment at catalog seed time (task 047); here it is seeded at the
    // module boundary so the slice runs in-process (no live deploy needed).
    private static readonly Guid ComposeBindingId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid ComposeActionId = Guid.Parse("ffffffff-1111-2222-3333-444444444444");

    private const string TestSessionId = "22222222-3333-4444-5555-666666666666";

    // The clause the ComposeAiToolbar puts in the `selectionText` slot. A distinctive marker so we
    // can prove it reached the executor's `## Input` section (structured-operand path), NOT the
    // session-file `## Document` path.
    // Apostrophe-free on purpose: it is asserted verbatim inside the executor prompt's ## Input
    // JSON block, where an apostrophe would be escaped to ' and defeat a literal substring match.
    private const string SelectionText =
        "The aggregate liability shall not exceed the fees paid in the prior twelve (12) months.";

    // The declared input schema the compose Binding carries (sprk_inputschema): declares the
    // `selectionText` operand field so ContextBinder.HasStructuredOperand resolves it from args.
    private const string ComposeSelectionInputSchema =
        """{"type":"object","properties":{"selectionText":{"type":"string"}}}""";

    public ComposeDispatchEndpointContractTests(DispatchSessionEndpointTestFixture fx)
    {
        _fx = fx;
    }

    // ── The exact wire body the client dispatchConsumer(bindingId, { slots }) helper POSTs from
    //    ComposeAiToolbar.handleActionClick — { bindingId, args: <slots> }. (dispatchConsumer.ts
    //    sends `body: { bindingId, args: args?.slots ?? {} }`.) ───────────────────────────────────
    private static object BuildToolbarDispatchBody(Guid bindingId) => new
    {
        bindingId = bindingId.ToString(),
        args = new
        {
            selectionText = SelectionText,
            selectionAnchorStart = 0,
            selectionAnchorEnd = SelectionText.Length,
            documentSpeId = "spe-drive-item-compose-001",
            documentRecordId = "doc-record-compose-001",
            sessionId = TestSessionId,
        },
    };

    /// <summary>A compose Binding seeded at the routing module boundary. Zero session files — the
    /// compose actions resolve their operand from the `selectionText` arg (structured-operand path),
    /// not from session files.</summary>
    private void SeedComposeBinding(BindingDisposition disposition, string consumerType)
    {
        _fx.Reset();

        // A session with NO uploaded files. If the structured-operand path did NOT engage, the
        // orchestrator would fall to the file path and stream a "No session files" error instead —
        // so a successful compose store here PROVES the selectionText operand path ran.
        _fx.Sessions.Session = new ChatSession(
            SessionId: TestSessionId,
            TenantId: "00000000-0000-0000-0000-000000000abc",
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: Array.Empty<ChatSessionFile>());

        _fx.ConsumerRoutingMock
            .Setup(c => c.GetBindingByIdAsync(ComposeBindingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Binding
            {
                BindingId = ComposeBindingId,
                ConsumerType = consumerType,
                ConsumerCode = "default",
                Environment = "*",
                ActionId = ComposeActionId,
                ActionKind = ActionKind.Prompted,
                Disposition = disposition,
                InputSchemaJson = ComposeSelectionInputSchema,
            });

        _fx.ScopeResolverMock
            .Setup(s => s.GetActionAsync(ComposeActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction
            {
                Id = ComposeActionId,
                Name = "Compose — Explain Clause",
                SystemPrompt =
                    """{"$schema":"https://spaarke.com/schemas/prompt/v1","instruction":{"role":"You are the Spaarke Compose clause assistant.","task":"Act on the clause in the ## Input section."},"output":{"fields":[{"name":"explanation","type":"string","description":"plain-language explanation"}],"structuredOutput":true}}""",
                OutputSchemaJson =
                    """{"type":"object","additionalProperties":false,"required":["explanation"],"properties":{"explanation":{"type":"string"}}}""",
                Temperature = 0.2m,
            });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Informational compose action — real HTTP /dispatch, structured operand
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_ComposeInformationalAction_DispatchesThroughRealDispatchRoute_StructuredSelectionOperand()
    {
        SeedComposeBinding(BindingDisposition.Informational, ConsumerTypes.ComposeExplainClause);
        _fx.OpenAi.RawJsonToReturn = """{"explanation":"It caps aggregate liability at the fees paid in the prior year."}""";

        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            BuildToolbarDispatchBody(ComposeBindingId));

        // Real HTTP /dispatch admitted the compose action and streamed a terminal chunk.
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "an informational compose action dispatches end-to-end through the REAL /dispatch route");
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"type\":\"complete\"", "the compose dispatch yields a terminal complete chunk");

        // The selectionText operand reached the executor's ## Input section (structured-operand
        // path via ContextBinder), NOT the session-file ## Document path — with ZERO session files,
        // this is the only way the action could have produced output at all.
        _fx.OpenAi.LastPrompt.Should().NotBeNull("the prompted executor ran (a compose action executed)");
        _fx.OpenAi.LastPrompt!.Should().Contain("## Input")
            .And.Contain(SelectionText,
                "the toolbar's selectionText slot resolved as the structured operand into ## Input " +
                "(ContextBinder.HasStructuredOperand), never the session-file ## Document path");

        // ADR-039: the row GUID was the routing decision — resolved by-id at the module boundary.
        _fx.ConsumerRoutingMock.Verify(
            c => c.GetBindingByIdAsync(ComposeBindingId, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce, "dispatch resolves the compose Binding BY ID (the client-sent bindingId)");

        // ADR-040 store-before-render: the SessionOutput was stored with the informational ledger
        // value, and its payload IS the executor output (the terminal chunk renders FROM this stored
        // entry — the render-follows-store contract). NOTE (spaarkeai-compose-r2 wire-loss fix): the
        // wire chunk NO LONGER coerces a compose payload to DocumentAnalysisResult — the compose
        // fields now survive on the wire (asserted directly in
        // Post_ComposeInformationalAction_ComposeFieldsSurviveOnTheWire below). This test still
        // asserts the stored-entry source of truth as before.
        _fx.Router.LastRouted.Should().NotBeNull("the ADR-040 ledger write ran before render");
        _fx.Router.LastRouted!.Entry.Disposition.Should().Be("informational");
        _fx.Router.LastRouted!.Entry.Key.Should().Be($"{ComposeBindingId}@t1");
        _fx.Router.LastExecutorOutput.Should().NotBeNull("the executor output was handed to the ledger seam");
        _fx.Router.LastExecutorOutput!.Value.GetRawText().Should().Contain("caps aggregate liability",
            "the executor output stored to the ledger (ADR-040) is what the terminal chunk renders from");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Compose-DISPOSITION action (draft-alternative) — THE anti-recurrence
    //    proof: the disposition admit-gate ADMITS `compose` (not the half-landed
    //    422), stores a "compose"-disposition SessionOutput BEFORE render.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_ComposeDispositionAction_AdmittedNot422_StoresComposeSessionOutputBeforeRender()
    {
        SeedComposeBinding(BindingDisposition.Compose, ConsumerTypes.ComposeDraftAlternative);
        // The draft-alternative executor emits a structured-edit payload; the router stores it with
        // the COMPOSE disposition (the disposition comes from the Binding, not the payload shape).
        _fx.OpenAi.RawJsonToReturn = """{"explanation":"Consider: 'Provider's aggregate liability shall not exceed the fees paid in the prior twelve (12) months.'"}""";

        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            BuildToolbarDispatchBody(ComposeBindingId));

        // THE regression guard: the disposition admit-gate (DispositionRoutability) admits `compose`.
        // Before E-20 this was a 422 (dispatch.disposition-not-supported) — the false-green that
        // shipped. A 200 here means admit → route → store → render all ran for the compose disposition.
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the disposition admit-gate ADMITS the `compose` disposition end-to-end (E-20) — NOT a 422");
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("dispatch.disposition-not-supported",
            "a `compose` dispatch must never be rejected at the admit-gate (the half-landed-422 regression)");
        body.Should().Contain("\"type\":\"complete\"", "the compose-disposition dispatch renders a terminal chunk");

        // ADR-040: the SessionOutput was STORED with disposition "compose" BEFORE the terminal chunk
        // rendered (the recording router captures the executor output it received pre-store + the
        // stored entry it returned).
        _fx.Router.LastExecutorOutput.Should().NotBeNull("the executor produced output the router stored");
        _fx.Router.LastRouted.Should().NotBeNull("the ADR-040 ledger write ran");
        _fx.Router.LastRouted!.Entry.Disposition.Should().Be("compose",
            "the stored SessionOutput carries the `compose` disposition (ComposeDisposition.DispositionValue)");
        _fx.Router.LastRouted!.Entry.Key.Should().Be($"{ComposeBindingId}@t1",
            "the compose output is addressable as {bindingId}@t{n} (ADR-040)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. NEGATIVE CONTRAST — the selectionText operand is LOAD-BEARING: WITHOUT
    //    it the compose action does not resolve the structured operand and falls
    //    to the session-file path (which here has nothing to act on), so nothing
    //    is stored. Proves the positive tests' operand resolution is not
    //    incidental (mirrors the memory-resume negative-contrast discipline).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_ComposeAction_WithoutSelectionText_FallsToFilePathNotOperandPath_StoresNothing()
    {
        SeedComposeBinding(BindingDisposition.Compose, ConsumerTypes.ComposeDraftAlternative);

        // One session file so the file path resolves it WITHOUT the ~5s manifest readiness probe,
        // but override the text source to return EMPTY extracted text → the file path has no content
        // to act on. (Kept distinct from the positive tests' zero-file structured-operand path.)
        _fx.Sessions.Session = new ChatSession(
            SessionId: TestSessionId,
            TenantId: "00000000-0000-0000-0000-000000000abc",
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: new[]
            {
                new ChatSessionFile(
                    FileId: "file-empty", FileName: "empty.docx", ContentType: "application/pdf",
                    SizeBytes: 0, SearchDocumentIdsCsv: "doc-file-empty-1", UploadedAt: DateTimeOffset.UtcNow),
            });
        _fx.TextSourceMock
            .Setup(t => t.FetchAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ChatSessionFile>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionFileText { ExtractedText = "", DisplayName = "empty.docx", ChunkCount = 0 });

        var client = _fx.CreateAuthenticatedClient();

        // Empty args → HasStructuredOperand is false → the file path runs (NOT the selectionText operand).
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            new { bindingId = ComposeBindingId.ToString(), args = new { } });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the stream opens (the failure is an in-stream error chunk, not a pre-stream reject)");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"type\":\"error\"",
            "without the selectionText operand the compose action fell to the file path, which had no text to act on");

        // No ledger write ⇒ the executor never produced a stored output. (LastPrompt is a sticky
        // capture on the shared fixture stub across the class's sequential tests, so it is not a
        // reliable "did-not-run" signal here — the store side-effect is.)
        _fx.Router.LastRouted.Should().BeNull(
            "no operand ⇒ no execution ⇒ no ledger write — proving the selectionText operand is " +
            "load-bearing in the positive tests, not incidental");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. THE WIRE-BODY anti-recurrence proof (spaarkeai-compose-r2 UAT wire-loss
    //    fix). The prior tests all assert on the STORED ledger entry (or a mere
    //    "type":"complete" substring) — the exact blind spot that let the empty-
    //    DocumentAnalysisResult blob ship: DeserializeResultChunk coerced EVERY
    //    payload to DocumentAnalysisResult and System.Text.Json dropped the compose
    //    fields on the wire. This test asserts the ACTUAL SSE RESPONSE BODY (the
    //    bytes the client dispatchConsumer reads at `dispatched.result`) still
    //    carries the compose fields (`explanation`, `keyConcepts`) — the value the
    //    client formatter (composeResultFormat.ts) shape-detects on.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_ComposeInformationalAction_ComposeFieldsSurviveOnTheWire()
    {
        SeedComposeBinding(BindingDisposition.Informational, ConsumerTypes.ComposeExplainClause);
        // The compose-explain-clause shape: distinctive marker values so we can prove they reached
        // the WIRE, not just the ledger. The executor stores exactly this (OutputRouter stores verbatim;
        // the stub returns it verbatim; ActionRunner returns the raw LLM JSON).
        _fx.OpenAi.RawJsonToReturn =
            """{"explanation":"WIRE_EXPLANATION_MARKER caps aggregate liability.","keyConcepts":["WIRE_KEYCONCEPT_LIABILITY","WIRE_KEYCONCEPT_CAP"],"relatedPlaybookIds":[]}""";

        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            BuildToolbarDispatchBody(ComposeBindingId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        // THE assertion the shipped blind-spot test lacked: read the WIRE body (the SSE chunk the
        // client actually receives) and prove the compose fields SURVIVE there — not merely in the
        // stored ledger. Before the fix these were stripped to an empty DocumentAnalysisResult blob.
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("\"type\":\"complete\"", "the compose dispatch yields a terminal complete chunk");
        body.Should().Contain("\"explanation\":\"WIRE_EXPLANATION_MARKER caps aggregate liability.\"",
            "the compose `explanation` field MUST survive on the wire at `result` — the client formatter " +
            "(composeResultFormat.ts formatExplainClauseResult) shape-detects on it");
        body.Should().Contain("\"keyConcepts\":[", "the compose `keyConcepts` array MUST survive on the wire");
        body.Should().Contain("WIRE_KEYCONCEPT_LIABILITY").And.Contain("WIRE_KEYCONCEPT_CAP");

        // And the coercion that shipped the bug is gone: the wire result is NOT an empty
        // DocumentAnalysisResult blob (no synthesized DAR default fields on a compose payload).
        body.Should().NotContain("\"tldr\":[]",
            "a compose payload must NOT be coerced to a DocumentAnalysisResult (the empty-DAR blob that shipped)");
        body.Should().NotContain("\"parsedSuccessfully\"",
            "the compose wire result carries the RAW capability shape, not synthesized DAR defaults");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. NON-REGRESSION — a summarize / DocumentAnalysisResult dispatch STILL
    //    renders as the coerced DAR shape ON THE WIRE (incl. DAR defaults). Proves
    //    the fix's discriminator preserves the shipped summarize contract byte-for-
    //    byte and did NOT turn the summarize path into a raw pass-through.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_SummarizeAction_StillRendersDocumentAnalysisResultOnTheWire_NoRegression()
    {
        // The fixture DEFAULT binding is the chat-summarize row (file-operand path) — reset restores it.
        _fx.Reset();
        _fx.Sessions.Session = new ChatSession(
            SessionId: TestSessionId,
            TenantId: "00000000-0000-0000-0000-000000000abc",
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: new[]
            {
                new ChatSessionFile(
                    FileId: "file-sum", FileName: "sum.pdf", ContentType: "application/pdf",
                    SizeBytes: 1024, SearchDocumentIdsCsv: "doc-file-sum-1", UploadedAt: DateTimeOffset.UtcNow),
            });
        // A genuine DocumentAnalysisResult-shaped payload (has `tldr` — the DAR signature).
        _fx.OpenAi.RawJsonToReturn =
            """{"tldr":["SUMMARIZE_WIRE_MARKER"],"summary":"A summarize summary.","keywords":"k","entities":{"organizations":[],"persons":[]}}""";

        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            new { bindingId = DispatchSessionEndpointTestFixture.DispatchBindingId.ToString(), args = new { } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("\"type\":\"complete\"");
        body.Should().Contain("SUMMARIZE_WIRE_MARKER", "the summarize output flows to the wire");
        // The discriminator (tldr present) coerced this to the typed DAR — so the DAR-specific default
        // fields ARE synthesized on the wire, proving the summarize contract is unchanged (NOT a raw
        // pass-through). `parsedSuccessfully` is a DAR-only field with no source in the raw payload.
        body.Should().Contain("\"parsedSuccessfully\":true",
            "a DocumentAnalysisResult-shaped payload is STILL coerced to the typed DAR on the wire " +
            "(byte-identical summarize contract) — the fix's discriminator is summarize-preserving");
        body.Should().Contain("\"entities\":", "the DAR `entities` field is present on the summarize wire shape");
    }
}
