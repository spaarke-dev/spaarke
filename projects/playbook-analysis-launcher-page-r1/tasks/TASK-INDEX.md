# Task Index — Playbook & Analysis Launcher Page R1

> **Total Tasks**: 22
> **Phases**: 5
> **Last Updated**: 2026-03-05

## Status Legend

| Symbol | Status |
|--------|--------|
| 🔲 | Not Started |
| 🔄 | In Progress |
| ✅ | Completed |
| ⏭️ | Skipped |

---

## Phase 1: Shared Playbook Component Library (Foundation)

| # | Task | Status | Rigor | Parallel Group | Dependencies |
|---|------|--------|-------|----------------|--------------|
| 001 | [Create Playbook Types and Interfaces](001-playbook-types-and-interfaces.poml) | ✅ | FULL | A | none |
| 002 | [Create PlaybookService — Dataverse WebAPI Queries](002-playbook-service.poml) | ✅ | FULL | A | 001 |
| 003 | [Create AnalysisService — Record Creation with N:N](003-analysis-service.poml) | ✅ | FULL | A | 001 |
| 004 | [Create PlaybookCardGrid Component](004-playbook-card-grid.poml) | ✅ | FULL | B | 001 |
| 005 | [Create ScopeList Component](005-scope-list-component.poml) | ✅ | FULL | B | 001 |
| 006 | [Create ScopeConfigurator Component](006-scope-configurator.poml) | ✅ | FULL | B | 001, 005 |
| 007 | [Create DocumentUploadStep Wrapper](007-document-upload-step.poml) | ✅ | FULL | C | 001 |
| 008 | [Create FollowUpActionsStep Component](008-followup-actions-step.poml) | ✅ | STANDARD | C | 001 |
| 009 | [Create Playbook Library Barrel Export](009-library-barrel-export.poml) | ✅ | MINIMAL | — | 001-008 |

## Phase 2: Analysis Builder Code Page

| # | Task | Status | Rigor | Parallel Group | Dependencies |
|---|------|--------|-------|----------------|--------------|
| 020 | [Scaffold Analysis Builder Code Page](020-analysis-builder-code-page-scaffold.poml) | ✅ | FULL | D | 009 |
| 021 | [Build AnalysisBuilderApp — 2-Tab Layout](021-analysis-builder-app.poml) | ✅ | FULL | — | 020, 002, 003, 004, 006 |
| 022 | [Verify Analysis Builder Build Pipeline](022-analysis-builder-build-verify.poml) | ✅ | STANDARD | — | 021 |
| 024 | [Update Command Bar Script for Code Page](024-update-command-bar-script.poml) | ✅ | FULL | D | 009 |

## Phase 3: Quick Start Playbook Wizards

| # | Task | Status | Rigor | Parallel Group | Dependencies |
|---|------|--------|-------|----------------|--------------|
| 030 | [Create QuickStartWizardDialog Component](030-quickstart-wizard-dialog.poml) | ✅ | FULL | E | 009 |
| 031 | [Create Quick Start Per-Card Configuration](031-quickstart-config.poml) | ✅ | STANDARD | E | 009 |
| 032 | [Update ActionCardHandlers for Wizard Launch](032-update-action-card-handlers.poml) | ✅ | FULL | — | 030, 031 |
| 033 | [Wire QuickStartWizardDialog in WorkspaceGrid](033-wire-workspace-grid.poml) | ✅ | FULL | — | 032 |
| 034 | [Rebuild and Verify Workspace](034-workspace-rebuild-verify.poml) | ✅ | STANDARD | — | 033 |

## Phase 4: Integration Testing & Polish

| # | Task | Status | Rigor | Parallel Group | Dependencies |
|---|------|--------|-------|----------------|--------------|
| 040 | [E2E: Analysis Builder Code Page](040-e2e-analysis-builder.poml) | 🔲 | STANDARD | F | 022, 024 |
| 041 | [E2E: Quick Start Playbook Wizards](041-e2e-quickstart-wizards.poml) | 🔲 | STANDARD | F | 034 |
| 042 | [Dark Mode Verification](042-dark-mode-verification.poml) | ✅ | STANDARD | F | 022, 034 |
| 043 | [Portability Check](043-portability-check.poml) | ✅ | MINIMAL | F | 034 |
| 044 | [Edge Case Testing](044-edge-case-testing.poml) | 🔲 | STANDARD | G | 040, 041 |

## Phase 5: Deployment & Retirement

| # | Task | Status | Rigor | Parallel Group | Dependencies |
|---|------|--------|-------|----------------|--------------|
| 050 | [Deploy Analysis Builder Code Page](050-deploy-analysis-builder.poml) | 🔲 | STANDARD | — | 040-044 |
| 051 | [Deploy Updated Corporate Workspace](051-deploy-workspace.poml) | 🔲 | STANDARD | — | 040-044 |
| 054 | [Retire AnalysisBuilder PCF Control](054-retire-analysis-builder-pcf.poml) | 🔲 | FULL | — | 050, 051 |
| 090 | [Project Wrap-Up](090-project-wrap-up.poml) | 🔲 | MINIMAL | — | 054 |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| **A** | 002, 003 | 001 complete | Services — independent Dataverse queries vs record creation |
| **B** | 004, 005 | 001 complete | UI components — independent; 006 depends on 005 |
| **C** | 007, 008 | 001 complete | Wizard steps — independent wrappers |
| **D** | 020, 024 | 009 complete | Code page scaffold + command bar update — separate files |
| **E** | 030, 031 | 009 complete | Wizard dialog + config — can co-develop |
| **F** | 040, 041, 042, 043 | Phases 2-3 complete | Independent verification tracks |
| **G** | 044 | Group F complete | Edge cases after main E2E |

## Critical Path

```
001 (types) → {002, 003, 004, 005, 007, 008} → 009 (barrel)
                                                    ↓
                        ┌───────────────────────────┼───────────────────────┐
                        ↓                           ↓                       ↓
                  020 → 021 → 022              030 + 031 → 032 → 033     024
                                                              → 034
                        ↓                           ↓
                  {040, 041, 042, 043} → 044
                        ↓
                  050, 051 → 054 → 090
```

**Longest path**: 001 → 002 → 009 → 020 → 021 → 022 → 040 → 044 → 050 → 054 → 090

---

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|-----------|
| 007 | Upload service imports across solution boundaries | Verify import paths early |
| 020 | Vite path aliases for shared components | Test in scaffold before full app |
| 024 | Command bar script breaks existing flow | Test independently |
| 054 | PCF retirement — destructive operation | Only after full E2E verification |
