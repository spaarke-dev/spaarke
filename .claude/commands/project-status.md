# /project-status

Check the status of all projects and get recommendations for next steps.

## Usage

```
/project-status [project-name]
```

**Examples**:
- `/project-status` - Scan all projects, show status summary
- `/project-status mda-darkmode-theme` - Show status of specific project

## What This Command Does

Scans the `projects/` directory and reports:
1. Project completeness (what artifacts exist)
2. Current phase and progress
3. Recommended next action

## Execution Instructions

When this command is invoked:

### Step 1: Scan Projects

```
FOR each folder in projects/:
  CHECK for:
    - spec.md (design specification)
    - README.md (project overview)
    - plan.md (implementation plan)
    - CLAUDE.md (AI context)
    - tasks/TASK-INDEX.md (task tracker)
    - tasks/*.poml (task files)
```

### Step 2: Determine Status

| State | Condition | Next Action |
|-------|-----------|-------------|
| 📋 **Spec Only** | spec.md exists, no README.md | `/project-init` |
| 📝 **Initialized** | README.md + plan.md exist, tasks/ empty | `/task-create` |
| 🚧 **In Progress** | Tasks exist, some not completed | Continue task execution |
| ✅ **Complete** | All tasks completed | Deploy or close project |
| ⚠️ **Incomplete** | Missing required files | Manual review needed |

### Step 3: Output Report

```
📊 Project Status Report
========================

projects/mda-darkmode-theme/
  Status: 📝 Initialized (needs task decomposition)
  Files:  ✅ spec.md  ✅ README.md  ✅ plan.md  ❌ tasks/
  Action: Run /task-create projects/mda-darkmode-theme

projects/sdap-fileviewer-enhancements-1/
  Status: ✅ Complete
  Files:  ✅ spec.md  ✅ README.md  ✅ plan.md  ✅ tasks/
  Tasks:  22/22 completed

projects/new-feature/
  Status: 📋 Spec Only (needs initialization)
  Files:  ✅ spec.md  ❌ README.md  ❌ plan.md  ❌ tasks/
  Action: Run /project-init projects/new-feature

─────────────────────────────
Summary: 3 projects (1 complete, 2 need action)
```

### Step 4: For Specific Project

If a project name is provided, show detailed status:

```
📊 Project: mda-darkmode-theme
==============================

Phase: Initialized
Progress: 0% (0/10 tasks)

Files:
  ✅ spec.md - Design specification
  ✅ README.md - Project overview
  ✅ plan.md - Implementation plan
  ✅ CLAUDE.md - AI context
  ✅ tasks/TASK-INDEX.md - Task index
  ✅ tasks/*.poml - 10 task files

Task Summary:
  Phase 1 (Shared Infrastructure): 0/3 complete
  Phase 2 (PCF Control Updates): 0/3 complete
  Phase 3 (Ribbon Configuration): 0/1 complete
  Phase 4 (Documentation & Testing): 0/3 complete

Next Task: 001-create-theme-storage.poml
  Title: Create themeStorage.ts Utilities
  Dependencies: none

To start: "Begin task 001" or read tasks/001-create-theme-storage.poml
```

## When to Use

- Starting a work session focused on projects
- Checking what needs attention across all projects
- Finding where you left off on a specific project
- Getting the recommended next action

## Related Commands

- `/project-init` - Initialize a new project
- `/task-create` - Create task files from plan
- `/new-project` - Interactive wizard for new projects
