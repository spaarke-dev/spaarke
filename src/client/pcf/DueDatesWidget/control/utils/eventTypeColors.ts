/**
 * Event Type Color Mapping Utility
 *
 * Maps event type names to Fluent UI v9 color tokens for consistent
 * badge coloring across the DueDatesWidget.
 *
 * Color mappings (per mockup):
 * - Hearing = yellow/gold
 * - Filing Deadline = green
 * - Regulatory Review = purple
 * - Meeting = blue
 * - Deadline = orange
 * - Court = yellow
 * - Patent = green
 *
 * ADR Compliance:
 * - ADR-021: All colors from Fluent semantic tokens (no hard-coded hex)
 */

import { tokens } from "@fluentui/react-components";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Available color variants for event type badges.
 * These map to Fluent UI v9 palette tokens.
 */
export type EventTypeColorVariant =
    | "yellow"
    | "green"
    | "purple"
    | "blue"
    | "orange"
    | "red"
    | "teal"
    | "default";

/**
 * Color configuration for an event type badge.
 * Includes both background and foreground tokens for accessibility.
 */
export interface IEventTypeColorConfig {
    /** Background color token for the badge indicator */
    background: string;
    /** Foreground color token for text on colored backgrounds (if needed) */
    foreground: string;
    /** Display name for the color (used in accessibility) */
    colorName: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Color Configurations (ADR-021: Design tokens only)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Color configurations using Fluent UI v9 semantic tokens.
 * These tokens automatically adjust for dark mode.
 */
export const eventTypeColorConfigs: Record<EventTypeColorVariant, IEventTypeColorConfig> = {
    yellow: {
        background: tokens.colorPaletteYellowBackground2,
        foreground: tokens.colorPaletteYellowForeground2,
        colorName: "yellow"
    },
    green: {
        background: tokens.colorPaletteGreenBackground2,
        foreground: tokens.colorPaletteGreenForeground2,
        colorName: "green"
    },
    purple: {
        background: tokens.colorPalettePurpleBackground2,
        foreground: tokens.colorPalettePurpleForeground2,
        colorName: "purple"
    },
    blue: {
        background: tokens.colorPaletteBlueBorderActive,
        foreground: tokens.colorPaletteBlueForeground2,
        colorName: "blue"
    },
    orange: {
        background: tokens.colorPaletteDarkOrangeBackground2,
        foreground: tokens.colorPaletteDarkOrangeForeground2,
        colorName: "orange"
    },
    red: {
        background: tokens.colorPaletteRedBackground2,
        foreground: tokens.colorPaletteRedForeground2,
        colorName: "red"
    },
    teal: {
        background: tokens.colorPaletteTealBackground2,
        foreground: tokens.colorPaletteTealForeground2,
        colorName: "teal"
    },
    default: {
        background: tokens.colorNeutralBackground5,
        foreground: tokens.colorNeutralForeground2,
        colorName: "neutral"
    }
};

// ─────────────────────────────────────────────────────────────────────────────
// Event Type to Color Mapping
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Mapping rules for event type names to color variants.
 * Keywords are checked in order, first match wins.
 */
const eventTypeKeywordMappings: Array<{ keywords: string[]; color: EventTypeColorVariant }> = [
    // Yellow variants (Hearings, Court)
    { keywords: ["hearing"], color: "yellow" },
    { keywords: ["court"], color: "yellow" },
    { keywords: ["trial"], color: "yellow" },

    // Green variants (Filing, Patent, Submission)
    { keywords: ["filing"], color: "green" },
    { keywords: ["patent"], color: "green" },
    { keywords: ["submission"], color: "green" },
    { keywords: ["application"], color: "green" },

    // Purple variants (Regulatory, Review)
    { keywords: ["regulatory"], color: "purple" },
    { keywords: ["review"], color: "purple" },
    { keywords: ["compliance"], color: "purple" },
    { keywords: ["audit"], color: "purple" },

    // Blue variants (Meeting, Conference)
    { keywords: ["meeting"], color: "blue" },
    { keywords: ["conference"], color: "blue" },
    { keywords: ["call"], color: "blue" },

    // Orange variants (Deadline, Due)
    { keywords: ["deadline"], color: "orange" },
    { keywords: ["due"], color: "orange" },
    { keywords: ["expiration"], color: "orange" },
    { keywords: ["renewal"], color: "orange" },

    // Teal variants (Project, Milestone)
    { keywords: ["project"], color: "teal" },
    { keywords: ["milestone"], color: "teal" },

    // Red variants (Urgent, Critical)
    { keywords: ["urgent"], color: "red" },
    { keywords: ["critical"], color: "red" },
    { keywords: ["emergency"], color: "red" }
];

/**
 * Get the color variant for an event type name.
 *
 * @param typeName - The event type name to map
 * @returns The color variant for the event type
 *
 * @example
 * getEventTypeColor("Hearing") // returns "yellow"
 * getEventTypeColor("Filing Deadline") // returns "green"
 * getEventTypeColor("Unknown Type") // returns "default"
 */
export function getEventTypeColor(typeName: string): EventTypeColorVariant {
    if (!typeName) return "default";

    const normalizedName = typeName.toLowerCase().trim();

    // Check each keyword mapping in order
    for (const mapping of eventTypeKeywordMappings) {
        for (const keyword of mapping.keywords) {
            if (normalizedName.includes(keyword)) {
                return mapping.color;
            }
        }
    }

    return "default";
}

/**
 * Get the full color configuration for an event type.
 *
 * @param typeName - The event type name
 * @returns The color configuration with background and foreground tokens
 *
 * @example
 * const config = getEventTypeColorConfig("Hearing");
 * // config.background = tokens.colorPaletteYellowBackground2
 */
export function getEventTypeColorConfig(typeName: string): IEventTypeColorConfig {
    const colorVariant = getEventTypeColor(typeName);
    return eventTypeColorConfigs[colorVariant];
}

/**
 * Get the background color token for an event type.
 * Convenience function for direct badge coloring.
 *
 * @param typeName - The event type name
 * @returns The background color token string
 */
export function getEventTypeBackgroundColor(typeName: string): string {
    return getEventTypeColorConfig(typeName).background;
}
