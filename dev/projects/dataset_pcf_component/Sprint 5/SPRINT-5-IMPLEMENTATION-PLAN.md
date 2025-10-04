# Sprint 5: Universal Dataset PCF Component - Implementation Plan

**Sprint:** Sprint 5
**Component:** Spaarke.UniversalDataset PCF Control
**Date:** 2025-10-03
**Authors:** Spaarke Engineering Team

---

## Executive Summary

This plan outlines the phased implementation of a **single, universal Dataset PCF component** that works across all Dataverse entities through configuration rather than code changes. The component adheres to all Spaarke ADRs, leverages Fluent UI v9, and supports both Dataset-bound (model-driven apps) and Headless (custom pages) modes.

### Key Design Principles
1. **One Component, Infinite Configurations** - Metadata-driven, no entity-specific code
2. **Component Reusability** - Build shared components once, use everywhere (ADR-012)
3. **ADR Compliance** - Follows ADR-006 (PCF over webresources), ADR-011 (Dataset over subgrids), ADR-010 (DI minimalism), ADR-012 (Shared component library)
4. **Performance First** - Virtual scrolling, <500ms initial render, 10K+ records support
5. **Fluent v9 Only** - Strict adherence to Microsoft's latest design system
6. **Separation of Concerns** - PCF project isolated from API module, shared components extracted to library

---

## Repository Structure & Organization

### Current Repository Layout
```
c:\code_files\spaarke/
├── src/                           # Backend & Shared Code
│   ├── api/Spe.Bff.Api/          # BFF API (.NET - separate concern)
│   └── shared/                    # Shared libraries
│       ├── Spaarke.Core/         # .NET shared library (existing)
│       ├── Spaarke.Dataverse/    # .NET Dataverse library (existing)
│       └── Spaarke.UI.Components/ # 🆕 NEW: Shared React/TypeScript components
├── power-platform/                # Power Platform artifacts
│   ├── plugins/                   # C# Plugins (existing)
│   └── pcf/                       # 🆕 NEW: PCF Controls directory
│       └── UniversalDataset/      # 🆕 Sprint 5 component (CONSUMES shared UI)
├── tests/                         # .NET Tests
├── dev/                           # Development artifacts
│   ├── projects/                  # Project planning docs
│   └── research/                  # Research samples
└── Spaarke.sln                    # Main .NET solution
```

### Component Reusability Strategy (ADR-012)

**Shared Component Library:** `src/shared/Spaarke.UI.Components/`
- **Purpose:** Single source of truth for React/TypeScript components used across PCF, future SPA, and Office Add-ins
- **Consumption:** PCF controls import from `@spaarke/ui-components` package
- **Benefits:** Write once, use everywhere; consistent UX; centralized testing

**What Gets Shared:**
- ✅ Reusable React components (DataGrid, CommandBar, StatusBadge, etc.)
- ✅ Common hooks (usePagination, useSelection, useDataverseFetch)
- ✅ Utility functions (formatters, transformers, validators)
- ✅ TypeScript types and interfaces
- ✅ Fluent UI theme definitions

**What Stays in PCF:**
- ❌ PCF lifecycle code (index.ts)
- ❌ PCF-specific manifest configuration
- ❌ PCF context handling (converts context to props for shared components)

**Example Flow:**
```
PCF Control (power-platform/pcf/UniversalDataset/)
  ├─ index.ts (PCF lifecycle) ────────────────┐
  │                                            │
  │  Converts PCF context to component props  │
  │                                            ▼
  └─ Imports from @spaarke/ui-components ──> Shared Library
                                              (src/shared/Spaarke.UI.Components/)
                                              ├─ DataGrid.tsx
                                              ├─ CommandBar.tsx
                                              ├─ formatters.ts
                                              └─ theme/spaarkeLight.ts
```

### Shared Component Library Structure (NEW)
```
src/shared/Spaarke.UI.Components/   # 🆕 SHARED across all modules
├── package.json                    # NPM package (@spaarke/ui-components)
├── tsconfig.json                   # TypeScript configuration
├── .eslintrc.json                  # ESLint rules
├── src/
│   ├── components/                 # ✅ REUSABLE React components
│   │   ├── DataGrid/               # Generic data grid
│   │   │   ├── DataGrid.tsx
│   │   │   ├── DataGrid.types.ts
│   │   │   └── index.ts
│   │   ├── CommandBar/             # Action toolbar
│   │   ├── StatusBadge/            # Status indicators
│   │   ├── EntityPicker/           # Dataverse entity picker
│   │   └── index.ts                # Barrel export
│   ├── hooks/                      # ✅ SHARED React hooks
│   │   ├── useDataverseFetch.ts    # Dataverse Web API
│   │   ├── usePagination.ts        # Pagination logic
│   │   ├── useSelection.ts         # Selection management
│   │   └── index.ts
│   ├── services/                   # ✅ SHARED business logic
│   │   ├── EntityMetadataService.ts
│   │   ├── CommandExecutor.ts
│   │   └── index.ts
│   ├── renderers/                  # ✅ SHARED column renderers
│   │   ├── TextRenderer.tsx
│   │   ├── LookupRenderer.tsx
│   │   ├── ChoiceRenderer.tsx
│   │   ├── DateTimeRenderer.tsx
│   │   └── index.ts
│   ├── types/                      # ✅ SHARED TypeScript types
│   │   ├── dataverse.ts
│   │   ├── common.ts
│   │   └── index.ts
│   ├── theme/                      # ✅ SHARED Fluent UI themes
│   │   ├── spaarkeLight.ts
│   │   ├── spaarkeDark.ts
│   │   └── index.ts
│   ├── utils/                      # ✅ SHARED utilities
│   │   ├── formatters.ts           # Date, number, currency formatters
│   │   ├── transformers.ts         # Data transformers
│   │   ├── validators.ts           # Validation helpers
│   │   └── index.ts
│   └── index.ts                    # Main entry point
├── __tests__/                      # Tests for shared components
│   ├── components/
│   ├── hooks/
│   └── utils/
└── README.md                       # Shared library documentation
```

