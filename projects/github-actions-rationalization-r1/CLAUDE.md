# github-actions-rationalization-r1 - AI Context

> **Purpose**: This file provides context for Claude Code when working on github-actions-rationalization-r1.
> **Always load this file first** when working on any task in this project (per NFR-08).

---

## Project Status

- **Phase**: Planning → Phase 0 (Inventory)
- **Last Updated**: 2026-06-01
- **Current Task**: Not started
- **Next Action**: Run task-create to decompose plan into task files, then execute task 001 (workflow inventory) + task 002 (master CI root cause) in parallel

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - AI-optimized specification (FRs, NFRs, success criteria)
- [`design.md`](design.md) - Original design document (motivation, locked decisions D-01..D-05, phased delivery)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan and WBS
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker (created by task-create)

### Project Metadata
- **Project Name**: github-actions-rationalization-r1
- **Type**: DevOps / CI/CD tooling rationalization (no `src/` changes)
- **Complexity**: Medium (DevOps-only scope; 13 workflows touched; new docs)
- **Branch**: `work/github-actions-rationalization-r1`
- **Predecessor**: `sdap-bff.api-test-suite-repair` (closed 2026-06-01) — surfaced symptoms; this project addresses root causes
- **Owner**: ralph.schroeder@hotmail.com

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task (NFR-08)
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for design decisions, FRs, NFRs, success criteria
4. **Reference design.md** for D-01..D-05 locked decisions and rationale
5. **Load the relevant task file** from `tasks/` based on current work
6. **Apply ADRs** — limited applicability on this project; ADR-029 only for `deploy-bff-api.yml` audit

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke task-execute skill:

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" | Execute task X via task-execute |
| "next task" | Execute next pending task via task-execute |
| "keep going" | Execute next pending task via task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

**Implementation**: When user triggers task work, invoke Skill tool with `skill="task-execute"` and task file path.

### Why This Matters

The task-execute skill ensures:
- ✅ Knowledge files are loaded (ADRs, constraints, predecessor patterns)
- ✅ Context is properly tracked in current-task.md
- ✅ Proactive checkpointing occurs every 3 steps
- ✅ Quality gates run (code-review + adr-check) at Step 9.5 for FULL rigor tasks
- ✅ Progress is recoverable after compaction
- ✅ Inherited `<repair-not-rewrite>true</repair-not-rewrite>` semantics honored (NFR-07)

### Parallel Task Execution

This project's TASK-INDEX defines parallel-execution groups (e.g., Phase 1 tasks 010/011/012, Phase 2 dispositions, Phase 4 docs 041/042/043). When tasks can run in parallel:
- Send ONE message with multiple Skill tool invocations
- Each invocation calls task-execute with a different task file
- Tasks in a parallel group are independent (no shared files)
- Wait for ALL agents in the group to complete before dispatching next group
- **Max concurrency**: 6 agents per wave

### Multi-File Work Decomposition

For tasks modifying 4+ files:
1. **Decompose into dependency graph**: group files by module/component
2. **Delegate to subagents in parallel where safe**: independent module work → parallel; shared interface → serial
3. **Track in current-task.md "Parallel Execution" section**

This project's per-task scope is narrow (1–3 workflow files OR 1 doc file), so multi-file decomposition rarely applies within a single task — but cross-task parallelization is heavily used.

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

### Spec-binding NFRs

- **NFR-01**: No production code changes (`src/`, `power-platform/`, `infra/`, `scripts/`)
- **NFR-02**: Each workflow file edit <50% line replacement; >50% requires escalation OR delete-and-rewrite with explicit decision record
- **NFR-03**: Don't disable `enforce_admins` outside the merge-window of a specific actionlint-fix PR. Each disable logged in `decisions/`
- **NFR-04**: All workflow deletions via `git rm` + commit; NOT comment-out-and-leave
- **NFR-05**: `actionlint` check added to required-status-checks BEFORE Phase 5's deliberate-fail verification
- **NFR-06**: `decisions/` contains a record for every keep-vs-delete decision on the 7 untested workflows (one per workflow, minimum 1-paragraph rationale)
- **NFR-07**: Task POML metadata declares `<repair-not-rewrite>true</repair-not-rewrite>` (inherited binding pattern from predecessor)
- **NFR-08**: Project CLAUDE.md (this file) is created and loaded by every task agent

### MUST Rules

- ✅ MUST use the `Test update obligation` pattern from `.claude/constraints/bff-extensions.md` § F for any workflow that runs tests
- ✅ MUST preserve the 3 currently-required status checks unless a deletion explicitly removes one
- ✅ MUST run `actionlint` locally on any workflow file BEFORE pushing
- ❌ MUST NOT push directly to master (use PR via feature branch)
- ❌ MUST NOT disable `enforce_admins` without a decisions/ entry recording the rationale and restoration timestamp
- ❌ MUST NOT delete a workflow without first checking `git log -- .github/workflows/{name}.yml` for recent contributors who may have context

### Out of Scope (Strict)

- `src/`, `power-platform/`, `infra/`, `scripts/` — never touched by this project
- Application-level bugs surfaced by CI runs — route to product backlog; do NOT fix in this project
- Migrating to a different CI/CD platform
- Adding new test types or expanding test coverage

