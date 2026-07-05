# SPAARKE AI CODE INVENTORY

> **Status**: v1.0 — Step 1 deliverable of `spaarke-ai-code-audit-r1`
> **Date**: 2026-07-05
> **Method**: 7 parallel read-only Explore agents (BFF orchestration, BFF chat, client shared libs,
> SpaarkeAi code page, manifest/JPS/schema, peripheral surfaces, r7 branch delta) + main-session
> worktree pre-scan. Full per-agent reports in [`notes/agent-findings-*.md`](notes/); worktree scan in
> [`notes/worktree-delta-scan.md`](notes/worktree-delta-scan.md). Every claim below carries a path and
> is verifiable by grep (NFR-4).
> **Classification rubric**: the five target categories from
> [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../../docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md)
> v0.2.6 (Session / Consumer / Tool / Dispatcher / Manifest) + two support buckets
> (Widget/Output-routing, Infra/Support). See [`spec.md`](spec.md) §2.
> **Consumed by**: Step 2 (§4-7 design) and Step 3 (`SPAARKE-AI-MIGRATION-MAP.md`).

---

## 0. Executive summary

**The good news**: substantial pieces of the target architecture already exist in recognizable form.
Session state has a real 3-tier store (Redis/Cosmos/Dataverse). A typed, data-driven tool framework
(`IToolHandler` + `sprk_analysistool` rows) replaced most hardcoded tools in R6 — that IS a Tool
catalog in embryo. `ConsumerRoutingService` + `sprk_playbookconsumer` is a maker-editable Consumer
routing table. The LinearConsumers stack (R7 Wave 12) is target-architecture-shaped Consumer
execution. PaneEventBus + widget registries + `StructuredOutputStreamWidget` are a working M6/M7
widget layer. The 33-executor vocabulary + JPS + PlaybookBuilder is a real (if fragmented) Manifest.

**The bad news, quantified**:
1. **Dispatch drift is ~2.5× worse than assumed** — the working figure was "four intent mechanisms";
   the audit found **NINE distinct dispatch/intent mechanisms live in master's chat path** (several
   firing sequentially inside one `SendMessageAsync` turn), plus r7's unmerged regex as a tenth (§4).
2. **Playbook routing truth is split across FOUR config surfaces** — the `sprk_playbookconsumer`
   table (canonical), the `LinearConsumers` appsettings block, the superseded-but-still-wired
   `Workspace.*PlaybookId` appsettings, and `Insights.Playbooks.Map` (§5.3).
3. **~24 identified duplicate/overlap pairs** across server and client (§8) — including two
   orchestration engines, two chat-summarize execution paths, three historical cross-pane event
   mechanisms, three independently-implemented chat-hook sets, and two client summarize implementations.
4. **A sizable dead-code register** (§9) — headlined by the ~14-file SpaarkeAi Insights renderer
   cluster (never wired), the `DirectOpenAiAgent`/`ISprkAgent` cluster (registered in DI, never
   consumed, with a test maintaining it), 5 source-less/empty PCF directories, R1 registries and
   providers superseded by R2, and a matched trio of built-but-unmounted workspace affordances.
5. **The manifest documentation surface is largely stale** — the live R7 vocabulary is defined by just
   three current artifacts (`executorMetadata.ts`, the R7-refreshed guides, `sprk-playbookconsumer.md`);
   the 2026-02 ERD docs are actively misleading (show a column R7 dropped), the scope/model catalog is
   4 months stale in two divergent copies, and no data-model doc exists for `sprk_playbooknode` (§5.4).

**Worktree answer**: 17 of 24 worktrees are fully merged — the "27 projects of debt" lives in master,
not in branches. Only r7 carries a substantive unmerged AI delta (Appendix A).

---

## 1. SESSION — per-session state storage (target: M1 session state graph)

