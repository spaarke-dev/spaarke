/**
 * SmartTodoModal — Card-open modal for the SmartTodo Code Page (R4 task 040).
 *
 * Implements the HYBRID modal pattern (spec FR-13 / FR-16 / FR-17):
 *
 *   Code Page wrapper (this component)
 *     └── Fluent v9 Dialog (modal envelope)
 *         └── RecordNavigationModalShell (chrome: `<` / `>` / "N of M" / actions)
 *             └── <iframe src=…OOB MDA To Do main form…>
 *
 * The iframe loads the OOB MDA "To Do main form" so save / BPF / business rules
 * / "Completed" statuscode control all function NATIVELY — no re-implementation
 * in React (spec MUST-NOT, OD-4 regression-fix).
 *
 * Navigation set:
 *   - Caller supplies `todoIds` (current filter-set order) + `currentId`.
 *   - `<` / `>` advance through the array; "N of M" reflects index + total.
 *   - On nav the iframe `src` rebuilds with the new GUID — the form does NOT
 *     unmount, but the browser loads a fresh document inside the iframe.
 *   - Dirty-check round-trip is delegated to RecordNavigationModalShell (task
 *     041 implements the iframe-side listener; the shell handles the parent
 *     side end-to-end including timeout fallback).
 *
 * Launch context (FR-17):
 *   - In a Code Page (this Code Page) `getClientUrl()` resolves via the
 *     `xrmAccess` helper (Xrm.Utility.getGlobalContext) — works because the
 *     Code Page runs INSIDE Dataverse iframe so the parent Xrm is reachable.
 *   - In MDA context the same `getClientUrl()` succeeds via the host page's
 *     Xrm bindings — no special-case branch needed. The hybrid pattern is
 *     context-agnostic by construction.
 *
 * @see spec.md FR-12, FR-13, FR-14, FR-16, FR-17, NFR-03, NFR-05
 * @see RecordNavigationModalShell/README.md — props + dirty-check protocol
 * @see utils/xrmAccess.ts — getClientUrl() implementation (no hardcoded host)
 */

import * as React from 'react';
import {
  Button,
  Dialog,
  DialogSurface,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Dismiss20Regular } from '@fluentui/react-icons';
