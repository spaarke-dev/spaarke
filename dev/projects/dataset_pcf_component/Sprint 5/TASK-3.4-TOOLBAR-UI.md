# TASK-3.4: Toolbar UI Enhancements

**Sprint:** Sprint 5 - Universal Dataset PCF Component
**Phase:** 3 - Advanced Features
**Estimated Time:** 3 hours
**Prerequisites:** TASK-3.1 (Command System)
**Next Task:** TASK-3.5 (Entity Configuration)

---

## Objective

Enhance the CommandToolbar component with advanced UI features including command groups, overflow menu for small screens, keyboard shortcuts, improved accessibility, and better visual feedback. This ensures the toolbar is production-ready for complex enterprise scenarios.

**Why This Matters:**
- **Usability:** Users need clear visual hierarchy and keyboard navigation
- **Accessibility:** WCAG 2.1 AA compliance for enterprise applications
- **Responsive Design:** Toolbar must work on various screen sizes
- **User Experience:** Loading states, tooltips, and keyboard shortcuts improve efficiency
- **ADR Alignment:** ADR-012 shared component library supports accessible, reusable components

---

## Critical Standards

**Must Read:**
- [KM-UX-FLUENT-DESIGN-V9-STANDARDS.md](../../../docs/KM-UX-FLUENT-DESIGN-V9-STANDARDS.md) - Toolbar patterns, accessibility
- [DATASET-COMPONENT-COMMANDS.md](./DATASET-COMPONENT-COMMANDS.md) - Command system architecture
- [ADR-012-SHARED-COMPONENT-LIBRARY.md](../../../docs/ADR-012-SHARED-COMPONENT-LIBRARY.md) - Shared library architecture

**Key Rules:**
1. ✅ Fluent UI v9 Toolbar components only (NO v8)
2. ✅ ARIA labels for all interactive elements
3. ✅ Keyboard navigation support (Tab, Enter, Arrow keys)
4. ✅ Tooltips for icon-only buttons
5. ✅ Loading and disabled states
6. ✅ Command grouping (primary, secondary, overflow)
7. ✅ Responsive design (overflow menu on small screens)
8. ✅ All work in `src/shared/Spaarke.UI.Components/`

---

## Current Implementation Review

### Existing CommandToolbar.tsx
**Location:** `src/shared/Spaarke.UI.Components/src/components/Toolbar/CommandToolbar.tsx`

**Current Features:**
- ✅ Basic Fluent UI v9 Toolbar
- ✅ Command execution with loading state
- ✅ Disabled state based on `canExecute`
- ✅ Icon display
- ✅ Spinner during execution

**Missing Features:**
- ❌ Command grouping (primary vs secondary)
- ❌ Overflow menu for many commands
- ❌ Tooltips for accessibility
- ❌ Keyboard shortcuts
- ❌ ARIA labels
- ❌ Command descriptions
- ❌ Visual separation between groups
- ❌ Icon-only mode for compact layout

---

## Implementation Steps

### Step 1: Enhance Command Types with Grouping

**File:** `src/shared/Spaarke.UI.Components/src/types/CommandTypes.ts`

**Add to ICommand interface:**
```typescript
export interface ICommand {
  key: string;
  label: string;
  icon?: React.ReactElement;

  // Execution
  execute: (context: ICommandContext) => Promise<void>;
  canExecute?: (context: ICommandContext) => boolean;
  requiresSelection?: boolean;

  // UI Enhancements (NEW)
  group?: "primary" | "secondary" | "overflow"; // Command group
  description?: string; // For tooltips and screen readers
  keyboardShortcut?: string; // e.g., "Ctrl+N"
  iconOnly?: boolean; // Show only icon (with tooltip)
  dividerAfter?: boolean; // Add divider after this command

  // Validation
  confirmationMessage?: string;

  // Behavior
  refresh?: boolean;
  successMessage?: string;
}
```

**Why:**
- Group commands for visual hierarchy
- Support keyboard shortcuts
- Enable icon-only mode for compact layouts
- Add descriptions for accessibility

---

### Step 2: Create Enhanced CommandToolbar

**File:** `src/shared/Spaarke.UI.Components/src/components/Toolbar/CommandToolbar.tsx`

