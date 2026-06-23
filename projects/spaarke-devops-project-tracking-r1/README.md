# Spaarke DevOps Project Tracking (r1)

> **Last Updated**: 2026-06-23
>
> **Status**: Ready for Implementation

## Overview

Extends GitHub Project #2 ("Spaarke Core") with new fields, a `Type=Project` option, GitHub-native sub-issue hierarchy (Epic → Project), 9 new `/devops-*` Claude Code skills, and automation hooks into 9 existing skills. Delivers single-source-of-truth visibility (Epics → Projects → POML tasks) with zero new external systems — the day-to-day development workflow keeps the portfolio current automatically.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Phase breakdown, WBS, dependencies, parallel groups |
| [Spec](./spec.md) | AI-optimized implementation spec (31 FRs, 10 NFRs, 6 phases) |
| [Design](./design.md) | Original design document (639 lines, 23 ratified decisions) |
| [Task Index](./tasks/TASK-INDEX.md) | Master task tracker (will be created in Step 3 of pipeline) |
| [Current Task](./current-task.md) | Active task state for context recovery |
| [CLAUDE.md](./CLAUDE.md) | AI context for this project (load first per task) |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Planning (artifacts scaffolded; tasks pending) |
| **Progress** | 0% (pre-implementation) |
| **Target Date** | TBD |
| **Completed Date** | — |
| **Owner** | spaarke-dev |

## Problem Statement

Spaarke runs ~30+ active and ~100+ historical projects as git worktrees following a structured spec-driven process, but has **no portfolio-level view** of which Epics exist, which projects are active/planned/on-hold/completed, or how they roll up. Stakeholders cannot answer "what's running?" or "what's stalled?" in <30 seconds. Engineering owner cannot see the portfolio at a glance. Existing GitHub Project #2 tracks Issues/Stories/Bugs/Spikes but does not model Projects as first-class entities.

## Solution Summary

Extend Project #2 — the single existing portfolio surface — with a `Type=Project` option, 6 new custom fields, 7 labels, and 3 issue templates (Epic, Project, Idea). Implement 9 new `/devops-*` skills covering the full lifecycle (capture idea → promote to project → start → sync → archive). Inject portfolio-update hooks into 9 existing skills (`design-to-spec`, `project-pipeline`, `task-create`, `task-execute`, `context-handoff`, `worktree-setup`, `worktree-sync`, `repo-cleanup`, `merge-to-master`) so portfolio state updates ride on normal workflow with no explicit `/devops-*` typing during day-to-day work. Backfill ~20–30 active projects onto the board. Extend two existing docs and root `CLAUDE.md` §16. No new entities, no new dashboards, no new external systems.

## Graduation Criteria

The project is considered **complete** when:

- [ ] **Phase 1 schema**: `gh project field-list 2 --owner spaarke-dev --format json | grep -c "Project Type"` returns 1; all 7 labels exist; 3 issue templates open from GitHub UI
- [ ] **All 12 initial Epics created**: visible in Epics-overview view
- [ ] **All 9 `/devops-*` skills implemented and discoverable**: `ls .claude/skills/devops-* | wc -l` returns 9; all listed in `.claude/skills/INDEX.md`
- [ ] **Active-project backfill complete**: every active/in-flight worktree (per F6 definition) has a matching `Type=Project` Issue with all 6 new fields populated
- [ ] **`/devops-project-sync` is idempotent**: dry-run after a clean sync produces zero proposed mutations
- [ ] **Hooks active in 9 existing skills**: `grep -l "devops-project-sync\|devops-project-register" .claude/skills/{design-to-spec,project-pipeline,task-create,task-execute,context-handoff,worktree-setup,worktree-sync,repo-cleanup,merge-to-master}/SKILL.md` returns 9 lines
- [ ] **Portfolio Roadmap view usable**: a non-implementer can answer in <30 seconds: "What Epics are active? How many projects in each? Rough portfolio status?"
- [ ] **`/devops-project-start` round-trip works end-to-end**: from a Project Issue, the skill creates folder + worktree + design.md skeleton + writes back fields + README pointer
- [ ] **Documentation extensions land**: `docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md` has portfolio integration section; `docs/procedures/AI-CODING-PROCEDURES-GUIDE.md` has all 7 new scenarios
- [ ] **Per-project README pointer present** in all active projects after Phase 3+4: `grep -L "> \*\*Portfolio\*\*" projects/*/README.md` returns no active projects
- [ ] **CLAUDE.md §16 updated** with portfolio row + entry in `.claude/CHANGELOG.md`
- [ ] **No regression in existing skills**: all hooked skills still pass their own success criteria

