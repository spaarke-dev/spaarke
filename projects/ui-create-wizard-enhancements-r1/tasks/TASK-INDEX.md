# Task Index — UI Create Wizard Enhancements R1

> **Last Updated**: 2026-03-23
> **Total Tasks**: 33
> **Estimated Effort**: ~85 hours

## Status Legend

| Symbol | Meaning |
|--------|---------|
| 🔲 | Not started |
| 🔄 | In progress |
| ✅ | Complete |
| ⛔ | Blocked |

---

## Phase 1: Foundation & Bug Fixes (~22 hours)

| # | Task | Tags | Depends On | Status |
|---|------|------|------------|--------|
| 001 | Fix theme cascade — remove OS prefers-color-scheme | theme, fluent-ui | — | 🔲 |
| 002 | Replace hard-coded colors with Fluent v9 tokens | theme, fluent-ui | — | 🔲 |
| 003 | Consolidate 6 duplicated ThemeProvider files | theme, code-pages | 001 | 🔲 |
| 004 | Fix overdue badge 400 error (field name) | bug-fix | — | 🔲 |
| 005 | Fix SprkChat double /api/api/ prefix | bug-fix, pcf | — | 🔲 |
| 006 | Standardize dialog sizing to 60%×70% | webresource | — | 🔲 |
| 007 | Rename "Send Email to Client" → "Send Notification Email" | shared-library | — | 🔲 |
| 008 | React 19 upgrade for all Code Pages | code-pages, react | — | 🔲 |
| 009 | Phase 1 deploy + verify | deploy | 001-008 | 🔲 |

## Phase 2: Shared Library Extensions (~17 hours)

| # | Task | Tags | Depends On | Status |
|---|------|------|------------|--------|
| 010 | Add openLookup() to INavigationService + adapters | shared-library | — | 🔲 |
| 011 | Create AssociateToStep shared component | shared-library, wizard | 010 | 🔲 |
| 012 | Extract WorkspaceShell from LegalWorkspace | shared-library, workspace | — | 🔲 |
| 013 | Fix duplicate title bars (hideTitle) | shared-library, wizard | — | 🔲 |
| 014 | Move SecureProjectSection to top | shared-library, wizard | — | 🔲 |
| 015 | Phase 2 deploy + verify | deploy | 010-014 | 🔲 |

## Phase 3: BFF API & Auth (~18 hours)

| # | Task | Tags | Depends On | Status |
|---|------|------|------------|--------|
| 020 | POST /api/ai/analysis/create BFF endpoint | bff-api, ai | — | 🔲 |
| 021 | IAnalysisDataverseService scope association | bff-api, dataverse | — | 🔲 |
| 022 | Rewrite analysisService.ts for BFF API | shared-library, ai | 020, 021 | 🔲 |
| 023 | MSAL auth standardization across Code Pages | code-pages, auth | — | 🔲 |
| 024 | bffBaseUrl propagation from launch points | webresource | — | 🔲 |
| 025 | Phase 3 deploy + verify | deploy | 020-024 | 🔲 |

## Phase 4: Wizard Flow Enhancements (~18 hours)

| # | Task | Tags | Depends On | Status |
|---|------|------|------------|--------|
| 030 | CreateMatter AssociateToStep integration | wizard | 011 | 🔲 |
| 031 | CreateProject AssociateToStep integration | wizard | 011 | 🔲 |
| 032 | Assign Work follow-on step | wizard | 010 | 🔲 |
| 033 | Create Event follow-on step | wizard | — | 🔲 |
| 034 | Dataverse lookup side pane integration | wizard | 010 | 🔲 |
| 035 | Phase 4 deploy + verify | deploy | 030-034 | 🔲 |

## Phase 5: Consolidation & Document Flow (~19 hours)

| # | Task | Tags | Depends On | Status |
|---|------|------|------------|--------|
| 040 | Merge PlaybookLibrary + AnalysisBuilder | code-pages, ai | — | 🔲 |
| 041 | Summarize → Analysis document creation | shared-library, ai | 040 | 🔲 |
| 042 | Document selector in PlaybookLibrary | code-pages, ai | 040, 041 | 🔲 |
| 043 | LegalWorkspace WorkspaceShell refactor | workspace | 012 | 🔲 |
| 044 | Update launch points + retire AnalysisBuilder | webresource, cleanup | 040 | 🔲 |
| 045 | Phase 5 deploy + verify | deploy | 040-044 | 🔲 |

## Phase 6: Integration & Wrap-up (~8 hours)

| # | Task | Tags | Depends On | Status |
|---|------|------|------------|--------|
| 050 | End-to-end integration testing | testing, e2e | 009, 015, 025, 035, 045 | 🔲 |
| 051 | Cross-wizard regression testing | testing, e2e | 050 | 🔲 |
| 090 | Project wrap-up | documentation, cleanup | 051 | 🔲 |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 001, 002, 004, 005, 006, 007, 008 | — | Independent Phase 1 foundation tasks |
| B | 010, 012, 013, 014 | — | Independent Phase 2 tasks (011 depends on 010) |
| C | 020, 021, 023, 024 | — | Independent Phase 3 tasks (022 depends on 020+021) |
| D | 030, 031 | 011 | Parallel wizard Associate To integrations |
| E | 032, 033, 034 | 010 | Parallel follow-on / lookup tasks |
| F | 040, 043 | 012 (043 only) | PlaybookLibrary merge + workspace refactor |

## Critical Path

```
001 → 003 → 009 (theme foundation)
010 → 011 → 030/031 (lookup → associate → wizard integration)
020+021 → 022 → 025 (BFF endpoint → frontend service → deploy)
012 → 043 → 045 (WorkspaceShell → consume → deploy)
040 → 041 → 042 → 045 (PlaybookLibrary merge → docs → selector → deploy)
All deploys → 050 → 051 → 090 (integration → regression → wrap-up)
```

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 002 | Hard-coded color sweep may miss patterns | grep-based acceptance criterion; systematic search |
| 008 | React 19 may surface unexpected issues | Fluent v9 supports react <20.0.0; incremental test |
| 023 | MSAL iframe silent flow edge cases | DocumentUploadWizard reference impl; error+retry |
| 012 | WorkspaceShell responsive edge cases | CSS Grid + aspect-ratio; test 768-2560px |
| 040 | AnalysisBuilder retirement may break undocumented references | Codebase-wide search before deletion |

---

*Generated by project-pipeline. Updated by task-execute during implementation.*
