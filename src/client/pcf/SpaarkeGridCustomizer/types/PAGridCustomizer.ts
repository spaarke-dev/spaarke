/**
 * Type definitions for Power Apps Grid Control Customizer
 *
 * These types define the interface contract that custom grid controls
 * must implement to work with Power Apps Grid Control.
 *
 * Based on: https://learn.microsoft.com/en-us/power-apps/developer/component-framework/customize-editable-grid-control
 */

import * as React from "react";

/**
 * Column information provided to cell renderers
 */
export interface ColumnInfo {
    /** Logical name of the column */
    name: string;
    /** Display name of the column */
    displayName?: string;
    /** Data type of the column */
    dataType?: string;
    /** Entity logical name the column belongs to */
    entityName?: string;
}

/**
 * Row data information
 */
export interface RowInfo {
    /** The unique ID of the row/record */
    id: string;
    /** Entity logical name */
    entityName?: string;
    /** Full record data */
    data?: Record<string, unknown>;
}

/**
 * Parameters passed to GetRenderer functions
 */
export interface GetRendererParams {
    /** The value to render in the cell */
    value: unknown;
    /** Information about the column being rendered */
    columnInfo?: ColumnInfo;
    /** Information about the row being rendered */
    rowInfo?: RowInfo;
    /** Callback to notify the grid that the value has changed */
    onChange?: (newValue: unknown) => void;
    /** Whether the cell is in readonly mode */
    isReadOnly?: boolean;
    /** The context utilities for navigation, web API, etc. */
    context?: ComponentFramework.Context<unknown>;
}

/**
 * Function type for custom cell renderers
 * Returns a React element or null to use default rendering
 */
export type GetRendererFunction = (params: GetRendererParams) => React.ReactElement | null;

/**
 * Cell renderer overrides by data type
 * Keys are Dataverse column data types: "Text", "Lookup", "OptionSet", etc.
 */
export interface CellRendererOverrides {
    [dataType: string]: GetRendererFunction;
}

/**
 * Cell editor overrides by data type (for inline editing)
 */
export interface CellEditorOverrides {
    [dataType: string]: GetRendererFunction;
}

/**
 * The main interface that grid customizers must implement
 */
export interface PAOneGridCustomizer {
    /**
     * Returns custom cell renderers keyed by column data type
     */
    getRendererOverrides(): CellRendererOverrides | null;

    /**
     * Returns custom cell editors keyed by column data type
     */
    getEditorOverrides(): CellEditorOverrides | null;
}
