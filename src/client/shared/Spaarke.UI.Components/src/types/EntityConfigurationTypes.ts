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
