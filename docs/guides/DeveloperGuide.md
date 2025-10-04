# Developer Guide

Architecture, design patterns, and extension guide for the Universal Dataset Grid PCF component.

---

## Overview

The Universal Dataset Grid is a **universal**, **configuration-driven** PCF component built with:

- **React 18.2** - Functional components with hooks
- **TypeScript 5.3** - Strict mode enabled
- **Fluent UI v9** - Microsoft's design system
- **react-window** - Virtualization for performance
- **PCF Framework** - Power Apps Component Framework

**Key Principle**: **Zero entity-specific code** - all customization via configuration.

---

## Architecture

### High-Level Architecture

```
┌────────────────────────────────────────────────────────┐
│                    PCF Container                       │
│  ┌──────────────────────────────────────────────────┐  │
│  │          UniversalDatasetGrid (React)           │  │
│  │  ┌────────────────────────────────────────────┐  │  │
│  │  │         CommandToolbar Component           │  │  │
│  │  │  - Built-in commands                       │  │  │
│  │  │  - Custom commands                         │  │  │
│  │  │  - View mode switcher                      │  │  │
│  │  └────────────────────────────────────────────┘  │  │
│  │  ┌────────────────────────────────────────────┐  │  │
│  │  │          DatasetGrid Component             │  │  │
│  │  │  ├─ GridView (default)                     │  │  │
│  │  │  ├─ ListView                               │  │  │
│  │  │  └─ CardView                               │  │  │
│  │  └────────────────────────────────────────────┘  │  │
│  └──────────────────────────────────────────────────┘  │
│                                                         │
│  ┌──────────────────────────────────────────────────┐  │
│  │              Service Layer                       │  │
│  │  - EntityConfigurationService                    │  │
│  │  - CommandRegistry                               │  │
│  │  - CommandExecutor                               │  │
│  │  - CustomCommandFactory                          │  │
│  │  - FieldSecurityService                          │  │
│  │  - PrivilegeService                              │  │
│  └──────────────────────────────────────────────────┘  │
│                                                         │
│  ┌──────────────────────────────────────────────────┐  │
│  │              React Hooks                         │  │
│  │  - useVirtualization                             │  │
│  │  - useKeyboardShortcuts                          │  │
│  │  - useDatasetMode                                │  │
│  │  - useHeadlessMode                               │  │
│  └──────────────────────────────────────────────────┘  │
│                                                         │
│  ┌──────────────────────────────────────────────────┐  │
│  │              PCF Framework                       │  │
│  │  - context.parameters.dataset                    │  │
│  │  - context.webAPI                                │  │
│  │  - context.navigation                            │  │
│  │  - context.mode                                  │  │
│  └──────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────┘
```

---

## Project Structure

```
src/shared/Spaarke.UI.Components/
├── src/
│   ├── components/           # React components
│   │   ├── DatasetGrid/      # Grid, List, Card views
│   │   │   ├── DatasetGrid.tsx
│   │   │   ├── GridView.tsx
│   │   │   ├── ListView.tsx
│   │   │   └── CardView.tsx
│   │   └── Toolbar/          # Command toolbar
│   │       └── CommandToolbar.tsx
│   ├── services/             # Business logic
│   │   ├── EntityConfigurationService.ts    # Config loading/merging
│   │   ├── CommandRegistry.ts               # Command registration
│   │   ├── CommandExecutor.ts               # Command execution
│   │   ├── CustomCommandFactory.ts          # Custom command creation
│   │   ├── FieldSecurityService.ts          # Field-level security
│   │   └── PrivilegeService.ts              # User privilege checking
│   ├── hooks/                # React hooks
│   │   ├── useVirtualization.ts             # Virtualization hook
│   │   ├── useKeyboardShortcuts.ts          # Keyboard handling
│   │   ├── useDatasetMode.ts                # Dataset/Headless mode
│   │   └── useHeadlessMode.ts               # Headless mode hook
│   ├── types/                # TypeScript types
│   │   ├── CommandTypes.ts                  # ICommand, ICommandContext
│   │   ├── DatasetTypes.ts                  # IDatasetConfig, IColumn
│   │   ├── EntityConfigurationTypes.ts      # IEntityConfiguration
│   │   └── ColumnRendererTypes.ts           # Custom renderers
│   ├── utils/                # Utilities
│   │   └── themeDetection.ts                # Theme detection
│   ├── theme/                # Fluent UI themes
│   │   └── brand.ts                         # Spaarke brand theme
│   └── index.ts              # Public API
├── __mocks__/                # Test mocks
│   └── pcfMocks.tsx          # PCF framework mocks
├── jest.config.js            # Jest configuration
├── jest.setup.js             # Jest setup
├── tsconfig.json             # TypeScript configuration
└── package.json              # Dependencies
```

