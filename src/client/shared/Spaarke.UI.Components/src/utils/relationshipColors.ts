/**
 * Shared color mapping for relationship types.
 *
 * Uses Fluent UI v9 design tokens so colors adapt to light/dark theme.
 * Matches the color scheme used in the DocumentRelationshipViewer code page.
 *
 * @see ADR-021 - Fluent UI v9 design tokens
 */

import { tokens } from "@fluentui/react-components";

/** Maps a relationship type to its Fluent UI v9 stroke color token. */
export function getRelationshipStroke(type?: string): string {
    switch (type) {
        case "semantic":
            return tokens.colorBrandStroke1;
        case "same_matter":
            return tokens.colorPaletteGreenBorder2;
        case "same_project":
            return tokens.colorPaletteGreenBorder1;
        case "same_email":
        case "same_thread":
            return tokens.colorStatusWarningBorder1;
        case "same_invoice":
            return tokens.colorPaletteBerryBorder2;
        default:
            return tokens.colorNeutralStroke1;
    }
}

/** Maps a relationship type to a node fill color for the mini graph preview. */
export function getRelationshipNodeFill(type?: string): string {
    switch (type) {
        case "semantic":
            return tokens.colorBrandBackground2;
        case "same_matter":
        case "same_project":
            return tokens.colorPaletteGreenBackground2;
        case "same_email":
        case "same_thread":
            return tokens.colorStatusWarningBackground1;
        case "same_invoice":
            return tokens.colorPaletteBerryBackground2;
        default:
            return tokens.colorNeutralBackground3;
    }
}
