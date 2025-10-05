# Shared Library Enhancement Plan: Fluent UI v9 Components & Icons

**Date:** October 4, 2025
**Status:** 📋 **PLAN - READY TO IMPLEMENT**
**Goal:** Create reusable Fluent UI v9 component and icon library for entire system

---

## Vision

Create `@spaarke/ui-components` as the **single source of truth** for:
1. ✅ Fluent UI v9 icons (central registry)
2. ✅ Fluent UI v9 components (Button, Dialog, Tooltip, etc.)
3. ✅ Common UI patterns (CommandBar, DataGrid, Forms, etc.)
4. ✅ Theme configuration (consistent styling)

**Used by:**
- PCF controls (Universal Grid, Document Viewer, Office Edit, etc.)
- React-based pages (admin pages, custom forms)
- Model-driven apps (web resources)
- Power Pages (portal components)

---

## ADR Compliance

**ADR-012: Shared Component Library** ✅
> "Create @spaarke/ui-components for reusable React components"

**Fluent UI v9 Standards** ✅
> "Use Fluent UI v9 with selective imports for consistent UX"

**Benefits:**
- Single place to upgrade Fluent UI versions
- Consistent UX across all applications
- Reduce bundle sizes (no duplication)
- Type-safe component usage
- Easier testing and maintenance

---

## Current State

**@spaarke/ui-components exists:**
- Location: `src/shared/Spaarke.UI.Components/`
- Package: `@spaarke/ui-components@1.0.0`
- Size: 195 KB
- Has peer dependencies on Fluent UI

**But:**
- ❌ Uses monolithic `@fluentui/react-components` (7 MB)
- ❌ No icon registry
- ❌ No reusable components exported
- ❌ Minimal content (mostly infrastructure)

---

## Implementation Plan

### Phase 1: Update Package Dependencies (30 min)

**Remove monolithic packages:**
```bash
cd src/shared/Spaarke.UI.Components
npm uninstall @fluentui/react-components
```

**Install selective Fluent UI v9 packages:**
```bash
npm install --save-peer \
  @fluentui/react-button@^9.6.7 \
  @fluentui/react-dialog@^9.15.3 \
  @fluentui/react-tooltip@^9.8.6 \
  @fluentui/react-progress@^9.4.6 \
  @fluentui/react-spinner@^9.7.6 \
  @fluentui/react-message-bar@^9.6.8 \
  @fluentui/react-input@^9.6.14 \
  @fluentui/react-label@^9.3.14 \
  @fluentui/react-theme@^9.2.0 \
  @fluentui/react-provider@^9.22.6 \
  @fluentui/react-icons@^2.0.311
```

**Why peer dependencies?**
- Consuming projects (PCF controls) install the actual packages
- Library doesn't bundle them
- Prevents version conflicts
- Smaller package size

---

### Phase 2: Create Icon Registry (1 hour)

**File:** `src/shared/Spaarke.UI.Components/src/icons/SpkIcons.tsx`

```typescript
/**
 * Spaarke Icon Library - Fluent UI v9 Icons
 *
 * Central registry for all icons used across the Spaarke platform.
 * Import from this library instead of directly from @fluentui/react-icons.
 *
 * Usage:
 *   import { SpkIcons } from '@spaarke/ui-components';
 *   <Button icon={<SpkIcons.Add />}>Add</Button>
 *
 * ADR: Use Fluent UI v9 icons exclusively
 */

import * as React from 'react';
import {
    // File operations
    Add24Regular,
    Delete24Regular,
    ArrowUpload24Regular,
    ArrowDownload24Regular,
    DocumentAdd24Regular,
    DocumentEdit24Regular,
    FolderOpen24Regular,

    // Navigation
    Home24Regular,
    Settings24Regular,
    People24Regular,
    Apps24Regular,

    // Status
    CheckmarkCircle24Regular,
    ErrorCircle24Regular,
    Warning24Regular,
    Info24Regular,

    // Actions
    Save24Regular,
    Dismiss24Regular,
    Edit24Regular,
    Search24Regular,
    Filter24Regular,
    MoreVertical24Regular,

    // Common
    ChevronRight24Regular,
    ChevronDown24Regular,
    ChevronLeft24Regular,
    ChevronUp24Regular,
} from '@fluentui/react-icons';

/**
 * Spaarke icon collection.
 * All icons are 24x24 Regular variant for consistency.
 */
export const SpkIcons = {
    // File Operations
    Add: Add24Regular,
    Delete: Delete24Regular,
    Upload: ArrowUpload24Regular,
    Download: ArrowDownload24Regular,
    DocumentAdd: DocumentAdd24Regular,
    DocumentEdit: DocumentEdit24Regular,
    FolderOpen: FolderOpen24Regular,

    // Navigation
    Home: Home24Regular,
    Settings: Settings24Regular,
    People: People24Regular,
    Apps: Apps24Regular,

    // Status
    Success: CheckmarkCircle24Regular,
    Error: ErrorCircle24Regular,
    Warning: Warning24Regular,
    Info: Info24Regular,

    // Actions
    Save: Save24Regular,
    Cancel: Dismiss24Regular,
    Edit: Edit24Regular,
    Search: Search24Regular,
    Filter: Filter24Regular,
    More: MoreVertical24Regular,

    // Common
    ChevronRight: ChevronRight24Regular,
    ChevronDown: ChevronDown24Regular,
    ChevronLeft: ChevronLeft24Regular,
    ChevronUp: ChevronUp24Regular,
} as const;

/**
 * Icon name type for type-safe usage.
 */
export type SpkIconName = keyof typeof SpkIcons;

/**
 * Get icon component by name.
 */
export function getIcon(name: SpkIconName): React.ComponentType {
    return SpkIcons[name];
}
```

