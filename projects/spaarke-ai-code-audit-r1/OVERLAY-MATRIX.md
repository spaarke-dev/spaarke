# Overlay Matrix — existing components → greenfield slots

> **Status**: DRAFT v1.0 — 2026-07-05, for operator review.
> **Method** (operator-agreed): **slot-first** — for each greenfield component
> (GREENFIELD-CONCEPTUAL-DESIGN.md v0.2), which existing components (per
> SPAARKE-AI-CODE-INVENTORY.md) can serve it? Governed by the **no-widening
> rule**: reuse is accepted only if it fills the slot WITHOUT widening the
> greenfield contract; every proposed widening is an explicit Exception (§E)
> for operator approval. Components selected for no slot land in one of two
> buckets: **TL** (transitional legacy — keeps running, named retirement
> trigger, NOT part of the target) or **DEL** (Track B delete).
> **Verdicts**: ✅ fills as-is · 🔧 fills with trim/extension · 🆕 build fresh
> (nothing fits) · TL · DEL.
> **This document + the inventory's §9 register = the substance of Step 3's
> migration map.** After operator review, canonical doc §4-7 rewrites to v0.4
> (greenfield-as-target + "fulfilled by" column) and Step 3 adds sequencing.

---

## Server slots

### S1 · Session Ledger

| Candidate | Verdict | Detail |
|---|---|---|
| `ChatSessionManager` 3-tier (Redis/Cosmos/Dataverse) + `SessionPersistenceService` + `ChatDataverseRepository` | 🔧 **fills** | The persistence machinery IS the ledger store. Extension: `ChatSession` model gains typed ledger entries — `Outputs` (the §5.2 `SessionOutput` record), `ToolChains`, `WidgetEvents`, `Gates` — all joining the Redis+Cosmos payloads. Fix: Cosmos mapping stops dropping file references (P2 violation). |
| `ChatHistoryManager` (summarize@15/archive@50) | 🔧 fills | Becomes the **session digest** maintainer; compaction generalizes to cover outputs, not just messages. |
| `ChatSessionFile` (+8 enrichment fields, +r7 `ExtractedText`) | ✅ fills | Already richer than the ledger's `Doc` entry needs. |
| `SessionFilesCleanupJob/Signal` | ✅ fills | Unchanged. |
| `useSessionRestore` + `SessionRestoreManager` (client) | ✅ fills | Restore reads the richer ledger; no contract change. |
| Redis doc-upload text cache (`ChatDocumentEndpoints`) | ✅ fills | Unchanged. |

No exceptions. Nothing displaced.

### S2 · Capability Catalog (Action + Binding)

| Candidate | Verdict | Detail |
|---|---|---|
| `sprk_analysisaction` + `AnalysisActionService` | 🔧 **fills** (Action row) | Add: `kind` (prompted\|coded + workflow ref), `input_schema`, default model tier. Prompt/output-schema/scope refs unchanged. |
| `sprk_playbookconsumer` + `ConsumerRoutingService` | 🔧 **fills** (Binding row + catalog reader) | Add: tool description/match surface, disposition, next_steps, risk, on_event, surfaces, model override. Reader returns full contract; cache pattern unchanged. Startup health check (extend `RoutingConsumerTypeHealthCheck`) reconciles `ConsumerTypes` constants ↔ rows. |
| Scope services (`ScopeResolverService`, skill/knowledge/persona CRUD, `ModelSelector`) | ✅ fills | Scopes unchanged in spirit (greenfield §8 Q8). |
| `DynamicCommandResolver` (slash catalog) + `ChatContextMappingService` | ✅ fills | Manifest reads, unchanged. |
| `ScopeConfigEditor` PCF + PlaybookBuilder (non-canvas parts) | 🔧 fills | Become THE Action/Binding/scope authoring surfaces (BA front end per OQ-2). PlaybookBuilder canvas de-scopes. |
| `sprk_analysisplaybook`/`sprk_playbooknode` + `PlaybookService`/`NodeService` | **TL** | Persist ONLY for frozen Insights composites (OQ-2). Retirement trigger: Insights pipeline restructure. |
| BFF-embedded `*.playbook.json` | **TL** | Frozen system composites, registered in catalog as such (kills shadow-manifest status, O-15). Same trigger. |
| `LinearConsumersOptions` maps, `Workspace.*PlaybookId`, `Insights.Playbooks.Map` | **DEL** | Rows migrate to Binding table (single-routing-surface rule), then delete. |
| `scope-model-index.json` ×2, `scripts/seed-data/` R4 taxonomy, `Seed-JpsActions.ps1` | **DEL/fix** | Per catalog-governance (canonical §6.6): one refreshed copy; stale taxonomy deletes. |
| 2026-02 ERD data-model docs | **DEL** | Replaced by current-schema docs (doc tasks in Step 3). |