**Replace entire file:**
```typescript
/**
 * CommandToolbar - Enhanced toolbar with groups, overflow, and accessibility
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */

import * as React from "react";
import {
  Toolbar,
  ToolbarButton,
  ToolbarDivider,
  ToolbarGroup,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  Tooltip,
  makeStyles,
  tokens,
  Spinner
} from "@fluentui/react-components";
import { MoreHorizontal20Regular } from "@fluentui/react-icons";
import { ICommand, ICommandContext } from "../../types/CommandTypes";
import { CommandExecutor } from "../../services/CommandExecutor";

export interface ICommandToolbarProps {
  commands: ICommand[];
  context: ICommandContext;
  onCommandExecuted?: (commandKey: string) => void;
  compact?: boolean; // Icon-only mode
  showOverflow?: boolean; // Enable overflow menu (default: true)
}

const useStyles = makeStyles({
  toolbar: {
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    minHeight: "44px"
  },
  toolbarCompact: {
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    minHeight: "36px"
  },
  shortcut: {
    marginLeft: tokens.spacingHorizontalM,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3
  }
});

export const CommandToolbar: React.FC<ICommandToolbarProps> = (props) => {
  const styles = useStyles();
  const [executingCommand, setExecutingCommand] = React.useState<string | null>(null);

  // Group commands
  const { primaryCommands, secondaryCommands, overflowCommands } = React.useMemo(() => {
    const primary: ICommand[] = [];
    const secondary: ICommand[] = [];
    const overflow: ICommand[] = [];

    props.commands.forEach((cmd) => {
      const group = cmd.group ?? "primary";
      if (group === "primary") primary.push(cmd);
      else if (group === "secondary") secondary.push(cmd);
      else overflow.push(cmd);
    });

    // Auto-overflow: If >8 commands total, move secondary to overflow
    const showOverflow = props.showOverflow ?? true;
    if (showOverflow && primary.length + secondary.length > 8) {
      overflow.unshift(...secondary);
      secondary.length = 0;
    }

    return {
      primaryCommands: primary,
      secondaryCommands: secondary,
      overflowCommands: overflow
    };
  }, [props.commands, props.showOverflow]);

  // Execute command
  const handleCommandClick = React.useCallback(async (command: ICommand) => {
    setExecutingCommand(command.key);

    try {
      await CommandExecutor.execute(command, props.context);

      if (props.onCommandExecuted) {
        props.onCommandExecuted(command.key);
      }
    } catch (error) {
      console.error(`Command ${command.key} failed`, error);
    } finally {
      setExecutingCommand(null);
    }
  }, [props]);

  // Render command button
  const renderCommandButton = (command: ICommand, inMenu: boolean = false) => {
    const canExecute = CommandExecutor.canExecute(command, props.context);
    const isExecuting = executingCommand === command.key;
    const showIconOnly = props.compact || command.iconOnly;

    const button = (
      <ToolbarButton
        key={command.key}
        icon={isExecuting ? <Spinner size="tiny" /> : command.icon}
        disabled={!canExecute || isExecuting}
        onClick={() => handleCommandClick(command)}
        aria-label={command.description || command.label}
        aria-keyshortcuts={command.keyboardShortcut}
      >
        {!showIconOnly && command.label}
      </ToolbarButton>
    );

    // Wrap with tooltip if icon-only or has description
    if (showIconOnly || command.description) {
      const tooltipContent = (
        <>
          {command.label}
          {command.description && <div>{command.description}</div>}
          {command.keyboardShortcut && (
            <span className={styles.shortcut}>{command.keyboardShortcut}</span>
          )}
        </>
      );

      return (
        <Tooltip key={command.key} content={tooltipContent} relationship="description">
          {button}
        </Tooltip>
      );
    }

    return button;
  };

  // Render overflow menu
  const renderOverflowMenu = () => {
    if (overflowCommands.length === 0) return null;

    return (
      <Menu>
        <MenuTrigger disableButtonEnhancement>
          <Tooltip content="More commands" relationship="label">
            <ToolbarButton
              aria-label="More commands"
              icon={<MoreHorizontal20Regular />}
            />
          </Tooltip>
        </MenuTrigger>
        <MenuPopover>
          <MenuList>
            {overflowCommands.map((command) => {
              const canExecute = CommandExecutor.canExecute(command, props.context);
              const isExecuting = executingCommand === command.key;

              return (
                <MenuItem
                  key={command.key}
                  icon={isExecuting ? <Spinner size="tiny" /> : command.icon}
                  disabled={!canExecute || isExecuting}
                  onClick={() => handleCommandClick(command)}
                  secondaryContent={command.keyboardShortcut}
                >
                  {command.label}
                </MenuItem>
              );
            })}
          </MenuList>
        </MenuPopover>
      </Menu>
    );
  };

  return (
    <Toolbar
      aria-label="Command toolbar"
      className={`${styles.toolbar} ${props.compact ? styles.toolbarCompact : ""}`}
    >
      {/* Primary commands */}
      {primaryCommands.length > 0 && (
        <ToolbarGroup>
          {primaryCommands.map((command, index) => (
            <React.Fragment key={command.key}>
              {renderCommandButton(command)}
              {command.dividerAfter && <ToolbarDivider />}
            </React.Fragment>
          ))}
        </ToolbarGroup>
      )}

      {/* Divider between primary and secondary */}
      {primaryCommands.length > 0 && secondaryCommands.length > 0 && (
        <ToolbarDivider />
      )}

      {/* Secondary commands */}
      {secondaryCommands.length > 0 && (
        <ToolbarGroup>
          {secondaryCommands.map((command) => renderCommandButton(command))}
        </ToolbarGroup>
      )}

      {/* Overflow menu */}
      {overflowCommands.length > 0 && (
        <>
          {(primaryCommands.length > 0 || secondaryCommands.length > 0) && (
            <ToolbarDivider />
          )}
          <ToolbarGroup>
            {renderOverflowMenu()}
          </ToolbarGroup>
        </>
      )}
    </Toolbar>
  );
};
```