**Export from icons/index.ts:**
```typescript
export * from './SpkIcons';
```

---

### Phase 3: Create Reusable Components (2 hours)

#### 3.1: SpkButton Component

**File:** `src/shared/Spaarke.UI.Components/src/components/SpkButton.tsx`

```typescript
/**
 * Spaarke Button - Fluent UI v9 wrapper with Spaarke standards
 */

import * as React from 'react';
import { Button, ButtonProps } from '@fluentui/react-button';
import { Tooltip } from '@fluentui/react-tooltip';

export interface SpkButtonProps extends Omit<ButtonProps, 'icon'> {
    /** Tooltip text (optional) */
    tooltip?: string;

    /** Icon component from SpkIcons */
    icon?: React.ReactElement;
}

/**
 * Standard button component with optional tooltip.
 *
 * @example
 * <SpkButton
 *   appearance="primary"
 *   icon={<SpkIcons.Add />}
 *   tooltip="Add a new item"
 * >
 *   Add Item
 * </SpkButton>
 */
export const SpkButton: React.FC<SpkButtonProps> = ({
    tooltip,
    icon,
    children,
    ...buttonProps
}) => {
    const button = (
        <Button icon={icon} {...buttonProps}>
            {children}
        </Button>
    );

    if (tooltip) {
        return (
            <Tooltip content={tooltip} relationship="label">
                {button}
            </Tooltip>
        );
    }

    return button;
};
```

#### 3.2: SpkDialog Component

**File:** `src/shared/Spaarke.UI.Components/src/components/SpkDialog.tsx`

```typescript
/**
 * Spaarke Dialog - Fluent UI v9 wrapper
 */

import * as React from 'react';
import {
    Dialog,
    DialogTrigger,
    DialogSurface,
    DialogTitle,
    DialogBody,
    DialogActions,
    DialogContent,
    Button
} from '@fluentui/react-dialog';

export interface SpkDialogProps {
    /** Dialog open state */
    open: boolean;

    /** Dialog title */
    title: string;

    /** Dialog content */
    children: React.ReactNode;

    /** Confirm button text */
    confirmText?: string;

    /** Cancel button text */
    cancelText?: string;

    /** Confirm callback */
    onConfirm?: () => void;

    /** Cancel callback */
    onCancel?: () => void;

    /** Close callback */
    onClose?: () => void;
}

/**
 * Standard dialog component.
 */
export const SpkDialog: React.FC<SpkDialogProps> = ({
    open,
    title,
    children,
    confirmText = 'OK',
    cancelText = 'Cancel',
    onConfirm,
    onCancel,
    onClose
}) => {
    return (
        <Dialog open={open} onOpenChange={(_, data) => !data.open && onClose?.()}>
            <DialogSurface>
                <DialogBody>
                    <DialogTitle>{title}</DialogTitle>
                    <DialogContent>{children}</DialogContent>
                    <DialogActions>
                        {onCancel && (
                            <Button appearance="secondary" onClick={onCancel}>
                                {cancelText}
                            </Button>
                        )}
                        {onConfirm && (
                            <Button appearance="primary" onClick={onConfirm}>
                                {confirmText}
                            </Button>
                        )}
                    </DialogActions>
                </DialogBody>
            </DialogSurface>
        </Dialog>
    );
};
```

#### 3.3: SpkProgressDialog Component

**File:** `src/shared/Spaarke.UI.Components/src/components/SpkProgressDialog.tsx`