| Component | Path | Status | Notes |
|---|---|---|---|
| `ChatSession` / `ChatSessionFile` models | `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatSession.cs` | working | `ChatSessionFile` carries 8 enrichment fields (SummaryText, ClassifiedDocType, Sections, TableMetadata, Citations, PageCount, Language, ClassifiedConfidence) from chat-routing-redesign-r1. r7 adds `ExtractedText` (unmerged). |
| `ChatSessionManager` (3-tier) | `Services/Ai/Chat/ChatSessionManager.cs` | working | Redis hot (24h sliding) → Cosmos warm (`SessionPersistenceService`, write-through) → Dataverse cold (`sprk_aichatsummary`). **Caveat**: Cosmos mapping drops `UploadedFiles`+`DocumentId` — restored sessions lose the file manifest. |
| `ChatHistoryManager` | `Services/Ai/Chat/ChatHistoryManager.cs` | working | Summarize@15 / archive@50. |
| `SessionPersistenceService` (Cosmos) | `Services/Ai/Sessions/` | working | Also backs the `/restore` endpoint (widget-state restore). |
| `ChatDataverseRepository` | `Services/Ai/Chat/ChatDataverseRepository.cs` | working | `sprk_aichatsummary`/`sprk_aichatmessage` cold store. |
| `PendingPlanManager` (+Null) | `Services/Ai/Chat/PendingPlanManager.cs` | working | Redis 30-min TTL compound-intent plan state. |
| `SessionFilesCleanupJob/Signal` | `Services/Ai/Chat/SessionFilesCleanup*.cs` | working | Hosted service evicting session-files index docs on session end. |
| `SessionFileTextSource` | `Services/Ai/LinearConsumers/SessionFileTextSource.cs` | working | Session-file text from `spaarke-session-files` index; r7 adds inline-ExtractedText-first read. |
| Chat doc upload + Redis text cache | `Api/Ai/ChatDocumentEndpoints.cs` | working | Extracted text in tenant Redis (`doc-upload-*`, 4h TTL); 50MB, PDF/DOCX/TXT/MD. |
| `AiSessionProvider` / `useAiSession` (client) | `src/client/shared/Spaarke.AI.Widgets/src/providers/` | working | Client session state (localStorage ids), routes SSE pane events → PaneEventBus. **Replaces R1 `StandaloneAiProvider` (dead)**. |
| `useChatSession` hook — **×2 duplicate impls** | `Spaarke.UI.Components/.../SprkChat/hooks/` AND `Spaarke.AI.Context/src/hooks/` | working ×2 | Same BFF contract, independent implementations (SprkChat consumers vs AnalysisWorkspace). Drift risk. |
| `useSessionRestore` / `SessionRestoreManager` | `src/solutions/SpaarkeAi/src/hooks/useSessionRestore.ts` + ThreePaneShell | working | GET `/restore`, once per session id. |
| `HistoryOverlay` (live) vs `ChatHistoryPanel` (orphaned) | `src/solutions/SpaarkeAi/src/components/` | working / **dead** | Two session-history surfaces; only HistoryOverlay wired. |

**Gap vs target (M1)**: no unified `session.outputs` addressable store (`uc_id@turn`), no
`in_progress_dispatch` slot-fill state, no widget-state-in-session contract. Conversation, files, and
widget tabs persist; **prior capability outputs do not persist as addressable session state** — the
walkthrough's P4 (output-as-input plumbing) has no server-side carrier today.

---

## 2. CONSUMER — curated end-to-end capabilities (target: Consumer catalog, §3.10.7.5)

### 2.1 Server execution paths (the engines Consumers run on)

| Component | Path | Status | Notes |
|---|---|---|---|
| `PlaybookOrchestrationService` | `Services/Ai/PlaybookOrchestrationService.cs` | working | Canonical node-graph engine (ExecutionGraph, executor registry, parallel batches, SSE). Legacy mode delegates to AnalysisOrchestrationService. |
| `AnalysisOrchestrationService` | `Services/Ai/AnalysisOrchestrationService.cs` | working (legacy) | Single-doc analysis; transitional dual path. R7 FR-11 targets deletion of `ExecuteAnalysisAsync`. |
| `PlaybookExecutionEngine` | `Services/Ai/PlaybookExecutionEngine.cs` | working but thin | "Unified" wrapper whose batch mode delegates back to PlaybookOrchestrationService. **Biggest server consolidation target.** |
| **LinearConsumers stack** | `Services/Ai/LinearConsumers/` (`FileSummarizeService`, `DocumentProfileService`, `ActionResolver`, `ActionRunner`, `DocumentTextSource`) | working, newest | R7 W12 target-architecture-shaped: consumerType → Action → JPS render → structured LLM call, no playbook engine. Registered unconditionally (Program.cs:135). |
| `SessionSummarizeOrchestrator` (+Null) | `Services/Ai/Chat/SessionSummarizeOrchestrator.cs` | working, mid-migration | Chat-summarize Consumer (`SUM-CHAT@v1`). **Contains BOTH Linear and playbook-engine paths** with separate chunk translators; runtime choice via `ResolveActionAsync`. |

