import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import {
    FluentProvider,
    Theme,
    webLightTheme,
    webDarkTheme,
} from "@fluentui/react-components";
import { resolveTheme, setupThemeListener } from "./services/ThemeService";
import { RelatedDocumentCount as RelatedDocumentCountComponent } from "./RelatedDocumentCount";
import { IRelatedDocumentCountProps } from "./types";

/**
 * RelatedDocumentCount PCF Control
 *
 * Displays a count of semantically related documents for the current record.
 * Uses the RelationshipCountCard shared component from @spaarke/ui-components.
 *
 * Follows:
 * - ADR-006: PCF for field-bound form controls
 * - ADR-012: Deep import from shared component library
 * - ADR-021: Fluent UI v9 with dark mode support
 * - ADR-022: React 16 APIs with platform libraries (ReactControl pattern)
 */
export class RelatedDocumentCount
    implements ComponentFramework.ReactControl<IInputs, IOutputs>
{
    private notifyOutputChanged: () => void;
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

        // Read properties from manifest
        const documentId = context.parameters.documentId?.raw ?? "";
        const tenantId = context.parameters.tenantId?.raw ?? undefined;
        const apiBaseUrl = context.parameters.apiBaseUrl?.raw ?? undefined;
        const cardTitle = context.parameters.cardTitle?.raw ?? undefined;

        // Create props for the main component
        const props: IRelatedDocumentCountProps = {
            context,
            documentId,
            tenantId: tenantId || undefined,
            apiBaseUrl: apiBaseUrl || undefined,
            cardTitle: cardTitle || undefined,
            isDarkMode: this._theme === webDarkTheme,
        };

        // Create the main component
        const content = React.createElement(RelatedDocumentCountComponent, props);

        // Wrap in FluentProvider with resolved theme (ADR-021)
        return React.createElement(
            FluentProvider,
            {
                theme: this._theme,
                style: { width: "100%", height: "100%", display: "flex", flexDirection: "column" },
            },
            content
        );
    }

    /**
     * Return output properties for Power Apps binding.
     */
    public getOutputs(): IOutputs {
        return {};
    }

    /**
     * Cleanup when control is removed from DOM.
     */
    public destroy(): void {
        this._cleanupThemeListener?.();
    }
}
