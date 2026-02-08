# Visualization Framework R2 - Design Document

> **Created**: 2026-02-08
> **Status**: Design Draft - Pending Spec Conversion
> **Author**: Claude Code + User Collaboration

---

## Executive Summary

This project enhances the VisualHost PCF control to support configuration-driven click actions and new visual types for displaying event due date cards. The work originated from the Events Workspace Apps UX R1 project's DueDateWidget requirements but is being implemented strategically as a framework enhancement to benefit all visualization use cases.

---

## Background & Context

### Origin: DueDateWidget Enhancement

The Events Workspace Apps UX R1 project identified the need for a reusable "Event Due Date Card" component. During design discussions, we determined that:

1. The card pattern should be reusable across the platform, not isolated to the DueDateWidget
2. The VisualHost PCF already provides a configuration-driven visualization framework
3. Adding the card as a new visual type in VisualHost enables broader reuse
4. Configuration-driven click actions would benefit all visual types, not just cards

### Architecture Decision: Integrate into VisualHost

Rather than creating a standalone component, we will:
- Add EventDueDateCard as a visual type in VisualHost
- Add configuration-driven click action support to all visuals
- Use view-driven data fetching for card lists
- Remove hardcoded behavior from DueDateWidget

---

## Current State

### VisualHost PCF (v1.1.17)
- **Status**: Working, React 16 compliant
- **Visual Types**: bar, pie, line, donut, calendar
- **Click Actions**: Hardcoded drill-through to Custom Page
- **Data Source**: Entity + aggregation configuration

### DueDateWidget PCF (v1.0.8)
- **Status**: Working, React 16 compliant
- **Purpose**: Display event due dates on Matter/Project forms
- **Click Action**: Opens Event side pane (hardcoded)
- **Data Source**: WebAPI query with hardcoded filters

---

## Requirements

### Functional Requirements

#### FR-01: Configuration-Driven Click Actions
All visual types in VisualHost must support configurable click actions:
- **openrecordform**: Open the clicked record's modal form
- **opensidepane**: Open a Custom Page in side pane
- **navigatetopage**: Navigate to a URL or Custom Page
- **opendatasetgrid**: Open drill-through workspace (existing behavior)
- **none**: No click action

#### FR-02: Single Due Date Card Visual
A visual type that displays a single event's due date card:
- Bound to a lookup field on the parent entity
- Displays: date, event type (with color), event name, description, assigned to, days until due
- Click action configurable via chart definition

#### FR-03: Due Date Card List Visual
A visual type that displays multiple event due date cards:
- Driven by a Dataverse view (view GUID property)
- Filters by current record context (context field name property)
- Includes "View List" link to navigate to full list
- Click action configurable via chart definition

#### FR-04: View-Driven Data Fetching
Card list visual must fetch data using a Dataverse view:
- Accept view GUID as PCF property
- Fetch view definition to get FetchXML
- Inject context filter dynamically (current record ID)
- Execute query and map results to card props

#### FR-05: "View List" Navigation
Card list visual must include a "View List" link:
- Configurable target (tab name or entity list)
- Default: Navigate to related entity tab on current form
- Pass view context to target

#### FR-06: Custom FetchXML Support
For sophisticated queries where standard views are insufficient:
- **Chart Definition level**: Use `sprk_fetchxmlquery` field for custom FetchXML
- **PCF deployment level**: Use `fetchXmlOverride` property for per-form overrides
- Support parameter substitution with common placeholders
- Query resolution priority: PCF override → Custom FetchXML → View → Direct entity query

### Non-Functional Requirements

#### NFR-01: React 16 Compatibility
All components must use React 16 APIs per ADR-022:
- Use `ReactDOM.render()` not `createRoot()`
- No React 18 concurrent features
- Compatible with Dataverse platform libraries

#### NFR-02: Fluent UI v9 Compliance
All UI must use Fluent UI v9 per ADR-021:
- Design tokens only, no hardcoded colors
- Support light and dark themes
- Accessible (WCAG 2.1 AA)

