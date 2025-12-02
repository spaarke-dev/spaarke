/**
 * Universal Dataset PCF Control for Spaarke
 * Configurable dataset grid/card/list component supporting any Dataverse entity
 *
 * Standards:
 * - KM-PCF-CONTROL-STANDARDS.md
 * - KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 * - ADR-012: Shared Component Library
 */

import * as React from "react";
import * as ReactDOM from "react-dom";
import {
  UniversalDatasetGrid,
  IDatasetConfig
} from "@spaarke/ui-components";

// Placeholder for generated types - will be created after manifest configuration
interface IInputs {
  datasetGrid: ComponentFramework.PropertyTypes.DataSet;
  viewMode: ComponentFramework.PropertyTypes.EnumProperty<"Grid" | "Card" | "List">;
  enableVirtualization: ComponentFramework.PropertyTypes.TwoOptionsProperty;
  rowHeight: ComponentFramework.PropertyTypes.WholeNumberProperty;
  enabledCommands: ComponentFramework.PropertyTypes.StringProperty;
  commandConfig: ComponentFramework.PropertyTypes.StringProperty;
  showToolbar: ComponentFramework.PropertyTypes.TwoOptionsProperty;
  theme: ComponentFramework.PropertyTypes.EnumProperty<"Auto" | "Spaarke" | "Host">;
  headlessMode: ComponentFramework.PropertyTypes.TwoOptionsProperty;
  headlessEntityName: ComponentFramework.PropertyTypes.StringProperty;
  headlessFetchXml: ComponentFramework.PropertyTypes.StringProperty;
  headlessPageSize: ComponentFramework.PropertyTypes.WholeNumberProperty;
  selectionMode: ComponentFramework.PropertyTypes.EnumProperty<"None" | "Single" | "Multiple">;
  scrollBehavior: ComponentFramework.PropertyTypes.EnumProperty<"Auto" | "Infinite" | "Paged">;
}

interface IOutputs {
  selectedRecordIds?: string;
  lastAction?: string;
}

export class UniversalDataset implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private container: HTMLDivElement;
  private context: ComponentFramework.Context<IInputs>;
  private notifyOutputChanged: () => void;
  private selectedRecords: string[] = [];
  private lastAction: string = "";

  /**
   * PCF Lifecycle: Initialize
   * Called once when control is loaded
   */
  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this.context = context;
    this.notifyOutputChanged = notifyOutputChanged;
    this.container = container;

    console.log("UniversalDataset: Initialized with infinite scroll support");
  }

  /**
   * PCF Lifecycle: Update View
   * Called when data changes or control is resized
   */
  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.context = context;

    // Build configuration from manifest properties
    const config: IDatasetConfig = {
      viewMode: context.parameters.viewMode?.raw || "Grid",
      enableVirtualization: context.parameters.enableVirtualization?.raw ?? true,
      rowHeight: context.parameters.rowHeight?.raw || 48,
      selectionMode: context.parameters.selectionMode?.raw || "Multiple",
      showToolbar: context.parameters.showToolbar?.raw ?? true,
      enabledCommands: (context.parameters.enabledCommands?.raw || "open,create,delete,refresh").split(","),
      theme: context.parameters.theme?.raw || "Auto",
      scrollBehavior: context.parameters.scrollBehavior?.raw || "Auto"
    };

    // Determine mode
    const isHeadlessMode = context.parameters.headlessMode?.raw ?? false;

    // Render React component with appropriate props based on mode
    const element = React.createElement(UniversalDatasetGrid, {
      config,
      dataset: isHeadlessMode ? undefined : context.parameters.datasetGrid,
      headlessConfig: isHeadlessMode ? {
        webAPI: context.webAPI,
        entityName: context.parameters.headlessEntityName?.raw || "",
        fetchXml: context.parameters.headlessFetchXml?.raw,
        pageSize: context.parameters.headlessPageSize?.raw || 25
      } : undefined,
      selectedRecordIds: this.selectedRecords,
      onSelectionChange: (ids) => {
        this.selectedRecords = ids;
        this.notifyOutputChanged();
      },
      onRecordClick: (recordId) => {
        if (!isHeadlessMode) {
          context.parameters.datasetGrid.openDatasetItem(recordId);
        } else {
          // In headless mode, could navigate via context.navigation
          console.log("Record clicked:", recordId);
        }
      },
      context: context
    });

    ReactDOM.render(element, this.container);
  }

  /**
   * PCF Lifecycle: Get Outputs
   * Return values to Power Platform (e.g., selected records, last action)
   */
  public getOutputs(): IOutputs {
    return {
      selectedRecordIds: this.selectedRecords.join(","),
      lastAction: this.lastAction
    };
  }

  /**
   * PCF Lifecycle: Destroy
   * Called when control is removed from DOM
   */
  public destroy(): void {
    ReactDOM.unmountComponentAtNode(this.container);
  }
}
