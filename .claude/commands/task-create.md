# /task-create

Decompose a project plan into numbered POML task files.

## Usage

```
/task-create {project-path}
```

**Example**: `/task-create projects/mda-darkmode-theme`

## What This Command Does

This command executes the `task-create` skill to transform `plan.md` work breakdown structure (WBS) into individual, executable task files in the `tasks/` directory.

## Prerequisites

Before running this command:
1. Project must be initialized (README.md, plan.md exist)
2. `plan.md` must have a Work Breakdown Structure (WBS) with phases

## Execution Instructions

**IMPORTANT**: When this command is invoked, you MUST:

1. **Load the skill**: Read `.claude/skills/task-create/SKILL.md`
2. **Follow the procedure exactly** as documented in the skill
3. **Create POML files** (valid XML) for each task
4. **Output structured status** with task breakdown summary

## Skill Location

`.claude/skills/task-create/SKILL.md`

## Expected Outputs

After successful execution:
- `tasks/TASK-INDEX.md` - Task index with status tracking
- `tasks/{NNN}-{slug}.poml` - Individual task files (POML/XML format)

## Task Numbering Convention

- Phase 1 tasks: 001, 002, 003...
- Phase 2 tasks: 010, 011, 012...
- Phase 3 tasks: 020, 021, 022...

(10-gap allows inserting tasks later)

## POML Task Structure

Each task file is valid XML with:
- `<metadata>` - id, title, phase, status, dependencies
- `<prompt>` - Natural language instruction
- `<goal>` - Clear definition of done
- `<context>` - Background and relevant files
- `<constraints>` - ADR constraints with source attributes
- `<steps>` - Ordered execution steps
- `<outputs>` - Expected file paths
- `<acceptance-criteria>` - Testable criteria

## Next Steps

After task-create completes:
- Review `tasks/TASK-INDEX.md` for execution order
- Start with tasks that have no dependencies
- Update task status as work progresses
