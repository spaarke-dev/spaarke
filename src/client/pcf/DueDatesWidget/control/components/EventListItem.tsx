/**
 * EventListItem Component
 *
 * Displays a single event row in the list layout per mockup:
 * - Date column (day number + abbreviation)
 * - Event type badge (colored indicator + type name)
 * - Event name
 * - Description (truncated if too long)
 * - Days-until-due badge (red circular badge on right)
 *
 * This component is clickable and supports keyboard navigation (Enter/Space).
 * Shows loading state while navigation is in progress.
 *
 * ADR Compliance:
 * - ADR-021: Fluent UI v9 exclusively, design tokens only
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/054-duedateswidget-card-navigation.poml
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Text,
    Spinner,
    mergeClasses
} from "@fluentui/react-components";
import { DateColumn } from "./DateColumn";
import { EventTypeBadge, EventTypeColor } from "./EventTypeBadge";
import { DaysUntilDueBadge } from "./DaysUntilDueBadge";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IEventListItemProps {
    /** Unique event ID */
    id: string;
    /** Event name/title */
    name: string;
    /** Event due date */
    dueDate: Date;
    /** Event type ID (for navigation) */
    eventType: string;
    /** Event type display name */
    eventTypeName: string;
    /** Event description (optional) */
    description?: string;
    /** Number of days until due */
    daysUntilDue: number;
    /** Whether the event is overdue */
    isOverdue: boolean;
    /** Optional color override for the event type badge */
    eventTypeColor?: EventTypeColor;
    /** Click handler for the entire row */
    onClick?: (eventId: string, eventType: string) => void;
    /** Whether this item is currently navigating (loading state) */
    isNavigating?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021: Design tokens only, no hard-coded colors)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "row",
        alignItems: "center",
        width: "100%",
        paddingTop: tokens.spacingVerticalM,
        paddingBottom: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalS,
        paddingRight: tokens.spacingHorizontalS,
        boxSizing: "border-box",
        borderRadius: tokens.borderRadiusMedium,
        cursor: "pointer",
        backgroundColor: "transparent",
        transitionProperty: "background-color, opacity",
        transitionDuration: tokens.durationNormal,
        transitionTimingFunction: tokens.curveEasyEase,
        // Hover state
        "&:hover": {
            backgroundColor: tokens.colorNeutralBackground1Hover
        },
        // Focus state for keyboard navigation
        "&:focus": {
            outline: `2px solid ${tokens.colorBrandStroke1}`,
            outlineOffset: "-2px"
        },
        "&:focus-visible": {
            outline: `2px solid ${tokens.colorBrandStroke1}`,
            outlineOffset: "-2px"
        },
        // Active/pressed state
        "&:active": {
            backgroundColor: tokens.colorNeutralBackground1Pressed
        }
    },
    containerNavigating: {
        opacity: 0.7,
        pointerEvents: "none"
    },
    dateSection: {
        flexShrink: 0
    },
    contentSection: {
        display: "flex",
        flexDirection: "column",
        flexGrow: 1,
        minWidth: 0,  // Allow text truncation
        rowGap: tokens.spacingVerticalXXS
    },
    headerRow: {
        display: "flex",
        flexDirection: "row",
        alignItems: "center",
        columnGap: tokens.spacingHorizontalM
    },
    eventName: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        whiteSpace: "nowrap",
        overflow: "hidden",
        textOverflow: "ellipsis"
    },
    description: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        whiteSpace: "nowrap",
        overflow: "hidden",
        textOverflow: "ellipsis",
        maxWidth: "100%"
    },
    badgeSection: {
        flexShrink: 0,
        marginLeft: tokens.spacingHorizontalM,
        display: "flex",
        alignItems: "center"
    },
    // Separator line between items
    withSeparator: {
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`
    },
    // Loading spinner when navigating
    loadingSpinner: {
        marginLeft: tokens.spacingHorizontalXS
    }
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const EventListItem: React.FC<IEventListItemProps> = ({
    id,
    name,
    dueDate,
    eventType,
    eventTypeName,
    description,
    daysUntilDue,
    isOverdue,
    eventTypeColor,
    onClick,
    isNavigating = false
}) => {
    const styles = useStyles();

    /**
     * Handle click on the row
     * Disabled while navigating to prevent double-clicks
     */
    const handleClick = (): void => {
        if (isNavigating) return;
        onClick?.(id, eventType);
    };

    /**
     * Handle keyboard navigation (Enter/Space to activate)
     * Disabled while navigating to prevent double-activation
     */
    const handleKeyDown = (event: React.KeyboardEvent<HTMLDivElement>): void => {
        if (isNavigating) return;
        if (event.key === "Enter" || event.key === " ") {
            event.preventDefault();
            onClick?.(id, eventType);
        }
    };

    // Build class names - add navigating state style when applicable
    const containerClasses = mergeClasses(
        styles.container,
        isNavigating && styles.containerNavigating
    );

    return (
        <div
            className={containerClasses}
            onClick={handleClick}
            onKeyDown={handleKeyDown}
            role="button"
            tabIndex={isNavigating ? -1 : 0}
            aria-label={`${name}, ${eventTypeName}, due ${dueDate.toLocaleDateString()}, ${isOverdue ? `${Math.abs(daysUntilDue)} days overdue` : `${daysUntilDue} days remaining`}${isNavigating ? ", loading" : ""}`}
            aria-busy={isNavigating}
            aria-disabled={isNavigating}
        >
            {/* Date Column */}
            <div className={styles.dateSection}>
                <DateColumn date={dueDate} />
            </div>

            {/* Content Section */}
            <div className={styles.contentSection}>
                {/* Header row with badge and name */}
                <div className={styles.headerRow}>
                    <EventTypeBadge typeName={eventTypeName} color={eventTypeColor} />
                    <Text className={styles.eventName}>{name}</Text>
                </div>

                {/* Description (if provided) */}
                {description && (
                    <Text className={styles.description}>{description}</Text>
                )}
            </div>

            {/* Days Until Due Badge + Loading Spinner */}
            <div className={styles.badgeSection}>
                <DaysUntilDueBadge daysUntilDue={daysUntilDue} isOverdue={isOverdue} />
                {isNavigating && (
                    <Spinner
                        size="tiny"
                        className={styles.loadingSpinner}
                        aria-label="Opening event..."
                    />
                )}
            </div>
        </div>
    );
};
