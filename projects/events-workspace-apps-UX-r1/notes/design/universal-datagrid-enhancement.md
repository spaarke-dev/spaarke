# Universal DataGrid Enhancement for Custom Pages

> **Document Type**: Design Specification
> **Created**: 2026-02-05
> **Project**: events-workspace-apps-UX-r1
> **Status**: Draft

---

## Executive Summary

Enhance the existing UniversalDatasetGrid components to support Custom Pages (HTML/React 18) with view selection, FetchXML execution, and runtime configuration via Dataverse table.

## Current State

### Existing Components

| Location | Purpose | React Version |
|----------|---------|---------------|
| `src/client/pcf/UniversalDatasetGrid/` | PCF control with full DataGrid | React 16.14 (platform) |
| `src/client/shared/Spaarke.UI.Components/` | Shared UI library | React 16+ compatible |
| `src/solutions/EventsPage/` | Custom Page (HTML) | React 18 |

### What Already Works

The PCF UniversalDatasetGrid has these features:
- Fluent UI v9 DataGrid with Power Apps native styling
- Column filters (header filter popups)
- Checkbox selection for bulk operations
- Optimistic row updates
- Calendar filter integration
- Side pane opening via HyperlinkCell
- Command toolbar

### What's Missing

1. **View Selector** - Dropdown to switch between saved views
2. **Headless FetchXML execution** - Query data without PCF dataset binding
3. **Custom Page adapter** - Use DataGrid in HTML pages (React 18)
4. **Configuration table** - Runtime-configurable views and FetchXML

---

## Proposed Architecture

### Component Hierarchy

```
src/client/shared/Spaarke.UI.Components/
└── src/
    └── components/
        └── DatasetGrid/
            ├── UniversalDatasetGrid.tsx      # Existing - orchestrator
            ├── GridView.tsx                   # Existing - Fluent DataGrid
            ├── VirtualizedGridView.tsx        # Existing - large datasets
            ├── ViewSelector.tsx               # NEW - view dropdown
            ├── ViewSelectorContext.tsx        # NEW - view state management
            └── services/
                ├── ViewService.ts             # NEW - fetch saved views
                ├── FetchXmlService.ts         # NEW - execute FetchXML
                └── ConfigurationService.ts    # NEW - read sprk_gridconfiguration

src/solutions/EventsPage/
└── src/
    └── components/
        └── EventsDataGrid.tsx                 # Adapter using shared components
```

### Data Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           EventsPage (React 18)                         │
├─────────────────────────────────────────────────────────────────────────┤
│  ┌─────────────────┐   ┌──────────────────────────────────────────────┐ │
│  │  ViewSelector   │   │              EventsDataGrid                  │ │
│  │  ┌───────────┐  │   │  ┌────────────────────────────────────────┐  │ │
│  │  │ savedquery│──┼───┼─▶│         Fluent UI v9 DataGrid          │  │ │
│  │  │ dropdown  │  │   │  │  - Column filters                      │  │ │
│  │  └───────────┘  │   │  │  - Checkbox selection                  │  │ │
│  │  ┌───────────┐  │   │  │  - Row click → Side pane               │  │ │
│  │  │ sprk_grid │──┼───┼─▶│  - Sorting                             │  │ │
│  │  │ config    │  │   │  │  - Infinite scroll                     │  │ │
│  │  └───────────┘  │   │  └────────────────────────────────────────┘  │ │
│  └─────────────────┘   └──────────────────────────────────────────────┘ │
│           │                              ▲                              │
│           ▼                              │                              │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │                    Data Fetching Layer                           │   │
│  │  ┌────────────────┐    ┌─────────────────┐    ┌───────────────┐  │   │
│  │  │  ViewService   │    │ FetchXmlService │    │ ConfigService │  │   │
│  │  │ (savedquery)   │    │ (Xrm.WebApi)    │    │ (sprk_grid*)  │  │   │
│  │  └────────────────┘    └─────────────────┘    └───────────────┘  │   │
│  └──────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Component Specifications

### 1. ViewSelector Component

**Purpose**: Dropdown that loads and displays available views for an entity

**Props**:
```typescript
interface ViewSelectorProps {
  /** Entity logical name */
  entityLogicalName: string;

  /** Currently selected view ID */
  selectedViewId?: string;

  /** Default view name if no ID specified */
  defaultViewName?: string;

  /** Callback when view changes */
  onViewChange: (view: IViewDefinition) => void;

  /** Include custom configuration views */
  includeCustomViews?: boolean;

  /** Filter to specific view types */
  viewTypes?: ('public' | 'private' | 'custom')[];
}

interface IViewDefinition {
  id: string;
  name: string;
  type: 'savedquery' | 'custom';
  fetchXml: string;
  layoutXml: string;
  isDefault: boolean;
}
```

**Data Sources** (priority order):
1. `savedquery` entity - System and public views
2. `sprk_gridconfiguration` - Custom views (if enabled)

### 2. FetchXmlService

