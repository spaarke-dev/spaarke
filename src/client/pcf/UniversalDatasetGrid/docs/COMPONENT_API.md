# Component API Reference

Complete API documentation for all components in the Universal Dataset Grid control.

## Table of Contents
- [Core Components](#core-components)
- [Utilities](#utilities)
- [Types](#types)
- [Providers](#providers)

---

## Core Components

### UniversalDatasetGrid (PCF Control)

**File**: `UniversalDatasetGrid/index.ts`

Main PCF control entry point implementing the Power Apps Component Framework interface.

#### Class Definition
```typescript
export class UniversalDatasetGrid implements ComponentFramework.StandardControl<IInputs, IOutputs>
```

#### Lifecycle Methods

##### `constructor()`
Initializes the control with default configuration.

```typescript
constructor()
```

##### `init(context, notifyOutputChanged, state, container)`
Called when control is initialized.

**Parameters**:
- `context: ComponentFramework.Context<IInputs>` - PCF context
- `notifyOutputChanged: () => void` - Callback to notify framework of changes
- `state: ComponentFramework.Dictionary` - State from previous session
- `container: HTMLDivElement` - Container element for control

**Creates**:
- React root using `createRoot()`
- Initial React component tree

##### `updateView(context)`
Called when control needs to update (dataset changes, resize, etc.)

**Parameters**:
- `context: ComponentFramework.Context<IInputs>` - Updated PCF context

**Behavior**:
- Re-renders React tree with new props
- Does NOT unmount/remount (React 18 pattern)

##### `destroy()`
Called when control is being removed.

**Behavior**:
- Unmounts React root
- Cleans up resources

##### `getOutputs()`
Returns outputs to framework.

**Returns**: `IOutputs` (currently empty object)

#### Private Methods

##### `renderReactTree(context)`
Renders the React component tree.

**Parameters**:
- `context: ComponentFramework.Context<IInputs>` - PCF context

**Error Handling**:
- Try-catch wrapper
- Logs errors via centralized logger
- Checks root initialized before rendering

---

### UniversalDatasetGridRoot

**File**: `UniversalDatasetGrid/components/UniversalDatasetGridRoot.tsx`

Main React component that coordinates grid functionality.

#### Props Interface
```typescript
interface UniversalDatasetGridRootProps {
    context: ComponentFramework.Context<IInputs>;
    notifyOutputChanged: () => void;
    config: GridConfiguration;
}
```

#### State Management

##### `selectedRecordIds: string[]`
Currently selected record IDs.

**Updates**: When user selects/deselects rows
**Syncs**: With PCF dataset via `dataset.setSelectedRecordIds()`

#### Callbacks

##### `handleSelectionChange(recordIds: string[])`
Handles grid selection changes.

**Parameters**:
- `recordIds: string[]` - Array of selected record IDs

**Behavior**:
1. Updates local state
2. Syncs with PCF dataset
3. Debounced notify to framework (300ms)

##### `handleRefresh()`
Handles refresh button click.

**Behavior**:
- Calls `dataset.refresh()`
- Notifies framework immediately

##### File Operation Handlers
- `handleAddFile()` - Triggers add file dialog
- `handleRemoveFile()` - Removes selected file
- `handleUpdateFile()` - Updates selected file
- `handleDownload()` - Downloads selected file

**Note**: File operations currently placeholder - to be implemented in future sprint

#### Memoization

Uses `React.useMemo` for:
- Debounced `notifyOutputChanged` callback (prevents excessive PCF calls)

Uses `React.useCallback` for:
- Event handlers (prevents unnecessary re-renders)

---

### DatasetGrid

**File**: `UniversalDatasetGrid/components/DatasetGrid.tsx`

Renders the main data grid using Fluent UI DataGrid.

#### Props Interface
```typescript
interface DatasetGridProps {
    dataset: ComponentFramework.PropertyTypes.DataSet;
    selectedRecordIds: string[];
    onSelectionChange: (recordIds: string[]) => void;
}
```

#### Data Processing

##### Row Generation
Converts PCF dataset to grid rows:

```typescript
const rows: GridRow[] = dataset.sortedRecordIds.map((recordId) => {
    const record = dataset.records[recordId];
    const row: GridRow = { recordId };

    dataset.columns.forEach((column) => {
        row[column.name] = record.getFormattedValue(column.name) || '';
    });

    return row;
});
```

##### Column Definitions
Creates Fluent UI column definitions:

```typescript
const columns: TableColumnDefinition<GridRow>[] = dataset.columns.map((column) =>
    createTableColumn<GridRow>({
        columnId: column.name,
        compare: (a, b) => {
            // Sorting logic
        },
        renderHeaderCell: () => column.displayName,
        renderCell: (item) => (
            <TableCellLayout>
                {item[column.name]?.toString() || ''}
            </TableCellLayout>
        )
    })
);
```

#### Event Handlers

##### `handleSelectionChange(e, data)`
Handles grid selection events.

**Parameters**:
- `e: React.MouseEvent | React.KeyboardEvent` - Event object
- `data: { selectedItems: Set<unknown> }` - Selection data from Fluent UI

**Behavior**:
- Converts Set to Array
- Calls parent's `onSelectionChange`

#### Rendering

**Container**:
```tsx
<div style={{
    height: '100%',
    width: '100%',
    overflow: 'auto',
    background: tokens.colorNeutralBackground1
}}>
```

**DataGrid**:
- `items={rows}` - Row data
- `columns={columns}` - Column definitions
- `sortable` - Enable column sorting
- `selectionMode="multiselect"` - Multiple row selection
- `selectedItems={new Set(selectedRecordIds)}` - Controlled selection
- `getRowId={(item) => item.recordId}` - Row key
- `focusMode="composite"` - Keyboard navigation
- `size="medium"` - Fluent UI sizing

#### Loading States

Shows loading message when columns not ready:
```tsx
if (!dataset.columns || dataset.columns.length === 0) {
    return <div>Loading columns...</div>;
}
```

---

### CommandBar

**File**: `UniversalDatasetGrid/components/CommandBar.tsx`

Toolbar with file operations and grid controls.

#### Props Interface
```typescript
interface CommandBarProps {
    selectedCount: number;
    onAddFile?: () => void;
    onRemoveFile?: () => void;
    onUpdateFile?: () => void;
    onDownload?: () => void;
    onRefresh?: () => void;
}
```

#### Buttons

##### Add File
- Icon: `DocumentAdd20Regular`
- Label: "Add File"
- Disabled: Never
- Handler: `onAddFile`

##### Remove File
- Icon: `Delete20Regular`
- Label: "Remove File"
- Disabled: `selectedCount === 0`
- Handler: `onRemoveFile`

##### Update File
- Icon: `ArrowUpload20Regular`
- Label: "Update File"
- Disabled: `selectedCount !== 1` (exactly one file must be selected)
- Handler: `onUpdateFile`

##### Download
- Icon: `ArrowDownload20Regular`
- Label: "Download"
- Disabled: `selectedCount === 0`
- Handler: `onDownload`

##### Refresh
- Icon: `ArrowClockwise20Regular`
- Label: "Refresh"
- Disabled: Never
- Handler: `onRefresh`

#### Selection Counter

Shows number of selected items:
```tsx
<div style={{
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    color: tokens.colorNeutralForeground2,
    visibility: selectedCount > 0 ? 'visible' : 'hidden',
    minWidth: '100px' // Prevents layout shift
}}>
    {selectedCount > 0 ? `${selectedCount} selected` : '\u00A0'}
</div>
```

**Note**: Always rendered (with `visibility: hidden`) to prevent layout shift

---

### ErrorBoundary

**File**: `UniversalDatasetGrid/components/ErrorBoundary.tsx`

React Error Boundary to catch and display errors gracefully.

#### Props Interface
```typescript
interface ErrorBoundaryProps {
    children: React.ReactNode;
}
```

#### State Interface
```typescript
interface ErrorBoundaryState {
    hasError: boolean;
    error: Error | null;
}
```

#### Lifecycle Methods

##### `static getDerivedStateFromError(error)`
React lifecycle method called when error caught.

**Parameters**:
- `error: Error` - The error thrown

**Returns**: Updated state with error

##### `componentDidCatch(error, errorInfo)`
Logs error details.

**Parameters**:
- `error: Error` - The error thrown
- `errorInfo: React.ErrorInfo` - Component stack trace

**Behavior**: Logs to centralized logger

#### Error UI

When error occurs, displays:
- ❌ "Something went wrong" heading
- Error message
- Expandable error details (stack trace)
- Styled with Fluent UI tokens

---

## Utilities

### Logger

**File**: `UniversalDatasetGrid/utils/logger.ts`

Centralized logging utility for structured logging.

#### Log Levels
```typescript
enum LogLevel {
    DEBUG = 0,
    INFO = 1,
    WARN = 2,
    ERROR = 3
}
```

#### API

##### `setLogLevel(level: LogLevel)`
Sets minimum log level to display.

**Example**:
```typescript
import { logger, LogLevel } from './utils/logger';

// Show only errors
logger.setLogLevel(LogLevel.ERROR);

// Show all logs (development)
logger.setLogLevel(LogLevel.DEBUG);
```

##### `debug(component: string, message: string, ...args: unknown[])`
Logs debug message (development only).

**Example**:
```typescript
logger.debug('DatasetGrid', 'Row data processed', { rowCount: 100 });
```

##### `info(component: string, message: string, ...args: unknown[])`
Logs informational message.

**Example**:
```typescript
logger.info('Control', 'Init complete');
```

##### `warn(component: string, message: string, ...args: unknown[])`
Logs warning message.

**Example**:
```typescript
logger.warn('ThemeProvider', 'No theme info from Power Apps, using default');
```

##### `error(component: string, message: string, error?: Error | unknown, ...args: unknown[])`
Logs error message.

**Example**:
```typescript
try {
    // risky operation
} catch (error) {
    logger.error('Control', 'Operation failed', error);
}
```

#### Log Format
```
[UniversalDatasetGrid][ComponentName] message
```

**Example output**:
```
[UniversalDatasetGrid][Control] Init complete
[UniversalDatasetGrid][ThemeProvider] Dark mode detected
[UniversalDatasetGrid][DatasetGrid] Rendered 100 rows
```

---

## Types

### GridConfiguration

**File**: `UniversalDatasetGrid/types/index.ts`

Configuration for grid behavior.

```typescript
export interface GridConfiguration {
    enablePaging: boolean;
    pageSize: number;
    enableSorting: boolean;
    enableFiltering: boolean;
}

export const DEFAULT_GRID_CONFIG: GridConfiguration = {
    enablePaging: false,
    pageSize: 5000,
    enableSorting: true,
    enableFiltering: false
};
```

### GridRow

**Internal type** for grid rows.

```typescript
interface GridRow {
    recordId: string;
    [key: string]: string | number | boolean | Date | null;
}
```

**Properties**:
- `recordId` - Unique record identifier
- Dynamic properties matching dataset column names

---

## Providers

### ThemeProvider

**File**: `UniversalDatasetGrid/providers/ThemeProvider.ts`

Resolves Fluent UI theme from Power Apps context.

#### API

##### `resolveTheme(context: ComponentFramework.Context<IInputs>): Theme`
Determines appropriate theme based on Power Apps environment.

**Algorithm**:
1. Check if `context.fluentDesignLanguage.tokenTheme` exists
2. Extract `colorNeutralBackground1` color
3. Calculate color luminance:
   ```typescript
   luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255
   ```
4. If luminance < 0.5 → Dark theme
5. Else → Light theme
6. Fallback to light theme if error

**Returns**:
- `webDarkTheme` - For dark mode
- `webLightTheme` - For light mode (default)

**Error Handling**:
- Try-catch wrapper
- Logs warnings
- Graceful fallback to light theme

#### Helper Functions

##### `isColorDark(color: string): boolean`
Determines if a color is dark.

**Parameters**:
- `color: string` - Hex color (e.g., "#1a1a1a")

**Algorithm**:
1. Parse hex to RGB
2. Calculate relative luminance
3. Return true if luminance < 0.5

**Example**:
```typescript
isColorDark("#1a1a1a") // true (dark)
isColorDark("#ffffff") // false (light)
```

---

## Usage Examples

### Basic Control Usage in Power Apps

1. **Add to Form**:
   - Open form designer
   - Add Universal Dataset Grid control
   - Bind to dataset property

2. **Configure Dataset**:
   ```javascript
   // In Power Apps
   UpdateContext({
       GridDataset: Filter(Matters, Status = "Active")
   })
   ```

3. **Handle Selection**:
   ```javascript
   // Access selected items
   UniversalDatasetGrid.Selected
   ```

### Custom Logging

```typescript
import { logger, LogLevel } from './utils/logger';

// Development environment
if (process.env.NODE_ENV === 'development') {
    logger.setLogLevel(LogLevel.DEBUG);
}

// Log operation
logger.info('MyComponent', 'Operation started');

// Log with data
logger.debug('MyComponent', 'Processing', { count: items.length });

// Log errors
try {
    // risky operation
} catch (error) {
    logger.error('MyComponent', 'Operation failed', error);
}
```

### Custom Theme Detection

```typescript
import { resolveTheme } from './providers/ThemeProvider';

function MyComponent({ context }) {
    const theme = resolveTheme(context);

    return (
        <FluentProvider theme={theme}>
            {/* Your components */}
        </FluentProvider>
    );
}
```

### Error Boundary Usage

```typescript
import { ErrorBoundary } from './components/ErrorBoundary';

function App() {
    return (
        <ErrorBoundary>
            <MyRiskyComponent />
        </ErrorBoundary>
    );
}
```

---

## Development Guidelines

### Adding New Components

1. Create component file in `components/` folder
2. Use functional components (no classes)
3. TypeScript with strict mode
4. Props interface with JSDoc comments
5. Use Fluent UI design tokens (no hardcoded colors)
6. Add error handling for risky operations
7. Use centralized logger
8. Export from component file

### Adding New Features

1. Update TypeScript interfaces
2. Add to GridConfiguration if configurable
3. Implement with React hooks
4. Add logging for key operations
5. Handle errors gracefully
6. Update this documentation
7. Add to README.md

### Best Practices

1. ✅ Always use design tokens from Fluent UI
2. ✅ Memoize expensive calculations with `useMemo`
3. ✅ Memoize callbacks with `useCallback`
4. ✅ Add loading states for async operations
5. ✅ Log errors with context
6. ✅ Wrap risky operations in try-catch
7. ✅ Use TypeScript strict mode
8. ✅ Pass ESLint with zero warnings

---

## Troubleshooting

### Common Issues

**Component not rendering**:
- Check ErrorBoundary caught error
- Review console logs
- Verify PCF context valid

**Theme not applying**:
- Check `fluentDesignLanguage` available
- Review theme detection logs
- Verify Fluent UI tokens used

**Selection not working**:
- Check `onSelectionChange` called
- Verify `selectedRecordIds` state updated
- Check PCF dataset sync

**Performance issues**:
- Check dataset size (<1000 records ideal)
- Verify debouncing working
- Review component re-renders

---

**Last Updated**: 2025-10-05
**Version**: 2.0.7
