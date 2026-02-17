# Domain Constraints Index

> **Purpose**: MUST/MUST NOT rules organized by domain for efficient AI loading
> **Target**: 100-200 lines per domain
> **Source**: Extracted from ADRs and validated against codebase
> **Last Updated**: 2025-12-25

---

## About This Directory

This directory contains actionable constraints (rules, requirements, limits) organized by technical domain. Each file is a focused set of MUST/MUST NOT directives with quick-reference code examples.

---

## Available Constraint Files

| Domain | File | Source ADRs | Lines |
|--------|------|-------------|-------|
| API/BFF | [api.md](api.md) | ADR-001, 004, 008, 010, 017 | ~130 |
| PCF Controls | [pcf.md](pcf.md) | ADR-006, 012, 014, 015, 018 | ~140 |
| Plugins | [plugins.md](plugins.md) | ADR-002 | ~95 |
| Authentication | [auth.md](auth.md) | ADR-004, 008, 009 | ~130 |
| Data Access | [data.md](data.md) | ADR-003, 005, 007, 009, 019 | ~120 |
| AI Features | [ai.md](ai.md) | ADR-013 | ~100 |
| Jobs/Workers | [jobs.md](jobs.md) | ADR-001, 004, 017 | ~100 |
| Configuration | [config.md](config.md) | ADR-018, 020 | ~115 |
| Testing | [testing.md](testing.md) | ADR-022 | ~115 |
| Web Resources | [webresource.md](webresource.md) | ADR-006, ADR-008 | ~140 |

---

## Usage by AI Agents

**Loading strategy**: Load domain-specific constraint files based on task type.

| Task Type | Load These Constraints |
|-----------|----------------------|
| Creating BFF endpoint | `api.md` + `auth.md` |
| Creating PCF control | `pcf.md` |
| Creating plugin | `plugins.md` |
| Data access/caching | `data.md` |
| Background jobs | `jobs.md` |
| AI features | `ai.md` |
| Configuration/flags | `config.md` |
| Writing tests | `testing.md` |
| Web resource (form events, ribbon) | `webresource.md` |
| API called from web resource | `webresource.md` + `api.md` |

This provides focused, actionable rules without loading full ADR context.

---

## Related Pattern Directories

Constraints define **what** - patterns show **how**:

| Constraint Domain | Pattern Directory |
|-------------------|-------------------|
| api.md | `.claude/patterns/api/` |
| pcf.md | `.claude/patterns/pcf/` |
| plugins.md | `.claude/patterns/dataverse/` |
| auth.md | `.claude/patterns/auth/` |
| data.md | `.claude/patterns/caching/` |
| ai.md | `.claude/patterns/ai/` |
| Testing | `.claude/patterns/testing/` |
| webresource.md | `.claude/patterns/webresource/` |

---

## File Structure Convention

Each constraint file follows this structure:

```markdown
# {Domain} Constraints

> **Domain**: {Description}
> **Source ADRs**: {List}
> **Last Updated**: {Date}

## When to Load This File
[Task triggers for loading this file]

## MUST Rules
### {Category} (ADR-XXX)
- ✅ **MUST** {rule}

## MUST NOT Rules
### {Category} (ADR-XXX)
- ❌ **MUST NOT** {rule}

## Quick Reference Patterns
[Minimal code examples]

## Pattern Files (Complete Examples)
[Links to pattern files]

## Source ADRs (Full Context)
[Links to ADRs for historical context]
```

---

**Lines**: ~80
