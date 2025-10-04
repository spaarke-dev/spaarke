# TASK-3.5: Entity Configuration System

**Sprint:** Sprint 5 - Universal Dataset PCF Component
**Phase:** 3 - Advanced Features
**Estimated Time:** 4 hours
**Prerequisites:** TASK-3.1 (Command System), TASK-3.4 (Toolbar UI)
**Next Task:** TASK-4.1 (Unit Tests)

---

## Objective

Implement an entity-specific configuration system that allows different Dataverse entities (e.g., Document, Contact, Account) to have customized command sets, view modes, and behaviors. This enables the Universal Dataset component to be truly universal by adapting its behavior based on the entity it's displaying, all through JSON configuration without code changes.

**Why This Matters:**
- **Flexibility:** Different entities need different commands (e.g., Document needs "Upload", Contact needs "Send Email")
- **Maintainability:** Configuration changes don't require code recompilation
- **Reusability:** One component works for all entities across Spaarke platform
- **Customization:** Per-entity view modes, toolbar settings, command lists
- **ADR Alignment:** ADR-012 shared component library supports configuration-driven design

---

## Critical Standards

**Must Read:**
- [DATASET-COMPONENT-COMMANDS.md](./DATASET-COMPONENT-COMMANDS.md) - Custom command configuration patterns
- [ADR-012-SHARED-COMPONENT-LIBRARY.md](../../../docs/ADR-012-SHARED-COMPONENT-LIBRARY.md) - Shared library architecture
- [KM-UX-FLUENT-DESIGN-V9-STANDARDS.md](../../../docs/KM-UX-FLUENT-DESIGN-V9-STANDARDS.md) - UI standards

**Key Rules:**
1. ✅ All configuration via JSON (no entity-specific code)
2. ✅ Schema validation for configuration
3. ✅ Default fallback configuration
4. ✅ Merge strategy: entity config overrides defaults
5. ✅ Support custom commands (Custom API, Actions, Functions)
6. ✅ Fluent UI v9 components only
7. ✅ All work in `src/shared/Spaarke.UI.Components/`

---

## Configuration Schema

### Entity Configuration JSON Structure

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enabledCommands": ["open", "create", "delete", "refresh"],
    "compactToolbar": false,
    "enableVirtualization": true,
    "rowHeight": 44,
    "scrollBehavior": "Auto"
  },
  "entityConfigs": {
    "sprk_document": {
      "viewMode": "Grid",
      "enabledCommands": ["open", "create", "delete", "refresh", "upload", "download"],
      "compactToolbar": false,
      "customCommands": {
        "upload": {
          "label": "Upload to SPE",
          "icon": "ArrowUpload",
          "actionType": "customapi",
          "actionName": "sprk_UploadDocument",
          "requiresSelection": false,
          "group": "primary",
          "description": "Upload document to SharePoint",
          "keyboardShortcut": "Ctrl+U",
          "parameters": {
            "ParentId": "{parentRecordId}",
            "ParentTable": "{parentTable}",
            "ContainerId": "{context.sprk_container_id}"
          },
          "refresh": true,
          "successMessage": "Document uploaded successfully"
        },
        "download": {
          "label": "Download",
          "icon": "ArrowDownload",
          "actionType": "customapi",
          "actionName": "sprk_DownloadDocument",
          "requiresSelection": true,
          "group": "secondary",
          "description": "Download selected documents",
          "refresh": false
        }
      }
    },
    "contact": {
      "viewMode": "Card",
      "enabledCommands": ["open", "create", "delete", "refresh", "sendEmail"],
      "compactToolbar": false,
      "customCommands": {
        "sendEmail": {
          "label": "Send Email",
          "icon": "Mail",
          "actionType": "action",
          "actionName": "SendEmail",
          "requiresSelection": true,
          "group": "primary",
          "description": "Send email to selected contacts",
          "keyboardShortcut": "Ctrl+E"
        }
      }
    },
    "account": {
      "viewMode": "List",
      "enabledCommands": ["open", "create", "delete", "refresh"],
      "compactToolbar": true
    }
  }
}
```

---

## Implementation Steps

### Step 1: Create Configuration Types

**File:** `src/shared/Spaarke.UI.Components/src/types/EntityConfigurationTypes.ts`

```typescript
/**
 * Entity configuration types for Universal Dataset component
 */

