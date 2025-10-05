# Task A.1: Create Single React Root Architecture

**Sprint:** 5B - Universal Dataset Grid Compliance
**Phase:** A - Architecture Refactor
**Priority:** CRITICAL
**Estimated Effort:** 4-6 hours
**Status:** 🔴 Not Started

---

## Objective

Consolidate the Universal Dataset Grid into a single React root using React 18's `createRoot()` API, eliminating multiple React trees and enabling proper component composition.

---

## Current Issues

**From Compliance Assessment (Section 2.1):**
> "ThemeProvider uses `ReactDOM.createRoot` (React 18) while the command bar still calls `ReactDOM.render`/`unmountComponentAtNode`. This mismatch can double-mount components, leak memory, and breaks modern React best practices."

**Current Architecture Problems:**
1. `ThemeProvider.ts` creates a React root
2. `CommandBar.tsx` creates a separate React root
3. `index.ts` manually manipulates DOM in `updateView()`
4. No unified component tree - can't share context/state
5. Re-creates entire DOM on every `updateView()` call

---

## Target Architecture

**Single React Root Pattern:**

```
PCF Container (index.ts)
  └─> React Root (created in init())
      └─> FluentProvider (theme wrapper)
          └─> UniversalDatasetGridRoot (main component)
              ├─> CommandBar (Fluent Toolbar)
              ├─> DataGrid (Fluent DataGrid)
              └─> Dialogs/Modals (future)
```

**Key Principles:**
- ✅ One `createRoot()` call in `init()`
- ✅ React manages all DOM updates
- ✅ Props pass context updates to components
- ✅ No manual DOM manipulation in `updateView()`

---

## Implementation Steps

### Step 1: Create Main React Component

**File:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/UniversalDatasetGridRoot.tsx`

**Purpose:** Top-level React component that receives PCF context as props

**AI Coding Instructions:**

```typescript
/**
 * Create a new file: UniversalDatasetGridRoot.tsx
 *
 * This is the main React component for the Universal Dataset Grid.
 * It receives PCF context as props and manages the component tree.
 */

import * as React from 'react';
import { IInputs } from '../generated/ManifestTypes';
import { GridConfiguration } from '../types';
import { CommandBar } from './CommandBar';

interface UniversalDatasetGridRootProps {
    /** PCF context - passed from index.ts */
    context: ComponentFramework.Context<IInputs>;

    /** Callback to notify Power Apps of state changes */
    notifyOutputChanged: () => void;

    /** Grid configuration */
    config: GridConfiguration;
}

/**
 * Main React component for Universal Dataset Grid.
 *
 * This component is rendered once in init() and receives updated
 * props in updateView() - no DOM recreation.
 */
export const UniversalDatasetGridRoot: React.FC<UniversalDatasetGridRootProps> = ({
    context,
    notifyOutputChanged,
    config
}) => {
    // Get dataset from context
    const dataset = context.parameters.dataset;

    // Track selected record IDs in React state
    const [selectedRecordIds, setSelectedRecordIds] = React.useState<string[]>(
        dataset.getSelectedRecordIds() || []
    );

    // Update selection when context changes
    React.useEffect(() => {
        const contextSelection = dataset.getSelectedRecordIds() || [];
        setSelectedRecordIds(contextSelection);
    }, [dataset]);

    // Handle command execution
    const handleCommandExecute = React.useCallback((commandId: string) => {
        console.log(`[UniversalDatasetGridRoot] Command executed: ${commandId}`);

        switch (commandId) {
            case 'addFile':
                console.log('Add File - will implement in SDAP phase');
                break;
            case 'removeFile':
                console.log('Remove File - will implement in SDAP phase');
                break;
            case 'updateFile':
                console.log('Update File - will implement in SDAP phase');
                break;
            case 'downloadFile':
                console.log('Download File - will implement in SDAP phase');
                break;
            default:
                console.warn(`Unknown command: ${commandId}`);
        }
    }, []);

    // Get selected records for command bar
    const selectedRecords = React.useMemo(() => {
        return selectedRecordIds
            .map(id => dataset.records[id])
            .filter(record => record != null);
    }, [selectedRecordIds, dataset.records]);

    return (
        <div
            style={{
                display: 'flex',
                flexDirection: 'column',
                height: '100%',
                width: '100%'
            }}
        >
            {/* Command Bar - will be replaced with Fluent Toolbar in Task A.3 */}
            <CommandBar
                config={config}
                selectedRecordIds={selectedRecordIds}
                selectedRecords={selectedRecords}
                onCommandExecute={handleCommandExecute}
            />

            {/* Grid - will be replaced with Fluent DataGrid in Task A.2 */}
            <div style={{ flex: 1, overflow: 'auto' }}>
                <div style={{ padding: '20px', textAlign: 'center' }}>
                    <h3>Dataset Grid</h3>
                    <p>Records: {dataset.sortedRecordIds.length}</p>
                    <p>Selected: {selectedRecordIds.length}</p>
                    <p>Task A.2 will implement Fluent UI DataGrid here</p>
                </div>
            </div>
        </div>
    );
};
```

---

### Step 2: Update CommandBar Component

**File:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/CommandBar.tsx`

