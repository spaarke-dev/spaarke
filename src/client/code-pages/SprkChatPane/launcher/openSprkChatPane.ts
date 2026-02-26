/**
 * SprkChatPane Side Pane Launcher Script
 *
 * Opens the SprkChatPane Code Page as a Dataverse side pane using
 * Xrm.App.sidePanes.createPane(). Called from ribbon buttons or form events.
 *
 * Deployment: Dataverse web resource sprk_/scripts/openSprkChatPane.js
 * Web Resource Name: sprk_SprkChatPane (HTML Code Page)
 *
 * @version 1.0.0
 * @namespace Spaarke.SprkChat
 *
 * Ribbon Configuration:
 *   Library: sprk_/scripts/openSprkChatPane.js
 *   Function: Spaarke.SprkChat.openPane
 *   CrmParameter: PrimaryControl
 *
 * PH-015-A: Icon uses placeholder data URI - replace when designer provides
 *           final sprk_ai_icon asset.
 */

// ============================================================================
// Type Declarations (Xrm.App.sidePanes subset)
// ============================================================================

/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Xrm: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

/**
 * Options for Xrm.App.sidePanes.createPane()
 */
interface SidePaneCreateOptions {
    /** Unique pane identifier (singleton key) */
    paneId: string;
    /** Display title in pane header */
    title: string;
    /** Icon URL, web resource path, or data URI */
    imageSrc?: string;
    /** Whether user can close the pane */
    canClose: boolean;
    /** Initial width in pixels */
    width: number;
    /** Whether pane is selected (focused) on creation */
    isSelected?: boolean;
    /** Whether to hide the pane header */
    hideHeader?: boolean;
    /** Whether to show a badge indicator */
    badge?: boolean;
}

/**
 * Side pane instance returned by createPane()
 */
interface SidePane {
    paneId: string;
    title?: string;
    close(): void;
    select(): void;
    navigate(pageInput: SidePanePageInput): Promise<void>;
}

/**
 * Page input for pane.navigate() - web resource variant
 */
interface SidePanePageInput {
    pageType: "webresource";
    webresourceName: string;
    data?: string;
}

/**
 * Xrm.App.sidePanes API
 */
interface AppSidePanes {
    state: 0 | 1;
    createPane(options: SidePaneCreateOptions): Promise<SidePane>;
    getPane(paneId: string): SidePane | undefined;
    getSelectedPane(): SidePane | undefined;
    getAllPanes(): SidePane[];
}

// ============================================================================
// Constants
// ============================================================================

/** Deterministic pane ID for singleton behavior */
const SPRK_CHAT_PANE_ID = "sprk-chat-pane";

/** Title displayed in the side pane header */
const SPRK_CHAT_PANE_TITLE = "SprkChat";

/** Web resource name for the SprkChatPane Code Page */
const SPRK_CHAT_WEB_RESOURCE = "sprk_SprkChatPane";

/** Default pane width in pixels */
const SPRK_CHAT_PANE_WIDTH = 400;

/**
 * PH-015-A: Placeholder AI icon (SVG data URI).
 * Replace with final designer-provided sprk_ai_icon web resource reference.
 *
 * This is a simple chat-bubble/AI icon rendered as an inline SVG data URI
 * so it works without deploying a separate image web resource.
 */
const SPRK_CHAT_ICON =
    "data:image/svg+xml;utf8," +
    encodeURIComponent(
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="%230078d4" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
        '<path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>' +
        '<circle cx="9" cy="10" r="1" fill="%230078d4"/>' +
        '<circle cx="12" cy="10" r="1" fill="%230078d4"/>' +
        '<circle cx="15" cy="10" r="1" fill="%230078d4"/>' +
        "</svg>"
    );

/** Log prefix for console output */
const LOG_PREFIX = "[Spaarke.SprkChat]";

// ============================================================================
// Helper Functions
// ============================================================================

/**
 * Get the Xrm.App.sidePanes API, checking parent window for iframe context.
 * @returns The sidePanes API or null if unavailable
 */
function getSidePanesApi(): AppSidePanes | null {
    try {
        // Try current window first
        if (typeof Xrm !== "undefined" && Xrm?.App?.sidePanes) {
            return Xrm.App.sidePanes as AppSidePanes;
        }

        // Try parent window (ribbon scripts may run in iframe context)
        if (window.parent && window.parent !== window) {
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            const parentXrm = (window.parent as any)?.Xrm;
            if (parentXrm?.App?.sidePanes) {
                return parentXrm.App.sidePanes as AppSidePanes;
            }
        }
    } catch (e) {
        console.warn(LOG_PREFIX, "Error accessing Xrm.App.sidePanes:", e);
    }
    return null;
}

