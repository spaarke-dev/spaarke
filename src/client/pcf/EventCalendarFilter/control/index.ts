/**
 * EventCalendarFilter PCF Control
 * Version 1.0.0 - React 16 Architecture with Fluent UI v9
 *
 * Date-based navigation and filtering for Event grids.
 * Displays multi-month vertical stack calendar with event indicators.
 *
 * ADR Compliance:
 * - ADR-022: React 16 APIs (ReactDOM.render, NOT createRoot)
 * - ADR-021: Fluent UI v9 exclusively
 * - ADR-006: PCF control structure
 */

import * as React from "react";
import * as ReactDOM from "react-dom"; // React 16 - NOT react-dom/client
import { FluentProvider } from "@fluentui/react-components";
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { EventCalendarFilterRoot } from "./components/EventCalendarFilterRoot";
import { ErrorBoundary } from "./components/ErrorBoundary";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";

export class EventCalendarFilter implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement | null = null;
    private context: ComponentFramework.Context<IInputs> | null = null;
    private notifyOutputChanged: () => void;
    private cleanupThemeListener: (() => void) | null = null;

    // Output values
    private filterOutputValue: string = "";
    private selectedDateOutputValue: string = "";

    constructor() {
        // Constructor
    }

    /**
     * Initialize the control instance.
     * React 16: Store container reference for ReactDOM.render
     */
    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        this.container = container;
        this.context = context;
        this.notifyOutputChanged = notifyOutputChanged;

        // Enable responsive container sizing
        context.mode.trackContainerResize(true);

        // Set up theme listener for dynamic theme changes
        this.cleanupThemeListener = setupThemeListener(
            () => {
                // Re-render when theme changes
                if (this.context && this.container) {
                    this.renderComponent();
                }
            },
            context
        );

        // Initial render
        this.renderComponent();
    }

    /**
     * Called when any value in the property bag has changed.
     */
    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;
        this.renderComponent();
    }

    /**
     * Return outputs to the framework.
     */
    public getOutputs(): IOutputs {
        return {
            filterOutput: this.filterOutputValue,
            selectedDateOutput: this.selectedDateOutputValue
        };
    }

    /**
     * Destroy the control.
     * React 16: Use unmountComponentAtNode (NOT root.unmount)
     */
    public destroy(): void {
        // Clean up theme listener
        if (this.cleanupThemeListener) {
            this.cleanupThemeListener();
            this.cleanupThemeListener = null;
        }

        // React 16: unmountComponentAtNode
        if (this.container) {
            ReactDOM.unmountComponentAtNode(this.container);
            this.container = null;
        }

        this.context = null;
    }

    /**
     * Handle filter output change from React component.
     */
    private handleFilterOutputChange = (filterJson: string): void => {
        this.filterOutputValue = filterJson;
        this.notifyOutputChanged();
    };

    /**
     * Handle selected date change from React component.
     */
    private handleSelectedDateChange = (dateString: string): void => {
        this.selectedDateOutputValue = dateString;
        this.notifyOutputChanged();
    };

    /**
     * Render the React component tree.
     * React 16: Use ReactDOM.render (NOT createRoot().render)
     */
    private renderComponent(): void {
        if (!this.container || !this.context) return;

        const theme = resolveTheme(this.context);

        // Parse event dates JSON
        let eventDates: string[] = [];
        const eventDatesJson = this.context.parameters.eventDatesJson?.raw;
        if (eventDatesJson) {
            try {
                eventDates = JSON.parse(eventDatesJson);
            } catch {
                // Invalid JSON - use empty array
            }
        }

        // Get display mode
        const displayMode = this.context.parameters.displayMode?.raw || "multiMonth";

        // React 16: ReactDOM.render
        ReactDOM.render(
            React.createElement(
                FluentProvider,
                { theme },
                React.createElement(
                    ErrorBoundary,
                    null,
                    React.createElement(EventCalendarFilterRoot, {
                        context: this.context,
                        eventDates,
                        displayMode: displayMode as "month" | "multiMonth",
                        onFilterOutputChange: this.handleFilterOutputChange,
                        onSelectedDateChange: this.handleSelectedDateChange
                    })
                )
            ),
            this.container
        );
    }
}
