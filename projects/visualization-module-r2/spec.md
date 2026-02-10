# Visualization Framework R2 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-02-08
> **Source**: design.md (collaborative design session)

## Executive Summary

This project enhances the VisualHost PCF control to support configuration-driven click actions and new visual types for displaying event due date cards. The work originated from the Events Workspace Apps UX R1 project's DueDateWidget requirements but is being implemented strategically as a framework enhancement to benefit all visualization use cases across the platform.

## Scope

### In Scope

- Configuration-driven click actions for all VisualHost visual types
- New visual type: `duedatecard` (single card bound to lookup)
- New visual type: `duedatecardlist` (card list driven by view)
- EventDueDateCard shared component in `@spaarke/ui-components`
- View-driven data fetching with context filtering
- Custom FetchXML support with parameter substitution
- "View List" navigation link
- Schema changes to `sprk_chartdefinition` entity
- New PCF property `fetchXmlOverride`
- Migration path for DueDateWidget deprecation

### Out of Scope

- UniversalDatasetGrid React 16 fix (will deploy as Custom Page with React 18)
- UniversalSubgrid PCF (future project when subgrid replacement needed)
- Changes to existing chart visual types (bar, pie, line, etc.)
- Mobile-specific layouts
- Offline support

### Affected Areas

- `src/client/pcf/VisualHost/` - VisualHost PCF control enhancements
- `src/client/pcf/VisualHost/control/types/index.ts` - IChartDefinition interface
- `src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx` - Click action handler
- `src/client/pcf/VisualHost/control/components/ChartRenderer.tsx` - Visual type routing
- `src/client/pcf/VisualHost/control/services/ConfigurationLoader.ts` - Field loading
- `src/client/shared/Spaarke.UI.Components/src/components/EventDueDateCard/` - New shared component
- `src/client/shared/Spaarke.UI.Components/src/components/index.ts` - Component exports
- Dataverse: `sprk_chartdefinition` entity - New fields
- Dataverse: `sprk_visualtype` option set - New values

## Requirements

### Functional Requirements

1. **FR-01: Configuration-Driven Click Actions** - All visual types in VisualHost must support configurable click actions via `sprk_chartdefinition` fields.
   - Acceptance: Click on any visual item triggers the configured action (openrecordform, opensidepane, navigatetopage, opendatasetgrid, or none)

2. **FR-02: Single Due Date Card Visual** - A visual type (`duedatecard`) that displays a single event's due date card bound to a lookup field.
   - Acceptance: Displays date column (with event type color), event type + name, description, assigned to, and days-until-due badge

3. **FR-03: Due Date Card List Visual** - A visual type (`duedatecardlist`) that displays multiple event due date cards driven by a Dataverse view.
   - Acceptance: Fetches events using configured view, displays up to `sprk_maxdisplayitems` cards, includes "View List" link

4. **FR-04: View-Driven Data Fetching** - Card list visual fetches data using a Dataverse view with context filtering.
   - Acceptance: Retrieves view FetchXML, injects context filter for current record ID, executes query, maps to card props

5. **FR-05: "View List" Navigation** - Card list visual includes a configurable "View List" link.
   - Acceptance: Clicking link navigates to configured tab on current form (default) or entity list with view selected

6. **FR-06: Custom FetchXML Support** - Support custom FetchXML queries at Chart Definition and PCF deployment levels.
   - Acceptance: Query resolution follows priority: PCF override → Custom FetchXML → View → Direct entity query
   - Acceptance: Parameter substitution works for `{contextRecordId}`, `{currentUserId}`, `{currentDate}`, `{currentDateTime}`

### Non-Functional Requirements

- **NFR-01: React 16 Compatibility** - All components must use React 16 APIs per ADR-022 (`ReactDOM.render()`, no `createRoot()`)
- **NFR-02: Fluent UI v9 Compliance** - All UI must use Fluent UI v9 design tokens per ADR-021, support light/dark themes, meet WCAG 2.1 AA
- **NFR-03: Shared Component Reusability** - EventDueDateCard must be in `@spaarke/ui-components` with generic props interface, no PCF-specific dependencies

## Technical Constraints

### Applicable ADRs

- **ADR-006**: PCF controls only (no legacy webresources)
- **ADR-012**: Shared components via `@spaarke/ui-components`
- **ADR-021**: Fluent UI v9 exclusively, design tokens only, dark mode required
- **ADR-022**: React 16 APIs only for PCF controls (platform library constraint)

### MUST Rules

- ✅ MUST use `ReactDOM.render()` for PCF controls (not `createRoot()`)
- ✅ MUST use Fluent UI v9 design tokens (no hard-coded colors)
- ✅ MUST support both light and dark themes
- ✅ MUST place shared components in `@spaarke/ui-components`
- ✅ MUST maintain backward compatibility with existing VisualHost configurations
- ❌ MUST NOT use React 18 concurrent features in PCF controls
- ❌ MUST NOT hard-code entity names or field names in shared components

### Existing Patterns to Follow

- See `src/client/pcf/VisualHost/control/index.ts` for React 16 render pattern
- See `src/client/pcf/VisualHost/control/services/ConfigurationLoader.ts` for chart definition loading
- See `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/CardView.tsx` for card component pattern

## Schema Changes