### 2.2 Cataloged consumer instances (server)

| Consumer | Where | Status |
|---|---|---|
| chat-summarize | `SessionSummarizeOrchestrator` + `SummarizeSessionEndpoint` | working (UC-A-1, verified 2026-07-03 e2e on r7) |
| document-profile | `LinearConsumers/DocumentProfileService` (AnalysisEndpoints branches) | working — migrated OFF the routing table into appsettings |
| matter-pre-fill / project-pre-fill | `Services/Workspace/{Matter,Project}PreFillService` via `WorkspacePrefillAi` facade | partial (UC-B-1/B-2, R7 W12.1/12.2 targets) |
| daily-briefing (summarize/narrate/render) | `DailyBriefingEndpoints` + `BriefingAi` / `DailyBriefingNarrator` + `DailyBriefingCollector` | partial — **narrate is dual-path** (playbook engine vs code narrator, flag `Features:NarrateUseCodeBasedNarrator` default false) |
| workspace file summarize | `/api/workspace/files/summarize` (WorkspaceFileEndpoints) | working — parallel to chat-summarize |
| invoice extraction / attachment classification | `Services/Jobs/Handlers/{InvoiceExtraction,AttachmentClassification}JobHandler` | working / flag-gated (UC-A-4 embryo) |
| insights ask/search + ingest | `Insights/InsightsOrchestrator` (+`InsightsIngestJobHandler`, default OFF) | partial |
| workspace feed/todo AI summaries | `Services/Workspace/{WorkspaceAiService,TodoGenerationService,BriefingService}` | working — **coverage-gap area outside Services/Ai**, all via facades |
| playbook authoring assistant | `AiPlaybookBuilderService` | working (maker-facing, not runtime) |

### 2.3 Client consumer surfaces

| Component | Path | Status |
|---|---|---|
| `executeSummarizeIntent.ts` | `src/solutions/SpaarkeAi/src/components/conversation/` | working — but `heldFilesRef` documented always-empty (un-landed File forwarding) → promotion path partial in prod |
| `executeLinearDispatch.ts` | same dir (r7 branch only) | **flagged for retirement** (Appendix A) |
| `executeComposeSummarize.ts` | `src/client/shared/Spaarke.Compose.Components/src/orchestrators/` | working — third client summarize orchestrator (Compose surface) |
| LegalWorkspace AI features | `SummarizeFiles/`, `Playbook/`, `FindSimilar/`, `CreateMatter/AiFieldTag`, `ActivityFeed/AISummaryDialog`, `SmartToDo/TodoAISummaryDialog` | working — second client summarize impl + playbook UI + AI form-fill |
| External SPA | `AiToolbar.tsx` (3 hardcoded playbooks), `usePlaybookExecution`, `PlaybookLibraryPage`, `SemanticSearch` | working |
| `SemanticSearchControl` PCF | `src/client/pcf/SemanticSearchControl/` | working, mature (v1.1.51) |
| Office add-in save flow | `office-addins/shared/taskpane/hooks/useSaveFlow.ts` | working — AI **trigger flags only** (profileSummary default true, deepAnalysis default false); no client-side AI |
| DailyBriefing components | `Spaarke.DailyBriefing.Components/` | working (live via subpath imports; Pattern D dual-use) |
| UI hooks: `useAiSummary`, `useAiPrefill`, `useInlineAiActions`/`useInlineAiToolbar` | `Spaarke.UI.Components/src/hooks/` | working — record-header inline AI + prefill primitives |

**Gap vs target**: no Consumer declares the 12-field contract (match hints, input schema w/ session
resolution, disposition, chip transitions, capture mode, confirmation threshold). Dispositions
(informational/work_product/overlay) exist only implicitly in per-consumer rendering code. Chip
transitions are hardcoded per-surface, not manifest data.

---

## 3. TOOL — typed primitives for LLM composition (target: Tool catalog, §3.10.7.6)