### PCF Project Structure (CONSUMES Shared Library)
```
power-platform/pcf/UniversalDataset/
├── .eslintrc.json                 # ESLint configuration
├── .pcfignore                     # PCF ignore patterns
├── package.json                   # NPM dependencies (includes @spaarke/ui-components)
├── tsconfig.json                  # TypeScript configuration
├── ControlManifest.Input.xml      # PCF manifest
├── index.ts                       # ❌ PCF-SPECIFIC: Lifecycle hooks
├── src/                           # PCF-specific code
│   ├── components/                # PCF wrapper components
│   │   └── UniversalDatasetGrid.tsx  # Wrapper that uses shared DataGrid
│   ├── hooks/                     # PCF-specific hooks
│   │   ├── useDatasetMode.ts      # PCF dataset adapter
│   │   └── useHeadlessMode.ts     # Web API adapter
│   └── types/                     # PCF-specific types
│       └── IUniversalDatasetProps.ts  # PCF props interface
├── __tests__/                     # PCF-specific tests
│   ├── unit/
│   ├── integration/
│   └── e2e/
├── css/                           # Minimal CSS (if needed)
│   └── UniversalDataset.css
├── strings/                       # Localization
│   └── UniversalDataset.1033.resx
├── generated/                     # PCF generated files
│   └── ManifestTypes.ts
└── README.md                      # Component documentation
```

**Key Difference:**
- **Before:** All components built in PCF project
- **After:** Shared components in `src/shared/Spaarke.UI.Components/`, PCF uses them via import

### Separation of Concerns - Key Principles

**✅ DO (Component Reusability - ADR-012):**
- Create shared React components in `src/shared/Spaarke.UI.Components/`
- Extract reusable logic (hooks, services, utilities) to shared library
- Use NPM workspace linking for local development
- Version shared library independently
- Test shared components once, benefit all consumers
- Build generic, configurable components (no hard-coded entity logic)

**✅ DO (PCF Structure):**
- Create PCF project under `power-platform/pcf/UniversalDataset/`
- Import shared components via `@spaarke/ui-components` package
- Keep PCF-specific code minimal (lifecycle, context adapter)
- Use separate `package.json` for PCF dependencies
- Build PCF independently using `npm run build` (produces bundle)
- Package PCF as Power Platform solution (`.zip`)

**❌ DON'T:**
- Duplicate React components between shared library and PCF
- Add PCF to `Spaarke.sln` (.NET solution)
- Reference .NET projects from PCF or shared UI library
- Build components in PCF that could be reused in future SPA
- Mix PCF build outputs with API build outputs
- Deploy PCF via Azure App Service (it deploys to Dataverse)

---

## Phase 1: Project Scaffolding & Foundation (10 hours)

### Objectives
- **Set up shared component library** (`src/shared/Spaarke.UI.Components/`) - ADR-012
- Set up isolated PCF project structure
- Configure NPM workspace for component reusability
- Configure tooling (TypeScript, ESLint, PCF CLI)
- Implement PCF lifecycle hooks
- Create minimal React mounting infrastructure

### Tasks

#### Task 1.1: Create Shared Component Library (3 hours)

**Actions:**
```bash
# Create shared component library directory
cd c:\code_files\spaarke\src\shared
mkdir Spaarke.UI.Components
cd Spaarke.UI.Components

# Initialize NPM package
npm init -y
```

**Update `package.json`:**
```json
{
  "name": "@spaarke/ui-components",
  "version": "1.0.0",
  "description": "Shared React/TypeScript component library for Spaarke",
  "main": "dist/index.js",
  "types": "dist/index.d.ts",
  "scripts": {
    "build": "tsc",
    "test": "jest",
    "lint": "eslint src --ext .ts,.tsx",
    "watch": "tsc --watch"
  },
  "peerDependencies": {
    "@fluentui/react-components": "^9.46.2",
    "react": "^18.2.0",
    "react-dom": "^18.2.0"
  },
  "devDependencies": {
    "@types/react": "^18.2.0",
    "@types/react-dom": "^18.2.0",
    "typescript": "^5.3.3",
    "jest": "^29.7.0",
    "@testing-library/react": "^14.0.0",
    "@testing-library/jest-dom": "^6.1.5",
    "eslint": "^9.17.0"
  }
}
```

**Create directory structure:**
```bash
mkdir -p src/{components,hooks,services,renderers,types,theme,utils}
mkdir -p __tests__/{components,hooks,utils}
```

**Create `tsconfig.json`:**
```json
{
  "compilerOptions": {
    "target": "ES2020",
    "module": "ESNext",
    "lib": ["ES2020", "DOM"],
    "jsx": "react",
    "declaration": true,
    "outDir": "./dist",
    "rootDir": "./src",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "moduleResolution": "node"
  },
  "include": ["src/**/*"],
  "exclude": ["node_modules", "dist", "__tests__"]
}
```

**Create `src/index.ts` (barrel export):**
```typescript
// Components
export * from "./components";

// Hooks
export * from "./hooks";

// Services
export * from "./services";

// Renderers
export * from "./renderers";

// Types
export * from "./types";

// Theme
export * from "./theme";

// Utils
export * from "./utils";
```

**Create root workspace configuration:**
```bash
cd c:\code_files\spaarke
```

**Create/Update root `package.json`:**
```json
{
  "name": "spaarke-workspace",
  "private": true,
  "workspaces": [
    "src/shared/Spaarke.UI.Components",
    "power-platform/pcf/*"
  ]
}
```

**Install workspace dependencies:**
```bash
npm install
```

**Expected Output:**
- ✅ `src/shared/Spaarke.UI.Components/` directory created
- ✅ NPM package initialized with peer dependencies
- ✅ TypeScript configured for component library
- ✅ Workspace linking configured

**Validation:**
```bash
cd src/shared/Spaarke.UI.Components
npm run build  # Should succeed (empty project)
```

#### Task 1.2: Initialize PCF Project (2 hours)
**Actions:**
```bash
# Navigate to power-platform directory
cd c:\code_files\spaarke\power-platform

# Create pcf directory if not exists
mkdir -p pcf
cd pcf

# Initialize PCF project
pac pcf init \
  --namespace Spaarke \
  --name UniversalDataset \
  --template dataset \
  --run-npm-install

cd UniversalDataset
```

**Expected Output:**
- `ControlManifest.Input.xml` created
- `index.ts` scaffold created
- `package.json` with PCF dependencies
- `tsconfig.json` with PCF TypeScript config

**Validation:**
```bash
npm run build
# Should compile without errors
```

#### Task 1.3: Configure PCF Dependencies (1 hour)
**Update `package.json`:**
```json
{
  "dependencies": {
    "@spaarke/ui-components": "workspace:*",  // 🆕 Link to shared library
    "@fluentui/react-components": "^9.46.2",
    "@fluentui/react-icons": "^2.0.220",
    "react": "^18.2.0",
    "react-dom": "^18.2.0",
    "react-window": "^1.8.10"
  },
  "devDependencies": {
    "@microsoft/eslint-plugin-power-apps": "^0.2.51",
    "@testing-library/react": "^14.0.0",
    "@testing-library/jest-dom": "^6.1.5",
    "@types/react": "^18.2.0",
    "@types/react-dom": "^18.2.0",
    "@types/react-window": "^1.8.8",
    "@playwright/test": "^1.40.0",
    "eslint": "^9.17.0",
    "jest": "^29.7.0",
    "typescript": "^5.3.3",
    "pcf-scripts": "^1",
    "pcf-start": "^1"
  },
  "scripts": {
    "build": "pcf-scripts build",
    "clean": "pcf-scripts clean",
    "rebuild": "pcf-scripts rebuild",
    "start": "pcf-scripts start watch",
    "test": "jest --coverage",
    "test:watch": "jest --watch",
    "test:e2e": "playwright test",
    "lint": "eslint src --ext .ts,.tsx",
    "lint:fix": "eslint src --ext .ts,.tsx --fix"
  }
}
```

