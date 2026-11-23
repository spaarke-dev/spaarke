# /project-pipeline

Full pipeline from `spec.md` to ready-to-execute tasks, then task execution.

## Usage

```
/project-pipeline {project-path}
```

Example: `/project-pipeline projects/mda-darkmode-theme`. Run `/design-to-spec` first if only a design document exists.

## Execution Instructions

When this command is invoked, load `.claude/skills/project-pipeline/SKILL.md` and follow it. The skill is authoritative for every step (0–5, including the Step 4.2 GitHub registration), the execution mode (autonomous by default; interactive on request), the branch convention (`work/{project-name}`) and the outputs. This file deliberately restates none of it, so the two cannot drift apart.