/**
 * Strip curly braces from a Dataverse GUID.
 * Dataverse entity.getId() returns GUIDs wrapped in braces: {guid-here}
 *
 * @param guid - GUID string, possibly wrapped in braces
 * @returns Clean GUID without braces, lowercased
 */
function cleanGuid(guid: string): string {
    if (!guid) return "";
    return guid.replace(/[{}]/g, "").toLowerCase();
}

/**
 * Get the current form context's entity type and record ID.
 *
 * Attempts multiple strategies to extract context:
 * 1. From primaryControl (form context passed by ribbon CrmParameter)
 * 2. From Xrm.Page (legacy, but still available in some contexts)
 *
 * @param primaryControl - Form context from ribbon CrmParameter (optional)
 * @returns Object with entityType and entityId (may be empty strings)
 */
function getFormContext(primaryControl?: any): { entityType: string; entityId: string } {
    const result = { entityType: "", entityId: "" };

    try {
        // Strategy 1: Use primaryControl (preferred - passed by ribbon as PrimaryControl)
        if (primaryControl?.data?.entity) {
            result.entityType = primaryControl.data.entity.getEntityName() || "";
            result.entityId = cleanGuid(primaryControl.data.entity.getId() || "");

            if (result.entityType || result.entityId) {
                console.log(LOG_PREFIX, "Context from primaryControl:", result);
                return result;
            }
        }

        // Strategy 2: Xrm.Page (legacy but widely available)
        const xrm = typeof Xrm !== "undefined" ? Xrm : (window.parent as any)?.Xrm;
        if (xrm?.Page?.data?.entity) {
            result.entityType = xrm.Page.data.entity.getEntityName() || "";
            result.entityId = cleanGuid(xrm.Page.data.entity.getId() || "");

            if (result.entityType || result.entityId) {
                console.log(LOG_PREFIX, "Context from Xrm.Page:", result);
                return result;
            }
        }
    } catch (e) {
        console.warn(LOG_PREFIX, "Error reading form context:", e);
    }

    console.log(LOG_PREFIX, "No form context available, opening pane without record context");
    return result;
}

/**
 * Build the data query string for the Code Page web resource.
 * Parameters are read by SprkChatPane/src/index.tsx via URLSearchParams.
 *
 * @param entityType - Dataverse entity logical name
 * @param entityId - Record GUID (clean, no braces)
 * @param playbookId - Optional playbook ID for guided interactions
 * @param sessionId - Optional existing session ID to resume
 * @returns URL-encoded query string
 */
function buildDataParams(
    entityType: string,
    entityId: string,
    playbookId?: string,
    sessionId?: string
): string {
    const params = new URLSearchParams();

    if (entityType) params.set("entityType", entityType);
    if (entityId) params.set("entityId", entityId);
    if (playbookId) params.set("playbookId", playbookId);
    if (sessionId) params.set("sessionId", sessionId);

    return params.toString();
}

// ============================================================================
// Main Launcher Function
// ============================================================================

/**
 * Open the SprkChatPane as a Dataverse side pane.
 *
 * Singleton behavior: If a pane with ID "sprk-chat-pane" already exists,
 * it is selected (brought to focus) and navigated to the current record
 * context. Otherwise a new pane is created.
 *
 * @param primaryControl - Form context from ribbon CrmParameter (optional).
 *   When called from a ribbon button, configure CrmParameter: PrimaryControl
 *   so the form context is passed automatically.
 * @param playbookId - Optional playbook ID to start a guided interaction
 * @param sessionId - Optional session ID to resume an existing chat
 *
 * @example
 * // Ribbon button invocation (global namespace):
 * Spaarke.SprkChat.openPane(primaryControl);
 *
 * // Programmatic with optional params:
 * Spaarke.SprkChat.openPane(primaryControl, "playbook-guid", "session-guid");
 */
