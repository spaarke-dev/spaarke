/**
 * useContextTool — persisted Context-pane tool selection (Task 095).
 *
 * The SpaarkeAi Context pane now has a dropdown selector in its PaneHeader
 * rightSlot (mirrors the WorkspacePaneMenu pattern). Selecting a tool
 * persists across browser sessions via localStorage so that modal closes
 * (wizard popups, Semantic Search results modal) return the pane to the
 * user-selected tool instead of going blank.
 *
 * Tool ids:
 *   - 'quick-start'     — GetStartedCardsWidget (default for first-time users)
 *   - 'semantic-search' — SemanticSearchCriteriaTool (in-pane search criteria;
 *                         Search button launches the full sprk_semanticsearch
 *                         Code Page in a popup modal)
 *
 * localStorage key:
 *   `spaarke:context:selected-tool` — JSON string holding one of the ContextToolId
 *   values. Validated against VALID_CONTEXT_TOOL_IDS on every read; corrupt /
 *   unknown values fall back to 'quick-start'.
 *
 * Pattern provenance: this hook mirrors usePaneCollapse.ts (task 094) verbatim
 * in posture — try/catch-wrapped accessors, type-safe validation, silent
 * failure on private-browsing / quota-exceeded.
 *
 * @see usePaneCollapse — same localStorage posture
 * @see pinnedWorkspaces — same try/catch + JSDoc cross-reference style
 * @see ContextPaneController — consumer
 * @see ContextPaneMenu — dropdown UI that calls setSelectedTool
 */

import { useCallback, useState } from 'react';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Identifier for the active Context-pane tool. Add new entries here +
 * VALID_CONTEXT_TOOL_IDS below when new tools land.
 */
export type ContextToolId = 'quick-start' | 'semantic-search';

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
 * localStorage key. Namespaced under the `spaarke:` prefix the rest of the
 * SpaarkeAi solution uses for client-side preferences (matches task 092's
 * `spaarke:workspace:pinned-list` and task 094's `spaarke:panes:collapsed`).
 */
const STORAGE_KEY = 'spaarke:context:selected-tool';

/** Default tool when no persisted value exists (first-time users). */
const DEFAULT_TOOL: ContextToolId = 'quick-start';

const VALID_CONTEXT_TOOL_IDS: ReadonlySet<string> = new Set<ContextToolId>([
  'quick-start',
  'semantic-search',
]);

// ---------------------------------------------------------------------------
// localStorage helpers — try/catch-wrapped for private browsing / quota
// ---------------------------------------------------------------------------

function readPersistedTool(): ContextToolId {
  if (typeof window === 'undefined') return DEFAULT_TOOL;
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (raw === null) return DEFAULT_TOOL;
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
    return DEFAULT_TOOL;
  } catch {
    // Private browsing, quota exceeded, or corrupt JSON → behave like a
    // fresh user. Never crash on storage failure.
    return DEFAULT_TOOL;
  }
}

function writePersistedTool(id: ContextToolId): void {
  if (typeof window === 'undefined') return;
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(id));
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
 * Initial state is read from localStorage; every setSelectedTool() updates
 * both the in-memory React state (driving rerender) and the persisted snapshot
 * (driving cold-load restoration + modal-close restoration).
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
