/**
 * Drill-Through Workspace PCF Control
 *
 * Expanded workspace for visualization drill-through:
 * - Two-panel layout: Chart (1/3) | Dataset Grid (2/3)
 * - Click chart elements to filter dataset grid
 * - Supports all 7 visual types from Visual Host
 * - Opens as dialog from Visual Host expand button
 *
 * ADR Compliance:
 * - ADR-006: PCF over webresources
 * - ADR-011: Dataset PCF over Subgrids
 * - ADR-021: Fluent UI v9 Design System
 *
 * @version 1.0.0
 */

import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom/client";
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
  teamsHighContrastTheme,
  Theme,
} from "@fluentui/react-components";
import { DrillThroughWorkspaceApp } from "./components/DrillThroughWorkspaceApp";
import { logger } from "./utils/logger";

// ─────────────────────────────────────────────────────────────────────────────
// Theme Utilities
// ─────────────────────────────────────────────────────────────────────────────

const STORAGE_KEY = "spaarke-theme";
type ThemePreference = "auto" | "light" | "dark";
type ThemeMode = "light" | "dark" | "high-contrast";

function getUserThemePreference(): ThemePreference {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === "light" || stored === "dark" || stored === "auto") {
      return stored;
    }
    try {
      const parentStored = window.parent?.localStorage?.getItem(STORAGE_KEY);
      if (
        parentStored === "light" ||
        parentStored === "dark" ||
        parentStored === "auto"
      ) {
        return parentStored;
      }
    } catch {
      /* Cross-origin blocked */
    }
  } catch {
    /* localStorage not available */
  }
  return "auto";
}

function isHighContrast(): boolean {
  if (window.matchMedia) {
    const forcedColors = window.matchMedia("(forced-colors: active)");
    if (forcedColors.matches) return true;
    const msHighContrast = window.matchMedia("(-ms-high-contrast: active)");
    if (msHighContrast.matches) return true;
  }
  if (
    document.body.classList.contains("high-contrast") ||
    document.body.classList.contains("ms-highContrast")
  ) {
    return true;
  }
  return false;
}

function detectDarkModeFromUrl(): boolean | null {
  try {
    if (
      window.location.href.includes("themeOption%3Ddarkmode") ||
      window.location.href.includes("themeOption=darkmode")
    ) {
      return true;
    }
    try {
      const parentUrl = window.parent?.location?.href;
      if (
        parentUrl?.includes("themeOption%3Ddarkmode") ||
        parentUrl?.includes("themeOption=darkmode")
      ) {
        return true;
      }
    } catch {
      /* Cross-origin blocked */
    }
  } catch {
    /* Error */
  }
  return null;
}

function getThemeMode(): ThemeMode {
  if (isHighContrast()) return "high-contrast";

  const preference = getUserThemePreference();
  if (preference === "dark") return "dark";
  if (preference === "light") return "light";

  // Auto mode - check URL flag first
  const urlDarkMode = detectDarkModeFromUrl();
  if (urlDarkMode !== null) {
    return urlDarkMode ? "dark" : "light";
  }

  // Fallback to system preference
  if (typeof window !== "undefined" && window.matchMedia) {
    return window.matchMedia("(prefers-color-scheme: dark)").matches
      ? "dark"
      : "light";
  }
  return "light";
}

function getResolvedTheme(): Theme {
  const mode = getThemeMode();
  if (mode === "high-contrast") return teamsHighContrastTheme;
  if (mode === "dark") return webDarkTheme;
  return webLightTheme;
}

// ─────────────────────────────────────────────────────────────────────────────
// PCF Control Class
// ─────────────────────────────────────────────────────────────────────────────

