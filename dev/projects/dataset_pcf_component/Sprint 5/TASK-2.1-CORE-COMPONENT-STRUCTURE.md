# Task 2.1: Build Core Component Structure

**Sprint:** Sprint 5 - Universal Dataset PCF Component
**Phase:** 2 - Core Component Development
**Estimated Time:** 3 hours
**Prerequisites:** [TASK-1.4-MANIFEST-CONFIGURATION.md](./TASK-1.4-MANIFEST-CONFIGURATION.md)
**Next Task:** [TASK-2.2-DATASET-HOOKS.md](./TASK-2.2-DATASET-HOOKS.md)

---

## Objective

Create the core React component structure for the Universal Dataset control, including:
- Main `UniversalDatasetGrid` component with FluentProvider
- Type definitions for component props
- Theme detection and bridging
- View mode routing (Grid/Card/List)

**Why:** Establishes the foundation for all UI features. The root component handles theme setup, configuration routing, and delegates to specialized views.

---

## Critical Standards

**MUST READ BEFORE STARTING:**
- [KM-UX-FLUENT-DESIGN-V9-STANDARDS.md](../../../docs/KM-UX-FLUENT-DESIGN-V9-STANDARDS.md) - Single FluentProvider, theme tokens
- [ADR-012: Shared Component Library](../../../docs/adr/ADR-012-shared-component-library.md) - Build in shared library

**Key Rules:**
- ✅ Build components in `src/shared/Spaarke.UI.Components/` (NOT in PCF project)
- ✅ Single FluentProvider at root
- ✅ Theme detection from host OR fallback to Spaarke theme
- ✅ Use Griffel makeStyles for ALL styling
- ✅ No hard-coded colors or spacing

---

## Step 1: Create Types in Shared Library

```bash
cd c:\code_files\spaarke\src\shared\Spaarke.UI.Components\src\types
```

**Create `types/DatasetTypes.ts`:**

```typescript
/**
 * Core types for Universal Dataset component
 */

export type ViewMode = "Grid" | "Card" | "List";
export type ThemeMode = "Auto" | "Spaarke" | "Host";
export type SelectionMode = "None" | "Single" | "Multiple";

export interface IDatasetRecord {
  id: string;
  entityName: string;
  [key: string]: any;
}

export interface IDatasetColumn {
  name: string;
  displayName: string;
  dataType: string;
  isKey?: boolean;
  isPrimary?: boolean;
  visualSizeFactor?: number;
}

export interface IDatasetConfig {
  viewMode: ViewMode;
  enableVirtualization: boolean;
  rowHeight: number;
  selectionMode: SelectionMode;
  showToolbar: boolean;
  enabledCommands: string[];
  theme: ThemeMode;
}

export interface ICommandContext {
  selectedRecords: IDatasetRecord[];
  entityName: string;
  webAPI: ComponentFramework.WebApi;
  navigation: ComponentFramework.Navigation;
  refresh?: () => void;
  parentRecord?: ComponentFramework.EntityReference;
  emitLastAction?: (action: string) => void;
}
```

**Update `types/index.ts`:**

```typescript
export * from "./DatasetTypes";
```

---

## Step 2: Create Theme Utilities

**Create `utils/themeDetection.ts`:**

```typescript
/**
 * Theme detection and bridging utilities
 * Detects Power Platform theme and bridges to Fluent UI v9
 */

import { Theme, webLightTheme, webDarkTheme } from "@fluentui/react-components";
import { spaarkeLight, spaarkeDark } from "../theme";
import { ThemeMode } from "../types";

export interface IThemeContext {
  fluentDesignLanguage?: {
    tokenTheme?: Theme;
    isDarkMode?: boolean;
  };
}

/**
 * Detect theme from Power Platform context
 * @param context PCF context (cast to any to access fluentDesignLanguage)
 * @param themeMode User-configured theme mode
 * @returns Fluent UI v9 Theme
 */
export function detectTheme(
  context: any,
  themeMode: ThemeMode = "Auto"
): Theme {
  // User explicitly chose Spaarke theme
  if (themeMode === "Spaarke") {
    return spaarkeLight;
  }

  // User explicitly chose Host theme
  if (themeMode === "Host") {
    const hostTheme = (context as IThemeContext).fluentDesignLanguage?.tokenTheme;
    if (hostTheme) {
      return hostTheme;
    }
    // Fallback to web theme if host theme unavailable
    const isDark = (context as IThemeContext).fluentDesignLanguage?.isDarkMode;
    return isDark ? webDarkTheme : webLightTheme;
  }

  // Auto mode: Try host theme, fallback to Spaarke
  const hostTheme = (context as IThemeContext).fluentDesignLanguage?.tokenTheme;
  if (hostTheme) {
    return hostTheme;
  }

  // No host theme available - use Spaarke brand theme
  return spaarkeLight;
}

/**
 * Detect if dark mode is enabled
 * @param context PCF context
 * @returns true if dark mode, false otherwise
 */
export function isDarkMode(context: any): boolean {
  return (context as IThemeContext).fluentDesignLanguage?.isDarkMode ?? false;
}
```

