# Task Index - AI Document Intelligence R2

> **Project**: AI Document Intelligence R2 - Analysis Workspace UI
> **Created**: 2025-12-25
> **Total Tasks**: 17 tasks (4 PCF + 5 custom pages + 5 form integration + 3 solution + 2 docs + 1 wrap-up)

---

## Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| Phase 1: PCF Deployment | 001-004 | ✅ Completed |
| Phase 2: Custom Page Creation | 010-014 | ✅ Completed |
| Phase 3: Document Form Integration | 020-024 | ✅ Completed |
| Phase 4: Solution Packaging | 030-032 | ⏭️ Deferred |
| Phase 5: Documentation | 040-041 | ✅ Completed |
| Project Completion | 090 | 🔲 Not Started |

---

## Phase 1: PCF Deployment

Deploy existing PCF controls to Dataverse.

| ID | Title | Status | Dependencies | Notes |
|----|-------|--------|--------------|-------|
| 001 | Build and Deploy AnalysisBuilder PCF | ✅ Completed | none | Deployed v1.12.0 (source v1.5.0 outdated); verified in PowerAppsToolsTemp_sprk |
| 002 | Build and Deploy AnalysisWorkspace PCF | ✅ Completed | 001 | Deployed v1.0.29 on 2025-12-17; verified |
| 003 | Test PCF Controls in Test Harness | ✅ Completed | 001, 002 | Pass with BUG-001: Workspace toolbar hover issue |
| 004 | Document PCF Deployment Results | ✅ Completed | 003 | phase1-summary.md created |

---

## Phase 2: Custom Page Creation

Create Power Apps Custom Pages hosting PCF controls.

| ID | Title | Status | Dependencies | Notes |
|----|-------|--------|--------------|-------|
| 010 | Create Analysis Builder Custom Page | ✅ Completed | 004 | sprk_analysisbuilder_40af8 deployed |
| 011 | Create Analysis Workspace Custom Page | ✅ Completed | 004 | sprk_analysisworkspace_52748 deployed |
| 012 | Configure Custom Page Navigation | ✅ Completed | 010, 011 | Builder → Workspace navigation working |
| 013 | Test SSE Streaming in Custom Page | ✅ Completed | 012 | Code working; 404 due to in-memory storage (deferred to R3) |
| 014 | Test Environment Variable Resolution | ✅ Completed | 013 | sprk_BffApiBaseUrl correctly resolved |

---

## Phase 3: Document Form Integration

Integrate analysis UI into the Document entity form.

| ID | Title | Status | Dependencies | Notes |
|----|-------|--------|--------------|-------|
| 020 | Add Analysis Tab to Document Form | ✅ Completed | 014 | Already exists on sprk_document form |
| 021 | Add Analysis Subgrid to Tab | ✅ Completed | 020 | Already exists with sprk_analysis records |
| 022 | Create Navigation JavaScript | ✅ Completed | 021 | Web resource deployed |
| 023 | Add New Analysis Ribbon Button | ✅ Completed | 022 | Button deployed, launches Builder |
| 024 | Test Form Integration E2E | ✅ Completed | 023 | Navigation PASS; data issues deferred to R3 |

---

## Phase 4: Solution Packaging

Package UI components for deployment. **DEFERRED** - not ready for production deployment.

| ID | Title | Status | Dependencies | Notes |
|----|-------|--------|--------------|-------|
| 030 | Export UI Solution (Unmanaged) | ⏭️ Deferred | 024 | Deferred to future |
| 031 | Test Solution Import | ⏭️ Deferred | 030 | Deferred to future |
| 032 | Export Managed Solution | ⏭️ Deferred | 031 | Deferred to future |

---

## Phase 5: Documentation

Create and consolidate AI documentation.

| ID | Title | Status | Dependencies | Notes |
|----|-------|--------|--------------|-------|
| 040 | Create Analysis UI Documentation | ✅ Completed | 024 | Created AI-DEPLOYMENT-GUIDE.md (R1+R2) |
| 041 | Consolidate AI Documentation | ✅ Completed | 040 | Reduced 7→4 files; merged duplicates |

---

## Project Completion

| ID | Title | Status | Dependencies | Notes |
|----|-------|--------|--------------|-------|
| 090 | Project Wrap-up | 🔲 Not Started | 040, 041 | MANDATORY final task |

---

## Critical Path

```
Phase 1: PCF Deployment
001 → 002 → 003 → 004
                    ↓
Phase 2: Custom Pages
010, 011 (parallel after 004)
    ↓
   012 → 013 → 014
                ↓
Phase 3: Form Integration
020 → 021 → 022 → 023 → 024
                          ↓
Phase 4: Solution
030 → 031 → 032
              ↓
Phase 5: Documentation
040, 041 (parallel after 032)
    ↓
   090 (Wrap-up)
```

---

## Status Legend

| Symbol | Meaning |
|--------|---------|
| 🔲 | Not Started |
| 🔄 | In Progress |
| ✅ | Completed |
| ⏸️ | Conditional/Pending |
| ❌ | Blocked |
| ⏭️ | Skipped |

---

## Execution Notes

1. **Start with Phase 1** (Tasks 001-004) - Deploy existing PCF controls
2. **Phase 2 depends on Phase 1** - Custom pages need deployed controls
3. **Phase 3 depends on Phase 2** - Form integration needs working pages
4. **Task 013 is critical** - SSE streaming must work in Custom Page context
5. **Task 090** is MANDATORY - must complete to close project

---

## Existing Code (DO NOT RECREATE)

| Component | Path | Status |
|-----------|------|--------|
| AnalysisBuilder PCF | `src/client/pcf/AnalysisBuilder/` | ✅ Deployed (v1.12.0) |
| AnalysisWorkspace PCF | `src/client/pcf/AnalysisWorkspace/` | ✅ Deployed (v1.0.29) |
| Analysis Builder Custom Page | `sprk_analysisbuilder_40af8` | ✅ Deployed |
| Analysis Workspace Custom Page | `sprk_analysisworkspace_52748` | ✅ Deployed |
| Document Form - Analysis Tab | `sprk_document` main form | ✅ Deployed |
| Analysis Subgrid | On Document Analysis tab | ✅ Deployed |
| Navigation JavaScript | Web resource | ✅ Deployed |
| New Analysis Ribbon Button | `sprk_document` form ribbon | ✅ Deployed |

---

## Deferred to R3

| Issue | Description | Impact |
|-------|-------------|--------|
| Analysis Persistence | BFF API uses in-memory storage; analysis sessions lost on restart | Users cannot return to previous analyses |
| Analysis Builder Empty | No scopes (skills, knowledge, actions) displayed | Builder UI shows no content |
| Analysis Workspace Empty | No analysis data loaded | Workspace shows empty state |
| Fix Location | `AnalysisOrchestrationService.cs:36` - replace static dictionary with Dataverse | Requires full Dataverse integration |

---

*Last Updated: 2025-12-29*
