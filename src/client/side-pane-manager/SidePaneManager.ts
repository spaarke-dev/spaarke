/// <reference path="types.ts" />
/**
 * Spaarke Side Pane Manager — Core Platform Module
 *
 * Auto-registers side panes in model-driven apps via a hidden Mscrm.GlobalTab
 * ribbon enable rule. The enable rule fires on every page navigation; the
 * initialize() function returns false (button hidden) while registering panes
 * as a side-effect.
 *
 * Deployed as: sprk_SidePaneManager (Dataverse JS web resource)
 * Ribbon ref:  FunctionName="Spaarke.SidePaneManager.initialize"
 *              Library="$webresource:sprk_SidePaneManager"
 *
 * Architecture: docs/architecture/SIDE-PANE-PLATFORM-ARCHITECTURE.md
 *
 * @namespace Spaarke.SidePaneManager
 * @version 1.0.0
 */

// ============================================================================
// Constants
// ============================================================================

const LOG_PREFIX = "[Spaarke.SidePaneManager]";

/** Guard flag — prevents duplicate registration attempts on re-evaluation */
let _initialized = false;

/** Track number of panes successfully registered this session */
let _registeredCount = 0;

// ============================================================================
// Pane Registry — Add new panes here
// ============================================================================

const PANE_REGISTRY: PaneConfig[] = [
    {
        paneId: "sprk-chat",
        title: "SprkChat",
        icon: "WebResources/sprk_SprkChatIcon16.svg",
        webResource: "sprk_SprkChatPane",
        width: 400,
        canClose: false,       // Always present (like Copilot)
        alwaysRender: true,    // Preserves chat state when switching pane tabs
        contextAware: true,    // Detects current entity/record
    },
    // Future panes:
    // {
    //     paneId: "sprk-actions",
    //     title: "Actions",
    //     icon: "WebResources/sprk_ActionsIcon16.svg",
    //     webResource: "sprk_ActionPane",
    //     width: 350,
    //     canClose: true,
    //     alwaysRender: false,
    //     contextAware: true,
    // },
];

// ============================================================================
// Helper Functions
// ============================================================================

/**
 * Get the Xrm.App.sidePanes API, checking parent window for iframe context.
 *
 * Side pane scripts may run in different frame contexts within UCI.
 * Walk: current window → parent → top.
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
 * Get current entity context for context-aware panes.
 *
 * Reads entity type and record ID from the active Dataverse form
 * and returns a URL-encoded data string for pane.navigate().
 *
 * Priority:
 *   1. Xrm.Page.data.entity (available on entity forms)
 *   2. Xrm.Utility.getPageContext() (available on forms, grids, dashboards)
 *   3. Empty string (dashboard/home with no entity context)
 */
function getContextData(): string {
    const params = new URLSearchParams();

    try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = typeof Xrm !== "undefined" ? Xrm : (window.parent as any)?.Xrm;

        // Strategy 1: Xrm.Page.data.entity
        if (xrm?.Page?.data?.entity) {
            const entityType = xrm.Page.data.entity.getEntityName() || "";
            const entityId = (xrm.Page.data.entity.getId() || "")
                .replace(/[{}]/g, "")
                .toLowerCase();

            if (entityType) params.set("entityType", entityType);
            if (entityId) params.set("entityId", entityId);
        }

        // Strategy 2: Xrm.Utility.getPageContext (fallback for grids/dashboards)
        if (!params.has("entityType") && xrm?.Utility?.getPageContext) {
            try {
                const pageCtx = xrm.Utility.getPageContext();
                if (pageCtx?.input?.entityName) {
                    params.set("entityType", pageCtx.input.entityName);
                }
                if (pageCtx?.input?.entityId) {
                    const id = pageCtx.input.entityId
                        .replace(/[{}]/g, "")
                        .toLowerCase();
                    params.set("entityId", id);
                }
            } catch (_e) {
                // getPageContext may not be available in all contexts
            }
        }
    } catch (e) {
        console.warn(LOG_PREFIX, "Error reading entity context:", e);
    }

    return params.toString();
}