**Run:**
```bash
npm install  # Automatically links to @spaarke/ui-components via workspace
```

**Validation:**
```bash
# Verify workspace link
ls node_modules/@spaarke  # Should see symlink to ../../src/shared/Spaarke.UI.Components
```

#### Task 1.4: Implement PCF Lifecycle (2 hours)
**Create `index.ts`:**
```typescript
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { UniversalDatasetGrid } from "./src/components/UniversalDatasetGrid";

export class UniversalDataset implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private container: HTMLDivElement;
  private notifyOutputChanged: () => void;
  private context: ComponentFramework.Context<IInputs>;

  // Output state
  private selectedRecordIds: string[] = [];
  private totalRecordCount: number = 0;
  private lastAction: string = "";

  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this.container = container;
    this.notifyOutputChanged = notifyOutputChanged;
    this.context = context;

    // Enable responsive sizing
    context.mode.trackContainerResize(true);
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.context = context;

    // Build React props from PCF context
    const props = {
      context,
      dataset: context.parameters.dataset,

      // Mode configuration
      componentMode: context.parameters.componentMode?.raw as "Auto" | "Dataset" | "Headless" ?? "Auto",
      viewMode: context.parameters.viewMode?.raw as "Grid" | "Card" | "List" ?? "Grid",

      // Data access (headless mode)
      tableName: context.parameters.tableName?.raw,
      viewId: context.parameters.viewId?.raw,
      fetchXml: context.parameters.fetchXml?.raw,

      // Rendering config
      columnBehavior: this.parseJson(context.parameters.columnBehavior?.raw),
      density: context.parameters.density?.raw as "Compact" | "Standard" | "Comfortable" ?? "Standard",

      // Commands
      enabledCommands: context.parameters.enabledCommands?.raw ?? "open,create,delete,refresh",
      commandConfig: this.parseJson(context.parameters.commandConfig?.raw),
      primaryAction: context.parameters.primaryAction?.raw as "Open" | "Select" | "QuickView" | "None" ?? "Open",

      // Display options
      showToolbar: context.parameters.showToolbar?.raw ?? true,
      showSearch: context.parameters.showSearch?.raw ?? true,
      showPaging: context.parameters.showPaging?.raw ?? true,
      pageSize: context.parameters.pageSize?.raw ?? 25,
      emptyStateText: context.parameters.emptyStateText?.raw ?? "No records found",
      title: context.parameters.title?.raw,

      // Callbacks
      onSelectionChange: (ids: string[]) => {
        this.selectedRecordIds = ids;
        this.notifyOutputChanged();
      },
      onRecordCountChange: (count: number) => {
        this.totalRecordCount = count;
        this.notifyOutputChanged();
      },
      onAction: (action: string) => {
        this.lastAction = action;
        this.notifyOutputChanged();
      }
    };

    // Render React component
    ReactDOM.render(
      React.createElement(UniversalDatasetGrid, props),
      this.container
    );
  }

  public getOutputs(): IOutputs {
    return {
      selectedRecordIds: this.selectedRecordIds.join(","),
      totalRecordCount: this.totalRecordCount,
      lastAction: this.lastAction
    } as any;
  }

  public destroy(): void {
    ReactDOM.unmountComponentAtNode(this.container);
  }

  private parseJson(value: string | undefined): any {
    if (!value) return undefined;
    try {
      return JSON.parse(value);
    } catch {
      console.warn("Failed to parse JSON:", value);
      return undefined;
    }
  }
}
```

**Create `src/types/IUniversalDatasetProps.ts`:**
```typescript
import { ComponentFramework } from "@microsoft/pcf-control";

export interface IUniversalDatasetProps {
  context: ComponentFramework.Context<any>;
  dataset: ComponentFramework.PropertyTypes.DataSet;

  // Mode
  componentMode: "Auto" | "Dataset" | "Headless";
  viewMode: "Grid" | "Card" | "List";

  // Data access
  tableName?: string;
  viewId?: string;
  fetchXml?: string;

  // Rendering
  columnBehavior?: Record<string, any>;
  density: "Compact" | "Standard" | "Comfortable";

  // Commands
  enabledCommands: string;
  commandConfig?: Record<string, any>;
  primaryAction: "Open" | "Select" | "QuickView" | "None";

  // Display
  showToolbar: boolean;
  showSearch: boolean;
  showPaging: boolean;
  pageSize: number;
  emptyStateText: string;
  title?: string;

  // Callbacks
  onSelectionChange: (ids: string[]) => void;
  onRecordCountChange: (count: number) => void;
  onAction: (action: string) => void;
}
```

#### Task 1.4: Create Manifest (2 hours)
**Reference:** Use `DATASET-COMPONENT-MANIFEST.md` as blueprint

