/**
 * useUpcomingEvents Hook
 *
 * React hook for fetching and managing upcoming/overdue events.
 * Provides loading, error, and refresh capabilities.
 *
 * Usage:
 * ```tsx
 * const { events, loading, error, refresh } = useUpcomingEvents({
 *   context,
 *   parentRecordId: "...",
 *   daysAhead: 7,
 *   maxItems: 10
 * });
 * ```
 *
 * ADR Compliance:
 * - ADR-022: React 16 APIs (no hooks from React 18+)
 */

import * as React from "react";
import {
    IEventItem,
    IEventFilterParams,
    fetchUpcomingEventsWithCount
} from "../services/eventFilterService";
import { IInputs } from "../generated/ManifestTypes";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IUseUpcomingEventsParams {
    /** PCF context with webAPI access */
    context: ComponentFramework.Context<IInputs>;
    /** Parent record ID (Matter/Project) */
    parentRecordId: string;
    /** Number of days ahead to include (default 7) */
    daysAhead?: number;
    /** Maximum number of items to return (default 10) */
    maxItems?: number;
    /** Include overdue events (default true) */
    includeOverdue?: boolean;
    /** Auto-fetch on mount and param changes (default true) */
    autoFetch?: boolean;
}

export interface IUseUpcomingEventsResult {
    /** List of upcoming/overdue events */
    events: IEventItem[];
    /** Total count of events matching filter (before maxItems limit) */
    totalCount: number;
    /** Loading state */
    loading: boolean;
    /** Error message if fetch failed */
    error: string | null;
    /** Refresh data manually */
    refresh: () => Promise<void>;
    /** Whether initial fetch has completed */
    initialized: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

const DEFAULT_DAYS_AHEAD = 7;
const DEFAULT_MAX_ITEMS = 10;

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Hook for fetching upcoming and overdue events
 *
 * Filters events by:
 * - Parent record (regarding record)
 * - Actionable status (not completed/cancelled)
 * - Date range (overdue + days ahead)
 *
 * Sorts by due date ascending (most urgent first)
 */
export function useUpcomingEvents(params: IUseUpcomingEventsParams): IUseUpcomingEventsResult {
    const {
        context,
        parentRecordId,
        daysAhead = DEFAULT_DAYS_AHEAD,
        maxItems = DEFAULT_MAX_ITEMS,
        includeOverdue = true,
        autoFetch = true
    } = params;

    // State
    const [events, setEvents] = React.useState<IEventItem[]>([]);
    const [totalCount, setTotalCount] = React.useState<number>(0);
    const [loading, setLoading] = React.useState<boolean>(false);
    const [error, setError] = React.useState<string | null>(null);
    const [initialized, setInitialized] = React.useState<boolean>(false);

    // Track if component is mounted to prevent state updates after unmount
    const mountedRef = React.useRef<boolean>(true);

    // Store context reference for comparison
    const contextRef = React.useRef(context);
    contextRef.current = context;

    /**
     * Fetch events from Dataverse
     */
    const fetchEvents = React.useCallback(async (): Promise<void> => {
        // Don't fetch if no parent record ID
        if (!parentRecordId) {
            setEvents([]);
            setTotalCount(0);
            setLoading(false);
            setInitialized(true);
            return;
        }

        // Check if webAPI is available
        if (!contextRef.current?.webAPI) {
            setError("WebAPI not available");
            setLoading(false);
            setInitialized(true);
            return;
        }

        setLoading(true);
        setError(null);

        try {
            const filterParams: IEventFilterParams = {
                parentRecordId,
                daysAhead,
                maxItems,
                includeOverdue
            };

            const result = await fetchUpcomingEventsWithCount(
                contextRef.current.webAPI,
                filterParams
            );

            // Only update state if still mounted
            if (mountedRef.current) {
                setEvents(result.events);
                setTotalCount(result.totalCount);
                setError(null);
            }
        } catch (err) {
            console.error("[useUpcomingEvents] Error fetching events:", err);

            if (mountedRef.current) {
                // Provide user-friendly error message
                if (err instanceof Error) {
                    if (err.message.includes("401") || err.message.includes("Unauthorized")) {
                        setError("Authentication required. Please refresh the page.");
                    } else if (err.message.includes("403") || err.message.includes("Forbidden")) {
                        setError("You don't have permission to view events.");
                    } else if (err.message.includes("Network") || err.message.includes("fetch")) {
                        setError("Unable to connect. Please check your connection.");
                    } else {
                        setError("Failed to load events. Please try again.");
                    }
                } else {
                    setError("An unexpected error occurred.");
                }
            }
        } finally {
            if (mountedRef.current) {
                setLoading(false);
                setInitialized(true);
            }
        }
    }, [parentRecordId, daysAhead, maxItems, includeOverdue]);

    /**
     * Manual refresh function exposed to consumers
     */
    const refresh = React.useCallback(async (): Promise<void> => {
        await fetchEvents();
    }, [fetchEvents]);

    /**
     * Auto-fetch on mount and when dependencies change
     */
    React.useEffect(() => {
        if (autoFetch) {
            fetchEvents();
        }
    }, [autoFetch, fetchEvents]);

    /**
     * Cleanup on unmount
     */
    React.useEffect(() => {
        mountedRef.current = true;

        return () => {
            mountedRef.current = false;
        };
    }, []);

    return {
        events,
        totalCount,
        loading,
        error,
        refresh,
        initialized
    };
}

/**
 * Helper function to determine urgency level for styling
 */
export function getUrgencyLevel(daysUntilDue: number, isOverdue: boolean): "overdue" | "urgent" | "soon" | "normal" {
    if (isOverdue) return "overdue";
    if (daysUntilDue === 0) return "urgent"; // Due today
    if (daysUntilDue <= 3) return "soon";
    return "normal";
}

/**
 * Format days until due for display
 */
export function formatDaysUntilDue(daysUntilDue: number, isOverdue: boolean): string {
    if (isOverdue) {
        const overdueDays = Math.abs(daysUntilDue);
        return overdueDays === 1 ? "1 day overdue" : `${overdueDays} days overdue`;
    }

    if (daysUntilDue === 0) return "Due today";
    if (daysUntilDue === 1) return "Due tomorrow";
    return `Due in ${daysUntilDue} days`;
}