---

## Component Hierarchy

### UniversalDatasetGrid (Entry Point)

**Location**: `src/components/DatasetGrid/DatasetGrid.tsx`

**Props**:
```typescript
interface IUniversalDatasetGridProps {
  dataset?: ComponentFramework.PropertyTypes.DataSet;
  context: ComponentFramework.Context<any>;
  config?: IDatasetConfig;
  headlessConfig?: IHeadlessConfig;
}
```

**Responsibilities**:
- Detect dataset vs. headless mode
- Load entity configuration
- Register commands (built-in + custom)
- Render CommandToolbar + DatasetGrid
- Handle view mode switching

**Key Logic**:
```typescript
// Mode detection
const { isHeadless, records, columns } = useDatasetMode(dataset, headlessConfig);

// Configuration
useEffect(() => {
  EntityConfigurationService.loadConfiguration(configJson);
}, [configJson]);

// Command registration
useEffect(() => {
  registerBuiltInCommands();
  registerCustomCommands();
}, [entityConfig]);
```

---

### CommandToolbar Component

**Location**: `src/components/Toolbar/CommandToolbar.tsx`

**Props**:
```typescript
interface ICommandToolbarProps {
  commands: ICommand[];
  context: any;
  selectedRecords?: any[];
  compactMode?: boolean;
  onViewModeChange?: (mode: ViewMode) => void;
  currentViewMode?: ViewMode;
}
```

**Responsibilities**:
- Render command buttons
- Disable buttons based on selection
- Execute commands on click
- Render view mode switcher

**Key Features**:
- Fluent UI Button/MenuButton components
- Icon-only mode (compact)
- Tooltip support
- Selection counter

---

### DatasetGrid Component

**Location**: `src/components/DatasetGrid/DatasetGrid.tsx`

**Props**:
```typescript
interface IDatasetGridProps {
  records: any[];
  columns: IColumn[];
  viewMode: ViewMode;
  enableVirtualization?: boolean;
  virtualizationThreshold?: number;
  onSelectionChange?: (selectedRecords: any[]) => void;
}
```

**Responsibilities**:
- Route to correct view (Grid/List/Card)
- Manage selection state
- Handle virtualization

**View Routing**:
```typescript
switch (viewMode) {
  case "Grid":
    return <GridView {...props} />;
  case "List":
    return <ListView {...props} />;
  case "Card":
    return <CardView {...props} />;
}
```

---

### GridView, ListView, CardView Components

**Shared Responsibilities**:
- Render records in specific layout
- Handle row/card selection
- Apply virtualization (if enabled)
- Accessibility (ARIA labels, keyboard nav)

**GridView** - Fluent UI DataGrid
**ListView** - Vertical list with primary field prominent
**Card** - Responsive card grid (1-4 columns)

---

## Service Layer

### EntityConfigurationService

**Location**: `src/services/EntityConfigurationService.ts`

**Purpose**: Load and manage entity configuration JSON

**Key Methods**:
```typescript
class EntityConfigurationService {
  static loadConfiguration(configJson: string): void;
  static getEntityConfiguration(entityName: string): IDatasetConfig;
  static isConfigurationLoaded(): boolean;
  static reset(): void;
}
```