```typescript
/**
 * Spaarke Progress Dialog - For long-running operations
 */

import * as React from 'react';
import {
    Dialog,
    DialogSurface,
    DialogTitle,
    DialogBody,
    DialogContent,
    Button
} from '@fluentui/react-dialog';
import { ProgressBar } from '@fluentui/react-progress';
import { Spinner } from '@fluentui/react-spinner';

export interface SpkProgressDialogProps {
    /** Dialog open state */
    open: boolean;

    /** Operation title */
    title: string;

    /** Progress message */
    message: string;

    /** Progress percentage (0-100), or undefined for indeterminate */
    progress?: number;

    /** Allow cancellation */
    cancellable?: boolean;

    /** Cancel callback */
    onCancel?: () => void;
}

/**
 * Progress dialog for uploads, downloads, etc.
 */
export const SpkProgressDialog: React.FC<SpkProgressDialogProps> = ({
    open,
    title,
    message,
    progress,
    cancellable,
    onCancel
}) => {
    const isIndeterminate = progress === undefined;

    return (
        <Dialog open={open} modalType="alert">
            <DialogSurface>
                <DialogBody>
                    <DialogTitle>{title}</DialogTitle>
                    <DialogContent>
                        <div style={{ marginBottom: '16px' }}>{message}</div>
                        {isIndeterminate ? (
                            <Spinner label="Processing..." />
                        ) : (
                            <>
                                <ProgressBar value={progress / 100} />
                                <div style={{ marginTop: '8px', textAlign: 'center' }}>
                                    {progress}%
                                </div>
                            </>
                        )}
                    </DialogContent>
                    {cancellable && onCancel && (
                        <div style={{ marginTop: '16px', textAlign: 'right' }}>
                            <Button onClick={onCancel}>Cancel</Button>
                        </div>
                    )}
                </DialogBody>
            </DialogSurface>
        </Dialog>
    );
};
```

#### 3.4: SpkMessageBar Component

**File:** `src/shared/Spaarke.UI.Components/src/components/SpkMessageBar.tsx`

```typescript
/**
 * Spaarke Message Bar - For success/error/warning messages
 */

import * as React from 'react';
import { MessageBar, MessageBarIntent } from '@fluentui/react-message-bar';
import { SpkIcons } from '../icons';

export interface SpkMessageBarProps {
    /** Message type */
    intent: 'success' | 'error' | 'warning' | 'info';

    /** Message text */
    message: string;

    /** Show dismiss button */
    dismissible?: boolean;

    /** Dismiss callback */
    onDismiss?: () => void;
}

/**
 * Standard message bar for notifications.
 */
export const SpkMessageBar: React.FC<SpkMessageBarProps> = ({
    intent,
    message,
    dismissible,
    onDismiss
}) => {
    const getIcon = () => {
        switch (intent) {
            case 'success': return <SpkIcons.Success />;
            case 'error': return <SpkIcons.Error />;
            case 'warning': return <SpkIcons.Warning />;
            case 'info': return <SpkIcons.Info />;
        }
    };

    return (
        <MessageBar
            intent={intent as MessageBarIntent}
            icon={getIcon()}
        >
            {message}
            {dismissible && onDismiss && (
                <button onClick={onDismiss}>Dismiss</button>
            )}
        </MessageBar>
    );
};
```

#### 3.5: Export Components

**File:** `src/shared/Spaarke.UI.Components/src/components/index.ts`

```typescript
export * from './SpkButton';
export * from './SpkDialog';
export * from './SpkProgressDialog';
export * from './SpkMessageBar';
```

---

### Phase 4: Update Main Export (15 min)

**File:** `src/shared/Spaarke.UI.Components/src/index.ts`

```typescript
/**
 * Spaarke UI Components - Shared component library
 * Standards: ADR-012, Fluent UI v9
 */

// Icons (Fluent UI v9 only)
export * from "./icons";

// Reusable Components
export * from "./components";

// Theme
export * from "./theme";

// Types
export * from "./types";

// Utils
export * from "./utils";

// Hooks
export * from "./hooks";

// Services
export * from "./services";
```

---

### Phase 5: Update package.json (15 min)

```json
{
  "name": "@spaarke/ui-components",
  "version": "2.0.0",
  "description": "Spaarke shared UI component library - Fluent UI v9 (selective imports)",
  "main": "dist/index.js",
  "types": "dist/index.d.ts",
  "peerDependencies": {
    "@fluentui/react-button": "^9.6.7",
    "@fluentui/react-dialog": "^9.15.3",
    "@fluentui/react-tooltip": "^9.8.6",
    "@fluentui/react-progress": "^9.4.6",
    "@fluentui/react-spinner": "^9.7.6",
    "@fluentui/react-message-bar": "^9.6.8",
    "@fluentui/react-input": "^9.6.14",
    "@fluentui/react-label": "^9.3.14",
    "@fluentui/react-theme": "^9.2.0",
    "@fluentui/react-provider": "^9.22.6",
    "@fluentui/react-icons": "^2.0.311",
    "react": "^18.2.0",
    "react-dom": "^18.2.0"
  },
  "keywords": [
    "spaarke",
    "react",
    "typescript",
    "fluent-ui",
    "fluent-ui-v9",
    "components",
    "icons"
  ]
}
```

