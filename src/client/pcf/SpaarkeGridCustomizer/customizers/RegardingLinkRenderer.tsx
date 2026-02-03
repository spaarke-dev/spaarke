/**
 * RegardingLinkRenderer - Custom cell renderer for Regarding Record links
 *
 * Renders a clickable link that navigates to the parent record.
 * Works with Event entities to show links to Projects, Matters, etc.
 *
 * ADR Compliance:
 * - ADR-021: Fluent UI v9 with dark mode support
 */

import * as React from "react";
import { Link } from "@fluentui/react-components";
import { OpenRegular } from "@fluentui/react-icons";
import { GetRendererParams } from "../types/PAGridCustomizer";

/**
 * Entity type to entity logical name mapping
 * Based on sprk_eventregardingtype option set values
 */
const REGARDING_TYPE_TO_ENTITY: Record<number, string> = {
    0: "sprk_project",      // Project
    1: "sprk_matter",       // Matter
    2: "sprk_opportunity",  // Opportunity
    3: "account",           // Account (system entity)
    4: "contact",           // Contact (system entity)
};

/**
 * Extracts the regarding record ID from the row data
 */
function getRegardingRecordId(rowData: Record<string, unknown> | undefined): string | null {
    if (!rowData) return null;

    // Check common field names for regarding record ID
    const idFields = [
        "sprk_regardingrecordid",
        "_sprk_regardingrecordid_value",
        "regardingrecordid",
    ];

    for (const field of idFields) {
        const value = rowData[field];
        if (typeof value === "string" && value) {
            return value;
        }
    }

    return null;
}

/**
 * Extracts the regarding record type from the row data
 */
function getRegardingRecordType(rowData: Record<string, unknown> | undefined): string | null {
    if (!rowData) return null;

    // Check for type field
    const typeFields = [
        "sprk_regardingrecordtype",
        "regardingrecordtype",
    ];

    for (const field of typeFields) {
        const value = rowData[field];
        if (typeof value === "number") {
            return REGARDING_TYPE_TO_ENTITY[value] || null;
        }
        if (typeof value === "string" && value) {
            // Try to parse as number
            const numValue = parseInt(value, 10);
            if (!isNaN(numValue)) {
                return REGARDING_TYPE_TO_ENTITY[numValue] || null;
            }
            // Already an entity name
            return value;
        }
    }

    return null;
}

/**
 * Opens a record in a new window/tab
 */
function openRecord(entityName: string, recordId: string, context?: ComponentFramework.Context<unknown>): void {
    if (context?.navigation?.openForm) {
        context.navigation.openForm({
            entityName: entityName,
            entityId: recordId,
            openInNewWindow: false,
        });
    } else {
        // Fallback: construct Dynamics URL
        const baseUrl = window.location.origin;
        const url = `${baseUrl}/main.aspx?etn=${entityName}&id=${recordId}&pagetype=entityrecord`;
        window.open(url, "_blank");
    }
}

/**
 * RegardingLinkRenderer Component
 *
 * Renders a clickable link to navigate to the regarding (parent) record.
 */
export const RegardingLinkRenderer: React.FC<GetRendererParams> = (props) => {
    const { value, rowInfo, context } = props;

    // Extract display name from value
    const displayName = typeof value === "string" ? value : "";

    // If no display name, show empty state
    if (!displayName) {
        return React.createElement("span", {
            className: "sprk-cell-empty"
        }, "—");
    }

    // Extract regarding record info from row data
    const recordId = getRegardingRecordId(rowInfo?.data);
    const entityName = getRegardingRecordType(rowInfo?.data);

    // If we can't determine the record to navigate to, just show text
    if (!recordId || !entityName) {
        return React.createElement("span", null, displayName);
    }

    // Handle click to navigate
    const handleClick = (event: React.MouseEvent): void => {
        event.preventDefault();
        event.stopPropagation();
        openRecord(entityName, recordId, context);
    };

    // Handle keyboard navigation
    const handleKeyDown = (event: React.KeyboardEvent): void => {
        if (event.key === "Enter" || event.key === " ") {
            event.preventDefault();
            event.stopPropagation();
            openRecord(entityName, recordId, context);
        }
    };

    // Render as clickable link with icon
    return React.createElement(Link, {
        className: "sprk-regarding-link",
        onClick: handleClick,
        onKeyDown: handleKeyDown,
        role: "link",
        tabIndex: 0,
        title: `Open ${displayName}`,
        "aria-label": `Open ${displayName} record`,
    },
        React.createElement(OpenRegular, {
            className: "sprk-regarding-link-icon",
            "aria-hidden": true,
        }),
        React.createElement("span", {
            className: "sprk-regarding-link-text"
        }, displayName)
    );
};

export default RegardingLinkRenderer;
