# TASK-INDEX — spaarke-ai-architecture-redesign-r1

> **Generated**: 2026-07-05 by `/project-pipeline` · **51 tasks** · Status legend: 🔲 pending · 🔄 in progress / needs retry · ✅ complete · ⛔ blocked
> **Portfolio**: [Project #550](https://github.com/spaarke-dev/spaarke/issues/550) · [Epic #421 SPAARKE AI](https://github.com/spaarke-dev/spaarke/issues/421)
> **Rules**: every task via `task-execute`; max 6 agents/wave; build verification between waves; browser gates (027/038/048/090) are operator UAT — never auto-passed (NFR-11). Wave `/goal` conditions below are the NFR-10 pilot — paste at wave start, `/goal clear` before every gate task.

---

## Phase P0 — Foundations (dark; gate G-P0 = engineering evidence) — ✅ **PHASE COMPLETE 2026-07-05** (G-P0 passed; ADR-040 Accepted; evidence: notes/g-p0-evidence.md)

### Wave W-P0-A (parallel ×6)

> **/goal**: `Tasks 001, 003, 006, 007, 013, 070 in projects/spaarke-ai-architecture-redesign-r1/tasks/ are complete via task-execute: ledger round-trip test passes with output shown; schema columns verified on spaarkedev1 with query output shown; services unresolvable when Analysis:Enabled=false (test output shown); narrator tests green (shown); R7 issue + trigger re-points linked; Track-B batch 1 grep-zero shown. dotnet build green (output shown). code-review + adr-check (Step 9.5) passed for FULL tasks. TASK-INDEX rows flipped ✅. Do not modify files outside these tasks' scopes. Stop after 40 turns.`

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 001 | ChatSession ledger model + persistence + Cosmos file-ref fix (5 new + 815 existing tests green; publish 49.97 MB; defer filed: widget/tab-state clobber) | FR-P0-01 | FULL | — | ✅ |
| 003 | Catalog schema extensions ×3 tables (spaarkedev1) | FR-P0-03 | STANDARD | — | ✅ |
| 006 | Registration hygiene: FinanceModule exits + 3 Null peers + compound gate (12 gating tests; publish −0.2 MB; repaired pre-existing orchestrator test breakage) | FR-P0-05 | FULL | — | ✅ |
| 007 | ICodedWorkflow convention + DailyBriefing retrofit (assembly-scan mirror of tool handlers; 3 convention tests; narrator tests unchanged) | FR-P0-06 | FULL | — | ✅ |
| 013 | Portfolio reconciliation (R7 #501 closed w/ absorption map; R4/ActionEngine/insights-r3 triggers → G-P3/G-P2; re-base stub filed) | FR-P0-11 | MINIMAL | — | ✅ |
| 070 | Track-B batch 1: DirectOpenAiAgent cluster (8 deleted, 2 keep-with-reason: SseEvent.cs live via SseOutputGuard; RunPhaseBManifestPresentAsync live until 035) | FR-TB-01 | STANDARD | — | ✅ |

### Wave W-P0-B (parallel ×6)

> **/goal**: `Tasks 002, 004, 005, 008, 009, 071 complete via task-execute: digest-with-outputs test green (shown); ConsumerRoutingService full-contract test green (shown); startup health check fails on seeded drift (test output shown); all six dataverse.* handler tests green under test-user OBO (shown); handler names frozen against the GA MCP list (cited); Track-B batch 2 grep-zero shown. dotnet build green (shown). Step 9.5 gates passed. TASK-INDEX updated. Scope-bound to these tasks. Stop after 40 turns.`

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 002 | Digest compaction generalized to outputs (keys verbatim in digest; 22 targeted + 768 namespace tests green) | FR-P0-02 | FULL | 001 | ✅ |
| 004 | ConsumerRoutingService full Binding contract (Binding record + 5 enums; action LinkEntity; legacy-tolerant; 15 new tests) | FR-P0-03 | FULL | 003 | ✅ |
| 005 | Boot reconciliation health checks (6 drift classes → Unhealthy; registry-abstraction bijection; 13 tests) | FR-P0-04 | FULL | 003, 004 | ✅ |
| 008 | `dataverse.*` READ handlers (GA-frozen names; user-OBO fail-closed; SQL→OData translator; rows seeded spaarkedev1; 305 tests) | FR-P0-07 | FULL | 003 | ✅ |
| 009 | `dataverse.*` WRITE handlers (GA-frozen; If-Match update-only; injection-hardened mapper; 6↔6 bijection live; 55 tests) | FR-P0-07 | FULL | 003, 008 | ✅ |
| 071 | Track-B batch 2: Insights renderers (23 deleted; AddToAssistantToggle KEPT — live via R6 Pillar 9, inventory stale) | FR-TB-01 | STANDARD | — | ✅ |

### Wave W-P0-C (parallel ×4)

> **/goal**: `Tasks 010, 011, 012, 072 complete: OBO spike result documented in notes/ (pass or fail both acceptable); eval scaffold runs in CI with ~30 utterances (run output shown); per-flow OBO audit note written with evidence per flow; Track-B batch 3 grep-zero shown. Step 9.5 gates passed where applicable. TASK-INDEX updated. Stop after 30 turns.`

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 010 | OBO spike: mcp.tools → /api/mcp (FAIL-with-path: consent grant missing, mechanics proven; native handlers stay runtime path) | FR-P0-08 | MINIMAL | — | ✅ |
| 011 | Golden-utterance eval scaffold (34 cases/14 families, BA-editable JSON; live P0 asserts + routing smoke; CI via existing glob; gate activates at 026) | FR-P0-09 | TEST-MODIFYING | — | ✅ |
| 012 | User-OBO audit (6 new handlers PASS; F-1 CRITICAL: legacy engine → app-only writes, 2 entry points — gate-014 decision required) | FR-P0-10 | STANDARD | 008, 009 | ✅ |
| 072 | Track-B batch 3: R1 registries/provider/widgets/cross-pane deleted (20+ symbols grep-zero; SprkChatBridge KEPT — live in SprkChat) | FR-TB-01 | STANDARD | — | ✅ |

### Gate (serial — `/goal clear` first)

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 014 | P0 deploy + G-P0 evidence + **ADR-040 → Accepted** (deployed spaarke-bff-dev 46.87 MB; /healthz 200 + /healthz/catalog Degraded-by-design; evidence at notes/g-p0-evidence.md; F-1 ruled accept-until-cutover) | G-P0 | STANDARD | 001–012 | ✅ |

## Phase P1 — First capability end-to-end (gate G-P1, BROWSER) — ✅ **PHASE COMPLETE 2026-07-06** (G-P1 two UAT rounds; ADR-039 Accepted; evidence: notes/g-p1-uat-round1-findings.md + round2)

### Wave W-P1-A (parallel ×3)

> **/goal**: `Tasks 020, 025, 073 complete via task-execute: summarize executes via catalog rows (test shown); SessionSummarizeOrchestrator dual-path deleted; r7 branch closed containing exactly the 4 keep-fixes, grep for linear_dispatch and TryDetectExplicitConsumerType returns zero hits outside git history (output shown); Track-B batch 4 grep-zero shown. Build green (shown). Step 9.5 passed. Scope-bound. Stop after 35 turns.`

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 020 | `chat-summarize` catalog capability (SUM-CHAT@v1 live on spaarkedev1; orchestrator 703→405, dual-path grep-zero; FIXED 3 pre-existing contract-test reds; ADR-040 seam marked for 021) | FR-P1-01 | FULL | 014 | ✅ |
| 025 | r7 branch closed (4 keepers inherited by ancestry; 492 lines of linear_dispatch/regex deleted in place, grep-zero; remote branch safe-to-delete — operator/repo-cleanup) | FR-P1-06 | FULL | 013 | ✅ |
| 073 | Track-B batch 4: 2 ERD docs deleted; catalog twin already-gone; seeds/Seed-JpsActions/fallbacks/LoadKnowledge KEPT-with-reason (live) — regen deferred to 050/051; Seed-JpsActions sweep needs main session | FR-TB-01 | STANDARD | — | ✅ |

### Wave W-P1-B (serial)

> **/goal**: `Task 021 complete: every execution writes an addressable SessionOutput keyed {bindingId}@t{n} BEFORE rendering (ordering test output shown); OutputRouter routes informational disposition; build green (shown); Step 9.5 passed. Stop after 25 turns.`

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 021 | Universal ledger write + OutputRouter (store-precedes-render test-proven; {bindingId}@t{n} addressable; loud stubs for P3 dispositions; size-cap enforcement deferred to 047) | FR-P1-02 | FULL | 020 | ✅ |

### Wave W-P1-C (parallel ×3)

> **/goal**: `Tasks 022, 023, 024 complete: upload-with-no-command produces classification + summary + chips (integration evidence shown); chips carry binding_id through ONE dispatchConsumer helper, grep-zero for executeSummarizeIntent and intentMatcher (shown); an insights run writes a ledger SessionOutput (test shown). Builds green (dotnet + npm run build:prod, shown). Step 9.5 passed. Stop after 40 turns.`

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 022 | Event path live (CLS-CHAT@v1 chat-classify created as catalog data; 4 bounds + precondition + M4 gate test-proven; SSE chip contract for 023; publish −1.31 MB) | FR-P1-03 | FULL | 021 | ✅ |
| 023 | Click path COMPLETE incl. 023b server /dispatch endpoint (GUID-resolved, catalog+ledger-first, 13 contract tests) + 022b Event-path client leg (upload→classify+summary+chips wired, 27 tests); ONE SSE loop + ONE chip vocabulary client-wide | FR-P1-04 | FULL | 021 | ✅ |
| 024 | Engine-output→ledger adapter E-2 (attach: InvokePlaybookHandler; interim BindingId=playbookId → re-point at 040; frozen diff empty; NOTE: 044's F-1 deletion must relocate the adapter call) | FR-P1-05 | FULL | 021 | ✅ |

### Wave W-P1-D + Gate

> **/goal (026 only)**: `Task 026 complete: UC-A-1 utterance family green in CI (run output shown); eval failure blocks merge (wiring shown); ADR-039 status Accepted with citation committed. Stop after 20 turns.` — then `/goal clear` before 027.

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 026 | Eval UC-A-1 green (11/11 live routing asserts; NFR-06 schema pinned); eval-gate CI job merge-blocking; **ADR-039 → Accepted both copies** (hot-path ci-workflows flipped N→Y) | FR-P1-07 | TEST-MODIFYING | 020–025 | ✅ |
| 027 | P1 deploy + **G-P1 browser UAT** (2 rounds; round-1: chips/batching/UX-ruling fixed `befcaa5da`; round-2: chip placement + frozen-fileIds + gating fixed `9ee30e672`, both surfaces redeployed; operator-directed close 2026-07-06 — notes/g-p1-uat-round2-findings.md; placement spot-check folds into 038) | G-P1 | STANDARD | all P1 | ✅ |

## Phase P2 — Text-path hard cutover (gate G-P2, BROWSER)

### Wave W-P2-A (parallel ×2)

> **/goal**: `Tasks 030, 031 complete: loop tests prove per-turn budget 8, deterministic pre-filter, citation enforcement on reads, ToolChain ledger persistence (output shown); ONE pending store (grep for the /actions/{id}/confirm store returns zero, shown); gating driven by side_effect_class + risk with hardcoded tool-name lists deleted (grep shown). Build green (shown). Step 9.5 passed. Stop after 40 turns.`

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 030 | Agent-turn loop contract (budget 8 CAS-reserved + BudgetedAIFunction; catalog capability-tools via SessionDispatchOrchestrator; deterministic pre-filter; NFR-04 fingerprint; citation repair block; ToolChain flush-before-render; orphan-handlers → Unhealthy, TemplateHandler deleted; factory −772 lines; 28 new tests; publish 45.59 MB) | FR-P2-01 | FULL | 027 | ✅ |
| 031 | ONE Confirmation Gate (PendingPlanManager → unified PendingInvocation store; gating by sprk_sideeffectclass + Binding risk — tool-name lists deleted grep-zero; SessionGate ledger markers via AppendGateAsync; /actions/{id}/confirm second store DELETED; 13 new tests; publish −1.31 MB; Seed-TypedHandlers re-run on spaarkedev1) | FR-P2-02 | FULL | 027 | ✅ |

### Wave W-P2-B (parallel ×2)

> **/goal**: `Tasks 032, 033 complete: missing-args → clarifying turn; capture_mode modal routes to wizard; Gate ledger markers tracked; mid-elicitation utterances parse as answers (tests shown); off-catalog utterance renders tenant refusal template + dispatch_refused lands in App Insights (evidence shown). Step 9.5 passed. Stop after 35 turns.`

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 032 | Loop-native elicitation (BindingInputSchemaValidator + ElicitationTurnRouter; suspend into ONE pending store, marker-before-render; capture_mode=modal → elicitation_modal SSE; mid-elicitation answers deterministic; unified POST /gates/{gateId}/resolve; 031-W1 throw; 28 new tests; eval +GU-043..046) | FR-P2-03 | FULL | 030, 031 | ✅ |
| 033 | Honest refusal (REF-CHAT@v1 `8d337be2` + no_match_handler Binding `48dcd7ec` on spaarkedev1; RefusalCapabilityTool loop-projected; dispatch_refused AiTelemetry counter; grounded-outcomes directive; 12 new tests; eval +GU-041/042; App Insights live-evidence deferred to gate-038 deploy) | FR-P2-04 | FULL | 030 | ✅ |

### Wave W-P2-C (serial)

> **/goal**: `Task 034 complete: no chat utterance reaches any legacy dispatcher (telemetry or test evidence shown); four soft slashes invoke deterministically; grep-zero for intentHint (shown). AgentContentSafetyMiddleware verified on the loop path (evidence shown). Build green. Step 9.5 passed. Stop after 30 turns.`

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 034 | **HARD CUTOVER** chat NL → loop (3 pre-passes deleted from SendMessageAsync; intentHint retired grep-zero end-to-end incl. SoftSlashRouter; content-safety-on-loop evidenced; eval 12/12; publish ±0; 🔔 soft-slash determinism PARTIAL — only /summarize has a Binding; recommended P3 FR-P3-06, operator rules at 038; dead click endpoints left for 035/036) | FR-P2-05 | FULL | 030–033 | ✅ |

### Wave W-P2-D (parallel ×2)

> **/goal**: `Tasks 035, 036 complete: grep for PlaybookDispatcher, IntentRerankerService, PlaybookCandidateSelector, CompoundIntentDetector returns zero hits outside git history (output shown); Chat/Tools directory removed; handlers cover the two migrated capabilities (tests shown); dotnet build + full test suite green (shown); publish-size delta reported. Step 9.5 passed. Stop after 30 turns.`

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 035 | DELETE dispatcher stack (4 services + whole PlaybookEmbedding subsystem + dead plan/execute endpoints ~814 lines; 27 symbols grep-zero; PhaseB flake dies with subject; gate-resolve endpoint intact; frozen engine untouched; live embeddings index → P4 sweep) | FR-P2-06 | FULL | 034 | ✅ |
| 036 | DELETE legacy Chat/Tools (11 classes + PlaybookOutputHandler grep-zero) after migrating rerun/refine → AnalysisExecutionHandler (2 new sprk_analysistool rows `2b09dfb5`/`55521abc`; fixed latent refine null-analysisId bug; text.* re-namespaced; 11-toolid bijection holds; F-1 legs KEPT for 044; 🔔 analysis.rerun now confirmation-gated — UX change visible at 038) | FR-P2-07 | FULL | 034 | ✅ |

### Wave W-P2-E + Gate

> **/goal (037 only)**: `Task 037 complete: eval suite green incl. refusal, compound, and hostile-document injection cases with no ungated side effects (CI output shown). Stop after 25 turns.` — then `/goal clear` before 038.

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 037 | Eval expanded to 55 cases / 20 families (+full-catalog, compound, 5 injection); FOUND+FIXED real defect: loop-invoked write tools ran UNGATED post-cutover → new SideEffectGateAIFunction (declared-class wrap, fail-closed, suspend-into-gate, marker-before-render); eval 29/29; P3 seam: typed-handler confirm-resume (422 today) → FR-P3-03 | FR-P2-08 | TEST-MODIFYING | 034–036 | ✅ |
| 038 | P2 deploy + **G-P2 browser UAT** (operator round-1 2026-07-06: 7 findings — 2 PASS incl. dark mode + FR-08 re-target; 5 → fix wave in flight; rulings: soft-slash→P3 FR-P3-06, analysis.rerun ungated-by-declaration; operator-directed close "continue with P3"; residual verification folds into G-P3 048; notes/g-p2-uat-round1-findings.md) | G-P2 | STANDARD | all P2 | ✅ |

## Phase P3 — Consumer + client consolidation (gate G-P3, BROWSER)

### Wave W-P3-A (parallel ×4)

> **/goal**: `Tasks 040-043 complete: all listed consumer routes resolve via the Binding table with grep-zero for LinearConsumers, Workspace.*PlaybookId, Insights.Playbooks.Map config keys (shown); draft-correspondence produces a Spaarke communication draft record gated as communicate (test shown, DRAFT-only); create-task writes sprk_event(type=task) with ledger refs under the confirm gate (test shown); briefing renders + emails via the coded path with the narrator flag grep-zero (shown). Builds green. Step 9.5 passed. Stop after 45 turns.`

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 040 | ALL remaining consumers → Bindings (8 consumers re-pointed; LinearConsumers/WorkspaceOptions/Insights:Playbooks config surfaces DELETED grep-zero; E-2 adapter reverse-resolves real Binding ids via GetBindingByPlaybookIdAsync; insights rows seeded `f32a7931`/`f82a7931`/`f89fa738`; W-1 live App-Service token key → deterministic ActionRunner ceiling 4000; universal-ingest NOT seeded — playbook absent, honest error; dead App-Service keys → operator hygiene) | FR-P3-01 | FULL | 038 | ✅ |
| 041 | **`draft-correspondence`** shipped as catalog data + EmailDraftToolHandler (`email.draft` Communicate→gated; DRAFT-only server-pinned under hostile args; zero Graph; user-OBO; rows DRAFT-CORR@v1 `4b8b50f4`, Binding `f7dc4a00`, tool `bc11e90d`; eval 57 cases; confirm-resume 422 until 042) | FR-P3-02 | FULL | 038 | ✅ |
| 042 | **`create-task`** live end-to-end (CREATE-TASK@v1 `b66c8dda` + Binding `3d9724e5`; required-args elicitation free via FR-P2-03; writes sprk_event(type=task) + ledger refs via existing dataverse.create_record) + **typed-handler confirm-RESUME** (TypedHandlerResumeExecutor: confirm executes under user OBO, ledger loop@t{n} before render, gate `confirmed`; activates dataverse writes + 041 email.draft; injection suspensions still green; 🔔 sprk_event-vs-sprk_todo ruling → gate 048, catalog-data-only to change) | FR-P3-03 | FULL | 038 | ✅ |
| 043 | Daily Briefing = FIRST coded composite (DailyBriefingCompositeService via ResolveBindingAsync + ICodedWorkflowRegistry; OutputRouter EMAIL disposition leg implemented store-precedes-send; /narrate engine default + NarrateUseCodeBasedNarrator flag DELETED grep-zero; rows DAILY-BRIEFING@v1 `2fa8ab19` + email Binding `800cc81f`; DAILY-BRIEFING-NARRATE playbook orphaned → Track-B; live email = gate 048; chat "prepare a briefing" refuses cleanly until loop coded-exec seam) | FR-P3-04 | FULL | 038 | ✅ |

### Wave W-P3-B (serial)

> **/goal**: `Task 044 complete: grep-zero for PlaybookExecutionEngine, SessionSummarizeOrchestrator, FileSummarizeService, DocumentProfileService wrappers outside git history (shown); frozen PlaybookOrchestrationService + nodes untouched (diff scope shown); callers re-pointed; build + tests green. Step 9.5 passed. Stop after 30 turns.`

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 044 | Engine shells + F-1 legs DELETED (−11,849 lines: PlaybookExecutionEngine, SessionSummarizeOrchestrator→/summarize on THE dispatch seam, FileSummarize/DocumentProfile wrappers absorbed incl. engine-fallback deletion, InvokePlaybook/AnalysisQuery/WorkingDocument handlers + facade triangle grep-zero, 5 tool rows deactivated; E-2 adapter relocated to AnalysisExecutionHandler store-before-render; frozen engine diff-EMPTY; publish 46.81 MB net-reduction; 🔔 residual F-1: analysis.rerun app-only engine leg, ungated per operator ruling — accept-with-note recommended, FR-P4-01 re-verifies) | FR-P3-05 | FULL | 040, 043 | ✅ |

### Wave W-P3-C (parallel ×2)

> **/goal**: `Tasks 045, 047 complete: exactly one SSE parse path client-wide (grep for hand-rolled parsers zero, shown); ConversationPane host ≤300 lines with line count shown (or an operator-approved exception on record — escalate before exceeding); a work_product capability persists its envelope to the host record (test shown). npm run build:prod green per package (shown). Step 9.5 passed. Stop after 40 turns.`

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 045 | Client consolidation: ConversationPane 3,172→**300 lines** (11 modules; batch-state extraction landed); ONE SSE path client-wide grep-zero (1 keep-with-reason: office-addins SseClient — no @spaarke dep, richer SSE semantics); LW SummarizeFiles cluster deleted; Compose already-migrated verified; net −5,300 lines; slash launchers → needs server capability-discovery endpoint (documented, POML silent); wizard→dispatchConsumer needs server contract ruling | FR-P3-06 | FULL | 038 | ✅ |
| 047 | work_product disposition leg live (TopicRegistryWorkProductPersister: Binding disposition → sprk_aitopicregistry → host record, user-OBO If-Match, store-precedes-persist proven; matter-summary Binding `05618e5d` + registry `cfca6a65` + new sprk_matter.sprk_mattersummary column; 🔔 ADR-040 size-cap NOT in POML — needs home ruling at 048: P4 or Track B) | FR-P3-08 | FULL | 038 | ✅ |

### Wave W-P3-D + Gate

> **/goal (046 only)**: `Task 046 complete: one register-context-widgets module (grep shown); ExecutionTraceWidget renders a real ToolChain from the ledger (UI-test evidence); FieldDelta grep-zero in the widget layer (shown). Build green. Step 9.5 passed. Stop after 25 turns.` — then `/goal clear` before 048.

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 046 | Widget layer: ONE register-context-widgets module (14 widgets, dup deleted); ExecutionTraceWidget renders REAL ledger ToolChains (tool_chain context_event emitted AFTER AppendToolChainAsync — no new endpoint; NFR-07 identifiers-only wire); FieldDelta dual-render DELETED grep-zero (widget layer + bus discriminant + dispatchConsumer delta case; section_started/completed pairs replace it; SectionRenderer shape-typed); server AnalysisChunk.FieldDelta + legacy trace emitters + playbook_options leg → Track-B/050 candidates; publish 45.47 MB (−0.18) | FR-P3-07 | FULL | 045 | ✅ |
| 048 | P3 deploy + **G-P3 browser UAT** ✅ CLOSED by operator 2026-07-07 after SIX rounds (notes/g-p3-uat-round1..4-findings.md): 16+ findings all fixed+deployed (schema outage→projection validator; fabrication→honesty directives+structural outcomes; confirm-resume live w/ record links; Compose layout open+pre-seed; translator _value rewrite; date context; trace replay; sprk_document hard-block). Refusal-with-affordance-links noted → r2. Rulings: tasks stay sprk_event (revisit r2); rerun ungated accept-with-note; size-cap→055; SseClient keep. **G-M maker gate DEFERRED by operator ruling** (BA editor shipped w/ jest evidence; live maker walkthrough post-r2) — 090 records the graduation amendment | G-P3 | STANDARD | all P3 | ✅ |

## Phase P4 — Sweep completion + hardening + graduation (gates G-P4 + G-M)

### Wave W-P4-A (parallel ×4)

> **/goal**: `Tasks 050, 051, 053, 054 complete: Track-B audit table lists every inventory-§9 + overlay-DEL item as grep-verified-deleted (output shown) or keep-with-reason; one catalog copy with round-tripping seed (shown); a BA can author Action + Binding end-to-end in the UI (UI-test evidence); KQL pack returns per-tenant rollups in dev (query output shown). Step 9.5 passed for FULL tasks. Stop after 45 turns.`

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 050 | Track-B completion audit ✅ 2026-07-07: `notes/track-b-completion-audit.md` — 62 rows, ZERO unexplained survivors (G-P4 MET): 44 grep-verified deleted, 15 Dataverse rows retired, 9 keep-with-reason, 5 operator-decision (§11 O-1..O-5). Also EXECUTED 053's deferred server leg: 42 files deleted (AiPlaybookBuilderEndpoints/Service, Builder/+Testing/ dirs, builder-scopes/ ×9 JSON, orphaned BuilderAdminApiKey auth scheme+policies — reviewer-flagged) + FieldDelta model + useEntityResolver orphans. Build GREEN, suite 4F/7444P (all-KNOWN), eval 35/35, publish 45.25 MB (−0.22). Main session applied the 3 `.claude` fixes (jps-action-create Seed-JpsActions pointer, bff-extensions + jps-validate ×3 executor-config-schemas endpoint refs → source-code schemas) | FR-P4-01 | STANDARD | 048, 070–073 | ✅ |
| 051 | Catalog governance ✅ (dispatched early per operator overlap ruling 2026-07-07): ONE scope-model-index regenerated with live GUIDs (60 Actions/31 Skills/31 Knowledge/40 Tools); Seed-PlaybookConsumers regenerated data-driven (18-row mirror, Seed/-Export/-DiffOnly, ROUND-TRIP CLEAN shown); Refresh-ScopeModelIndex FIXED (sprk_externalid→sprk_knowledgecode drift); Seed-JpsActions RETIRED (closes 073 deferral); EMAIL-DRAFT map entry added; sprk_nodetype gap OBSOLETE (schema evolved to sprk_executortype incl. DeliverComposite); 2 env writes (environment null→'*' pre-fill rows); TL-004/006/008/010 + KNW dupes → 050/operator | FR-P4-02 | STANDARD | 048 | ✅ |
| 053 | PlaybookBuilder de-scope → BA catalog editor ✅ (dispatched early per operator overlap ruling): canvas/graph estate DELETED −24,942 lines grep-zero (xyflow/lexical/zustand deps pruned); NEW Actions+Bindings authoring tabs with client twin of OpenAiFunctionSchemaValidator — the outage schema is UNAUTHORABLE; chipTransitions + onEventBindings structured editors; direct Dataverse Web API saves (decision-criteria cited); NFR-06 eval reminder on save; ScopeConfigEditor PCF v1.3.0 Binding variant; jest 103/103 + 53/53; server leg (AiPlaybookBuilderService + dead graph endpoints) EXECUTED by task 050 2026-07-07 (42 files deleted, grep-zero shown); validator triple-twin hoist → /defer at 090 | FR-P4-04 | FULL | 048 | ✅ |
| 054 | Per-tenant metering counters + KQL pack ✅ 2026-07-08: meter `Sprk.Bff.Api.Ai` — turns (tool-budget spent/cap/denied), tool_calls, tokens (loop/executor × entry.path × model), capability_invocations (entry.path/ucid/outcome + event budget.cap); AiMeteringContext AsyncLocal scope at all 4 entry seams; 2 latent gaps fixed (EventRules meter never exported since 022; AiTelemetry → unconditional per §F.1); KQL pack scripts/kql/ai-metering/ (7 files) with LIVE dev rollup evidence (notes/task-054-metering-evidence.md); 12 meter-boundary tests; Step 9.5 code-review + adr-check PASSED (no violations); suite 7444P/4F all-KNOWN; eval 35/35; publish 48.33 MB | FR-P4-05 | FULL | 048 | ✅ |

### Wave W-P4-B (parallel ×2)

> **/goal**: `Tasks 052, 055 complete: 2026-02 ERD docs deleted + replaced; doc-drift-audit clean (output shown); publish size + diff reported with no new HIGH CVEs (dotnet list output shown); ADR-029 baseline updated. Stop after 25 turns.`

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 052 | Documentation refresh ✅ (dispatched early per operator overlap ruling): AI-ARCHITECTURE + canonical doc → v0.5 SHIPPED with as-built deltas; workspace arch §2.3/2.6/3.6 (assistant-drivable layouts, tab persistence, visible-to-assistant); ERD replacement — 3 NEW + 1 refreshed data-model docs live-schema-verified, INDEX reconciled zero dead links; consumer-wiring guide REWRITTEN as capability-wiring; auth runbook +SPA redirect URIs (AADSTS50011); doc-drift-audit CLEAN (7 High + 6 Medium all fixed); ADR A-3 minor refreshes deferred → 090 ADR-verification step; stale ChatEndpoints resume-seam comment → next code commit | FR-P4-03 | MINIMAL | 050, 051 | ✅ |
| 055 | Publish-size + CVE verification; ADR-029 baseline ✅ 2026-07-08: canonical baseline **49.63 MB incl. PDBs** (45.87 excl.; ≤60 ceiling OK, headroom 5.37) — 050/054 discrepancy reconciled as PDB convention; ⚠️ NFR-01 "net reduction" NOT met in absolute terms (+3.98 vs 2026-05-26, of which +1.22 master drift; zero NuGet adds; levers: PDB exclusion −3.76 or Graph SDK 6.x) → operator judgment at 090; CVE scan: sole finding = pre-existing accepted-risk Kiota HIGH, NO new; ADR-029 baseline table + guard-drift note (Deploy-BffApi.ps1 warns@100 MB, 50 MB hard-fail never existed); **ADR-040 size-cap ENFORCE** per operator ruling: `SessionLedger.CapInlinePayload` 128 KB + truncation marker at both write seams (OutputRouter + TypedHandlerResumeExecutor), disposition legs fail loud, 3 new boundary tests 16/16; suite 7447P/4F all-KNOWN; .claude twins (ADR-029/040, azure-deployment, root CLAUDE §10) applied by main session + CHANGELOG | FR-P4-06 | STANDARD | 050–054 | ✅ |

### Wrap-up (serial — `/goal clear` first)

| # | Task | FR | Rigor | Deps | Status |
|---|---|---|---|---|---|
| 090 | Wrap-up: **G-M maker gate** + `/test-diet` + `/defer` filings + graduation | FR-P4-07 | STANDARD | all | 🔲 |

---

## Dependency graph / critical path

Critical path: 001 → 002 → 020 → 021 → 022 → 026 → **027** → 030 → 032 → 034 → 035 → 037 → **038** → 040 → 044 → **048** → 050 → 055 → **090**.
Track-B batches (070–073) ride waves W-P0-A..W-P1-A; they are dependency-free (deadwood by definition).
Task 013 runs earliest possible — it unblocks 025 (r7 branch close).

## Parallel Execution Groups (summary)

| Group | Tasks | Prerequisite | Max agents |
|---|---|---|---|
| W-P0-A | 001, 003, 006, 007, 013, 070 | — | 6 |
| W-P0-B | 002, 004, 005, 008, 009, 071 | W-P0-A | 6 |
| W-P0-C | 010, 011, 012, 072 | W-P0-B | 4 |
| Gate | 014 | W-P0-C | serial |
| W-P1-A | 020, 025, 073 | 014 | 3 |
| W-P1-B | 021 | 020 | serial |
| W-P1-C | 022, 023, 024 | 021 | 3 |
| W-P1-D | 026 → Gate 027 | W-P1-C | serial |
| W-P2-A | 030, 031 | 027 | 2 |
| W-P2-B | 032, 033 | W-P2-A | 2 |
| W-P2-C | 034 | W-P2-B | serial |
| W-P2-D | 035, 036 | 034 | 2 |
| W-P2-E | 037 → Gate 038 | W-P2-D | serial |
| W-P3-A | 040, 041, 042, 043 | 038 | 4 |
| W-P3-B | 044 | 040, 043 | serial |
| W-P3-C | 045, 047 | 038 | 2 |
| W-P3-D | 046 → Gate 048 | 045 | serial |
| W-P4-A | 050, 051, 053, 054 | 048 | 4 |
| W-P4-B | 052, 055 | W-P4-A | 2 |
| Wrap-up | 090 | all | serial |

## High-risk items

- **034 (hard cutover)** — G-P2 UAT is the safety net; Event/Click paths structurally unaffected.
- **001 (ledger model)** — ships dark (zero readers at P0); session TTLs bound blast radius.
- **025 (r7 close)** — surgical keep/drop list; coordinate with `spaarke-ai-platform-unification-r7` worktree before deleting its branch.
- **044 (engine-shell deletions)** — frozen engine MUST remain untouched; diff-scope proof required.
- **053 (PlaybookBuilder de-scope)** — G-M maker gate depends on it; schedule early in W-P4-A.
