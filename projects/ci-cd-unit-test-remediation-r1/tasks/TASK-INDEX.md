# Task Index — `ci-cd-unit-test-remediation-r1`

> **Auto-updated by task-execute when tasks transition status**
> **Mode**: 🤖 **AUTONOMOUS** — no approval gates; tasks auto-advance to next available
> **Created**: 2026-06-26 (pipeline-initialized)

## Legend

| Marker | Meaning |
|---|---|
| 🔲 | not-started |
| 🔄 | in-progress |
| ✅ | completed |
| ⏸ | blocked |
| 🚫 | cancelled |

## Status Summary

- **Total tasks**: 25
- **Completed**: 20 of 25 (80%) — Phase 1 + Phase 2 (with reorganization scope adjustments)
- **In progress**: 0
- **Blocked**: 0
- **Not started**: 5 (Phase 3: 070, 071, 075, 076, 077 + wrap-up: 090)
- **Skipped/cancelled**: 1 (000-preflight; pipeline covered) + 3 (051, 052, 053c — cancelled-no-scope per inventory finding)
- **Complete-partial / complete-merged**: 1 (050 scaffolded; bulk move deferred) + 2 (053a/b merged into single 053 PR)

## Task Registry

### Phase 0 — Pre-flight

| # | Task | Rigor | Status | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 000 | preflight-baseline-build | MINIMAL | ⏭️ skipped (pipeline covered) | false | — |

### Phase 1 — Diagnose + directive rewrites (cross-cutting) ✅

| # | Task | Rigor | Status | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 001 | branch-protection-baseline-reuse-decision | STANDARD | ✅ | true | — |
| 002 | phase1-kickoff-coordination | MINIMAL | ✅ | true | — |

### Phase 1 — Stream A (CI tiering diagnosis) ✅

| # | Task | Rigor | Status | Parallel-safe | Dependencies | Blocks |
|---|---|---|---|---|---|---|
| 010 | catalog-sdap-ci-failures | STANDARD | ✅ | true | — | — |
| 011 | measure-baseline-p50-p95 | STANDARD | ✅ | true | — | — |
| 012 | router-signal-model-spike | STANDARD | ✅ | true | — | 040 |

### Phase 1 — Stream B (test reset directives + ADR + standards) ✅

| # | Task | Rigor | Status | Parallel-safe | Dependencies | Blocks |
|---|---|---|---|---|---|---|
| 020 | test-inventory-csv | STANDARD | ✅ | true | — | 050, 051, 052, 053a, 053b, 053c |
| 021 | rewrite-tests-CLAUDE-md | FULL | ✅ | true | — | — |
| 022 | rewrite-constraints-testing-md | FULL | ✅ | true | — | 050 |
| 023 | draft-TEST-ARCHITECTURE-md | STANDARD | ✅ | true | — | — |
| 024 | draft-ADR-038-standalone | STANDARD | ✅ | true | — | — |

### Phase 1 — Stream C (INDEX + conflict-check watchlist) ✅

| # | Task | Rigor | Status | Parallel-safe | Dependencies | Blocks |
|---|---|---|---|---|---|---|
| 030 | build-projects-INDEX-md | STANDARD | ✅ | true | — | 061 |
| 031 | update-conflict-check-skill-watchlist | FULL | ✅ | **false** (`.claude/` write) | — | 060 |

### Phase 2 — Stream A (shadow workflows + Tier 3 + CD) ✅

| # | Task | Rigor | Status | Parallel-safe | Dependencies | Blocks |
|---|---|---|---|---|---|---|
| 040 | build-ci-router-yml | FULL | ✅ | true | 012 | — |
| 041 | build-ci-tier1-blocking-yml | FULL | ✅ | true | 012 | — |
| 042 | build-ci-tier2-advisory-yml | FULL | ✅ | true | — | — |
| 043 | augment-nightly-health-tier3 | FULL | ✅ | true | — | — |
| 044 | build-deploy-spaarke-ai-yml-and-validate-bff | FULL | ✅ | true | — | — |

### Phase 2 — Stream B (path scaffold + deletion — collapsed per inventory)

