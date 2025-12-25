# AI Document Intelligence R2 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: December 25, 2025
> **Source**: README.md (project scope definition)

## Executive Summary

R2 delivers the **Analysis Workspace UI** - interactive custom pages and PCF controls that enable users to create, execute, and refine AI-driven document analyses directly within Power Apps Model-Driven Apps. This release deploys the already-built PCF controls and creates the custom page infrastructure.

## Scope

### In Scope

- Deploy AnalysisBuilder PCF control to Dataverse
- Deploy AnalysisWorkspace PCF control to Dataverse
- Create Analysis Builder Custom Page
- Create Analysis Workspace Custom Page
- Add Analysis tab to sprk_document form
- Add Analysis subgrid to Analysis tab
- Add "+ New Analysis" ribbon button with navigation
- Create navigation JavaScript for workspace redirect
- Test SSE streaming in custom page context
- Test environment variable resolution in PCF
- Export UI solution package
- Create UI user guide

### Out of Scope

- BFF API development (completed in R1)
- Dataverse entity creation (completed in R1)
- Hybrid RAG infrastructure (moved to R3)
- Playbook system save/load/share (moved to R3)
- Export to DOCX/PDF/Email/Teams (moved to R3)
- Performance optimization (moved to R3)
- Production deployment (moved to R3)

### Affected Areas

- `src/client/pcf/AnalysisBuilder/` - PCF control (deployment)
- `src/client/pcf/AnalysisWorkspace/` - PCF control (deployment)
- `src/solutions/` - Dataverse solution package (UI components)
- Power Apps Custom Pages (new)
- sprk_document entity form (customization)

## Requirements

### Functional Requirements

1. **FR-01**: AnalysisBuilder PCF loads in custom page - Acceptance: Control renders with scope tabs visible
2. **FR-02**: AnalysisWorkspace PCF displays 3-column layout - Acceptance: Analysis, Source, Chat columns visible
3. **FR-03**: SSE streaming works in PCF context - Acceptance: Chat messages stream in real-time
4. **FR-04**: PCF reads API URL from environment variable - Acceptance: No hard-coded URLs in control
5. **FR-05**: Monaco editor saves and loads content - Acceptance: Working document persists across sessions
6. **FR-06**: Analysis tab appears on Document form - Acceptance: Tab visible with Analysis grid
7. **FR-07**: "+ New Analysis" button launches builder - Acceptance: Click navigates to Analysis Builder page
8. **FR-08**: Analysis grid shows related analyses - Acceptance: Grid displays analyses linked to document

### Non-Functional Requirements

- **NFR-01**: PCF control loads within 3 seconds
- **NFR-02**: SSE stream connection established within 2 seconds
- **NFR-03**: Monaco editor responsive to typing (no lag)
- **NFR-04**: UI works in dark mode (Fluent UI v9)

## Technical Constraints

### Applicable ADRs

- **ADR-006**: PCF over webresources - All UI via PCF controls, no legacy JS webresources
- **ADR-011**: PCF control patterns - Follow established control structure
- **ADR-012**: Shared component library - Use @spaarke/ui-components where applicable
- **ADR-021**: Fluent UI v9 Design System - All UI uses Fluent v9, dark mode required

### MUST Rules

- MUST use Fluent UI v9 exclusively (no v8 components)
- MUST support dark mode theme
- MUST read configuration from Dataverse environment variables
- MUST NOT hard-code API URLs or Dataverse entity schemas
- MUST follow PCF-V9-PACKAGING.md for version bumping (4 locations)
- MUST include footer with version number

### Existing Patterns

- See `src/client/pcf/SpeFileViewer/` for PCF control patterns
- See `docs/guides/PCF-V9-PACKAGING.md` for deployment procedure
- See `.claude/patterns/pcf/control-initialization.md` for initialization
- See `.claude/patterns/pcf/theme-management.md` for theming

## Success Criteria

1. [ ] Analysis tab visible on Document form - Verify: Open document record
2. [ ] AnalysisBuilder PCF renders in custom page - Verify: Navigate to builder page
3. [ ] AnalysisWorkspace displays 3-column layout - Verify: Open existing analysis
4. [ ] SSE streaming works - Verify: Send chat message, see streaming response
5. [ ] Environment variables resolve - Verify: PCF logs show correct API URL
6. [ ] Solution exports cleanly - Verify: Import to test environment
7. [ ] UI user guide created - Verify: Document covers all workflows

## Dependencies

### Prerequisites (from R1)

| Requirement | Source | Status |
|-------------|--------|--------|
| Dataverse entities deployed | R1 Tasks 010-021 | Required |
| BFF API endpoints working | R1 Tasks 022-032 | Complete |
| Environment Variables in Dataverse | R1 Task 001 | Required |

