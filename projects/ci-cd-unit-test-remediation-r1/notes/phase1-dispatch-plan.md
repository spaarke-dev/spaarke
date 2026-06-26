# Phase 1 (PG-1) Dispatch Plan

> **Author**: CICD-002 (`002-phase1-kickoff-coordination`)
> **Created**: 2026-06-26
> **Status**: ACTIVE — refines `TASK-INDEX.md` §PG-1 with empirical wave-progress observations
> **Scope**: PG-1 dispatch only (10 Phase 1 parallel-safe tasks across Streams A/B/C + cross-cutting)

---

## TL;DR

Phase 1 (PG-1) is being dispatched in **three waves**:

| Wave | Status | Mode | Tasks | Concurrency |
|---|---|---|---|---|
| **1A** | done | sub-agent (parallel) | `010, 011, 012, 020, 023` | 5 parallel |
| **1B** | in progress | sub-agent (parallel) | `030, 001, 002` | 3 parallel |
| **1C** | next | main-session (sequential) | `024 → 022 → 031 → 021` | 1-at-a-time |

PG-1 completion gate is unchanged: all 10 Phase 1 tasks done → unblocks PG-2, PG-3, SERIAL-DEL.

---

## Wave 1A — done (sub-agent dispatch, parallel)

**Tasks**: `010, 011, 012, 020, 023`

| # | Task | Output | `.claude/` write? |
|---|---|---|---|
| 010 | catalog-sdap-ci-failures | `notes/sdap-ci-failure-catalog.md` | no |
| 011 | measure-baseline-p50-p95 | `notes/baseline-metrics.md` | no |
| 012 | router-signal-model-spike | `notes/router-signal-model-decision.md` | no |
| 020 | test-inventory-csv | `notes/test-inventory.csv` + summary | no |
| 023 | draft-TEST-ARCHITECTURE-md | `docs/standards/TEST-ARCHITECTURE.md` (draft) | no |

**Rationale**: All five are diagnosis / docs tasks writing to `notes/`, `docs/standards/`, or `docs/architecture/`. None touch `.claude/`. Sub-agents safe per root CLAUDE.md §3 write boundary. Five parallel agents fit comfortably under the 6-per-wave hard limit.

**Evidence of completion**: `notes/` directory now contains all five outputs (verified by directory listing at 2026-06-26).

---

## Wave 1B — in progress (sub-agent dispatch, parallel)

**Tasks**: `030, 001, 002`

| # | Task | Output | `.claude/` write? |
|---|---|---|---|
| 030 | build-projects-INDEX-md | `projects/INDEX.md` | no |
| 001 | branch-protection-baseline-reuse-decision | `notes/branch-protection-decision.md` (+ possibly `notes/branch-protection-current.json`, already present) | no |
| 002 | phase1-kickoff-coordination | `notes/phase1-dispatch-plan.md` (this file) | no |

**Rationale**: Three coordination / docs tasks. Outputs land in `projects/` root and `notes/` — neither under `.claude/`. Sub-agents safe.

**Cross-stream concurrency observation**: Wave 1B spans Stream C (030) + Phase 1 cross-cutting (001, 002). No stream-A or stream-B work in this wave because:

- Stream A's remaining work (040-044) is Phase 2, dependent on `012` and `020` outputs from Wave 1A → belongs to PG-2.
- Stream B's remaining Phase 1 work (021, 022, 024) deferred to Wave 1C because two of them touch `.claude/`.

---

## Wave 1C — next (main-session, sequential)

**Tasks**: `024 → 022 → 031 → 021`

| # | Task | Output | `.claude/` write? | Why main-session-only |
|---|---|---|---|---|
| 024 | draft-ADR-038-standalone | `.claude/adr/INDEX.md` (+ `.claude/adr/ADR-038-*.md` pointer; full ADR in `docs/adr/`) | **yes** | Root CLAUDE.md §3: sub-agents cannot write to `.claude/` |
| 022 | rewrite-constraints-testing-md | `.claude/constraints/testing.md` | **yes** | Same |
| 031 | update-conflict-check-skill-watchlist | `.claude/skills/conflict-check/SKILL.md` | **yes** | Same |
| 021 | rewrite-tests-CLAUDE-md | `tests/CLAUDE.md` | no, but bundled here | Small + fast main-session tail; not worth a fresh sub-agent dispatch after 024/022/031 finish |

