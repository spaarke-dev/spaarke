# /design-to-spec

Transform a human design document into an AI-optimized `spec.md` for `/project-pipeline`.

## Usage

```
/design-to-spec {project-path}
```

Example: `/design-to-spec projects/mda-darkmode-theme`. Expects a design document at `{project-path}/design.md`, or a path you provide.

## Execution Instructions

When this command is invoked, load `.claude/skills/design-to-spec/SKILL.md` and follow it. The skill is authoritative for its steps (including the Step 2.5 owner interview), the spec template and the review gate. This file deliberately restates none of it, so the two cannot drift apart.
