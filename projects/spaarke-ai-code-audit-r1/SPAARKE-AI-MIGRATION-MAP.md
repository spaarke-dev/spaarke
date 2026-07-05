# SPAARKE AI MIGRATION MAP

> **Status**: v1.0 — Step 3 deliverable of `spaarke-ai-code-audit-r1`, 2026-07-05.
> **Target**: canonical doc **v0.4** (converged; all decisions ratified).
> Per-component verdicts: [`OVERLAY-MATRIX.md`](OVERLAY-MATRIX.md) (approved incl. E-1..E-5).
> **Cutover doctrine** (operator 2026-07-05): existing-customer continuity is NOT a
> constraint — **hard cutover per surface**, no parallel-run, no compat shims kept.
> Track-B deletes with no dependency start immediately and run continuously
> ("sweep-as-you-go").
> **Consumption**: this map + canonical v0.4 seed `/design-to-spec` →
> `/project-pipeline` for the implementation project (working name:
> `spaarke-ai-target-architecture-r1`). Phases below are that project's wave
> structure; each phase closes with the named cutover trigger verified.

---

## Phase P0 — Foundations (everything else depends on this)

**Theme**: schema + model groundwork; zero user-visible change.

| # | Work item | Target component | Fulfilled by / action |
|---|---|---|---|
| P0-1 | `ChatSession` model gains ledger entries (`Outputs`, `ToolChains`, `WidgetEvents`, `Gates`) + `SessionOutput` record; Redis+Cosmos payloads carry them; **fix Cosmos file-reference drop** | T-01 | extend `Models/Ai/Chat/ChatSession.cs`, `ChatSessionManager`, `SessionPersistenceService` |
| P0-2 | Digest generalization: compaction covers outputs, not just messages | T-01 | extend `ChatHistoryManager` |
| P0-3 | Catalog schema: `sprk_analysisaction` + `sprk_kind`/`sprk_workflowclass`/`sprk_inputschema`/`sprk_modeltier`; `sprk_playbookconsumer` + the §6.2 columns; `sprk_analysistool` + 8-field contract columns | T-02/T-05 | `dataverse-create-schema` tasks; `ConsumerRoutingService` returns full contract |
| P0-4 | Boot reconciliation: `ConsumerTypes` constants ↔ Binding rows; tool row ↔ handler bijection | T-02/T-05 | extend `RoutingConsumerTypeHealthCheck` |
| P0-5 | Registration hygiene: `PlaybookLookupService` + `OutputOrchestratorService` out of FinanceModule into AI modules + Null peers; LinearConsumers under the compound gate | T-03/T-05 | ADR-032 enforcement (inventory §10 placement smells) |
| P0-6 | `ICodedWorkflow` registration convention (E-1) + retrofit `DailyBriefingNarrator`/`Collector` as the first instances (no behavior change yet) | T-03 | new interface + assembly scan mirroring tool-handler discovery |
| P0-7 | `dataverse.*` typed handlers mirroring GA MCP contracts (`describe`, `read_query`, `search_data`, `create_record`, `update_record`, `delete_record`) over BFF-OBO Web API | T-05 | NEW handlers (D10); freeze names against GA MCP before implementation |
| P0-8 | **OBO spike**: confidential client → delegated `Dynamics CRM/mcp.tools` → `/api/mcp` under a test user's roles | T-05 | decides whether per-tool `/api/mcp` transport stays open (research brief follow-up 1) |
| P0-9 | Golden-utterance eval-suite scaffold (`tests/integration/contract/`) + seed ~30 utterances from §3 UC triggers | quality | ADR-038 KEEP-class; grows every phase |
| P0-10 | Verify all D10-exposed Dataverse flows run user-OBO (any app-only path = real side-channel risk) | T-05 | research brief follow-up 2 |

**Cutover trigger P0 → P1**: schema deployed to `spaarkedev1`; health checks green; ledger round-trips (write → Redis → Cosmos → restore) verified; **ADR-040 → Accepted**.

**Sweep-as-you-go (Track B, no dependencies — start now)**: `DirectOpenAiAgent`+`ISprkAgent` cluster + DTOs + its test · `Services/Ai/Chat/SseEvent.cs` · `ScopeGapDetector` · SpaarkeAi Insights renderer cluster (~14 files + tests) · `notificationContextLoader.ts` · Pillar-6b trio (`SendToWorkspaceButton`/`PinToMatterButton`/`AddToAssistantToggle`) · `ChatHistoryPanel.tsx` (SpaarkeAi wrapper) · `StandaloneAiProvider`+`useStandaloneAi` · AI.Outputs `output-registry`/`source-registry` + 4 unregistered widgets + `cross-pane/` · `SprkChatExportWord` · `SprkChatBridge` · 5 dead PCF dirs (`AIMetadataExtractor`, `AnalysisWorkspace`, `AnalysisBuilder`, `PlaybookBuilderHost`, `DrillThroughWorkspace`) · `DocumentVectorBackfillService` · stale catalogs/seeds (`docs/ai-knowledge/catalogs/` twin, `scripts/seed-data/` R4 taxonomy, `Seed-JpsActions.ps1`) · 2026-02 ERD data-model docs (replacements authored in P4) · `SummarizeInvocationPath.AgentTool` · `PlaybookDispatcher.RunPhaseBManifestPresentAsync` scaffolding · `LinearConsumersOptions.ActionIds` residue · CompoundIntentDetector dead assignment (file itself deletes in P2).