### S3 · Capability Executor (prompted | coded)

| Candidate | Verdict | Detail |
|---|---|---|
| `ActionRunner` + `PromptSchemaRenderer` + `PromptSchemaOverrideMerger` + `ActionResolver` (LinearConsumers) | ✅ **fills `prompted`** | The R7 W12 stack IS the prompted executor. Registration moves under the compound AI gate with Null peers (placement fix). |
| `SessionFileTextSource` / `DocumentTextSource` | ✅ fills | Arg/ledger resolution feeders. |
| `DailyBriefingNarrator` + `DailyBriefingCollector` + `EntityNameScrubber` | 🔧 **fills `coded`** (first instances) | The Wave-11 pattern = the coded-workflow shape. Needs **E-1** (a light `ICodedWorkflow` registration convention). Narrate dual-path flag retires — the Binding row decides (O-6). |
| `FileSummarizeService` / `DocumentProfileService` | 🔧 absorb | Thin consumer-specific wrappers dissolve into Action rows + executor; residual multi-file assembly already lives in text sources. |
| `SessionSummarizeOrchestrator` (+Null) | **DEL** (after migration) | chat-summarize becomes a Binding row on the prompted path; the dual-engine shell dissolves (O-3). |
| `PlaybookExecutionEngine` | **DEL** | Callers (Insights orchestrator, Agent gateway, summarize) re-point to executor/frozen engine (O-1). |
| `AnalysisOrchestrationService` legacy path | **DEL** | Already planned (R7 FR-11, O-2). |
| `PlaybookOrchestrationService` + 33 node executors + `ExecutionGraph` + `NodeExecutorRegistry` | **TL** | Frozen representation for existing Insights composites only (OQ-2). No new capability lands on it. Retirement: attrition. Needs **E-2** (engine-output→ledger adapter). |
| `AiPlaybookBuilderService` | 🔧 fills | Retargets to AI-assisted Action/Binding authoring. |

### S4 · Agent Turn Runtime — **the OQ-1 slot**

