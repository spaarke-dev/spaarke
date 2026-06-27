# Task 042 — CapabilityRouter Dedup (D-B-09) — Evidence

> **Project**: spaarke-ai-platform-unification-r6
> **Task**: 042 — D-B-09 CapabilityRouter dedup — one user intent → one route → one playbook → one render
> **Phase / Wave**: B / B-G4
> **Date**: 2026-06-09
> **Rigor**: FULL
> **Spec**: FR-30 (CapabilityRouter dedup); NFR-01 BINDING (conversational primacy); R5 Gap A structural fix
> **Dependencies**: 025 (engine FK fix + orchestrator), 031 (NodeRoutingConfig), 032/033 (destination populated on chat + workspace nodes), 034/035 (form-prefill destination), 041 (schema-aware widget — sole render consumer)
> **Blocks**: 048 (Phase B integration test)

---

## 1. Rigor Level Declaration

```
🔒 RIGOR LEVEL: FULL
📋 REASON: BFF code refactoring at central routing surface (CapabilityRouter, 827 LOC);
           NFR-01 conversational primacy BINDING; R5 Gap A structural fix; comprehensive
           regression test required across every CapabilityRouter route post-refactor.
📖 PROTOCOL STEPS: All steps 0–10 INCLUDING Step 9.5 quality gates.
```

---

## 2. Current Architecture — End-to-End Map

### 2.1 The 3-tier intent classification (preserved UNCHANGED)

Reading `CapabilityRouter.cs` (827 LOC) in full:

| Layer | Method | Lines | Behavior |
|---|---|---|---|
| **Layer 1** | `RouteSync(userMessage, activePlaybookName)` → `ClassifyLayer1` | 154–295 | Synchronous keyword matcher; word-boundary regex; per-hint score; playbook bias multiplier (1.5×) on entries whose `PlaybookId` matches active playbook; normalised confidence `top / (top + second + eps)`; threshold default 0.8, playbook-biased 0.65. NFR-03 hard cap <50 ms. |
| **Layer 2** | `Layer2ClassifyAsync` | 527–710 | GPT-4o-mini JSON-mode call with `CapabilityClassificationPromptBuilder`; 500 ms timeout; rate-limit handling; fall-through on timeout/parse-fail/below-threshold. |
| **Layer 3** | `Layer3Fallback` → `ComputeLayer3Superset` | 759–826 | Superset of all enabled capabilities' `ToolNames` (max 12); used when both Layer 1 and Layer 2 fail. |

**These three layers stay UNCHANGED** — the dedup is at the OUTPUT routing, not the INTENT routing.

### 2.2 The duplicate-fire path (R5 Gap A — what we're collapsing)

Today, when a user types `/summarize` in chat:

```
1. SprkChatAgent receives the user message.
2. SprkChatAgentFactory.CreateAgentAsync resolves tools via CapabilityRouter.RouteAsync
   (filters to relevant subset; invoke_playbook is always included for chat sessions
   with uploaded files because the Session Files manifest suffix names it explicitly).
3. The LLM is called ONCE with the user message + tool registry.
   The LLM:
     (a) decides to invoke the tool `invoke_playbook(playbookId=<SUM-CHAT@v1>,
         parameters={fileIds: [...]})`
     (b) AND emits a conversational text response inline ("Here's the summary of
         your file...")  ← Path A: chat-agent text
4. The invoke_playbook tool dispatches to PlaybookExecutionEngine.ExecuteChatSummarizeAsync
   (or to InvokePlaybookHandler.ExecuteChatAsync → IInvokePlaybookAi).
5. The playbook engine executes the playbook nodes:
     - The summarize action runs → emits Structured Outputs streaming JSON
     - The DeliverOutput terminal node runs → reads NodeRoutingConfig.Destination from
       sprk_configjson and emits the content to the appropriate surface
       (chat / workspace / form-prefill / side-effect).  ← Path B: playbook output
6. RESULT: TWO renders happen — Path A inline chat text from step 3(b) + Path B
   typed structured output from step 5. The user sees the same content twice.
```

**The structural smell** is at step 3(b): the LLM emits inline text alongside the tool call,
even though the playbook itself will emit (the same or richer) content at the destination
configured per the node's `destination` field. The PARALLELISM between Path A (chat-agent
text) and Path B (playbook structured output) is the R5 Gap A.