**Changes Required:**

**AI Coding Instructions:**

```typescript
/**
 * Update CommandBar.tsx to be a pure React component
 *
 * REMOVE:
 * - The CommandBar class wrapper (lines 135-196)
 * - All DOM manipulation code
 * - createRoot/render/unmount logic
 *
 * KEEP:
 * - CommandBarComponent (the actual React component)
 * - All Fluent UI v9 imports and usage
 *
 * RENAME:
 * - Export CommandBarComponent as CommandBar (default export)
 */

// At the bottom of CommandBar.tsx, REPLACE the class with:

/**
 * Command Bar component for Universal Dataset Grid.
 * Now a pure React component - no wrapper class needed.
 */
export const CommandBar = CommandBarComponent;
```

**Full updated file:**

```typescript
import * as React from 'react';
import {
    Button,
    Tooltip,
    tokens
} from '@fluentui/react-components';
import {
    Add24Regular,
    Delete24Regular,
    ArrowUpload24Regular,
    ArrowDownload24Regular
} from '@fluentui/react-icons';
import { GridConfiguration } from '../types';

interface CommandBarProps {
    config: GridConfiguration;
    selectedRecordIds: string[];
    selectedRecords: ComponentFramework.PropertyHelper.DataSetApi.EntityRecord[];
    onCommandExecute: (commandId: string) => void;
}

/**
 * Fluent UI v9 command bar with file operation buttons.
 */
export const CommandBar: React.FC<CommandBarProps> = ({
    config,
    selectedRecordIds,
    selectedRecords,
    onCommandExecute
}) => {
    const selectedCount = selectedRecordIds.length;
    const selectedRecord = selectedCount === 1 ? selectedRecords[0] : null;

    const hasFile = selectedRecord
        ? (selectedRecord.getValue(config.fieldMappings.hasFile) as boolean) === true
        : false;

    return (
        <div
            style={{
                display: 'flex',
                padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
                background: tokens.colorNeutralBackground2,
                gap: tokens.spacingHorizontalS,
                borderBottom: `1px solid ${tokens.colorNeutralStroke1}`
            }}
        >
            <Tooltip content="Upload a file to the selected document" relationship="label">
                <Button
                    appearance="primary"
                    icon={<Add24Regular />}
                    disabled={selectedCount !== 1 || hasFile}
                    onClick={() => onCommandExecute('addFile')}
                >
                    Add File
                </Button>
            </Tooltip>

            <Tooltip content="Delete the file from the selected document" relationship="label">
                <Button
                    appearance="secondary"
                    icon={<Delete24Regular />}
                    disabled={selectedCount !== 1 || !hasFile}
                    onClick={() => onCommandExecute('removeFile')}
                >
                    Remove File
                </Button>
            </Tooltip>

            <Tooltip content="Replace the file in the selected document" relationship="label">
                <Button
                    appearance="secondary"
                    icon={<ArrowUpload24Regular />}
                    disabled={selectedCount !== 1 || !hasFile}
                    onClick={() => onCommandExecute('updateFile')}
                >
                    Update File
                </Button>
            </Tooltip>

            <Tooltip content="Download the selected file(s)" relationship="label">
                <Button
                    appearance="secondary"
                    icon={<ArrowDownload24Regular />}
                    disabled={selectedCount === 0 || (selectedRecord !== null && !hasFile)}
                    onClick={() => onCommandExecute('downloadFile')}
                >
                    Download
                </Button>
            </Tooltip>

            {selectedCount > 0 && (
                <span
                    style={{
                        padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
                        color: tokens.colorNeutralForeground2,
                        lineHeight: '32px',
                        marginLeft: 'auto'
                    }}
                >
                    {selectedCount} selected
                </span>
            )}
        </div>
    );
};
```

---

### Step 3: Simplify ThemeProvider

**File:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/providers/ThemeProvider.ts`

**AI Coding Instructions:**

```typescript
/**
 * Simplify ThemeProvider.ts
 *
 * REMOVE:
 * - providerContainer creation
 * - contentContainer management
 * - All DOM manipulation
 * - getContentContainer() method
 * - isInitialized() method
 *
 * KEEP ONLY:
 * - Theme resolution logic (for Task B.1)
 *
 * This will become a utility function, not a class.
 */

