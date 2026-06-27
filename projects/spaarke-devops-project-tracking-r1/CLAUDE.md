# Spaarke DevOps Project Tracking (r1) - AI Context

> **Purpose**: This file provides context for Claude Code when working on `spaarke-devops-project-tracking-r1`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning (artifacts scaffolded, tasks pending)
- **Last Updated**: 2026-06-23
- **Current Task**: Not started
- **Next Action**: Pipeline Step 3 generates task POMLs; then run `/task-execute 001`

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - AI-optimized spec (31 FRs, 10 NFRs, 6 phases, 23 ratified decisions) — permanent reference
- [`design.md`](design.md) - Original design document (639 lines)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan, WBS, parallel groups
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker (will be created by task-create)

### Project Metadata
- **Project Name**: spaarke-devops-project-tracking-r1
- **Type**: DevOps tooling + Skill authoring + GitHub configuration + Documentation (NOT code in `src/`)
- **Complexity**: Medium (greenfield skill family + hook injection into 9 existing skills + doc extensions; sparse ADR surface)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for FR/NFR contracts and ratified decisions (D-01..D-23)
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply skill convention** (NFR-07) — every new SKILL.md needs YAML frontmatter + Prerequisites/Purpose/Steps/Failure-Modes

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md).

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
- ✅ Knowledge files are loaded (skill exemplars, conventions, spec FR/NFR cross-references)
- ✅ Context is properly tracked in current-task.md
- ✅ Proactive checkpointing occurs every 3 steps
- ✅ Quality gates run (code-review + adr-check) at Step 9.5 — note: adr-check is informational only on this project (no mandatory ADRs)
- ✅ Progress is recoverable after compaction

**Bypassing this skill leads to**: missing skill-convention constraints, lost progress after compaction, broken hook injection.

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each task MUST still use task-execute:
- Send one message with multiple Skill tool invocations
- Example: Phase 2 skill-creation tasks 011, 012, 013 can run in parallel IF they touch different SKILL.md files

**🚨 Critical exception for THIS project**: most Phase 2 + Phase 4 tasks modify files under `.claude/skills/` — these are subject to the **Sub-Agent Write Boundary** (root CLAUDE.md §3): sub-agents launched via the Agent tool CANNOT write to `.claude/` paths. Task POMLs in those phases must have `parallel-safe: false` and run in the main session sequentially.

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

### Multi-File Work Decomposition

This project rarely modifies 4+ files in a single task (most tasks land one SKILL.md or one doc section). When a task DOES touch multiple files:

1. **Decompose into dependency graph** — `/devops-portfolio-setup` SKILL.md must land before docs reference it
2. **Sequential** if files are all under `.claude/skills/` (Sub-Agent Write Boundary)
3. **Parallel-safe** if files are independent docs (`HOW-TO-INITIATE-NEW-PROJECT.md`, `AI-CODING-PROCEDURES-GUIDE.md`, root `CLAUDE.md` are 3 different files; main session can edit them in parallel)

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

**From spec MUST rules**:
- ✅ MUST use `gh` CLI (or `gh api graphql`) for all GitHub Project API mutations — no Octokit / REST-client wrapper
- ✅ MUST preserve all 20 existing fields on Project #2 — additive only
- ✅ MUST keep all 9 new skills idempotent (NFR-04)
- ❌ MUST NOT commit any GitHub tokens, PATs, or credentials
- ❌ MUST NOT modify existing Spaarke skill contracts — hook injection is additive only (NFR-03)
- ❌ MUST NOT introduce parallel portfolio tracking system (NFR-01, D-01, D-09)
- ❌ MUST NOT mirror POML tasks as GitHub sub-issues (NFR-02, D-08)

**From NFRs**:
- NFR-03: hooks silent on success or emit single ✅ line; MUST NOT block host skill on failure
- NFR-05: rate-limit hygiene — batch of 20 + exponential backoff on 429 during Phase 3 backfill
- NFR-07: all 9 new SKILL.md files follow Spaarke convention (frontmatter + Prerequisites/Purpose/Steps/Failure-Modes)
- NFR-08: doc extensions PRESERVE existing structure (no section renumbering, no rearranged scenarios)
- NFR-09: archive deletes worktree, retains `projects/{name}/` folder + `.archived` marker file

**Sub-Agent Write Boundary** (root CLAUDE.md §3):
Sub-agents launched via the Agent tool CANNOT write to `.claude/` paths. For this project, this means:
- Phase 2 (9 new SKILL.md files) — main session only
- Phase 4 (9 existing SKILL.md hook injections) — main session only
- Phase 6 root `CLAUDE.md` modification — main session only
- Phase 6 docs (`docs/guides/*`, `docs/procedures/*`) — sub-agents CAN write here

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Source -->

- **2026-06-23**: Single GitHub Project #2 surface (no new board). Rationale: NFR-01, D-01, D-09. Source: spec.
- **2026-06-23**: POML tasks remain authoritative; only `Task Count`/`Tasks Completed` aggregates mirrored. Rationale: D-08, NFR-02. Source: spec.
- **2026-06-23**: Every Project has an Epic parent (no orphans). Rationale: D-12, forces stable Epic taxonomy. Source: spec.
- **2026-06-23**: Worktree deleted on archive; folder retained with `.archived` marker. Rationale: D-18, NFR-09. Source: spec.
- **2026-06-23**: `gh` CLI only — no Octokit/REST wrapper. Rationale: reuses existing auth; lower dep footprint. Source: spec MUST rule.

