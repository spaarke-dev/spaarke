/**
 * FieldMappingAdmin PCF Control
 *
 * Admin UI for configuring field mapping profiles and rules.
 * Allows administrators to define which source entity fields map to which
 * target entity fields, with type compatibility validation.
 *
 * ADR Compliance:
 * - ADR-021: Fluent UI v9 with dark mode support
 * - ADR-022: React 16 APIs (ReactDOM.render, not createRoot)
 *
 * @version 1.0.0
 */

import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom"; // React 16 - NOT react-dom/client
import { FluentProvider, webLightTheme, webDarkTheme, Theme } from "@fluentui/react-components";
import { FieldMappingAdminApp } from "./FieldMappingAdminApp";

const CONTROL_VERSION = "1.1.0";

function resolveTheme(context?: ComponentFramework.Context<IInputs>): Theme {
    if (context?.fluentDesignLanguage?.isDarkTheme) return webDarkTheme;
    const stored = localStorage.getItem('spaarke-theme');
    if (stored === 'dark') return webDarkTheme;
    if (stored === 'light') return webLightTheme;
    if (window.matchMedia?.('(prefers-color-scheme: dark)').matches) return webDarkTheme;
    return webLightTheme;
}

export class FieldMappingAdmin implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement | null = null;
    private context: ComponentFramework.Context<IInputs>;
    private notifyOutputChanged: () => void;

    constructor() {}

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        this.context = context;
        this.notifyOutputChanged = notifyOutputChanged;
        this.container = container;
        context.mode.trackContainerResize(true);
        this.renderComponent();
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;
        this.renderComponent();
    }

    public getOutputs(): IOutputs {
        return {};
    }

    public destroy(): void {
        if (this.container) {
            ReactDOM.unmountComponentAtNode(this.container);
            this.container = null;
        }
    }

    private renderComponent(): void {
        if (!this.container) return;

        const theme = resolveTheme(this.context);
        const profileId = this.context.parameters.profileId?.raw || "";
        const apiBaseUrl = this.context.parameters.apiBaseUrl?.raw || "https://spe-api-dev-67e2xz.azurewebsites.net/api";

        ReactDOM.render(
            React.createElement(
                FluentProvider,
                { theme, style: { height: '100%', width: '100%' } },
                React.createElement(FieldMappingAdminApp, {
                    context: this.context,
                    profileId,
                    apiBaseUrl,
                    version: CONTROL_VERSION
                })
            ),
            this.container
        );
    }
}
