# AI Procedure Refactoring R2 — Substantive Documentation Library

## Executive Summary

Build a complete, code-verified documentation library that enables AI-driven development to produce production-quality code. R1 refactored documentation structure (pointer format, trimming, consolidation). R2 addresses substantive content — restoring over-trimmed architecture docs, creating missing docs, verifying accuracy, and establishing new document types (standards, data model, procedures).

## Problem Statement

After R1's structural refactoring:
- 12 architecture docs were over-trimmed and lack technical depth needed for implementation
- 25 subsystems have zero architecture documentation
- No cross-cutting standards docs exist (coding conventions, anti-patterns, integration contracts)
- Data model docs (21 files) haven't been verified against current Dataverse schema
- Development procedures (testing strategy, code review checklists) are generic rather than module-specific
- The documentation doesn't function as an integrated system — architecture, guides, standards, data model, and procedures need to work together

## Scope

### In Scope
- Restore 12 over-trimmed architecture docs using git history + code verification
- Create 13 new architecture docs from code analysis
- Create 3 new standards docs (coding standards, anti-patterns, integration contracts)
- Create 2 new data model docs + enhance 2 existing + verify 21 existing entity docs
- Create 2 new procedure docs + enhance 2 existing
- Create 2 new guide docs (configuration matrix, deployment verification)
- Update 8 existing guides for accuracy
- Add Known Pitfalls sections to architecture docs
- Add module-specific checklists to code review procedures

### Out of Scope
- Creating new ADRs or modifying existing ones
- Modifying source code (this is documentation only)
- Changing pattern pointer files (R1 handled this)
- Product documentation (end-user docs)

## Requirements

### Document Types and Their Purpose

| Type | Directory | Purpose |
|------|-----------|---------|
| Architecture | `docs/architecture/` | How systems work technically — components, data flows, design decisions, constraints, known pitfalls |
| Guide | `docs/guides/` | How to do things operationally — procedures, configuration, setup, troubleshooting |
| Data Model | `docs/data-model/` | Entity schemas, relationships, alternate keys, field mappings, JSON field contracts |
| Standards | `docs/standards/` | Cross-cutting coding conventions, anti-patterns, integration contracts |
| Procedures | `docs/procedures/` | Development workflow — testing, CI/CD, code review, dependency management |

### Drafting Skills

Each document type has a dedicated skill with mandatory structure and quality checklist:
- `/docs-architecture` — `.claude/skills/docs-architecture/SKILL.md`
- `/docs-guide` — `.claude/skills/docs-guide/SKILL.md`
- `/docs-standards` — `.claude/skills/docs-standards/SKILL.md`
- `/docs-data-model` — `.claude/skills/docs-data-model/SKILL.md`
- `/docs-procedures` — `.claude/skills/docs-procedures/SKILL.md`

### Document Requirements by Priority Tier

#### Tier 1 — Highest Impact (prevents bugs, enables correct code)

| # | Document | Type | Status | Key Deliverable |
|---|----------|------|--------|-----------------|
| 1 | `sdap-component-interactions.md` | arch | over-trimmed | Full cross-module impact map — when you change X, what breaks |
| 2 | `ANTI-PATTERNS.md` | standards | new | Top 20 anti-patterns sourced from deploy skills, ADRs, incident history |
| 3 | `INTEGRATION-CONTRACTS.md` | standards | new | Interface contracts at every subsystem seam |
| 4 | `entity-relationship-model.md` | data-model | new | Master ERD with lookup chains, cascade behaviors, polymorphic lookups |
| 5 | `CODING-STANDARDS.md` | standards | new | Consolidated C#/TypeScript conventions from CLAUDE.md + skills |
| 6 | `testing-and-code-quality.md` | procedures | enhance | Module-specific test guidance: modify X → run these tests |

#### Tier 2 — Core Architecture (enables understanding before implementation)

