/**
 * UpdateRelatedButton PCF Control
 *
 * Triggers update of related records based on configured field mappings.
 * Uses the BFF API to execute field mapping rules.
 *
 * @remarks
 * - Uses React 16 APIs per ADR-022 (ReactDOM.render, not createRoot)
 * - Uses Fluent UI v9 per ADR-021 (via platform libraries)
 */

import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { FluentProvider, webLightTheme, webDarkTheme, Theme } from "@fluentui/react-components";
import { UpdateRelatedButtonApp, IUpdateRelatedButtonAppProps } from "./UpdateRelatedButtonApp";

export class UpdateRelatedButton implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement;
    private notifyOutputChanged: () => void;
    private context: ComponentFramework.Context<IInputs>;

    constructor() {
        // Empty constructor
    }

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        this.container = container;
        this.notifyOutputChanged = notifyOutputChanged;
        this.context = context;

        this.renderControl();
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;
        this.renderControl();
    }

    private renderControl(): void {
        const theme = this.resolveTheme();

        const props: IUpdateRelatedButtonAppProps = {
            buttonLabel: this.context.parameters.buttonLabel?.raw || "Update Related Records",
            sourceEntityId: this.context.parameters.sourceEntityId?.raw || "",
            sourceEntityType: this.context.parameters.sourceEntityType?.raw || "",
            mappingProfileId: this.context.parameters.mappingProfileId?.raw || undefined,
            apiBaseUrl: this.context.parameters.apiBaseUrl?.raw || "",
            webApi: this.context.webAPI,
            onUpdateComplete: this.handleUpdateComplete.bind(this),
        };

        // React 16 API per ADR-022 - use ReactDOM.render, NOT createRoot
        ReactDOM.render(
            React.createElement(
                FluentProvider,
                { theme, style: { height: '100%', width: '100%' } },
                React.createElement(UpdateRelatedButtonApp, props)
            ),
            this.container
        );
    }

    /**
     * Resolve theme following ADR-021 theme management pattern
     * Priority: localStorage > URL flag > PCF context > navbar detection > system preference
     */
    private resolveTheme(): Theme {
        // 1. Check localStorage override
        const storedTheme = localStorage.getItem('spaarke-theme');
        if (storedTheme === 'dark') return webDarkTheme;
        if (storedTheme === 'light') return webLightTheme;

        // 2. Check URL flag
        const urlParams = new URLSearchParams(window.location.search);
        const urlTheme = urlParams.get('theme');
        if (urlTheme === 'dark') return webDarkTheme;
        if (urlTheme === 'light') return webLightTheme;

        // 3. Try to detect from D365 navbar (common dark mode indicator)
        const navbar = document.querySelector('[data-id="navbar"]');
        if (navbar) {
            const bgColor = window.getComputedStyle(navbar).backgroundColor;
            if (bgColor && this.isDarkColor(bgColor)) {
                return webDarkTheme;
            }
        }

        // 4. Fallback to system preference
        if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
            return webDarkTheme;
        }

        return webLightTheme;
    }

    private isDarkColor(color: string): boolean {
        const match = color.match(/rgb\((\d+),\s*(\d+),\s*(\d+)\)/);
        if (match) {
            const [, r, g, b] = match.map(Number);
            const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
            return luminance < 0.5;
        }
        return false;
    }

    private handleUpdateComplete(success: boolean, message: string): void {
        // Could trigger notification or refresh
        if (success) {
            console.log("UpdateRelatedButton: Update completed successfully", message);
        } else {
            console.error("UpdateRelatedButton: Update failed", message);
        }
    }

    public getOutputs(): IOutputs {
        return {};
    }

    public destroy(): void {
        // React 16 API per ADR-022 - use unmountComponentAtNode, NOT root.unmount()
        ReactDOM.unmountComponentAtNode(this.container);
    }
}