**Update `utils/index.ts`:**

```typescript
export * from "./themeDetection";
```

---

## Step 3: Create Grid View Component (Placeholder)

```bash
cd c:\code_files\spaarke\src\shared\Spaarke.UI.Components\src\components
mkdir DatasetGrid
cd DatasetGrid
```

**Create `components/DatasetGrid/GridView.tsx`:**

```typescript
/**
 * GridView - Table layout using Fluent UI DataGrid
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */

import * as React from "react";
import {
  DataGrid,
  DataGridHeader,
  DataGridRow,
  DataGridHeaderCell,
  DataGridBody,
  DataGridCell,
  TableColumnDefinition,
  createTableColumn,
  makeStyles,
  tokens
} from "@fluentui/react-components";
import { IDatasetRecord, IDatasetColumn } from "../../types";

export interface IGridViewProps {
  records: IDatasetRecord[];
  columns: IDatasetColumn[];
  selectedRecordIds: string[];
  onSelectionChange: (selectedIds: string[]) => void;
  onRecordClick: (record: IDatasetRecord) => void;
  enableVirtualization: boolean;
  rowHeight: number;
}

const useStyles = makeStyles({
  grid: {
    width: "100%",
    height: "100%"
  }
});

export const GridView: React.FC<IGridViewProps> = (props) => {
  const styles = useStyles();

  // Placeholder: Will implement full grid in Phase 3
  return (
    <div className={styles.grid}>
      <p style={{ padding: tokens.spacingVerticalM }}>
        GridView Placeholder - {props.records.length} records, {props.columns.length} columns
      </p>
      <p style={{ padding: tokens.spacingVerticalM, fontStyle: "italic" }}>
        Will implement DataGrid with virtualization in TASK-2.3
      </p>
    </div>
  );
};
```

---

## Step 4: Create Card View Component (Placeholder)

**Create `components/DatasetGrid/CardView.tsx`:**

```typescript
/**
 * CardView - Tile/Card layout
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */

import * as React from "react";
import { makeStyles, tokens } from "@fluentui/react-components";
import { IDatasetRecord, IDatasetColumn } from "../../types";

export interface ICardViewProps {
  records: IDatasetRecord[];
  columns: IDatasetColumn[];
  selectedRecordIds: string[];
  onSelectionChange: (selectedIds: string[]) => void;
  onRecordClick: (record: IDatasetRecord) => void;
}

const useStyles = makeStyles({
  cardContainer: {
    display: "grid",
    gridTemplateColumns: "repeat(auto-fill, minmax(300px, 1fr))",
    gap: tokens.spacingHorizontalM,
    padding: tokens.spacingVerticalM
  }
});

export const CardView: React.FC<ICardViewProps> = (props) => {
  const styles = useStyles();

  return (
    <div className={styles.cardContainer}>
      <p>CardView Placeholder - {props.records.length} records</p>
      <p style={{ fontStyle: "italic" }}>Will implement Card layout in Phase 3</p>
    </div>
  );
};
```

---

## Step 5: Create List View Component (Placeholder)

**Create `components/DatasetGrid/ListView.tsx`:**

```typescript
/**
 * ListView - Compact list layout
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */

import * as React from "react";
import { makeStyles, tokens } from "@fluentui/react-components";
import { IDatasetRecord, IDatasetColumn } from "../../types";

export interface IListViewProps {
  records: IDatasetRecord[];
  columns: IDatasetColumn[];
  selectedRecordIds: string[];
  onSelectionChange: (selectedIds: string[]) => void;
  onRecordClick: (record: IDatasetRecord) => void;
}

const useStyles = makeStyles({
  listContainer: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalM
  }
});

export const ListView: React.FC<IListViewProps> = (props) => {
  const styles = useStyles();

  return (
    <div className={styles.listContainer}>
      <p>ListView Placeholder - {props.records.length} records</p>
      <p style={{ fontStyle: "italic" }}>Will implement List layout in Phase 3</p>
    </div>
  );
};
```

---

## Step 6: Create Main UniversalDatasetGrid Component

**Create `components/DatasetGrid/UniversalDatasetGrid.tsx`:**

