/**
 * usePaneCollapse — per-pane collapse/expand state for the SpaarkeAi
 * three-pane shell (Task 094).
 *
 * The operator wants all three SpaarkeAi panes (Assistant / Workspace /
 * Context) to be independently collapsible into narrow vertical strips,
 * the same way SmartToDo's Kanban columns collapse. State persists across
 * browser sessions via localStorage (per-device, per-user) so a collapsed
 * pane stays collapsed after a refresh.
 *
 * Why localStorage (not sessionStorage):
 *   The shell's existing pane *widths* (`spaarke-ai-r2-shell-*-width-px`)
 *   live in sessionStorage because they're a transient working-state knob
 *   (resize during a session, reset on close). Collapse state is a stickier
 *   user preference — the operator expects a collapsed pane to stay
 *   collapsed across browser restarts. Same reasoning as task 092's
 *   `spaarke:workspace:pinned-list` migration from sessionStorage to
 *   localStorage.
 *
 * Why a Set, not three booleans:
 *   Future-proofs against adding more panes (e.g. a 4th "Inspector" strip).
 *   Set-based API mirrors SmartToDo SmartToDo.tsx:338 `collapsedColumns:
 *   ReadonlySet<string>` so the mental model stays consistent across
 *   solutions.
 *
 * Pattern provenance: this is the SpaarkeAi equivalent of SmartToDo's
 * `collapsedColumns` Set state in SmartToDo.tsx (lines 337-352), generalised
 * to localStorage persistence + a typed PaneId union.
 *
 * @see SmartToDo.tsx — original Set-based collapse-state pattern
 * @see KanbanBoard.tsx — click-to-collapse column-header pattern
 * @see ThreePaneShell.tsx — consumer
 * @see PaneHeader.tsx — `onCollapse` prop wired by consumer panes
 */

import { useCallback, useState } from 'react';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Identifier for each of the three SpaarkeAi panes.
 * Keep in sync with the three pane components consumed by ThreePaneShell.
 */
export type PaneId = 'assistant' | 'workspace' | 'context';

/** Public contract returned by usePaneCollapse. */
export interface UsePaneCollapseResult {
  /** Whether the named pane is currently collapsed. */
  isCollapsed: (id: PaneId) => boolean;
  /** Toggle the named pane's collapsed state and persist to localStorage. */
  toggle: (id: PaneId) => void;
  /** Read-only snapshot of currently-collapsed pane ids (debug / a11y). */
  collapsedIds: ReadonlySet<PaneId>;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * localStorage key for the collapse-state snapshot. Namespaced under the
 * `spaarke:` prefix the rest of the SpaarkeAi solution uses for client-side
 * preferences (matches task 092's `spaarke:workspace:pinned-list`).
 *
 * Stored value shape: JSON string array of `PaneId` values, e.g.
 *   '["assistant","context"]'  → Assistant + Context collapsed
 *   '[]'                       → all three panes expanded
 */
const STORAGE_KEY = 'spaarke:panes:collapsed';

// ---------------------------------------------------------------------------
// localStorage helpers — try/catch-wrapped for private browsing / quota
// ---------------------------------------------------------------------------

const VALID_PANE_IDS: ReadonlySet<string> = new Set<PaneId>([
  'assistant',
  'workspace',
  'context',
]);

function readPersistedCollapseSet(): Set<PaneId> {
  if (typeof window === 'undefined') return new Set();
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (raw === null) return new Set();
    const parsed = JSON.parse(raw) as unknown;
    if (!Array.isArray(parsed)) return new Set();
    const next = new Set<PaneId>();
    for (const entry of parsed) {
      if (typeof entry === 'string' && VALID_PANE_IDS.has(entry)) {
        next.add(entry as PaneId);
      }
    }
    return next;
  } catch {
    // Private browsing, quota exceeded, or corrupt JSON → behave like a
    // fresh user (no panes collapsed). Never crash on storage failure.
    return new Set();
  }
}

function writePersistedCollapseSet(next: ReadonlySet<PaneId>): void {
  if (typeof window === 'undefined') return;
  try {
    const arr: PaneId[] = [];
    next.forEach((id) => arr.push(id));
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(arr));
  } catch {
    // Best-effort persistence — same posture as task 092's
    // pinnedWorkspaces.ts: silent no-op on storage failure.
  }
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * usePaneCollapse — React hook returning a typed pane-collapse controller.
 *
 * Initial state is read from localStorage; every `toggle()` updates both the
 * in-memory React state (driving rerender) and the persisted snapshot
 * (driving cold-load restoration).
 *
 * @example
 *   const { isCollapsed, toggle } = usePaneCollapse();
 *   <PaneHeader
 *     title="Assistant"
 *     onCollapse={() => toggle('assistant')}
 *     expanded={!isCollapsed('assistant')}
 *   />
 */
export function usePaneCollapse(): UsePaneCollapseResult {
  const [collapsed, setCollapsed] = useState<Set<PaneId>>(() =>
    readPersistedCollapseSet()
  );

  const isCollapsed = useCallback(
    (id: PaneId): boolean => collapsed.has(id),
    [collapsed]
  );

  const toggle = useCallback((id: PaneId): void => {
    setCollapsed((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      writePersistedCollapseSet(next);
      return next;
    });
  }, []);

  return {
    isCollapsed,
    toggle,
    collapsedIds: collapsed,
  };
}
