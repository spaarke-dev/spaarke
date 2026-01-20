/**
 * Playbook Builder Host PCF Control
 *
 * Direct React Flow integration - NO iframe.
 * Uses react-flow-renderer v10 for React 16 compatibility (ADR-022).
 *
 * Architecture:
 * - PCF control renders React Flow canvas directly
 * - Zustand store manages canvas state
 * - No postMessage complexity
 *
 * ADR Compliance:
 * - ADR-022: React 16 APIs (ReactDOM.render, not createRoot)
 * - ADR-021: Fluent UI v9 theming
 *
 * @version 2.0.0
 */

import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { FluentProvider, webLightTheme, webDarkTheme, Theme } from "@fluentui/react-components";
import { PlaybookBuilderHost as PlaybookBuilderHostApp } from "./PlaybookBuilderHost";

// ─────────────────────────────────────────────────────────────────────────────
// Theme Utilities
// ─────────────────────────────────────────────────────────────────────────────

const STORAGE_KEY = 'spaarke-theme';
type ThemePreference = 'auto' | 'light' | 'dark';

function getUserThemePreference(): ThemePreference {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === 'light' || stored === 'dark' || stored === 'auto') {
      return stored;
    }
    try {
      const parentStored = window.parent?.localStorage?.getItem(STORAGE_KEY);
      if (parentStored === 'light' || parentStored === 'dark' || parentStored === 'auto') {
        return parentStored;
      }
    } catch { /* Cross-origin blocked */ }
  } catch { /* localStorage not available */ }
  return 'auto';
}

function detectDarkModeFromUrl(): boolean | null {
  try {
    if (window.location.href.includes('themeOption%3Ddarkmode') ||
        window.location.href.includes('themeOption=darkmode')) {
      return true;
    }
    try {
      const parentUrl = window.parent?.location?.href;
      if (parentUrl?.includes('themeOption%3Ddarkmode') ||
          parentUrl?.includes('themeOption=darkmode')) {
        return true;
      }
    } catch { /* Cross-origin blocked */ }
  } catch { /* Error */ }
  return null;
}

function getResolvedTheme(): Theme {
  const preference = getUserThemePreference();
  if (preference === 'dark') return webDarkTheme;
  if (preference === 'light') return webLightTheme;

  // Auto mode - check URL flag first
  const urlDarkMode = detectDarkModeFromUrl();
  if (urlDarkMode !== null) {
    return urlDarkMode ? webDarkTheme : webLightTheme;
  }

  // Fallback to system preference
  if (typeof window !== 'undefined' && window.matchMedia) {
    return window.matchMedia('(prefers-color-scheme: dark)').matches
      ? webDarkTheme
      : webLightTheme;
  }
  return webLightTheme;
}

// ─────────────────────────────────────────────────────────────────────────────
// Logger
// ─────────────────────────────────────────────────────────────────────────────

const LOG_PREFIX = '[PlaybookBuilderHost]';

function logInfo(message: string, data?: unknown): void {
  console.info(`${LOG_PREFIX} ${message}`, data ?? '');
}

function logError(message: string, error?: unknown): void {
  console.error(`${LOG_PREFIX} ${message}`, error ?? '');
}

// ─────────────────────────────────────────────────────────────────────────────
// PCF Control Class
// ─────────────────────────────────────────────────────────────────────────────

