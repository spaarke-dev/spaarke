# Visualization Framework R2

> **Status**: Ready for Implementation
> **Priority**: High
> **Created**: 2026-01-02
> **Updated**: 2026-02-08

---

## Executive Summary

This project enhances the VisualHost PCF control to support configuration-driven click actions and new visual types for displaying event due date cards. The work originated from the Events Workspace Apps UX R1 project's DueDateWidget requirements but is being implemented strategically as a framework enhancement.

## Background

### Origin: Events Workspace Apps UX R1

During the Events project, the DueDateWidget PCF required a visual refresh with a new card design. Analysis revealed that:

1. The card pattern should be reusable across the platform
2. VisualHost already provides a configuration-driven visualization framework
3. Integrating the card as a new visual type enables broader reuse
4. Configuration-driven click actions would benefit all visual types

### Decision: Integrate into VisualHost

Rather than maintaining DueDateWidget as a standalone PCF, we will:
- Add EventDueDateCard as a visual type in VisualHost
- Add configuration-driven click action support
- Use view-driven data fetching
- Deprecate hardcoded behavior in DueDateWidget

---

## Scope

### In Scope

| Feature | Description |
|---------|-------------|
| **Click Action Configuration** | Schema changes to `sprk_chartdefinition` for configurable click actions |
| **EventDueDateCard Component** | Shared UI component in `@spaarke/ui-components` |
| **Single Card Visual Type** | VisualHost visual bound to lookup field |
| **Card List Visual Type** | VisualHost visual driven by Dataverse view |
| **View-Driven Data Fetching** | Fetch data using view GUID + context filter |
| **"View List" Navigation** | Configurable navigation to entity tab |

### Out of Scope (Deferred)

| Feature | Reason |
|---------|--------|
| UniversalDatasetGrid React 16 fix | Will deploy as Custom Page (React 18), not PCF |
| UniversalSubgrid PCF | Future project when subgrid replacement needed |
| Calendar visual type | Existing, no changes needed |
| Chart visual types | Existing, no changes needed |

---

## Technical Approach

### React Version Strategy

| Component | Deployment | React Version |
|-----------|------------|---------------|
| VisualHost | PCF Standard Control | React 16 (platform library) |
| UniversalDatasetGrid | Custom Page | React 18 (bundled) |
| EventDueDateCard | Shared Library | React 16 compatible |

### Key Constraints

| ADR | Constraint |
|-----|------------|
| ADR-021 | Fluent UI v9 exclusively, design tokens only |
| ADR-022 | PCF controls use React 16 APIs (`ReactDOM.render`) |
| ADR-012 | Shared components via `@spaarke/ui-components` |

---

## Phases

| Phase | Description | Effort |
|-------|-------------|--------|
| **Phase 1** | Schema changes to `sprk_chartdefinition` | 4-6 hrs |
| **Phase 2** | Click Action Handler in VisualHostRoot | 6-8 hrs |
| **Phase 3** | EventDueDateCard shared component | 4-6 hrs |
| **Phase 4** | Due Date Card visual types in VisualHost | 6-8 hrs |
| **Phase 5** | View-driven data fetching | 4-6 hrs |
| **Phase 6** | Testing & deployment | 4-6 hrs |
| **Total** | | **28-40 hrs** |

---

## Schema Changes

### sprk_chartdefinition (Existing Entity)

New fields to add:

| Field | Type | Description |
|-------|------|-------------|
| `sprk_onclickaction` | Choice | Click action type |
| `sprk_onclicktarget` | Text | Target for click action |
| `sprk_onclickrecordfield` | Text | Field containing record ID |
| `sprk_viewid` | Text | Dataverse view GUID |
| `sprk_contextfieldname` | Text | Lookup field for context filtering |
| `sprk_viewlisttabname` | Text | Tab name for "View List" navigation |
| `sprk_maxdisplayitems` | Whole Number | Maximum items to display |

### Click Action Options

| Value | Description |
|-------|-------------|
| `none` | No click action |
| `openrecordform` | Open record's modal form |
| `opensidepane` | Open Custom Page in side pane |
| `navigatetopage` | Navigate to URL or Custom Page |
| `opendatasetgrid` | Open drill-through workspace |

---

## Dependencies

### Incoming (Blocked By)

None - this project can start immediately.

### Outgoing (Blocks)

| Project | Blocked Item |
|---------|--------------|
| events-workspace-apps-UX-r1 | DueDateWidget visual refresh |
| events-workspace-apps-UX-r1 | "View List" navigation |

---

## Quick Links

| Resource | Path |
|----------|------|
| Design Document | [design.md](design.md) |
| VisualHost Source | [../../src/client/pcf/VisualHost/](../../src/client/pcf/VisualHost/) |
| DueDateWidget Source | [../../src/client/pcf/DueDatesWidget/](../../src/client/pcf/DueDatesWidget/) |
| Shared Components | [../../src/client/shared/Spaarke.UI.Components/](../../src/client/shared/Spaarke.UI.Components/) |
| Card Mockup | [../events-workspace-apps-UX-r1/notes/due-date-widget-event-card.png](../events-workspace-apps-UX-r1/notes/due-date-widget-event-card.png) |
| Color Codes | [../events-workspace-apps-UX-r1/notes/event-type-color-codes.png](../events-workspace-apps-UX-r1/notes/event-type-color-codes.png) |

---

## Next Steps

1. ~~**Convert design.md to spec.md** using `/design-to-spec projects/visualization-module-r2`~~ ✅ Done
2. ~~**Run project-pipeline** to generate tasks~~ ✅ In Progress
3. **Execute Phase 1** (schema changes)
4. **Continue through phases** sequentially

## Graduation Criteria

Per [spec.md](spec.md):

- [ ] Click action configuration works for all visual types
- [ ] Single due date card visual displays correctly
- [ ] Card list visual fetches from view
- [ ] Context filtering works
- [ ] "View List" link navigates correctly
- [ ] Custom FetchXML executes
- [ ] PCF override takes precedence
- [ ] Parameter substitution works
- [ ] Dark mode support verified
- [ ] Backward compatibility maintained

---

## Historical Context

This project was originally scoped as "Universal Dataset Grid R2" focused on fixing React 18 → React 16 compatibility. That scope was revised on 2026-02-08 when it was determined that:

1. UniversalDatasetGrid will deploy as Custom Page (React 18 OK)
2. The Events project's DueDateWidget needs align better with VisualHost enhancement
3. The framework-level approach provides more value than isolated fixes

The original UniversalDatasetGrid React 16 fix is no longer needed for Custom Page deployment. If subgrid replacement is needed in the future, a separate "UniversalSubgrid" PCF project will be created.

---

*Created: 2026-01-02*
*Scope revised: 2026-02-08*
*Design document added: 2026-02-08*