#### NFR-03: Shared Component Reusability
EventDueDateCard component must be reusable:
- Placed in `@spaarke/ui-components` shared library
- Generic props interface
- No PCF-specific dependencies in the component itself

---

## Technical Design

### Schema Changes: sprk_chartdefinition

#### Existing Fields (Already Created)

These fields already exist in the Chart Definition entity and will be leveraged:

| Field | Type | Description |
|-------|------|-------------|
| `sprk_baseviewid` | Text | Dataverse view GUID for view-driven queries |
| `sprk_fetchxmlquery` | Multiline Text | Custom FetchXML query for advanced scenarios |
| `sprk_fetchxmlparams` | Multiline Text (JSON) | Parameter mappings for FetchXML substitution |
| `sprk_optionsjson` | Multiline Text (JSON) | Visual-specific options (colors, labels, etc.) |

#### New Fields (To Be Created)

| Field | Type | Description |
|-------|------|-------------|
| `sprk_onclickaction` | Choice | Click action type (none, openrecordform, opensidepane, navigatetopage, opendatasetgrid) |
| `sprk_onclicktarget` | Text | Target for click action (entity name, page name, URL) |
| `sprk_onclickrecordfield` | Text | Field containing the record ID to use for click action |
| `sprk_contextfieldname` | Text | Lookup field for context filtering (e.g., `_sprk_matterid_value`) |
| `sprk_viewlisttabname` | Text | Tab name for "View List" navigation |
| `sprk_maxdisplayitems` | Whole Number | Maximum items to display (default 10) |

#### New PCF Properties

| Property | Type | Description |
|----------|------|-------------|
| `fetchXmlOverride` | SingleLine.Text | Per-deployment FetchXML override (takes precedence over Chart Definition) |

### New Visual Types

#### Visual Type: `duedatecard`
Single card bound to a lookup field.

```typescript
// ChartRenderer routing
case "duedatecard":
  return (
    <EventDueDateCard
      event={singleEventData}
      onClick={() => handleClickAction(eventId, "sprk_event")}
      isNavigating={isNavigating}
    />
  );
```

#### Visual Type: `duedatecardlist`
List of cards driven by a view.

```typescript
// ChartRenderer routing
case "duedatecardlist":
  return (
    <EventDueDateCardList
      events={eventListData}
      onEventClick={(eventId) => handleClickAction(eventId, "sprk_event")}
      onViewListClick={handleViewListClick}
      maxItems={chartDefinition.sprk_maxdisplayitems || 10}
      showViewListLink={!!chartDefinition.sprk_viewlisttabname}
    />
  );
```

### Shared Component: EventDueDateCard

Location: `src/client/shared/Spaarke.UI.Components/src/components/EventDueDateCard/`

```typescript
export interface IEventDueDateCardProps {
  // Required
  eventId: string;
  eventName: string;
  eventTypeName: string;
  dueDate: Date;
  daysUntilDue: number;
  isOverdue: boolean;

  // Optional
  eventTypeColor?: string;  // Hex color from sprk_eventtypecolor
  description?: string;
  assignedTo?: string;

  // Interaction
  onClick?: (eventId: string) => void;
  isNavigating?: boolean;
}

export interface IEventDueDateCardListProps {
  events: IEventDueDateCardProps[];
  onEventClick?: (eventId: string) => void;
  onViewListClick?: () => void;
  maxItems?: number;
  showViewListLink?: boolean;
  emptyMessage?: string;
  loading?: boolean;
}
```

### Click Action Handler

Generic handler in VisualHostRoot:

```typescript
const handleClickAction = async (recordId: string, entityName: string) => {
  const action = chartDefinition.sprk_onclickaction;
  const target = chartDefinition.sprk_onclicktarget;

  switch (action) {
    case "openrecordform":
      await Xrm.Navigation.openForm({
        entityName: target || entityName,
        entityId: recordId,
      });
      break;

    case "opensidepane":
      await Xrm.App.sidePanes.createPane({
        title: "Details",
        paneId: `details_${recordId}`,
        canClose: true,
      }).then(pane => {
        pane.navigate({
          pageType: "custom",
          name: target,
          recordId: recordId,
        });
      });
      break;

    case "navigatetopage":
      await Xrm.Navigation.navigateTo({
        pageType: "custom",
        name: target,
        recordId: recordId,
      });
      break;

    case "opendatasetgrid":
      // Existing drill-through behavior
      handleExpandClick();
      break;

    default:
      // No action
      break;
  }
};
```

