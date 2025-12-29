# Spaarke Visuals Framework – Design Specification (Fluent v9 Charting Update)

## Purpose
Spaarke Visuals is a lightweight, configuration-driven visualization framework for **operational, in-app visuals** within Spaarke’s Model-Driven Apps and Custom Pages. It provides **cards, charts, calendars, and drill-down tables** tied directly to Dataverse views and security.

This framework is **not** a BI or analytics system—Power BI/Fabric serve those needs. Instead, Spaarke Visuals replaces legacy Power Apps charts with a **modern, Fluent v9–aligned visualization layer** optimized for legal operations workflows.

The system is **configuration-driven**, **Dataverse-first**, and extensible for future user-configurable dashboards and advanced aggregation frameworks.

---

# Core Concepts

- **Configuration-driven** via Dataverse table `sprk_chartdefinition`.
- **Unified Visual Host PCF** renders any visual type based on configuration.
- **Fluent UI v9 Charting Library** is the primary visualization engine.
- **Dataverse queries** (Views, FetchXML, Web API) provide the data layer.
- **Universal drill-down** opens side-pane grids filtered to underlying records.
- **Calendar visual** built with Fluent v9 layout primitives.
- **Future compatibility** with optional BFF aggregation and external surfaces.

---

# Visual Types (v1 Catalog)

- Metric Card  
- Bar / Column Chart  
- Line / Area Chart  
- Donut Chart  
- Status Distribution Bar  
- Task / Deadline Calendar  
- Mini Table (Top-N)

All chart visuals are backed by **Fluent UI v9 charting components** (`@fluentui/react-charting`). The Calendar and Card visuals are custom components built with Fluent v9 primitives.

---

# Binding to Views/Subgrids and Security Model

This section clarifies **how visuals are bound to data**, **how security is enforced**, and **how charts are created and governed over time**.

## Binding Model: Views First

### Primary Binding Mechanism
Each visual (`sprk_chartdefinition`) is bound to data through:

- `sprk_entitylogicalname`
- `sprk_baseviewid` (SavedQuery or UserQuery)

This view:
- Defines the baseline FetchXML (filters, joins, sorting).
- May already be used by a **subgrid** on a form.
- Serves as the canonical query for both:
  - Subgrid display
  - Visual aggregation (card/chart/calendar)

A Spaarke visual is therefore **associated to a Dataverse view**, not directly to a subgrid control.

### Custom FetchXML (Admin-Only)
Custom FetchXML is supported in a controlled manner:

- Preferred: create a system view with the desired FetchXML.
- Optional: admin-only FetchXML override stored in `sprk_optionsjson.fetchXmlOverride`.

End users cannot author arbitrary FetchXML.

---

## Security Model

### Data Security
All visuals execute in the **current user’s Dataverse context**:

- Row-level security, BU/team rules, and sharing apply automatically.
- Field-level security is enforced.
- Aggregates and counts only reflect records the user can access.

No additional custom security logic is required in the Visual Host PCF.

### Visual Definition Security
Creation and editing of visuals is governed by:

- Dataverse security roles on `sprk_chartdefinition`.
- Ownership model (organization-owned vs user-owned).
- Access to the Visual Configurator app.

---

## Chart Creation Governance

### Phase 1 – Admin-Defined Visuals (Initial Model)

- `sprk_chartdefinition` is organization-owned.
- Only admin roles can create or edit visuals.
- End users consume visuals but cannot modify them.

**Admin Workflow**
1. Create a new `sprk_chartdefinition`.
2. Select entity and base view.
3. Choose visual type and aggregation.
4. Place the Visual Host PCF on a form, dashboard, or custom page.

---

### Phase 2 – Personal/User-Defined Visuals (Optional)

Two supported patterns:

#### Option A: Personal Chart Definitions
- User-owned chart definitions.
- Users select from allowed entities and views.
- Users can edit only their own charts.

#### Option B: Guided Chart Builder
- Custom Page wizard with constrained choices.
- Writes validated definitions to Dataverse.
- Recommended for broader user adoption.

All personal visuals remain fully Dataverse security-trimmed.

---

## Drill-Down to Underlying Data (Confirmed)

**Yes—drill-down is a first-class capability for all visuals.**

### Drill-Down Behavior
- Card → filtered list of records.
- Chart segment → records represented by that segment.
- Calendar day → records associated with that date.

### Technical Contract

```ts
type DrillDownTarget = {
  entityLogicalName: string;
  baseViewId?: string;
  additionalFilters: Array<{
    field: string;
    operator: string;
    value: any;
  }>;
  label?: string;
};
```

### Execution
1. Visual emits `DrillDownTarget`.
2. Visual Host PCF opens side-pane Custom Page.
3. Drill-down page renders a Dataset PCF or Fluent grid.
4. Only records the user is authorized to see are displayed.

---

# Summary

- Visuals bind to **Dataverse views**, not subgrid controls.
- **Dataverse security** governs all data access.
- **Drill-down to underlying data is fully supported**.
- Phase 1 emphasizes admin-defined visuals.
- Phase 2 optionally enables personal charts with guardrails.
