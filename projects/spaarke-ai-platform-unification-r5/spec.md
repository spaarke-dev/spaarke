# Spaarke AI Platform Unification R5 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-03 (late)
> **Source**: `projects/spaarke-ai-platform-unification-r5/design.md` (post-Insights-r2-v1.1-negotiation)
> **Predecessor**: R4 shipped to master `18b9323f` 2026-06-03; PR #331
> **Parallel project**: Insights Engine R2 (effectively complete; Wave F v1.1 approved by operator 2026-06-03 late; in-flight in parallel via Claude Code)

---

## Executive Summary

R5 ships a chat-driven "Summarize a Document" vertical slice through the SpaarkeAi three-pane shell, exercising the existing Spaarke AI platform (RagService, AnalysisOrchestrationService, SprkChatAgent, PaneEventBus, session-storage triple-tier) for the first time as a unified end-user flow. The R5 chat agent simultaneously consumes the Insights Engine R2 endpoint (`POST /api/insights/assistant/query`, contract v1.1 incoming) as a sibling tool — making R5's chat surface the **Spaarke Assistant** with two coexisting AI capabilities (Summarize + Insights) plus the existing ad-hoc grounded-LLM mode. The project is **configuration-first**: 12 work items in 3 phases, ~2–3 engineering weeks; no new ADRs, no new event-bus channels, no new top-level DI registrations. R5's deliverable is the platform's first operator-visible proof point that new AI capabilities arrive via configuration + thin orchestration, not new architecture.

---

## Scope

### In Scope

**Vertical slice — "Summarize a Document"**:
- Multi-file Summarize playbook invocation via `/summarize` slash command + natural-language tool-call routing
- ChatGPT/Claude-style token streaming of structured summary output via SSE
- Session-scoped file persistence (Tier 2 — Redis hot + Cosmos warm + Dataverse cold)
- Source file preview rendering in Context pane (single + multi-file selection)
- Streaming output as new Workspace tab; other tabs untouched; static restoration after refresh
- Multi-turn intent routing ("summarize just one of them") via LLM tool-call + UI affordance
- General grounded Q&A over session-scoped uploaded files (Step 10b of user flow)

**Platform foundations** (extensions to existing infrastructure):
- New `spaarke-session-files` Azure AI Search index for session-scoped retrieval
- `RagSearchOptions.sessionId` filter (additive parameter)
- `AnalysisChunk.FieldDelta` variant (additive SSE event type for structured-field streaming)
- `ChatSession.UploadedFiles[]` manifest extension
- Azure OpenAI Structured Outputs mode for Summarize playbook execution
- Session-files cleanup background job (`IHostedService`, scheduled + immediate on session-end)

**Insights tool integration**:
- Register `insights.query` tool on `SprkChatAgent` calling `POST /api/insights/assistant/query`
- Consume v1.1 SSE deltas for RAG-path `answer` streaming (with v1.0 single-shot fallback)
- Consume `citations[].href` for clickable citation navigation (with display-name-only fallback)
- Two-path response renderer (playbook structured envelope + RAG citation-grounded prose)
- All 12 binding error codes per Insights v1.0 contract
- `x-correlation-id` propagation end-to-end
- `forceMode` semantics (set for explicit slash-command paths; omit for natural-language)
- Low-confidence badge (`confidence < 0.6` → "Low confidence — verify before relying")

**Chat surface orchestration**:
- Slash command `/summarize` dual-mode semantic extension (uploaded files OR active document)
- Get Started welcome card "Summarize a Document" (discoverable parallel entry)
- ChatSession file lifecycle UX (chips, persistent N-files indicator, per-file actions including "Summarize this only")
- Two valid upload orderings: upload-first → prompt OR prompt-first → "please upload files" interjection
- Multi-file combined-summary deterministic acknowledgement

**New shared components**:
- `StructuredOutputStreamWidget` (Workspace; schema-driven; renders any structured AI output progressively via `FieldDelta` events; reusable for future AI tools)
- `FilePreviewContextWidget` (Context-pane; non-modal wrapper around extracted `RichFilePreview` renderer core)
- Extraction of `RichFilePreview` renderer core from existing `RichFilePreviewDialog` (modal stays; renderer shared)

### Out of Scope

**R5 deferrals** (explicitly deferred per design.md §9):
- Editor widget destination for "Add to Workspace" — workspace tab opens upgraded `DocumentViewerWidget`; richer editor (annotation, redline) deferred to future project
- Extracted-text Workspace widget — out of scope unless trivial during Phase 2
- Tier 3 file grounding (SharePoint Embedded reference-scoped persistence) — R5 ships Tier 2 only
- Step 9 follow-on actions (save, email, attach to record, create task from completed summary) — future use case
- Calendar widget Direct registration (per ground-truth survey § 5)
- Stages 2–4 header treatment (A-1 deferred to Moment 2)
- Standalone follow-on playbooks (`/analyze`, `/compare`, `/translate`, `/extract`) — Phase 3 ships only `/analyze` as configuration-only proof point; others are R6+ backlog

**Insights Phase 2 features** (Insights team owns; R5 silently accepts forward-compatible response fields):
- Bidirectional clarification (BFF 422 with `clarification` envelope)
- Multi-turn conversation state persisted on BFF
- `playbookHint` field in request schema
- Actionable citations (`citations[].action` — distinct from clickable `href`)
- Cross-tenant federation

**Insights catalog scope decisions** (deferred to follow-on project):
- Global router across all chat-agent tools vs per-tool classification — Phase 1.5 ships per-tool; R5 uses `forceMode` to bypass when invoking via slash command

**Separate projects**:
- Kiota CVE chain upgrade (`spaarke-graph-sdk-kiota-upgrade-r1`)
- BFF test-infrastructure cleanup (separate dedicated project)
- Iframe-wizards pattern enhancement (`spaarke-iframe-wizard-pattern-enhancement`)

### Affected Areas