### View-Driven Data Fetching

```typescript
const fetchEventsFromView = async (
  webAPI: ComponentFramework.WebApi,
  viewId: string,
  contextFieldName?: string,
  contextRecordId?: string
): Promise<IEventDueDateCardProps[]> => {
  // 1. Fetch view definition
  const view = await webAPI.retrieveRecord(
    "savedquery",
    viewId,
    "?$select=fetchxml,returnedtypecode"
  );

  // 2. Parse and modify FetchXML
  let fetchXml = view.fetchxml;
  if (contextFieldName && contextRecordId) {
    fetchXml = injectContextFilter(fetchXml, contextFieldName, contextRecordId);
  }

  // 3. Execute query
  const results = await webAPI.retrieveMultipleRecords(
    "sprk_event",
    `?fetchXml=${encodeURIComponent(fetchXml)}`
  );

  // 4. Map to card props
  return results.entities.map(mapEventToCardProps);
};

const injectContextFilter = (
  fetchXml: string,
  fieldName: string,
  recordId: string
): string => {
  // Parse FetchXML, find <filter> element, add condition
  // <condition attribute="{fieldName}" operator="eq" value="{recordId}" />
  // Return modified FetchXML
};
```

### Custom FetchXML Support

For sophisticated queries where standard views are insufficient, the framework supports custom FetchXML at two levels.

#### Query Resolution Priority

```typescript
const resolveFetchXml = async (
  chartDef: IChartDefinition,
  pcfFetchXmlOverride: string | undefined,
  contextRecordId: string,
  webAPI: ComponentFramework.WebApi
): Promise<string | null> => {

  // Priority 1: PCF property override (deployment-specific)
  if (pcfFetchXmlOverride) {
    return substituteParams(pcfFetchXmlOverride, chartDef.sprk_fetchxmlparams, contextRecordId);
  }

  // Priority 2: Chart Definition custom FetchXML
  if (chartDef.sprk_fetchxmlquery) {
    return substituteParams(chartDef.sprk_fetchxmlquery, chartDef.sprk_fetchxmlparams, contextRecordId);
  }

  // Priority 3: View-based query
  if (chartDef.sprk_baseviewid) {
    const view = await webAPI.retrieveRecord("savedquery", chartDef.sprk_baseviewid, "?$select=fetchxml");
    return view.fetchxml as string;
  }

  // Priority 4: Direct entity query (existing aggregation behavior)
  return null; // Use existing DataAggregationService
};
```

#### Parameter Substitution

The `sprk_fetchxmlparams` field stores JSON mapping placeholders to runtime values:

```json
{
  "{contextRecordId}": "context.entityId",
  "{currentUserId}": "context.userId",
  "{currentDate}": "runtime.today",
  "{currentDateTime}": "runtime.now"
}
```

**Supported Placeholders:**

| Placeholder | Source | Description |
|-------------|--------|-------------|
| `{contextRecordId}` | `context.mode.contextInfo.entityId` | Current record ID from form |
| `{currentUserId}` | `context.userSettings.userId` | Logged-in user ID |
| `{currentDate}` | Runtime | Today's date (YYYY-MM-DD) |
| `{currentDateTime}` | Runtime | Current timestamp (ISO 8601) |

**Parameter Substitution Function:**

