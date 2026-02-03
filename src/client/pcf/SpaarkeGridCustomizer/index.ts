/**
 * SpaarkeGridCustomizer - General-purpose Power Apps Grid Control Customizer
 *
 * Provides custom cell rendering capabilities for Power Apps Grid Control.
 * Extensible architecture allows entity-specific customizations to be registered.
 *
 * ADR Compliance:
 * - ADR-021: Fluent UI v9 with dark mode support
 * - ADR-022: React 16 APIs (platform-provided React)
 *
 * @version 1.0.0
 */

import * as React from "react";
import { PAOneGridCustomizer, CellRendererOverrides, GetRendererParams } from "./types/PAGridCustomizer";
import { RegardingLinkRenderer } from "./customizers/RegardingLinkRenderer";

const CUSTOMIZER_VERSION = "1.0.0";

/**
 * Registry of cell renderers by column logical name or pattern
 * Extensible: add new renderers here for additional customizations
 */
const cellRendererRegistry: Record<string, React.FC<GetRendererParams>> = {
    // Regarding Record columns - renders clickable links to parent records
    "sprk_regardingrecordname": RegardingLinkRenderer,
    "sprk_regardingrecordid": RegardingLinkRenderer,
    // Add more column-specific renderers here as needed
};

/**
 * Pattern-based renderer lookup for columns matching naming conventions
 */
function getRendererForColumn(columnName: string): React.FC<GetRendererParams> | null {
    // Direct match first
    const lowerName = columnName.toLowerCase();
    if (cellRendererRegistry[lowerName]) {
        return cellRendererRegistry[lowerName];
    }

    // Pattern matching for regarding-related columns
    if (lowerName.includes("regarding") && (lowerName.includes("name") || lowerName.includes("id"))) {
        return RegardingLinkRenderer;
    }

    return null;
}

/**
 * Creates CellRendererOverrides based on registered renderers
 */
function createCellRendererOverrides(): CellRendererOverrides {
    return {
        // Text columns - check for registered custom renderers
        ["Text"]: (props: GetRendererParams) => {
            const columnName = props.columnInfo?.name || "";
            const CustomRenderer = getRendererForColumn(columnName);

            if (CustomRenderer) {
                return React.createElement(CustomRenderer, props);
            }

            // Return null to use default renderer
            return null;
        },
        // Lookup columns - for regarding lookups
        ["Lookup"]: (props: GetRendererParams) => {
            const columnName = props.columnInfo?.name || "";
            const CustomRenderer = getRendererForColumn(columnName);

            if (CustomRenderer) {
                return React.createElement(CustomRenderer, props);
            }

            return null;
        },
    };
}

/**
 * SpaarkeGridCustomizer - PAOneGridCustomizer implementation
 *
 * This is the main entry point that Power Apps Grid Control calls
 * to get custom cell renderers.
 */
export class SpaarkeGridCustomizer implements PAOneGridCustomizer {
    /**
     * Returns the cell renderer overrides for the grid
     */
    public getRendererOverrides(): CellRendererOverrides {
        if (typeof console !== "undefined") {
            console.log(`[SpaarkeGridCustomizer v${CUSTOMIZER_VERSION}] Initializing cell renderer overrides`);
        }
        return createCellRendererOverrides();
    }

    /**
     * Returns the cell editor overrides (not implemented yet)
     * Can be extended for inline editing customization
     */
    public getEditorOverrides(): null {
        return null;
    }
}

// Export the customizer class as the default export
// Power Apps Grid Control expects this pattern
export default SpaarkeGridCustomizer;
