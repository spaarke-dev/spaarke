# AI Document Intelligence R2 - Analysis Workspace UI

> **Status**: Not Started
> **Version**: 1.0
> **Predecessor**: ai-document-intelligence-r1 (Core Infrastructure)
> **Target**: Power Apps Custom Pages + PCF Controls Deployment

---

## Overview

R2 delivers the **Analysis Workspace UI** - interactive custom pages and PCF controls that enable users to create, execute, and refine AI-driven document analyses directly within Power Apps Model-Driven Apps.

**Key Deliverables:**
- Analysis Builder Custom Page (configure analysis parameters)
- Analysis Workspace Custom Page (3-column layout: Analysis + Source + Chat)
- PCF Controls deployed to Dataverse
- Form customizations on `sprk_document` entity
- End-to-end testing of UI with deployed API

---

## Prerequisites (from R1)

Before starting R2, the following must be complete:

| Requirement | Source | Status |
|-------------|--------|--------|
| Dataverse entities (sprk_analysis, sprk_analysisaction, etc.) | R1 Tasks 010-021 | Required |
| BFF API endpoints (/api/ai/analysis/*) | R1 Tasks 022-032 | Complete |
| Environment Variables in Dataverse | R1 Task 001 | Required |
| PCF controls built (AnalysisBuilder, AnalysisWorkspace) | R1 Tasks 042-061 | Complete |

---

## Existing Code Inventory

### PCF Controls (Already Created - Need Deployment/Testing)

| Component | Location | Status |
|-----------|----------|--------|
| AnalysisBuilder PCF | [src/client/pcf/AnalysisBuilder/](../../src/client/pcf/AnalysisBuilder/) | Built, not deployed |
| AnalysisWorkspace PCF | [src/client/pcf/AnalysisWorkspace/](../../src/client/pcf/AnalysisWorkspace/) | Built, not deployed |

**AnalysisBuilder Components:**
- `AnalysisBuilderApp.tsx` - Main app container
- `ScopeTabs.tsx` - Action/Skills/Knowledge/Tools tabs
- `ScopeList.tsx` - Scope item selection
- `PlaybookSelector.tsx` - Playbook dropdown
- `FooterActions.tsx` - Start Analysis button
- `environmentVariables.ts` - Environment variable access

**AnalysisWorkspace Components:**
- `AnalysisWorkspaceApp.tsx` - Main 3-column layout
- `MonacoEditor.tsx` - Working document editor
- `RichTextEditor/` - Alternative rich text editor
- `SourceDocumentViewer.tsx` - Source preview panel
- `useSseStream.ts` - SSE streaming hook for chat

### BFF API (Already Complete)

| File | Location | Endpoints |
|------|----------|-----------|
| AnalysisEndpoints.cs | [src/server/api/Sprk.Bff.Api/Api/Ai/](../../src/server/api/Sprk.Bff.Api/Api/Ai/) | /execute, /continue, /save, /export, GET |

---

## Scope

### In Scope (R2)

1. **Custom Page Development**
   - Analysis Builder Custom Page
   - Analysis Workspace Custom Page
   - Navigation integration

2. **Form Customizations**
   - Add Analysis tab to sprk_document form
   - Analysis grid showing related analyses
   - "+ New Analysis" command button

3. **PCF Deployment**
   - Build and package existing PCF controls
   - Deploy to Dataverse dev environment
   - Configure on custom pages/forms

4. **Integration Testing**
   - Test SSE streaming in custom page context
   - Verify environment variable resolution
   - End-to-end analysis workflow testing

### Out of Scope (Deferred to R3)

- Hybrid RAG infrastructure
- Tool handler framework
- Playbook system (save/load/share)
- Export to DOCX/PDF/Email/Teams
- Production deployment
- Performance optimization

---

## Tasks

### Phase 2A: Dataverse Form Customizations

| ID | Task | Status | Hours |
|----|------|--------|-------|
| R2-001 | Create Analysis tab on sprk_document form | Not Started | 2h |
| R2-002 | Add Analysis subgrid to tab | Not Started | 2h |
| R2-003 | Add "+ New Analysis" ribbon button | Not Started | 2h |
| R2-004 | Create navigation JavaScript for workspace redirect | Not Started | 3h |

### Phase 2B: Custom Page Creation

| ID | Task | Status | Hours |
|----|------|--------|-------|
| R2-005 | Create Analysis Builder Custom Page | Not Started | 4h |
| R2-006 | Create Analysis Workspace Custom Page | Not Started | 4h |
| R2-007 | Configure page navigation and parameters | Not Started | 3h |

### Phase 2C: PCF Deployment

| ID | Task | Status | Hours |
|----|------|--------|-------|
| R2-008 | Build AnalysisBuilder PCF (npm run build) | Not Started | 1h |
| R2-009 | Build AnalysisWorkspace PCF (npm run build) | Not Started | 1h |
| R2-010 | Create PCF solution project | Not Started | 2h |
| R2-011 | Package PCF controls into solution | Not Started | 2h |
| R2-012 | Import solution to dev environment | Not Started | 2h |
| R2-013 | Add AnalysisBuilder PCF to custom page | Not Started | 2h |
| R2-014 | Add AnalysisWorkspace PCF to custom page | Not Started | 2h |

### Phase 2D: Integration Testing

| ID | Task | Status | Hours |
|----|------|--------|-------|
| R2-015 | Test SSE streaming in custom page | Not Started | 3h |
| R2-016 | Test environment variable resolution | Not Started | 2h |
| R2-017 | Test analysis creation flow | Not Started | 2h |
| R2-018 | Test analysis workspace navigation | Not Started | 2h |
| R2-019 | Test Monaco editor save/load | Not Started | 2h |
| R2-020 | Fix any integration issues | Not Started | 4h |

### Phase 2E: Solution Export

| ID | Task | Status | Hours |
|----|------|--------|-------|
| R2-021 | Export UI solution package | Not Started | 2h |
| R2-022 | Document deployment steps | Not Started | 3h |
| R2-023 | Create UI user guide | Not Started | 4h |

---

## Estimated Effort

| Phase | Tasks | Hours |
|-------|-------|-------|
| 2A: Form Customizations | 4 | 9h |
| 2B: Custom Pages | 3 | 11h |
| 2C: PCF Deployment | 7 | 12h |
| 2D: Integration Testing | 6 | 15h |
| 2E: Solution Export | 3 | 9h |
| **Total** | **23** | **56h** |

---

## Success Criteria

- [ ] Analysis tab visible on Document form
- [ ] Analysis Builder custom page launches from Document
- [ ] Analysis Workspace displays 3-column layout
- [ ] SSE streaming works in custom page context
- [ ] PCF controls read API URL from environment variable
- [ ] Can create and execute an analysis end-to-end
- [ ] Solution exports and imports cleanly

---

## Dependencies on R1

| R1 Task | Description | Required By |
|---------|-------------|-------------|
| 010-021 | Dataverse entities | R2-001 (Analysis tab) |
| 001 | Environment Variables | R2-016 (env var testing) |
| 022-032 | API endpoints | All integration testing |

**Note**: R1 API code is complete, but Dataverse entities may not be deployed. Verify entity existence before starting R2.

---

## Related Documentation

- [AnalysisBuilder README](../../src/client/pcf/AnalysisBuilder/README.md) (if exists)
- [AnalysisWorkspace README](../../src/client/pcf/AnalysisWorkspace/README.md) (if exists)
- [PCF-V9-PACKAGING.md](../../docs/guides/PCF-V9-PACKAGING.md) - PCF deployment guide
- [Custom Pages Guide](../../docs/guides/power-apps-custom-pages.md)

---

*Created: December 25, 2025*