| # | Task | Rigor | Status | Parallel-safe | Dependencies | Blocks |
|---|---|---|---|---|---|---|
| 050 | fr-b05-path-reorganization | FULL | ⚠️ **complete-partial** (scaffolded; bulk move deferred — see `notes/path-reorganization-design.md`) | **false** | 020, 022 | (deletion not blocked by reorg) |
| 051 | delete-plugins-tests | FULL | 🚫 **cancelled-no-scope** (0 DELETE files per inventory) | — | — | — |
| 052 | delete-scheduling-tests | FULL | 🚫 **cancelled-no-scope** (0 DELETE files per inventory; flakes addressed separately) | — | — | — |
| 053a | delete-bff-mock-httpmessagehandler | FULL | ✅ **complete-merged** (9 files removed in single 053 PR) | **false** | 052 → bypassed | — |
| 053b | delete-bff-di-registration-and-null-checks | FULL | ✅ **complete-merged** (2 files removed in single 053 PR) | **false** | — | — |
| 053c | delete-bff-remaining-by-directory | FULL | 🚫 **cancelled-no-scope** (0 remaining DELETE files) | — | — | — |

> **✅ Sub-slicing revision applied (2026-06-26)**: per task 020 inventory finding of only 11 DELETE files total, sub-slicing 053a/b/c collapsed to a single 053 PR; 051 and 052 cancelled (no DELETE scope). Build verified green (0 errors, 18 pre-existing warnings). Critical path SERIAL-DEL chain reduces from 6 PRs to ~1 PR (this PR). Full post-mortem: `notes/post-deletion-summary.md`. Bulk path move (the deferred portion of 050) is a clearly-flagged follow-up — see `notes/path-reorganization-design.md` for csproj/namespace/sequencing strategy decisions.

### Phase 2 — Stream C (skill + bff-extensions + root CLAUDE) ✅

| # | Task | Rigor | Status | Parallel-safe | Dependencies | Blocks |
|---|---|---|---|---|---|---|
| 060 | update-task-execute-skill | FULL | ✅ (Step 0.5 conflict-check auto-invoke + Step 9.5 test-PR override) | **false** (`.claude/` write) | 031 | — |
| 061 | update-project-pipeline-skill | FULL | ✅ (Step 2 INDEX.md overlap + Step 3 hot-path-declaration requirement) | **false** (`.claude/` write) | 030 | — |
| 062 | update-bff-extensions-and-root-CLAUDE | FULL | ✅ (bff-extensions §G + root CLAUDE.md §8 §10 §17) | **false** (`.claude/` + root write) | 030, 031, 060, 061 | — |

### Phase 3 — Cutover + monitoring (STRICT SERIAL)

| # | Task | Rigor | Status | Parallel-safe | Dependencies | Blocks |
|---|---|---|---|---|---|---|
| 070 | pre-cutover-branch-protection-snapshot | FULL | 🔲 | **false** | 053c, 040, 041, 042, 043, 044, 060, 061, 062 | 071 |
| 071 | cutover-window | FULL | 🔲 | **false** | 070 | 075 |
| 075 | soak-7day-gate | STANDARD | 🔲 | **false** (gate: cutover+7d) | 071 | 077 |
| 077 | retire-sdap-ci-yml | STANDARD | 🔲 | **false** (gate: cutover+14d) | 075 | — |
| 076 | 30day-success-criteria-measurements | STANDARD | 🔲 | **false** (gate: cutover+30d) | 071 | 090 |

### Wrap-up

| # | Task | Rigor | Status | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 090 | wrapup-readme-lessons-cleanup | MINIMAL | 🔲 | **false** | 076, 077 |

---

## 🤖 Autonomous Execution — Parallel Group Dispatch

In autonomous mode, the agent automatically advances to the next available task without user confirmation. The waves below maximize concurrency within constraints.

### **PG-1** — Phase 1 (10 parallel-safe tasks, max 6 per wave per task-execute hard limit)

**Wave 1A (sub-agent dispatch, parallel):** `010, 011, 020, 023, 024`
- Five docs/diagnosis tasks. No `.claude/` writes. Sub-agents safe.

**Wave 1B (sub-agent dispatch, parallel):** `012, 021, 022`
- Spike + two test directive rewrites. No `.claude/` writes. Sub-agents safe.

**Wave 1C (main-session, sequential):** `001, 002, 030, 031`
- 001, 002, 030 are MINIMAL/STANDARD cross-cutting; 031 modifies `.claude/skills/conflict-check/SKILL.md` (main-session-only per root CLAUDE.md §3 write boundary).
- Recommended execution order within Wave 1C: `001 → 002 → 030 → 031`.

**Wave 1C and Waves 1A/1B can run concurrently** — no file overlap.

**PG-1 completion gate**: all 10 Phase 1 tasks done. Unblocks PG-2, PG-3, SERIAL-DEL.