| Component | Path | Status | Notes |
|---|---|---|---|
| **Typed handler framework** | `ToolHandlerToAIFunctionAdapter` + `ToolFrameworkExtensions.AddToolHandlersFromAssembly` + `sprk_analysistool` rows | working | The R6 FR-11 data-driven tool surface — closest existing thing to the target Tool catalog. Gated `ToolFramework:Enabled`. |
| Live handlers (~16) | `Services/Ai/Handlers/`: RecallSessionFile, ManagePinnedContext, InvokePlaybook, DocumentSearch, KnowledgeRetrieval, AnalysisQuery, WorkingDocument, TextRefinement, WebSearch, VerifyCitations, CodeInterpreter, LegalResearch + 4 workspace-tab handlers | working | Auto-discovered; per-playbook capability gating in `SprkChatAgentFactory` (FR-23). |
| Legacy `Chat/Tools/*` classes | `Services/Ai/Chat/Tools/` | **mostly dead** | Superseded by handlers per factory comments (lines 880-1013). Live exceptions: `AnalysisExecutionTools` (reanalyze-gated), `TextRefinementTools` (RefineTextAsync only). |
| Write-shape node executors | `Services/Ai/Nodes/`: CreateTask, CreateNotification, SendEmail, UpdateRecord, QueryDataverse | working | These ARE the three write-shapes (§3.9.1) — but as playbook nodes, not LLM-composable tools. |
| Insights retrieval nodes | LiveFact(80), IndexRetrieve(90) | working | Insights zone. |
| `RecordMatchingAi` facade | `PublicContracts/RecordMatchingAi.cs` | working, gated | Record search primitive. |
| RAG retrieval | `Services/Ai/RagService.cs` (session branch) + `RecallSessionFileHandler` | working | `spaarke-session-files` index, tenant+session filtered. |
| **Dataverse MCP** | — | **ABSENT in repo** | No in-repo MCP server (only external reference in `IGroundingVerifier.cs:13`). The target's `dataverse.*` tool namespace (§3.10.7.8) has no current implementation — dev-tooling MCP exists but no runtime surface for the L3 loop. |

**Gap vs target**: handlers lack the 8-field tool contract (`side_effect_class`, `permission_scope`,
`budget_class`); no bounded L3 planner loop with tool-call budget + audit trail (`session.outputs["L3@t{n}"]`);
writes gate through CompoundIntentDetector name-matching rather than a declared side-effect class.

---

## 4. DISPATCHER — the drift, enumerated (target: Layer 0 + L1-L4, §3.10.7.2)

### 4.1 The census: TEN mechanisms (9 in master + 1 unmerged)

| # | Mechanism | Path | Routes to |
|---|---|---|---|
| 1 | `CompoundIntentDetector` — keyword heuristic over proposed tool-call names | `Services/Ai/Chat/CompoundIntentDetector.cs` | plan_preview gate when ≥2 tools or name in hardcoded write/external sets |
| 2 | `PlaybookDispatcher` — 2-stage vector (≥0.85 bypass) + LLM refinement; separate Phase B per-file vector match | `Services/Ai/Chat/PlaybookDispatcher.cs` | `DispatchResult` → `PlaybookOutputHandler` |
| 3 | LLM agent tool loop (`ChatToolMode.Auto`) | `Services/Ai/Chat/SprkChatAgent.cs` | any typed handler |
| 4 | `SoftSlashRouter` → `intentHint` bias (client-side; BFF consumes `IntentHint`) | `src/solutions/SpaarkeAi/.../SoftSlashRouter.ts` | biases PlaybookDispatcher Phase B query |
| 5 | `AgentServiceRoutingMiddleware` — keyword classifier | `Services/Ai/Chat/Middleware/` | Foundry Agent Service vs direct pipeline (conditional wiring) |
| 6 | `IntentRerankerService` — gpt-4o-mini rerank top-5→top-3 (FR-46) | `Services/Ai/Chat/IntentRerankerService.cs` | reranked `playbook_options` candidates |
| 7 | `PlaybookCandidateSelector` — top-N file-aware selection (FR-47/48) | `Services/Ai/Chat/PlaybookCandidateSelector.cs` | `playbook_options` SSE (never auto-executes) |
| 8 | `ConsumerRoutingService` — consumer-key routing table | `Services/Ai/PublicContracts/ConsumerRoutingService.cs` | Linear vs engine vs config fallback |
| 9 | `InvokePlaybookHandler` — LLM-chosen playbook GUID (tool-mediated) | `Services/Ai/Handlers/InvokePlaybookHandler.cs` | orchestration triangle |
| 10 | `TryDetectExplicitConsumerType` regex + `linear_dispatch` SSE (**r7 branch only, flagged for retirement**) | r7 `Api/Ai/ChatEndpoints.cs` | hardcoded chat-summarize bypass |

