---
description: Draft or update a standards document — cross-cutting coding conventions, anti-patterns, and integration contracts
tags: [documentation, standards]
techStack: []
appliesTo: ["write standards doc", "update standards", "coding standards", "anti-patterns", "docs-standards"]
alwaysApply: false
---

# Standards Document Skill

> **Category**: Documentation
> **Last Updated**: April 2026

---

## Purpose

Draft or update a standards document in `docs/standards/`. Standards documents define **cross-cutting rules that apply across the entire codebase** — coding conventions, anti-patterns, integration contracts, and quality expectations.

**Standards docs are NOT:**
- Module-specific architecture (→ use `/docs-architecture`)
- Step-by-step procedures (→ use `/docs-guide`)
- ADRs (→ `.claude/adr/` — standards may reference ADRs but don't replace them)

---

## When to Use

- "write coding standards", "document anti-patterns", "integration contracts"
- When a cross-cutting convention needs to be codified
- When a recurring bug pattern should be documented to prevent repetition

---

## Document Structure

```markdown
# {Standard Name}

> **Last Updated**: {date}
> **Applies To**: {which parts of the codebase — all, backend, frontend, etc.}

---

## Rules

{Numbered rules. Each rule is a single clear statement.
Use MUST/MUST NOT/SHOULD/SHOULD NOT language.}

1. **MUST**: {rule}
2. **MUST NOT**: {rule}
3. **SHOULD**: {rule}

## Examples

{For each rule, show a correct and incorrect example.
Keep examples minimal — 3-5 lines of code max.}

### Rule 1: {rule name}
✅ Correct: {brief code or description}
❌ Wrong: {brief code or description}

## Anti-Pattern Reference

{For anti-pattern docs specifically: pattern → why it's wrong → correct approach → ADR ref}

| Anti-Pattern | Why It's Wrong | Correct Approach | Reference |
|-------------|---------------|-----------------|-----------|

## Related

- [ADR-XXX](../.claude/adr/ADR-XXX.md) — {constraint this standard enforces}
```

---

## Drafting Rules

- Source rules from: ADRs, deploy skill troubleshooting tables, pattern pointer constraints, CLAUDE.md coding standards
- Every rule must be verifiable — if Claude can't check it, it's not enforceable
- Anti-patterns must include the **specific symptom** so they're recognizable when encountered
- Keep total length under 300 lines — standards must be scannable, not exhaustive