**Logic**:
```typescript
// Merge defaults with entity-specific config
const defaultConfig = configuration.defaultConfig || {};
const entityConfig = configuration.entityConfigs?.[entityName] || {};

return {
  ...DEFAULT_CONFIG,        // Built-in defaults
  ...defaultConfig,         // User defaults
  ...entityConfig           // Entity overrides
};
```

---

### CommandRegistry

**Location**: `src/services/CommandRegistry.ts`

**Purpose**: Register and retrieve commands (built-in + custom)

**Key Methods**:
```typescript
class CommandRegistry {
  static registerCommand(command: ICommand): void;
  static getCommands(entityName?: string): ICommand[];
  static getCommand(key: string): ICommand | undefined;
  static reset(): void;
}
```

**Usage**:
```typescript
// Register built-in commands
CommandRegistry.registerCommand({
  key: "create",
  label: "New",
  icon: <AddRegular />,
  requiresSelection: false,
  handler: (context) => context.navigation.openForm({ entityName })
});

// Retrieve for toolbar
const commands = CommandRegistry.getCommands(entityName);
```

---

### CommandExecutor

**Location**: `src/services/CommandExecutor.ts`

**Purpose**: Execute commands with error handling

**Key Methods**:
```typescript
class CommandExecutor {
  static async executeCommand(
    command: ICommand,
    context: ICommandContext
  ): Promise<void>;
}
```

**Error Handling**:
```typescript
try {
  await command.handler(context);
} catch (error) {
  console.error(`Command ${command.key} failed:`, error);
  // Error automatically surfaces to user via PCF
}
```

---

### CustomCommandFactory

**Location**: `src/services/CustomCommandFactory.ts`

**Purpose**: Create ICommand objects from JSON configuration

**Key Methods**:
```typescript
class CustomCommandFactory {
  static createCommand(
    key: string,
    config: ICustomCommandConfiguration
  ): ICommand;

  private static async executeCustomApi(...): Promise<void>;
  private static async executeAction(...): Promise<void>;
  private static async executeFunction(...): Promise<void>;
  private static async executeWorkflow(...): Promise<void>;
  private static interpolateTokens(...): any;
}
```

**Token Interpolation**:
```typescript
private static interpolateTokens(
  params: Record<string, string>,
  context: ICommandContext
): any {
  const interpolated: any = {};

  for (const [key, value] of Object.entries(params)) {
    if (value === "{selectedRecordId}") {
      interpolated[key] = context.selectedRecords[0]?.id;
    } else if (value === "{selectedRecordIds}") {
      interpolated[key] = context.selectedRecords.map(r => r.id).join(",");
    } else if (value === "{selectedCount}") {
      interpolated[key] = context.selectedRecords.length.toString();
    } else {
      interpolated[key] = value;  // Literal
    }
  }

  return interpolated;
}
```

---

### FieldSecurityService

**Location**: `src/services/FieldSecurityService.ts`

**Purpose**: Query and cache field-level security metadata

**Key Methods**:
```typescript
class FieldSecurityService {
  static async canRead(entityName: string, fieldName: string): Promise<boolean>;
  static async canUpdate(entityName: string, fieldName: string): Promise<boolean>;
  static clearCache(): void;
}
```

**Caching**:
- Metadata cached per entity
- Cache cleared on form refresh
- Reduces Dataverse API calls

---

### PrivilegeService

**Location**: `src/services/PrivilegeService.ts`

**Purpose**: Check user privileges for entities

**Key Methods**:
```typescript
class PrivilegeService {
  static async hasPrivilege(entityName: string, privilege: string): Promise<boolean>;
  static clearCache(): void;
}
```

**Privileges**:
- `prvCreate<Entity>` - Create records
- `prvRead<Entity>` - Read records
- `prvWrite<Entity>` - Update records
- `prvDelete<Entity>` - Delete records

---

## React Hooks

### useVirtualization

**Location**: `src/hooks/useVirtualization.ts`

**Purpose**: Determine if virtualization should be enabled

**Signature**:
```typescript
function useVirtualization(
  recordCount: number,
  threshold: number,
  enabled: boolean
): boolean
```

**Logic**:
```typescript
return enabled && recordCount > threshold;
```