```typescript
/**
 * UniversalDatasetGrid - Main component for dataset display
 * Routes to GridView, CardView, or ListView based on configuration
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md, ADR-012
 */

import * as React from "react";
import { FluentProvider, makeStyles, tokens } from "@fluentui/react-components";
import { detectTheme } from "../../utils";
import { IDatasetConfig, IDatasetRecord, IDatasetColumn } from "../../types";
import { GridView } from "./GridView";
import { CardView } from "./CardView";
import { ListView } from "./ListView";

export interface IUniversalDatasetGridProps {
  // Configuration
  config: IDatasetConfig;

  // Data
  records: IDatasetRecord[];
  columns: IDatasetColumn[];
  loading: boolean;

  // Selection
  selectedRecordIds: string[];
  onSelectionChange: (selectedIds: string[]) => void;

  // Actions
  onRecordClick: (record: IDatasetRecord) => void;
  onRefresh?: () => void;

  // Context (for theme detection)
  context: any; // ComponentFramework.Context<IInputs>
}

const useStyles = makeStyles({
  root: {
    width: "100%",
    height: "100%",
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
    fontFamily: tokens.fontFamilyBase
  },
  content: {
    flex: 1,
    overflow: "auto"
  },
  loading: {
    padding: tokens.spacingVerticalXL,
    textAlign: "center",
    color: tokens.colorNeutralForeground2
  }
});

export const UniversalDatasetGrid: React.FC<IUniversalDatasetGridProps> = (props) => {
  const styles = useStyles();

  // Detect theme from context
  const theme = React.useMemo(
    () => detectTheme(props.context, props.config.theme),
    [props.context, props.config.theme]
  );

  // Select view component based on config
  const ViewComponent = React.useMemo(() => {
    switch (props.config.viewMode) {
      case "Card":
        return CardView;
      case "List":
        return ListView;
      case "Grid":
      default:
        return GridView;
    }
  }, [props.config.viewMode]);

  return (
    <FluentProvider theme={theme}>
      <div className={styles.root}>
        {/* Toolbar will be added in Phase 3 */}

        <div className={styles.content}>
          {props.loading ? (
            <div className={styles.loading}>Loading...</div>
          ) : (
            <ViewComponent
              records={props.records}
              columns={props.columns}
              selectedRecordIds={props.selectedRecordIds}
              onSelectionChange={props.onSelectionChange}
              onRecordClick={props.onRecordClick}
              enableVirtualization={props.config.enableVirtualization}
              rowHeight={props.config.rowHeight}
            />
          )}
        </div>
      </div>
    </FluentProvider>
  );
};
```

---

## Step 7: Export from Shared Library

**Update `components/index.ts`:**

```typescript
// Dataset components
export * from "./DatasetGrid/UniversalDatasetGrid";
export * from "./DatasetGrid/GridView";
export * from "./DatasetGrid/CardView";
export * from "./DatasetGrid/ListView";
```

---

## Step 8: Build Shared Library

```bash
cd c:\code_files\spaarke\src\shared\Spaarke.UI.Components
npm run build

# Expected output: "Successfully compiled X files"
```

---

## Step 9: Update PCF index.ts to Use Component

**Edit `c:\code_files\spaarke\power-platform\pcf\UniversalDataset\index.ts`:**

```typescript
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom";
import {
  UniversalDatasetGrid,
  IDatasetConfig,
  IDatasetRecord,
  IDatasetColumn
} from "@spaarke/ui-components";

export class UniversalDataset implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private container: HTMLDivElement;
  private context: ComponentFramework.Context<IInputs>;
  private notifyOutputChanged: () => void;
  private selectedRecords: string[] = [];
  private lastAction: string = "";

  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this.context = context;
    this.notifyOutputChanged = notifyOutputChanged;
    this.container = container;
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.context = context;

    // Build configuration from manifest properties
    const config: IDatasetConfig = {
      viewMode: context.parameters.viewMode?.raw || "Grid",
      enableVirtualization: context.parameters.enableVirtualization?.raw ?? true,
      rowHeight: context.parameters.rowHeight?.raw || 48,
      selectionMode: context.parameters.selectionMode?.raw || "Multiple",
      showToolbar: context.parameters.showToolbar?.raw ?? true,
      enabledCommands: (context.parameters.enabledCommands?.raw || "open,create,delete,refresh").split(","),
      theme: context.parameters.theme?.raw || "Auto"
    };

    // Extract dataset records and columns (placeholder - will enhance in Phase 2)
    const dataset = context.parameters.datasetGrid;
    const records: IDatasetRecord[] = [];
    const columns: IDatasetColumn[] = [];

    // Convert dataset columns to IDatasetColumn
    if (dataset.columns) {
      dataset.columns.forEach((col) => {
        columns.push({
          name: col.name,
          displayName: col.displayName,
          dataType: col.dataType,
          isKey: false,
          isPrimary: col.name === dataset.columns[0]?.name
        });
      });
    }

    // Convert dataset records to IDatasetRecord
    if (dataset.sortedRecordIds) {
      dataset.sortedRecordIds.forEach((recordId) => {
        const record = dataset.records[recordId];
        const dataRecord: IDatasetRecord = {
          id: recordId,
          entityName: dataset.getTargetEntityType()
        };

        // Extract column values
        columns.forEach((col) => {
          const value = record.getFormattedValue(col.name);
          dataRecord[col.name] = value;
        });

        records.push(dataRecord);
      });
    }

    // Render React component
    const element = React.createElement(UniversalDatasetGrid, {
      config,
      records,
      columns,
      loading: dataset.loading,
      selectedRecordIds: this.selectedRecords,
      onSelectionChange: (ids) => {
        this.selectedRecords = ids;
        this.notifyOutputChanged();
      },
      onRecordClick: (record) => {
        context.parameters.datasetGrid.openDatasetItem(record.id);
      },
      onRefresh: () => {
        context.parameters.datasetGrid.refresh();
      },
      context: context
    });

    ReactDOM.render(element, this.container);
  }

  public getOutputs(): IOutputs {
    return {
      selectedRecordIds: this.selectedRecords.join(","),
      lastAction: this.lastAction
    };
  }

  public destroy(): void {
    ReactDOM.unmountComponentAtNode(this.container);
  }
}
```

