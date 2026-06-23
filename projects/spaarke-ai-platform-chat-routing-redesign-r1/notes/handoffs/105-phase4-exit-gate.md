# Task 105 — Phase 4 Exit Gate Evidence (MVP-Cut Scope)

> **Date**: 2026-06-23
> **Author**: Phase 4 MVP closeout
> **Verdict**: 🟢 **GO for Phase 5** — MVP-cut surface complete and verified locally; deferred handlers contracted in lock-ins; task 104 (deploy) deferred per task 026/054 owner pattern.

---

## Phase 4 MVP scope (per Q5b owner decision 2026-06-22)

Phase 4 was the largest phase originally — 42 tasks across 21 waves covering the 6-tier memory subsystem. Per Q5b decision, Phase 4 was reduced to **12 active tasks + 5 substrate lock-ins**, with the remaining 30 tasks deferred to a future ramp. The decision rationale + lock-in inventory live in `notes/handoffs/032-loader-gap-and-036-bundling.md`.

### Shipped this Phase 4 MVP run (10 tasks complete)

| Task | Title | Status |
|---|---|---|
| 071 | ChatSessionFile shape extension (architecture §11.2) | ✅ |
| 072 | SessionPersistenceService.UpdateUploadedFilesAsync | ✅ |
| 074 | Upload-pipeline ADR-015 tier-1 telemetry (4 emitted + 3 contract-skipped) | ✅ 🟡 PARTIAL |
| 078 | FR-27 single-pipeline + FR-45 line invariant lock-in (MVP Scope A) | ✅ |
| 080 | FR-45 binding-invariant regression test class | ✅ |
| 085 | RecallSessionFileHandler (LOAD-BEARING MVP retrieval tool) | ✅ |
| 091 | Canonical IRecentlyDiscussedTracker + DI (MVP-cut from 8-handler batch) | ✅ |
| 092 | Seed-TypedHandlers update for RECALL-SESSION-FILE (MVP-cut from 8 rows) | ✅ |
| 100 | T5 binding-NEGATIVE guard against spaarke-insights-index (MVP-cut from 3 wrappers) | ✅ |
| 103 | Phase 4 integration test suite | ⏭️ DEFERRED with coverage map (see `notes/handoffs/103-coverage-deferral.md`) |
| 104 | Phase 4 deploy | ⏭️ DEFERRED per task 026/054 owner pattern |
| **105** | **Phase 4 exit gate (this evidence)** | **✅** |

### Deferred per Q5b cut (30 tasks; lock-ins documented for future restoration)

The deferred 30 tasks fall into these categories:
- **4a PaneEventBus `memory` channel** (5 tasks) — channel exists in ADR-030 v2; subscribers/publishers deferred
- **4c Per-turn prompt assembly refactor** (4 tasks — 075/076/077/079) — single-pipeline structure preserved by task 078; plug-in points documented
- **4d 7 additional tool handlers** (083/084/086/087/088/089/090) — recall_session_file is the one excellent MVP tool
- **4e DI for the 7 deferred handlers** (covered by task 091 MVP-cut)
- **4f Phase 4 integration test** (deferred via task 103 coverage map)
- **Various wave-specific tests/wiring** depending on deferred handlers

---

## FR satisfaction matrix (MVP-cut)

### WP5 Functional Requirements

| FR | Description | MVP status | Evidence |
|---|---|---|---|
| FR-25 | sprk_aichatmessage retire-to-write-only | 🟡 PRE-EXISTING (pre-MVP) | R6 work; not retired in MVP; audit container remains canonical reader |
| FR-26 | chat /summarize convergence point | ✅ PRESERVED | Task 078 single-pipeline lock-in; FR-26 invariant test in task 080 |
| FR-27 | No parallel pipelines | ✅ COVERED | Task 078 audit found 0 duplicate composition seams; FR-27 invariant test in task 080 |
| FR-28 | T2 session memory + upload pipeline | ✅ MVP-SCOPED | Tasks 071+072+074 — file upload persistence chain |
| FR-29 | recall_session_file citation enforcement | ✅ COVERED | Task 085 39 unit tests + requireCitations=true default + NO_CITATIONS_AVAILABLE error |
| FR-30 | recall_session_file 6 purpose × 5 scope | ✅ COVERED | Task 085 6×5 Theory matrix (30 tests) |
| FR-31 | per-purpose retrieval semantics | ✅ COVERED | Task 085 documented per-purpose behavior + tests |
| FR-32 | promote_to_matter_memory full chain | 🚫 DEFERRED | Task 088 promotion handler deferred per Q5b |
| FR-33 — FR-35 | Additional tool surfaces | 🚫 DEFERRED | Tasks 083/084/086/087 deferred per Q5b |
| FR-36 | Chat-domain index isolation | ✅ COVERED | Task 100 binding-NEGATIVE guard (4 tests + grep enforcement) |
| FR-37 | T6 audit telemetry | 🟡 PARTIAL | Task 074 4 events emitted at existing sites + 3 contract-skipped at deferred services |
| FR-42 | Pinned-never-drops invariant | ✅ PRESERVED | MemoryCompositionService.ComposeAsync (R6 work; FR-42 logic unchanged) |
| FR-45 | PlaybookChatContextProvider matter-memory invocation | ✅ COVERED | Task 080 dedicated regression test class (3 tests: behavioral Moq.Verify + source-text + graceful no-matter-context) |

### Architectural NFR-A1 through NFR-A7 (architecture §2 binding principles)

