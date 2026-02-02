/**
 * AssociationResolver PCF Control
 *
 * Allows users to select a parent entity type and record for Events.
 * After selection, auto-populates Event fields via the Field Mapping Framework.
 *
 * Supports 8 entity types:
 * - Matter, Project, Invoice, Analysis, Account, Contact, Work Assignment, Budget
 *
 * ADR Compliance:
 * - ADR-021: Fluent UI v9 with dark mode support
 * - ADR-022: React 16 APIs (ReactDOM.render, not createRoot)
 *
 * STUB: [AUTH] - S009: No authentication token handling for BFF API calls
 * Currently relies on browser session cookies. May need MSAL integration for
 * bearer token authentication when calling BFF API endpoints.
 *
 * @version 1.0.0
 */

import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom"; // React 16 - NOT react-dom/client
import { FluentProvider, webLightTheme, webDarkTheme, Theme } from "@fluentui/react-components";
import { AssociationResolverApp } from "./AssociationResolverApp";

// Control version for footer display
const CONTROL_VERSION = "1.0.0";

/**
 * Resolve theme based on PCF context and user preference
 */
function resolveTheme(context?: ComponentFramework.Context<IInputs>): Theme {
    // Check PCF context for dark mode
    if (context?.fluentDesignLanguage?.isDarkTheme) {
        return webDarkTheme;
    }

    // Check localStorage user preference
    const stored = localStorage.getItem('spaarke-theme');
    if (stored === 'dark') return webDarkTheme;
    if (stored === 'light') return webLightTheme;

    // Check URL flag
    const url = window.location.href;
    if (url.includes('themeOption%3Ddarkmode') || url.includes('themeOption=darkmode')) {
        return webDarkTheme;
    }

    // Check system preference
    if (window.matchMedia?.('(prefers-color-scheme: dark)').matches) {
        return webDarkTheme;
    }

    return webLightTheme;
}

/**
 * AssociationResolver PCF Control
 */
export class AssociationResolver implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement | null = null;
    private context: ComponentFramework.Context<IInputs>;
    private notifyOutputChanged: () => void;

    // Output values
    private _regardingRecordId: string = "";
    private _regardingRecordName: string = "";

    constructor() {
        // Constructor
    }

    /**
     * Initialize the control
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

        // Enable responsive container sizing
        context.mode.trackContainerResize(true);

        // Initial render
        this.renderComponent();
    }

    /**
     * Update view when context changes
     */
    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;
        this.renderComponent();
    }

    /**
     * Get output values
     */
    public getOutputs(): IOutputs {
        return {
            regardingRecordId: this._regardingRecordId,
            regardingRecordName: this._regardingRecordName
        };
    }

    /**
     * Cleanup on destroy
     */
    public destroy(): void {
        // React 16: unmountComponentAtNode (NOT root.unmount())
        if (this.container) {
            ReactDOM.unmountComponentAtNode(this.container);
            this.container = null;
        }
    }

    /**
     * Handle record selection from child component
     */
    private handleRecordSelected = (recordId: string, recordName: string): void => {
        this._regardingRecordId = recordId;
        this._regardingRecordName = recordName;
        this.notifyOutputChanged();
    };

    /**
     * Render the React component
     */
    private renderComponent(): void {
        if (!this.container) return;

        const theme = resolveTheme(this.context);
        const regardingRecordType = this.context.parameters.regardingRecordType?.raw;
        const apiBaseUrl = this.context.parameters.apiBaseUrl?.raw || "https://spe-api-dev-67e2xz.azurewebsites.net/api";

        // React 16: ReactDOM.render (NOT createRoot().render())
        ReactDOM.render(
            React.createElement(
                FluentProvider,
                { theme, style: { height: '100%', width: '100%' } },
                React.createElement(AssociationResolverApp, {
                    context: this.context,
                    regardingRecordType,
                    apiBaseUrl,
                    onRecordSelected: this.handleRecordSelected,
                    version: CONTROL_VERSION
                })
            ),
            this.container
        );
    }
}
