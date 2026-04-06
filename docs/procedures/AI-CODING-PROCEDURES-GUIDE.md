# AI Coding Procedures Guide

> **Last Updated**: 2026-04-05
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: New
> **Purpose**: Scenario-based quick reference for using Claude Code skills, tools, and procedures in the Spaarke repository

---

## How to Use This Guide

Find your scenario below. Each tells you **what to say**, **what happens automatically**, and **what to check**.

---

## Starting a New Project

### Scenario: I have a design document and want to start a project

```
/design-to-spec projects/{project-name}
```

Then:

```
/project-pipeline projects/{project-name}
```

**What happens**:
1. Claude enters Plan Mode (enforced — confirms before proceeding)
2. Pre-flight checks run: clean tree, master current, build passing
3. Design doc → structured `spec.md`
4. Resource discovery: ADRs, patterns, constraints, knowledge docs loaded automatically
5. Artifacts generated: README.md, plan.md, CLAUDE.md, folder structure
6. Tasks decomposed into POML files with parallel grouping
7. Feature branch created and pushed
8. Task execution begins (parallel where safe)

**Your role**:
- Review `plan.md` before saying "proceed"
- Verify discovered resources are complete ("also load ADR-021" if missing)
- Switch from Plan Mode to Accept Edits when prompted

### Scenario: I want to start from an existing spec.md

```
/project-pipeline projects/{project-name}
```

Same flow as above, skipping the design-to-spec step.

### Scenario: I just want to set up project scaffolding without full pipeline

```
/project-setup projects/{project-name}
```

Creates artifacts only (README, PLAN, CLAUDE.md). Does NOT create tasks or branches. Use when you want manual control.

---

## Working on Tasks

### Scenario: I want to work on the next task

```
continue
```

Or be specific:

```
work on task 013
```

**What happens**:
1. `task-execute` skill loads automatically (mandatory — never bypass)
2. Rigor level determined (FULL/STANDARD/MINIMAL)
3. Knowledge files loaded from task POML
4. Constraints and patterns loaded based on task tags
5. Implementation proceeds with checkpointing every 3 steps

**Never do this**:
- ❌ Read the POML file and implement manually
- ❌ Skip the task-execute skill "for speed"

### Scenario: I want to run multiple tasks in parallel

```
work on Phase 2 tasks in parallel
```

**What happens**:
1. task-execute reads TASK-INDEX.md parallel groups
2. Spawns up to 6 concurrent agents (one per task)
3. Each agent runs task-execute independently
4. Build verification runs between waves
5. Failed tasks get 🔄 status (retry), not ❌ (abandoned)

**Quality guardrails** (automatic):
- Tasks touching `.claude/` run sequentially (main session only)
- Tasks touching same files are auto-demoted to sequential
- Build must pass between waves

### Scenario: I need to resume after a session restart

```
where was I?
```

Or:

```
continue work on {project-name}
```

**What happens**: `project-continue` skill loads `current-task.md`, reads last checkpoint, resumes from the exact step where work stopped.

---

## Code Quality & Review

### Scenario: I want to review my changes before pushing

```
/code-review
```

**What it checks**: Security, performance, maintainability, style, architectural compliance.

### Scenario: I want to check my changes against ADRs

```
/adr-check
```

**What it checks**: Code compliance with Architecture Decision Records. Catches violations like injecting `GraphServiceClient` directly (ADR-007) or using global middleware (ADR-008).

### Scenario: I want both code review AND ADR check

Both run automatically at **Step 9.5** of task-execute for FULL rigor tasks. For manual invocation:

```
/code-review
/adr-check
```

---

## Pushing & Merging

### Scenario: I'm ready to push my changes

```
/push-to-github
```

**What happens**:
1. Checks for untracked source files (prevents "forgot to add" bugs)
2. Reviews changes with `git status` and `git diff`
3. Generates conventional commit message
4. Pushes to remote
5. Creates PR (or updates existing)
6. Monitors CI status

### Scenario: I want to merge my branch to master

```
/merge-to-master
```

**What happens**:
1. Audit mode: shows unmerged branches
2. Pre-merge safety checks (clean tree, no conflicts)
3. Build verification (mandatory — never push broken code)
4. Pushes to origin/master
5. Syncs main repo if in a worktree

### Scenario: I just want to check what branches are unmerged

```
/merge-to-master
```

Default mode is audit — it reports without merging. You choose what to merge.

---

## Documentation

### Scenario: I built a new feature and need to document it

**For architecture** (how it works technically):
```
create architecture doc for {subsystem-name}
```
Invokes `/docs-architecture`. Produces doc with mandatory sections: Overview, Component Structure, Data Flow, Integration Points, Known Pitfalls.

**For operational guide** (how to use/configure it):
```
create guide for {feature-name}
```
Invokes `/docs-guide`. Produces doc with: Prerequisites, Procedure, Configuration, Verification, Troubleshooting.

**For standards** (cross-cutting rules):
```
update CODING-STANDARDS.md with {new convention}
update ANTI-PATTERNS.md with {new anti-pattern}
```

**For data model** (Dataverse entities):
```
create data model doc for {entity-name}
```

**For procedures** (development workflow):
```
create procedure for {workflow-name}
```

### Scenario: I changed existing code and want to update related docs

```
run /doc-drift-audit on changes since {commit-or-branch}
```

**What happens**:
1. Computes diff of your changes
2. Finds docs/patterns/constraints that reference changed code
3. Verifies references are still accurate
4. Auto-fixes stale paths
5. Flags content drift for your review
6. Updates Last Reviewed stamps

### Scenario: I added a new ADR, pattern, or skill and need to propagate

```
/ai-procedure-maintenance
```

