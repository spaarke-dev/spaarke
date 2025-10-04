# UniversalDatasetGrid API Reference

## Overview

`UniversalDatasetGrid` is a configuration-driven PCF component that provides Grid, List, and Card views for Dataverse datasets with built-in command support, virtualization, and entity-specific customization.

**Package**: `@spaarke/ui-components`
**Namespace**: `Spaarke.UI.Components`
**Version**: 1.0.0

---

## Props

### Required Props

#### `dataset`
- **Type**: `ComponentFramework.PropertyTypes.DataSet`
- **Required**: Yes (when not using headless mode)
- **Description**: PCF dataset containing records, columns, and metadata

```typescript
<UniversalDatasetGrid
  dataset={context.parameters.dataset}
  context={context}
/>
```

#### `context`
- **Type**: `ComponentFramework.Context<IInputs>`
- **Required**: Yes
- **Description**: PCF framework context providing WebAPI, navigation, and utilities

---

### Optional Props

#### `config`
- **Type**: `IDatasetConfig`
- **Required**: No
- **Default**: `{ viewMode: "Grid", enabledCommands: ["open", "create", "delete", "refresh"], ... }`
- **Description**: Component configuration object

```typescript
<UniversalDatasetGrid
  dataset={dataset}
  context={context}
  config={{
    viewMode: "List",
    enabledCommands: ["open", "refresh"],
    compactToolbar: true,
    enableVirtualization: true
  }}
/>
```

#### `configJson`
- **Type**: `string`
- **Required**: No
- **Description**: JSON string containing entity-specific configurations (schema v1.0)

```typescript
const config = JSON.stringify({
  schemaVersion: "1.0",
  defaultConfig: { viewMode: "Grid" },
  entityConfigs: {
    account: { viewMode: "Card" }
  }
});

<UniversalDatasetGrid
  dataset={dataset}
  context={context}
  configJson={config}
/>
```

#### `headlessConfig`
- **Type**: `IHeadlessConfig`
- **Required**: No (Required when dataset is not provided)
- **Description**: Configuration for headless mode (custom data source)

```typescript
<UniversalDatasetGrid
  context={context}
  headlessConfig={{
    entityName: "account",
    columns: [...],
    records: [...],
    totalRecordCount: 100,
    onLoadMore: () => {},
    onRefresh: () => {}
  }}
/>
```

---

## Interfaces

### IDatasetConfig

```typescript
interface IDatasetConfig {
  // View Configuration
  viewMode?: ViewMode;                    // "Grid" | "List" | "Card" (default: "Grid")

  // Commands
  enabledCommands?: string[];             // Array of command keys (default: ["open", "create", "delete", "refresh"])

  // Toolbar
  compactToolbar?: boolean;               // Compact toolbar mode (default: false)
  toolbarShowOverflow?: boolean;          // Show overflow menu for >8 commands (default: true)

  // Virtualization
  enableVirtualization?: boolean;         // Enable virtual scrolling (default: true)
  rowHeight?: number;                     // Row height in pixels (default: 44)
  scrollBehavior?: ScrollBehavior;        // "Auto" | "Infinite" | "Paged" (default: "Auto")

  // Styling
  theme?: ThemeMode;                      // "Auto" | "Spaarke" | "Host" (default: "Auto")
}
```

### IHeadlessConfig

```typescript
interface IHeadlessConfig {
  entityName: string;                     // Entity logical name
  columns: IDatasetColumn[];              // Column definitions
  records: IDatasetRecord[];              // Record data
  totalRecordCount: number;               // Total records available
  pageSize?: number;                      // Records per page (default: 25)
  onLoadMore?: () => void;                // Load more callback
  onRefresh?: () => void;                 // Refresh callback
  onRecordSelect?: (recordIds: string[]) => void;  // Selection callback
}
```

---

## Configuration

### View Modes

| Mode | Description | Use Case |
|------|-------------|----------|
| `Grid` | Tabular layout with columns | Default, detailed data view |
| `List` | Vertical list with primary field | Mobile-friendly, simple view |
| `Card` | Card-based layout | Visual, dashboard-style |

