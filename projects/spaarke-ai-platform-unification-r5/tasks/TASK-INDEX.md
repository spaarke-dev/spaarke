# Task Index — Spaarke AI Platform Unification R5

> **Status**: READY — all 37 task POMLs generated; tasks ready for `task-execute` invocation
> **Created**: 2026-06-03 (late); POMLs generated 2026-06-04
> **Source**: `plan.md` Phase Breakdown
> **Universal task driver**: `task-execute` skill (MANDATORY per root CLAUDE.md §4)
> **Total tasks**: 37 across 3 phases + wrap-up (Phase 1: 9; Phase 2: 22; Phase 3: 5; wrap-up: 1)

---

## Status legend

🔲 not-started · 🔄 in-progress · ✅ complete · 🚧 blocked · ⏭️ deferred (R6+)

---

## Phase organization

R5 ships in 3 sequential phases + 1 wrap-up task, with parallel-execution opportunities within phases. Critical path crosses phases; non-critical-path tasks parallelize within Wave groups.

| Phase | Wave structure | Status |
|---|---|---|
| Phase 1: Platform Extensions | 5 parallel groups (P1-G1 through P1-G5) | 🔲 |
| Phase 2: Vertical Slice + Insights Tool Integration | 8 parallel groups (P2-G1 through P2-G8) | 🔲 |
| Phase 3: Polish + Future-Use Validation | Mostly serial (D3-01 → D3-05) | 🔲 |
| Wrap-up | Single task (090) | 🔲 |

---

## Tasks by phase

### Phase 1 — Platform Extensions (~5 days, ~8–10 tasks)

| ID | Wave-item | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| 001 | P1-G1 | D1-01 Provision `spaarke-session-files` AI Search index (Bicep + schema) | ✅ | 4h (actual ~1h) | ❌ | — |
| 002 | P1-G2 | D1-02 Extend `RagSearchOptions` with `sessionId` filter (additive parameter) | ✅ | 2h (actual 1.5h) | ✅ | 001 |
| 003 | P1-G2 | D1-03 Parameterize `RagIndexingPipeline` for session-files writes | ✅ | 3h (actual 3h) | ✅ | 001 |
| 004 | P1-G3 | D1-04 Extend `ChatSession` model with `UploadedFiles[]` manifest | ✅ | 2h (actual 2h) | ✅ | — |
| 005 | P1-G3 | D1-05 Add `FieldDelta` variant to `AnalysisChunk` (additive SSE event type) | ✅ | 2h (actual 2h) | ✅ | — |
| 006 | P1-G4 | D1-06 Switch Summarize playbook to Azure OpenAI Structured Outputs + incremental JSON parser | ✅ | 1d (actual 2h, primitives only; orchestrator wiring deferred to 012) | ❌ | 005 |
| 007 | P1-G5 | D1-07 Session-files cleanup `IHostedService` (background job) | ✅ | 4h (actual 4h) | ✅ | 001, 003 |
| 008 | P1-G5 | D1-08 Telemetry events + cost observability instrumentation | ✅ | 3h (actual 3h) | ✅ | — |
| 009 | P1-G5 | D1-09 Phase 1 tests + BFF publish-size verification | ✅ | 4h (actual ~15m — checks aggregated) | ❌ | 002, 003, 004, 005, 006, 007, 008 |

---

### Phase 2 — Vertical Slice + Insights Tool Integration (~7–8 days, ~22–28 tasks)

**Pre-Phase-2 gate**: R5 lead reviews `design-e3-tool-call-contract.md` v1.0 + records D1–D6 decisions in §10 review log. Closes Insights task 042 sub-task A.5.

#### Summarize vertical slice (D2-01 to D2-12)

| ID | Wave-item | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| 010 | P2-G1 | D2-01 New `sprk_analysisaction` seed "Summarize Document for Chat" (Dataverse deploy) | ✅ | 2h (actual ~1.5h incl deploy) | ✅ | Phase 1 ✅ |
| 011 | P2-G1 | D2-02 New `sprk_analysisplaybook` configuration | ✅ | 2h (actual ~1h incl deploy) | ✅ | Phase 1 ✅ |
| 012 | P2-G2 | D2-03 New `SessionSummarizeOrchestrator` concrete class | ✅ | 4h (actual ~3h) | ✅ | 010, 011 |
| 013 | P2-G2 | D2-08 Extract `RichFilePreview` renderer core from `RichFilePreviewDialog` | ✅ | 4h (actual 2h) | ✅ | Phase 1 ✅ |
| 014 | P2-G3 | D2-04 New `POST /api/ai/chat/sessions/{id}/summarize` endpoint | ✅ | 3h (actual ~2.5h) | ✅ | 012 |
| 015 | P2-G3 | D2-05 Register `InvokeSummarizePlaybookTool` on `SprkChatAgent` | ✅ | 3h (actual ~2.5h) | ✅ | 012 |
| 016 | P2-G3 | D2-06 Add additive PaneEventBus event types (5 new) per ADR-030 | ✅ | 2h (actual 1h) | ✅ | — |
| 017 | P2-G4 | D2-07 Build `StructuredOutputStreamWidget` (Workspace; schema-driven) | 🔲 | 1d | ✅ | 016 |
| 018 | P2-G4 | D2-09 Build `FilePreviewContextWidget` (Context pane; non-modal) | 🔲 | 1d | ✅ | 013, 016 |
| 019 | P2-G5 | D2-10 Slash command `/summarize` semantic extension (dual-mode routing) | ✅ | 3h (actual 1.5h; branch-a wiring deferred to task 020) | ✅ | — |
| 020 | P2-G5 | D2-11 Chat-pane orchestration UX (file chips, indicator, interjection) | 🔲 | 1d | ✅ | 017 |
| 021 | P2-G5 | D2-12 "Summarize this only" per-file affordance + UI multi-turn refinement | 🔲 | 3h | ✅ | 018 |
| 022 | P2-G5 | D2-08 Upgrade `DocumentViewerWidget` (R4 stub) to use extracted renderer | 🔲 | 2h | ✅ | 013 |

