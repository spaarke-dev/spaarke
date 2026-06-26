# ci-cd-unit-test-remediation-r1 - AI Context

> **Purpose**: This file provides context for Claude Code when working on the CI/CD + Unit Test Remediation project.
> **Always load this file first** when working on any task in this project.

---

## 🤖 Autonomous Execution Mode (BINDING for this project)

**This project runs autonomously without approval gates.** All task POMLs are marked `autonomous="true"`. Behavior overrides:

- **No mid-task confirmation prompts** — do not ask "should I proceed?", "do you want me to continue?", or "ready for next task?" between steps or tasks. Just execute.
- **Auto-advance to next task** — when one task completes (POML status = complete, TASK-INDEX.md updated to ✅), automatically pick the next pending task per TASK-INDEX.md (next 🔲 in dependency order, or any from the next available parallel group).
- **`/project-pipeline` interactive prompts → silenced** — the pipeline's normal `[Y to proceed]` gates are skipped; this project runs in autonomous mode per pipeline default.
- **`task-execute` Step approval gates → silenced** — the task-execute skill does not pause for user confirmation between phases within a task; only the mandatory checkpointing (every 3 steps; on context >70%) still runs because those are recoverability gates, not approval gates.
- **Quality gates remain ON** — Step 9.5 code-review + adr-check still runs for FULL-rigor tasks (binding per spec FR-B07 for all test-modifying tasks). These are quality enforcement, NOT approval gates.
- **Tool permission prompts** — repo `.claude/settings.json` already has `defaultMode: "acceptEdits"` and broad `Bash`/`Read`/`Write` allowed; no per-tool prompts expected during normal task execution.
- **Hard escalations remain ON** — must still escalate to user for: ambiguous/conflicting requirements (after attempting reasonable interpretation), security-sensitive changes (auth/secrets/encryption), ADR conflicts requiring resolution, scope expansion beyond task boundaries, rollback triggers in Phase 3 (per spec.md §152 rollback conditions). These are correctness gates, not approval gates.
- **Destructive operations remain explicit** — must still confirm before `git reset --hard`, `git push --force`, `rm -rf` on shared paths, deletions outside the planned slice, branch deletion. These are safety gates.

When in doubt: **prefer action over asking**. Authorization for autonomous execution was granted at project setup (2026-06-25) and persists for the project lifetime.

---

## Project Status

