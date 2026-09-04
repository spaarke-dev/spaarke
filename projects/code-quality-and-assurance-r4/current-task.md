# Current Task State — Code Quality & Assurance R4

> **Last Updated**: 2026-09-04 (by context-handoff)
> **Recovery**: read "Quick Recovery" first — it is sufficient to resume.
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none — project initialized, no task started |
| **Step** | — |
| **Status** | not-started |
| **Execution** | **AUTONOMOUS** — do not ask between waves |
| **Next Action** | **(1)** `/worktree-sync` Update Only — branch is **36 commits behind master**; several tasks measure the tree at head. **(2)** Amend `spec.md` FR-10 to the revision-header standard (see below) — plan.md risk **R4**, must land before P3. **(3)** `/conflict-check` for `.claude/` and `.github/workflows/`. **(4)** `task-execute` on `tasks/001-adr-012-amendment-enumerate-shared-set.poml`, then run the waves in `tasks/TASK-INDEX.md` without stopping for confirmation. |

### Files Modified This Session

All committed — working tree clean. Commits `36b13d702` (pipeline) and the posture follow-up.

- `projects/code-quality-and-assurance-r4/plan.md` — Created — WBS, phases, §3.5 autonomous contract, risk register
- `projects/code-quality-and-assurance-r4/CLAUDE.md` — Created — project AI context + autonomous posture + FR-10 amendment
- `projects/code-quality-and-assurance-r4/current-task.md` — Created — this file
- `projects/code-quality-and-assurance-r4/tasks/*.poml` — Created — 33 task files, lint-clean
- `projects/code-quality-and-assurance-r4/tasks/TASK-INDEX.md` — Created — waves, deps, critical path
- `projects/code-quality-and-assurance-r4/README.md` — Modified — status + stamp-claim accuracy fix
- `projects/INDEX.md` — Modified — r4 registry row

### Critical Context

`/project-pipeline` ran Steps 0–4 and generated everything; **no task has executed yet**. Two things a
fresh session must know before starting: **P2 is split** (P2a classifies; P2b is sized by task 020 and
decomposed by re-running `/task-create`), and **FR-10 was replaced** by a revision-header standard, but
`spec.md` still describes the superseded version — fix that first.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none |
| **Task File** | next up: `tasks/001-adr-012-amendment-enumerate-shared-set.poml` |
| **Phase** | next up: P1 — The shared surface is knowable |
| **Status** | not-started |
| **Started** | — |

---

## How to run this project (autonomous contract)

Owner direction 2026-09-04: **run autonomously as long as it is safe and accurate.** Full contract in
[plan.md §3.5](plan.md).

- Dispatch each task via `task-execute` at its declared `<model-tier>`/`<effort>`; run Step 9.5 gates; mark ✅; continue.
- **Build between waves**: any `.cs` → `dotnet build Spaarke.sln` (the **solution** — test projects glob shared sources, so a green single-project build proves nothing). A red build **stops the run**.
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

*No task steps completed — pipeline initialization only.*

### Files Modified (All Task)

*No task files modified yet.*

### Decisions Made

- 2026-09-04: **Split P2** into P2a (010–013, classify) and P2b (020 sizes it, then `/task-create` re-runs). FR-07's size is unknown until FR-05 completes. Resolves spec Unresolved Question 1, which blocked decomposition. (Owner)
- 2026-09-04: **Autonomous execution** — supersedes the initialize-only posture chosen earlier the same day. Escalation triggers are the stop conditions. (Owner)
- 2026-09-04: **FR-10 replaced** by a repo-wide revision-header standard — top-of-file, human readable, `version` + `revision-type` beyond the dates, applied **by script not LLM**. (Owner)
- 2026-09-04: **Frontmatter over blockquote** — skills cannot drop frontmatter (Claude Code parses `description`/`tags`/`appliesTo`), so a blockquote standard would leave all 71 skills carrying two headers. (Analysis)
- 2026-09-04: **Header scope is `.claude/**` in r4**; `docs/**` (324 files) deferred to the same script with a different `-Path` — a 554-file diff across both trees while ~17 worktrees are active is the collision NFR-06 warns about. (Analysis)

---

## The FR-10 amendment (do this before P3)

`spec.md` FR-10 asks for "a parseable `last-reviewed`; ~110 files gain a first stamp (0/16 constraints,
0/94 patterns)". **That count is true only for YAML frontmatter.** Measured 2026-09-04:

| Surface | frontmatter `last-reviewed:` | blockquote `> **Last Reviewed**:` |
|---|---|---|
| skills (71) | 63 | — |
| constraints (16) | **0** | **15** |
| patterns (94) | **0** | **87** |