**What happens**: Provides a checklist of everywhere to update: INDEX files, CLAUDE.md references, skill mappings, constraint cross-references. Prevents the "added ADR but forgot to update 5 other files" problem.

---

## Periodic Maintenance

### Scenario: I want to check that our docs are accurate

**Quick check** (10-30 min):
```
/doc-drift-audit
```

Audits everything changed since last review stamp. Fast and focused.

**Full audit** (1-2 hours, quarterly):
```
Run a full documentation accuracy audit across docs/architecture/, .claude/patterns/, and .claude/constraints/
```

R2-style comprehensive audit: reads every file, greps for every referenced class/path, flags stale content, stamps verified files.

### Scenario: I want to see what's stale

```
Which docs haven't been reviewed in 90+ days?
```

Claude greps for `Last Reviewed` stamps older than the threshold and reports. All files audited in R2 have `2026-04-05` stamps — next review window opens ~July 2026.

### Scenario: A project is complete and I want to clean up

```
/repo-cleanup projects/{project-name}
```

Validates repository structure, identifies orphaned files, enforces conventions.

---

## Deployment

### Scenario: I need to deploy the BFF API

```
/azure-deploy
```
Or specifically: `/bff-deploy`

### Scenario: I need to deploy a PCF control

```
/pcf-deploy
```

### Scenario: I need to deploy a Code Page web resource

```
/code-page-deploy
```

### Scenario: I need to deploy to Dataverse (solutions, plugins, web resources)

```
/dataverse-deploy
```

### Scenario: I need to deploy the external workspace SPA

```
/power-page-deploy
```

### Scenario: I want to verify a deployment worked

See `docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md` — consolidated post-deploy checks for all component types.

---

## AI Playbooks (JPS)

### Scenario: I want to design a new AI playbook

```
/jps-playbook-design
```

Loads scope catalog from `.claude/catalogs/scope-model-index.json`, guides through design → scope selection → deploy → verify.

### Scenario: I want to audit existing playbooks

```
/jps-playbook-audit
```

Reviews all playbooks against current scope catalog and standards.

### Scenario: I want to create a new Analysis Action

```
/jps-action-create
```

### Scenario: I want to refresh the scope catalog from Dataverse

```
/jps-scope-refresh
```

---

## Parallel Development

### Scenario: I want to work on multiple projects simultaneously

```
/worktree-setup
```

Creates an isolated git worktree. Each worktree has its own branch and working directory. See `docs/procedures/parallel-claude-sessions.md` for the full guide.

### Scenario: I want to sync my worktree with master

```
/worktree-sync
```

Ensures worktree is committed, pushed, merged to master, and updated from master.

---

## Context & Session Management

### Scenario: I want to save my progress before stopping

```
/checkpoint
```

Saves current state to `current-task.md` for reliable recovery.

### Scenario: Context is getting large

At **60% context**: Claude auto-checkpoints and continues.
At **70% context**: Claude checkpoints and requests `/compact`.
At **>85%**: Emergency checkpoint and stop.

You can manually trigger:
```
/compact
```

### Scenario: I want to start fresh

```
/clear
```

Wipes conversation. Use `/checkpoint` first if you have unsaved state.

---

## Troubleshooting

### Scenario: CI is failing on my PR

```
/ci-cd
```

Shows CI status, identifies failures, suggests fixes.

### Scenario: I need to clean up dev environment caches

```
/dev-cleanup
```

Clears Azure CLI, NuGet, npm, and Git credential caches.

### Scenario: I'm getting merge conflicts

```
/conflict-check
```

Detects file overlap between your work and active PRs. Helps prevent conflicts before they happen.

### Scenario: Claude Code keeps asking for permission

Press **Shift+Tab** to cycle permission modes:
- **Accept Edits** (⏵⏵) — auto-approves file changes (recommended for implementation)
- **Plan Mode** (⏸) — read-only, no changes (recommended for planning/analysis)

Or pre-approve common tools in `.claude/settings.json` `permissions.allow` list.

---

## Quick Command Reference

| I want to... | Command |
|---|---|
| Start new project | `/project-pipeline projects/{name}` |
| Transform design doc | `/design-to-spec projects/{name}` |
| Work on next task | `continue` or `work on task {N}` |
| Run tasks in parallel | `work on Phase {N} tasks in parallel` |
| Resume after restart | `where was I?` |
| Code review | `/code-review` |
| ADR compliance check | `/adr-check` |
| Push to GitHub | `/push-to-github` |
| Merge to master | `/merge-to-master` |
| Check doc accuracy | `/doc-drift-audit` |
| Create architecture doc | `create architecture doc for {X}` |
| Update procedures | `/ai-procedure-maintenance` |
| Deploy BFF API | `/bff-deploy` |
| Deploy PCF | `/pcf-deploy` |
| Deploy to Dataverse | `/dataverse-deploy` |
| Save progress | `/checkpoint` |
| Clean up repo | `/repo-cleanup` |
| Design AI playbook | `/jps-playbook-design` |
| Create worktree | `/worktree-setup` |

---

## Key Principles

1. **Code is the source of truth** — read code first, docs second
2. **Always use skills** — never bypass task-execute or skip quality gates
3. **Plan Mode before implementing** — enforced in project-pipeline and design-to-spec
4. **Parallel when safe** — tasks with disjoint files run concurrently; shared files force sequential
5. **Sub-agents can't write to `.claude/`** — by design; main session applies fixes
6. **Stamp what you review** — every verified file gets `Last Reviewed` header
7. **Drift audits at transitions** — run `/doc-drift-audit` between project iterations

---

*Created during ai-procedure-refactoring-r2. Maintained as a living document — update when new skills or procedures are added.*