**Key Enhancements:**
- ✅ Command grouping (primary, secondary, overflow)
- ✅ Auto-overflow when >8 commands
- ✅ Tooltips with descriptions and keyboard shortcuts
- ✅ ARIA labels for accessibility
- ✅ Icon-only mode (compact)
- ✅ Keyboard shortcut display
- ✅ Overflow menu with MenuList
- ✅ Visual dividers between groups

---

### Step 3: Update CommandRegistry with Enhanced Metadata

**File:** `src/shared/Spaarke.UI.Components/src/services/CommandRegistry.ts`

**Update built-in commands with new metadata:**
```typescript
// Add imports
import {
  Add20Regular,
  Open20Regular,
  Delete20Regular,
  ArrowClockwise20Regular
} from "@fluentui/react-icons";
import * as React from "react";

// Update registerBuiltInCommands method
private static registerBuiltInCommands(): void {
  // Create command
  this.register({
    key: "create",
    label: "New",
    icon: React.createElement(Add20Regular),
    group: "primary",
    description: "Create a new record",
    keyboardShortcut: "Ctrl+N",
    execute: async (context) => {
      context.navigation.openForm({
        entityName: context.entityName,
        useQuickCreateForm: false
      });
    },
    canExecute: (context) => true
  });

  // Open command
  this.register({
    key: "open",
    label: "Open",
    icon: React.createElement(Open20Regular),
    group: "primary",
    description: "Open selected record",
    keyboardShortcut: "Ctrl+O",
    requiresSelection: true,
    execute: async (context) => {
      const record = context.selectedRecords[0];
      context.navigation.openForm({
        entityName: record.entityName,
        entityId: record.id
      });
    }
  });

  // Delete command
  this.register({
    key: "delete",
    label: "Delete",
    icon: React.createElement(Delete20Regular),
    group: "secondary",
    description: "Delete selected records",
    keyboardShortcut: "Delete",
    requiresSelection: true,
    confirmationMessage: "Delete {count} selected records?",
    refresh: true,
    dividerAfter: true,
    execute: async (context) => {
      for (const record of context.selectedRecords) {
        await context.webAPI.deleteRecord(record.entityName, record.id);
      }
    }
  });

  // Refresh command
  this.register({
    key: "refresh",
    label: "Refresh",
    icon: React.createElement(ArrowClockwise20Regular),
    group: "secondary",
    description: "Refresh the grid",
    keyboardShortcut: "F5",
    execute: async (context) => {
      if (context.refresh) {
        await context.refresh();
      }
    },
    canExecute: (context) => !!context.refresh
  });
}
```

