# Universal Dataset Grid Architecture

> **Status**: Draft
> **Created**: 2026-02-05
> **Domain**: UI Components / PCF / React Code Pages
> **Related ADRs**: [ADR-012](../adr/ADR-012-shared-components.md), [ADR-021](../adr/ADR-021-fluent-ui-design-system.md), [ADR-022](../adr/ADR-022-pcf-platform-libraries.md)

---

## Executive Summary

This document defines the architecture for **universal dataset grid components** that provide consistent, Power Apps-native grid experiences across both PCF controls (form-embedded, React 16/17) and React Code Pages (standalone HTML web resources, React 18). The architecture emphasizes:

1. **Shared codebase** - Core components and services live in a shared library
2. **OOB appearance** - Grid looks identical to Power Apps native grids
3. **Theme compliance** - Full support for light, dark, and high-contrast modes
4. **Configuration-driven** - Runtime configuration via Dataverse table
5. **View selection** - Dropdown to switch between saved views and custom FetchXML

---

## Problem Statement

### Current Gaps

1. **No View Selector** - Custom Pages cannot select different views like OOB grids
2. **Inconsistent appearance** - Custom grids don't match Power Apps native styling
3. **Missing page chrome** - No command bar or view toolbar in Custom Pages
4. **Code duplication** - Grid logic repeated across PCF and Custom Page implementations
5. **Limited configuration** - Views and FetchXML are hardcoded, not runtime-configurable

### Goals

| Goal | Description |
|------|-------------|
| **Visual parity** | Grid indistinguishable from OOB Power Apps grid |
| **Theme support** | Automatic light/dark mode following user preference |
| **View selection** | Dropdown to switch between saved views |
| **Configuration table** | Admin-configurable views without deployment |
| **Code reuse** | Single component library for PCF and Custom Pages |

---

## Architecture Overview

### Component Hierarchy

```
src/client/shared/Spaarke.UI.Components/          # @spaarke/ui-components
├── src/
│   ├── components/
│   │   ├── DatasetGrid/
│   │   │   ├── UniversalDatasetGrid.tsx          # Main orchestrator
│   │   │   ├── GridView.tsx                      # Fluent DataGrid wrapper
│   │   │   ├── VirtualizedGridView.tsx           # Large dataset support
│   │   │   ├── CardView.tsx                      # Card layout
│   │   │   ├── ListView.tsx                      # List layout
│   │   │   ├── ViewSelector.tsx                  # NEW: View dropdown
│   │   │   ├── ViewSelectorContext.tsx           # NEW: View state
│   │   │   └── ColumnHeaderFilter.tsx            # NEW: Header filters
│   │   ├── PageChrome/
│   │   │   ├── CommandBar.tsx                    # NEW: OOB-style command bar
│   │   │   └── ViewToolbar.tsx                   # NEW: View dropdown + search
│   │   └── Toolbar/
│   │       └── CommandToolbar.tsx                # Existing command toolbar
│   ├── services/
│   │   ├── FetchXmlService.ts                    # NEW: Execute FetchXML
│   │   ├── ViewService.ts                        # NEW: Fetch saved views
│   │   ├── ConfigurationService.ts               # NEW: Read grid config
│   │   ├── ColumnRendererService.tsx             # Cell rendering
│   │   ├── PrivilegeService.ts                   # Security privileges
│   │   └── EntityConfigurationService.ts         # Entity-specific config
│   ├── hooks/
│   │   ├── useDatasetMode.ts                     # PCF dataset binding
│   │   ├── useHeadlessMode.ts                    # FetchXML direct query
│   │   └── useViewSelector.ts                    # NEW: View state hook
│   ├── types/
│   │   ├── IViewDefinition.ts                    # NEW: View type
│   │   └── IColumnDefinition.ts                  # Column metadata
│   └── utils/
│       ├── themeDetection.ts                     # Detect Power Platform theme
│       └── xrmContext.ts                         # NEW: Get Xrm from context
└── package.json
```

### Deployment Models

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        @spaarke/ui-components                               │
│                    (Shared Component Library)                               │
│                                                                             │
│   React Version: 16.14 API compatible                                       │
│   peerDependency: "react": ">=16.14.0"                                     │
│   Fluent UI: v9.x (@fluentui/react-components)                             │
│                                                                             │
│   Services are framework-agnostic (receive Xrm as constructor arg)          │
└─────────────────────────────────────────────────────────────────────────────┘
                    │                               │
         ┌──────────┴──────────┐         ┌─────────┴──────────┐
         │                     │         │                    │
         ▼                     │         ▼                    │