**Usage**:
```typescript
const shouldVirtualize = useVirtualization(
  records.length,
  config.virtualizationThreshold || 100,
  config.enableVirtualization !== false
);

if (shouldVirtualize) {
  return <VirtualizedGrid />;
} else {
  return <StandardGrid />;
}
```

---

### useKeyboardShortcuts

**Location**: `src/hooks/useKeyboardShortcuts.ts`

**Purpose**: Register keyboard shortcuts for commands

**Signature**:
```typescript
function useKeyboardShortcuts(
  commands: ICommand[],
  context: any,
  enabled: boolean
): void
```

**Shortcuts**:
```typescript
const shortcuts = {
  "Ctrl+N": "create",
  "Ctrl+R": "refresh",
  "Delete": "delete",
  "Enter": "open"
};
```

**Event Handler**:
```typescript
useEffect(() => {
  if (!enabled) return;

  const handleKeyDown = (event: KeyboardEvent) => {
    const key = `${event.ctrlKey ? 'Ctrl+' : ''}${event.key}`;
    const commandKey = shortcuts[key];
    if (commandKey) {
      const command = commands.find(c => c.key === commandKey);
      if (command && !command.requiresSelection || selectedRecords.length > 0) {
        event.preventDefault();
        CommandExecutor.executeCommand(command, context);
      }
    }
  };

  window.addEventListener("keydown", handleKeyDown);
  return () => window.removeEventListener("keydown", handleKeyDown);
}, [commands, context, enabled]);
```

---

### useDatasetMode

**Location**: `src/hooks/useDatasetMode.ts`

**Purpose**: Detect dataset vs. headless mode and provide unified interface

**Signature**:
```typescript
function useDatasetMode(
  dataset?: ComponentFramework.PropertyTypes.DataSet,
  headlessConfig?: IHeadlessConfig
): {
  isHeadless: boolean;
  records: any[];
  columns: IColumn[];
  selectedRecords: any[];
  entityName: string;
}
```

**Logic**:
```typescript
if (dataset) {
  return {
    isHeadless: false,
    records: dataset.records.map(recordId => dataset.records[recordId]),
    columns: dataset.columns.map(col => ({ name: col.name, ... })),
    selectedRecords: dataset.getSelectedRecordIds().map(id => dataset.records[id]),
    entityName: dataset.getTargetEntityType()
  };
} else {
  return {
    isHeadless: true,
    records: headlessConfig.records,
    columns: headlessConfig.columns,
    selectedRecords: [],
    entityName: headlessConfig.entityName || "unknown"
  };
}
```

---

### useHeadlessMode

**Location**: `src/hooks/useHeadlessMode.ts`

**Purpose**: Support headless mode (non-PCF usage)

**Use Case**: Use component in custom pages, canvas apps, or standalone React apps

**Example**:
```typescript
const headlessConfig = {
  entityName: "account",
  records: [
    { id: "1", name: "Acme Corp", city: "NYC" },
    { id: "2", name: "Contoso", city: "Seattle" }
  ],
  columns: [
    { name: "name", displayName: "Name", dataType: "SingleLine.Text" },
    { name: "city", displayName: "City", dataType: "SingleLine.Text" }
  ]
};

<UniversalDatasetGrid
  context={mockContext}
  headlessConfig={headlessConfig}
/>
```

---

## Type System

### Core Types

**ICommand** (`src/types/CommandTypes.ts`):
```typescript
interface ICommand {
  key: string;
  label: string;
  icon?: React.ReactElement;
  requiresSelection?: boolean;
  minSelection?: number;
  maxSelection?: number;
  handler: (context: ICommandContext) => void | Promise<void>;
}
```

**ICommandContext** (`src/types/CommandTypes.ts`):
```typescript
interface ICommandContext {
  webAPI: any;
  navigation: any;
  entityName: string;
  selectedRecords: any[];
  parentRecordId?: string;
  currentUserId?: string;
  refresh: () => void;
}
```

