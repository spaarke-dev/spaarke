# TASK-INDEX — spaarke-ai-architecture-redesign-r2 (Core)

> **Generated**: 2026-07-08 by `/project-pipeline` Step 3
> **Spec**: [../spec.md](../spec.md) · **Plan**: [../plan.md](../plan.md)
> **Total tasks**: 53 · **Status legend**: 🔲 not-started · 🔄 in-progress · ✅ complete · ⛔ blocked
> **Execution**: every task runs via `task-execute`. Tiering per CLAUDE.md §8.5 (dispatched by project-pipeline Step 5). **STOP-for-review gate: no task executes until the operator approves this plan.**

---

## Task Registry

| # | Task | Status | Tier | Parallel-safe | Depends on | Gate |
|---|---|---|---|---|---|---|
| 001 | r1 P4-close reconciliation (§10 rows 4/5/6/8/12) ✅ 2026-07-08: row 4=in-scope-FR (r1 did NOT close — O-2; escalated per trigger; unblocks FR-D-06/075), row 5=verified-closed (r1 task 055 shipped `SessionLedger.CapInlinePayload`; unblocks FR-B-15/064 as no-op), row 6=accept-as-ruled (`sprk_event`, live-verified), row 8=accept-as-ruled (SseClient keep, grep-verified), row 12=in-scope-FR (DAILY-BRIEFING-NARRATE closed; `spaarke-playbook-embeddings` index residual unblocks FR-D-07/076). Note: `notes/r1-p4-reconciliation.md` | ✅ | sonnet | ✅ | — | Phase 0 |
| 002 | Measure-first prompt-assembly baseline ✅ 2026-07-08: measured (not source-only) via a temporary harness driving the REAL seam — `PlaybookChatContextProvider.GetContextAsync` + `SprkChatAgentFactory` static directives + `ChatHistoryManager.BuildLedgerOutputsContext` (harness deleted after capture, zero residual diff). Found the POML's stated anchor (`OrchestratorPromptBuilder.cs`) is DEAD CODE — no production call site; corrected to the real FR-27 seam. Escalated 3 findings to task 054: Environment measured 111 vs. ≤50 estimate (exceeds on every turn); Business measured 1,118 vs. ≤1,200 (near/at ceiling, two unconditional directives untracked by the shared budget tracker); Conversation structurally unbounded to ~8,000 via `BuildLedgerOutputsContext`'s ledger-window caps (outside `IPromptBudgetTracker` entirely). See `notes/prompt-assembly-baseline.md`; spec.md FR-B-05 annotated. | ✅ | sonnet·xhigh | ✅ | — | Phase 0 |
| 003 | Business-slice determinism check ✅ 2026-07-08: CONFIRMED DETERMINISTIC — Business slice stays in the ContextEnvelope stable prefix (NFR-04 not re-scoped). Verified host-identity block (PlaybookChatContextProvider.cs:582) + hand-mirrored write-contract description (DataverseCreateRecordHandler.cs:111) on the existing assembly path (ADR-040); pinning test + 3 negative controls added (`tests/integration/contract/Api/Ai/BusinessSliceDeterminismContractTests.cs`, 6/6 passed). No production code changed. Note: `notes/business-slice-determinism.md` | ✅ | sonnet | ✅ | — | Phase 0 |
| 004 | Discovery obligations confirm | ✅ | sonnet | ✅ | — | Phase 0 |
| 010 | Contract: ComposeDisposition v1 (publish FIRST) ✅ 2026-07-08 (7/7 tests; unblocks Compose FR-04/16/17) | ✅ | opus | ✅ | — | A0 |
| 011 | Contract: OutcomeCard v1 ✅ 2026-07-08 (10/10; FR-05/28) | ✅ | opus | ✅ | — | A0 |
| 012 | Contract: GateDecision v2 ✅ 2026-07-08 (34/34; +association picker; **engine 032 ✅ 2026-07-09**; FR-05/28) | ✅ | opus | ✅ | — | A0 |
| 013 | Contract: TraceEvent v1 ✅ 2026-07-08 (7/7; view 038 pending; FR-32) | ✅ | opus | ✅ | — | A0 |
| 014 | Contract: JobAwareCompletionState v1 ✅ 2026-07-08 (22/22; consumer-declared steps; FR-05/28) | ✅ | opus | ✅ | — | A0 |
| 015 | Contract: ContextEnvelope v1 ✅ 2026-07-08 (12/12; budgets provisional→task 054) | ✅ | opus | ✅ | — | A0 |
| 016 | Contract: MemoryItem v1 (2026-07-08: 10/10; Record+User scope, provenance envelope; FR-30) | ✅ | opus | ✅ | — | A0 |
| 017 | Seam-publication ordering + cross-project obligation | 🔲 | sonnet | ❌ | 010–016 | A0 |
| 020 | **Triple-twin validator hoist** (2026-07-08: Model 1 GitOps + Option C; JSON=source, live=managed mirror, code parity via KEEP-path test + health-check dimension; 23 handlers reconciled; eval 35/35; unblocks catalog tasks + Compose FR-12) | ✅ | opus·xhigh | ❌ | — | A-infra |
| 021 | Test-repair (2026-07-08: found 16 SpaarkeAi + 10 AI.Widgets suites red, not ~3+8; fixed all — SpaarkeAi 378/378, AI.Widgets 638/638; found+fixed a real prod bug in EntityInfoWidget date rendering) | ✅ | sonnet·xhigh | ✅ | — | A-infra |
| 030 | D-F0 Resourcefulness Doctrine (2026-07-08: ResourcefulnessDoctrineDirective on existing suffix site; pin audit 0-fold/11-keep — adds read-freedom+ladder+affordance, no gate/block weakened; eval 35/35) | ✅ | opus | ❌ | — | G-R2-A |
| 031 | D-F0(e) resourcefulness eval family (2026-07-08: 23 cases across 5 families + mechanical fabrication oracle vs ledger; wired into eval-gate; 49/49 green) | ✅ | opus | ❌ | 030 | G-R2-A |
| 032 | Confirmation Policy v2 gate engine ✅ 2026-07-09 (Services/Ai/Chat/Gate/: RiskTierResolver fail-closed floor + RequestOriginClassifier + ConfirmationPolicyEngine PRODUCER; all 7 tiers + E-1..E-6 w/ +/- tests; reuses task-012 GateDecisionProjector; risk=catalog DATA in sprk_configuration; 57 new + 138/138 gate suite green; code-review+adr-check clean; FR-05/28 producer-side unblocked; live gate-call-site origin wiring→034/042) | ✅ | opus | ❌ | **020** | G-R2-A |
| 033 | Origin-classification eval family ✅ 2026-07-09 (12 cases, +/- per E-1..E-6; oracle calls REAL merged RequestOriginClassifier + ConfirmationPolicyEngine, hard-equality on (origin,outcome,overlay); perturbation-verified; 57/57 eval-gate green) | ✅ | sonnet | ❌ | 032 | G-R2-A |
| 034 | Gate pre-suspend validation ✅ 2026-07-09 (FR-A1-05: SideEffectGateAIFunction runs handler ValidateChat BEFORE suspend; doomed call → honest ❌ w/ D-F0 affordance link, no pointless confirm dialog; store-before-render validation-failed marker; PASS/faulting falls through to unchanged suspend floor; +ValidateForGate adapter hook; 7825/0. NOTE: this is pre-suspend validation, NOT ConfirmationPolicyEngine wiring — engine stays dark/0-call-sites, see current-task open item) | ✅ | opus | ❌ | 032 | G-R2-A |
| 035 | Completion Engine + OutcomeCard (all paths) ✅ 2026-07-09 (CompletionEngine.cs static composer yields OutcomeCard on every side-effect path; key finding: OutputRouter.RouteAsync is the SINGLE universal disposition surface — covers auto+event at one choke-point; gated path composes at TypedHandlerResumeExecutor; async paths consume 036 JobAwareOutcomeProjection; BuildGateOutcomeMessage preserved as reload-durable transcript, client live-view link upgraded to OutcomeCard component; store-before-render throughout; ChatEndpoints touched 2 additive spots only; server+client suites green. DEF-001 partial win: refusal-CAPABILITY path now structured; pre-suspend hard-block stays markdown/#591 open) | ✅ | opus | ❌ | 011 | G-R2-A |
| 036 | Job-aware completion (ingestion-parity) ✅ 2026-07-09 (FR-A1-07: JobAwareOutcomeProjection PublicContracts bridge 014→011, pure/dependency-free; ONLY path to Succeeded = fully-Completed job aggregate so a doc-create card can't render done while indexing queued — NFR-12 structural; 28 tests; store-before-render preserved, no new store/cache. 035 consumes ForJobAwareOutcome) | ✅ | opus | ❌ | 014 | G-R2-A |
| 037 | UI-action truthfulness (client-ack) ✅ 2026-07-09 (IUiActionAckCoordinator PublicContracts facade + TimeProvider timeout; server emits frameId on workspace_open_tab → client acks after tab materializes → SendWorkspaceArtifactHandler awaits ack, honest Timeout on 8s; 12/12 contract + full Ai suite green; FR-34. ⚠️ ack endpoint has no session-ownership check — GUID capability-token; hardening deferred) | ✅ | sonnet | ✅ | — | G-R2-A |
| 038 | Traceability view + narration + server read surface ✅ 2026-07-09 (ISessionTraceReader facade + SessionTraceReader/NullSessionTraceReader; GET /sessions/{id}/trace read endpoint; SessionContextFingerprint ledger entry ids/counts-only NFR-07; host-embeddable ExecutionTraceWidget rehydrates from durable ledger on mount; narration structurally honest — narrateTrace takes only real TraceEventDto[], negative-test-pinned; consumes TraceEvent v1 013; 7/7+37/37 server, 33/33+8 client. PUBLISHES TraceEvent view seam→Compose FR-32. NOTE: fingerprint WRITE seam AppendContextFingerprintAsync lands dark — writer = task 053 Binder) | ✅ | opus | ❌ | 013 | G-R2-A |
| 039 | Progressive render ✅ 2026-07-09 (root cause: dispatchConsumer published all sections in one synchronous loop→one paint; fix=paced client-side section reveal ~120ms on EXISTING task-046 section-keyed contract, no new SSE vocab; +ProgressiveRenderGuard server-side ADR-040 store-before-render assertion; 22/22 BFF + 32/32 client) | ✅ | sonnet | ✅ | — | G-R2-A |
| 040 | Refusal-affordance links (Document Upload deep-link) ✅ 2026-07-09 (HandoffUrlBuilder deep-link on R5-E sprk_document hard-block; server-composed URL, never model-invented; host-scoped to matter, degrades to valid unscoped wizard link; GATE CONTROL FLOW UNCHANGED — block still rejects w/ zero Dataverse writes, only refusal message enriched; 768/768. Deferral: OutcomeCard-structured refusal blocked by ADR-040 store-before-render→035/032 follow-up) | ✅ | sonnet | ✅ | — | G-R2-A |
| 041 | Capability-discovery READ endpoint ✅ 2026-07-09 (GET /api/ai/capabilities reuses existing IConsumerRoutingService closed-catalog query, zero new DI; client-safe DTO omits risk/capture-mode/match-conditions; unconditional map + RequireAuthorization; 9 contract + 5 hook tests; read=free D-F0(b). Deferral: soft-slash launcher menu UI wiring→follow-on) | ✅ | sonnet | ✅ | — | G-R2-A |
| 042 | Cataloged create-matter capability ✅ 2026-07-09 (mirrors create-task exactly: JPS Action + input/output schema mirrors + KEEP-path contract test + UC-B-6 doc; drafts matter then invokes EXISTING gated dataverse.create_record — no new handler/gate/dispatch; DataverseCreateRecordHandler reused UNCHANGED; 7762/0 BFF suite. **Live Binding/Action seed + ConsumerTypes.CreateMatter activation + GU-065/066/067 planned→existing DEFERRED to gate G-R2-A task 049** per notes/jps/create-matter-binding-row-pending-seed.json 7-step deploySequence) | ✅ | sonnet | ❌ | **020** | G-R2-A |
| 043 | ADR-041 authoring (Proposed→Accepted@G-R2-A) ✅ 2026-07-09 (full docs/adr/ADR-041-judgment-confirmation-completion-policy.md + concise .claude/adr/ mirror + INDEX row + CHANGELOG; D-F0 preamble + D-F1 tier table/overlay/E-1..E-6 + D-F2 completion; risk=catalog-DATA + confirm-state=gate-ledger invariants; Status PROPOSED, Accepted flip gated on G-R2-A/task 049; main-session write per §3 boundary; records the 032-engine-0-call-sites open item) | ✅ | fable | ❌ | 030,032,035 | G-R2-A |
| 049 | **G-R2-A browser UAT (operator, spaarkedev1)** | 🔲 | sonnet | ❌ | Wave J | **GATE** |
| 050 | Memory: generalize→RecordMemory + User + new container | 🔲 | opus·xhigh | ❌ | 016 | G-R2-B |
| 051 | Governance envelope on MemoryFact + migration | 🔲 | opus | ❌ | 050 | G-R2-B |
| 052 | Memory governance (retention/review-delete/audit) | 🔲 | opus | ❌ | 050 | G-R2-B |
| 053 | Context Binder + ContextEnvelope assembly | 🔲 | opus·xhigh | ❌ | 015 | G-R2-B |
| 054 | ContextEnvelope token budgets (breach-fails-eval) | 🔲 | opus | ❌ | 002,053 | G-R2-B |
| 055 | Caller-contact self-assignment resolution | 🔲 | sonnet | ✅ | — | G-R2-B |
| 056 | Portfolio fresh-retrieval bias | 🔲 | sonnet | ✅ | 053 | G-R2-B |
| 057 | memory.write — AI-initiated + provenance-tagged (no gate) | 🔲 | opus | ❌ | 050 | G-R2-B |
| 060 | Organizational-scope provider interface | 🔲 | sonnet | ✅ | — | G-R2-B |
| 061 | Semantic-scope provider interface | 🔲 | sonnet | ✅ | — | G-R2-B |
| 062 | Workspace-intelligence precursors | 🔲 | sonnet | ✅ | 035,050 | G-R2-B |
| 063 | Matter-level retrieval ACL verification spike | 🔲 | sonnet | ✅ | — | G-R2-B |
| 064 | ADR-040 inline size-cap enforcement home | 🔲 | sonnet | ❌ | **001** | G-R2-B |
| 065 | ADR-042 authoring (Proposed→Accepted@G-R2-B) | 🔲 | fable | ❌ | 050,057 | G-R2-B |
| 069 | **G-R2-B browser UAT (operator, spaarkedev1)** | 🔲 | sonnet | ❌ | Wave M | **GATE** |
| 070 | Publish-size verification harness | 🔲 | sonnet | ✅ | — | G-R2-D |
| 071 | Eval-suite-green merge gate | 🔲 | sonnet | ✅ | 031,033 | G-R2-D |
| 072 | Cross-satellite seam-fork verification | 🔲 | sonnet | ✅ | — | G-R2-D |
| 073 | Track-B hygiene sweep | 🔲 | sonnet | ✅ | — | G-R2-D |
| 074 | Audit-container partition re-key (coord 050) | 🔲 | sonnet | ❌ | 050 | G-R2-D |
| 075 | Legacy workspace tools verdict | 🔲 | sonnet | ❌ | **001** | G-R2-D |
| 076 | Orphan verification | 🔲 | sonnet | ✅ | **001** | G-R2-D |
| 079 | **G-R2-D verification (CI + publish-size + seam-fork)** | 🔲 | sonnet | ❌ | Wave H | **GATE** |
| 090 | Project wrap-up (/test-diet + /defer) | 🔲 | sonnet | ❌ | ALL | close |

---

## Parallel Execution Groups

MAX 6 agents per wave (CLAUDE.md §8.5). `parallel-safe=false` tasks touch the shared `Services/Ai/` factory/gate/directive stack or `.claude/` — run **sequentially in the main session**. Contract tasks (010–016) are the most parallelizable (separate files).

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **P0** | 001, 002, 003, 004 | none | Phase 0 — independent research/measurement; parallel |
| **A0-contracts** | 010, 011, 012, 013, 014, 015, 016 | none | Walking-skeleton contracts; parallel (≤6); **010 published first** |
| **A-infra** | 020 (serial), 021 (∥) | none | **020 blocks all catalog-row tasks**; runs early |
| **D-F0** | 030 (serial) → 031 | none | Runs in parallel with A0 (ruling R-3) |
| **J-serial** | 032→033, 034, 035, 036, 038, 043 | 020 / A0 | Core-stack tasks — sequential (shared factory/gate files) |
| **J-parallel** | 037, 039, 040, 041, 042 | 020 (for 042) | Independent surfaces — parallel |
| **17/seam** | 017 | 010–016 | Cross-project obligation filing |
| **M-serial** | 050→051,052; 053→054; 057; 065 | 016/015/020/032 | Memory core — sequential (shared memory service + binder) |
| **M-parallel** | 055, 056, 060, 061, 062, 063 | 050/053 | Independent — parallel |
| **contingent** | 064, 075, 076 | **001** | Re-checked against r1 P4-close at task 001 |
| **H-parallel** | 070, 071, 072, 073, 074 | Waves / 050 | Hardening — mostly parallel |
| **Gates** | 049, 069, 079 | their wave | **Operator browser UAT — never auto-run** |
| **Close** | 090 | ALL | Prescriptive; `/test-diet` before complete |

---

## Critical Path

`002/016 → 020 (hoist) → 032 (Policy v2) → 057 (memory.write) → 065 (ADR-042) → 069 (G-R2-B UAT) → 079 → 090`
and the memory build `015 → 053 (Binder) → 054 (budgets)`. The **triple-twin hoist (020)** and the **A0 contracts** are the two upstream unblockers — schedule both first.

## High-Risk / Judgment-Boundary Tasks (carry `<escalation><trigger>`)

- **020** triple-twin hoist (blast radius; ADR-039 one-intent tension)
- **032 / 034 / 057** gate + memory-write (side-effect determinism; security floor)
- **050 / 074** Cosmos migration + audit re-key (irreversible partition change)
- **043 / 065** ADR authoring (§6.5 conflict-resolution + `.claude/` write boundary)
- **063** matter-wall ACL spike (security escalation path pre-declared)
- **001 / 064 / 075 / 076** contingent on r1 P4-close ruling
- **090** wrap-up (operator-accepted memory-governance deferral risk)

---

## Model-Tier Summary (CLAUDE.md §8.5)

- **fable** (2): 043 ADR-041, 065 ADR-042
- **opus** (18): all A0 contracts (010–016), 020, 030–032, 034–036, 038, 050–054, 057
- **sonnet** (33): Phase 0, catalog-row/eval/interface/hygiene/UAT/wrap-up
- **xhigh effort**: 002, 020, 021, 050, 053 (brownfield / high-blast-radius)

---

*Generated by `/project-pipeline` Step 3. Updated by `task-execute` (🔲→✅) + `/devops-project-sync`.*
