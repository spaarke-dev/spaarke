/**
 * WidgetFooter Component
 *
 * Displays the widget footer with "All Events" link and optional count badge.
 * Shows count badge when there are more events than displayed (maxItems limit).
 *
 * Task 055: Add "All Events" link to navigate to Events Custom Page.
 *
 * ADR Compliance:
 * - ADR-021: Fluent UI v9 exclusively, design tokens only
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Link,
    Badge,
    Text,
    shorthands
} from "@fluentui/react-components";
import { ChevronRight20Regular } from "@fluentui/react-icons";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IWidgetFooterProps {
    /** Total number of events matching the filter criteria */
    totalEventCount: number;
    /** Number of items currently displayed */
    displayedCount: number;
    /** Callback when "All Events" link is clicked */
    onViewAllClick: () => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Show badge when total exceeds this threshold */
const BADGE_THRESHOLD = 10;

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021: Design tokens only, no hard-coded colors)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    footer: {
        display: "flex",
        alignItems: "center",
        justifyContent: "flex-end",
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
        ...shorthands.borderTop("1px", "solid", tokens.colorNeutralStroke2),
        marginTop: "auto"
    },
    linkContainer: {
        display: "flex",
        alignItems: "center",
        columnGap: tokens.spacingHorizontalXS
    },
    link: {
        display: "flex",
        alignItems: "center",
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightMedium,
        color: tokens.colorBrandForeground1,
        textDecoration: "none",
        cursor: "pointer",
        transitionProperty: "color",
        transitionDuration: tokens.durationNormal,
        "&:hover": {
            color: tokens.colorBrandForeground2,
            textDecoration: "underline"
        },
        "&:focus": {
            outlineStyle: "solid",
            outlineWidth: "2px",
            outlineColor: tokens.colorStrokeFocus2,
            outlineOffset: "2px"
        }
    },
    chevron: {
        fontSize: "12px",
        marginLeft: tokens.spacingHorizontalXXS,
        color: "inherit"
    },
    badge: {
        marginLeft: tokens.spacingHorizontalXS
    },
    badgeText: {
        fontWeight: tokens.fontWeightSemibold
    }
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const WidgetFooter: React.FC<IWidgetFooterProps> = ({
    totalEventCount,
    displayedCount,
    onViewAllClick
}) => {
    const styles = useStyles();

    // Determine if we should show the badge
    // Show badge when there are more events than displayed AND total exceeds threshold
    const showBadge = totalEventCount > displayedCount && totalEventCount > BADGE_THRESHOLD;

    // Calculate remaining events not shown
    const remainingCount = totalEventCount - displayedCount;

    /**
     * Handle keyboard navigation for the link
     */
    const handleKeyDown = (event: React.KeyboardEvent<HTMLAnchorElement>): void => {
        if (event.key === "Enter" || event.key === " ") {
            event.preventDefault();
            onViewAllClick();
        }
    };

    return (
        <div className={styles.footer}>
            <div className={styles.linkContainer}>
                <Link
                    className={styles.link}
                    onClick={onViewAllClick}
                    onKeyDown={handleKeyDown}
                    tabIndex={0}
                    aria-label={
                        showBadge
                            ? `View all events (${totalEventCount} total, ${remainingCount} more)`
                            : "View all events"
                    }
                >
                    All Events
                    <ChevronRight20Regular className={styles.chevron} />
                </Link>
                {showBadge && (
                    <Badge
                        className={styles.badge}
                        appearance="filled"
                        color="informative"
                        size="small"
                        aria-label={`${remainingCount} more events`}
                    >
                        <Text className={styles.badgeText}>+{remainingCount}</Text>
                    </Badge>
                )}
            </div>
        </div>
    );
};
