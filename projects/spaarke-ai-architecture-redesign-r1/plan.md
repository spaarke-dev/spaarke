# Implementation Plan — spaarke-ai-architecture-redesign-r1

> **Generated**: 2026-07-05 by `/project-pipeline` (from `spec.md` 2026-07-05)
> **Portfolio**: [Project #550](https://github.com/spaarke-dev/spaarke/issues/550) · Epic [#421 SPAARKE AI](https://github.com/spaarke-dev/spaarke/issues/421)
> **Governing docs**: `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` v0.4 (target) · `notes/audit-inputs/SPAARKE-AI-MIGRATION-MAP.md` v1.0 (sequencing) · `notes/audit-inputs/OVERLAY-MATRIX.md` (per-component HOW). Where this plan and those disagree, they govern.

---

## 1. Overview

Five phases (P0–P4) plus a continuous Track-B deadwood stream. Each phase ends at a gate:
**G-P0** is the sole engineering gate (dark foundations); **G-P1..G-P4 are browser gates** — a
user executes the UAT script in the Spaarke UI on spaarkedev1 with rendered results (NFR-11:
curl/tests/logs NEVER satisfy a browser gate). **G-M** (maker gate) runs inside P4 wrap-up.

Hard cutover doctrine (binding): no parallel-run, no compat shims; every retirement is
grep-zero-verified with shown output (NFR-08).

**51 tasks**: P0×14 (001–014) · P1×8 (020–027) · P2×9 (030–038) · P3×9 (040–048) ·
P4×6 (050–055) · Track-B×4 (070–073) · wrap-up (090).

## 2. Discovered Resources (pipeline Step 2)

### ADRs (binding)
| ADR | Role in this project |
|---|---|
| **ADR-039** Grounded Execution & Closed Catalogs | THE dispatch/catalog contract; Proposed → **Accepted at P1** (task 026) |
| **ADR-040** Session Ledger | Storage-precedes-rendering contract; Proposed → **Accepted at P0** (task 014) |
| **ADR-013** (amended 2026-07-05) | Capability invocation = canonical facade verb; PublicContracts boundary unchanged |
| **ADR-037** (amended 2026-07-05) | Section-name-keyed streaming binds composite executors; FieldDelta deletable at cutover |
| ADR-032 | Null-Object kill-switch peers for every gated registration (tasks 006, P0-wide) |
| ADR-029 | Publish-size per-task verification; ceiling ≤60 MB; expectation NET REDUCTION (NFR-01) |
| ADR-038 | Test pyramid; eval suite is KEEP-class `tests/integration/contract/**` |
| ADR-015 / ADR-016 | Ledger data-governance tiers (NFR-07) / AI budgets (NFR-09) |
| Standing | 001, 004, 008, 009, 010, 014, 018, 019, 028, 030, 031, 036 |

### Skills
`task-execute` (every task) · `dataverse-create-schema` (003) · `jps-action-create`/`jps-validate` (020, 041, 042) · `code-review` + `adr-check` (Step 9.5, FULL rigor) · `bff-deploy` + `code-page-deploy` (gate-deploy tasks 014/027/038/048) · `test-diet` + `project-defer-issue-tracking` (090) · `doc-drift-audit` (052).

### Patterns / canonical code (from spec §Existing Patterns)
| Slot | Entry point |
|---|---|
| Prompted executor | `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/` (ActionRunner + PromptSchemaRenderer) |
| Coded workflow shape | `Services/Ai/Narrators/DailyBriefingNarrator.cs` (Wave-11 pattern) |
| Tool framework | `Services/Ai/Handlers/` + `ToolHandlerToAIFunctionAdapter` + `sprk_analysistool` discovery |
| Gate store (generalize) | `Services/Ai/Chat/PendingPlanManager.cs` |
| Client | canonical `useSseStream`, `PaneEventBus`, widget registries, `StructuredOutputStreamWidget` |
| Record-persisted outputs | widgets-r1 topic-registry pattern (`InsightSummaryCard` + `sprk_aitopicregistry`) |

### Constraints
`.claude/constraints/bff-extensions.md` (binding — BFF hot-path) · `.claude/constraints/azure-deployment.md` (publish-size rule) · `.claude/constraints/testing.md` · root CLAUDE.md §10/§11.

### Placement justification (CLAUDE.md §10)
Every server component stays in `Sprk.Bff.Api` per ADR-013's extraction criteria (latency + transactional coupling with session/SSE state). No new deployables. Net publish-size REDUCTION expected.

## 3. The `/goal` wave pilot (NFR-10)

Project-scoped pilot per design §6.5 + `notes/goal-feature-evaluation.md`. Every wave below
carries a **pre-authored `/goal` condition** — paste it at wave start. Composition rules:
shown evidence (test/grep/build output visible in transcript) + scope bind + turn cap +
"Step 9.5 gates passed". `/goal` NEVER wraps a G-gate (human UAT). Run `/goal clear` before
every gate task. If the pilot proves out by P1, file a `/defer` to promote it into the skills.

## 4. Phase Breakdown & Waves

### Phase P0 — Foundations (dark; gate G-P0 = engineering evidence)

| Task | FR | Title | Rigor | Parallel |
|---|---|---|---|---|
| 001 | FR-P0-01 | ChatSession typed ledger entries + Redis/Cosmos persistence + Cosmos file-ref fix | FULL | W-P0-A |
| 002 | FR-P0-02 | Digest compaction generalized to outputs (`ChatHistoryManager`) | FULL | W-P0-B |
| 003 | FR-P0-03 | Catalog schema extensions ×3 tables (spaarkedev1) | STANDARD (Data) | W-P0-A |
| 004 | FR-P0-03 | `ConsumerRoutingService` returns full Binding contract | FULL | W-P0-B |
| 005 | FR-P0-04 | Boot reconciliation health checks (constants↔rows, tool↔handler bijection) | FULL | W-P0-B |
| 006 | FR-P0-05 | Registration hygiene: FinanceModule exits + Null peers + compound AI gate | FULL | W-P0-A |
| 007 | FR-P0-06 | `ICodedWorkflow` convention + DailyBriefing retrofit (no behavior change) | FULL | W-P0-A |
| 008 | FR-P0-07 | `dataverse.*` READ handlers (`describe`, `read_query`, `search_data`) over BFF-OBO | FULL | W-P0-B |
| 009 | FR-P0-07 | `dataverse.*` WRITE handlers (`create_record`, `update_record`, `delete_record`) | FULL | W-P0-B |
| 010 | FR-P0-08 | OBO spike: `Dynamics CRM/mcp.tools` → `/api/mcp` (documented pass/fail) | MINIMAL (spike) | W-P0-C |
| 011 | FR-P0-09 | Golden-utterance eval-suite scaffold + ~30 seed utterances | TEST-MODIFYING | W-P0-C |
| 012 | FR-P0-10 | User-OBO audit of every AI-reachable Dataverse flow | STANDARD | W-P0-C |
| 013 | FR-P0-11 | Portfolio reconciliation: close/re-scope R7; re-point R4/Action-Engine/insights-r3 triggers | MINIMAL | W-P0-A |
| 014 | G-P0 | P0 deploy + gate evidence + **ADR-040 → Accepted** | STANDARD | serial |

**Wave W-P0-A** (5 tasks + Track-B 070, max 6 agents): 001, 003, 006, 007, 013
`/goal` — `Tasks 001, 003, 006, 007, 013 in projects/spaarke-ai-architecture-redesign-r1/tasks/ are complete via task-execute: ledger round-trip test passes with output shown; schema columns verified on spaarkedev1 with query output shown; services unresolvable when Analysis:Enabled=false (test output shown); narrator tests green (shown); R7 issue + trigger re-points linked. dotnet build green (output shown). code-review + adr-check (Step 9.5) passed for FULL tasks. TASK-INDEX rows flipped ✅. Do not modify files outside these tasks' scopes. Stop after 40 turns.`

**Wave W-P0-B** (5 tasks + 071): 002, 004, 005, 008, 009 — deps: 001 (for 002), 003 (for 004, 005)
`/goal` — `Tasks 002, 004, 005, 008, 009 complete via task-execute: digest-with-outputs test green (shown); ConsumerRoutingService full-contract test green (shown); startup health check fails on seeded drift (test output shown); all six dataverse.* handler tests green under test-user OBO (shown); handler names frozen against the GA MCP list (cited). dotnet build green (shown). Step 9.5 gates passed. TASK-INDEX updated. Scope-bound to these tasks. Stop after 40 turns.`

**Wave W-P0-C** (3 tasks + 072): 010, 011, 012
`/goal` — `Tasks 010, 011, 012 complete: OBO spike result documented in notes/ (pass or fail both acceptable); eval scaffold runs in CI with ~30 utterances (run output shown); per-flow OBO audit note written with evidence per flow. Step 9.5 gates passed where applicable. TASK-INDEX updated. Stop after 30 turns.`

**Gate task 014** (serial, `/goal clear` first): bff-deploy; ledger round-trip + health-check evidence assembled; ADR-040 status flipped with citation; publish-size reported.

### Phase P1 — First capability end-to-end (gate G-P1, BROWSER)

| Task | FR | Title | Rigor | Parallel |
|---|---|---|---|---|
| 020 | FR-P1-01 | `chat-summarize` as Action+Binding rows; endpoint delegates; dual-path dissolves | FULL | W-P1-A |
| 021 | FR-P1-02 | Universal ledger write before render + `OutputRouter` (informational) | FULL | W-P1-B (dep 020) |
| 022 | FR-P1-03 | Event Rules service + `document_uploaded → [classify, summarize]` with bounds | FULL | W-P1-C (dep 021) |
| 023 | FR-P1-04 | Click path: `dispatchConsumer(bindingId, args)` helper; `executeSummarizeIntent` + `intentMatcher` deleted | FULL | W-P1-C (dep 021) |
| 024 | FR-P1-05 | Engine-output→ledger adapter (E-2) | FULL | W-P1-C (dep 021) |
| 025 | FR-P1-06 | Close r7 tactical branch: keep 4 fixes, drop 3 dispatch patches | FULL | W-P1-A |
| 026 | FR-P1-07 | Eval UC-A-1 family green; **ADR-039 → Accepted** | TEST-MODIFYING | W-P1-D |
| 027 | G-P1 | P1 deploy + browser UAT (upload → classify + summary + chips; typo-tolerant summarize) | STANDARD | serial |

**W-P1-A** (+073): 020, 025 · `/goal` — `Tasks 020, 025 complete via task-execute: summarize executes via catalog rows (test shown); SessionSummarizeOrchestrator dual-path deleted; r7 branch closed containing exactly the 4 keep-fixes, grep for linear_dispatch and TryDetectExplicitConsumerType returns zero hits outside git history (output shown). Build green (shown). Step 9.5 passed. Scope-bound. Stop after 35 turns.`
**W-P1-B**: 021 · `/goal` — `Task 021 complete: every execution writes an addressable SessionOutput keyed {bindingId}@t{n} BEFORE rendering (ordering test output shown); OutputRouter routes informational disposition; build green (shown); Step 9.5 passed. Stop after 25 turns.`
**W-P1-C**: 022, 023, 024 · `/goal` — `Tasks 022, 023, 024 complete: upload-with-no-command produces classification + summary + chips (integration evidence shown); chips carry binding_id through ONE dispatchConsumer helper, grep-zero for executeSummarizeIntent and intentMatcher (shown); an insights run writes a ledger SessionOutput (test shown). Builds green (dotnet + npm run build:prod, shown). Step 9.5 passed. Stop after 40 turns.`
**W-P1-D**: 026 · `/goal` — `Task 026 complete: UC-A-1 utterance family green in CI (run link/output shown); eval failure blocks merge (wiring shown); ADR-039 status Accepted with citation committed. Stop after 20 turns.`

**Gate 027**: `/goal clear`; bff-deploy + code-page-deploy; **operator executes G-P1 UAT script in the browser on spaarkedev1**; latency NFR-04 checked (upload→first-summary-token ≤5s p50).

### Phase P2 — Text-path hard cutover (gate G-P2, BROWSER)

| Task | FR | Title | Rigor | Parallel |
|---|---|---|---|---|
| 030 | FR-P2-01 | Agent-turn loop contract (budget 8, catalog projection, pre-filter, citations, ToolChain persist) | FULL | W-P2-A |
| 031 | FR-P2-02 | ONE Confirmation Gate: PendingPlanManager generalized; second store deleted; `side_effect_class` gating | FULL | W-P2-A |
| 032 | FR-P2-03 | Loop-native elicitation + `capture_mode: modal` escape + Gate ledger markers | FULL | W-P2-B (deps 030, 031) |
| 033 | FR-P2-04 | Honest refusal: `no_match_handler` Binding + `dispatch_refused` telemetry | FULL | W-P2-B (dep 030) |
| 034 | FR-P2-05 | **HARD CUTOVER** chat NL → loop; soft slashes → direct invocations; `intentHint` retired | FULL | W-P2-C (deps all) |
| 035 | FR-P2-06 | DELETE dispatcher stack (PlaybookDispatcher, IntentReranker, CandidateSelector, CompoundIntentDetector + tests) | FULL | W-P2-D (dep 034) |
| 036 | FR-P2-07 | DELETE legacy `Chat/Tools/*` after 2 capability migrations to typed handlers | FULL | W-P2-D (dep 034) |
| 037 | FR-P2-08 | Eval: full catalog families + refusal + compound + prompt-injection (NFR-03) | TEST-MODIFYING | W-P2-E |
| 038 | G-P2 | P2 deploy + browser UAT (four-outcome contract; session memory; confirmed writes) | STANDARD | serial |

**W-P2-A**: 030, 031 · `/goal` — `Tasks 030, 031 complete: loop tests prove per-turn budget 8, deterministic pre-filter, citation enforcement on reads, ToolChain ledger persistence (output shown); ONE pending store (grep for the /actions/{id}/confirm store returns zero, shown); gating driven by side_effect_class + risk with hardcoded tool-name lists deleted (grep shown). Build green (shown). Step 9.5 passed. Stop after 40 turns.`
**W-P2-B**: 032, 033 · `/goal` — `Tasks 032, 033 complete: missing-args → clarifying turn; capture_mode modal routes to wizard; Gate ledger markers tracked; mid-elicitation utterances parse as answers (tests shown); off-catalog utterance renders tenant refusal template + dispatch_refused lands in App Insights (evidence shown). Step 9.5 passed. Stop after 35 turns.`
**W-P2-C**: 034 · `/goal` — `Task 034 complete: no chat utterance reaches any legacy dispatcher (telemetry or test evidence shown); four soft slashes invoke deterministically; grep-zero for intentHint (shown). AgentContentSafetyMiddleware verified on the loop path (evidence shown). Build green. Step 9.5 passed. Stop after 30 turns.`
**W-P2-D**: 035, 036 · `/goal` — `Tasks 035, 036 complete: grep for PlaybookDispatcher, IntentRerankerService, PlaybookCandidateSelector, CompoundIntentDetector returns zero hits outside git history (output shown); Chat/Tools directory removed; handlers cover the two migrated capabilities (tests shown); dotnet build + full test suite green (shown); publish-size delta reported. Step 9.5 passed. Stop after 30 turns.`
**W-P2-E**: 037 · `/goal` — `Task 037 complete: eval suite green incl. refusal, compound, and hostile-document injection cases with no ungated side effects (CI output shown). Stop after 25 turns.`

**Gate 038**: `/goal clear`; deploy; **operator G-P2 browser UAT** (do / clarify-then-do / cited ad-hoc answer / honest refusal; "email that summary to John" resolves ledger ref); text-turn TTFB ≤3s p50 verified.

### Phase P3 — Consumer + client consolidation (gate G-P3, BROWSER)

| Task | FR | Title | Rigor | Parallel |
|---|---|---|---|---|
| 040 | FR-P3-01 | Remaining consumers → Bindings; 3 routing-appsettings blocks deleted | FULL | W-P3-A |
| 041 | FR-P3-02 | **`draft-correspondence`**: prompted Action → Communication (Email) service draft via Graph, gated `communicate` | FULL | W-P3-A |
| 042 | FR-P3-03 | **`create-task`**: prompted Action → `dataverse.create` `sprk_event(type=task)` w/ ledger refs | FULL | W-P3-A |
| 043 | FR-P3-04 | Daily Briefing = first full `coded` composite; narrator flag deleted | FULL | W-P3-A |
| 044 | FR-P3-05 | Engine-shell deletions (PlaybookExecutionEngine, AnalysisOrchestrationService legacy, wrappers) | FULL | W-P3-B (deps 040, 043) |
| 045 | FR-P3-06 | Client consolidation: ConversationPane thin host; LW + Compose onto shared helper; ONE SSE parse path | FULL | W-P3-C |
| 046 | FR-P3-07 | Widget layer: dedupe registries; ExecutionTraceWidget bridge; FieldDelta path deleted | FULL | W-P3-D (dep 045) |
| 047 | FR-P3-08 | Work-product record persistence (Binding-declared, widgets-r1 pattern) | FULL | W-P3-C |
| 048 | G-P3 | P3 deploy + browser UAT (flagship one-conversation journey; briefing email; 3 surfaces identical) | STANDARD | serial |

**W-P3-A**: 040, 041, 042, 043 · `/goal` — `Tasks 040-043 complete: all listed consumer routes resolve via the Binding table with grep-zero for LinearConsumers, Workspace.*PlaybookId, Insights.Playbooks.Map config keys (shown); draft-correspondence produces a Spaarke communication draft record gated as communicate (test shown, DRAFT-only); create-task writes sprk_event(type=task) with ledger refs under the confirm gate (test shown); briefing renders + emails via the coded path with the narrator flag grep-zero (shown). Builds green. Step 9.5 passed. Stop after 45 turns.`
**W-P3-B**: 044 · `/goal` — `Task 044 complete: grep-zero for PlaybookExecutionEngine, SessionSummarizeOrchestrator, FileSummarizeService, DocumentProfileService wrappers outside git history (shown); frozen PlaybookOrchestrationService + nodes untouched (diff scope shown); callers re-pointed; build + tests green. Step 9.5 passed. Stop after 30 turns.`
**W-P3-C**: 045, 047 · `/goal` — `Tasks 045, 047 complete: exactly one SSE parse path client-wide (grep for hand-rolled parsers zero, shown); ConversationPane host ≤300 lines with line count shown (or an operator-approved exception on record — escalate before exceeding); a work_product capability persists its envelope to the host record (test shown). npm run build:prod green per package (shown). Step 9.5 passed. Stop after 40 turns.`
**W-P3-D**: 046 · `/goal` — `Task 046 complete: one register-context-widgets module (grep shown); ExecutionTraceWidget renders a real ToolChain from the ledger (UI-test evidence); FieldDelta grep-zero in the widget layer (shown). Build green. Step 9.5 passed. Stop after 25 turns.`

**Gate 048**: `/goal clear`; deploy all surfaces; **operator G-P3 browser UAT**: upload → auto-summary → clause chat → pre-filled matter wizard → confirm → client-letter draft (real ledger refs) → follow-up task; briefing email arrives; record form + workspace + SPA identical.

### Phase P4 — Sweep completion + hardening + graduation (gates G-P4 + G-M)

| Task | FR | Title | Rigor | Parallel |
|---|---|---|---|---|
| 050 | FR-P4-01 | Track-B completion audit (inventory §9 + overlay-DEL; zero unexplained survivors) | STANDARD | W-P4-A |
| 051 | FR-P4-02 | Catalog governance: single `scope-model-index.json`; seed regenerated; `sprk_nodetype` gap ruled | STANDARD | W-P4-A |
| 052 | FR-P4-03 | Data-model docs ×4 + INDEX reconcile + capability-wiring guide + ADR A-3 refreshes | MINIMAL | W-P4-B (deps 050, 051) |
| 053 | FR-P4-04 | PlaybookBuilder canvas de-scope → BA Action/Binding editor; ScopeConfigEditor variant | FULL | W-P4-A |
| 054 | FR-P4-05 | Per-tenant metering counters + KQL query pack | FULL | W-P4-A |
| 055 | FR-P4-06 | Publish-size + CVE verification; ADR-029 baseline update | STANDARD | W-P4-B |
| 090 | FR-P4-07 | Wrap-up: **G-M maker gate** + `/test-diet` + `/defer` filings + ADR status verify + graduation | STANDARD | serial |

**W-P4-A**: 050, 051, 053, 054 · `/goal` — `Tasks 050, 051, 053, 054 complete: Track-B audit table lists every inventory-§9 + overlay-DEL item as grep-verified-deleted (output shown) or keep-with-reason; one catalog copy with round-tripping seed (shown); a BA can author Action + Binding end-to-end in the UI (UI-test evidence); KQL pack returns per-tenant rollups in dev (query output shown). Step 9.5 passed for FULL tasks. Stop after 45 turns.`
**W-P4-B**: 052, 055 · `/goal` — `Tasks 052, 055 complete: 2026-02 ERD docs deleted + replaced; doc-drift-audit clean (output shown); publish size + diff reported with no new HIGH CVEs (dotnet list output shown); ADR-029 baseline updated. Stop after 25 turns.`

**Gate 090** (`/goal clear`): G-P4 NFR sweep + **G-M maker gate** (operator observes a BA authoring a new capability as pure data, ZERO deploys, then invoking it in the UI) + `/test-diet` + `/defer` filings (admin observability dashboards; assistant-initiated send; `/goal` skill promotion) + ADR-039/040 verified Accepted + README/portfolio close-out.

### Track B — Continuous deadwood sweep (from P0; FR-TB-01)

Four batches over the migration map's ~30 dependency-free deletes (inventory §9 register).
Each batch: delete → grep-zero per symbol (shown) → `dotnet build` + `npm run build:prod` green → TASK-INDEX ✅.
Batches ride ALONGSIDE the phase waves (files are dead by definition — no overlap with live-code tasks):

| Task | Batch | Rides with | Contents (per inventory §9 / migration-map delete list) |
|---|---|---|---|
| 070 | TB-1 | W-P0-A | DirectOpenAiAgent cluster + dependents |
| 071 | TB-2 | W-P0-B | Insights renderer cluster |
| 072 | TB-3 | W-P0-C | Dead PCF dirs + R1 client registries/providers/cross-pane |
| 073 | TB-4 | W-P1-A | Stale catalogs/seeds/docs/scripts + remainder of the dependency-free list |

`/goal` (per batch, parameterize N) — `Every file in Track-B batch N is deleted; grep for each deleted symbol returns zero hits outside git history (output shown); dotnet build and npm run build:prod succeed (output shown); TASK-INDEX rows marked ✅. Do not modify files outside the batch list. Stop after 25 turns.`

## 5. Dependency spine (critical path)

```
001 → 002 → 020 → 021 → {022, 023, 024} → 026 → [G-P1 027]
003 → 004/005 → 008/009 ↗                      → 030/031 → 032/033 → 034 → 035/036 → 037 → [G-P2 038]
                                                → 040..043 → 044; 045 → 046; 047 → [G-P3 048]
                                                → 050..055 → [090: G-P4 + G-M]
Track B (070-073) rides waves W-P0-A..W-P1-A; 013 (portfolio reconciliation) earliest possible — unblocks r7 close (025).
```

Critical path: 001 → 002 → 020 → 021 → 022 → 026 → 027 → 030 → 032 → 034 → 035 → 037 → 038 → 040 → 044 → 048 → 090.

## 6. Rules of execution (binding)

1. Every task via `task-execute` (root CLAUDE.md §4). P0–P3 code tasks = **FULL rigor**; eval/test tasks = **TEST-MODIFYING override**; docs/spike/portfolio = STANDARD/MINIMAL.
2. **Max 6 agents per wave**; build verification between waves (dotnet + npm per touched surface).
3. Publish-size verification EVERY BFF-touching task (ADR-029 / NFR-01; expect net reduction; ≥+2 MB single-task delta needs justification).
4. Eval suite green = MERGE GATE from P1 (NFR-02); every catalog/prompt change adds/updates eval cases (NFR-06).
5. Browser rule (NFR-11): gates 027/038/048/090 are executed by the operator in the UI — never auto-passed.
6. Ledger entries = ADR-015 Tier 3; ToolChain = identifiers/filters/counts only; no content in logs (NFR-07).
7. `/goal` at wave level only; `/goal clear` before every gate task (NFR-10).

## 7. Timeline (informed projection — Target 2026-08-15)

| Phase | Estimate | Cumulative |
|---|---|---|
| P0 (+TB-1..3) | ~1.5 weeks | 2026-07-15 |
| P1 (+TB-4) | ~1 week | 2026-07-22 |
| P2 | ~1 week | 2026-07-29 |
| P3 | ~1.5 weeks | 2026-08-07 |
| P4 + G-M + wrap-up | ~1 week | 2026-08-14 |

## 8. References

- `spec.md` (FR/NFR contract) · `design.md` v1.1 (charter) · canonical doc v0.4 · migration map v1.0 · overlay matrix · `notes/goal-feature-evaluation.md`
- `tasks/TASK-INDEX.md` (live status; wave headers carry the /goal conditions)
- Portfolio: Issue #550 · Epic #421 · Board Project #2
