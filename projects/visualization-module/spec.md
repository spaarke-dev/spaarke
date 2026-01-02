# Spaarke Visuals Framework - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2025-12-29
> **Source**: Spaarke_Visuals_Design_Spec_Fluent_v9_v3.md

## Executive Summary

Spaarke Visuals is a configuration-driven visualization framework that delivers operational, in-app visuals (cards, charts, calendars, drill-through workspaces) within Model-Driven Apps and Custom Pages. It replaces legacy Power Apps charts with a modern, Fluent v9-aligned visualization layer optimized for legal operations workflows. The system is Dataverse-first, configuration-driven via `sprk_chartdefinition`, and designed for AI-assisted implementation.

---

## Scope

### In Scope

- **Visual Host PCF**: Single unified PCF control that renders any visual type based on `sprk_chartdefinition` configuration (Calendar may require separate control if not in Fluent charting library)
- **7 Visual Types**:
  - Metric Card
  - Bar / Column Chart
  - Line / Area Chart
  - Donut Chart
  - Status Distribution Bar
  - Task / Deadline Calendar
  - Mini Table (Top-N)
- **Fluent UI v9 Charting Integration**: All chart visuals use `@fluentui/react-charting`
- **Drill-Through Visual Workspace**: Expanded modal pattern with chart + dataset side-by-side
- **Dataverse View Binding**: Visuals bind to `sprk_baseviewid` (SavedQuery or UserQuery)
- **Interactive Filtering**: Click chart element → filter dataset grid in real-time
- **Shared Component Library**: Reusable chart components in `@spaarke/ui-components`
- **Phase 1 Admin Governance**: Organization-owned `sprk_chartdefinition` with admin-only CRUD
- **Theme Integration**: Chart colors follow Power App MDA theme; support light, dark, and high-contrast modes
- **Supported Entities (Phase 1)**: `sprk_project`, `sprk_matter`, `sprk_document`, `sprk_invoice`, `sprk_event`, `email`

### Out of Scope

- BI/Analytics (Power BI/Fabric serves this need)
- Chart.js, Recharts, VisX, or D3 libraries
- Fluent v8 APIs
- End-user FetchXML authoring
- Phase 2 personal/user-defined visuals (optional future phase)
- BFF aggregation endpoints (future compatibility noted, not implemented)
- External surfaces (Power Pages, add-ins) for charts

### Affected Areas

- `src/client/pcf/` - New Visual Host PCF and chart components
- `src/client/shared/Spaarke.UI.Components/` - Shared chart components
- `src/solutions/` - Dataverse solution with `sprk_chartdefinition` entity
- Model-driven app forms - Visual Host PCF placement
- Custom Pages - New drill-through workspace Custom Page (to be created)

---

## Requirements

### Functional Requirements

1. **FR-01**: Visual Host PCF renders any configured visual type
   - Acceptance: Given a `sprk_chartdefinition` record, the PCF renders the correct visual type

2. **FR-02**: Chart components use Fluent UI v9 React Charting library
   - Acceptance: All Bar, Line, Area, Donut charts render using `@fluentui/react-charting`

3. **FR-03**: Visuals bind to Dataverse views via `sprk_baseviewid`
   - Acceptance: Visual data comes from the referenced SavedQuery/UserQuery FetchXML

4. **FR-04**: Drill-Through Workspace opens as expanded modal
   - Acceptance: Chart toolbar "Expand/View Details" opens Custom Page with chart + dataset

5. **FR-05**: Interactive filtering updates dataset grid in real-time
   - Acceptance: Clicking a chart segment filters the adjacent dataset grid without navigation

6. **FR-06**: Calendar visual displays tasks/deadlines by date
   - Acceptance: Calendar built with Fluent v9 primitives showing date-based records

7. **FR-07**: Metric Card displays single aggregate value with optional trend
   - Acceptance: Card shows count/sum/avg with optional comparison indicator

8. **FR-08**: `sprk_chartdefinition` stores complete visual configuration
   - Acceptance: Entity stores visual type, entity, view, aggregation, options JSON

9. **FR-09**: Admin workflow for chart creation via Model-driven app
   - Acceptance: Admin can create/edit `sprk_chartdefinition` records

