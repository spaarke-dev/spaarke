# Code Quality & Assurance R4 — AI Context

> **Purpose**: context for Claude Code when working on `code-quality-and-assurance-r4`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Initialized (initialize-only; execution operator-gated)
- **Last Updated**: 2026-09-04
- **Current Task**: Not started
- **Next Action**: resolve the FR-10 spec amendment (R4 below), then task 001

---

## Quick Reference

### Key Files

- [`spec.md`](spec.md) — 27 FRs, 7 NFRs, ADR tensions, scope estimate
- [`design.md`](design.md) — 13 findings, phase definitions, rejected alternatives
- [`plan.md`](plan.md) — WBS, phase breakdown, risk register
- [`README.md`](README.md) — overview + portfolio pointer
- [`current-task.md`](current-task.md) — **active task state** (context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task tracker + parallel groups
- [`docs/assessments/ai-native-development-model-2026-09-03.md`](../../docs/assessments/ai-native-development-model-2026-09-03.md) — the parent frame

### Project Metadata

- **Type**: Governance-layer quality program (no product surface)
- **Complexity**: Medium — no BFF source change; test + `.claude/` + one workflow
- **Portfolio**: [Project #940](https://github.com/spaarke-dev/spaarke/issues/940) under [Epic #427](https://github.com/spaarke-dev/spaarke/issues/427) · PR [#935](https://github.com/spaarke-dev/spaarke/pull/935)

---

## Context Loading Rules

1. **Always load this file first**
2. **Check `current-task.md`** for active work state (especially after compaction)
3. **Reference `spec.md` + `plan.md`** for FRs and phase constraints
4. **Load the task file** from `tasks/`
5. **Apply ADRs** via `adr-aware`

**Context Recovery**: [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: all task work MUST use the `task-execute` skill. Do NOT read POML files directly and
implement manually.

| User Says | Required Action |
|---|---|
| "work on task X" / "continue with task X" / "resume task X" | Execute task X via task-execute |
| "continue" / "next task" / "keep going" | Execute next pending task (first 🔲 in TASK-INDEX.md) |
| "pick up where we left off" | Load `current-task.md`, invoke task-execute |

Bypassing loses ADR constraints, checkpointing, and the Step 9.5 gates.

### 🚨 Project-specific execution rules

- **Every arch/census test needs positive AND negative controls** (ADR-038) and MUST reuse
  `tests/Spaarke.ArchTests/SourceScan.cs`. **Never fork `SourceScan`.**
- **No DI resolution in tests** — ADR-038 ban B3. Non-negotiable.
- **Exactly ONE new workflow across the whole project** (NFR-04). FR-23, FR-25, FR-27 are *sections* of
  FR-12's workflow, not new files. Before adding any `.yml`, check
  `git diff --stat .github/workflows/` — it must show one addition, total.
- **No thresholds.** Not on test count, not on duplication percentage, not on file size. A gate that
  blocks on a count-proxy for a judgment question is the retired God-class ratchet, and it is **the line
  r4 must not cross**.
- **Nothing is un-promoted.** FR-01 is enumeration, not cleanup. No existing shared component is
  regressed, un-packaged, or marked for removal.
- **Class-1 artifacts are generated, never hand-authored** — the FR-16 export index and the FR-21
  review checklist. A hand edit means the generator is broken.
- **Never scan `.claude/worktrees/` or the ~17 sibling worktrees** in any repo-wide pass — they produce
  phantom findings.
- **`.claude/`-touching tasks are main-session-only** (`parallel-safe: false`) per root CLAUDE.md §3. A
  sub-agent dispatched to one fails with "Edit denied" — that is the boundary working, not a bug.
- **`/conflict-check` before AND before merge** for P3, P4, P5 (NFR-06).
- **Advisory means exit 0.** FR-12, FR-18, FR-23, FR-25, FR-27 never block. Only the censuses (FR-02,
  FR-09c, FR-10) and FR-07's arch tests block.

### Parallel Task Execution

Tasks with no dependencies still each use task-execute — one message, multiple Skill invocations. Max 6
agents per wave. **`.claude/` tasks are sequential.**

---

## Execution Model & Tiering (Sonnet-5)

- **Planning** (design-to-spec, project-pipeline Steps 0–3): Opus 4.8 / Fable 5
- **Execution**: default **Sonnet 5 @ effort `high`**; each POML carries `<model-tier>` + `<effort>`.
  ADR classification (010) and the criterion-set sizing (020) carry `xhigh` — they are judgment-dense.
- **Step modes**: `directional` default; `prescriptive` for the header backfill (irreversible across 230 files)

---

## Key Technical Constraints

- **ADR-012** is amended by this project (path B, pre-declared in spec ADR Tensions). The amendment
  enumerates 15 packages and records three evaluation questions — **none of which is a gate**. It does
  **not** raise the 2+ trigger and does **not** un-package anything.
- **ADR-038** governs every test added. 7 KEEP paths, 17 bans, positive/negative controls.
- **CLAUDE.md §6.5** — every ADR found `stale` or `contested` by FR-05 gets its own path-B/C record. This
  is the point of FR-08, and classification will surface them **by construction**.
- **NFR-07** — no BFF source change, so the ≤60 MB publish gate does not apply.

---

## Decisions Made

- 2026-09-04: **Split P2** into P2a (classify, 010–013) and P2b (write tests, 020 → N). FR-07's size is
  unknown until FR-05 runs; committing to a count first is a guess. (Owner, spec Unresolved Q1)
- 2026-09-04: **Initialize-only** — pipeline generates artifacts + tasks; execution operator-gated wave by
  wave. Matches the repo convention across ~10 active projects. (Owner)
- 2026-09-04: **FR-10 replaced by a revision-header standard** — one standard way to date and
  revision-track files: at the top, human readable, carrying `version` + `revision-type` beyond the date,
  and **applied by script, not by LLM**. (Owner)
- 2026-09-04: **Frontmatter is the standard, not the blockquote.** Skills cannot drop frontmatter (Claude
  Code parses `description`/`tags`/`appliesTo`), so a blockquote standard would leave skills carrying two
  headers. (Analysis, owner-directed)
- 2026-09-04: **Header scope is `.claude/**` in r4**; `docs/**` (324 files) deferred to the same script
  with a different `-Path`, run when fewer worktrees are active. A 554-file diff across both trees is the
  collision NFR-06 warns about. (Analysis)

---

## Implementation Notes

### The FR-10 amendment (read before P3)

**`spec.md` FR-10 as written is superseded.** It asked for "a parseable `last-reviewed` on every
primitive; ~110 files gain a first stamp (0/16 constraints, 0/94 patterns)."

That count is true **only for YAML frontmatter**. Measured 2026-09-04:

| Surface | frontmatter `last-reviewed:` | blockquote `> **Last Reviewed**:` |
|---|---|---|
| skills (71) | 63 | — |
| constraints (16) | **0** | **15** |
| patterns (94) | **0** | **87** |

Those files are **not undated** — they are dated in the other of two competing conventions. Only ~9 files
genuinely lack a date. The work is therefore a **format standardisation**, not a 110-file authoring job.

**The standard** — `docs/standards/FILE-REVISION-HEADER.md`, applied at the top of every governance file:

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

Only `version` and `revision-type` are new; the rest already exist in the files (`status` on 90/94
patterns and 49/49 ADRs, `reviewed-by` on 68/94 patterns).

Bump rules: `major` = a MUST/MUST NOT or the rule's meaning changed · `minor` = content added or
clarified · `editorial` = typo/link/formatting, no version bump · `baseline` = standard applied
retroactively, history predates versioning · `initial` = first authored under the standard.

**`last-updated` and `last-reviewed` must stay distinct** — FR-13's auto-bump moves only `last-reviewed`,
so the stamp honestly means *verified nothing changed*. Collapsing them breaks the three-tier model.

**Applied by `scripts/quality/Update-DocHeader.ps1`**, idempotent: parse existing blockquote values and
**preserve them** (no dates invented) · derive `last-updated` from `git log -1 --format=%as` where absent ·
seed `version: 1.0`, `revision-type: baseline` · remove the migrated blockquote lines so there is one
source of truth · **for skills, add the new keys only** — never touch `description`/`tags`/`techStack`/
`appliesTo`/`alwaysApply`/`exemplar`. Domain-specific blockquotes (`Domain`, `Source ADR`, `See Also`)
stay as prose. Verified safe: no script in `scripts/**` parses these headers today.

> **Obligation**: update `spec.md` FR-10 to match before P3 executes, or spec and tasks disagree. Tracked
> as plan.md risk **R4**.

### Corrections to spec.md carried into the tasks

1. **FR-24 names three docs; there are four.** `docs/procedures/DEPENDENCY-MANAGEMENT.md` also describes
   the nonexistent `nightly-quality.yml`.
2. **The spec says "worktree not yet created."** It exists — branch `work/code-quality-and-assurance-r4`
   and PR #935 are live. Pipeline Step 4 reused both.
3. **README's "0/16 constraints and 0/94 patterns carry any review stamp"** was corrected to say
   *machine-parseable* stamp — the blockquote stamps exist.

### Hot-path collisions (live as of 2026-09-04)

r4 declares `ci-workflows=Y`, `skill-directives=Y`, `root-claude-md=Y`. Overlapping worktrees:

- **PR #894** `ci/tier2-unit-scope` — draft, held until the CI shadow window closes, edits `.github/workflows/`
- **`unified-access-control-r2`** (PR #939, executing) — `skill-directives=Y`, edits the same
  `task-create` / `code-review` SKILL.md files r4 touches in P3–P5
- **`spaarkeai-compose-r8`** (wrap-up) — `ci-workflows=Y`

---

## Deferrals & Issues — tracking obligation

Deferred work and newly-discovered issues go in **both** `notes/defer-issues.md` and GitHub Issues on the
portfolio board (Epic #427). Invoke `/project-defer-issue-tracking` (alias `/defer`) — it writes to both.

Already deferred by design: the 68 skipped tests ([#794](https://github.com/spaarke-dev/spaarke/issues/794))
· merging the known-divergent code paths (r4 records them only) · the `docs/**` header backfill.

CLAUDE.md §11 applies to every deferral — name a concrete failing behavior or contract; "flexibility" is
not a reason.

---

## Resources

### Applicable ADRs

- **ADR-012** shared components (**amended** by P1) · **ADR-038** testing strategy (governs every test added)
- CLAUDE.md **§6.5** ADR conflict protocol · **§10 / §11** hot-path + component justification

### Canonical implementations

- `tests/Spaarke.ArchTests/SourceScan.cs` — reuse, never fork
- `tests/Spaarke.ArchTests/CredentialCensusTests.cs` — the census pattern
- `.github/workflows/nightly-health.yml` — rolling-issue pattern
- `.github/workflows/adr-audit.yml` — idempotent tracking-issue pattern
- `scripts/quality/post-edit-lint.sh`, `task-quality-gate.sh` — hook pattern (live since March)
- `scripts/quality/nightly-review-prompt.md` — **already written**; FR-23 is wiring, not authoring

### Related projects

- `code-quality-and-assurance-r3` — predecessor (✅ 2026-08-14); r4 is the same move one level up
- `ci-cd-unit-test-remediation-r1` — ADR-038 authority; owns `.github/workflows` conventions
- `unified-access-control-r2` — **live collision** on skill directives

### External documentation

- `docs/standards/TEST-ARCHITECTURE.md` · `docs/adr/ADR-038-testing-strategy.md`
- `docs/standards/COMPONENT-COMPLEXITY.md` (the retired God-class ratchet — why r4 adds no thresholds)

---

*Keep this file updated throughout the project lifecycle.*
