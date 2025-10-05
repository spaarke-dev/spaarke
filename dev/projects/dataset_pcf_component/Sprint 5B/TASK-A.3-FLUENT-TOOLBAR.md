# Task A.3: Implement Fluent UI Toolbar

**Sprint:** 5B - Universal Dataset Grid Compliance
**Phase:** A - Architecture Refactor
**Priority:** HIGH
**Estimated Effort:** 2-3 hours
**Status:** 🔴 Not Started
**Depends On:** Task A.1 (Single React Root)

---

## Objective

Replace the raw HTML toolbar buttons with Fluent UI v9 `Toolbar` component for proper layout, accessibility, and theme compliance.

---

## Current Issues

**From Compliance Assessment (Section 2.2):**
> "Even though `CommandBar.tsx` uses Fluent components, it is outside the main React tree, making state management clumsy and preventing hooks/context sharing."

**Current Implementation:**
- CommandBar uses Fluent Buttons ✅
- BUT: Uses plain `<div>` wrapper with inline styles ❌
- Should use Fluent UI `Toolbar` component for proper layout

---

## Target Implementation

**Before:**
```tsx
<div style={{ display: 'flex', padding: '...', background: '...' }}>
  <Button>Add File</Button>
  <Button>Remove File</Button>
</div>
```

**After:**
```tsx
<Toolbar>
  <ToolbarButton icon={<Add24Regular />}>Add File</ToolbarButton>
  <ToolbarDivider />
  <ToolbarButton icon={<Delete24Regular />}>Remove File</ToolbarButton>
</Toolbar>
```

---

## Implementation Steps

### Step 1: Update CommandBar Component

**File:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/CommandBar.tsx`

**AI Coding Instructions:**

```typescript
/**
 * Update CommandBar.tsx to use Fluent UI Toolbar
 *
 * CHANGES:
 * 1. Import Toolbar components from @fluentui/react-components
 * 2. Replace outer <div> with <Toolbar>
 * 3. Use ToolbarButton instead of Button (optional, but more semantic)
 * 4. Remove inline styles from wrapper div
 * 5. Use Toolbar's built-in layout and spacing
 */

