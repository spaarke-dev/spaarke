/**
 * Navigation Service for DueDatesWidget
 *
 * Handles navigation actions when users click on event cards:
 * 1. Navigate to Events tab on the current form (if tabs exist)
 * 2. Open the EventDetailSidePane with the clicked event
 *
 * This follows the pattern established in Task 031 (sidePaneUtils.ts) for the
 * UniversalDatasetGrid, but adds tab navigation support specific to
 * entity forms where DueDatesWidget is placed.
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/054-duedateswidget-card-navigation.poml
 * @see FR-01.6: Click card opens Side Pane | Navigate to Events tab, open Side Pane for that Event
 *
 * References:
 * - https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-app-sidepanes
 * - https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/formcontext-ui-tabs
 */

/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Xrm: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Unique pane ID for EventDetailSidePane - must match sidePaneUtils.ts */
const EVENT_DETAIL_PANE_ID = "eventDetailPane";

/** Custom Page name for the Event Detail Side Pane */
const EVENT_DETAIL_PAGE_NAME = "sprk_eventdetailsidepane";

/** Default width for the side pane (per design spec) */
const PANE_WIDTH = 400;

/** Common names for Events tab on entity forms */
const EVENTS_TAB_NAMES = ["events", "Events", "eventsTab", "tab_events", "sprk_events"];

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Parameters for navigating to an event
 */
export interface NavigateToEventParams {
    /** GUID of the Event record to display */
    eventId: string;
    /** GUID of the Event Type lookup (optional) */
    eventType?: string;
    /** Whether to navigate to Events tab first (default: true) */
    navigateToTab?: boolean;
    /** Callback when navigation completes */
    onNavigationComplete?: () => void;
    /** Callback when navigation fails */
    onNavigationError?: (error: string) => void;
}

/**
 * Result of the navigation operation
 */
export interface NavigationResult {
    /** Whether the operation succeeded */
    success: boolean;
    /** Error message if failed */
    error?: string;
    /** Whether tab navigation was performed */
    navigatedToTab: boolean;
    /** Whether side pane was opened */
    openedSidePane: boolean;
}

/**
 * Xrm.App.sidePanes type definitions (subset of full API)
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
    recordId?: string;
}

interface AppSidePanes {
    state: 0 | 1;
    createPane(options: SidePaneCreateOptions): Promise<SidePane>;
    getPane(paneId: string): SidePane | undefined;
    getAllPanes(): SidePane[];
}

/**
 * Form context tab interface (simplified)
 */
interface FormTab {
    getName(): string;
    setFocus(): void;
    setVisible(visible: boolean): void;
    getVisible(): boolean;
}

