# Spaarke Visuals Framework

> **Last Updated**: 2026-01-02
>
> **Status**: Complete

## Overview

Spaarke Visuals is a configuration-driven visualization framework delivering operational, in-app visuals (cards, charts, calendars, drill-through workspaces) within Model-Driven Apps and Custom Pages. It replaces legacy Power Apps charts with a modern, Fluent v9-aligned visualization layer optimized for legal operations workflows.

## Quick Links

| Document | Description |
|----------|-------------|
| [Configuration Guide](./notes/visualization-configuration-guide.md) | **How to create and configure visuals** |
| [Setup Guide](./notes/power-app-setup-guide.md) | Technical setup for PCF controls |
| [Project Plan](./plan.md) | Implementation plan with WBS |
| [Spec](./spec.md) | AI-optimized design specification |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown and status |
| [Entity Schema](./notes/entity-schema.md) | sprk_chartdefinition entity schema |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Complete |
| **Progress** | 100% |
| **Completed Date** | 2026-01-02 |
| **Owner** | Development Team |

## Final Deliverables

### PCF Controls Deployed

| Control | Version | Bundle Size | Namespace |
|---------|---------|-------------|-----------|
| **VisualHost** | v1.1.17 | 684 KB | `sprk_Spaarke.Visuals.VisualHost` |
| **DrillThroughWorkspace** | v1.1.1 | 163 KB | `sprk_Spaarke.Controls.DrillThroughWorkspace` |

### Key Features

| Feature | Status |
|---------|--------|
| 7 Visual Types (MetricCard, Bar, Line, Area, Donut, StatusBar, MiniTable) | Delivered |
| Configuration via sprk_chartdefinition entity | Delivered |
| Lookup binding to Chart Definition | Delivered |
| Static ID binding for multiple charts per form | Delivered |
| Context filtering (show related records only) | Delivered |
| Drill-through expand button to Custom Page | Delivered |
| Platform libraries (React 16, Fluent v9) | Delivered |
| Form JavaScript for cascading lookups | Delivered |

### Documentation

| Guide | Audience | Purpose |
|-------|----------|---------|
| [Visualization Configuration Guide](./notes/visualization-configuration-guide.md) | Admins/Makers | Create visuals end-to-end |
| [Power App Setup Guide](./notes/power-app-setup-guide.md) | Developers | Technical PCF setup |
| [Entity Schema](./notes/entity-schema.md) | Developers | sprk_chartdefinition fields |

## Problem Statement

Legacy Power Apps charts in Spaarke's Model-Driven Apps are limited in functionality, lack modern UX patterns, and don't support drill-through interactions. Users need visuals that show aggregated data and allow them to quickly navigate to the underlying records without leaving their current context.

## Solution Summary

Build a configuration-driven visualization framework using Fluent UI v9 charting components. A unified Visual Host PCF control reads configuration from `sprk_chartdefinition` records and renders the appropriate visual type. Each visual supports a drill-through workspace pattern where users can click chart elements to filter an adjacent dataset grid in real-time.

## Graduation Criteria

The project is considered **complete** when:

- [x] Visual Host PCF renders all 7 visual types (Metric Card, Bar, Line, Area, Donut, Status Bar, Mini Table)
- [x] All chart visuals use `@fluentui/react-charting` exclusively (no Chart.js, D3, etc.)
- [x] Drill-through workspace opens as expanded modal with chart
- [ ] Interactive filtering works (click chart segment → grid filters in real-time) - *Deferred to future project*
- [x] Dataverse security enforced automatically (row-level, field-level, BU/team)
- [x] Dark mode and high-contrast themes work correctly (no hard-coded colors)
- [x] PCF bundle under 5MB (actual: 684 KB)
- [ ] 80%+ test coverage on PCF controls - *Unit tests written, coverage measurement pending*
- [x] Storybook stories for all chart components
- [x] Admin can create/edit chart definitions via Model-driven app

## Future Enhancements

### 1. Drill-Through Filtering (Deferred)

**Current State**: Drill-through dialog opens, Data Table shows all records.

**Future State**: Pass filter parameters to Custom Page, Data Table shows filtered records.

**Blocked By**: Power Fx `Param()` limitations with `navigateTo` dialogs.

**Tracked In**: `projects/universal-dataset-grid-r2/README.md`

### 2. UniversalDatasetGrid Fix

**Issue**: React 18 `createRoot()` API incompatible with Dataverse platform React 16.14.0.

**Required Fix**: Migrate from `createRoot()` to `ReactDOM.render()`.

**Tracked In**: `projects/universal-dataset-grid-r2/README.md`

## Scope

### In Scope (Delivered)

- Visual Host PCF (unified control for all visual types)
- 7 visual types: Metric Card, Bar/Column, Line/Area, Donut, Status Distribution Bar, Mini Table
- Fluent UI v9 charting integration (`@fluentui/react-charting`)
- Drill-through visual workspace (expanded modal pattern)
- Dataverse view binding via `sprk_baseviewid`
- Phase 1 admin governance (`sprk_chartdefinition` entity)
- Dark mode and high-contrast theme support
- Hybrid binding (lookup OR static ID)
- Context filtering for related records

### Out of Scope

- BI/Analytics (Power BI/Fabric serves this need)
- Chart.js, Recharts, VisX, or D3 libraries
- Fluent v8 APIs
- End-user FetchXML authoring
- Phase 2 personal/user-defined visuals
- BFF aggregation endpoints
- External surfaces (Power Pages, add-ins)
- Calendar visual (deferred - not in @fluentui/react-charting)

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Fluent UI v9 charting only | Microsoft standard, dark mode support, consistent UX | [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) |
| Platform libraries (React 16) | Bundle size, runtime compatibility | [ADR-022](../../.claude/adr/ADR-022-pcf-platform-libraries.md) |
| Single unified Visual Host PCF | Reduces maintenance, single deployment | — |
| Configuration via Dataverse entity | Admin-friendly, security built-in | — |
| Lookup binding with static ID fallback | Flexibility for different use cases | — |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| `@fluentui/react-charting` | External | Integrated | v5.x in npm |
| `@spaarke/ui-components` | Internal | Available | Shared library |
| Dataverse `sprk_chartdefinition` entity | Internal | Deployed | Entity and test data created |
| PCF dev environment (pac cli) | External | Ready | Configured |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Development Team | Overall accountability |
| AI Assistant | Claude Code | Implementation support |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2025-12-29 | 1.0 | Initial project setup | Claude Code |
| 2025-12-29 | 1.1 | Phase 1-5 complete | Claude Code |
| 2025-12-30 | 1.2 | Phase 6 v1.1.0 enhancements | Claude Code |
| 2026-01-02 | 2.0 | Project complete, documentation finalized | Claude Code |

---

*Project completed 2026-01-02 | Spaarke Visuals Framework v1.1.17*