- **Phase**: Phase 0 (pipeline-initialized; Phase 1 not yet started)
- **Last Updated**: 2026-06-25
- **Current Task**: none
- **Next Action**: Start with `task-execute 000-preflight-baseline-build.poml` (or skip to `001` if pipeline pre-flight covered it)

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI-optimized specification (binding source of truth)
- [`design.md`](design.md) — Design rationale (3 streams, 3 phases, evidence base)
- [`README.md`](README.md) — Project overview + graduation criteria
- [`plan.md`](plan.md) — WBS + hot-path declaration + parallel groups
- [`current-task.md`](current-task.md) — Active task state (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — Task registry

### Project Metadata
- **Project Name**: ci-cd-unit-test-remediation-r1
- **Type**: CI/CD + Test Architecture + Coordination (cross-cutting infra)
- **Complexity**: High (32 tasks across 3 streams × 3 phases; ~28 elapsed days)
- **Worktree branch**: `work/ci-cd-unit-test-remediation-r1`
- **Worktree path**: `c:\code_files\spaarke-wt-ci-cd-unit-test-remediation-r1\`

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for binding FRs/NFRs/SCs and MUST/MUST NOT rules
4. **Reference design.md** for rationale only (spec is the binding source where they conflict)
5. **Load the relevant task file** from `tasks/` based on current work
6. **Apply ADRs** relevant to the technologies used (loaded automatically via adr-aware)

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md).

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke task-execute skill:

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" / "keep going" / "next task" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" / "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

### Why This Matters

The task-execute skill ensures:
- ✅ Knowledge files are loaded (ADRs, constraints, patterns)
- ✅ Context is properly tracked in current-task.md
- ✅ Proactive checkpointing occurs every 3 steps
- ✅ Quality gates run (code-review + adr-check) at Step 9.5
- ✅ Progress is recoverable after compaction

### Parallel Task Execution

For Phase 1 (PG-1 has 10 parallel-safe tasks), each task MUST still use task-execute. Send ONE message with MULTIPLE Skill tool invocations (max 6 per wave per task-execute hard limit).

**NOTE on `.claude/` writes**: Per root CLAUDE.md §3, sub-agents CANNOT write to `.claude/` paths. PG-3 tasks (060, 061, 062, 031) modify skill SKILL.md files — these MUST be marked `parallel-safe: false` and executed sequentially in main session.

---

## 🚨 Project-specific binding rules (override general defaults)

These come from spec.md and supersede the root defaults for any task in this project.

### Rigor levels (per spec FR-B07)

- **FULL** (mandatory): all test-modifying (021, 022, 050, 051, 052, 053a/b/c), all workflow YAML (040, 041, 042, 043, 044, 070, 071, 077), all skill-directive (031, 060, 061, 062)
- **STANDARD**: docs-only (010, 011, 012, 020, 023, 024, 030, 075, 076)
- **MINIMAL**: pre-flight / coordination / wrap-up (000, 001, 002, 090)

Spec FR-B07 explicitly overrides the default STANDARD-rigor skip on Step 9.5 for test PRs — all test-modifying tasks run code-review + adr-check at Step 9.5.

### MUST/MUST NOT rules (from spec.md §120-128)

- ✅ MUST keep `sdap-ci.yml` running in parallel through Phase 2; retire only post-cutover after **14 days** of new-tier stability (gates task `077`)
- ✅ MUST enforce deletion-safety via path check at Step 9.5 (FR-B06); no CSV consultation at runtime
- ✅ MUST keep existing Azure deployment workflows functionally untouched (`deploy-promote.yml`, `deploy-infrastructure.yml`, `deploy-office-addins.yml` zero changes; `deploy-bff-api.yml` trigger audit only)
- ❌ MUST NOT restore `Release` matrix before Phase 2 deletion has merged AND surviving suite green ≥7 days
- ❌ MUST NOT add commit-marker skip mechanism to Tier 1 (FR-A05) — path-aware dispatch only
- ❌ MUST NOT introduce coverage-% targets to any new directive file (binding for ≥6 months)
- ❌ MUST NOT redesign BFF DI registry or SpaarkeAi widget/route registries in this project (OUT of scope)

### Hot-Path Declaration (dogfooding the new rule)

```xml
<hot-path-declaration>
  <bff-api>NO — no production code in src/server/api/Sprk.Bff.Api/** modified by this project</bff-api>
  <spaarke-ai>NO production code modified; YES adds .github/workflows/deploy-spaarke-ai.yml as new CD plumbing for src/solutions/SpaarkeAi/</spaarke-ai>
  <ci-workflows>YES — adds router/tier1/tier2, augments nightly-health, retires sdap-ci, flips branch protection. Highest-impact hot-path category for this project.</ci-workflows>
  <skill-directives>YES — modifies task-execute, project-pipeline, conflict-check SKILL.md. Coordination required with any other in-flight project modifying same skills.</skill-directives>
  <root-CLAUDE-md>YES — §8 (rigor table), §10 (BFF Hygiene), §17 (pointers). Single-file edit, coordinated via this project's worktree.</root-CLAUDE-md>
</hot-path-declaration>
```

---

## Decisions Made

- **2026-06-25** — ADR-038 will be STANDALONE testing strategy ADR (NOT a supersession of ADR-022, which is PCF Platform Libraries). Reason: spec FR-B03 misattributes; the misattribution lives in `.claude/constraints/testing.md` line 25 (corrected in its own footer at lines 131-133). Task `022` fixes the misattribution.
- **2026-06-25** — `projects/INDEX.md` (FR-C02) initial sweep scope = worktrees with commits in last 30 days only. Today 18 worktrees exist; recent subset matches spec's 5-6 active design point.
- **2026-06-25** — Skip pipeline Step 4 (feature-branch creation) — worktree branch IS the project branch.
- **2026-06-25** — Drop design.md "escape hatches first" task. Spec FR-A05 wins (no commit-marker skip).
- **2026-06-25** — Sub-slice `Sprk.Bff.Api.Tests` deletion into 3 sub-tasks by antipattern: `053a` HttpMessageHandler, `053b` DI registration + null checks, `053c` remaining-by-directory. Boundaries revisable after `020` inventory.

---

## Implementation Notes

- **Frameworks confirmed**: xUnit 2.9.0, Moq 4.20.70-72, FluentAssertions 6.12.0 (do NOT migrate these — not the issue per design.md §5 Out of Scope)
- **Critical files for Stream B path reorg (`050`)**: `tests/integration/auth/**`, `tests/integration/regression/**`, `tests/integration/data-mutation/**`, `tests/integration/tenant/**`, `tests/integration/contract/**`, `tests/unit/domain/**` — NONE exist today; create from scratch
- **PR-comment dedup pattern**: live in `sdap-ci.yml` lines 591-619 (job `adr-pr-comment`) — reuse verbatim for Tier 2 advisory comment
- **Baseline branch protection reference**: `projects/github-actions-rationalization-r1/baseline/branch-protection-2026-06-01.json` exists; task `001` decides whether to use as reference or replace
- **`deploy-bff-api.yml` trigger**: confirmed master-only on `src/server/api/**` (audit task `044` should be quick confirm + document)
- **`[Trait("status", "repaired")]`**: 137 occurrences in 78 files; SC-7 says "no NEW markers" — leave existing as-is, bar new via Step 9.5 code-review
- **`[Skip]` markers**: 0 occurrences across test tree (good news for SC-8)

---

## Resources

### Applicable ADRs

- **ADR-028** (Spaarke Auth Architecture) — Tier 1 auth smoke aligns with this
- **ADR-030** (BFF feature flags / kill switches) — path-aware dispatch may interact
- **ADR-032** (Null-Object kill-switch pattern) — relevant if any test PR removes a conditional service
- **ADR-038** (Testing Strategy) — NEW, drafted in task `024`; standalone (NOT a supersession of ADR-022)
- **Existing ADR-022** (PCF Platform Libraries) — UNCHANGED; spec's wording mistakenly cited it as a coverage source

### Related Projects

- `projects/github-actions-rationalization-r1/` — provides branch-protection baseline JSON for Phase 1 task `001`
- `projects/ci-cd-github-enhancement/` — REMOVED 2026-06-25 (superseded by this project; preserved in git history)
- `projects/test-architecture-reset-r1/` — REMOVED 2026-06-25 (superseded by this project; preserved in git history)

### External Documentation

- GitHub merge queue + branch-protection docs — Phase 1 UQ #1 spike validates required-check semantics
- GitHub Actions analytics — for SC-01..SC-10 measurements (30-day window)

---

*This file should be kept updated throughout project lifecycle.*