10. **FR-10**: Visual supports reset/clear selection action
    - Acceptance: User can clear chart selection to reset dataset to unfiltered state

### Non-Functional Requirements

- **NFR-01**: Security - All visuals execute in current user's Dataverse context (row-level, field-level, BU/team security automatic)
- **NFR-02**: Performance - Chart renders within 2 seconds for datasets under 1000 records
- **NFR-03**: Accessibility - WCAG 2.1 AA compliance; keyboard navigation; screen reader support
- **NFR-04**: Theming - Chart colors follow Power App MDA theme; support light, dark, and high-contrast modes via Fluent tokens
- **NFR-05**: Bundle Size - PCF control bundle under 5MB
- **NFR-06**: Test Coverage - 80%+ test coverage on PCF controls; 90%+ on shared components
- **NFR-07**: Documentation - Storybook stories for all chart components

---

## Technical Constraints

### Applicable ADRs

| ADR | Summary | Key Constraint |
|-----|---------|----------------|
| **ADR-006** | PCF over WebResources | MUST build new UI as PCF; MUST NOT create legacy webresources |
| **ADR-011** | Dataset PCF over Subgrids | MUST use Dataset PCF for drill-through grid; MUST include Storybook stories |
| **ADR-012** | Shared Component Library | MUST use `@spaarke/ui-components`; MUST NOT hard-code entity schemas |
| **ADR-021** | Fluent UI v9 Design System | MUST use `@fluentui/react-components` v9; MUST NOT use Fluent v8 or hard-coded colors |

### MUST Rules (from ADRs)

- ✅ MUST use `@fluentui/react-charting` for all chart visuals
- ✅ MUST use `@fluentui/react-components` v9 for Calendar and Card
- ✅ MUST wrap all UI in `FluentProvider` with theme
- ✅ MUST use Fluent design tokens (no hard-coded colors)
- ✅ MUST support light, dark, and high-contrast modes
- ✅ MUST use React ^18.2.0 (not React 19)
- ✅ MUST import shared components via `@spaarke/ui-components`
- ✅ MUST place PCF controls in `src/client/pcf/`
- ✅ MUST achieve 80%+ test coverage on PCF controls
- ✅ MUST include Storybook stories for components
- ✅ MUST declare `platform-library` in PCF manifests
- ✅ MUST keep PCF bundle under 5MB

### MUST NOT Rules

- ❌ MUST NOT use Chart.js, Recharts, VisX, or D3 libraries
- ❌ MUST NOT use Fluent v8 (`@fluentui/react`)
- ❌ MUST NOT hard-code colors (hex, rgb, named colors)
- ❌ MUST NOT bundle React/Fluent in PCF artifacts
- ❌ MUST NOT use React 19.x in PCF controls
- ❌ MUST NOT hard-code Dataverse entity names or schemas
- ❌ MUST NOT create legacy JavaScript webresources

### Existing Patterns to Follow

- See `src/client/pcf/UniversalDatasetGrid/` for Dataset PCF pattern
- See `.claude/patterns/pcf/control-initialization.md` for PCF initialization
- See `.claude/patterns/pcf/react-hooks.md` for React hooks in PCF
- See `src/client/shared/Spaarke.UI.Components/` for shared component structure

### Technology References

