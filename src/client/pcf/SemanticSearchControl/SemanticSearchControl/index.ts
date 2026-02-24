import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import {
    FluentProvider,
    Theme,
    webLightTheme,
    webDarkTheme,
} from "@fluentui/react-components";
import { resolveTheme, setupThemeListener } from "./services/ThemeService";
import {
    SemanticSearchControl as SemanticSearchControlComponent,
} from "./SemanticSearchControl";
import { ISemanticSearchControlProps } from "./types";

/**
 * SemanticSearchControl PCF Control
 *
 * Provides semantic document search with natural language queries and filters.
 * Integrates with the BFF Semantic Search API.
 *
 * Follows:
 * - ADR-006: PCF for all custom UI
 * - ADR-021: Fluent UI v9 with dark mode support
 * - ADR-022: React 16 APIs with platform libraries
 */
export class SemanticSearchControl
    implements ComponentFramework.ReactControl<IInputs, IOutputs>
{
    private notifyOutputChanged: () => void;
    private selectedDocumentId: string | undefined;
    private _theme: Theme = webLightTheme;
    private _cleanupThemeListener?: () => void;
    private _context: ComponentFramework.Context<IInputs>;

    constructor() {
        // Constructor - initialization happens in init()
    }

    /**
     * Initialize the control instance.
     */
    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary
    ): void {
        this.notifyOutputChanged = notifyOutputChanged;
        this._context = context;

        // Resolve initial theme from context
        this._theme = resolveTheme(context);

        // Set up theme change listener for dynamic updates
        this._cleanupThemeListener = setupThemeListener((isDark) => {
            this._theme = isDark ? webDarkTheme : webLightTheme;
            // Force re-render with new theme
            this.notifyOutputChanged();
        }, context);
    }

    /**
     * Called when any value in the property bag has changed.
     * Returns a React element (ReactControl pattern - ADR-022).
     *
     * Wraps content in FluentProvider with theme from context.
     */
    public updateView(
        context: ComponentFramework.Context<IInputs>
    ): React.ReactElement {
        // Update context reference and re-resolve theme
        this._context = context;
        this._theme = resolveTheme(context);

        // Create props for the main component
        const props: ISemanticSearchControlProps = {
            context,
            notifyOutputChanged: this.notifyOutputChanged,
            onDocumentSelect: this.handleDocumentSelect.bind(this),
            isDarkMode: this._theme === webDarkTheme,
        };

        // Create the main SemanticSearchControl component
        const content = React.createElement(SemanticSearchControlComponent, props);

        // Wrap in FluentProvider with resolved theme (ADR-021).
        // Explicit width/height/flex ensure the provider fills the PCF-allocated container.
        return React.createElement(
            FluentProvider,
            { theme: this._theme, style: { width: "100%", height: "100%", display: "flex", flexDirection: "column" } },
            content
        );
    }

    /**
     * Handle document selection from search results.
     * Updates the output property for Power Apps consumption.
     */
    private handleDocumentSelect(documentId: string): void {
        this.selectedDocumentId = documentId;
        this.notifyOutputChanged();
    }

    /**
     * Return output properties for Power Apps binding.
     */
    public getOutputs(): IOutputs {
        return {
            selectedDocumentId: this.selectedDocumentId,
        };
    }

    /**
     * Cleanup when control is removed from DOM.
     */
    public destroy(): void {
        // Clean up theme listener
        this._cleanupThemeListener?.();
    }
}
