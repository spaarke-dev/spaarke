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

**Execution order**: Theme chain is sequential (001→002→003) — each builds on the previous.
Bug fixes and React 19 run in parallel where file sets don't overlap.

| # | Task | Tags | Depends On | Status |
|---|------|------|------------|--------|
| 001 | Fix theme cascade — remove OS prefers-color-scheme | theme, fluent-ui | — | ✅ |
| 002 | Replace hard-coded colors with Fluent v9 tokens | theme, fluent-ui | 001 | ✅ |
| 003 | Consolidate 6 duplicated ThemeProvider files | theme, code-pages | 002 | ✅ |
| 004 | Fix overdue badge 400 error (field name) | bug-fix | — | ✅ |
| 005 | Fix SprkChat double /api/api/ prefix | bug-fix, pcf | — | ✅ |
| 006 | Standardize dialog sizing to 60%×70% | webresource | — | ✅ |
| 007 | Rename "Send Email to Client" → "Send Notification Email" | shared-library | — | ✅ |
| 008 | React 19 upgrade for all Code Pages | code-pages, react | 003 | ✅ |
| 009 | Phase 1 deploy + verify | deploy | 001-008 | 🔲 |

## Phases 2+3: Shared Library + BFF API & Auth (parallel tracks, ~35 hours)

**These two phases run on parallel tracks** — Phase 2 touches frontend shared library
components while Phase 3 touches BFF API (.cs) and Code Page entry points (main.tsx).
No file overlap between tracks.

### Track A — Shared Library Extensions (Phase 2)

| # | Task | Tags | Depends On | Status |
|---|------|------|------------|--------|
| 010 | Add openLookup() to INavigationService + adapters | shared-library | — | ✅ |
| 011 | Create AssociateToStep shared component | shared-library, wizard | 010 | ✅ |
| 012 | Extract WorkspaceShell from LegalWorkspace | shared-library, workspace | — | ✅ |
| 013 | Fix duplicate title bars (hideTitle) | shared-library, wizard | — | ✅ |
| 014 | Move SecureProjectSection to top | shared-library, wizard | — | ✅ |
| 015 | Phase 2 deploy + verify | deploy | 010-014 | 🔲 |

### Track B — BFF API & Auth (Phase 3)

| # | Task | Tags | Depends On | Status |
|---|------|------|------------|--------|
| 020 | POST /api/ai/analysis/create BFF endpoint | bff-api, ai | — | ✅ |
| 021 | IAnalysisDataverseService scope association | bff-api, dataverse | — | ✅ |
| 022 | Rewrite analysisService.ts for BFF API | shared-library, ai | 020, 021 | ✅ |
| 023 | MSAL auth standardization across Code Pages | code-pages, auth | — | ✅ |
| 024 | bffBaseUrl propagation from launch points | webresource | — | ✅ |
| 025 | Phase 3 deploy + verify | deploy | 020-024 | 🔲 |

## Phase 4: Wizard Flow Enhancements (~18 hours)

**Execution order**: 030+031 parallel (different wizards), then 032→033 serial (both touch
NextSteps in same wizards), then 034 last (touches form steps modified by 030/031).

| # | Task | Tags | Depends On | Status |
|---|------|------|------------|--------|
| 030 | CreateMatter AssociateToStep integration | wizard | 011 | ✅ |
| 031 | CreateProject AssociateToStep integration | wizard | 011 | ✅ |
| 032 | Assign Work follow-on step | wizard | 010, 030, 031 | ✅ |
| 033 | Create Event follow-on step | wizard | 032 | ✅ |
| 034 | Dataverse lookup side pane integration | wizard | 010, 030, 031 | ✅ |
| 035 | Phase 4 deploy + verify | deploy | 030-034 | 🔲 |

## Phase 5: Consolidation & Document Flow (~19 hours)

| # | Task | Tags | Depends On | Status |
|---|------|------|------------|--------|
| 040 | Merge PlaybookLibrary + AnalysisBuilder | code-pages, ai | — | ✅ |
| 041 | Summarize → Analysis document creation | shared-library, ai | 040 | ✅ |
| 042 | Document selector in PlaybookLibrary | code-pages, ai | 040, 041 | ✅ |
| 043 | LegalWorkspace WorkspaceShell refactor | workspace | 012 | ✅ |
| 044 | Update launch points + retire AnalysisBuilder | webresource, cleanup | 040 | ✅ |
| 045 | Phase 5 deploy + verify | deploy | 040-044 | 🔲 |