import { ViewMode, ScrollBehavior } from "./DatasetTypes";

/**
 * Custom command action types
 */
export type CustomCommandActionType = "customapi" | "action" | "function" | "workflow";

/**
 * Custom command parameter value (supports token interpolation)
 */
export type CommandParameterValue = string | number | boolean | Record<string, any>;

/**
 * Custom command configuration from JSON
 */
export interface ICustomCommandConfiguration {
  label: string;
  icon?: string;
  actionType: CustomCommandActionType;
  actionName: string;
  requiresSelection?: boolean;
  group?: "primary" | "secondary" | "overflow";
  description?: string;
  keyboardShortcut?: string;
  parameters?: Record<string, CommandParameterValue>;
  refresh?: boolean;
  successMessage?: string;
  confirmationMessage?: string;
  minSelection?: number;
  maxSelection?: number;
}

/**
 * Entity-specific configuration
 */
export interface IEntityConfiguration {
  viewMode?: ViewMode;
  enabledCommands?: string[];
  compactToolbar?: boolean;
  enableVirtualization?: boolean;
  rowHeight?: number;
  scrollBehavior?: ScrollBehavior;
  toolbarShowOverflow?: boolean;
  customCommands?: Record<string, ICustomCommandConfiguration>;
}

/**
 * Complete configuration schema
 */
export interface IConfigurationSchema {
  schemaVersion: string;
  defaultConfig: IEntityConfiguration;
  entityConfigs: Record<string, IEntityConfiguration>;
}

/**
 * Resolved configuration (after merging entity config with defaults)
 */
export interface IResolvedConfiguration extends Required<Omit<IEntityConfiguration, "customCommands">> {
  customCommands: Record<string, ICustomCommandConfiguration>;
}
```

---

### Step 2: Create Configuration Service

**File:** `src/shared/Spaarke.UI.Components/src/services/EntityConfigurationService.ts`

```typescript
/**
 * EntityConfigurationService - Loads and merges entity configurations
 */

import {
  IConfigurationSchema,
  IEntityConfiguration,
  IResolvedConfiguration,
  ICustomCommandConfiguration
} from "../types/EntityConfigurationTypes";
import { ViewMode, ScrollBehavior } from "../types/DatasetTypes";

export class EntityConfigurationService {
  private static schema: IConfigurationSchema | null = null;

  /**
   * Load configuration from JSON string (from manifest parameter)
   */
  static loadConfiguration(configJson: string | null | undefined): void {
    if (!configJson) {
      this.schema = this.getDefaultSchema();
      return;
    }

    try {
      const parsed = JSON.parse(configJson);

      // Validate schema version
      if (parsed.schemaVersion !== "1.0") {
        console.warn(`Unsupported schema version: ${parsed.schemaVersion}. Using defaults.`);
        this.schema = this.getDefaultSchema();
        return;
      }

      this.schema = parsed as IConfigurationSchema;
    } catch (error) {
      console.error("Failed to parse entity configuration JSON", error);
      this.schema = this.getDefaultSchema();
    }
  }