import { Theme, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { IInputs } from '../generated/ManifestTypes';

/**
 * Resolve the appropriate Fluent UI theme based on Power Apps context.
 *
 * This function will be enhanced in Task B.1 to detect theme from context.
 * For now, it returns the light theme.
 *
 * @param context - PCF context
 * @returns Fluent UI theme
 */
export function resolveTheme(context: ComponentFramework.Context<IInputs>): Theme {
    // TODO: Task B.1 will implement dynamic theme detection
    // For now, always return light theme
    return webLightTheme;
}
```

---

### Step 4: Refactor index.ts to Single Root

**File:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/index.ts`

**AI Coding Instructions:**

```typescript
/**
 * Refactor index.ts to use single React root pattern
 *
 * CRITICAL CHANGES:
 * 1. Create React root in init()
 * 2. Render UniversalDatasetGridRoot with FluentProvider
 * 3. updateView() only re-renders with new props (no DOM manipulation)
 * 4. Remove all manual DOM operations
 * 5. Remove ThemeProvider class instantiation
 * 6. Remove CommandBar class instantiation
 */

import * as React from 'react';
import * as ReactDOM from 'react-dom/client';
import { FluentProvider } from '@fluentui/react-components';
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { UniversalDatasetGridRoot } from "./components/UniversalDatasetGridRoot";
import { resolveTheme } from "./providers/ThemeProvider";
import { DEFAULT_GRID_CONFIG, GridConfiguration } from "./types";

export class UniversalDatasetGrid implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private root: ReactDOM.Root | null = null;
    private notifyOutputChanged: () => void;
    private config: GridConfiguration;

    constructor() {
        console.log('[UniversalDatasetGrid] Constructor');
        this.config = DEFAULT_GRID_CONFIG;
    }

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        console.log('[UniversalDatasetGrid] Init - Creating single React root');

        this.notifyOutputChanged = notifyOutputChanged;

        // Create single React root
        this.root = ReactDOM.createRoot(container);

        // Render React tree
        this.renderReactTree(context);

        console.log('[UniversalDatasetGrid] Init complete');
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        console.log('[UniversalDatasetGrid] UpdateView - Re-rendering with new props');

        // Just re-render with new context - React handles the updates
        this.renderReactTree(context);
    }

    public destroy(): void {
        console.log('[UniversalDatasetGrid] Destroy - Unmounting React root');

        if (this.root) {
            this.root.unmount();
            this.root = null;
        }
    }

    public getOutputs(): IOutputs {
        return {};
    }

    /**
     * Render the React component tree.
     * Called from init() and updateView().
     */
    private renderReactTree(context: ComponentFramework.Context<IInputs>): void {
        if (!this.root) {
            console.error('[UniversalDatasetGrid] Cannot render - root not initialized');
            return;
        }

        const theme = resolveTheme(context);

        this.root.render(
            React.createElement(
                FluentProvider,
                { theme },
                React.createElement(UniversalDatasetGridRoot, {
                    context,
                    notifyOutputChanged: this.notifyOutputChanged,
                    config: this.config
                })
            )
        );
    }
}
```

---

## Testing Checklist

After implementing this task:

- [ ] Build succeeds: `npm run build`
- [ ] ESLint passes: No errors or warnings
- [ ] Bundle size: Still under 5 MB
- [ ] Deploy to dev environment: `pac pcf push --publisher-prefix sprk`
- [ ] Control loads in Power Apps without errors
- [ ] Console shows: `Init - Creating single React root`
- [ ] Console shows: `UpdateView - Re-rendering with new props`
- [ ] Command bar renders with Fluent buttons
- [ ] Placeholder grid shows record count
- [ ] No memory leaks (check DevTools Memory profiler)
- [ ] No React warnings in console

---

## Validation Criteria

### Success Criteria:
1. ✅ Only ONE `createRoot()` call in entire codebase
2. ✅ No `ReactDOM.render()` or legacy APIs
3. ✅ All UI rendered through React component tree
4. ✅ No manual DOM manipulation in `updateView()`
5. ✅ Control works in Power Apps model-driven app

### Anti-Patterns to Avoid:
- ❌ Multiple React roots
- ❌ `innerHTML` or `createElement` in `updateView()`
- ❌ Clearing/rebuilding DOM manually
- ❌ Class components (use function components)

---

## References

- **Compliance Assessment:** Section 2.1 "PCF Control Architecture Gaps"
- **Compliance Assessment:** Section 4.1 "Enter a Single React Root"
- **React 18 Docs:** https://react.dev/blog/2022/03/08/react-18-upgrade-guide
- **PCF Best Practices:** https://learn.microsoft.com/power-apps/developer/component-framework/code-components-best-practices

---

## Completion Criteria

Task A.1 is complete when:
1. All code changes implemented and tested
2. Build and deployment successful
3. All validation criteria met
4. No console errors or warnings
5. Ready to proceed to Task A.2 (Fluent DataGrid)

---

_Task Version: 1.0_
_Created: 2025-10-05_
_Status: Ready for Implementation_