import * as React from 'react';
import {
    Toolbar,
    ToolbarButton,
    ToolbarDivider,
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
 * Fluent UI v9 command bar using Toolbar component.
 *
 * Provides file operation buttons with proper layout, spacing, and theming.
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
        <Toolbar
            aria-label="File operations toolbar"
            style={{
                borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
                padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`
            }}
        >
            {/* Add File Button */}
            <Tooltip content="Upload a file to the selected document" relationship="label">
                <ToolbarButton
                    appearance="primary"
                    icon={<Add24Regular />}
                    disabled={selectedCount !== 1 || hasFile}
                    onClick={() => onCommandExecute('addFile')}
                >
                    Add File
                </ToolbarButton>
            </Tooltip>

            <ToolbarDivider />

            {/* Remove File Button */}
            <Tooltip content="Delete the file from the selected document" relationship="label">
                <ToolbarButton
                    icon={<Delete24Regular />}
                    disabled={selectedCount !== 1 || !hasFile}
                    onClick={() => onCommandExecute('removeFile')}
                >
                    Remove File
                </ToolbarButton>
            </Tooltip>

            {/* Update File Button */}
            <Tooltip content="Replace the file in the selected document" relationship="label">
                <ToolbarButton
                    icon={<ArrowUpload24Regular />}
                    disabled={selectedCount !== 1 || !hasFile}
                    onClick={() => onCommandExecute('updateFile')}
                >
                    Update File
                </ToolbarButton>
            </Tooltip>

            {/* Download Button */}
            <Tooltip content="Download the selected file(s)" relationship="label">
                <ToolbarButton
                    icon={<ArrowDownload24Regular />}
                    disabled={selectedCount === 0 || (selectedRecord !== null && !hasFile)}
                    onClick={() => onCommandExecute('downloadFile')}
                >
                    Download
                </ToolbarButton>
            </Tooltip>

            {/* Selection Counter */}
            {selectedCount > 0 && (
                <>
                    <ToolbarDivider />
                    <div
                        style={{
                            padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
                            color: tokens.colorNeutralForeground2
                        }}
                    >
                        {selectedCount} selected
                    </div>
                </>
            )}
        </Toolbar>
    );
};
```

---

### Step 2: Add Refresh Button to Toolbar

**AI Coding Instructions:**

```typescript
/**
 * Add a Refresh button to the toolbar
 *
 * This replaces the old toolbar refresh button that was in renderMinimalGrid()
 */

// Import the refresh icon
import {
    Add24Regular,
    Delete24Regular,
    ArrowUpload24Regular,
    ArrowDownload24Regular,
    ArrowClockwise24Regular // ADD THIS
} from '@fluentui/react-icons';

// Add onRefresh prop to CommandBarProps
interface CommandBarProps {
    config: GridConfiguration;
    selectedRecordIds: string[];
    selectedRecords: ComponentFramework.PropertyHelper.DataSetApi.EntityRecord[];
    onCommandExecute: (commandId: string) => void;
    onRefresh: () => void; // ADD THIS
}

// Add Refresh button after Download button in the Toolbar
<Tooltip content="Refresh the dataset" relationship="label">
    <ToolbarButton
        icon={<ArrowClockwise24Regular />}
        onClick={props.onRefresh}
    >
        Refresh
    </ToolbarButton>
</Tooltip>
```

---

### Step 3: Update UniversalDatasetGridRoot

**File:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/UniversalDatasetGridRoot.tsx`

**AI Coding Instructions:**

```typescript
/**
 * Update UniversalDatasetGridRoot to pass onRefresh callback
 */

export const UniversalDatasetGridRoot: React.FC<UniversalDatasetGridRootProps> = ({
    context,
    notifyOutputChanged,
    config
}) => {
    const dataset = context.parameters.dataset;

    // ... existing code ...

    // Add refresh handler
    const handleRefresh = React.useCallback(() => {
        console.log('[UniversalDatasetGridRoot] Refreshing dataset');
        dataset.refresh();
    }, [dataset]);

    return (
        <div style={{ display: 'flex', flexDirection: 'column', height: '100%', width: '100%' }}>
            <CommandBar
                config={config}
                selectedRecordIds={selectedRecordIds}
                selectedRecords={selectedRecords}
                onCommandExecute={handleCommandExecute}
                onRefresh={handleRefresh} {/* ADD THIS */}
            />

            <DatasetGrid
                dataset={dataset}
                selectedRecordIds={selectedRecordIds}
                onSelectionChange={handleSelectionChange}
            />
        </div>
    );
};
```

---

## Testing Checklist

### Build & Deploy
- [ ] Build succeeds: `npm run build`
- [ ] ESLint passes
- [ ] Bundle size check
- [ ] Deploy: `pac pcf push --publisher-prefix sprk`

### Visual Testing
- [ ] Toolbar has proper Fluent UI styling
- [ ] Buttons aligned horizontally
- [ ] Dividers between button groups
- [ ] Selection counter aligned to right
- [ ] Hover states work on buttons
- [ ] Disabled buttons have proper opacity

### Functional Testing
- [ ] All buttons still work (Add, Remove, Update, Download, Refresh)
- [ ] Tooltips appear on hover
- [ ] Button states update based on selection
- [ ] Refresh button refreshes dataset

### Accessibility Testing
- [ ] Tab navigation works through toolbar
- [ ] Aria-label on toolbar
- [ ] Button labels read by screen reader
- [ ] Disabled state announced

---

## Validation Criteria

### Success Criteria:
1. ✅ Uses Fluent UI `Toolbar` component
2. ✅ Uses `ToolbarButton` instead of plain `Button`
3. ✅ No inline styles on wrapper (uses Toolbar's layout)
4. ✅ Proper spacing with `ToolbarDivider`
5. ✅ All buttons functional
6. ✅ Refresh functionality works

### Anti-Patterns to Avoid:
- ❌ Plain `<div>` wrapper with manual flexbox
- ❌ Inline CSS for layout/spacing
- ❌ Missing aria-label on toolbar
- ❌ Buttons without icons

---

## References

- **Fluent UI Toolbar Docs:** https://react.fluentui.dev/?path=/docs/components-toolbar--default
- **Compliance Assessment:** Section 4.2 "Fluent UI Toolbar & Grid"

---

## Completion Criteria

Task A.3 is complete when:
1. Toolbar component implemented
2. All buttons working correctly
3. Visual validation passed
4. Accessibility validation passed
5. No console errors or warnings

---

_Task Version: 1.0_
_Created: 2025-10-05_
_Status: Ready for Implementation_
_Requires: Task A.1 completion_