---

### Phase 6: Build and Package (15 min)

```bash
cd src/shared/Spaarke.UI.Components

# Build
npm run build

# Run tests
npm test

# Package
npm pack
# Creates: spaarke-ui-components-2.0.0.tgz
```

---

### Phase 7: Update Universal Grid (30 min)

**Install updated library:**
```bash
cd src/controls/UniversalDatasetGrid
npm uninstall @spaarke/ui-components
npm install ../../shared/Spaarke.UI.Components/spaarke-ui-components-2.0.0.tgz
```

**Update CommandBar.tsx:**

```typescript
// BEFORE:
import { Button } from '@fluentui/react-button';
import { Tooltip } from '@fluentui/react-tooltip';
import {
    Add24Regular,
    Delete24Regular,
    ArrowUpload24Regular,
    ArrowDownload24Regular
} from '@fluentui/react-icons';

// AFTER:
import { SpkButton, SpkIcons } from '@spaarke/ui-components';

// Usage:
<SpkButton
    appearance="primary"
    icon={<SpkIcons.Add />}
    tooltip="Upload a file to the selected document"
    onClick={() => onCommandExecute('addFile')}
>
    Add File
</SpkButton>
```

**Update ThemeProvider.tsx:**
```typescript
// BEFORE:
import { FluentProvider } from '@fluentui/react-provider';
import { webLightTheme } from '@fluentui/react-theme';

// AFTER:
import { FluentProvider, webLightTheme } from '@spaarke/ui-components';
// (if we export theme from shared library)
```

---

## Expected Results

### Package Sizes

**Before:**
- @spaarke/ui-components: 195 KB (mostly empty)
- Universal Grid: 3.8 MB (includes all Fluent UI)

**After:**
- @spaarke/ui-components: ~250 KB (icons + components, no bundled deps)
- Universal Grid: ~3.8 MB (same, but cleaner imports)

**Future PCF controls:**
- Document Viewer: ~2 MB (reuses icons from library)
- Office Edit: ~2 MB (reuses icons from library)

**Savings:** ~2 MB per additional PCF control (no icon duplication)

---

### Developer Experience

**Before:**
```typescript
// Every PCF control imports directly from Fluent UI
import { Button } from '@fluentui/react-button';
import { Add24Regular } from '@fluentui/react-icons';
<Button icon={<Add24Regular />}>Add</Button>
```

**After:**
```typescript
// Every PCF control uses shared library
import { SpkButton, SpkIcons } from '@spaarke/ui-components';
<SpkButton icon={<SpkIcons.Add />} tooltip="Add item">Add</SpkButton>
```

**Benefits:**
- ✅ Shorter imports
- ✅ Consistent naming
- ✅ Type safety
- ✅ Built-in tooltip support
- ✅ Central place to update Fluent UI versions

---

## Timeline

**Total: 5 hours**

- Phase 1: Update dependencies (30 min)
- Phase 2: Icon registry (1 hour)
- Phase 3: Components (2 hours)
- Phase 4: Main export (15 min)
- Phase 5: package.json (15 min)
- Phase 6: Build (15 min)
- Phase 7: Update Universal Grid (30 min)
- Testing: (15 min)

---

## Testing Plan

1. **Build @spaarke/ui-components** ✅
2. **Install in Universal Grid** ✅
3. **Build Universal Grid** ✅
4. **Verify bundle size** (should be ~3.8 MB)
5. **Test in test harness:**
   - Buttons render
   - Icons display
   - Tooltips work
   - Dialogs open/close

---

## Future Enhancements

**Additional components to add:**
- SpkDataGrid (for entity lists)
- SpkForm (form builder)
- SpkCommandBar (reusable command bar)
- SpkNavigation (navigation menu)
- SpkCard (content card)

**When:** As needed in future sprints

---

## Status

**Ready to implement:** ✅ YES
**Estimated time:** 5 hours
**Dependencies:** None (can start immediately)

**Recommendation:** Implement now before continuing Phase 2. This will:
- Establish clean architecture from the start
- Prevent refactoring later
- Make remaining Phase 2 tasks easier
- Set foundation for all future PCF controls
