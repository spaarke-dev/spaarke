# project-init

---
description: Initialize a new project folder structure with README, plan, tasks directory, and CLAUDE.md from a design specification
alwaysApply: false
---

## Purpose

Creates the foundational project structure under `docs/projects/{project-name}/` when starting a new development initiative. This skill implements Phase 3 (Generate) of the AI-AGENT-PLAYBOOK by producing scaffolded project artifacts from a design specification.

## When to Use

- User says "initialize project", "create project", or "start project {name}"
- A design specification exists and needs to be converted to an actionable project
- Explicitly invoked with `/project-init {project-name}`

## Inputs Required

| Input | Required | Source |
|-------|----------|--------|
| Project path | Yes | Path to `projects/{project-name}/` folder (name derived from folder) |
| Design specification | Yes | Must exist at `projects/{project-name}/spec.md` |
| Complexity estimate | No | Default: "medium" - affects task granularity |

### Design Spec Location

The design specification should live **with the project** at:
```
projects/{project-name}/spec.md
```

**Workflow:**

1. **Operator creates project folder**: `projects/{descriptive-project-name}/`
2. **Operator places spec**: `projects/{project-name}/spec.md`
3. **Invoke skill**: `/project-init projects/{project-name}` or just provide the path
4. **Skill derives project name** from the folder name automatically

**Why root-level `projects/`?**
- Separates active project work from reference documentation
- Operator controls naming (descriptive, meaningful names)
- Everything about a project lives in one folder
- Clear traceability from design → implementation

## Workflow

### Step 1: Validate Inputs
```
IF project path provided:
  EXTRACT project-name from folder name
ELSE:
  ASK user for project path

IF projects/{project-name}/spec.md does NOT exist:
  → STOP - "spec.md not found. Please create projects/{project-name}/spec.md first."

IF projects/{project-name}/README.md already exists:
  → WARN - "Project already initialized. Continue anyway?"
  → Offer to view existing project or re-initialize
```

### Step 2: Load Context
```
LOAD templates:
  - docs/ai-knowledge/templates/project-README.template.md
  - docs/ai-knowledge/templates/project-plan.template.md
  
LOAD projects/{project-name}/spec.md
EXTRACT: problem statement, scope, success criteria, technical constraints
```

### Step 3: Create Folder Structure
```
projects/{project-name}/
├── spec.md            # Design specification (input - already exists)
├── README.md          # Project overview (generated)
├── plan.md            # Implementation plan (generated)
├── CLAUDE.md          # AI context file for this project (generated)
├── tasks/             # Task files go here
│   └── .gitkeep
└── notes/             # Ephemeral working files
    ├── .gitkeep
    ├── debug/         # Debugging session artifacts
    ├── spikes/        # Exploratory code/research
    ├── drafts/        # Work-in-progress content
    └── handoffs/      # Context reset summaries
```

**Notes directory purpose**: Store temporary artifacts during development (debug logs, spike code, drafts, handoff summaries). Contents may be deleted after project completion. See `task-execution.template.md` for detailed guidance.

### Step 4: Generate README.md
Use `project-README.template.md` structure:
- **Title**: Project name in Title Case
- **Quick Links**: Pre-fill with plan.md and tasks/ paths
- **Overview**: Extract from design spec or prompt user
- **Problem Statement**: Direct copy from design spec
- **Proposed Solution**: High-level approach from design spec
- **Scope**: In-scope/out-of-scope from design spec
- **Graduation Criteria**: Success criteria as checklist

### Step 5: Generate plan.md
Use `project-plan.template.md` structure:
- **Section 1 (Overview)**: Populated from design spec
- **Section 5 (WBS)**: Create phase structure based on complexity:
  - Simple: 2-3 phases
  - Medium: 4-5 phases  
  - Complex: 6+ phases with explicit dependencies
- **Section 7 (Risks)**: Extract from design spec constraints
- Leave detailed task breakdown for `task-create` skill

### Step 6: Generate CLAUDE.md
Create project-specific AI context file:
```markdown
# {Project Name} - AI Context

## Project Status
- **Phase**: Planning
- **Last Updated**: {today}
- **Next Action**: Run task-create to decompose plan

## Key Files
- `spec.md` - Original design specification (permanent reference)
- `README.md` - Project overview and graduation criteria
- `plan.md` - Implementation plan and WBS
- `tasks/` - Individual task files (POML format)

## Context Loading Rules
1. Always load this file first when working on {project-name}
2. Reference spec.md for design decisions and requirements
3. Load relevant task file from tasks/ based on current work

## Decisions Made
<!-- Log key decisions here as project progresses -->

## Current Constraints
{extracted from design spec}
```