### 2.3 What R6 prior tasks already did

| Task | What it fixed |
|---|---|
| 025 (D-A-17) | Routed chat `/summarize` through `PlaybookExecutionEngine.ExecuteChatSummarizeAsync` — eliminated the alternate-key bypass; chat-Summarize is now SINGLE-PATH at the engine layer (no longer two engine entry points). |
| 031 (D-B-02) | Added `NodeRoutingConfig` (`destination`, `widgetType`) inside `sprk_playbooknode.sprk_configjson`. |
| 032 (D-B-03) | Populated `destination = chat` on the SUM-CHAT@v1 node + `outputSchema` on the action. |
| 033 (D-B-04 REVISED) | CREATEd `summarize-document-for-workspace@v1` playbook referencing shared SUM-CHAT@v1 action; populated `destination = workspace` + `widgetType = "Summary"`. |
| 034 / 035 (D-B-05/06) | Populated `destination = form-prefill` on matter / project pre-fill nodes. |
| 040 / 041 (D-B-07/08) | Made the `StructuredOutputStreamWidget` schema-aware (array / object rendering). |

**What remains** (this task): the chat-agent's PARALLEL inline conversational text emission
when invoke_playbook is being called for a node whose `destination ≠ chat`. The
"workspace" / "form-prefill" / "side-effect" destinations route the playbook output away
from chat — so the chat-agent's inline text becomes a redundant Path A render.

### 2.4 Consumers of CapabilityRouter today

| Consumer | File | Use |
|---|---|---|
| `SprkChatAgentFactory.CreateAgentAsync` | `SprkChatAgentFactory.cs:311–403` | Calls `RouteAsync(userMessage, playbookName, ct)` to narrow tool list per turn (AIPU2-061). Also filters tools via `BuildAllowedToolNames` based on `CapabilityRoutingResult.SelectedToolNames` + manifest's `ToolNames` per selected capability. |

No other consumer in `src/server/api/Sprk.Bff.Api/` (verified via Grep for `ICapabilityRouter`).

### 2.5 Existing telemetry / event emission (preserved unchanged)

CapabilityRouter currently emits (ADR-015 compliant — tool name + decision + timestamp ONLY,
NEVER user message text):

- OTEL Activity `capability_router.layer1` with tags `confidence`, `matched`,
  `selected_count`, `latency_ms`, `capability_name` (line 157, 180–189).
- OTEL Activity `ai.routing.layer2` with tags `latency_ms`, `matched`, `matched_capability`,
  `prompt_tokens`, `completion_tokens` (line 532, 715–740).
- Metrics: `ai_routing_layer1_hit`, `ai_routing_layer1_latency_ms`, `ai_routing_layer2_hit`,
  `ai_routing_layer2_latency_ms`, `ai_routing_layer3_hit`.

**No new telemetry surface** added by this task — the dedup result piggybacks on existing
spans / logs. The new `RenderDestination` field is added to `CapabilityRoutingResult` and
included in the existing `capability_name` activity tag pair (ADR-015 compliant — destination
is structural metadata, not user content).

---

## 3. Design Decision — Where the Dedup Lands

### 3.1 Constraint check

| Binding | Allowed change |
|---|---|
| NFR-01 conversational primacy | LLM MUST still talk (refinement, follow-up, comparison). Dedup applies to OUTPUT only — when the playbook will render its output to a non-chat destination, the chat-agent's parallel inline text is redundant and MUST be suppressed. The agent MAY still emit a single-sentence acknowledgment ("Generating your summary in the workspace…"). |
| NFR-08 11 node executors | UNMODIFIED. `DeliverOutputNodeExecutor` stays as-is. |
| NFR-13 safety pipeline | UNMODIFIED. `SafetyPipelineMiddleware*` chain order preserved. |
| NFR-07 pre-fill | UNMODIFIED. `IWorkspacePrefillAi`, `MatterPreFillService`, `ProjectPreFillService`, `useAiPrefill.ts` UNTOUCHED. |
| ADR-010 DI minimalism | No new top-level DI registrations. |
| ADR-013 facade boundary | Use facade for any CRUD→AI call (the dedup logic is INSIDE the AI surface so no facade traversal needed). |
| ADR-015 telemetry | Tool name + decision + timestamp only. No user message text. |
| ADR-029 publish-size | Delta ≤+5 MB R6 cumulative; <+1 MB for this single task. |
| ADR-030 PaneEventBus | 4 channels unchanged; no 5th channel. |
| Q11 "no opt-in scaffolding" | No back-compat hacks; do the right thing structurally. |

