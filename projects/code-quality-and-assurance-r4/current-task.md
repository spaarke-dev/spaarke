# Current Task State — Code Quality & Assurance R4

> **Last Updated**: 2026-09-04 (by context-handoff)
> **Recovery**: read "Quick Recovery" first — it is sufficient to resume.
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 003 — **BLOCKED on an operator decision**. Tasks 001 ✅ and 002 ✅ complete. |
| **Step** | 003 step 6 of 8 — five measures written, measure (c) withheld |
| **Status** | blocked (escalation trigger fired — a legitimate outcome, not a failure) |
| **Execution** | **AUTONOMOUS** — resume without per-wave confirmation once the decision below is answered |
| **Next Action** | **(1)** Operator answers the fan-in recipe question in [`notes/task-003-escalation.md`](notes/task-003-escalation.md) — recommendation is **option A (R1, `package.json` declarations)**. **(2)** Write measure (c) into `notes/baseline-2026-09.md` with the chosen command, flip 003 to ✅. **(3)** Continue to P2a task 010 and run the waves without stopping. **P2a does not depend on measure (c)** — it may start before the decision lands. |

### Files Modified This Session

All committed and pushed to PR [#935](https://github.com/spaarke-dev/spaarke/pull/935). HEAD `cf7633587`, tree clean, 0 unpushed, 0 behind master.

- `projects/.../spec.md` — Modified — FR-10 rewritten (revision-header standard); Owner Clarifications +3; Unresolved Q1+Q3 resolved
- `projects/.../plan.md` — Modified — risk R4 discharged
- `.claude/adr/ADR-012-shared-components.md` — Modified — **task 001**: closed 15-package enumeration + three promotion questions + §6.5 path-B note
- `docs/adr/ADR-012-shared-component-library.md` — Modified — v2.2 revision row (pointer, not a restatement)
- `tests/Spaarke.ArchTests/SharedPackageCensusTests.cs` — **Created** — **task 002**: 8 tests, 4 negative controls
- `tests/Spaarke.ArchTests/SourceScan.cs` — Modified — extended with `SharedClientPackageDirectories()`
- `projects/.../notes/baseline-2026-09.md` — **Created** — **task 003**: 5 of 6 measures
- `projects/.../notes/task-003-escalation.md` — **Created** — the blocking decision
- `projects/.../notes/task-00{1,2}-deviations.md` — **Created** — the corrections below
- `projects/.../tasks/{TASK-INDEX.md,001,002,003}` — Modified — statuses ✅ ✅ 🔄

### Critical Context

P1 is **functionally complete**: the shared surface is enumerated (001) and a 16th package now fails the
build (002). Only measure (c) of the baseline is outstanding, and it is blocked on a **choice**, not on
work. **Three separate factual errors in the authored POMLs were caught by measuring rather than trusting**
— see Decisions below. Assume other POML notes carry stale figures; measure before relying on one.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 003 |
| **Task File** | `tasks/003-publish-governance-baseline.poml` |
| **Phase** | P1 — The shared surface is knowable |
| **Status** | blocked |
| **Started** | 2026-09-04 |

---

## How to run this project (autonomous contract)

Owner direction 2026-09-04: **run autonomously as long as it is safe and accurate.** Full contract in
[plan.md §3.5](plan.md).

- Dispatch each task via `task-execute` at its declared `<model-tier>`/`<effort>`; run Step 9.5 gates; mark ✅; continue.
- **Build between waves**: any `.cs` → `dotnet build Spaarke.sln`. Note **`tests/Spaarke.ArchTests` is NOT in `Spaarke.sln`** — it must be run separately (`dotnet test tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj`). A green solution build says nothing about it.
- **A task failing its own verification is not a stop.** Fix and re-run, or mark 🔄 and continue with the wave.

**Hard stops (decisions, not obstacles)**

| Where | Condition |
|---|---|
| 012 | Stale/contested ADR governing auth, security, tenant isolation, or compliance — CLAUDE.md §6.5 **requires** human sign-off |
| 012 | Path-B amendment changing a rule other active worktrees depend on |
| 020 | Criterion set empty, or so large P2b exceeds the rest of the project |
| 052 | Headless runner fails — P5 drops FR-23; do **not** build an alternative |
| any | `/conflict-check` shows another worktree editing the same file |
| any | A fired `<escalation><trigger>` — a legitimate outcome, not a failure. Do not retry past it. |

---

## Progress

### Completed Steps

- ✅ `/worktree-sync` Update Only — merged 38 commits from master, pushed, 0 behind
- ✅ `spec.md` FR-10 amended to the revision-header standard (plan.md risk **R4** discharged)
- ✅ `/conflict-check` — **PR #939 merged**, so the skill-directives collision is gone; PR #894 touches only `ci-tier2-advisory.yml` (not r4's file); no sibling worktree has uncommitted work on `.claude/`, `.github/workflows/`, or `tests/Spaarke.ArchTests/`
- ✅ Pre-flight `dotnet build Spaarke.sln` — 0 errors, 5 pre-existing CA2024 warnings
- ✅ **Task 001** — ADR-012 amended (closed enumeration + three non-gate questions + §6.5 path-B note)
- ✅ **Task 002** — `SharedPackageCensusTests` (8 tests); ArchTests **199/199**; empirically verified against a real 16th directory
- 🔄 **Task 003** — 5 of 6 measures published; (c) escalated

### Decisions Made

- 2026-09-04: **Split P2**, **autonomous execution**, **FR-10 replaced**, **frontmatter over blockquote**, **header scope is `.claude/**`** (all Owner/Analysis, carried forward from pipeline)
- 2026-09-04: **`SourceScan` extended, not forked** — added `SharedClientPackageDirectories()` following the precedent by which `TestSourceFiles()` was added. The fork escalation did **not** fire; a private walker would have duplicated `ResolveRepoRoot()`, the fragile part. (Analysis)
- 2026-09-04: **Task 002's false-premise criterion replaced, not waived** — its intent (prove directory-keying) is load-bearing; only its example was wrong. (Analysis)
- 2026-09-04: **The application allow-list is asserted, not used as a filter** — none of the four names lives in the scanned tree, so as a filter it would match nothing. (Analysis)

### Blockers

**One.** Measure (c)'s canonical recipe — see [`notes/task-003-escalation.md`](notes/task-003-escalation.md).
Recommendation: **option A (R1)**. Does **not** block P2a.

---

## Session Notes

### Key Learnings

- **Three authored POML facts were wrong, all caught by measuring.** (1) "`Spaarke.LegalWorkspace` has no `package.json`" — all 15 do; it appeared 5× across tasks 001 and 002 including in an acceptance criterion. (2) "`@spaarke/visuals` has 0 consumers" — it has 1. (3) `spec.md`'s fan-in figures are irreproducible. **Do not trust a figure in a POML `<notes>` or `<constraint>` without re-measuring it.**
- **FR-04 proved itself on day one** — a number recorded on 2026-09-03 without its command was unverifiable on 2026-09-04.
- **`tests/Spaarke.ArchTests` is not in `Spaarke.sln`** (stated in its own `.csproj` comment). Any wave touching it needs its own `dotnet test`.
- **~32% of `<extension>` answers across 470 justifications lead with prose, not a verdict** — relevant to any future mechanism parsing that field.
- **ADR citation ranks 4/5 swapped on a 6-of-1,520 gap.** FR-07 consumes the *ordering*; treat those two as tied.
- Task 040 remains main-session-only (writes `.claude/catalogs/`); P4-A parallel group stays withdrawn.

### Live hot-path collisions

- **PR #894** `ci/tier2-unit-scope` — DRAFT, held for the shadow window. Touches only `.github/workflows/ci-tier2-advisory.yml`, `Spaarke.sln`, `per-pr-tests.slnf`. **No file overlap** with r4's planned single new workflow — but re-check before P3 merges.
- **`unified-access-control-r2` PR #939 — MERGED 2026-09-04.** The skill-directives collision affecting tasks 038, 042, 043, 051, 057 is **resolved**.
- **`customer-provisioning-orchestration-r1`** (active) has unmerged edits to `.claude/adr/ADR-028`, `.claude/constraints/provisioning.md`, `.claude/patterns/provisioning/*`, `.claude/skills/provision-environment/SKILL.md` — **will collide with P3's header backfill (task 032)**, which rewrites headers across all `.claude/` primitives. Mitigation stands (plan.md R7): the script is idempotent, so re-running after that branch merges is free.

### Handoff Notes

Everything is committed and pushed. A fresh session needs only this file plus `CLAUDE.md` and
`tasks/TASK-INDEX.md`. **Answer the measure-(c) question first, or start P2a and answer it in parallel.**

---

## Quick Reference

### Project Context

- **Project**: code-quality-and-assurance-r4 · **Branch**: `work/code-quality-and-assurance-r4`
- **CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md) · **Plan**: [`plan.md`](./plan.md) · **Tasks**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs

- **ADR-012** Shared Component Library — **amended by task 001** (§6.5 path B) → now v2.2, closed 15-package set
- **ADR-038** Testing Strategy — governs every test added: positive **and** negative controls, no DI resolution (ban B3), reuse `SourceScan`

### Standing constraints (every task)

Exactly **one** new workflow project-wide (NFR-04) · **no threshold** on test count, duplication %, or file
size · reuse `SourceScan`, never fork · Class-1 artifacts generated never hand-authored · never scan
`.claude/worktrees/` or sibling worktrees · `.claude/`-touching tasks are **main-session only**.

---

## Recovery Instructions

1. Read **Quick Recovery** above (< 30 seconds)
2. Read [`notes/task-003-escalation.md`](notes/task-003-escalation.md) — the one open decision
3. Load `tasks/TASK-INDEX.md` for the wave order
4. Start: `task-execute` on `tasks/010-classify-49-adrs-three-axes.poml` (P2a, **opus/xhigh**)

**Commands**: `/project-continue` · `/context-handoff` · "where was I?"

---

*This file is the primary source of truth for active work state. Keep it updated.*