**Purpose**: Execute FetchXML queries via Xrm.WebApi

```typescript
class FetchXmlService {
  private xrm: any; // Resolved via getXrm()

  /**
   * Execute FetchXML and return typed results
   */
  async executeFetchXml<T>(
    entityLogicalName: string,
    fetchXml: string,
    options?: {
      pageSize?: number;
      pagingCookie?: string;
    }
  ): Promise<{
    entities: T[];
    moreRecords: boolean;
    pagingCookie?: string;
  }>;

  /**
   * Parse layoutxml to get column definitions
   */
  parseLayoutXml(layoutXml: string): IColumnDefinition[];
}
```

### 3. ViewService

**Purpose**: Fetch saved views from Dataverse

```typescript
class ViewService {
  /**
   * Get all views for an entity
   */
  async getViews(
    entityLogicalName: string,
    options?: {
      includePrivate?: boolean;
      includeCustom?: boolean;
    }
  ): Promise<IViewDefinition[]>;

  /**
   * Get default view for entity
   */
  async getDefaultView(entityLogicalName: string): Promise<IViewDefinition | null>;

  /**
   * Get view by ID
   */
  async getViewById(viewId: string): Promise<IViewDefinition | null>;
}
```

### 4. sprk_gridconfiguration Table

**Purpose**: Store custom grid configurations and complex FetchXML

**Schema**:

| Column | Type | Description |
|--------|------|-------------|
| `sprk_gridconfigurationid` | GUID | Primary key |
| `sprk_name` | String (100) | Display name (required) |
| `sprk_entitylogicalname` | String (100) | Target entity (required) |
| `sprk_viewtype` | Choice | SavedView=1, CustomFetchXML=2, LinkedView=3 |
| `sprk_savedviewid` | String (36) | Link to savedquery.savedqueryid |
| `sprk_fetchxml` | Multiline (max) | Custom FetchXML |
| `sprk_layoutxml` | Multiline (max) | Column layout XML |
| `sprk_configjson` | Multiline (max) | Additional JSON config |
| `sprk_isdefault` | Boolean | Default view for entity |
| `sprk_sortorder` | Integer | Display order |
| `sprk_iconname` | String (50) | Fluent icon name |
| `statecode` | State | Active/Inactive |
| `statuscode` | Status | Status reason |

**View Type Values**:
- `1 - SavedView`: References an existing savedquery by ID
- `2 - CustomFetchXML`: Uses custom FetchXML in sprk_fetchxml field
- `3 - LinkedView`: Reserved for future cross-entity scenarios

**Use Cases**:
1. **Complex filters** - FetchXML with linked entity conditions not supported by view builder
2. **Aggregate views** - Counts, sums, groupings
3. **Admin-configurable** - Create views without deployment
4. **Fallback chain** - If savedquery fails, use custom FetchXML

---

## Integration with EventsPage

### Current EventsPage GridSection

The current GridSection (800+ lines) uses:
- Custom HTML table
- Hardcoded columns
- OData query building
- Manual filter logic

### Proposed EventsDataGrid

Replace GridSection with adapter using shared DataGrid:

```tsx
// src/solutions/EventsPage/src/components/EventsDataGrid.tsx

import * as React from 'react';
import { FluentProvider } from '@fluentui/react-components';
import {
  ViewSelector,
  FetchXmlService,
  DataGrid,
  IViewDefinition,
  IColumnDefinition
} from '@spaarke/ui-components';
import { resolveTheme } from '../providers/ThemeProvider';
import { getXrm } from '../utils/xrmContext';

interface EventsDataGridProps {
  /** Additional filters to apply (calendar, assigned to, etc.) */
  additionalFilter?: string;

  /** Callback when row clicked */
  onRowClick?: (eventId: string, eventTypeId?: string) => void;

  /** Callback when selection changes */
  onSelectionChange?: (selectedIds: string[]) => void;
}

export const EventsDataGrid: React.FC<EventsDataGridProps> = ({
  additionalFilter,
  onRowClick,
  onSelectionChange
}) => {
  const theme = resolveTheme();
  const [selectedView, setSelectedView] = React.useState<IViewDefinition | null>(null);
  const [records, setRecords] = React.useState<any[]>([]);
  const [columns, setColumns] = React.useState<IColumnDefinition[]>([]);
  const [loading, setLoading] = React.useState(true);

  const fetchXmlService = React.useMemo(() => {
    const xrm = getXrm();
    return xrm ? new FetchXmlService(xrm) : null;
  }, []);

  // Handle view change
  const handleViewChange = React.useCallback(async (view: IViewDefinition) => {
    setSelectedView(view);
    setLoading(true);

    if (!fetchXmlService) {
      console.warn('[EventsDataGrid] FetchXmlService not available');
      return;
    }

    try {
      // Parse columns from layout
      const cols = fetchXmlService.parseLayoutXml(view.layoutXml);
      setColumns(cols);

      // Merge additional filter into FetchXML
      let fetchXml = view.fetchXml;
      if (additionalFilter) {
        fetchXml = mergeFetchXmlFilter(fetchXml, additionalFilter);
      }

      // Execute query
      const result = await fetchXmlService.executeFetchXml('sprk_event', fetchXml);
      setRecords(result.entities);

    } catch (error) {
      console.error('[EventsDataGrid] Failed to load view:', error);
    } finally {
      setLoading(false);
    }
  }, [fetchXmlService, additionalFilter]);

  return (
    <FluentProvider theme={theme}>
      <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
        {/* View Selector */}
        <ViewSelector
          entityLogicalName="sprk_event"
          defaultViewName="Active Events"
          onViewChange={handleViewChange}
          includeCustomViews={true}
        />

        {/* Data Grid */}
        <DataGrid
          records={records}
          columns={columns}
          loading={loading}
          enableColumnFilters={true}
          enableCheckboxSelection={true}
          onRowClick={(record) => onRowClick?.(record.sprk_eventid, record._sprk_eventtype_value)}
          onSelectionChange={onSelectionChange}
        />
      </div>
    </FluentProvider>
  );
};
```

