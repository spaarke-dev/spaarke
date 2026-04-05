# AI Procedure Refactoring R2 — Substantive Documentation Library

> **Status**: In Progress
> **Branch**: `work/ai-procedure-refactoring-r2`
> **Started**: April 5, 2026

## Purpose

Build a complete, code-verified documentation library that enables AI-driven development to produce production-quality code. R1 refactored documentation structure (pointer format, trimming, consolidation). R2 addresses substantive content — restoring over-trimmed architecture docs, creating missing docs, verifying accuracy, and establishing new document types (standards, data model, procedures).

## Problem

After R1's structural refactoring:
- 12 architecture docs were over-trimmed and lack technical depth
- 13+ subsystems have zero architecture documentation
- No cross-cutting standards docs exist
- Data model docs haven't been verified against current Dataverse schema
- Development procedures are generic rather than module-specific

## Scope

| Category | Count | Action |
|----------|-------|--------|
| Over-trimmed architecture docs | 12 | Restore depth from git history + code verification |
| New architecture docs | 13 | Create from code analysis |
| New standards docs | 3 | Consolidate from CLAUDE.md, ADRs, skills |
| New/enhanced data model docs | 4 | Create ERD, field mapping, JSON schemas |
| Data model verification | 21 | Verify existing entity docs against schema |
| New/enhanced procedures | 4 | Testing, CI/CD, code review, dependencies |
| New guides | 2 | Configuration matrix, deployment verification |
| Guide updates | 8 | Verify accuracy of existing guides |
| **Total** | **74** | |

## Skills Used

| Skill | Purpose |
|-------|---------|
| `/docs-architecture` | Draft/update architecture documents |
| `/docs-guide` | Draft/update operational guides |
| `/docs-standards` | Draft/update standards documents |
| `/docs-data-model` | Draft/update data model documents |
| `/docs-procedures` | Draft/update development procedures |

## Graduation Criteria

- [ ] All 74 documents addressed (created, updated, or verified)
- [ ] Zero broken file paths across all documentation
- [ ] Architecture docs have adequate depth proportional to code complexity
- [ ] Standards docs consolidate conventions from scattered sources
- [ ] Data model entity docs match current Dataverse schema
- [ ] `/code-review` skill can load module-specific checklists

## Quick Links

- [Specification](spec.md)
- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Requirements Table](notes/documentation-requirements.md)