interface FormContextUi {
    tabs: {
        get(tabName?: string): FormTab | FormTab[];
        getLength(): number;
        forEach(callback: (tab: FormTab, index: number) => void): void;
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Helper Functions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Check if Xrm.App.sidePanes API is available
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
 * Check if form context is available for tab navigation
 */
function isFormContextAvailable(): boolean {
    return !!(
        typeof Xrm !== "undefined" &&
        Xrm.Page &&
        Xrm.Page.ui
    );
}

/**
 * Get form context UI for tab operations
 */
function getFormContextUi(): FormContextUi | null {
    if (!isFormContextAvailable()) {
        return null;
    }
    return Xrm.Page.ui as FormContextUi;
}

/**
 * Build the Custom Page input for side pane navigation
 */
function buildPageInput(eventId: string, eventType?: string): SidePanePageInput {
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

/**
 * Find and focus the Events tab on the current form
 * Returns true if tab was found and focused
 */
function navigateToEventsTab(): boolean {
    const ui = getFormContextUi();
    if (!ui || !ui.tabs) {
        console.log("[NavigationService] Form context not available for tab navigation");
        return false;
    }

    // Try to find the Events tab by common names
    for (const tabName of EVENTS_TAB_NAMES) {
        try {
            const tab = ui.tabs.get(tabName) as FormTab | undefined;
            if (tab && typeof tab.setFocus === "function") {
                // Make sure tab is visible before focusing
                if (typeof tab.getVisible === "function" && !tab.getVisible()) {
                    if (typeof tab.setVisible === "function") {
                        tab.setVisible(true);
                    }
                }
                tab.setFocus();
                console.log(`[NavigationService] Navigated to Events tab: ${tabName}`);
                return true;
            }
        } catch (e) {
            // Tab with this name doesn't exist, try next
            continue;
        }
    }

    // Try to find tab by iterating through all tabs and checking names
    try {
        let foundTabByIteration: FormTab | undefined = undefined;
        ui.tabs.forEach((tab: FormTab) => {
            const name = tab.getName().toLowerCase();
            if (name.includes("event") && foundTabByIteration === undefined) {
                foundTabByIteration = tab;
            }
        });

        if (foundTabByIteration !== undefined) {
            // Type assertion needed because TypeScript doesn't track forEach mutations
            const tabToFocus = foundTabByIteration as FormTab;
            if (typeof tabToFocus.getVisible === "function" && !tabToFocus.getVisible()) {
                if (typeof tabToFocus.setVisible === "function") {
                    tabToFocus.setVisible(true);
                }
            }
            tabToFocus.setFocus();
            console.log(`[NavigationService] Navigated to Events tab (found by iteration)`);
            return true;
        }
    } catch (e) {
        console.log("[NavigationService] Error iterating tabs:", e);
    }

    console.log("[NavigationService] Events tab not found on current form");
    return false;
}

// ─────────────────────────────────────────────────────────────────────────────
// Main Export Functions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Navigate to an event from the DueDatesWidget.
 *
 * This function:
 * 1. Attempts to navigate to the Events tab on the current form (if navigateToTab is true)
 * 2. Opens the EventDetailSidePane with the selected event
 *
 * @param params - Navigation parameters including eventId and options
 * @returns Promise resolving to navigation result
 *
 * @example
 * ```typescript
 * const result = await navigateToEvent({
 *     eventId: "12345678-1234-1234-1234-123456789012",
 *     eventType: "87654321-4321-4321-4321-210987654321",
 *     onNavigationComplete: () => console.log("Navigation complete"),
 *     onNavigationError: (err) => console.error("Navigation failed:", err)
 * });
 * ```
 */
export async function navigateToEvent(
    params: NavigateToEventParams
): Promise<NavigationResult> {
    const {
        eventId,
        eventType,
        navigateToTab = true,
        onNavigationComplete,
        onNavigationError
    } = params;

    const result: NavigationResult = {
        success: false,
        navigatedToTab: false,
        openedSidePane: false
    };

    console.log("[NavigationService] Navigating to event", { eventId, eventType, navigateToTab });

    // Validate eventId
    if (!eventId || eventId.trim() === "") {
        const error = "eventId is required for navigation";
        console.error("[NavigationService]", error);
        result.error = error;
        onNavigationError?.(error);
        return result;
    }

    // Step 1: Navigate to Events tab (if requested and available)
    if (navigateToTab) {
        result.navigatedToTab = navigateToEventsTab();
    }

    // Step 2: Open side pane
    const sidePanes = getSidePanes();
    if (!sidePanes) {
        // If sidePanes API not available, we may be in test harness or custom page
        // Fall back to just the tab navigation result
        const warning = "Xrm.App.sidePanes API is not available. Side pane cannot be opened.";
        console.warn("[NavigationService]", warning);

        // If we at least navigated to the tab, consider partial success
        if (result.navigatedToTab) {
            result.success = true;
            onNavigationComplete?.();
        } else {
            result.error = warning;
            onNavigationError?.(warning);
        }
        return result;
    }

    try {
        // Check for existing pane
        const existingPane = sidePanes.getPane(EVENT_DETAIL_PANE_ID);

        if (existingPane) {
            // Pane exists - navigate to new event (reuses pane)
            console.log("[NavigationService] Reusing existing side pane");

            const pageInput = buildPageInput(eventId, eventType);
            await existingPane.navigate(pageInput);
            existingPane.select();

            result.openedSidePane = true;
            result.success = true;
        } else {
            // Create new pane
            console.log("[NavigationService] Creating new side pane");

            const newPane = await sidePanes.createPane({
                title: "Event Details",
                paneId: EVENT_DETAIL_PANE_ID,
                canClose: true,
                width: PANE_WIDTH,
                isSelected: true,
            });

            const pageInput = buildPageInput(eventId, eventType);
            await newPane.navigate(pageInput);

            result.openedSidePane = true;
            result.success = true;
        }

        console.log("[NavigationService] Navigation complete", result);
        onNavigationComplete?.();

    } catch (error) {
        const errorMessage = error instanceof Error ? error.message : String(error);
        console.error("[NavigationService] Failed to open side pane:", errorMessage);

        result.error = `Failed to open side pane: ${errorMessage}`;

        // If tab navigation worked, still partial success
        if (result.navigatedToTab) {
            result.success = true;
            onNavigationComplete?.();
        } else {
            onNavigationError?.(result.error);
        }
    }

    return result;
}

/**
 * Check if event navigation is available (sidePanes API exists)
 */
export function isNavigationAvailable(): boolean {
    return isSidePanesAvailable();
}

/**
 * Check if tab navigation is available (form context exists)
 */
export function isTabNavigationAvailable(): boolean {
    return isFormContextAvailable();
}

/**
 * Close the Event Detail Side Pane if it's open.
 * @returns true if pane was closed
 */
export function closeEventDetailPane(): boolean {
    const sidePanes = getSidePanes();
    if (!sidePanes) {
        return false;
    }

    const existingPane = sidePanes.getPane(EVENT_DETAIL_PANE_ID);
    if (existingPane) {
        console.log("[NavigationService] Closing Event Detail pane");
        existingPane.close();
        return true;
    }

    return false;
}

/**
 * Check if the Event Detail Side Pane is currently open.
 */
export function isEventDetailPaneOpen(): boolean {
    const sidePanes = getSidePanes();
    if (!sidePanes) {
        return false;
    }

    return !!sidePanes.getPane(EVENT_DETAIL_PANE_ID);
}

/**
 * Parameters for navigating to Events page
 */
export interface NavigateToEventsPageParams {
    /** Callback when navigation completes */
    onNavigationComplete?: () => void;
    /** Callback when navigation fails */
    onNavigationError?: (error: string) => void;
}

/**
 * Navigate to the Events Custom Page (system-level events view).
 * This is used by the "All Events" link in the widget footer.
 *
 * Navigation priority:
 * 1. Try to navigate to Events tab on the current form (best UX)
 * 2. If no tab found, navigate to Events Custom Page
 * 3. Fallback to Events entity list view
 *
 * @param params - Optional callbacks for navigation events
 * @returns Promise resolving to true if navigation succeeded
 *
 * @see FR-01.7: "All Events" link navigates to Events tab
 * @see projects/events-workspace-apps-UX-r1/tasks/055-duedateswidget-all-events-link.poml
 */
export async function navigateToEventsPage(
    params?: NavigateToEventsPageParams
): Promise<boolean> {
    const { onNavigationComplete, onNavigationError } = params || {};

    console.log("[NavigationService] Navigating to Events page/tab");

    // First try to navigate to the Events tab on the current form
    if (navigateToEventsTab()) {
        console.log("[NavigationService] Navigated to Events tab on current form");
        onNavigationComplete?.();
        return true;
    }

    // If no Events tab found, try navigating to the Events Custom Page
    try {
        if (typeof Xrm !== "undefined" && Xrm.Navigation && Xrm.Navigation.navigateTo) {
            console.log("[NavigationService] Navigating to Events Custom Page");
            await Xrm.Navigation.navigateTo({
                pageType: "custom",
                name: "sprk_eventspage"
            });
            onNavigationComplete?.();
            return true;
        }
    } catch (error) {
        console.error("[NavigationService] Failed to navigate to Events page:", error);
    }

    // Fallback: try to navigate to the Events entity list
    try {
        if (typeof Xrm !== "undefined" && Xrm.Navigation && Xrm.Navigation.navigateTo) {
            console.log("[NavigationService] Navigating to Events entity list (fallback)");
            await Xrm.Navigation.navigateTo({
                pageType: "entitylist",
                entityName: "sprk_event"
            });
            onNavigationComplete?.();
            return true;
        }
    } catch (error) {
        console.error("[NavigationService] Failed to navigate to Events entity list:", error);
    }

    const errorMsg = "Unable to navigate to Events page - Xrm.Navigation API not available";
    console.warn("[NavigationService]", errorMsg);
    onNavigationError?.(errorMsg);
    return false;
}