---

## Implementation Tasks

### Phase 1: Core Services (Foundation)

| Task | Description | Estimate |
|------|-------------|----------|
| 1.1 | Create `FetchXmlService` in shared library | Medium |
| 1.2 | Create `ViewService` for savedquery fetching | Medium |
| 1.3 | Create `getXrm()` utility for Custom Pages | Small |
| 1.4 | Unit tests for services | Medium |

### Phase 2: ViewSelector Component

| Task | Description | Estimate |
|------|-------------|----------|
| 2.1 | Create `ViewSelector` component | Medium |
| 2.2 | Create `ViewSelectorContext` for state | Small |
| 2.3 | Style to match Power Apps dropdown | Small |
| 2.4 | Integration tests | Medium |

### Phase 3: Configuration Table

| Task | Description | Estimate |
|------|-------------|----------|
| 3.1 | Create `sprk_gridconfiguration` entity schema | Small |
| 3.2 | Create `ConfigurationService` | Medium |
| 3.3 | Add admin form for configuration | Medium |
| 3.4 | Integration with ViewSelector | Small |

### Phase 4: EventsPage Integration

| Task | Description | Estimate |
|------|-------------|----------|
| 4.1 | Create `EventsDataGrid` adapter component | Medium |
| 4.2 | Replace current `GridSection` | Large |
| 4.3 | Maintain calendar/filter integration | Medium |
| 4.4 | End-to-end testing | Large |

### Phase 5: Documentation & Rollout

| Task | Description | Estimate |
|------|-------------|----------|
| 5.1 | Update ADR-012 for shared grid pattern | Small |
| 5.2 | Developer guide for using universal grid | Medium |
| 5.3 | Admin guide for configuration table | Small |

---

## Risk Assessment

| Risk | Mitigation |
|------|------------|
| FetchXML parsing complexity | Use existing PCF DatasetGrid patterns |
| Performance with large datasets | Existing virtualization in GridView |
| Custom Page Xrm access | getXrm() helper with parent/top fallback |
| Breaking current EventsPage | Feature flag for gradual rollout |

---

## Success Criteria

1. **ViewSelector** displays all saved views + custom configurations
2. **DataGrid** renders columns dynamically from view definition
3. **FetchXML** executes correctly with calendar/user filters merged
4. **Configuration table** allows admin to create views without deployment
5. **Performance** matches or exceeds current custom implementation
6. **Accessibility** WCAG 2.1 AA compliant (existing Fluent UI)

---

## Appendix: FetchXML Example

```xml
<!-- Complex FetchXML that can't be built in OOB view builder -->
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="true">
  <entity name="sprk_event">
    <attribute name="sprk_eventid" />
    <attribute name="sprk_eventname" />
    <attribute name="sprk_duedate" />
    <attribute name="sprk_eventstatus" />
    <attribute name="ownerid" />
    <filter type="and">
      <condition attribute="statecode" operator="eq" value="0" />
      <condition attribute="sprk_eventstatus" operator="in">
        <value>0</value><!-- Draft -->
        <value>1</value><!-- Open -->
        <value>4</value><!-- On Hold -->
      </condition>
    </filter>
    <link-entity name="sprk_matter" from="sprk_matterid" to="sprk_regardingid" link-type="outer" alias="matter">
      <attribute name="sprk_name" />
      <filter type="and">
        <condition attribute="sprk_matterstatus" operator="eq" value="1" /><!-- Active matters only -->
      </filter>
    </link-entity>
    <order attribute="sprk_duedate" descending="false" />
  </entity>
</fetch>
```

This FetchXML:
- Filters to actionable statuses (Draft, Open, On Hold)
- Joins to Matter with additional filter (Active matters only)
- Can't be fully expressed in OOB view builder

---

*Document Version: 1.0*
*Last Updated: 2026-02-05*