---

## Phase P1 — First capability end-to-end (proves the platform)

**Theme**: chat-summarize becomes the first Action+Binding on the new path; Event + Click paths live.

| # | Work item | Notes |
|---|---|---|
| P1-1 | `chat-summarize` as Action row (`kind: prompted`, SUM-CHAT@v1 schemas) + Binding row (disposition informational, chips, risk none) executed by `ConsumerExecutionService`-thin over `ActionRunner` | dissolves `SessionSummarizeOrchestrator` dual-path; `SummarizeSessionEndpoint` delegates |
| P1-2 | Universal ledger write on execution; Output Router (T-07) routes by disposition; SSE unchanged | ADR-040 in force |
| P1-3 | **Event path** (T-08): thin Event Rules service; `document_uploaded → [classify(1), summarize(2)]` with bounds (cost cap, opt-out, bulk top-1, explicit-command supersede); classify-confidence M4 policy | classify confidence fields already on `ChatSessionFile`; emission point exists in `ChatDocumentEndpoints` |
| P1-4 | **Click path**: chips carry `binding_id` (D4); ONE client `dispatchConsumer(bindingId, args)` helper (SSE→PaneEventBus inside); `executeSummarizeIntent` + `intentMatcher` migrate onto it and DELETE | `CommandRouter`/`HardSlashExecutor` unchanged |
| P1-5 | E-2 engine-output→ledger adapter (frozen Insights composites' outputs become addressable) | small seam, approved |
| P1-6 | **Close the r7 tactical branch WITHOUT merging the dispatch patches**: keep session-id fix, ExtractedText persistence, auto-promote, field_delta synthesis; drop `TryDetectExplicitConsumerType` regex + `linear_dispatch` SSE + `executeLinearDispatch.ts` + the debug log | r7 delta findings; preserve the empty-attachments guard as an Event/Click precondition |
| P1-7 | Eval suite: UC-A-1 utterance family green | |

**Cutover trigger P1 → P2**: UC-A-1 verified e2e (curl + browser UAT) on the new path in `spaarkedev1`; upload auto-composite live; **ADR-039 → Accepted**.

---

## Phase P2 — Text-path hard cutover (the dispatch consolidation)

**Theme**: the bounded agent turn becomes the only probabilistic decider; the dispatcher stack dies the same phase.

| # | Work item | Notes |
|---|---|---|
| P2-1 | T-04 loop contract on the `SprkChatAgent` stack: per-turn tool budget (default 8); capability-tools projection from the catalog; deterministic context pre-filter; cite enforcement on reads; `ToolChain` → ledger | factory slims as legacy dispatch leaves |
| P2-2 | Confirmation Gate unification (D12): `PendingPlanManager` store generalized; `/actions/{id}/confirm` second store DELETED; FR-48 must-click becomes gate presentation; policies incl. Action-Engine taxonomy; gating by `side_effect_class` (rows from P0-3) | |
| P2-3 | Loop-native elicitation (OQ-3): missing required args → clarifying turn; `capture_mode: modal` escape; `Gate` ledger markers | no SlotFillEngine |
| P2-4 | `no_match_handler` refusal Binding per tenant + `dispatch_refused` telemetry | L4 |
| P2-5 | **HARD CUTOVER of chat NL** to the loop; soft slashes → direct invocations (E-3); `intentHint` plumbing retired | |
| P2-6 | **DELETE the dispatcher stack**: `PlaybookDispatcher` (+ embeddings index jobs), `IntentRerankerService`, `PlaybookCandidateSelector`, `CompoundIntentDetector` + their tests-of-dead-code | TL ends; re-entry documented in ADR-039 (pre-filter only) |
| P2-7 | Legacy `Chat/Tools/*` deletion after `AnalysisExecutionTools`/`TextRefinementTools` migrate to handlers; handler ids re-namespaced | |
| P2-8 | Eval suite: full catalog utterance families + refusal + compound cases green; r3's classifier↔dispatcher reconciliation debt closed by construction | |

**Cutover trigger P2 → P3**: zero references to the four deleted mechanisms; eval suite green; chat NL verified in UAT.

---

## Phase P3 — Consumer + client consolidation

**Theme**: every remaining capability becomes an Action+Binding; every surface uses the one client helper.

| # | Work item | Notes |
|---|---|---|
| P3-1 | Bindings for: `document-profile` (appsettings map retires), `matter-pre-fill`, `project-pre-fill` (facades delegate to capability invocation per amended ADR-013), workspace `summarize-file`, `email-analysis` | single-routing-surface rule lands: `LinearConsumers`/`Workspace.*PlaybookId`/`Insights.Playbooks.Map` config DELETED |
| P3-2 | Daily Briefing as the first full `coded` composite Action; `/narrate` engine-default + `Features:NarrateUseCodeBasedNarrator` flag DELETED (Binding decides); Insights ask/search surface as Bindings via `IInsightsAi` | closes R4 graduation gate the R7 way |
| P3-3 | Server engine-shell deletions: `PlaybookExecutionEngine`, `AnalysisOrchestrationService` legacy path (FR-11), `SessionSummarizeOrchestrator` remnants, `FileSummarizeService`/`DocumentProfileService` wrappers absorbed | engine itself stays frozen (Insights only) |
| P3-4 | Client consolidation: `ConversationPane` decomposes to thin host + helper; LegalWorkspace `summarizeService` + Compose `executeComposeSummarize` migrate to the helper (hand-rolled SSE parsers die); duplicated chat-hook triples → re-exports; wizard/launcher widgets carry binding ids | O-16/O-20/O-21/O-22/O-23 close |
| P3-5 | Widget layer: dedupe `register-context-widgets` ×2; `ExecutionTraceWidget` bridge (renders ledger ToolChains); FieldDelta dual-render path DELETED at last-playbook cutover (ADR-037 amendment) | |
| P3-6 | Work-product record persistence generalized from the widgets-r1 pattern (Binding-declared) | |

**Cutover trigger P3 → P4**: every §2.2-inventory consumer runs via catalog; grep-zero for retired config keys; per-surface UAT.

---

## Phase P4 — Sweep completion + hardening + graduation

| # | Work item | Notes |
|---|---|---|
| P4-1 | Track B remainder audit: every inventory-§9 + overlay-DEL item confirmed deleted (grep-verified) or has a written keep-with-reason | the deadwood gate |
| P4-2 | Catalog governance: single `scope-model-index.json` refreshed; `Seed-PlaybookConsumers.ps1` regenerated from table; `sprk_nodetype` option-set gap resolved-or-documented for the frozen engine | |
| P4-3 | Docs: new data-model docs for extended `sprk_analysisaction`/`sprk_playbookconsumer`/`sprk_analysistool` + frozen `sprk_playbooknode`; `docs/data-model/INDEX.md` reconciled; guide refresh (consumer-wiring guide → capability-wiring); ADR A-3 minor refreshes (033/034/010/016/018/038) | doc-discipline: DELETE outdated |
| P4-4 | PlaybookBuilder canvas de-scope → BA scope/prompt/binding editor; `ScopeConfigEditor` Binding variant; `AiPlaybookBuilderService` retargets | OQ-2 |
| P4-5 | BFF publish-size + CVE verification (expect net reduction); ADR-029 baseline update | |
| P4-6 | `/test-diet` + project wrap-up; Action Engine R1 re-based spec filed; audit project graduates | |

---

## Dependency spine (why this order)

`P0-1 ledger` ← everything (P1-2, P2-1, P2-3, P3-6). `P0-3 catalog columns` ←
P1-1, P2-1 projection, P2-2 side-effect gating, P3-1. `P1 first capability` ←
P2 (the loop needs at least one capability tool + the Event/Click paths proven).
`P2 cutover` ← P3 client consolidation (helpers assume loop semantics).
Track-B sweep runs continuously; only engine-adjacent deletes wait for their
phase (e.g. FieldDelta path waits for P3-5).

## Risk register (top 4)

| Risk | Mitigation |
|---|---|
| Loop dispatch accuracy below expectation at launch catalog size | P0-9 eval suite from day one; deterministic context pre-filter; ADR-039's documented pre-filter re-entry if catalog >100 |
| Ledger model change destabilizes existing sessions | P0 ships model+persistence dark (no readers) → P1 turns on writes → readers follow; session TTLs bound the blast radius |
| P2 hard cutover regresses chat UX | eval suite + UAT gate on the trigger; Event/Click paths unaffected by definition |
| Frozen-engine drift (someone lands new capability on it) | ADR-039 MUST NOT + amended ADR-037; adr-check at Step 9.5 |

## What Step 3 hands to the implementation project

Canonical v0.4 (the WHAT) + this map (the WHEN/ORDER) + the overlay matrix
(the per-component HOW) + ADR-039/040 (the guardrails) → `/design-to-spec` →
`/project-pipeline`. Rigor: FULL for every P0-P3 code task (BFF + client hot
paths); hot-path declaration BFF=Y SpaarkeAi=Y.