## Scope

### In Scope

- GitHub Project #2 extension: `Type=Project` option, 6 custom fields, 7 labels, 3 issue templates
- 9 new `/devops-*` Claude Code skills
- Automation hooks injected into 9 existing skills
- Active-project backfill (~20–30 in-flight projects)
- Portfolio views: Roadmap, Active, Backlog, On-hold/Cancelled, By type tag
- Documentation extensions to `HOW-TO-INITIATE-NEW-PROJECT.md`, `AI-CODING-PROCEDURES-GUIDE.md`, root `CLAUDE.md` §16
- Per-project README portfolio pointer block (auto-written by skills)
- Initial Epic taxonomy (~10–12 Epics)

### Out of Scope

- Historical backfill of all ~133 projects (D-07)
- Mirroring POML tasks as GitHub sub-issues (D-08)
- A new dedicated GitHub Project board separate from #2 (D-09)
- Customer-facing portfolio view (D-10)
- A new top-level `docs/guides/PROJECT-PORTFOLIO-MANAGEMENT.md` (D-23)
- Real-time file-watcher sync (on-demand + hook-triggered only)
- Time tracking / story points / velocity
- Custom web dashboard outside GitHub
- GitHub Copilot Workspace / Coding Agent integration in r1
- Auto-trigger of `/devops-project-archive` on PR merge (F3 default: explicit gate)

## Key Decisions

| Decision | Rationale | Source |
|---|---|---|
| Extend Project #2 (not a new board) | Single source of truth; reuse existing 20 fields; avoid parallel tracking | D-01, D-09 |
| POML tasks remain authoritative; track aggregates only | Spec-driven process unchanged; avoid GitHub-Issue churn per task | D-08, NFR-02 |
| Every Project has an Epic parent (no orphans) | Forces stable Epic taxonomy; enables Roadmap rollup | D-12 |
| Worktree deleted on archive; folder kept with `.archived` | Reclaims disk; preserves history | D-18, NFR-09 |
| Auto-generated Issue body with DO NOT EDIT marker | Avoids manual/sync conflicts | D-17, NFR-06 |
| `gh` CLI only (no Octokit wrapper) | Reuses existing auth; lower dep footprint | MUST rule in spec |
| Idempotent skills; partial-success-tolerant sync | Hooks ride on normal flow; failures degrade gracefully | NFR-03, NFR-04, F7 resolution |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|---|---|---|---|
| Drift between local state and GitHub fields | Medium | Medium | `/devops-project-sync` idempotent + hook-driven; Phase 5 optional scheduled drift check |
| GitHub API rate limits during Phase 3 backfill | High | Low–Med | Batch in groups of 20 with exponential backoff on 429 (NFR-05) |
| POML format drift breaks parsing | Medium | Low | Spec assumes format stability over r1; hooks only read `TASK-INDEX.md` checkboxes |
| Hook injection breaks existing skill contract | High | Low | Additive only; hooks silent on success, degrade-to-warn on failure (NFR-03) |
| "Active/in-flight" definition contested mid-backfill | Low | Low | Locked in F6 resolution: worktree + (active task OR open PR OR commits in 30d) |

## Dependencies

| Dependency | Type | Status | Notes |
|---|---|---|---|
| `gh` CLI v2.40+ | External | Ready | Authenticated as spaarke-dev (verified) |
| GitHub Project #2 ("Spaarke Core") | External | Ready | https://github.com/users/spaarke-dev/projects/2 |
| 9 existing Spaarke skills (to be hooked) | Internal | Ready | All present at `.claude/skills/<skill>/SKILL.md` |
| GitHub Projects v2 GraphQL API | External | GA | Mutations: `addProjectV2ItemById`, `updateProjectV2ItemFieldValue`, `updateProjectV2Field`, `createProjectV2Field` |
| `.github/ISSUE_TEMPLATE/` form-template YAML schema | External | GA | Stable schema; templates land in Phase 1 |
| Token scopes: `repo`, `project`, `read:org`, `gist`, `workflow` | External | Ready | Already in place per current `gh auth status` |

## Team

| Role | Name | Responsibilities |
|---|---|---|
| Owner | spaarke-dev | Overall accountability, GitHub Project admin, archive decisions |
| Implementer | TBD | Skill authoring, hook injection, doc extensions |
| Reviewer | TBD | Code review on skill PRs, doc-change review |

## Changelog

| Date | Version | Change | Author |
|---|---|---|---|
| 2026-06-23 | 1.0 | Initial draft scaffolded by `/project-pipeline` from spec.md + design.md | Claude Code |

---

*Template version: 1.0 | Based on Spaarke development lifecycle*
