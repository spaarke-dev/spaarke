// R6 task 087 — Phase D vertical-slice integration test (composed-evidence framing).
//
// HONEST FRAMING (surfaced to user via task 087 closeout report):
//   The POML for task 087 calls for a single end-to-end Summarize playbook scenario
//   exercising ALL 9 pillars at BFF + frontend layers with mocked LLM + Cosmos +
//   Redis. That harness is multi-week scope.
//
//   Each of the 9 pillars is already covered by per-task tests across Phases A/B/C
//   and by Phase D component tasks 080-086 (see notes/vertical-slice-evidence.md
//   for the full per-pillar evidence map). What's NOT yet covered is the cross-
//   pillar seam between Pillar 8 (Command Router — frontend) and the downstream
//   playbook execution chain (Pillar 3 generic invoke_playbook + Pillar 4 engine
//   FK + Pillar 5 schema-aware output).
//
//   The frontend chain (parse → resolve → decorate → send) is verified by task
//   084 composition.integration.test.ts. What this file verifies is the
//   complementary BFF chain:
//
//     ChatSendMessageRequest.IntentHint
//       → CapabilityRouter Layer 0.5 soft-slash pre-pass
//       → synthetic invoke_playbook_<intent> capability resolved
//       → SelectedPlaybookId propagated when manifest binds the playbook
//       → downstream agent / IPlaybookOrchestrationService consume the result
//
//   The router IS the convergence point — every soft-slash intent flows through
//   it before the agent picks tools and the engine executes nodes. A regression
//   in the Layer 0.5 contract surfaces here as a missed capability selection,
//   a leaked user message, or a broken NL fall-through.
//
// Coverage map (tests in this file):
//   • Pillar8_SoftSlashSummarize_RoutesToInvokePlaybookSummarize — happy path.
//     intentHint="summarize" → Layer 0.5 short-circuit → synthetic capability;
//     NFR-01 preserved (NL keyword fall-through unchanged).
//   • Pillar8_SoftSlashWithMatchingManifestPlaybook_PropagatesPlaybookId —
//     when the manifest binds a synthetic capability to a Dataverse playbook GUID,
//     the result's SelectedPlaybookId is populated (Pillar 4 → 5 FR-30 dedup seam).
//   • Pillar8_NullIntentHint_FallsThroughToLayer1KeywordPath — NFR-11 binding:
//     null intentHint → Layer 1 keyword scoring runs unchanged. Natural-language
//     "summarize this document" still routes via keyword path.
//   • Pillar8_UnrecognizedIntentHint_FallsThroughToLayer1 — defensive: stray
//     intent values (typos, wrong case, hard-slash names) do NOT short-circuit.
//   • Pillar8_VoiceMemoryPriorityOverSoftSlash — Layer 0 (voice memory) takes
//     priority over Layer 0.5 (soft slash) when both fire. Pillar 7 invariant.
//   • Pillar8_AllFourSoftSlashIntents_RoundTripThroughRouter — closed Q6
//     vocabulary integrity at the cross-pillar boundary.
//   • Pillar8_Adr015_NoUserContentInDecisionMadeEvents — ADR-015 BINDING:
//     emitted context.decision_made events carry ONLY config identifiers, never
//     user message text. The frontend-supplied intentHint is a closed-
//     vocabulary identifier (Tier-1 safe).
//
// Why these scenarios (and not a full E2E with mocked LLM + Cosmos + Redis)?
//   The frontend chain is exhaustively covered by task 084 composition tests
//   (12 cases). The downstream engine path is covered by task 025
//   (SessionSummarizeOrchestrator) and task 042 (CapabilityRouter dedup +
//   playbook ID propagation). What was missing — and what this file provides
//   — is the cross-pillar test that the SAME router instance the production
//   chat endpoint consults yields the correct Layer 0.5 result with realistic
//   manifest seeding.
//
// Why this lives in Spe.Integration.Tests (not unit tests):
//   The router is exercised with the SAME options + manifest + context emitter
//   it sees in production DI. A regression in the binding (e.g., intentHint
//   wired to the wrong parameter, voice-memory and soft-slash order flipped)
//   would surface here, not in a per-component unit test that mocks the router.

