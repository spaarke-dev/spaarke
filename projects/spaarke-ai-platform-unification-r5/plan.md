# Spaarke AI Platform Unification R5 — Implementation Plan

> **Status**: Ready for task decomposition
> **Created**: 2026-06-03 (late)
> **Source**: `spec.md` v1 (post-Insights-r2-v1.1-negotiation)
> **Phases**: 3 (Platform extensions → Vertical slice + Insights tool integration → Polish + future-use validation)
> **Estimated effort**: ~2–3 engineering weeks
> **Branch**: `work/spaarke-ai-platform-unification-r5` (on top of `origin/master`)

---

## Architecture Context

### Discovered Resources (compiled from comprehensive 10-layer inventory done during design)

**Binding ADRs** (20 referenced; bold = highest-leverage):
ADR-001, ADR-006, ADR-007, ADR-008, ADR-009, **ADR-010** (DI minimalism — ≤15 `Program.cs` lines via feature modules; concretes by default), ADR-012, **ADR-013** (AI architecture — BFF-only; agents via `SprkChatAgentFactory`), ADR-014 (`tenantId` isolation), ADR-016 (rate limiting), **ADR-018** (Feature Flag Scope Discipline — added 2026-06-03 from R5 design review), ADR-019 (ProblemDetails), ADR-021 (Fluent UI v9), ADR-022 (React 19), ADR-026 (Code Page build), **ADR-028** (Spaarke Auth v2 — no token snapshots), ADR-029 (BFF publish hygiene), **ADR-030** (PaneEventBus closed 4 channels; event types additive), ADR-031 (stage lifecycle), **ADR-032** (BFF Null-Object kill-switch — NOT applicable to R5).

**Skills applicable**:
- `task-execute` (MANDATORY per CLAUDE.md §4 for every R5 task)
- `adr-aware`, `adr-check`, `code-review`
- `pcf-deploy`, `code-page-deploy`, `bff-deploy` (deployment helpers)
- `context-handoff` (per CLAUDE.md §5 checkpointing)
- `worktree-sync`, `pull-from-github`, `push-to-github` (workflow)
- `dataverse-deploy`, `dataverse-create-schema` (seed action + playbook config)
- `conflict-check` (per PR-01)
- `azure-deploy` (Bicep — for new `spaarke-session-files` AI Search index)

**Architecture docs (load-bearing)**:
- `docs/architecture/AI-ARCHITECTURE.md` — `AnalysisOrchestrationService`, `IStreamingAnalysisToolHandler`, four-tier taxonomy
- `docs/architecture/playbook-architecture.md` — `PlaybookOrchestrationService`, `INodeExecutor` framework, streaming
- `docs/architecture/chat-architecture.md` — `SprkChatAgent`, middleware stack, `CompoundIntentDetector`, `PaneEventBus`
- `docs/architecture/rag-architecture.md` — hybrid search, `EmbeddingCache`, `RagIndexingPipeline`
- `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` — two-wrapper architecture
- `docs/architecture/SPAARKEAI-COMPONENT-MODEL.md` — component inventory
- `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` — workspace pane pipeline
- `docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md` — D-04 decline-to-find, artifact envelope
- `docs/architecture/auth-AI-azure-resources.md` — Azure resources

**Cross-cutting constraints**:
- `.claude/constraints/bff-extensions.md` — binding BFF additions governance (CLAUDE.md §10)
- `docs/standards/CHAT-ATTACHMENT-POLICY.md` — 25 MB binary cap, MIME allow-list, total-text caps
- `notes/insights-r2-coordination.md` — cross-project coordination (REQUIRED READING)
- `notes/insights-engine-assistant-integration-brief.md` — binding v1.0 contract for `insights.query` consumption
- `notes/insights-engine-contract-v1.1-request.md` — Wave F negotiation outcome + acceptance criteria