export class DrillThroughWorkspace
  implements ComponentFramework.StandardControl<IInputs, IOutputs>
{
  private _container: HTMLDivElement;
  private _context: ComponentFramework.Context<IInputs>;
  private _notifyOutputChanged: () => void;
  private _root: ReactDOM.Root | null = null;

  // Output values
  private _shouldClose: boolean = false;
  private _selectedRecordIds: string = "";

  // Theme state
  private _currentTheme: Theme = webLightTheme;
  private _themeMediaQuery: MediaQueryList | null = null;
  private _forcedColorsQuery: MediaQueryList | null = null;

  constructor() {
    logger.info("DrillThroughWorkspace", "Constructor called");
  }

  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    logger.info("DrillThroughWorkspace", "init() called");

    this._context = context;
    this._container = container;
    this._notifyOutputChanged = notifyOutputChanged;

    // Enable container resize tracking for responsive sizing
    context.mode.trackContainerResize(true);

    // Set container base styles
    this._container.style.display = "flex";
    this._container.style.flexDirection = "column";
    this._container.style.boxSizing = "border-box";
    this._container.style.overflow = "hidden";

    // Set up theme
    this._currentTheme = getResolvedTheme();
    this.setupThemeListeners();

    // Set up escape key handler for closing
    this.setupKeyboardHandler();

    // Initial render
    this.renderComponent();
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    logger.info("DrillThroughWorkspace", "updateView() called");
    this._context = context;
    this.renderComponent();
  }

  public getOutputs(): IOutputs {
    return {
      shouldClose: this._shouldClose,
      selectedRecordIds: this._selectedRecordIds,
    };
  }

  public destroy(): void {
    logger.info("DrillThroughWorkspace", "destroy() called");
    this.cleanupThemeListeners();
    this.cleanupKeyboardHandler();
    if (this._root) {
      this._root.unmount();
      this._root = null;
    }
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Private Methods
  // ─────────────────────────────────────────────────────────────────────────

  private renderComponent(): void {
    // Get allocated dimensions from Custom Page (responsive sizing)
    const allocatedWidth = this._context.mode.allocatedWidth;
    const allocatedHeight = this._context.mode.allocatedHeight;

    // Apply dimensions to container
    if (allocatedWidth > 0) {
      this._container.style.width = `${allocatedWidth}px`;
    } else {
      this._container.style.width = "100%";
    }

    if (allocatedHeight > 0) {
      this._container.style.height = `${allocatedHeight}px`;
    } else {
      this._container.style.height = "100%";
    }

    logger.info(
      "DrillThroughWorkspace",
      `Container size: ${allocatedWidth}x${allocatedHeight}`
    );

    // Get input parameters
    const chartDefinitionId =
      this._context.parameters.chartDefinitionId?.raw || "";

    // Get dataset from platform (Dataset PCF pattern per ADR-011)
    const dataset = this._context.parameters.dataset;

    logger.info(
      "DrillThroughWorkspace",
      `Rendering with chartDefinitionId: ${chartDefinitionId}, dataset records: ${
        dataset?.sortedRecordIds?.length ?? 0
      }`
    );

    // Create or update React root
    if (!this._root) {
      this._root = ReactDOM.createRoot(this._container);
    }

    this._root.render(
      React.createElement(
        FluentProvider,
        { theme: this._currentTheme },
        React.createElement(DrillThroughWorkspaceApp, {
          chartDefinitionId,
          dataset,
          webApi: this._context.webAPI,
          onRecordSelect: this.handleRecordSelect.bind(this),
          onClose: this.handleClose.bind(this),
        })
      )
    );
  }

  private handleRecordSelect(recordIds: string[]): void {
    this._selectedRecordIds = recordIds.join(",");
    this._notifyOutputChanged();
  }

  private handleClose(): void {
    this._shouldClose = true;
    this._notifyOutputChanged();
    this.closeDialog();
  }

  private closeDialog(): void {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (window as any).Xrm;

      // Try navigateBack for custom pages opened as dialogs
      if (xrm?.Navigation?.navigateBack) {
        xrm.Navigation.navigateBack();
        return;
      }

      // Try closing the form/dialog
      if (xrm?.Page?.ui?.close) {
        xrm.Page.ui.close();
        return;
      }

      // Fallback to window methods
      this.tryWindowClose();
    } catch (err) {
      logger.error(
        "DrillThroughWorkspace",
        "closeDialog error, trying fallback",
        err
      );
      this.tryWindowClose();
    }
  }

  private tryWindowClose(): void {
    try {
      // Try history.back() first
      if (window.history.length > 1) {
        window.history.back();
        return;
      }

      // For dialogs in iframe, message the parent
      if (window.parent !== window) {
        window.parent.postMessage({ type: "DRILLTHROUGH_WORKSPACE_CLOSE" }, "*");
      }

      // Try window.close as last resort
      window.close();
    } catch {
      logger.error("DrillThroughWorkspace", "Failed to close dialog");
    }
  }

  private _keyboardHandler = (e: KeyboardEvent) => {
    if (e.key === "Escape") {
      this.handleClose();
    }
  };

  private setupKeyboardHandler(): void {
    window.addEventListener("keydown", this._keyboardHandler);
  }

  private cleanupKeyboardHandler(): void {
    window.removeEventListener("keydown", this._keyboardHandler);
  }

  private setupThemeListeners(): void {
    if (typeof window !== "undefined" && window.matchMedia) {
      // Listen for dark mode changes
      this._themeMediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
      this._themeMediaQuery.addEventListener(
        "change",
        this.handleThemeChange.bind(this)
      );

      // Listen for high-contrast changes
      this._forcedColorsQuery = window.matchMedia("(forced-colors: active)");
      this._forcedColorsQuery.addEventListener(
        "change",
        this.handleThemeChange.bind(this)
      );
    }

    // Listen for custom theme change events
    window.addEventListener(
      "spaarke-theme-change",
      this.handleThemeChange.bind(this)
    );
  }

  private cleanupThemeListeners(): void {
    if (this._themeMediaQuery) {
      this._themeMediaQuery.removeEventListener(
        "change",
        this.handleThemeChange.bind(this)
      );
    }
    if (this._forcedColorsQuery) {
      this._forcedColorsQuery.removeEventListener(
        "change",
        this.handleThemeChange.bind(this)
      );
    }
    window.removeEventListener(
      "spaarke-theme-change",
      this.handleThemeChange.bind(this)
    );
  }

  private handleThemeChange(): void {
    this._currentTheme = getResolvedTheme();
    this.renderComponent();
  }
}