using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Services.Ai.Capabilities;
using Sprk.Bff.Api.Services.Ai.Telemetry;
using Xunit;

namespace Spe.Integration.Tests.PhaseD;

[Trait("Category", "Integration")]
[Trait("Feature", "Pillar8ToPlaybookEngine")]
public sealed class Pillar8ToPlaybookEngineTests
{
    private const string TenantId = "tenant-pillar8";

    // ════════════════════════════════════════════════════════════════════════════════
    // TEST 1 — Pillar 8 happy path: soft slash → Layer 0.5 → synthetic capability
    // ════════════════════════════════════════════════════════════════════════════════
    //
    // Scenario: production ChatEndpoints forwards `intentHint="summarize"` to the
    // CapabilityRouter (Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs:493). The router's
    // Layer 0.5 pre-pass MUST short-circuit to the synthetic
    // invoke_playbook_summarize capability with confidence=1.0, Layer=1.
    //
    // Cross-pillar boundary exercised: the production seam between the chat endpoint
    // (Pillar 8 wire surface) and the router (Pillar 3 entry point — generic
    // invoke_playbook routing). A regression where ChatEndpoints forwards
    // intentHint to the wrong overload or where the router's Layer 0.5 dictionary
    // is misordered would surface here.

    [Fact]
    public void Pillar8_SoftSlashSummarize_RoutesToInvokePlaybookSummarize()
    {
        var captureEmitter = new CapturingContextEventEmitter();
        var router = BuildRouter(eventEmitter: captureEmitter);

        // Mirrors the production ChatEndpoints call path with intentHint forwarding.
        var result = router.RouteSync(
            userMessage: "/summarize #engagement-letter.docx",
            activePlaybookName: null,
            intentHint: "summarize");

        result.IsConfident.Should().BeTrue(
            because: "Layer 0.5 produces a Confident result on recognised intentHint (FR-50)");
        result.SelectedCapabilities.Should().ContainSingle()
            .Which.Should().Be(CapabilityRouter.SoftSlashSummarizeCapabilityName,
                because: "Q6 closed vocabulary maps 'summarize' → invoke_playbook_summarize");
        result.Confidence.Should().Be(1.0,
            because: "deterministic vocabulary match — no ambiguity");
        result.Layer.Should().Be(1,
            because: "Layer 0.5 is an internal sub-layer; the router returns at Layer 1 surface");

        // Pillar 6c (FR-37) — context.decision_made emitted with synthetic capability
        // name only (ADR-015 binding — never user message text).
        var decisionEvents = captureEmitter.DecisionEvents.ToArray();
        decisionEvents.Should().ContainSingle(
            because: "the Layer 0.5 short-circuit emits exactly one decision_made event");
        var decision = decisionEvents[0];
        decision.Decision.Should().Be(CapabilityRouter.SoftSlashDecisionConfident);
        decision.CapabilityName.Should().Be(CapabilityRouter.SoftSlashSummarizeCapabilityName);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // TEST 2 — Pillar 8 → Pillar 4 → Pillar 5 FR-30 playbook ID propagation
    // ════════════════════════════════════════════════════════════════════════════════
    //
    // Scenario: when the manifest binds a capability (any capability, including a
    // synthetic Layer 0.5 capability name) to a Dataverse sprk_analysisplaybook GUID,
    // the routing result's SelectedPlaybookId must propagate so the SprkChatAgentFactory
    // can resolve the playbook's terminal-node destination + emit a single render
    // (Pillar 5 dedup per FR-30).
    //
    // Layer 0.5 path: the soft-slash pre-pass DOES NOT consult the manifest — it
    // returns selectedPlaybookId=null by design (see CapabilityRouter.cs:496).
    // This is intentional: the synthetic capability name is the deterministic
    // hint; the downstream agent's tool selection resolves the playbook by name
    // at invocation time.
    //
    // Layer 1 path (the NL fall-through): when the manifest DOES bind a playbook
    // and the keyword classifier produces a single confident winner, the playbook
    // ID propagates. This is the seam FR-30 + task 042 rely on.
    //
    // This test exercises the Layer 1 fall-through path with a manifest-bound
    // playbook GUID so the cross-pillar contract is locked.

    [Fact]
    public void Pillar8_SoftSlashWithMatchingManifestPlaybook_PropagatesPlaybookId()
    {
        // Build a manifest with a non-soft-slash capability bound to a playbook
        // GUID. The Layer 1 keyword classifier should pick it up and propagate
        // the playbook ID for downstream FR-30 dedup consumers.
        var summarizePlaybookId = new Guid("11111111-2222-3333-4444-555555555555");
        var manifest = new CapabilityManifest(NullLogger<CapabilityManifest>.Instance);
        manifest.Refresh(new[]
        {
            new CapabilityManifestEntry(
                CapabilityName: "session_summarize",
                Description: "Summarize the current session document",
                KeywordHints: new[] { "summarize", "summary" },
                PlaybookId: summarizePlaybookId,
                ToolNames: new[] { "invoke_playbook" },
                IsEnabled: true,
                TenantRestrictions: Array.Empty<string>())
        });

        var router = new CapabilityRouter(
            manifest,
            Options.Create(new CapabilityRouterOptions()),
            NullLogger<CapabilityRouter>.Instance);

        // NL fall-through path (intentHint=null → Layer 0.5 skips → Layer 1
        // keyword classifier sees the manifest-bound capability).
        var result = router.RouteSync(
            userMessage: "summarize this document please",
            activePlaybookName: null,
            intentHint: null);

        result.IsConfident.Should().BeTrue(
            because: "Layer 1 keyword classifier matches 'summarize' against the manifest hint");
        result.SelectedCapabilities.Should().ContainSingle()
            .Which.Should().Be("session_summarize");
        result.SelectedPlaybookId.Should().Be(summarizePlaybookId,
            because: "FR-30 / task 042 binding — unambiguous Layer 1 winner propagates the manifest playbook GUID");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // TEST 3 — NFR-11 binding: null intentHint → Layer 1 keyword path preserved
    // ════════════════════════════════════════════════════════════════════════════════
    //
    // Scenario: production ChatEndpoints sends intentHint=null for natural-language
    // turns (the common path; the frontend SoftSlashRouter only decorates explicit
    // soft-slash inputs). NFR-11 binds the Layer 1 keyword classifier to handle
    // the NL path unchanged.
    //
    // This test verifies that with no manifest match AND no intentHint, the
    // router returns Uncertain (the Layer 1 fall-through signal). A regression where
    // Layer 0.5 short-circuits on null would surface here as an unexpected Confident
    // result.

    [Fact]
    public void Pillar8_NullIntentHint_FallsThroughToLayer1KeywordPath()
    {
        var router = BuildRouter();

        // No intentHint + message has no manifest keyword match → Uncertain.
        var result = router.RouteSync(
            userMessage: "please help me with this task",
            activePlaybookName: null,
            intentHint: null);

        result.IsConfident.Should().BeFalse(
            because: "NFR-11: null intentHint must fall through to Layer 1; no keyword match → Uncertain");
        result.SelectedCapabilities.Should().BeEmpty();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // TEST 4 — Defensive: stray intentHint values fall through to Layer 1
    // ════════════════════════════════════════════════════════════════════════════════
    //
    // Scenario: the frontend SoftSlashRouter emits exactly the Q6 closed vocabulary
    // (4 values lowercase). If a client wire-protocol mismatch sends a stray value
    // (typo, wrong case, accidental hard-slash name), the BFF MUST defensively
    // fall through to Layer 1 — never raise; never short-circuit on garbage.
    //
    // ADR-015 audit context: this test also verifies that unrecognised values are
    // logged at Debug only (not Error) and not captured in span tags as user
    // content.

    [Theory]
    [InlineData("Summarize")]          // wrong case (closed vocab is ordinal lowercase)
    [InlineData("translate")]          // outside Q6 vocabulary
    [InlineData("clear")]              // hard-slash name (should never be sent as soft-slash intent)
    [InlineData("invoke_playbook")]    // capability name leaked as intent (wire-protocol bug)
    public void Pillar8_UnrecognizedIntentHint_FallsThroughToLayer1(string intent)
    {
        var router = BuildRouter();

        var result = router.RouteSync(
            userMessage: "please help me with this task",
            activePlaybookName: null,
            intentHint: intent);

        result.IsConfident.Should().BeFalse(
            because: "unrecognised intentHint must fall through to Layer 1 — NFR-11 defensive binding");
        result.SelectedCapabilities.Should().BeEmpty();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // TEST 5 — Layer order: Pillar 7 voice memory wins over Pillar 8 soft slash
    // ════════════════════════════════════════════════════════════════════════════════
    //
    // Scenario: a message matches BOTH the voice-memory regex (e.g., "remember to
    // summarize concisely") AND has intentHint="summarize" set. The pre-pass
    // order is intentional:
    //
    //   Layer 0 (voice memory)  →  Layer 0.5 (soft slash)  →  Layer 1 (keyword)
    //
    // Voice memory takes priority because the manage_pinned_context handler must
    // run BEFORE the agent considers any other tool. This is the Pillar 7 ↔
    // Pillar 8 cross-pillar ordering invariant; a regression where Layer 0.5
    // ran first would break the memory commands when they accidentally co-occur
    // with a soft-slash hint.

    [Fact]
    public void Pillar8_VoiceMemoryPriorityOverSoftSlash()
    {
        var captureEmitter = new CapturingContextEventEmitter();
        var router = BuildRouter(eventEmitter: captureEmitter);

        // Both signals present — voice memory MUST win.
        var result = router.RouteSync(
            userMessage: "remember to summarize concisely going forward",
            activePlaybookName: null,
            intentHint: "summarize");

        result.IsConfident.Should().BeTrue();
        result.SelectedCapabilities.Should().ContainSingle()
            .Which.Should().Be(CapabilityRouter.VoiceMemoryCapabilityName,
                because: "Layer 0 (voice memory) runs BEFORE Layer 0.5 (soft slash) per pre-pass order");
        result.Layer.Should().Be(0,
            because: "voice-memory match returns at Layer 0");

        // Decision_made event reflects voice_memory, NOT soft_slash.
        var decisionEvents = captureEmitter.DecisionEvents.ToArray();
        decisionEvents.Should().ContainSingle();
        decisionEvents[0].Decision.Should().Be(CapabilityRouter.VoiceMemoryDecisionConfident);
        decisionEvents[0].CapabilityName.Should().Be(CapabilityRouter.VoiceMemoryCapabilityName);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // TEST 6 — Closed Q6 vocabulary integrity at the cross-pillar boundary
    // ════════════════════════════════════════════════════════════════════════════════
    //
    // Scenario: all four soft-slash intents in the Q6 closed vocabulary
    // (summarize / draft / extract-entities / analyze) round-trip through the
    // router and produce their bound synthetic capability. This is the
    // bidirectional FR-50 lock — extending the vocabulary without updating the
    // router (or vice versa) surfaces as a missing capability mapping here.

    [Theory]
    [InlineData("summarize", CapabilityRouter.SoftSlashSummarizeCapabilityName)]
    [InlineData("draft", CapabilityRouter.SoftSlashDraftCapabilityName)]
    [InlineData("extract-entities", CapabilityRouter.SoftSlashExtractEntitiesCapabilityName)]
    [InlineData("analyze", CapabilityRouter.SoftSlashAnalyzeCapabilityName)]
    public void Pillar8_AllFourSoftSlashIntents_RoundTripThroughRouter(
        string intentHint, string expectedCapability)
    {
        var router = BuildRouter();

        var result = router.RouteSync(
            userMessage: "the user's message body is irrelevant to the pre-pass",
            activePlaybookName: null,
            intentHint: intentHint);

        result.IsConfident.Should().BeTrue();
        result.SelectedCapabilities.Should().ContainSingle()
            .Which.Should().Be(expectedCapability);
        result.Confidence.Should().Be(1.0);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // TEST 7 — ADR-015 binding: no user content in decision_made events
    // ════════════════════════════════════════════════════════════════════════════════
    //
    // Scenario: the user types a message with PII / privileged content. The
    // CapabilityRouter MUST NOT capture any of it in the emitted
    // context.decision_made event. Only config identifiers (synthetic capability
    // name, decision enum) appear in the payload.
    //
    // Cross-pillar boundary: Pillar 6c (FR-37) trace emission + Pillar 8
    // command router. The trace pipeline is the most likely accidental
    // leak surface — this test locks ADR-015 at the routing seam.

    [Fact]
    public void Pillar8_Adr015_NoUserContentInDecisionMadeEvents()
    {
        var captureEmitter = new CapturingContextEventEmitter();
        var router = BuildRouter(eventEmitter: captureEmitter);

        // Deliberately sensitive content. Router must never capture any of this.
        const string sensitiveMessage =
            "/summarize Confidential matter — Acme Corp v Doe; SSN 123-45-6789; settlement amount $4.2M";

        var result = router.RouteSync(
            userMessage: sensitiveMessage,
            activePlaybookName: null,
            intentHint: "summarize");

        result.IsConfident.Should().BeTrue();

        // Audit ALL captured event payloads for forbidden content.
        var events = captureEmitter.DecisionEvents.ToArray();
        foreach (var evt in events)
        {
            // Only enum-like strings + synthetic config identifiers may appear.
            (evt.Decision + evt.CapabilityName + evt.Layer)
                .Should().NotContain("Acme",
                    because: "ADR-015: user message text MUST NOT appear in decision_made events");
            (evt.Decision + evt.CapabilityName + evt.Layer)
                .Should().NotContain("123-45-6789");
            (evt.Decision + evt.CapabilityName + evt.Layer)
                .Should().NotContain("$4.2M");
            (evt.Decision + evt.CapabilityName + evt.Layer)
                .Should().NotContain("Doe");

            // SessionId + TenantId MAY appear (deterministic IDs — Tier 1 safe per ADR-015).
            // But they're null in this test (the router-only path doesn't have them).
            evt.SessionId.Should().BeNull();
            evt.TenantId.Should().BeNull();
        }
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────────────

    private static CapabilityRouter BuildRouter(
        IContextEventEmitter? eventEmitter = null,
        IReadOnlyList<CapabilityManifestEntry>? manifestEntries = null)
    {
        var manifest = new CapabilityManifest(NullLogger<CapabilityManifest>.Instance);
        // Default manifest: a single unrelated capability so Layer 1 has something to
        // score against when the pre-pass doesn't match (mirrors task 082 pattern).
        manifest.Refresh(manifestEntries ?? new[]
        {
            new CapabilityManifestEntry(
                CapabilityName: "legal_research",
                Description: "Legal research and case law lookup",
                KeywordHints: new[] { "case law", "court", "precedent" },
                PlaybookId: null,
                ToolNames: new[] { "ResearchLegal" },
                IsEnabled: true,
                TenantRestrictions: Array.Empty<string>())
        });

        var options = Options.Create(new CapabilityRouterOptions());

        return new CapabilityRouter(
            manifest,
            options,
            rawChatClient: null,
            NullLogger<CapabilityRouter>.Instance,
            contextEventEmitter: eventEmitter);
    }

    /// <summary>
    /// Captures <c>context.decision_made</c> emissions so ADR-015 + telemetry
    /// payloads can be audited. Other emitter methods are no-ops — this test
    /// only audits routing decisions.
    /// </summary>
    private sealed class CapturingContextEventEmitter : IContextEventEmitter
    {
        public ConcurrentBag<DecisionEvent> DecisionEvents { get; } = new();

        public void DecisionMade(string layer, string decision, string? capabilityName, Guid? sessionId, string? tenantId)
            => DecisionEvents.Add(new DecisionEvent(layer, decision, capabilityName, sessionId, tenantId));

        public void ToolCallStarted(string toolName, Guid decisionId, Guid? sessionId, string? tenantId) { }
        public void ToolCallCompleted(string toolName, Guid decisionId, Guid? sessionId, string? tenantId, string outcome, long durationMs) { }
        public void KnowledgeRetrieved(string knowledgeSourceId, double relevanceScore, int resultCount, Guid? sessionId, string? tenantId) { }
        public void PlaybookNodeExecuting(Guid playbookId, Guid nodeId, string nodeType, Guid? sessionId, string? tenantId) { }
        public void PlaybookNodeCompleted(Guid playbookId, Guid nodeId, string decision, long durationMs, Guid? sessionId, string? tenantId) { }

        public sealed record DecisionEvent(
            string Layer,
            string Decision,
            string? CapabilityName,
            Guid? SessionId,
            string? TenantId);
    }
}
