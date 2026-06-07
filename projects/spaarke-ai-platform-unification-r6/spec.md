# Spaarke AI Platform Unification R6 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-07
> **Source**: `design.md` (refined 2026-06-07 against verified code state + authoritative architecture docs)
> **Predecessor**: R5 closed 2026-06-06 with known limitations after SC-18 surfaced systemic architecture gaps

---

## Executive Summary

R6 is the **convergence phase** that aligns the conversational chat-agent with the production-mature playbook side of the Spaarke AI platform. The Scope library (Actions, Skills, Knowledge, Tools), JPS pipeline, `PlaybookExecutionEngine`, `IAnalysisToolHandler` registry, 11 node executors, safety pipeline, AI Public Contracts facades, Cosmos persistence, `CapabilityRouter`, 4-stage shell lifecycle, and pre-fill flow are already production. R6 converges the chat-agent side onto these patterns — persona becomes a Dataverse-driven scope, the 12 hardcoded chat-tool classes become data-backed handlers, one generic `invoke_playbook` chat tool replaces specialized bridges, the chat `/summarize` FK bypass is removed, the renderer becomes schema-aware, and the Assistant ↔ Context ↔ Workspace surface becomes tri-directional with a Claude-Code-like execution-trace widget. Plus R6 builds the 8 unimplemented typed tool handlers (`EntityExtractorHandler`, `ClauseAnalyzerHandler`, etc.) that the platform's data model anticipates but only stubs in code today. After R6, R7+ feature work becomes "design a playbook in data + declare its output schema + reference scopes" while the conversational user experience remains the primary surface.

**Total scope**: ~60–90 engineer-days across 4 phases + 1 parallel handler workstream; ~5–7 calendar weeks with appropriate parallelism. The architectural critical path alone is ~45–65 days (~4–5 weeks).

---

## Scope

### In Scope (9 Refined Pillars)

1. **Persona as 5th Scope Entity** — `sprk_aipersona` Dataverse entity following existing SYS-/CUST- / inheritance / SaveAs scope patterns; resolved via `IScopeResolverService`; default global SYS- row seeded with current hardcoded text
2. **Tool Registry Convergence + 8 Typed Handlers** — generalize `IAnalysisToolHandler` to `IToolHandler` for cross-context use; add `AvailableInContexts` discriminator + `JsonSchema` to `AnalysisTool` DTO; build `ToolHandlerToAIFunctionAdapter`; build the 8 missing typed handlers (EntityExtractor, ClauseAnalyzer, RiskDetector, ClauseComparison, DateExtractor, FinancialCalculator, InvoiceExtraction, FinancialCalculation); migrate 10 pre-R5 chat tool classes to data-driven registration
3. **Generic `invoke_playbook` Chat Tool** — new `IInvokePlaybookAi` facade (ADR-013); generic `invoke_playbook(playbookId, params)` chat tool with dynamic description; specialized `InvokeSummarizePlaybookTool` + `InvokeInsightsQueryTool` bridges removed
4. **Route Chat `/summarize` Through `PlaybookExecutionEngine`** — wire `summarize-document-for-chat@v1` playbook FK; refactor `SessionSummarizeOrchestrator` to invoke engine instead of alternate-key lookup
5. **Output Schema as 6th Scope + Schema-Aware Renderers** — extend `sprk_analysisaction` with `renderingDestination` / `widgetType` / `widgetSchema`; make `StructuredOutputStreamWidget` schema-aware for array/object fields (fixes R5 TL;DR + Entities rendering bugs)
6. **Tri-directional Assistant ↔ Context ↔ Workspace State Model + Execution-Trace Widget** — canonical `WorkspaceTab` typed schema; Redis hot + Cosmos durable persistence; per-turn workspace snapshot in agent prompt; new chat tools for workspace mutation; Claude-Code-like Context-pane execution-trace widget; additive PaneEventBus event types; user affordances (Send to Workspace, Add to Assistant, Pin to Matter)
7. **Cross-Conversation Memory + Smart Recall** — utilize existing Cosmos `memory` container; summarization compression for old turns; pinned-context entity (user preferences, system rules, matter facts); selective recall via embedding similarity; hierarchical memory composition; user-facing primitives ("remember X" / "forget X" / "always X")
8. **Command Router** — formal slash/hash/at vocabulary; `CommandRouter` parser; hard slashes (deterministic, bypass LLM); soft slashes (intent shortcuts routed via agent); references (entity/scope/file)
9. **Workspace Widget Visibility Contract** — per-widget `getAgentVisibleState()` returning compact schema-typed state; extends `WorkspaceWidgetRegistry`; agent prompt builder gathers visible state from Assistant-visible tabs

### Out of Scope (Explicit Exclusions)

- **Microsoft Agent Framework adoption** — Spaarke functional/requirements review concluded Agent Framework is not appropriate for R6 (or near-term). R6 treats in-process `PlaybookExecutionEngine` as THE engine; no "future replacement" abstractions, no "backend flexibility" seams, no Agent Framework references in design or code. Future evaluation is a separate project's call.
- **Replacement of the 11 production node executors** — AiAnalysis, AiCompletion, Condition, DeliverOutput, DeliverToIndex, UpdateRecord, CreateTask, SendEmail, CreateNotification, QueryDataverse, Start — all preserved as-is
- **Replacement of visual playbook canvas** — `PlaybookBuilder` Code Page stays
- **Replacement of JPS authoring** — 6-layer rendering pipeline, JPS schema, `$ref` / `$choices` resolution, override merge — all preserved
- **Replacement of M365 Copilot integration** — Agent Gateway endpoints (`/api/agent/*`) remain thin adapters delegating to BFF services
- **Modification of the pre-fill flow** — `MatterPreFillService`, `ProjectPreFillService`, `useAiPrefill` hook, 45s timeout, `$choices`-constrained output, `Workspace:PreFillPlaybookId` config — all UNCHANGED (binding)
- **New ADRs** — R6 operates within existing constraints (ADR-013/030/031/014/016/028/010/008/015)
- **New top-level DI registrations** — all R6 services register inside existing module pattern (per ADR-010)
- **Frontend redesign** — three-pane shell, SprkChat component, workspace tab strip all stay; tri-directional shell adds events + widgets but visual model is unchanged
- **R7+ polish** — full eval harness with metrics + CI integration, scope admin UI panel, "Pinned Memory" management view, additional ActionType node executors (RuleEngine, Calculation, DataTransform, CallWebhook, SendTeamsMessage, Parallel, Wait)

### Affected Areas (file paths)

