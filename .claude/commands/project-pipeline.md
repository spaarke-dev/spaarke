# /project-pipeline

**🚀 RECOMMENDED**: Full automated pipeline from spec.md to ready-to-execute tasks.

## Usage

```
/project-pipeline {project-path}
```

**Example**: `/project-pipeline projects/mda-darkmode-theme`

## What This Command Does

This is the **recommended orchestrator** for project initialization. It chains multiple skills together with human-in-loop confirmations at each step:

1. ✅ Validates spec.md exists and is well-formed
2. ✅ Comprehensive resource discovery (ADRs, skills, patterns, knowledge docs)
3. ✅ Generates project artifacts (README.md, plan.md, CLAUDE.md)
4. ✅ Decomposes plan into POML task files (50-200+ tasks for large projects)
5. ✅ Creates feature branch and initial commit
6. ✅ Optionally auto-starts task 001

## Prerequisites

Before running this command:
1. `{project-path}/spec.md` must exist (run `/design-to-spec` first if needed)
2. Extended context recommended for complex projects:
   ```
   CLAUDE_CODE_MAX_OUTPUT_TOKENS=64000
   ```

## Execution Instructions

**IMPORTANT**: When this command is invoked, you MUST:

1. **Load the skill**: Read `.claude/skills/project-pipeline/SKILL.md`
2. **Follow all 5 steps** with human confirmation between each
3. **Do comprehensive resource discovery** (not just constraints)
4. **Create feature branch** using Spaarke git conventions
5. **Ask about auto-starting task 001**

## Skill Location

`.claude/skills/project-pipeline/SKILL.md`

## Expected Outputs

After successful execution:
- `{project-path}/README.md` - Project overview
- `{project-path}/plan.md` - Implementation plan with WBS
- `{project-path}/CLAUDE.md` - AI context file
- `{project-path}/tasks/TASK-INDEX.md` - Task tracker
- `{project-path}/tasks/*.poml` - Individual task files
- Git branch: `work/{project-name}` or `feature/{project-name}`

## Workflow Position

```
spec.md → /project-pipeline → Ready Tasks → Task Execution → Complete
              │
              ├── Step 1: Validate spec
              ├── Step 2: Resource discovery (CALLS project-setup internally)
              ├── Step 3: Task decomposition (CALLS task-create internally)
              ├── Step 4: Git branch + commit
              └── Step 5: Optional auto-start task 001
```

## When to Use

- **Always use this** instead of calling project-setup/task-create separately
- After creating or receiving a spec.md
- When starting any new development project

## Related Commands

- `/design-to-spec` - **Step 1**: Transform design doc to spec.md (run first if needed)
- `/project-status` - Check project state after initialization
- `/task-create` - (AI-internal) Called automatically by this command
