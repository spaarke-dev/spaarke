# /project-init

Initialize a new project from a design specification.

## Usage

```
/project-init {project-path}
```

**Example**: `/project-init projects/mda-darkmode-theme`

## What This Command Does

This command executes the `project-init` skill to create project artifacts from a design specification.

## Prerequisites

Before running this command:
1. Create the project folder: `projects/{project-name}/`
2. Add the design spec: `projects/{project-name}/spec.md`

## Execution Instructions

**IMPORTANT**: When this command is invoked, you MUST:

1. **Load the skill**: Read `.claude/skills/project-init/SKILL.md`
2. **Follow the procedure exactly** as documented in the skill
3. **Output structured status** at each step

## Skill Location

`.claude/skills/project-init/SKILL.md`

## Expected Outputs

After successful execution:
- `projects/{project-name}/README.md` - Project overview
- `projects/{project-name}/plan.md` - Implementation plan
- `projects/{project-name}/CLAUDE.md` - AI context file
- `projects/{project-name}/tasks/` - Empty tasks directory
- `projects/{project-name}/notes/` - Working files directory

## Next Steps

After project-init completes:
- Run `/task-create {project-path}` to decompose plan into tasks
- Or run `/design-to-project {project-path}` for the full pipeline