### **PG-2** — Phase 2 Stream A (5 workflows, parallel-safe)

**Single wave (sub-agent dispatch, max 5 parallel):** `040, 041, 042, 043, 044`
- All independent `.github/workflows/*.yml` files. Different file targets.

### **PG-3** — Phase 2 Stream C (3 skill-directive tasks, MAIN-SESSION SEQUENTIAL)

**Sequential in main session:** `031 (Phase 1) → 060 → 061 → 062`
- Each task modifies `.claude/` paths (or root CLAUDE.md). Per root CLAUDE.md §3 sub-agents cannot write to `.claude/`. Must execute in main session, one at a time.
- `031` belongs to Phase 1 chronologically but is the same write-boundary group as 060/061/062.
- Order: `031 → 060 → 061 → 062`. (060 depends on 031; 061 depends on 030; 062 depends on all of 030, 031, 060, 061.)

### **SERIAL-DEL** — Phase 2 Stream B (strict serial; each rebases on master)

**Strict serial sequence:** `050 → 051 → 052 → 053a → 053b → 053c`
- Path reorganization (050) must complete before any deletion.
- Each deletion PR must rebase on master after the previous merges (deletion PRs touch same .csproj file, will conflict if parallel).
- 053c unblocks Phase 3 cutover (070).

### **SERIAL-CUTOVER** — Phase 3 (strict serial, with calendar gates)

**Strict serial with time gates:** `070 → 071 → 075 (7d gate) → 077 (14d gate)`
**Concurrent serial (independent timeline):** `071 → 076 (30d gate)`

### Cross-stream concurrency in Phase 2

```
Time →
PG-2 (workflows):     [040 041 042 043 044 — all parallel]
PG-3 (skills):        [031 → 060 → 061 → 062 — sequential main-session]
SERIAL-DEL (tests):   [050 → 051 → 052 → 053a → 053b → 053c]
```

All three lanes run concurrently. The critical path is SERIAL-DEL.

---

## Critical Path

`000 → 022 → 050 → 053c → 070 → 071 → 075 → 077 → 076 → 090` ≈ **28 elapsed days**

Where:
- `000`: 0.5d (or skip)
- `022`: directive rewrite (sets path-MUST rules consumed by 050)
- `050`: path reorg
- `051-053c`: serial deletion (4d total active)
- `070-071`: cutover prep + ~4h cutover
- `075`: 7-day soak gate
- `077`: 14-day gate (additional 7 days past 075)
- `076`: 30-day SC measurements gate
- `090`: wrap-up

---

## High-Risk Items (gate Phase 3)

| Risk | Mitigation |
|---|---|
| Tier 1 flake >1% after migration | Measured in shadow phase BEFORE cutover; if >1%, pause cutover (don't run 071) until re-triaged |
| Sub-slice 053a/b/c distribution wildly skewed | Boundaries revisable after 020 inventory; document if revised |
| `deploy-bff-api.yml` audit reveals NOT master-triggered | 044 becomes fix-task; estimate slips ~0.5d |
| Rollback triggered in 071 cutover window | Per spec §152: settings flips <15min; 070 snapshot is round-trip source; sdap-ci.yml still parallel (so retire 077 NOT yet executed); shadow workflows continue for diagnosis |

---

## Files Modified / Created (full list)

See [`plan.md` §6 Critical Files](../plan.md) for the canonical inventory. Touch summary:

- **`.github/workflows/`**: 4 new (router/tier1/tier2/deploy-spaarke-ai), 1 augmented (nightly-health), 1 audited (deploy-bff-api), 1 modified-then-deleted (sdap-ci)
- **`tests/`**: 6 new path-organized directories; ~430+ DELETE files removed across 5 sliced PRs; `tests/CLAUDE.md` rewritten
- **`.claude/`**: 4 skill files (task-execute, project-pipeline, conflict-check), 2 constraints (testing.md, bff-extensions.md), 1 ADR index update
- **`docs/`**: 1 new ADR (038), 1 new standard (TEST-ARCHITECTURE.md), 2-3 procedure updates, INDEX update
- **`projects/`**: this project + `projects/INDEX.md` (new)
- **Root `CLAUDE.md`**: §8 (rigor table), §10 (BFF Hygiene), §17 (pointers)
- **`scripts/`**: 1 new (validate-markdown-links.ps1)
- **`notes/`**: many transient/permanent (test-inventory, baseline-metrics, branch-protection snapshots, soak monitoring, SC measurements, lessons-learned, etc.)
