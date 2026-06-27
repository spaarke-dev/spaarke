/**
 * ToolbarActions — Selection-aware toolbar action handlers for SmartTodo.
 *
 * Implements the 4 actions consumed by `<SelectionAwareToolbar>` (task 012)
 * from the SmartTodo Code Page header Row 4:
 *
 *   - Open   — dispatches a CustomEvent `sprk-smarttodo:open-todos` with the
 *              selected `sprk_todo` ids. Task 040 will subscribe to this event
 *              and route to `<RecordNavigationModalShell>` + To Do main form
 *              iframe. Until 040 lands the event is consumed by `console.info`
 *              so smoke tests still see action wiring.
 *
 *   - Delete — confirms via `window.confirm`, then calls
 *              `Xrm.WebApi.deleteRecord('sprk_todo', id)` for each selected
 *              record in parallel. Refreshes the kanban via the supplied
 *              `onAfterMutate` callback.
 *
 *   - Email  — composes a `mailto:` URL with subject "To-Dos: N selected" and a
 *              body containing each todo's name + due-date (if any). Opens via
 *              `window.location.href` per platform convention (no popup blocker
 *              issues since user-initiated).
 *
 *   - Pin    — toggles `sprk_todopinned`. Strategy: if ANY selected record is
 *              currently unpinned, the action PINS all (so a mixed-state set
 *              promotes to a fully-pinned set). If every selected record is
 *              already pinned, the action UNPINS all. Persists via
 *              `Xrm.WebApi.updateRecord('sprk_todo', id, { sprk_todopinned })`.
 *
 * **Design notes**:
 *   - This module is a pure function factory — no React state. Lifts behaviour
 *     out of `SmartTodoApp.tsx` so the 4 handlers can be unit-tested by mocking
 *     the injected `webApi` adapter (Xrm.WebApi.*) — see ITodoActionWebApi.
 *   - All handlers return a `Promise<ActionResult>` so the caller (a React
 *     component) can surface success/failure consistently (toast in a future
 *     task; for now `console.info` / `console.error`).
 *   - We DO NOT touch statuscode here. `sprk_todopinned` is independent of
 *     status per spec (FR-08 + R3 entity schema) — verified by the R3 task
 *     `updateEventPinned` already separating them in `DataverseService.ts`.
 *
 * **Data access policy** (per `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`):
 *   Use `Xrm.WebApi` directly from this Code Page host. No BFF involvement —
 *   these are simple per-record CRUD operations against the user's own dataset
 *   in the host security context. (BFF would only be used for cross-record
 *   aggregations, batch transactions, or AI features.)
 *
 * @see ADR-021 Fluent UI v9 design system (component callers only)
 * @see ADR-012 Shared component library (callers consume `SelectionAwareToolbar`)
 * @see smart-todo-r4 spec FR-08 (Open / Delete / Email / Pin actions)
 * @see smart-todo-r4 spec NFR-09 (multi-environment portable — no hardcoded URLs)
 */

import type { ITodo } from '../../types/entities';

// ---------------------------------------------------------------------------
// Minimal Xrm.WebApi surface
// ---------------------------------------------------------------------------

/**
 * Minimal `Xrm.WebApi` shape consumed by these actions. Keeping the surface
 * narrow lets us mock the adapter with a plain object in unit tests instead of
 * stubbing the entire `Xrm` global. Mirrors the pattern in
 * `services/DataverseService.ts`.
 */
export interface ITodoActionWebApi {
  /** DELETE sprk_todos({id}) */
  deleteRecord: (entityName: string, id: string) => Promise<unknown>;
  /** PATCH sprk_todos({id}) with the supplied partial record. */
  updateRecord: (
    entityName: string,
    id: string,
    data: Record<string, unknown>,
  ) => Promise<unknown>;
}

// ---------------------------------------------------------------------------
// Outcome shape
// ---------------------------------------------------------------------------

/**
 * Per-action result returned by every handler. Success/failure is reported as
 * `succeeded` + `failed` counts so the caller can surface a "3 of 4 deleted"
 * style toast in a future task.
 */
export interface ActionResult {
  /** Number of records the action successfully processed. */
  succeeded: number;
  /** Number of records the action failed on. */
  failed: number;
  /** Optional human-readable message (errors join here for debugging). */
  message?: string;
}

// ---------------------------------------------------------------------------
// Selection resolution helper
// ---------------------------------------------------------------------------

/**
 * Given a selection Set and the current `items` list, return the matching
 * ITodo records. Skips selection ids that aren't in `items` (defensively
 * tolerates stale selection after a refetch).
 */
export function resolveSelectedTodos(
  selectedIds: ReadonlySet<string>,
  items: readonly ITodo[],
): ITodo[] {
  if (selectedIds.size === 0) return [];
  return items.filter(item => selectedIds.has(item.sprk_todoid));
}