| Candidate | Verdict | Detail |
|---|---|---|
| `SprkChatAgent` + `SprkChatAgentFactory` + middleware (ContentSafety/Cost/Telemetry) + `ToolHandlerToAIFunctionAdapter` | 🔧 **fills** | Contract additions per greenfield §4.3: bounded per-turn tool budget; capability-tools projection from the catalog; deterministic context pre-filter of the tool list; cite enforcement on reads; chain → ledger. Factory (2,714 lines) trims as legacy dispatch responsibilities leave it. |
| `AgentServiceRoutingMiddleware` | ✅ fills (infra) | Model-host routing, orthogonal to intent; stays middleware. |
| `SoftSlashRouter` + `CommandRouter` soft-slash vocab (client) | 🔧 fills | The 4 soft slashes map to deterministic direct invocation (click-path semantics) — no loop-contract widening (**E-3**). |
| `PlaybookDispatcher` (2-stage vector + Phase B) | **TL — this is the OQ-1 call** | Fills NO greenfield slot: the loop + tool descriptions replace utterance→catalog classification. Runs unchanged for existing chat paths until they migrate to the loop (retirement trigger: last NL chat path migrated). **Documented re-entry**: its embedding infra returns as a *tool-list pre-filter* if the catalog exceeds ~100 entries — an optimization in front of the loop, never the decision-maker. |
| `IntentRerankerService` | **TL** | Retires with the dispatcher (no slot). |
| `PlaybookCandidateSelector` | **TL** | Vector top-N has no slot; the must-click *presentation* it feeds survives as S6 gate UX. |
| `CompoundIntentDetector` | **DEL** (after S6 migration) | Compound handling is loop-native; write-gating moves to `side_effect_class` (the hardcoded name lists are exactly the shadow-catalog pattern the target forbids). |
| `InvokePlaybookHandler` | 🔧 fills | Becomes the loop's tool for invoking composite Actions; visibility-gating unchanged. |
| **Slot-fill / elicitation — the OQ-3 call** | 🆕-none | **No SlotFillEngine is built.** Missing required args are loop-native elicitation (schema-triggered asks); `capture_mode: modal` on a Binding routes to the wizard surface instead. A light in-flight marker in the ledger (part of the `Gate`/turn entries) replaces the dedicated `in_progress_dispatch` machinery. |
| Golden-utterance eval suite (CI) | 🆕 build (small) | The regression harness that replaces threshold tuning (greenfield §9 Q5). Test asset, not runtime. |

### S5 · Tool Gateway

| Candidate | Verdict | Detail |
|---|---|---|
| Typed handler framework (`IToolHandler` + adapter + registry + `sprk_analysistool` + auto-discovery) | 🔧 **fills** | Extend rows to the 8-field contract (`tool_id` namespaced, `side_effect_class`, `permission_scope`, `budget_class`, output schema). Health check: row ↔ handler reconciliation. |
| ~16 live handlers (Recall, DocumentSearch, KnowledgeRetrieval, WorkingDocument, WebSearch, VerifyCitations, workspace-tab, etc.) | ✅ fills | Re-namespace (`document.*`, `search.*`, `session.*`, …); logic unchanged. |
| `dataverse.*` family | 🆕 build | New handlers mirroring GA MCP tool contracts over existing OBO Web API (D10 revised). OBO-for-`mcp.tools` spike parked alongside. |
| Write shapes: `SendEmailNodeExecutor` logic → `email.draft`; `OutputOrchestratorService` → the `dataverse` write service (moves OUT of FinanceModule); SPE versioning → `document.write` | 🔧 fills | Node executors and tools converge on the same write services + same S6 gate (canonical §5.5). |
| `RagService` (session branch), indexing jobs | ✅ fills | `search.*` backing; three index-writers consolidate opportunistically (inventory O-24, low priority). |
| Legacy `Chat/Tools/*` | **DEL** | After the two live stragglers (`AnalysisExecutionTools`, `TextRefinementTools`) migrate to handlers. |
| `RecordMatchingAi` facade | ✅ fills | A read tool, gated as today. |

### S6 · Confirmation Gate

| Candidate | Verdict | Detail |
|---|---|---|
| `PendingPlanManager` (+Redis store, Null peer) | 🔧 **fills** | Generalizes to THE single pending-action store (D12). |
| `/actions/{id}/confirm` HITL path + its store | **DEL** | Merged into the one store. |
| FR-48 must-click `playbook_options` flow + `PlaybookOptionsEventBuilder` | 🔧 fills | Becomes a *presentation* of the gate (candidate options → confirm chips), no longer a dispatch mechanism. |
| `PlanPreviewCard` + plan_preview SSE (client) | ✅ fills | Gate UX, unchanged. |
| Action Engine gate taxonomy (5 types, timeout, resolver plurality — planned, unbuilt) | 🔧 absorb as policy vocabulary | Enriches gate policies (`explicit-click`, `conversational-confirm-suffices`, `always-modal`, timeout). No `sprk_gate_approval` entity — gate state is ledger entries. |
| D1 confidence dial | policy note | Calibrated thresholds survive ONLY where a real confidence exists: the Event-path classify step (Layer 0) gates on classifier confidence per event-rule policy. Text-path gating is risk-class + ask-when-uncertain (greenfield §9 Q1). |