┌─────────────────────┐        │   ┌─────────────────────────┐│
│   PCF Controls      │        │   │   Custom Pages (HTML)   ││
│                     │        │   │                         ││
│ React: 16.14        │        │   │ React: 18.x             ││
│ (platform-library)  │        │   │ (bundled)               ││
│                     │        │   │                         ││
│ Use Cases:          │        │   │ Use Cases:              ││
│ - Subgrids on forms │        │   │ - Entity homepages      ││
│ - Simple grids      │        │   │ - Full page control     ││
│ - Canvas embedding  │        │   │ - Side panels           ││
│                     │        │   │ - Custom headers        ││
└─────────────────────┘        │   └─────────────────────────┘│
         │                     │              │               │
         │  platform-library   │              │  bundled      │
         │  declarations       │              │  React 18     │
         └─────────────────────┘              └───────────────┘
```

---

## Visual Design: OOB Parity

### Power Apps Grid Layout

The standard Power Apps entity grid has this visual structure:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ COMMAND BAR (ribbon)                                                        │
│ ┌─────┐ ┌────────┐ ┌────────┐ ┌─────┐ ┌────────┐            ┌────────────┐ │
│ │ New │ │ Delete │ │ Refresh│ │ ... │ │ Custom │            │ Search     │ │
│ └─────┘ └────────┘ └────────┘ └─────┘ └────────┘            └────────────┘ │
├─────────────────────────────────────────────────────────────────────────────┤
│ VIEW TOOLBAR                                                                │
│ ┌─────────────────────┐  ┌───────────────┐  ┌─────────────────────────────┐│
│ │ ▼ Active Events     │  │ Edit filters  │  │ Edit columns               ││
│ └─────────────────────┘  └───────────────┘  └─────────────────────────────┘│
├─────────────────────────────────────────────────────────────────────────────┤
│ DATA GRID                                                                   │
│ ┌──┬─────────────────┬──────────────┬────────────┬───────────┬───────────┐ │
│ │☐ │ Name          ▼ │ Due Date   ▼ │ Status   ▼ │ Owner   ▼ │ Type    ▼ │ │
│ ├──┼─────────────────┼──────────────┼────────────┼───────────┼───────────┤ │
│ │☐ │ Contract Review │ 2026-02-10   │ ● Open     │ J. Smith  │ Task      │ │
│ │☐ │ Filing Deadline │ 2026-02-15   │ ● Draft    │ A. Jones  │ Deadline  │ │
│ │☐ │ Client Meeting  │ 2026-02-20   │ ● On Hold  │ J. Smith  │ Meeting   │ │
│ └──┴─────────────────┴──────────────┴────────────┴───────────┴───────────┘ │
│                                                                             │
│ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ │
│                           [Load more records]                               │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Component Mapping

| OOB Element | Our Component | Notes |
|-------------|---------------|-------|
| Command Bar | `CommandBar.tsx` | Matches ribbon styling |
| View Dropdown | `ViewSelector.tsx` | Fetches from savedquery + config table |
| Edit Filters | Native Fluent DataGrid | Column header filter popups |
| Edit Columns | Future enhancement | Column picker dialog |
| Data Grid | `GridView.tsx` | Fluent UI v9 DataGrid |
| Row Selection | Native Fluent | Checkbox column |
| Status Pills | `ColumnRendererService` | Status badge with color |
| Load More | Native to GridView | Infinite scroll or button |

### Theme Compliance

Per [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md), all UI must:

| Requirement | Implementation |
|-------------|----------------|
| Fluent UI v9 only | `@fluentui/react-components` |
| No hard-coded colors | Use `tokens.colorNeutralBackground1`, etc. |
| FluentProvider wrapper | Wrap all UI in `<FluentProvider theme={...}>` |
| Dark mode support | Detect via `fluentDesignLanguage.isDarkTheme` |
| High contrast | Use `teamsHighContrastTheme` when detected |

#### Theme Detection

```typescript
// Custom Pages: Detect theme from parent window
function detectThemeFromHost(): Theme {
  const xrm = getXrm();

  // Try Power Platform context
  const context = xrm?.Utility?.getGlobalContext?.();
  const isDark = context?.userSettings?.isDarkMode ?? false;

  // Fallback to system preference
  if (isDark || window.matchMedia("(prefers-color-scheme: dark)").matches) {
    return webDarkTheme;
  }
  return webLightTheme;
}
```

#### Spaarke Brand Theme

```typescript
// Use Spaarke brand colors when "Spaarke" theme selected
import { spaarkeLight, spaarkeDark } from "@spaarke/ui-components/theme";

