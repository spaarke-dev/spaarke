/**
 * Context Service for SprkChatPane Code Page
 *
 * Handles four responsibilities:
 *   1. Context detection — entityType + entityId from URL params or Xrm APIs
 *   2. Default playbook resolution — configurable entity-type-to-playbook mapping
 *   3. Session persistence — sessionStorage keyed by pane ID
 *   4. Context-change detection — polling for Xrm navigation changes
 *
 * Context detection priority:
 *   1. URL parameters (entityType, entityId) — set by launcher script
 *   2. Xrm.Page.data.entity — current form's entity type and record ID
 *   3. Xrm.Utility.getPageContext() — alternative context API
 *   4. Graceful fallback with empty context
 *
 * CONSTRAINTS:
 *   - Session persistence MUST use sessionStorage (not localStorage) — scoped to tab lifetime
 *   - Playbook mapping MUST be configurable (object literal, not switch/case)
 *   - Context-change detection uses polling (Xrm has no navigation event API for side panes)
 *   - All Xrm API calls have null checks for graceful degradation outside Dataverse
 *
 * @see ADR-021 - Fluent UI v9 (dialog for context switch uses Fluent components)
 */

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const LOG_PREFIX = "[SprkChatPane:ContextService]";

/**
 * Default polling interval for context-change detection (2 seconds).
 * Side pane persists across form navigation in Dataverse, so we poll
 * the Xrm context to detect when the user navigates to a different record.
 */
const CONTEXT_POLL_INTERVAL_MS = 2_000;

/**
 * Session storage key prefix. Keyed by pane ID for isolation when multiple
 * side panes exist simultaneously.
 */
const SESSION_STORAGE_PREFIX = "sprkchat-session-";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Detected context describing the active Dataverse record.
 */
export interface DetectedContext {
    /** Dataverse entity logical name (e.g., "sprk_matter"). Empty if unknown. */
    entityType: string;
    /** Record GUID (without braces). Empty if unknown. */
    entityId: string;
    /** Resolved playbook ID. Empty if no mapping and no URL param. */
    playbookId: string;
}

/**
 * Persisted session state stored in sessionStorage.
 * Survives pane reloads (Code Page re-mount) within the same browser tab.
 */
export interface PersistedSession {
    /** Active chat session ID from the BFF API. */
    sessionId: string;
    /** Entity type when the session was started. */
    entityType: string;
    /** Entity ID when the session was started. */
    entityId: string;
    /** Playbook ID used for this session. */
    playbookId: string;
    /** ISO timestamp when the session was last updated. */
    timestamp: string;
}

/**
 * Callback for context-change detection.
 * Invoked when the Xrm context differs from the current context.
 */
export type ContextChangeCallback = (
    newContext: DetectedContext,
    previousContext: DetectedContext
) => void;

// ---------------------------------------------------------------------------
// Default Playbook Mapping (Configurable)
// ---------------------------------------------------------------------------

/**
 * Configurable mapping of entity logical names to default playbook IDs.
 *
 * This is an extensible object literal — add new entity-to-playbook mappings
 * by calling setPlaybookMapping() or by extending DEFAULT_PLAYBOOK_MAP.
 *
 * Playbook IDs are string identifiers that the BFF API resolves to
 * full playbook configurations. These default IDs serve as the
 * "out-of-the-box" mapping; they can be overridden at runtime.
 */
const DEFAULT_PLAYBOOK_MAP: Record<string, string> = {
    sprk_matter: "legal-analysis",
    sprk_project: "project-analysis",
    sprk_invoice: "financial-review",
};

/**
 * Fallback playbook when no entity-specific mapping exists.
 */
const FALLBACK_PLAYBOOK_ID = "general-assistant";

/**
 * Runtime playbook mapping. Initialized from DEFAULT_PLAYBOOK_MAP and
 * can be overridden via setPlaybookMapping().
 */
let activePlaybookMap: Record<string, string> = { ...DEFAULT_PLAYBOOK_MAP };

/**
 * Override the default playbook mapping at runtime.
 * Merges with existing mappings (does not replace the entire map).
 *
 * @param mapping - Partial mapping of entity types to playbook IDs.
 */