Client-side dispatch companions: `CommandRouter` (7 hard + 4 soft slashes + 3 ref sigils),
`HardSlashExecutor` (deterministic), `ReferenceResolver` (#scope/@entity/#file), `intentMatcher`
(config-shaped registry with exactly ONE entry ever registered), wizard-launcher widgets (7 thin
dispatcher widgets in AI.Widgets), `GetStartedCardsWidget` card dispatch, suggestion chips.

### 4.2 Mapping to the target layers

| Target layer | Exists today? |
|---|---|
| **Layer 0** on-upload auto-composite | ❌ Nothing auto-dispatches on upload. Closest: `playbook_options` attachment flow (requires user click, FR-48 must-click by design) and Office save-flow flags. |
| **L1** chip click deterministic | ✅ partial — chip/suggestion wiring + HardSlashExecutor + `playbook_options` click exist, but chips are NL prompts or hardcoded launchers, not Consumer-id dispatches. |
| **L2** NL → Consumer catalog classify | ⚠️ fragmented — mechanisms #2/#4/#6/#7 collectively approximate it against the PLAYBOOK catalog (not a Consumer catalog), with none reading session context beyond attachments. |
| **L3** bounded tool loop | ⚠️ mechanism #3 is an UNBOUNDED-ish tool loop (no per-turn call budget, no L3 audit-chain schema, no fallthrough contract from L2). |
| **L4** honest refusal | ❌ no no-match handler; unmatched utterances fall through to free-form-ish grounded chat. |
| **M4** confirmation gate | ⚠️ THREE overlapping gate-before-write surfaces: CompoundIntentDetector/PendingPlanManager (`/plan/approve`) vs `/actions/{id}/confirm` HITL (different pending store) vs FR-48 must-click. No confidence-threshold gating. |

**None of the ten mechanisms reads prior-capability outputs or session graph state** — confirming the
doc's §3.0 point 3 diagnosis. The r7 close plan's own remark ("Phase 12.4: replace regex with
`sprk_analysisplaybook.sprk_intenttriggers` lookup") shows the tactical path was already pointing at
data-driven intent triggers.

---

## 5. MANIFEST — maker configuration (target: §6 capability manifest)

### 5.1 Live, R7-current vocabulary (three artifacts)
- **`executorMetadata.ts`** — 33-executor vocabulary, 6 tiers (`src/client/code-pages/PlaybookBuilder/src/config/`). Mirrors server `ExecutorType`.
- **R7-refreshed guides** — `JPS-AUTHORING-GUIDE.md` v4.0, `PLAYBOOK-AUTHOR-GUIDE.md`, `ai-guide-playbook-deploy-recipe.md` (2026-06-28/29).
- **`sprk-playbookconsumer.md`** (2026-06-28) — routing-table schema + 7 deployed rows.

What a maker can declare as data: playbook + nodes (executorType, actionCode, dependsOn, configJson),
action (JPS prompt + output schema), consumer routing row, tool row (`sprk_analysistool`), scopes/
skills/knowledge/personas, chat context mappings (`sprk_aichatcontextmap`), slash commands (dynamic
resolver), grid configs. What still requires code: executor impls, ConsumerTypes constants, handler
classes, canvas renderers (20/33 fallback to generic), all appsettings-based routing.

### 5.2 Manifest-reading services (all working)
`PlaybookService`, `NodeService`, `ScopeResolverService` + focused CRUD services (Action/Skill/
Knowledge/Tool/Persona), `ModelSelector`, `ConsumerRoutingService`, `ChatContextMappingService`,
`DynamicCommandResolver`, `PlaybookLookupService` (⚠️ registered in FinanceModule — placement smell),
context providers resolving ACT-* prompts. Maker UIs: PlaybookBuilder code page, `ScopeConfigEditor`
PCF (per-entity editors), WorkspaceLayoutWizard (layout manifest).

### 5.3 Routing truth split across FOUR surfaces
1. `sprk_playbookconsumer` table — canonical, maker-editable (7 rows per doc).
2. `LinearConsumers` appsettings — R7 W12 moved `document-profile` here.
3. `Workspace.*PlaybookId` appsettings — the pattern the table was created to replace; still wired as fallback.
4. `Insights.Playbooks.Map` appsettings — name-keyed.
Plus: **consumer count disagrees across 3 artifacts** (doc=7, seed script=6-or-7, `ConsumerTypes.cs`=8)
and **BFF-embedded `*.playbook.json`** files are a parallel non-maker-editable playbook source.

### 5.4 Stale manifest surface (actively misleading items first)
- `docs/data-model/sprk_ERD-ai-analysis-entities.md` + `sprk_ai-analysis-related-entities.md` (2026-02-13) — show `sprk_actiontypeid` which R7 **dropped**; no playbooknode/executortype/playbookconsumer. **Misleading.**
- `.claude/catalogs/scope-model-index.json` + `docs/ai-knowledge/catalogs/` twin — $generated 2026-03-05, divergent taxonomy vs `scripts/seed-data/` (ACT-001 means different things), compositions don't match deployed GUIDs. Refresh script exists but unrun ~4 months.
- `scripts/seed-data/{actions,playbooks}.json` (2026-01, R4 taxonomy) — superseded.
- `scripts/Seed-JpsActions.ps1` — sources from project-notes dirs, likely broken.
- `docs/data-model/INDEX.md` — omits sprk-playbookconsumer.md.
- `infra/dataverse/playbooks/summarize-document-for-workspace-v1-multinode.json` — authoritative target, **blocked-undeployed** on `sprk_nodetype` option-set gap (`DeliverComposite=100000004` missing).
- No standalone JPS JSON schema; no `sprk_playbooknode` data-model doc.

---

## 6. WIDGET / OUTPUT-ROUTING (target: M6 widget contract + M7 disposition routing)

| Component | Path | Status |
|---|---|---|
| **PaneEventBus** + provider + hooks + `PaneEventTypes` | `Spaarke.AI.Widgets/src/events/` | working — typed multi-subscriber bus, 4 channels. The current cross-pane architecture. |
| `WorkspaceWidgetRegistry` / `ContextWidgetRegistry` | `Spaarke.AI.Widgets/src/registry/` | working — canonical lazy registries (R2). **R1 registries in AI.Outputs are dead.** |
| `StructuredOutputStreamWidget` | `Spaarke.AI.Widgets/src/widgets/workspace/` | working — schema-driven FieldDelta renderer (SUMMARIZE_SCHEMA etc.). Load-bearing. |
| ~20 registered workspace widgets + 6 context wrappers + safety overlays + interactions (TextSelection, CitationLink, TabContextMapping, StageTransitionRules) | `Spaarke.AI.Widgets/src/` | working |
| `WorkspacePane` + `WorkspaceTabManager` | `src/solutions/SpaarkeAi/src/components/workspace/` | working — widget_load→addTab, persistence, restore, visibility. Heavy hotfix history. |
| `sseToPaneEventBridge.ts` | SpaarkeAi conversation | working (r7 adds field_delta synthesis from terminal chunk) |
| Server SSE: `ChatSseEventFactory` + typed events, `R2SseEventEmitter`, `SseOutputGuard`, `PlaybookOutputHandler` (7 output types), Deliver* node executors, `OutputOrchestratorService` (⚠️ FinanceModule placement), export/delivery services | `Services/Ai/` | working |
| `ExecutionTraceWidget` | AI.Widgets + ContextPane | **partial** — mounted, but BFF→SSE→bus bridge unbuilt; renders empty. |
| Historical cross-pane mechanisms | AI.Outputs `cross-pane/` (CustomEvent), `SprkChatBridge` (BroadcastChannel, @deprecated) | **dead/superseded** by PaneEventBus |

**Gap vs target**: no `disposition` field anywhere — rendering targets are per-consumer code decisions.
Widget→session emit (M6 third leg) exists only for tab persistence, not as consumable session events.

---

## 7. INFRA / SUPPORT (condensed)

Working: OpenAI client + keyed raw client, `PromptSchemaRenderer` (shared by Linear + node paths) +
`PromptSchemaOverrideMerger`, agent middleware (ContentSafety/CostControl/Telemetry), `IPromptBudgetTracker`,
`ModelSelector`, TemplateEngine + Word/Email delivery + Export services, extraction pipeline
(Document Intelligence), embedding/indexing jobs (RAG docs / invoices / records — three parallel
index-writers), `EntityNameScrubber`, ADR-032 Null-Object kill-switch fleet (17 services), compound
gate `Analysis:Enabled && DocumentIntelligence:Enabled`, `RoutingConsumerTypeHealthCheck`,
`IAnalysisDataverseService` (server-shared), SpaarkeAi shell (ThreePaneShell/stage machine/auth).
LLM-call wrapping duplicated 4× (ActionRunner / AiCompletionNodeExecutor / BriefingAi / InvokePlaybookAi).
Dataverse plugins: **confirmed no AI**. Office add-ins: no client-side AI.

---

## 8. Duplicate / overlap register (consolidation candidates for §5-7 design)

| # | Overlap | Evidence |
|---|---|---|
| O-1 | Two server orchestration engines | PlaybookOrchestrationService vs PlaybookExecutionEngine (batch delegates back) |
| O-2 | Legacy vs node-based analysis | AnalysisOrchestrationService vs engine Legacy-mode |
| O-3 | Two chat-summarize execution paths in one class | SessionSummarizeOrchestrator Linear vs engine + 2 translators |
| O-4 | Summarize capability across subtrees | FileSummarizeService (Linear) vs SessionSummarizeOrchestrator (Chat) vs `/api/workspace/files/summarize` |
| O-5 | Document-profile dual path | LinearConsumer vs engine branch in AnalysisEndpoints |
| O-6 | Narrate dual path | engine vs code narrator (flag) |
| O-7 | Vector matching ×2 in PlaybookDispatcher | Stage-1 vs Phase B, divergent cache keys |
| O-8 | Three gate-before-write surfaces | plan/approve vs actions/confirm vs FR-48 must-click |
| O-9 | Legacy Chat/Tools vs typed Handlers | same capabilities, two class families |
| O-10 | Two agent abstractions | ISprkChatAgent (live) vs ISprkAgent (dead) |
| O-11 | LLM-call wrapping ×4 | ActionRunner / AiCompletionNodeExecutor / BriefingAi / InvokePlaybookAi |
| O-12 | Four playbook-routing config surfaces | §5.3 |
| O-13 | Two Action/scope taxonomies | .claude/catalogs vs scripts/seed-data |
| O-14 | Two scope-model-index copies | .claude vs docs/ai-knowledge |
| O-15 | Playbooks in Dataverse AND BFF-embedded JSON | Services/Ai/{Chat,Insights}/Playbooks/ |
| O-16 | Three chat hooks duplicated | SprkChat vs AI.Context (useChatSession/ContextMapping/Playbooks) |
| O-17 | Two widget-registry systems | AI.Outputs R1 (dead) vs AI.Widgets R2 |
| O-18 | Two register-context-widgets.ts | inside AI.Widgets ("deliberate" per index) |
| O-19 | Three historical cross-pane mechanisms | cross-pane CustomEvent vs SprkChatBridge vs PaneEventBus |
| O-20 | Two client summarize implementations | executeSummarizeIntent vs LegalWorkspace summarizeService (+ Compose's executeComposeSummarize = third orchestrator) |
| O-21 | Manual SSE line-parsers ×3+ client | executeSummarizeIntent, insightsQueryClient (dead), LegalWorkspace summarizeService (shared-lib side resolved by AIPU2-082) |
| O-22 | Two Get-Started card catalogs | SpaarkeAi widget vs LegalWorkspace config |
| O-23 | pinned-workspaces localStorage duplicated | SpaarkeAi vs WorkspaceLayoutWizard ("MUST stay in sync") |
| O-24 | Semantic search + playbook-execution clients ×2-3 | PCF vs SPA; SPA hook vs PlaybookLibraryShell vs SpaarkeAi |

## 9. Dead-code register (Step 3 retirement candidates)

**Server**: `DirectOpenAiAgent` + `ISprkAgent` + DTOs (`AgentRequest`, `ConversationTurn`, `SseEvent.cs`)
— registered `AiChatModule.cs:61`, never consumed, test maintains it · legacy `Chat/Tools/*` (except 2
live) · `SummarizeInvocationPath.AgentTool` · `PlaybookDispatcher.RunPhaseBManifestPresentAsync`
(unreachable scaffolding) · `ScopeGapDetector` (no DI reg) · `LoadKnowledgeNodeExecutor` (R4 placeholder)
· `FallbackScopeCatalog`/`FallbackPrompts` (verify) · `LinearConsumersOptions.ActionIds` residue ·
`DocumentVectorBackfillService` (one-time migration, stub method) · CompoundIntentDetector dead
assignment (lines 97-98).

**Client shared**: `StandaloneAiProvider` + `useStandaloneAi` (AI.Context) · `output-registry` +
`source-registry` (AI.Outputs) · 4 unregistered AI.Outputs widgets (Chart, DataTable, Timeline,
DocumentCompare) · `cross-pane/` · `SprkChatExportWord` · `SprkChatBridge`.

**SpaarkeAi page**: **Insights renderer cluster (~14 files)** — never wired ·
`notificationContextLoader.ts` (self-declared dead) · Pillar-6b trio (`SendToWorkspaceButton`,
`PinToMatterButton`, `AddToAssistantToggle`) built+tested, zero importers · `ChatHistoryPanel.tsx` ·
vestigial refs in WorkspacePane/ConversationPane.

**PCF**: `AIMetadataExtractor` (empty) · `AnalysisWorkspace`, `AnalysisBuilder`, `PlaybookBuilderHost`,
`DrillThroughWorkspace` (source-less, orphaned build artifacts).

**Manifest/scripts**: §5.4 stale list.

## 10. Notable operational caveats surfaced
- Cosmos-restored sessions lose the file manifest (intentional but composition-relevant).
- `/summarize` slash deliberately rerouted to NL path (B9 rewire); deterministic client path reachable mainly via buttons.
- `heldFilesRef` always-empty → client promotion path may not receive real File binaries in prod.
- Placement smells: `PlaybookLookupService` + `OutputOrchestratorService` in FinanceModule; LinearConsumers in Program.cs — all escape the compound AI gate.
- `CompoundIntentDetector` has no dedicated test; `DirectOpenAiAgentTests` maintains dead code.
- Diagnostic `LogInformation` in r7 ChatEndpoints marked "remove after root cause" must not merge as-is.

---

## Appendix A — Worktree deltas (full detail: notes/worktree-delta-scan.md + notes/agent-findings-r7-delta.md)

**17/24 worktrees fully merged** — master baseline covers them. Unmerged branches:

| Branch | AI verdict |
|---|---|
| **r7** (this one) | Wave 12.3 Phase 12.3a. **KEEP (sound)**: resumeSession/onSessionStale/clearChatSession session-id fix; ExtractedText persistence (write+model+read); ConversationPane auto-promote w/ retry; field_delta synthesis in bridge. **RETIRE (confirmed)**: `TryDetectExplicitConsumerType` regex + `linear_dispatch` SSE event/factory/useSseStream case/types + `executeLinearDispatch.ts` (bare-fetch ADR-028 divergence). Redesign must PRESERVE: empty-attachments guard; must REPLACE (not revert) the retired NL branch (double-dispatch race). Debug log must not merge. |
| fix-daily-briefing-shared-lib | build-config only, no AI logic |
| dataset-grid-framework-r2 | grid plumbing into DataverseEntityViewWidget + Prettier on Compose; no AI logic |
| daily-update-service-r4 / workspace-UI-r2 / set-regarding-r1 / fix-events-smarttodo | no AI code |

## Appendix B — Target-model presence scorecard (input to Step 2)

| Target concept | Today | Verdict |
|---|---|---|
| M1 session outputs store | 3-tier session store, no addressable outputs | **build on existing** |
| M2/M3 dispatchers | 10 fragmented mechanisms | **consolidate hard** |
| M4 confirmation gate | 3 overlapping gates, no thresholds | **consolidate** |
| M5 slot-fill | nothing (modal wizards only) | **new** |
| M6 widget contract | PaneEventBus + registries + restore | **mostly exists** |
| M7 disposition routing | per-consumer hardcode | **formalize** |
| Layer 0 auto-composite | absent (must-click by design today) | **new** |
| Consumer catalog | sprk_playbookconsumer + ConsumerTypes (8) + 4 routing surfaces | **unify + extend contract** |
| Tool catalog | typed handler framework + sprk_analysistool | **extend contract (8-field), add bounded L3 planner** |
| Manifest | 33 executors + JPS + routing table + scopes | **exists, fragmented + doc-stale** |
| Dataverse MCP tools | absent at runtime | **new** |
| Honest refusal (L4) | absent | **new** |