### S7 · Output Router

| Candidate | Verdict | Detail |
|---|---|---|
| `PlaybookOutputHandler` (7 output types) | 🔧 fills | Output-type vocabulary folds into the disposition vocabulary. |
| `ChatSseEventFactory` + typed SSE events + `R2SseEventEmitter` + `SseOutputGuard` | ✅ fills | Existing event families carry the routed output; no new event types needed. |
| Export/delivery services (Docx/Pdf/Email templates, `TemplateEngine`) | ✅ fills | email/record disposition implementations. |
| Deliver*/ReturnResponse node executors | **TL** | Frozen with the engine (Insights composites only). |
| r7 `linear_dispatch` event + factory method + client case | **DEL** | Never merges (confirmed, Appendix A). |

### S8 · Event Rules (Event path / Layer 0)

| Candidate | Verdict | Detail |
|---|---|---|
| Event Rules service | 🆕 **build (thin)** | Nothing exists (audit: Layer 0 absent). Reads `on_event` Binding rows; enforces bounds (cost cap, opt-out, bulk top-1, explicit-command supersede). |
| Upload emission point | ✅ exists | End of `ChatDocumentEndpoints` upload handling — wiring, not new infrastructure. Classify confidence fields already on `ChatSessionFile` (chat-routing-redesign-r1) — Layer 0 is partly wiring. |
| `PlaybookSchedulerJob`, `IncomingCommunicationJobHandler`, Office save-flow flags | 🔧 fills | Become event-path *clients* invoking catalog Bindings (greenfield §4.6: jobs invoke capabilities). |

### S9 · Telemetry / cost meter

`AgentCostControlMiddleware`, `IPromptBudgetTracker`, App Insights wiring, `Sprk.Bff.Api.InsightWidgets` meter — ✅ fill. Extension: per-user daily budget consumed by Event-path auto-composites. `dispatch_refused` telemetry event added (L4 backlog signal).

---

## Client slots

### C1 · Chat control
`SprkChat` + canonical `useSseStream` + input/citations/suggestions/upload/plan-card — ✅ fills. Trims: chips carry Binding ids (D4); duplicated hook triples consolidate (AI.Context copies → re-exports, the AIPU2-082 precedent). `SprkChatExportWord`, `SprkChatBridge` — **DEL**.

### C2 · Event bus
`PaneEventBus` + provider + types — ✅ fills as-is. AI.Outputs `cross-pane/` — **DEL**. (Third historical mechanism `SprkChatBridge` deleted under C1.)

### C3 · Widget registry
`WorkspaceWidgetRegistry` + `ContextWidgetRegistry` — ✅ fill. Dedupe the two `register-context-widgets.ts`. AI.Outputs R1 `output-registry`/`source-registry` — **DEL**.

### C4 · Streaming + specialized widgets
`StructuredOutputStreamWidget` — ✅ fills (THE schema-driven renderer). Specialized widgets (DocumentViewer, Redline, Calendar, `InsightSummaryCard` per umbrella, DataverseEntityView, wizard launchers) — ✅ keep. `ExecutionTraceWidget` — 🔧 gets its bridge (renders the ledger tool-chains — natural fit). Four unregistered AI.Outputs widgets (Chart, DataTable, Timeline, DocumentCompare) — **DEL**.