**Canonical implementations to follow**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` — orchestration pattern
- `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` — hybrid search + `RagSearchOptions`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` — agent creation + middleware (note: unsealed per ADR-032)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/AssistantToolCallHandler.cs` — chat-tool handler pattern (R5 mirrors for SessionSummarize)
- `src/server/api/Sprk.Bff.Api/Api/Insights/InsightsAssistantEndpoint.cs` — SSE-streaming endpoint pattern
- `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreviewDialog.tsx` — renderer source for extraction
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-document-viewer-widget.ts` — Workspace widget registration pattern
- `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBus.ts` — additive event-type pattern (ADR-030)
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/PlaybookGalleryWidget.tsx` — Context widget pattern
- `src/solutions/LegalWorkspace/src/components/SummarizeFiles/summarizeService.ts` — SSE parsing reference

**Scripts available**:
- `scripts/Deploy-Playbook.ps1` — JPS playbook + action seed deployment (used by Insights; reusable for R5 Summarize-for-Chat seed)
- `scripts/Migrate-InsightsIndexScopeShape.ps1` — index migration reference (similar pattern for `spaarke-session-files` provisioning)
- `scripts/Deploy-PCFWebResources.ps1` — not applicable to R5
- `scripts/Test-SdapBffApi.ps1` — applicable for BFF endpoint smoke tests

**Schema validation note**: R5 adds zero new Dataverse entities. New `sprk_analysisaction` seed row + new `sprk_analysisplaybook` configuration are data, not schema. Existing schemas (`sprk_analysisaction`, `sprk_analysisplaybook`, `sprk_aichatsession`) cover R5 needs.

---

## Phase Breakdown

### Phase 1: Platform Extensions (~1 week)

**Purpose**: Extend existing AI infrastructure with R5-required additive capabilities. All work is independent of Insights team Wave F (can proceed in parallel).

**Phase gating**: Phase 1 ships when: BFF builds clean; new `spaarke-session-files` index provisioned on Spaarke Dev; SSE `FieldDelta` events produce correct progressive output for the existing Summarize wizard path (back-compat verified); session-files cleanup job runs without error; tests pass; publish-size delta < +0.5 MB.

**Deliverables**:

1. **D1-01 Session-scoped AI Search index** — Provision `spaarke-session-files` index. Bicep + index JSON schema (same shape as `spaarke-knowledge-index-v2`: 3072-dim HNSW + BM25 + semantic config). Required fields: `tenantId`, `sessionId`, plus existing chunk schema. Per-tenant routing decision deferred (Phase 1 spike → assume per-tenant initially).
   - **Files affected**: `infrastructure/ai-search/spaarke-session-files.json` (new); `infrastructure/bicep/` module (extend)
   - **Tasks**: ~1 task
   - **Acceptance**: Index visible in Azure Portal; schema matches knowledge-index-v2; `tenantId` filter works

2. **D1-02 `RagSearchOptions.sessionId` filter extension** — Additive parameter on existing `RagSearchOptions`. `RagService.SearchAsync()` routes to `spaarke-session-files` index when `sessionId` provided; falls back to existing tenant-scoped index when absent.
   - **Files affected**: `src/server/api/Sprk.Bff.Api/Services/Ai/RagSearchOptions.cs`; `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs`
   - **Tasks**: ~1 task
   - **Acceptance**: Backwards-compatible (existing wizard query path unchanged); session-scoped query returns only docs with matching `sessionId`

3. **D1-03 `RagIndexingPipeline` parameterization** — Pipeline writes to either customer-corpus index OR session-files index, parameterized by call site. New session-files writes carry `tenantId` + `sessionId` per ADR-014.
   - **Files affected**: `src/server/api/Sprk.Bff.Api/Services/Ai/RagIndexingPipeline.cs`
   - **Tasks**: ~1 task
   - **Acceptance**: Both write paths exist; write tests pass; tenant isolation verified

4. **D1-04 `ChatSession.UploadedFiles[]` manifest** — Extend `ChatSession` model with `List<ChatSessionFile> UploadedFiles` (per spec §4.4). Triple-tier persistence inherited (Redis hot 24h + Cosmos warm write-through + Dataverse cold).
   - **Files affected**: `src/server/api/Sprk.Bff.Api/Models/Ai/ChatSession.cs` (or equivalent path)
   - **Tasks**: ~1 task
   - **Acceptance**: Manifest property persists across Redis TTL refresh + Cosmos write-through; cold tier audit unaffected

5. **D1-05 `AnalysisChunk.FieldDelta` variant** — Additive SSE event type (`Type = "delta"`); new `FieldDelta` record (`Path`, `Content`, `Sequence`). Back-compat: existing wizard consumers ignore unknown event types.
   - **Files affected**: `src/server/api/Sprk.Bff.Api/Models/Ai/AnalysisChunk.cs` (or equivalent)
   - **Tasks**: ~1 task
   - **Acceptance**: New variant compiles; serialization round-trips; wizard consumer unchanged behavior

6. **D1-06 Azure OpenAI Structured Outputs mode + incremental JSON parser** — Switch Summarize playbook execution path to use Structured Outputs (JSON Schema mode); implement incremental JSON parser that emits `FieldDelta` events tagged by JSON path as values stream in.
   - **Files affected**: Summarize playbook execution path (specific service TBD per code inventory at task time)
   - **Tasks**: ~1–2 tasks (Structured Outputs setup + JSON streaming parser)
   - **Acceptance**: Summarize produces `delta` events progressively (TL;DR field populated first, then summary, then per-file highlights); final result matches existing wizard output schema

7. **D1-07 Session-files cleanup `IHostedService`** — Background job: scheduled (every 6h default) + triggered immediately on session-end. Deletes `spaarke-session-files` documents for sessionIds not in active Redis session set. Idempotent.
   - **Files affected**: `src/server/api/Sprk.Bff.Api/Services/Ai/SessionFilesCleanupJob.cs` (or equivalent path); DI registration in `AnalysisServicesModule.cs`
   - **Tasks**: ~1 task
   - **Acceptance**: Job runs without error on Spaarke Dev; metrics show eviction count; ADR-010 compliant (no new `Program.cs` line)

8. **D1-08 Telemetry events + cost observability** — New telemetry event: `r5.summarize.invocation` (path: agent_tool|direct_endpoint; file_count; total_tokens; latency_ms; completion_status). Session-files index size metric (per-session document count).
   - **Files affected**: telemetry layer (ApplicationInsights / OpenTelemetry instrumentation in BFF)
   - **Tasks**: ~1 task
   - **Acceptance**: Events visible in App Insights / Kusto; per-session metric queryable

9. **D1-09 Phase 1 tests + BFF publish-size verification** — Unit tests for D1-02 through D1-07. `dotnet publish` measurement; report absolute size + delta vs prior baseline; CVE scan.
   - **Files affected**: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/` (multiple)
   - **Tasks**: ~1 task
   - **Acceptance**: All new tests pass; publish-size delta < +0.5 MB; no new HIGH CVEs