**Update `ControlManifest.Input.xml`:**
```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest>
  <control
    namespace="Spaarke"
    constructor="UniversalDataset"
    version="1.0.0"
    display-name-key="Universal_Dataset_Display"
    description-key="Universal_Dataset_Description"
    control-type="dataset">

    <!-- Primary Dataset Binding -->
    <data-set
      name="dataset"
      display-name-key="Dataset"
      cds-data-set-options="DisplayCommandBar:false;DisplayViewSelector:false">
    </data-set>

    <!-- Mode Configuration -->
    <property name="componentMode" of-type="Enum" usage="input" required="false" default-value="Auto">
      <value name="Auto" display-name-key="Mode_Auto">Auto Detect</value>
      <value name="Dataset" display-name-key="Mode_Dataset">Dataset Bound</value>
      <value name="Headless" display-name-key="Mode_Headless">Headless</value>
    </property>

    <property name="viewMode" of-type="Enum" usage="input" required="false" default-value="Grid">
      <value name="Grid" display-name-key="View_Grid">Grid</value>
      <value name="Card" display-name-key="View_Card">Card</value>
      <value name="List" display-name-key="View_List">List</value>
    </property>

    <!-- Data Access (Headless Mode) -->
    <property name="tableName" of-type="SingleLine.Text" usage="input" required="false"
              display-name-key="Table_Name" description-key="Table_Name_Desc"/>
    <property name="viewId" of-type="SingleLine.Text" usage="input" required="false"
              display-name-key="View_ID" description-key="View_ID_Desc"/>
    <property name="fetchXml" of-type="Multiple" usage="input" required="false"
              display-name-key="Fetch_XML" description-key="Fetch_XML_Desc"/>

    <!-- Rendering Configuration -->
    <property name="columnBehavior" of-type="Multiple" usage="input" required="false"
              display-name-key="Column_Behavior" description-key="JSON_column_overrides"/>
    <property name="density" of-type="Enum" usage="input" required="false" default-value="Standard">
      <value name="Compact">Compact</value>
      <value name="Standard">Standard</value>
      <value name="Comfortable">Comfortable</value>
    </property>

    <!-- Command Configuration -->
    <property name="enabledCommands" of-type="Multiple" usage="input" required="false"
              default-value="open,create,delete,refresh"
              display-name-key="Enabled_Commands"/>
    <property name="commandConfig" of-type="Multiple" usage="input" required="false"
              display-name-key="Command_Config" description-key="JSON_command_defs"/>
    <property name="primaryAction" of-type="Enum" usage="input" required="false" default-value="Open">
      <value name="Open">Open Record</value>
      <value name="Select">Select</value>
      <value name="QuickView">Quick View</value>
      <value name="None">None</value>
    </property>

    <!-- Display Options -->
    <property name="showToolbar" of-type="TwoOptions" usage="input" required="false" default-value="true"/>
    <property name="showSearch" of-type="TwoOptions" usage="input" required="false" default-value="true"/>
    <property name="showPaging" of-type="TwoOptions" usage="input" required="false" default-value="true"/>
    <property name="pageSize" of-type="Whole.None" usage="input" required="false" default-value="25"/>
    <property name="emptyStateText" of-type="SingleLine.Text" usage="input" required="false"
              default-value="No records found"/>
    <property name="title" of-type="SingleLine.Text" usage="input" required="false"/>

    <!-- Output Properties -->
    <property name="selectedRecordIds" of-type="Multiple" usage="output"/>
    <property name="totalRecordCount" of-type="Whole.None" usage="output"/>
    <property name="lastAction" of-type="SingleLine.Text" usage="output"/>

    <!-- Resources -->
    <resources>
      <code path="index.ts" order="1"/>
      <platform-library name="React" version="18.2.0"/>
      <platform-library name="Fluent" version="9.46.2"/>
      <css path="css/UniversalDataset.css" order="2"/>
      <resx path="strings/UniversalDataset.1033.resx" version="1.0.0"/>
    </resources>

    <!-- Feature Usage -->
    <feature-usage>
      <uses-feature name="WebAPI" required="true"/>
      <uses-feature name="Navigation" required="true"/>
    </feature-usage>
  </control>
</manifest>
```

**Create `strings/UniversalDataset.1033.resx`:**
```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="Universal_Dataset_Display">
    <value>Universal Dataset Grid</value>
  </data>
  <data name="Universal_Dataset_Description">
    <value>A flexible, configurable dataset component that works with any Dataverse entity</value>
  </data>
  <data name="Dataset">
    <value>Records</value>
  </data>
  <!-- Add remaining keys... -->
</root>
```

**Validation:**
```bash
npm run build
# Should generate ManifestTypes.ts
```

### Phase 1 Deliverables
- ✅ PCF project initialized under `power-platform/pcf/UniversalDataset/`
- ✅ Dependencies installed (React 18, Fluent v9, TypeScript 5)
- ✅ PCF lifecycle implemented (`init`, `updateView`, `getOutputs`, `destroy`)
- ✅ Manifest configured with all properties
- ✅ Build succeeds without errors
- ✅ No references to BFF API or .NET projects

---

## Phase 2: Core React Infrastructure (12 hours)

### Objectives
- Implement main React component with Fluent Provider
- Create Dataset and Headless mode hooks
- Implement basic Grid view
- Set up theme infrastructure

### Tasks

#### Task 2.1: Main React Component (3 hours)
**Create `src/components/UniversalDatasetGrid.tsx`:**
```typescript
import * as React from "react";
import { FluentProvider, webLightTheme, webDarkTheme } from "@fluentui/react-components";
import { IUniversalDatasetProps } from "../types/IUniversalDatasetProps";
import { useDatasetMode } from "../hooks/useDatasetMode";
import { useHeadlessMode } from "../hooks/useHeadlessMode";
import { GridView } from "./views/GridView";
import { CardView } from "./views/CardView";
import { ListView } from "./views/ListView";
import { EmptyState } from "./EmptyState";

export const UniversalDatasetGrid: React.FC<IUniversalDatasetProps> = (props) => {
  // Determine data mode
  const isHeadlessMode = props.componentMode === "Headless" ||
    (props.componentMode === "Auto" && !props.dataset);

  // Use appropriate hook
  const dataSource = isHeadlessMode
    ? useHeadlessMode(props)
    : useDatasetMode(props);

  // Select view component
  const ViewComponent = React.useMemo(() => {
    switch (props.viewMode) {
      case "Card": return CardView;
      case "List": return ListView;
      default: return GridView;
    }
  }, [props.viewMode]);

  // Detect theme
  const hostTheme = (props.context as any).fluentDesignLanguage?.tokenTheme;
  const theme = hostTheme ?? webLightTheme;

  // Empty state
  if (!dataSource.loading && dataSource.items.length === 0) {
    return (
      <FluentProvider theme={theme}>
        <EmptyState message={props.emptyStateText} />
      </FluentProvider>
    );
  }

  return (
    <FluentProvider theme={theme}>
      <ViewComponent
        data={dataSource}
        config={props}
        onAction={props.onAction}
        onSelectionChange={props.onSelectionChange}
      />
    </FluentProvider>
  );
};
```

#### Task 2.2: Dataset Mode Hook (3 hours)
**Create `src/hooks/useDatasetMode.ts`:**
```typescript
import * as React from "react";
import { IUniversalDatasetProps } from "../types/IUniversalDatasetProps";
import { IDataSource, IDataRow } from "../types/IDataSource";
import { transformRecord } from "../utils/transformers";

export function useDatasetMode(props: IUniversalDatasetProps): IDataSource {
  const dataset = props.dataset;

  // Transform records
  const items = React.useMemo<IDataRow[]>(() => {
    if (!dataset || dataset.loading) return [];

    const recordIds = dataset.sortedRecordIds ?? [];
    return recordIds.map(id => {
      const record = dataset.records[id];
      return transformRecord(record, dataset.columns);
    });
  }, [dataset?.records, dataset?.sortedRecordIds, dataset?.columns, dataset?.loading]);

  // Load more handler
  const loadMore = React.useCallback(() => {
    if (dataset?.paging?.hasNextPage) {
      dataset.paging.loadNextPage();
    }
  }, [dataset?.paging]);

  // Refresh handler
  const refresh = React.useCallback(() => {
    dataset?.refresh();
  }, [dataset]);

  // Update record count
  React.useEffect(() => {
    props.onRecordCountChange(items.length);
  }, [items.length]);

  return {
    items,
    loading: dataset?.loading ?? false,
    hasMore: dataset?.paging?.hasNextPage ?? false,
    loadMore,
    refresh
  };
}
```

