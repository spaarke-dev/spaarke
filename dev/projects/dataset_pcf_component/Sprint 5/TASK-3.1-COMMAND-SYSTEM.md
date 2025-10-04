# Task 3.1: Implement Command System and Toolbar

**Sprint:** Sprint 5 - Universal Dataset PCF Component
**Phase:** 3 - Advanced Features
**Estimated Time:** 5 hours
**Prerequisites:** [TASK-2.4-CARD-LIST-VIEWS.md](./TASK-2.4-CARD-LIST-VIEWS.md)
**Next Task:** [TASK-3.2-COLUMN-RENDERERS.md](./TASK-3.2-COLUMN-RENDERERS.md)

---

## Objective

Implement a flexible command system that supports:
- **Built-in commands**: Create, Open, Delete, Refresh, Upload
- **Custom commands**: Power Automate flows, Custom APIs, Actions
- **Toolbar UI**: Fluent UI buttons with icons and tooltips
- **Context-aware execution**: Commands receive selected records and entity context

**Why:** Users need to perform actions on records. The command system provides a standard way to execute operations while maintaining flexibility for custom scenarios.

---

## Critical Standards

**MUST READ BEFORE STARTING:**
- [KM-PCF-CONTROL-STANDARDS.md](../../../docs/KM-PCF-CONTROL-STANDARDS.md) - Web API patterns, navigation
- [KM-UX-FLUENT-DESIGN-V9-STANDARDS.md](../../../docs/KM-UX-FLUENT-DESIGN-V9-STANDARDS.md) - Button patterns, icons

**Key Rules:**
- ✅ Use Web API for data operations
- ✅ Navigation API for opening records
- ✅ Fluent UI v9 icons from `@fluentui/react-icons`
- ✅ All commands are async (return Promise)
- ✅ Commands in shared library for reusability

---

## Step 1: Define Command Types

**Create `src/shared/Spaarke.UI.Components/src/types/CommandTypes.ts`:**

```typescript
/**
 * Command system types
 */

import { IDatasetRecord } from "./DatasetTypes";

/**
 * Command context provided to command handlers
 */
export interface ICommandContext {
  selectedRecords: IDatasetRecord[];
  entityName: string;
  webAPI: ComponentFramework.WebApi;
  navigation: ComponentFramework.Navigation;
  refresh?: () => void;
  parentRecord?: ComponentFramework.EntityReference;
  emitLastAction?: (action: string) => void;
}

/**
 * Command handler function signature
 */
export type CommandHandler = (context: ICommandContext) => Promise<void>;

/**
 * Command definition
 */
export interface ICommand {
  key: string;
  label: string;
  icon?: React.ReactElement;
  handler: CommandHandler;
  requiresSelection?: boolean;
  multiSelectSupport?: boolean;
  confirmationMessage?: string;
}

/**
 * Custom command configuration (from manifest)
 */
export interface ICustomCommandConfig {
  key: string;
  label: string;
  actionType: "workflow" | "customapi" | "action" | "function";
  actionName: string;
  icon?: string;
  requiresSelection?: boolean;
}
```

**Update `src/types/index.ts`:**

```typescript
export * from "./DatasetTypes";
export * from "./CommandTypes";
```

---

## Step 2: Create CommandRegistry

**Create `src/shared/Spaarke.UI.Components/src/services/CommandRegistry.ts`:**