**IDatasetConfig** (`src/types/DatasetTypes.ts`):
```typescript
interface IDatasetConfig {
  viewMode?: ViewMode;
  compactToolbar?: boolean;
  enabledCommands?: string[];
  customCommands?: Record<string, ICustomCommandConfiguration>;
  enableVirtualization?: boolean;
  virtualizationThreshold?: number;
  enableKeyboardShortcuts?: boolean;
  enableAccessibility?: boolean;
}
```

**ICustomCommandConfiguration** (`src/types/EntityConfigurationTypes.ts`):
```typescript
interface ICustomCommandConfiguration {
  label: string;
  icon?: string;
  actionType: "customapi" | "action" | "function" | "workflow";
  actionName: string;
  parameters?: Record<string, string>;
  requiresSelection?: boolean;
  minSelection?: number;
  maxSelection?: number;
  confirmationMessage?: string;
  refresh?: boolean;
}
```

---

## Design Patterns

### 1. Service Singleton Pattern

All services are static classes (singleton pattern):

```typescript
class EntityConfigurationService {
  private static configuration: IEntityConfiguration | null = null;

  static loadConfiguration(configJson: string): void { /* ... */ }
  static getEntityConfiguration(entityName: string): IDatasetConfig { /* ... */ }
}
```

**Benefits**:
- Single source of truth
- No dependency injection needed
- Easy to test (reset method)

---

### 2. Factory Pattern

CustomCommandFactory creates ICommand objects from configuration:

```typescript
class CustomCommandFactory {
  static createCommand(key: string, config: ICustomCommandConfiguration): ICommand {
    return {
      key,
      label: config.label,
      icon: config.icon ? getIconComponent(config.icon) : undefined,
      handler: async (context) => {
        switch (config.actionType) {
          case "customapi": return this.executeCustomApi(...);
          case "action": return this.executeAction(...);
          case "function": return this.executeFunction(...);
          case "workflow": return this.executeWorkflow(...);
        }
      }
    };
  }
}
```

**Benefits**:
- Encapsulates command creation logic
- Supports multiple action types
- Easy to extend with new action types

---

### 3. Hooks for State Management

React hooks manage component state and side effects:

```typescript
// Virtualization
const shouldVirtualize = useVirtualization(records.length, threshold, enabled);

// Keyboard shortcuts
useKeyboardShortcuts(commands, context, enabled);

// Dataset mode
const { records, columns } = useDatasetMode(dataset, headlessConfig);
```

**Benefits**:
- Reusable logic
- Declarative
- Testable

---

### 4. Configuration-Driven Design

All behavior controlled by JSON configuration:

```json
{
  "viewMode": "Grid",
  "customCommands": {
    "approve": { /* ... */ }
  }
}
```

**Benefits**:
- No code changes for new entities
- Configuration can be changed without redeploy
- Easy to test different configurations

---

## Extension Points

### 1. Adding New Built-in Commands

**Location**: `src/components/DatasetGrid/DatasetGrid.tsx`

**Steps**:
1. Define command in `registerBuiltInCommands()`
2. Implement handler logic
3. Add to default `enabledCommands` array

**Example**:
```typescript
const registerBuiltInCommands = () => {
  // ... existing commands

  CommandRegistry.registerCommand({
    key: "export",
    label: "Export",
    icon: <DocumentArrowRightRegular />,
    requiresSelection: false,
    handler: async (context) => {
      // Export logic here
      const records = await context.webAPI.retrieveMultipleRecords(context.entityName);
      downloadAsCSV(records);
    }
  });
};
```

---

### 2. Adding New Action Types

**Location**: `src/services/CustomCommandFactory.ts`

**Steps**:
1. Add new action type to `ICustomCommandConfiguration.actionType`
2. Implement execution method
3. Add case to handler switch statement

**Example**:
```typescript
// types/EntityConfigurationTypes.ts
interface ICustomCommandConfiguration {
  actionType: "customapi" | "action" | "function" | "workflow" | "javascript";
  // ...
}

// CustomCommandFactory.ts
private static async executeJavaScript(
  config: ICustomCommandConfiguration,
  context: ICommandContext
): Promise<void> {
  const jsCode = config.parameters?.["code"];
  const fn = new Function("context", jsCode);
  await fn(context);
}

// In createCommand handler
switch (config.actionType) {
  // ... existing cases
  case "javascript":
    return this.executeJavaScript(config, context);
}
```

