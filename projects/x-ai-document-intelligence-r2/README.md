# AI Document Intelligence R2 - Analysis Workspace UI

> **Status**: Ready for Implementation
> **Phase**: Planning Complete
> **Progress**: 0%
> **Created**: December 25, 2025
> **Last Updated**: December 25, 2025

---

## Overview

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

## Dependencies

### Prerequisites from R1
| Requirement | Source | Status |
|-------------|--------|--------|
| Dataverse entities deployed | R1 Phase 1B | Required |
| BFF API endpoints working | R1 existing code | Required |
| Environment Variables in Dataverse | R1 Task 001 | Required |

### External Dependencies
- Power Apps Custom Pages (GA feature)
- Dataverse PCF deployment infrastructure
- BFF API deployed and accessible

## Existing Code Status

### PCF Controls (Built - Ready for Deployment)

| Control | Location | Status |
|---------|----------|--------|
| AnalysisBuilder | `src/client/pcf/AnalysisBuilder/` | Built, not deployed |
| AnalysisWorkspace | `src/client/pcf/AnalysisWorkspace/` | Built, not deployed |

> **IMPORTANT**: These controls are COMPLETE. R2 tasks should DEPLOY and CONFIGURE, not recreate code.

## Graduation Criteria

- [ ] AnalysisBuilder PCF deployed and renders in custom page
- [ ] AnalysisWorkspace PCF deployed with 3-column layout
- [ ] Analysis tab visible on Document form with subgrid
- [ ] "+ New Analysis" ribbon button navigates to builder
- [ ] SSE streaming works in custom page context
- [ ] Environment variables resolve correctly
- [ ] UI solution exported successfully
- [ ] User guide documented

## Key Documents

| Document | Purpose |
|----------|---------|
| [spec.md](spec.md) | Full specification |
| [plan.md](plan.md) | Implementation plan |
| [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) | Task registry |
| [CLAUDE.md](CLAUDE.md) | AI context |

## Technical Constraints

- **ADR-006**: PCF over webresources - All UI via PCF controls
- **ADR-011**: PCF control patterns - Follow established structure
- **ADR-012**: Shared component library - Use @spaarke/ui-components
- **ADR-021**: Fluent UI v9 Design System - Dark mode required

## Changelog

| Date | Change |
|------|--------|
| 2025-12-25 | Project initialized |

---

*AI Document Intelligence R2 - Analysis Workspace UI*
