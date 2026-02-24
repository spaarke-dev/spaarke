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

import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import {
    FluentProvider,
    Theme,
    webLightTheme,
    webDarkTheme,
} from "@fluentui/react-components";
import { ScopeConfigEditorApp } from "./components/ScopeConfigEditorApp";

// ─────────────────────────────────────────────────────────────────────────────
// Theme Utilities
// ─────────────────────────────────────────────────────────────────────────────

function resolveTheme(context: ComponentFramework.Context<IInputs>): Theme {
    // Check URL for Dataverse dark mode flag
    try {
        const href = window.location.href;
        if (
            href.includes("themeOption%3Ddarkmode") ||
            href.includes("themeOption=darkmode")
        ) {
            return webDarkTheme;
        }
        // Try parent frame (PCF runs in iframe)
        try {
            const parentHref = window.parent?.location?.href;
            if (
                parentHref?.includes("themeOption%3Ddarkmode") ||
                parentHref?.includes("themeOption=darkmode")
            ) {
                return webDarkTheme;
            }
        } catch {
            // Cross-origin blocked — ignore
        }
    } catch {
        // Error reading location — ignore
    }

    // Light mode unless Dataverse explicitly sets dark mode via URL param.
    // Do NOT respect system prefers-color-scheme — the control is embedded in a
    // Dataverse form whose theme is determined by the Dataverse themeOption, not the OS.
    return webLightTheme;
}

// ─────────────────────────────────────────────────────────────────────────────
// PCF Control Class (virtual / ReactControl)
// ─────────────────────────────────────────────────────────────────────────────

export class ScopeConfigEditor
    implements ComponentFramework.ReactControl<IInputs, IOutputs>
{
    private _notifyOutputChanged: () => void;
    private _context: ComponentFramework.Context<IInputs>;
    private _theme: Theme = webLightTheme;
    private _cleanupThemeListener?: () => void;

    // Output state
    private _updatedValue: string | undefined;

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
        this._theme = resolveTheme(context);

        // No system theme listener — theme is determined solely by Dataverse
        // URL param (themeOption=darkmode), not OS preference.
    }

    /**
     * Called when any value in the property bag changes.
     * Returns a React element (virtual / ReactControl pattern — ADR-022).
     */
    public updateView(
        context: ComponentFramework.Context<IInputs>
    ): React.ReactElement {
        this._context = context;
        this._theme = resolveTheme(context);

        const fieldValue = context.parameters.fieldValue?.raw ?? "";
        const apiBaseUrl =
            context.parameters.apiBaseUrl?.raw ??
            "https://spe-api-dev-67e2xz.azurewebsites.net/api";

        // Detect entity type from form context
        const entityLogicalName = this._getEntityLogicalName(context);

        const appElement = React.createElement(ScopeConfigEditorApp, {
            entityLogicalName,
            fieldValue,
            apiBaseUrl,
            onValueChange: this._handleValueChange.bind(this),
        });

        // style={{ width: "100%" }} ensures the FluentProvider stretches to fill
        // the PCF container — without this, it collapses to content width on new-record forms.
        return React.createElement(
            FluentProvider,
            { theme: this._theme, style: { width: "100%" } },
            appElement
        );
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
     * Detect the entity logical name from PCF context.
     * PCF virtual controls expose contextInfo.entityTypeName in mode.
     */
    private _getEntityLogicalName(
        context: ComponentFramework.Context<IInputs>
    ): string {
        try {
            // Access mode contextInfo — available when control is on a form
            const contextInfo = (context.mode as unknown as {
                contextInfo?: { entityTypeName?: string };
            }).contextInfo;
            if (contextInfo?.entityTypeName) {
                return contextInfo.entityTypeName.toLowerCase();
            }
        } catch {
            // Not on a form context — ignore
        }
        return "";
    }

    /**
     * Called by child components when the editor value changes.
     */
    private _handleValueChange(newValue: string): void {
        this._updatedValue = newValue;
        this._notifyOutputChanged();
    }
}