**BFF additions / refactors**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/` — `IToolHandler` (renamed from `IAnalysisToolHandler`), `IInvokePlaybookAi` facade, scope-aware execution context
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/` — 8 new typed handler implementations
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs` — refactor to invoke `PlaybookExecutionEngine`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` — refactor `ResolveTools()` to read from `sprk_analysistool`; resolve persona from `sprk_aipersona`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs` — read persona from new scope entity
- `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs` — add `AvailableInContexts`, `JsonSchema` to `AnalysisTool` DTO; add persona resolution methods
- `src/server/api/Sprk.Bff.Api/Api/Ai/ScopeEndpoints.cs` — add `/api/ai/scopes/personas` endpoint
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IInvokePlaybookAi.cs` — new facade per ADR-013
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/` — summarization compression service, pinned-context entity, selective recall, token budget tracker
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` — register new services
- `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceStateEndpoints.cs` (new) — `GET /api/workspace/state`
- `src/server/api/Sprk.Bff.Api/Services/Workspace/WorkspaceStateService.cs` (new) — Redis hot + Cosmos durable persistence

**Frontend additions / refactors**:
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/StructuredOutputStreamWidget.tsx` — schema-aware rendering for array/object fields
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/ExecutionTraceWidget.tsx` (new) — Claude-Code-like process stream
- `src/client/shared/Spaarke.AI.Widgets/src/registry/WorkspaceWidgetRegistry.ts` — extend with optional `getVisibleState`
- `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` — additive event types on `context` + `workspace` channels (no 5th channel per ADR-030)
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManager.ts` — extend with `visibleToAssistant` + provenance
- `src/solutions/SpaarkeAi/src/components/conversation/CommandRouter.ts` (new) — slash/hash/at parser
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` — wire CommandRouter
- `src/solutions/SpaarkeAi/src/components/workspace/SendToWorkspaceButton.tsx` (new) — chat affordance
- `src/solutions/SpaarkeAi/src/components/workspace/AddToAssistantToggle.tsx` (new) — workspace affordance

**Dataverse**:
- `sprk_aipersona` entity (new) — 5th scope; SYS-/CUST- with inheritance
- `sprk_analysisaction` extension — `renderingDestination`, `widgetType`, `widgetSchema` fields (or new sub-entity)
- `sprk_analysistool` data — populate `AvailableInContexts` for chat-availability; seed handler-class rows for 10 migrated tools + 8 new typed handlers
- Playbook FK fix: wire `summarize-document-for-chat@v1` node to `SUM-CHAT@v1` action

**Tests**:
- `tests/unit/Sprk.Bff.Api.Tests/` — handler unit tests (8 typed handlers + persona resolution + workspace state)
- `tests/integration/` — full vertical-slice integration test (per §6 Vertical-Slice Validation Target)

---

## Requirements

### Functional Requirements

#### Pillar 1 — Persona as 5th Scope Entity

- **FR-01**: Add `sprk_aipersona` Dataverse entity following existing scope patterns. Acceptance: SYS-/CUST- prefix enforcement, `parentScopeId` for inheritance, `description`, `systemPrompt` field, `scopeType` (global/tenant/playbook-attached) — all mirror existing 4 scope entities.
- **FR-02**: Add `GET /api/ai/scopes/personas` endpoint mirroring existing scope endpoints in `ScopeEndpoints.cs`. Acceptance: returns `ScopeListResult<AnalysisPersona>` with same pagination/filtering/sorting as other scope endpoints.
- **FR-03**: Update `SprkChatAgentFactory.CreateAgentAsync` to resolve persona via existing `IScopeResolverService`. Acceptance: resolution order is global SYS- → tenant CUST- → playbook-bound (most specific wins); replaces hardcoded `BuildDefaultSystemPrompt()` call.
- **FR-04**: Seed default global SYS- persona row containing current hardcoded `BuildDefaultSystemPrompt()` text verbatim. Acceptance: behavior with no tenant/playbook persona override is identical to today.
- **FR-05**: Power Apps Dataverse form for CUST- persona authoring (no custom UI). Acceptance: tenant admin can create/edit/SaveAs/Extend persona rows via Power Apps; admin UI deferred to R7 per Q3.

#### Pillar 2 — Tool Registry Convergence + 8 Typed Handlers

- **FR-06**: Rename `IAnalysisToolHandler` → `IToolHandler`. Acceptance: type alias preserved for source compatibility; existing handlers (`GenericAnalysisHandler`, `DocumentClassifierHandler`, `SummaryHandler`, `SemanticSearchToolHandler`) continue to work unchanged.
- **FR-07**: Add `AvailableInContexts` enum field (`Playbook` / `Chat` / `Both`) to `AnalysisTool` DTO + corresponding Dataverse column. Acceptance: existing playbook tool rows default to `Playbook`; new chat tools declare `Chat` or `Both`.
- **FR-08**: Add `JsonSchema` field to `AnalysisTool` for chat function-calling parameter declaration. Acceptance: nullable for playbook-only tools; required for chat-available tools.
- **FR-09**: Split execution context: existing `ToolExecutionContext` keeps playbook-node semantics; new `ChatInvocationContext` adds chat-session + LLM parameter passing. Acceptance: both contexts inherit a shared base; handlers receive the appropriate context type.
- **FR-10**: Build `ToolHandlerToAIFunctionAdapter` that wraps an `IToolHandler` as `Microsoft.Extensions.AI.AIFunction`. Acceptance: adapter reads `JsonSchema` from `AnalysisTool` for parameter declaration; LLM sees standard function-calling tool.
- **FR-11**: Build chat-side discovery: `SprkChatAgentFactory.ResolveTools()` reads `sprk_analysistool` rows with `AvailableInContexts ∋ "Chat"` and wraps via adapter. Acceptance: tool list is data-driven; adding a Dataverse row exposes the tool to the LLM next request.
- **FR-12**: Migrate 10 pre-R5 chat tool C# classes to `IToolHandler` implementations + corresponding `sprk_analysistool` rows (DocumentSearch, AnalysisQuery, KnowledgeRetrieval, TextRefinement, WorkingDocument, AnalysisExecution, WebSearch, CodeInterpreter, LegalResearch, VerifyCitations). Acceptance: incremental per Q9 — start with `invoke_playbook`, validate, then migrate remaining 10; existing tools continue to work during migration.
- **FR-13**: Build `EntityExtractorHandler`. Acceptance: LLM-assisted Named Entity Recognition with code-based validation/normalization; configurable via `sprk_analysistool.sprk_configuration` (entityTypes, confidenceThreshold).
- **FR-14**: Build `ClauseAnalyzerHandler`. Acceptance: LLM-assisted contract clause structuring with structural diff; clauseTypes configurable.
- **FR-15**: Build `RiskDetectorHandler`. Acceptance: LLM-assisted risk identification with code-based severity scoring; severityLevels configurable.
- **FR-16**: Build `ClauseComparisonHandler`. Acceptance: pure deterministic text-diff with structural awareness; no LLM call required.
- **FR-17**: Build `DateExtractorHandler`. Acceptance: pure deterministic date normalization + relative-date resolution (e.g., "next quarter").
- **FR-18**: Build `FinancialCalculatorHandler`. Acceptance: pure deterministic math on extracted figures; currencies configurable.
- **FR-19**: Build `InvoiceExtractionToolHandler`. Acceptance: LLM extraction + line-item arithmetic; tax/discount handling.
- **FR-20**: Build `FinancialCalculationToolHandler`. Acceptance: pure deterministic formulas (rate × principal × time, currency-aware).

