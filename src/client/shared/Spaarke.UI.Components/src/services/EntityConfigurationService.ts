/**
 * EntityConfigurationService - Loads and merges entity configurations
 */

import {
  IConfigurationSchema,
  IResolvedConfiguration,
  ICustomCommandConfiguration
} from "../types/EntityConfigurationTypes";

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