  /**
   * Get configuration for a specific entity
   */
  static getEntityConfiguration(entityLogicalName: string): IResolvedConfiguration {
    const schema = this.schema ?? this.getDefaultSchema();
    const defaultConfig = schema.defaultConfig;
    const entityConfig = schema.entityConfigs[entityLogicalName.toLowerCase()] ?? {};

    // Merge entity config with defaults
    return {
      viewMode: entityConfig.viewMode ?? defaultConfig.viewMode ?? "Grid",
      enabledCommands: entityConfig.enabledCommands ?? defaultConfig.enabledCommands ?? ["open", "create", "delete", "refresh"],
      compactToolbar: entityConfig.compactToolbar ?? defaultConfig.compactToolbar ?? false,
      enableVirtualization: entityConfig.enableVirtualization ?? defaultConfig.enableVirtualization ?? true,
      rowHeight: entityConfig.rowHeight ?? defaultConfig.rowHeight ?? 44,
      scrollBehavior: entityConfig.scrollBehavior ?? defaultConfig.scrollBehavior ?? "Auto",
      toolbarShowOverflow: entityConfig.toolbarShowOverflow ?? defaultConfig.toolbarShowOverflow ?? true,
      customCommands: {
        ...(defaultConfig.customCommands ?? {}),
        ...(entityConfig.customCommands ?? {})
      }
    };
  }

  /**
   * Get custom command configuration by key
   */
  static getCustomCommand(entityLogicalName: string, commandKey: string): ICustomCommandConfiguration | undefined {
    const config = this.getEntityConfiguration(entityLogicalName);
    return config.customCommands[commandKey];
  }

  /**
   * Check if configuration is loaded
   */
  static isConfigurationLoaded(): boolean {
    return this.schema !== null;
  }

  /**
   * Get default schema (fallback when no configuration provided)
   */
  private static getDefaultSchema(): IConfigurationSchema {
    return {
      schemaVersion: "1.0",
      defaultConfig: {
        viewMode: "Grid",
        enabledCommands: ["open", "create", "delete", "refresh"],
        compactToolbar: false,
        enableVirtualization: true,
        rowHeight: 44,
        scrollBehavior: "Auto",
        toolbarShowOverflow: true,
        customCommands: {}
      },
      entityConfigs: {}
    };
  }

  /**
   * Validate configuration schema (optional, for development)
   */
  static validateConfiguration(configJson: string): { valid: boolean; errors: string[] } {
    const errors: string[] = [];

    try {
      const parsed = JSON.parse(configJson);

      if (!parsed.schemaVersion) {
        errors.push("Missing schemaVersion");
      }

      if (!parsed.defaultConfig) {
        errors.push("Missing defaultConfig");
      }

      if (!parsed.entityConfigs) {
        errors.push("Missing entityConfigs");
      }

      // Validate custom commands
      Object.entries(parsed.entityConfigs || {}).forEach(([entityName, config]: [string, any]) => {
        if (config.customCommands) {
          Object.entries(config.customCommands).forEach(([key, cmd]: [string, any]) => {
            if (!cmd.label) errors.push(`${entityName}.${key}: Missing label`);
            if (!cmd.actionType) errors.push(`${entityName}.${key}: Missing actionType`);
            if (!cmd.actionName) errors.push(`${entityName}.${key}: Missing actionName`);
          });
        }
      });

      return {
        valid: errors.length === 0,
        errors
      };
    } catch (error) {
      return {
        valid: false,
        errors: [`Invalid JSON: ${(error as Error).message}`]
      };
    }
  }
}
```

---

### Step 3: Create Custom Command Factory

**File:** `src/shared/Spaarke.UI.Components/src/services/CustomCommandFactory.ts`

```typescript
/**
 * CustomCommandFactory - Creates ICommand instances from JSON configuration
 */

import * as React from "react";
import {
  ArrowUploadRegular,
  ArrowDownloadRegular,
  MailRegular,
  SendRegular
} from "@fluentui/react-icons";
import { ICommand, ICommandContext } from "../types/CommandTypes";
import { ICustomCommandConfiguration } from "../types/EntityConfigurationTypes";