// Theme precedence:
// 1. User explicit choice (Spaarke, Host)
// 2. Host platform theme (from fluentDesignLanguage)
// 3. System preference (prefers-color-scheme)
// 4. Default light theme
```

---

## Data Sources

### View Definition Sources

Views can come from multiple sources, merged in priority order:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          View Sources (Priority Order)                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  1. sprk_gridconfiguration (Dataverse table)                               │
│     - Custom FetchXML views                                                 │
│     - Complex queries not expressible in view builder                       │
│     - Admin-configurable without deployment                                 │
│                                                                             │
│  2. savedquery (System entity)                                              │
│     - System views (Active, Inactive, My, etc.)                            │
│     - Public views created by users                                         │
│     - Standard Dataverse view management                                    │
│                                                                             │
│  3. userquery (System entity) - Future                                      │
│     - Personal views                                                        │
│     - User-specific filters                                                 │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### ViewService Interface

```typescript
interface IViewDefinition {
  id: string;
  name: string;
  type: 'savedquery' | 'userquery' | 'custom';
  fetchXml: string;
  layoutXml: string;
  isDefault: boolean;
  iconName?: string;
  sortOrder?: number;
}

class ViewService {
  constructor(private xrm: XrmContext) {}

  /**
   * Get all views for an entity, merged from all sources
   */
  async getViews(
    entityLogicalName: string,
    options?: {
      includePersonal?: boolean;   // Include userquery
      includeCustom?: boolean;     // Include sprk_gridconfiguration
    }
  ): Promise<IViewDefinition[]>;

  /**
   * Get the default view for an entity
   */
  async getDefaultView(entityLogicalName: string): Promise<IViewDefinition | null>;

  /**
   * Get a specific view by ID
   */
  async getViewById(viewId: string): Promise<IViewDefinition | null>;
}
```

### FetchXmlService Interface

```typescript
interface IFetchXmlResult<T> {
  entities: T[];
  moreRecords: boolean;
  pagingCookie?: string;
  totalRecordCount?: number;
}

class FetchXmlService {
  constructor(private xrm: XrmContext) {}

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
  ): Promise<IFetchXmlResult<T>>;

  /**
   * Parse layoutxml to column definitions
   */
  parseLayoutXml(layoutXml: string): IColumnDefinition[];

  /**
   * Merge additional filter into existing FetchXML
   */
  mergeFetchXmlFilter(fetchXml: string, additionalFilter: string): string;
}
```

---

## Configuration Table: sprk_gridconfiguration

### Purpose

Store custom grid configurations that cannot be expressed in standard Dataverse views:

- Complex FetchXML with linked entity filters
- Aggregate queries (counts, sums, groupings)
- Admin-configurable views without solution deployment
- Fallback views when savedquery fails

### Schema

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
| `sprk_sortorder` | Integer | Display order in dropdown |
| `sprk_iconname` | String (50) | Fluent icon name |
| `statecode` | State | Active/Inactive |
| `statuscode` | Status | Status reason |

### View Type Values

| Value | Name | Description |
|-------|------|-------------|
| 1 | SavedView | References existing savedquery by ID |
| 2 | CustomFetchXML | Uses custom FetchXML in sprk_fetchxml |
| 3 | LinkedView | Reserved for cross-entity scenarios |

### Example: Complex FetchXML

```xml
<!-- This FetchXML cannot be built in OOB view builder -->
<fetch version="1.0" mapping="logical" distinct="true">
  <entity name="sprk_event">
    <attribute name="sprk_eventid" />
    <attribute name="sprk_eventname" />
    <attribute name="sprk_duedate" />
    <attribute name="sprk_eventstatus" />
    <filter type="and">
      <condition attribute="statecode" operator="eq" value="0" />
      <condition attribute="sprk_eventstatus" operator="in">
        <value>0</value><!-- Draft -->
        <value>1</value><!-- Open -->
        <value>4</value><!-- On Hold -->
      </condition>
    </filter>
    <!-- Linked entity with additional filter -->
    <link-entity name="sprk_matter" from="sprk_matterid" to="sprk_regardingid"
                 link-type="outer" alias="matter">
      <attribute name="sprk_name" />
      <filter type="and">
        <condition attribute="sprk_matterstatus" operator="eq" value="1" />
      </filter>
    </link-entity>
    <order attribute="sprk_duedate" descending="false" />
  </entity>
