/**
 * useContextTool — persisted Context-pane tool selection (Task 095 / 099 / 101).
 *
 * The SpaarkeAi Context pane has a dropdown selector in its PaneHeader
 * rightSlot (mirrors the WorkspacePaneMenu pattern). Selecting a tool
 * persists across modal closes (wizard popups, Semantic Search results modal)
 * so the pane returns to the user-selected tool instead of going blank.
 *
 * Tool ids:
 *   - 'quick-start'     — GetStartedCardsWidget (default for first-time users)
 *   - 'semantic-search' — SemanticSearchCriteriaTool (in-pane search criteria;
 *                         Search button launches the full sprk_semanticsearch
 *                         Code Page in a popup modal)
 *
 * Storage split (task 101 — pin persistence fix):
 *   Task 095 originally stored the selected tool in `localStorage`. This
 *   defeated task 099's "pin = default on load" intent: once a user clicked
 *   ANY tool, `selected-tool` was set in localStorage, and the first-mount
 *   logic that honors the pin only fires when `selected-tool` is null — so
 *   subsequent browser refreshes ignored the pin forever.
 *
 *   Fix: split by lifetime.
 *     - sessionStorage `spaarke:context:selected-tool`
 *         Holds the user's within-session active tool. Cleared on browser
 *         close. This still preserves task 095's modal-close-restoration
 *         requirement because Semantic Search results modals open and close
 *         WITHIN the same browser session.
 *     - localStorage `spaarke:context:pinned-tool` (unchanged — task 099
 *         utility `contextToolPin.ts`). Persists the user's pin across
 *         browser sessions.
 *
 *   On cold mount (browser restart):
 *     1. sessionStorage is empty (browser was closed) → fall through.
 *     2. Read pinned tool. If set → use it. (pin is now AUTHORITATIVE on
 *        cold start. Bug A from the operator feedback resolved.)
 *     3. Else default to `'quick-start'`.
 *
 *   On within-session refresh (browser stays open):
 *     1. sessionStorage has the user's last-clicked tool → use it.
 *
 * One-time migration:
 *   Existing users may have a `selected-tool` value in localStorage from
 *   task 095/099 sessions. On first mount per session, we look for it; if
 *   found, we seed sessionStorage with the value and DELETE the legacy
 *   localStorage entry. This avoids stranding existing-user defaults under
 *   the old key without forcing them to re-pick.
 *
 * Pattern provenance: this hook mirrors usePaneCollapse.ts (task 094) verbatim
 * in posture — try/catch-wrapped accessors, type-safe validation, silent
 * failure on private-browsing / quota-exceeded.
 *
 * @see usePaneCollapse — same storage posture
 * @see contextToolPin — pin (localStorage) accessor — UNCHANGED by task 101
 * @see ContextPaneController — consumer
 * @see ContextPaneMenu — dropdown UI that calls setSelectedTool
 */

import { useCallback, useState } from 'react';
import { getPinnedContextTool } from '../services/contextToolPin';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Identifier for the active Context-pane tool. Add new entries here +
 * VALID_CONTEXT_TOOL_IDS below when new tools land.
 */
export type ContextToolId = 'quick-start' | 'semantic-search' | 'pinned-memory';

