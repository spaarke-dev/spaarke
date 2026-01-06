# Implementation Plan - AI Document Intelligence R2

> **Project**: AI Document Intelligence R2 - Analysis Workspace UI
> **Status**: Ready for Implementation
> **Created**: December 25, 2025

---

## 1. Executive Summary

This plan covers the deployment and configuration of the Analysis Workspace UI components for the AI Document Intelligence feature. The PCF controls (AnalysisBuilder and AnalysisWorkspace) are already built; this release focuses on deploying them to Dataverse and creating the custom page infrastructure.

## 2. Objectives

1. Deploy existing AnalysisBuilder and AnalysisWorkspace PCF controls to Dataverse
2. Create custom pages for Analysis Builder and Analysis Workspace
3. Integrate analysis UI into the Document form (tab, subgrid, ribbon button)
4. Validate SSE streaming and environment variable resolution
5. Package and document the UI solution

## 3. Architecture Context

### Discovered Resources

| Type | Resources |
|------|-----------|
| **ADRs** | ADR-006 (PCF over webresources), ADR-011 (PCF patterns), ADR-012 (Shared components), ADR-021 (Fluent v9) |
| **Skills** | dataverse-deploy, ribbon-edit |
| **Knowledge** | PCF-V9-PACKAGING.md, src/client/pcf/CLAUDE.md |
| **Existing Code** | AnalysisBuilder (15 files), AnalysisWorkspace (18 files) |
| **Scripts** | Deploy-PCFWebResources.ps1 |

### Existing PCF Controls

```
src/client/pcf/
├── AnalysisBuilder/           # Ready for deployment
│   ├── ControlManifest.Input.xml
│   ├── index.ts
│   └── components/
│       ├── AnalysisBuilderApp.tsx
│       ├── ScopeTabs.tsx
│       ├── ScopeList.tsx
│       └── FooterActions.tsx
│
└── AnalysisWorkspace/         # Ready for deployment
    ├── ControlManifest.Input.xml
    ├── index.ts
    └── components/
        ├── AnalysisWorkspaceApp.tsx
        ├── MonacoEditor.tsx
        ├── ChatPanel.tsx
        └── SourceDocumentViewer.tsx
```

## 4. Constraints

### MUST Rules
- MUST use Fluent UI v9 exclusively (no v8 components)
- MUST support dark mode theme
- MUST read configuration from Dataverse environment variables
- MUST NOT hard-code API URLs or Dataverse entity schemas
- MUST follow PCF-V9-PACKAGING.md for version bumping (4 locations)
- MUST include footer with version number

### ADR Constraints
- **ADR-006**: PCF over webresources - All UI via PCF controls, no legacy JS webresources
- **ADR-011**: PCF control patterns - Follow established control structure
- **ADR-012**: Shared component library - Use @spaarke/ui-components where applicable
- **ADR-021**: Fluent UI v9 Design System - All UI uses Fluent v9, dark mode required

## 5. Phase Breakdown (WBS)

### Phase 1: PCF Deployment (Tasks 001-004)

Deploy the existing PCF controls to Dataverse environment.

| Task | Description | Deliverable |
|------|-------------|-------------|
| 001 | Build and deploy AnalysisBuilder PCF | Control available in Dataverse |
| 002 | Build and deploy AnalysisWorkspace PCF | Control available in Dataverse |
| 003 | Test PCF controls in test harness | Verification results |
| 004 | Document PCF deployment | Deployment notes |

### Phase 2: Custom Page Creation (Tasks 010-014)

Create custom pages that host the PCF controls.

| Task | Description | Deliverable |
|------|-------------|-------------|
| 010 | Create Analysis Builder Custom Page | Custom page in Power Apps |
| 011 | Create Analysis Workspace Custom Page | Custom page in Power Apps |
| 012 | Configure custom page navigation | URL parameters, routing |
| 013 | Test SSE streaming in custom page context | SSE verification |
| 014 | Test environment variable resolution | Env var verification |

### Phase 3: Document Form Integration (Tasks 020-024)

Integrate analysis functionality into the Document entity form.