export class CustomCommandFactory {
  /**
   * Create ICommand from custom command configuration
   */
  static createCommand(key: string, config: ICustomCommandConfiguration): ICommand {
    return {
      key,
      label: config.label,
      icon: this.getIcon(config.icon),
      requiresSelection: config.requiresSelection ?? false,
      group: config.group ?? "overflow",
      description: config.description,
      keyboardShortcut: config.keyboardShortcut,
      confirmationMessage: config.confirmationMessage,
      refresh: config.refresh ?? false,
      successMessage: config.successMessage,
      handler: async (context: ICommandContext) => {
        await this.executeCustomCommand(key, config, context);
      }
    };
  }

  /**
   * Execute custom command based on action type
   */
  private static async executeCustomCommand(
    key: string,
    config: ICustomCommandConfiguration,
    context: ICommandContext
  ): Promise<void> {
    // Validate selection requirements
    if (config.requiresSelection && context.selectedRecords.length === 0) {
      throw new Error("No records selected");
    }

    if (config.minSelection && context.selectedRecords.length < config.minSelection) {
      throw new Error(`Select at least ${config.minSelection} record(s)`);
    }

    if (config.maxSelection && context.selectedRecords.length > config.maxSelection) {
      throw new Error(`Select no more than ${config.maxSelection} record(s)`);
    }

    // Interpolate parameters
    const parameters = this.interpolateParameters(config.parameters ?? {}, context);

    // Execute based on action type
    switch (config.actionType) {
      case "customapi":
        await this.executeCustomApi(config.actionName, parameters, context);
        break;

      case "action":
        await this.executeAction(config.actionName, parameters, context);
        break;

      case "function":
        await this.executeFunction(config.actionName, parameters, context);
        break;

      case "workflow":
        await this.executeWorkflow(config.actionName, parameters, context);
        break;

      default:
        throw new Error(`Unsupported action type: ${config.actionType}`);
    }
  }

  /**
   * Execute Custom API
   */
  private static async executeCustomApi(
    apiName: string,
    parameters: Record<string, any>,
    context: ICommandContext
  ): Promise<void> {
    const request = {
      ...parameters,
      getMetadata: () => ({
        boundParameter: null,
        parameterTypes: {},
        operationType: 0,
        operationName: apiName
      })
    };

    await context.webAPI.execute(request);
  }

  /**
   * Execute Action (bound or unbound)
   */
  private static async executeAction(
    actionName: string,
    parameters: Record<string, any>,
    context: ICommandContext
  ): Promise<void> {
    // If records selected, execute as bound action on each record
    if (context.selectedRecords.length > 0) {
      for (const record of context.selectedRecords) {
        const request = {
          entity: {
            entityType: context.entityName,
            id: record.id
          },
          ...parameters,
          getMetadata: () => ({
            boundParameter: "entity",
            parameterTypes: {
              entity: {
                typeName: context.entityName,
                structuralProperty: 5
              }
            },
            operationType: 0,
            operationName: actionName
          })
        };

        await context.webAPI.execute(request);
      }
    } else {
      // Execute as unbound action
      const request = {
        ...parameters,
        getMetadata: () => ({
          boundParameter: null,
          parameterTypes: {},
          operationType: 0,
          operationName: actionName
        })
      };

      await context.webAPI.execute(request);
    }
  }

  /**
   * Execute Function
   */
  private static async executeFunction(
    functionName: string,
    parameters: Record<string, any>,
    context: ICommandContext
  ): Promise<void> {
    // Build OData function URL
    const params = Object.entries(parameters)
      .map(([key, value]) => `${key}=${encodeURIComponent(JSON.stringify(value))}`)
      .join(",");

    const url = params ? `${functionName}(${params})` : functionName;

    await context.webAPI.retrieveMultipleRecords(context.entityName, `?${url}`);
  }

  /**
   * Execute Workflow (Power Automate Flow)
   */
  private static async executeWorkflow(
    workflowId: string,
    parameters: Record<string, any>,
    context: ICommandContext
  ): Promise<void> {
    // Execute workflow via ExecuteWorkflow action
    for (const record of context.selectedRecords) {
      const request = {
        EntityId: record.id,
        ...parameters,
        getMetadata: () => ({
          boundParameter: null,
          parameterTypes: {
            EntityId: { typeName: "Edm.Guid", structuralProperty: 1 }
          },
          operationType: 0,
          operationName: "ExecuteWorkflow"
        })
      };

      await context.webAPI.execute(request);
    }
  }

