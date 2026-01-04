# Spaarke Visuals Framework - AI Context

> **Purpose**: This file provides context for Claude Code when working on visualization-module.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning
- **Last Updated**: 2025-12-29
- **Current Task**: Not started
- **Next Action**: Execute task 001 or run task-create to decompose plan

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - Original design specification (permanent reference)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan and WBS
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker (will be created by task-create)

### Project Metadata
- **Project Name**: visualization-module
- **Type**: PCF / Frontend
- **Complexity**: High (7 visual types, drill-through workspace, dark mode)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for design decisions, requirements, and acceptance criteria
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded automatically via adr-aware)

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## Key Technical Constraints

**Charting Library**:
- MUST use `@fluentui/react-charting` for Bar, Line, Area, Donut charts
- MUST NOT use Chart.js, Recharts, VisX, or D3 libraries
- Calendar and Card visuals use `@fluentui/react-components` v9 primitives

**PCF Control**:
- MUST build as PCF control, not legacy webresource - per ADR-006
- MUST use Dataset PCF pattern for drill-through grid - per ADR-011
- MUST declare platform-library in manifest (React 18, Fluent 9)
- MUST keep bundle under 5MB

**UI/Theming**:
- MUST use Fluent UI v9 exclusively (`@fluentui/react-components`) - per ADR-021
- MUST NOT use Fluent v8 (`@fluentui/react`)
- MUST NOT hard-code colors (use Fluent tokens)
- MUST support light, dark, and high-contrast modes
- Chart colors follow Power App MDA theme

**Shared Library**:
- MUST import shared components via `@spaarke/ui-components` - per ADR-012
- MUST NOT hard-code Dataverse entity schemas

**Testing**:
- MUST achieve 80%+ test coverage on PCF controls
- MUST include Storybook stories for all components

---

## Supported Entities (Phase 1)

Charts can be created for these entities:
- `sprk_project`
- `sprk_matter`
- `sprk_document`
- `sprk_invoice`
- `sprk_event`
- `email`

---

## Visual Types

| Type | Implementation | Library |
|------|----------------|---------|
| Metric Card | Custom component | `@fluentui/react-components` |
| Bar Chart | `VerticalBarChart` / `HorizontalBarChart` | `@fluentui/react-charting` |
| Line Chart | `LineChart` | `@fluentui/react-charting` |
| Area Chart | `AreaChart` | `@fluentui/react-charting` |
| Donut Chart | `DonutChart` | `@fluentui/react-charting` |
| Status Distribution Bar | Custom stacked bar | `@fluentui/react-charting` |
| Calendar | Custom component | `@fluentui/react-components` |
| Mini Table | Custom Top-N list | `@fluentui/react-components` |

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Who -->

| Date | Decision | Rationale | Who |
|------|----------|-----------|-----|
| 2025-12-29 | Single unified Visual Host PCF | Reduces deployment complexity, single configuration table | Design |
| 2025-12-29 | Calendar may need separate control | Not in @fluentui/react-charting | Design |
| 2025-12-29 | sprk_optionsjson schema defined during implementation | Not pre-documented, needs per-visual-type flexibility | Design |

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

### Fluent UI v9 Charting Notes
- Package: `@fluentui/react-charting`
- GitHub: https://github.com/microsoft/fluentui/tree/master/packages/charts/react-charting
- Storybook: https://storybooks.fluentui.dev/react/?path=/docs/charts_introduction--docs

### PCF Version Footer Requirement
Every PCF control MUST display a version footer - see `src/client/pcf/CLAUDE.md` for pattern.

---

## Resources

### Applicable ADRs
- [ADR-006](../../.claude/adr/ADR-006-pcf-over-webresources.md) - PCF over WebResources
- [ADR-011](../../.claude/adr/ADR-011-dataset-pcf.md) - Dataset PCF over Subgrids
- [ADR-012](../../.claude/adr/ADR-012-shared-components.md) - Shared Component Library
- [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) - Fluent UI v9 Design System

### PCF Patterns
- [Control Initialization](../../.claude/patterns/pcf/control-initialization.md)
- [Theme Management](../../.claude/patterns/pcf/theme-management.md)
- [Dataverse Queries](../../.claude/patterns/pcf/dataverse-queries.md)
- [Dialog Patterns](../../.claude/patterns/pcf/dialog-patterns.md)
- [Error Handling](../../.claude/patterns/pcf/error-handling.md)

### Existing PCF Controls (Reference)
- `src/client/pcf/UniversalDatasetGrid/` - Dataset PCF pattern
- `src/client/pcf/AnalysisWorkspace/` - Two-panel layout
- `src/client/pcf/ThemeEnforcer/` - Theme management

### Scripts
- `scripts/Deploy-PCFWebResources.ps1` - Deploy PCF controls
- `scripts/Deploy-CustomPage.ps1` - Deploy Custom Pages

### External Documentation
- [Fluent UI React Charting](https://github.com/microsoft/fluentui/tree/master/packages/charts/react-charting)
- [Fluent UI v9 Storybook](https://storybooks.fluentui.dev/react/)
- [PCF Framework Docs](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/overview)

---

*This file should be kept updated throughout project lifecycle*