// ---------------------------------------------------------------------------
// Custom event name (Open action — task 040 subscriber)
// ---------------------------------------------------------------------------

/**
 * Event name dispatched by the Open action. Task 040 (modal-shell wire-up)
 * will subscribe to this event on `window` and route to
 * `<RecordNavigationModalShell>` + To Do main form iframe. Until then a
 * default listener in `bindToolbarActions` consumes the event so the toolbar
 * stays wired during smoke testing.
 */
export const OPEN_TODOS_EVENT = 'sprk-smarttodo:open-todos';

/**
 * Strongly-typed payload dispatched on `OPEN_TODOS_EVENT`.
 *
 *   - `selectedIds` lists the `sprk_todoid` GUIDs (deduplicated, original
 *     selection order preserved).
 *   - `firstId` is a convenience alias for `selectedIds[0]` since the modal
 *     shell expects a single record to open in the first frame.
 */
export interface OpenTodosEventDetail {
  /** Ordered list of selected `sprk_todoid` GUIDs (≥1 entry). */
  selectedIds: string[];
  /** First selected id (= `selectedIds[0]`). */
  firstId: string;
}

// ---------------------------------------------------------------------------
// Action factory
// ---------------------------------------------------------------------------

/**
 * Adapter context passed to {@link createToolbarActions}. The factory does NOT
 * read these eagerly — each returned handler dereferences from `ctx` at call
 * time, so React callers can pass mutating refs (the `webApi` adapter is
 * stable, `getSelectedTodos` returns the current frame's selection).
 */
export interface ToolbarActionContext {
  /** Xrm.WebApi adapter (delete/update). May be `null` outside Dataverse. */
  webApi: ITodoActionWebApi | null;
  /** Returns the currently-selected ITodo records (resolved from selectedIds + items). */
  getSelectedTodos: () => ITodo[];
  /**
   * Called after any action that mutates Dataverse state (Delete, Pin). The
   * caller should re-query the kanban so the UI shows the new state.
   * Returning a Promise is supported but not required.
   */
  onAfterMutate?: () => void | Promise<void>;
  /**
   * Called after the selection should be cleared (e.g. after Delete the
   * deleted records can no longer be selected). The caller usually invokes
   * `setSelectedIds(new Set())`.
   */
  onClearSelection?: () => void;
  /**
   * Optional confirmation function (defaults to `window.confirm`). Lets tests
   * inject a deterministic stub without monkey-patching globals.
   */
  confirm?: (message: string) => boolean;
}

/** The 4 toolbar handlers returned by {@link createToolbarActions}. */
export interface ToolbarActionHandlers {
  /** Open — dispatches `OPEN_TODOS_EVENT` with the selected ids. */
  handleOpen: () => ActionResult;
  /** Delete — confirms, deletes all selected, refreshes. */
  handleDelete: () => Promise<ActionResult>;
  /** Email — composes a mailto: with selected todo summaries. */
  handleEmail: () => ActionResult;
  /** Pin — toggles `sprk_todopinned` for all selected (any-unpinned ⇒ pin all). */
  handlePin: () => Promise<ActionResult>;
}

/**
 * Build the 4 action handlers bound to the supplied context. The handlers
 * close over `ctx` by reference so the React caller can pass a stable
 * `ctx.getSelectedTodos` closure that reads the latest state.
 *
 * Returns synchronous results for Open + Email (no I/O) and Promises for
 * Delete + Pin (Dataverse round-trip).
 */
