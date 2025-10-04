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
import { ICommand, ICommandContext, IEntityPrivileges } from "../types/CommandTypes";
import { EntityConfigurationService } from "./EntityConfigurationService";
import { CustomCommandFactory } from "./CustomCommandFactory";

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
      group: "primary",
      description: "Create a new record",
      keyboardShortcut: "Ctrl+N",
      handler: async (context: ICommandContext) => {
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
      group: "primary",
      description: "Open selected record",
      keyboardShortcut: "Ctrl+O",
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
      group: "secondary",
      description: "Delete selected records",
      keyboardShortcut: "Delete",
      dividerAfter: true,
      refresh: true,
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
      group: "secondary",
      description: "Refresh the grid",
      keyboardShortcut: "F5",
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
      group: "overflow",
      description: "Upload a file",
      keyboardShortcut: "Ctrl+U",
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
   * Get multiple commands by keys, filtered by user privileges
   */
  static getCommands(keys: string[], privileges?: IEntityPrivileges): ICommand[] {
    const commands = keys
      .map(key => this.getCommand(key))
      .filter((cmd): cmd is ICommand => cmd !== undefined);

    // If no privileges provided, return all commands
    if (!privileges) {
      return commands;
    }

    // Filter commands based on required privileges
    return commands.filter(cmd => this.hasRequiredPrivilege(cmd, privileges));
  }

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

  /**
   * Check if user has required privilege for a command
   */
  private static hasRequiredPrivilege(command: ICommand, privileges: IEntityPrivileges): boolean {
    switch (command.key.toLowerCase()) {
      case "create":
        return privileges.canCreate;

      case "delete":
        return privileges.canDelete;

      case "open":
        // Open requires read privilege
        return privileges.canRead;

      case "refresh":
        // Refresh only requires read privilege
        return privileges.canRead;

      case "upload":
        // Upload requires create/append privilege
        return privileges.canCreate || privileges.canAppend;

      default:
        // For custom commands, default to allowing if user can write
        return privileges.canWrite;
    }
  }
}
