/**
 * DueDatesWidgetRoot Component
 *
 * Main container component for the DueDatesWidget PCF control.
 * Displays upcoming and overdue events in list format per mockup with:
 * - Date column (day number + abbreviation)
 * - Event type badges (colored indicators)
 * - Days-until-due indicators (red circular badges)
 * - Click action to navigate to Events tab and open Event in side pane
 * - "All Events" footer link to navigate to Events Custom Page (Task 055)
 *
 * ADR Compliance:
 * - ADR-021: Fluent UI v9 exclusively, design tokens only
 * - ADR-022: React 16 APIs
 *
 * @see FR-01.6: Click card opens Side Pane | Navigate to Events tab, open Side Pane for that Event
 * @see projects/events-workspace-apps-UX-r1/tasks/054-duedateswidget-card-navigation.poml
 * @see projects/events-workspace-apps-UX-r1/tasks/055-duedateswidget-all-events-link.poml
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Text,
    Spinner,
    shorthands
} from "@fluentui/react-components";
import { Calendar20Regular } from "@fluentui/react-icons";
import { IInputs } from "../generated/ManifestTypes";
import { useUpcomingEvents } from "../hooks/useUpcomingEvents";
import { IEventItem } from "../services/eventFilterService";
import { EventListItem } from "./EventListItem";
import { WidgetFooter } from "./WidgetFooter";
import { navigateToEvent, navigateToEventsPage, NavigationResult } from "../services/navigationService";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IDueDatesWidgetRootProps {
    context: ComponentFramework.Context<IInputs>;
    parentRecordId: string;
    parentEntityName: string;
    maxItems: number;
    daysAhead: number;
    onEventSelect: (eventId: string, eventType: string) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021: Design tokens only, no hard-coded colors)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        height: "100%",
        width: "100%",
        boxSizing: "border-box",
        position: "relative"
    },
    header: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
        marginBottom: tokens.spacingVerticalXS
    },
    headerTitleText: {
        fontSize: tokens.fontSizeBase400,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1
    },
    eventList: {
        display: "flex",
        flexDirection: "column",
        overflowY: "auto",
        flexGrow: 1,
        ...shorthands.padding(0, tokens.spacingHorizontalM)
    },
    emptyState: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        flexGrow: 1,
        color: tokens.colorNeutralForeground3,
        rowGap: tokens.spacingVerticalS
    },
    emptyIcon: {
        fontSize: "48px",
        color: tokens.colorNeutralForeground4
    },
    loadingContainer: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        height: "100%"
    },
    errorContainer: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        color: tokens.colorStatusDangerForeground1,
        rowGap: tokens.spacingVerticalS
    },
    versionText: {
        position: "absolute",
        bottom: tokens.spacingVerticalXS,
        right: tokens.spacingHorizontalS,
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground4
    }
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const DueDatesWidgetRoot: React.FC<IDueDatesWidgetRootProps> = ({
    context,
    parentRecordId,
    parentEntityName,
    maxItems,
    daysAhead,
    onEventSelect
}) => {
    const styles = useStyles();

    // Navigation state for loading indicator
    const [navigatingEventId, setNavigatingEventId] = React.useState<string | null>(null);
    const [navigationError, setNavigationError] = React.useState<string | null>(null);

    // Use the hook for fetching events with proper filtering
    const { events, totalCount, loading, error, initialized } = useUpcomingEvents({
        context,
        parentRecordId,
        daysAhead,
        maxItems,
        includeOverdue: true,
        autoFetch: true
    });

    /**
     * Handle event list item click - navigates to Events tab and opens side pane
     * Per FR-01.6: Click card navigates to Events tab, opens Side Pane for that Event
     */
    const handleEventClick = React.useCallback(async (eventId: string, eventType: string): Promise<void> => {
        // Clear any previous navigation error
        setNavigationError(null);

        // Set loading state for this specific event
        setNavigatingEventId(eventId);

        try {
            const result: NavigationResult = await navigateToEvent({
                eventId,
                eventType,
                navigateToTab: true, // Navigate to Events tab first
                onNavigationComplete: () => {
                    console.log("[DueDatesWidget] Navigation complete for event:", eventId);
                },
                onNavigationError: (err: string) => {
                    console.error("[DueDatesWidget] Navigation error:", err);
                    setNavigationError(err);
                }
            });

            // Also notify the PCF control of the selection (for output binding)
            onEventSelect(eventId, eventType);

            if (!result.success && result.error) {
                // Show error only if complete failure (not partial success)
                setNavigationError(result.error);
            }

        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : "Navigation failed";
            console.error("[DueDatesWidget] Unexpected navigation error:", err);
            setNavigationError(errorMessage);
        } finally {
            // Clear loading state
            setNavigatingEventId(null);
        }
    }, [onEventSelect]);

    /**
     * Navigate to Events Custom Page (All Events link)
     * Task 055: Navigates to system-level Events page
     */
    const handleViewAllClick = React.useCallback((): void => {
        console.log("[DueDatesWidget] View All Events clicked");
        navigateToEventsPage({
            onNavigationComplete: () => {
                console.log("[DueDatesWidget] Navigation to Events page complete");
            },
            onNavigationError: (err: string) => {
                console.error("[DueDatesWidget] Navigation to Events page failed:", err);
                setNavigationError(err);
            }
        });
    }, []);

    // Loading state (only show spinner if not yet initialized)
    if (loading && !initialized) {
        return (
            <div className={styles.container}>
                <div className={styles.loadingContainer}>
                    <Spinner size="medium" label="Loading events..." />
                </div>
                <span className={styles.versionText}>v1.0.8</span>
            </div>
        );
    }

    // Error state
    if (error) {
        return (
            <div className={styles.container}>
                <div className={styles.errorContainer}>
                    <Text>{error}</Text>
                </div>
                <span className={styles.versionText}>v1.0.8</span>
            </div>
        );
    }

    return (
        <div className={styles.container}>
            {/* Header: "Upcoming Events" title */}
            <div className={styles.header}>
                <Text className={styles.headerTitleText}>Upcoming Events</Text>
            </div>

            {/* Event List - using new list layout components per mockup */}
            {events.length === 0 ? (
                <div className={styles.emptyState}>
                    <Calendar20Regular className={styles.emptyIcon} />
                    <Text>No upcoming events</Text>
                </div>
            ) : (
                <div className={styles.eventList}>
                    {events.map((event: IEventItem) => (
                        <EventListItem
                            key={event.id}
                            id={event.id}
                            name={event.name}
                            dueDate={event.dueDate}
                            eventType={event.eventType}
                            eventTypeName={event.eventTypeName}
                            description={event.description}
                            daysUntilDue={event.daysUntilDue}
                            isOverdue={event.isOverdue}
                            onClick={handleEventClick}
                            isNavigating={navigatingEventId === event.id}
                        />
                    ))}
                </div>
            )}

            {/* Footer with "All Events" link and count badge (Task 055) */}
            <WidgetFooter
                totalEventCount={totalCount}
                displayedCount={events.length}
                onViewAllClick={handleViewAllClick}
            />

            {/* Version footer (ADR requirement) */}
            <span className={styles.versionText}>v1.0.8</span>
        </div>
    );
};