### Built-in Commands

| Command | Key | Requires Selection | Keyboard Shortcut | Description |
|---------|-----|-------------------|-------------------|-------------|
| New | `create` | No | Ctrl+N | Create new record |
| Open | `open` | Yes (single) | Ctrl+O | Open record form |
| Delete | `delete` | Yes (multi) | Delete | Delete selected records |
| Refresh | `refresh` | No | F5 | Refresh grid data |
| Upload | `upload` | No | Ctrl+U | Upload file (entity-specific) |

### Scroll Behaviors

| Behavior | Description |
|----------|-------------|
| `Auto` | Automatic based on dataset size (<100: none, 100-1000: built-in, >1000: virtual) |
| `Infinite` | Infinite scroll with auto-load |
| `Paged` | Traditional pagination |

---

## Methods

### Instance Methods

The component doesn't expose public methods directly (React component pattern). Interactions are handled through:

1. **Props** - Configure behavior
2. **Callbacks** - Respond to events
3. **PCF Context** - Trigger actions (refresh, navigation)

### Triggering Refresh

```typescript
// Via PCF dataset
context.parameters.dataset.refresh();

// Via command
const refreshCommand = CommandRegistry.getCommand('refresh');
await refreshCommand.handler(commandContext);
```

---

## Events

### Command Execution

Commands execute through the `ICommandContext`:

```typescript
interface ICommandContext {
  selectedRecords: IDatasetRecord[];      // Currently selected records
  entityName: string;                     // Entity logical name
  webAPI: ComponentFramework.WebApi;      // PCF WebAPI
  navigation: ComponentFramework.Navigation;  // PCF Navigation
  refresh: () => void;                    // Refresh grid
  emitLastAction?: (action: string, data?: any) => void;  // Emit action event
  parentRecord?: ComponentFramework.EntityReference;  // Parent record (sub-grid)
}
```

### Selection Changes

Selection is managed by the dataset:

```typescript
// Get selected records
const selectedIds = dataset.getSelectedRecordIds();

// Set selection
dataset.setSelectedRecordIds(["id1", "id2"]);

// Clear selection
dataset.clearSelectedRecordIds();
```

---

## Entity Configuration (JSON Schema v1.0)

### Schema Structure

```typescript
interface IConfigurationSchema {
  schemaVersion: string;                  // "1.0"
  defaultConfig: IEntityConfiguration;    // Default settings
  entityConfigs: Record<string, IEntityConfiguration>;  // Entity overrides
}

interface IEntityConfiguration {
  viewMode?: ViewMode;
  enabledCommands?: string[];
  compactToolbar?: boolean;
  enableVirtualization?: boolean;
  rowHeight?: number;
  scrollBehavior?: ScrollBehavior;
  toolbarShowOverflow?: boolean;
  customCommands?: Record<string, ICustomCommandConfiguration>;
}
```

