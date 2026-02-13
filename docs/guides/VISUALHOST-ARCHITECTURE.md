# VisualHost - Architecture Documentation

> **Version**: 1.2.33 | **Last Updated**: February 13, 2026
>
> **Audience**: Developers, solution architects, AI coding agents
>
> **Purpose**: Technical architecture of the VisualHost visualization framework, component design, data flow, and reusability patterns

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Component Architecture](#component-architecture)
3. [Data Flow](#data-flow)
4. [Service Layer](#service-layer)
5. [Visual Component Library](#visual-component-library)
6. [Shared Component Library](#shared-component-library)
7. [Query Resolution System](#query-resolution-system)
8. [Click Action Framework](#click-action-framework)
9. [Drill-Through Navigation System](#drill-through-navigation-system)
10. [Events Page as Universal Dataset Grid](#events-page-as-universal-dataset-grid)
11. [Caching Strategy](#caching-strategy)
12. [Reusability: PCF, Custom Pages, and Beyond](#reusability-pcf-custom-pages-and-beyond)
13. [Solution Packaging](#solution-packaging)
14. [Technology Stack](#technology-stack)
15. [Extension Guide](#extension-guide)
16. [Future Enhancement Context](#future-enhancement-context)

---

## Architecture Overview

VisualHost is a **configuration-driven visualization framework** for Dataverse model-driven apps. A single PCF control renders 10 different visual types based on a `sprk_chartdefinition` entity record. It also provides **drill-through navigation** that opens web resource-based dataset grids (such as the Events Page) in Dataverse dialogs with full context filtering.

```
                    ┌──────────────────────────────┐
                    │       Dataverse Form          │
                    │  ┌────────────────────────┐   │
                    │  │   VisualHost PCF        │   │
                    │  │   Control Instance       │   │
                    │  └────────┬───────────────┘   │
                    └───────────┼───────────────────┘
                                │
                    ┌───────────▼───────────────────┐
                    │     VisualHostRoot.tsx         │
                    │  (Orchestration Component)     │
                    └───┬───────┬───────┬──────────┘
                        │       │       │
            ┌───────────▼─┐  ┌──▼────┐  ┌▼──────────────┐
            │ Configuration│  │ Data  │  │  Click Action  │
            │   Loader     │  │ Layer │  │   Handler      │
            └──────────────┘  └───┬───┘  └───────┬────────┘
                                  │              │
                    ┌─────────────┼──────────┐   │
                    │             │           │   │
              ┌─────▼─────┐ ┌────▼────┐ ┌───▼───▼──────────────┐
              │ Aggregation│ │  View   │ │  Drill-Through       │
              │  Service   │ │  Data   │ │  Navigation          │
              │ (Charts)   │ │ Service │ │  (Web Resource Dialog)│
              └────────────┘ │(Cards)  │ └──────────┬───────────┘
                             └─────────┘            │
                                  │         ┌───────▼────────────┐
                    ┌─────────────▼──────┐  │  Events Page       │
                    │      ChartRenderer │  │  (sprk_eventspage) │
                    │  (Visual Type      │  │  Dataset Grid      │
                    │   Router)          │  │  in Dialog Mode    │
                    └──┬──┬──┬──┬──┬──┘  └────────────────────┘
                       │  │  │  │  │
              Chart Components    Card Components
              (10 visual types)   (shared library)
```

### Design Principles

1. **Configuration over Code** - No code changes needed to add a new visual instance; just create a chart definition record
2. **Single Control, Multiple Visuals** - One PCF control handles all visual types, reducing solution complexity
3. **Layered Data Fetching** - Query priority resolution with 4 tiers, caching at each layer
4. **Shared Components** - Visual components in `@spaarke/ui-components` are reusable across PCF controls and Custom Pages
5. **Platform Integration** - Deep integration with Dataverse WebAPI, form context, side panes, and navigation
6. **Drill-Through to Dataset Grids** - Expand button opens web resource-based grids in Dataverse dialogs with context parameters

---

## Component Architecture

### File Structure

```
src/client/pcf/VisualHost/
├── control/
│   ├── ControlManifest.Input.xml    # PCF manifest (properties, platform libs)
│   ├── index.ts                      # PCF lifecycle (init, updateView, destroy)
│   ├── types/
│   │   └── index.ts                  # Enums, interfaces (IChartDefinition, DrillInteraction)
│   ├── components/
│   │   ├── VisualHostRoot.tsx        # Main orchestration + drill-through navigation (~483 lines)
│   │   ├── ChartRenderer.tsx         # Visual type router (374 lines)
│   │   ├── MetricCard.tsx            # Single value display
│   │   ├── MetricCardMatrix.tsx     # Responsive card grid for matrix mode
│   │   ├── BarChart.tsx              # Bar chart (vertical/horizontal)
│   │   ├── LineChart.tsx             # Line/area chart
│   │   ├── DonutChart.tsx            # Donut/pie chart
│   │   ├── StatusDistributionBar.tsx # Colored status bar
│   │   ├── CalendarVisual.tsx        # Calendar heat map
│   │   ├── MiniTable.tsx             # Compact ranked table
│   │   ├── DueDateCard.tsx           # Single event card (172 lines)
│   │   └── DueDateCardList.tsx       # Event card list (250 lines)
│   ├── services/
│   │   ├── ConfigurationLoader.ts    # Chart definition loading + cache (447 lines)
│   │   ├── DataAggregationService.ts # Record fetch + aggregation (561 lines)
│   │   ├── ViewDataService.ts        # Query resolution, FetchXML (619 lines)
│   │   └── ClickActionHandler.ts     # Click action execution (197 lines)
│   └── utils/
│       ├── logger.ts                 # Structured logging utility
│       ├── valueFormatters.ts        # Centralized value formatting
│       └── cardConfigResolver.ts     # 3-tier card configuration resolution
├── Solution/                          # Dataverse solution packaging
│   ├── solution.xml                   # Solution manifest
│   ├── customizations.xml             # Solution customizations
│   ├── pack.ps1                       # ZIP packaging script
│   └── Controls/                      # Compiled control artifacts
└── package.json                       # Dependencies
```

### Component Hierarchy

```
VisualHostRoot
├── Toolbar (expand button + tooltip)
├── Loading State (Spinner)
├── Error State (MessageBar)
├── ChartRenderer
│   ├── MetricCard (renders as MetricCardMatrix in matrix mode)
│   │   └── MetricCardMatrix (responsive CSS Grid of metric cards)
│   ├── BarChart (via @fluentui/react-charting)
│   ├── LineChart (via @fluentui/react-charting)
│   ├── DonutChart (via @fluentui/react-charting)
│   ├── StatusDistributionBar
│   ├── CalendarVisual
│   ├── MiniTable
│   ├── DueDateCardVisual
│   │   └── EventDueDateCard (from @spaarke/ui-components)
│   └── DueDateCardListVisual
│       ├── EventDueDateCard[] (from @spaarke/ui-components)
│       └── "View All" Link
├── Drill-Through Dialog (opened via handleExpandClick)
│   └── Web Resource (e.g., Events Page) with context params
└── Version Badge (v1.2.33)
```

---

## Data Flow

### Chart Visual Types (MetricCard, BarChart, LineChart, etc.)

```
Form Load
  │
  ▼
VisualHostRoot: Extract chartDefinitionId from lookup or static property
  │
  ▼
ConfigurationLoader.loadChartDefinition(id)
  │  ├── Cache Hit (5-min TTL) → Return cached IChartDefinition
  │  └── Cache Miss → WebAPI.retrieveRecord("sprk_chartdefinition", id)
  │                    → Map fields → Cache → Return
  ▼
VisualHostRoot: Check if visual type needs aggregation
  │  ├── DueDateCard/DueDateCardList → Skip aggregation (self-managed data)
  │  └── All other types → Continue to DataAggregationService
  ▼
DataAggregationService.fetchAndAggregate(context, definition)
  │  ├── Cache Hit (2-min TTL) → Return cached IChartData
  │  └── Cache Miss:
  │       1. If viewId set → fetchRecordsFromView():
  │          a. Retrieve saved view's FetchXML via getViewFetchXml()
  │          b. Inject required attributes for groupByField/aggregationField (v1.2.14+)
  │          c. Inject context filter into FetchXML (if configured)
  │          d. Execute via ?fetchXml=...
  │       2. If no viewId → fetchRecordsBasic():
  │          a. Build FetchXML with <attribute> elements + context filter
  │          b. Execute via ?fetchXml=...
  │       3. Group records by sprk_groupbyfield (uses formatted value annotations for labels)
  │       4. Aggregate per group (Count/Sum/Average/Min/Max)
  │       5. Capture option set colors from OData annotation (`@OData.Community.Display.V1.Color`) and sort order from numeric field values
  │       6. Sort groups alphabetically by label (A→Z)
  │       7. Cache result → Return IChartData
  ▼
ChartRenderer: Switch on sprk_visualtype
  │
  ▼
Appropriate Component: Render with data + interaction handlers
```

### Card Visual Types (DueDateCard, DueDateCardList)

```
Form Load
  │
  ▼
VisualHostRoot: Load chart definition (same as above)
  │
  ▼
ChartRenderer: Routes to DueDateCardVisual or DueDateCardListVisual
  │
  ├── DueDateCardVisual (single card):
  │     1. Build FetchXML with link-entity for event type
  │     2. Filter by contextRecordId (event primary key)
  │     3. Execute via ?fetchXml=...
  │     4. Map to EventDueDateCard props
  │     5. Render single card
  │
  └── DueDateCardListVisual (card list):
        1. Build substitution params from runtime context
        2. ViewDataService.resolveQuery():
        │  Priority 1: PCF fetchXmlOverride → use as-is
        │  Priority 2: sprk_fetchxmlquery → custom FetchXML
        │  Priority 3: sprk_baseviewid → retrieve view FetchXML
        │  Priority 4: Direct entity query → caller builds FetchXML
        3. Inject context filter into resolved FetchXML (if configured)
        4. Execute via ?fetchXml=...
        5. Map records to EventDueDateCard props[]
        6. Render card list with "View All" link
```

---

## Service Layer

### ConfigurationLoader

**Purpose:** Load and cache `sprk_chartdefinition` records from Dataverse.

| Function | Description |
|----------|-------------|
| `loadChartDefinition(context, id, skipCache?)` | Load single definition by ID |
| `loadChartDefinitions(context, ids, skipCache?)` | Load multiple definitions |
| `queryChartDefinitions(context, filter?)` | Query with OData filter (see note below) |
| `getChartOptions(definition)` | Parse `sprk_optionsjson` safely |
| `clearCache(id?)` | Clear cache for ID or all |

**Cache:** In-memory Map, 5-minute TTL.

**Field Mapping:** Maps Dataverse record fields to strongly-typed `IChartDefinition` interface. Handles:
- Option set values → TypeScript enums (with validation)
- Lookup fields → Read `_fieldname_value` computed property
- JSON fields → Parsed with fallback to empty object
- Drill-through target → `sprk_drillthroughtarget` (web resource name for expand dialog)

### DataAggregationService

**Purpose:** Fetch entity records via FetchXML and aggregate for chart rendering.

| Function | Description |
|----------|-------------|
| `fetchAndAggregate(context, definition, options?)` | Main entry: fetch + aggregate |
| `fetchRecords(context, entity, options?)` | Route to view-based or basic FetchXML |
| `clearAggregationCache(key?)` | Clear aggregation cache |

**Data Fetching (all FetchXML):**
- **With viewId** → `fetchRecordsFromView()`: Retrieves saved view's FetchXML via `getViewFetchXml()`, injects required chart attributes (groupByField, aggregationField) if missing from view, injects context filter, executes via `?fetchXml=...`
- **Without viewId** → `fetchRecordsBasic()`: Builds basic FetchXML with `<attribute>` elements and context filter condition

**Grouping & Labels:**
- Uses `@OData.Community.Display.V1.FormattedValue` annotation for human-readable labels on lookup and choice fields
- Captures option set hex colors from `@OData.Community.Display.V1.Color` annotation per data point
- Captures numeric field values as `sortOrder` for option set ordering
- Groups sorted alphabetically by label (A→Z)

**Aggregation Types:**
- **Count** (default) - Number of records per group
- **Sum** - Sum of numeric field values per group
- **Average** - Average of numeric field values per group
- **Min/Max** - Minimum/maximum value per group

**Cache:** In-memory Map, 2-minute TTL (data changes more frequently than config).

### ViewDataService

**Purpose:** Query resolution, FetchXML manipulation, and parameter substitution.

| Function | Description |
|----------|-------------|
| `resolveQuery(inputs)` | 4-tier priority resolution |
| `getViewFetchXml(webApi, viewId)` | Retrieve saved view FetchXML |
| `injectContextFilter(fetchXml, fieldName, recordId)` | Add filter to FetchXML |
| `injectRequiredAttributes(fetchXml, requiredColumns)` | Inject missing `<attribute>` elements for chart fields (v1.2.14+) |
| `applyMaxItems(fetchXml, maxItems)` | Set FetchXML `top` attribute |
| `substituteParameters(fetchXml, params, paramMappings?)` | Replace placeholders |
| `fetchEventsFromView(webApi, viewContext)` | Execute view-based query |
| `fetchEventsFromChartDefinition(webApi, def, contextId?)` | Convenience wrapper |

**View Cache:** In-memory Map, 10-minute TTL (views change very infrequently).

**Parameter Substitution Placeholders:**
| Placeholder | Replaced With |
|-------------|---------------|
| `{contextRecordId}` | Current form record GUID |
| `{currentUserId}` | Current Dataverse system user GUID |
| `{currentDate}` | Today (YYYY-MM-DD) |
| `{currentDateTime}` | Now (ISO 8601) |
| Custom params | From `sprk_fetchxmlparams` JSON |

### ClickActionHandler

**Purpose:** Execute configured click actions using Xrm APIs.

| Function | Description |
|----------|-------------|
| `executeClickAction(ctx, onExpandClick?)` | Execute the configured action |
| `hasClickAction(definition)` | Check if definition has a click action |

**Actions use Xrm APIs:**
- `Xrm.Navigation.openForm()` - Modal record form
- `Xrm.App.sidePanes.createPane()` - Side pane Custom Page
- `Xrm.Navigation.navigateTo()` - Full Custom Page navigation

---

## Visual Component Library

### Internal Components

These components are defined within the VisualHost PCF control and render specific chart types:

| Component | Props Interface | Data Source |
|-----------|----------------|-------------|
| **MetricCard** | value, label, description, trend, trendValue, compact, valueFormat, nullDisplay, icon, accentColor, iconColor, cardBackground, valueColor | IChartData.dataPoints[0] |
| **MetricCardMatrix** | dataPoints, columns, width, height, justification, drillField, cardConfig | IChartData.dataPoints (multiple) |
| **BarChart** | data, title, orientation, showLabels, showLegend, height | IChartData.dataPoints |
| **LineChart** | data, title, variant (line/area), showLegend, lineColor | IChartData.dataPoints |
| **DonutChart** | data, title, innerRadius, showCenterValue, centerLabel | IChartData.dataPoints |
| **StatusDistributionBar** | segments, title, showLabels, showCounts, height | IChartData.dataPoints |
| **CalendarVisual** | events, title, showNavigation | IChartData.dataPoints → ICalendarEvent[] |
| **MiniTable** | items, columns, title, topN, showRank | IChartData.dataPoints → IMiniTableItem[] |

### Shared Components (from @spaarke/ui-components)

| Component | Package | Used By |
|-----------|---------|---------|
| **EventDueDateCard** | `@spaarke/ui-components/dist/components/EventDueDateCard` | DueDateCard, DueDateCardList |

The `EventDueDateCard` renders a single event card with:
- Event name and type name
- Due date with days-until-due count
- Overdue/upcoming visual indicators
- Event type color accent bar
- Optional description and assignee
- Click handler with loading state

### Common Data Contracts

```typescript
// All chart components receive data through this interface:
interface IAggregatedDataPoint {
  label: string;       // Display label (group value or "Total")
  value: number;       // Aggregated numeric value
  color?: string;      // Optional color for the data point
  fieldValue: unknown; // Original field value (for drill-through)
  sortOrder?: number;  // Option set sort order
}

interface IChartData {
  dataPoints: IAggregatedDataPoint[];
  totalRecords: number;
  aggregationType: AggregationType;
  aggregationField?: string;
  groupByField?: string;
}

// Card components map Dataverse records to this interface:
interface IEventDueDateCardProps {
  eventId: string;
  eventName: string;
  eventTypeName: string;
  dueDate: Date;
  daysUntilDue: number;
  isOverdue: boolean;
  eventTypeColor?: string;
  description?: string;
  assignedTo?: string;
  onClick?: (eventId: string) => void;
  isNavigating?: boolean;
}

// Card configuration for MetricCard and MetricCardMatrix:
interface ICardConfig {
  valueFormat: ValueFormatType;     // shortNumber | letterGrade | percentage | wholeNumber | decimal | currency
  colorSource: ColorSourceType;     // none | optionSetColor | valueThreshold
  cardDescription?: string;         // Template with {value}, {formatted}, {label}, {count} placeholders
  nullDisplay: string;              // Text when value is null (default: "—")
  nullDescription?: string;
  cardSize: CardSize;               // small (140px) | medium (200px) | large (280px)
  sortBy: CardSortBy;               // label | value | valueAsc | optionSetOrder
  columns?: number;                 // Fixed columns (undefined = auto-fill responsive)
  compact: boolean;
  showTitle: boolean;
  maxCards?: number;
  accentFromOptionSet: boolean;     // Use option set hex color as border accent
  iconMap?: Record<string, string>; // Map group labels → Fluent icon names
  colorThresholds?: IColorThreshold[];
}
```

### Card Configuration System (v1.2.33)

The MetricCard and MetricCardMatrix components use a **3-tier configuration resolution** system implemented in `cardConfigResolver.ts`. Configuration is merged top-down, with higher tiers overriding lower tiers:

| Tier | Source | Description |
|------|--------|-------------|
| **Tier 1** | PCF property override (`valueFormatOverride`) | Per-deployment override set at form control properties |
| **Tier 2** | Chart Definition Dataverse fields (`sprk_valueformat`, `sprk_colorsource`) | Per-chart-definition configuration in Dataverse |
| **Tier 3** | Configuration JSON (`sprk_configurationjson`) | Full `ICardConfig` object stored as JSON on the chart definition |
| **Tier 4** | Defaults | Built-in defaults (shortNumber format, no color source, medium size, etc.) |

The `cardConfigResolver.ts` utility merges all tiers, with Tier 1 taking highest precedence.

**ReportCardMetric Preset:** The `ReportCardMetric` visual type (enum `100000010`) is a preset that auto-applies domain-specific defaults via the card configuration system:
- **valueFormat**: `letterGrade`
- **colorSource**: `valueThreshold`
- **Icons**: Grade-specific icons (e.g., trophy for A, warning for F)
- **nullDisplay**: `"N/A"`
- **sortBy**: `optionSetOrder`

This preset pattern allows domain-specific card configurations without creating separate components. The `cardConfigResolver` detects the `ReportCardMetric` visual type and applies these defaults at Tier 3 level, which can still be overridden by Tiers 1-2.

---

## Shared Component Library

### Location and Structure

```
src/client/shared/Spaarke.UI.Components/
├── src/
│   ├── components/
│   │   ├── EventDueDateCard/
│   │   │   ├── EventDueDateCard.tsx    # Component implementation
│   │   │   └── index.ts               # Public export
│   │   └── ... (other shared components)
│   └── index.ts                        # Barrel export
├── dist/                               # Compiled output (TypeScript → JavaScript)
├── package.json                        # @spaarke/ui-components
└── tsconfig.json                       # Build configuration
```

### Import Pattern

VisualHost uses **direct path imports** to avoid pulling unnecessary dependencies:

```typescript
// CORRECT: Direct path import (only imports EventDueDateCard)
import { EventDueDateCard } from "@spaarke/ui-components/dist/components/EventDueDateCard";

// INCORRECT: Barrel import (pulls ALL components, including unused deps like lexical)
import { EventDueDateCard } from "@spaarke/ui-components";
```

This reduces bundle size from ~13.8 MiB to ~483 KiB.

### Reusing Shared Components

The `@spaarke/ui-components` library is designed for cross-context reuse:

| Context | Import Method | Example |
|---------|---------------|---------|
| **PCF Controls** | `npm link` or `file:` dependency | VisualHost imports EventDueDateCard |
| **Custom Pages** | `npm install` from package | Custom Page renders EventDueDateCard |
| **React Web Apps** | Standard npm import | Standalone dashboard app |

Components in `@spaarke/ui-components` follow these rules:
- Use **Fluent UI v9** exclusively (ADR-021)
- Use **design tokens** only (no hard-coded colors)
- Support dark mode automatically through token system
- Accept data via props (no internal data fetching)
- No Dataverse/Xrm API dependencies

---

## Query Resolution System

### 4-Tier Priority

```
┌─────────────────────────────────────────────────┐
│              Query Resolution                     │
│                                                   │
│  Priority 1: fetchXmlOverride (PCF property)      │
│  ───────────────────────────────────────────       │
│  Source: Per-deployment override                   │
│  Set at: Form control properties                  │
│  Use: Different queries per form placement         │
│                    │                               │
│                    ▼ (if empty)                    │
│  Priority 2: sprk_fetchxmlquery                   │
│  ───────────────────────────────────────────       │
│  Source: Chart definition record field             │
│  Set at: Chart definition form                    │
│  Use: Complex queries not expressible as views     │
│                    │                               │
│                    ▼ (if empty)                    │
│  Priority 3: sprk_baseviewid                      │
│  ───────────────────────────────────────────       │
│  Source: Dataverse saved view                     │
│  Set at: Chart definition (view lookup)           │
│  Use: Standard data filtering via views            │
│                    │                               │
│                    ▼ (if empty)                    │
│  Priority 4: Direct Entity Query                  │
│  ───────────────────────────────────────────       │
│  Source: Basic FetchXML on sprk_entitylogicalname │
│  Use: Fallback - all records from entity          │
└─────────────────────────────────────────────────┘
```

### Context Filter Injection

When a context filter is configured, it is injected into the query regardless of which priority level provided the base query:

```
Base FetchXML (from any priority)
  │
  ▼
injectContextFilter(fetchXml, fieldName, recordId)
  │  1. Find <entity> element
  │  2. Find existing <filter> (if any)
  │  3. Inject <condition attribute="fieldName" operator="eq" value="recordId"/>
  │  4. Wrap in <filter type="and"> if needed
  ▼
Modified FetchXML with context filter
```

---

## Click Action Framework

### Flow

```
User clicks chart element or card
  │
  ▼
Component calls onClickAction(recordId, entityName, recordData)
  │
  ▼
VisualHostRoot.handleClickAction()
  │
  ▼
ClickActionHandler.executeClickAction(ctx)
  │  ctx = { definition, recordId, entityName, recordData, xrmContext }
  │
  ├── None (100000000)
  │     → No action, return false
  │
  ├── OpenRecordForm (100000001)
  │     → Extract record ID from ctx.recordData[sprk_onclickrecordfield]
  │     → Xrm.Navigation.openForm({ entityName: target, entityId: recordId })
  │
  ├── OpenSidePane (100000002)
  │     → Xrm.App.sidePanes.createPane({
  │         title, paneId, canClose: true,
  │         pageInput: { pageType: "custom", name: target }
  │       })
  │
  ├── NavigateToPage (100000003)
  │     → Xrm.Navigation.navigateTo({
  │         pageType: "custom", name: target,
  │         recordId: recordId
  │       })
  │
  └── OpenDatasetGrid (100000004)
        → onExpandClick() callback → opens drill-through dialog
        → If sprk_drillthroughtarget configured:
            Opens web resource in dialog with context params
        → If not configured:
            Falls back to entitylist dialog (unfiltered)
```

---

## Drill-Through Navigation System

### Overview

When a user clicks the **expand button** (upper-right toolbar icon) on a VisualHost chart, the control navigates to a drill-through view of the underlying data. The drill-through target is configurable per chart definition via the `sprk_drillthroughtarget` field.

### Two Navigation Paths

```
handleExpandClick()
  │
  ├── sprk_drillthroughtarget IS configured
  │     │
  │     ▼
  │   Build URL params:
  │     entityName = sprk_entitylogicalname
  │     filterField = context field (stripped of _ prefix/_value suffix)
  │     filterValue = current record GUID (stripped of braces)
  │     viewId = sprk_baseviewid (stripped of braces)
  │     mode = "dialog"
  │     │
  │     ▼
  │   Xrm.Navigation.navigateTo({
  │     pageType: "webresource",
  │     webresourceName: sprk_drillthroughtarget,
  │     data: URLSearchParams.toString()
  │   }, { target: 2, position: 1, width: 90%, height: 85% })
  │     │
  │     ├── Dialog opens → web resource receives params via ?data= query string
  │     └── Dialog not supported → falls back to inline navigation (target: 1)
  │
  └── sprk_drillthroughtarget NOT configured (fallback)
        │
        ▼
      Xrm.Navigation.navigateTo({
        pageType: "entitylist",
        entityName: sprk_entitylogicalname,
        viewId: sprk_baseviewid
      }, { target: 2 })
        │
        └── Opens standard entity list dialog (no context filtering available)
```

### Parameter Passing Contract

Parameters are passed to the web resource via a URL-encoded string in the `data` property of the `navigateTo` page input. The web resource receives them as `?data=key1=val1&key2=val2...` on `window.location.search`.

| Parameter | Source | Description |
|-----------|--------|-------------|
| `entityName` | `sprk_entitylogicalname` | The entity the chart queries (e.g., `sprk_event`) |
| `filterField` | Context field name (cleaned) | Dataverse field to filter on (e.g., `sprk_regardingrecordid`) |
| `filterValue` | `contextRecordId` (cleaned) | Parent record GUID to filter by |
| `viewId` | `sprk_baseviewid` (cleaned) | Saved view GUID for query source |
| `mode` | Always `"dialog"` | Signals the target page is running inside a dialog |

**Field name cleaning:** The context field name is transformed from WebAPI format to FetchXML attribute format (strips leading `_` and trailing `_value` from lookup field names).

### Xrm Resolution

PCF controls run inside iframes. The `Xrm` object may not be on the PCF control's own `window`. VisualHost resolves Xrm from multiple scopes:

```typescript
const xrm = (window.parent as any)?.Xrm || (window as any).Xrm;
```

This ensures `navigateTo` works regardless of the iframe nesting context.

### Key Design Decision: Web Resource vs Custom Page

The drill-through target uses `pageType: "webresource"` (not `pageType: "custom"`) because the Events Page and similar dataset grids are deployed as **Dataverse web resources** (HTML files), not as Power Apps Custom Pages.

| Page Type | `navigateTo` pageType | Data Passing | Use Case |
|-----------|----------------------|--------------|----------|
| Web Resource | `"webresource"` | `data` property (URL-encoded string) | HTML web resources like `sprk_eventspage.html` |
| Custom Page | `"custom"` | `recordId` property | Power Apps canvas apps registered as Custom Pages |
| Entity List | `"entitylist"` | `viewId`, `entityName` | Standard Dataverse entity grid (no context filter support) |

**Why not `entitylist`?** The `entitylist` page type does not support `filterXml` in `navigateTo`, so context filtering is impossible. Web resource dialogs allow full control over filtering.

---

## Events Page as Universal Dataset Grid

### Architecture

The Events Page (`sprk_eventspage.html`) is a React web resource that serves as a **universal dataset grid** for the `sprk_event` entity. It operates in two modes:

```
┌─────────────────────────────────────────────────────────┐
│                  Events Page Modes                       │
├───────────────────────┬─────────────────────────────────┤
│   Standalone Mode     │   Dialog Mode (v2.16.0+)        │
│   (Full Page)         │   (Drill-Through from VisualHost)│
├───────────────────────┼─────────────────────────────────┤
│ URL: /webresources/   │ URL: ?data=mode=dialog&         │
│   sprk_eventspage.html│   filterField=X&filterValue=Y   │
│                       │                                  │
│ ✅ Calendar side pane │ ❌ Calendar side pane (skipped)  │
│ ✅ Full command bar   │ ✅ Full command bar              │
│ ✅ All records        │ ✅ Context-filtered records      │
│ ✅ View selector      │ ✅ View selector                 │
│ ✅ Column filters     │ ✅ Column filters                │
└───────────────────────┴─────────────────────────────────┘
```

### Dialog Mode Behavior (v2.16.0+)

When the Events Page detects `mode=dialog` in its URL parameters, it activates dialog mode:

1. **Parameter Parsing**: `parseDrillThroughParams()` extracts `mode`, `entityName`, `filterField`, `filterValue`, `viewId` from the `?data=` query string at module load time.

2. **Calendar Suppression**: If `IS_DIALOG_MODE === true`, the `useEffect` that registers the Calendar side pane skips registration entirely. This prevents the Calendar from appearing in a drill-through dialog where it serves no purpose.

3. **Context Filter Application**: If `filterField` and `filterValue` are present, a `ContextFilter` object is passed as a prop to `GridSection`:
   ```typescript
   contextFilter={
     DRILL_THROUGH_PARAMS.filterField && DRILL_THROUGH_PARAMS.filterValue
       ? { fieldName: DRILL_THROUGH_PARAMS.filterField, value: DRILL_THROUGH_PARAMS.filterValue }
       : undefined
   }
   ```

4. **FetchXML Filter Injection**: `GridSection` injects a `<condition>` element into the FetchXML query:
   ```xml
   <condition attribute="{filterField}" operator="eq" value="{filterValue}" />
   ```
   This is inserted into the first `</filter>` block, or wrapped in a new `<filter type="and">` if none exists.

5. **OData Fallback**: For non-FetchXML query paths, the filter is applied as an OData condition:
   ```
   $filter=... and {filterField} eq '{filterValue}'
   ```

### Connected Components

```
VisualHost PCF (sender)                    Events Page Web Resource (receiver)
┌──────────────────────┐                   ┌──────────────────────────────────┐
│ VisualHostRoot.tsx    │                   │ App.tsx                          │
│                      │  navigateTo()     │  parseDrillThroughParams()       │
│ handleExpandClick()──┼──────────────────▶│  IS_DIALOG_MODE flag             │
│  builds URL params   │  pageType:        │  Conditional calendar skip       │
│  calls navigateTo    │  "webresource"    │                                  │
│                      │  data: params     │ GridSection.tsx                   │
│ ConfigurationLoader  │                   │  ContextFilter interface         │
│  loads sprk_drill-   │                   │  FetchXML condition injection    │
│  throughtarget       │                   │  OData filter addition           │
└──────────────────────┘                   └──────────────────────────────────┘

src/client/pcf/VisualHost/                 src/solutions/EventsPage/src/
```

### Source Files

| File | Component | Role |
|------|-----------|------|
| `src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx` | VisualHost | Builds params, calls `navigateTo` with `pageType: "webresource"` |
| `src/client/pcf/VisualHost/control/types/index.ts` | VisualHost | `sprk_drillthroughtarget` on `IChartDefinition` |
| `src/client/pcf/VisualHost/control/services/ConfigurationLoader.ts` | VisualHost | Loads `sprk_drillthroughtarget` from Dataverse |
| `src/solutions/EventsPage/src/App.tsx` | Events Page | `parseDrillThroughParams()`, `IS_DIALOG_MODE`, calendar suppression |
| `src/solutions/EventsPage/src/components/GridSection.tsx` | Events Page | `ContextFilter` interface, FetchXML/OData filter injection |
| `scripts/Deploy-EventsPage.ps1` | Deployment | Deploys Events Page web resource to Dataverse |

---

## Caching Strategy

Three independent cache layers with different TTLs:

| Cache | TTL | Key Composition | Rationale |
|-------|-----|-----------------|-----------|
| **Configuration** | 5 min | Chart definition GUID | Definitions rarely change during a session |
| **Aggregation** | 2 min | Entity + view + fields + context filter hash | Data changes more frequently |
| **View FetchXML** | 10 min | View GUID | System views change very infrequently |

All caches are in-memory `Map<string, ICacheEntry>` with timestamp-based TTL validation.

```typescript
// Cache check pattern used throughout:
const cached = cache.get(key);
if (cached && Date.now() - cached.timestamp < TTL_MS) {
  return cached.value; // Cache hit
}
// Cache miss → fetch from Dataverse → store in cache
```

---

## Reusability: PCF, Custom Pages, and Beyond

### Component Reuse Layers

The VisualHost architecture separates components into three reusability tiers:

#### Tier 1: Shared Visual Components (@spaarke/ui-components)

**Location:** `src/client/shared/Spaarke.UI.Components/`

These are pure React components with no Dataverse dependencies. They accept data via props and render visuals.

**Reusable in:**
- PCF controls (VisualHost, other controls)
- Custom Pages (React apps hosted in Dataverse)
- Standalone web applications
- Power Apps component framework (custom connectors)

**Components:**
- `EventDueDateCard` - Due date card with overdue indicators
- *(Future: Extracted chart components)*

**How to reuse:**
```typescript
// In any React application:
import { EventDueDateCard } from "@spaarke/ui-components/dist/components/EventDueDateCard";

<EventDueDateCard
  eventId="abc-123"
  eventName="Filing Deadline"
  eventTypeName="Filing Deadline"
  dueDate={new Date("2026-03-15")}
  daysUntilDue={35}
  isOverdue={false}
  eventTypeColor="#E74C3C"
  onClick={(id) => navigate(`/events/${id}`)}
/>
```

#### Tier 2: Service Layer (VisualHost Services)

**Location:** `src/client/pcf/VisualHost/control/services/`

These services depend on the Dataverse WebAPI interface (`IConfigWebApi`) but not on PCF-specific APIs. They can be extracted for use in Custom Pages.

**Reusable in:**
- PCF controls (via `context.webAPI`)
- Custom Pages (via Dataverse client API: `Xrm.WebApi`)
- Any context that implements `IConfigWebApi`

**Interface abstraction enables testing and reuse:**
```typescript
// The service accepts any object with this interface:
interface IConfigWebApi {
  retrieveRecord(entityType: string, id: string, options?: string): Promise<Record<string, unknown>>;
  retrieveMultipleRecords(entityType: string, options?: string): Promise<{ entities: Array<Record<string, unknown>> }>;
}

// In PCF: context.webAPI
// In Custom Page: Xrm.WebApi
// In tests: Mock implementation
```

#### Tier 3: PCF Integration Layer

**Location:** `src/client/pcf/VisualHost/control/` (index.ts, VisualHostRoot.tsx)

This layer is PCF-specific and handles:
- PCF lifecycle (init, updateView, destroy)
- Property binding and extraction
- Form context integration (contextRecordId, entityId)
- React root management

**Not reusable** outside PCF - this is the "glue" layer.

### Custom Page Integration Pattern

To use VisualHost components in a Custom Page:

```
Custom Page (React)
├── Import @spaarke/ui-components for shared visual components
├── Import or copy service layer (ConfigurationLoader, ViewDataService)
├── Implement IConfigWebApi using Xrm.WebApi
├── Load chart definition using ConfigurationLoader
├── Fetch data using ViewDataService or DataAggregationService
└── Render visual components with fetched data
```

**Example Custom Page setup:**
```typescript
import { EventDueDateCard } from "@spaarke/ui-components/dist/components/EventDueDateCard";
import { loadChartDefinition } from "./services/ConfigurationLoader";
import { resolveQuery } from "./services/ViewDataService";

// Implement the WebAPI interface using Xrm.WebApi
const webApi: IConfigWebApi = {
  retrieveRecord: (entity, id, options) =>
    Xrm.WebApi.retrieveRecord(entity, id, options),
  retrieveMultipleRecords: (entity, options) =>
    Xrm.WebApi.retrieveMultipleRecords(entity, options),
};

// Load and render
const definition = await loadChartDefinition({ webAPI: webApi }, chartDefId);
const query = await resolveQuery({ chartDefinition: definition, webApi });
// ... execute query and render components
```

### Extracting Chart Components (Future)

The internal chart components (BarChart, DonutChart, etc.) currently live inside the VisualHost PCF control. To make them reusable:

1. Extract to `@spaarke/ui-components` shared library
2. Define clean prop interfaces (no Dataverse types)
3. Accept `IAggregatedDataPoint[]` as data input
4. Keep styling via Fluent UI design tokens (automatic dark mode)

This follows ADR-012 (shared component library) and enables chart reuse across all React contexts.

---

## Solution Packaging

### Build and Package Process

```
Source Code (TypeScript/React)
  │
  ▼
npm run build (pcf-scripts build)
  │  ├── TypeScript compilation
  │  ├── Webpack bundling
  │  └── Output: out/controls/VisualHost/bundle.js
  ▼
Copy artifacts to Solution/Controls/
  │  ├── bundle.js
  │  ├── ControlManifest.xml (from ControlManifest.Input.xml)
  │  └── styles.css
  ▼
pack.ps1 (PowerShell)
  │  ├── Creates ZIP using System.IO.Compression
  │  ├── Forward slashes in paths (Dataverse requirement)
  │  ├── [Content_Types].xml with bracket handling
  │  └── Output: bin/VisualHostSolution_v{version}.zip
  ▼
Import to Dataverse (unmanaged solution)
```

### Version Management

Version must be updated in **5 locations** for each release:

| Location | File | Format |
|----------|------|--------|
| PCF Manifest | `control/ControlManifest.Input.xml` | `version="X.Y.Z"` |
| Solution Manifest | `Solution/solution.xml` | `<Version>X.Y.Z</Version>` |
| Solution Control | `Solution/Controls/.../ControlManifest.xml` | `version="X.Y.Z"` |
| Pack Script | `Solution/pack.ps1` | `$version = "X.Y.Z"` |
| UI Badge | `control/components/VisualHostRoot.tsx` | `vX.Y.Z • YYYY-MM-DD` |

### Solution Contents

```
VisualHostSolution_v1.2.2.zip
├── [Content_Types].xml
├── solution.xml
├── customizations.xml
└── Controls/
    └── sprk_Spaarke.Visuals.VisualHost/
        ├── ControlManifest.xml
        ├── bundle.js (~825 KiB compressed (v1.2.33); ~4.97 MiB uncompressed)
        └── styles.css
```

---

## Technology Stack

| Layer | Technology | Version | Notes |
|-------|-----------|---------|-------|
| **Runtime** | React | 16.14.0 | Platform-provided (ADR-022: React 16 APIs only) |
| **UI Framework** | Fluent UI v9 | 9.46.2 | Platform-provided (ADR-021) |
| **Charts** | @fluentui/react-charting | 5.23.0 | Bar, line, pie, donut |
| **Icons** | @fluentui/react-icons | 2.0.311 | SVG icon components |
| **Bundler** | Webpack | 5.x | Via pcf-scripts |
| **Language** | TypeScript | 4.x | Strict mode |
| **Platform** | PCF (PowerApps Component Framework) | 1.3.18 | API version |
| **Data** | Dataverse WebAPI | v9.2 | OData + FetchXML |

### Platform Library Dependencies

These are provided by the Dataverse runtime (not bundled):

```xml
<platform-library name="React" version="16.14.0" />
<platform-library name="Fluent" version="9.46.2" />
```

---

## Extension Guide

### Adding a New Visual Type

1. **Define the enum value** in `control/types/index.ts`:
   ```typescript
   export enum VisualType {
     // ... existing types
     ReportCardMetric = 100000010,
   }
   ```

2. **Create the component** in `control/components/NewVisual.tsx`:
   - Accept `IAggregatedDataPoint[]` or custom data props
   - Use Fluent UI v9 components and design tokens
   - Support dark mode via tokens (no hard-coded colors)

3. **Add the route** in `control/components/ChartRenderer.tsx`:
   ```typescript
   case VT.NewVisualType: {
     return <NewVisual data={dataPoints} ... />;
   }
   ```

4. **Update the type name** in `getVisualTypeName()` in ChartRenderer.tsx

5. **Add the option set value** in Dataverse for `sprk_visualtype`

6. **Bump version** in all 5 locations

> **Note:** `ReportCardMetric` (100000010) is now a preset for MetricCard -- it auto-applies grade defaults via `cardConfigResolver`. This pattern can be used for other domain-specific presets without creating separate components.

### Adding a New Data Source

1. **Add the service** in `control/services/`
2. **Integrate with VisualHostRoot** data fetching flow
3. **Consider caching** (follow existing patterns with TTL)

### Adding a New Click Action

1. **Add the enum value** in `types/index.ts` (OnClickAction)
2. **Implement the handler** in `services/ClickActionHandler.ts`
3. **Add the option set value** in Dataverse for `sprk_onclickaction`

### Adding a New Drill-Through Target

To configure a different web resource as a drill-through target (beyond the Events Page):

1. **Create the web resource** in Dataverse (HTML file, e.g., `sprk_documentspage.html`)
2. **Implement parameter parsing** in the web resource — read `?data=` query string for `mode`, `filterField`, `filterValue`, `entityName`, `viewId`
3. **Implement dialog mode** — detect `mode=dialog` and adjust UI (hide navigation elements, apply context filter)
4. **Set `sprk_drillthroughtarget`** on the chart definition record to the web resource name (e.g., `sprk_documentspage.html`)
5. No changes needed in VisualHost — the drill-through system is generic and works with any web resource that follows the parameter contract

### Extending the Events Page for New Entities

The Events Page currently queries `sprk_event` exclusively. To support additional entities:

1. **GridSection.tsx** — The entity name is hardcoded as `sprk_event` in the `executeFetchXml` call and OData query. To support other entities, parameterize this using the `entityName` drill-through param.
2. **Column definitions** — Column metadata in `GridSection.tsx` is event-specific. A generic grid would need dynamic column discovery (from the saved view's FetchXML or entity metadata).
3. **ContextFilter** — The `ContextFilter` interface and injection logic are entity-agnostic and will work with any entity/field combination.

---

## Future Enhancement Context

This section captures architectural context relevant to the next project that will enhance the VisualHost and Events Page systems.

### VisualHost Enhancement Opportunities

| Area | Current State | Enhancement Path |
|------|--------------|------------------|
| **Drill-through target** | Single `sprk_drillthroughtarget` field (web resource name) | Could support multiple targets per click action type, or per-visual-type targets |
| **Context params** | Fixed set: entityName, filterField, filterValue, viewId, mode | Extensible via `sprk_optionsjson` for additional custom params |
| **Visual types** | 11 types (enum 100000000–100000010), with ReportCardMetric as a MetricCard preset | Add new types by extending the enum and ChartRenderer switch; card configuration is now extensible via ICardConfig without new components |
| **Click actions** | 4 actions + expand button | Could add: open in new tab, navigate to URL, trigger Power Automate flow |
| **Data services** | Client-side aggregation only | Could add server-side aggregation via BFF API for large datasets |

### Events Page Enhancement Opportunities

| Area | Current State | Enhancement Path |
|------|--------------|------------------|
| **Entity support** | `sprk_event` only | Parameterize entity from drill-through `entityName` param |
| **Column definitions** | Hardcoded event columns | Dynamic columns from saved view metadata or entity definition |
| **Dialog mode** | Skips calendar, applies context filter | Could also hide command bar actions that don't apply in dialogs (New, Delete) |
| **View selector** | Shows all event views | In dialog mode, could default to the `viewId` passed from VisualHost |
| **Filtering** | Context filter + column filters | Could support multi-field context filters (multiple conditions) |

### Cross-Component Integration Points

When making changes that span both VisualHost and the Events Page, these are the integration boundaries:

```
VisualHost (sender)                          Events Page (receiver)
─────────────────                            ──────────────────────
sprk_drillthroughtarget ──────────────────── webresourceName
URL params (data property) ───────────────── parseDrillThroughParams()
  entityName ─────────────────────────────── DRILL_THROUGH_PARAMS.entityName
  filterField ────────────────────────────── contextFilter.fieldName
  filterValue ────────────────────────────── contextFilter.value
  viewId ─────────────────────────────────── (available but not yet used by view selector)
  mode ───────────────────────────────────── IS_DIALOG_MODE flag
```

**Contract rule:** Changes to the parameter names or encoding on the VisualHost side must be matched by corresponding changes in `parseDrillThroughParams()` on the Events Page side. Both components are deployed independently (VisualHost as a PCF solution ZIP, Events Page via `Deploy-EventsPage.ps1`), so parameter contracts must remain backward-compatible or both must be updated and deployed together.

### Key Files for Future Changes

| Change Type | VisualHost Files | Events Page Files |
|-------------|-----------------|-------------------|
| New drill-through params | `VisualHostRoot.tsx` (handleExpandClick) | `App.tsx` (parseDrillThroughParams, DrillThroughParams interface) |
| New context filter logic | `VisualHostRoot.tsx` (param building) | `GridSection.tsx` (ContextFilter, FetchXML injection) |
| New visual types | `types/index.ts`, `ChartRenderer.tsx`, new component file | N/A |
| New chart definition fields | `types/index.ts`, `ConfigurationLoader.ts` | N/A |
| Dialog UI changes | N/A | `App.tsx` (IS_DIALOG_MODE conditionals) |
| Entity-agnostic grid | N/A | `GridSection.tsx` (entity name, columns, FetchXML) |

---

*For setup and configuration instructions, see [VISUALHOST-SETUP-GUIDE.md](VISUALHOST-SETUP-GUIDE.md)*