  /**
   * Interpolate parameter values with context tokens
   */
  private static interpolateParameters(
    parameters: Record<string, any>,
    context: ICommandContext
  ): Record<string, any> {
    const result: Record<string, any> = {};

    Object.entries(parameters).forEach(([key, value]) => {
      if (typeof value === "string") {
        result[key] = this.interpolateString(value, context);
      } else {
        result[key] = value;
      }
    });

    return result;
  }

  /**
   * Interpolate string tokens
   */
  private static interpolateString(value: string, context: ICommandContext): string {
    return value
      .replace("{selectedCount}", String(context.selectedRecords.length))
      .replace("{entityName}", context.entityName)
      .replace("{parentRecordId}", context.parentRecord?.id ?? "")
      .replace("{parentTable}", context.parentRecord?.entityType ?? "");
    // Add more token replacements as needed
  }

  /**
   * Get icon from icon name
   */
  private static getIcon(iconName?: string): React.ReactElement | undefined {
    if (!iconName) return undefined;

    const iconMap: Record<string, React.ComponentType> = {
      ArrowUpload: ArrowUploadRegular,
      ArrowDownload: ArrowDownloadRegular,
      Mail: MailRegular,
      Send: SendRegular
      // Add more icon mappings as needed
    };

    const IconComponent = iconMap[iconName];
    return IconComponent ? React.createElement(IconComponent) : undefined;
  }
}
```

---

### Step 4: Update CommandRegistry to Support Custom Commands

**File:** `src/shared/Spaarke.UI.Components/src/services/CommandRegistry.ts`

**Add method:**
```typescript
/**
 * Get commands including custom commands from entity configuration
 */
static getCommandsWithCustom(
  keys: string[],
  entityLogicalName: string,
  privileges?: IEntityPrivileges
): ICommand[] {
  const commands: ICommand[] = [];

  keys.forEach(key => {
    // Try built-in command first
    let command = this.getCommand(key);

    // If not built-in, check custom commands
    if (!command) {
      const customConfig = EntityConfigurationService.getCustomCommand(entityLogicalName, key);
      if (customConfig) {
        command = CustomCommandFactory.createCommand(key, customConfig);
      }
    }

    if (command) {
      commands.push(command);
    }
  });

  // Filter by privileges if provided
  if (!privileges) return commands;

  return commands.filter(cmd => this.hasRequiredPrivilege(cmd, privileges));
}
```

**Add import:**
```typescript
import { EntityConfigurationService } from "./EntityConfigurationService";
import { CustomCommandFactory } from "./CustomCommandFactory";
```

---

### Step 5: Update UniversalDatasetGrid to Use Entity Configuration

**File:** `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/UniversalDatasetGrid.tsx`

**Add import:**
```typescript
import { EntityConfigurationService } from "../../services/EntityConfigurationService";
```

**Update props interface:**
```typescript
export interface IUniversalDatasetGridProps {
  // Configuration
  config?: IDatasetConfig; // Make optional
  configJson?: string; // NEW: JSON configuration string