import { RecordNavigationModalShell } from '@spaarke/ui-components';
import { buildTodoIframeUrl } from './buildTodoIframeUrl';
import { getClientUrl } from '../../utils/xrmAccess';
import type { ITodo } from '../../types/entities';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * Dialog surface — sized to maximise iframe area (NFR-05 < 500ms perceived
   * nav latency hinges on giving the form room so it doesn't reflow on swap).
   * Uses viewport-relative sizing so the modal scales with the host window.
   */
  surface: {
    width: '90vw',
    maxWidth: '1400px',
    height: '85vh',
    maxHeight: '900px',
    padding: 0,
    overflow: 'hidden',
    display: 'flex',
    flexDirection: 'column',
  },
  /**
   * Iframe fills the shell's content slot. `border: none` removes the default
   * browser border so the OOB form sits flush.
   */
  iframe: {
    width: '100%',
    height: '100%',
    border: 'none',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  /**
   * Loading-state fallback shown briefly between iframe `src` swaps. Keeps
   * the modal at full height so the shell chrome doesn't reflow during nav.
   */
  iframeLoading: {
    visibility: 'hidden',
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/**
 * Subset of `ITodo` consumed by this modal — only `sprk_todoid` (for nav) and
 * `sprk_name` (for the title bar). Reducing the surface area lets callers pass
 * either full `ITodo` objects or a lightweight derived projection.
 */
export interface SmartTodoModalRecord {
  sprk_todoid: string;
  sprk_name?: string;
}

export interface SmartTodoModalProps {
  /**
   * The ordered set of to-do IDs representing the current filter set (search +
   * facets applied). The modal uses this for `<` / `>` nav and "N of M".
   * MUST contain `currentId`; otherwise the modal renders "0 of 0" and
   * disables nav (defensive fallback — caller bug).
   */
  todos: ReadonlyArray<SmartTodoModalRecord>;
  /**
   * GUID of the to-do currently displayed in the iframe.
   */
  currentId: string;
  /**
   * Invoked when the user navigates to a sibling record. Caller updates its
   * `currentId` state which re-renders this modal with the new iframe `src`.
   */
  onNavigateToId: (nextId: string) => void;
  /**
   * Invoked when the user closes the modal (X button, ESC, or backdrop click).
   */
  onClose: () => void;
  /**
   * Optional override of the resolved client URL. Provided for testing — in
   * production the modal falls back to `getClientUrl()` from `xrmAccess`.
   */
  clientUrlOverride?: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SmartTodoModal: React.FC<SmartTodoModalProps> = ({
  todos,
  currentId,
  onNavigateToId,
  onClose,
  clientUrlOverride,
}) => {
  const styles = useStyles();

  // ── Derive nav set + index ──────────────────────────────────────────────
  // Defensive: if `currentId` isn't in `todos`, `indexOf` returns -1 which we
  // surface as "0 of 0" via `RecordNavigationModalShell`'s defensive counter.
  const currentIndex = React.useMemo(
    () => todos.findIndex((t) => t.sprk_todoid === currentId),
    [todos, currentId],
  );
  const navTotal = todos.length;
  const safeIndex = currentIndex >= 0 ? currentIndex : 0;

  // ── Resolve title from current record (best-effort) ─────────────────────
  const title = React.useMemo(() => {
    const rec = todos[currentIndex];
    const name = rec?.sprk_name;
    return name && name.length > 0 ? name : 'To Do';
  }, [todos, currentIndex]);

  // ── Resolve iframe URL ──────────────────────────────────────────────────
  // The client URL is resolved ONCE per mount — Dataverse client URLs do not
  // change within a session. Errors (e.g. Xrm not reachable) surface as an
  // explicit message inside the iframe slot, not a thrown render error.
  const clientUrl = React.useMemo(
    () => clientUrlOverride ?? getClientUrl(),
    [clientUrlOverride],
  );

  const iframeSrc = React.useMemo(() => {
    if (!clientUrl) return null;
    if (!currentId) return null;
    try {
      return buildTodoIframeUrl({ clientUrl, todoId: currentId });
    } catch {
      return null;
    }
  }, [clientUrl, currentId]);

  // ── Track iframe contentWindow for dirty-check (task 041 wires the
  //    listener; for task 040 we plumb the window reference so the shell
  //    is ready to use it the moment 041 lands).
  const [iframeWindow, setIframeWindow] = React.useState<Window | null>(null);
  const iframeRef = React.useCallback((node: HTMLIFrameElement | null) => {
    setIframeWindow(node?.contentWindow ?? null);
  }, []);

  // ── Iframe load state — used to hide the iframe momentarily between
  //    `src` swaps so the previous form's chrome doesn't flash before the
  //    next form loads. NFR-05 perceived-latency optimisation.
  const [iframeLoading, setIframeLoading] = React.useState<boolean>(false);
  React.useEffect(() => {
    setIframeLoading(true);
  }, [iframeSrc]);

  // ── Navigation handler ──────────────────────────────────────────────────
  // Shell guarantees `direction` is 'prev' | 'next' and is only invoked when
  // the corresponding affordance is enabled (i.e. within bounds). Still
  // defensive-guard for index drift.
  const handleNavigate = React.useCallback(
    (direction: 'prev' | 'next') => {
      const idx = todos.findIndex((t) => t.sprk_todoid === currentId);
      if (idx < 0) return;
      const next = direction === 'next' ? idx + 1 : idx - 1;
      if (next < 0 || next >= todos.length) return;
      const nextId = todos[next].sprk_todoid;
      onNavigateToId(nextId);
    },
    [todos, currentId, onNavigateToId],
  );

  // ── Render ──────────────────────────────────────────────────────────────
  return (
    <Dialog
      open
      modalType="modal"
      onOpenChange={(_, data) => {
        if (!data.open) onClose();
      }}
    >
      <DialogSurface className={styles.surface} aria-label="Edit To Do">
        <RecordNavigationModalShell
          currentIndex={safeIndex}
          navigationTotal={navTotal}
          onNavigate={handleNavigate}
          title={title}
          dirtyCheckTargetWindow={iframeWindow}
          actionBar={
            <Button
              appearance="subtle"
              icon={<Dismiss20Regular />}
              aria-label="Close modal"
              onClick={onClose}
            />
          }
          data-testid="smart-todo-modal-shell"
        >
          {iframeSrc ? (
            <iframe
              ref={iframeRef}
              src={iframeSrc}
              className={
                iframeLoading
                  ? `${styles.iframe} ${styles.iframeLoading}`
                  : styles.iframe
              }
              onLoad={() => setIframeLoading(false)}
              title={`To Do ${safeIndex + 1} of ${navTotal}`}
              // Allow the iframe to navigate same-origin Dataverse links
              // without being blocked. The OOB form needs `allow-scripts`
              // + `allow-same-origin` + `allow-forms` + `allow-popups` for
              // save / BPF / lookup pop-ups respectively.
              sandbox="allow-scripts allow-same-origin allow-forms allow-popups allow-modals allow-downloads"
            />
          ) : (
            <div
              role="alert"
              style={{
                padding: '16px',
                color: tokens.colorPaletteRedForeground1,
              }}
            >
              Unable to resolve the Dataverse client URL. Open this modal from
              within an MDA app or the SmartTodo Code Page so the To Do form
              host can be located.
            </div>
          )}
        </RecordNavigationModalShell>
      </DialogSurface>
    </Dialog>
  );
};

SmartTodoModal.displayName = 'SmartTodoModal';

// ---------------------------------------------------------------------------
// Convenience adapter for callers that already have `ITodo[]`
// ---------------------------------------------------------------------------

/**
 * Project an `ITodo[]` down to the minimal record shape this modal needs.
 * Lets callers (e.g. SmartTodoApp) pass the existing context items array
 * without rebuilding a separate projection inline.
 */
export function todosToModalRecords(
  items: ReadonlyArray<ITodo>,
): SmartTodoModalRecord[] {
  return items.map((t) => ({
    sprk_todoid: t.sprk_todoid,
    sprk_name: t.sprk_name,
  }));
}