/**
 * Register a single pane in the side pane launcher.
 *
 * Async fire-and-forget: errors are logged but don't block other
 * registrations or the enable rule return value.
 */
async function registerPane(
    sidePanes: AppSidePanes,
    config: PaneConfig
): Promise<void> {
    try {
        console.log(LOG_PREFIX, `Creating pane: ${config.title} (${config.paneId})`);

        const pane = await sidePanes.createPane({
            paneId: config.paneId,
            title: config.title,
            imageSrc: config.icon,
            canClose: config.canClose,
            width: config.width,
            isSelected: false,           // Start collapsed — icon visible in launcher
            alwaysRender: config.alwaysRender,
        });

        // Navigate to the Code Page web resource
        const data = config.contextAware ? getContextData() : "";
        await pane.navigate({
            pageType: "webresource",
            webresourceName: config.webResource,
            data: data,
        });

        _registeredCount++;
        console.log(
            LOG_PREFIX,
            `Registered pane: ${config.title} (${config.paneId})`,
            config.contextAware ? `context=[${data}]` : ""
        );
    } catch (error) {
        const msg = error instanceof Error ? error.message : String(error);
        console.warn(LOG_PREFIX, `Failed to register pane ${config.paneId}:`, msg);
        // Don't re-throw — allow other panes to register
    }
}

// ============================================================================
// Main Entry Point
// ============================================================================

/**
 * Initialize the Side Pane Manager.
 *
 * Called by the hidden Mscrm.GlobalTab enable rule on every page navigation.
 * Registers all configured panes that aren't already present in the launcher.
 *
 * @returns false — keeps the hidden ribbon button hidden (never rendered)
 */
function initialize(): boolean {
    // Guard: If all panes already registered, skip silently
    if (_initialized && _registeredCount >= PANE_REGISTRY.length) {
        return false;
    }

    const sidePanes = getSidePanesApi();
    if (!sidePanes) {
        console.log(LOG_PREFIX, "sidePanes API not available yet");
        return false;
    }

    console.log(
        LOG_PREFIX,
        `Initializing v1.0.0 — ${PANE_REGISTRY.length} pane(s) configured`
    );

    let newRegistrations = 0;

    for (const config of PANE_REGISTRY) {
        // Skip if pane is already registered (singleton pattern)
        if (sidePanes.getPane(config.paneId)) {
            continue;
        }

        // Async fire-and-forget — doesn't block enable rule return
        registerPane(sidePanes, config);
        newRegistrations++;
    }

    if (newRegistrations > 0) {
        console.log(LOG_PREFIX, `Registering ${newRegistrations} new pane(s)`);
    } else {
        console.log(
            LOG_PREFIX,
            `All ${PANE_REGISTRY.length} pane(s) already registered`
        );
    }

    _initialized = true;
    return false; // Button stays hidden
}

// ============================================================================
// Global Namespace Export (Required for Dataverse Ribbon Commands)
// ============================================================================

/**
 * Expose initialize() on window.Spaarke.SidePaneManager namespace so it is
 * callable from the Dataverse ribbon enable rule CustomRule definition.
 *
 * Ribbon XML reference:
 *   FunctionName="Spaarke.SidePaneManager.initialize"
 *   Library="$webresource:sprk_SidePaneManager"
 */

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const _window = (typeof window !== "undefined" ? window : globalThis) as any;

_window.Spaarke = _window.Spaarke || {};
_window.Spaarke.SidePaneManager = _window.Spaarke.SidePaneManager || {};

_window.Spaarke.SidePaneManager.initialize = initialize;

// ============================================================================
// Auto-Initialize on Script Load
// ============================================================================

// Call initialize() immediately when the script loads.
// Works for both loading scenarios:
//   - Ribbon enable rule: initialize() runs here, then ribbon also calls it
//     (guard flag prevents duplicate registration)
//   - Code Page injection: initialize() runs here, registering panes
//     without needing an explicit onload callback
initialize();