### C5 · Page shell + dispatch adapter
`ThreePaneShell`/stage machine/`WorkspacePane`/`ContextPaneController` — ✅ fill. `ConversationPane` (2,498 lines) — 🔧 decomposes to thin host + ONE `dispatchConsumer(bindingId, args)` helper (SSE→bus bridging inside it). `CommandRouter`/`HardSlashExecutor` — ✅ fill (deterministic click-path pre-layer). `ReferenceResolver` — ✅ fills (arg-resolution affordance). `intentMatcher`, `executeSummarizeIntent`, `executeLinearDispatch` (r7) — **DEL** after the helper lands. LegalWorkspace `summarizeService` + Compose `executeComposeSummarize` — 🔧 migrate to the shared helper (kills client summarize duplication + hand-rolled SSE parsers, O-20/O-21). Insights renderer cluster (~14 files), `notificationContextLoader`, Pillar-6b trio, `ChatHistoryPanel` — **DEL** (verdicts verified). Dead PCF dirs ×5, `DirectOpenAiAgent` cluster + test, misc §9 register — **DEL** (Track B).

---

## E · Exceptions register (no-widening rule — operator approval required)

| # | Proposed widening | Recommendation |
|---|---|---|
| **E-1** | `ICodedWorkflow` registration convention (tiny platform concept: how a coded composite Action's class is discovered + invoked by the executor) | **Accept** — unavoidable minimum for `kind: coded`; one interface + assembly scan, mirroring the tool-handler discovery pattern. |
| **E-2** | Engine-output→ledger adapter (frozen Insights composites must write `SessionOutput` entries so their results are composable) | **Accept** — the price of the OQ-2 attrition path; one adapter, no ledger contract change. |
| **E-3** | Soft-slash vocabulary kept as deterministic direct-invocation mappings (client) | **Accept** — retained UX (D-13) expressed through the click path; the loop contract is untouched. |
| **E-4** | Per-Binding calibrated `confirmation_threshold` column (D1's dial) on the TEXT path | **Reject** — the loop emits no calibrated confidence; risk classes + ask-when-uncertain + hard gates deliver D1's intent. The dial survives only on the Event-path classify step, where a real confidence exists. (This formally amends D1's mechanism; product behavior preserved.) |
| **E-5** | Keeping `PlaybookDispatcher`'s embedding index warm "just in case" | **Reject** — TL means it runs for legacy paths only; the re-entry point (tool-list pre-filter at ~100+ catalog) is documented, not pre-built. |

## Bucket roll-up

- **Fills (✅/🔧)**: ~45 components — the audited working core (session stack, LinearConsumers executor, agent stack, tool framework + handlers, SSE/event surfaces, PaneEventBus/registries/widgets, shells, catalogs/scopes, jobs-as-invokers).
- **Build fresh (🆕)**: 3.5 — Event Rules service (thin), `dataverse.*` handlers, golden-utterance eval suite, + the S1/S2 model/column extensions.
- **TL (transitional legacy, named triggers)**: engine + 33 executors + graph + playbook/node tables + embedded playbook JSON (trigger: Insights restructure) · PlaybookDispatcher + IntentReranker + CandidateSelector (trigger: last NL chat path migrated to the loop).
- **DEL (Track B)**: the inventory §9 register in full, plus: PlaybookExecutionEngine, SessionSummarizeOrchestrator shell, legacy analysis path, CompoundIntentDetector, one HITL store, legacy Chat/Tools, three routing-config appsettings blocks, r7 linear_dispatch set, client dispatch helpers ×3, client summarize duplicates, R1 client registries/providers/cross-pane, stale catalogs/seeds/docs.

**Resolved by this matrix (pending your review): OQ-1** (PlaybookDispatcher → TL; loop dispatches; pre-filter re-entry documented) **and OQ-3** (no SlotFillEngine; loop-native elicitation + modal escape). With OQ-2/OQ-4 already resolved, **no open architecture questions remain after this review** — D7-D12 stand as amended (D10 revised; D1 mechanism amended per E-4).

## After review
1. Canonical doc §4-7 → **v0.4**: greenfield-as-target with a "fulfilled by" column per slot (one target document; this matrix referenced, not duplicated).
2. Step 3: `SPAARKE-AI-MIGRATION-MAP.md` = this matrix + Track B + **sequencing** (phases, dependencies, cutover triggers) → then §8 roadmap.
