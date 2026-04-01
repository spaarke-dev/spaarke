# Task Index — Power BI Embedded Reporting R1

> **Last Updated**: 2026-03-31
> **Total Tasks**: 30
> **Status**: 0/30 complete

## Status Legend

| Icon | Status |
|------|--------|
| 🔲 | Not started |
| 🔄 | In progress |
| ✅ | Complete |
| ⛔ | Blocked |

---

## Phase 1: Foundation & BFF API (Tasks 001-008)

| # | Task | Status | Tags | Dependencies | Parallel Group |
|---|------|--------|------|--------------|----------------|
| 001 | Add PBI NuGet package & environment config | 🔲 | `bff-api`, `config` | none | — |
| 002 | Create ReportingEmbedService (SP auth + token gen) | 🔲 | `bff-api`, `auth` | 001 | — |
| 003 | Create ReportingProfileManager (SP profiles) | 🔲 | `bff-api`, `auth` | 001 | A |
| 004 | Add embed token Redis caching | 🔲 | `bff-api`, `caching` | 002 | — |
| 005 | Create ReportingEndpoints (API routes) | 🔲 | `bff-api`, `api` | 002, 003, 004 | — |
| 006 | Create ReportingAuthorizationFilter | 🔲 | `bff-api`, `auth` | 001 | A |
| 007 | Create ReportingModule DI registration | 🔲 | `bff-api`, `config` | 002, 003, 006 | — |
| 008 | Unit tests for reporting BFF services | 🔲 | `testing`, `unit-test` | 005, 007 | — |

## Phase 2: Code Page & Embedding (Tasks 010-016)

| # | Task | Status | Tags | Dependencies | Parallel Group |
|---|------|--------|------|--------------|----------------|
| 010 | Scaffold sprk_reporting Code Page | ✅ | `frontend`, `code-page` | none | B |
| 011 | Power BI embed component (ReportViewer) | 🔲 | `frontend`, `fluent-ui` | 010, 005 | — |
| 012 | Report catalog dropdown | ✅ | `frontend`, `fluent-ui` | 010, 005 | — |
| 013 | Token auto-refresh hook (80% TTL) | 🔲 | `frontend` | 011 | — |
| 014 | Dark mode support (transparent PBI bg) | 🔲 | `frontend`, `fluent-ui` | 011 | C |
| 015 | Module disabled state UI | 🔲 | `frontend` | 010, 006 | C |
| 016 | Deploy Code Page to Dataverse | 🔲 | `deploy`, `dataverse` | 011, 012 | — |

## Phase 3: Authoring & Export (Tasks 020-024)

| # | Task | Status | Tags | Dependencies | Parallel Group |
|---|------|--------|------|--------------|----------------|
| 020 | Edit mode toggle (view/edit switch) | 🔲 | `frontend`, `fluent-ui` | 011 | — |
| 021 | Create new report flow | 🔲 | `frontend`, `bff-api` | 020, 005 | — |
| 022 | Save / Save As with catalog update | 🔲 | `frontend`, `bff-api` | 020 | D |
| 023 | Export to PDF/PPTX | 🔲 | `frontend`, `bff-api` | 005 | D |
| 024 | Role-based UI controls (Viewer/Author/Admin) | 🔲 | `frontend`, `auth` | 006, 020 | — |

## Phase 4: Dataverse & Reports (Tasks 030-036)

| # | Task | Status | Tags | Dependencies | Parallel Group |
|---|------|--------|------|--------------|----------------|
| 030 | Define sprk_report entity schema | 🔲 | `dataverse`, `solution` | none | E |
| 031 | Create sprk_ReportingAccess security role | 🔲 | `dataverse`, `solution` | none | E |
| 032 | Create sprk_ReportingModuleEnabled env var | 🔲 | `dataverse`, `solution` | none | E |
| 033 | Deploy Dataverse schema | 🔲 | `deploy`, `dataverse` | 030, 031, 032 | — |
| 034 | Standard .pbix report templates (placeholder) | 🔲 | `docs`, `manual` | none | — |
| 035 | Create Deploy-ReportingReports.ps1 | 🔲 | `deploy`, `script` | none | E |
| 036 | Report versioning setup (reports/ folder) | 🔲 | `docs`, `config` | none | E |

## Phase 5: Integration & Onboarding (Tasks 040-045)

| # | Task | Status | Tags | Dependencies | Parallel Group |
|---|------|--------|------|--------------|----------------|
| 040 | Customer onboarding script | 🔲 | `script`, `deploy` | 035 | — |
| 041 | BU RLS verification tests | 🔲 | `testing`, `integration-test` | 005, 033 | F |
| 042 | Multi-deployment model testing | 🔲 | `testing`, `integration-test` | 016, 033 | F |
| 043 | Integration tests for reporting endpoints | 🔲 | `testing`, `integration-test` | 005, 033 | F |
| 044 | E2E smoke tests | 🔲 | `testing`, `e2e-test` | all Phase 1-4 | — |
| 045 | User documentation and admin guide | 🔲 | `docs` | all Phase 1-4 | — |

## Wrap-up

| # | Task | Status | Tags | Dependencies | Parallel Group |
|---|------|--------|------|--------------|----------------|
| 090 | Project wrap-up | 🔲 | `docs`, `cleanup` | all tasks | — |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 003, 006 | 001 complete | Independent auth components |
| B | 010 | none | Code Page scaffold can start immediately (parallel with Phase 1) |
| C | 014, 015 | 011 complete (014), 010+006 complete (015) | Independent UI features |
| D | 022, 023 | 020 complete (022), 005 complete (023) | Independent authoring features |
| E | 030, 031, 032, 035, 036 | none | Dataverse schema + scripts — fully independent of code |
| F | 041, 042, 043 | Phase 1+4 complete | Independent test suites |

## Critical Path

```
001 → 002 → 004 → 005 → 011 → 020 → 021 → 024 → 044 → 090
                    ↗ 003 ↗         ↗ 013
              ↗ 006 ↗          ↗ 012
         010 ──────────────────┘
         030, 031, 032 → 033 ──────────────────────────→ 041, 042, 043
```

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 002 | First SP profile usage in codebase | Follow MS docs closely; spike if needed |
| 011 | First powerbi-client-react integration | Prototype early; pin version |
| 041 | BU RLS EffectiveIdentity complexity | Test with 2+ BU users |

---

*Generated by project-pipeline skill*
