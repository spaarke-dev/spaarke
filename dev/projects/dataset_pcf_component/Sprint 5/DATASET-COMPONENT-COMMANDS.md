# Dataset Component Command System

## Command Architecture
### Command Registry Pattern
```typescript
// commands/CommandRegistry.ts
export interface ICommand {
  key: string;
  label: string;
  icon?: React.ComponentType;
  execute: (context: ICommandContext) => Promise<void>;
  canExecute?: (context: ICommandContext) => boolean;
  requiresSelection?: boolean;
  confirmationMessage?: string;
  refresh?: boolean;
  successMessage?: string;
}

export class CommandRegistry {
  private commands = new Map<string, ICommand>();

  constructor() {
    this.registerBuiltInCommands();
  }

  register(cmd: ICommand) {
    this.commands.set(cmd.key, cmd);
  }

  get(key: string) {
    return this.commands.get(key);
  }

  private registerBuiltInCommands(): void {
    // Open command
    this.register({
      key: "open",
      label: "Open",
      requiresSelection: true,
      execute: async (context) => {
        const record = context.selectedRecords[0];
        context.navigation.openForm({
          entityName: record.entityName,
          entityId: record.id
        });
      }
    });

    // Create command
    this.register({
      key: "create",
      label: "New",
      execute: async (context) => {
        context.navigation.openForm({
          entityName: context.entityName,
          useQuickCreateForm: true,
          createFromEntity: context.parentRecord
        });
      }
    });

    // Delete command
    this.register({
      key: "delete",
      label: "Delete",
      requiresSelection: true,
      confirmationMessage: "Delete {count} selected items?",
      refresh: true,
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
      execute: async (context) => context.refresh?.()
    });
  }
}
```

## Custom Command Configuration
### JSON Command Definitions
```json
{
  "upload": {
    "type": "customApi",
    "api": "sprk_UploadDocument",
    "label": "Upload to SPE",
    "icon": "Upload",
    "parameters": {
      "ParentId": "{parentRecordId}",
      "ParentTable": "{parentTable}",
      "ContainerId": "{context.sprk_container_id}"
    },
    "refresh": true,
    "successMessage": "Document uploaded successfully"
  },
  "approve": {
    "type": "action",
    "action": "sprk_ApproveRecords",
    "label": "Approve",
    "icon": "CheckMark",
    "requiresSelection": true,
    "minSelection": 1,
    "maxSelection": 10,
    "confirmation": "Approve {selectedCount} items?",
    "enableRule": "record.statuscode != 'approved'"
  },
  "export": {
    "type": "function",
    "function": "exportToExcel",
    "label": "Export to Excel",
    "icon": "ArrowDownload",
    "parameters": {
      "columns": "{visibleColumns}",
      "filters": "{currentFilters}"
    }
  }
}
```

## Command Execution Pipeline
```typescript
// commands/CommandExecutor.ts
export class CommandExecutor {
  constructor(private registry: CommandRegistry, private ui: UIService) {}

  private interpolate(text: string, ctx: ICommandContext) {
    return text
      .replace("{count}", String(ctx.selectedRecords.length))
      .replace("{selectedCount}", String(ctx.selectedRecords.length));
  }

  async execute(commandKey: string, context: ICommandContext): Promise<void> {
    const command = this.registry.get(commandKey);

    if (!command) throw new Error(`Command ${commandKey} not found`);
    if (command.canExecute && !command.canExecute(context)) return;

    if (command.requiresSelection && context.selectedRecords.length === 0) {
      this.ui.toast("Select one or more records first", "info");
      return;
    }

    if (command.confirmationMessage) {
      const confirmed = await this.ui.confirm(this.interpolate(command.confirmationMessage, context));
      if (!confirmed) return;
    }

    try {
      this.ui.busy(true);
      await command.execute(context);
      if (command.successMessage) this.ui.toast(command.successMessage, "success");
      if (command.refresh) context.refresh?.();
      context.emitLastAction?.(commandKey);
    } catch (e: any) {
      this.ui.toast(`Error: ${e.message}`, "error");
    } finally {
      this.ui.busy(false);
    }
  }
}
```

## Command UI Integration
### Toolbar Implementation
```typescript
// components/CommandToolbar.tsx
import { Toolbar, ToolbarButton } from "@fluentui/react-components";
export const CommandToolbar: React.FC<IToolbarProps> = ({
  enabledCommands,
  commandConfig,
  selectedRecords,
  context,
  registry,
  executor
}) => {
  const commands = React.useMemo(() => {
    return enabledCommands.split(",").map(key => {
      const cfg = commandConfig?.[key];
      return registry.get(key) ?? createCustomCommand(cfg);
    });
  }, [enabledCommands, commandConfig, registry]);

  const canExecute = React.useCallback((cmd) => {
    if (cmd.requiresSelection && selectedRecords.length === 0) return false;
    return cmd.canExecute ? cmd.canExecute({ ...context, selectedRecords }) : true;
  }, [selectedRecords, context]);

  return (
    <Toolbar aria-label="Dataset actions">
      {commands.map(cmd => (
        <ToolbarButton
          key={cmd.key}
          icon={cmd.icon ? React.createElement(cmd.icon) : undefined}
          disabled={!canExecute(cmd)}
          onClick={() => executor.execute(cmd.key, { ...context, selectedRecords })}
        >
          {cmd.label}
        </ToolbarButton>
      ))}
    </Toolbar>
  );
};
```

## AI Coding Prompt
Build a command system that is data- and config-driven:
- Implement `CommandRegistry` with built-ins: `open`, `create`, `delete`, `refresh`, `export`. Include `requiresSelection`, `canExecute`, and optional confirmation text.
- Implement `CommandExecutor` that validates state, prompts when needed, executes, shows success/error toasts, and optionally triggers dataset `refresh`.
- Accept external JSON command definitions (Custom API/action/function). Support token interpolation.
- Render a Fluent v9 `Toolbar` with accessible labels; reflect selection-based enablement.
- Emit `lastAction` output on completion.
Deliverables: registry, executor, toolbar, and an example `commandConfig.json`.