---

## Decisions Made

D-01..D-05 are inherited from `design.md`:

| Ref | Decision | Why | Source |
|---|---|---|---|
| D-01 | `actionlint` is the canonical validator (via `rhysd/actionlint@v1`) | Actions-specific knowledge; catches duplicate keys, undefined vars, runner labels | design.md §4 |
| D-02 | Failure observability via weekly scheduled report, not real-time alerts | Real-time alerts recreate "ignored notifications" failure mode; weekly aggregate has higher signal | design.md §4 |
| D-03 | Delete-by-default for never-used workflows | Burden of proof on retention; comment-out accumulates dead code | design.md §4 |
| D-04 | One workflow per concern; consolidate liberally | 13 workflows' cognitive overhead exceeds their value | design.md §4 |
| D-05 | Notification routing at GitHub account/org level, not workflow level | SMTP actions fragile; built-in routing is proper path; owner-applied manually | design.md §4 |

Additional project-scope decisions (D-06+) will be added as work progresses, in `decisions/D-NN-{name}.md`.

---

## Implementation Notes

### Predecessor patterns to reuse

From `projects/sdap-bff.api-test-suite-repair/` (closed 2026-06-01):
- `decisions/D-NN-{name}.md` per-decision record pattern
- `ledgers/{topic}-ledger.md` rollup pattern (this project: `ledgers/workflow-disposition-ledger.md`)
- `baseline/{state}-{date}.md` pre/post state capture
- Phase-gap task numbering (001..090 with gaps for insertion)
- `<repair-not-rewrite>true</repair-not-rewrite>` POML metadata (NFR-07)
- Triple-run validation pattern (predecessor task 084) — applicable to workflow stability checks

### Gotchas / Surprises

- The active branch is `work/github-actions-rationalization-r1` (NOT a new `feature/` branch). The pipeline did NOT create a new branch; commits go to `work/`.
- Multiple Dependabot PRs (#244, #263, #264, etc.) are touching `.github/workflows/` simultaneously. Coordinate task ordering or rebase as needed.
- The first PR adding `workflows-validate.yml` can't satisfy its own required-status-check. Use brief `enforce_admins` bypass (predecessor pattern); log in `decisions/`.

---

## Resources

### Applicable ADRs (limited — this project is DevOps tooling)

- [`ADR-029`](../../docs/adr/ADR-029-bff-publish-hygiene.md) — BFF Publish Hygiene; for `deploy-bff-api.yml` audit only (Phase 2)
- [`ADR-001`](../../docs/adr/ADR-001-minimal-api-no-azure-functions.md) — Minimal API + Workers; deploy-bff-api.yml deploy target preserves this
- [`ADR-027`](../../docs/adr/ADR-027-subscription-isolation-managed-solutions.md) — Subscription isolation; governance context for `deploy-*` workflows

### Related Projects

- [`projects/sdap-bff.api-test-suite-repair/`](../sdap-bff.api-test-suite-repair/) — **Predecessor**; closed 2026-06-01; surfaced these symptoms
- [`projects/code-quality-and-assurance-r1/`](../code-quality-and-assurance-r1/) — Nightly/weekly quality workflow patterns; graduated enforcement
- [`projects/sdap-bff-api-remediation-fix/`](../sdap-bff-api-remediation-fix/) — Contains `inventory/ci-workflow-inventory.md` — gold-standard audit format for Phase 0

### External Documentation

- GitHub Actions docs: https://docs.github.com/en/actions
- `actionlint` reference: https://github.com/rhysd/actionlint
- GitHub Actions API: `gh api repos/{owner}/{repo}/actions/...`

### Internal Knowledge Docs

- [`docs/procedures/ci-cd-workflow.md`](../../docs/procedures/ci-cd-workflow.md) — Existing CI/CD pipeline guide (extend in Phase 4)
- [`docs/guides/INCIDENT-RESPONSE.md`](../../docs/guides/INCIDENT-RESPONSE.md) — Template for `workflow-incident-response.md`
- [`docs/guides/GITHUB-ENVIRONMENT-PROTECTION.md`](../../docs/guides/GITHUB-ENVIRONMENT-PROTECTION.md) — Branch-protection reference
- [`docs/guides/MONITORING-AND-ALERTING-GUIDE.md`](../../docs/guides/MONITORING-AND-ALERTING-GUIDE.md) — Observability patterns

### Applicable Skills

- `.claude/skills/ci-cd/` — GitHub Actions CI/CD pipeline status & workflow management
- `.claude/skills/adr-check/` — ADR compliance (light load on this project)
- `.claude/skills/code-review/` — Quality gate at task-execute Step 9.5 (FULL rigor)
- `.claude/skills/docs-procedures/` — Author `docs/procedures/workflow-incident-response.md`
- `.claude/skills/docs-guide/` — Author `.github/WORKFLOWS.md`
- `.claude/skills/push-to-github/` — Per-task commits
- `.claude/skills/repo-cleanup/` — End-of-project hygiene
- `.claude/skills/merge-to-master/` — Final merge

---

*This file should be kept updated throughout project lifecycle. Per NFR-08, every task agent loads this file.*
