/**
 * Side Pane Utilities for UniversalDatasetGrid
 *
 * Provides functions to open the EventDetailSidePane Custom Page via
 * Xrm.App.sidePanes API when clicking hyperlinks in the grid.
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/031-implement-sidepane-opening.poml
 * @see projects/events-workspace-apps-UX-r1/design.md - Side pane opening pattern
 *
 * References:
 * - https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-app-sidepanes
 */

/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Xrm: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

import { logger } from "./logger";

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Unique pane ID for EventDetailSidePane - used to check for existing pane */
const EVENT_DETAIL_PANE_ID = "eventDetailPane";

/** Custom Page name for the Event Detail Side Pane */
const EVENT_DETAIL_PAGE_NAME = "sprk_eventdetailsidepane";

/** Default width for the side pane (per design spec) */
const PANE_WIDTH = 400;

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Parameters for opening the Event Detail Side Pane
 */
export interface OpenEventDetailPaneParams {
    /** GUID of the Event record to display */
    eventId: string;
    /** GUID of the Event Type lookup (optional) */
    eventType?: string;
    /** Callback when side pane is closed (optional) */
    onClose?: () => void;
}

/**
 * Result of the side pane open operation
 */
export interface SidePaneOpenResult {
    /** Whether the operation succeeded */
    success: boolean;
    /** Error message if failed */
    error?: string;
    /** The pane ID if successful */
    paneId?: string;
}

/**
 * Xrm.App.sidePanes type definitions (subset of full API)
 * These are not fully typed in @types/xrm, so we define what we need here.
 */
interface SidePaneCreateOptions {
    title: string;
    paneId: string;
    canClose: boolean;
    width: number;
    imageSrc?: string;
    hideHeader?: boolean;
    isSelected?: boolean;
    badge?: boolean;
}

interface SidePane {
    paneId: string;
    title?: string;
    close(): void;
    select(): void;
    navigate(pageInput: SidePanePageInput): Promise<void>;
}

interface SidePanePageInput {
    pageType: "custom";
    name: string;
    /** Parameters passed as URL query string to the Custom Page */
    recordId?: string;
}

