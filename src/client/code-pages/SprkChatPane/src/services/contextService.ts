/**
 * Context Service for SprkChatPane Code Page
 *
 * Handles four responsibilities:
 *   1. Context detection — entityType + entityId from URL params or Xrm APIs
 *   2. Dynamic playbook resolution — API-driven entity-type-to-playbook mapping with sessionStorage cache
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
 *   - Playbook mapping MUST come from API (no hardcoded GUIDs); cached in sessionStorage with 5-min TTL
 *   - Context-change detection uses polling (Xrm has no navigation event API for side panes)
 *   - All Xrm API calls have null checks for graceful degradation outside Dataverse
 *
 * @see ADR-021 - Fluent UI v9 (dialog for context switch uses Fluent components)
 */

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const LOG_PREFIX = '[SprkChatPane:ContextService]';

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
const SESSION_STORAGE_PREFIX = 'sprkchat-session-';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Detected context describing the active Dataverse record.
 */
/** Page type describing the Dataverse page where SprkChat is embedded, using native Dataverse values. */
export type PageType = 'entityrecord' | 'entitylist' | 'dashboard' | 'webresource' | 'custom' | 'unknown';

export interface DetectedContext {
  /** Dataverse entity logical name (e.g., "sprk_matter"). Empty if unknown. */
  entityType: string;
  /** Record GUID (without braces). Empty if unknown. */
  entityId: string;
  /** Resolved playbook ID. Empty if no mapping and no URL param. */
  playbookId: string;
  /** Detected page type using native Dataverse values (entityrecord, entitylist, dashboard, webresource, custom, or unknown). */
  pageType: PageType;
  // ---- Analysis launch context (task 002) ----
  /** Analysis type identifier (e.g. 'patent-claims', 'contract-review'). Empty if not set. */
  analysisType: string;
  /** Matter type from the related matter record. Empty if not set. */
  matterType: string;
  /** Practice area from the analysis or matter. Empty if not set. */
  practiceArea: string;
  /** sprk_analysisoutput record ID (GUID without braces). Empty if not set. */
  analysisId: string;
  /** Source SPE file ID associated with the analysis. Empty if not set. */
  sourceFileId: string;
  /** SPE container ID for the source document. Empty if not set. */
  sourceContainerId: string;
  /** Interaction mode: 'analysis' for contextual workspace, 'general' for generic chat. Empty if not set. */
  mode: 'analysis' | 'general' | '';
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
export type ContextChangeCallback = (newContext: DetectedContext, previousContext: DetectedContext) => void;

// ---------------------------------------------------------------------------
// Dynamic Context Mapping (API-driven)
// ---------------------------------------------------------------------------

/**
 * Cache TTL for context mapping responses in sessionStorage (5 minutes).
 */
const CONTEXT_MAPPING_CACHE_TTL_MS = 5 * 60 * 1000;

/**
 * SessionStorage key prefix for context mapping cache entries.
 */
const CONTEXT_MAPPING_CACHE_PREFIX = 'sprkchat-context-';

/**
 * Response shape from GET /api/ai/chat/context-mappings.
 */
export interface ContextMappingResponse {
  /** The default playbook for this entity type + page type combination, or null if none configured. */
  defaultPlaybook: { id: string; name: string; description?: string } | null;
  /** All available playbooks the user can switch to. */
  availablePlaybooks: Array<{ id: string; name: string; description?: string }>;
}

/**
 * Resolve the context mapping (default playbook + available playbooks) from the BFF API.
 *
 * Uses sessionStorage caching with a 5-minute TTL to avoid repeated API calls
 * within the same tab session.
 *
 * CONSTRAINTS:
 *   - MUST NOT block pane rendering (caller handles the async result)
 *   - MUST cache in sessionStorage with 5-min TTL
 *   - MUST handle API errors gracefully (returns empty/null defaults)
 *
 * @param entityType - Dataverse entity logical name (e.g., "sprk_matter").
 * @param pageType - Detected page type (form, list, dashboard, workspace, unknown).
 * @param apiBaseUrl - BFF API base URL.
 * @param accessToken - Bearer token for API authentication.
 * @returns The resolved context mapping with default and available playbooks.
 */
export async function resolveContextMapping(
  entityType: string,
  pageType: string,
  apiBaseUrl: string,
  accessToken: string
): Promise<ContextMappingResponse> {
  // 1. Check sessionStorage cache
  const cacheKey = `${CONTEXT_MAPPING_CACHE_PREFIX}${entityType}-${pageType}`;
  try {
    const cached = sessionStorage.getItem(cacheKey);
    if (cached) {
      const { data, timestamp } = JSON.parse(cached) as { data: ContextMappingResponse; timestamp: number };
      if (Date.now() - timestamp < CONTEXT_MAPPING_CACHE_TTL_MS) {
        console.debug(`${LOG_PREFIX} Context mapping cache hit: ${cacheKey}`);
        return data;
      }
      // Expired — remove stale entry
      sessionStorage.removeItem(cacheKey);
    }
  } catch {
    // Corrupted cache entry — continue to API call
    console.debug(`${LOG_PREFIX} Context mapping cache read failed, fetching from API`);
  }

  // 2. Call API
  // Normalize: strip trailing slashes and trailing /api to prevent double /api/api/ prefix.
  // The SprkChat PCF control stores bffBaseUrl as "https://host/api" but all route
  // constants below already include the /api prefix.
  const normalizedBaseUrl = apiBaseUrl.replace(/\/+$/, '').replace(/\/api\/?$/, '');
  const url = `${normalizedBaseUrl}/api/ai/chat/context-mappings?entityType=${encodeURIComponent(entityType)}&pageType=${encodeURIComponent(pageType)}`;
  try {
    // Extract tenant ID from JWT for X-Tenant-Id header fallback (BFF requires tid claim or header)
    let tenantId: string | null = null;
    try {
      const parts = accessToken.split('.');
      if (parts.length === 3) {
        const payload = JSON.parse(atob(parts[1]));
        tenantId = payload.tid || null;
      }
    } catch { /* ignore JWT parse errors */ }

    const response = await fetch(url, {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        ...(tenantId ? { 'X-Tenant-Id': tenantId } : {}),
      },
    });

    if (!response.ok) {
      console.warn(`${LOG_PREFIX} Context mapping API failed: ${response.status} ${response.statusText}`);
      return { defaultPlaybook: null, availablePlaybooks: [] };
    }

    const data: ContextMappingResponse = await response.json();

    // 3. Cache in sessionStorage
    try {
      sessionStorage.setItem(cacheKey, JSON.stringify({ data, timestamp: Date.now() }));
      console.debug(`${LOG_PREFIX} Context mapping cached: ${cacheKey}`);
    } catch {
      console.debug(`${LOG_PREFIX} Failed to cache context mapping (sessionStorage full?)`);
    }

    return data;
  } catch (err) {
    console.warn(`${LOG_PREFIX} Context mapping fetch error:`, err);
    return { defaultPlaybook: null, availablePlaybooks: [] };
  }
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
  return guid.replace(/[{}]/g, '').toLowerCase();
}