**Create `src/types/IDataSource.ts`:**
```typescript
export interface IDataRow {
  id: string;
  entityName: string;
  columns: Record<string, any>;
  formattedValues: Record<string, string>;
}

export interface IDataSource {
  items: IDataRow[];
  loading: boolean;
  hasMore: boolean;
  loadMore: () => void;
  refresh: () => void;
}
```

**Create `src/utils/transformers.ts`:**
```typescript
import DataSetInterfaces = ComponentFramework.PropertyHelper.DataSetApi;

export function transformRecord(
  record: DataSetInterfaces.EntityRecord,
  columns: DataSetInterfaces.Column[]
): IDataRow {
  const columnValues: Record<string, any> = {};
  const formattedValues: Record<string, string> = {};

  columns.forEach(col => {
    columnValues[col.name] = record.getValue(col.name);
    formattedValues[col.name] = record.getFormattedValue(col.name) ?? "";
  });

  const ref = record.getNamedReference();

  return {
    id: record.getRecordId(),
    entityName: ref?.name ?? "",
    columns: columnValues,
    formattedValues
  };
}
```

#### Task 2.3: Headless Mode Hook (3 hours)
**Create `src/hooks/useHeadlessMode.ts`:**
```typescript
import * as React from "react";
import { IUniversalDatasetProps } from "../types/IUniversalDatasetProps";
import { IDataSource, IDataRow } from "../types/IDataSource";

export function useHeadlessMode(props: IUniversalDatasetProps): IDataSource {
  const [items, setItems] = React.useState<IDataRow[]>([]);
  const [loading, setLoading] = React.useState(false);
  const [nextLink, setNextLink] = React.useState<string | undefined>();

  const fetchData = React.useCallback(async () => {
    if (!props.tableName) {
      console.warn("Headless mode requires tableName property");
      return;
    }

    setLoading(true);
    try {
      // Build query
      const options = props.fetchXml
        ? `?fetchXml=${encodeURIComponent(props.fetchXml)}`
        : `?$top=${props.pageSize}`;

      const result = await props.context.webAPI.retrieveMultipleRecords(
        props.tableName,
        options
      );

      const transformed = result.entities.map(entity => ({
        id: entity[`${props.tableName}id`],
        entityName: props.tableName!,
        columns: entity,
        formattedValues: Object.fromEntries(
          Object.keys(entity).map(k => [k, String(entity[k] ?? "")])
        )
      }));

      setItems(prev => [...prev, ...transformed]);
      setNextLink(result.nextLink);
      props.onRecordCountChange(items.length + transformed.length);
    } catch (error) {
      console.error("Error fetching data in headless mode:", error);
    } finally {
      setLoading(false);
    }
  }, [props.tableName, props.fetchXml, props.pageSize]);

  // Initial fetch
  React.useEffect(() => {
    fetchData();
  }, [fetchData]);

  const loadMore = React.useCallback(() => {
    if (nextLink) {
      fetchData();
    }
  }, [nextLink, fetchData]);

  const refresh = React.useCallback(() => {
    setItems([]);
    setNextLink(undefined);
    fetchData();
  }, [fetchData]);

  return {
    items,
    loading,
    hasMore: !!nextLink,
    loadMore,
    refresh
  };
}
```

#### Task 2.4: Basic Grid View (3 hours)
**Create `src/components/views/GridView.tsx`:**
```typescript
import * as React from "react";
import {
  DataGrid,
  DataGridHeader,
  DataGridRow,
  DataGridHeaderCell,
  DataGridBody,
  DataGridCell,
  TableCellLayout,
  Button
} from "@fluentui/react-components";
import { IDataSource } from "../../types/IDataSource";
import { IUniversalDatasetProps } from "../../types/IUniversalDatasetProps";

interface IGridViewProps {
  data: IDataSource;
  config: IUniversalDatasetProps;
  onAction: (action: string) => void;
  onSelectionChange: (ids: string[]) => void;
}

export const GridView: React.FC<IGridViewProps> = ({ data, config, onAction, onSelectionChange }) => {
  const [selectedIds, setSelectedIds] = React.useState<Set<string>>(new Set());

  // Get columns from first item
  const columns = React.useMemo(() => {
    if (data.items.length === 0) return [];
    return Object.keys(data.items[0].formattedValues);
  }, [data.items]);

  const handleRowClick = (id: string) => {
    if (config.primaryAction === "Select") {
      const newSelection = new Set(selectedIds);
      if (newSelection.has(id)) {
        newSelection.delete(id);
      } else {
        newSelection.add(id);
      }
      setSelectedIds(newSelection);
      onSelectionChange(Array.from(newSelection));
    } else if (config.primaryAction === "Open") {
      // Open record
      const item = data.items.find(i => i.id === id);
      if (item) {
        config.context.navigation.openForm({
          entityName: item.entityName,
          entityId: id
        });
      }
    }
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%" }}>
      <DataGrid items={data.items} columns={columns} sortable>
        <DataGridHeader>
          <DataGridRow>
            {columns.map(col => (
              <DataGridHeaderCell key={col}>{col}</DataGridHeaderCell>
            ))}
          </DataGridRow>
        </DataGridHeader>
        <DataGridBody>
          {data.items.map(item => (
            <DataGridRow
              key={item.id}
              onClick={() => handleRowClick(item.id)}
              style={{ cursor: "pointer" }}
            >
              {columns.map(col => (
                <DataGridCell key={col}>
                  <TableCellLayout truncate>
                    {item.formattedValues[col]}
                  </TableCellLayout>
                </DataGridCell>
              ))}
            </DataGridRow>
          ))}
        </DataGridBody>
      </DataGrid>

      {config.showPaging && data.hasMore && (
        <Button onClick={data.loadMore} disabled={data.loading}>
          Load More
        </Button>
      )}
    </div>
  );
};
```

