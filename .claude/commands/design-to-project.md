# /design-to-project

Transform a design specification into a complete project with implementation-ready tasks.

## Usage

```
/design-to-project {project-path}
```

**Example**: `/design-to-project projects/mda-darkmode-theme`

## What This Command Does

This command executes the full `design-to-project` skill - a 5-phase pipeline that transforms a design spec into executable project artifacts.

## Prerequisites

Before running this command:
1. Create the project folder: `projects/{project-name}/`
2. Add the design spec: `projects/{project-name}/spec.md`

## Execution Instructions

**IMPORTANT**: When this command is invoked, you MUST:

1. **Load the skill**: Read `.claude/skills/design-to-project/SKILL.md`
2. **Execute all 5 phases** in order:
   - Phase 1: INGEST - Extract key info from spec
   - Phase 2: CONTEXT - Gather ADRs, architecture, existing code
   - Phase 3: GENERATE - Create README, plan, task files
   - Phase 4: VALIDATE - Cross-reference checklist
   - Phase 5: IMPLEMENT - (Optional, wait for user approval)
3. **Output structured status** after each phase

## Skill Location

`.claude/skills/design-to-project/SKILL.md`

## Phase Outputs

### Phase 1: Ingest
- Design spec summary with feature name, problem, solution, complexity

### Phase 2: Context
- Applicable ADRs identified
- Reusable components catalogued
- Key knowledge articles referenced

### Phase 3: Generate (calls project-init + task-create)
- `README.md` - Project overview with graduation criteria
- `plan.md` - Implementation plan with WBS
- `CLAUDE.md` - AI context file
- `tasks/TASK-INDEX.md` - Task tracker
- `tasks/*.poml` - Individual task files

### Phase 4: Validate
- Cross-reference checklist (ADRs, scope, criteria)
- "Ready for Development" summary

### Phase 5: Implement
- Waits for user approval before starting
- Executes tasks with context management

## Orchestrated Skills

This command orchestrates:
- `project-init` - Creates folder structure and initial artifacts
- `task-create` - Decomposes plan into POML task files
- `adr-aware` - Loads relevant ADRs during context phase
- `spaarke-conventions` - Applied during implementation