### External Dependencies

- Power Apps Custom Pages (GA feature)
- Dataverse PCF deployment infrastructure
- BFF API deployed and accessible

## Existing Code Status

### PCF Controls (Built - Ready for Deployment)

| Control | Location | Files | Status |
|---------|----------|-------|--------|
| AnalysisBuilder | `src/client/pcf/AnalysisBuilder/` | 15 files | Built, not deployed |
| AnalysisWorkspace | `src/client/pcf/AnalysisWorkspace/` | 18 files | Built, not deployed |

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

## Existing Implementation (DO NOT RECREATE)

> **CRITICAL**: The following files already exist and are COMPLETE. Tasks should DEPLOY and CONFIGURE, not recreate code.

### PCF Controls (Built - Ready for Deployment)

| Control | Path | Status |
|---------|------|--------|
| AnalysisBuilder | `src/client/pcf/AnalysisBuilder/` | Built, not deployed |
| AnalysisWorkspace | `src/client/pcf/AnalysisWorkspace/` | Built, not deployed |

**AnalysisBuilder Components (15 files):**

| File | Path | Status |
|------|------|--------|
| AnalysisBuilderApp.tsx | `src/client/pcf/AnalysisBuilder/components/AnalysisBuilderApp.tsx` | COMPLETE |
| ScopeTabs.tsx | `src/client/pcf/AnalysisBuilder/components/ScopeTabs.tsx` | COMPLETE |
| ScopeList.tsx | `src/client/pcf/AnalysisBuilder/components/ScopeList.tsx` | COMPLETE |
| PlaybookSelector.tsx | `src/client/pcf/AnalysisBuilder/components/PlaybookSelector.tsx` | COMPLETE |
| FooterActions.tsx | `src/client/pcf/AnalysisBuilder/components/FooterActions.tsx` | COMPLETE |
| environmentVariables.ts | `src/client/pcf/AnalysisBuilder/utils/environmentVariables.ts` | COMPLETE |
| index.ts | `src/client/pcf/AnalysisBuilder/index.ts` | COMPLETE |
| ControlManifest.Input.xml | `src/client/pcf/AnalysisBuilder/ControlManifest.Input.xml` | COMPLETE |

**AnalysisWorkspace Components (18 files):**

| File | Path | Status |
|------|------|--------|
| AnalysisWorkspaceApp.tsx | `src/client/pcf/AnalysisWorkspace/components/AnalysisWorkspaceApp.tsx` | COMPLETE |
| MonacoEditor.tsx | `src/client/pcf/AnalysisWorkspace/components/MonacoEditor.tsx` | COMPLETE |
| RichTextEditor/ | `src/client/pcf/AnalysisWorkspace/components/RichTextEditor/` | COMPLETE |
| SourceDocumentViewer.tsx | `src/client/pcf/AnalysisWorkspace/components/SourceDocumentViewer.tsx` | COMPLETE |
| ChatPanel.tsx | `src/client/pcf/AnalysisWorkspace/components/ChatPanel.tsx` | COMPLETE |
| useSseStream.ts | `src/client/pcf/AnalysisWorkspace/hooks/useSseStream.ts` | COMPLETE |
| index.ts | `src/client/pcf/AnalysisWorkspace/index.ts` | COMPLETE |
| ControlManifest.Input.xml | `src/client/pcf/AnalysisWorkspace/ControlManifest.Input.xml` | COMPLETE |

### Prerequisites from R1 (STATUS UNKNOWN)

These items must be verified complete in R1 before R2 can proceed:

| Requirement | Source | Action Required |
|-------------|--------|-----------------|
| Dataverse entities deployed | R1 Tasks 010-021 | VERIFY in R1 first |
| BFF API endpoints working | R1 BFF code | VERIFY in R1 first |
| Environment Variables in Dataverse | R1 Task 001 | VERIFY in R1 first |

## Task Type Guidelines

When generating tasks, use these guidelines:

| Existing Status | Task Type | Task Action |
|-----------------|-----------|-------------|
| COMPLETE (PCF) | Deploy | Build and deploy to Dataverse, no code changes |
| Built, not deployed | Deploy + Configure | Deploy control, add to forms/pages |
| STATUS UNKNOWN | Verify + Create | Check if exists, create if missing |
| (not listed) | Create | New implementation needed |

## Questions/Clarifications

- [ ] Is the custom page infrastructure already set up in Dataverse?
- [ ] Are there existing JavaScript web resources for navigation that need updating?
- [ ] What is the target solution name for UI components?

---

*AI-optimized specification. Original: README.md*
