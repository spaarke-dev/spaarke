# Implementation Plan — AI Procedure Refactoring R2

> **Project**: ai-procedure-refactoring-r2
> **Created**: April 5, 2026
> **Approach**: Documentation-only — no source code changes

---

## Architecture Context

### Discovered Resources

**Applicable Skills** (5):
- `.claude/skills/docs-architecture/SKILL.md` — mandatory structure for architecture docs
- `.claude/skills/docs-guide/SKILL.md` — mandatory structure for guide docs
- `.claude/skills/docs-standards/SKILL.md` — mandatory structure for standards docs
- `.claude/skills/docs-data-model/SKILL.md` — mandatory structure for data model docs
- `.claude/skills/docs-procedures/SKILL.md` — mandatory structure for procedure docs

**Existing Documentation**:
- `docs/architecture/` — 35 files (12 over-trimmed, 7 adequate, 16 to verify/keep)
- `docs/guides/` — 30+ files (8 need accuracy verification)
- `docs/data-model/` — 22 files (21 entity docs to verify, 1 schema additions)
- `docs/standards/` — 3 files (1 adequate, 2 reference)
- `docs/procedures/` — 4 files (2 to enhance)

**Reference Material**:
- `projects/ai-procedure-refactoring-r2/notes/documentation-requirements.md` — master requirements table with per-document prompts
- `projects/ai-procedure-refactoring-r1/notes/architecture-audit.md` — R1 audit results

### Quality Rules

1. Every file path in any document MUST resolve to an existing file
2. Every architecture doc MUST include Known Pitfalls section
3. Every architecture doc MUST include Integration Points
4. Every guide MUST include Verification steps
5. Standards MUST be sourced from ADRs, skills, incident history — not invented
6. Data model docs MUST match current Dataverse schema
7. All documents MUST follow their respective `/docs-*` skill structure

---

## Phase Breakdown

### Phase 1: Foundation Standards (Tasks 001-006)

**Goal**: Establish cross-cutting reference documents that all subsequent docs depend on.

| Task | Document | Type | Status | Parallel Group |
|------|----------|------|--------|----------------|
| 001 | `CODING-STANDARDS.md` | standards | new | A |
| 002 | `ANTI-PATTERNS.md` | standards | new | A |
| 003 | `INTEGRATION-CONTRACTS.md` | standards | new | A |
| 004 | `entity-relationship-model.md` | data-model | new | A |
| 005 | `testing-and-code-quality.md` | procedures | enhance | A |
| 006 | `sdap-component-interactions.md` | arch | over-trimmed | A |

**Parallelism**: All 6 tasks are independent — each reads code/ADRs and writes a different doc. Run as **Parallel Group A** (6 concurrent agents).

### Phase 2: Core Architecture Restoration (Tasks 010-022)

**Goal**: Restore depth to over-trimmed architecture docs and create new core architecture docs.

| Task | Document | Type | Status | Parallel Group |
|------|----------|------|--------|----------------|
| 010 | `sdap-bff-api-patterns.md` | arch | over-trimmed | B |
| 011 | `sdap-auth-patterns.md` | arch | over-trimmed | B |
| 012 | `AI-ARCHITECTURE.md` | arch | over-trimmed | B |
| 013 | `playbook-architecture.md` | arch | over-trimmed | B |
| 014 | `finance-intelligence-architecture.md` | arch | over-trimmed | B |
| 015 | `communication-service-architecture.md` | arch | over-trimmed | B |
| 016 | `email-processing-architecture.md` | arch | over-trimmed (merge) | B |
| 017 | `sdap-pcf-patterns.md` | arch | over-trimmed | B |
| 018 | `external-access-spa-architecture.md` | arch | over-trimmed | B |
| 019 | `sdap-workspace-integration-patterns.md` | arch | over-trimmed | B |
| 020 | `office-outlook-teams-integration-architecture.md` | arch | over-trimmed | B |
| 021 | `jobs-architecture.md` | arch | new | B |
| 022 | `background-workers-architecture.md` | arch | new | B |

**Parallelism**: All 13 tasks are independent — each restores/creates a different architecture doc from different code areas. Run as **Parallel Group B** (13 concurrent agents, or split into 2 waves if resource-constrained).

### Phase 3: New Architecture & UI Framework (Tasks 030-040)

**Goal**: Create architecture docs for undocumented subsystems.

| Task | Document | Type | Status | Parallel Group |
|------|----------|------|--------|----------------|
| 030 | `configuration-architecture.md` | arch | new | C |
| 031 | `resilience-architecture.md` | arch | new | C |
| 032 | `shared-libraries-architecture.md` | arch | new | C |
| 033 | `chat-architecture.md` | arch | new | C |
| 034 | `rag-architecture.md` | arch | new | C |
| 035 | `scope-architecture.md` | arch | new | C |
| 036 | `shared-ui-components-architecture.md` | arch | new | C |
| 037 | `code-pages-architecture.md` | arch | new | C |
| 038 | `wizard-framework-architecture.md` | arch | new | C |
| 039 | `workspace-architecture.md` | arch | new | C |
| 040 | `caching-architecture.md` | arch | new | C |

**Parallelism**: All 11 tasks are independent. Run as **Parallel Group C** (11 concurrent agents).

### Phase 4: Infrastructure, Data Layer & Remaining Architecture (Tasks 050-056)

**Goal**: Complete remaining architecture docs and infrastructure documentation.

