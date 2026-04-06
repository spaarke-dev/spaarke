# Domain Constraints Index

> **Purpose**: MUST/MUST NOT rules organized by domain for efficient AI loading
> **Target**: 100-200 lines per domain
> **Source**: Extracted from ADRs and validated against codebase
> **Last Updated**: 2025-12-25
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

---

## About This Directory

This directory contains actionable constraints (rules, requirements, limits) organized by technical domain. Each file is a focused set of MUST/MUST NOT directives with quick-reference code examples.

---

## Available Constraint Files

| Domain | File | Source ADRs | Last Updated | Last Reviewed | Status |
|--------|------|-------------|--------------|---------------|--------|
| API/BFF | [api.md](api.md) | ADR-001, 004, 008, 010, 019 | 2025-12-18 | 2026-04-05 | Verified |
| PCF Controls | [pcf.md](pcf.md) | ADR-006, 011, 012, 021, 022 | 2026-02-23 | 2026-04-05 | Verified |
| Plugins | [plugins.md](plugins.md) | ADR-002 | 2026-01-05 | 2026-04-05 | Verified |
| Authentication | [auth.md](auth.md) | ADR-003, 004, 008, 009 | 2026-03-09 | 2026-04-05 | Verified |
| Data Access | [data.md](data.md) | ADR-005, 007, 009 | 2026-04-05 | 2026-04-05 | Current |
| AI Features | [ai.md](ai.md) | ADR-013, 014, 015, 016 | 2026-04-05 | 2026-04-05 | Current |
| Jobs/Workers | [jobs.md](jobs.md) | ADR-004, 017 | 2026-04-05 | 2026-04-05 | Current |
| Configuration | [config.md](config.md) | ADR-018, 020 | 2026-04-05 | 2026-04-05 | Current |
| Testing | [testing.md](testing.md) | (see procedures) | 2026-04-05 | 2026-04-05 | Current |
| Web Resources | [webresource.md](webresource.md) | ADR-006, 008 | 2026-02-16 | 2026-04-05 | Verified |
| Azure Deployment | [azure-deployment.md](azure-deployment.md) | — | 2026-02-18 | 2026-04-05 | Verified |
| React Versioning | [react-versioning.md](react-versioning.md) | ADR-012, 022 | 2026-03-17 | 2026-04-05 | Verified |
| Theme Consistency | [theme-consistency.md](theme-consistency.md) | ADR-021, 012 | 2026-03-30 | 2026-04-05 | Verified |

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
| Azure deployment | `azure-deployment.md` |

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