---

## Implementation Notes

<!-- Add gotchas, workarounds, or important learnings as the project progresses -->

- **Phase 1 vs Phase 2 separation**: Phase 1 is hand-driven `gh` commands; Phase 2 task 010 (`/devops-portfolio-setup`) codifies Phase 1 into an idempotent skill. Re-running the skill against Phase 1's already-extended schema must be a no-op.
- **Hook-injection pattern**: Look at how `task-execute` invokes `code-review` and `adr-check` at Step 9.5 — the calling skill mentions the hooked skill in its `Steps` section. Replicate this style for FR-16..FR-24.
- **Slash-command vs Skill tool**: All 9 new skills are invoked via the Skill tool by Claude Code (auto-detection) and via the `/devops-*` slash-command prefix by the user. Both invocation paths must be documented in each SKILL.md.
- **Smoke-test loop in Phase 2**: create + destroy a throwaway Project Issue per smoke test; do NOT pollute the real portfolio. Track test artifacts in `notes/spikes/`.

---

## Resources

### Applicable ADRs

**No mandatory ADRs apply** to this project. The Spaarke ADR catalog focuses on code/auth/AI architecture; this project's domain (DevOps tooling, skill authoring, GitHub configuration, documentation) has no binding ADR coverage. Confirmed during design-to-spec Step 3 + pipeline Step 2 comprehensive resource discovery (2026-06-23).

**Informational only**:
- [`.claude/adr/ADR-010-di-minimalism.md`](../../.claude/adr/ADR-010-di-minimalism.md) — would apply if any skill introduces .NET service code (not anticipated)

### Related Projects (templates)

- [`projects/ci-cd-github-enhancement/`](../ci-cd-github-enhancement/) — tiered CI model + escape hatches (Complete) — reference for workflow patterns
- [`projects/github-actions-rationalization-r1/`](../github-actions-rationalization-r1/) — workflow rationalization + `actionlint` (Complete) — reference for Phase 5 optional Actions
- [`projects/x-ui-dialog-shell-standardization/`](../x-ui-dialog-shell-standardization/) — canonical project artifact layout (Complete)

### Existing Skills (exemplars + targets)

**Skill structure exemplars** (load when authoring new SKILL.md files):
- [`.claude/skills/task-execute/SKILL.md`](../../.claude/skills/task-execute/SKILL.md) — orchestrator skill + hook-injection pattern (Step 9.5)
- [`.claude/skills/worktree-setup/SKILL.md`](../../.claude/skills/worktree-setup/SKILL.md) — git-worktree skill exemplar
- [`.claude/skills/design-to-spec/SKILL.md`](../../.claude/skills/design-to-spec/SKILL.md) — long-form skill with phases
- [`.claude/skills/INDEX.md`](../../.claude/skills/INDEX.md) — frontmatter + convention reference (NFR-07 binding)

**Hook-target skills** (Phase 4 will modify these):
- [`.claude/skills/design-to-spec/SKILL.md`](../../.claude/skills/design-to-spec/SKILL.md) — FR-16
- [`.claude/skills/project-pipeline/SKILL.md`](../../.claude/skills/project-pipeline/SKILL.md) — FR-17
- [`.claude/skills/task-create/SKILL.md`](../../.claude/skills/task-create/SKILL.md) — FR-18
- [`.claude/skills/task-execute/SKILL.md`](../../.claude/skills/task-execute/SKILL.md) — FR-19
- [`.claude/skills/context-handoff/SKILL.md`](../../.claude/skills/context-handoff/SKILL.md) — FR-20 (highest-value)
- [`.claude/skills/worktree-setup/SKILL.md`](../../.claude/skills/worktree-setup/SKILL.md) — FR-21
- [`.claude/skills/worktree-sync/SKILL.md`](../../.claude/skills/worktree-sync/SKILL.md) — FR-22
- [`.claude/skills/repo-cleanup/SKILL.md`](../../.claude/skills/repo-cleanup/SKILL.md) — FR-23
- [`.claude/skills/merge-to-master/SKILL.md`](../../.claude/skills/merge-to-master/SKILL.md) — FR-24

### Documentation Targets (Phase 6)

- [`docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md`](../../docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md) — 423 lines today; extend Step 0 + Portfolio Integration section
- [`docs/procedures/AI-CODING-PROCEDURES-GUIDE.md`](../../docs/procedures/AI-CODING-PROCEDURES-GUIDE.md) — 525 lines today; add 7 lifecycle scenarios
- [`CLAUDE.md`](../../CLAUDE.md) root §16 Pointers — add portfolio row
- [`.claude/CHANGELOG.md`](../../.claude/CHANGELOG.md) — add entry referencing this project

### External Documentation

- **GitHub Project #2**: https://github.com/users/spaarke-dev/projects/2
- **GitHub Projects v2 GraphQL API**: search "GitHub Projects v2 GraphQL" for current mutation docs (use researcher subagent if needed)
- **`gh` CLI reference**: `gh project --help`, `gh api graphql --help`
- **GitHub Issue Templates form schema**: https://docs.github.com/en/communities/using-templates-to-encourage-useful-issues-and-pull-requests

---

*This file should be kept updated throughout project lifecycle. When a key decision lands or a non-obvious learning surfaces, append it to "Decisions Made" or "Implementation Notes" above.*