**Create `src/components/EmptyState.tsx`:**
```typescript
import * as React from "react";
import { makeStyles, tokens } from "@fluentui/react-components";

const useStyles = makeStyles({
  container: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "200px",
    color: tokens.colorNeutralForeground3
  }
});

export const EmptyState: React.FC<{ message: string }> = ({ message }) => {
  const styles = useStyles();
  return <div className={styles.container}>{message}</div>;
};
```

### Phase 2 Deliverables
- ✅ Main React component with FluentProvider
- ✅ Dataset mode hook (reads from PCF dataset)
- ✅ Headless mode hook (calls Web API)
- ✅ Basic Grid view rendering
- ✅ Empty state handling
- ✅ Selection and navigation working

---

## Phase 3: Advanced Features (16 hours)

### Tasks

#### Task 3.1: Command System (4 hours)
- Implement `CommandRegistry` with built-in commands (open, create, delete, refresh)
- Implement `CommandExecutor` with validation and error handling
- Create `CommandToolbar` component with Fluent v9 Toolbar
- Support custom JSON command configuration

**Reference:** `DATASET-COMPONENT-COMMANDS.md`

#### Task 3.2: Column Renderers (4 hours)
- Create `ColumnRendererFactory` for type-to-renderer mapping
- Implement renderers for: Text, Lookup, Choice, DateTime, Currency, Boolean
- Use Fluent v9 components (TableCellLayout, Badge, Link, Avatar)
- Support custom renderer overrides via `columnBehavior` prop

#### Task 3.3: Card & List Views (4 hours)
- Implement `CardView.tsx` (tile-based layout)
- Implement `ListView.tsx` (compact single-column)
- Use Fluent v9 Card component
- Support density configuration (Compact/Standard/Comfortable)

#### Task 3.4: Virtual Scrolling (4 hours)
- Integrate `react-window` for virtualization
- Implement `useVirtualization` hook
- Test with 10K+ records
- Ensure <100 DOM elements for large datasets

---

## Phase 4: Testing & Quality (12 hours)

### Tasks

#### Task 4.1: Unit Tests (6 hours)
**Reference:** `DATASET-COMPONENT-TESTING.md`

- Test components: UniversalDatasetGrid, GridView, CardView, ListView
- Test hooks: useDatasetMode, useHeadlessMode
- Test services: CommandRegistry, CommandExecutor, ColumnRendererFactory
- Target: 80% code coverage

**Setup Jest:**
```bash
npm install --save-dev jest @testing-library/react @testing-library/jest-dom
```

**Example test:**
```typescript
// __tests__/unit/hooks/useDatasetMode.test.ts
import { renderHook } from "@testing-library/react";
import { useDatasetMode } from "../../../src/hooks/useDatasetMode";
import { createMockDataset } from "../../fixtures/mockDataset";

describe("useDatasetMode", () => {
  it("transforms records correctly", () => {
    const dataset = createMockDataset("account", [
      { id: "1", name: "Test Account" }
    ]);
    const { result } = renderHook(() => useDatasetMode({ dataset } as any));

    expect(result.current.items).toHaveLength(1);
    expect(result.current.items[0].id).toBe("1");
  });
});
```

#### Task 4.2: Integration Tests (3 hours)
- Test dataset binding end-to-end
- Test command execution (open, delete, refresh)
- Test navigation integration

#### Task 4.3: E2E Tests (3 hours)
- Set up Playwright
- Test sorting, selection, delete workflow
- Test virtualization with 10K records
- Test performance (<500ms first render)

---

## Phase 5: Documentation & Deployment (8 hours)

### Tasks

#### Task 5.1: Component Documentation (3 hours)
**Create `power-platform/pcf/UniversalDataset/README.md`:**
- Component overview and capabilities
- Property reference
- Usage examples for common scenarios
- Configuration recipes (Document Library, Job Status, etc.)
- Troubleshooting guide

#### Task 5.2: Build & Package (2 hours)
```bash
# Build release
npm run build -- --configuration Release

# Create solution
cd ../..  # Back to power-platform directory
pac solution init --publisher-name Spaarke --publisher-prefix spe
pac solution add-reference --path ./pcf/UniversalDataset
msbuild /t:build /restore /p:Configuration=Release
```

**Output:** `SpaarkePCFControls_1_0_0_0.zip` in `bin/Release/`

#### Task 5.3: Deployment Guide (2 hours)
**Create `docs/PCF-DEPLOYMENT-GUIDE.md`:**
- Import solution to Dataverse
- Add control to model-driven app
- Configuration examples
- Environment-specific settings

#### Task 5.4: Sprint Summary (1 hour)
**Create `dev/projects/dataset_pcf_component/Sprint 5/SPRINT-5-COMPLETION.md`:**
- Summary of deliverables
- Testing results
- Known limitations
- Recommendations for Sprint 6

---

## ADR Compliance Checklist

### ADR-012: Shared Component Library (NEW)
- ✅ Create `src/shared/Spaarke.UI.Components/` for reusable React components
- ✅ Use NPM workspace linking for local development
- ✅ Extract DataGrid, hooks, formatters, renderers to shared library
- ✅ PCF imports from `@spaarke/ui-components` package
- ✅ Generic components work in PCF, future SPA, Office Add-ins
- ✅ Single source of truth for UI patterns

### ADR-006: Prefer PCF Controls Over Web Resources
- ✅ Using PCF framework (TypeScript, React)
- ✅ No legacy JavaScript webresources
- ✅ Packaged as Power Platform solution
- ✅ Testable with Jest and Playwright

### ADR-011: Dataset PCF Controls Over Native Subgrids
- ✅ Implements Dataset PCF control
- ✅ Reusable across entities via configuration
- ✅ Custom actions via command system
- ✅ Virtual scrolling for performance
- ✅ Fluent UI React for consistency

### ADR-010: DI Minimalism
- ✅ No dependency injection in PCF (React hooks pattern)
- ✅ Services as simple classes in shared library
- ✅ Stateless functions where possible
- ✅ Minimal abstraction layers

### ADR-002: No Heavy Plugins
- ✅ No C# plugins for UI logic
- ✅ PCF handles all client-side operations
- ✅ Web API calls for data (no custom APIs needed initially)

---

## Best Practices from Senior MVP Developer Perspective

**CRITICAL REFERENCES:**
- **[KM-PCF-CONTROL-STANDARDS.md](../../../docs/KM-PCF-CONTROL-STANDARDS.md)** - PCF development standards
- **[KM-UX-FLUENT-DESIGN-V9-STANDARDS.md](../../../docs/KM-UX-FLUENT-DESIGN-V9-STANDARDS.md)** - Fluent UI v9 requirements

### 1. **Fluent UI v9 Strict Adherence** (KM-UX-FLUENT-DESIGN-V9-STANDARDS.md)

**MANDATORY - Zero Tolerance for Violations:**