</fetch>
```

---

## Integration Patterns

### PCF Control Integration

```typescript
// src/client/pcf/UniversalDatasetGrid/control/index.ts
import * as React from "react";
import * as ReactDOM from "react-dom"; // React 16 API
import { FluentProvider } from "@fluentui/react-components";
import { UniversalDatasetGrid, detectTheme } from "@spaarke/ui-components";

export class UniversalDatasetGridControl
  implements ComponentFramework.StandardControl<IInputs, IOutputs> {

  private container: HTMLDivElement | null = null;

  public init(context, notifyOutputChanged, state, container): void {
    this.container = container;
    this.render(context);
  }

  public updateView(context): void {
    this.render(context);
  }

  private render(context): void {
    if (!this.container) return;

    const theme = detectTheme(context, "Auto");

    // React 16 render API (NOT createRoot)
    ReactDOM.render(
      React.createElement(FluentProvider, { theme },
        React.createElement(UniversalDatasetGrid, {
          dataset: context.parameters.dataset,
          context: context,
          // Platform handles view selection
        })
      ),
      this.container
    );
  }

  public destroy(): void {
    if (this.container) {
      ReactDOM.unmountComponentAtNode(this.container);
    }
  }
}
```

### Custom Page Integration

```tsx
// src/solutions/EventsPage/src/components/EventsDataGrid.tsx
import * as React from "react";
import { FluentProvider } from "@fluentui/react-components";
import {
  ViewSelector,
  GridView,
  CommandBar,
  ViewToolbar,
  FetchXmlService,
  ViewService,
  IViewDefinition,
  IColumnDefinition,
} from "@spaarke/ui-components";
import { getXrm, detectThemeFromHost } from "../utils/xrmContext";

interface EventsDataGridProps {
  /** Additional filters (calendar, user, etc.) */
  additionalFilter?: string;
  /** Callback when row clicked */
  onRowClick?: (recordId: string, entityType?: string) => void;
  /** Callback for selection changes */
  onSelectionChange?: (selectedIds: string[]) => void;
}