export function setPlaybookMapping(mapping: Record<string, string>): void {
    activePlaybookMap = { ...activePlaybookMap, ...mapping };
}

/**
 * Get the full current playbook mapping (for testing/debugging).
 */
export function getPlaybookMapping(): Readonly<Record<string, string>> {
    return { ...activePlaybookMap };
}

/**
 * Resolve the default playbook ID for an entity type.
 *
 * @param entityType - Dataverse entity logical name.
 * @returns The mapped playbook ID, or the fallback "general-assistant".
 */
export function resolveDefaultPlaybook(entityType: string): string {
    if (!entityType) return FALLBACK_PLAYBOOK_ID;
    return activePlaybookMap[entityType] ?? FALLBACK_PLAYBOOK_ID;
}

// ---------------------------------------------------------------------------
// Xrm Frame-Walk (reused from authService pattern)
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Walk the frame hierarchy to locate the Xrm SDK.
 * Checks: window -> window.parent -> window.top
 *
 * @returns The Xrm namespace object, or null if not found.
 */
function findXrm(): XrmNamespace | null {
    const frames: Window[] = [window];
    try {
        if (window.parent && window.parent !== window) {
            frames.push(window.parent);
        }
    } catch {
        /* cross-origin */
    }
    try {
        if (window.top && window.top !== window && window.top !== window.parent) {
            frames.push(window.top);
        }
    } catch {
        /* cross-origin */
    }

    for (const frame of frames) {
        try {
            const xrm = (frame as any).Xrm as XrmNamespace | undefined;
            if (xrm?.Utility?.getGlobalContext) {
                return xrm;
            }
        } catch {
            /* cross-origin or property access error */
        }
    }

    return null;
}

/* eslint-enable @typescript-eslint/no-explicit-any */

// ---------------------------------------------------------------------------
// Context Detection
// ---------------------------------------------------------------------------

/**
 * Normalize a Dataverse GUID by removing braces and lowering case.
 * Xrm.Page.data.entity.getId() returns "{GUID}" format.
 */
function normalizeGuid(guid: string): string {
    return guid.replace(/[{}]/g, "").toLowerCase();
}

/**
 * Detect the current entity context from URL parameters.
 *
 * @param params - The unwrapped URL search parameters.
 * @returns Partial context from URL, or null if not available.
 */
function detectContextFromUrl(params: URLSearchParams): { entityType: string; entityId: string } | null {
    const entityType = params.get("entityType") ?? "";
    const entityId = params.get("entityId") ?? "";

    if (entityType && entityId) {
        return { entityType, entityId: normalizeGuid(entityId) };
    }

    return null;
}

/**
 * Detect the current entity context from Xrm.Page.data.entity.
 *
 * @returns Context from Xrm Page API, or null if unavailable.
 */
function detectContextFromXrmPage(): { entityType: string; entityId: string } | null {
    try {
        const xrm = findXrm();
        if (!xrm?.Page?.data?.entity) return null;

        const entity = xrm.Page.data.entity;
        const entityType = entity.getEntityName?.();
        const entityId = entity.getId?.();

        if (entityType && entityId) {
            return { entityType, entityId: normalizeGuid(entityId) };
        }
    } catch {
        console.debug(`${LOG_PREFIX} Xrm.Page.data.entity not available`);
    }
    return null;
}

/**
 * Detect the current entity context from Xrm.Utility.getPageContext().
 *
 * @returns Context from Xrm page context API, or null if unavailable.
 */
function detectContextFromPageContext(): { entityType: string; entityId: string } | null {
    try {
        const xrm = findXrm();
        if (!xrm?.Utility?.getPageContext) return null;

        const pageContext = xrm.Utility.getPageContext();
        const input = pageContext?.input;

        if (input?.entityName && input?.entityId) {
            return {
                entityType: input.entityName,
                entityId: normalizeGuid(input.entityId),
            };
        }
    } catch {
        console.debug(`${LOG_PREFIX} Xrm.Utility.getPageContext() not available`);
    }
    return null;
}