**Ordering rationale**:

1. **024 first** — creates ADR-038 (standalone testing strategy ADR). The ADR ID is referenced by 022's rewritten `.claude/constraints/testing.md` (line 25 misattribution fix per project CLAUDE.md "Decisions Made" 2026-06-25). Doing 024 first means 022 can cite the live `.claude/adr/INDEX.md` entry rather than a forward-reference.
2. **022 second** — rewrites `.claude/constraints/testing.md` with corrected ADR-038 reference and path-MUST rules consumed by task `050` (Stream B path reorg in Phase 2).
3. **031 third** — updates `.claude/skills/conflict-check/SKILL.md` watchlist. Independent of 024/022 content but in the same `.claude/` write-boundary group. PG-3 (Phase 2 Stream C) chains 060/061/062 after 031, so 031 is the hand-off point between Phase 1 and Phase 2 sequential `.claude/` work.
4. **021 last** — rewrites `tests/CLAUDE.md`. No `.claude/` write per se (lives under `tests/`), but small enough that paying a sub-agent context-switch cost is wasteful; tucked into the main-session tail.

**Why sequential (not parallel)**: Per root CLAUDE.md §3, sub-agents launched via the Agent tool cannot write to `.claude/`. The three `.claude/`-touching tasks (024, 022, 031) MUST execute in the main session. Main session is single-threaded, so they run sequentially. Adding 021 to the tail keeps the wave coherent without spinning up a sub-agent for a single 5-minute task.

---

## Concurrency model summary

```
Time →
Wave 1A (sub-agents, parallel):  [010 011 012 020 023]  ← done
Wave 1B (sub-agents, parallel):  [030 001 002]           ← in progress
Wave 1C (main-session, serial):  [024 → 022 → 031 → 021] ← next
```

Wave 1A + 1B + 1C can in principle overlap (no file conflicts between waves), but the project is dispatching them as discrete waves to keep the dispatch ledger simple and to ensure Wave 1A outputs (especially `010, 011, 020`) are observable before any Wave 1C task starts citing them.

---

## Reasoning recap: sub-agent vs main-session

Per root CLAUDE.md §3 (sub-agent write boundary):

> Sub-agents launched via the Agent tool CANNOT write to `.claude/` paths (skills, patterns, constraints, catalogs, agents, settings).

Phase 1 task classification follows directly:

- **Sub-agent-safe** (any path EXCEPT `.claude/`): `010, 011, 012, 020, 023, 030, 001, 002, 021`
- **Main-session-only** (writes under `.claude/`): `024, 022, 031`

The 9 sub-agent-safe tasks are split into Wave 1A (5) and Wave 1B (3 dispatched + 1 pulled into Wave 1C tail = 021). The 3 main-session-only tasks form the head of Wave 1C.

The 6-per-wave hard limit (from `task-execute` skill) is not binding here — actual concurrency is 5 (Wave 1A) / 3 (Wave 1B) / 1 (Wave 1C).

---

## What this plan does NOT cover

- **PG-2** (Phase 2 Stream A workflows: 040–044) — independent of `.claude/`; will dispatch as a single sub-agent wave of 5 after PG-1 + dependencies (012 → 040, 020 → none Phase 2). See TASK-INDEX.md §PG-2.
- **PG-3** (Phase 2 Stream C: 060, 061, 062) — main-session sequential, chains after Wave 1C's 031. See TASK-INDEX.md §PG-3.
- **SERIAL-DEL** (Phase 2 Stream B: 050 → 051 → 052 → 053a → 053b → 053c) — strict serial with master rebases between PRs. Gated by 020 + 022 outputs (Wave 1A + Wave 1C). See TASK-INDEX.md §SERIAL-DEL.

---

## Acceptance criteria (CICD-002)

- [x] Dispatch plan note exists with concrete wave assignments → this file
- [x] `.claude/` write-boundary tasks identified → 024, 022, 031 (main-session-only)
- [x] Wave concurrency within 6-per-wave hard limit → max 5 (Wave 1A)
- [x] Ordering within Wave 1C justified → 024 first (ADR-038 ID), 022 second (cites 024), 031 third (PG-3 handoff), 021 last (tail)
