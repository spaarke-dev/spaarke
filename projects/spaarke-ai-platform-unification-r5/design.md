# Spaarke AI Platform Unification R5 — Design

> **Project**: spaarke-ai-platform-unification-r5
> **Created**: 2026-06-03
> **Status**: Design (pre-spec)
> **Predecessor**: R4 shipped to master at `18b9323f` on 2026-06-03 (PR #331)
> **Forcing function**: "Summarize a Document" — chat-driven AI flow exercising all three SpaarkeAi panes end-to-end
> **Framing**: R5 surfaces months of platform investment by exercising the existing AI execution, retrieval, session, and pane-bus layers through a new end-user flow. Every architectural seam R5 extends already exists — no new ADRs are required. R5 ships real code (new widgets, session-scoped retrieval, SSE delta protocol, file preview), but no new architectural patterns.

---

## 1. Vision & Framing

### 1.1 What R5 is

R5 is the first **chat-pane orchestration** that demonstrates Spaarke's AI platform can absorb a new end-user capability by **extending existing architectural seams** — no new ADRs, no new event-bus channels, no new orchestrators. R5 ships real code (a new schema-driven streaming widget, a session-scoped retrieval extension, a new SSE delta event type, a file-preview renderer extraction, a chat-driven entry path), but every piece slots into a pattern the platform already provides.

The vertical slice is "Summarize a Document": a user in the Assistant pane uploads one or more files, invokes `/summarize` (slash command or natural language), watches the Workspace pane stream a richly-structured summary token-by-token while the Context pane shows source-file previews, and follows up with grounded Q&A over the same uploaded files within the session.

### 1.2 What R5 is NOT

- Not a new AI subsystem. Every execution layer R5 depends on already ships in production.
- Not a refactor or rearchitecture. R5 introduces no new ADRs.
- Not a new orchestrator, indexer, or chat agent. The existing `AnalysisOrchestrationService`, `RagIndexingPipeline`, and `SprkChatAgent` are the engines.
- Not a re-implementation of summary functionality. The Summarize playbook (`4a72f99c-a119-f111-8343-7ced8d1dc988`) and its endpoint already exist; R5 re-surfaces them through the chat pane and adds session-scoped grounding for follow-up turns.
- Not a free ride. R5 ships ~1.5–2.5 weeks of focused engineering (§7). The extensibility claim means R5 needs no new patterns — not that it needs no code.

### 1.3 Why this matters

Across R1–R4 plus parallel projects (Insights Engine, matter-ui-r1, semantic search, communication, playbook node builder, RAG references, et al.), Spaarke has built a sophisticated AI platform: 20 node executors, 5+ AI Search indexes, a hybrid retrieval pipeline, a triple-tier session store, a four-channel typed pane event bus, a 32-ADR governance baseline, and a 55+ entity Dataverse data model dedicated to AI orchestration. R5 is the moment that infrastructure gets exercised through a polished end-user flow that any operator can immediately understand the value of.

If the platform is well-designed, R5 should require minimal code. This design proves that hypothesis — and serves as the template for every subsequent AI capability (`/analyze`, `/compare`, `/ask`, `/translate`, etc.).

### 1.4 Parallel project context — Insights Engine R2

R5 is NOT the only project building on the AI platform right now. **Insights Engine R2 (Phase 1.5)** is concurrently in-flight (~62% complete by task count, ~3–4 weeks remaining as of 2026-06-03). Insights r2 extends the same platform for analytical/inference workflows (pre-authored playbooks + ad-hoc RAG over Insights index) while R5 extends it for conversational/generative workflows (chat-driven Summarize + session-scoped RAG).

The two projects share substantial infrastructure (`PlaybookExecutionEngine`, `RagService`, `SprkChatAgent` + factory, `ChatSessionManager`, `AnalysisChunk` SSE protocol, `sprk_analysisaction.sprk_systemprompt` JPS primitive, the 32-ADR governance baseline). They also have **five coordination touchpoints** (chat-agent tool registration, intent classifier catalog, additive PaneEventBus event types, `FieldDelta` SSE protocol reuse, cumulative ADR-010 DI pressure) and **seven explicitly verified non-conflicts** (no orchestrator collision, no schema collision, no index collision, no new ADRs, no chat-session model conflict, no node-executor conflict, no background-job conflict).

**Required reading before R5 implementation begins**: [`notes/insights-r2-coordination.md`](notes/insights-r2-coordination.md) — full alignment + conflict surface + reuse mandate + refinement-gate plan.

**Operative decision (2026-06-03)**: R5 implementation proceeds in two passes:
- **Pass 1 (now → mid-July 2026)**: R5 Phase 1 platform extensions (session indexing, file manifest, SSE deltas, RAG `sessionId` filter, Structured Outputs switch, cleanup job, telemetry) — fully independent of Insights r2 remaining work; safe to spec and execute now.
- **Pass 2 (post-Insights-r2-close)**: Refinement gate. Re-read this design.md against the then-completed Insights r2 work; update §2.6 / §2.8 / §4.5 / §7 as needed; only then proceed with Phase 2 vertical slice (chat-tool registration, `StructuredOutputStreamWidget`, `FilePreviewWidget`, slash-command extension).

The refinement gate is **mandatory**, not optional. See §7.4 below for the gate's trigger, scope, and what R5 work proceeds in parallel beforehand.

---

## 2. Investment Surfacing Map — What R5 Exercises

This section makes explicit which existing components R5 stands on. Every row here is shipped, tested, and in production.

### 2.1 Execution layer

| Component | Purpose | Reused by R5 for |
|---|---|---|
| `AnalysisOrchestrationService` + 3 sub-services (`AnalysisDocumentLoader`, `AnalysisRagProcessor`, `AnalysisResultPersistence`) | Central orchestration of AI analysis runs; `IAsyncEnumerable<AnalysisStreamChunk>` streaming | Summarize playbook execution + result persistence |
| `PlaybookOrchestrationService` + `INodeExecutor` framework (20 executors) | Node-based playbook execution with topological sort + parallel batching | Summarize playbook nodes (AiAnalysisNodeExecutor) + post-summary chain (DeliverToIndexNodeExecutor for session-files indexing) |
| Summarize playbook (`4a72f99c-a119-f111-8343-7ced8d1dc988`) | Multi-file aware summarization; produces `DocumentAnalysisResult` (tldr, summary, fileHighlights[], practiceAreas, mentionedParties, callToAction, shortSummary, confidence) | The actual summary generation |
| `SprkChatAgent` + `SprkChatAgentFactory` (ADR-013, ADR-032) | Chat agent with middleware stack; kill-switch via `NullSprkChatAgentFactory` | Both R5 entry points (agent tool + direct endpoint) go through the factory and inherit middleware |

### 2.2 Retrieval layer

| Component | Purpose | Reused by R5 for |
|---|---|---|
| `RagService` (~1253 LOC) + `RagIndexingPipeline` (~1800 LOC) | Hybrid search (BM25 + vector + RRF + semantic ranking); chunking + embedding + indexing | Session-scoped retrieval over uploaded files; multi-turn grounded Q&A (Step 10b) |
| `EmbeddingCache` (Redis, SHA256 keys, 7-day TTL) per ADR-014 | Query-time embedding cache | Reused unchanged; covers session file re-uploads across sessions |
| AI Search indexes (`spaarke-knowledge-index-v2`, `spaarke-rag-references`, `discovery-index`, `spaarke-invoices-dev`, per-tenant dedicated, customer-owned) | Org-scoped + reference + invoice retrieval | R5 adds a 5th index: `spaarke-session-files` with identical schema, separate document set |
| `ReferenceRetrievalService` | L1 curated-reference retrieval for `AiAnalysisNodeExecutor` | Unchanged; complements session-scoped retrieval |
| `text-embedding-3-large` (3072 dims) | Primary embedding model | Same for session files |

### 2.3 Session + state layer

| Component | Purpose | Reused by R5 for |
|---|---|---|
| `ChatSessionManager` + `ChatHistoryManager` | Session lifecycle, history persistence | Extended with file manifest (no new tier) |
| Redis hot tier (24h sliding TTL; key `chat:session:{tenantId}:{sessionId}`) per ADR-009 | Hot session state | Session file manifest lives here |
| Cosmos warm tier (write-through, fire-and-forget) | Durable session state | Write-through propagates file manifest |
| Dataverse cold tier (`sprk_aichatsummary`) | Audit trail | Unchanged |

### 2.4 Chat agent middleware stack (ADR-013, ADR-016, ADR-032)

Every agent created via `SprkChatAgentFactory.CreateAgentAsync()` is automatically wrapped in:
1. **`AgentCostControlMiddleware`** — per-session token budget (default 10K), `BudgetExceededMessage` on overshoot
2. **`SafetyPipelineMiddleware`** — `PromptShieldService` (Azure AI Content Safety Prompt Shields, jailbreak detection) + `GroundednessCheckService` (post-execution validation)
3. **Telemetry** via `ILogger<SprkChatAgent>` + OpenTelemetry

`CompoundIntentDetector` gates multi-tool intents via plan_preview before execution (AIPU2 task 071).
`ICapabilityRouter` (AIPU2-061) provides per-turn tool resolution.

R5 endpoints MUST route through the factory; bypassing forfeits all of this for free.

### 2.5 SSE streaming protocol

`AnalysisChunk` discriminated union (`Type` = "progress" | "result" | "complete" | "error") drives current SSE streams. The existing summarize endpoint (`POST /api/workspace/files/summarize`) emits `progress` events per pipeline step (`document_loaded`, `extracting_text`, ...) followed by a single `result` event with the full `DocumentAnalysisResult`.

R5 extends the union with a new `FieldDelta` variant (§4.3) — additive, backwards-compatible.

### 2.6 UI component layer

| Component | Library | Reused by R5 for |
|---|---|---|
| `SlashCommandMenu` + `useSlashCommands` + `DEFAULT_SLASH_COMMANDS` | `@spaarke/ui-components` | The `/summarize` entry surface (already shipped; R5 extends semantics only) |
| `RichFilePreviewDialog` (iframe + metadata pane + prev/next + 3-dot menu + `onFetchSummary` + `onToggleWorkspace` callbacks) | `@spaarke/ui-components` | Renderer core extracted for non-modal Context-pane use |
| `DocumentRowMenu` (12 actions in 3 groups: preview, aiSummary, openFile, findSimilar / download, copyLink, email, openRecord / toggleWorkspace, pinToTop, rename, delete) | `@spaarke/ui-components` | Reused unchanged; `aiSummary` + `toggleWorkspace` already designed for this flow |
| `AnalysisEditorWidget` (section-based) | `@spaarke/ai-outputs` | Stays as the static analysis viewer; R5 does NOT extend it for streaming |
| `DocumentViewerWidget` (R4 W-4 stub) | `@spaarke/ai-widgets` | Upgraded to use the extracted `RichFilePreview` renderer (shared with Context) |
| `PaneEventBus` (closed 4-channel union: workspace, context, conversation, safety) per ADR-030 | `@spaarke/ai-widgets` | R5 dispatches via existing channels; NO new channels |
| `WorkspaceWidgetRegistry` (lazy-factory) | `@spaarke/ai-widgets` | R5's `StructuredOutputStreamWidget` registers here |
| Get Started cards + Playbook gallery | `@spaarke/ai-widgets` | R5 adds one Get Started card; existing cards unchanged |

### 2.7 Data model

55+ AI-related Dataverse entities including `sprk_document`, `sprk_analysis`, `sprk_analysisaction`, `sprk_analysisplaybook`, `sprk_analysisoutput`, `sprk_analysischatmessage`, `sprk_analysisemailmetadata`, `sprk_analysisworkingversion`, `sprk_analysisskill`, `sprk_analysisknowledge`, `sprk_analysistool`, `sprk_aichatsession`, `sprk_aichatsummary`, `sprk_aiknowledgesource`, `sprk_aiknowledgedeployment`, `sprk_aiactiontype`, `sprk_aimodeldeployment`, `sprk_airetrievalmode`, join tables, and configuration tables.

R5 adds **zero new entities**. New capability arrives as configuration: one new `sprk_analysisaction` seed record (Summarize Document) and configuration in `sprk_analysisplaybook` linkage.

### 2.8 ADRs that constrain R5 (binding)

| ADR | Title | R5 implication |
|---|---|---|
| ADR-001 | Minimal API + BackgroundService | R5 endpoints in BFF only |
| ADR-006 | Code Pages default UI | SpaarkeAi shell is Code Page; new widgets target React 19 |
| ADR-007 | SpeFileStore facade | File ops via `SpeFileStore.UploadSmallAsync` / `DownloadFileAsync` only |
| ADR-008 | Endpoint filters for auth | New endpoint MUST add `[EndpointFilter(typeof(AuthorizationFilter))]` |
| ADR-009 | Redis-first caching | Session file manifest in Redis hot tier |
| **ADR-010** | DI minimalism (≤15 `Program.cs` lines via feature modules; concretes by default) | **R5 adds new services via feature module extensions** — extending `AddAnalysisServicesModule` or adding `AddR5SessionFilesModule()` (whichever has better cohesion). `Program.cs` line count unchanged. Concrete classes per ADR-010; production classes made `virtual` where Null-Object subclassing is required (per ADR-032). The "≤15" is `Program.cs` lines, not total registrations (Phase 5 baseline acknowledges 265 actual registrations as accepted legacy state). |
| ADR-012 | Shared component library | New widgets in `@spaarke/ai-widgets` (workspace) and `@spaarke/ui-components` (preview core) |
| ADR-013 | AI architecture (BFF-only) | R5 endpoints in BFF; agents via factory |
| ADR-014 | AI caching with `tenantId` isolation | Session-files index documents MUST carry `tenantId` and `sessionId` |
| **ADR-018** | Feature flags + kill switches (capability-scoped per 2026-06-03 R5 refinement) | **R5 introduces NO new feature flags.** Session-files and structured-streaming sub-services are unconditionally registered. Kill-switch coverage inherited from existing `Analysis:Enabled` / `Chat:Enabled` flags — the chat agent factory returns Null when those are OFF, and R5 sub-services downstream of it inherit the disabled state without needing their own flag. (Adding per-service flags would multiply ADR-032 boilerplate; see ADR-018 Flag Scope Discipline.) |
| ADR-021 | Fluent UI v9 (semantic tokens, dark-mode) | New widgets dark-mode tested |
| ADR-022 | React 19 for Code Pages | All new R5 widgets React 19 |
| ADR-026 | Code Page build (Vite + singlefile) | No build-tooling change |
| ADR-028 | Spaarke Auth v2 | New endpoint inherits Auth v2 filter |
| **ADR-030** | PaneEventBus (closed 4-channel union; event types additive) | **Channels unchanged.** R5 adds new **additive event types** within existing channels: `workspace.streaming_started`, `workspace.field_delta`, `workspace.streaming_complete`, `context.files_staged`, `context.file_selected`. Streaming a summary is semantically a workspace event (a workspace tab is changing state), not a context event — so streaming-complete dispatches to `workspace`, not `context`. Existing subscribers ignore unknown event-type discriminants and continue to compile. |
| ADR-031 | Stage lifecycle (welcome/loading/active-chat/review) | R5 UI respects `useShellStage()` |
| **ADR-032** | BFF Null-Object kill-switch | **No new Null impls needed for R5.** R5 services are unconditionally registered (per ADR-018 capability-scoped flag discipline above). The existing `NullSprkChatAgentFactory` handles broader AI-flag-OFF; R5 sub-services downstream inherit that state. New Null subclasses are introduced ONLY if R5 surfaces a service needing its own kill switch — none expected based on current scope. |

### 2.9 Decisions that constrain R5

- **D-04 (Insights honesty contract)**: "Structured decline-to-find over hallucination." If the summarize playbook detects insufficient content, it chains to `DeclineToFindNode` (zero-LLM, deterministic 5-field response: `reason`, `explanation`, `suggested_actions`, `confidence_in_decline`, `metadata`).
- **CLAUDE.md §10 BFF Hygiene**: R5 MUST produce a Placement Justification (§5 below) and verify publish-size impact (≤60 MB compressed ceiling; current baseline ~45.65 MB).

### 2.10 BFF placement (pointer)

R5 places one new endpoint + one new agent tool + one new orchestrator class + one hosted cleanup job in the BFF. Full Placement Justification per CLAUDE.md §10 is in §5 below.

### 2.11 Reuse mandate — components R5 MUST reuse, NOT rebuild

This is the explicit no-rebuild list. Before any R5 task adds a new component, the implementer MUST confirm there is no existing equivalent. **Rebuilding any of these is a defect**, not a design choice.

**Backend services R5 MUST reuse (not parallel)**:
- `AnalysisOrchestrationService` (+ `AnalysisDocumentLoader`, `AnalysisRagProcessor`, `AnalysisResultPersistence` sub-services) — Summarize execution path goes through these
- `PlaybookExecutionEngine` — JPS playbook execution; R5 adds zero new node executors
- `RagService` + `RagIndexingPipeline` — R5 EXTENDS `RagSearchOptions` with `sessionId` filter; does not create a parallel retrieval service
- `EmbeddingCache` (Redis, 7-day TTL) — reused unchanged
- `ChatSessionManager` + `ChatHistoryManager` — R5 EXTENDS `ChatSession` model with `UploadedFiles[]`; does not parallel the manager
- `SprkChatAgent` + `SprkChatAgentFactory` — R5 registers a new tool function; does not create a parallel agent
- `SpeFileStore` (per ADR-007) — all file ops go through this facade
- `ReferenceRetrievalService` — reused unchanged for cross-corpus grounding
- Safety pipeline (`PromptShieldService`, `GroundednessCheckService`) + cost control (`AgentCostControlMiddleware`) — auto-inherited via factory

**Existing summarize machinery R5 MUST reuse**:
- Summarize playbook (`4a72f99c-a119-f111-8343-7ced8d1dc988`) — same playbook for chat invocation
- `DocumentAnalysisResult` schema (tldr, summary, fileHighlights[], practiceAreas, mentionedParties, callToAction, shortSummary, confidence) — same output shape
- `AnalysisChunk` SSE envelope — R5 EXTENDS with `FieldDelta` variant; does not introduce a new envelope

**JPS primitives R5 MUST reuse**:
- `sprk_analysisaction.sprk_systemprompt` as the prompt-bearing primitive (per Insights r2 explicit terminology lock-in) — R5 adds a new seed row, NOT a new entity
- `sprk_analysisplaybook` for playbook configuration — R5 adds a new configuration, NOT a new entity

**Frontend components R5 MUST reuse**:
- `SlashCommandMenu` + `useSlashCommands` + `DEFAULT_SLASH_COMMANDS` — R5 extends `/summarize` semantics; does not build a new prompt selector
- `RichFilePreviewDialog` renderer core — extracted as a non-modal primitive; reused in BOTH the Context-pane file preview AND the upgraded `DocumentViewerWidget`. R5 does NOT build a parallel preview component.
- `DocumentRowMenu` (12 actions in 3 groups) — `aiSummary` + `toggleWorkspace` actions reused unchanged
- `PaneEventBus` (4 closed channels) — R5 adds additive event types within existing channels; does NOT add a new channel
- `WorkspaceWidgetRegistry` (lazy-factory) — new `StructuredOutputStreamWidget` registers here
- Get Started cards catalog — R5 adds one entry; does not parallel the catalog

**Components R5 ADDS (because no equivalent exists)**:
- `StructuredOutputStreamWidget` — schema-driven streaming widget; renders any structured output progressively via `FieldDelta` events. **NOT** a duplicate of `AnalysisEditorWidget` (which is section-based and static).
- `FilePreviewContextWidget` — Context-pane shell wrapping the extracted `RichFilePreview` renderer for non-modal use. **NOT** a duplicate of `RichFilePreviewDialog` (which remains the modal wrapper).
- `SessionSummarizeOrchestrator` (concrete class, feature-module-registered) — shared internal method bridging the agent-tool path and the direct endpoint. **NOT** a duplicate of `AnalysisOrchestrationService` (which it calls).
- Session-files cleanup `IHostedService` — new background job.

**Explicitly prohibited (R5 MUST NOT)**:
- Build a new orchestrator paralleling `AnalysisOrchestrationService` or `InsightsOrchestrator`
- Build a new RAG search service paralleling `RagService`
- Build a new session-management layer paralleling `ChatSessionManager`
- Build a new chat agent paralleling `SprkChatAgent`
- Build a new file-preview component paralleling `RichFilePreviewDialog`
- Build a new SSE envelope paralleling `AnalysisChunk` (R5 extends; does not parallel)
- Add a new prompt-bearing entity (`sprk_analysisaction.sprk_systemprompt` is the primitive — per Insights r2 explicit lock-in)
- Add a new playbook orchestration layer paralleling `PlaybookExecutionEngine`
- Add a new PaneEventBus channel (closed at 4 per ADR-030)

**Verification at task time**: every implementation task in spec.md/POML MUST run `/conflict-check` before merge AND cite the existing component being reused (or justify the gap if proposing new).

Full coordination context (including reuse mandate cross-referenced against in-flight Insights Engine R2): [`notes/insights-r2-coordination.md`](notes/insights-r2-coordination.md) §3 and §5.

---

## 3. Vertical Slice — "Summarize a Document"

### 3.1 User flow (acceptance criteria)

This is the operator-authored flow from project kickoff, formalized as acceptance criteria.

1. **Prompt selection (Assistant pane)**: User types `/` in the Assistant pane input. `SlashCommandMenu` opens (per `useSlashCommands`). User selects `/summarize` (existing system command).
2. **Two valid orderings**:
   - (a) User uploads files first via the existing chat upload control, then types `/summarize`. Files are already staged in `ChatSession.UploadedFiles[]` (§4.4).
   - (b) User types `/summarize` first. The Assistant emits a deterministic interjection: "Upload the file(s) you'd like me to summarize." User then uploads.
3. **Playbook invocation**: The `/summarize` slash command resolves to the Summarize playbook (GUID `4a72f99c-a119-f111-8343-7ced8d1dc988`). Either entry path (agent tool or direct endpoint, §4.5) reaches the same internal invocation.
4. **Multi-file support**: The Summarize playbook is already multi-file aware — it produces `DocumentAnalysisResult.fileHighlights[]` only when more than one file is supplied.
5. **Combined-summary acknowledgement**: When more than one file is staged, the Assistant deterministically interjects "I'll provide a combined summary for the files you uploaded" before the streaming summary begins. (Chat-layer logic, not playbook-emitted, to keep the playbook focused on analysis.)
6. **Playbook execution**: `AnalysisOrchestrationService.ExecutePlaybookAsync()` runs the Summarize playbook with session-scoped grounding (`RagSearchOptions.sessionId` filter).
7. **Context pane**: When files are staged in a session, the Context pane transitions to a new state and renders source files. Single file: rendered preview (via the extracted `RichFilePreview` renderer core). Multi-file: list of file cards → click to swap active preview. Each card has a 3-dot menu reusing `DocumentRowMenu` (preview, aiSummary, findSimilar, toggleWorkspace).
8. **Workspace pane streaming**: A new workspace tab opens hosting the `StructuredOutputStreamWidget` (§4.5). It subscribes to `FieldDelta` SSE events tagged by JSON path. As the LLM emits tokens via Azure OpenAI Structured Outputs, the widget populates `tldr` first, then `summary`, then per-file `fileHighlights[i].summary`, then `mentionedParties`, then `callToAction` — each field animates in ChatGPT/Claude-style, NOT as a final-result plop.
9. **Other tabs untouched**: The new tab opens additively; existing workspace tabs remain open. FIFO eviction (`MAX_WORKSPACE_TABS` ≈ 8) applies at cap.
10. **Persistence**: When streaming completes, the full `ISummarizeResult` is written to the workspace tab state via existing `PATCH /api/ai/chat/sessions/{id}/tabs`. On browser refresh, the tab restores statically — no re-streaming.

### 3.2 Follow-up turn flow (Step 10 + 10b)

11. **"Summarize just one of them"** (multi-turn intent routing):
    - **LLM tool-call path**: User types natural language. `CompoundIntentDetector` detects re-invocation of summarize with a subset; agent calls `InvokeSummarizePlaybookTool` with `{ fileIds: [subset] }`.
    - **UI affordance path**: Each file preview card has a "Summarize this only" button. Direct dispatch to the same internal invocation.
    - Both produce a NEW workspace tab; the prior summary tab remains.
12. **General grounded Q&A** (Step 10b — "are there litigation risks?", "who are the parties?", "translate to Spanish?"):
    - SprkChat agent already does grounded chat via tool calls into `RagService`.
    - R5's extension: `RagSearchOptions` accepts `sessionId` filter; `RagService.SearchAsync()` returns chunks from `spaarke-session-files` (the session's uploaded files only, plus optionally `spaarke-knowledge-index-v2` if cross-corpus grounding is desired).
    - LLM responds in the Assistant pane with grounded citations.