The files are **not undated** — the repo has two competing conventions, and only ~9 files genuinely lack a
date. The work is a format standardisation, not a 110-file authoring job.

**The standard** (task 030 → `docs/standards/FILE-REVISION-HEADER.md`):

```yaml
---
version: 1.0
status: active
revision-type: baseline
last-updated: 2026-08-14
last-reviewed: 2026-05-17
reviewed-by: ai-procedure-quality-r1
---
```

Only `version` and `revision-type` are new. Bump rules: `major` = a MUST/MUST NOT or the rule's meaning
changed · `minor` = content added/clarified · `editorial` = typo/link, no version bump · `baseline` =
applied retroactively · `initial` = first authored under the standard.

**`last-updated` and `last-reviewed` must stay distinct** — task 037's auto-bump moves only
`last-reviewed`, so the stamp honestly means *verified nothing changed*. Collapsing them breaks the
three-tier model.

Applied by `scripts/quality/Update-DocHeader.ps1` (task 031): preserves existing dates, derives
`last-updated` from `git log` where absent, seeds `version`/`revision-type`, removes migrated blockquote
lines, **adds keys only** for skills, idempotent, `-Check` mode for CI.

> **Obligation**: amend `spec.md` FR-10 to match, or record the divergence explicitly. Tracked as plan.md **R4**.

---

## Blockers

**Status**: None. R4 is a prerequisite for P3, not a blocker on P1.

---

## Session Notes

### Key Learnings

- **Two-convention stamp finding** — the basis for the FR-10 rewrite (table above).
- **`nightly-quality.yml` is claimed by four docs, not three** — `docs/procedures/DEPENDENCY-MANAGEMENT.md` is the one spec FR-24 missed. Carried into task 055.
- **Task 040 was a caught defect** — it emits `.claude/catalogs/shared-export-index.json`, so it is main-session-only despite its script living under `scripts/`. The artifact determines the write boundary, not the script. P4-A parallel group withdrawn.
- **Pre-flight build 2026-09-04**: `dotnet build Spaarke.sln` → 0 errors, 5 pre-existing CA2024 warnings.
- **POML lint**: 33/33 clean, 0 errors, 0 warnings.

### Live hot-path collisions

- **PR #894** `ci/tier2-unit-scope` — draft, held for the CI shadow window, edits `.github/workflows/`. Affects tasks 013, 035, 041, 054, 056, 058.
- **`unified-access-control-r2`** PR #939 — executing, `skill-directives=Y`, edits the same `task-create`/`code-review` SKILL.md files. Affects tasks 038, 042, 043, 051, 057.

### Handoff Notes

Everything is committed and pushed to PR [#935](https://github.com/spaarke-dev/spaarke/pull/935) on
`work/code-quality-and-assurance-r4` (HEAD `235ec7de3`, working tree clean). A fresh session needs only
this file plus `CLAUDE.md` and `tasks/TASK-INDEX.md` to start executing.

⚠️ **Re-sync before executing.** The branch was level with `origin/master` at pipeline time but master has
since moved — **36 commits behind as of 2026-09-04**. Run `/worktree-sync` (Update Only) first: task 003's
baseline and task 044's divergence register both measure the tree at head, and task 010 classifies ADRs
against current code, so stale inputs produce wrong answers rather than merge conflicts.

**Not done by the pipeline**: the `/devops-project-sync` portfolio hook (writes to GitHub Project #940) —
run it if you want the board current.

---

## Quick Reference

### Project Context

- **Project**: code-quality-and-assurance-r4 · **Branch**: `work/code-quality-and-assurance-r4`
- **CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md) · **Plan**: [`plan.md`](./plan.md) · **Tasks**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs

- **ADR-012** Shared Component Library — **amended by task 001** (§6.5 path B, pre-declared in spec ADR Tensions)
- **ADR-038** Testing Strategy — governs every test added: positive **and** negative controls, no DI resolution (ban B3), reuse `SourceScan`

### Standing constraints (every task)

Exactly **one** new workflow project-wide (NFR-04) · **no threshold** on test count, duplication %, or file
size · reuse `SourceScan`, never fork · Class-1 artifacts generated never hand-authored · never scan
`.claude/worktrees/` or sibling worktrees · `.claude/`-touching tasks are **main-session only**.

---

## Recovery Instructions

1. Read **Quick Recovery** above (< 30 seconds)
2. Read **How to run this project** for the autonomous contract
3. Load `tasks/TASK-INDEX.md` for the wave order
4. Start: `task-execute` on `tasks/001-adr-012-amendment-enumerate-shared-set.poml`

**Commands**: `/project-continue` · `/context-handoff` · "where was I?"

---

*This file is the primary source of truth for active work state. Keep it updated.*
