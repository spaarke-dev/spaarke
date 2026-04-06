---
description: Draft or update a development procedure — testing strategy, CI/CD workflow, code review checklists, dependency management
tags: [documentation, procedures, development]
techStack: []
appliesTo: ["write procedure", "update procedure", "testing strategy", "code review checklist", "docs-procedures"]
alwaysApply: false
---

# Development Procedures Skill

> **Category**: Documentation
> **Last Updated**: April 2026

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

- Procedures must be deterministic — given the same inputs, any developer follows the same steps
- Include module-specific sections rather than one generic flow
- Reference skills that automate parts of the procedure
- Include checklists that can be loaded by `/code-review` skill
- Cross-reference testing procedures with coverage targets from architecture docs
