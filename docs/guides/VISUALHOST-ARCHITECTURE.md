# VisualHost - Architecture Documentation

> **Version**: 1.2.12 | **Last Updated**: February 9, 2026
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
9. [Caching Strategy](#caching-strategy)
10. [Reusability: PCF, Custom Pages, and Beyond](#reusability-pcf-custom-pages-and-beyond)
11. [Solution Packaging](#solution-packaging)
12. [Technology Stack](#technology-stack)
13. [Extension Guide](#extension-guide)

---

## Architecture Overview

VisualHost is a **configuration-driven visualization framework** for Dataverse model-driven apps. A single PCF control renders 10 different visual types based on a `sprk_chartdefinition` entity record.

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
            └──────────────┘  └───┬───┘  └────────────────┘
                                  │
                    ┌─────────────┼─────────────┐
                    │             │              │
              ┌─────▼─────┐ ┌────▼────┐  ┌─────▼─────┐
              │ Aggregation│ │  View   │  │  Direct   │
              │  Service   │ │  Data   │  │  Entity   │
              │ (Charts)   │ │ Service │  │  Query    │
              └────────────┘ │(Cards)  │  │(Fallback) │
                             └─────────┘  └───────────┘
                                  │
                    ┌─────────────▼─────────────┐
                    │      ChartRenderer        │
                    │  (Visual Type Router)      │
                    └──┬──┬──┬──┬──┬──┬──┬──┬──┘
                       │  │  │  │  │  │  │  │
              Chart Components    Card Components
              (10 visual types)   (shared library)
```

### Design Principles

1. **Configuration over Code** - No code changes needed to add a new visual instance; just create a chart definition record
2. **Single Control, Multiple Visuals** - One PCF control handles all visual types, reducing solution complexity
3. **Layered Data Fetching** - Query priority resolution with 4 tiers, caching at each layer
4. **Shared Components** - Visual components in `@spaarke/ui-components` are reusable across PCF controls and Custom Pages
5. **Platform Integration** - Deep integration with Dataverse WebAPI, form context, side panes, and navigation

---

## Component Architecture

### File Structure

```
src/client/pcf/VisualHost/
├── control/
│   ├── ControlManifest.Input.xml    # PCF manifest (properties, platform libs)
│   ├── index.ts                      # PCF lifecycle (init, updateView, destroy)
│   ├── types/
│   │   └── index.ts                  # Enums, interfaces, type definitions
│   ├── components/
│   │   ├── VisualHostRoot.tsx        # Main orchestration component (476 lines)
│   │   ├── ChartRenderer.tsx         # Visual type router (374 lines)
│   │   ├── MetricCard.tsx            # Single value display
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
│       └── logger.ts                 # Structured logging utility
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
│   ├── MetricCard
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
└── Version Badge (v1.2.12)
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
  │          b. Inject context filter into FetchXML (if configured)
  │          c. Execute via ?fetchXml=...
  │       2. If no viewId → fetchRecordsBasic():
  │          a. Build FetchXML with <attribute> elements + context filter
  │          b. Execute via ?fetchXml=...
  │       3. Group records by sprk_groupbyfield
  │       4. Aggregate per group (Count/Sum/Average/Min/Max)
  │       5. Sort groups by value (descending)
  │       6. Cache result → Return IChartData
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

### DataAggregationService

**Purpose:** Fetch entity records via FetchXML and aggregate for chart rendering.

| Function | Description |
|----------|-------------|
| `fetchAndAggregate(context, definition, options?)` | Main entry: fetch + aggregate |
| `fetchRecords(context, entity, options?)` | Route to view-based or basic FetchXML |
| `clearAggregationCache(key?)` | Clear aggregation cache |

**Data Fetching (all FetchXML):**
- **With viewId** → `fetchRecordsFromView()`: Retrieves saved view's FetchXML via `getViewFetchXml()`, injects context filter, executes via `?fetchXml=...`
- **Without viewId** → `fetchRecordsBasic()`: Builds basic FetchXML with `<attribute>` elements and context filter condition

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
| **MetricCard** | value, label, description, trend, trendValue, compact | IChartData.dataPoints[0] |
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
```

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
        → onExpandClick() callback → opens drill-through workspace
```

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
        ├── bundle.js (~483 KiB production)
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
     NewVisualType = 100000010,
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

### Adding a New Data Source

1. **Add the service** in `control/services/`
2. **Integrate with VisualHostRoot** data fetching flow
3. **Consider caching** (follow existing patterns with TTL)

### Adding a New Click Action

1. **Add the enum value** in `types/index.ts` (OnClickAction)
2. **Implement the handler** in `services/ClickActionHandler.ts`
3. **Add the option set value** in Dataverse for `sprk_onclickaction`

---

*For setup and configuration instructions, see [VISUALHOST-SETUP-GUIDE.md](VISUALHOST-SETUP-GUIDE.md)*