### 3.3 Stage lifecycle (ADR-031)

| Stage | Trigger | Pane behavior |
|---|---|---|
| welcome | No files, no playbook selected | Assistant: WelcomePanel + Get Started cards (including new "Summarize a Document" card). Context: playbook-gallery. Workspace: empty placeholder. |
| loading | Files uploading / playbook starting | Assistant: progress indicator. Context: file cards begin appearing. Workspace: empty. |
| active-chat | Playbook running / streaming summary | Assistant: chat with interjections. Context: file preview(s). Workspace: tab with `StructuredOutputStreamWidget` populating. |
| review | Streaming complete | Assistant: chat continues. Context: file preview(s). Workspace: completed summary tab + any other tabs. |

---

## 4. R5 Deliverables (11 work items)

### 4.1 New `sprk_analysisaction` seed: "Summarize Document for Chat" (XS)

Currently the Summarize playbook is invoked via `POST /api/workspace/files/summarize` (LegalWorkspace wizard path). A new `sprk_analysisaction` record dedicated to the chat-pane flow lets us:
- Tune the system prompt for conversational context
- Configure output schema for streaming-aware structured output
- Maintain the wizard's action record unchanged

Configuration: Dataverse seed row + JSON output schema (no C# code for this specific item — the new action is data, not code).

### 4.2 Session-scoped RAG indexing (M)

**New AI Search index**: `spaarke-session-files`
- Schema: identical to `spaarke-knowledge-index-v2` (3072-dim HNSW, BM25 + vector + semantic config)
- Required fields: `tenantId`, `sessionId`, plus the existing chunk schema
- Provision via `infrastructure/ai-search/spaarke-session-files.json` (Bicep + index JSON)

**Pipeline extensions**:
- Extend `RagSearchOptions` with optional `sessionId` filter
- Extend `RagIndexingPipeline` to write to either the customer-corpus index OR the session-files index, parameterized by call site
- New session-files cleanup job: scheduled (e.g., every 6h) — deletes documents from `spaarke-session-files` where `sessionId` is not in the active Redis session set. Idempotent. Triggered immediately on explicit session-end events.

**DI**: Cleanup job registered as `IHostedService` inside the existing `AddAnalysisServicesModule` (per ADR-010 feature-module pattern). `RagSearchOptions` + `RagIndexingPipeline` modifications are in-place edits — no new registrations.

**Per-session cost-control caps** (also baked into ChatSession schema, §4.4):
- Max 20 files per session (hard cap; rejects 21st upload)
- Files < 500 tokens skip chunking, index as single chunk
- Aggressive cleanup-on-session-end (don't wait for the scheduled sweep)

### 4.3 SSE structured-field deltas (`FieldDeltaChunk`) (M)

**Protocol extension** (backwards-compatible):

```csharp
record AnalysisChunk(
  string Type,        // existing: "progress" | "result" | "complete" | "error" | NEW: "delta"
  string? Content,
  bool Done,
  string? Summary,
  DocumentAnalysisResult? Result,
  string? Error,
  FieldDelta? Delta);  // NEW (nullable)

record FieldDelta(
  string Path,        // JSON path, e.g., "tldr", "fileHighlights[0].summary"
  string Content,     // token chunk for this field
  int Sequence);      // for ordering correctness if deltas are reordered
```

**Implementation**:
- Switch the Summarize playbook execution path to use Azure OpenAI Structured Outputs (JSON Schema mode) so the model natively emits structured JSON as it streams
- BFF runs an incremental JSON parser; emits `FieldDelta` events tagged by JSON path as values stream in
- Existing wizard consumers (LegalWorkspace `streamSummarize()`) ignore unknown `delta` events — fully backwards-compatible

### 4.4 ChatSession file manifest (S)

Extend `ChatSession` model (Redis hot + Cosmos warm) with:

```csharp
record ChatSessionFile(
  string FileId,         // stable session-scoped ID
  string FileName,
  string ContentType,
  long SizeBytes,
  string SearchDocumentIdsCsv,  // IDs in spaarke-session-files index
  DateTimeOffset UploadedAt);
```

Plus a `List<ChatSessionFile> UploadedFiles` property on `ChatSession`. Triple-tier persistence inherited automatically (Redis hot + Cosmos warm via existing `ChatSessionManager` write-through; Dataverse cold audit unchanged).

**DI**: No new registration — `ChatSessionManager` already manages the session record; manifest is an additive property on the model.

### 4.5 Chat-driven summarize entry points (dual) (S each)

**Path A — Agent tool function** (`InvokeSummarizePlaybookTool`):
- Registered via existing tool-registration mechanism on `SprkChatAgent`
- Parameters: `{ fileIds: string[]?, style?: string }` (omitting `fileIds` summarizes all session files)
- LLM decides to call it based on user intent (`/summarize`, "summarize the contracts I uploaded", "summarize just file 2", etc.)
- Inherits middleware automatically

**Path B — Direct endpoint** (`POST /api/ai/chat/sessions/{sessionId}/summarize`):
- Body: `{ fileIds: string[]?, style?: string }`
- Slash command (`/summarize`) routes here for predictable, low-latency execution path
- Goes through `SprkChatAgentFactory` (per ADR-013) — inherits middleware
- Auth v2 filter per ADR-028; ProblemDetails errors per ADR-019

Both paths converge on a shared internal method:
```csharp
internal async Task<IAsyncEnumerable<AnalysisChunk>> SummarizeSessionFilesAsync(
  string sessionId, IReadOnlyList<string>? fileIds, string? style, CancellationToken ct)
```

That method:
1. Loads `ChatSession.UploadedFiles` from Redis
2. Filters to `fileIds` if provided (else all files)
3. Invokes `AnalysisOrchestrationService.ExecutePlaybookAsync()` with the Chat-Summarize playbook ID + session-scoped RAG retrieval
4. Streams `AnalysisChunk` (including new `FieldDelta` deltas) back to the caller
5. On completion, writes `ISummarizeResult` to workspace tab state

**DI**: A new `SessionSummarizeOrchestrator` concrete class (encapsulates the shared internal method) registered inside `AddAnalysisServicesModule` per ADR-010 feature-module pattern. The agent tool function is a stateless wrapper that takes the orchestrator via DI. No new top-level `Program.cs` line. No interface introduced (no genuine seam beyond testing, which uses the concrete class directly).

### 4.6 `StructuredOutputStreamWidget` (Workspace, M-L)

**Library**: `@spaarke/ai-widgets` (NEW workspace widget; registered via `WorkspaceWidgetRegistry`)
**Pattern**: schema-driven; reusable for ANY future structured-output playbook (`/analyze`, `/compare`, etc.)

**API**:
```typescript
interface StructuredOutputStreamWidgetData {
  schema: JSONSchema;           // declarative field layout (TL;DR, summary, per-file, etc.)
  uiHints?: SchemaUIHints;      // per-field rendering hints (heading style, list vs prose, etc.)
  sessionId: string;
  invocationId: string;          // ties to SSE stream
  initialState?: PartialOutput;  // for restoration after refresh
  onComplete?: (result: unknown) => void;
}
```

**Behavior**:
- Subscribes to `FieldDelta` events via `AiSessionProvider` streaming
- Maintains a partial-output object keyed by JSON path
- Renders each field progressively as deltas arrive (no plop)
- Animates new content with cursor (ChatGPT/Claude visual)
- Once complete, calls `onComplete` and persists final state via the standard workspace tab persistence path

**Why schema-driven, not Summarize-specific**: The user's directive to "build for the next use cases." `/analyze`, `/compare`, `/ask` all produce structured outputs with different schemas. A schema-driven widget renders all of them.

### 4.7 Context-pane file preview (M)

**Renderer core extraction**:
- Pull the iframe + metadata + prev/next + 3-dot menu UI from `RichFilePreviewDialog` into a reusable primitive `RichFilePreview` (no Dialog chrome)
- `RichFilePreviewDialog` continues to wrap it for modal use cases (existing consumers unchanged)
- Upgraded `DocumentViewerWidget` (R4 stub) consumes it for Workspace destination

**Context-pane widget shell**:
- New `FilePreviewContextWidget` (library: `@spaarke/ai-widgets/context/`)
- Single file: renders `RichFilePreview` inline (no Dialog)
- Multi-file: renders a `FileListView` (cards with name + type + size); click swaps active preview
- Each card's 3-dot menu reuses `DocumentRowMenu` (existing component — `aiSummary` action wires to a single-file invocation of `InvokeSummarizePlaybookTool`; `toggleWorkspace` opens the file in a Workspace tab via the upgraded `DocumentViewerWidget`)

**File source**: previews fetched via `SpeFileStore.GetFilePreviewUrlAsync()` (PDF.js for PDFs, Graph preview for Office). Files in session storage have stable preview URLs for the session lifetime.

### 4.8 Slash command `/summarize` semantic extension (XS)

- Update `/summarize` description in `DEFAULT_SLASH_COMMANDS` to "Summarize uploaded files or the active document"
- Intent handler: if `ChatSession.UploadedFiles.length > 0`, route to session-files summarize; else route to active-workspace-document summarize (existing behavior — wizard flow); else emit the "upload files" interjection (step 2b)

### 4.9 Get Started welcome card (optional, XS)

Add "Summarize a Document" as a `GetStartedCardsWidget` entry. On click, the Assistant pane:
- Opens the file upload dialog directly
- Pre-fills the chat input with `/summarize ` (waiting for upload)
- Discoverable parallel entry to the slash command

### 4.10 ChatSession file lifecycle UX (S)

- File chips in the Assistant input area (visible always when session has files)
- Persistent "N files attached" indicator with click-to-expand
- Per-file remove action (also removes from `spaarke-session-files` index)
- Inline file confirmations in chat (file appears in the message bubble where uploaded)

### 4.11 Telemetry + cost observability (S)

- New telemetry event: `r5.summarize.invocation` with dimensions (path: agent_tool|direct_endpoint, file_count, total_tokens, latency_ms, completion_status)
- Per-session cost tracking continues via existing `AgentCostControlMiddleware`
- New metric: session-files index size (count per active session) — capacity-planning signal

---

## 5. BFF Placement Justification (per CLAUDE.md §10)

**Component**: `POST /api/ai/chat/sessions/{id}/summarize` endpoint + `InvokeSummarizePlaybookTool` agent tool + session-files cleanup job

**Placement**: BFF (`src/server/api/Sprk.Bff.Api/`)

**Decision criteria** (from `.claude/constraints/bff-extensions.md`):

| Criterion | Assessment |
|---|---|
| Tight latency coupling (TTFB ≤ 500ms per ADR-013) | ✅ All dependencies BFF-resident (AnalysisOrchestrationService, RagService, ChatSessionManager, SpeFileStore). Network hop to separate service would breach budget. |
| Auth boundary alignment (Auth v2 per ADR-028) | ✅ Endpoint inherits Auth v2 filter automatically; no inter-service token forwarding. |
| Reuses existing BFF services | ✅ Every dependency is BFF-resident; no duplication. |
| Adds new NuGet packages | ❌ Zero new packages. |
| Independently deployable | ❌ Tied to chat session lifecycle + SSE streaming pipeline. |
| Independently scalable | ❌ Scales with BFF chat traffic. |

**Conclusion**: Per ADR-013 four-criterion exception test, no criteria met for separate deployment. Placement: BFF.

**Publish-size impact**:
- Pre-R5 baseline: ~45.65 MB compressed (R4 Phase 5 Outcome A measurement)
- Estimated R5 delta: < +0.5 MB compressed (thin endpoint + tool registration + cleanup job; no new packages)
- Projected post-R5: ~46.15 MB compressed
- Ceiling: 60 MB compressed (NFR-01)
- Headroom: ~14 MB

Per-task publish-size verification will be enforced via the existing `dotnet publish` verification step in each implementation task's notes (per CLAUDE.md §10).

**DI registration**: R5 introduces one new concrete class (`SessionSummarizeOrchestrator`, §4.5) and one new hosted service (session-files cleanup job, §4.2), both registered inside an existing or new feature module per ADR-010. `Program.cs` line count is unchanged. No new top-level registrations.

**Feature-flag scope**: R5 introduces NO new feature flags (per ADR-018 Flag Scope Discipline, 2026-06-03 refinement). All new R5 services register unconditionally; kill-switch coverage inherited from existing `Analysis:Enabled` / `Chat:Enabled` flags via the `NullSprkChatAgentFactory` upstream gate.

**Test obligation**: Per the F.1 / F.2 / F.3 sub-mechanisms in `bff-extensions.md`, the new endpoint MUST have corresponding tests in `tests/unit/Sprk.Bff.Api.Tests/`. The endpoint is registered unconditionally (no feature-flag gating per the above), so no asymmetric-registration risk; ADR-032 Null-Object pattern does NOT apply to R5 services.

---

## 6. Future Use-Case Validation

This section validates that R5's foundations support the next AI capabilities **without further platform work**. If these fall out of R5's design, the platform-extension claim is validated.

### 6.1 `/analyze` — general analysis on uploaded files

- Reuse `StructuredOutputStreamWidget` with the existing `DocumentAnalysisResult` schema
- New `sprk_analysisaction` seed (Analyze Document); new system prompt
- Direct endpoint or agent tool — both paths reuse the chat-driven internal method (just different action ID)
- **Effort**: XS (configuration + small intent-routing addition)

### 6.2 `/compare` — compare two or more uploaded files

- New schema: `ContractComparisonResult` (already exists in `@spaarke/ai-outputs` — different output widget)
- New `sprk_analysisaction` seed; system prompt configures multi-file diff
- `StructuredOutputStreamWidget` consumes the new schema (no widget change — it's schema-driven)
- **Effort**: S

### 6.3 `/ask` — free-form grounded Q&A

- Already works via SprkChat's existing tool-call grounding. R5's session-scoped indexing makes the answers grounded in uploaded files.
- No code changes once R5 ships; just user education ("/ask" already does this)
- **Effort**: zero

### 6.4 `/translate` — translate uploaded files

- New `sprk_analysisaction` seed; system prompt configures translation
- Output schema: `{ translatedText: string, sourceLanguage: string, targetLanguage: string }`
- `StructuredOutputStreamWidget` renders it (or a new specialized widget if richer layout is wanted)
- **Effort**: XS–S

### 6.5 `/extract` — extract key clauses / entities

- New `sprk_analysisaction` seed; system prompt configures clause extraction
- Output schema: `{ clauses: ClauseEntry[], confidence: number }`
- `StructuredOutputStreamWidget` renders progressively
- **Effort**: S

### 6.6 Validation

R5 ships approximately 11 work items totaling ~1.5–2.5 engineering weeks. Each subsequent AI capability in §6.1–6.5 is ≤1 week. The platform's extensibility claim is validated by the cost ratio.

---

## 7. Phasing — Sequential

Per the operator's earlier decision, R5 ships in three sequential phases. Each phase produces shippable, deployable state.

### Phase 1: Platform extensions (1 week)

- Provision `spaarke-session-files` AI Search index (Bicep + index JSON)
- Extend `RagSearchOptions` + `RagIndexingPipeline` with `sessionId` parameter
- Extend `ChatSession` model with `UploadedFiles[]` (Redis hot + Cosmos warm)
- Add `FieldDelta` variant to `AnalysisChunk`; implement BFF incremental JSON parser
- Switch Summarize playbook execution to Azure OpenAI Structured Outputs mode
- Session-files cleanup job
- Telemetry events
- Tests + BFF publish-size verification

### Phase 2: Vertical slice (1 week)

- New `sprk_analysisaction` seed: "Summarize Document for Chat" (Dataverse data deploy)
- New `sprk_analysisplaybook` configuration
- New `SessionSummarizeOrchestrator` concrete class (registered in feature module per ADR-010)
- Register `InvokeSummarizePlaybookTool` on `SprkChatAgent` (delegates to orchestrator)
- Add `POST /api/ai/chat/sessions/{id}/summarize` endpoint (delegates to orchestrator)
- Add additive PaneEventBus event types (`workspace.streaming_started`, `workspace.field_delta`, `workspace.streaming_complete`, `context.files_staged`, `context.file_selected`) per ADR-030
- Build `StructuredOutputStreamWidget` (`@spaarke/ai-widgets`)
- Extract `RichFilePreview` renderer core; build `FilePreviewContextWidget`
- Upgrade `DocumentViewerWidget` to use the extracted renderer
- Wire slash command `/summarize` semantic extension
- Chat-pane orchestration UX (file chips, interjections, persistence)
- Tests + integration verification

### Phase 3: Polish + future-use validation (0.5 week)

- Add `/analyze` as configuration-only proof point (validates §6.1)
- Get Started "Summarize a Document" card
- Telemetry dashboards (Application Insights queries / Grafana panel)
- Operator-led end-to-end testing per the kickoff doc's discoverability questions
- Lessons-learned + R6 backlog capture

### 7.4 Refinement gate — MANDATORY between Phase 1 and Phase 2

**Operative constraint** (from §1.4): R5 implementation runs in two passes coordinated with Insights Engine R2's completion.

**Pass 1 (independent of Insights r2)** — Phase 1 platform extensions above. Safe to spec → tasks → implement now. Each Phase 1 work item was vetted against Insights r2 remaining work and confirmed independent. See [`notes/insights-r2-coordination.md`](notes/insights-r2-coordination.md) §6.4 for the per-item independence assessment.

**Refinement gate** — fires when Insights r2 closes (all Wave D + Wave E + task 090 wrap-up ✅ and merged to master; estimated mid-to-late July 2026). At the gate, R5 design.md is **re-validated** against the then-completed Insights work:

1. Read Insights r2 lessons-learned.md (Wave 090 deliverable)
2. Re-validate §2.11 reuse mandate — confirm paths, ownership, any new shared components introduced by Insights waves D + E
3. Re-validate §4.5 chat-tool registration approach against Insights E3 (Spaarke Assistant integration) conventions
4. Re-validate intent-routing strategy against Insights E2 classifier catalog
5. Re-validate `FieldDelta` SSE protocol — did Insights E3 adopt it? Resolve any divergence
6. Re-validate PaneEventBus event-type discriminants — any name collisions to resolve
7. Re-run BFF Placement Justification with updated publish-size baseline
8. Update `notes/insights-r2-coordination.md` with closed touchpoints

**Pass 2 (post-refinement)** — Phase 2 vertical slice (chat-tool registration, `StructuredOutputStreamWidget`, `FilePreviewWidget`, slash-command semantic extension, chat-pane UX orchestration). Only proceeds after the refinement gate signs off.

**Risk if gate is skipped**: see [`notes/insights-r2-coordination.md`](notes/insights-r2-coordination.md) §6.5 (~rework of chat-tool conventions, classifier catalog, streaming protocol).

**Gate ownership**: project owner. Refinement pass output: updated design.md + updated coordination doc + green-light to run `/design-to-spec` for Phase 2 spec.md addendum.

---

## 8. Open Questions (to resolve during spec.md or implementation)

1. **Embedding model for session-files**: `text-embedding-3-large` (3072 dims) matches existing knowledge-index parity but is ~5× more expensive than `-3-small`. For ephemeral session data, `-3-small` may be acceptable. Decide based on early implementation cost telemetry.
2. **Structured Outputs vs Function Calling**: Azure OpenAI supports both. Structured Outputs is newer and stricter; Function Calling is more flexible. Validate which works better for incremental JSON streaming during Phase 1 spike.
3. **Cleanup job cadence**: scheduled every 6h vs every 1h. Trade-off: index storage cost vs job overhead. Start with 6h; monitor.
4. **Session-files index per-tenant routing**: Dedicated-model tenants get their own knowledge index. Should they get their own session-files index too? Likely yes for isolation; confirm during Phase 1.
5. **Editor widget destination** (out of scope for R5 but design relevant): when `toggleWorkspace` is clicked on a file preview, the Workspace tab opens the upgraded `DocumentViewerWidget`. Is that final, or is a true editor experience (annotation, redline) expected? Out of scope for R5 either way.

---

## 9. Out of Scope (explicit)

These are deliberately deferred to keep R5 focused:

- **Editor widget** (full document editing in Workspace tab — the destination of "Add to Workspace"). R5 lands `toggleWorkspace` on the upgraded viewer; richer editing is a future project.
- **Extracted-text Workspace widget** (toggle between rendered preview and extracted text). Out of scope unless trivial during Phase 2.
- **Tier 3 file grounding** (SharePoint Embedded reference-scoped persistence beyond chat session). R5 ships Tier 2 (session-lifetime).
- **Step 9 follow-on actions** (what user does WITH a completed summary — save, email, attach to record, create task). Future use case.
- **Calendar widget Direct registration** (per ground-truth survey § 5 known limitations).
- **Stages 2–4 header treatment** (A-1 deferred to Moment 2).
- **Kiota CVE chain upgrade** (separate project `spaarke-graph-sdk-kiota-upgrade-r1`).
- **BFF test-infrastructure cleanup** (separate dedicated project).
- **Iframe-wizards pattern enhancement** (separate project `spaarke-iframe-wizard-pattern-enhancement`).

---

## 10. Acceptance Criteria (gating R5 → R6 handoff)

R5 ships when ALL of these are demonstrable:

- [ ] Operator can upload 1+ files in the Assistant pane and successfully invoke `/summarize`
- [ ] Streaming summary appears in a new Workspace tab progressively (ChatGPT/Claude-style, NOT plop)
- [ ] Other workspace tabs remain untouched during streaming
- [ ] Context pane shows source file preview(s); multi-file shows list with click-to-swap
- [ ] After streaming completes, browser refresh restores the summary tab statically
- [ ] Follow-up question "summarize just one of the files" works via both LLM tool-call AND explicit UI button
- [ ] Follow-up general grounded Q&A ("are there litigation risks?") returns answers grounded in session files
- [ ] Both `/summarize` (slash command direct endpoint) and natural-language summarize requests (agent tool) produce identical output
- [ ] `/analyze` works as configuration-only follow-on (validates platform-extension claim)
- [ ] BFF publish-size delta ≤ +1 MB compressed (≤ 47 MB total)
- [ ] All tests pass; no new HIGH-severity CVEs
- [ ] Lessons-learned + R6 backlog produced

---

## 11. References

### R5 project documents
- [`README.md`](../README.md) — project overview
- [`notes/ground-truth-spaarkeai-state.md`](notes/ground-truth-spaarkeai-state.md) — code-grounded survey of shipped SpaarkeAi state at R4 close
- [`notes/user-testing-kickoff.md`](notes/user-testing-kickoff.md) — kickoff scoping doc (largely superseded by this design.md)
- [`notes/insights-r2-coordination.md`](notes/insights-r2-coordination.md) — **REQUIRED READING before any R5 implementation** — alignment + conflict surface + reuse mandate + refinement-gate plan covering the parallel Insights Engine R2 project

### Architecture docs (load-bearing for R5)
- [`docs/architecture/AI-ARCHITECTURE.md`](../../docs/architecture/AI-ARCHITECTURE.md) — `AnalysisOrchestrationService`, `IStreamingAnalysisToolHandler`, `ReferenceRetrievalService`, four-tier taxonomy
- [`docs/architecture/playbook-architecture.md`](../../docs/architecture/playbook-architecture.md) — `PlaybookOrchestrationService`, `INodeExecutor`, parallel batching, streaming
- [`docs/architecture/chat-architecture.md`](../../docs/architecture/chat-architecture.md) — `SprkChatAgent`, middleware stack, `CompoundIntentDetector`, `PaneEventBus` integration
- [`docs/architecture/rag-architecture.md`](../../docs/architecture/rag-architecture.md) — hybrid search, embedding cache, dual-index strategy, `RagIndexingPipeline`
- [`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — two-wrapper architecture (canonical)
- [`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`](../../docs/architecture/SPAARKEAI-COMPONENT-MODEL.md) — component inventory
- [`docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md`](../../docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md) — reuse audit
- [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — workspace pane pipeline
- [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) — D-04 decline-to-find, artifact envelope
- [`docs/architecture/auth-AI-azure-resources.md`](../../docs/architecture/auth-AI-azure-resources.md) — Azure resources, AI Search indexes, Content Safety

### ADRs (binding)
- ADR-001, ADR-006, ADR-007, ADR-008, ADR-009, **ADR-010**, ADR-012, **ADR-013**, ADR-014, ADR-016, **ADR-018** (Flag Scope Discipline added 2026-06-03 from this design review), ADR-021, ADR-022, ADR-026, **ADR-028**, **ADR-030**, ADR-031, **ADR-032** (bold = highest-leverage for R5)

### Constraints
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — binding BFF additions governance (CLAUDE.md §10)
- [`docs/standards/CHAT-ATTACHMENT-POLICY.md`](../../docs/standards/CHAT-ATTACHMENT-POLICY.md) — 25 MB binary cap, MIME allow-list, total-text caps

### Predecessor projects
- [`projects/spaarke-ai-platform-unification-r4/`](../spaarke-ai-platform-unification-r4/) — R4 (shipped 2026-06-03; PR #331)
- [`projects/spaarke-ai-platform-unification-r3/`](../spaarke-ai-platform-unification-r3/) — R3 (predecessor FRs still in force)

---

*This design.md is the source of truth for R5 scope. The next step is `/design-to-spec` to produce `spec.md` with formal FR/NFR/DR/PR enumeration, followed by `/project-pipeline` to generate task POMLs.*
