# How to Initiate a New Project

> **Last Updated**: December 5, 2025
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
│  STEP 1                STEP 2                STEP 3                STEP 4   │
│  ──────                ──────                ──────                ──────   │
│                                                                              │
│  Create Folder    →    Add Spec       →    Initialize      →    Create     │
│  & Design Spec         (if not done)       Project              Tasks       │
│                                                                              │
│  ┌──────────┐         ┌──────────┐         ┌──────────┐         ┌────────┐ │
│  │ projects/│         │ spec.md  │         │ README   │         │ tasks/ │ │
│  │ {name}/  │         │          │         │ plan.md  │         │ *.poml │ │
│  └──────────┘         └──────────┘         │ CLAUDE   │         └────────┘ │
│                                            └──────────┘                     │
│       │                    │                    │                    │      │
│       │                    │                    │                    │      │
│       ▼                    ▼                    ▼                    ▼      │
│                                                                              │
│    Manual              Manual            /project-init          /task-create │
│    or                  or                or                     or          │
│    /new-project        /new-project      /design-to-project     (auto)      │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 1.2 Two Paths to Start

| Path | When to Use | Commands |
|------|-------------|----------|
| **Quick Path** | You have a spec ready | `/project-init` then `/task-create` |
| **Full Pipeline** | You want guided validation | `/design-to-project` (does both + validation) |
| **Interactive** | You're starting from scratch | `/new-project` (wizard) |

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

| Skill | File | Purpose |
|-------|------|---------|
| `project-init` | `.claude/skills/project-init/SKILL.md` | Create folder structure and initial artifacts |
| `task-create` | `.claude/skills/task-create/SKILL.md` | Decompose plan into POML task files |
| `design-to-project` | `.claude/skills/design-to-project/SKILL.md` | Full 5-phase pipeline (orchestrates both above) |
| `adr-aware` | `.claude/skills/adr-aware/SKILL.md` | Auto-load relevant ADRs (always-apply) |
| `spaarke-conventions` | `.claude/skills/spaarke-conventions/SKILL.md` | Apply coding standards (always-apply) |

### 2.3 Slash Commands

| Command | Purpose | Invokes Skill |
|---------|---------|---------------|
| `/project-status [name]` | Check project status, get recommendations | - |
| `/new-project` | Interactive wizard for new projects | `project-init` + `task-create` |
| `/project-init {path}` | Initialize project from spec | `project-init` |
| `/design-to-project {path}` | Full pipeline with validation | `design-to-project` |
| `/task-create {path}` | Create task files from plan | `task-create` |

### 2.4 Templates (Referenced by Skills)

| Template | Location | Used By |
|----------|----------|---------|
| Project README | `docs/ai-knowledge/templates/project-README.template.md` | `project-init` |
| Project Plan | `docs/ai-knowledge/templates/project-plan.template.md` | `project-init` |
| Task Execution | `docs/ai-knowledge/templates/task-execution.template.md` | `task-create` |

---

## 3. Quick Start

### 3.1 Fastest Path (You Have a Spec)

```bash
# 1. Create project folder
mkdir projects/my-feature

# 2. Add your spec file
# Place your design specification at: projects/my-feature/spec.md

# 3. Tell Claude to initialize
```

Then say to Claude:
```
Initialize the project at projects/my-feature
```

Or use the command:
```
/project-init projects/my-feature
```

Then:
```
/task-create projects/my-feature
```

### 3.2 Full Pipeline (With Validation)

```
/design-to-project projects/my-feature
```

This runs all 5 phases:
1. **Ingest** - Extracts key info from spec
2. **Context** - Gathers ADRs and existing patterns
3. **Generate** - Creates README, plan, tasks
4. **Validate** - Checks everything before proceeding
5. **Implement** - (Waits for your approval)

### 3.3 Interactive (Starting From Scratch)

```
/new-project
```

The wizard will ask:
1. Project name
2. Whether you have a spec
3. Help create a spec if needed
4. Run initialization automatically

---

## 4. Detailed Process

### Step 1: Create Project Folder

**Location**: `projects/{project-name}/`

**Naming Convention**:
- Use `kebab-case`
- Be descriptive (e.g., `mda-darkmode-theme`, `sdap-fileviewer-enhancements`)
- No abbreviations unless well-known

**Manual**:
```bash
mkdir projects/my-awesome-feature
```