/**
 * Detect the current entity context using the priority chain:
 *   1. URL parameters (set by launcher script)
 *   2. Xrm.Page.data.entity (current form context)
 *   3. Xrm.Utility.getPageContext() (alternative API)
 *   4. Empty fallback (graceful degradation)
 *
 * Also resolves the default playbook based on the detected entity type,
 * unless a playbookId is explicitly provided via URL parameters.
 *
 * @param params - The unwrapped URL search parameters.
 * @returns The fully resolved DetectedContext.
 */
export function detectContext(params: URLSearchParams): DetectedContext {
    // Priority 1: URL parameters
    const urlContext = detectContextFromUrl(params);
    if (urlContext) {
        const playbookFromUrl = params.get("playbookId") ?? "";
        const playbookId = playbookFromUrl || resolveDefaultPlaybook(urlContext.entityType);
        console.info(`${LOG_PREFIX} Context from URL params:`, urlContext.entityType, urlContext.entityId);
        return { ...urlContext, playbookId };
    }

    // Priority 2: Xrm.Page.data.entity
    const xrmPageContext = detectContextFromXrmPage();
    if (xrmPageContext) {
        const playbookId = resolveDefaultPlaybook(xrmPageContext.entityType);
        console.info(`${LOG_PREFIX} Context from Xrm.Page:`, xrmPageContext.entityType, xrmPageContext.entityId);
        return { ...xrmPageContext, playbookId };
    }

    // Priority 3: Xrm.Utility.getPageContext()
    const pageContext = detectContextFromPageContext();
    if (pageContext) {
        const playbookId = resolveDefaultPlaybook(pageContext.entityType);
        console.info(`${LOG_PREFIX} Context from getPageContext():`, pageContext.entityType, pageContext.entityId);
        return { ...pageContext, playbookId };
    }

    // Priority 4: Empty fallback (pane opened without entity context)
    console.warn(`${LOG_PREFIX} No entity context detected — using fallback`);
    return {
        entityType: "",
        entityId: "",
        playbookId: FALLBACK_PLAYBOOK_ID,
    };
}

// ---------------------------------------------------------------------------
// Session Persistence (sessionStorage)
// ---------------------------------------------------------------------------

/**
 * Build the sessionStorage key for a given pane ID.
 * Scoped by pane to support multiple side panes simultaneously.
 */
function getStorageKey(paneId: string): string {
    return `${SESSION_STORAGE_PREFIX}${paneId}`;
}

/**
 * Save session state to sessionStorage.
 * Uses sessionStorage (not localStorage) so state is scoped to the current tab.
 *
 * @param paneId - Unique pane identifier (e.g., "sprkchat" from createPane).
 * @param session - The session state to persist.
 */
export function saveSession(paneId: string, session: PersistedSession): void {
    try {
        const key = getStorageKey(paneId);
        sessionStorage.setItem(key, JSON.stringify(session));
        console.debug(`${LOG_PREFIX} Session saved for pane: ${paneId}`);
    } catch (err) {
        console.warn(`${LOG_PREFIX} Failed to save session to sessionStorage:`, err);
    }
}

/**
 * Restore session state from sessionStorage.
 *
 * @param paneId - Unique pane identifier.
 * @returns The persisted session, or null if not found or corrupted.
 */
export function restoreSession(paneId: string): PersistedSession | null {
    try {
        const key = getStorageKey(paneId);
        const raw = sessionStorage.getItem(key);
        if (!raw) return null;

        const parsed = JSON.parse(raw) as PersistedSession;

        // Validate required fields
        if (!parsed.sessionId || !parsed.entityType || !parsed.entityId) {
            console.warn(`${LOG_PREFIX} Invalid persisted session, discarding`);
            sessionStorage.removeItem(key);
            return null;
        }

        console.debug(`${LOG_PREFIX} Session restored for pane: ${paneId}`);
        return parsed;
    } catch (err) {
        console.warn(`${LOG_PREFIX} Failed to restore session from sessionStorage:`, err);
        return null;
    }
}

/**
 * Clear the persisted session for a pane.
 *
 * @param paneId - Unique pane identifier.
 */