#### Pillar 3 — Generic `invoke_playbook` Chat Tool

- **FR-21**: Add new `IInvokePlaybookAi` facade in `Services/Ai/PublicContracts/` per ADR-013 (per Q11 recommendation — cleaner than extending `IWorkspacePrefillAi`). Acceptance: facade wraps `IPlaybookOrchestrationService`; CRUD-side callers never inject `IPlaybookOrchestrationService` directly.
- **FR-22**: Build generic `invoke_playbook(playbookId: string, parameters: object)` chat tool. Acceptance: validates `playbookId` against `sprk_analysisplaybook` rows accessible to tenant; validates parameters against playbook's parameter schema.
- **FR-23**: Dynamically populate `invoke_playbook` tool description with active playbook list at chat-agent build time. Acceptance: description includes name + description of each accessible playbook; filtered by tenant + capability gates.
- **FR-24**: Remove specialized `InvokeSummarizePlaybookTool` and `InvokeInsightsQueryTool` bridges. Acceptance: their callers route through generic `invoke_playbook` tool; insights playbook-vs-RAG routing handled internally via `InsightsIntentClassifier`.

#### Pillar 4 — Route Chat `/summarize` Through `PlaybookExecutionEngine`

- **FR-25**: Wire `summarize-document-for-chat@v1` playbook node FK to `SUM-CHAT@v1` action (Dataverse data fix). Acceptance: playbook → node → action chain is fully populated; FK validates at startup.
- **FR-26**: Refactor `SessionSummarizeOrchestrator` to invoke `PlaybookExecutionEngine.ExecuteAsync(playbookId: "summarize-document-for-chat@v1", ...)` instead of loading action by alternate key. Acceptance: existing functionality preserved (session-files Azure Search filter, Structured Outputs mode, streaming JSON delta); FK chain traversed; no `sprk_actioncode` alternate-key lookup remains in chat path.

#### Pillar 5 — Output Schema as 6th Scope + Schema-Aware Renderers

- **FR-27**: Extend `sprk_analysisaction` with output-schema fields per Q5: `renderingDestination` (chat / workspace / both / side-effect), `widgetType` (for workspace destinations), `widgetSchema` (per-field type declaration). Acceptance: existing actions default to `chat` rendering destination; new actions can declare workspace artifact + schema.
- **FR-28**: Update `StructuredOutputStreamWidget` renderer for array-typed fields. Acceptance: accumulate token deltas until `streaming_complete`, JSON.parse, render as bulleted list; fixes R5 TL;DR rendering bug (`tldr: string[]` no longer shows raw JSON fragments).
- **FR-29**: Update renderer for object-typed fields. Acceptance: accumulate, parse, render as labeled key-value blocks; fixes R5 Entities rendering bug (`entities: {organizations: string[], persons: string[]}` no longer shows raw JSON literal).
- **FR-30**: Orchestrator routes output per output-schema scope. Acceptance: `chat` returns text/markdown inline; `workspace` publishes PaneEventBus events with configured widget type; `both` emits chat ack + workspace artifact via ONE LLM call (eliminates duplicate-fire structurally).

#### Pillar 6 — Tri-directional Assistant ↔ Context ↔ Workspace State Model

- **FR-31**: Define canonical `WorkspaceTab` TypeScript interface with typed `widgetType`, `widgetData`, `sessionId`, `visibleToAssistant: boolean`, `sourceProvenance`, `matterContext`, `isPinned`, `canEdit`, audit fields. Acceptance: existing workspace tabs migrate transparently; new tabs created via state model.
- **FR-32**: Workspace state persistence. Acceptance: Redis hot tier (24h TTL) for active session — reuse existing Redis infrastructure; Cosmos durable tier for pinned/matter-attached tabs (extend `memory` container or add `workspace_tabs` container per established pattern).
- **FR-33**: `GET /api/workspace/state` BFF endpoint (tenant-scoped, session-scoped). Acceptance: returns current open tabs + active tab + user selection state; honors per-widget `getAgentVisibleState()` (Pillar 9) for token-efficient response.
- **FR-34**: Per-turn workspace snapshot included in agent system prompt. Acceptance: factory queries workspace state at `CreateAgentAsync` time + includes "Workspace State" block ("Tab 1 (active): Summary of ..., user selection: ...").
- **FR-35**: New chat tools for workspace mutation. Acceptance: `send_workspace_artifact(content, widgetType)` creates new tab; `update_workspace_tab(tabId, changes)` modifies existing; `close_workspace_tab(tabId)` closes. All registered as `sprk_analysistool` rows with `AvailableInContexts ∋ "Chat"`.
- **FR-36**: Build Context-pane execution-trace widget. Acceptance: registered with `ContextWidgetRegistry`; subscribes to `context.*` execution events; renders ordered timeline of tool invocations, knowledge sources consulted, decisions made — matches Claude Code transparency surface.
- **FR-37**: Additive PaneEventBus event types on `context` channel (no 5th channel per ADR-030): `context.tool_call_started`, `context.tool_call_completed`, `context.knowledge_retrieved`, `context.playbook_node_executing`, `context.playbook_node_completed`, `context.decision_made`. Acceptance: telemetry from chat agent + playbook execution emits these events; trace widget renders them in order.
- **FR-38**: Additive PaneEventBus event types on `workspace` channel (reverse-flow): `workspace.user_selection`, `workspace.tab_edited`, `workspace.tab_focused`, `workspace.tab_provenance_clicked`. Acceptance: workspace surface dispatches to assistant + context; agent reads via `getWorkspaceState()` (FR-33).
- **FR-39**: User affordances. Acceptance: "Send to Workspace" button on chat assistant messages — promotes chat content to workspace tab; "Add to Assistant" toggle on user-created tabs — flips `visibleToAssistant: true`; "Pin to Matter" persists tab attached to matter record.
- **FR-40**: Conflict resolution per Q8. Acceptance: user-edit timestamp tracked per tab; agent updates check timestamp; if user edit is newer than agent's read, agent refuses with polite re-read prompt ("tab was edited by user; please re-read"). No OT/CRDT in R6.