| Principle | Status | Evidence |
|---|---|---|
| **P1** Single per-turn composer | ✅ | Task 078 single-pipeline lock-in |
| **P2** Insights vs Chat boundary | ✅ | Task 100 binding-NEGATIVE guard on spaarke-insights-index |
| **P3** Citation-bearing trust framing | ✅ | Task 085 requireCitations=true default; NO_CITATIONS_AVAILABLE error path |
| **P4** No new chat-memory AI Search index | ✅ | Task 100 audit; no new index added |
| **P5** Cosmos doc-type extension over new container | ✅ | Task 071/072 ChatSessionFile + StoredUploadedFile extension; no new container |
| **P6** Append-only audit | ✅ | T6 audit container writes from R6 unchanged |
| **P7** Tier-1 telemetry only | ✅ | Task 074 method-per-event-type API structurally prevents content leakage; ADR-015 absence assertions in tests |

---

## Binding-NEGATIVE rule audit

| Rule | Status | Evidence |
|---|---|---|
| `spaarke-insights-index` read from chat-memory paths | ✅ ZERO | Task 100 grep enforcement (Services/Ai/Memory/, Services/Ai/Handlers/Recall*) + binding guard fail-fast |
| `MultiIndexComposer` reuse from chat-memory | ✅ N/A | Not introduced in MVP; deferred handlers would have triggered this — not shipped |
| `InsightsOrchestrator` reuse from chat-memory | ✅ N/A | Same |
| `sprk_matter.sprk_performancesummary` write from chat-memory | ✅ N/A | Same |
| New chat-memory AI Search index | ✅ ZERO | Architecture §4.5 binding upheld |
| New Cosmos container (architecture §7.1) | ✅ ZERO | Task 071/072 extended StoredSession; no new container |

---

## Quality gates (cumulative through Phase 4 MVP)

| Check | Result |
|---|---|
| BFF build | ✅ 0 errors, 17 warnings (1 pre-existing-style noise from Phase 4 work; below tolerance) |
| All Phase 4 unit tests | ✅ Substantial coverage: task 085 (39), 091 (7+1 integration), 100 (4) = 51+ new tests |
| Phase 1 regression suite | ✅ 10/10 pass throughout every Phase 4 commit |
| Cumulative BFF publish (compressed) | ✅ **48.89 MB** (11.11 MB headroom under 60 MB NFR-01 ceiling; **+2.80 MB delta vs Phase 3 closeout's 46.09 MB** — well under +5 MB per-task escalation threshold averaged over 10 tasks) |
| FR-45 invariant test (task 080) | ✅ 3/3 pass — behavioral + source-text + graceful no-matter-context |
| ADR compliance (013, 014, 015, 029, 010, 032, 033) | ✅ All checks pass; ADR-015 telemetry-safety structural enforcement via method-per-event-type signatures (task 074) |
| Sub-agent dispatch (E2 pattern) | ✅ 19 sub-agents through Phase 4 / 0 stalls |

---

## Tasks 103 + 104 deferral pattern

Following the established precedent from Phase 1 (task 026) and Phase 3 (task 054):

- **Task 103 (integration test suite)**: ⏭️ DEFERRED with comprehensive coverage map at `notes/handoffs/103-coverage-deferral.md` — existing unit + integration coverage (task 085 + 091 + 100) covers the MVP surface; deferred surface coverage is contingent on deferred handler tasks landing
- **Task 104 (Phase 4 deploy to bff-dev)**: ⏭️ DEFERRED per task 026/054 owner pattern — managed deploy window. Phase 4 code verified locally:
  - BFF build: 0 errors
  - 51+ new tests pass across all Phase 4 MVP areas
  - 48.89 MB publish (within NFR-01)
  - Binding-NEGATIVE guards in place
  - FR-45 invariant tests still green

When the deploy window opens, the deploy script ships the cumulative state of commits through this exit gate. Phase 4 changes are largely additive (new handler + 8 new ChatSessionFile fields + new tracker + new model property + DI registration); the only behavior change is the `RagService` binding-NEGATIVE guard which fail-fasts on misconfiguration.

---

## Phase 5 unblocked

The Phase 4 MVP exit gate is **GO**. Phase 5 (WP2 File-Aware Classification MVP — 6 tasks per Q5b cut) may begin. The MVP cut removed the auto-routing engine; suggested-playbooks UX is preserved.

When the deferred Phase 4 handlers land in a future ramp, Phase 4 ramps to its full 42-task scope, and the deferred FRs (FR-32 / FR-33 / FR-34 / FR-35 + frontend-bound coverage of FR-37) graduate to ✅.

---

## Project-level milestone

- 32+ commits this session on `work/spaarke-ai-platform-chat-routing-redesign-r1`
- Phase 1 ✅ + Phase 2 partial ✅ (037 done; 033/035 🟡 Power Apps; 038-040 pending) + Phase 3 ✅ + Phase 4 MVP ✅
- PR #409 status at last known good: GREEN at `3810e6f51` (build hotfix + timing relaxation landed before this exit gate's commits)
- 19 sub-agent dispatches / 0 stalls across the session (E2 code-only pattern proven reliable)

---

## Related artifacts

- Phase 4 commits: `9ad2244cc` (071) → `ae7785bdd` (072) → `7dfa148cf` (074) → `21c199a77` (078) → `a1317bff4` (080) → `3810e6f51` (085) → `1cfb392a4` (091) → `d59517274` (092) → `fec3506f8` (100)
- Coverage deferral: `notes/handoffs/103-coverage-deferral.md`
- Phase 3 analogue: `notes/handoffs/055-phase-3-exit.md`
- MVP cut rationale: `notes/handoffs/032-loader-gap-and-036-bundling.md`
- FR-45 evidence: `notes/handoffs/078-fr-45-line-verified.md`
- Task 074 telemetry partial: commit `7dfa148cf` body + handoff text
- Q5b decision lock-ins: `current-task.md` "Critical decisions this session" table
