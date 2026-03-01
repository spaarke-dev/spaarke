# AI Playbook Node Builder R5

> **Last Updated**: 2026-02-28
>
> **Status**: In Progress

## Overview

Rebuild the Playbook Builder from a PCF control (React 16, react-flow-renderer v10) into a standalone React 19 Code Page using @xyflow/react v12+, and close the critical canvas-to-execution gap by building typed configuration forms for all 7 node types. This project transforms the Playbook Builder from a visual POC into a fully functional tool for composing executable AI playbooks.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan with 7 phases |
| [Design Spec](./design.md) | Comprehensive technical design |
| [AI Spec](./spec.md) | AI-optimized implementation specification |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown and status |
| [Project CLAUDE.md](./CLAUDE.md) | AI context file |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development |
| **Progress** | 0% |
| **Branch** | `work/ai-playbook-node-builder-r5` |
| **Predecessor** | `ai-playbook-node-builder-r4` (PCF, React 16) |
| **Execution Model** | Autonomous Claude Code with parallel task agents |

## Problem Statement

The current Playbook Builder (R4 PCF) has two critical issues: (1) it's built as a PCF control with React 16, preventing use of @xyflow/react v12 which requires React 18+; (2) only 2 of 7 node types (AI Analysis, Condition) can execute end-to-end because the other 5 lack configuration forms that write `sprk_configjson`. All scope selectors use hardcoded mock data ('skill-1' through 'skill-6', fake GUIDs).

## Solution Summary

Rebuild as a React 19 Code Page using @xyflow/react v12+, following proven patterns from AnalysisWorkspace (auth, build pipeline) and DocumentRelationshipViewer (@xyflow v12). Replace all mock data with real Dataverse queries. Build typed configuration forms for all 7 node types so `buildConfigJson()` produces valid execution config. Remove the PCF control from the solution.

## Graduation Criteria

The project is considered **complete** when:

- [ ] All 7 node types render on @xyflow/react v12 canvas
- [ ] All scope selectors query real Dataverse tables (zero mock data)
- [ ] All 7 node types have configuration forms that write valid `sprk_configjson`
- [ ] All 7 node types execute end-to-end from AnalysisWorkspace without ConfigJson errors
- [ ] Code page opens from playbook form via `Xrm.Navigation.navigateTo`
- [ ] Dark mode, light mode, high-contrast all render correctly (ADR-021)
- [ ] PCF PlaybookBuilderHost removed from solution
- [ ] No `react-flow-renderer` references remain in codebase
- [ ] Build produces single inline HTML web resource (`sprk_playbookbuilder.html`)

## Scope

### In Scope

- Code Page scaffold (React 19, Webpack 5, build-webresource.ps1)
- Authentication (multi-strategy from AnalysisWorkspace)
- DataverseClient service (fetch-based CRUD)
- Canvas migration (v10 → v12 with typed generics)
- All 7 custom node type migrations
- Scope resolution (mock → real Dataverse queries)
- ActionSelector component (new)
- Node configuration forms for all 7 types
- Template variable panel (`{{nodeName.output.fieldName}}`)
- Node validation badges
- playbookNodeSync rewrite
- AI Assistant migration
- Auto-save + dark mode
- PCF cleanup

### Out of Scope

- New node types beyond existing 7
- Playbook execution engine changes (BFF API unchanged)
- AnalysisWorkspace changes
- New Dataverse entities or schema changes
- Office add-in integration
- Staging/production deployment

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Code Page over PCF | Standalone workspace needs React 19 for @xyflow v12 | ADR-006 |
| Direct Dataverse REST API | Code Page owns build-time CRUD; BFF only reads at execution | — |
| Preserve canvas JSON format | v12 uses same node/edge schema as v10 | — |
| Auth from AnalysisWorkspace | Proven multi-strategy pattern | — |
| Zustand stays | Framework-agnostic; only data sources change | — |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| @xyflow v12 API differences | Medium | Low | DocumentRelationshipViewer proves compatibility |
| N:N relationship writes from Code Page | Medium | Low | Fallback to $batch requests |
| Missing executors (Wait, AI Completion) | Low | High | Build config forms; note execution requires separate BFF work |
| Canvas JSON backward compatibility | High | Low | v12 uses same schema; add migration function if needed |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| AnalysisWorkspace code page | Internal | Ready | Auth, build pipeline, config patterns |
| DocumentRelationshipViewer | Internal | Ready | @xyflow v12 reference implementation |
| BFF API streaming endpoint | Internal | Ready | AI Assistant chat — unchanged |
| Dataverse dev environment | Internal | Ready | Scope table data must be populated |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Ralph Schroeder | Design review, specification |
| Developer | Claude Code (autonomous) | Implementation via parallel task agents |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-02-28 | 1.0 | Initial project setup from spec.md | Claude Code |

---

*Template version: 1.0 | Based on Spaarke development lifecycle*