| Task | Description | Deliverable |
|------|-------------|-------------|
| 020 | Add Analysis tab to sprk_document form | Form customization |
| 021 | Add Analysis subgrid to Analysis tab | Subgrid configuration |
| 022 | Create navigation JavaScript web resource | Navigation script |
| 023 | Add "+ New Analysis" ribbon button | Ribbon customization |
| 024 | Test form integration end-to-end | E2E verification |

### Phase 4: Solution Packaging (Tasks 030-032)

Package all UI components into a deployable solution.

| Task | Description | Deliverable |
|------|-------------|-------------|
| 030 | Export UI solution package (unmanaged) | Solution ZIP |
| 031 | Test solution import to clean environment | Import verification |
| 032 | Export managed solution for production | Managed solution |

### Phase 5: Documentation (Tasks 040-041)

Create user and deployment documentation.

| Task | Description | Deliverable |
|------|-------------|-------------|
| 040 | Create UI user guide | User documentation |
| 041 | Update deployment guide with UI steps | Deployment docs |

### Project Completion (Task 090)

| Task | Description | Deliverable |
|------|-------------|-------------|
| 090 | Project wrap-up | README updated, lessons learned |

## 6. Dependencies

```
Phase 1: PCF Deployment
001 (AnalysisBuilder)
  ↓
002 (AnalysisWorkspace)
  ↓
003 (Test harness)
  ↓
004 (Document deployment)

Phase 2: Custom Pages (depends on Phase 1)
010 (Builder page) ──┬──→ 012 (Navigation)
011 (Workspace page) ┘        ↓
                           013 (SSE test)
                             ↓
                           014 (Env var test)

Phase 3: Form Integration (depends on Phase 2)
020 (Analysis tab)
  ↓
021 (Subgrid)
  ↓
022 (Navigation JS) ──→ 023 (Ribbon button)
                              ↓
                           024 (E2E test)

Phase 4: Solution (depends on Phase 3)
030 (Export unmanaged)
  ↓
031 (Test import)
  ↓
032 (Export managed)

Phase 5: Documentation (parallel with Phase 4)
040 (User guide)
041 (Deployment guide)

Project Completion
090 (Wrap-up) ← depends on all above
```

## 7. Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Custom page SSE limitations | High | Test early in Phase 2, have fallback polling |
| PCF bundle size exceeds limit | Medium | Use platform libraries (React/Fluent external) |
| Environment variable access in PCF | Medium | Test in Task 014, document workarounds |
| Ribbon customization conflicts | Low | Use ribbon-edit skill, test in dev first |

## 8. Success Criteria

1. [ ] AnalysisBuilder PCF renders in custom page with scope tabs visible
2. [ ] AnalysisWorkspace PCF displays 3-column layout (Analysis, Source, Chat)
3. [ ] SSE streaming works - chat messages stream in real-time
4. [ ] Environment variables resolve - PCF logs show correct API URL
5. [ ] Analysis tab appears on Document form with subgrid
6. [ ] "+ New Analysis" button navigates to Analysis Builder page
7. [ ] Solution exports cleanly and imports to test environment
8. [ ] UI user guide covers all workflows

## 9. Milestones

| Milestone | Tasks | Status |
|-----------|-------|--------|
| M1: PCF Deployed | 001-004 | 🔲 Not Started |
| M2: Custom Pages Working | 010-014 | 🔲 Not Started |
| M3: Form Integration Complete | 020-024 | 🔲 Not Started |
| M4: Solution Packaged | 030-032 | 🔲 Not Started |
| M5: Documentation Complete | 040-041 | 🔲 Not Started |
| M6: Project Complete | 090 | 🔲 Not Started |

## 10. References

- [spec.md](spec.md) - Full specification
- [PCF-V9-PACKAGING.md](../../docs/guides/PCF-V9-PACKAGING.md) - PCF deployment guide
- [src/client/pcf/CLAUDE.md](../../src/client/pcf/CLAUDE.md) - PCF module context
- [dataverse-deploy skill](../../.claude/skills/dataverse-deploy/SKILL.md) - Deployment procedures

---

*Implementation Plan for AI Document Intelligence R2*