| Technology | Package | Documentation |
|------------|---------|---------------|
| Fluent UI v9 Charting | `@fluentui/react-charting` | [GitHub](https://github.com/microsoft/fluentui/tree/master/packages/charts/react-charting) |
| Fluent UI v9 Components | `@fluentui/react-components` | [Storybook](https://storybooks.fluentui.dev/react/) |
| Fluent Icons | `@fluentui/react-icons` | [Icon Search](https://react.fluentui.dev/?path=/docs/icons-catalog--docs) |

---

## Data Model

### sprk_chartdefinition Entity

| Field | Type | Description |
|-------|------|-------------|
| `sprk_chartdefinitionid` | GUID | Primary key |
| `sprk_name` | String | Display name |
| `sprk_visualtype` | OptionSet | metriccard, bar, line, area, donut, statusbar, calendar, minitable |
| `sprk_entitylogicalname` | String | Target entity logical name |
| `sprk_baseviewid` | Lookup/String | SavedQuery or UserQuery GUID |
| `sprk_aggregationfield` | String | Field to aggregate |
| `sprk_aggregationtype` | OptionSet | count, sum, avg, min, max |
| `sprk_groupbyfield` | String | Category/grouping field |
| `sprk_optionsjson` | Multiline | JSON for advanced options (colors, fetchXmlOverride, etc.) |

### DrillInteraction Contract

```typescript
type DrillInteraction = {
  field: string;
  operator: "eq" | "in" | "between";
  value: any;
  label?: string;
};
```

---

## Success Criteria

1. [ ] Visual Host PCF renders all 7 visual types based on configuration - Verify: Manual testing with each visual type
2. [ ] All chart visuals use `@fluentui/react-charting` - Verify: Code review, no alternative charting libraries
3. [ ] Drill-through workspace opens and filters dataset interactively - Verify: Click chart segment, observe grid filter
4. [ ] Dataverse security is enforced automatically - Verify: Different users see appropriate data
5. [ ] Dark mode and high-contrast work correctly - Verify: Toggle themes, verify no hard-coded colors
6. [ ] PCF bundle under 5MB - Verify: Build output size check
7. [ ] 80%+ test coverage on PCF controls - Verify: Coverage report
8. [ ] Storybook stories for all chart components - Verify: Storybook documentation complete
9. [ ] Admin can create/edit chart definitions - Verify: CRUD operations on `sprk_chartdefinition`

---

## Dependencies

### Prerequisites

- Dataverse solution with `sprk_chartdefinition` entity deployed
- `@spaarke/ui-components` shared library available
- PCF development environment configured (pac cli, npm)
- Fluent UI v9 packages installed

### External Dependencies

- `@fluentui/react-charting` - Microsoft Fluent UI charting library
- `@fluentui/react-components` - Microsoft Fluent UI v9 components
- Power Platform PCF framework
- Dataverse Web API for data queries

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                    Model-Driven App                      │
├─────────────────────────────────────────────────────────┤
│  Form / Dashboard / Custom Page                          │
│  ┌─────────────────────────────────────────────────────┐│
│  │              Visual Host PCF                         ││
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌───────────┐ ││
│  │  │ Metric  │ │  Bar    │ │ Donut   │ │ Calendar  │ ││
│  │  │  Card   │ │ Chart   │ │ Chart   │ │  Visual   │ ││
│  │  └─────────┘ └─────────┘ └─────────┘ └───────────┘ ││
│  └─────────────────────────────────────────────────────┘│
├─────────────────────────────────────────────────────────┤
│  Drill-Through Workspace (Custom Page Modal)             │
│  ┌───────────────┬──────────────────────────────────────┐│
│  │   Chart       │   Dataset Grid (Filtered)            ││
│  │   (1/3)       │   (2/3)                              ││
│  └───────────────┴──────────────────────────────────────┘│
├─────────────────────────────────────────────────────────┤
│                   Dataverse                              │
│  ┌─────────────────┐  ┌────────────────────────────────┐│
│  │sprk_chartdefn   │  │ Entity Views (SavedQuery)      ││
│  │  - visualtype   │  │  - FetchXML                    ││
│  │  - baseviewid   │  │  - Security Context            ││
│  └─────────────────┘  └────────────────────────────────┘│
└─────────────────────────────────────────────────────────┘
```

---

## Clarifications (Resolved)

| Question | Resolution |
|----------|------------|
| **Q1: Single vs multiple PCF controls** | Single unified Visual Host PCF for all visual types. Exception: Calendar may require separate control if not available in `@fluentui/react-charting` library. |
| **Q2: sprk_optionsjson schema** | Not pre-documented. Define schema during implementation based on per-visual-type requirements. |
| **Q3: Supported entities (Phase 1)** | `sprk_project`, `sprk_matter`, `sprk_document`, `sprk_invoice`, `sprk_event`, `email` |
| **Q4: Drill-through Custom Page** | Create new Custom Page for drill-through workspace (no existing template). |
| **Q5: Chart colors** | Follow selected Power App Model-Driven App theme. Must support dark mode. No custom per-chart color configuration in Phase 1. |

---

*AI-optimized specification. Original design: Spaarke_Visuals_Design_Spec_Fluent_v9_v3.md*
