/**
 * DueDatesWidget PCF Control
 * Version 1.0.0 - React 16 Architecture with Fluent UI v9
 *
 * Displays upcoming and overdue events in card format for Matter/Project Overview tabs.
 * Shows actionable items with event type badges and days-until-due indicators.
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
import { DueDatesWidgetRoot } from "./components/DueDatesWidgetRoot";
import { ErrorBoundary } from "./components/ErrorBoundary";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";

export class DueDatesWidget implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement | null = null;
    private context: ComponentFramework.Context<IInputs> | null = null;
    private notifyOutputChanged: () => void;
    private cleanupThemeListener: (() => void) | null = null;

    // Output values
    private selectedEventOutputValue: string = "";

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
            selectedEventOutput: this.selectedEventOutputValue
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
     * Handle event selection from React component.
     */
    private handleEventSelect = (eventId: string, eventType: string): void => {
        this.selectedEventOutputValue = JSON.stringify({ eventId, eventType });
        this.notifyOutputChanged();
    };

    /**
     * Render the React component tree.
     * React 16: Use ReactDOM.render (NOT createRoot().render)
     */
    private renderComponent(): void {
        if (!this.container || !this.context) return;

        const theme = resolveTheme(this.context);

        // Get properties from manifest
        // Fallback to context.mode.contextInfo.entityId when property is not bound
        // Note: contextInfo exists at runtime but not in PCF type definitions
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const modeAny = this.context.mode as any;
        const contextInfo = modeAny?.contextInfo;

        let parentRecordId = this.context.parameters.parentRecordId?.raw || "";
        if (!parentRecordId && contextInfo?.entityId) {
            parentRecordId = contextInfo.entityId;
            console.log("[DueDatesWidget] v1.0.8 parentRecordId from context:", parentRecordId);
        }

        // Fallback to context.mode.contextInfo.entityTypeName when property is not bound
        let parentEntityName = this.context.parameters.parentEntityName?.raw || "";
        if (!parentEntityName && contextInfo?.entityTypeName) {
            parentEntityName = contextInfo.entityTypeName;
            console.log("[DueDatesWidget] v1.0.8 parentEntityName from context:", parentEntityName);
        }
        const maxItems = this.context.parameters.maxItems?.raw ?? 5;
        const daysAhead = this.context.parameters.daysAhead?.raw ?? 30;

        // React 16: ReactDOM.render
        ReactDOM.render(
            React.createElement(
                FluentProvider,
                { theme },
                React.createElement(
                    ErrorBoundary,
                    null,
                    React.createElement(DueDatesWidgetRoot, {
                        context: this.context,
                        parentRecordId,
                        parentEntityName,
                        maxItems,
                        daysAhead,
                        onEventSelect: this.handleEventSelect
                    })
                )
            ),
            this.container
        );
    }
}
