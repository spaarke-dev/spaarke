/**
 * commandBar/defaults — built-in command handlers for the DataGrid framework.
 *
 * Six built-in `CommandBarAction` kinds map to default handlers here:
 *   - `create-form`     → `defaultCreateFormHandler` — `Xrm.Navigation.openForm`
 *   - `delete-selected` → `defaultDeleteSelectedHandler` — `Xrm.WebApi.deleteRecord` x N
 *   - `refresh`         → `defaultRefreshHandler` — invokes `context.refresh()`
 *   - `export-excel`    → `defaultExportExcelHandler` — client-side CSV via {@link exportCsv}
 *   - `edit-columns`    → `defaultEditColumnsHandler` — hidden in R1 (no UI; logs to console)
 *   - `edit-filters`    → `defaultEditFiltersHandler` — toggles filter strip visibility
 *
 * Handlers are pure async functions; the `<CommandBar />` component composes them with
 * the user's `onCommandInvoke` callback and the `<Dialog />` confirmation flow.
 *
 * **Lift sources**:
 *  - `defaultCreateFormHandler`     ← `src/solutions/EventsPage/src/App.tsx:461` (`openNewEventForm`),
 *                                     generalized over `entityName` + optional `parentContext`.
 *  - `defaultDeleteSelectedHandler` ← `src/solutions/EventsPage/src/App.tsx:687` (`deleteSelectedEvents`),
 *                                     generalized over `entityName`. **`window.confirm` REMOVED**
 *                                     — the `<CommandBar />` component prompts via Fluent `<Dialog>`.
 *
 * **ADR**: ADR-021 (Fluent v9 + dark mode), ADR-022 (React-16-safe).
 * **FR**: FR-DG-08 (command bar), FR-DG-14 (CRUD lift), FR-DG-17 (CSV export).
 *
 * @see CommandBar
 * @see exportCsv
 */

import type { ResolvedColumn } from '../configResolution';
import { exportCsv, csvFilename } from './csvExport';

// ─────────────────────────────────────────────────────────────────────────────
// Handler context — what every default handler receives
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Shared context passed to every default handler.
 *
 * The `<CommandBar />` builds this from {@link useDataGridContext} + grid-level
 * props (`entityName`, `records`, `columns`, `currentView`, `parentContext`).
 */
export interface DefaultHandlerContext {
  /** Logical name of the primary entity (`'sprk_event'`, `'account'`, etc.). */
  entityName: string;
  /** Set of selected record GUIDs (preserved across lazy-load pages). */
  selectedIds: ReadonlyArray<string>;
  /** Currently-loaded records (the rows displayed in the grid as of right now). */
  records: ReadonlyArray<Record<string, unknown>>;
  /** Visible columns (post-filtering) — used by `export-excel`. */
  columns: ReadonlyArray<ResolvedColumn>;
  /** Display name of the active savedquery / configjson title — used by `export-excel`. */
  currentView: string;
  /** Trigger a hard refresh of the grid (re-fetch page 1, preserve filter/sort state). */
  refresh: () => void;
  /** Optional drill-through parent context — used by `create-form` to pre-fill regarding. */
  parentContext?: {
    entityType: string;
    id: string;
    name: string;
  };
}

/**
 * Signature every default handler conforms to. Returning `false` indicates the
 * caller cancelled (the `<CommandBar />` swallows cancellations silently).
 */
export type DefaultHandler = (ctx: DefaultHandlerContext) => Promise<void | boolean>;

// ─────────────────────────────────────────────────────────────────────────────
// Default labels + icon names per action — the `<CommandBar />` uses these as
// fallbacks when configjson does not specify a custom `label` / `icon`.
// ─────────────────────────────────────────────────────────────────────────────

export interface DefaultActionMeta {
  label: string;
  /** Fluent v9 icon name as it appears in `@fluentui/react-icons`. */
  icon: string;
  /** Default Fluent v9 `Button` appearance. */
  appearance: 'subtle' | 'primary' | 'secondary';
}

/**
 * Default labels + icons per built-in action. Every default action renders as
 * `appearance: 'subtle'` to match the Power Apps OOB grid command bar pattern
 * (icon + text, no background fill, no primary blue button). Hosts that want
 * an explicit primary call-to-action must author it explicitly in configjson.
 */
