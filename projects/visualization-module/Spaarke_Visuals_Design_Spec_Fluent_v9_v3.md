# Spaarke Visuals Framework – Design Specification (Fluent v9 Charting Update)

## Purpose
Spaarke Visuals is a lightweight, configuration-driven visualization framework for **operational, in-app visuals** within Spaarke’s Model-Driven Apps and Custom Pages. It provides **cards, charts, calendars, and drill-through workspaces** tied directly to Dataverse views and security.

This framework is **not** a BI or analytics system—Power BI/Fabric serve those needs. Instead, Spaarke Visuals replaces legacy Power Apps charts with a **modern, Fluent v9–aligned visualization layer** optimized for legal operations workflows.

The system is **configuration-driven**, **Dataverse-first**, and designed to be consumed directly by AI coding agents (Claude Code) for implementation.

---

# Core Concepts

- **Configuration-driven** via Dataverse table `sprk_chartdefinition`.
- **Unified Visual Host PCF** renders any visual type based on configuration.
- **Fluent UI v9 Charting Library** is the standard charting engine.
- **Dataverse queries** (Views, FetchXML, Web API) provide the data layer.
- **Drill-through workspace** (chart + dataset) is a first-class UX pattern.
- **Calendar visual** built with Fluent v9 layout primitives.
- **Future compatibility** with optional BFF aggregation and external surfaces.

---

# Visualization Technology Standard (MANDATORY)

All chart-based visuals MUST use **Microsoft Fluent UI React Charting (v9)**.

**Authoritative references (must be used by Claude Code):**
- GitHub (source of truth):
  https://github.com/microsoft/fluentui/tree/master/packages/charts/react-charting
- Storybook / Docs:
  https://storybooks.fluentui.dev/react/?path=/docs/charts_introduction--docs

**Rules**
- Use `@fluentui/react-charting` for Bar, Line, Area, Donut, and stacked charts.
- Do NOT introduce Chart.js, Recharts, VisX, or D3 unless explicitly instructed.
- Do NOT use Fluent v8 APIs.
- Calendar visuals are custom components built with `@fluentui/react-components` v9 primitives.

---

# Visual Types (v1 Catalog)

- Metric Card
- Bar / Column Chart
- Line / Area Chart
- Donut Chart
- Status Distribution Bar
- Task / Deadline Calendar
- Mini Table (Top-N)

All chart visuals are backed by **Fluent UI v9 charting components**. The Calendar and Card visuals are custom Fluent v9 components.

---

# Binding to Views/Subgrids and Security Model

## Binding Model: Views First

Each visual (`sprk_chartdefinition`) binds to:
- `sprk_entitylogicalname`
- `sprk_baseviewid` (SavedQuery or UserQuery)

The referenced view:
- Defines the baseline FetchXML.
- May also be used by a subgrid on a form.
- Serves as the canonical query for aggregation, drill-through, and dataset views.

Visuals bind to **Dataverse views**, not directly to subgrid controls.

### Custom FetchXML (Admin-Only)
- Preferred: encapsulate custom logic in a system view.
- Optional override: admin-only FetchXML stored in `sprk_optionsjson.fetchXmlOverride`.
- End users cannot author arbitrary FetchXML.

---

## Security Model

### Data Security
- All queries execute in the **current user’s Dataverse context**.
- Row-level security, BU/team rules, sharing, and field-level security apply automatically.
- Aggregates and visuals only reflect records the user can access.

### Visual Definition Security
- Governed via Dataverse roles and ownership of `sprk_chartdefinition`.
- Separate from data security.

---

# Chart Creation Governance

## Phase 1 – Admin-Defined Visuals (Initial)

- `sprk_chartdefinition` is organization-owned.
- Only admin roles can create or edit visuals.
- End users consume visuals but cannot modify them.

**Admin Workflow**
1. Create `sprk_chartdefinition`.
2. Select entity and base view.
3. Choose visual type, aggregation, and date range.
4. Place the Visual Host PCF on a form, dashboard, or custom page.

## Phase 2 – Personal/User-Defined Visuals (Optional)

Two supported patterns:

**Option A: Personal Chart Definitions**
- User-owned chart definitions.
- Users select from allowed entities and views.
- Users can edit only their own charts.

**Option B: Guided Chart Builder**
- Custom Page wizard with constrained choices.
- Writes validated definitions to Dataverse.
- Recommended if broad end-user charting is required.

All personal visuals remain fully Dataverse security-trimmed.

---

# Drill-Through Visual Workspace (Expanded Modal Pattern)

## Overview
Every Spaarke chart supports a **Drill-Through Visual Workspace** that enables users to move from insight to records without losing context.

- Main page shows a compact chart/card.
- Chart toolbar includes **Expand / View Details**.
- An **expanded modal Custom Page** opens.
- The workspace presents **chart + dataset side-by-side**.
- Chart interactions dynamically filter the dataset grid.

This pattern is mandatory for non-trivial charts.

---

## Layout

- **Left pane (≈ 1/3 width):** Interactive chart.
- **Right pane (≈ 2/3 width):** Dataset grid (records).

```
┌──────────────────────────────────────────────┐
│ Expanded Visual – <Title>                    │
├───────────────┬──────────────────────────────┤
│   Chart       │   Dataset Grid               │
│   (1/3)       │   (2/3)                      │
│               │   Records update live        │
│   Click bar   │   based on chart selection   │
└───────────────┴──────────────────────────────┘
```

---

## Interaction Model

### Initial State
- Chart loads aggregated data.
- Dataset grid loads using the same entity, base view, and date range.

### Interactive Filtering
- Clicking a chart element filters the dataset grid in place.
- No navigation occurs.

Examples:
- Bar → click bar → grid filters to that category.
- Donut → click slice → grid filters to that segment.
- Calendar → click day → grid filters to that date.

### Reset
- User can clear chart selection to reset the dataset.

---

## Drill Interaction Contract

```ts
type DrillInteraction = {
  field: string;
  operator: "eq" | "in" | "between";
  value: any;
  label?: string;
};
```

---

## Technical Implementation

- Implemented as a **Power Apps Custom Page** opened as an expanded modal.
- Left: `Spaarke<ChartType>` component.
- Right: Dataset PCF or Fluent grid.
- Shared state (React context) manages active filters.
- Dataset re-queries Dataverse on filter changes.
- Dataverse security is always enforced.

---

# Summary

- Visuals bind to **Dataverse views**, not subgrid controls.
- **Fluent UI v9 React Charting** is mandatory for charts.
- **Drill-through workspace** is a core UX pattern.
- Dataverse security governs all data access automatically.
- Phase 1 uses admin-defined visuals; Phase 2 optionally enables personal charts.