export const EventsDataGrid: React.FC<EventsDataGridProps> = ({
  additionalFilter,
  onRowClick,
  onSelectionChange,
}) => {
  const theme = detectThemeFromHost();
  const xrm = React.useMemo(() => getXrm(), []);

  // Services
  const viewService = React.useMemo(
    () => xrm ? new ViewService(xrm) : null, [xrm]
  );
  const fetchXmlService = React.useMemo(
    () => xrm ? new FetchXmlService(xrm) : null, [xrm]
  );

  // State
  const [selectedView, setSelectedView] = React.useState<IViewDefinition | null>(null);
  const [records, setRecords] = React.useState<any[]>([]);
  const [columns, setColumns] = React.useState<IColumnDefinition[]>([]);
  const [loading, setLoading] = React.useState(true);
  const [selectedIds, setSelectedIds] = React.useState<string[]>([]);

  // Load data when view changes
  const handleViewChange = React.useCallback(async (view: IViewDefinition) => {
    setSelectedView(view);
    setLoading(true);

    if (!fetchXmlService) return;

    try {
      // Parse columns from layout
      const cols = fetchXmlService.parseLayoutXml(view.layoutXml);
      setColumns(cols);

      // Merge additional filter
      let fetchXml = view.fetchXml;
      if (additionalFilter) {
        fetchXml = fetchXmlService.mergeFetchXmlFilter(fetchXml, additionalFilter);
      }

      // Execute query
      const result = await fetchXmlService.executeFetchXml("sprk_event", fetchXml);
      setRecords(result.entities);
    } catch (error) {
      console.error("[EventsDataGrid] Failed to load view:", error);
    } finally {
      setLoading(false);
    }
  }, [fetchXmlService, additionalFilter]);

  return (
    <FluentProvider theme={theme}>
      <div style={{ display: "flex", flexDirection: "column", height: "100%" }}>
        {/* Command Bar - matches OOB ribbon */}
        <CommandBar
          entityLogicalName="sprk_event"
          selectedIds={selectedIds}
          onRefresh={() => selectedView && handleViewChange(selectedView)}
        />

        {/* View Toolbar - matches OOB view selector row */}
        <ViewToolbar>
          <ViewSelector
            entityLogicalName="sprk_event"
            defaultViewName="Active Events"
            onViewChange={handleViewChange}
            includeCustomViews={true}
          />
        </ViewToolbar>

        {/* Data Grid - matches OOB grid appearance */}
        <GridView
          records={records}
          columns={columns}
          loading={loading}
          selectedRecordIds={selectedIds}
          onSelectionChange={(ids) => {
            setSelectedIds(ids);
            onSelectionChange?.(ids);
          }}
          onRecordClick={(record) => onRowClick?.(record.id, record.entityType)}
          enableVirtualization={true}
          rowHeight={44}
          scrollBehavior="Auto"
          hasNextPage={false}
          loadNextPage={() => {}}
        />
      </div>
    </FluentProvider>
  );
};
```

---

## React Version Strategy

### Challenge

- **PCF controls** must use React 16 APIs per [ADR-022](../../.claude/adr/ADR-022-pcf-platform-libraries.md)
- **Custom Pages** can use React 18 (bundled)
- **Shared library** must work with both

### Solution

Write shared components using **React 16 API** only:

| API | React 16 (Use) | React 18 (Avoid) |
|-----|----------------|------------------|
| Render | `ReactDOM.render()` | `createRoot().render()` |
| Unmount | `unmountComponentAtNode()` | `root.unmount()` |
| Hooks | `useState`, `useEffect`, `useMemo`, `useCallback` | `useId`, `useSyncExternalStore` |
| Concurrent | N/A | `startTransition`, Suspense for data |

### Package Configuration

**Shared Library (package.json)**

```json
{
  "name": "@spaarke/ui-components",
  "peerDependencies": {
    "react": ">=16.14.0",
    "react-dom": ">=16.14.0",
    "@fluentui/react-components": "^9.0.0"
  },
  "devDependencies": {
    "react": "^16.14.0",
    "@types/react": "^16.14.0"
  }
}
```

**PCF Control (ControlManifest.Input.xml)**

```xml
<resources>
  <code path="index.ts" order="1" />
  <platform-library name="React" version="16.14.0" />
  <platform-library name="Fluent" version="9.46.2" />
