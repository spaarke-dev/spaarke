/**
 * ScopeConfigEditor PCF Control
 *
 * Adaptive editor for all 4 AI scope entity forms:
 * - Action  (sprk_analysisaction)       → rich system prompt textarea with token counter
 * - Skill   (sprk_analysisskill)       → compact textarea with injection preview
 * - Knowledge Source (sprk_analysisknowledge) → markdown textarea + file upload
 * - Tool    (sprk_analysistool)         → CodeMirror JSON editor + handler class dropdown
 *
 * ADR Compliance:
 * - ADR-006: PCF over webresources
 * - ADR-021: Fluent UI v9 exclusively; makeStyles; design tokens; dark mode; WCAG 2.1 AA
 * - ADR-022: virtual control-type (ReactControl); React 16 APIs; platform-library manifest
 *
 * Bundle target: < 1MB (CodeMirror ~300KB, NOT Monaco ~4MB)
 *
 * @version 1.0.0
 */

import { IInputs, IOutputs } from './generated/ManifestTypes';
import * as React from 'react';
import { FluentProvider, Theme, webLightTheme } from '@fluentui/react-components';
import { resolveThemeWithUserPreference } from '@spaarke/ui-components';
import { ScopeConfigEditorApp } from './components/ScopeConfigEditorApp';
import { getApiBaseUrl } from '../../shared/utils/environmentVariables';

// ─────────────────────────────────────────────────────────────────────────────
// PCF Control Class (virtual / ReactControl)
// ─────────────────────────────────────────────────────────────────────────────

export class ScopeConfigEditor implements ComponentFramework.ReactControl<IInputs, IOutputs> {
  private _notifyOutputChanged: () => void;
  private _context: ComponentFramework.Context<IInputs>;
  private _theme: Theme = webLightTheme;
  private _cleanupThemeListener?: () => void;

  // Output state
  private _updatedValue: string | undefined;

  // Runtime-resolved API base URL (from Dataverse environment variable)
  private _resolvedApiBaseUrl: string | undefined;
  private _apiBaseUrlError: string | undefined;

  constructor() {
    // Initialization happens in init()
  }

  /**
   * Initialize the control instance.
   * Called once when the control is first loaded.
   */
  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary
  ): void {
    this._notifyOutputChanged = notifyOutputChanged;
    this._context = context;

    // Resolve initial theme
    this._theme = resolveThemeWithUserPreference(context);

    // Theme is resolved by shared resolveThemeWithUserPreference() which checks
    // localStorage, PCF context, and navbar — OS prefers-color-scheme is NOT consulted (ADR-021).

    // Resolve BFF API base URL from Dataverse environment variable at runtime.
    // Falls back to the PCF input property apiBaseUrl if the env var query fails.
    this._resolveApiBaseUrl(context);
  }

  /**
   * Called when any value in the property bag changes.
   * Returns a React element (virtual / ReactControl pattern — ADR-022).
   */
  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    this._context = context;
    this._theme = resolveThemeWithUserPreference(context);

    const fieldValue = context.parameters.fieldValue?.raw ?? '';

    // Use runtime-resolved BFF URL from Dataverse environment variable.
    // Falls back to the PCF input property if the env var hasn't been resolved yet.
    const apiBaseUrl = this._resolvedApiBaseUrl ?? context.parameters.apiBaseUrl?.raw ?? '';

    // Detect entity type from form context
    const entityLogicalName = this._getEntityLogicalName(context);

    const appElement = React.createElement(ScopeConfigEditorApp, {
      entityLogicalName,
      fieldValue,
      apiBaseUrl,
      apiBaseUrlError: this._apiBaseUrlError,
      onValueChange: this._handleValueChange.bind(this),
    });

    // style={{ width: "100%" }} ensures the FluentProvider stretches to fill
    // the PCF container — without this, it collapses to content width on new-record forms.
    return React.createElement(FluentProvider, { theme: this._theme, style: { width: '100%' } }, appElement);
  }

  /**
   * Return current output values to Power Apps.
   */
  public getOutputs(): IOutputs {
    return {
      updatedValue: this._updatedValue,
    };
  }

  /**
   * Cleanup when the control is removed from the DOM.
   */
  public destroy(): void {
    this._cleanupThemeListener?.();
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Private helpers
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * Resolve BFF API base URL from Dataverse environment variable (sprk_BffApiBaseUrl).
   * This runs asynchronously during init and triggers a re-render when resolved.
   */
  private _resolveApiBaseUrl(context: ComponentFramework.Context<IInputs>): void {
    getApiBaseUrl(context.webAPI)
      .then((url) => {
        this._resolvedApiBaseUrl = url;
        this._apiBaseUrlError = undefined;
        // Trigger re-render so updateView picks up the resolved URL
        this._notifyOutputChanged();
        return;
      })
      .catch((err: unknown) => {
        const message = err instanceof Error ? err.message : 'Unknown error resolving BFF API URL';
        console.error('[ScopeConfigEditor] Failed to resolve BFF API URL from environment variable:', message);
        this._apiBaseUrlError = message;
        // Still trigger re-render so the error can be displayed
        this._notifyOutputChanged();
      });
  }

  /**
   * Detect the entity logical name from PCF context.
   * PCF virtual controls expose contextInfo.entityTypeName in mode.
   */
  private _getEntityLogicalName(context: ComponentFramework.Context<IInputs>): string {
    try {
      // Access mode contextInfo — available when control is on a form
      const contextInfo = (
        context.mode as unknown as {
          contextInfo?: { entityTypeName?: string };
        }
      ).contextInfo;
      if (contextInfo?.entityTypeName) {
        return contextInfo.entityTypeName.toLowerCase();
      }
    } catch {
      // Not on a form context — ignore
    }
    return '';
  }

  /**
   * Called by child components when the editor value changes.
   */
  private _handleValueChange(newValue: string): void {
    this._updatedValue = newValue;
    this._notifyOutputChanged();
  }
}