#### Insights tool integration (D2-13 to D2-20)

| ID | Wave-item | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| 023 | Pre-G6 | D2-13 R5 lead contract review + sign-off (`design-e3-tool-call-contract.md` v1.0 §10) | ✅ (skipped-as-redundant; D-decisions locked in spec.md §8.2) | 1h (actual ~5m governance) | ❌ | — (operator-led) |
| 024 | P2-G6 | D2-14 `InsightsQueryToolHandler` chat-agent tool function (register `insights.query`) | ✅ | 4h | ✅ | 023 |
| 025 | P2-G6 | D2-15 Subject resolution + HTTP client (existing `@spaarke/auth`) | ✅ | 3h (actual ~2h sub-agent; main session owns commit + quality gates + test execution) | ✅ | 023 |
| 026 | P2-G6 | D2-16 Two-path response renderer (`InsightsResponseRenderer`) | 🔲 | 1d | ✅ | 017, 024 |
| 027 | P2-G6 | D2-17 Clickable citations (v1.1 `citations[].href`; v1.0 fallback) | 🔲 | 4h | ✅ | 026 |
| 028 | P2-G6 | D2-18 Confidence floor badge (D5 R5 client-side; `<0.6` threshold) | 🔲 | 2h | ✅ | 026 |
| 029 | P2-G7 | D2-19 12 Insights error codes + correlation propagation + retry logic | 🔲 | 4h | ❌ | 024, 025, 026 |
| 030 | P2-G8 | D2-20 Insights tool smoke tests (Wave D7 synthetic GUIDs + SME walkthrough) | 🔲 | 4h | ❌ | 024, 025, 026, 027, 028, 029 |

#### Cross-cutting Phase 2

| ID | Wave-item | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| 031 | P2-G8 | D2-22 Phase 2 tests + integration verification (E2E Summarize + Insights smoke + cross-tool disambiguation) | 🔲 | 4h | ❌ | All P2 tasks |

---

### Phase 3 — Polish + Future-Use Validation (~2–3 days, 5 tasks)

| ID | Wave-item | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| 040 | P3-1 | D3-01 `/analyze` configuration-only proof point (validates SC-19 platform-extension claim) | 🔲 | 4h | ❌ | Phase 2 ✅ |
| 041 | P3-2 | D3-02 Get Started welcome card "Summarize a Document" | 🔲 | 3h | ✅ | Phase 2 ✅ |
| 042 | P3-2 | D3-03 Telemetry dashboards (App Insights / Grafana) | 🔲 | 4h | ✅ | Phase 2 ✅ |
| 043 | P3-3 | D3-04 Operator-led end-to-end testing (discoverability + correctness) | 🔲 | 1d | ❌ | 040, 041, 042 |
| 044 | P3-3 | D3-05 Lessons-learned + R6 backlog | 🔲 | 4h | ❌ | 043 |

---

### Wrap-up

| ID | Wave-item | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| 090 | Wrap | Project wrap-up (README → Complete; coordination doc §8; R5 PR + merge) | 🔲 | 4h | ❌ | All prior |

---

## Parallel Execution Groups

Tasks within a group can be dispatched in parallel via Skill tool calls (one per task in a single message). Cross-group dependencies enforce serial execution between groups.