**Phase 1 estimate**: 8–10 tasks, ~5 engineering days

---

### Phase 2: Vertical Slice + Insights Tool Integration (~1.5 weeks)

**Purpose**: Ship the chat-driven Summarize vertical slice AND the Insights tool integration (`insights.query` chat-agent tool). Both follow the "one endpoint per tool capability with internal routing" convention.

**Phase gating**: Phase 2 ships when: end-to-end Summarize flow works on Spaarke Dev (slash command + natural-language tool-call both produce identical output); SSE token streaming populates Workspace tab progressively; Context pane file preview + multi-file selection works; tab persistence + static restoration works; `insights.query` tool registered + both response paths render correctly; R5 lead has signed off on Insights contract v1.0 + recorded D1–D6 decisions; smoke tests pass for both tools against Spaarke Dev synthetic test entities.

**Pre-Phase-2 gate** (per PR-04): R5 lead reviews `projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md` v1.0 and records D1–D6 contract-review decisions in the contract's §10 review log. Closes Insights task 042 sub-task A.5.

**Deliverables**:

#### Summarize vertical slice (D2-01 through D2-12)

1. **D2-01 New `sprk_analysisaction` seed row "Summarize Document for Chat"** — Dataverse data deploy via `scripts/Deploy-Playbook.ps1` (or equivalent). Action carries JPS-formatted system prompt + output schema tuned for streaming-aware output.
   - **Files affected**: New seed JSON / config (TBD); deploy script invocation
   - **Tasks**: ~1 task (data + deploy)
   - **Acceptance**: Action visible in Spaarke Dev Dataverse; system prompt loaded from action row (no `.txt` file); `dataverse-deploy` skill invoked correctly

2. **D2-02 New `sprk_analysisplaybook` configuration** — Playbook record linking the new Summarize action; mirrors existing Summarize playbook structure.
   - **Files affected**: Playbook config JSON
   - **Tasks**: ~1 task
   - **Acceptance**: Playbook visible in Spaarke Dev; linked to D2-01 action