```typescript
const substituteParams = (
  fetchXml: string,
  paramsJson: string | undefined,
  contextRecordId: string
): string => {
  let result = fetchXml;

  // Always substitute contextRecordId
  result = result.replace(/\{contextRecordId\}/g, contextRecordId);

  // Substitute custom params from JSON config
  if (paramsJson) {
    try {
      const params = JSON.parse(paramsJson) as Record<string, string>;
      for (const [placeholder, source] of Object.entries(params)) {
        const value = resolveParamSource(source);
        result = result.replace(new RegExp(placeholder, 'g'), value);
      }
    } catch (e) {
      logger.warn("substituteParams", "Failed to parse params JSON", e);
    }
  }

  return result;
};
```

#### Example: Custom FetchXML with Parameters

**Chart Definition Record:**

```
sprk_fetchxmlquery:
<fetch top="10">
  <entity name="sprk_event">
    <attribute name="sprk_eventid" />
    <attribute name="sprk_eventname" />
    <attribute name="sprk_duedate" />
    <filter>
      <condition attribute="_sprk_matterid_value" operator="eq" value="{contextRecordId}" />
      <condition attribute="sprk_duedate" operator="on-or-after" value="{currentDate}" />
      <condition attribute="sprk_eventstatus" operator="ne" value="100000002" /> <!-- Not Completed -->
    </filter>
    <order attribute="sprk_duedate" />
  </entity>
</fetch>

sprk_fetchxmlparams:
{
  "{contextRecordId}": "context.entityId",
  "{currentDate}": "runtime.today"
}
```

#### Use Case: PCF Override

A form may need additional filtering beyond what the Chart Definition provides:

**PCF Property `fetchXmlOverride`:**
```xml
<fetch top="5">
  <entity name="sprk_event">
    <!-- Same base query but limited to 5 items and filtered to high priority -->
    <filter>
      <condition attribute="_sprk_matterid_value" operator="eq" value="{contextRecordId}" />
      <condition attribute="sprk_priority" operator="eq" value="100000000" /> <!-- High Priority Only -->
    </filter>
  </entity>
</fetch>
```

This override takes precedence over the Chart Definition's `sprk_fetchxmlquery`.

### Event Type Color Mapping

Colors are sourced from the `sprk_eventtypecolor` choice field on Event Type entity:

| Event Type | Choice Value | Hex Color |
|------------|--------------|-----------|
| Task | 1 | #DCEAF7 (Light Green) |
| Action | 2 | #DCEAF7 (Light Blue) |
| Reminder | 3 | #FFFFAB (Yellow) |
| Notification | 4 | #F2CFEE (Light Pink) |
| Deadline | 5 | #FFD1D1 (Light Red) |

The PCF will need to expand the Event Type lookup to retrieve the color:
```
$expand=sprk_eventtype_ref($select=sprk_eventtypecolor)
```

---

## Card Layout Design

Based on mockup: `projects/events-workspace-apps-UX-r1/notes/due-date-widget-event-card.png`

```
┌──────────────────────────────────────────────────────────────────────┐
│ ┌─────────────┐                                      Days Until Due  │
│ │     4       │  {eventtype}: {eventname}                  ┌───┐    │
│ │  February   │  {description}                             │ 3 │    │
│ │ (bg: color) │  Assigned To: {assignedto}                 └───┘    │
│ └─────────────┘                                           (red)     │
└──────────────────────────────────────────────────────────────────────┘
```

| Section | Content | Styling |
|---------|---------|---------|
| **Date Column** | Day number + Month name | Background: event type color |
| **Content** | Line 1: Event Type : Event Name | Bold title |
| | Line 2: Description | Truncated, secondary text |
| | Line 3: Assigned To: {name} | Tertiary text |
| **Badge** | Days Until Due label | Small text above |
| | Number in circle | Red for overdue, contextual for upcoming |

---

## Migration Path

### DueDateWidget Deprecation

Once VisualHost supports the `duedatecardlist` visual type:

1. Create Chart Definition records for existing DueDateWidget placements
2. Replace DueDateWidget PCF with VisualHost on forms
3. Configure click action as `openrecordform` targeting `sprk_event`
4. Deprecate DueDateWidget PCF (keep for backward compatibility)

### Backward Compatibility

- Existing VisualHost configurations continue to work unchanged
- Click action defaults to existing drill-through behavior if not configured
- DueDateWidget can remain deployed during transition

