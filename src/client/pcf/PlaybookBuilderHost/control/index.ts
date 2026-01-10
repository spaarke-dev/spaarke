/**
 * Playbook Builder Host PCF Control
 *
 * Embeds the React 18 Playbook Builder app in an iframe.
 * Uses React 16 APIs per ADR-022 (platform-provided libraries).
 *
 * Architecture:
 * - PCF control (React 16) hosts an iframe
 * - Builder app (React 18) runs inside iframe
 * - Communication via postMessage bridge
 *
 * ADR Compliance:
 * - ADR-022: React 16 APIs (ReactDOM.render, not createRoot)
 * - ADR-021: Fluent UI v9 theming
 *
 * @version 1.0.0
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
      logInfo('Init - Setting up container');

      this.context = context;
      this.notifyOutputChanged = notifyOutputChanged;
      this.container = container;

      // Enable container resize tracking for responsive sizing
      context.mode.trackContainerResize(true);

      // Set container styles
      this.container.style.display = 'flex';
      this.container.style.flexDirection = 'column';
      this.container.style.boxSizing = 'border-box';
      this.container.style.overflow = 'hidden';
      this.container.style.width = '100%';
      this.container.style.height = '100%';

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

      // Debug: Log context.mode structure to understand what's available
      logInfo('UpdateView - context.mode keys', Object.keys(context.mode));
      logInfo('UpdateView - context.mode', context.mode);

      this.renderReactTree(context);
    } catch (error) {
      logError('UpdateView failed', error);
    }
  }

  public getOutputs(): IOutputs {
    return {
      isDirty: this.isDirty,
    };
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
      const canvasJson = context.parameters.canvasJson?.raw || '';
      const builderBaseUrl = context.parameters.builderBaseUrl?.raw || '/playbook-builder/';

      logInfo('Rendering with context', {
        playbookId,
        entityType: contextInfo?.entityTypeName,
        hasCanvasJson: !!canvasJson
      });

      // React 16 API - ReactDOM.render
      ReactDOM.render(
        React.createElement(
          FluentProvider,
          { theme: this.currentTheme },
          React.createElement(PlaybookBuilderHostApp, {
            playbookId,
            playbookName,
            canvasJson,
            builderBaseUrl,
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

  private handleSave(canvasJson: string): void {
    logInfo('Save requested', { jsonLength: canvasJson.length });

    // Update the bound field via WebAPI
    if (this.context?.parameters.canvasJson?.raw !== undefined) {
      // Trigger form update by notifying output changed
      // The parent form should handle persisting the canvasJson
      this.notifyOutputChanged();
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
}