export const DEFAULT_ACTION_META: Record<string, DefaultActionMeta> = {
  'create-form': { label: 'New', icon: 'Add20Regular', appearance: 'subtle' },
  'delete-selected': { label: 'Delete', icon: 'Delete20Regular', appearance: 'subtle' },
  refresh: { label: 'Refresh', icon: 'ArrowSync20Regular', appearance: 'subtle' },
  'export-excel': { label: 'Export to Excel', icon: 'ArrowDownload20Regular', appearance: 'subtle' },
  'edit-columns': { label: 'Edit columns', icon: 'Column20Regular', appearance: 'subtle' },
  'edit-filters': { label: 'Edit filters', icon: 'Filter20Regular', appearance: 'subtle' },
};

// ─────────────────────────────────────────────────────────────────────────────
// Internal helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Walk `window` → `window.parent` → … to locate the Xrm object (the Custom Page
 * iframe case). Mirrors the pattern in `XrmDataverseClient`. Returns `null` when
 * Xrm is not available (Storybook, Code Pages outside MDA, tests).
 *
 * The Xrm shape is intentionally untyped here — the framework consumes only the
 * tiny `Navigation.openForm` + `WebApi.deleteRecord` slice and pivots through
 * `any` casts at each call site to avoid pulling in a heavy ambient typing.
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
function getXrm(): any {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let w: any = typeof window !== 'undefined' ? (window as any) : null;
  let depth = 0;
  while (w && depth < 10) {
    if (w.Xrm) {
      return w.Xrm;
    }
    if (w.parent && w.parent !== w) {
      w = w.parent;
      depth++;
      continue;
    }
    return null;
  }
  return null;
}

/** Strip `{` / `}` from a GUID (mirrors lifted EventsPage delete pattern). */
function cleanGuid(id: string): string {
  return id.replace(/[{}]/g, '');
}