## Phase 6: Integration & Wrap-up (~8 hours)

| # | Task | Tags | Depends On | Status |
|---|------|------|------------|--------|
| 050 | End-to-end integration testing | testing, e2e | 009, 015, 025, 035, 045 | 🔲 |
| 051 | Cross-wizard regression testing | testing, e2e | 050 | 🔲 |
| 090 | Project wrap-up | documentation, cleanup | 051 | 🔲 |

---

## Execution Strategy (Quality-Optimized)

**Principle**: Serialize work that touches overlapping files. Parallelize only when
file sets are truly disjoint. Prefer correctness over speed.

### Phase 1 Execution Order

```
Serial theme chain:  001 → 002 → 003 → 008 (React 19 after clean theme base)
Parallel bug fixes:  004 + 005 + 006 + 007 (run alongside theme chain — no file overlap)
Gate:                009 (deploy + verify all Phase 1)
```

**Why serial theme chain?**
- 001 changes theme resolution logic that 002's token replacements depend on
- 003 consolidates ThemeProvider files using the utilities 001 changed
- 008 upgrades React/build tooling across all Code Pages — must happen after
  source files are stable (otherwise you're debugging React 19 issues mixed
  with theme regressions)

### Phases 2+3 Parallel Tracks

```
Track A (shared lib):   010 → 011          012         013 + 014
                         (sequential)    (independent)  (parallel, different files)
                         └──────────────────┴──────────────┘ → 015

Track B (BFF + auth):   020 + 021 → 022    023 + 024
                        (parallel .cs)      (parallel, different files from 020/021)
                                            └──────────────────────┘ → 025

Tracks A and B run concurrently — zero file overlap between them.
```

### Phase 4 Execution Order

```
030 + 031 (parallel — different wizard components, no shared files)
    → 032 → 033 (serial — both add follow-on steps to NextSteps in SAME wizards)
    → 034 (serial — touches form step components modified by 030/031)
    → 035 (deploy + verify)
```

**Why serialize 032→033?**
Both tasks add new follow-on cards to the NextSteps step component. Running them in
parallel risks merge conflicts in the card array, step routing, and Next Steps imports.

**Why 034 after 030/031?**
Task 034 integrates lookup side panes into the Enter Info steps that 030/031 modify
(they change step numbering and wizard flow). Working from a stable base is safer.

### Phase 5 Execution Order

```
040 (PlaybookLibrary merge) + 043 (WorkspaceShell refactor)  ← parallel, different solutions
    → 041 → 042 (serial — document flow builds sequentially)
    → 044 (launch point updates + AnalysisBuilder retirement — after 040 verified)
    → 045 (deploy + verify)
```

### Phase 6 (Serial)

```
050 → 051 → 090
```

## Parallel Execution Summary

| Group | Tasks | File Sets | Notes |
|-------|-------|-----------|-------|
| P1-bugs | 004, 005, 006, 007 | QuickSummary, SprkChat, webresources, shared labels | Run alongside serial theme chain |
| P2A | 010, 012, 013+014 | serviceInterfaces, WorkspaceShell, WizardShell/ProjectStep | 3 independent file sets in shared lib |
| P2B | 020+021, 023+024 | AnalysisEndpoints.cs, Code Page main.tsx files | 2 independent file sets in BFF/auth |
| P2A+P2B | Track A + Track B | Frontend shared lib vs BFF API | Cross-track parallelism |
| P4-assoc | 030, 031 | CreateMatterWizard vs CreateProjectWizard | Different wizard components |
| P5-consol | 040, 043 | PlaybookLibrary vs LegalWorkspace | Different solution folders |

## Critical Path

```
001 → 002 → 003 → 008 → 009          (theme chain — longest in Phase 1)
           ↓
    ┌──────┴──────┐
Track A:          Track B:
010 → 011 → 015   020+021 → 022 → 025
           ↓
030+031 → 032 → 033 → 034 → 035      (wizard integration — serial for quality)
                              ↓
040 → 041 → 042 → 044 → 045          (consolidation chain)
                              ↓
                    050 → 051 → 090
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