#### Pillar 7 — Cross-Conversation Memory + Smart Recall

- **FR-41**: Build summarization compression service. Acceptance: when sliding window exceeds budget, replace oldest M turns with LLM-generated summary; append `[Conversation summary: ...]` at history top; reuses existing Cosmos `sessions` container.
- **FR-42**: Build pinned-context entity in Cosmos `memory` container. Acceptance: `pinType: 'user-preference' | 'system-rule' | 'matter-fact'`; pinned items never drop from prompt; resolved at chat-agent build time.
- **FR-43**: Build selective recall via embedding similarity. Acceptance: vectorize each turn at write time using existing `IEmbeddingCache`; retrieve old turns by similarity when current question relates; respects token budget.
- **FR-44**: Build hierarchical memory composition. Acceptance: recent verbatim (last 10 turns) + compressed mid-distance (turns 10-50 as summary blocks) + retrieved old (turns 50+ via similarity).
- **FR-45**: Activate per-matter memory snapshot utilization. Acceptance: `MatterMemoryService` already exists in production; R6 wires it into chat-agent system prompt assembly so cross-session same-matter conversations are coherent.
- **FR-46**: Build shared token budget tracker. Acceptance: factory + document context + knowledge retrieval + memory all consume a shared budget; respects existing 8K system prompt budget; doesn't change ceiling.
- **FR-47**: User-facing memory affordances per Q7 (primitives in R6; UI panel deferred to R7). Acceptance: agent recognizes "remember X" / "forget X" / "always X" via existing CapabilityRouter; dispatches to pinned-context tool.

#### Pillar 8 — Command Router

- **FR-48**: Build `CommandRouter` parser. Acceptance: parses user input into structured `Intent { command, references[], rawText }`; runs before agent invocation.
- **FR-49**: Hard slashes (deterministic, bypass LLM) per Q6 (~5): `/clear`, `/new-session`, `/help`, `/export`, `/save-to-matter [matterId]`, `/pin`. Acceptance: execute directly; no LLM call; instant response.
- **FR-50**: Soft slashes (intent shortcuts, flow through agent) per Q6 (~5): `/summarize`, `/draft`, `/extract-entities`, `/analyze`. Acceptance: set strong intent signal; route to agent; agent may ask clarifying questions if intent isn't actionable.
- **FR-51**: References (data identifiers, agent uses as context). Acceptance: `#scope` resolves to scope reference; `@<entity>` resolves to matter/person/organization; `#<filename>` resolves to session file or workspace tab; resolved at parse time + included in agent prompt as known entities.
- **FR-52**: Composition. Acceptance: `/summarize #engagement-letter.docx` (command + reference) works; `/draft response to @opposing-counsel about #motion-to-dismiss` (command + multiple references) works.
- **FR-53**: `/help` UI affordance. Acceptance: shows command reference; discoverable from chat input bar.
- **FR-54**: Natural language requests still work alongside slashes. Acceptance: saying "summarize this" in natural language continues to work via existing `CapabilityRouter`; `/summarize` is the explicit shortcut for power users.

#### Pillar 9 — Workspace Widget Visibility Contract