### Example Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enabledCommands": ["open", "create", "delete", "refresh"],
    "compactToolbar": false,
    "enableVirtualization": true,
    "rowHeight": 44,
    "scrollBehavior": "Auto",
    "toolbarShowOverflow": true
  },
  "entityConfigs": {
    "account": {
      "viewMode": "Card",
      "enabledCommands": ["open", "refresh"],
      "compactToolbar": true
    },
    "sprk_document": {
      "enabledCommands": ["open", "upload", "download"],
      "customCommands": {
        "upload": {
          "label": "Upload to SharePoint",
          "icon": "ArrowUpload",
          "actionType": "customapi",
          "actionName": "sprk_UploadDocument",
          "parameters": {
            "ParentId": "{parentRecordId}",
            "ParentTable": "{parentTable}"
          },
          "refresh": true,
          "successMessage": "Document uploaded successfully"
        }
      }
    }
  }
}
```

---

## Custom Commands

### ICustomCommandConfiguration

```typescript
interface ICustomCommandConfiguration {
  label: string;                          // Button label
  icon?: string;                          // Icon name (Fluent UI)
  actionType: CustomCommandActionType;    // "customapi" | "action" | "function" | "workflow"
  actionName: string;                     // API/Action/Function/Workflow name
  requiresSelection?: boolean;            // Requires record selection (default: false)
  group?: "primary" | "secondary" | "overflow";  // Command group (default: "overflow")
  description?: string;                   // Tooltip description
  keyboardShortcut?: string;              // Keyboard shortcut (e.g., "Ctrl+U")
  parameters?: Record<string, CommandParameterValue>;  // Command parameters
  refresh?: boolean;                      // Refresh grid after execution (default: false)
  successMessage?: string;                // Success notification message
  confirmationMessage?: string;           // Confirmation dialog message
  minSelection?: number;                  // Minimum records required
  maxSelection?: number;                  // Maximum records allowed
}
```

### Token Interpolation

Parameters support dynamic tokens:

| Token | Description | Example Value |
|-------|-------------|---------------|
| `{selectedCount}` | Number of selected records | "3" |
| `{entityName}` | Entity logical name | "account" |
| `{parentRecordId}` | Parent record GUID (sub-grid) | "abc-123-def" |
| `{parentTable}` | Parent entity name (sub-grid) | "opportunity" |

---

## Virtualization

### Configuration

```typescript
<UniversalDatasetGrid
  dataset={dataset}
  context={context}
  config={{
    enableVirtualization: true,
    rowHeight: 44,  // Must match actual row height
    scrollBehavior: "Auto"
  }}
/>
```

### Thresholds

- **<100 records**: Standard rendering (no virtualization)
- **100-1000 records**: Fluent UI DataGrid built-in virtualization
- **>1000 records**: react-window custom virtualization

### Performance

- **Overscan**: 5 rows (renders slightly more for smooth scrolling)
- **Fixed height**: All rows must have same height for optimal performance
- **Update strategy**: Only visible rows re-render on data changes

---

## Accessibility

### WCAG 2.1 AA Compliance

- ✅ Keyboard navigation (Tab, Arrow keys, Enter, Space)
- ✅ Screen reader support (ARIA labels, roles, live regions)
- ✅ Focus management (visible focus indicators)
- ✅ Color contrast (4.5:1 minimum)
- ✅ Semantic HTML (proper heading hierarchy)

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+N` | New record |
| `Ctrl+O` | Open record |
| `Delete` | Delete selected |
| `F5` | Refresh |
| `Tab` | Navigate toolbar |
| `Arrow Keys` | Navigate grid |
| `Space` | Select/deselect |
| `Enter` | Open record |

### ARIA Attributes

```typescript
// Toolbar
<Toolbar role="toolbar" aria-label="Commands">

// Commands
<Button
  aria-label="New record"
  aria-keyshortcuts="Ctrl+N"
  aria-disabled={!canExecute}
/>

// Grid
<DataGrid role="grid" aria-label="Records">
  <DataGridRow role="row" aria-selected={isSelected}>
    <DataGridCell role="gridcell">
```

---

## Browser Support

| Browser | Minimum Version | Notes |
|---------|-----------------|-------|
| Chrome | 90+ | Recommended |
| Edge | 90+ | Recommended |
| Firefox | 88+ | Supported |
| Safari | 14+ | Supported |

---

## Performance

### Recommendations

- **Dataset size**: Optimize for <10,000 records
- **Column count**: Keep under 20 visible columns
- **Custom renderers**: Memoize expensive renders
- **Network**: Use $select to limit fields returned

### Metrics

- **Initial render**: <500ms for 100 records
- **Virtual scroll**: 60 FPS for 10,000 records
- **Command execution**: <100ms (network excluded)

---

## TypeScript Usage

### Type Imports

```typescript
import {
  IDatasetConfig,
  IHeadlessConfig,
  ICommand,
  ICommandContext,
  ViewMode,
  ScrollBehavior,
  ThemeMode
} from '@spaarke/ui-components';
```

### Strict Mode

Component is built with `strictNullChecks` and `noImplicitAny` enabled.

---

## See Also

- [Types Reference](./Types.md)
- [Commands API](./Commands.md)
- [Usage Guide](../guides/UsageGuide.md)
- [Configuration Guide](../guides/ConfigurationGuide.md)