---

### 3. Adding New View Modes

**Location**: `src/components/DatasetGrid/`

**Steps**:
1. Create new view component (e.g., `TableView.tsx`)
2. Add to `ViewMode` type
3. Add case to DatasetGrid routing logic

**Example**:
```typescript
// types/DatasetTypes.ts
export type ViewMode = "Grid" | "List" | "Card" | "Table";

// components/DatasetGrid/TableView.tsx
export const TableView: React.FC<IDatasetGridProps> = (props) => {
  return <table>...</table>;
};

// components/DatasetGrid/DatasetGrid.tsx
switch (viewMode) {
  // ... existing cases
  case "Table":
    return <TableView {...props} />;
}
```

---

### 4. Custom Column Renderers

**Location**: `src/types/ColumnRendererTypes.ts`

**Define Renderer Type**:
```typescript
export type IColumnRenderer = (
  value: any,
  record: any,
  column: IColumn
) => React.ReactElement;
```

**Usage**:
```typescript
const customRenderers: Record<string, IColumnRenderer> = {
  "account.revenue": (value, record, column) => (
    <span style={{ color: value > 1000000 ? "green" : "black" }}>
      ${value.toLocaleString()}
    </span>
  )
};

<GridView
  records={records}
  columns={columns}
  customRenderers={customRenderers}
/>
```

---

## Testing

### Unit Tests

**Framework**: Jest + @testing-library/react

**Coverage**: 85.88% (statements)

**Example**:
```typescript
describe("EntityConfigurationService", () => {
  it("should merge entity config with defaults", () => {
    const config = {
      schemaVersion: "1.0",
      defaultConfig: { viewMode: "Grid" },
      entityConfigs: { account: { viewMode: "Card" } }
    };

    EntityConfigurationService.loadConfiguration(JSON.stringify(config));

    const accountConfig = EntityConfigurationService.getEntityConfiguration("account");
    expect(accountConfig.viewMode).toBe("Card");
  });
});
```

---

### Integration Tests

**Framework**: Jest + @testing-library/react

**Coverage**: 84.31% (overall)

**Example**:
```typescript
describe("CommandToolbar", () => {
  it("should execute command on button click", async () => {
    const mockHandler = jest.fn();
    const commands = [{ key: "create", label: "New", handler: mockHandler }];

    renderWithProviders(<CommandToolbar commands={commands} context={mockContext} />);

    const button = screen.getByRole("button", { name: /new/i });
    await userEvent.click(button);

    expect(mockHandler).toHaveBeenCalled();
  });
});
```

---

### E2E Tests

**Framework**: Playwright

**Location**: `tests/e2e/`

**Example**:
```typescript
test("should render grid with records", async ({ page }) => {
  await page.goto("/main.aspx?pagetype=entitylist&etn=account");

  const gridPage = new UniversalDatasetGridPage(page, config);
  await gridPage.waitForControlInit();

  await expect(gridPage.grid).toBeVisible();
  const recordCount = await gridPage.getRecordCount();
  expect(recordCount).toBeGreaterThan(0);
});
```

---

## Performance Optimization

### Virtualization

**Library**: react-window

**When**: Dataset > `virtualizationThreshold` (default 100)

**Impact**:
- 1000 records: ~2000ms → ~100ms (20x faster)
- 5000 records: ~10000ms → ~100ms (100x faster)

**Implementation**:
```typescript
import { FixedSizeList } from "react-window";

const VirtualizedGrid = ({ records }) => (
  <FixedSizeList
    height={600}
    itemCount={records.length}
    itemSize={50}
    width="100%"
  >
    {({ index, style }) => (
      <div style={style}>{renderRow(records[index])}</div>
    )}
  </FixedSizeList>
);
```

---

### Memoization

**Technique**: React.memo, useMemo, useCallback

