/**
 * EventTypeBadge Component
 *
 * Displays an event type with:
 * - Colored square/rounded badge indicator
 * - Event type name text
 * - Accessible labels for screen readers
 *
 * Colors are mapped semantically to Fluent tokens for dark mode compatibility.
 *
 * ADR Compliance:
 * - ADR-021: Fluent UI v9 exclusively, design tokens only
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Text,
    mergeClasses
} from "@fluentui/react-components";
import {
    EventTypeColorVariant,
    getEventTypeColor,
    getEventTypeColorConfig
} from "../utils/eventTypeColors";

// Re-export for backward compatibility
export type EventTypeColor = EventTypeColorVariant;
export { getEventTypeColor };

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IEventTypeBadgeProps {
    /** The event type name to display */
    typeName: string;
    /** The color variant for the badge (optional - auto-detected from typeName) */
    color?: EventTypeColorVariant;
    /** Whether to show only the indicator without text */
    indicatorOnly?: boolean;
    /** Custom aria-label override */
    ariaLabel?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021: Design tokens only, no hard-coded colors)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    container: {
        display: "flex",
        alignItems: "center",
        columnGap: tokens.spacingHorizontalS
    },
    badge: {
        width: "12px",
        height: "12px",
        borderRadius: tokens.borderRadiusSmall,
        flexShrink: 0
    },
    // Color variants using semantic tokens for dark mode compatibility
    // These map to the mockup colors while maintaining accessibility
    yellow: {
        backgroundColor: tokens.colorPaletteYellowBackground2
    },
    green: {
        backgroundColor: tokens.colorPaletteGreenBackground2
    },
    purple: {
        backgroundColor: tokens.colorPalettePurpleBackground2
    },
    blue: {
        backgroundColor: tokens.colorPaletteBlueBorderActive
    },
    orange: {
        backgroundColor: tokens.colorPaletteDarkOrangeBackground2
    },
    red: {
        backgroundColor: tokens.colorPaletteRedBackground2
    },
    teal: {
        backgroundColor: tokens.colorPaletteTealBackground2
    },
    default: {
        backgroundColor: tokens.colorNeutralBackground5
    },
    typeName: {
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        whiteSpace: "nowrap"
    }
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const EventTypeBadge: React.FC<IEventTypeBadgeProps> = ({
    typeName,
    color,
    indicatorOnly = false,
    ariaLabel
}) => {
    const styles = useStyles();

    // Use provided color or derive from type name using the utility
    const badgeColor = color ?? getEventTypeColor(typeName);

    // Get color config for accessibility label
    const colorConfig = getEventTypeColorConfig(typeName);

    // Get the appropriate color class
    const colorClass = styles[badgeColor] || styles.default;

    // Generate accessible label
    const accessibleLabel = ariaLabel ?? `Event type: ${typeName}`;

    return (
        <div
            className={styles.container}
            role="img"
            aria-label={accessibleLabel}
        >
            <div
                className={mergeClasses(styles.badge, colorClass)}
                aria-hidden="true"
            />
            {!indicatorOnly && (
                <Text className={styles.typeName}>{typeName}</Text>
            )}
        </div>
    );
};