```typescript
/**
 * CommandRegistry - Manages built-in and custom commands
 */

import * as React from "react";
import {
  AddRegular,
  DeleteRegular,
  ArrowSyncRegular,
  OpenRegular,
  ArrowUploadRegular
} from "@fluentui/react-icons";
import { ICommand, ICommandContext } from "../types/CommandTypes";

/**
 * Built-in command handlers
 */
export class CommandRegistry {

  /**
   * Create new record
   */
  static createCommand(): ICommand {
    return {
      key: "create",
      label: "New",
      icon: React.createElement(AddRegular),
      requiresSelection: false,
      handler: async (context: ICommandContext) => {
        const entityRef = {
          entityType: context.entityName
        };

        context.navigation.openForm({
          entityName: context.entityName,
          useQuickCreateForm: false
        }).then(() => {
          if (context.refresh) {
            context.refresh();
          }
          if (context.emitLastAction) {
            context.emitLastAction("create");
          }
        });
      }
    };
  }

  /**
   * Open selected record
   */
  static openCommand(): ICommand {
    return {
      key: "open",
      label: "Open",
      icon: React.createElement(OpenRegular),
      requiresSelection: true,
      multiSelectSupport: false,
      handler: async (context: ICommandContext) => {
        if (context.selectedRecords.length === 0) {
          throw new Error("No record selected");
        }

        const record = context.selectedRecords[0];
        context.navigation.openForm({
          entityName: context.entityName,
          entityId: record.id,
          openInNewWindow: false
        });

        if (context.emitLastAction) {
          context.emitLastAction("open");
        }
      }
    };
  }

  /**
   * Delete selected records
   */
  static deleteCommand(): ICommand {
    return {
      key: "delete",
      label: "Delete",
      icon: React.createElement(DeleteRegular),
      requiresSelection: true,
      multiSelectSupport: true,
      confirmationMessage: "Are you sure you want to delete the selected record(s)?",
      handler: async (context: ICommandContext) => {
        if (context.selectedRecords.length === 0) {
          throw new Error("No records selected");
        }

        // Delete all selected records
        for (const record of context.selectedRecords) {
          await context.webAPI.deleteRecord(context.entityName, record.id);
        }

        // Refresh dataset
        if (context.refresh) {
          context.refresh();
        }

        if (context.emitLastAction) {
          context.emitLastAction("delete");
        }
      }
    };
  }

  /**
   * Refresh dataset
   */
  static refreshCommand(): ICommand {
    return {
      key: "refresh",
      label: "Refresh",
      icon: React.createElement(ArrowSyncRegular),
      requiresSelection: false,
      handler: async (context: ICommandContext) => {
        if (context.refresh) {
          context.refresh();
        }

        if (context.emitLastAction) {
          context.emitLastAction("refresh");
        }
      }
    };
  }

  /**
   * Upload file (example custom command)
   */
  static uploadCommand(): ICommand {
    return {
      key: "upload",
      label: "Upload",
      icon: React.createElement(ArrowUploadRegular),
      requiresSelection: false,
      handler: async (context: ICommandContext) => {
        // Trigger file picker (implementation depends on use case)
        console.log("Upload command executed");

        if (context.emitLastAction) {
          context.emitLastAction("upload");
        }
      }
    };
  }

  /**
   * Get command by key
   */
  static getCommand(key: string): ICommand | undefined {
    switch (key.toLowerCase()) {
      case "create":
        return this.createCommand();
      case "open":
        return this.openCommand();
      case "delete":
        return this.deleteCommand();
      case "refresh":
        return this.refreshCommand();
      case "upload":
        return this.uploadCommand();
      default:
        return undefined;
    }
  }

  /**
   * Get multiple commands by keys
   */
  static getCommands(keys: string[]): ICommand[] {
    return keys
      .map(key => this.getCommand(key))
      .filter((cmd): cmd is ICommand => cmd !== undefined);
  }
}
```

---

## Step 3: Create CommandExecutor

**Create `src/shared/Spaarke.UI.Components/src/services/CommandExecutor.ts`:**

```typescript
/**
 * CommandExecutor - Executes commands with error handling
 */

import { ICommand, ICommandContext } from "../types/CommandTypes";

export class CommandExecutor {

  /**
   * Execute a command with error handling and confirmation
   */
  static async execute(
    command: ICommand,
    context: ICommandContext
  ): Promise<void> {
    try {
      // Validation: Check if selection required
      if (command.requiresSelection && context.selectedRecords.length === 0) {
        throw new Error(`${command.label} requires at least one record to be selected`);
      }

      // Validation: Check single selection
      if (command.requiresSelection && !command.multiSelectSupport && context.selectedRecords.length > 1) {
        throw new Error(`${command.label} can only be performed on a single record`);
      }

      // Confirmation dialog (if required)
      if (command.confirmationMessage) {
        const confirmed = window.confirm(command.confirmationMessage);
        if (!confirmed) {
          return; // User cancelled
        }
      }

      // Execute command
      await command.handler(context);

    } catch (error) {
      console.error(`Command '${command.key}' failed:`, error);
      throw error; // Re-throw for caller to handle
    }
  }

  /**
   * Check if command can be executed (validation only, no execution)
   */
  static canExecute(command: ICommand, context: ICommandContext): boolean {
    if (command.requiresSelection && context.selectedRecords.length === 0) {
      return false;
    }

    if (command.requiresSelection && !command.multiSelectSupport && context.selectedRecords.length > 1) {
      return false;
    }

    return true;
  }
}
```