**Package Requirements:**
```bash
# ✅ ONLY these packages allowed
npm install @fluentui/react-components@^9.46.2
npm install @fluentui/react-icons@^2.0.220

# ❌ NEVER install these
# npm install @fluentui/react  (v8 - PROHIBITED)
```

**Provider Pattern (Required):**
```tsx
// Single FluentProvider at app root
import { FluentProvider } from "@fluentui/react-components";
import { spaarkeLight } from "@spaarke/ui-components/theme";

// PCF: Detect host theme, fallback to Spaarke
const hostTheme = (context as any).fluentDesignLanguage?.tokenTheme;
const theme = hostTheme ?? spaarkeLight;

<FluentProvider theme={theme}>
  <UniversalDatasetGrid {...props} />
</FluentProvider>
```

**Styling Pattern (Griffel - Required):**
```tsx
import { makeStyles, shorthands, tokens, mergeClasses } from "@fluentui/react-components";

// ✅ Correct: Use tokens, shorthands, makeStyles
const useStyles = makeStyles({
  root: {
    display: "grid",
    ...shorthands.gap(tokens.spacingHorizontalM),
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorNeutralBackground1,
  }
});

// ❌ Prohibited: Hard-coded values
const badStyles = {
  padding: "16px",          // Use tokens.spacingHorizontalM
  backgroundColor: "#fff"   // Use tokens.colorNeutralBackground1
};
```

**DataGrid Pattern (Required for tables):**
```tsx
import {
  DataGrid, DataGridBody, DataGridRow, DataGridCell,
  TableColumnDefinition, createTableColumn, TableCellLayout
} from "@fluentui/react-components";

const columns: TableColumnDefinition<Row>[] = [
  createTableColumn<Row>({
    columnId: "name",
    compare: (a, b) => a.name.localeCompare(b.name),
    renderHeaderCell: () => "Name",
    renderCell: item => <TableCellLayout truncate>{item.name}</TableCellLayout>
  })
];

<DataGrid
  items={items}
  columns={columns}
  sortable
  selectionMode="multiselect"
  getRowId={item => item.id}
>
  <DataGridBody>
    {({ item }) => (
      <DataGridRow key={item.id}>
        {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
      </DataGridRow>
    )}
  </DataGridBody>
</DataGrid>
```

**Accessibility Requirements:**
```tsx
// ✅ Icon-only buttons MUST have aria-label
<Button aria-label="Upload document" icon={<ArrowUpload20Regular />} />

// ✅ Async state changes MUST announce to screen readers
<div role="status" aria-live="polite">{statusMessage}</div>

// ✅ Maintain WCAG AA contrast
// - Body text: 4.5:1
// - Components: 3:1
```

**Performance Requirements:**
```tsx
// ✅ Virtualize lists with >100 items
import { useVirtual } from "@tanstack/react-virtual";

// ✅ Memoize components and callbacks
const MemoizedRow = React.memo(DataGridRow);
const handleClick = useCallback(() => { /* ... */ }, [deps]);
```

**Prohibited Patterns:**
```tsx
// ❌ NEVER import v8
import { PrimaryButton } from "@fluentui/react";  // v8 - PROHIBITED

// ❌ NEVER hard-code colors
style={{ color: "#0078d4" }}  // Use tokens.colorBrandForeground1

// ❌ NEVER query/mutate DOM
document.querySelector(".ms-Button").style.color = "red";

// ❌ NEVER render outside FluentProvider
ReactDOM.render(<MyComponent />, container);  // Missing provider
```

**Compliance Checklist (Copy to PR template):**
- [ ] Uses `@fluentui/react-components` v9 only (no v8 imports)
- [ ] Wrapped in single app-level `FluentProvider`
- [ ] All custom CSS via `makeStyles`; tokens only
- [ ] A11y labels provided; keyboard flow validated
- [ ] Contrast meets WCAG AA (4.5:1 text, 3:1 components)
- [ ] Lists virtualized when `items.length > 100`
- [ ] Unit test uses `renderWithFluent` helper

### 2. **PCF Development Standards** (KM-PCF-CONTROL-STANDARDS.md)

**Project Structure:**
```bash
# ✅ Correct PCF initialization
pac pcf init --namespace Spaarke --name UniversalDataset --template dataset
npm install react react-dom @types/react @types/react-dom
npm install @spaarke/ui-components@workspace:*
```

**Lifecycle Management:**
```typescript
export class UniversalDataset implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  // ✅ Initialize in init()
  public init(context, notifyOutputChanged, state, container) {
    context.mode.trackContainerResize(true);  // Enable responsive
    // Subscribe to events, create DOM structure
  }

  // ✅ Update React on every updateView()
  public updateView(context) {
    ReactDOM.render(React.createElement(Component, props), container);
  }

  // ✅ Cleanup in destroy()
  public destroy() {
    ReactDOM.unmountComponentAtNode(this.container);
    // Remove event listeners, clear timers
  }
}
```

**Dataset API Best Practices:**
```typescript
// ✅ Respect dataset loading state
if (dataset.loading) {
  return <Spinner label="Loading..." />;
}

// ✅ Use platform paging
if (dataset.paging.hasNextPage) {
  dataset.paging.loadNextPage();
}

// ✅ Use platform sorting
const columns = dataset.columns
  .filter(col => col.order >= 0)
  .sort((a, b) => a.order - b.order);

// ❌ NEVER directly manipulate form data
// context.parameters.dataset.records[id].setValue()  // PROHIBITED
```

**Localization (Required):**
```xml
<!-- strings/UniversalDataset.1033.resx -->
<data name="LoadMore_ButtonLabel">
  <value>Load More</value>
</data>
```

```typescript
// Access localized strings
this.loadPageButton.innerText = context.resources.getString("LoadMore_ButtonLabel");
```

### 3. **TypeScript Type Safety**
- Strong typing for all props, state, and return values
- Use generated `ManifestTypes.ts` for PCF inputs/outputs
- Define interfaces for all data structures
- Enable strict TypeScript compiler options

### 3. **React Best Practices**
- Functional components only (no class components)
- React Hooks for state management
- `useMemo` for expensive computations
- `useCallback` for stable function references
- Proper dependency arrays in `useEffect`

### 4. **Performance Optimization**
- Virtual scrolling for lists >50 items
- Memoize column definitions
- Debounce search/filter operations
- Lazy load images and previews
- Monitor render performance with React DevTools

### 5. **Accessibility (A11y)**
- ARIA labels for all interactive elements
- Keyboard navigation support (Tab, Enter, Escape)
- Focus management (trap focus in dialogs)
- Screen reader announcements for async updates
- WCAG 2.1 AA compliance