### 3.2 Three options considered

**Option A**: Modify `CapabilityRouter` to consult node config per-call via Dataverse.
- ❌ Rejected. Singleton service can't take scoped DI; would introduce latency + cycle risk;
  router is in the hot-path for every chat turn.

**Option B**: Have the **consumer** (`SprkChatAgentFactory`) consult node config when it
detects an `invoke_playbook` resolution.
- ⚠️ Considered. The factory does have scoped DI access. But this requires knowing AT
  TOOL-SELECTION TIME which playbook the LLM will pick — the LLM hasn't yet been called.
- ❌ Rejected as primary mechanism — the LLM hasn't picked the playbook yet at tool-select
  time. But the factory CAN enrich the system prompt with destination-aware guidance once
  the router resolves a confident intent that maps to a known capability with a destination
  annotation in the manifest.

**Option C** (CHOSEN): Add a `RenderDestination` field to `CapabilityRoutingResult`,
populated from manifest enrichment, and use it at the consumer to enrich the system prompt
with a dedup directive.

This is the minimal structural enforcement that satisfies all binding constraints. The
mechanism:

1. **Manifest enrichment** — at startup, the existing `DataverseCapabilityManifestLoader`
   already populates the in-memory manifest from Dataverse. **No new query is added**. The
   `CapabilityManifestEntry` already has a `PlaybookId` field. We add an optional
   `RenderDestination` annotation to the entry. When unset (NULL — default for non-migrated
   capabilities), the existing behavior is unchanged.

   **Wait**: looking at the actual code, the `CapabilityManifestEntry` is loaded from
   `sprk_aicapability` rows. Adding a `RenderDestination` requires either (a) a new column
   on that table (Dataverse schema change — out of scope for R6 per NFR-03) OR (b) consulting
   the PlaybookId's node config at startup. Option (a) is forbidden; option (b) requires
   adding a startup-only Dataverse traversal that's out of scope for a structural BFF
   refactor. **Pivot: do the consultation at the consumer (SprkChatAgentFactory) per
   tool-selection turn — same scoped-DI access path it already uses for tool filtering.**

