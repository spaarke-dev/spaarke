# Task Index — UI Dialog & Shell Standardization

> **Last Updated**: 2026-03-19
> **Total Tasks**: 37
> **Status**: 🔲 Not started

## Status Legend

| Icon | Status |
|------|--------|
| 🔲 | Not started |
| 🔄 | In progress |
| ✅ | Completed |
| ⛔ | Blocked |

---

## Phase 1: Foundation — Service Abstraction + First Wizard Extraction

| # | Task | Effort | Dependencies | Status |
|---|------|--------|-------------|--------|
| 001 | Define IDataService, IUploadService, INavigationService interfaces | 4h | none | 🔲 |
| 002 | Create Xrm.WebApi adapter implementing IDataService | 3h | 001 | 🔲 |
| 003 | Create mock IDataService adapter for unit testing | 2h | 001 | 🔲 |
| 004 | Extract detectTheme() and parseDataParams() shared utilities | 3h | none | 🔲 |
| 005 | Extract CreateMatter wizard to shared library (15 files) | 8h | 001, 002 | 🔲 |
| 006 | Create CreateMatterWizard Code Page (Vite) | 4h | 004, 005 | 🔲 |
| 007 | Extract CreateProject wizard to shared library (7+ files) | 6h | 001, 002 | 🔲 |
| 008 | Create CreateProjectWizard Code Page (Vite) | 3h | 004, 007 | 🔲 |
| 009 | Update Corporate Workspace for Matter + Project (navigateTo) | 4h | 006, 008 | 🔲 |
| 010 | Deploy and verify Phase 1 end-to-end | 3h | 009 | 🔲 |

**Phase 1 Total**: ~40 hours

---

## Phase 2: Complete Wizard Extraction

| # | Task | Effort | Dependencies | Status |
|---|------|--------|-------------|--------|
| 011 | Extract CreateEvent wizard + Code Page | 5h | 001, 002, 004 | 🔲 |
| 012 | Extract CreateTodo wizard + Code Page | 5h | 001, 002, 004 | 🔲 |
| 013 | Extract CreateWorkAssignment wizard + Code Page (WizardShell direct) | 7h | 001, 002, 004 | 🔲 |
| 014 | Extract SummarizeFiles wizard + Code Page | 5h | 001, 002, 004 | 🔲 |
| 015 | Extract FindSimilar dialog + Code Page | 4h | 001, 002, 004 | 🔲 |
| 016 | Migrate DocumentUploadWizard from webpack to Vite | 5h | 004 | 🔲 |
| 017 | Complete Corporate Workspace restructuring (all navigateTo) | 6h | 011-015 | 🔲 |
| 018 | Deploy and verify Phase 2 end-to-end | 4h | 016, 017 | 🔲 |

**Phase 2 Total**: ~41 hours

---

## Phase 3: PlaybookLibraryShell

| # | Task | Effort | Dependencies | Status |
|---|------|--------|-------------|--------|
| 020 | Extract PlaybookLibraryShell from AnalysisBuilder (~508 LOC) | 8h | 001 | 🔲 |
| 021 | Add intent pre-selection support (replaces QuickStart) | 5h | 020 | 🔲 |
| 022 | Refactor AnalysisBuilder to thin wrapper | 3h | 020 | 🔲 |
| 023 | Create PlaybookLibrary Code Page (sprk_playbooklibrary) | 3h | 020 | 🔲 |
| 024 | Update Corporate Workspace playbook cards (navigateTo) | 4h | 021, 023 | 🔲 |
| 025 | Remove QuickStart code from LegalWorkspace | 2h | 024 | 🔲 |
| 026 | Deploy and verify Phase 3 end-to-end | 3h | 022, 024, 025 | 🔲 |

**Phase 3 Total**: ~28 hours

---

## Phase 4: Power Pages SPA Integration

| # | Task | Effort | Dependencies | Status |
|---|------|--------|-------------|--------|
| 030 | Create BFF API adapter for IDataService (SPA context) | 5h | 001 | 🔲 |
| 031 | Integrate Document Upload wizard in SPA | 4h | 016, 030 | 🔲 |
| 032 | Integrate PlaybookLibraryShell in SPA | 4h | 020, 030 | 🔲 |
| 033 | SPA theme integration for shared components | 2h | 031, 032 | 🔲 |
| 034 | Deploy and verify Phase 4 end-to-end | 3h | 031, 032, 033 | 🔲 |

**Phase 4 Total**: ~18 hours

---

## Phase 5: Ribbon / Command Bar Wiring

| # | Task | Effort | Dependencies | Status |
|---|------|--------|-------------|--------|
| 040 | Create sprk_wizard_commands.js webresource | 4h | 018 | 🔲 |
| 041 | Add wizard buttons to sprk_matter form command bar | 4h | 040 | 🔲 |
| 042 | Add wizard button to sprk_project form command bar | 3h | 040 | 🔲 |
| 043 | Add wizard buttons to sprk_event form command bar | 3h | 040 | 🔲 |
| 044 | Deploy all ribbon customizations | 2h | 041, 042, 043 | 🔲 |
| 045 | Verify all command bar wizard launches | 3h | 044 | 🔲 |

**Phase 5 Total**: ~19 hours

---

## Wrap-Up

| # | Task | Effort | Dependencies | Status |
|---|------|--------|-------------|--------|
| 090 | Project wrap-up (docs, testing, close) | 4h | 034, 045 | 🔲 |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 001, 004 | none | Independent foundation tasks |
| B | 002, 003 | 001 | Both depend only on interfaces |
| C | 005, 007 | 002 | Independent wizard extractions |
| D | 011, 012, 013, 014, 015 | 001, 002, 004 | All Phase 2 wizard extractions |
| E | 022, 023 | 020 | Independent AnalysisBuilder/PlaybookLibrary wrappers |
| F | 041, 042, 043 | 040 | Independent ribbon button additions |

---

## Critical Path

```
001 → 002 → 005 → 006 ─┐
                         ├→ 009 → 010 → [Phase 2] → 017 → 018 → 040 → 041-043 → 044 → 045 → 090
004 → 006 ──────────────┘
```

---

## Summary

| Phase | Tasks | Effort | Status |
|-------|-------|--------|--------|
| Phase 1: Foundation | 10 | ~40h | 🔲 |
| Phase 2: Complete Extraction | 8 | ~41h | 🔲 |
| Phase 3: PlaybookLibraryShell | 7 | ~28h | 🔲 |
| Phase 4: SPA Integration | 5 | ~18h | 🔲 |
| Phase 5: Ribbon Wiring | 6 | ~19h | 🔲 |
| Wrap-Up | 1 | ~4h | 🔲 |
| **Total** | **37** | **~150h** | 🔲 |