**Why:**
- Icons from Fluent UI v9 icon library
- Groups for visual hierarchy (create/open primary, delete/refresh secondary)
- Descriptions for tooltips
- Keyboard shortcuts for power users
- Divider after delete to separate destructive action

---

### Step 4: Add Keyboard Shortcut Handler

**File:** `src/shared/Spaarke.UI.Components/src/hooks/useKeyboardShortcuts.ts`

**Create new file:**
```typescript
import { useEffect } from "react";
import { ICommand, ICommandContext } from "../types/CommandTypes";
import { CommandExecutor } from "../services/CommandExecutor";

export interface UseKeyboardShortcutsOptions {
  commands: ICommand[];
  context: ICommandContext;
  enabled?: boolean;
}

/**
 * Hook to register keyboard shortcuts for commands
 */
export function useKeyboardShortcuts(options: UseKeyboardShortcutsOptions): void {
  const { commands, context, enabled = true } = options;

  useEffect(() => {
    if (!enabled) return;

    const handleKeyDown = async (event: KeyboardEvent) => {
      // Build shortcut key (e.g., "Ctrl+N", "F5", "Delete")
      const parts: string[] = [];
      if (event.ctrlKey || event.metaKey) parts.push("Ctrl");
      if (event.shiftKey) parts.push("Shift");
      if (event.altKey) parts.push("Alt");

      // Map key codes to friendly names
      let keyName = event.key;
      if (keyName === " ") keyName = "Space";
      if (keyName.length === 1) keyName = keyName.toUpperCase();

      parts.push(keyName);
      const shortcut = parts.join("+");

      // Find command with matching shortcut
      const command = commands.find(
        (cmd) => cmd.keyboardShortcut === shortcut
      );

      if (!command) return;

      // Check if command can execute
      if (!CommandExecutor.canExecute(command, context)) return;

      // Prevent default browser behavior
      event.preventDefault();
      event.stopPropagation();

      // Execute command
      try {
        await CommandExecutor.execute(command, context);
      } catch (error) {
        console.error(`Keyboard shortcut ${shortcut} failed`, error);
      }
    };

    window.addEventListener("keydown", handleKeyDown);

    return () => {
      window.removeEventListener("keydown", handleKeyDown);
    };
  }, [commands, context, enabled]);
}
```

**Why:**
- Enables keyboard navigation (accessibility)
- Power user efficiency
- Prevents default browser behavior
- Validates command can execute before running

---

### Step 5: Update UniversalDatasetGrid to Use Keyboard Shortcuts

**File:** `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/UniversalDatasetGrid.tsx`

**Add import:**
```typescript
import { useKeyboardShortcuts } from "../../hooks/useKeyboardShortcuts";
```

**Add hook call after command context:**
```typescript
// Enable keyboard shortcuts
useKeyboardShortcuts({
  commands,
  context: commandContext,
  enabled: props.config.showToolbar && commands.length > 0
});
```

**Why:**
- Keyboard shortcuts only active when toolbar is shown
- Uses same command context as toolbar
- Can be disabled via config

---

### Step 6: Update CommandToolbar Props in UniversalDatasetGrid

**File:** `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/UniversalDatasetGrid.tsx`

**Update CommandToolbar rendering:**
```typescript
{props.config.showToolbar && (
  <CommandToolbar
    commands={commands}
    context={commandContext}
    compact={props.config.compactToolbar}
    showOverflow={props.config.toolbarShowOverflow}
    onCommandExecuted={(key) => {
      console.log(`Command executed: ${key}`);
    }}
  />
)}
```

---

### Step 7: Update IDatasetConfig Type

**File:** `src/shared/Spaarke.UI.Components/src/types/DatasetTypes.ts`

**Add to IDatasetConfig interface:**
```typescript
export interface IDatasetConfig {
  // ... existing props

  // Toolbar configuration (NEW)
  compactToolbar?: boolean;           // Icon-only mode
  toolbarShowOverflow?: boolean;      // Enable overflow menu (default: true)
}
```

---

### Step 8: Export New Hook

**File:** `src/shared/Spaarke.UI.Components/src/hooks/index.ts`

