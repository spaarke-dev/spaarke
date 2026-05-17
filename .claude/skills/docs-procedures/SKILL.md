---
description: Draft or update a development procedure — testing strategy, CI/CD workflow, code review checklists, dependency management
tags: [documentation, procedures, development]
techStack: [all]
appliesTo: ["write procedure", "update procedure", "testing strategy", "code review checklist", "docs-procedures"]
alwaysApply: false
exemplar: docs/procedures/testing-and-code-quality.md
last-reviewed: 2026-05-16
---

# Development Procedures Skill

> **Category**: Documentation
> **Last Reviewed**: 2026-05-16
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2b-A)

---

## Purpose

Draft or update a development procedure in `docs/procedures/`. Procedures define **how the development team works** — testing strategies, CI/CD workflows, code review processes, and dependency management.

**Procedures are NOT:**
- Feature-specific guides (→ use `/docs-guide`)
- System architecture (→ use `/docs-architecture`)
- Coding conventions (→ use `/docs-standards`)

The distinction from guides: **procedures are about the development process itself**, while guides are about operating or configuring the product.

---

## When to Use

- "write testing strategy", "update CI/CD workflow", "code review checklist"
- When defining how to test a specific module type
- When documenting the PR review process
- When establishing dependency management rules

---

## Document Structure

```markdown
# {Procedure Name}

> **Last Updated**: {date}
> **Applies To**: {who follows this procedure — all developers, reviewers, deployers}

---

## When to Follow This Procedure

{Clear trigger conditions — when does this procedure apply?}

## Procedure

{Numbered steps or decision tree.
Each step should be unambiguous — no judgment calls without criteria.}

### For {Module Type A}
{Module-specific steps}

### For {Module Type B}
{Module-specific steps}

## Checklists

{Checkbox lists that can be verified mechanically.
The /code-review and /adr-check skills can load these contextually.}

- [ ] {Check 1}
- [ ] {Check 2}

## Automation

{Which parts of this procedure are automated (CI, hooks, skills) vs manual.}

| Step | Automated By | Manual? |
|------|-------------|---------|

## Related

- [{Skill}](../../.claude/skills/{skill}/SKILL.md) — automates part of this procedure
```

---

## Drafting Rules

- **MUST**: Procedures be deterministic — given the same inputs, any developer follows the same steps. No "use your judgment" without explicit criteria.
- **MUST**: Include module-specific sections rather than one generic flow. Backend, PCF, Code Pages, plugins have different procedures.
- **MUST**: Reference skills that automate parts of the procedure (so the agent can find them).
- **MUST**: Include checklists that can be loaded by `/code-review` skill — they ARE the verification gate.
- **MUST**: Cross-reference testing procedures with coverage targets from architecture docs (so the procedure ties back to the standard).
- **MUST NOT**: Write a procedure that requires reading another procedure to understand step 1. Procedures must be self-contained.

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Procedure has "use judgment" steps without criteria | Author left a decision point ambiguous | Replace "use judgment" with explicit criteria. If criteria can't be enumerated, the step belongs in `docs-guide` (operational, judgment-allowed), not `docs-procedures` (deterministic). |
| Two procedures contradict each other | Same domain documented twice in different `docs/procedures/*.md` files | Designate one as canonical; the other becomes a pointer. `doc-drift-audit` flags this. |
| Procedure references a skill that no longer exists | Skill was renamed or removed; procedure not updated | Run `Find-SkillReferenceDrift.ps1` (Phase 4a) before merging procedure changes. Update inline references to current skill name. |
| CI fails because procedure-prescribed step isn't enforced by a tool | Procedure documents the rule; no validator enforces it | Either add a validator (preferred) OR demote the rule from MUST to SHOULD and document the lack of enforcement. Don't pretend enforcement exists. |
