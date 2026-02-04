/**
 * DaysUntilDueBadge Component
 *
 * Displays a circular badge showing the number of days until an event is due.
 * Uses urgency-based coloring:
 * - Overdue: Deep red
 * - Critical (0-1 days): Red
 * - Urgent (2-3 days): Dark orange
 * - Warning (4-7 days): Orange/marigold
 * - Normal (8+ days): Neutral gray
 *
 * Features:
 * - Circular badge design per mockup
 * - Dynamic urgency coloring
 * - Accessible labels for screen readers
 * - Dark mode compatible via Fluent tokens
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
    getDaysUntilDue,
    getUrgencyColors,
    UrgencyLevel,
    IDaysUntilDueResult
} from "../utils/daysUntilDue";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IDaysUntilDueBadgeProps {
    /** The due date (will calculate days automatically) */
    dueDate?: Date;
    /** Number of days until due (alternative to dueDate) */
    daysUntilDue?: number;
    /** Whether the event is overdue (used with daysUntilDue) */
    isOverdue?: boolean;
    /** Force a specific urgency level (overrides automatic calculation) */
    urgencyOverride?: UrgencyLevel;
    /** Custom aria-label override */
    ariaLabel?: string;
    /** Size variant */
    size?: "small" | "medium";
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021: Design tokens only, no hard-coded colors)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    // Base badge style - circular per mockup
    badge: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        borderRadius: tokens.borderRadiusCircular,
        flexShrink: 0
    },
    // Size variants
    small: {
        minWidth: "20px",
        height: "20px",
        paddingLeft: tokens.spacingHorizontalXS,
        paddingRight: tokens.spacingHorizontalXS
    },
    medium: {
        minWidth: "24px",
        height: "24px",
        paddingLeft: tokens.spacingHorizontalSNudge,
        paddingRight: tokens.spacingHorizontalSNudge
    },
    // Urgency-based background colors
    overdue: {
        backgroundColor: tokens.colorStatusDangerBackground3
    },
    critical: {
        backgroundColor: tokens.colorPaletteRedBackground3
    },
    urgent: {
        backgroundColor: tokens.colorPaletteDarkOrangeBackground3
    },
    warning: {
        backgroundColor: tokens.colorPaletteMarigoldBackground3
    },
    normal: {
        backgroundColor: tokens.colorNeutralBackground5
    },
    // Text styles
    text: {
        lineHeight: "1",
        fontWeight: tokens.fontWeightSemibold
    },
    textSmall: {
        fontSize: tokens.fontSizeBase100
    },
    textMedium: {
        fontSize: tokens.fontSizeBase200
    },
    // Text colors based on background
    textOnBrand: {
        color: tokens.colorNeutralForegroundOnBrand
    },
    textNormal: {
        color: tokens.colorNeutralForeground1
    }
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const DaysUntilDueBadge: React.FC<IDaysUntilDueBadgeProps> = ({
    dueDate,
    daysUntilDue,
    isOverdue = false,
    urgencyOverride,
    ariaLabel,
    size = "medium"
}) => {
    const styles = useStyles();

    // Calculate days-until-due result
    const result: IDaysUntilDueResult = React.useMemo(() => {
        if (dueDate) {
            // If dueDate is provided, calculate everything from it
            return getDaysUntilDue(dueDate);
        }

        // Otherwise, use the provided daysUntilDue and isOverdue values
        const days = isOverdue ? -Math.abs(daysUntilDue ?? 0) : Math.abs(daysUntilDue ?? 0);
        return getDaysUntilDue(
            new Date(Date.now() + days * 24 * 60 * 60 * 1000)
        );
    }, [dueDate, daysUntilDue, isOverdue]);

    // Determine urgency level (allow override)
    const urgency = urgencyOverride ?? result.urgency;

    // Get background class based on urgency
    const getBackgroundClass = (): string => {
        switch (urgency) {
            case "overdue": return styles.overdue;
            case "critical": return styles.critical;
            case "urgent": return styles.urgent;
            case "warning": return styles.warning;
            default: return styles.normal;
        }
    };

    // Get text color class based on urgency
    // Dark backgrounds (overdue, critical, urgent) need light text
    const getTextColorClass = (): string => {
        switch (urgency) {
            case "overdue":
            case "critical":
            case "urgent":
                return styles.textOnBrand;
            default:
                return styles.textNormal;
        }
    };

    // Get size classes
    const sizeClass = size === "small" ? styles.small : styles.medium;
    const textSizeClass = size === "small" ? styles.textSmall : styles.textMedium;

    // Build accessible label
    const accessibleLabelText = ariaLabel ?? result.accessibleLabel;

    return (
        <div
            className={mergeClasses(
                styles.badge,
                sizeClass,
                getBackgroundClass()
            )}
            role="status"
            aria-label={accessibleLabelText}
        >
            <Text
                className={mergeClasses(
                    styles.text,
                    textSizeClass,
                    getTextColorClass()
                )}
                aria-hidden="true"
            >
                {result.displayValue}
            </Text>
        </div>
    );
};

// Re-export utility types and functions for convenience
export { UrgencyLevel, IDaysUntilDueResult, getDaysUntilDue, getUrgencyColors };