| Group | Wave | Tasks | Prerequisite | Notes |
|---|---|---|---|---|
| **P1-G1** | Phase 1 | 001 | — | Serial — index provisioning (sequential before downstream) |
| **P1-G2** | Phase 1 | 002, 003 | 001 ✅ | Parallel — RagSearchOptions + RagIndexingPipeline extensions |
| **P1-G3** | Phase 1 | 004, 005 | — | Parallel — different model files (ChatSession + AnalysisChunk); can run alongside P1-G2 |
| **P1-G4** | Phase 1 | 006 | 005 ✅ | Serial — Structured Outputs + JSON parser |
| **P1-G5** | Phase 1 | 007, 008, 009 | 002, 003, 004, 005, 006 ✅ | Parallel — cleanup + telemetry + tests; 009 depends on all P1 implementation tasks |
| **P2-Gate** | Pre-Phase-2 | 023 | Phase 1 ✅ | Sign-off gate; operator-led; blocks rest of Phase 2 Insights work |
| **P2-G1** | Phase 2 | 010, 011 | Phase 1 ✅ | Parallel — Dataverse seeds (data deploys) |
| **P2-G2** | Phase 2 | 012, 013 | 010, 011 ✅ | Parallel — orchestrator class + renderer extraction (different worktree areas) |
| **P2-G3** | Phase 2 | 014, 015, 016 | 012 ✅ | Parallel — endpoint + agent-tool + PaneEventBus events (all in `AnalysisServicesModule` but additive) |
| **P2-G4** | Phase 2 | 017, 018 | 013, 016 ✅ | Parallel — new widgets (Workspace + Context) |
| **P2-G5** | Phase 2 | 019, 020, 021, 022 | 017, 018 ✅ | Parallel — UI orchestration (slash command + chat-pane UX + per-file affordance + DocumentViewerWidget upgrade) |
| **P2-G6** | Phase 2 | 024, 025, 026, 027, 028 | 023 ✅ AND 017 ✅ | Parallel — Insights tool integration suite (5 tasks share `InsightsResponseRenderer`; coordinate via single PR or split into 2 sub-groups) |
| **P2-G7** | Phase 2 | 029 | 024, 025, 026 ✅ | Serial — error codes + retry depends on tool implementation |
| **P2-G8** | Phase 2 | 030, 031 | All P2 tasks ✅ | Serial — smoke tests + integration verification |
| **P3-1** | Phase 3 | 040 | Phase 2 ✅ | Serial — proof-point validation |
| **P3-2** | Phase 3 | 041, 042 | Phase 2 ✅ | Parallel — Get Started card + telemetry dashboards |
| **P3-3** | Phase 3 | 043, 044 | 040, 041, 042 ✅ | Serial — testing pass + lessons-learned (043 → 044) |
| **Wrap** | — | 090 | All prior ✅ | Final task |

**Max concurrency per wave**: 6 agents (per project-pipeline Step 5 hard limit; tune only with evidence).

**Permission boundary** (per root CLAUDE.md §3): R5 tasks do NOT touch `.claude/` paths (ADR-018 Flag Scope Discipline already shipped pre-implementation). Standard parallel dispatch applies for all R5 tasks.

---

## Critical Path

`001 (Index) → 005 (FieldDelta) → 006 (Structured Outputs + parser) → 010 (Action seed) → 012 (Orchestrator) → 014 (Endpoint) → 017 (StructuredOutputStreamWidget) → 020 (Chat-pane UX) → 031 (Phase 2 verification) → 040 (/analyze proof point) → 043 (Operator testing) → 044 (Lessons-learned) → 090 (Wrap-up)`

Critical path ≈ 13 sequential dependencies. Slack exists in Phase 2 deliverables that can run in parallel within their waves.

---

## High-Risk Items (from plan.md)

| Risk | Affected Tasks | Mitigation |
|---|---|---|
| Azure OpenAI Structured Outputs streaming behavior surprises | 006 | Phase 1 spike — validate Structured Outputs vs Function Calling before committing |
| RagSearchOptions extension conflicts with Insights | 002 | Insights additions are orthogonal (subject/artifact/predicate); R5 adds sessionId. Quick PR review. |
| Wave F (Insights v1.1) deploys later than Phase 2 W3 timing | 026, 027 | Graceful v1.0 fallback per NFR-11; v1.1 upgrade is incremental swap |
| Tool routing disambiguation between summarize + insights.query | 015, 024 | Tool description discipline per NFR-12; observation testing during Phase 2 |
| `citations[].href` schema-plumbing spike defers document href to v1.2 | 027 | R5 handles `href: null` gracefully (back-compat path); display-name-only citations work |
| BFF publish-size delta exceeds +1 MB compressed | All BFF tasks (001, 002, 003, 004, 005, 006, 007, 012, 014, 015) | Per-task verification (PR-02); 14 MB headroom available |
| `StructuredOutputStreamWidget` schema-driven complexity | 017 | UR-02 flagged; iterate during Phase 2 with concrete schemas; 80/20 on rendering hints |

---

## Task POML Generation Status

✅ **COMPLETE** — all 37 task POML files generated 2026-06-04 via parallel sub-agent waves (Wave 1 → 9). Each POML follows `task-execute.template.md` structure with `<knowledge>`, `<steps>`, `<acceptance-criteria>`, Step 0 RIGOR LEVEL declaration, and Step 9.5 quality gates (for FULL-rigor tasks). All R5-specific binding constraints encoded per project CLAUDE.md §3.1-3.8.

**Path-drift correction applied** to plan.md / spec.md / CLAUDE.md (2026-06-04): canonical paths are `infrastructure/ai-search/spaarke-session-files.json` and `infrastructure/bicep/` (not legacy `infra/ai-search/*.index.json`).

---

*This index is maintained by task-execute (status updates on task start/complete) + task-create (new task entries when added). Manual edits should preserve the structure and update timestamp.*