  // ... rest of props
}
```

**Update component:**
```typescript
export const UniversalDatasetGrid: React.FC<IUniversalDatasetGridProps> = (props) => {
  const styles = useStyles();

  // Load entity configuration if provided
  React.useEffect(() => {
    if (props.configJson) {
      EntityConfigurationService.loadConfiguration(props.configJson);
    }
  }, [props.configJson]);

  // Determine data mode
  const isHeadlessMode = !!props.headlessConfig;

  // ... existing hooks

  // Get entity name
  const entityName = records[0]?.entityName || props.headlessConfig?.entityName || "";

  // Get entity-specific configuration
  const entityConfig = React.useMemo(() => {
    if (!entityName) return null;
    return EntityConfigurationService.getEntityConfiguration(entityName);
  }, [entityName]);

  // Merge props config with entity config
  const finalConfig: IDatasetConfig = React.useMemo(() => {
    const baseConfig = props.config ?? {
      viewMode: "Grid",
      enableVirtualization: true,
      rowHeight: 44,
      selectionMode: "Multiple",
      showToolbar: true,
      enabledCommands: ["open", "create", "delete", "refresh"],
      theme: "Auto",
      scrollBehavior: "Auto"
    };

    if (!entityConfig) return baseConfig;

    return {
      ...baseConfig,
      viewMode: entityConfig.viewMode,
      enabledCommands: entityConfig.enabledCommands,
      compactToolbar: entityConfig.compactToolbar,
      enableVirtualization: entityConfig.enableVirtualization,
      rowHeight: entityConfig.rowHeight,
      scrollBehavior: entityConfig.scrollBehavior,
      toolbarShowOverflow: entityConfig.toolbarShowOverflow
    };
  }, [props.config, entityConfig]);

  // Get commands based on config and privileges (including custom commands)
  const commands = React.useMemo(() => {
    if (!entityName) {
      return CommandRegistry.getCommands(finalConfig.enabledCommands, privileges);
    }
    return CommandRegistry.getCommandsWithCustom(
      finalConfig.enabledCommands,
      entityName,
      privileges
    );
  }, [finalConfig.enabledCommands, entityName, privileges]);

  // ... rest of component
};
```

---

### Step 6: Export New Types and Services

**File:** `src/shared/Spaarke.UI.Components/src/types/index.ts`

**Add exports:**
```typescript
export * from "./EntityConfigurationTypes";
```

**File:** `src/shared/Spaarke.UI.Components/src/services/index.ts`

Create if doesn't exist:
```typescript
export { CommandRegistry } from "./CommandRegistry";
export { CommandExecutor } from "./CommandExecutor";
export { PrivilegeService } from "./PrivilegeService";
export { FieldSecurityService } from "./FieldSecurityService";
export { ColumnRendererService } from "./ColumnRendererService";
export { EntityConfigurationService } from "./EntityConfigurationService";
export { CustomCommandFactory } from "./CustomCommandFactory";
```

---

### Step 7: Create Example Configuration JSON

**File:** `src/shared/Spaarke.UI.Components/examples/entity-config-example.json`

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
    "sprk_document": {
      "viewMode": "Grid",
      "enabledCommands": ["open", "create", "delete", "refresh", "upload", "download"],
      "customCommands": {
        "upload": {
          "label": "Upload to SPE",
          "icon": "ArrowUpload",
          "actionType": "customapi",
          "actionName": "sprk_UploadDocument",
          "requiresSelection": false,
          "group": "primary",
          "description": "Upload document to SharePoint",
          "keyboardShortcut": "Ctrl+U",
          "parameters": {
            "ParentId": "{parentRecordId}",
            "ParentTable": "{parentTable}"
          },
          "refresh": true,
          "successMessage": "Document uploaded successfully"
        },
        "download": {
          "label": "Download",
          "icon": "ArrowDownload",
          "actionType": "customapi",
          "actionName": "sprk_DownloadDocument",
          "requiresSelection": true,
          "group": "secondary",
          "description": "Download selected documents"
        }
      }
    },
    "contact": {
      "viewMode": "Card",
      "enabledCommands": ["open", "create", "delete", "refresh", "sendEmail"],
      "customCommands": {
        "sendEmail": {
          "label": "Send Email",
          "icon": "Mail",
          "actionType": "action",
          "actionName": "SendEmail",
          "requiresSelection": true,
          "group": "primary",
          "description": "Send email to selected contacts",
          "keyboardShortcut": "Ctrl+E"
        }
      }
    }
  }
}
```

