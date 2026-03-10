# Task Index — SDAP File Upload & Document Creation Dialog (R2)

> **Project**: sdap-file-upload-document-r2
> **Total Tasks**: 25
> **Created**: 2026-03-09

## Status Legend

| Symbol | Status |
|--------|--------|
| 🔲 | Pending |
| 🔄 | In Progress |
| ✅ | Complete |
| ⏭️ | Skipped |
| ❌ | Blocked |

## Task Registry

### Phase 1: Shared Component Extraction

| # | Task | Status | Dependencies | Estimate |
|---|------|--------|-------------|----------|
| 001 | Extract WizardShell components to shared | ✅ | none | 2-3h |
| 002 | Extract FileUpload components to shared | ✅ | none | 2-3h |
| 003 | Extract EmailStep components to shared | ✅ | none | 2-3h |
| 004 | Extract FindSimilar components to shared | ✅ | none | 2-3h |
| 005 | Extract upload services to shared | ✅ | none | 3-4h |
| 006 | Extract useAiSummary hook to shared | ✅ | none | 1-2h |
| 007 | Update LegalWorkspace imports to shared | ✅ | 001-004 | 2-3h |
| 008 | Update UniversalQuickCreate imports to shared | ✅ | 005, 006 | 1-2h |

### Phase 2: Document Upload Wizard Code Page

| # | Task | Status | Dependencies | Estimate |
|---|------|--------|-------------|----------|
| 010 | Scaffold Code Page solution | ✅ | 001-006 | 2-3h |
| 011 | Implement wizard dialog and AddFilesStep | ✅ | 010 | 3-4h |
| 012 | Implement upload orchestrator with chunked upload | ✅ | 011 | 3-4h |
| 013 | Implement SummaryStep with Document Profile streaming | ✅ | 011, 006 | 3-4h |
| 014 | Implement NextStepsStep with dynamic step injection | ✅ | 011 | 2-3h |
| 015 | Implement Send Email dynamic wizard step | ✅ | 014, 003 | 2-3h |
| 016 | Implement success screen with document picker | ✅ | 014 | 2-3h |
| 017 | Implement next step launchers | ✅ | 016 | 1-2h |

### Phase 3: Search Profile Integration (Backend)

| # | Task | Status | Dependencies | Estimate |
|---|------|--------|-------------|----------|
| 020 | Add searchprofile mapping to DocumentProfileFieldMapper | ✅ | none | 1-2h |
| 021 | Implement BuildSearchProfile deterministic builder | ✅ | 020 | 2-3h |
| 022 | Integrate BuildSearchProfile into CreateFieldMapping | ✅ | 021 | 1-2h |
| 023 | Test search profile generation end-to-end | 🔲 | 022 | 1-2h |

### Phase 4: Ribbon Integration & Deployment

| # | Task | Status | Dependencies | Estimate |
|---|------|--------|-------------|----------|
| 030 | Build Code Page web resource | ✅ | 017 | 1-2h |
| 031 | Update ribbon commands for new wizard | ✅ | 030 | 2-3h |
| 032 | Deploy to Dataverse | 🔲 | 031 | 1-2h |
| 033 | End-to-end testing | 🔲 | 032, 023 | 2-3h |
| 034 | Dark mode and accessibility testing | ✅ | 032 | 1-2h |

### Phase 5: Wrap-Up

| # | Task | Status | Dependencies | Estimate |
|---|------|--------|-------------|----------|
| 040 | Update documentation | ✅ | 033 | 1-2h |
| 090 | Project wrap-up and archive | 🔲 | 040 | 1h |

## Dependencies Graph

```
Phase 1 (Shared Extraction) — all independent
  001 WizardShell ─┐
  002 FileUpload  ─┤
  003 EmailStep   ─┤── 007 Update LegalWorkspace imports
  004 FindSimilar ─┘
  005 Upload services ─┐
  006 useAiSummary  ───┤── 008 Update UniversalQuickCreate imports
                       └── 010 Scaffold Code Page (Phase 2 gate)

Phase 2 (Wizard Code Page)
  010 Scaffold ── 011 WizardDialog+AddFiles ─┬─ 012 Upload Orchestrator
                                              ├─ 013 SummaryStep
                                              └─ 014 NextStepsStep ─┬─ 015 Email Step
                                                                     └─ 016 Success Screen ── 017 Launchers

Phase 3 (Search Profile) — independent of Phase 2
  020 Field Mapping ── 021 BuildSearchProfile ── 022 Integration ── 023 Testing

Phase 4 (Deployment) — depends on Phase 2 + Phase 3
  030 Build ── 031 Ribbon ── 032 Deploy ─┬─ 033 E2E Testing
                                          └─ 034 Dark Mode Testing

Phase 5 (Wrap-up) — depends on Phase 4
  040 Documentation ── 090 Project Wrap-up
```

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 001, 002, 003, 004, 005, 006 | None | Independent component extractions |
| B | 007, 008 | Group A complete | Independent import updates |
| C | 012, 013, 014 | 011 complete | Independent wizard steps |
| D | Phase 2, Phase 3 | Phase 1 complete | Frontend wizard and backend search profile are independent |
| E | 033, 034 | 032 complete | Independent testing tracks |

## Critical Path

```
001-006 (parallel) → 010 → 011 → 014 → 016 → 017 → 030 → 031 → 032 → 033 → 040 → 090
```

Estimated total: ~45-60 hours across all tasks

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 001-004 | Shared extraction breaks LegalWorkspace build | Verify imports immediately (007) |
| 005 | Dual-API (webAPI + OData) adds complexity | Strategy pattern with IDataverseClient |
| 012 | Chunked upload untested in Code Page context | Fallback to single PUT for files under limit |
| 021 | Search profile quality for BM25 ranking | Test with variety of document types (023) |