/** Public contract returned by useContextTool. */
export interface UseContextToolResult {
  /** The currently-selected Context tool. */
  selectedTool: ContextToolId;
  /** Set the active tool + persist to localStorage. */
  setSelectedTool: (id: ContextToolId) => void;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * Storage key for the user's within-session active tool selection. Lives in
 * sessionStorage (task 101) so that browser-close clears it and the pinned
 * default tool is honored on cold start.
 *
 * Namespaced under the `spaarke:` prefix the rest of the SpaarkeAi solution
 * uses for client-side preferences (matches task 092's
 * `spaarke:workspace:pinned-list` and task 094's `spaarke:panes:collapsed`).
 */
const STORAGE_KEY = 'spaarke:context:selected-tool';

/** Default tool when no persisted value exists (first-time users). */
const DEFAULT_TOOL: ContextToolId = 'quick-start';

const VALID_CONTEXT_TOOL_IDS: ReadonlySet<string> = new Set<ContextToolId>([
  'quick-start',
  'semantic-search',
  'pinned-memory',
]);

// ---------------------------------------------------------------------------
// Storage helpers — try/catch-wrapped for private browsing / quota
// (task 101: split between sessionStorage for selection and localStorage for
//  legacy migration. Pin storage lives in `contextToolPin.ts`.)
// ---------------------------------------------------------------------------

/**
 * One-time migration: if a legacy `selected-tool` value lives in localStorage
 * (from task 095/099 sessions), seed sessionStorage with it (so within-session
 * behaviour is preserved for the upgrading user) and DELETE the legacy entry.
 * After the migration runs the legacy key is gone forever, so this is a
 * no-op on subsequent calls.
 *
 * Idempotent + safe against private-browsing / quota errors (any throw
 * short-circuits silently — the user will simply fall through to the pin
 * default / hardcoded default).
 */
function migrateLegacyLocalStorageEntry(): void {
  if (typeof window === 'undefined') return;
  try {
    const legacy = window.localStorage.getItem(STORAGE_KEY);
    if (legacy === null) return;
    // Validate the legacy payload before seeding sessionStorage — we'd
    // rather discard a corrupt entry than propagate it.
    let parsed: unknown;
    try {
      parsed = JSON.parse(legacy);
    } catch {
      parsed = legacy;
    }
    if (
      typeof parsed === 'string' &&
      VALID_CONTEXT_TOOL_IDS.has(parsed) &&
      window.sessionStorage.getItem(STORAGE_KEY) === null
    ) {
      window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify(parsed));
    }
    // Always delete the legacy entry — its presence is the bug we're fixing.
    window.localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Storage unavailable — give up silently. Pin resolution still works.
  }
}

function readPersistedTool(): ContextToolId {
  if (typeof window === 'undefined') return resolveFirstMountDefault();
  // Run the one-time migration before reading sessionStorage so an upgrading
  // user's last selection is preserved within their current session.
  migrateLegacyLocalStorageEntry();
  try {
    const raw = window.sessionStorage.getItem(STORAGE_KEY);
    if (raw === null) {
      // No within-session selection exists — could be a cold start (browser
      // restart) or a brand-new user. Honor the pinned default (task 099) and
      // fall back to the hardcoded `DEFAULT_TOOL`.
      return resolveFirstMountDefault();
    }
    // Stored as a JSON string (matches the rest of the spaarke: namespace);
    // tolerate raw strings too in case an older / hand-edited value exists.
    let parsed: unknown;
    try {
      parsed = JSON.parse(raw);
    } catch {
      parsed = raw;
    }
    if (typeof parsed === 'string' && VALID_CONTEXT_TOOL_IDS.has(parsed)) {
      return parsed as ContextToolId;
    }
    // Corrupt / unknown persisted value — treat as fresh user and honor pin.
    return resolveFirstMountDefault();
  } catch {
    // Private browsing, quota exceeded, or corrupt JSON → behave like a
    // fresh user. Never crash on storage failure.
    return resolveFirstMountDefault();
  }
}

/**
 * First-mount default resolution (task 099): when no within-session
 * `selected-tool` value exists in sessionStorage, fall back to the pinned
 * default before the hardcoded `DEFAULT_TOOL`. The pin is the user's
 * "default on load" preference set via the ContextPaneMenu pin icons.
 * If no pin exists, use the hardcoded default (quick-start) — preserves
 * task 095 behaviour for users who have never pinned anything.
 */
function resolveFirstMountDefault(): ContextToolId {
  const pinned = getPinnedContextTool();
  return pinned ?? DEFAULT_TOOL;
}

function writePersistedTool(id: ContextToolId): void {
  if (typeof window === 'undefined') return;
  try {
    // Task 101: sessionStorage (not localStorage). Cleared on browser close;
    // pin (localStorage) is then authoritative on the next cold mount.
    window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify(id));
  } catch {
    // Best-effort persistence — same posture as task 092's
    // pinnedWorkspaces.ts: silent no-op on storage failure.
  }
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * useContextTool — React hook returning a typed Context-pane tool controller.
 *
 * Initial state is read from sessionStorage (task 101 — was localStorage in
 * task 095; see file header for the rationale); every setSelectedTool()
 * updates both the in-memory React state (driving rerender) and the
 * sessionStorage snapshot (driving within-session modal-close restoration).
 * On cold mount with empty sessionStorage the hook falls back to the pinned
 * default tool (task 099) and finally to the hardcoded DEFAULT_TOOL.
 *
 * @example
 *   const { selectedTool, setSelectedTool } = useContextTool();
 *   return (
 *     <ContextPaneMenu
 *       selectedTool={selectedTool}
 *       onSelectTool={setSelectedTool}
 *     />
 *   );
 */
export function useContextTool(): UseContextToolResult {
  const [selectedTool, setSelectedToolState] = useState<ContextToolId>(() =>
    readPersistedTool()
  );

  const setSelectedTool = useCallback((id: ContextToolId): void => {
    setSelectedToolState(id);
    writePersistedTool(id);
  }, []);

  return {
    selectedTool,
    setSelectedTool,
  };
}
