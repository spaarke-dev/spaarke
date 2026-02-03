/**
 * RegardingLink PCF Grid Customizer Control
 *
 * A Power Apps Grid Customizer that renders the "Regarding Record Name" column
 * as a clickable navigation link to the parent record.
 *
 * Implements PAGridCustomizer interface per:
 * https://learn.microsoft.com/en-us/power-apps/developer/component-framework/customize-editable-grid-control
 *
 * ADR Compliance:
 * - ADR-021: Fluent UI v9 with dark mode support
 * - ADR-022: React 16 APIs (ReactDOM.render, not createRoot)
 *
 * @version 1.1.0
 */

import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom"; // React 16 - NOT react-dom/client
import { FluentProvider, webLightTheme, webDarkTheme, Link, Theme } from "@fluentui/react-components";
import { Open16Regular } from "@fluentui/react-icons";

const CONTROL_VERSION = "1.1.0";

// Entity type mapping - values correspond to sprk_recordtype.sprk_entitylogicalname
// STUB: [CONFIG] - S007: Should query Record Type entity for dynamic mapping
const ENTITY_TYPE_MAP: Record<string, string> = {
    "sprk_project": "sprk_project",
    "sprk_matter": "sprk_matter",
    "sprk_invoice": "sprk_invoice",
    "sprk_analysis": "sprk_analysis",
    "account": "account",
    "contact": "contact",
    "sprk_workassignment": "sprk_workassignment",
    "sprk_budget": "sprk_budget"
};

// Column logical names for the regarding fields
const REGARDING_RECORD_TYPE_COLUMN = "sprk_regardingrecordtype";
const REGARDING_RECORD_ID_COLUMN = "sprk_regardingrecordid";
const REGARDING_RECORD_NAME_COLUMN = "sprk_regardingrecordname";

function resolveTheme(): Theme {
    const stored = localStorage.getItem('spaarke-theme');
    if (stored === 'dark') return webDarkTheme;
    if (stored === 'light') return webLightTheme;
    if (window.matchMedia?.('(prefers-color-scheme: dark)').matches) return webDarkTheme;
    return webLightTheme;
}

/**
 * Navigate to a record using Xrm.Navigation.openForm
 */
function navigateToRecord(entityLogicalName: string, entityId: string): void {
    const xrm = (window as any).Xrm || (window.parent as any)?.Xrm;
    if (xrm?.Navigation?.openForm) {
        xrm.Navigation.openForm({
            entityName: entityLogicalName,
            entityId: entityId.replace(/[{}]/g, '')
        });
    } else {
        console.error("[RegardingLink] Xrm.Navigation.openForm not available");
    }
}

/**
 * React component for rendering the clickable link cell
 */
interface LinkCellProps {
    recordName: string;
    recordId: string;
    entityLogicalName: string;
}

const LinkCell: React.FC<LinkCellProps> = ({ recordName, recordId, entityLogicalName }) => {
    const handleClick = (e: React.MouseEvent) => {
        e.preventDefault();
        e.stopPropagation();
        navigateToRecord(entityLogicalName, recordId);
    };

    // Using React.createElement instead of JSX for .ts file compatibility
    return React.createElement(
        FluentProvider,
        { theme: resolveTheme(), style: { display: 'inline-flex', alignItems: 'center' } },
        React.createElement(
            Link,
            {
                onClick: handleClick,
                style: { display: 'inline-flex', alignItems: 'center', gap: '4px' }
            },
            recordName,
            React.createElement(Open16Regular, null)
        )
    );
};

/**
 * PAGridCustomizer implementation
 * Customizes the "Regarding Record Name" column to render as a clickable link
 *
 * Note: Using 'any' type because PAGridCustomizer and CellRendererOverridesProps
 * are not yet exported in @types/powerapps-component-framework but exist at runtime
 */
const gridCustomizer: any = {
    /**
     * Called to get the cell customizer for specific columns
     */
    cellRendererOverrides: {
        /**
         * Override cell renderer for the Regarding Record Name column
         * This renders the cell as a clickable link that navigates to the parent record
         */
        [REGARDING_RECORD_NAME_COLUMN]: (
            props: any,
            renderedValue: React.ReactNode
        ): React.ReactNode => {
            // Get the row data
            const rowData = props.rowData;
            if (!rowData) {
                return renderedValue;
            }

            // Get the regarding record values from the row
            const recordName = rowData[REGARDING_RECORD_NAME_COLUMN]?.value as string;
            const recordId = rowData[REGARDING_RECORD_ID_COLUMN]?.value as string;
            const recordTypeValue = rowData[REGARDING_RECORD_TYPE_COLUMN]?.value;

            // If no record is associated, show the default value (em dash)
            if (!recordName || !recordId) {
                return React.createElement('span', { style: { color: '#888', fontStyle: 'italic' } }, '—');
            }

            // Determine entity logical name from record type
            // recordTypeValue could be a lookup reference or the entity logical name directly
            let entityLogicalName: string | undefined;

            if (typeof recordTypeValue === 'object' && recordTypeValue !== null) {
                // It's a lookup - try to get entity logical name from the lookup record
                // The Record Type entity stores sprk_entitylogicalname
                const lookupRef = recordTypeValue as any;
                entityLogicalName = lookupRef.sprk_entitylogicalname || lookupRef.name?.toLowerCase().replace(/\s/g, '') || undefined;
            } else if (typeof recordTypeValue === 'string') {
                // It's already an entity logical name
                entityLogicalName = recordTypeValue;
            } else if (typeof recordTypeValue === 'number') {
                // Legacy: optionset value - map to entity name
                const legacyMap: Record<number, string> = {
                    0: "sprk_project", 1: "sprk_matter", 2: "sprk_invoice", 3: "sprk_analysis",
                    4: "account", 5: "contact", 6: "sprk_workassignment", 7: "sprk_budget"
                };
                entityLogicalName = legacyMap[recordTypeValue];
            }

            if (!entityLogicalName) {
                // Can't determine entity type - show plain text
                return renderedValue;
            }

            // Return the clickable link component
            return React.createElement(LinkCell, {
                recordName,
                recordId,
                entityLogicalName
            });
        }
    }
};

/**
 * RegardingLink Virtual Control
 * Main entry point for the PCF control
 *
 * The getGridCustomizer static method is called by Power Apps Grid
 * to get the cell renderer overrides
 */
export class RegardingLink implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    /**
     * Static method to get the grid customizer
     * Called by Power Apps Grid to get cell renderer overrides
     * Note: Using 'any' return type because PAGridCustomizer is not in @types
     */
    public static getGridCustomizer(): any {
        return gridCustomizer;
    }

    private container: HTMLDivElement | null = null;
    private context: ComponentFramework.Context<IInputs>;
    private notifyOutputChanged: () => void;

    constructor() {}

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

        console.log(`[RegardingLink] Grid Customizer initialized v${CONTROL_VERSION}`);
    }

    /**
     * Update view - for grid customizer, minimal rendering
     */
    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;
        // Grid customizer handles cell rendering via getGridCustomizer()
        // This control renders nothing visually
    }

    /**
     * Get outputs - not used for grid customizers
     */
    public getOutputs(): IOutputs {
        return {};
    }

    /**
     * Cleanup
     */
    public destroy(): void {
        console.log("[RegardingLink] Grid Customizer destroyed");
    }
}