/** Trigger a browser download from a `Blob`. SSR-safe (no-op if `document` is unavailable). */
function downloadBlob(blob: Blob, filename: string): void {
  if (typeof document === 'undefined') return;
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.style.display = 'none';
  document.body.appendChild(a);
  try {
    a.click();
  } finally {
    document.body.removeChild(a);
    // Schedule revocation on next tick so the download dialog has time to read the URL.
    setTimeout(() => URL.revokeObjectURL(url), 0);
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Built-in handlers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Open a new-form for `entityName`. Pre-fills `createFromEntity` from the drill-
 * through `parentContext` so embedded grids retain the regarding relationship.
 *
 * Lifted from `openNewEventForm` (EventsPage App.tsx:461). Generalized to any
 * entity by reading `ctx.entityName` instead of the hard-coded `EVENT_ENTITY_NAME`.
 */
export const defaultCreateFormHandler: DefaultHandler = async ctx => {
  const xrm = getXrm();
  if (!xrm?.Navigation?.openForm) {
    // eslint-disable-next-line no-console
    console.warn('[CommandBar] Xrm.Navigation.openForm not available. Cannot open new form.');
    return;
  }
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const formOptions: any = { entityName: ctx.entityName };
    if (ctx.parentContext) {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (formOptions as any).createFromEntity = {
        entityType: ctx.parentContext.entityType,
        id: ctx.parentContext.id,
        name: ctx.parentContext.name,
      };
    }
    await xrm.Navigation.openForm(formOptions);
  } catch (err) {
    // eslint-disable-next-line no-console
    console.error('[CommandBar] Failed to open new form:', err);
  }
};

/**
 * Delete every record in `ctx.selectedIds`. Uses `Promise.all` so concurrent
 * deletes complete in O(slowest-request) rather than O(N).
 *
 * **Confirmation is NOT performed here** — the `<CommandBar />` opens a Fluent
 * v9 `<Dialog>` and only invokes this handler after the user confirms. This
 * intentionally diverges from the lifted `deleteSelectedEvents` (which called
 * `window.confirm`) per FR-DG-14 + NFR-03.
 *
 * After all deletes resolve, `ctx.refresh()` is invoked so the grid re-fetches
 * page 1 and the freshly-deleted rows disappear.
 */
export const defaultDeleteSelectedHandler: DefaultHandler = async ctx => {
  if (ctx.selectedIds.length === 0) return;
  const xrm = getXrm();
  if (!xrm?.WebApi?.deleteRecord) {
    // eslint-disable-next-line no-console
    console.warn('[CommandBar] Xrm.WebApi.deleteRecord not available. Cannot delete.');
    return;
  }
  try {
    await Promise.all(ctx.selectedIds.map(id => xrm.WebApi.deleteRecord(ctx.entityName, cleanGuid(id))));
    // eslint-disable-next-line no-console
    console.log(`[CommandBar] Deleted ${ctx.selectedIds.length} ${ctx.entityName} record(s).`);
    ctx.refresh();
  } catch (err) {
    // eslint-disable-next-line no-console
    console.error('[CommandBar] Bulk delete failed:', err);
    if (xrm?.Navigation?.openAlertDialog) {
      try {
        await xrm.Navigation.openAlertDialog({
          title: 'Delete failed',
          text: `Some records failed to delete: ${err instanceof Error ? err.message : String(err)}`,
        });
      } catch {
        /* swallow — alert dialog is best-effort */
      }
    }
  }
};

/**
 * Trigger a hard refresh — delegates to `ctx.refresh()` from the
 * `<DataGrid />`'s context (re-fetches page 1, preserves filter/sort state).
 */
export const defaultRefreshHandler: DefaultHandler = async ctx => {
  ctx.refresh();
};

/**
 * Export the currently-loaded, currently-visible-columns records to a CSV file.
 *
 * Scope per spec Q-D answer: this exports ONLY the records the grid has loaded
 * so far AND respects active filter chip state — it does NOT auto-fetch
 * additional pages. Callers wanting "everything" must scroll to load all pages
 * before invoking export.
 *
 * Filename format: `{entityName}-{savedQueryName}-{yyyymmdd}.csv`.
 */
export const defaultExportExcelHandler: DefaultHandler = async ctx => {
  const blob = exportCsv(ctx.records as Record<string, unknown>[], ctx.columns, ctx.currentView, ctx.entityName);
  const filename = csvFilename(ctx.entityName, ctx.currentView);
  downloadBlob(blob, filename);
};

/**
 * `edit-columns` is hidden in R1 — see design.md §11.5 (deferred to R2 when the
 * column picker dialog ships). The handler exists so the action id resolves
 * cleanly; the `<CommandBar />` itself short-circuits rendering an `edit-columns`
 * button by default.
 */
export const defaultEditColumnsHandler: DefaultHandler = async _ctx => {
  // eslint-disable-next-line no-console
  console.log('[CommandBar] Edit columns is not available in R1.');
};

/**
 * Toggle the filter chip strip's visibility. Wired by the `<CommandBar />`
 * via a host-supplied callback (R1 keeps filter strip always visible by default;
 * future task wires this into a parent state setter).
 *
 * For now the handler emits a custom event that hosts may listen for, so the
 * action remains useful as a hook point without forcing a `<CommandBar />`
 * dependency on a filter-strip controller it does not own.
 */
export const defaultEditFiltersHandler: DefaultHandler = async _ctx => {
  if (typeof window !== 'undefined' && typeof window.dispatchEvent === 'function') {
    try {
      window.dispatchEvent(new CustomEvent('spaarke-datagrid:toggle-filter-strip'));
    } catch {
      /* CustomEvent unavailable in some test envs — silent fallback. */
    }
  }
};

// ─────────────────────────────────────────────────────────────────────────────
// Action → handler dispatch table — consumed by `<CommandBar />`
// ─────────────────────────────────────────────────────────────────────────────

export const DEFAULT_ACTION_HANDLERS: Record<string, DefaultHandler> = {
  'create-form': defaultCreateFormHandler,
  'delete-selected': defaultDeleteSelectedHandler,
  refresh: defaultRefreshHandler,
  'export-excel': defaultExportExcelHandler,
  'edit-columns': defaultEditColumnsHandler,
  'edit-filters': defaultEditFiltersHandler,
};