### Step 7: Output Summary
```
✅ Project initialized: projects/{project-name}/

Created files:
  - README.md (project overview)
  - plan.md (implementation plan)
  - CLAUDE.md (AI context)
  - tasks/.gitkeep
  - notes/.gitkeep (with subdirectories)

Existing files:
  - spec.md (design specification - input)

Next steps:
  1. Review README.md and plan.md for accuracy
  2. Run /task-create to decompose plan into tasks
  3. Create feature branch and optionally draft PR (see below)
  4. Begin Phase 1 implementation
```

### Step 8: Create Feature Branch (Recommended)

After project initialization, create a feature branch for isolation:

```powershell
# Create feature branch (naming matches project folder)
git checkout -b feature/{project-name}

# Commit project artifacts
git add projects/{project-name}/
git commit -m "feat({scope}): initialize {project-name} project"

# Push to remote
git push -u origin feature/{project-name}

# Optional: Create draft PR for visibility
gh pr create --draft --title "feat({scope}): {project-name}" \
  --body "## Summary\nImplementation of {project-name}\n\n## Status\n- [x] Project initialized\n- [ ] Tasks created\n- [ ] Implementation\n- [ ] Ready for review"
```

**Why create branch now?**
- Isolates project work from master
- Enables incremental commits during implementation
- Draft PR provides visibility to team
- Clean merge when project completes

## Conventions

### Naming
- Project folder: `kebab-case` (e.g., `sdap-refactor`, `spe-integration`)
- Files: lowercase with hyphens
- No abbreviations in project names unless well-known (e.g., `sdap`, `spe`)

### Content Standards
- README.md should be readable in under 2 minutes
- plan.md WBS phases should map to logical milestones
- Each phase in plan.md should have 3-7 tasks (decompose further if more)

### Graduation Criteria
Every project must have measurable graduation criteria:
- At least one functional requirement (feature works)
- At least one quality requirement (tests pass, no regressions)
- Optional: performance, security, documentation requirements

## Resources

### Templates (Auto-loaded)
- `docs/ai-knowledge/templates/project-README.template.md`
- `docs/ai-knowledge/templates/project-plan.template.md`

### Related Skills
- **task-create**: Decompose plan.md into task files (run after project-init)
- **design-to-project**: Full pipeline from design spec (includes project-init)

## Examples

### Example 1: Initialize from Existing Spec File
**Trigger**: "/project-init projects/sdap-refactor" (spec already at `projects/sdap-refactor/spec.md`)

**Result**:
```
projects/sdap-refactor/
├── spec.md             # Already existed - used as input
├── README.md           # Generated from spec.md
├── plan.md             # 6 phases matching spec sections
├── CLAUDE.md           # References spec.md as source
├── tasks/
└── notes/
```

### Example 2: Initialize FileViewer Enhancements
**Trigger**: "/project-init projects/sdap-fileviewer-enhancements-1"

**Result**:
```
projects/sdap-fileviewer-enhancements-1/
├── spec.md             # Operator placed this first
├── README.md           # Generated with goals, scope, criteria
├── plan.md             # Phases: BFF endpoint, PCF update, performance
├── CLAUDE.md           # Project-specific AI context
├── tasks/
└── notes/
```

### Example 3: Missing Spec Error
**Trigger**: "/project-init projects/new-feature"

**Result** (if spec.md doesn't exist):
```
❌ Cannot initialize: projects/new-feature/spec.md not found.

Please create the spec file first:
  1. Create folder: projects/new-feature/
  2. Add design spec: projects/new-feature/spec.md
  3. Re-run: /project-init projects/new-feature
```

## Validation Checklist

Before completing project-init, verify:
- [ ] spec.md exists and was read successfully
- [ ] Project name derived from folder name correctly
- [ ] README.md has problem statement from spec
- [ ] plan.md has at least one WBS phase
- [ ] CLAUDE.md references spec.md as source
- [ ] Graduation criteria are measurable (not vague)
- [ ] No PII or secrets in any generated file