**Backend (BFF)**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/` (multiple subfolders) — RagService extensions, AnalysisChunk extension, new SessionSummarizeOrchestrator class, new SummarizePlaybookTool registration, new InsightsQueryToolHandler
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` — register new services inside existing feature module (per ADR-010); no new top-level `Program.cs` lines
- `src/server/api/Sprk.Bff.Api/Models/` — `ChatSession.UploadedFiles[]` extension; `FieldDelta` variant on `AnalysisChunk`
- New endpoint: `POST /api/ai/chat/sessions/{sessionId}/summarize`
- New `IHostedService`: session-files cleanup job

**Frontend (client)**:
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/` — new `StructuredOutputStreamWidget`
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/` — new `FilePreviewContextWidget` (or new subfolder)
- `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/` — extract `RichFilePreview` renderer core from existing dialog
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DocumentViewerWidget.tsx` — upgrade R4 stub to use extracted renderer
- `src/solutions/SpaarkeAi/src/components/conversation/` — chat-pane file chips, interjection logic, file lifecycle UX
- `src/solutions/SpaarkeAi/src/components/context/` — file preview widget dispatch
- `src/client/shared/Spaarke.UI.Components/src/components/SlashCommandMenu/slashCommandMenu.types.ts` — `/summarize` description extension

**Infrastructure**:
- `infrastructure/ai-search/spaarke-session-files.json` — new index schema (mirrors canonical `spaarke-knowledge-index-v2.json`)
- `infrastructure/bicep/` modules — session-files index provisioning (idempotent extension)

**Dataverse data**:
- New `sprk_analysisaction` seed row: "Summarize Document for Chat" (configuration only — no schema changes)
- New `sprk_analysisplaybook` configuration linking the action

**Tests**:
- `tests/unit/Sprk.Bff.Api.Tests/` — coverage for new services, endpoint, RagSearchOptions extension, SSE protocol, session storage
- `tests/integration/` — Summarize end-to-end + Insights tool consumption smoke tests

---

## Requirements

### Functional Requirements

**Summarize vertical slice**:

1. **FR-01** — Multi-file Summarize via `/summarize` slash command. User uploads 1–N files in Assistant pane (within 25 MB cap + MIME allow-list from R4 FR-04), invokes `/summarize`, receives a structured summary. Multi-file produces `DocumentAnalysisResult.fileHighlights[]`.
   - **Acceptance**: Slash command resolves to Summarize playbook (GUID `4a72f99c-a119-f111-8343-7ced8d1dc988`); playbook output schema matches `ISummarizeResult` from existing wizard path; multi-file results populate `fileHighlights[]`.

2. **FR-02** — SSE token-streamed structured summary in Workspace pane. Summary output streams progressively via `FieldDelta` SSE events as Azure OpenAI emits tokens; structured fields (TL;DR, summary, per-file highlights, mentioned parties, call-to-action) populate progressively. NOT a final-result plop.
   - **Acceptance**: `AnalysisChunk.Type = "delta"` events emitted with `FieldDelta { path, content, sequence }`; `StructuredOutputStreamWidget` renders each field progressively; existing wizard consumers continue to work (back-compat).

3. **FR-03** — Two valid file upload orderings. User can upload files first then invoke `/summarize`, OR invoke `/summarize` first and receive deterministic interjection "Upload the file(s) you'd like me to summarize" before uploading.
   - **Acceptance**: Both orderings reach the same Summarize playbook invocation; prompt-first path interjects via chat-layer logic (not playbook-emitted).

4. **FR-04** — Multi-file combined-summary acknowledgement. When user uploads 2+ files and invokes `/summarize`, Assistant deterministically interjects "I'll provide a combined summary for the files you uploaded" before streaming begins.
   - **Acceptance**: Chat-layer logic emits the interjection on multi-file + Summarize-intent detection; playbook itself remains analysis-focused.

5. **FR-05** — Context pane source file preview. When files are staged in a session, Context pane renders source file preview(s). Single file: rendered preview via extracted `RichFilePreview` renderer (PDF.js / Graph preview). Multi-file: list of file cards → click swaps active preview.
   - **Acceptance**: New `FilePreviewContextWidget` registered in `ContextWidgetRegistry`; renders via `SpeFileStore.GetFilePreviewUrlAsync` / equivalent; multi-file selection updates active preview; per-file 3-dot menu reuses existing `DocumentRowMenu` (aiSummary + toggleWorkspace + findSimilar actions).

6. **FR-06** — Workspace tab opens additively. Streaming summary opens as a NEW Workspace tab; existing tabs remain untouched. FIFO eviction (`MAX_WORKSPACE_TABS` ≈ 8) applies at cap with brief toast.
   - **Acceptance**: New tab dispatched via `workspace.widget_load` (existing channel); subscribers ignore additive event-type discriminants per ADR-030.

7. **FR-07** — Tab persistence + static restoration. After streaming completes, full `ISummarizeResult` is persisted to workspace tab state via existing `PATCH /api/ai/chat/sessions/{id}/tabs`. Browser refresh restores the tab statically (no re-streaming).
   - **Acceptance**: Tab state survives close+reopen; restoration renders completed structured output; no re-invocation of the playbook.

8. **FR-08** — Multi-turn "summarize just one of the files". User can refine by natural language ("just summarize file 2") or explicit UI affordance ("Summarize this only" button per file card in Context pane). Each refined invocation produces a NEW workspace tab; prior summary tab remains.
   - **Acceptance**: LLM tool-call path: `CompoundIntentDetector` detects re-invocation; agent calls `InvokeSummarizePlaybookTool` with `fileIds: [subset]`. UI path: per-file button dispatches directly to the orchestrator. Both produce additive tabs.

9. **FR-09** — General grounded Q&A over session-scoped files (Step 10b). User asks free-form questions about uploaded files ("are there litigation risks?", "who are the parties?", "translate to Spanish?"); LLM responds in Assistant pane with grounded citations from session-scoped retrieval.
   - **Acceptance**: `RagSearchOptions.sessionId` filter scopes retrieval to `spaarke-session-files` index for the active session; LLM tool-calls existing RAG search infrastructure; citations link to retrieved chunks.

10. **FR-10** — Three interaction modes coexist. The chat agent supports (a) explicit playbook via slash command, (b) intent-routed playbook via LLM tool-call, (c) ad-hoc grounded LLM response with RAG context. Modes are complementary; ad-hoc is the default when no tool intent matches.
    - **Acceptance**: All three paths produce expected output for representative test prompts; no tool description overlap that misroutes; ad-hoc grounded chat is default fallback.

11. **FR-11** — Decline-to-find structured response. When Summarize playbook detects insufficient content (per D-04 honesty contract), execution chains to `DeclineToFindNode` (zero-LLM, deterministic 5-field response). Result: structured `DeclineResponse` rendered, NOT hallucinated summary.
    - **Acceptance**: Decline path returns structured response with `reason`, `explanation`, `suggested_actions`, `confidence_in_decline`, `metadata`; `StructuredOutputStreamWidget` renders decline state distinctly from successful summary.

**Insights tool integration**:

12. **FR-12** — Insights tool registration in chat agent. `insights.query` tool is registered on `SprkChatAgent` via `SprkChatAgentFactory`; LLM can call it for natural-language matter/project/invoice queries; slash command path bypasses classifier via `forceMode`.
    - **Acceptance**: Tool registered; description scopes routing correctly ("Answer matter/project/invoice-scoped analytical questions about entities"); `forceMode` semantics correct (set on slash command; omit on natural language); inherits middleware (cost, safety, telemetry).

13. **FR-13** — Two-path response rendering for Insights. Insights playbook path renders structured envelope via `StructuredOutputStreamWidget` (schema-driven). Insights RAG path renders citation-grounded prose with `[n]` clickable citations.
    - **Acceptance**: Both paths consume v1.1 SSE deltas progressively when available (fallback to v1.0 single-shot rendering); decline case rendered with `SuggestedActions`; empty-results case renders "couldn't find anything" hint (not empty answer).

14. **FR-14** — Clickable citations (assumes v1.1 `citations[].href`). When `href` present, citation renders as clickable button → dispatches `context.context_update` PaneEventBus event → `FilePreviewContextWidget` opens URL in Context pane via iframe. Document URLs derived from `DocumentCheckoutService.GetPreviewUrlAsync`.
    - **Acceptance**: Document citations open in Context pane preview; observation citations open per Insights team's URL choice; `href: null` falls back to display-name-only rendering (back-compat).

15. **FR-15** — Low-confidence badge. When response `confidence < 0.6`, render Fluent v9 `Badge` or `MessageBar` with "Low confidence — verify before relying" text alongside the response. Threshold configurable.
    - **Acceptance**: Badge appears for confidence < threshold; absent for confidence ≥ threshold; threshold configurable via R5 settings (default 0.6).

16. **FR-16** — All 12 Insights error codes handled. Per integration brief §5.1 error matrix: 400-class (4 codes), 401, 429, 503-class (4 codes), 500. Per-code UX from brief column 4. Special cases: 503 `ai.intent-classification.disabled` → retry with `forceMode`; 500 → retry once with 1s backoff.
    - **Acceptance**: Each error code triggers documented user-facing message; no raw stack traces or document content leaking; `correlationId` surfaced for ops debugging.

17. **FR-17** — `x-correlation-id` propagation. R5 generates unique `x-correlation-id` per Assistant turn; propagates through `insights.query` tool invocations; verifiable via App Insights / Kusto log correlation.
    - **Acceptance**: Header set on every outbound request; correlation ID retained in chat telemetry; cross-service log lookups work end-to-end.

### Non-Functional Requirements

1. **NFR-01** — **Latency**: SSE TTFB < 500ms per ADR-013 (chat-streaming latency budget). Verified via load test of representative payloads.
2. **NFR-02** — **Concurrency caps**: Max 20 files per session (hard cap; rejects 21st upload). Files < 500 tokens skip chunking (single-chunk index). Aggressive cleanup on session-end (no scheduled-sweep delay).
3. **NFR-03** — **Multi-tenancy**: All `spaarke-session-files` index documents carry `tenantId` + `sessionId`; per-query filtering enforced. Dedicated-model tenants get their own session-files index (per existing per-tenant index pattern).
4. **NFR-04** — **Auth**: All new endpoints register Auth v2 filter per ADR-028; `[EndpointFilter(typeof(AuthorizationFilter))]` per ADR-008. No token snapshots (chat agent uses fresh token per request).
5. **NFR-05** — **Safety**: Auto-applied via `SprkChatAgentFactory` middleware (PromptShield + GroundednessCheck). All new tool invocations inherit safety pipeline.
6. **NFR-06** — **Cost control**: Per-session token budget enforced via existing `AgentCostControlMiddleware` (default 10K; configurable per playbook). New session-files indexing telemetry tracks per-session embedding cost.
7. **NFR-07** — **BFF publish-size**: R5 delta ≤ +1 MB compressed; total ≤ 47 MB compressed (current baseline ~45.65 MB; NFR-01 ceiling 60 MB). Per-task `dotnet publish` verification (CLAUDE.md §10).
8. **NFR-08** — **DI registration discipline (ADR-010)**: All new services register inside `AnalysisServicesModule` (or extended module) — zero new `services.AddXxx()` lines in `Program.cs`. Concrete classes by default; `virtual` methods only where Null-Object subclassing required (none expected for R5 per ADR-018 Flag Scope Discipline).
9. **NFR-09** — **PaneEventBus additive (ADR-030)**: 4 channels unchanged. New event-type discriminants additive: `workspace.streaming_started`, `workspace.field_delta`, `workspace.streaming_complete`, `context.files_staged`, `context.file_selected`. Existing subscribers ignore unknown types.
10. **NFR-10** — **Back-compat for AnalysisChunk SSE protocol**: New `FieldDelta` variant additive; existing wizard consumers (LegalWorkspace `SummarizeFilesDialog` via `streamSummarize()`) ignore unknown event types and continue to function with v1.0 protocol.
11. **NFR-11** — **Back-compat for Insights consumption**: R5 falls back gracefully to Insights v1.0 (single-shot, display-name-only citations) if v1.1 is delayed or declined. UX is degraded but functional. R5 design.md §4.12 documents the fallback.
12. **NFR-12** — **Tool description quality**: Tool descriptions for `summarize` and `insights.query` MUST be non-overlapping and scope-explicit. `summarize`: "Summarize uploaded files in the current chat session" (file-scoped, session-scoped). `insights.query`: "Answer matter/project/invoice-scoped analytical questions about entities" (entity-scoped, knowledge-scoped). Tool routing reliability depends on this.
13. **NFR-13** — **No new HIGH-severity CVEs**: `dotnet list package --vulnerable --include-transitive` clean before merge per ADR-029.
14. **NFR-14** — **Test coverage**: New endpoints, services, and widgets have corresponding tests in `tests/unit/Sprk.Bff.Api.Tests/` (BFF) and equivalent frontend test paths. Per CLAUDE.md §10 F.1 sub-mechanism — endpoints that map unconditionally must have unconditional service registration (R5 services are unconditionally registered per ADR-018 Flag Scope Discipline; no Null-Object impls needed).

### Documentation Requirements

1. **DR-01** — ADR-018 Flag Scope Discipline section already shipped (commit `ee25b49a`). No further ADR changes.
2. **DR-02** — Update `notes/insights-r2-coordination.md` §8 changelog when Wave F kicks off and when it ships, per coordination protocol.
3. **DR-03** — R5 lead signs off on Insights `design-e3-tool-call-contract.md` v1.0 and records 6 contract-review decisions (D1–D6 per design.md §8.2) in the contract's §10 review log. Closes Insights task 042 sub-task A.5.
4. **DR-04** — Generate R5 lessons-learned per phase (Phase 1 wrap-up; Phase 2 wrap-up; Phase 3 wrap-up) per CLAUDE.md §7 task-completion protocol.
5. **DR-05** — Update `docs/architecture/SPAARKEAI-COMPONENT-MODEL.md` after Phase 2 ship to add `StructuredOutputStreamWidget`, `FilePreviewContextWidget`, and shared `RichFilePreview` renderer to component inventory.
6. **DR-06** — Update `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` if R5 surfaces new pattern variations beyond the current 5 archetypes (likely — `StructuredOutputStreamWidget` is schema-driven; this may be a new archetype).

### Process Requirements

1. **PR-01** — All R5 implementation tasks MUST invoke the `task-execute` skill at FULL rigor (per CLAUDE.md §4 Mandatory Task Execution Protocol). Tasks reference real file paths verified against current master post-rebase.
2. **PR-02** — Per-task BFF publish-size verification per CLAUDE.md §10. Each task touching BFF reports absolute size + delta vs prior baseline in task notes/PR description.
3. **PR-03** — Sequential phase gating: Phase 1 → Phase 2 → Phase 3. Each phase produces shippable, deployable state before next begins. Per phase: tests pass, BFF publish-size verified, no new HIGH CVEs.
4. **PR-04** — R5 lead reviews + signs off Insights `design-e3-tool-call-contract.md` v1.0 BEFORE Phase 2 Insights tool integration work item begins (§4.12 of design.md). Closes Insights task 042 sub-task A.5.
5. **PR-05** — Coordinate Wave F deployment timing with Insights team via `notes/insights-r2-coordination.md` §8 changelog. R5 Phase 2 Insights consumption work proceeds against v1.0 fallback if v1.1 not yet on Spaarke Dev when needed.

---

## Technical Constraints

### Applicable ADRs (binding)

- **ADR-001** — Minimal API + BackgroundService. R5 endpoints in BFF only; no Azure Functions; cleanup job is `IHostedService`.
- **ADR-006** — Code Pages default UI. SpaarkeAi shell is Code Page; new widgets target React 19.
- **ADR-007** — SpeFileStore facade. All file ops via `SpeFileStore.UploadSmallAsync` / `DownloadFileAsync` / `GetFilePreviewUrlAsync` (or `DocumentCheckoutService.GetPreviewUrlAsync` for citation hrefs per Insights team correction).
- **ADR-008** — Endpoint filters for auth. New endpoint `POST /api/ai/chat/sessions/{id}/summarize` MUST add `[EndpointFilter(typeof(AuthorizationFilter))]`.
- **ADR-009** — Redis-first caching. Session file manifest in Redis hot tier (24h TTL); existing dual-tier write-through to Cosmos.
- **ADR-010** — DI minimalism (≤15 `Program.cs` lines via feature modules). R5 adds zero new top-level lines. New services register inside `AnalysisServicesModule` (or extended module). Concrete classes per ADR-010.
- **ADR-012** — Shared component library. New widgets in `@spaarke/ai-widgets` (workspace + context) and `@spaarke/ui-components` (extracted renderer).
- **ADR-013** — AI architecture (BFF-only). R5 endpoints in BFF; agents via `SprkChatAgentFactory`; 4-criterion exception test fails for R5 → placement is BFF.
- **ADR-014** — AI caching with `tenantId` isolation. `spaarke-session-files` index documents MUST carry `tenantId` AND `sessionId`. Tenant-scoped filtering on every query.
- **ADR-016** — Rate limiting. R5 inherits `ai-context` policy (60 req/min sliding window per `oid` across `/ask` + `/search` + `/assistant/query`); also applies to summarize endpoint.
- **ADR-018** — Feature flags + kill switches (Flag Scope Discipline added 2026-06-03). R5 introduces NO new feature flags. Kill-switch coverage inherited from existing `Analysis:Enabled` / `Chat:Enabled` flags via `NullSprkChatAgentFactory`.
- **ADR-019** — ProblemDetails errors. All R5 endpoint errors use RFC 7807 ProblemDetails with stable `errorCode` extension.
- **ADR-021** — Fluent UI v9 (semantic tokens, dark-mode). New widgets dark-mode tested.
- **ADR-022** — React 19 for Code Pages. All new R5 widgets React 19.
- **ADR-026** — Code Page build (Vite + singlefile). No build-tooling change.
- **ADR-028** — Spaarke Auth v2. New endpoint inherits Auth v2 filter; no token snapshots; chat agent uses fresh token per request.
- **ADR-029** — BFF publish hygiene. Per-task publish-size verification; framework-dependent linux-x64; CVE override pattern.
- **ADR-030** — PaneEventBus (closed 4-channel union; event types additive). R5 adds new additive event-type discriminants within existing channels. Zero new channels.
- **ADR-031** — Stage lifecycle (welcome/loading/active-chat/review). R5 UI respects `useShellStage()` per existing patterns.
- **ADR-032** — BFF Null-Object kill-switch. NOT applicable to R5 services (unconditionally registered per Flag Scope Discipline). Existing `NullSprkChatAgentFactory` covers broader AI-flag-OFF state.

### MUST Rules

- ✅ MUST register all new services via existing feature module (`AnalysisServicesModule`) per ADR-010
- ✅ MUST use `SprkChatAgentFactory` for agent creation (auto-applies cost / safety / telemetry middleware) per ADR-013
- ✅ MUST extend `RagSearchOptions` with `sessionId` as additive parameter (back-compat)
- ✅ MUST extend `AnalysisChunk` with `FieldDelta` variant as additive (back-compat for wizard consumers)
- ✅ MUST emit `tenantId` + `sessionId` on every `spaarke-session-files` index document per ADR-014
- ✅ MUST route file operations through `SpeFileStore` facade per ADR-007
- ✅ MUST register new PaneEventBus event types within existing 4 channels per ADR-030
- ✅ MUST follow CLAUDE.md §10 BFF Hygiene — Placement Justification per task; publish-size verification per task; CVE scan; test obligation

### MUST NOT Rules

- ❌ MUST NOT add new top-level DI registrations to `Program.cs` (use feature modules)
- ❌ MUST NOT add new PaneEventBus channels (closed at 4 per ADR-030)
- ❌ MUST NOT introduce new feature flags for sub-services (per ADR-018 Flag Scope Discipline)
- ❌ MUST NOT introduce new Null-Object impls (no R5 service needs independent kill-switch coverage)
- ❌ MUST NOT add new ADRs (R5 leverages existing 32-ADR baseline; ADR-018 refinement already shipped pre-implementation)
- ❌ MUST NOT build parallel orchestrators (use existing `AnalysisOrchestrationService`)
- ❌ MUST NOT build parallel chat agents (use existing `SprkChatAgent`)
- ❌ MUST NOT build parallel session managers (use existing `ChatSessionManager`)
- ❌ MUST NOT build parallel file-preview component (extract renderer from existing `RichFilePreviewDialog`)
- ❌ MUST NOT introduce new prompt-bearing Dataverse entity (per Insights r2 + ADR — `sprk_analysisaction.sprk_systemprompt` is the canonical primitive)
- ❌ MUST NOT inject Insights internal services into R5 code (Zone B consumer of HTTP contract only per ADR-013)

### Existing Patterns to Follow

- **Playbook execution**: see `AnalysisOrchestrationService.ExecutePlaybookAsync` + Insights universal-ingest@v1 + Summarize playbook pattern
- **JPS action seed**: see existing Insights `sprk_analysisaction` rows (INS-FACT/IDXR/EVID/GRND/DECL/RART) + R5's new "Summarize Document for Chat" row follows same shape
- **SSE streaming**: see existing `POST /api/workspace/files/summarize` SSE handling in `summarizeService.ts` for parsing pattern
- **PaneEventBus dispatch**: see Calendar widget (R3 task 115) Pattern D as canonical shared-lib widget reference
- **RAG hybrid search**: see existing `RagService.SearchAsync` + `RagSearchOptions` patterns; extend with `sessionId` filter
- **Cost control middleware**: see `AgentCostControlMiddleware` — auto-inherited via `SprkChatAgentFactory`
- **Safety pipeline**: see `SafetyPipelineMiddleware` — auto-inherited
- **Schema-driven streaming widget**: novel for R5; pattern emerges as `StructuredOutputStreamWidget` ships; document in `BUILD-A-NEW-WORKSPACE-WIDGET.md` post-Phase-2
- **Chat-agent tool registration**: see existing `CapabilityRouter` (AIPU2-061) per-turn tool resolution; Insights `insights.query` integration brief provides v1.0 consumption reference

---

## Success Criteria

R5 ships when ALL of these are demonstrable:

### Summarize vertical slice

1. [ ] **SC-01** — Operator can upload 1+ files in Assistant pane and successfully invoke `/summarize`. Verify by: end-to-end Spaarke Dev smoke test.
2. [ ] **SC-02** — Streaming summary appears in a new Workspace tab progressively (ChatGPT/Claude-style; NOT plop). Verify by: visual confirmation + SSE event capture in browser DevTools.
3. [ ] **SC-03** — Other workspace tabs remain untouched during streaming. Verify by: tabs visible before/after streaming match in count + order.
4. [ ] **SC-04** — Context pane shows source file preview(s); multi-file shows list with click-to-swap. Verify by: upload 1 file → single preview; upload 3 files → file list + click to swap.
5. [ ] **SC-05** — After streaming completes, browser refresh restores the summary tab statically. Verify by: refresh post-completion; tab restores with completed result; no re-stream.
6. [ ] **SC-06** — Follow-up "summarize just one of the files" works via both LLM tool-call AND explicit UI button. Verify by: chat natural-language refinement + click per-file "Summarize this only" affordance; both produce new tab; prior tab remains.
7. [ ] **SC-07** — Follow-up general grounded Q&A ("are there litigation risks?") returns answers grounded in session files. Verify by: ask question; response cites session-file chunks; not knowledge-index citations.
8. [ ] **SC-08** — Both `/summarize` (slash command → direct endpoint) and natural-language summarize requests (LLM tool-call → agent tool) produce identical output. Verify by: invoke both paths with same files; compare `DocumentAnalysisResult` shape.
9. [ ] **SC-09** — Decline-to-find renders correctly when content is insufficient. Verify by: upload empty/tiny file → decline state rendered with `SuggestedActions` (not hallucinated summary).

### Insights tool integration

10. [ ] **SC-10** — R5 lead has reviewed `design-e3-tool-call-contract.md` v1.0 and recorded 6 contract-review decisions (D1–D6) in contract's §10 review log. Closes Insights task 042 sub-task A.5.
11. [ ] **SC-11** — `insights.query` tool registered in `SprkChatAgent`'s tool/skill registry; both response paths (playbook structured envelope + RAG citation-grounded prose) render correctly. Verify by: smoke test against Spaarke Dev with matter `da116923-d65a-f111-a825-3833c5d9bcb1`, project `27845394-8e5f-f111-a825-70a8a59455f4`, invoice `05c8ef8d-8e5f-f111-a825-70a8a59455f4` (Wave D7 synthetic test entities).
12. [ ] **SC-12** — RAG-path `answer` field renders progressively via SSE `delta` events (assumes Wave F v1.1 deployed); graceful fallback to single-shot rendering if v1.0 only. Verify by: hit RAG-path query; observe progressive token rendering; if v1.0, observe spinner + complete render.
13. [ ] **SC-13** — Citations are clickable when `href` is present: click opens source in `FilePreviewContextWidget` via PaneEventBus (assumes Wave F v1.1 deployed); graceful fallback to display-name-only if v1.0. Verify by: click citation; Context pane opens source; if v1.0, citation is text-only.
14. [ ] **SC-14** — Low-confidence badge renders when response `confidence < 0.6`. Verify by: hit low-confidence response; badge visible; hit high-confidence response; badge absent.
15. [ ] **SC-15** — All 12 error codes from Insights integration brief §5.1 handled with appropriate user messaging; no raw stack traces or document content leaking. Verify by: trigger each error code (mock or via kill-switches on Spaarke Dev); confirm user-facing message + no leakage.
16. [ ] **SC-16** — `x-correlation-id` propagated end-to-end. Verify by: invoke Insights tool; lookup correlation ID in App Insights / Kusto; observe end-to-end trace.
17. [ ] **SC-17** — `forceMode` correctly set when user invokes named tools; omitted otherwise. Verify by: invoke via `/ask-insights` slash command (forceMode set per chosen mode); invoke via natural language ("what's the predicted cost?") with forceMode omitted.
18. [ ] **SC-18** — UX walkthrough with ≥1 legal-ops SME on Spaarke Dev — 5 realistic questions per practice area (CTRNS, IPPAT, BNKF) — responses usable. Verify by: SME signoff in walkthrough notes.

### Platform validation

19. [ ] **SC-19** — `/analyze` works as configuration-only follow-on (validates platform-extension claim per §6.1 of design.md). Verify by: add new `sprk_analysisaction` seed + minimal tool wrapper; invoke via slash command; produces analysis output. Estimated cost: < 1 day.
20. [ ] **SC-20** — BFF publish-size delta ≤ +1 MB compressed (≤ 47 MB total per NFR-07). Verify by: `dotnet publish` measurement post-Phase-2; report in PR description.
21. [ ] **SC-21** — All tests pass; no new HIGH-severity CVEs introduced per NFR-13. Verify by: CI green; `dotnet list package --vulnerable --include-transitive` clean.
22. [ ] **SC-22** — Lessons-learned + R6 backlog produced per phase wrap-up. Verify by: `projects/spaarke-ai-platform-unification-r5/notes/lessons-learned.md` populated.

---

## Dependencies

### Prerequisites

- R4 shipped + on master (commit `18b9323f`, PR #331, 2026-06-03) — done
- Insights r2 Wave D + Wave E shipped + on master (PR #336 + PR #337, 2026-06-03 late) — done
- ADR-018 Flag Scope Discipline section shipped (commit `ee25b49a`) — done
- Branch `work/spaarke-ai-platform-unification-r5` rebased onto current master — done
- Existing Spaarke AI platform infrastructure (RagService, AnalysisOrchestrationService, SprkChatAgent, PaneEventBus, session storage, etc.) operational on Spaarke Dev — verified per investment inventory

### Internal Spaarke Dependencies

- **Insights Engine R2 Wave F** (~4.5d Insights engineering): v1.1 contract minor-version (SSE + clickable citations) — approved by operator 2026-06-03 late; in flight in parallel via Claude Code; expected on Spaarke Dev by R5 Phase 2 W3 timing. R5 has graceful v1.0 fallback if delayed.
- **Insights team coordination**: Wave F start/ship updates in `notes/insights-r2-coordination.md` §8 changelog; R5 lead's contract sign-off in `design-e3-tool-call-contract.md` §10.
- **Spaarke Dev environment**: BFF deploy capacity for R5 + Insights Wave F deploys; existing Azure AI Search instance with capacity for new `spaarke-session-files` index.

### External Dependencies

- **Azure OpenAI**: streaming completions API (Structured Outputs mode); embedding API (`text-embedding-3-large`). Both already in use; R5 adds session-file embedding workload (~$6.50/day at 1000 sessions/day per design.md §2.2 cost analysis).
- **Azure AI Search**: existing Standard S1 instance ($245/mo); R5 adds 1 of 50 index quota slots (`spaarke-session-files`); marginal cost ~$10–20/month additional.
- **Azure Cosmos DB serverless**: existing session-storage warm tier; R5 extends `ChatSession` schema with file manifest (additive property).

---

## Owner Clarifications

*Answers captured during design phase + Insights v1.1 negotiation:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Prompt selector UI | Slash command vs Get Started cards vs new affordance? | Reuse existing `SlashCommandMenu` + `/summarize` (already in `DEFAULT_SLASH_COMMANDS`). Optionally add Get Started card as discoverable parallel. | FR-13 / FR-08; no new UI component for prompt selection |
| Context pane file preview | Render-only or full editor? | Render-only. "Add to Workspace" (toggleWorkspace action) routes to editor widget (future scope). | FR-05; out-of-scope keeps R5 focused |
| Workspace summary widget | Extend AnalysisEditorWidget or new widget? | NEW `StructuredOutputStreamWidget` (schema-driven; reusable for any future structured output). AnalysisEditorWidget stays for legacy static rendering. | FR-02 + FR-06; new widget in `@spaarke/ai-widgets` |
| Session-scoped file persistence | One-shot (re-upload required) vs session-scoped vs SharePoint-Embedded reference? | Tier 2 — session-scoped via existing Redis hot + Cosmos warm dual-tier. Tier 3 explicitly out-of-scope. | FR-09 + NFR-02 / NFR-03 |
| General grounded chat over session files | Constrained (Summarize re-invocations only) vs open (full RAG over session files)? | Open — full RAG via existing `RagService` with new `sessionId` filter. Users expect this of any LLM tool. | FR-09 / FR-10; reuses existing infrastructure |
| SSE protocol shape | One-dimensional (raw token stream) vs multi-dimensional (structured-field deltas)? | Path B — structured-field deltas via new `FieldDelta` variant on `AnalysisChunk`. Reusable for any future structured AI output. | FR-02 + NFR-10 |
| Session storage backend | Redis vs Cosmos vs SQL? | Existing triple-tier (Redis hot 24h + Cosmos warm + Dataverse cold). R5 extends `ChatSession` schema; no new storage tier. | FR-09 / NFR-03 |
| RAG approach | Inline grounding only vs full retrieval RAG? | Full RAG — reuse existing infrastructure; R5 adds session-scoped index slice. | FR-09 / NFR-03 |
| Phasing | Sequential / slice-first / parallel? | Sequential — Phase 1 (foundations) → Phase 2 (vertical slice + Insights tool) → Phase 3 (polish + future-use validation). | PR-03 |
| Session-files index strategy | Single shared index with sessionId filter (Option A) vs dedicated `spaarke-session-files` index (Option B)? | Option B — dedicated index. Isolation; clean eviction; doesn't pollute customer-corpus query metrics. | FR-02 + NFR-03; new infra provisioning |
| Summarize tool integration | Endpoint only / agent tool only / both? | Both — direct endpoint for slash-command shortcut + agent tool for natural-language routing; converge on shared internal method. | FR-01 + FR-08 |
| Tab restore after refresh | Persist final + re-render static / persist in-progress + resume / ephemeral? | Persist final structured `ISummarizeResult` + re-render statically. | FR-07 |
| Generic streaming-widget pattern | Purpose-built SummaryStreamWidget vs extend AnalysisEditorWidget vs generic schema-driven `StructuredOutputStreamWidget`? | Generic schema-driven `StructuredOutputStreamWidget`. Reusable for any future structured output. | FR-02 + FR-06 + FR-13 |
| File preview component | Build new vs reuse existing? | Reuse existing — extract `RichFilePreview` renderer core from `RichFilePreviewDialog`; non-modal Context-pane shell uses extracted renderer. | FR-05; no parallel preview component |
| Insights v1.1 D1 (SSE) | Accept default (no SSE) or request v1.1 minor-version? | **Request v1.1** — leverage R5's `FieldDelta` infrastructure; SSE benefits RAG-path responses significantly. | FR-13 + NFR-11 (graceful v1.0 fallback) |
| Insights v1.1 D2 (`forceMode` without playbook hint) | Accept default? | Accept default (BFF resolves to `predict-matter-cost@v1`). R5 won't blind-fire. | FR-17 |
| Insights v1.1 D3 (decline rendering) | Plain strings or actionable verbs? | Accept default — plain strings in v1; actionable buttons → R6 backlog. | FR-13 (Decline case) |
| Insights v1.1 D4 (clickable citations) | Accept default (display-name only) or request `citations[].href`? | **Request v1.1** — clickable citations close major trust gap. Wave F includes 0.5d spike to confirm schema plumbing; fallback to observation-only `href` in v1.1 if document plumbing is large; document `href` defers to v1.2. | FR-14 + NFR-11 (graceful v1.0 fallback) |
| Insights v1.1 D5 (confidence floor) | Accept default (no badge) or implement client-side badge? | **Implement client-side** — `confidence < 0.6` → "Low confidence" badge. Pure R5 work; no contract change. | FR-15 |
| Insights v1.1 D6 (`previousTurnSummary` to classifier) | Accept default (logged only)? | Accept default — Phase 2 of classifier can use it; R5 doesn't need it for v1. | (No FR impact) |
| Insights v1.1 bandwidth | Operator approval for ~4.5d Insights engineering between task 090 and Phase 2 outline? | **Approved** — Insights team will complete Wave F in parallel via Claude Code (operator confirmation 2026-06-03 late). | Wave F in flight; coordination doc §8 changelog tracks status |

---

## Assumptions

*Proceeding with these assumptions (owner did not explicitly specify, or the design includes a default):*

- **Embedding model for session-files**: `text-embedding-3-large` (3072 dims) matches existing knowledge-index parity. May switch to `-3-small` if cost telemetry shows ~5× cost burden is unacceptable. Affects: NFR-03 + cost projections.
- **Structured Outputs vs Function Calling**: assuming Azure OpenAI Structured Outputs (JSON Schema mode). Validate during Phase 1 spike; switch to Function Calling if Structured Outputs streaming has limitations. Affects: FR-02 + NFR-10.
- **Session-files cleanup cadence**: assuming scheduled every 6h (background job). May tune to 1h if session-files index storage grows beyond expected. Affects: NFR-03.
- **Per-tenant routing for session-files index**: assuming dedicated-model tenants get their own session-files index (matching their dedicated knowledge index). Confirm during Phase 1 implementation; may consolidate to single shared session-files index if isolation is sufficient via `tenantId` filter. Affects: NFR-03.
- **`StructuredOutputStreamWidget` archetype documentation**: assuming this widget becomes a documented archetype in `BUILD-A-NEW-WORKSPACE-WIDGET.md` (currently 5 archetypes). Affects: DR-06.
- **Confidence threshold default**: assuming 0.6 for low-confidence badge. Configurable via R5 settings if operator preference shifts. Affects: FR-15.
- **Tool description quality**: assuming R5 implementer applies `summarize` ("file-scoped, session-scoped") vs `insights.query` ("entity-scoped, knowledge-scoped") descriptions that don't overlap. Affects: NFR-12 + FR-10.

---

## Unresolved Questions

*Open questions to resolve during implementation; do NOT block Phase 1 start:*

- [ ] **UR-01** — Tool routing disambiguation when user input could match both `summarize` AND `insights.query`. Example: "summarize this matter" with files uploaded (file-scoped Summarize) vs without (entity-scoped Insights). LLM may misroute. **Resolution path**: tool description refinement during Phase 2 implementation + observation testing; if needed, surface explicit "did you mean..." clarification in Phase 3.

- [ ] **UR-02** — `StructuredOutputStreamWidget` rendering of Insights playbook outputs vs Summarize outputs — both are structured but have different schemas. Single widget renders both; need clear visual differentiation. **Resolution path**: schema-driven rendering with optional `displayHints` config per output type; designed during Phase 2 implementation.

- [ ] **UR-03** — Wave F SSE event protocol exact alignment with R5's `FieldDelta`. Insights team agrees with R5's suggested shape (per negotiation §0a); concrete schema confirmation happens when Wave F lands. **Resolution path**: coordination via `notes/insights-r2-coordination.md` §8 changelog when Wave F deploys to Spaarke Dev; smoke test confirms.

- [ ] **UR-04** — `citations[].href` schema-plumbing spike outcome (Insights Wave F 0.5d spike). If plumbing cost > 1d, document-citation `href` defers to v1.2; R5 lives with display-name-only document citations in v1.1. **Resolution path**: Wave F spike output (decision memo at `projects/ai-spaarke-insights-engine-r2/decisions/D-XX-citation-href-plumbing.md`).

- [ ] **UR-05** — Per-task BFF publish-size projection. Current baseline ~45.65 MB; R5 estimate +0.5 MB total. Actual per-task delta only knowable during implementation. **Resolution path**: each Phase 1 / Phase 2 / Phase 3 task reports actual delta per CLAUDE.md §10 + ADR-029.

---

## Phasing Reference

Per design.md §7:

**Phase 1: Platform extensions** (1 week) — provision session-files index, extend `RagSearchOptions` + `RagIndexingPipeline` with `sessionId`, extend `ChatSession` model with `UploadedFiles[]`, add `FieldDelta` variant + incremental JSON parser, switch Summarize playbook execution to Structured Outputs mode, session-files cleanup job, telemetry events, tests + BFF publish-size verification.

**Phase 2: Vertical slice + Insights tool integration** (1.5 weeks) — Summarize seed + playbook configuration + orchestrator + tool registration + endpoint + PaneEventBus event types + `StructuredOutputStreamWidget` + `FilePreviewContextWidget` + upgraded `DocumentViewerWidget` + slash command extension + chat-pane UX. Plus: Insights contract review + sign-off, `insights.query` tool registration + response renderer + clickable citations + confidence badge + 12 error codes + correlation propagation + `forceMode` semantics.

**Phase 3: Polish + future-use validation** (0.5 week) — `/analyze` as configuration-only proof point, Get Started welcome card, telemetry dashboards, operator-led end-to-end testing, lessons-learned + R6 backlog.

**Refinement gate**: FIRED 2026-06-03 late per design.md §7.4 historical record. R5 lead contract-review sign-off (per PR-04) is the only remaining gate before Phase 2 Insights work begins.

---

## References

### R5 project documents
- `design.md` — full R5 design (source of this spec)
- `notes/ground-truth-spaarkeai-state.md` — R4 close baseline survey
- `notes/insights-r2-coordination.md` — cross-project coordination doc + §8 changelog
- `notes/insights-engine-assistant-integration-brief.md` — binding Insights v1.0 contract for R5 consumption
- `notes/insights-engine-contract-v1.1-request.md` — R5's v1.1 request + Insights team's negotiated agreements
- `notes/insights-team-v1.1-response.md` — R5 response to Insights team feedback

### Architecture docs (load-bearing for R5)
- `docs/architecture/AI-ARCHITECTURE.md`
- `docs/architecture/playbook-architecture.md`
- `docs/architecture/chat-architecture.md`
- `docs/architecture/rag-architecture.md`
- `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`
- `docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`
- `docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md`
- `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`
- `docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`
- `docs/architecture/auth-AI-azure-resources.md`

### Constraints
- `.claude/constraints/bff-extensions.md` — binding BFF additions governance (CLAUDE.md §10)
- `docs/standards/CHAT-ATTACHMENT-POLICY.md` — 25 MB binary cap, MIME allow-list, total-text caps

### Predecessor projects
- `projects/spaarke-ai-platform-unification-r4/` (R4 shipped 2026-06-03; PR #331)
- `projects/spaarke-ai-platform-unification-r3/` (R3 predecessor FRs still in force)
- `projects/ai-spaarke-insights-engine-r2/` (parallel; Waves D+E shipped; Wave F in-flight)

---

*AI-optimized specification. Original design: `design.md`. Ready for `/project-pipeline` to generate README + PLAN + tasks/.*