---

## Step 4: Create Toolbar Component

**Create `src/shared/Spaarke.UI.Components/src/components/Toolbar/CommandToolbar.tsx`:**

```typescript
/**
 * CommandToolbar - Displays command buttons
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */

import * as React from "react";
import {
  Toolbar,
  ToolbarButton,
  makeStyles,
  tokens,
  Spinner
} from "@fluentui/react-components";
import { ICommand, ICommandContext } from "../../types/CommandTypes";
import { CommandExecutor } from "../../services/CommandExecutor";

export interface ICommandToolbarProps {
  commands: ICommand[];
  context: ICommandContext;
  onCommandExecuted?: (commandKey: string) => void;
}

const useStyles = makeStyles({
  toolbar: {
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    padding: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM
  }
});

export const CommandToolbar: React.FC<ICommandToolbarProps> = (props) => {
  const styles = useStyles();
  const [executingCommand, setExecutingCommand] = React.useState<string | null>(null);

  const handleCommandClick = React.useCallback(async (command: ICommand) => {
    setExecutingCommand(command.key);

    try {
      await CommandExecutor.execute(command, props.context);

      if (props.onCommandExecuted) {
        props.onCommandExecuted(command.key);
      }
    } catch (error) {
      // Error already logged in CommandExecutor
      console.error(`Command ${command.key} failed`, error);
    } finally {
      setExecutingCommand(null);
    }
  }, [props]);

  return (
    <Toolbar className={styles.toolbar}>
      {props.commands.map((command) => {
        const canExecute = CommandExecutor.canExecute(command, props.context);
        const isExecuting = executingCommand === command.key;

        return (
          <ToolbarButton
            key={command.key}
            icon={isExecuting ? <Spinner size="tiny" /> : command.icon}
            disabled={!canExecute || isExecuting}
            onClick={() => handleCommandClick(command)}
          >
            {command.label}
          </ToolbarButton>
        );
      })}
    </Toolbar>
  );
};
```

**Create `src/components/Toolbar/index.ts`:**

```typescript
export * from "./CommandToolbar";
```

---

## Step 5: Create Services Index

**Create `src/services/index.ts`:**

```typescript
export * from "./CommandRegistry";
export * from "./CommandExecutor";
```

---

## Step 6: Update Main Index

**Update `src/index.ts`:**

```typescript
/**
 * Spaarke UI Components - Shared component library
 * Standards: ADR-012, KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */

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

// Components
export * from "./components";
```

---

## Step 7: Integrate Toolbar into UniversalDatasetGrid

**Update `src/components/DatasetGrid/UniversalDatasetGrid.tsx`:**

Add imports:
```typescript
import { CommandToolbar } from "../Toolbar/CommandToolbar";
import { CommandRegistry } from "../../services/CommandRegistry";
import { ICommandContext } from "../../types/CommandTypes";
```

Add toolbar before content:
```typescript
return (
  <FluentProvider theme={theme}>
    <div className={styles.root}>
      {/* Command Toolbar */}
      {props.config.showToolbar && (
        <CommandToolbar
          commands={getCommands()}
          context={getCommandContext()}
          onCommandExecuted={(key) => {
            console.log(`Command executed: ${key}`);
          }}
        />
      )}

      <div className={styles.content}>
        {/* existing view components */}
      </div>
    </div>
  </FluentProvider>
);
```

Add helper functions:
```typescript
// Get commands based on config
const getCommands = React.useCallback(() => {
  return CommandRegistry.getCommands(props.config.enabledCommands);
}, [props.config.enabledCommands]);

// Build command context
const getCommandContext = React.useCallback((): ICommandContext => {
  return {
    selectedRecords: records.filter(r => props.selectedRecordIds.includes(r.id)),
    entityName: records[0]?.entityName || "",
    webAPI: props.headlessConfig?.webAPI || (props.dataset as any)?.parent?.webAPI || ({} as any),
    navigation: (props.context as any)?.navigation || ({} as any),
    refresh: result.refresh,
    emitLastAction: (action) => {
      console.log(`Last action: ${action}`);
    }
  };
}, [records, props.selectedRecordIds, props.headlessConfig, props.dataset, props.context, result.refresh]);
```

---

## Step 8: Export Components

**Update `src/components/index.ts`:**

