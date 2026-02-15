/**
 * Visual Host PCF Control
 * Version 1.0.0 - Configuration-driven visualization framework
 * Project: visualization-module
 *
 * Renders charts, cards, and calendars based on sprk_chartdefinition configuration
 */

import * as React from "react";
import * as ReactDOM from "react-dom";
import { FluentProvider } from "@fluentui/react-components";
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { VisualHostRoot } from "./components/VisualHostRoot";
import { ErrorBoundary } from "./components/ErrorBoundary";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";
import { logger } from "./utils/logger";

export class VisualHost
  implements ComponentFramework.StandardControl<IInputs, IOutputs>
{
  private container: HTMLDivElement | null = null;
  private notifyOutputChanged: () => void;
  private _cleanupThemeListener: (() => void) | null = null;
  private _context: ComponentFramework.Context<IInputs> | null = null;

  constructor() {
    logger.info("VisualHost", "Constructor called");
  }

  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    try {
      logger.info("VisualHost", "Init - Setting up container");

      this.notifyOutputChanged = notifyOutputChanged;
      this._context = context;
      this.container = container;

      // Fill the platform wrapper (div.pa-cb.flexbox) — without this,
      // the container shrinks to content width inside the flex parent
      container.style.width = "100%";
      container.style.height = "100%";

      // Set up theme listener for dynamic theme changes
      this._cleanupThemeListener = setupThemeListener((isDark) => {
        logger.info("VisualHost", `Theme changed: isDark=${isDark}`);
        if (this._context && this.container) {
          this.renderReactTree(this._context);
        }
      }, context);

      // Render React tree
      this.renderReactTree(context);

      logger.info("VisualHost", "Init complete");
    } catch (error) {
      logger.error("VisualHost", "Init failed", error);
      throw error;
    }
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    try {
      // Store latest context for theme listener callback
      this._context = context;

      // Extract chart definition ID from lookup EntityReference
      const lookupValue = context.parameters.chartDefinition?.raw;
      const chartDefinitionId = lookupValue?.[0]?.id;

      logger.debug("VisualHost", "UpdateView - Re-rendering", {
        chartDefinitionId,
        lookupValue,
      });

      // Re-render with new context
      this.renderReactTree(context);
    } catch (error) {
      logger.error("VisualHost", "UpdateView failed", error);
    }
  }

  public destroy(): void {
    try {
      logger.info("VisualHost", "Destroy - Unmounting React");

      // Clean up theme listener
      if (this._cleanupThemeListener) {
        this._cleanupThemeListener();
        this._cleanupThemeListener = null;
      }

      if (this.container) {
        ReactDOM.unmountComponentAtNode(this.container);
        this.container = null;
      }

      this._context = null;
    } catch (error) {
      logger.error("VisualHost", "Destroy failed", error);
    }
  }

  public getOutputs(): IOutputs {
    return {};
  }

  /**
   * Render the React component tree.
   * Called from init() and updateView().
   * Uses React 16 API (ReactDOM.render) for platform library compatibility.
   */
  private renderReactTree(context: ComponentFramework.Context<IInputs>): void {
    if (!this.container) {
      logger.error("VisualHost", "Cannot render - container not initialized");
      return;
    }

    try {
      const theme = resolveTheme(context);

      ReactDOM.render(
        React.createElement(
          FluentProvider,
          { theme, style: { width: "100%", height: "100%" } },
          React.createElement(
            ErrorBoundary,
            null,
            React.createElement(VisualHostRoot, {
              context,
              notifyOutputChanged: this.notifyOutputChanged,
            })
          )
        ),
        this.container
      );
    } catch (error) {
      logger.error("VisualHost", "Render failed", error);
      throw error;
    }
  }
}