**Or via wizard**:
```
/new-project
```

---

### Step 2: Create Design Specification

**Location**: `projects/{project-name}/spec.md`

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

### Step 3: Initialize Project

**Command**: `/project-init projects/{name}`

**Or trigger phrase**: "initialize project at projects/{name}"

**What happens**:
1. AI loads `.claude/skills/project-init/SKILL.md`
2. Validates spec.md exists
3. Extracts key information from spec
4. Creates folder structure
5. Generates README.md, plan.md, CLAUDE.md
6. Creates tasks/ and notes/ directories

**Output**:
```
✅ Project initialized: projects/my-feature/

Created files:
  - README.md (project overview)
  - plan.md (implementation plan)
  - CLAUDE.md (AI context)
  - tasks/.gitkeep
  - notes/.gitkeep

Next steps:
  1. Review README.md and plan.md for accuracy
  2. Run /task-create to decompose plan into tasks
```

---

### Step 4: Create Task Files

**Command**: `/task-create projects/{name}`

**Or trigger phrase**: "create tasks for projects/{name}"

**What happens**:
1. AI loads `.claude/skills/task-create/SKILL.md`
2. Reads plan.md WBS (Work Breakdown Structure)
3. Decomposes into individual tasks
4. Creates POML files (valid XML) for each task
5. Creates TASK-INDEX.md with status tracking

**Output**:
```
✅ Tasks created for: projects/my-feature/

Task breakdown:
  Phase 1: 3 tasks (001-003)
  Phase 2: 4 tasks (010-013)
  Phase 3: 2 tasks (020-021)
  Total: 9 tasks

Files created:
  - tasks/TASK-INDEX.md
  - tasks/001-setup-infrastructure.poml
  - tasks/002-create-data-model.poml
  ...

Execution order recommendation:
  1. Start with task 001 (no dependencies)
```

---

### Step 5: Begin Implementation

**Check status**:
```
/project-status my-feature
```

**Start first task**:
```
Begin task 001
```

Or read the task file directly:
```
Read tasks/001-setup-infrastructure.poml and execute it
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
| `/new-project` | Interactive project wizard | `/new-project` |
| `/project-init {path}` | Initialize from spec | `/project-init projects/my-feature` |
| `/design-to-project {path}` | Full pipeline | `/design-to-project projects/my-feature` |
| `/task-create {path}` | Create tasks from plan | `/task-create projects/my-feature` |

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
| "initialize project", "create project", "start project" | `project-init` |
| "implement spec", "design to project", "transform spec" | `design-to-project` |
| "create tasks", "decompose plan", "generate tasks" | `task-create` |
| "review code", "code review" | `code-review` |
| "check ADRs", "validate architecture" | `adr-check` |

---

## 7. Troubleshooting

### 7.1 Common Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| "spec.md not found" | Spec file missing or wrong location | Create `projects/{name}/spec.md` |
| "Project already initialized" | README.md exists | Review existing files or re-initialize |
| "plan.md missing WBS" | Plan lacks work breakdown | Add phases and deliverables to plan.md |
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
| Regenerate README/plan | `/project-init {path}` (will warn about existing) |
| Regenerate tasks only | Delete `tasks/*.poml`, run `/task-create {path}` |
| Full reset | Delete all except spec.md, run `/design-to-project {path}` |

---

## Quick Reference Card

```
┌─────────────────────────────────────────────────────────────────┐
│                    PROJECT INITIATION CHEAT SHEET               │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  CHECK STATUS          /project-status                          │
│                                                                 │
│  NEW PROJECT           /new-project                             │
│  (interactive)                                                  │
│                                                                 │
│  FROM SPEC             1. Create projects/{name}/spec.md        │
│  (manual)              2. /project-init projects/{name}         │
│                        3. /task-create projects/{name}          │
│                                                                 │
│  FROM SPEC             /design-to-project projects/{name}       │
│  (full pipeline)       (does steps 2-3 plus validation)         │
│                                                                 │
│  START WORK            "Begin task 001"                         │
│                                                                 │
│  REVIEW                /code-review                             │
│                        /adr-check                               │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Related Documents

- [Skills Index](.claude/skills/INDEX.md) - All available skills
- [ADR Index](docs/reference/adr/) - Architecture decisions
- [Root CLAUDE.md](CLAUDE.md) - Repository-wide AI instructions

---

*Last updated: December 5, 2025*
