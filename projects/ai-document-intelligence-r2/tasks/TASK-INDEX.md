# Task Index - AI Document Intelligence R2

> **Project**: AI Document Intelligence R2 - Analysis Workspace UI
> **Created**: 2025-12-25
> **Total Tasks**: 17 tasks (4 PCF + 5 custom pages + 5 form integration + 3 solution + 2 docs + 1 wrap-up)

---

## Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| Phase 1: PCF Deployment | 001-004 | 🔲 Not Started |
| Phase 2: Custom Page Creation | 010-014 | 🔲 Not Started |
| Phase 3: Document Form Integration | 020-024 | 🔲 Not Started |
| Phase 4: Solution Packaging | 030-032 | 🔲 Not Started |
| Phase 5: Documentation | 040-041 | 🔲 Not Started |
| Project Completion | 090 | 🔲 Not Started |

---

## Phase 1: PCF Deployment

Deploy existing PCF controls to Dataverse.

| ID | Title | Status | Dependencies | Notes |
|----|-------|--------|--------------|-------|
| 001 | Build and Deploy AnalysisBuilder PCF | 🔲 Not Started | none | Uses dataverse-deploy skill |
| 002 | Build and Deploy AnalysisWorkspace PCF | 🔲 Not Started | 001 | Uses dataverse-deploy skill |
| 003 | Test PCF Controls in Test Harness | 🔲 Not Started | 001, 002 | |
| 004 | Document PCF Deployment Results | 🔲 Not Started | 003 | |

---

## Phase 2: Custom Page Creation

Create Power Apps Custom Pages hosting PCF controls.

| ID | Title | Status | Dependencies | Notes |
|----|-------|--------|--------------|-------|
| 010 | Create Analysis Builder Custom Page | 🔲 Not Started | 004 | |
| 011 | Create Analysis Workspace Custom Page | 🔲 Not Started | 004 | |
| 012 | Configure Custom Page Navigation | 🔲 Not Started | 010, 011 | |
| 013 | Test SSE Streaming in Custom Page | 🔲 Not Started | 012 | Critical - verify streaming works |
| 014 | Test Environment Variable Resolution | 🔲 Not Started | 013 | |

---

## Phase 3: Document Form Integration

Integrate analysis UI into the Document entity form.

| ID | Title | Status | Dependencies | Notes |
|----|-------|--------|--------------|-------|
| 020 | Add Analysis Tab to Document Form | 🔲 Not Started | 014 | |
| 021 | Add Analysis Subgrid to Tab | 🔲 Not Started | 020 | |
| 022 | Create Navigation JavaScript | 🔲 Not Started | 021 | Minimal webresource |
| 023 | Add New Analysis Ribbon Button | 🔲 Not Started | 022 | Uses ribbon-edit skill |
| 024 | Test Form Integration E2E | 🔲 Not Started | 023 | Full workflow test |

---

## Phase 4: Solution Packaging

Package UI components for deployment.

| ID | Title | Status | Dependencies | Notes |
|----|-------|--------|--------------|-------|
| 030 | Export UI Solution (Unmanaged) | 🔲 Not Started | 024 | |
| 031 | Test Solution Import | 🔲 Not Started | 030 | Clean environment test |
| 032 | Export Managed Solution | 🔲 Not Started | 031 | Production-ready |

---

## Phase 5: Documentation

Create user and deployment documentation.

| ID | Title | Status | Dependencies | Notes |
|----|-------|--------|--------------|-------|
| 040 | Create UI User Guide | 🔲 Not Started | 032 | |
| 041 | Update Deployment Guide | 🔲 Not Started | 032 | Add UI steps to R1 guide |

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
| AnalysisBuilder PCF | `src/client/pcf/AnalysisBuilder/` | Built - ready to deploy |
| AnalysisWorkspace PCF | `src/client/pcf/AnalysisWorkspace/` | Built - ready to deploy |

---

*Last Updated: 2025-12-25*