- **FR-55**: Define `getAgentVisibleState(): SerializedWidgetState` TypeScript interface. Acceptance: returns compact, schema-typed representation of widget state; nullable (widget can opt out).
- **FR-56**: Extend `WorkspaceWidgetRegistry` registration metadata with optional `getVisibleState?: () => unknown` field. Acceptance: existing widget registrations continue to work (visibility opt-in, not retrofitted automatically).
- **FR-57**: Implement `getAgentVisibleState()` per widget type. Acceptance: Summary tab returns `{ widgetType, summary, tldr, hasUserEdits }`; DocumentViewer returns `{ widgetType, filename, mimeType, sizeBytes, hasSelection, selectionText? }`; Dashboard returns `{ widgetType, dashboardName, lastViewedSection }` (NOT full chart data); Table returns `{ widgetType, rowCount, sortColumn, filteredColumns, selectedRows[] }`.
- **FR-58**: Per-turn agent prompt builder gathers visible state from each Assistant-visible tab. Acceptance: included in system prompt "Workspace State" block; respects `visibleToAssistant: true` filter; per ADR-015 data governance — widgets choose what to expose.
- **FR-59**: Privacy default. Acceptance: widgets that should NOT expose to LLM (e.g., user's private dashboard) don't appear in agent prompts; user can override via "Add to Assistant" toggle (FR-39).

### Non-Functional Requirements

- **NFR-01**: **Conversational primacy preserved** (binding, per design.md §1.6). LLM responds conversationally for most turns; CapabilityRouter restricts tool list but never prevents conversational ability; users can pivot mid-conversation; refinement / follow-up / comparison / context injection all work via full conversation history + cross-conversation memory.
- **NFR-02**: BFF publish size ≤60 MB compressed per ADR-029. R6 budget ≤+5 MB total across all pillars. Per-task verification required (per CLAUDE.md §10).
- **NFR-03**: ADR compliance — R6 honors without modification: ADR-013 (AI as BFF extension + facade boundary), ADR-030 (PaneEventBus closed at 4 channels; additive event types only), ADR-031 (4-stage lifecycle deterministic), ADR-014 (AI caching), ADR-016 (rate limiting), ADR-028 (Spaarke Auth v2 — no token snapshots), ADR-010 (DI minimalism via module pattern), ADR-008 (endpoint filters for auth), ADR-015 (AI data governance — no logging of user message content).
- **NFR-04**: **ZERO Microsoft Agent Framework references** in R6 code or design (binding). The only acceptable mentions are the explicit "NO Agent Framework" statements in design.md §1.2 and §4.0.
- **NFR-05**: 4-channel PaneEventBus preserved (workspace / context / conversation / safety per ADR-030). R6 adds additive event types only; no 5th channel.
- **NFR-06**: 4-stage shell lifecycle preserved (welcome / loading / active-chat / review per ADR-031). No new stages.
- **NFR-07**: **Pre-fill flow preserved entirely** (binding). `MatterPreFillService`, `ProjectPreFillService`, `useAiPrefill` hook, `findBestLookupMatch`, `AiFieldTag`, 45s timeout, `$choices`-constrained output contract, `Workspace:PreFillPlaybookId` / `Workspace:ProjectPreFillPlaybookId` config — all UNCHANGED. R6 may extend `IWorkspacePrefillAi` with new methods but existing signatures stay.
- **NFR-08**: 11 production node executors preserved (AiAnalysis, AiCompletion, Condition, DeliverOutput, DeliverToIndex, UpdateRecord, CreateTask, SendEmail, CreateNotification, QueryDataverse, Start). R6 does NOT modify, deprecate, or extend any of them.
- **NFR-09**: M365 Copilot integration thinness preserved. Agent Gateway endpoints (`/api/agent/*`) remain thin adapters delegating to BFF services per `M365-COPILOT-INTEGRATION-ARCHITECTURE.md`.
- **NFR-10**: Token budget management. 8K system prompt budget per `PlaybookChatContextProvider` preserved. R6 manages within it more efficiently (Pillar 7 utilization).
- **NFR-11**: Backward compatibility during migration. Existing 12 hardcoded chat tools continue to work while migrating to data-driven registration (Q9 incremental approach). No regression on R5 features.
- **NFR-12**: Telemetry preserved + new events emitted. R5 task 008 telemetry infrastructure reused; R6 adds new `context.*` events for execution-trace widget; new tool invocations emit telemetry per existing pattern.
- **NFR-13**: Safety pipeline preserved (PromptShield + Groundedness + Citations + Privilege + Cross-matter via `SafetyPipelineMiddleware`). All R6 chat-agent changes flow through the existing middleware chain unchanged.
- **NFR-14**: All scope CRUD operations honor SYS-/CUST- ownership boundary per `scope-architecture.md`. Persona (Pillar 1) + Output (Pillar 5) entities follow the same enforcement model.
- **NFR-15**: Conversation history persistence preserved. Cosmos `sessions` container (90d TTL) write-through pattern unchanged; R6 builds memory utilization layer on top.
- **NFR-16**: Per-tenant cache key isolation per ADR-014. All R6 Redis cache keys include `tenantId` (`chat:session:{tenantId}:{sessionId}`).

---

## Technical Constraints

### Applicable ADRs

- **ADR-013** — AI Architecture: AI as BFF extension; PublicContracts facade boundary; no separate AI microservice; CRUD-side consumers MUST route through `Services/Ai/PublicContracts/`. R6's new `IInvokePlaybookAi` facade follows this pattern.
- **ADR-030** — Pane Event Bus: closed at 4 channels; additive event types only. R6 adds new event types within existing channels; no 5th channel.
- **ADR-031** — Stage Lifecycle: 4-stage deterministic from `SessionState`. R6 does not add stages or modify transitions.
- **ADR-014** — AI Caching: Redis hot tier + Cosmos warm tier with write-through. R6 reuses existing pattern for workspace state.
- **ADR-016** — Rate Limiting: existing `ai-context` policy inherited by all R6 endpoints.
- **ADR-028** — Spaarke Auth v2: no token snapshots; OBO + MI patterns. R6 chat-tool migrations preserve fresh-token-per-request semantics.
- **ADR-010** — DI Minimalism: module pattern for new registrations. R6 registers all new services inside existing modules (`AnalysisServicesModule`, `AiCapabilitiesModule`, etc.).
- **ADR-008** — Endpoint Filters: authorization filters per endpoint, not global middleware. New R6 endpoints (`/api/ai/scopes/personas`, `/api/workspace/state`) inherit existing filter pattern.
- **ADR-015** — AI Data Governance: no logging of user message content. R6 execution-trace events emit tool names + decisions + timestamps; never user message text.
- **ADR-018** — Feature Flag Discipline: no new feature flags introduced. R6 services unconditionally registered or gated by existing capability flags.
- **ADR-029** — BFF Publish Hygiene: ≤60 MB compressed ceiling; per-task verification required.

### MUST Rules

- ✅ **MUST** NOT introduce Microsoft Agent Framework dependencies in code or design
- ✅ **MUST** preserve `MatterPreFillService` / `ProjectPreFillService` / `useAiPrefill` / 45s timeout / `$choices`-constrained output unchanged
- ✅ **MUST** preserve all 11 node executors as-is
- ✅ **MUST** keep PaneEventBus at exactly 4 channels (workspace / context / conversation / safety)
- ✅ **MUST** keep shell stages at exactly 4 (welcome / loading / active-chat / review)
- ✅ **MUST** route CRUD-side AI consumers through `Services/Ai/PublicContracts/` facades (ADR-013)
- ✅ **MUST** include `tenantId` in all Redis cache keys (ADR-014)
- ✅ **MUST** verify BFF publish size per task; report delta vs prior baseline; ≤60 MB ceiling (ADR-029)
- ✅ **MUST** register new R6 services within existing DI modules (ADR-010)
- ✅ **MUST** apply endpoint filters for authorization on new endpoints (ADR-008)
- ✅ **MUST** preserve LLM conversational primacy — scopes augment, never replace (NFR-01)
- ✅ **MUST** route chat `/summarize` through `PlaybookExecutionEngine` (no alternate-key bypass after Pillar 4 lands)
- ✅ **MUST** preserve existing 12 chat tools' functionality during incremental migration (Q9)
- ✅ **MUST** build all 8 typed handlers (per owner clarification — Generic was stopgap, specialized handlers were intent)

### MUST NOT Rules

- ❌ **MUST NOT** add 5th PaneEventBus channel
- ❌ **MUST NOT** add a new shell lifecycle stage
- ❌ **MUST NOT** inject `IOpenAiClient`, `IPlaybookService`, `IPlaybookOrchestrationService`, or other AI-internal types directly into CRUD-side code (ADR-013 facade boundary)
- ❌ **MUST NOT** introduce new ADRs (R6 operates within existing constraints)
- ❌ **MUST NOT** add new feature flags (ADR-018)
- ❌ **MUST NOT** modify the wizard pre-fill flow's existing signatures or contracts
- ❌ **MUST NOT** replace `PlaybookExecutionEngine` or any of the 11 node executors
- ❌ **MUST NOT** add Microsoft Agent Framework references anywhere
- ❌ **MUST NOT** log user message content in execution-trace events (ADR-015)
- ❌ **MUST NOT** introduce backwards-incompatible changes to existing 12 chat tools before migration is complete
- ❌ **MUST NOT** hardcode persona text in C# after Pillar 1 lands (data-driven only)
- ❌ **MUST NOT** load action by alternate key in chat `/summarize` path after Pillar 4 lands

### Existing Patterns to Follow

- **`docs/architecture/playbook-architecture.md`** — node type system, execution engine, canvas data model
- **`docs/architecture/scope-architecture.md`** — scope CRUD, SYS-/CUST- ownership, inheritance, SaveAs (R6's persona + output entities follow these patterns)
- **`docs/architecture/chat-architecture.md`** — chat agent factory, middleware pipeline, capability routing
- **`docs/architecture/AI-ARCHITECTURE.md`** — Tier 4 architecture, scope library + composition patterns + execution runtime
- **`docs/guides/JPS-AUTHORING-GUIDE.md`** — 6-layer JPS pipeline, `$choices` resolution, override merge, structured output
- **`docs/guides/SCOPE-CONFIGURATION-GUIDE.md`** — scope authoring, playbook composition, builder UI
- **`docs/guides/WORKSPACE-AI-PREFILL-GUIDE.md`** — canonical "playbook from anywhere" pattern; R6 Pillar 3 reuses this shape
- **`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`** — three-pane shell, PaneEventBus integration
- **`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`** — `@spaarke/*` library inventory, PaneEventBus 4-channel contract
- **`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`** — widget registry, two-wrapper architecture (required reading for Pillar 6 + 9)
- **`docs/guides/INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md`** — chat-agent routes to BOTH playbook AND RAG paths; `forceMode` override (preserved in R6)

---

## Success Criteria

### Phase A Exit Criteria (Pillars 1, 2-infra, 3, 4 — Week 1-2)

1. [ ] Chat-agent tool list driven by `sprk_analysistool` rows with `AvailableInContexts ∋ "Chat"` — Verify by: query `/api/ai/scopes/tools?available=Chat` returns expected tools; chat-agent's runtime tool list matches
2. [ ] Persona is a Dataverse-driven scope (`sprk_aipersona`) — Verify by: tenant override changes chat agent voice without code deploy; SYS- persona inherited as fallback; `GET /api/ai/scopes/personas` lists rows
3. [ ] One generic `invoke_playbook` chat tool exists; specialized bridges removed — Verify by: `InvokeSummarizePlaybookTool` and `InvokeInsightsQueryTool` C# files removed; LLM sees single `invoke_playbook` tool with dynamic playbook list in description
4. [ ] `SessionSummarizeOrchestrator` routes through `PlaybookExecutionEngine` — Verify by: code review confirms no `GetByCodeAsync("SUM-CHAT@v1")` alternate-key lookup; FK chain (playbook → node → action) traversed; existing `/summarize` functionality preserved

### Phase B Exit Criteria (Pillar 5 — Week 2-3)

1. [ ] Workspace renderer handles array-typed (`tldr`) fields correctly — Verify by: SC-18-style walkthrough; TL;DR renders as bulleted list (not raw JSON tokens)
2. [ ] Workspace renderer handles object-typed (`entities`) fields correctly — Verify by: Entities renders as orgs/persons labeled groups (not raw JSON object literal)
3. [ ] Output destination declarable as data in `sprk_analysisaction.renderingDestination` — Verify by: playbook author can switch a playbook from `chat` to `workspace` to `both` without code deploy
4. [ ] Duplicate-fire problem eliminated structurally — Verify by: typing `/summarize` produces ONE summary in workspace (no parallel chat-agent duplicate); one orchestrator call, one source of truth

### Phase C Exit Criteria (Pillars 6, 7, 9 — Week 3-5)

1. [ ] Agent has accurate workspace awareness in every prompt — Verify by: open multiple workspace tabs, ask "what's on the workspace?", agent responds correctly
2. [ ] "Update the summary in Tab 1" works — Verify by: chat agent dispatches `update_workspace_tab` chat tool; targeted tab updates; user edit timestamp respected
3. [ ] User can "Send to Workspace" + "Add to Assistant" + "Pin to Matter" — Verify by: UI affordances visible; persistence behaviors confirmed via reload + matter navigation
4. [ ] Context pane shows live execution trace — Verify by: invoke `/summarize`; Context pane shows ordered timeline of tool calls + knowledge retrieved + decisions; trace updates in real time
5. [ ] Cross-conversation memory recalls prior-matter context — Verify by: yesterday's matter analysis is retrievable via "the contract we reviewed yesterday"; per-matter memory snapshots utilized
6. [ ] Pinned facts persist as user preferences — Verify by: user says "always use formal tone for opposing counsel"; subsequent sessions reflect preference; `MatterMemoryService` shows pinned entry

### Phase D Exit Criteria (Pillar 8 + integration — Week 5-6)

1. [ ] `/help` works and is discoverable — Verify by: typing `/help` shows command reference; UI affordance shows available commands
2. [ ] Hard slashes bypass LLM — Verify by: `/clear` resets session without LLM call; latency <100ms; instrumentation confirms no Azure OpenAI request
3. [ ] Soft slashes route via agent with prioritized intent — Verify by: `/summarize` triggers Summarize playbook intent via CapabilityRouter; agent confirms before action
4. [ ] References resolve at parse time — Verify by: `@matter` resolves to matter record; `#contract.docx` resolves to session file; resolved entities appear in agent prompt
5. [ ] All R6 changes have integration test coverage — Verify by: `tests/integration/` has end-to-end test for Vertical-Slice Validation Target (§6 of design.md)

### 8 Typed Tool Handlers Exit Criteria (parallel workstream)

1. [ ] All 8 handlers implemented as `IToolHandler` implementations — Verify by: `Services/Ai/Handlers/` contains 8 new files; each has unit tests; each registered in `IToolHandlerRegistry` via auto-discovery
2. [ ] Handler dispatch works from playbook context — Verify by: playbook references each handler by `sprk_handlerclass`; execution succeeds with structured output
3. [ ] Handler dispatch works from chat context (where applicable) — Verify by: chat tools using these handlers (if any declared `AvailableInContexts = "Both"`) invoke correctly via `ToolHandlerToAIFunctionAdapter`

### Vertical-Slice Validation Target (per design.md §6)

After all pillars land, executing the Summarize vertical slice exercises every pillar end-to-end. Acceptance: full walkthrough completes with:
- Persona pulled from `sprk_aipersona` (Pillar 1)
- Tools from `sprk_analysistool` rows (Pillar 2)
- Skills + Knowledge inherited from bound playbook
- Workspace state in agent prompt (Pillar 6 + 9)
- Memory: recent verbatim + compressed + pinned + selective recall (Pillar 7)
- `/summarize` triggers `invoke_playbook` (Pillar 3)
- Routes via `PlaybookExecutionEngine` with FK chain (Pillar 4)
- Output renders schema-aware (Pillar 5)
- Context pane shows execution trace (Pillar 6)
- "Send to Workspace" / "Make it shorter" / `/clear` all work (Pillars 6, 8)
- Conversational refinement works after playbook output

---

## Dependencies

### Prerequisites (must exist before R6 implementation)

- ✅ R5 closed with known limitations (2026-06-06)
- ✅ R5 cycle-6-to-9 fixes merged to master (PR #364, PR #365)
- ✅ R6 design.md merged to master (PR #367)
- ✅ Cosmos DB containers production: `sessions`, `prompts`, `audit`, `memory`, `feedback`
- ✅ Azure OpenAI deployments operational (GPT-4o + GPT-4o-mini for CapabilityRouter Layer 2)
- ✅ Azure AI Search indexes: `spaarke-knowledge-index-v2`, `spaarke-rag-references`, `spaarke-session-files`
- ✅ Production-mature playbook side: `PlaybookExecutionEngine`, `IAnalysisToolHandler` registry, 11 node executors, JPS pipeline, scope library (4 entities + management infrastructure)
- ✅ Production-mature chat side: `SprkChatAgent`, `SprkChatAgentFactory`, `CapabilityRouter` (3-tier), safety pipeline, `Microsoft.Extensions.AI` integration
- ✅ Production pre-fill flow: `MatterPreFillService`, `ProjectPreFillService`, `useAiPrefill` hook
- ✅ Production frontend infrastructure: 4-channel PaneEventBus, 4-stage shell lifecycle, three-pane shell, workspace tab manager
- ✅ R6 spec.md (this document)

### External Dependencies

- **Insights Engine R2** (Wave F may impact Pillar 3's `invoke_playbook` for `insights.query` capability) — coordinate via `notes/insights-r2-coordination.md`
- **Power Apps Dataverse forms** for `sprk_aipersona` + extended `sprk_analysisaction` authoring (no custom UI needed in R6 per Q3)
- **Azure infrastructure operational** — no new Azure resources required by R6; reuses existing Cosmos DB, Redis, Azure OpenAI, Azure AI Search

---

## Owner Clarifications

*Decisions captured during R6 design refinement (2026-06-07 conversation):*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Microsoft Agent Framework | Should R6 build to or anticipate Agent Framework? | **NO.** Spaarke functional/requirements review concluded Agent Framework is not appropriate for R6 or near-term. Not even worth mentioning in design. | R6 treats in-process `PlaybookExecutionEngine` as THE engine. No "backend flexibility" abstractions. No Agent Framework references anywhere. |
| Conversational primacy | Should playbook focus subordinate the conversational UX? | **NO.** Conversational UX is the primary user experience. LLM is the primary cognitive agent. Scopes augment, never replace. Playbooks are one capability among many. Most user turns never invoke a playbook. | NFR-01 (binding). Every R6 pillar must articulate "how it serves conversation." §1.6 in design.md codifies 8 commitments. |
| Pre-fill flow | Is the Document Profile / pre-fill flow preserved? | **YES, binding.** `AppOnlyDocumentAnalysisJobHandler`, `MatterPreFillService`, `ProjectPreFillService`, `useAiPrefill`, 45s timeout, `$choices`-constrained output, `Workspace:PreFillPlaybookId` config — all UNCHANGED. This is canonical proven architecture and the template for `invoke_playbook`. | NFR-07 (binding). R6 may extend `IWorkspacePrefillAi` with new methods but existing signatures stay. R6's `invoke_playbook` follows this pattern. |
| 8 typed handlers | Were the 8 declared-but-unbuilt handlers stubs that should stay, or real intent? | **Real intent.** All 12 tool handlers were meant to be built; `GenericAnalysisHandler` was a stopgap. R6 should build the 8 missing handlers as production code. | FR-13 through FR-20. ~15-25 days as parallel workstream. |
| ToolHandlerRegistry abstraction | Can the playbook-side `IAnalysisToolHandler` registry be abstracted for chat-agent use? | **YES.** The auto-discovering registry pattern is the right primitive. Generalize the interface, split execution context, add adapter. | FR-06 through FR-11. ~3-5 days infrastructure work. |
| Node executors | Are the 11 node executors (AiAnalysis, AiCompletion, Condition, DeliverOutput, etc.) in R6 scope? | **NO modifications.** All 11 preserved as-is. They are the platform's processing-step library. R6 does NOT modify any of them. | NFR-08 (binding). Reserved ActionType values (RuleEngine, Calculation, DataTransform, CallWebhook, SendTeamsMessage, Parallel, Wait) remain placeholders without executors — not R6 scope. |
| Existing scope architecture | Are the 4 scope entities (Action/Skill/Knowledge/Tool) + SYS-CUST + inheritance + SaveAs all production-mature? | **YES, production-mature per `scope-architecture.md`.** R6 does NOT rebuild scope CRUD, ownership, inheritance, gap detection, or endpoints. R6 adds 2 more scope entities (Persona, Output) following existing patterns. | FR-01 through FR-05 (persona). FR-27 (output). |
| JPS pipeline | Is the 6-layer JPS pipeline production-mature? | **YES, per `JPS-AUTHORING-GUIDE.md`.** R6 does NOT rebuild JPS rendering, `$ref` resolution, `$choices` resolution, override merge, structured output generation. R6 inherits all of this. | Pillar 4 (FR-25/26) routes chat through playbook engine which uses JPS pipeline unchanged. |

---

## Assumptions

*Working assumptions used in R6 implementation; flagged for owner final confirmation. Each assumption maps to an Open Question (Q1-Q11) in design.md §7. Plan phase will re-confirm before implementation kicks off.*

- **Q1 (persona inheritance)**: Most-specific-wins hierarchy (global SYS- < tenant CUST- < playbook-attached). Matches existing scope inheritance pattern. Affects: FR-03 resolution logic.
- **Q2 (persona entity)**: Standalone `sprk_aipersona` entity (not extension of `sprk_analysisaction`). Persona is functionally distinct from action. Affects: FR-01 entity model.
- **Q3 (scope admin UI)**: Defer custom scope admin UI to R7. Power Apps Dataverse forms suffice for persona + output scope authoring in R6. Affects: FR-05 (no custom UI in R6).
- **Q4 (workspace tab persistence)**: Hybrid model — agent-generated tabs default-ephemeral until pinned; user-pinned + matter-attached survive. Affects: FR-31 / FR-32 storage semantics.
- **Q5 (output schema entity)**: Extend `sprk_analysisaction` with output-schema fields (NOT standalone entity). Output schema is conceptually a property of an action. Affects: FR-27.
- **Q6 (slash vocabulary)**: ~5 hard slashes + ~5 soft slashes. List: `/clear`, `/new-session`, `/help`, `/export`, `/save-to-matter`, `/pin` (hard); `/summarize`, `/draft`, `/extract-entities`, `/analyze` (soft). Final list finalized in plan phase. Affects: FR-49 / FR-50.
- **Q7 (memory UX scope)**: R6 ships memory primitives + basic user-facing recognition ("remember/forget/always"). Full UI panel deferred to R7. Affects: FR-47 scope.
- **Q8 (conflict resolution)**: User wins. Agent reads state, checks user-edit timestamp, refuses if user edit is newer than agent's read. No OT/CRDT in R6. Affects: FR-40 implementation.
- **Q9 (tool registry migration timing)**: Incremental. Start with `invoke_playbook` (Pillar 3), validate, then migrate the remaining 10 chat tools over Phase A. Existing hardcoded tools continue to work during migration. Affects: FR-12 sequencing.
- **Q10 (eval harness)**: Lightweight eval in R6 (markdown-driven test conversations per persona + playbook). Full harness with metrics + CI integration deferred to R7. Affects: success criteria verification approach.
- **Q11 (facade for invoke_playbook)**: New `IInvokePlaybookAi` facade (cleaner than extending `IWorkspacePrefillAi`). Affects: FR-21 facade design.

---

## Unresolved Questions

*Items requiring owner final decision before R6 implementation kicks off. All have working assumptions above; final confirmation needed in plan phase.*

- [ ] **Q1 — Persona inheritance hierarchy**: Confirm "most-specific-wins" model is correct (Q1 assumption above).
- [ ] **Q2 — Persona entity model**: Confirm standalone `sprk_aipersona` vs extension of `sprk_analysisaction`.
- [ ] **Q3 — Scope admin UI timing**: Confirm R7 deferral acceptable (Power Apps forms in R6).
- [ ] **Q4 — Workspace tab persistence**: Confirm hybrid model (agent-ephemeral, user-pinned-persistent).
- [ ] **Q5 — Output schema entity**: Confirm extending `sprk_analysisaction` (vs standalone entity).
- [ ] **Q6 — Slash vocabulary final list**: Confirm specific hard/soft slash commands (current proposal: 6 hard + 4 soft).
- [ ] **Q7 — Memory user affordances**: Confirm R6 primitives only (UI panel R7).
- [ ] **Q8 — Conflict resolution**: Confirm user-wins approach (no OT/CRDT in R6).
- [ ] **Q9 — Tool registry migration**: Confirm incremental approach (one tool first, then batch).
- [ ] **Q10 — Eval harness scope**: Confirm lightweight (R6) + full harness (R7) split.
- [ ] **Q11 — Facade for invoke_playbook**: Confirm new `IInvokePlaybookAi` vs extending `IWorkspacePrefillAi`.

**Additional decisions for plan phase**:
- [ ] Order of 8 typed handler implementation (which to ship first — likely pure-deterministic ones first for quick wins: `DateExtractorHandler`, `FinancialCalculatorHandler`, `ClauseComparisonHandler`)
- [ ] Migration sequence for 10 pre-R5 chat tools (which to migrate first, validation gate before next)
- [ ] Concrete schema for `WorkspaceTab` interface (per-widget data discriminators)
- [ ] Whether `context.tool_call_*` events log tool inputs (ADR-015 implications — likely tool NAME only, not input content)

---

## R6 Initialization Checklist

Per design.md §9 — items needed to formally start R6 implementation after this spec is approved:

- [x] R5 closed
- [x] R6 design.md merged to master
- [x] R6 spec.md (this document) authored
- [ ] R6 plan.md (WBS with task decomposition per pillar)
- [ ] R6 CLAUDE.md (project-scoped rules — extend root CLAUDE.md)
- [ ] R6 tasks/ folder (POML files per task — generated via `/project-pipeline`)
- [ ] R6 design decisions captured in `notes/` (one per pillar where significant tradeoffs need recording)
- [ ] Open Questions Q1-Q11 reviewed + decided
- [ ] Architecture review meeting (if applicable)
- [ ] Feature branch + worktree created off latest master (with design.md + spec.md merged)

---

## References

- [`design.md`](design.md) — full R6 architecture design (1457 lines, 9 pillars + 11 open questions + appendix discussion notes)
- [`README.md`](README.md) — R6 project landing page
- **R5 predecessor**: [`../spaarke-ai-platform-unification-r5/`](../spaarke-ai-platform-unification-r5/) — closed 2026-06-06; lessons-learned + closeout docs
- **Architecture docs (canonical)**:
  - [`docs/architecture/playbook-architecture.md`](../../docs/architecture/playbook-architecture.md)
  - [`docs/architecture/scope-architecture.md`](../../docs/architecture/scope-architecture.md)
  - [`docs/architecture/chat-architecture.md`](../../docs/architecture/chat-architecture.md)
  - [`docs/architecture/AI-ARCHITECTURE.md`](../../docs/architecture/AI-ARCHITECTURE.md)
  - [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md)
  - [`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`](../../docs/architecture/SPAARKEAI-COMPONENT-MODEL.md)
  - [`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md)
  - [`docs/guides/JPS-AUTHORING-GUIDE.md`](../../docs/guides/JPS-AUTHORING-GUIDE.md)
  - [`docs/guides/SCOPE-CONFIGURATION-GUIDE.md`](../../docs/guides/SCOPE-CONFIGURATION-GUIDE.md)
  - [`docs/guides/WORKSPACE-AI-PREFILL-GUIDE.md`](../../docs/guides/WORKSPACE-AI-PREFILL-GUIDE.md)
  - [`docs/guides/INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md`](../../docs/guides/INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md)
- **ADRs** (binding for R6):
  - [`.claude/adr/ADR-013-ai-architecture.md`](../../.claude/adr/ADR-013-ai-architecture.md)
  - [`.claude/adr/ADR-030-pane-event-bus.md`](../../.claude/adr/ADR-030-pane-event-bus.md)
  - [`.claude/adr/ADR-031-stage-lifecycle.md`](../../.claude/adr/ADR-031-stage-lifecycle.md)
  - [`.claude/adr/ADR-014-ai-caching.md`](../../.claude/adr/ADR-014-ai-caching.md)
  - [`.claude/adr/ADR-016-ai-rate-limits.md`](../../.claude/adr/ADR-016-ai-rate-limits.md)
  - [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md)
  - [`.claude/adr/ADR-010-di-minimalism.md`](../../.claude/adr/ADR-010-di-minimalism.md)
  - [`.claude/adr/ADR-008-endpoint-filters.md`](../../.claude/adr/ADR-008-endpoint-filters.md)
  - [`.claude/adr/ADR-015-ai-data-governance.md`](../../.claude/adr/ADR-015-ai-data-governance.md)
  - [`.claude/adr/ADR-029-bff-publish-hygiene.md`](../../.claude/adr/ADR-029-bff-publish-hygiene.md)

---

*AI-optimized specification. Original design: `design.md`. Authored 2026-06-07 via `/design-to-spec` skill following extensive design refinement conversation (2026-06-06 → 2026-06-07). Owner clarifications captured throughout the design phase; Q1-Q11 working assumptions noted for final plan-phase confirmation.*
