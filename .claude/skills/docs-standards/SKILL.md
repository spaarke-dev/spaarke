---
description: Draft or update a standards document — cross-cutting coding conventions, anti-patterns, and integration contracts
tags: [documentation, standards]
techStack: [all]
appliesTo: ["write standards doc", "update standards", "coding standards", "anti-patterns", "docs-standards"]
alwaysApply: false
exemplar: docs/standards/CODING-STANDARDS.md
last-reviewed: 2026-05-16
---

# Standards Document Skill

> **Category**: Documentation
> **Last Reviewed**: 2026-05-16
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2b-A)

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

- **MUST**: Source rules from real authoritative material: ADRs, deploy-skill troubleshooting tables, pattern pointer constraints, root CLAUDE.md coding standards. Invented rules without a source rot.
- **MUST**: Every rule be verifiable — if Claude can't check it (via grep, build, test, or visible code state), it's not enforceable. Demote to SHOULD or remove.
- **MUST**: Anti-patterns include the **specific symptom** so they're recognizable when encountered. "Don't do X" without "you'll see Y if you did" is unactionable.
- **MUST**: Keep total length under 300 lines — standards must be scannable, not exhaustive. If hitting the limit, split by domain (one standards doc per domain).
- **MUST NOT**: Document an anti-pattern without an evidence trail (commit SHA, FAILURE-MODES entry, ADR, or production incident). Absolute "NEVER" claims without evidence age badly — see [`FAILURE-MODES.md#AP-1`](../../FAILURE-MODES.md#ap-1-skill-prescribes-x-but-x-is-wrong).

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Standard says X but code does Y (or vice versa) | Standard was written aspirationally OR code drifted | Trust the code; update the standard. If the code is wrong, file an issue and link from the standard ("known violation: see issue #N"). Don't pretend the standard is enforced when it isn't. |
| Anti-pattern is listed but Claude doesn't recognize it during review | Anti-pattern lacks a grep-able symptom | Add a concrete code-pattern symptom (regex or characteristic substring) to the anti-pattern entry so `/code-review` can find it. |
| Two standards docs contradict each other | Same rule documented twice with different wordings | Designate one canonical doc; the other becomes a pointer with a one-line explanation. |
| "NEVER" rule turns out to be wrong (AP-1 class) | Author overconfident; rule wasn't verified empirically | Remove or weaken the NEVER claim. Add a FAILURE-MODES.md entry documenting the bad rule's discovery. See `pcf-deploy` AP-1 history (2026-05-14, commit c132773c). |