async function openSprkChatPane(
    primaryControl?: any,
    playbookId?: string,
    sessionId?: string
): Promise<void> {
    console.log(LOG_PREFIX, "========================================");
    console.log(LOG_PREFIX, "openSprkChatPane: Starting v1.0.0");
    console.log(LOG_PREFIX, "========================================");

    // -------------------------------------------------------------------------
    // Step 1: Check if sidePanes API is available
    // -------------------------------------------------------------------------
    const sidePanes = getSidePanesApi();
    if (!sidePanes) {
        const errorMsg =
            "The SprkChat side pane requires Xrm.App.sidePanes API, " +
            "which is not available in this context. " +
            "Please ensure you are using a supported model-driven app.";
        console.error(LOG_PREFIX, errorMsg);

        // Fallback: Try to show an alert dialog
        try {
            const xrm = typeof Xrm !== "undefined" ? Xrm : (window.parent as any)?.Xrm;
            if (xrm?.Navigation?.openAlertDialog) {
                await xrm.Navigation.openAlertDialog({
                    title: "SprkChat",
                    text: errorMsg,
                });
            }
        } catch (alertError) {
            console.error(LOG_PREFIX, "Could not show alert dialog:", alertError);
        }
        return;
    }

    // -------------------------------------------------------------------------
    // Step 2: Get current form context
    // -------------------------------------------------------------------------
    const { entityType, entityId } = getFormContext(primaryControl);
    const dataParams = buildDataParams(entityType, entityId, playbookId, sessionId);

    console.log(LOG_PREFIX, "entityType:", entityType);
    console.log(LOG_PREFIX, "entityId:", entityId);
    console.log(LOG_PREFIX, "dataParams:", dataParams);

    // -------------------------------------------------------------------------
    // Step 3: Check for existing pane (singleton pattern)
    // -------------------------------------------------------------------------
    try {
        const existingPane = sidePanes.getPane(SPRK_CHAT_PANE_ID);

        if (existingPane) {
            console.log(LOG_PREFIX, "Existing pane found, reusing");

            // Navigate to updated context (in case record changed)
            await existingPane.navigate({
                pageType: "webresource",
                webresourceName: SPRK_CHAT_WEB_RESOURCE,
                data: dataParams,
            });

            // Bring pane to focus
            existingPane.select();

            console.log(LOG_PREFIX, "Existing pane navigated and selected");
            return;
        }

        // ---------------------------------------------------------------------
        // Step 4: Create new pane
        // ---------------------------------------------------------------------
        console.log(LOG_PREFIX, "No existing pane, creating new one");

        const newPane = await sidePanes.createPane({
            paneId: SPRK_CHAT_PANE_ID,
            title: SPRK_CHAT_PANE_TITLE,
            imageSrc: SPRK_CHAT_ICON, // PH-015-A: placeholder icon
            canClose: true,
            width: SPRK_CHAT_PANE_WIDTH,
            isSelected: true,
        });

        // Navigate to the Code Page web resource
        await newPane.navigate({
            pageType: "webresource",
            webresourceName: SPRK_CHAT_WEB_RESOURCE,
            data: dataParams,
        });

        console.log(LOG_PREFIX, "New pane created and navigated successfully");
    } catch (error) {
        const errorMessage = error instanceof Error ? error.message : String(error);
        console.error(LOG_PREFIX, "Error opening SprkChat pane:", errorMessage);

        // Attempt to show user-facing error
        try {
            const xrm = typeof Xrm !== "undefined" ? Xrm : (window.parent as any)?.Xrm;
            if (xrm?.Navigation?.openAlertDialog) {
                await xrm.Navigation.openAlertDialog({
                    title: "SprkChat",
                    text: "Unable to open SprkChat: " + errorMessage,
                });
            }
        } catch (alertError) {
            console.error(LOG_PREFIX, "Could not show error dialog:", alertError);
        }
    }
}

// ============================================================================
// Enable / Visibility Rules (for ribbon configuration)
// ============================================================================

/**
 * Enable rule: SprkChat button is enabled when sidePanes API is available.
 * Ribbon configuration should reference this as an EnableRule.
 *
 * @returns true if button should be enabled
 */
function enableSprkChatPane(): boolean {
    return getSidePanesApi() !== null;
}

/**
 * Visibility rule: Always show the SprkChat button.
 *
 * @returns true to show button
 */
function showSprkChatPane(): boolean {
    return true;
}

// ============================================================================
// Global Namespace Export (Required for Dataverse Ribbon Commands)
// ============================================================================

/**
 * Expose functions on window.Spaarke.SprkChat namespace so they are
 * callable from Dataverse ribbon command definitions.
 *
 * Ribbon XML references:
 *   FunctionName="Spaarke.SprkChat.openPane"
 *   Library="$webresource:sprk_/scripts/openSprkChatPane.js"
 */

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const _window = (typeof window !== "undefined" ? window : globalThis) as any;

_window.Spaarke = _window.Spaarke || {};
_window.Spaarke.SprkChat = _window.Spaarke.SprkChat || {};

_window.Spaarke.SprkChat.openPane = openSprkChatPane;
_window.Spaarke.SprkChat.enable = enableSprkChatPane;
_window.Spaarke.SprkChat.show = showSprkChatPane;

// Also export for module-based consumption (if imported by other TypeScript)
export { openSprkChatPane, enableSprkChatPane, showSprkChatPane };
