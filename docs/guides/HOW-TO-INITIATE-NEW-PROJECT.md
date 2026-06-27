# How to Initiate a New Project

> **Last Updated**: December 24, 2025
>
> **Purpose**: Step-by-step guide for starting new development projects using AI-assisted workflows.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Components Reference](#2-components-reference)
3. [Quick Start](#3-quick-start)
4. [Detailed Process](#4-detailed-process)
5. [What Gets Created](#5-what-gets-created)
6. [Command Reference](#6-command-reference)
7. [Troubleshooting](#7-troubleshooting)

---

## 1. Overview

### 1.1 Process Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        PROJECT INITIATION FLOW                               │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  STEP 1                              STEP 2                                  │
│  ──────                              ──────                                  │
│                                                                              │
│  Create spec.md                  →   Run Pipeline                            │
│  (from design doc or manual)         (generates all artifacts + tasks)       │
│                                                                              │
│  ┌──────────────────┐               ┌──────────────────────────────────────┐ │
│  │ Design doc       │               │ README.md, plan.md, CLAUDE.md        │ │
│  │    ↓             │               │ tasks/*.poml, TASK-INDEX.md          │ │
│  │ /design-to-spec  │               │ Feature branch created               │ │
│  │    ↓             │               │                                      │ │
│  │ spec.md          │               │ /project-pipeline                    │ │
│  └──────────────────┘               └──────────────────────────────────────┘ │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 1.2 The 2-Step Workflow

| Step | Command | When to Use |
|------|---------|-------------|
| **Step 1** | `/design-to-spec {path}` | Transform human design doc → AI-optimized spec.md |
| **Step 2** | `/project-pipeline {path}` | Full pipeline: spec.md → ready tasks + branch |

**Note**: If you already have a spec.md, skip Step 1 and go directly to Step 2.

---

## 2. Components Reference

### 2.1 File Locations

| Component | Location | Purpose |
|-----------|----------|---------|
| **Skills** | `.claude/skills/` | Detailed procedures for AI to follow |
| **Commands** | `.claude/commands/` | Slash command definitions |
| **Root Config** | `CLAUDE.md` | Repository-wide AI instructions |
| **Skill Index** | `.claude/skills/INDEX.md` | Master list of all skills |

### 2.2 Skills Used in Project Initiation

| Skill | Type | Purpose |
|-------|------|---------|
| `design-to-spec` | Developer-facing | Transform design docs to spec.md |
| `project-pipeline` | Developer-facing | Full orchestrator (recommended) |
| `project-setup` | AI-internal | Generate artifacts (called by pipeline) |
| `task-create` | AI-internal | Create task files (called by pipeline) |
| `adr-aware` | Always-apply | Auto-load relevant ADRs |
| `spaarke-conventions` | Always-apply | Apply coding standards |

### 2.3 Slash Commands

| Command | Purpose |
|---------|---------|
| `/project-status [name]` | Check project status, get recommendations |
| `/design-to-spec {path}` | Transform design doc to spec.md (Step 1) |
| `/project-pipeline {path}` | Full pipeline from spec.md to ready tasks (Step 2) |
| `/repo-cleanup [path]` | Clean up after project completion |

### 2.4 Templates (Referenced by Skills)

| Template | Location | Used By |
|----------|----------|---------|
| Project README | `docs/ai-knowledge/templates/project-README.template.md` | `project-setup` |
| Project Plan | `docs/ai-knowledge/templates/project-plan.template.md` | `project-setup` |
| Task Execution | `docs/ai-knowledge/templates/task-execution.template.md` | `task-create` |

---

## 3. Quick Start

### 3.1 If You Have a Design Document

```bash
# 1. Create project folder and place design doc
mkdir projects/my-feature
# Copy design.md, design.docx, or notes to projects/my-feature/

# 2. Transform to spec.md
/design-to-spec projects/my-feature

# 3. Review spec.md, then run full pipeline
/project-pipeline projects/my-feature
```

### 3.2 If You Already Have a spec.md

```bash
# 1. Create project folder with spec
mkdir projects/my-feature
# Place spec.md at projects/my-feature/spec.md

# 2. Run full pipeline
/project-pipeline projects/my-feature
```

### 3.3 Starting From Scratch

If you don't have any documentation yet:

1. Create `projects/my-feature/spec.md` manually with:
   - Problem statement
   - Proposed solution
   - Scope (in/out)
   - Technical approach
   - Acceptance criteria

2. Run `/project-pipeline projects/my-feature`

---

## 4. Detailed Process

### Step 1: Create Design Specification

**Location**: `projects/{project-name}/spec.md`

**Option A: Transform from design doc**
```
/design-to-spec projects/my-feature
```

This reads `design.md` (or similar) and creates an AI-optimized `spec.md`.

**Option B: Write spec.md manually**

**Required Sections**:
- Problem statement / Background
- Proposed solution
- Scope (in/out)
- Technical approach
- Acceptance criteria

**Example Minimal Spec**:
```markdown
# Feature Name

## Problem
What problem does this solve?

## Solution
High-level approach to solve it.

## Scope
### In Scope
- Item 1
- Item 2

### Out of Scope
- Item A

## Technical Approach
How will this be implemented?

## Acceptance Criteria
- [ ] Criterion 1
- [ ] Criterion 2
```

---

### Step 2: Run Project Pipeline

**Command**: `/project-pipeline projects/{name}`

**What happens**:
1. **Validates** spec.md exists and is well-formed
2. **Discovers** applicable ADRs, skills, patterns, knowledge docs
3. **Generates** README.md, plan.md, CLAUDE.md, folder structure
4. **Creates** 50-200+ task files (POML format) with full context
5. **Creates** feature branch and initial commit
6. **Optionally** starts task 001 (you'll be asked)

**Human-in-loop confirmations** after each major step.

**Output**:
```
✅ Project initialized: projects/my-feature/

Created files:
  - README.md (project overview)
  - plan.md (implementation plan)
  - CLAUDE.md (AI context)
  - tasks/TASK-INDEX.md
  - tasks/*.poml (N task files)

Branch created: work/my-feature

Next steps:
  1. Review README.md for accuracy
  2. Check tasks/TASK-INDEX.md for execution order
  3. Start with task 001

To begin: "work on task 001"
```

---

### Step 3: Execute Tasks

**Start first task**:
```
work on task 001
```

Or be more explicit:
```
/task-execute projects/my-feature/tasks/001-*.poml
```

**Check progress**:
```
/project-status my-feature
```

---

## 5. What Gets Created

### 5.1 Final Project Structure

```
projects/{project-name}/
├── spec.md                    # Design specification (INPUT - you create this)
├── README.md                  # Project overview (GENERATED)
├── plan.md                    # Implementation plan with WBS (GENERATED)
├── CLAUDE.md                  # AI context for this project (GENERATED)
├── tasks/                     # Task files directory (GENERATED)
│   ├── TASK-INDEX.md          # Task status tracker
│   ├── 001-{slug}.poml        # Phase 1 tasks
│   ├── 002-{slug}.poml
│   ├── 010-{slug}.poml        # Phase 2 tasks
│   ├── 011-{slug}.poml
│   └── ...
└── notes/                     # Working files directory (GENERATED)
    ├── debug/                 # Debugging artifacts
    ├── spikes/                # Exploratory code
    ├── drafts/                # Work-in-progress
    └── handoffs/              # Context handoff summaries
```

### 5.2 File Descriptions

| File | Purpose | Contents |
|------|---------|----------|
| `spec.md` | Source of truth for requirements | Problem, solution, scope, criteria |
| `README.md` | Project overview for humans and AI | Status, scope, decisions, risks |
| `plan.md` | Implementation roadmap | Phases, WBS, estimates, milestones |
| `CLAUDE.md` | AI-specific context | Key files, constraints, next actions |
| `TASK-INDEX.md` | Progress tracking | Task list with status indicators |
| `*.poml` | Executable task definitions | Steps, constraints, acceptance criteria |

### 5.3 Task File Format (POML)

Each `.poml` file is valid XML:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<task id="001" project="my-feature">
  <metadata>
    <title>Setup Infrastructure</title>
    <phase>1: Foundation</phase>
    <status>not-started</status>
    <estimated-hours>2</estimated-hours>
    <dependencies>none</dependencies>
  </metadata>

  <prompt>
    Create the base infrastructure for the feature...
  </prompt>

  <goal>
    Working foundation with tests passing.
  </goal>

  <steps>
    <step order="1">Create folder structure</step>
    <step order="2">Add dependencies</step>
    <step order="3">Write unit tests</step>
  </steps>

  <acceptance-criteria>
    <criterion testable="true">All tests pass</criterion>
  </acceptance-criteria>
</task>
```

---

## 6. Command Reference

### 6.1 Project Management Commands

| Command | Description | Example |
|---------|-------------|---------|
| `/project-status` | List all projects with status | `/project-status` |
| `/project-status {name}` | Detailed status of one project | `/project-status mda-darkmode` |
| `/design-to-spec {path}` | Transform design doc to spec.md | `/design-to-spec projects/my-feature` |
| `/project-pipeline {path}` | Full pipeline (recommended) | `/project-pipeline projects/my-feature` |
| `/repo-cleanup [path]` | Clean up after completion | `/repo-cleanup projects/my-feature` |

### 6.2 Quality Commands

| Command | Description | Example |
|---------|-------------|---------|
| `/code-review` | Review recent changes | `/code-review` |
| `/code-review {path}` | Review specific path | `/code-review src/server/api/` |
| `/adr-check` | Validate ADR compliance | `/adr-check` |
| `/adr-check {path}` | Check specific path | `/adr-check src/client/pcf/` |

### 6.3 Trigger Phrases

These phrases automatically invoke skills:

| Phrase | Invokes |
|--------|---------|
| "transform spec", "design to spec", "create AI spec" | `design-to-spec` |
| "run pipeline", "initialize project from spec", "start project" | `project-pipeline` |
| "work on task", "begin task", "execute task" | `task-execute` |
| "review code", "code review" | `code-review` |
| "check ADRs", "validate architecture" | `adr-check` |

---

## 7. Troubleshooting

### 7.1 Common Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| "spec.md not found" | Spec file missing or wrong location | Create `projects/{name}/spec.md` |
| "Project already initialized" | README.md exists | Review existing files or delete to re-initialize |
| "plan.md missing WBS" | Plan lacks work breakdown | Ensure plan.md has phases and deliverables |
| Tasks not created | plan.md has no phases | Ensure plan.md Section 5 has WBS |

### 7.2 Validation Checklist

Before starting implementation, verify:

- [ ] `spec.md` exists with problem, solution, scope, criteria
- [ ] `README.md` has graduation criteria (measurable)
- [ ] `plan.md` has WBS with at least one phase
- [ ] `CLAUDE.md` references spec.md
- [ ] `tasks/TASK-INDEX.md` exists with task list
- [ ] Task dependencies form valid order (no circular refs)
- [ ] First task(s) have no unmet dependencies

### 7.3 Re-Running Commands

| Scenario | Command |
|----------|---------|
| Regenerate everything | Delete all except spec.md, run `/project-pipeline {path}` |
| Regenerate tasks only | Delete `tasks/*.poml`, pipeline will detect and regenerate |

---

## Quick Reference Card

```
┌─────────────────────────────────────────────────────────────────┐
│                    PROJECT INITIATION CHEAT SHEET               │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  CHECK STATUS          /project-status                          │
│                                                                 │
│  FROM DESIGN DOC       1. /design-to-spec projects/{name}       │
│  (2-step)              2. Review spec.md                        │
│                        3. /project-pipeline projects/{name}     │
│                                                                 │
│  FROM SPEC             /project-pipeline projects/{name}        │
│  (1-step)              (if spec.md already exists)              │
│                                                                 │
│  START WORK            "work on task 001"                       │
│                                                                 │
│  AFTER COMPLETE        /repo-cleanup projects/{name}            │
│                                                                 │
│  REVIEW                /code-review                             │
│                        /adr-check                               │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Related Documents

- [Skills Index](.claude/skills/INDEX.md) - All available skills
- [ADR Index](docs/adr/INDEX.md) - Architecture decisions
- [Root CLAUDE.md](CLAUDE.md) - Repository-wide AI instructions

---

*Last updated: December 24, 2025*



---

## Portfolio Integration (added 2026-06-23 by spaarke-devops-project-tracking-r1 · FR-29)

Spaarke runs ~30+ active projects as git worktrees. **Project #2** ([github.com/users/spaarke-dev/projects/2](https://github.com/users/spaarke-dev/projects/2)) is the single portfolio surface — Epics → Projects → POML tasks. The day-to-day development workflow keeps the board current automatically via 9 hook-injected existing skills.

### Step 0 (NEW): Capture the idea

Before running `/design-to-spec`, capture the raw idea as an Idea Issue on the portfolio:

```bash
/devops-idea-create --summary "Add shared dashboard for portfolio stakeholders" --why-it-matters "Stakeholders ask weekly; takes ~30s to answer from a single page"
```

This creates a GitHub Issue with `Type=Idea`, label `backlog`. **No local folder or worktree is created.** Promotion to a Project happens later via `/devops-idea-promote`.

If the idea is already mature enough to be a project, skip Step 0 and start at Step 1 (the existing `/design-to-spec` workflow).

### Epic ↔ Project mechanics

Every Project must have an Epic parent (per D-12). Epics are the portfolio rollup; Projects are the unit of work.

- **Create an Epic**: `/devops-epic-create --title "AI Platform & Chat" --objectives "..." --scope "..." --success "..." --timeframe "H2 2026"`
- **Attach a Project to its Epic**: the `Parent issue` field on the Project Issue (set automatically by `/devops-idea-promote --epic #E` and `/devops-project-start`)

### Idea → Project promotion

Two paths per D-12:

**Path A** (1 → 1): a single Idea matures into a single Project:

```bash
/devops-idea-promote --to-project #N --epic #E
```

Flips the Idea's Type from `Idea` to `Project`, sets Parent issue to Epic #E, swaps label `backlog` → `project`. Issue number preserved.

**Path B** (N → 1): multiple Ideas package into one Project (with Idea sub-issues kept open per D-20):

```bash
/devops-idea-promote --package #X #Y #Z --epic #E
```

Creates a new Project Issue with #X, #Y, #Z as sub-issues. Original Ideas remain open.

### `/devops-project-start` — THE BLESSED HANDOFF (D-13)

The one canonical bridge from a portfolio Issue to a local worktree:

```bash
/devops-project-start --from-issue #N
```

In one invocation:
1. Reads Project Issue #N body + fields
2. Derives slug from Issue title (per F9: kebab-cased + `-r1`; auto-bumps `-r2+` if folder exists; `--slug <override>` available)
3. Creates `projects/{slug}-r1/` folder
4. Creates git worktree at `c:/code_files/spaarke-wt-{slug}-r1` with feature branch `work/{slug}-r1`
5. Drafts `design.md` skeleton populated from Issue body
6. Writes back `Worktree Path` + `Project Folder` field values to the Issue
7. Writes `> **Portfolio**: ...` header block to the new local `README.md`

For Path B promotions: `--absorbs #X #Y #Z` includes a "Source Ideas" section in `design.md`.

`--open-editor` (default OFF per D-21) launches VS Code after scaffolding.

### Auto-hook behaviors (no explicit `/devops-*` typing needed)

Once a project has a portfolio pointer block in its README, **9 existing skills automatically keep the portfolio current** during normal workflow (per FR-16..FR-24):

| Existing skill | Hook behavior |
|---|---|
| `/design-to-spec` | At end: sync + set Status=In Progress |
| `/project-pipeline` | At start: register-or-sync (prompts for `--epic` once) |
| `/task-create` | At end: set Task Count on Issue |
| `/task-execute` | At Step 9.6: increment Tasks Completed; prompt to promote at last task |
| `/context-handoff` | **Highest-value hook** — always sync at end of handoff |
| `/worktree-setup` | At end: link-or-prompt register |
| `/worktree-sync` | At end: sync |
| `/repo-cleanup` | Mid-skill: prompt to archive each candidate |
| `/merge-to-master` | After merge: PR comment + conditional archive prompt |

Hooks are silent on success (single `✅ Portfolio synced: #N` line) and degrade-to-warn on failure (NFR-03 — never block host skill). Hooks are no-ops on projects without portfolio pointer blocks.

### 9 `/devops-*` skills — command reference

| Skill | Purpose |
|---|---|
| `/devops-portfolio-setup` | One-shot idempotent bootstrap of Project #2 schema (Type=Project option, 6 fields, 7 labels, 3 issue templates) |
| `/devops-epic-create` | Create an Epic Issue |
| `/devops-idea-create` | Capture an Idea (no local side-effects) |
| `/devops-idea-promote` | Path A (1→1) or Path B (N→1) Idea → Project promotion |
| `/devops-project-start` | **THE BLESSED HANDOFF** — Issue → folder + worktree + design.md + field round-trip |
| `/devops-project-register` | Inverse of project-start — folder → Issue (used for backfill) |
| `/devops-project-sync` | Workhorse — idempotent local→Issue field sync (called by 5 hooks) |
| `/devops-portfolio-status` | Terminal dashboard + `--snapshot` for stakeholder narrative |
| `/devops-project-archive` | **DESTRUCTIVE** — set Status, close Issue, delete worktree, retain folder + `.archived` marker |

### Portfolio-specific troubleshooting

**Q: I just created a project but it's not showing in the Active view.**
- Check the Project Issue's `Project Status` field — must be `In Progress`. Run `/devops-project-sync` to recompute from local state. If still wrong, check that `current-task.md` shows an active task OR that the branch has commits in the last 30 days.

**Q: My worktree exists but there's no Project Issue.**
- Run `/devops-project-register --from-folder projects/{name} --epic #E` to backfill it.

**Q: Issue says Type=Project but Project Status field is empty.**
- The Project was created manually without using the skills. Run `/devops-project-sync` to populate fields from local state (assuming the README has the portfolio pointer block — otherwise re-register first).

**Q: I want to archive a project but the worktree has uncommitted changes.**
- `/devops-project-archive` refuses by default (safety contract). Either commit/stash first, OR re-run with `--force` (uncommitted work will be LOST).

**Q: Multiple projects on Project #2 board have the same Type=Project but no Parent issue.**
- D-12 violation — every Project must have an Epic parent. Set `Parent issue` manually in GitHub UI, OR re-run `/devops-project-register --epic #E` for each orphan.

---

## Related Documents

- [Skills Index](.claude/skills/INDEX.md) - All available skills (includes 9 `/devops-*` family)
- [ADR Index](docs/adr/INDEX.md) - Architecture decisions
- [Root CLAUDE.md](CLAUDE.md) - Repository-wide AI instructions
- [Project #2 (Spaarke Core)](https://github.com/users/spaarke-dev/projects/2) - Portfolio board
- [AI Coding Procedures Guide](docs/procedures/AI-CODING-PROCEDURES-GUIDE.md) - Lifecycle scenarios

---

*Portfolio Integration section added 2026-06-23 by spaarke-devops-project-tracking-r1 / FR-29*