/**
 * Detect the current entity context from URL parameters.
 *
 * @param params - The unwrapped URL search parameters.
 * @returns Partial context from URL, or null if not available.
 */
function detectContextFromUrl(params: URLSearchParams): { entityType: string; entityId: string } | null {
  const entityType = params.get('entityType') ?? '';
  const entityId = params.get('entityId') ?? '';

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
function detectContextFromXrmPage(): {
  entityType: string;
  entityId: string;
} | null {
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
function detectContextFromPageContext(): {
  entityType: string;
  entityId: string;
} | null {
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

// ---------------------------------------------------------------------------
// Page Type Detection
// ---------------------------------------------------------------------------

/**
 * Allowlist of known workspace web resource names.
 * Used for workspace detection via URL matching.
 * Add new workspace web resource names here as they are created.
 */
const WORKSPACE_ALLOWLIST = [
  'sprk_corporateworkspace',
  'sprk_legalworkspace',
  'sprk_analysisworkspace',
];

/**
 * Detect the type of Dataverse page where SprkChat is embedded.
 *
 * Detection priority:
 *   1. Xrm.Utility.getPageContext().input.pageType — primary Xrm API
 *   2. Workspace allowlist — check if URL contains a known workspace web resource
 *   3. URL pattern matching — fallback heuristic
 *   4. "unknown" — safe default on any error
 *
 * CONSTRAINTS (ADR-006):
 *   - Returns a constrained union type, never throws
 *   - Uses explicit allowlist for workspace detection (not broad sprk_ prefix matching)
 *   - Xrm.Utility.getPageContext() is the primary detection method
 *
 * @returns The detected page type.
 */
export function detectPageType(): PageType {
  try {
    // 1. Primary: Xrm.Utility.getPageContext()
    const xrm = findXrm();
    const pageContext = xrm?.Utility?.getPageContext?.();
    if (pageContext?.input?.pageType) {
      const pt = pageContext.input.pageType;
      // Return native Dataverse values directly
      if (pt === 'entityrecord') return 'entityrecord';
      if (pt === 'entitylist') return 'entitylist';
      if (pt === 'dashboard') return 'dashboard';
      if (pt === 'webresource') return 'webresource';
      if (pt === 'custom') return 'custom';
    }

    // 2. Workspace allowlist — check if current URL contains a known workspace web resource
    const url = window.location.href.toLowerCase();
    if (WORKSPACE_ALLOWLIST.some((ws) => url.includes(ws))) return 'webresource';

    // 3. Fallback: URL pattern matching
    if (url.includes('entityrecord')) return 'entityrecord';
    if (url.includes('entitylist')) return 'entitylist';
    if (url.includes('dashboard')) return 'dashboard';

    return 'unknown';
  } catch {
    return 'unknown';
  }
}

/**
 * Detect the current entity context using the priority chain:
 *   1. URL parameters (set by launcher script)
 *   2. Xrm.Page.data.entity (current form context)
 *   3. Xrm.Utility.getPageContext() (alternative API)
 *   4. Empty fallback (graceful degradation)
 *
 * Playbook resolution is NOT done here — it happens asynchronously via
 * resolveContextMapping() after authentication is ready. The returned
 * DetectedContext has playbookId set to the URL parameter value (if provided)
 * or empty string (to be resolved async by the caller).
 *
 * @param params - The unwrapped URL search parameters.
 * @returns The detected context with playbookId empty unless explicitly provided via URL.
 */
export function detectContext(params: URLSearchParams): DetectedContext {
  // Detect page type once (shared across all return paths)
  const pageType = detectPageType();

  // ---- Parse analysis launch context params (task 002) ----
  // Present on all return paths when launched from AnalysisWorkspace.
  // Uses empty string as the absent sentinel (consistent with entityType/entityId).
  const analysisLaunchContext = {
    analysisType: params.get('analysisType') ?? '',
    matterType: params.get('matterType') ?? '',
    practiceArea: params.get('practiceArea') ?? '',
    analysisId: params.get('analysisId') ?? '',
    sourceFileId: params.get('sourceFileId') ?? '',
    sourceContainerId: params.get('sourceContainerId') ?? '',
    mode: (params.get('mode') ?? '') as 'analysis' | 'general' | '',
  };

  // Priority 1: URL parameters
  const urlContext = detectContextFromUrl(params);
  if (urlContext) {
    const playbookFromUrl = params.get('playbookId') ?? '';
    console.info(`${LOG_PREFIX} Context from URL params:`, urlContext.entityType, urlContext.entityId, `[${pageType}]`);
    return { ...urlContext, playbookId: playbookFromUrl, pageType, ...analysisLaunchContext };
  }

  // Priority 2: Xrm.Page.data.entity
  const xrmPageContext = detectContextFromXrmPage();
  if (xrmPageContext) {
    console.info(`${LOG_PREFIX} Context from Xrm.Page:`, xrmPageContext.entityType, xrmPageContext.entityId, `[${pageType}]`);
    return { ...xrmPageContext, playbookId: '', pageType, ...analysisLaunchContext };
  }

  // Priority 3: Xrm.Utility.getPageContext()
  const pageContext = detectContextFromPageContext();
  if (pageContext) {
    console.info(`${LOG_PREFIX} Context from getPageContext():`, pageContext.entityType, pageContext.entityId, `[${pageType}]`);
    return { ...pageContext, playbookId: '', pageType, ...analysisLaunchContext };
  }

  // Priority 4: Empty fallback (pane opened without entity context)
  console.warn(`${LOG_PREFIX} No entity context detected — using fallback`);
  return {
    entityType: '',
    entityId: '',
    playbookId: '',
    pageType,
    ...analysisLaunchContext,
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
      (newEntityType !== lastEntityType || newEntityId !== lastEntityId) && newEntityType !== '' && newEntityId !== '';

    if (hasChanged && !notified) {
      notified = true;
      const currentPageType = detectPageType();
      // Analysis launch context fields are empty on navigation-triggered context changes
      // (those fields are set only when launched explicitly from AnalysisWorkspace via openSprkChatPane)
      const emptyAnalysisCtx = {
        analysisType: '',
        matterType: '',
        practiceArea: '',
        analysisId: '',
        sourceFileId: '',
        sourceContainerId: '',
        mode: '' as const,
      };
      const newContext: DetectedContext = {
        entityType: newEntityType,
        entityId: newEntityId,
        playbookId: '', // Resolved async via resolveContextMapping() by caller
        pageType: currentPageType,
        ...emptyAnalysisCtx,
      };
      const previousContext: DetectedContext = {
        entityType: lastEntityType,
        entityId: lastEntityId,
        playbookId: '', // Previous playbook no longer relevant during switch
        pageType: currentPageType,
        ...emptyAnalysisCtx,
      };

      console.info(
        `${LOG_PREFIX} Context change detected:`,
        `${lastEntityType}/${lastEntityId}`,
        '->',
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
export function acceptContextSwitch(newContext: DetectedContext, paneId: string, sessionId: string): void {
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
    sprk_matter: 'Matter',
    sprk_project: 'Project',
    sprk_invoice: 'Invoice',
    account: 'Account',
    contact: 'Contact',
    opportunity: 'Opportunity',
  };

  if (displayNames[entityType]) {
    return displayNames[entityType];
  }

  // Strip prefix and capitalize: "sprk_something" -> "Something"
  const stripped = entityType.replace(/^[a-z]+_/, '');
  return stripped.charAt(0).toUpperCase() + stripped.slice(1);
}