### 6. **Error Handling**
- Try-catch around all async operations
- User-friendly error messages (no stack traces)
- Fallback UI for component errors (Error Boundaries)
- Log errors to console for debugging
- Toast notifications for user actions

### 7. **Testing Strategy**
- Unit test all business logic (hooks, services, transformers)
- Integration test PCF lifecycle and dataset binding
- E2E test critical user workflows
- Performance test with realistic data volumes
- Accessibility testing with axe-core

### 8. **Configuration Over Code**
- No entity-specific logic in components
- Use manifest properties for behavior changes
- Support JSON configuration for extensibility
- Provide sensible defaults for all properties

### 9. **Separation of Concerns**
- PCF lifecycle in `index.ts` only
- React components in `src/components/`
- Business logic in `src/services/`
- Data transformation in `src/utils/`
- Types in `src/types/`

### 10. **Build & Deploy**
- Automate build with npm scripts
- Version control manifest version
- Test in harness before packaging
- Document deployment process
- Maintain changelog

---

## Estimated Effort Summary

| Phase | Tasks | Hours |
|-------|-------|-------|
| **Phase 1: Scaffolding** | Shared library setup, PCF project setup, PCF lifecycle, manifest | 10 |
| **Phase 2: Core Infrastructure** | Shared components (DataGrid, hooks), PCF wrappers, basic Grid view | 14 |
| **Phase 3: Advanced Features** | Commands, renderers, views, virtualization (in shared library) | 18 |
| **Phase 4: Testing** | Unit tests (shared + PCF), integration, E2E tests | 14 |
| **Phase 5: Documentation & Deployment** | Shared library docs, PCF docs, packaging, deployment guide | 10 |
| **Total** | | **66 hours** |

**Sprint Duration:** 2 weeks (80 hours capacity)
**Buffer:** 14 hours for unknowns, refinement, and code review

**Key Additions for ADR-012:**
- +2 hours for shared library setup (Phase 1)
- +2 hours for component extraction to shared library (Phase 2)
- +2 hours for shared component development (Phase 3)
- +2 hours for shared component testing (Phase 4)
- +2 hours for shared library documentation (Phase 5)
- **Total added:** +10 hours

---

## Success Criteria

### Functional Requirements
- ✅ Works with ANY Dataverse entity without code changes
- ✅ Supports Dataset (model-driven) and Headless (custom pages) modes
- ✅ Renders Grid, Card, and List views
- ✅ Executes built-in commands (open, create, delete, refresh)
- ✅ Supports custom commands via JSON configuration
- ✅ Handles 10,000+ records with virtual scrolling

### Performance Requirements
- ✅ Initial render <500ms (50 records)
- ✅ Scroll to 500th record <100ms
- ✅ <100 DOM elements for large datasets (virtualization)

### Quality Requirements
- ✅ 80%+ code coverage (statements, branches, functions, lines)
- ✅ Zero ESLint errors
- ✅ Zero TypeScript errors
- ✅ Passes WCAG 2.1 AA accessibility tests
- ✅ Works in Edge, Chrome, Firefox

### Deployment Requirements
- ✅ Builds successfully with `npm run build`
- ✅ Packages as Power Platform solution
- ✅ Imports to Dataverse without errors
- ✅ Works in model-driven app and custom pages

---

## Risk Mitigation

### Risk 1: Fluent v9 Breaking Changes
**Mitigation:** Pin versions in `package.json`, test before upgrading

### Risk 2: PCF Platform Limitations
**Mitigation:** Prototype early, reference official docs, test in real environment

### Risk 3: Performance Issues with Large Datasets
**Mitigation:** Implement virtual scrolling from day 1, test with 10K+ records

### Risk 4: Integration with Existing Spaarke Solution
**Mitigation:** No code sharing with BFF API, isolated deployment

---

## Next Steps (Post-Sprint 5)

### Sprint 6 Enhancements (Optional)
- Kanban view mode (board by status column)
- Gallery view (image-focused)
- Bulk operations (multi-select actions)
- Inline editing (editable grid)
- Advanced filtering UI
- Export to Excel
- File drag-drop upload
- Quick view panel

---

## References

**🔴 CRITICAL STANDARDS (MUST READ BEFORE CODING):**
- **[KM-PCF-CONTROL-STANDARDS.md](../../../docs/KM-PCF-CONTROL-STANDARDS.md)** - PCF development best practices
- **[KM-UX-FLUENT-DESIGN-V9-STANDARDS.md](../../../docs/KM-UX-FLUENT-DESIGN-V9-STANDARDS.md)** - Fluent UI v9 strict requirements

**Internal Documentation:**
- [DATASET-COMPONENT-OVERVIEW.md](./DATASET-COMPONENT-OVERVIEW.md)
- [DATASET-COMPONENT-IMPLEMENTATION.md](./DATASET-COMPONENT-IMPLEMENTATION.md)
- [DATASET-COMPONENT-MANIFEST.md](./DATASET-COMPONENT-MANIFEST.md)
- [DATASET-COMPONENT-COMMANDS.md](./DATASET-COMPONENT-COMMANDS.md)
- [DATASET-COMPONENT-TESTING.md](./DATASET-COMPONENT-TESTING.md)
- [DATASET-COMPONENT-PERFORMANCE.md](./DATASET-COMPONENT-PERFORMANCE.md)
- [DATASET-COMPONENT-DEPLOYMENT.md](./DATASET-COMPONENT-DEPLOYMENT.md)
- [PCF-DATASET-COMPONENT-GUIDE.md](./PCF-DATASET-COMPONENT-GUIDE.md)
- [ADR-012-IMPLEMENTATION-SUMMARY.md](./ADR-012-IMPLEMENTATION-SUMMARY.md)

**ADRs:**
- [ADR-012: Shared Component Library](../../docs/adr/ADR-012-shared-component-library.md) - Component reusability
- [ADR-006: Prefer PCF Over Web Resources](../../docs/adr/ADR-006-prefer-pcf-over-webresources.md)
- [ADR-011: Dataset PCF Over Subgrids](../../docs/adr/ADR-011-dataset-pcf-over-subgrids.md)
- [ADR-010: DI Minimalism](../../docs/adr/ADR-010-di-minimalism.md)

**External References:**
- [PCF Framework Documentation](https://docs.microsoft.com/en-us/power-apps/developer/component-framework/overview)
- [Fluent UI React v9](https://react.fluentui.dev/)
- [React Window (Virtualization)](https://github.com/bvaughn/react-window)
- [Microsoft PCF Samples](https://github.com/microsoft/PowerApps-Samples/tree/master/component-framework)

---

**Document Version:** 1.0
**Last Updated:** 2025-10-03
**Status:** Ready for Implementation
**Approved By:** Spaarke Engineering Team