---

## Phases

| Phase | Scope | Dependencies |
|-------|-------|--------------|
| **Phase 1** | Schema changes to sprk_chartdefinition (click action fields) | None |
| **Phase 2** | Click Action Handler in VisualHostRoot | Phase 1 |
| **Phase 3** | EventDueDateCard shared component | None (parallel) |
| **Phase 4** | Due Date Card visual types in VisualHost | Phases 2, 3 |
| **Phase 5** | Advanced Query Support (View + Custom FetchXML + PCF Override) | Phase 4 |
| **Phase 6** | Testing & deployment | Phase 5 |

---

## Resolved Design Decisions

| # | Question | Decision |
|---|----------|----------|
| 1 | **Event Type Color Source** | Confirmed: `sprk_eventtypecolor` choice field on Event Type entity. Expand lookup in OData query. |
| 2 | **"View List" Navigation** | Default to entity tab on current form. Configurable via `sprk_viewlisttabname` property. |
| 3 | **Max Display Items** | Add as `sprk_maxdisplayitems` property with default of 10. View can return more, but UI shows max with "View List" link for full set. |
| 4 | **Custom FetchXML** | Support via existing `sprk_fetchxmlquery` and `sprk_fetchxmlparams` fields. Add `fetchXmlOverride` PCF property for deployment-level overrides. |
| 5 | **Query Priority** | PCF override → Custom FetchXML → View → Direct entity query. |
| 6 | **Context Filtering** | PCF injects context filter dynamically. Views don't need placeholder fields. |

---

## Updated IChartDefinition Interface

The TypeScript interface will be extended to include all fields:

```typescript
export interface IChartDefinition {
  // Existing fields
  sprk_chartdefinitionid: string;
  sprk_name: string;
  sprk_description?: string;
  sprk_visualtype: VisualType;
  sprk_sourceentity?: string;
  sprk_entitylogicalname?: string;
  sprk_baseviewid?: string;
  sprk_aggregationfield?: string;
  sprk_aggregationtype?: AggregationType;
  sprk_groupbyfield?: string;
  sprk_optionsjson?: string;
  sprk_configurationjson?: string;

  // Existing FetchXML fields (already in Dataverse)
  sprk_fetchxmlquery?: string;
  sprk_fetchxmlparams?: string;

  // New click action fields (to be added)
  sprk_onclickaction?: OnClickAction;
  sprk_onclicktarget?: string;
  sprk_onclickrecordfield?: string;

  // New display fields (to be added)
  sprk_contextfieldname?: string;
  sprk_viewlisttabname?: string;
  sprk_maxdisplayitems?: number;
}

export enum OnClickAction {
  None = 100000000,
  OpenRecordForm = 100000001,
  OpenSidePane = 100000002,
  NavigateToPage = 100000003,
  OpenDatasetGrid = 100000004,
}

export enum VisualType {
  MetricCard = 100000000,
  BarChart = 100000001,
  LineChart = 100000002,
  AreaChart = 100000003,
  DonutChart = 100000004,
  StatusBar = 100000005,
  Calendar = 100000006,
  MiniTable = 100000007,
  DueDateCard = 100000008,      // NEW
  DueDateCardList = 100000009,  // NEW
}
```

---

## References

- [VisualHost PCF Source](../../src/client/pcf/VisualHost/)
- [DueDateWidget PCF Source](../../src/client/pcf/DueDatesWidget/)
- [ADR-021: Fluent UI v9](../../.claude/adr/ADR-021.md)
- [ADR-022: PCF Platform Libraries](../../.claude/adr/ADR-022.md)
- [Event Due Date Card Mockup](../events-workspace-apps-UX-r1/notes/due-date-widget-event-card.png)
- [Event Type Color Codes](../events-workspace-apps-UX-r1/notes/event-type-color-codes.png)

---

*This design document captures the collaborative discussion on 2026-02-08 between the development team and Claude Code. It should be converted to spec.md using the design-to-spec skill before project initialization.*