export function createToolbarActions(
  ctx: ToolbarActionContext,
): ToolbarActionHandlers {
  const confirmFn = ctx.confirm ?? ((m: string) => window.confirm(m));

  // ────────────────────────────────────────────────────────────────────────
  // Open — dispatches CustomEvent on window
  // ────────────────────────────────────────────────────────────────────────
  const handleOpen = (): ActionResult => {
    const selected = ctx.getSelectedTodos();
    if (selected.length === 0) {
      return { succeeded: 0, failed: 0, message: 'No items selected.' };
    }
    const detail: OpenTodosEventDetail = {
      selectedIds: selected.map(t => t.sprk_todoid),
      firstId: selected[0].sprk_todoid,
    };
    try {
      window.dispatchEvent(
        new CustomEvent<OpenTodosEventDetail>(OPEN_TODOS_EVENT, { detail }),
      );
      return { succeeded: selected.length, failed: 0 };
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      console.error('[SmartTodo] Open action failed to dispatch event:', msg);
      return { succeeded: 0, failed: selected.length, message: msg };
    }
  };

  // ────────────────────────────────────────────────────────────────────────
  // Delete — confirm, delete in parallel, refresh
  // ────────────────────────────────────────────────────────────────────────
  const handleDelete = async (): Promise<ActionResult> => {
    const selected = ctx.getSelectedTodos();
    if (selected.length === 0) {
      return { succeeded: 0, failed: 0, message: 'No items selected.' };
    }
    if (!ctx.webApi) {
      const msg = 'Xrm.WebApi not available — cannot delete.';
      console.error('[SmartTodo]', msg);
      return { succeeded: 0, failed: selected.length, message: msg };
    }

    const summary =
      selected.length === 1
        ? `Delete "${selected[0].sprk_name}"?`
        : `Delete ${selected.length} to-do items? This cannot be undone.`;
    if (!confirmFn(summary)) {
      return { succeeded: 0, failed: 0, message: 'Cancelled by user.' };
    }

    const webApi = ctx.webApi;
    const results = await Promise.allSettled(
      selected.map(t => webApi.deleteRecord('sprk_todo', t.sprk_todoid)),
    );

    let succeeded = 0;
    let failed = 0;
    const errors: string[] = [];
    results.forEach((r, i) => {
      if (r.status === 'fulfilled') {
        succeeded++;
      } else {
        failed++;
        const reason = r.reason instanceof Error ? r.reason.message : String(r.reason);
        errors.push(`${selected[i].sprk_todoid}: ${reason}`);
      }
    });

    if (failed > 0) {
      console.error('[SmartTodo] Delete failures:', errors.join('; '));
    }

    // Always refresh + clear selection so partial failure still updates UI.
    if (ctx.onClearSelection) ctx.onClearSelection();
    if (ctx.onAfterMutate) await ctx.onAfterMutate();

    return {
      succeeded,
      failed,
      message: failed > 0 ? errors.join('; ') : undefined,
    };
  };

  // ────────────────────────────────────────────────────────────────────────
  // Email — compose mailto: with selected summaries
  // ────────────────────────────────────────────────────────────────────────
  const handleEmail = (): ActionResult => {
    const selected = ctx.getSelectedTodos();
    if (selected.length === 0) {
      return { succeeded: 0, failed: 0, message: 'No items selected.' };
    }

    const subject = `To-Dos: ${selected.length} selected`;
    const lines = selected.map(t => {
      const due = t.sprk_duedate
        ? ` (due ${new Date(t.sprk_duedate).toLocaleDateString()})`
        : '';
      return `- ${t.sprk_name}${due}`;
    });
    const body = lines.join('\n');

    const href = `mailto:?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;
    try {
      // window.location.href avoids popup-blocker issues with window.open.
      window.location.href = href;
      return { succeeded: selected.length, failed: 0 };
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      console.error('[SmartTodo] Email action failed:', msg);
      return { succeeded: 0, failed: selected.length, message: msg };
    }
  };

  // ────────────────────────────────────────────────────────────────────────
  // Pin — toggle sprk_todopinned (any-unpinned ⇒ pin all; all-pinned ⇒ unpin all)
  // ────────────────────────────────────────────────────────────────────────
  const handlePin = async (): Promise<ActionResult> => {
    const selected = ctx.getSelectedTodos();
    if (selected.length === 0) {
      return { succeeded: 0, failed: 0, message: 'No items selected.' };
    }
    if (!ctx.webApi) {
      const msg = 'Xrm.WebApi not available — cannot update pin state.';
      console.error('[SmartTodo]', msg);
      return { succeeded: 0, failed: selected.length, message: msg };
    }

    // Mutual-exclusivity policy: any unpinned record promotes the whole set
    // to PINNED. Only when EVERY selected record is already pinned do we
    // unpin them all. This matches the UX users expect from "select N + Pin"
    // in M365 mail (any unread + Mark-as-read → all read).
    const allPinned = selected.every(t => t.sprk_todopinned === true);
    const nextPinned = !allPinned;

    const webApi = ctx.webApi;
    const results = await Promise.allSettled(
      selected.map(t =>
        webApi.updateRecord('sprk_todo', t.sprk_todoid, {
          sprk_todopinned: nextPinned,
        }),
      ),
    );

    let succeeded = 0;
    let failed = 0;
    const errors: string[] = [];
    results.forEach((r, i) => {
      if (r.status === 'fulfilled') {
        succeeded++;
      } else {
        failed++;
        const reason = r.reason instanceof Error ? r.reason.message : String(r.reason);
        errors.push(`${selected[i].sprk_todoid}: ${reason}`);
      }
    });

    if (failed > 0) {
      console.error('[SmartTodo] Pin failures:', errors.join('; '));
    }

    if (ctx.onAfterMutate) await ctx.onAfterMutate();

    return {
      succeeded,
      failed,
      message: failed > 0 ? errors.join('; ') : undefined,
    };
  };

  return { handleOpen, handleDelete, handleEmail, handlePin };
}
