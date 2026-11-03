# /design-to-spec

Transform a human design document into an AI-optimized specification.

## Usage

```
/design-to-spec {project-path}
```

**Example**: `/design-to-spec projects/mda-darkmode-theme`

## What This Command Does

This command executes the `design-to-spec` skill to transform verbose human design documents (Word docs, markdown, notes) into a structured, AI-ready `spec.md` that can be processed by `/project-pipeline`.

**Transforms**:
- Design.docx / design.md / rough notes → AI-optimized `spec.md`

**Outputs**:
- Structured specification with clear requirements
- ADR constraint references (preliminary)
- Flagged ambiguities requiring human resolution

## Prerequisites

Before running this command:
1. Have a design document at `{project-path}/design.md` (or provide path to source)
2. Extended context recommended for complex specs (>2000 words)

## Execution Instructions

**IMPORTANT**: When this command is invoked, you MUST:

1. **Load the skill**: Read `.claude/skills/design-to-spec/SKILL.md`
2. **Follow the 4-step procedure** exactly as documented
3. **Flag ambiguities** for human review before proceeding
4. **Output spec.md** in the standard format

## Skill Location

`.claude/skills/design-to-spec/SKILL.md`

## Expected Outputs

After successful execution:
- `{project-path}/spec.md` - AI-optimized specification

## Workflow Position

```
Human Design Doc → /design-to-spec → spec.md → /project-pipeline → Ready Tasks
```

This is **Step 1** of the standard 2-step project initialization workflow.

## Next Steps

After design-to-spec completes:
1. Review `spec.md` for accuracy
2. Resolve any flagged ambiguities
3. Run `/project-pipeline {project-path}`

## Related Commands

- `/project-pipeline` - **Step 2**: Full pipeline from spec.md to ready tasks
- `/project-status` - Check project state