```typescript
// Dataset components
export * from "./DatasetGrid/UniversalDatasetGrid";
export * from "./DatasetGrid/GridView";
export * from "./DatasetGrid/CardView";
export * from "./DatasetGrid/ListView";

// Toolbar components
export * from "./Toolbar";
```

---

## Step 9: Build and Test

```bash
# Build shared library
cd /c/code_files/spaarke/src/shared/Spaarke.UI.Components
npm run build

# Expected output: Successfully compiled
```

---

## Validation Checklist

```bash
# 1. Verify command types created
cd /c/code_files/spaarke/src/shared/Spaarke.UI.Components
cat src/types/CommandTypes.ts | grep "ICommand"
# Should show interface

# 2. Verify services created
ls src/services/
# Should show CommandRegistry.ts, CommandExecutor.ts

# 3. Verify toolbar component
ls src/components/Toolbar/
# Should show CommandToolbar.tsx

# 4. Verify build succeeds
npm run build
# Should succeed with 0 errors

# 5. Check exports
cat dist/services/index.d.ts
# Should export CommandRegistry, CommandExecutor
```

---

## Success Criteria

- ✅ CommandTypes defined (ICommand, ICommandContext, CommandHandler)
- ✅ CommandRegistry implements 5 built-in commands
- ✅ CommandExecutor handles validation and errors
- ✅ CommandToolbar renders Fluent UI buttons
- ✅ Toolbar integrated into UniversalDatasetGrid
- ✅ Commands receive proper context
- ✅ Disabled state works (no selection when required)
- ✅ Loading state shows spinner
- ✅ Confirmation dialogs work
- ✅ All services in shared library

---

## Built-in Commands

| Command | Icon | Requires Selection | Multi-Select | Action |
|---------|------|-------------------|--------------|---------|
| **Create** | AddRegular | ❌ No | N/A | Opens new record form |
| **Open** | OpenRegular | ✅ Yes | ❌ No | Opens selected record |
| **Delete** | DeleteRegular | ✅ Yes | ✅ Yes | Deletes selected records |
| **Refresh** | ArrowSyncRegular | ❌ No | N/A | Refreshes dataset |
| **Upload** | ArrowUploadRegular | ❌ No | N/A | Custom file upload |

---

## Deliverables

**Files Created:**
1. `src/types/CommandTypes.ts` - Command type definitions
2. `src/services/CommandRegistry.ts` - Built-in command implementations
3. `src/services/CommandExecutor.ts` - Command execution logic
4. `src/components/Toolbar/CommandToolbar.tsx` - Toolbar UI component
5. `src/services/index.ts` - Services barrel export
6. `src/components/Toolbar/index.ts` - Toolbar barrel export

**Files Updated:**
7. `src/types/index.ts` - Export CommandTypes
8. `src/index.ts` - Export services
9. `src/components/index.ts` - Export Toolbar
10. `src/components/DatasetGrid/UniversalDatasetGrid.tsx` - Integrate toolbar

**Total:** 10 files created/updated

---

## Common Issues & Solutions

**Issue:** "Cannot find navigation API"
**Solution:** Access via `(context as any).navigation` - PCF context typing issue

**Issue:** Commands disabled even with selection
**Solution:** Verify `selectedRecordIds` prop is being passed and updated

**Issue:** Delete command doesn't refresh
**Solution:** Ensure `refresh` function is passed in command context

**Issue:** Icons not showing
**Solution:** Import from `@fluentui/react-icons`, use `React.createElement()`

---

## Command Context Flow

```
User clicks toolbar button
  ↓
CommandToolbar.handleCommandClick()
  ↓
CommandExecutor.execute()
  ↓
  ├─ Validate selection requirements
  ├─ Show confirmation if needed
  ├─ Execute command.handler(context)
  └─ Handle errors
  ↓
Command accesses:
  - selectedRecords (filtered from dataset)
  - entityName (from first record)
  - webAPI (for CRUD operations)
  - navigation (for opening forms)
  - refresh (to reload data)
  - emitLastAction (for output property)
```

---

## Next Steps

After completing this task:
1. Proceed to [TASK-3.2-COLUMN-RENDERERS.md](./TASK-3.2-COLUMN-RENDERERS.md)
2. Will implement type-based column renderers (date, choice, lookup)

---

**Task Status:** Ready for Execution
**Estimated Time:** 5 hours
**Actual Time:** _________ (fill in after completion)
**Completed By:** _________ (developer name)
**Date:** _________ (completion date)