| # | Document | Type | Status | Key Deliverable |
|---|----------|------|--------|-----------------|
| 7-18 | 12 over-trimmed architecture docs | arch | over-trimmed | Restore depth from git history + verify against current code |
| 19 | `jobs-architecture.md` | arch | new | Service Bus processor, 11 handlers, idempotency, dead-letter |
| 20 | `background-workers-architecture.md` | arch | new | All IHostedService implementations |
| 21 | `rag-architecture.md` | arch | new | Full RAG pipeline end-to-end |
| 22 | `scope-architecture.md` | arch | new | Scope inheritance, resolution chain, fallback |
| 23 | `chat-architecture.md` | arch | new | SprkChat agent, intent routing, session management |

#### Tier 3 — UI & Framework

| # | Document | Type | Status | Key Deliverable |
|---|----------|------|--------|-----------------|
| 24 | `shared-ui-components-architecture.md` | arch | new | 37 components, composition, theming, PCF-safe exports |
| 25 | `code-pages-architecture.md` | arch | new | React 18 entry, auth bootstrap, webpack vs Vite |
| 26 | `wizard-framework-architecture.md` | arch | new | Shared wizard, 7 solutions, AI pre-fill |
| 27 | `workspace-architecture.md` | arch | new | Layout system, panel composition |
| 28 | `DEPENDENCY-MANAGEMENT.md` | procedures | new | Shared lib → consumer dependency graph |

#### Tier 4 — Operational & Data

| # | Document | Type | Status | Key Deliverable |
|---|----------|------|--------|-----------------|
| 29 | `CONFIGURATION-MATRIX.md` | guide | new | Every setting mapped to location and defaults |
| 30 | `DEPLOYMENT-VERIFICATION-GUIDE.md` | guide | new | Post-deploy checks for all component types |
| 31 | `ci-cd-architecture.md` | arch | new | 9 workflows, slot-swap, promotion model |
| 32 | `CODE-REVIEW-BY-MODULE.md` | procedures | new | Module-specific code review checklists |
| 33-40 | 8 existing guide updates | guide | existing | Verify accuracy against current code |
| 41-44 | Data model docs (2 new + 2 enhance) | data-model | mixed | Field mapping reference, JSON schemas, alternate key verification |
| 45-65 | 21 existing entity docs | data-model | existing | Verify all against current Dataverse schema |

### Quality Rules

1. Every file path in any document MUST resolve to an existing file in the repo
2. Every architecture doc MUST include a Known Pitfalls section
3. Every architecture doc MUST include Integration Points showing dependencies
4. Every guide MUST include Verification steps after significant procedures
5. Standards MUST be sourced from actual ADRs, skills, and incident history — not invented
6. Data model docs MUST match current Dataverse schema — verify via code or metadata
7. All documents MUST follow the structure defined in their respective `/docs-*` skill

## Success Criteria

1. All 74 documents in the requirements table are addressed (created, updated, or verified)
2. Zero broken file paths across all documentation
3. Architecture docs have adequate depth proportional to code complexity
4. Standards docs consolidate conventions from scattered sources into single references
5. The `/code-review` skill can load module-specific checklists from `CODE-REVIEW-BY-MODULE.md`
6. Data model entity docs match current Dataverse schema

## Technical Approach

- Use `/docs-architecture`, `/docs-guide`, `/docs-standards`, `/docs-data-model`, `/docs-procedures` skills for all drafting
- For over-trimmed docs: use `git log -p` to recover removed content, verify against current code, restore what's still accurate
- For new docs: read all source files in the subsystem, then draft following skill structure
- For verification: grep docs for file paths/class names, verify each exists in codebase
- Parallelize by tier — Tier 1 docs are independent, Tier 2 architecture docs are independent per module group

## Reference

- Full requirements table with prompts: `projects/ai-procedure-refactoring-r2/notes/documentation-requirements.md`
- R1 architecture audit: `projects/ai-procedure-refactoring-r1/notes/architecture-audit.md`
- R1 guides audit: `projects/ai-procedure-refactoring-r1/notes/guides-audit.md`