---

## Step 10: Build and Test

```bash
# Build shared library first
cd c:\code_files\spaarke
npm run build:shared

# Build PCF control
cd power-platform\pcf\UniversalDataset
npm run build

# Start test harness
npm start
```

**Expected Result:**
- Control loads with FluentProvider
- Shows "GridView Placeholder" with record count
- Theme applied (Spaarke light theme)
- No console errors

---

## Validation Checklist

```bash
# 1. Verify shared library has components
cd c:\code_files\spaarke\src\shared\Spaarke.UI.Components
dir src\components\DatasetGrid
# Should see: UniversalDatasetGrid.tsx, GridView.tsx, CardView.tsx, ListView.tsx

# 2. Verify types exist
dir src\types\DatasetTypes.ts
dir src\utils\themeDetection.ts

# 3. Verify shared library builds
npm run build
# Should succeed

# 4. Verify PCF imports component
cd c:\code_files\spaarke\power-platform\pcf\UniversalDataset
type index.ts | findstr "UniversalDatasetGrid"
# Should show import

# 5. Verify PCF builds
npm run build
# Should succeed

# 6. Verify test harness runs
npm start
# Open http://localhost:8181 - should show component
```

---

## Success Criteria

- ✅ Types created in shared library (`DatasetTypes.ts`)
- ✅ Theme utilities created (`themeDetection.ts`)
- ✅ View components created (Grid, Card, List placeholders)
- ✅ Main `UniversalDatasetGrid` component created
- ✅ Single FluentProvider at root
- ✅ Theme detection from context working
- ✅ PCF imports from `@spaarke/ui-components`
- ✅ Test harness loads component with theme
- ✅ No hard-coded colors or spacing (all use tokens)

---

## Deliverables

**Shared Library Files Created:**
1. `src/types/DatasetTypes.ts`
2. `src/utils/themeDetection.ts`
3. `src/components/DatasetGrid/UniversalDatasetGrid.tsx`
4. `src/components/DatasetGrid/GridView.tsx`
5. `src/components/DatasetGrid/CardView.tsx`
6. `src/components/DatasetGrid/ListView.tsx`
7. Updated `src/types/index.ts`
8. Updated `src/utils/index.ts`
9. Updated `src/components/index.ts`

**PCF Control Files Updated:**
1. `index.ts` (imports and renders UniversalDatasetGrid)

---

## Common Issues & Solutions

**Issue:** Import error "Cannot find module '@spaarke/ui-components'"
**Solution:** Rebuild shared library: `npm run build:shared` from root

**Issue:** Theme not applying
**Solution:** Verify FluentProvider wraps entire component tree (check React DevTools)

**Issue:** TypeScript error "Property 'fluentDesignLanguage' does not exist"
**Solution:** Cast to `any` in theme detection: `(context as any).fluentDesignLanguage`

**Issue:** Placeholder text not using tokens
**Solution:** Replace inline styles with Griffel makeStyles

---

## Next Steps

After completing this task:
1. Proceed to [TASK-2.2-DATASET-HOOKS.md](./TASK-2.2-DATASET-HOOKS.md)
2. Will create custom hooks for dataset/headless mode logic

---

**Task Status:** Ready for Execution
**Estimated Time:** 3 hours
**Actual Time:** _________ (fill in after completion)
**Completed By:** _________ (developer name)
**Date:** _________ (completion date)
