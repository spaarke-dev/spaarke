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

## Dataverse Operations (via MCP)

### Scenario: I need to inspect a Dataverse table schema

```
describe the sprk_matter table
```

**What happens**: Claude uses `mcp__dataverse__describe_table` to show T-SQL schema — columns, types, lookups, option sets. No manual Web API calls needed.

### Scenario: I need to query Dataverse data

```
show me all active matters with their assigned attorneys
```

**What happens**: Claude uses `mcp__dataverse__read_query` to execute a SELECT query directly against Dataverse and returns results.

### Scenario: I need to verify a deployment changed the schema correctly

```
verify that sprk_workassignment has the new priority field after deployment
```

**What happens**: Claude uses `mcp__dataverse__describe_table` to confirm the column exists with the correct type.

### Scenario: I need to create test data

```
create a test matter named "MCP Test" with status Draft
```

**What happens**: Claude uses `mcp__dataverse__create_record` to insert a row. Requires user confirmation for write operations.

### Scenario: I want to discover what tables exist

```
list all sprk_ tables in Dataverse
```

**What happens**: Claude uses `mcp__dataverse__list_tables` and filters to custom tables.

**Setup**: MCP tools are pre-configured in `.mcp.json`. New developers need one-time auth setup — see [`docs/guides/DATAVERSE-MCP-INTEGRATION-GUIDE.md`](../guides/DATAVERSE-MCP-INTEGRATION-GUIDE.md).

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


---

## Portfolio Scenarios (added 2026-06-23 by spaarke-devops-project-tracking-r1 · FR-30)

The following 7 scenarios use the 9 `/devops-*` skills + hook automation. Each follows the existing **"what to say / what happens automatically / what to check"** pattern.

### Scenario: Capture an idea before it's a real project

**What to say**: "Capture this idea — add shared dashboard for portfolio stakeholders"

**What happens automatically**:
- `/devops-idea-create` runs with the summary
- A GitHub Issue is created with `Type=Idea`, label `backlog`
- The Issue lands on Project #2's backlog view
- **No local folder, worktree, or branch is created**

**What to check**:
- `gh issue list --label backlog --state open --limit 5` shows your Idea at the top
- The Project #2 board's backlog view shows the Idea
- `projects/` directory is unchanged

---

### Scenario: Promote ideas into a project (with packaging)

**What to say**: "Promote idea #157 to a project under Epic #422" *(1 → 1, Path A)*
**Or**: "Package ideas #157, #158, #159 into one project under Epic #422" *(N → 1, Path B)*

**What happens automatically**:
- Path A: `/devops-idea-promote --to-project #157 --epic #422` flips Issue #157's Type to `Project`, sets Parent issue Epic #422, swaps label `backlog` → `project`. Number preserved.
- Path B: `/devops-idea-promote --package #157 #158 #159 --epic #422` creates a new Project Issue with the 3 Ideas as sub-issues (Ideas remain open per D-20).

**What to check**:
- Path A: `gh issue view 157 --json labels,number` shows `project` label.
- Path B: new Project Issue's body includes "Source Ideas: #157, #158, #159"; sub-issues panel shows count 3.

---

### Scenario: Update a project's status

**What to say**: "Sync my project's portfolio status" (or just continue normal work — the hooks fire automatically)

**What happens automatically**:
- During normal work (`/task-execute`, `/context-handoff`, `/worktree-sync`, etc.): hooks call `/devops-project-sync` after each operation.
- Per FR-20 (HIGHEST VALUE): `/context-handoff` always syncs at end — so every 3 task steps, the board refreshes.
- Fields updated: `Task Count`, `Tasks Completed`, `Project Status`, `Worktree Path`.

**What to check**:
- `gh issue view <#N> --json title,body` — body shows current Task Count + Tasks Completed.
- Project Status field reflects current state (In Progress / On Hold / Completed candidate).

---

### Scenario: Close (complete or cancel) a project

**What to say**: "Archive my completed project" (after merge) or "Cancel this project"

**What happens automatically**:
- `/merge-to-master` post-merge hook adds a "Merged via PR #M" comment to the Project Issue
- If all POML tasks complete AND PR merged → prompt "Archive project? [y/N]" (per F3 — explicit gate, never auto-archives)
- On Y: `/devops-project-archive --status Completed --pr-number #M` runs:
  1. Sets `Project Status = Completed`
  2. Closes Issue with `Status=Done`
  3. **Deletes the local git worktree** (per D-18; refuses if dirty unless `--force`)
  4. Retains `projects/{name}/` folder with new `.archived` marker file
  5. Preserves the remote branch (NFR-09)

**What to check**:
- `git worktree list` does NOT include the archived worktree
- `projects/{name}/.archived` exists with date + final status + PR ref
- `gh issue view <#N>` shows state=closed, comments include archive note

---

### Scenario: See what's running across all projects

**What to say**: "Show me the portfolio status"

**What happens automatically**:
- `/devops-portfolio-status` queries Project #2 + groups by Epic
- Prints concise terminal dashboard in <30 seconds (per Success Criterion 7):

```
Spaarke Portfolio — 2026-06-23
Epic                              | Total | In Prog | Planned | Hold | Done | Cancel
----------------------------------|-------|---------|---------|------|------|-------
AI Platform & Chat            #421 |    8  |    3    |    2    |  1   |  2   |   0
Insights Engine               #422 |    4  |    2    |    1    |  0   |  1   |   0
...
Totals: 30 projects (12 in progress, 8 planned, 3 on hold, 7 done, 0 cancelled)
```

**What to check**:
- Output is human-readable in <30 seconds (UX target)
- Counts match expectations from your knowledge of active work

---

### Scenario: Package multiple ideas into one project

**What to say**: "Package ideas #157, #158, #159 into one project under Epic #422"

(Same as Scenario 2 — Path B. Detailed there.)

**Why use this scenario**: when multiple loosely-related Ideas all converge on one engineering effort, packaging into a single Project Issue keeps the portfolio cleaner than 3 separate Project Issues. The N source Ideas remain visible as sub-issues for narrative context.

---

### Scenario: I'm a stakeholder — where do I look?

**What to say**: "Generate a stakeholder snapshot" (or asked of the engineering owner)

**What happens automatically**:
- `/devops-portfolio-status --snapshot` writes `docs/portfolio/snapshot-{YYYY-MM-DD}.md` — an Epic-by-Epic narrative (NOT raw field dump per D-10)
- The markdown reads like a briefing: "AI Platform & Chat (Epic #421) — 3 projects in progress: chat routing redesign 60% complete, capability router 40% complete, Foundry grounding 20% complete..."

**What to check**:
- File exists at `docs/portfolio/snapshot-{date}.md`
- Content has zero raw GitHub field IDs leaking (`PVT*` prefixes)
- Each Epic gets a paragraph; Projects under each Epic listed with brief status

**For ongoing stakeholder visibility**: the [Project #2 Portfolio Roadmap view](https://github.com/users/spaarke-dev/projects/2) is the live equivalent — answers the same questions in <30 seconds without generating a snapshot.

---

*Portfolio Scenarios section added 2026-06-23 by spaarke-devops-project-tracking-r1 / FR-30. See also: [HOW-TO-INITIATE-NEW-PROJECT.md § Portfolio Integration](../guides/HOW-TO-INITIATE-NEW-PROJECT.md#portfolio-integration-added-2026-06-23-by-spaarke-devops-project-tracking-r1--fr-29).*