**Example**:
```typescript
const MemoizedCommandToolbar = React.memo(CommandToolbar, (prev, next) => {
  return prev.commands === next.commands &&
         prev.selectedRecords?.length === next.selectedRecords?.length;
});
```

---

### Service Caching

**FieldSecurityService** and **PrivilegeService** cache metadata:

```typescript
private static cache = new Map<string, boolean>();

static async canRead(entityName: string, fieldName: string): Promise<boolean> {
  const cacheKey = `${entityName}.${fieldName}.read`;

  if (this.cache.has(cacheKey)) {
    return this.cache.get(cacheKey)!;
  }

  const result = await this.queryFieldSecurity(entityName, fieldName);
  this.cache.set(cacheKey, result);
  return result;
}
```

---

## Best Practices

### 1. Use Strict TypeScript

```json
{
  "compilerOptions": {
    "strict": true,
    "noImplicitAny": true,
    "strictNullChecks": true
  }
}
```

---

### 2. Follow Fluent UI v9 Patterns

```typescript
import { makeStyles, tokens } from "@fluentui/react-components";

const useStyles = makeStyles({
  toolbar: {
    display: "flex",
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingVerticalM
  }
});

const CommandToolbar = () => {
  const styles = useStyles();
  return <div className={styles.toolbar}>...</div>;
};
```

---

### 3. Write Testable Code

- Pure functions when possible
- Dependency injection via props
- Avoid side effects in render
- Use service reset methods in tests

---

### 4. Handle Errors Gracefully

```typescript
try {
  await context.webAPI.execute(request);
} catch (error) {
  console.error("Command failed:", error);
  // PCF automatically shows error to user
}
```

---

### 5. Optimize Re-renders

```typescript
// Bad: Creates new array on every render
<GridView columns={dataset.columns.map(c => ({ name: c.name }))} />

// Good: Memoize transformation
const columns = useMemo(
  () => dataset.columns.map(c => ({ name: c.name })),
  [dataset.columns]
);
<GridView columns={columns} />
```

---

## Debugging

### Browser DevTools

**Console Logging**:
```typescript
console.log("[UniversalDatasetGrid] Records loaded:", records.length);
console.log("[CommandExecutor] Executing command:", command.key);
```

**React DevTools**:
- Inspect component props
- View hooks state
- Profile re-renders

---

### PCF Debugging

**Enable PCF debugging**:
1. Press F12 in browser
2. Sources tab → Event Listener Breakpoints → Control → "PCF Control Loaded"
3. Set breakpoints in TypeScript (source maps enabled)

---

### Network Inspection

**Check Custom API calls**:
1. F12 → Network tab
2. Filter: XHR
3. Look for `/api/data/v9.2/` requests
4. Check request payload and response

---

## Common Pitfalls

### 1. Mutating PCF Dataset Directly

**Bad**:
```typescript
dataset.records[0].name = "New Name";  // Mutates PCF object
```

**Good**:
```typescript
const records = dataset.records.map(r => ({ ...r }));  // Clone
records[0].name = "New Name";
```

---

### 2. Forgetting to Reset Services in Tests

**Bad**:
```typescript
it("test 1", () => {
  EntityConfigurationService.loadConfiguration(config1);
  // ...
});

it("test 2", () => {
  // Still using config1 from test 1!
});
```

**Good**:
```typescript
beforeEach(() => {
  EntityConfigurationService.reset();
  CommandRegistry.reset();
});
```

---

### 3. Not Handling Async Errors

**Bad**:
```typescript
const handler = async (context) => {
  await context.webAPI.execute(request);  // No error handling
};
```

**Good**:
```typescript
const handler = async (context) => {
  try {
    await context.webAPI.execute(request);
  } catch (error) {
    console.error("API call failed:", error);
  }
};
```

---

## Next Steps

- [API Reference](../api/UniversalDatasetGrid.md) - Complete API documentation
- [Configuration Guide](./ConfigurationGuide.md) - Configuration options
- [Custom Commands Guide](./CustomCommands.md) - Create custom commands
- [Deployment Guide](./DeploymentGuide.md) - Deploy to Dataverse
- [Examples](../examples/) - Code examples
