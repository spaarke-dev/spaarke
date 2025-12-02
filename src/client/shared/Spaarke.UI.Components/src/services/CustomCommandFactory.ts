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
    _key: string,
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

    await (context.webAPI as any).execute(request);
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

        await (context.webAPI as any).execute(request);
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

      await (context.webAPI as any).execute(request);
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
    _workflowId: string,
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

      await (context.webAPI as any).execute(request);
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
    let result = value;
    result = result.replace(/\{selectedCount\}/g, String(context.selectedRecords.length));
    result = result.replace(/\{entityName\}/g, context.entityName);
    result = result.replace(/\{parentRecordId\}/g, context.parentRecord?.id?.guid ?? "");
    result = result.replace(/\{parentTable\}/g, (context.parentRecord as any)?.entityType ?? "");
    // Add more token replacements as needed
    return result;
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