**Add export:**
```typescript
export * from "./types";
export * from "./useDatasetMode";
export * from "./useHeadlessMode";
export * from "./useVirtualization";
export * from "./useKeyboardShortcuts"; // NEW
```

---

### Step 9: Build and Verify

```bash
cd src/shared/Spaarke.UI.Components
npm run build
```

**Validation:**
- ✅ Build succeeds with 0 TypeScript errors
- ✅ Enhanced toolbar compiles
- ✅ Keyboard shortcuts hook exports
- ✅ Type definitions updated

---

## Validation Checklist

Run these commands to verify completion:

```bash
# 1. Verify files exist
cd src/shared/Spaarke.UI.Components
ls src/hooks/useKeyboardShortcuts.ts
ls src/components/Toolbar/CommandToolbar.tsx

# 2. Build succeeds
npm run build

# 3. Verify exports
grep -r "useKeyboardShortcuts" src/hooks/index.ts
grep -r "CommandToolbar" src/components/index.ts
```

---

## Success Criteria

- ✅ Command grouping (primary, secondary, overflow)
- ✅ Auto-overflow when >8 commands
- ✅ Tooltips with descriptions and keyboard shortcuts
- ✅ ARIA labels for all buttons
- ✅ Keyboard shortcut handler (`useKeyboardShortcuts`)
- ✅ Icon-only mode (compact)
- ✅ Visual dividers between command groups
- ✅ Overflow menu with MenuList
- ✅ Loading states during command execution
- ✅ Disabled states based on `canExecute`
- ✅ Fluent UI v9 components throughout
- ✅ Build succeeds with 0 errors

---

## Accessibility Features

| Feature | Implementation | WCAG Criteria |
|---------|---------------|---------------|
| ARIA labels | `aria-label` on all buttons | 4.1.2 Name, Role, Value |
| Keyboard shortcuts | `aria-keyshortcuts` attribute | 2.1.1 Keyboard |
| Tooltips | `Tooltip` with `relationship="description"` | 1.3.1 Info and Relationships |
| Focus management | Fluent Toolbar built-in | 2.4.3 Focus Order |
| Disabled states | `disabled` prop with visual feedback | 4.1.2 Name, Role, Value |
| Screen reader text | `aria-label` with descriptions | 1.1.1 Non-text Content |

---

## Performance Considerations

- **Memoization**: Command grouping memoized with `useMemo`
- **Event listeners**: Keyboard shortcuts cleaned up on unmount
- **Overflow**: Auto-overflow prevents toolbar from becoming too wide
- **Icons**: Fluent UI v9 icons are tree-shakeable

---

## Common Issues

### Issue: Keyboard shortcuts conflict with browser shortcuts
**Solution:** Check for conflicts and use custom combinations (e.g., `Ctrl+Shift+N` instead of `Ctrl+N`)

### Issue: Overflow menu not appearing
**Solution:** Set `showOverflow={true}` explicitly or check if >8 commands exist

### Issue: Tooltips not showing
**Solution:** Ensure Tooltip wraps the button and has `content` prop

### Issue: ARIA warnings in console
**Solution:** Verify all interactive elements have `aria-label` or visible text

---

## Deliverables

- ✅ Enhanced `src/components/Toolbar/CommandToolbar.tsx` with groups and overflow
- ✅ New `src/hooks/useKeyboardShortcuts.ts` hook
- ✅ Updated `src/services/CommandRegistry.ts` with icons and metadata
- ✅ Updated `src/types/CommandTypes.ts` with new ICommand properties
- ✅ Updated `src/types/DatasetTypes.ts` with toolbar config
- ✅ Updated exports in `src/hooks/index.ts`
- ✅ Build output with 0 errors

---

## Next Steps

1. ✅ Mark TASK-3.4 complete
2. ➡️ Proceed to [TASK-3.5-ENTITY-CONFIGURATION.md](./TASK-3.5-ENTITY-CONFIGURATION.md)
3. Test toolbar with multiple commands
4. Verify keyboard shortcuts work
5. Test accessibility with screen reader

---

**Estimated Time:** 3 hours
**Actual Time:** _(Fill in upon completion)_
**Completion Date:** _(Fill in upon completion)_
**Status:** 📝 Ready for execution
