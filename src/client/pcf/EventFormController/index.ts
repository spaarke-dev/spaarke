/**
 * EventFormController PCF Control
 *
 * Controls Event form field visibility based on Event Type configuration.
 * Reads the Event Type lookup from the current record via WebAPI and fetches
 * required fields configuration from sprk_eventtype.
 *
 * ADR Compliance:
 * - ADR-021: Fluent UI v9 with dark mode support
 * - ADR-022: React 16 APIs (ReactDOM.render, not createRoot)
 *
 * @version 2.0.0 - Refactored to use shared EventTypeService (ADR-012)
 */

import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom"; // React 16 - NOT react-dom/client
import { FluentProvider, webLightTheme, webDarkTheme, Theme } from "@fluentui/react-components";
import { EventFormControllerApp } from "./EventFormControllerApp";

const CONTROL_VERSION = "2.0.0";
const DEFAULT_EVENT_TYPE_FIELD = "sprk_eventtype";

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
    private _eventTypeFieldName: string = DEFAULT_EVENT_TYPE_FIELD;
    private _eventTypeId: string = "";
    private _eventTypeName: string = "";
    private _currentRecordId: string = "";
    private _isLoading: boolean = false;

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

        // Get configured field name or use default
        const configuredFieldName = context.parameters.eventTypeFieldName?.raw;
        this._eventTypeFieldName = configuredFieldName || DEFAULT_EVENT_TYPE_FIELD;

        // Log context info for debugging
        const modeAny = context.mode as unknown as Record<string, unknown>;
        console.log("[EventFormController] Init - contextInfo:", JSON.stringify(modeAny.contextInfo || {}));

        // Fetch Event Type from current record
        this.fetchEventTypeFromRecord();
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;

        // Check if field name configuration changed
        const configuredFieldName = context.parameters.eventTypeFieldName?.raw;
        const newFieldName = configuredFieldName || DEFAULT_EVENT_TYPE_FIELD;

        if (newFieldName !== this._eventTypeFieldName) {
            this._eventTypeFieldName = newFieldName;
        }

        // Check if record changed (new record or navigation)
        const entityId = this.getEntityId();
        if (entityId && entityId !== this._currentRecordId) {
            console.log(`[EventFormController] Record changed from ${this._currentRecordId} to ${entityId}`);
            this._currentRecordId = entityId;
            this.fetchEventTypeFromRecord();
        } else {
            // Just re-render with current state
            this.renderComponent();
        }
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

    /**
     * Gets the current entity ID from context
     */
    private getEntityId(): string {
        // Try multiple ways to get the entity ID
        const modeAny = this.context.mode as unknown as Record<string, unknown>;
        const contextInfo = modeAny.contextInfo as Record<string, unknown> | undefined;

        if (contextInfo?.entityId) {
            return String(contextInfo.entityId).replace(/[{}]/g, '');
        }

        // Try from page context
        const pageContext = (this.context as unknown as Record<string, unknown>).page as Record<string, unknown> | undefined;
        if (pageContext?.entityId) {
            return String(pageContext.entityId).replace(/[{}]/g, '');
        }

        return "";
    }

    /**
     * Gets the entity name from context
     */
    private getEntityName(): string {
        const modeAny = this.context.mode as unknown as Record<string, unknown>;
        const contextInfo = modeAny.contextInfo as Record<string, unknown> | undefined;

        if (contextInfo?.entityTypeName) {
            return String(contextInfo.entityTypeName);
        }

        // Default to sprk_event
        return "sprk_event";
    }

    /**
     * Fetches the Event Type lookup value from the current record via WebAPI
     */
    private async fetchEventTypeFromRecord(): Promise<void> {
        const entityId = this.getEntityId();
        const entityName = this.getEntityName();

        console.log(`[EventFormController] Fetching Event Type for ${entityName}:${entityId}, field: ${this._eventTypeFieldName}`);

        if (!entityId) {
            console.log("[EventFormController] No entity ID available - might be new record");
            this._eventTypeId = "";
            this._eventTypeName = "";
            this.renderComponent();
            return;
        }

        this._isLoading = true;
        this._currentRecordId = entityId;
        this.renderComponent();

        try {
            // Build the select query for the lookup field
            // For lookup fields, we need to request _fieldname_value for the ID
            // and use $expand to get the related record's name
            const lookupValueField = `_${this._eventTypeFieldName}_value`;

            const result = await this.context.webAPI.retrieveRecord(
                entityName,
                entityId,
                `?$select=${lookupValueField}`
            );

            console.log("[EventFormController] WebAPI result:", JSON.stringify(result));

            // Extract lookup value - the ID is in _fieldname_value
            const eventTypeId = result[lookupValueField] as string || "";
            // The formatted value (display name) is in _fieldname_value@OData.Community.Display.V1.FormattedValue
            const eventTypeName = result[`${lookupValueField}@OData.Community.Display.V1.FormattedValue`] as string || "";

            this._eventTypeId = eventTypeId ? eventTypeId.replace(/[{}]/g, '') : "";
            this._eventTypeName = eventTypeName;

            console.log(`[EventFormController] Event Type: id=${this._eventTypeId}, name=${this._eventTypeName}`);

            this._controlStatus = this._eventTypeId ? "loaded" : "no-event-type";
        } catch (err) {
            console.error("[EventFormController] Error fetching Event Type:", err);
            this._eventTypeId = "";
            this._eventTypeName = "";
            this._controlStatus = "error";
        } finally {
            this._isLoading = false;
            this.renderComponent();
        }
    }

    private handleStatusChange = (status: string): void => {
        this._controlStatus = status;
        this.notifyOutputChanged();
    };

    private renderComponent(): void {
        if (!this.container) return;

        const theme = resolveTheme(this.context);

        console.log(`[EventFormController] Rendering with eventTypeId=${this._eventTypeId}, eventTypeName=${this._eventTypeName}, loading=${this._isLoading}`);

        ReactDOM.render(
            React.createElement(
                FluentProvider,
                { theme, style: { height: '100%', width: '100%' } },
                React.createElement(EventFormControllerApp, {
                    context: this.context,
                    eventTypeId: this._eventTypeId,
                    eventTypeName: this._eventTypeName,
                    onStatusChange: this.handleStatusChange,
                    version: CONTROL_VERSION
                })
            ),
            this.container
        );
    }
}