export function clearSession(paneId: string): void {
    try {
        const key = getStorageKey(paneId);
        sessionStorage.removeItem(key);
        console.debug(`${LOG_PREFIX} Session cleared for pane: ${paneId}`);
    } catch (err) {
        console.warn(`${LOG_PREFIX} Failed to clear session from sessionStorage:`, err);
    }
}

// ---------------------------------------------------------------------------
// Context-Change Detection (Polling)
// ---------------------------------------------------------------------------

/**
 * Start polling for context changes.
 *
 * The Dataverse side pane persists across form navigation. There is no
 * event-based API for detecting navigation in the side pane context.
 * Instead, we poll the Xrm context at a fixed interval and compare
 * with the current known context.
 *
 * @param currentContext - The initial/current context to compare against.
 * @param onChange - Callback invoked when a context mismatch is detected.
 * @param intervalMs - Polling interval in milliseconds (default: 2000).
 * @returns A cleanup function that stops the polling.
 */
export function startContextChangeDetection(
    currentContext: DetectedContext,
    onChange: ContextChangeCallback,
    intervalMs: number = CONTEXT_POLL_INTERVAL_MS
): () => void {
    let lastEntityType = currentContext.entityType;
    let lastEntityId = currentContext.entityId;
    let notified = false;

    const intervalId = setInterval(() => {
        // Try Xrm.Page first, then getPageContext
        const xrmPageCtx = detectContextFromXrmPage();
        const pageCtx = xrmPageCtx ?? detectContextFromPageContext();

        if (!pageCtx) {
            // Xrm not available — cannot detect changes
            return;
        }

        const newEntityType = pageCtx.entityType;
        const newEntityId = pageCtx.entityId;

        // Check if context has changed
        const hasChanged =
            (newEntityType !== lastEntityType || newEntityId !== lastEntityId) &&
            newEntityType !== "" &&
            newEntityId !== "";

        if (hasChanged && !notified) {
            notified = true;
            const newPlaybookId = resolveDefaultPlaybook(newEntityType);
            const newContext: DetectedContext = {
                entityType: newEntityType,
                entityId: newEntityId,
                playbookId: newPlaybookId,
            };
            const previousContext: DetectedContext = {
                entityType: lastEntityType,
                entityId: lastEntityId,
                playbookId: resolveDefaultPlaybook(lastEntityType),
            };

            console.info(
                `${LOG_PREFIX} Context change detected:`,
                `${lastEntityType}/${lastEntityId}`,
                "->",
                `${newEntityType}/${newEntityId}`
            );

            onChange(newContext, previousContext);
        } else if (!hasChanged && notified) {
            // User navigated back to the original context — reset notification flag
            notified = false;
        }
    }, intervalMs);

    return () => {
        clearInterval(intervalId);
    };
}

/**
 * Accept a context switch by updating the tracked context.
 * Should be called after the user confirms the context switch in the dialog.
 *
 * @param newContext - The new context to track.
 * @param paneId - Pane ID for session storage update.
 * @param sessionId - Current session ID (empty to start fresh).
 */
export function acceptContextSwitch(
    newContext: DetectedContext,
    paneId: string,
    sessionId: string
): void {
    if (sessionId) {
        saveSession(paneId, {
            sessionId,
            entityType: newContext.entityType,
            entityId: newContext.entityId,
            playbookId: newContext.playbookId,
            timestamp: new Date().toISOString(),
        });
    }
}

/**
 * Get a human-readable display name for an entity type.
 * Used in the context-switch dialog to show friendly labels.
 */
export function getEntityDisplayName(entityType: string): string {
    const displayNames: Record<string, string> = {
        sprk_matter: "Matter",
        sprk_project: "Project",
        sprk_invoice: "Invoice",
        account: "Account",
        contact: "Contact",
        opportunity: "Opportunity",
    };

    if (displayNames[entityType]) {
        return displayNames[entityType];
    }

    // Strip prefix and capitalize: "sprk_something" -> "Something"
    const stripped = entityType.replace(/^[a-z]+_/, "");
    return stripped.charAt(0).toUpperCase() + stripped.slice(1);
}
