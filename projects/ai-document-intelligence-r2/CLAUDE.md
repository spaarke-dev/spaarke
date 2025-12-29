# CLAUDE.md - AI Document Intelligence R2

> **Project**: AI Document Intelligence R2 - Analysis Workspace UI
> **Status**: Ready for Implementation
> **Created**: December 25, 2025

---

## Project Context

R2 deploys the Analysis Workspace UI - PCF controls and custom pages for AI-driven document analysis. The PCF controls (AnalysisBuilder, AnalysisWorkspace) are already built and ready for deployment.

**Key Focus**: This project is about DEPLOYMENT and CONFIGURATION, not code creation.

## Applicable ADRs

| ADR | Title | Key Constraint |
|-----|-------|----------------|
| ADR-006 | PCF over webresources | All UI via PCF controls, no legacy JS webresources |
| ADR-011 | PCF control patterns | Follow established control structure |
| ADR-012 | Shared component library | Use @spaarke/ui-components where applicable |
| ADR-021 | Fluent UI v9 Design System | All UI uses Fluent v9, dark mode required |

## Applicable Skills

| Skill | Purpose | When to Use |
|-------|---------|-------------|
| `dataverse-deploy` | Deploy PCF, solutions, web resources | PCF deployment tasks (001-004) |
| `ribbon-edit` | Edit Dataverse ribbon customizations | Task 023 (ribbon button) |

## Knowledge Resources

| Resource | Path | Purpose |
|----------|------|---------|
| PCF Packaging Guide | `docs/guides/PCF-V9-PACKAGING.md` | Version management, deployment workflow |
| PCF Module Context | `src/client/pcf/CLAUDE.md` | PCF patterns, conventions |
| Dataverse Deploy Skill | `.claude/skills/dataverse-deploy/SKILL.md` | Deployment procedures |
| Ribbon Edit Skill | `.claude/skills/ribbon-edit/SKILL.md` | Ribbon customization |

## Existing Code References

### PCF Controls (DO NOT RECREATE)

| Control | Location | Files |
|---------|----------|-------|
| AnalysisBuilder | `src/client/pcf/AnalysisBuilder/` | 15 files |
| AnalysisWorkspace | `src/client/pcf/AnalysisWorkspace/` | 18 files |

**AnalysisBuilder Key Components:**
- `AnalysisBuilderApp.tsx` - Main app container
- `ScopeTabs.tsx` - Action/Skills/Knowledge/Tools tabs
- `ScopeList.tsx` - Scope item selection
- `FooterActions.tsx` - Start Analysis button

**AnalysisWorkspace Key Components:**
- `AnalysisWorkspaceApp.tsx` - Main 3-column layout
- `MonacoEditor.tsx` - Working document editor
- `ChatPanel.tsx` - SSE streaming chat
- `SourceDocumentViewer.tsx` - Source preview

## Task Phases

| Phase | Tasks | Focus |
|-------|-------|-------|
| Phase 1 | 001-004 | PCF Deployment |
| Phase 2 | 010-014 | Custom Page Creation |
| Phase 3 | 020-024 | Document Form Integration |
| Phase 4 | 030-032 | Solution Packaging |
| Phase 5 | 040-041 | Documentation |
| Completion | 090 | Project Wrap-up |

## MUST Rules

- MUST follow PCF-V9-PACKAGING.md for version bumping (4 locations)
- MUST include footer with version number in PCF controls
- MUST use Fluent UI v9 exclusively
- MUST support dark mode
- MUST read configuration from environment variables
- MUST NOT hard-code API URLs

## Quick Reference

```bash
# Build PCF control
cd src/client/pcf/AnalysisBuilder
npm run build

# Deploy via pac
pac pcf push --publisher-prefix sprk

# Solution workflow (production)
pac solution pack --zipfile Solution_vX.Y.Z.zip --folder Solution_extracted
pac solution import --path Solution_vX.Y.Z.zip --force-overwrite --publish-changes

# Verify deployment
pac solution list | grep -i "Analysis"
```

## Context Recovery

If resuming work:
1. Check `current-task.md` for active task state
2. Check `tasks/TASK-INDEX.md` for overall progress
3. Run `pac auth list` to verify Dataverse connection

---

*AI Document Intelligence R2 - Analysis Workspace UI*