</resources>
```

**Custom Page (package.json)**

```json
{
  "dependencies": {
    "react": "^18.2.0",
    "react-dom": "^18.2.0",
    "@fluentui/react-components": "^9.54.0",
    "@spaarke/ui-components": "workspace:*"
  }
}
```

---

## When to Use PCF vs React Code Page

See [ADR-006](../adr/ADR-006-prefer-pcf-over-webresources.md) for the authoritative surface selection rule. Summary: **field-bound → PCF; standalone dialog/page → React Code Page**.

### Decision Matrix

| Requirement | PCF Control (React 16/17) | React Code Page (React 18) |
|-------------|--------------------------|---------------------------|
| Subgrid on entity form | **Required** | Not possible |
| Standalone list/browse dialog | Not possible (needs wrapper) | **Native** |
| Entity homepage with side panel | Cannot render outside container | **Required** |
| Entity homepage with custom header | Limited | **Full control** |
| Simple grid enhancement on form | Less code, platform handles chrome | Overkill |
| Canvas app embedding | Supported | Not applicable |
| Need React 18 features | Not available (platform constraint) | **Native** |
| Complex page layout | Container-bound | **Full flexibility** |
| Multi-step wizard (e.g. Create Matter) | Not recommended | **Use WizardDialog component** |
| Filter/detail side panel | Not recommended | **Use SidePanel component** |

### Recommendations

| Scenario | Recommendation |
|----------|----------------|
| Form subgrids (dataset binding) | **PCF** — only option for form-embedded datasets |
| Standalone list/browse dialog | **React Code Page** — no form binding needed |
| Entity homepage needing side panels | **React Code Page** — PCF can't render outside container |
| Simple entity homepage enhancement | **PCF** — less code, platform integration |
| Dashboard embedded grid | **PCF** — dashboard tile support |
| Complex entity workspace / wizard | **React Code Page** — full page control, React 18 |

---

## Implementation Roadmap

### Phase 1: Core Services (Foundation)

| Task | Description | Priority |
|------|-------------|----------|
| 1.1 | Create `FetchXmlService` in shared library | High |
| 1.2 | Create `ViewService` for savedquery fetching | High |
| 1.3 | Create `getXrm()` utility for Custom Pages | High |
| 1.4 | Update shared library peerDependencies to `>=16.14.0` | High |
| 1.5 | Unit tests for services | Medium |

### Phase 2: ViewSelector Component

| Task | Description | Priority |
|------|-------------|----------|
| 2.1 | Create `ViewSelector` component | High |
| 2.2 | Create `useViewSelector` hook | Medium |
| 2.3 | Style to match OOB Power Apps dropdown | High |
| 2.4 | Integration tests | Medium |

### Phase 3: Configuration Table

| Task | Description | Priority |
|------|-------------|----------|
| 3.1 | Create `sprk_gridconfiguration` entity schema | Medium |
| 3.2 | Create `ConfigurationService` | Medium |
| 3.3 | Admin form for configuration management | Low |
| 3.4 | Integration with ViewSelector | Medium |

### Phase 4: Page Chrome Components

| Task | Description | Priority |
|------|-------------|----------|
| 4.1 | Create `CommandBar` component | High |
| 4.2 | Create `ViewToolbar` component | High |
| 4.3 | Match OOB Power Apps styling exactly | High |
| 4.4 | Theme testing (light/dark/high-contrast) | Medium |

### Phase 5: Entity Integration

| Task | Description | Priority |
|------|-------------|----------|
| 5.1 | Refactor EventsPage to use shared components | High |
| 5.2 | Validate visual parity with OOB grid | High |
| 5.3 | End-to-end testing | Medium |

---

## Success Criteria

| Criterion | Measurement |
|-----------|-------------|
| **Visual parity** | Side-by-side comparison indistinguishable from OOB |
| **Theme support** | Light, dark, high-contrast all render correctly |
| **View selection** | Dropdown shows all saved views + custom configs |
| **Performance** | <500ms initial load, <100ms view switch |
| **Bundle size** | PCF bundle <1MB (uses platform libraries) |
| **Code reuse** | >80% shared code between PCF and Custom Page |
| **Accessibility** | WCAG 2.1 AA compliant |

---

## Appendix: Xrm Context Utility

```typescript
// src/client/shared/Spaarke.UI.Components/src/utils/xrmContext.ts

declare const Xrm: any;

export interface XrmContext {
  WebApi: {
    retrieveMultipleRecords: (entityName: string, options: string) => Promise<any>;
    retrieveRecord: (entityName: string, id: string, options?: string) => Promise<any>;
    updateRecord: (entityName: string, id: string, data: any) => Promise<any>;
    createRecord: (entityName: string, data: any) => Promise<any>;
    deleteRecord: (entityName: string, id: string) => Promise<any>;
  };
  Navigation: {
    openForm: (options: any) => Promise<any>;
    navigateTo: (options: any) => Promise<any>;
  };
  Utility: {
    getGlobalContext: () => any;
  };
}

/**
 * Get Xrm object from appropriate context.
 * - PCF controls: window.Xrm
 * - Custom Pages (iframe): parent.Xrm
 */
export function getXrm(): XrmContext | undefined {
  // Try window.Xrm first (PCF or direct access)
  if (typeof Xrm !== "undefined" && Xrm?.WebApi) {
    return Xrm;
  }

  // Try parent.Xrm for Custom Pages in iframe
  try {
    if (typeof window !== "undefined" && window.parent) {
      const parentXrm = (window.parent as any).Xrm;
      if (parentXrm?.WebApi) {
        return parentXrm;
      }
    }
  } catch (e) {
    // Cross-origin access denied
    console.debug("[xrmContext] Cannot access parent.Xrm:", e);
  }

  return undefined;
}
```

---

## Related Documentation

| Document | Description |
|----------|-------------|
| [ADR-012: Shared Components](../../.claude/adr/ADR-012-shared-components.md) | Shared component library architecture |
| [ADR-021: Fluent Design System](../../.claude/adr/ADR-021-fluent-design-system.md) | Fluent UI v9 requirements |
| [ADR-022: PCF Platform Libraries](../../.claude/adr/ADR-022-pcf-platform-libraries.md) | React 16 API requirements |
| [KM-UX-FLUENT-DESIGN-V9-STANDARDS.md](../standards/KM-UX-FLUENT-DESIGN-V9-STANDARDS.md) | Detailed design standards |

---

*Document Version: 1.0*
*Last Updated: 2026-02-05*
