/**
 * EventFormController PCF Control
 *
 * Controls Event form field visibility based on Event Type configuration.
 * Reads required fields configuration from sprk_eventtype and shows/hides
 * form fields accordingly.
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
import { EventFormControllerApp } from "./EventFormControllerApp";

const CONTROL_VERSION = "1.0.0";

function resolveTheme(context?: ComponentFramework.Context<IInputs>): Theme {
    if (context?.fluentDesignLanguage?.isDarkTheme) return webDarkTheme;
    const stored = localStorage.getItem('spaarke-theme');
    if (stored === 'dark') return webDarkTheme;
    if (stored === 'light') return webLightTheme;
    if (window.matchMedia?.('(prefers-color-scheme: dark)').matches) return webDarkTheme;
    return webLightTheme;
}

export class EventFormController implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement | null = null;
    private context: ComponentFramework.Context<IInputs>;
    private notifyOutputChanged: () => void;
    private _controlStatus: string = "initialized";

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
        return {
            controlStatus: this._controlStatus
        };
    }

    public destroy(): void {
        if (this.container) {
            ReactDOM.unmountComponentAtNode(this.container);
            this.container = null;
        }
    }

    private handleStatusChange = (status: string): void => {
        this._controlStatus = status;
        this.notifyOutputChanged();
    };

    private renderComponent(): void {
        if (!this.container) return;

        const theme = resolveTheme(this.context);
        const eventTypeId = this.context.parameters.eventTypeId?.raw || "";
        const eventTypeName = this.context.parameters.eventTypeName?.raw || "";

        ReactDOM.render(
            React.createElement(
                FluentProvider,
                { theme, style: { height: '100%', width: '100%' } },
                React.createElement(EventFormControllerApp, {
                    context: this.context,
                    eventTypeId,
                    eventTypeName,
                    onStatusChange: this.handleStatusChange,
                    version: CONTROL_VERSION
                })
            ),
            this.container
        );
    }
}