export class PlaybookBuilderHost
  implements ComponentFramework.StandardControl<IInputs, IOutputs>
{
  private container: HTMLDivElement | null = null;
  private notifyOutputChanged: () => void;
  private context: ComponentFramework.Context<IInputs> | null = null;
  private currentTheme: Theme = webLightTheme;
  private themeMediaQuery: MediaQueryList | null = null;

  // Output values
  private isDirty: boolean = false;
  private canvasJsonOutput: string | undefined = undefined;
  private playbookNameOutput: string | undefined = undefined;
  private playbookDescriptionOutput: string | undefined = undefined;

  // Fallback canvas data loaded via WebAPI when bound property is null
  private fallbackCanvasJson: string | null = null;
  private fallbackLoadAttempted: boolean = false;

  constructor() {
    logInfo('Constructor called');
  }

  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    try {
      // Log all bound parameters on init for debugging
      const canvasJsonParam = context.parameters.canvasJson;
      logInfo('Init - Parameter bindings', {
        canvasJson_raw: canvasJsonParam?.raw,
        canvasJson_type: canvasJsonParam?.type,
        canvasJson_formatted: canvasJsonParam?.formatted,
        playbookName: context.parameters.playbookName?.raw,
        playbookDescription: context.parameters.playbookDescription?.raw,
        playbookId: context.parameters.playbookId?.raw,
      });
      logInfo('Init - Setting up container');

      this.context = context;
      this.notifyOutputChanged = notifyOutputChanged;
      this.container = container;

      // Enable container resize tracking for responsive sizing
      context.mode.trackContainerResize(true);

      // Set container styles
      // Note: Custom Pages provide allocatedHeight/Width, form sections may not
      this.container.style.display = 'flex';
      this.container.style.flexDirection = 'column';
      this.container.style.boxSizing = 'border-box';
      this.container.style.overflow = 'hidden';

      // Use allocated dimensions from context (Custom Page provides these)
      this.updateContainerSize(context);

      // Set up theme
      this.currentTheme = getResolvedTheme();
      this.setupThemeListeners();

      // Initial render
      this.renderReactTree(context);

      logInfo('Init complete');
    } catch (error) {
      logError('Init failed', error);
      throw error;
    }
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    try {
      this.context = context;

      // Update container size on each view update (handles resize)
      this.updateContainerSize(context);

      this.renderReactTree(context);
    } catch (error) {
      logError('UpdateView failed', error);
    }
  }

  public getOutputs(): IOutputs {
    const outputs: IOutputs = {
      isDirty: this.isDirty,
    };

    // Include canvasJson only if it has been updated
    if (this.canvasJsonOutput !== undefined) {
      outputs.canvasJson = this.canvasJsonOutput;
    }

    // Include name and description if updated
    if (this.playbookNameOutput !== undefined) {
      outputs.playbookName = this.playbookNameOutput;
    }
    if (this.playbookDescriptionOutput !== undefined) {
      outputs.playbookDescription = this.playbookDescriptionOutput;
    }

    return outputs;
  }

  public destroy(): void {
    try {
      logInfo('Destroy - Unmounting React');

      this.cleanupThemeListeners();

      if (this.container) {
        // React 16 API - unmountComponentAtNode
        ReactDOM.unmountComponentAtNode(this.container);
        this.container = null;
      }

      this.context = null;
    } catch (error) {
      logError('Destroy failed', error);
    }
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Private Methods
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * Update container size based on allocated dimensions from context.
   * Custom Pages provide allocatedHeight/Width; form sections may not.
   */
  private updateContainerSize(context: ComponentFramework.Context<IInputs>): void {
    if (!this.container) return;

    const allocatedWidth = context.mode.allocatedWidth;
    const allocatedHeight = context.mode.allocatedHeight;

    logInfo('Container size update', { allocatedWidth, allocatedHeight });

    // Width: use allocated width if available, otherwise 100%
    if (allocatedWidth > 0) {
      this.container.style.width = `${allocatedWidth}px`;
    } else {
      this.container.style.width = '100%';
    }

    // Height: use allocated height if available, otherwise use minHeight fallback
    // Increased height by 30% (800px → 1040px) for better canvas visibility
    if (allocatedHeight > 0) {
      this.container.style.height = `${allocatedHeight}px`;
      this.container.style.minHeight = ''; // Clear minHeight when explicit height set
    } else {
      // Fallback for form sections that don't provide allocated height
      this.container.style.height = '100%';
      this.container.style.minHeight = '1040px';
    }
  }

  /**
   * Render the React component tree.
   * Uses React 16 API (ReactDOM.render) per ADR-022.
   */
  private renderReactTree(context: ComponentFramework.Context<IInputs>): void {
    if (!this.container) {
      logError('Cannot render - container not initialized');
      return;
    }

    try {
      // Try multiple methods to get the record ID
      // Method 1: contextInfo.entityId (standard for model-driven apps)
      const contextInfo = (context.mode as unknown as { contextInfo?: { entityId?: string; entityTypeName?: string } }).contextInfo;

      // Method 2: Extract from URL (fallback for model-driven apps)
      let urlRecordId = '';
      try {
        const url = window.location.href;
        // Model-driven app URL pattern: ...&id={guid}&...
        const idMatch = url.match(/[?&]id=([^&]+)/i);
        if (idMatch) {
          urlRecordId = decodeURIComponent(idMatch[1]);
        }
      } catch { /* URL parsing error */ }

      // Method 3: Input parameter (manual binding - fallback)
      const paramId = context.parameters.playbookId?.raw || '';

      // Use first available ID
      const playbookId = contextInfo?.entityId || urlRecordId || paramId;

      logInfo('Record ID resolution', {
        contextInfoEntityId: contextInfo?.entityId,
        urlRecordId,
        paramId,
        resolved: playbookId
      });

      // Get other input parameters
      const playbookName = context.parameters.playbookName?.raw || '';
      const playbookDescription = context.parameters.playbookDescription?.raw || '';
      const boundCanvasJson = context.parameters.canvasJson?.raw || '';
      const apiBaseUrl = context.parameters.apiBaseUrl?.raw || '';

      // Use bound property if available, otherwise use fallback from WebAPI
      const canvasJson = boundCanvasJson || this.fallbackCanvasJson || '';

      // If bound property is null and we have a playbook ID, load via WebAPI
      if (!boundCanvasJson && !this.fallbackCanvasJson && playbookId && !this.fallbackLoadAttempted) {
        logInfo('Bound canvasJson is null - triggering fallback WebAPI load', { playbookId });
        this.loadCanvasFromDataverse(playbookId);
      }

      logInfo('Rendering with context', {
        playbookId,
        entityType: contextInfo?.entityTypeName,
        hasCanvasJson: !!canvasJson,
        canvasJsonLength: canvasJson?.length || 0,
        canvasJsonPreview: canvasJson ? canvasJson.substring(0, 200) : '(empty)',
        playbookName,
        hasDescription: !!playbookDescription,
        usingFallback: !boundCanvasJson && !!this.fallbackCanvasJson,
      });

      // React 16 API - ReactDOM.render
      // FluentProvider needs flex styling to pass through the height chain
      // Use a key that changes when fallback canvas loads to force re-initialization
      const componentKey = this.fallbackCanvasJson ? 'with-fallback' : 'initial';

      ReactDOM.render(
        React.createElement(
          FluentProvider,
          {
            theme: this.currentTheme,
            style: { height: '100%', display: 'flex', flexDirection: 'column', flex: 1, minHeight: 0 }
          },
          React.createElement(PlaybookBuilderHostApp, {
            key: componentKey,
            playbookId,
            playbookName,
            playbookDescription,
            canvasJson,
            apiBaseUrl,
            onDirtyChange: this.handleDirtyChange.bind(this),
            onSave: this.handleSave.bind(this),
          })
        ),
        this.container
      );
    } catch (error) {
      logError('Render failed', error);
      throw error;
    }
  }

  private handleDirtyChange(isDirty: boolean): void {
    if (this.isDirty !== isDirty) {
      this.isDirty = isDirty;
      this.notifyOutputChanged();
    }
  }

  /**
   * Handle canvas sync from React component.
   * Updates bound field outputs, notifies framework, and auto-saves to Dataverse.
   */
  private handleSave(canvasJson: string, name: string, description: string): void {
    logInfo('Canvas synced to bound field', { jsonLength: canvasJson.length });

    // Store the updated values for getOutputs() to return
    this.canvasJsonOutput = canvasJson;
    this.playbookNameOutput = name;
    this.playbookDescriptionOutput = description;

    // Notify the framework that outputs have changed
    // This triggers getOutputs() and writes values back to bound fields
    this.notifyOutputChanged();

    // Auto-save to Dataverse so changes persist
    this.autoSaveToDataverse();
  }

  /**
   * Auto-save canvas to Dataverse using WebAPI.
   * This ensures changes persist without requiring manual form save.
   */
  private autoSaveToDataverse(): void {
    if (!this.context) {
      logInfo('No context available for auto-save');
      return;
    }

    try {
      // Try multiple methods to get the record ID (same as renderReactTree)
      const contextInfo = (this.context.mode as unknown as {
        contextInfo?: { entityId?: string; entityTypeName?: string }
      }).contextInfo;

      // Method 2: Extract from URL
      let urlRecordId = '';
      try {
        const url = window.location.href;
        const idMatch = url.match(/[?&]id=([^&]+)/i);
        if (idMatch) {
          urlRecordId = decodeURIComponent(idMatch[1]);
        }
      } catch { /* URL parsing error */ }

      // Method 3: Input parameter
      const paramId = this.context.parameters.playbookId?.raw || '';

      // Resolve entity ID
      const entityId = contextInfo?.entityId || urlRecordId || paramId;
      const entityName = contextInfo?.entityTypeName || 'sprk_analysisplaybook';

      logInfo('Auto-save entity resolution', {
        contextInfoId: contextInfo?.entityId,
        contextInfoType: contextInfo?.entityTypeName,
        urlRecordId,
        paramId,
        resolvedId: entityId,
        resolvedType: entityName,
      });

      if (!entityId) {
        logError('No entity ID found - cannot auto-save. Check PCF binding configuration.');
        return;
      }

      // Build the update data using the correct Dataverse field name
      const updateData: Record<string, unknown> = {};
      if (this.canvasJsonOutput !== undefined) {
        updateData['sprk_canvaslayoutjson'] = this.canvasJsonOutput;
      }

      if (Object.keys(updateData).length === 0) {
        logInfo('No canvas data to auto-save');
        return;
      }

      logInfo('Auto-saving canvas via WebAPI', {
        entityName,
        entityId,
        jsonLength: this.canvasJsonOutput?.length || 0,
      });

      // Use the PCF webAPI to update the record directly
      logInfo('Calling webAPI.updateRecord', { entityName, entityId, updateDataKeys: Object.keys(updateData) });
      this.context.webAPI.updateRecord(entityName, entityId, updateData).then(
        (result: unknown) => {
          logInfo('Canvas auto-saved successfully', { result });
        },
        (error: unknown) => {
          logError('Canvas auto-save FAILED', {
            error,
            errorMessage: (error as { message?: string })?.message,
            entityName,
            entityId,
            fieldName: 'sprk_canvaslayoutjson',
          });
        }
      );
    } catch (error) {
      logError('Failed to auto-save canvas', error);
    }
  }

  private setupThemeListeners(): void {
    // Listen for system theme changes (auto mode)
    if (typeof window !== 'undefined' && window.matchMedia) {
      this.themeMediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
      this.themeMediaQuery.addEventListener('change', this.handleSystemThemeChange.bind(this));
    }

    // Listen for custom theme change events
    window.addEventListener('spaarke-theme-change', this.handleThemeChange.bind(this));
  }

  private cleanupThemeListeners(): void {
    if (this.themeMediaQuery) {
      this.themeMediaQuery.removeEventListener('change', this.handleSystemThemeChange.bind(this));
    }
    window.removeEventListener('spaarke-theme-change', this.handleThemeChange.bind(this));
  }

  private handleSystemThemeChange(): void {
    if (getUserThemePreference() === 'auto') {
      this.currentTheme = getResolvedTheme();
      if (this.context) {
        this.renderReactTree(this.context);
      }
    }
  }

  private handleThemeChange(): void {
    this.currentTheme = getResolvedTheme();
    if (this.context) {
      this.renderReactTree(this.context);
    }
  }

  /**
   * Load canvas data directly from Dataverse via WebAPI.
   * Used as a fallback when the bound property is null (form binding not configured).
   */
  private loadCanvasFromDataverse(entityId: string): void {
    if (!this.context || this.fallbackLoadAttempted) {
      return;
    }

    this.fallbackLoadAttempted = true;
    const entityName = 'sprk_analysisplaybook';

    logInfo('Loading canvas from Dataverse via WebAPI (fallback)', {
      entityId,
      entityName,
    });

    // Retrieve the record with the canvas field
    this.context.webAPI
      .retrieveRecord(entityName, entityId, '?$select=sprk_canvaslayoutjson,sprk_name,sprk_description')
      .then(
        (record: ComponentFramework.WebApi.Entity) => {
          const canvasJson = record['sprk_canvaslayoutjson'] as string | null;
          logInfo('Canvas loaded from Dataverse (fallback)', {
            hasCanvas: !!canvasJson,
            canvasLength: canvasJson?.length || 0,
            canvasPreview: canvasJson ? canvasJson.substring(0, 200) : '(empty)',
          });

          if (canvasJson) {
            this.fallbackCanvasJson = canvasJson;
            // Re-render with the loaded canvas data
            if (this.context) {
              this.renderReactTree(this.context);
            }
          }
        },
        (error: unknown) => {
          logError('Failed to load canvas from Dataverse (fallback)', {
            error,
            errorMessage: (error as { message?: string })?.message,
            entityId,
          });
        }
      );
  }
}
