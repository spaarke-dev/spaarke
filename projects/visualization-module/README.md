# Spaarke Visuals Framework

> **Last Updated**: 2025-12-29
>
> **Status**: In Progress

## Overview

Spaarke Visuals is a configuration-driven visualization framework delivering operational, in-app visuals (cards, charts, calendars, drill-through workspaces) within Model-Driven Apps and Custom Pages. It replaces legacy Power Apps charts with a modern, Fluent v9-aligned visualization layer optimized for legal operations workflows.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan with WBS |
| [Spec](./spec.md) | AI-optimized design specification |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown and status |
| [AI Context](./CLAUDE.md) | Project-specific AI instructions |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Planning |
| **Progress** | 0% |
| **Target Date** | TBD |
| **Completed Date** | — |
| **Owner** | Development Team |

## Problem Statement

Legacy Power Apps charts in Spaarke's Model-Driven Apps are limited in functionality, lack modern UX patterns, and don't support drill-through interactions. Users need visuals that show aggregated data and allow them to quickly navigate to the underlying records without leaving their current context.

## Solution Summary

Build a configuration-driven visualization framework using Fluent UI v9 charting components. A unified Visual Host PCF control reads configuration from `sprk_chartdefinition` records and renders the appropriate visual type. Each visual supports a drill-through workspace pattern where users can click chart elements to filter an adjacent dataset grid in real-time.

## Graduation Criteria

The project is considered **complete** when:

- [ ] Visual Host PCF renders all 7 visual types (Metric Card, Bar, Line, Area, Donut, Status Bar, Calendar, Mini Table)
- [ ] All chart visuals use `@fluentui/react-charting` exclusively (no Chart.js, D3, etc.)
- [ ] Drill-through workspace opens as expanded modal with chart + filtered dataset grid
- [ ] Interactive filtering works (click chart segment → grid filters in real-time)
- [ ] Dataverse security enforced automatically (row-level, field-level, BU/team)
- [ ] Dark mode and high-contrast themes work correctly (no hard-coded colors)
- [ ] PCF bundle under 5MB
- [ ] 80%+ test coverage on PCF controls
- [ ] Storybook stories for all chart components
- [ ] Admin can create/edit chart definitions via Model-driven app

## Scope

### In Scope

- Visual Host PCF (unified control for all visual types)
- 7 visual types: Metric Card, Bar/Column, Line/Area, Donut, Status Distribution Bar, Calendar, Mini Table
- Fluent UI v9 charting integration (`@fluentui/react-charting`)
- Drill-through visual workspace (expanded modal pattern)
- Dataverse view binding via `sprk_baseviewid`
- Interactive filtering (chart → dataset grid)
- Shared chart components in `@spaarke/ui-components`
- Phase 1 admin governance (`sprk_chartdefinition` entity)
- Dark mode and high-contrast theme support
- Support for 6 entities: `sprk_project`, `sprk_matter`, `sprk_document`, `sprk_invoice`, `sprk_event`, `email`

### Out of Scope

- BI/Analytics (Power BI/Fabric serves this need)
- Chart.js, Recharts, VisX, or D3 libraries
- Fluent v8 APIs
- End-user FetchXML authoring
- Phase 2 personal/user-defined visuals
- BFF aggregation endpoints
- External surfaces (Power Pages, add-ins)

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Fluent UI v9 charting only | Microsoft standard, dark mode support, consistent UX | [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) |
| Single unified Visual Host PCF | Reduces maintenance, single deployment | — |
| Calendar may need separate control | Not in @fluentui/react-charting library | — |
| Configuration via Dataverse entity | Admin-friendly, security built-in | — |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Calendar not in Fluent charting | Medium | High | Build with Fluent v9 primitives if needed |
| Bundle size exceeds 5MB | High | Medium | Use platform-library declarations, tree-shaking |
| Performance with large datasets | Medium | Medium | Implement virtualization, pagination |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| `@fluentui/react-charting` | External | Available | v5.x in npm |
| `@spaarke/ui-components` | Internal | Available | Shared library |
| Dataverse `sprk_chartdefinition` entity | Internal | To Create | Part of project scope |
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

---

*Template version: 1.0 | Based on Spaarke development lifecycle*