| Task | Document | Type | Status | Parallel Group |
|------|----------|------|--------|----------------|
| 050 | `dataverse-infrastructure-architecture.md` | arch | new | D |
| 051 | `ci-cd-architecture.md` | arch | new | D |
| 052 | `DEPENDENCY-MANAGEMENT.md` | procedures | new | D |
| 053 | `CODE-REVIEW-BY-MODULE.md` | procedures | new | D |
| 054 | `ci-cd-workflow.md` | procedures | enhance | D |
| 055 | `CONFIGURATION-MATRIX.md` | guide | new | D |
| 056 | `DEPLOYMENT-VERIFICATION-GUIDE.md` | guide | new | D |

**Parallelism**: All 7 tasks are independent. Run as **Parallel Group D** (7 concurrent agents).

### Phase 5: Guide Updates & Verification (Tasks 060-069)

**Goal**: Verify and update existing guides for accuracy.

| Task | Document | Type | Status | Parallel Group |
|------|----------|------|--------|----------------|
| 060 | `AI-MODEL-SELECTION-GUIDE.md` | guide | verify | E |
| 061 | `SCOPE-CONFIGURATION-GUIDE.md` | guide | verify | E |
| 062 | `RAG-CONFIGURATION.md` | guide | verify | E |
| 063 | `HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md` | guide | verify | E |
| 064 | `PRODUCTION-DEPLOYMENT-GUIDE.md` | guide | verify | E |
| 065 | `ENVIRONMENT-DEPLOYMENT-GUIDE.md` | guide | verify | E |
| 066 | `SHARED-UI-COMPONENTS-GUIDE.md` | guide | verify | E |
| 067 | `WORKSPACE-ENTITY-CREATION-GUIDE.md` | guide | verify | E |
| 068 | `sdap-overview.md` | arch | verify | E |
| 069 | `sdap-document-processing-architecture.md` | arch | verify | E |

**Parallelism**: All 10 tasks are independent verification tasks. Run as **Parallel Group E** (10 concurrent agents).

### Phase 6: Data Model (Tasks 070-076)

**Goal**: Create new data model docs and verify existing entity docs.

| Task | Document | Type | Status | Parallel Group |
|------|----------|------|--------|----------------|
| 070 | `field-mapping-reference.md` | data-model | new | F |
| 071 | `json-field-schemas.md` | data-model | enhance | F |
| 072 | `alternate-keys-and-constraints.md` | data-model | enhance | F |
| 073 | Verify entity docs batch 1 (7 files) | data-model | verify | F |
| 074 | Verify entity docs batch 2 (7 files) | data-model | verify | F |
| 075 | Verify entity docs batch 3 (7 files) | data-model | verify | F |
| 076 | Verify architecture docs (3 remaining) | arch | verify | F |

**Parallelism**: All 7 tasks are independent. Run as **Parallel Group F** (7 concurrent agents).

### Phase 7: Cross-Reference Verification & Wrap-Up (Tasks 080-082)

**Goal**: Verify all file paths across all docs, update indexes, wrap up.

| Task | Document | Action | Parallel Group |
|------|----------|--------|----------------|
| 080 | All docs | Cross-reference path verification | G (serial) |
| 081 | `docs/*/INDEX.md` | Update all index files | G (serial) |
| 082 | Project wrap-up | README status, lessons learned | G (serial) |

**Parallelism**: Serial — these tasks depend on all previous phases being complete.

---

## Parallel Execution Groups

| Group | Phase | Tasks | Count | Prerequisite | Notes |
|-------|-------|-------|-------|--------------|-------|
| A | 1 | 001-006 | 6 | None | Foundation standards — all independent |
| B | 2 | 010-022 | 13 | None (can start with A) | Over-trimmed restoration — all independent code areas |
| C | 3 | 030-040 | 11 | None (can start with A, B) | New architecture — all independent subsystems |
| D | 4 | 050-056 | 7 | None (can start with A-C) | Infrastructure & procedures — independent |
| E | 5 | 060-069 | 10 | None (can start with A-D) | Guide verification — independent |
| F | 6 | 070-076 | 7 | 004 (ERD) | Data model — depends on ERD from task 004 |
| G | 7 | 080-082 | 3 | All previous | Cross-reference verification — serial |

**Key insight**: Groups A through E have NO inter-group dependencies. They can ALL run concurrently if agent capacity allows. Only Group F depends on task 004 (ERD), and Group G depends on everything.

**Recommended execution strategy**:
- Wave 1: Groups A + B + C + D + E (47 tasks, all concurrent)
- Wave 2: Group F (7 tasks, after task 004 completes)
- Wave 3: Group G (3 tasks, serial)

**Practical execution**: Claude Code can spawn multiple task agents concurrently. Given context window limits, run in batches of 4-6 concurrent agents per wave, cycling through groups.

---

## Dependencies

```
Phase 1 (A) ──┐
Phase 2 (B) ──┤
Phase 3 (C) ──┼── All independent, run concurrently
Phase 4 (D) ──┤
Phase 5 (E) ──┘
                └── Phase 6 (F) depends on task 004 only
                      └── Phase 7 (G) depends on ALL phases
```

---

## Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|------------|
| Over-trimmed docs: git history may not have original content | Medium | Fall back to code-first analysis if git recovery fails |
| Large doc count (74) may exceed context limits | High | Parallel agents each handle one doc independently |
| Dataverse schema may have drifted from entity docs | Medium | Code-based verification (read entity classes, not metadata API) |
| Standards docs could become opinionated without evidence | Medium | Every standard must cite ADR, skill, or code example as source |

---

## References

- [Specification](spec.md)
- [Documentation Requirements](notes/documentation-requirements.md)
- [R1 Architecture Audit](../ai-procedure-refactoring-r1/notes/architecture-audit.md)
- [R1 Guides Audit](../ai-procedure-refactoring-r1/notes/guides-audit.md)
- Skills: `docs-architecture`, `docs-guide`, `docs-standards`, `docs-data-model`, `docs-procedures`