interface AppSidePanes {
    state: 0 | 1; // 0 = collapsed, 1 = expanded
    createPane(options: SidePaneCreateOptions): Promise<SidePane>;
    getPane(paneId: string): SidePane | undefined;
    getAllPanes(): SidePane[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Helper Functions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Check if Xrm.App.sidePanes API is available
 * @returns true if sidePanes API exists
 */
function isSidePanesAvailable(): boolean {
    return !!(
        typeof Xrm !== "undefined" &&
        Xrm.App &&
        (Xrm.App as unknown as { sidePanes?: AppSidePanes }).sidePanes
    );
}

/**
 * Get the sidePanes API with proper typing
 */
function getSidePanes(): AppSidePanes | null {
    if (!isSidePanesAvailable()) {
        return null;
    }
    return (Xrm.App as unknown as { sidePanes: AppSidePanes }).sidePanes;
}

/**
 * Build the Custom Page input for navigation
 * Note: Custom Pages receive parameters via URL query string or recordId.
 * We use query string format: ?eventId={guid}&eventType={guid}
 */
function buildPageInput(
    eventId: string,
    eventType?: string
): SidePanePageInput {
    // For Custom Pages, we can pass parameters via the name with query string
    // Format: pagename?param1=value1&param2=value2
    let pageName = EVENT_DETAIL_PAGE_NAME;

    // Build query parameters
    const params = new URLSearchParams();
    params.set("eventId", eventId);
    if (eventType) {
        params.set("eventType", eventType);
    }

    return {
        pageType: "custom",
        name: `${pageName}?${params.toString()}`,
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Main Export Functions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Open the Event Detail Side Pane for a specific event.
 *
 * This function:
 * 1. Checks if an EventDetailSidePane is already open
 * 2. If open, navigates to the new event (reuses pane)
 * 3. If not open, creates a new pane with the event
 *
 * @param params - Parameters including eventId and optionally eventType
 * @returns Promise resolving to operation result
 *
 * @example
 * ```typescript
 * // Open side pane for an event
 * const result = await openEventDetailPane({
 *     eventId: "12345678-1234-1234-1234-123456789012",
 *     eventType: "87654321-4321-4321-4321-210987654321"
 * });
 *
 * if (!result.success) {
 *     console.error("Failed to open side pane:", result.error);
 * }
 * ```
 */
export async function openEventDetailPane(
    params: OpenEventDetailPaneParams
): Promise<SidePaneOpenResult> {
    const { eventId, eventType, onClose } = params;

    logger.info("SidePaneUtils", "Opening Event Detail pane", {
        eventId,
        eventType,
    });

    // Validate eventId
    if (!eventId || eventId.trim() === "") {
        const error = "eventId is required to open the side pane";
        logger.error("SidePaneUtils", error);
        return { success: false, error };
    }

    // Check if sidePanes API is available
    const sidePanes = getSidePanes();
    if (!sidePanes) {
        const error =
            "Xrm.App.sidePanes API is not available. " +
            "This feature requires a model-driven app context.";
        logger.error("SidePaneUtils", error);
        return { success: false, error };
    }

    try {
        // Check for existing pane with same ID
        const existingPane = sidePanes.getPane(EVENT_DETAIL_PANE_ID);

        if (existingPane) {
            // Pane exists - navigate to new event (reuses pane)
            logger.info(
                "SidePaneUtils",
                "Reusing existing pane, navigating to new event"
            );

            const pageInput = buildPageInput(eventId, eventType);
            await existingPane.navigate(pageInput);
            existingPane.select(); // Ensure pane is selected/visible

            return { success: true, paneId: EVENT_DETAIL_PANE_ID };
        } else {
            // Create new pane
            logger.info("SidePaneUtils", "Creating new side pane");

            const newPane = await sidePanes.createPane({
                title: "Event Details",
                paneId: EVENT_DETAIL_PANE_ID,
                canClose: true,
                width: PANE_WIDTH,
                isSelected: true,
            });

            // Navigate to the Custom Page with event parameters
            const pageInput = buildPageInput(eventId, eventType);
            await newPane.navigate(pageInput);

            logger.info("SidePaneUtils", "Side pane created and navigated", {
                paneId: newPane.paneId,
            });

            return { success: true, paneId: newPane.paneId };
        }
    } catch (error) {
        const errorMessage =
            error instanceof Error ? error.message : String(error);
        logger.error("SidePaneUtils", "Failed to open side pane", {
            error: errorMessage,
        });
        return {
            success: false,
            error: `Failed to open side pane: ${errorMessage}`,
        };
    }
}

/**
 * Close the Event Detail Side Pane if it's open.
 *
 * @returns true if pane was closed, false if pane wasn't open or API unavailable
 */
export function closeEventDetailPane(): boolean {
    const sidePanes = getSidePanes();
    if (!sidePanes) {
        logger.warn("SidePaneUtils", "sidePanes API not available for close");
        return false;
    }

    const existingPane = sidePanes.getPane(EVENT_DETAIL_PANE_ID);
    if (existingPane) {
        logger.info("SidePaneUtils", "Closing Event Detail pane");
        existingPane.close();
        return true;
    }

    logger.debug("SidePaneUtils", "No Event Detail pane open to close");
    return false;
}

/**
 * Check if the Event Detail Side Pane is currently open.
 *
 * @returns true if pane is open
 */
export function isEventDetailPaneOpen(): boolean {
    const sidePanes = getSidePanes();
    if (!sidePanes) {
        return false;
    }

    return !!sidePanes.getPane(EVENT_DETAIL_PANE_ID);
}

/**
 * Get the Event Detail pane if it's open.
 *
 * @returns The pane object or undefined if not open
 */
export function getEventDetailPane(): SidePane | undefined {
    const sidePanes = getSidePanes();
    if (!sidePanes) {
        return undefined;
    }

    return sidePanes.getPane(EVENT_DETAIL_PANE_ID);
}