### Existing Fields (Already in Dataverse)

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_baseviewid` | Text | View GUID for view-driven queries |
| `sprk_fetchxmlquery` | Multiline Text | Custom FetchXML query |
| `sprk_fetchxmlparams` | Multiline Text (JSON) | Parameter mappings |
| `sprk_optionsjson` | Multiline Text (JSON) | Visual-specific options |

### New Fields (To Be Created)

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_onclickaction` | Choice | Click action type (none=100000000, openrecordform=100000001, opensidepane=100000002, navigatetopage=100000003, opendatasetgrid=100000004) |
| `sprk_onclicktarget` | Text (200) | Target for click action |
| `sprk_onclickrecordfield` | Text (100) | Field containing record ID |
| `sprk_contextfieldname` | Text (100) | Lookup field for context filtering |
| `sprk_viewlisttabname` | Text (100) | Tab name for "View List" navigation |
| `sprk_maxdisplayitems` | Whole Number | Maximum items to display (default 10) |

### New Option Set Values

Add to `sprk_visualtype` option set:
- `DueDateCard` = 100000008
- `DueDateCardList` = 100000009

### New PCF Property

| Property | Type | Purpose |
|----------|------|---------|
| `fetchXmlOverride` | SingleLine.Text | Per-deployment FetchXML override |

## Success Criteria

1. [ ] Click action configuration works for all visual types - Verify: Configure `openrecordform` on existing chart, click opens record form
2. [ ] Single due date card visual displays correctly - Verify: Create chart definition with `duedatecard` type, bind to event lookup, card renders with all fields
3. [ ] Card list visual fetches from view - Verify: Create chart definition with `duedatecardlist` type, configure view GUID, cards display filtered data
4. [ ] Context filtering works - Verify: Place card list on Matter form, only related events display
5. [ ] "View List" link navigates correctly - Verify: Click link, navigates to Events tab on current form
6. [ ] Custom FetchXML executes - Verify: Configure custom FetchXML with parameters, data loads correctly
7. [ ] PCF override takes precedence - Verify: Configure both Chart Definition FetchXML and PCF override, override query executes
8. [ ] Parameter substitution works - Verify: Use `{contextRecordId}` in FetchXML, correct record ID substituted
9. [ ] Dark mode support - Verify: Switch to dark theme, all card elements render correctly
10. [ ] Backward compatibility - Verify: Existing VisualHost configurations continue to work unchanged

## Dependencies

### Prerequisites

- VisualHost PCF v1.1.17 (current working version)
- `sprk_chartdefinition` entity exists in Dataverse
- `@spaarke/ui-components` shared library configured

### External Dependencies

- Dataverse environment for schema changes
- Power Apps maker portal for option set updates

## Phases

| Phase | Scope | Estimated Effort |
|-------|-------|------------------|
| **Phase 1** | Schema changes to sprk_chartdefinition (click action fields, option set values) | 4-6 hrs |
| **Phase 2** | Click Action Handler in VisualHostRoot | 6-8 hrs |
| **Phase 3** | EventDueDateCard shared component | 4-6 hrs |
| **Phase 4** | Due Date Card visual types in VisualHost | 6-8 hrs |
| **Phase 5** | Advanced Query Support (View + Custom FetchXML + PCF Override) | 4-6 hrs |
| **Phase 6** | Testing & deployment | 4-6 hrs |
| **Total** | | **28-40 hrs** |

## Owner Clarifications

*Answers captured during design-to-spec discussion (2026-02-08):*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Event Type Color | Where does event type color come from? | `sprk_eventtypecolor` choice field on Event Type entity | Expand lookup in OData query |
| "View List" Target | Where should "View List" link navigate? | Default to entity tab on current form, configurable via property | Added `sprk_viewlisttabname` field |
| Max Items | How many cards to display? | Property with default of 10, "View List" shows full set | Added `sprk_maxdisplayitems` field |
| Custom FetchXML | How to support advanced queries? | Use existing `sprk_fetchxmlquery` + `sprk_fetchxmlparams` fields, add PCF override | No new schema for FetchXML |
| Query Priority | Which query source takes precedence? | PCF override → Custom FetchXML → View → Direct entity | Documented in code |
| Context Filtering | How to filter by current record? | PCF injects filter dynamically, views don't need placeholders | Added `sprk_contextfieldname` field |

## Assumptions

*Proceeding with these assumptions:*

- **Click action default**: If `sprk_onclickaction` is not set, existing drill-through behavior applies (backward compatible)
- **Event Type entity**: `sprk_eventtype` entity has `sprk_eventtypecolor` choice field with values 1-5 mapped to hex colors
- **View security**: Views used for card list are accessible by current user (no additional security filtering)

## Unresolved Questions

*None - all design questions resolved during collaborative discussion.*

## Migration Path

### DueDateWidget Deprecation

1. Create Chart Definition records for existing DueDateWidget placements
2. Replace DueDateWidget PCF with VisualHost on forms
3. Configure click action as `openrecordform` targeting `sprk_event`
4. Deprecate DueDateWidget PCF (keep deployed for backward compatibility)

### Backward Compatibility

- Existing VisualHost configurations continue to work unchanged
- Click action defaults to existing behavior if not configured
- DueDateWidget can remain deployed during transition period

---

*AI-optimized specification. Original design: design.md*
*Generated: 2026-02-08*