---

### Step 8: Build and Verify

```bash
cd src/shared/Spaarke.UI.Components
npm run build
```

**Validation:**
- ✅ Build succeeds with 0 TypeScript errors
- ✅ EntityConfigurationService exports
- ✅ CustomCommandFactory exports
- ✅ All types export correctly

---

## Validation Checklist

Run these commands to verify completion:

```bash
# 1. Verify files exist
cd src/shared/Spaarke.UI.Components
ls src/types/EntityConfigurationTypes.ts
ls src/services/EntityConfigurationService.ts
ls src/services/CustomCommandFactory.ts
ls examples/entity-config-example.json

# 2. Build succeeds
npm run build

# 3. Verify exports
grep -r "EntityConfigurationService" src/types/index.ts
grep -r "CustomCommandFactory" src/services/index.ts
```

---

## Success Criteria

- ✅ Entity configuration types defined
- ✅ EntityConfigurationService loads and merges configs
- ✅ CustomCommandFactory creates commands from JSON
- ✅ CommandRegistry supports custom commands
- ✅ UniversalDatasetGrid uses entity configuration
- ✅ Example configuration JSON provided
- ✅ Configuration validation implemented
- ✅ Build succeeds with 0 errors
- ✅ All exports valid
- ✅ Token interpolation working ({parentRecordId}, {selectedCount}, etc.)

---

## Configuration Features

| Feature | Implementation | Example |
|---------|---------------|---------|
| **Default Config** | Fallback for all entities | viewMode: "Grid" |
| **Entity Override** | Per-entity customization | contact: viewMode: "Card" |
| **Custom Commands** | JSON-defined commands | upload, download, sendEmail |
| **Action Types** | Custom API, Action, Function, Workflow | customapi, action |
| **Token Interpolation** | Dynamic parameter values | {parentRecordId}, {selectedCount} |
| **Command Groups** | Toolbar organization | primary, secondary, overflow |
| **Keyboard Shortcuts** | Custom shortcuts | Ctrl+U, Ctrl+E |

---

## Token Interpolation Support

| Token | Description | Example Value |
|-------|-------------|---------------|
| `{selectedCount}` | Number of selected records | "3" |
| `{entityName}` | Logical name of entity | "contact" |
| `{parentRecordId}` | Parent record GUID | "a1b2c3d4-..." |
| `{parentTable}` | Parent entity logical name | "account" |

---

## Common Issues

### Issue: Configuration not loading
**Solution:** Check JSON syntax with `EntityConfigurationService.validateConfiguration()`

### Issue: Custom command not appearing
**Solution:** Ensure command key is in `enabledCommands` array

### Issue: Custom API execution fails
**Solution:** Verify API is registered in Dataverse and user has permissions

### Issue: Token not interpolating
**Solution:** Check token name matches exactly (case-sensitive)

---

## Deliverables

- ✅ `src/types/EntityConfigurationTypes.ts` - Configuration schema types
- ✅ `src/services/EntityConfigurationService.ts` - Config loading/merging
- ✅ `src/services/CustomCommandFactory.ts` - Command creation from JSON
- ✅ Updated `src/services/CommandRegistry.ts` - Custom command support
- ✅ Updated `src/components/DatasetGrid/UniversalDatasetGrid.tsx` - Config integration
- ✅ `examples/entity-config-example.json` - Example configuration
- ✅ Updated exports in `src/types/index.ts` and `src/services/index.ts`
- ✅ Build output with 0 errors

---

## Next Steps

1. ✅ Mark TASK-3.5 complete
2. ➡️ Proceed to TASK-4.1 (Unit Tests)
3. Test entity configurations with multiple entities
4. Validate custom commands execute correctly
5. Verify token interpolation

---

**Estimated Time:** 4 hours
**Actual Time:** _(Fill in upon completion)_
**Completion Date:** _(Fill in upon completion)_
**Status:** 📝 Ready for execution