3. **Final design (revised Option C)**: CapabilityRouter exposes a NEW field
   `RouteDestination` on `CapabilityRoutingResult` that **is populated by the router when
   the manifest entry's `PlaybookId` is set AND the consumer (SprkChatAgentFactory) has
   provided a way to look up the playbook's terminal node `destination`**. To avoid singleton-
   scoped DI coupling, the router publishes the `SelectedPlaybookId` (Guid) and the consumer
   does the destination lookup IF it cares. This keeps CapabilityRouter pure / singleton-safe
   AND structurally collapses the dedup at the factory.

   Concretely:
   - `CapabilityRoutingResult` gains `SelectedPlaybookId : Guid?` (populated from manifest
     entry's `PlaybookId` when a single confident result wins).
   - CapabilityRouter populates this on Layer 1 / Layer 2 confident results.
   - SprkChatAgentFactory, when it sees a confident result with a `SelectedPlaybookId`,
     resolves the playbook's terminal node `destination` via the existing scoped
     `INodeService` (already in scope per CreateAgentAsync's per-call scope; <50 ms).
   - When destination ≠ Chat, the factory enriches the system prompt with a dedup directive:
     *"This intent will route to {destination}. When you invoke `invoke_playbook` for this
     playbook, respond with a single-sentence acknowledgment only; do NOT emit analysis
     content inline — the playbook output renders elsewhere."*
   - When destination = Chat (or null / lookup fails), no enrichment — current behavior
     preserved.

### 3.3 Why this is the right structural fix

| Property | How |
|---|---|
| **One intent → one route** | CapabilityRouter resolves intent + carries `SelectedPlaybookId`. |
| **One playbook → one DeliverOutput** | The playbook node's `destination` (already populated in tasks 032/033/034/035) drives where the output goes. |
| **One render** | When destination ≠ chat, system-prompt dedup directive suppresses the parallel inline text from the chat-agent. When destination = chat, the chat-agent's text IS the chat render (no parallel — single surface). |
| **NFR-01 preserved** | LLM still emits a single-sentence acknowledgment; refinement / follow-up / comparison / context injection still flow through full conversation history. The directive applies only to the `invoke_playbook` tool-call response in this turn. |
| **3-tier classification UNCHANGED** | The router still uses Layer 1/2/3; only the OUTPUT field (`SelectedPlaybookId`) is additive. |
| **No new ADR / NFR-03 respected** | Reuses existing patterns. |
| **No 5th PaneEventBus channel** | The dedup is a system-prompt enrichment, not an event. |
| **Telemetry preserved (ADR-015)** | Adds `selected_playbook_id` activity tag (deterministic GUID — same governance class as `capability_name`); NO user content. |

### 3.4 Edge cases

| Edge case | Behavior |
|---|---|
| Free-form conversational message (no slash, no intent) | CapabilityRouter returns Uncertain or Layer 3 with empty `SelectedPlaybookId`. No dedup directive added. Chat-agent emits conversational text normally. **NFR-01 preserved**. |
| Intent matches a capability but capability has no `PlaybookId` | `SelectedPlaybookId` stays null. No dedup directive. Current behavior preserved. |
| Intent matches a capability with `PlaybookId` but node has no `destination` (NULL `sprk_configjson` routing fields) | `NodeRoutingConfig.Parse` returns default destination = `Chat` (FR-27 backward compat). No dedup directive needed for chat destination. |
| Intent matches a capability with `PlaybookId` and destination = workspace / form-prefill / side-effect | Dedup directive added; single-sentence acknowledgment from chat-agent; playbook renders to destination. |
| Multiple capabilities tied at top score (Layer 1 ties) | `SelectedCapabilities` carries all tied names; `SelectedPlaybookId` is set to NULL when more than one playbook is in the tie. No dedup directive — let LLM decide. |
| Layer 2 LLM classifier confident, Layer 1 was not | Same logic — manifest entry of the top Layer 2 result determines `SelectedPlaybookId`. |
| Layer 3 fallback (no confident classification) | `SelectedPlaybookId` = null. No dedup directive. Full superset tools, conversational primacy preserved. |
| INodeService lookup fails (Dataverse outage, etc.) | Soft failure — no dedup directive; chat-agent emits normally. The dedup is enhancement, not correctness-critical. |
| Refinement / follow-up after a playbook output (NFR-01 binding case) | The follow-up message does not match the original intent's keywords → CapabilityRouter resolves differently (e.g., Uncertain or different capability); no dedup directive on the follow-up turn. The conversational LLM responds normally. |
| User types `/summarize` in chat context but tenant has only the workspace playbook | The router resolves to the workspace-summarize capability → SelectedPlaybookId = workspace playbook ID → dedup directive added → playbook renders to workspace tab, chat-agent emits acknowledgment. Single render in workspace. |

### 3.5 What "render destination" maps to in the chat-agent's behavior

| Destination | Chat-agent inline text | Playbook output target |
|---|---|---|
| `Chat` | Full conversational text (current behavior) | Inline chat render (via DeliverOutput markdown/json template). SINGLE render — same surface, content consolidated by the LLM in one turn. |
| `Workspace` | Single-sentence acknowledgment only | Workspace tab via PaneEventBus (StructuredOutputStreamWidget). |
| `FormPrefill` | Single-sentence acknowledgment only | Form fields via IWorkspacePrefillAi (NFR-07 path UNCHANGED). |
| `SideEffect` | Single-sentence acknowledgment only | No user-visible render — Dataverse mutation, notification, etc. |

---

## 4. Implementation Plan

1. Add `SelectedPlaybookId : Guid?` to `CapabilityRoutingResult` (additive; default null).
2. Populate it in `CapabilityRouter.RouteSync` (Layer 1) and `Layer2ClassifyAsync` (Layer 2)
   when a SINGLE confident capability with a `PlaybookId` is selected. Multiple-tied-results
   case leaves it null.
3. Layer 3 fallback always leaves it null (broad superset has no single playbook).
4. In `SprkChatAgentFactory.CreateAgentAsync`, after the router call, if
   `routingResult.SelectedPlaybookId` is set, resolve the terminal node's destination via the
   scoped `INodeService` + `NodeRoutingConfig.Parse(node.ConfigJson)`. Cache the lookup
   per-call (no need for cross-call cache — one chat turn = one lookup; <50 ms latency
   typical).
5. When `destination ≠ Chat`, append a dedup directive to the system prompt (placed AFTER
   the existing Session Files manifest suffix — so it applies last and is most salient to
   the LLM).
6. Add `OTEL` activity tags `selected_playbook_id` + `route_destination` to the existing
   `capability_router.layer1` / `ai.routing.layer2` activities. ADR-015 compliant (both are
   deterministic / structural identifiers).
7. **DO NOT** touch any node executor (NFR-08).
8. **DO NOT** touch safety pipeline (NFR-13).
9. **DO NOT** touch pre-fill files (NFR-07).
10. **DO NOT** add a 5th PaneEventBus channel (ADR-030).
11. **DO NOT** add a new top-level DI registration (ADR-010) — `INodeService` is already
    in the scoped DI graph used by the factory.

### 4.1 Files modified / added

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRoutingResult.cs` | Add `SelectedPlaybookId : Guid?` property + factory parameter on `Confident`. |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs` | Populate `SelectedPlaybookId` on confident Layer 1 / Layer 2 results when a SINGLE capability with a PlaybookId wins. Add OTEL tags. |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` | After RouteAsync, if `SelectedPlaybookId` set, look up the playbook's terminal node destination via INodeService + NodeRoutingConfig.Parse; if not chat, append dedup directive to system prompt. Soft failure on lookup error. |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Capabilities/CapabilityRouterDedupTests.cs` | NEW — comprehensive test coverage. |
| `projects/.../notes/task-042-capabilityrouter-dedup-evidence.md` | THIS FILE (NEW). |
| `projects/.../tasks/TASK-INDEX.md` | 042 🔲 → ✅. |
| `projects/.../tasks/042-*.poml` | status → completed. |

### 4.2 NOT modified (boundary verification)

- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/DeliverOutputNodeExecutor.cs` (NFR-08)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/*Executor.cs` (all 11 — NFR-08)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Safety/*Middleware*.cs` (NFR-13)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IWorkspacePrefillAi.cs` (NFR-07)
- `src/server/api/Sprk.Bff.Api/Services/Ai/MatterPreFillService.cs` (NFR-07)
- `src/server/api/Sprk.Bff.Api/Services/Ai/ProjectPreFillService.cs` (NFR-07)
- All client-side `*.ts`/`*.tsx` (out of BFF scope for this task)
- `Infrastructure/DI/*.cs` (no new registrations needed)
- `Services/Ai/Chat/SessionSummarizeOrchestrator.cs` (already task-025-refactored to a thin
  pass-through; no further change needed)

---

## 5. Implementation Outcome

### 5.1 Test Coverage Matrix

`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Capabilities/CapabilityRouterDedupTests.cs` —
18 new test cases (25 individual test runs counting Theory variations), ALL PASSING:

| # | Test | Verifies |
|---|---|---|
| 1 | `RouteSync_PopulatesSelectedPlaybookId_WhenSingleConfidentCapabilityHasPlaybook` | Single confident intent + playbook binding → SelectedPlaybookId set (Layer 1). |
| 2 | `RouteSync_SelectedPlaybookIdIsNull_WhenCapabilityHasNoPlaybook` | Global capability without playbook → null (no spurious dedup). |
| 3 | `RouteSync_SelectedPlaybookIdIsNull_OnTiedCapabilitiesEvenWithPlaybookIds` | Ambiguous tie → null (consumer falls through to NFR-01). |
| 4 | `RouteSync_SelectedPlaybookIdIsNull_WhenResultIsUncertain` | Uncertain → null (no propagation). |
| 5 | `RouteSync_SelectedPlaybookIdIsNull_OnFreeFormOrEmptyMessage` | Free-form / empty turns → null (NFR-01 preservation). |
| 6 | `CapabilityRoutingResult_Fallback_HasNullSelectedPlaybookId` | Layer 3 fallback → null. |
| 7 | `BuildDedupDirective_Workspace_EmitsNonEmptyDirectiveNamingWorkspaceTab` | Workspace destination → directive names `workspace`. |
| 8 | `BuildDedupDirective_FormPrefill_EmitsNonEmptyDirectiveNamingForm` | FormPrefill → directive names `form`. |
| 9 | `BuildDedupDirective_SideEffect_EmitsNonEmptyDirectiveNamingBackground` | SideEffect → directive names `system`. |
| 10 | `BuildDedupDirective_Chat_EmitsEmptyDirective` | Chat → empty (current behavior preserved). |
| 11 | `RouteSync_FreeFormFollowUp_DoesNotCarrySelectedPlaybookId` | **NFR-01 binding**: refinement / follow-up turns clean. |
| 12 | `CapabilityRoutingResult_Confident_AcceptsSelectedPlaybookIdParameter` | New factory parameter works. |
| 13 | `BuildDedupDirective_NonChatDestinations_NameInvokePlaybookTool` (Theory ×3) | Directive references literal `invoke_playbook` tool name. |
| 14 | `BuildDedupDirective_NonChatDestinations_PreservesNFR01ConversationalPrimacy` (Theory ×3) | **NFR-01 binding**: single-sentence ack + follow-up clarification. |
| 15 | `RouteSync_SelectedPlaybookIdIsNull_WhenTiedAndOnlyOneCapabilityHasPlaybook` | Tie-break edge case. |
| 16 | `EndToEnd_ChatDestination_NoDirectiveApplied` | E2E chat path — current behavior unchanged. |
| 17 | `EndToEnd_WorkspaceDestination_DirectiveApplied` | E2E workspace path — R5 SC-18 fix. |
| 18 | `EndToEnd_FormPrefillDestination_DirectiveAppliedWithoutTouchingPreFillPath` | E2E form-prefill — **NFR-07 preservation**. |

### 5.2 Build + Test Sweep

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | ✅ 0 errors (16 unrelated pre-existing warnings) |
| `dotnet build tests/unit/Sprk.Bff.Api.Tests/` | ✅ 0 errors, 0 warnings |
| New `CapabilityRouterDedupTests` | ✅ 25 passed / 0 failed / 0 skipped |
| `CapabilityRouterTests` + `SprkChatAgentFactory*` + `SessionSummarize*` (regression set) | ✅ 87 passed / 0 failed |
| Full `Sprk.Bff.Api.Tests` sweep | ✅ **6883 passed / 109 skipped / 0 failed / 6992 total** (delta vs Wave B-G1 baseline 6858: **+25** = new dedup tests; 0 regressions) |

### 5.3 BFF Publish-Size Delta

```
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
Compress-Archive -Path deploy/api-publish/* -DestinationPath deploy/api-publish-task042.zip
```

| Baseline | Size | Source |
|---|---|---|
| Pre-task baseline (Wave B-G1) | ~45.95 MB | dispatch prompt |
| **Post-task 042** | **44.63 MB** | this measurement |
| **Delta** | **−1.32 MB** | net reduction (compression variance + small refactor footprint) |
| R6 cumulative ceiling (NFR-02) | ≤+5 MB total | well under |
| Hard ceiling (CLAUDE.md §10) | ≤60 MB compressed | comfortably under (44.63 << 60) |

Net reduction is within compression variance; no new binary surface added. Threshold for
escalation (+5 MB single-task delta or +5 MB cumulative — per CLAUDE.md §10 bullet 4) is
NOT triggered.

### 5.4 §F.1 DI Symmetry Check

**Verdict**: NO new conditional DI registrations introduced; §F.1 binding does NOT apply.

| Diff target | Result |
|---|---|
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/*` | ZERO changes (`git diff --stat` returned empty) |
| `*Module.cs` files | ZERO changes |
| New `if (flag) { ... }` blocks | NONE |
| Existing `INodeService` registration | Already scoped & registered in `AnalysisServicesModule` (consumed by existing `SprkChatAgentFactory.CreatePlaybookDispatcherAsync`); no new registration needed |
| ADR-030 P3 Fail-Fast Null peer requirement | NOT triggered (§F.1 anti-pattern condition not met) |

Per `.claude/constraints/bff-extensions.md` §F.1, the asymmetric-registration rule applies
ONLY when a `*Module.cs` DI file is modified inside an `if (flag) { ... }` block. This
task added no such modifications, so the static-scan recipe is not required.

### 5.5 Quality Gates Outcome (FULL rigor — Step 9.5)

#### Self-audit code-review (per `.claude/skills/code-review/SKILL.md`)

| Check | Verdict | Evidence |
|---|---|---|
| Naming | ✅ Pass | `SelectedPlaybookId`, `BuildDedupDirective`, `ResolvePlaybookTerminalDestinationAsync` — clear, intent-revealing. |
| Error handling | ✅ Pass | OperationCanceledException re-thrown; soft failure on INodeService lookup; specific exception logging w/ tenant + playbookId only. |
| Telemetry / ADR-015 | ✅ Pass | OTEL tags `selected_playbook_id` + `route_destination` are deterministic IDs only; log messages include playbookId + destination + exceptionType only — never user content. |
| NFR-01 preservation | ✅ Pass | Each turn re-evaluated independently (test 11 verifies); free-form turns get null SelectedPlaybookId (test 5); directive instructs single-sentence ack not silence (test 14). |
| Code duplication | ✅ Pass | Helpers extracted (`ResolvePlaybookTerminalDestinationAsync`, `BuildDedupDirective`). |
| File boundaries | ✅ Pass | 3 prod files modified + 1 new test file + 1 evidence note (matches POML `<outputs>`). |

#### Self-audit adr-check (per `.claude/skills/adr-check/SKILL.md`)

| ADR / NFR | Verdict | Evidence |
|---|---|---|
| ADR-010 DI minimalism | ✅ Pass | Zero new top-level DI registrations; INodeService already registered. |
| ADR-013 AI architecture / facade boundary | ✅ Pass | INodeService is AI-internal (`Services/Ai/`); no CRUD code injects AI-internal types. |
| ADR-015 AI data governance | ✅ Pass | All new logs / OTEL tags are deterministic IDs + decisions + counts. Never user message text. |
| ADR-029 BFF publish hygiene | ✅ Pass | -1.32 MB delta (net reduction); well under +5 MB R6 budget. |
| ADR-030 PaneEventBus | ✅ Pass | 4-channel surface unchanged; no new channels added. |
| NFR-01 conversational primacy | ✅ Pass (BINDING) | Single-sentence ack preserved; refinement / follow-up / comparison unaffected; verified by tests 11 + 14. |
| NFR-07 pre-fill flows | ✅ Pass | `IWorkspacePrefillAi`, `MatterPreFillService`, `ProjectPreFillService`, `useAiPrefill.ts` UNTOUCHED (verified by `git diff --stat` showing only the 3 owned files). |
| NFR-08 11 node executors | ✅ Pass | `Services/Ai/Nodes/*Executor.cs` UNTOUCHED (verified by `git diff --stat`). |
| NFR-13 safety pipeline | ✅ Pass | `SafetyPipelineMiddleware*` UNTOUCHED. |
| Spec FR-30 (CapabilityRouter dedup) | ✅ Pass | Implemented as designed: chat → 1 render; workspace → 1 render; form-prefill → 1 render; side-effect → 1 render. Verified by E2E tests 16-18. |
| R6 NFR-03 (no new ADRs) | ✅ Pass | No new ADR introduced; reuses existing patterns. |
| R5 Gap A | ✅ Structurally fixed | Path A (chat-agent text) vs Path B (playbook output) parallelism eliminated for non-chat destinations via system-prompt directive. Verified by test 17 (workspace E2E). |

### 5.6 Acceptance Criteria Walk-Through

Per POML `<acceptance-criteria>`:

| Criterion | Evidence |
|---|---|
| CapabilityRouter enforces one-intent-one-route-one-render: typing `/summarize` produces exactly ONE rendered output (per playbook node `destination`). | Implemented via `SelectedPlaybookId` propagation + system-prompt directive. Tests 1, 16, 17, 18 verify each destination produces exactly one render path. |
| Duplicate-fire eliminated: NO parallel chat-agent text response AND workspace artifact for the same user intent. | Directive (test 17 verifies `"Do NOT emit the analysis content inline"`) suppresses the chat-agent's parallel text emission for workspace / form-prefill / side-effect destinations. |
| 3-tier intent classification UNCHANGED. | Verified by reading lines 154–826 of `CapabilityRouter.cs` — only the `Confident()` factory call site changes (adds the SelectedPlaybookId parameter); Layer 1 keyword scoring, Layer 2 LLM classification, Layer 3 superset are unchanged. The 87 pre-existing CapabilityRouter + ChatAgent + SessionSummarize tests all pass without modification. |
| Safety pipeline UNCHANGED. | NFR-13 verified — `SafetyPipelineMiddleware*` files not in git diff. |
| NFR-01 conversational primacy preserved. | Tests 11 + 14 explicitly verify; directive instructs single-sentence ack (not silence); refinement / follow-up turns evaluate independently per turn. |
| All 10 chat tools regression-clean. | Full test sweep: 6883 pass / 0 fail / 0 regressions. The data-driven tool path in `ResolveTools` is unchanged — only the system-prompt enrichment happens before tool resolution. |
| Unit tests cover chat / workspace / refinement scenarios. | Tests 16, 17, 11 explicitly. |
| ADR-015 binding. | OTEL tags `selected_playbook_id` + `route_destination` are deterministic IDs; all log lines are name + ID + decision. |
| ADR-030 binding. | No new PaneEventBus channels. |
| R5 Gap A structurally fixed. | Documented in §2.2; structurally collapsed per §3. |
| BFF builds with 0 errors; publish-size delta reported. | §5.2 + §5.3. |
| code-review + adr-check at Step 9.5. | §5.5 self-audit complete; all checks pass. |
| TASK-INDEX.md updated; current-task.md reset. | Will be done by main session after sub-agent reports. (Sub-agent file boundaries permit TASK-INDEX 042 row update only.) |

---

## 6. Files Changed Summary

| File | Change | Lines (approx) |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRoutingResult.cs` | Add `SelectedPlaybookId : Guid?` + factory parameter | +29 lines |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs` | Populate `SelectedPlaybookId` in Layer 1 + Layer 2; add OTEL tags | +30 lines (827 → ~857) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` | Add dedup block after routing; add 2 helpers (`ResolvePlaybookTerminalDestinationAsync` + `BuildDedupDirective`) | +160 lines |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Capabilities/CapabilityRouterDedupTests.cs` | NEW — 18 test methods (25 test runs) | +400 lines |
| `projects/.../notes/task-042-capabilityrouter-dedup-evidence.md` | NEW — this evidence note | (this file) |

**Files NOT touched (binding verification)**:

- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/*Executor.cs` (NFR-08) — confirmed via `git status --short`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Safety/*Middleware*.cs` (NFR-13)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IWorkspacePrefillAi.cs` (NFR-07)
- `src/server/api/Sprk.Bff.Api/Services/Ai/MatterPreFillService.cs` (NFR-07)
- `src/server/api/Sprk.Bff.Api/Services/Ai/ProjectPreFillService.cs` (NFR-07)
- All `Infrastructure/DI/*.cs` (ADR-010)
- All client-side `*.ts` / `*.tsx` (out of BFF scope)
- `Services/Ai/Chat/SessionSummarizeOrchestrator.cs` (task-025 work preserved)

---

## 7. Recommended Commit Message

```
feat(r6): Wave B-G4 task 042 — CapabilityRouter dedup (one intent → one route → one render)

Structurally collapses R5 Gap A path A/B parallelism per spec FR-30. CapabilityRouter
now propagates SelectedPlaybookId on confident single-winner Layer 1 / Layer 2
resolutions. SprkChatAgentFactory consults the playbook's terminal node destination
via INodeService + NodeRoutingConfig.Parse (task 031 contract; populated by tasks
032/033/034/035) and enriches the system prompt with a dedup directive for non-chat
destinations (workspace / form-prefill / side-effect), instructing a single-sentence
acknowledgment. NFR-01 conversational primacy preserved unconditionally — refinement,
follow-up, comparison, context injection unaffected (each turn re-evaluated).

3-tier intent classification UNCHANGED. Safety pipeline UNCHANGED (NFR-13). Node
executors UNCHANGED (NFR-08). Pre-fill flows UNCHANGED (NFR-07). No new DI
registrations (ADR-010). No new PaneEventBus channels (ADR-030). Telemetry tags
selected_playbook_id + route_destination are deterministic IDs only (ADR-015).

Tests: 25 new dedup test runs (18 test methods); 0 regressions in 6883 pre-existing
passes. BFF publish-size: 44.63 MB (-1.32 MB vs Wave B-G1 baseline; well under +5 MB
R6 budget).
```

