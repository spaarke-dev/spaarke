# UI Dialog & Shell Standardization

## Quick Links

- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Design Specification](spec.md)
- [Original Design](design.md)
- [AI Context](CLAUDE.md)

## Overview

This project extracts all wizard/dialog components embedded in the Corporate Workspace into standalone, independently deployable Code Page web resources backed by shared library components. It introduces PlaybookLibraryShell as a reusable shared component that replaces AnalysisBuilder's monolithic App.tsx. The Corporate Workspace is restructured to use `navigateTo` calls instead of inline dialogs.

## Problem Statement

The Corporate Workspace (`sprk_corporateworkspace`) embeds 11 lazy-loaded React dialogs directly in its bundle via `WorkspaceGrid.tsx` (~835 LOC). This creates several problems:

1. **No reusability**: Wizards (Create Matter, Create Project, etc.) cannot be launched from entity main form command bars — they only exist inside the Corporate Workspace bundle
2. **Inconsistent chrome**: Some dialogs get Dataverse modal chrome (expand button, title bar) while inline dialogs do not
3. **Monolithic bundle**: All wizard code ships in the workspace bundle even when only the grid is needed
4. **No cross-platform reuse**: Power Pages external SPA cannot use these wizards
5. **Playbook Library not portable**: AnalysisBuilder's playbook browsing UI is a standalone 508-LOC monolith that can't be embedded elsewhere

## Proposed Solution

**Three-layer architecture:**

| Layer | Purpose | Examples |
|-------|---------|---------|
| **Shared Library** (`@spaarke/ui-components`) | Single source of truth for all wizard/shell/dialog components | CreateMatterWizard, PlaybookLibraryShell, WizardShell |
| **Code Page Wrappers** (`src/solutions/`) | Thin ~30-50 LOC entry points for Dataverse context | `sprk_creatematterwizard`, `sprk_playbooklibrary` |
| **Consumer Integration** | Hosting contexts that open or embed shared components | Corporate Workspace (navigateTo), Power Pages SPA (inline Dialog), Entity forms (ribbon → navigateTo) |

## Scope

### In Scope

- **Part A**: IDataService abstraction layer (ADR-012 service portability)
- **Part B**: Extract 7 wizard component sets to shared library
- **Part C**: Create 7 Code Page wrappers (Vite + React 19)
- **Part D**: PlaybookLibraryShell (extract from AnalysisBuilder, absorb QuickStart)
- **Part E**: Restructure Corporate Workspace (navigateTo, reduce WorkspaceGrid)
- **Part F**: Shared utilities (detectTheme, parseDataParams)
- **Part G**: Power Pages SPA integration
- **Part H**: Ribbon / command bar wiring

### Out of Scope

- QuickSummaryDashboard, GetStartedExpandDialog, CloseProjectDialog (workspace-specific, remain inline)
- Smart To Do / Kanban (separate concern)
- Activity Feed, Notification Panel (workspace-specific)
- New wizard or playbook creation

## Graduation Criteria

- [ ] All 7 create/tool wizards launchable from both Corporate Workspace AND entity main form command bars
- [ ] Consistent Dataverse modal chrome across all wizard dialogs
- [ ] Single source of truth for wizard logic in `@spaarke/ui-components` (no wizard step components remain in LegalWorkspace)
- [ ] PlaybookLibraryShell replaces AnalysisBuilder App.tsx core logic
- [ ] Corporate Workspace WorkspaceGrid.tsx < 500 LOC
- [ ] DocumentUploadWizard migrated from webpack to Vite
- [ ] Power Pages SPA can render Upload Documents and PlaybookLibrary using shared components
- [ ] All services use IDataService abstraction (zero direct webApi calls in shared library)
- [ ] Clean webresource display names in Dataverse modal title bars

---

*Project initialized: 2026-03-19*