3. **D2-03 New `SessionSummarizeOrchestrator` concrete class** — Encapsulates shared internal method bridging agent-tool path and direct endpoint. Registered inside `AnalysisServicesModule.cs` per ADR-010 feature-module pattern.
   - **Files affected**: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs` (new); `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` (extend)
   - **Tasks**: ~1 task
   - **Acceptance**: Class is concrete (no interface — no genuine seam beyond testing); compiles; unit-tested

4. **D2-04 New `POST /api/ai/chat/sessions/{sessionId}/summarize` endpoint** — Direct endpoint. Auth v2 filter per ADR-028; ProblemDetails errors per ADR-019; delegates to `SessionSummarizeOrchestrator`.
   - **Files affected**: `src/server/api/Sprk.Bff.Api/Api/Chat/SummarizeSessionEndpoint.cs` (new path TBD)
   - **Tasks**: ~1 task
   - **Acceptance**: Endpoint reachable; OBO auth works; SSE streams `FieldDelta` events progressively

5. **D2-05 `InvokeSummarizePlaybookTool` agent-tool function** — Registered via existing tool-registration mechanism on `SprkChatAgent` (per existing `CapabilityRouter` AIPU2-061 pattern). Parameters: `{ fileIds: string[]?, style?: string }`. LLM tool-call routes here for natural-language Summarize.
   - **Files affected**: Tool registration site in `AnalysisServicesModule.cs` or chat-tool setup file
   - **Tasks**: ~1 task
   - **Acceptance**: Tool visible in agent's tool catalog; LLM tool-calls correctly for natural-language test prompts; both paths converge on shared orchestrator

6. **D2-06 New additive PaneEventBus event types** — Per ADR-030 additive event-type discipline: `workspace.streaming_started`, `workspace.field_delta`, `workspace.streaming_complete`, `context.files_staged`, `context.file_selected`. Existing subscribers ignore unknown discriminants.
   - **Files affected**: `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts`
   - **Tasks**: ~1 task
   - **Acceptance**: New event types compile; existing subscribers unaffected; new R5 widgets subscribe correctly

7. **D2-07 `StructuredOutputStreamWidget` (Workspace; schema-driven)** — NEW widget. Schema-driven progressive rendering via `FieldDelta` events. Reusable for BOTH Summarize streaming output AND Insights static response rendering. Renders structured fields (TL;DR, summary, per-file highlights, mentioned parties, call-to-action) progressively. ChatGPT/Claude-style cursor animation.
   - **Files affected**: `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/StructuredOutputStreamWidget.tsx` (new); `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-structured-output-stream-widget.ts` (new)
   - **Tasks**: ~1–2 tasks (widget + registration; tests)
   - **Acceptance**: Widget consumes `FieldDelta` events; renders progressively; handles streaming completion + decline state + empty-result state distinctly

8. **D2-08 Extract `RichFilePreview` renderer core** — Pull iframe + metadata + prev/next + 3-dot menu UI from existing `RichFilePreviewDialog` into reusable primitive. Dialog continues to wrap it for modal use cases. Upgraded `DocumentViewerWidget` (R4 stub) consumes it for Workspace destination.
   - **Files affected**: `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreview.tsx` (new — extracted renderer); `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreviewDialog.tsx` (refactor to wrap extracted renderer); `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DocumentViewerWidget.tsx` (upgrade R4 stub)
   - **Tasks**: ~1 task
   - **Acceptance**: Existing `RichFilePreviewDialog` consumers unaffected; new shared renderer reusable; `DocumentViewerWidget` upgraded from stub to real preview

9. **D2-09 `FilePreviewContextWidget` (Context pane; non-modal)** — NEW widget. Wraps extracted `RichFilePreview` for non-modal Context-pane use. Single-file: inline preview. Multi-file: list of file cards → click swaps active preview. Per-file 3-dot menu reuses existing `DocumentRowMenu` (12 actions; aiSummary + toggleWorkspace + findSimilar relevant).
   - **Files affected**: `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/FilePreviewContextWidget.tsx` (new); `src/client/shared/Spaarke.AI.Widgets/src/registry/register-context-widgets.ts` (extend)
   - **Tasks**: ~1–2 tasks (widget + multi-file list + tests)
   - **Acceptance**: Single + multi-file rendering works; 3-dot menu actions wire to correct dispatches; PaneEventBus dispatch for file selection

10. **D2-10 Slash command `/summarize` semantic extension** — Update `/summarize` description in `DEFAULT_SLASH_COMMANDS` to "Summarize uploaded files or the active document". Intent handler: if `ChatSession.UploadedFiles.length > 0` → route to session-files summarize; else → existing wizard flow; else → emit "upload files" interjection (FR-03 prompt-first path).
    - **Files affected**: `src/client/shared/Spaarke.UI.Components/src/components/SlashCommandMenu/slashCommandMenu.types.ts`; intent-handler in SpaarkeAi shell
    - **Tasks**: ~1 task
    - **Acceptance**: Slash command description updated; dual-mode routing correct; "upload files" interjection appears on prompt-first path

11. **D2-11 Chat-pane orchestration UX** — File chips in Assistant input area; persistent "N files attached" indicator; per-file remove action (also removes from session-files index); inline file confirmations in chat; multi-file combined-summary deterministic interjection.
    - **Files affected**: `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` + ChatInputBar/file-chip helpers
    - **Tasks**: ~1–2 tasks (chips + indicator + remove + interjection)
    - **Acceptance**: File chips visible on upload; remove updates session manifest + index; multi-file interjection appears once

12. **D2-12 "Summarize this only" per-file affordance + UI multi-turn refinement** — Per-file card has "Summarize this only" button (FR-08 UI path). Click dispatches directly to orchestrator with `fileIds: [singleFile]`. Produces new Workspace tab; prior tab remains.
    - **Files affected**: `FilePreviewContextWidget` (extend D2-09) + dispatch wiring
    - **Tasks**: ~1 task
    - **Acceptance**: Button renders per file; click produces new tab; prior tab unaffected

#### Insights tool integration (D2-13 through D2-20)

13. **D2-13 R5 lead contract review + sign-off** — Required gate before Insights tool work. R5 lead reviews `projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md` v1.0 and records D1–D6 decisions in §10 review log (D1=request v1.1 SSE; D2=accept default; D3=accept default; D4=request v1.1 `citations[].href`; D5=R5 client-side confidence badge; D6=accept default; per spec §8.2).
    - **Files affected**: `projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md` (operator action; Cross-worktree edit OR coordination via PR comment)
    - **Tasks**: ~1 task (operator-led; documentation)
    - **Acceptance**: §10 review log shows R5 lead sign-off + 6 decisions recorded; Insights task 042 sub-task A.5 closed

14. **D2-14 `InsightsQueryToolHandler` chat-agent tool function** — Registered on `SprkChatAgent` as `insights.query`. POSTs to `POST /api/insights/assistant/query` (Insights endpoint). Goes through `SprkChatAgentFactory` per ADR-013; inherits middleware (cost, safety, telemetry). Tool description: "Answer matter/project/invoice-scoped analytical questions about entities" (NFR-12).
    - **Files affected**: Tool handler class + registration site
    - **Tasks**: ~1 task
    - **Acceptance**: Tool visible in agent's tool catalog; description scopes routing correctly; LLM tool-calls for matter/project/invoice queries; slash command sets `forceMode` correctly

15. **D2-15 Subject resolution + HTTP client** — Resolve current entity from chat context (HostContext active matter/project/invoice ID); format as `<scheme>:<guid>`. HTTP client via existing `@spaarke/auth` `useAuth()` + `authenticatedFetch`. Opt into v1.1 SSE via `Accept: text/event-stream` (graceful fallback to v1.0 if 406).
    - **Files affected**: Frontend tool-handler module
    - **Tasks**: ~1 task
    - **Acceptance**: Subject correctly formatted; auth header set; correlation-id propagated; SSE consumed when available

16. **D2-16 Two-path response renderer** — Playbook path renders via `StructuredOutputStreamWidget` in static mode (Insights v1.0 single-shot; v1.1 SSE-streamed when available). RAG path renders citation-grounded prose with clickable `[n]` tokens. Decline case renders `answer` + `envelope.SuggestedActions` as plain text. Empty-results case renders "couldn't find anything" hint.
    - **Files affected**: New `InsightsResponseRenderer` component + sub-renderers (playbook / RAG / decline / empty)
    - **Tasks**: ~1–2 tasks
    - **Acceptance**: All four cases render correctly on Spaarke Dev synthetic test entities

17. **D2-17 Clickable citations** — When v1.1 `citations[].href` present, render `[n]` as clickable button. Click dispatches `context.context_update` PaneEventBus event with URL. `FilePreviewContextWidget` opens URL via iframe. Document URLs from `DocumentCheckoutService.GetPreviewUrlAsync(driveId, itemId, ct)` per Insights team correction. Graceful v1.0 fallback: display-name-only rendering when `href: null` or absent.
    - **Files affected**: `InsightsResponseRenderer` citation rendering
    - **Tasks**: ~1 task
    - **Acceptance**: Click opens source in Context pane; v1.0 fallback works; observation-only-href scenario (Wave F spike-outcome contingency) handled

18. **D2-18 Confidence floor badge (D5 R5 client-side)** — `confidence < 0.6` → Fluent v9 `Badge` or `MessageBar` with "Low confidence — verify before relying" text. Threshold configurable via R5 settings.
    - **Files affected**: `InsightsResponseRenderer` (badge component); R5 settings config
    - **Tasks**: ~1 task
    - **Acceptance**: Badge appears for low-confidence; absent for high-confidence; threshold configurable

19. **D2-19 12 error codes handled** — All 12 from integration brief §5.1. Per-code user messaging from brief column 4. Special cases: 503 `ai.intent-classification.disabled` + no `forceMode` → retry with `forceMode` if R5 has intent signal; 500 → retry once with 1s backoff. `x-correlation-id` propagated end-to-end.
    - **Files affected**: `InsightsResponseRenderer` error handling + retry policy
    - **Tasks**: ~1 task
    - **Acceptance**: Each error code triggers documented user-facing message; no raw stack traces / document content leaking; `correlationId` surfaced for ops debugging

20. **D2-20 Insights tool smoke tests** — Smoke test against Spaarke Dev BFF using Wave D7 synthetic test entities (matter `da116923-d65a-f111-a825-3833c5d9bcb1`, project `27845394-8e5f-f111-a825-70a8a59455f4`, invoice `05c8ef8d-8e5f-f111-a825-70a8a59455f4`). Confirm 5 realistic questions per practice area (CTRNS, IPPAT, BNKF) per integration brief §8.
    - **Files affected**: `tests/integration/InsightsToolIntegrationTests.cs` (or equivalent)
    - **Tasks**: ~1 task
    - **Acceptance**: All synthetic test entities return usable responses; SME walkthrough completes (1 SME minimum per integration brief §8)

#### Cross-cutting Phase 2 work

21. **D2-21 PaneEventBus subscription wiring** — `StructuredOutputStreamWidget` subscribes to `workspace.streaming_started/field_delta/streaming_complete`. `FilePreviewContextWidget` subscribes to `context.files_staged/file_selected`. Test new subscriptions don't break existing event flows.
    - **Files affected**: Subscription code in widgets
    - **Tasks**: rolled into D2-07 / D2-09
    - **Acceptance**: Subscriptions live; existing flows unchanged

22. **D2-22 Phase 2 tests + integration verification** — Unit + integration tests for D2-03 through D2-20. End-to-end Summarize flow verification. Insights tool consumption verification. Cross-tool tests (Summarize uploaded files vs Insights matter-query disambiguation).
    - **Files affected**: `tests/unit/Sprk.Bff.Api.Tests/` + `tests/integration/` + frontend test paths
    - **Tasks**: ~1 task (consolidated test pass)
    - **Acceptance**: All tests pass; coverage meets project standards; CVE scan clean

**Phase 2 estimate**: 22–28 tasks, ~7–8 engineering days

---

### Phase 3: Polish + Future-Use Validation (~0.5 week)

**Purpose**: Validate platform-extensibility claim via configuration-only follow-on; complete polish UX; produce lessons-learned + R6 backlog.

**Phase gating**: Phase 3 ships when: `/analyze` configuration-only proof point works (validates platform-extension claim per SC-19); Get Started welcome card visible at welcome stage; telemetry dashboards live; operator-led end-to-end testing complete; lessons-learned authored.

**Deliverables**:

1. **D3-01 `/analyze` configuration-only proof point** — Add new `sprk_analysisaction` seed for analyze + minimal tool wrapper. Invocable via slash command; produces analysis output via `StructuredOutputStreamWidget`. Validates SC-19 platform-extension claim.
   - **Files affected**: New seed + minimal tool wrapper (mirrors D2-03 + D2-05 patterns)
   - **Tasks**: ~1 task
   - **Acceptance**: `/analyze` works end-to-end; total cost < 1 day per SC-19 platform-validation claim

2. **D3-02 Get Started welcome card "Summarize a Document"** — Add card entry to `GetStartedCardsWidget` catalog. Click opens file upload dialog directly + pre-fills chat input with `/summarize `.
   - **Files affected**: `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/GetStartedCardsWidget.tsx` (extend); Get Started cards config
   - **Tasks**: ~1 task
   - **Acceptance**: Card visible at welcome stage; click opens upload + pre-fills input; discoverable parallel entry to slash command

3. **D3-03 Telemetry dashboards** — Application Insights queries / Grafana panel for: `r5.summarize.invocation` events; session-files index size metric; cost-per-session breakdown; SSE event-rate histogram.
   - **Files affected**: Dashboard config (Bicep / portal-managed)
   - **Tasks**: ~1 task
   - **Acceptance**: Dashboards visible; queries return data from Spaarke Dev; cost telemetry usable for capacity planning

4. **D3-04 Operator-led end-to-end testing** — Discoverability + correctness pass per kickoff doc's high-readiness surfaces (workspace switching, attachment uploads, section picker, pane collapse, chat + playbook lifecycle, W-4/W-5 concept validation now that they're real, NEW Summarize + Insights tool integration).
   - **Files affected**: `notes/testing-pass-results.md` (new)
   - **Tasks**: ~1 task (operator-led; documentation of findings)
   - **Acceptance**: Testing pass complete; findings synthesized + ranked by severity

5. **D3-05 Lessons-learned + R6 backlog** — Wrap-up doc per CLAUDE.md §7. Lessons captured; surprises documented; deferred items + R6 candidates listed (Tier 3 file grounding, editor widget, extracted-text widget, additional playbooks, etc.).
   - **Files affected**: `notes/lessons-learned.md` (new)
   - **Tasks**: ~1 task (final)
   - **Acceptance**: Lessons-learned authored; R6 backlog items listed; project status flips to Complete

**Phase 3 estimate**: 5 tasks, ~2–3 engineering days

---

### Wrap-up (~0.5 day)

**090 — Project wrap-up** (mandatory per task-create Step 3.7):
- Update R5 README status to Complete
- Confirm lessons-learned.md exists and is comprehensive
- Archive project artifacts
- Update `notes/insights-r2-coordination.md` §8 changelog with R5 completion entry
- R5 PR opens against master; final code review + merge

---

## Task Summary

| Phase | Deliverables | Estimated Tasks | Estimated Days |
|---|---|---|---|
| Phase 1: Platform extensions | 9 (D1-01 to D1-09) | 8–10 tasks | ~5 days |
| Phase 2: Vertical slice + Insights tool integration | 22 (D2-01 to D2-22) | 22–28 tasks | ~7–8 days |
| Phase 3: Polish + future-use validation | 5 (D3-01 to D3-05) | 5 tasks | ~2–3 days |
| Wrap-up | 1 (task 090) | 1 task | ~0.5 day |
| **TOTAL** | **37 deliverables** | **~36–44 tasks** | **~14–17 days (~2.5–3 weeks)** |

---

## Parallel Execution Opportunities

Per task-create Step 3.8 + project-pipeline Step 5 parallel-group strategy:

**Phase 1 parallel groups** (after D1-01 lands):
- Group P1-G1: D1-02, D1-03 (RagSearchOptions + RagIndexingPipeline parameterization — independent edits within same service area)
- Group P1-G2: D1-04, D1-05 (ChatSession manifest + AnalysisChunk FieldDelta — different model files)
- Group P1-G3: D1-06 (Structured Outputs + JSON parser — depends on D1-05)
- Group P1-G4: D1-07, D1-08 (cleanup job + telemetry — independent)
- Group P1-G5: D1-09 (tests + publish-size verification — depends on all D1-* completion)

**Phase 2 parallel groups** (after Phase 1 + D2-13 R5-lead-sign-off lands):
- Group P2-G1: D2-01, D2-02 (Dataverse seeds — independent data deploys)
- Group P2-G2: D2-03, D2-08 (SessionSummarizeOrchestrator + RichFilePreview extraction — independent; different worktree areas)
- Group P2-G3: D2-04, D2-05, D2-06 (endpoint + agent-tool + PaneEventBus event types — all touch DI module but additively)
- Group P2-G4: D2-07, D2-09 (StructuredOutputStreamWidget + FilePreviewContextWidget — independent widgets)
- Group P2-G5: D2-10, D2-11, D2-12 (slash command + chat-pane UX + per-file affordance — independent UI work)
- Group P2-G6: D2-14, D2-15, D2-16, D2-17, D2-18 (Insights tool integration suite — share `InsightsResponseRenderer` so partial overlap; possibly 2 sub-groups)
- Group P2-G7: D2-19 (error codes — depends on D2-16)
- Group P2-G8: D2-20, D2-22 (smoke tests + integration verification — after implementation lands)

**Phase 3**: Mostly serial (D3-01 → D3-02 → D3-03 → D3-04 → D3-05) since each builds on prior. Some parallelism possible for D3-02 + D3-03.

**Max concurrency per wave**: 6 agents (per project-pipeline Step 5 hard limit; check via TASK-INDEX.md per-task `parallel-safe` markers at task time).

**Permission boundary** (per CLAUDE.md §3): No R5 task touches `.claude/` paths (ADR-018 Flag Scope Discipline section already shipped pre-implementation). Standard parallel dispatch applies.

---

## Critical Path

`D1-01 (index provision) → D1-02 (RagSearchOptions sessionId) → D1-06 (Structured Outputs + parser) → D2-03 (Orchestrator) → D2-04 (Endpoint) → D2-07 (StructuredOutputStreamWidget) → D2-11 (Chat-pane UX) → D2-22 (Phase 2 verification) → D3-01 (/analyze validation) → 090 (wrap-up)`

Critical path ≈ 10 sequential dependencies. Slack exists in Phase 2 deliverables that can run in parallel within their groups.

---

## High-Risk Items

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Azure OpenAI Structured Outputs streaming behavior differs from expectation | Medium | High (blocks D1-06) | Phase 1 spike — validate Structured Outputs vs Function Calling for incremental JSON streaming before committing to one path |
| `RagSearchOptions` extension conflicts with Insights team's parallel extensions | Low | Low | Insights team has already done their RagSearchOptions extensions (subject/artifactType/predicate); R5's `sessionId` is orthogonal. Quick review at PR time. |
| Wave F (Insights v1.1) deploys later than R5 Phase 2 W3 timing | Low (Insights team confirmed in flight) | Low (R5 has graceful v1.0 fallback per NFR-11) | R5 Phase 2 ships with v1.0 consumption; v1.1 upgrade is incremental swap on the Insights consumption code path. |
| Tool routing disambiguation between `summarize` and `insights.query` produces misroutes | Medium | Medium | Tool description quality discipline per NFR-12; observation testing during Phase 2; potentially explicit slash-command disambiguation for ambiguous cases |
| `citations[].href` schema plumbing spike reveals large cost → document-citation `href` defers to v1.2 | Medium (per Insights team flagging) | Low | R5 implementation handles `href: null` gracefully (back-compat path); display-name-only citations work; v1.2 brings document `href` later |
| BFF publish-size delta exceeds +1 MB compressed | Low | Medium | Per-task verification (PR-02); current baseline +14 MB headroom (45.65 MB current vs 60 MB ceiling) |
| StructuredOutputStreamWidget's schema-driven design becomes complex for diverse output shapes | Medium | Medium | UR-02 flagged in spec; iterate during Phase 2 with concrete schemas (Summarize + Insights playbook + Insights RAG observation); apply 80/20 rule on rendering hints |

---

## Discovery Reference

This plan derives from the comprehensive design phase. Cross-references:

- Full design rationale: `design.md` (this project)
- Formal requirements (FR/NFR/DR/PR): `spec.md` (this project)
- Cross-project coordination: `notes/insights-r2-coordination.md`
- Binding Insights contract: `notes/insights-engine-assistant-integration-brief.md`
- v1.1 contract negotiation: `notes/insights-engine-contract-v1.1-request.md`
- v1.1 response sent to Insights team: `notes/insights-team-v1.1-response.md`

---

## Next Step

`/task-create projects/spaarke-ai-platform-unification-r5` (or continuation of `/project-pipeline` which invokes `task-create` at Step 3) — decomposes this plan into ~36–44 individual POML task files in `tasks/` folder.

After task-create runs: `/task-execute` is invoked per individual task at FULL rigor per CLAUDE.md §4.
