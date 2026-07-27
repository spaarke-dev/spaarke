# /use-case-to-design

Turn a concrete legal/business use case into an AI-ready `design.md` using a structured 6-lens method.

## Usage

```
/use-case-to-design {project-path}
```

**Example**: `/use-case-to-design projects/email-communication-solution-r5`

## What This Command Does

This command executes the `use-case-to-design` skill. Use it when starting a new
AI-capability project driven by a concrete use case (e.g., "NDA review", "lease
analysis", "Outlook-style email workspace"). It runs a structured 6-lens method:

1. **Use case** — the concrete legal/business scenario
2. **Surface / UX** — where it lives (workspace widget, code page, PCF, modal…)
3. **Required AI capabilities** — what the feature needs to do
4. **Have-vs-gap** — what already exists in the codebase vs what's missing
5. **Configuration** — Dataverse contracts, JPS Actions, playbooks, wiring
6. **Acceptance** — closed-set acceptance criteria

**Outputs**: a complete `design.md` ready to feed into `/design-to-spec`.

## Prerequisites

Before running this command:
1. Have a clear articulation of the use case (the scenario and desired UX)
2. Know the target surface(s) — workspace widget / code page / modal / PCF

## Execution Instructions

**IMPORTANT**: When this command is invoked, you MUST:

1. **Load the skill**: Read `.claude/skills/use-case-to-design/SKILL.md`
2. **Run the 6-lens method** exactly as documented, including the have-vs-gap
   codebase audit (default to reuse per root CLAUDE.md §11)
3. **Flag ambiguities and ADR tensions** for human review
4. **Output design.md** in the standard format

## Skill Location

`.claude/skills/use-case-to-design/SKILL.md`

## Expected Outputs

After successful execution:
- `{project-path}/design.md` — AI-ready design document

## Workflow Position

```
Use Case → /use-case-to-design → design.md → /design-to-spec → spec.md → /project-pipeline → Ready Tasks
```

This is **Step 0** — the use-case-vertical front door that feeds `design-to-spec`.

## Next Steps

After use-case-to-design completes:
1. Review `design.md` for accuracy and resolve flagged ambiguities
2. Run `/design-to-spec {project-path}`

## Related Commands

- `/design-to-spec` — **Step 1**: transform design.md into AI-optimized spec.md
- `/project-pipeline` — **Step 2**: full pipeline from spec.md to ready tasks
- `/project-status` — Check project state
