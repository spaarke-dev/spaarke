/**
 * SmartTodoApp — Main layout component for the SmartTodo Code Page.
 *
 * Renders the Kanban board as the primary surface. To-do detail editing opens
 * the OOB `sprk_todo` main form via `Xrm.Navigation.navigateTo` at Layout 1
 * (85% × 85% centered modal, target 2). See `notes/smart-todo-modal-callsites.md`
 * in `projects/ai-spaarke-ai-workspace-UI-r2/` for the R2 migration context.
 *
 * ai-spaarke-ai-workspace-UI-r2 FR-13/FR-14 (2026-07-01) retired the hybrid
 * `<SmartTodoModal>` (R4 task 040) which embedded the OOB MDA main form in an
 * iframe — Microsoft Learn cites iframe-hosting `main.aspx` as a contractually
 * unsupported anti-pattern. The R2 replacement uses the OOB
 * `Xrm.Navigation.navigateTo` centered-modal path.
 *
 * Trade-off: the R4 modal's `<` / `>` record-navigation feature (walking the
 * current filter set) is INTENTIONALLY dropped in R2 per FR-13. Users navigating
 * between records return to the workspace and pick the next one.
 *
 * Layout (task 020 / FR-05 / U-3, 2026-08-16 top-bar redesign; UAT pass 2
 * 2026-08-17 moved the inline search into Header — see below):
 *   TodoProvider (shared state)
 *     ├── Header (title + [inline expanding SearchFilter] + Filter pill +
 *     │           + New Task / ⋮ overflow — Settings, Layout, Refresh — or
 *     │           selection-aware actions when a selection is active)
 *     └── SmartToDo (Kanban, with `hideHeader` so inner KanbanHeader is
 *           suppressed — chrome lives in the consolidated Header above)
 *
 * Open triggers (all route to `launchOpenTodoForm` in services/openTodoLauncher.ts;
 * task 033 threads a post-close `handleRefresh` so a Save & Close refreshes the
 * Kanban — see that module for the save-vs-cancel-signal rationale):
 *   • `OPEN_TODOS_EVENT` window event — toolbar Open + card double-click + card Open icon
 *   • `useLaunchContext` → `openTodo` action — URL-param launch (parent-form ribbon path)
 *
 * @see ADR-021 - Fluent UI v9 design system (makeStyles + tokens only)
 */

import * as React from "react";
import { makeStyles, tokens } from "@fluentui/react-components";
import { CreateTodoWizard, thinScrollbarDescendantStyle } from "@spaarke/ui-components";
import type { Orientation, ToolbarAction } from "@spaarke/ui-components";
import {
  createXrmDataService,
  createXrmNavigationService,
} from "@spaarke/ui-components/utils";
import {
  Open20Regular,
  Delete20Regular,
  Mail20Regular,
  Pin20Regular,
} from "@fluentui/react-icons";
import { resolveRuntimeConfig, initAuth, authenticatedFetch } from "@spaarke/auth";
import { TodoProvider, useTodoContext } from "./context/TodoContext";
import { SmartToDo } from "./components/SmartToDo";
// ListView import removed 2026-06-19 per UAT: list view discontinued — kanban only.
import { Header, useCurrentContactId } from "@spaarke/smart-todo-components";
// smart-todo-r5 UAT 2026-08-17 — the structured Filter pane (task 021,
// FR-06 / F-3: Priority / Status / Due-date / Assigned-To) is REPLACED by a
// single expanding text search, mounted against task 020's
// isFilterPaneOpen/onToggleFilterPane state (below). See
// projects/smart-todo-r5/notes/uat-filter-text-search.md for the decision +
// the FR-07 "Show Completed" regression this intentionally accepts.
// UAT pass 2 (2026-08-17): `<SearchFilter>` is now MOUNTED BY `Header.tsx`
// itself (inline, left of the Filter pill) — this file no longer imports or
// renders it directly, only lifts the `searchQuery` state and threads it
// (plus its setter) through Header's `searchQuery` / `onSearchQueryChange`
// props.
import { createToolbarActions, OPEN_TODOS_EVENT } from "./components/Toolbar";
import type { ITodoActionWebApi, OpenTodosEventDetail } from "./components/Toolbar";
import { getWebApi, getUserId, getSpeContainerIdFromBusinessUnit } from "./services/xrmProvider";
import { useLaunchContext } from "./hooks/useLaunchContext";
import { useUserPreferences } from "./hooks/useUserPreferences";
import { launchNewTaskCreateForm } from "./services/newTaskLauncher";
import { launchOpenTodoForm } from "./services/openTodoLauncher";
// SmartTodoViewMode type import removed 2026-06-19 — see viewMode removal above.

// ---------------------------------------------------------------------------
// R2 FR-13 — Layout 1 open helper (task 031 / FR-11: delegates to the shared
// `navigateToEntityRecordSurfaceAsync` launcher instead of an inline
// `Xrm.Navigation.navigateTo` call — see wizardLaunchers.ts)
//
// Opens the OOB `sprk_todo` main form at the full-cover OOB size (task 032 /
// FR-13) via `Xrm.Navigation.navigateTo`. Replaces the R4 iframe-hosted
// `<SmartTodoModal>` per ai-spaarke-ai-workspace-UI-r2 FR-13 (iframe-hosting
// `main.aspx` is a contractually unsupported anti-pattern per MS Learn).
//
// task 033 (FR-14) — the open wrapper is now `launchOpenTodoForm` in
// `services/openTodoLauncher.ts` (extracted from the pre-033 module-scope
// `openSprkTodoAsLayout1` for unit-testability, mirroring the CREATE path's
// `newTaskLauncher.ts`). It threads an `onClose` refetch callback so a Save &
// Close reflects in the Kanban without a manual reload. Called from two open
// triggers below — the `OPEN_TODOS_EVENT` listener and the `useLaunchContext`
// openTodo effect — both passing `handleRefresh` so the refetch fires once the
// dialog closes. See openTodoLauncher.ts for the save-vs-cancel-signal
// rationale (OPEN mode refetches UNCONDITIONALLY on close; CREATE gates on
// `savedEntityReference`).
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * Outer page frame — vertical stack: Header on top, primary Kanban surface
   * below. R4 task 030 (FR-06); R2 FR-14 (2026-07-01) retired the side-pane
   * `<SmartTodoModal>` mount that previously overlaid this frame.
   */
  page: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
    // Thin, theme-aware scrollbar for EVERY scroll container in the code page
    // (kanban columns, dialogs, grids). Descendant variant because
    // ::-webkit-scrollbar does not cascade — one spread here covers the whole
    // surface. Canonical: @spaarke/ui-components theme/scrollbar.ts.
    ...thinScrollbarDescendantStyle,
  },
  /** Primary surface row — fills remaining height under the header. */
  primaryPanel: {
    flexGrow: 1,
    minHeight: 0,
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
  },
});

// ---------------------------------------------------------------------------
// Inner layout (needs TodoContext access)
// ---------------------------------------------------------------------------

function SmartTodoLayout(): React.ReactElement {
  const styles = useStyles();
  const { selectedEventId, refetch, items, selectItem } = useTodoContext();

  // ── R4 task 033 + R4-104 — Persisted view-mode + orientation (FR-09 + FR-28) ──
  //
  // viewMode + orientation both round-trip through `useUserPreferences` — the
  // hook extends the kanban-prefs JSON envelope so all view-shape preferences
  // share a single Dataverse record (preference-type 100000000, no new
  // optionset values). The inner SmartToDo also calls `useUserPreferences` for
  // its threshold logic; both hook instances read/write the SAME record so the
  // next fetch on either surface sees the latest persisted state.
  //
  // R4-104 (UAT 10): Default viewMode = "kanban" for NEW users (no preference
  // record). Existing users keep their saved choice — see
  // `hooks/useUserPreferences.ts::DEFAULT_SMART_TODO_VIEW_MODE`.
  //
  // R4-104: Orientation is hoisted to App-level so the consolidated Header
  // can drive it (now via the overflow "Layout" menu item, task 020 — the
  // inner KanbanHeader is suppressed via `hideHeader`).
  const { preferences: viewPrefs, updatePreferences: updateViewPrefs } =
    useUserPreferences({ webApi: getWebApi(), userId: getUserId() });
  const orientation = viewPrefs.orientation;
  // task 020 (FR-05 / U-3, 2026-08-16) — the QuickAdd Assigned-To typeahead
  // that consumed `useCurrentContactId` was removed from Header along with
  // the rest of the QuickAdd group; the hook call is removed here as it has
  // no other consumer (was Header-only plumbing).
  // viewMode / handleViewModeChange removed 2026-06-19 per UAT — list view
  // discontinued, kanban is the sole presentation. The preference field
  // remains in useUserPreferences (back-compat for old user records) but
  // is no longer surfaced in the chrome.
  const handleOrientationChange = React.useCallback(
    (next: Orientation) => {
      void updateViewPrefs({ orientation: next });
    },
    [updateViewPrefs],
  );

  // ── smart-todo-r5 UAT 2026-08-17 (item #1) — default "Assigned To" to the
  //    current user's contact when "+ New Task" opens the OOB create form.
  //    `sprk_todo.sprk_assignedto` targets the OOB `contact` table (resolved
  //    from systemuser via `contact.sprk_systemuser`), so we resolve the
  //    current user's contact once at mount and thread it into the launcher's
  //    three-key lookup default. The Field Mapping Framework does NOT apply
  //    here (it's for wizard creates that inherit from a parent record). When
  //    the user has no associated contact, this stays null and the form opens
  //    with Assigned To blank (today's behavior — never an error).
  const { contactId: currentContactId, contactName: currentContactName } =
    useCurrentContactId({ webApi: getWebApi(), userId: getUserId() });

  // R4-104 — Settings opener callback ref. The inner `<SmartToDo>` exposes
  // its threshold-settings popover trigger via `onSettingsOpenerReady`; we
  // capture it here so the consolidated Header's Settings button can open
  // the popover. The popover anchor (a hidden span) stays mounted inside
  // SmartToDo regardless of hideHeader.
  const settingsOpenerRef = React.useRef<(() => void) | null>(null);
  const handleSettingsOpenerReady = React.useCallback((open: () => void) => {
    settingsOpenerRef.current = open;
  }, []);
  const handleOpenSettings = React.useCallback(() => {
    settingsOpenerRef.current?.();
  }, []);

  // UAT 2026-06-21 round 8: capture the inner SmartToDo's refetch so the
  // Header's Refresh button can actually trigger a re-fetch. The
  // TodoContext.refetch is a no-op placeholder (see TodoContext.tsx — was
  // never wired to a real data source); the real data lives in SmartToDo's
  // internal `useTodoItems` hook. SmartToDo exposes its refetch via
  // `onRefetchReady`; we capture it in a ref + use it in `handleRefresh`.
  const innerRefetchRef = React.useRef<(() => void) | null>(null);
  const handleInnerRefetchReady = React.useCallback((fn: () => void) => {
    innerRefetchRef.current = fn;
  }, []);

  // Refresh — UAT 2026-06-21 round 8: route to the inner SmartToDo's real
  // refetch (captured via onRefetchReady). Fall back to the TodoContext
  // refetch only if the inner one hasn't reported in yet (defensive — would
  // never happen in normal mount order). Declared here (moved up by task 030,
  // FR-10) so `handleNewTask` below can call it on save — was previously
  // defined further down, used only by the Header's Refresh menu item.
  const handleRefresh = React.useCallback(() => {
    if (innerRefetchRef.current) {
      innerRefetchRef.current();
    } else {
      refetch();
    }
  }, [refetch]);

  // ── R4 task 030 — Header state (App-level, per task brief) ───────────────
  //
  // `searchQuery` is owned here and forwarded to `<SmartToDo>` for its
  // card-level substring filter (title / description / regarding-record name
  // / number / assigned-to — extended by the smart-todo-r5 UAT 2026-08-17
  // change, see `SmartToDo.tsx`'s `displayItems` memo). Its producer is now
  // the `<SearchFilter>` box that `Header.tsx` mounts inline against
  // `isFilterPaneOpen` (UAT pass 2, 2026-08-17 — moved inline, left of the
  // Filter pill; REPLACES task 021's structured Filter pane per the
  // operator's UAT decision) — see
  // `projects/smart-todo-r5/notes/uat-filter-text-search.md`.
  // `selectedIds` is the **multi-select** set driving Row 4 of the header
  // (card affordances — task 060). It is INDEPENDENT of `selectedEventId`
  // from TodoContext (which, post-R4 task 042, drives only the ListView
  // highlight — the side-pane is retired per FR-18).
  //
  // For task 030 `selectedIds` is initialized empty; the toolbar renders
  // `null` (zero selection). Task 060 will populate the Set as the user
  // multi-selects cards in the kanban + list views.
  const [searchQuery, setSearchQuery] = React.useState<string>("");
  // `setSelectedIds` is consumed by task 032 Delete's `onClearSelection`
  // callback. Task 060 will additionally populate the Set as the user
  // multi-selects cards in the kanban + list views.
  const [selectedIds, setSelectedIds] = React.useState<Set<string>>(
    () => new Set<string>(),
  );

  // ── task 020 (FR-05 / U-3, 2026-08-16) — Filter pane trigger state ───────
  //
  // Controlled open/close boolean for the Filter pill.
  const [isFilterPaneOpen, setIsFilterPaneOpen] = React.useState<boolean>(false);
  const handleToggleFilterPane = React.useCallback(() => {
    setIsFilterPaneOpen((v) => !v);
  }, []);

  // task 021's structured Filter pane predicate (`filterState` /
  // `ITodoFilterState` / `DEFAULT_TODO_FILTER`) was REMOVED here per the
  // smart-todo-r5 UAT 2026-08-17 decision — `searchQuery` (above) is now the
  // sole filter predicate, applied client-side in `<SmartToDo>`. The
  // Dataverse query (`buildTodoItemsQuery`) reverted to its pre-task-021
  // default (no structured filter params) — see queryHelpers.ts.

  // ── Launch context — read once on mount (task 100 / W-2, extended R4-034,
  //     hoisted here by task 030 so both the "+ New Task" handler below AND
  //     the openTodo effect further down can read it). ───────────────────────
  const launchContext = useLaunchContext();

  // ── task 030 (FR-10, 2026-08-16) — "+ New Task" opens the sprk_todo OOB
  //     main form in CREATE mode as a modal ────────────────────────────────
  //
  // Replaces task 020's documented no-op stub. Reuses
  // `navigateToEntityRecordSurfaceAsync` (via `launchNewTaskCreateForm` in
  // `services/newTaskLauncher.ts`) — the SAME launcher already wired into the
  // Assistant surface-launch registry — per CLAUDE.md §11 (no second,
  // parallel `Xrm.Navigation.navigateTo` call site). ADR-050 Path A exception
  // (spec.md ADR Tensions / CLAUDE.md §6.5): this deliberately stays on the
  // OOB main form, NOT a proprietary `SprkModal`/`FormModal`. On save
  // (`outcome.savedEntityReference` present), `handleRefresh` re-fetches the
  // Kanban so the new To Do appears without a manual reload; on cancel,
  // nothing happens (negative acceptance criterion). Does NOT touch
  // `LaunchCreateTodoWizardHost` / `CreateTodoWizard` below — that stays the
  // Outlook/parent-ribbon entry point (FR-19, out of this task's scope).
  const handleNewTask = React.useCallback(() => {
    void launchNewTaskCreateForm(
      launchContext,
      handleRefresh,
      // smart-todo-r5 UAT item #1 — pre-fill Assigned To with the current
      // user's contact (undefined when unresolved → form opens blank).
      currentContactId
        ? { contactId: currentContactId, contactName: currentContactName ?? undefined }
        : undefined,
    );
  }, [launchContext, handleRefresh, currentContactId, currentContactName]);

  // R2 FR-13 (2026-07-01) — The hybrid `<SmartTodoModal>` (R4 task 040) that
  // used to overlay this component has been retired. The `OPEN_TODOS_EVENT`
  // listener and the `useLaunchContext` openTodo effect (below) now call
  // `openSprkTodoAsLayout1` directly, which uses `Xrm.Navigation.navigateTo` at
  // 85% × 85% (Layout 1 standard per FR-20). No local modal state remains.

  // ── openTodo launch-context — routes to Layout 1 ─────────────────────────
  //
  // When the LegalWorkspace SmartTodo widget's Open button launches this Code
  // Page with `?action=openTodo&todoId=<guid>`, `useLaunchContext` returns the
  // `openTodo` discriminator. R2 FR-13 (2026-07-01) — this path now calls
  // `openSprkTodoAsLayout1` directly (was setModalTodoId under R4-100/W-2).
  React.useEffect(() => {
    if (launchContext?.action === 'openTodo') {
      // task 033 (FR-14) — refetch the Kanban once the OOB form dialog closes
      // so a Save & Close reflects without a manual reload. `handleRefresh` is
      // stable (wraps innerRefetchRef; refetch is context-stable), so the
      // mount-once capture below reads a current refetch.
      void launchOpenTodoForm(launchContext.todoId, handleRefresh);
    }
    // Run once on mount — `launchContext` is memoised by the hook for the
    // component's lifetime and won't change.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ── Subscribe to OPEN_TODOS_EVENT — routes to Layout 1 ────────────────────
  // Task 032's `createToolbarActions` Open handler dispatches a window
  // `CustomEvent` with shape `{selectedIds: string[], firstId: string}`
  // whenever the user clicks the toolbar Open. Task 060 ALSO dispatches the
  // same event from card double-click + the per-card Open icon. R2 FR-13
  // (2026-07-01) — the listener now calls `openSprkTodoAsLayout1` directly
  // (was setModalTodoId under R4-040). Dispatchers are unchanged.
  React.useEffect(() => {
    const handler = (ev: Event): void => {
      const detail = (ev as CustomEvent<OpenTodosEventDetail>).detail;
      if (detail?.firstId) {
        // task 033 (FR-14) — refetch the Kanban once the OOB form dialog closes
        // (unconditional; OPEN mode has no save-vs-cancel resolve signal).
        void launchOpenTodoForm(detail.firstId, handleRefresh);
      }
    };
    window.addEventListener(OPEN_TODOS_EVENT, handler);
    return () => {
      window.removeEventListener(OPEN_TODOS_EVENT, handler);
    };
    // `handleRefresh` is stable; listing it keeps the subscription in lockstep
    // without churn (re-subscribe is a harmless remove+add).
  }, [handleRefresh]);

  // ── R4 task 060 — Per-card open callback (Open icon + double-click) ──────
  //
  // Dispatches the canonical `OPEN_TODOS_EVENT` exported from
  // `./components/Toolbar` (Wave A task 032) so the modal subscriber above
  // routes uniformly regardless of the launcher (toolbar / card / future
  // keyboard shortcut). We dispatch on `window` so the listener above
  // (registered in this same component) receives it.
  const handleCardOpen = React.useCallback((todoId: string) => {
    const detail: OpenTodosEventDetail = {
      selectedIds: [todoId],
      firstId: todoId,
    };
    window.dispatchEvent(
      new CustomEvent<OpenTodosEventDetail>(OPEN_TODOS_EVENT, { detail }),
    );
  }, []);

  // ── R4 task 060 — Per-card selection toggle ──────────────────────────────
  //
  // Reads/writes the `selectedIds` Set lifted to this layout (above). The Set
  // is the source of truth driving Header Row 4's selection-aware toolbar
  // (Wave A task 032 + 030); toggling here causes the toolbar to appear /
  // update its count without any extra plumbing.
  const handleToggleSelect = React.useCallback((todoId: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(todoId)) {
        next.delete(todoId);
      } else {
        next.add(todoId);
      }
      return next;
    });
  }, []);

  // ── R4 task 032 — Selection-aware toolbar actions (Open / Delete / Email /
  // Pin) wired via `createToolbarActions` from `./components/Toolbar`
  // (spec FR-08). The action factory closes over `ctx` by reference so each
  // handler reads the LATEST items + selectedIds at click time (avoids
  // recreating handlers on every render).
  //
  // Open  — dispatches `sprk-smarttodo:open-todos` on window. The listener
  //         above (R2 FR-13, 2026-07-01) calls `openSprkTodoAsLayout1` — opens
  //         the OOB sprk_todo main form via `Xrm.Navigation.navigateTo` at
  //         Layout 1 (85% × 85%). (Was: R4-040 iframe-hosted `<SmartTodoModal>`,
  //         retired per FR-14.)
  // Delete — confirms via window.confirm, deletes selected, refetches via
  //          TodoContext.
  // Email  — composes a mailto: with selected todo names + due dates.
  // Pin    — toggles `sprk_todopinned`; any-unpinned ⇒ pin all; all-pinned ⇒
  //          unpin all (mirrors M365 "Mark as read" UX).
  //
  // Per `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` Delete + Pin use
  // `Xrm.WebApi` directly from this Code Page host (no BFF needed for simple
  // per-record CRUD).
  const itemsRef = React.useRef(items);
  itemsRef.current = items;
  const selectedIdsRef = React.useRef(selectedIds);
  selectedIdsRef.current = selectedIds;

  const actionHandlers = React.useMemo(() => {
    // Cast getWebApi() to our minimal action surface (Xrm.WebApi has these
    // members at runtime; the narrow interface decouples this code from the
    // full Xrm typings).
    const rawWebApi = getWebApi() as unknown as ITodoActionWebApi | null;
    return createToolbarActions({
      webApi: rawWebApi,
      getSelectedTodos: () =>
        itemsRef.current.filter((t) =>
          selectedIdsRef.current.has(t.sprk_todoid),
        ),
      onAfterMutate: () => refetch(),
      onClearSelection: () => setSelectedIds(new Set<string>()),
    });
  }, [refetch]);

  const toolbarActions: ToolbarAction[] = React.useMemo(
    () => [
      {
        id: "open",
        label: "Open",
        icon: <Open20Regular />,
        onClick: () => {
          const result = actionHandlers.handleOpen();
          if (result.failed > 0) {
            console.error("[SmartTodo] Open action failed:", result.message);
          }
        },
      },
      {
        id: "delete",
        label: "Delete",
        icon: <Delete20Regular />,
        onClick: () => {
          void actionHandlers.handleDelete().then((result) => {
            if (result.failed > 0) {
              console.error(
                "[SmartTodo] Delete action failures:",
                result.message,
              );
            }
          });
        },
      },
      {
        id: "email",
        label: "Email",
        icon: <Mail20Regular />,
        onClick: () => {
          const result = actionHandlers.handleEmail();
          if (result.failed > 0) {
            console.error("[SmartTodo] Email action failed:", result.message);
          }
        },
      },
      {
        id: "pin",
        label: "Pin",
        icon: <Pin20Regular />,
        onClick: () => {
          void actionHandlers.handlePin().then((result) => {
            if (result.failed > 0) {
              console.error(
                "[SmartTodo] Pin action failures:",
                result.message,
              );
            }
          });
        },
      },
    ],
    [actionHandlers],
  );

  // task 020 (FR-05 / U-3, 2026-08-16) — The QuickAdd inline input (title →
  // QUICK_ADD_TODO_EVENT → SmartToDo's handleAdd) and its Header-side
  // "+ New" wizard button (`onOpenWizard`) were removed along with the rest
  // of the QuickAdd group. The Header's new primary add path is the
  // "+ New Task" button (`handleNewTask`, wired to the OOB create form by
  // task 030 / FR-10 — see declaration above). The richer `CreateTodoWizard`
  // path remains reachable via Outlook ribbon + parent-form ribbon launches
  // (`LaunchCreateTodoWizardHost`, below) — those launch triggers are
  // unrelated to the Header and were not touched here.

  // ── R4 task 042 (FR-18) — TodoDetailPanel side-pane retired ──────────────
  // The R3 two-pane layout (kanban + collapsible TodoDetailPanel separated by
  // a draggable PanelSplitter) is gone. R2 FR-13 (2026-07-01) further retired
  // the R4 hybrid `<SmartTodoModal>` in favor of `Xrm.Navigation.navigateTo`
  // at Layout 1 (see `openSprkTodoAsLayout1` at module scope). The single
  // remaining primary surface — Kanban — fills the page below the Header.

  return (
    <div className={styles.page}>
      {/* ── task 020 (FR-05 / U-3, 2026-08-16) — Top-bar redesign ───────────
            ──────────────────────────────────────────────────────────────────
            Replaces the R4-104 single-row toolbar (3-field QuickAdd + broken
            inline-SearchBox "Filter") with the mockup's minimal chrome:
            Filter pill / + New Task / ⋮ overflow (Settings, Layout, Refresh).
            The inner `<KanbanHeader>` stays suppressed via
            `<SmartToDo hideHeader />`. All prior Settings/Layout/Refresh
            BEHAVIOR is preserved verbatim — only their trigger UI moved into
            the overflow Menu:
              - R4-031 Assigned-to-Me — already query-baked, no UI here
              - R4-032 Selection-aware Open/Delete/Email/Pin — embedded
                <SelectionAwareToolbar> renders when selectedCount > 0
              - R4-070 Kanban orientation toggle — now the overflow "Layout"
                menu item (flips horizontal ↔ vertical)
            List/Card view toggle (R4-033) was already discontinued
            2026-06-19 (kanban-only) and is not part of this redesign. */}
      <Header
        onRefresh={handleRefresh}
        onOpenSettings={handleOpenSettings}
        selectedCount={selectedIds.size}
        toolbarActions={toolbarActions}
        orientation={orientation}
        onOrientationChange={handleOrientationChange}
        isFilterPaneOpen={isFilterPaneOpen}
        onToggleFilterPane={handleToggleFilterPane}
        searchQuery={searchQuery}
        onSearchQueryChange={setSearchQuery}
        onNewTask={handleNewTask}
      />

      {/* ── Primary surface — Kanban Board (UAT 2026-06-19: list view removed
            per user feedback; kanban is the sole presentation.) ── */}
      <div className={styles.primaryPanel}>
        <SmartToDo
          webApi={getWebApi()}
          userId={getUserId()}
          // smart-todo-r5 UAT 2026-08-17 — `searchQuery` is now produced by
          // the `<SearchFilter>` expanding text search above (task 021's
          // structured Filter pane + its `filter` prop below are removed).
          searchQuery={searchQuery}
          // R4 task 060 — Card affordances plumbing (FR-25/26/27)
          selectedIds={selectedIds}
          onToggleSelect={handleToggleSelect}
          onOpenTodo={handleCardOpen}
          // R4-104 (Wave E-3) — Suppress inner KanbanHeader (chrome lives
          // in the consolidated <Header /> above) + expose Settings opener
          // so the Header's gear button can trigger the threshold popover
          // that's still anchored inside SmartToDo.
          hideHeader
          onSettingsOpenerReady={handleSettingsOpenerReady}
          onRefetchReady={handleInnerRefetchReady}
          // UAT 2026-06-19 — single-source-of-truth for orientation.
          // Without this prop, SmartToDo's internal useUserPreferences
          // instance held its own copy that didn't react to Header toggle
          // clicks; the kanban stayed stuck regardless of persisted state.
          orientation={orientation}
        />
      </div>

      {/* R2 FR-14 (2026-07-01) — the R4 hybrid `<SmartTodoModal>` mount was
          retired here. Open triggers now call `openSprkTodoAsLayout1` which
          opens the OOB sprk_todo main form via `Xrm.Navigation.navigateTo` at
          Layout 1 (85% × 85%). See notes/smart-todo-modal-callsites.md. */}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Launch-context wizard host (FR-16 / FR-27 — task 070 + 070b)
//
// When the Outlook ribbon's "Create To Do" action launches the SmartTodo Code
// Page via `window.open`, the URL carries `?action=createTodo&regardingType=…
// &regardingId=…&regardingName=…`. `useLaunchContext` parses those params (and
// clears them from the URL so a refresh doesn't re-trigger the wizard). When
// the action is detected, this component mounts the shared `CreateTodoWizard`
// with `initialRegarding` pre-filled per the launch contract:
//
//   • Kanban "Add To Do"        → initialRegarding undefined (NOT used here;
//                                  AddTodoBar handles the in-page kanban add)
//   • Parent-form ribbon         → initialRegarding = launch record triple
//   • Outlook "Create To Do"     → initialRegarding = sprk_communication triple
//
// Auth: the wizard's `authenticatedFetch` + `bffBaseUrl` come from `@spaarke/auth`
// via `initAuth`. The auth init runs lazily — only when a launch context is
// present — so normal kanban loads don't pay the MSAL bootstrap cost.
//
// See: projects/smart-todo-decoupling-r3/notes/createtodo-launch-contract.md
// ---------------------------------------------------------------------------

function LaunchCreateTodoWizardHost(): React.ReactElement | null {
  const launchContext = useLaunchContext();
  // Narrow once to the createTodo branch — `useLaunchContext` returns a
  // discriminated union (createTodo | openTodos | undefined; R4 task 034); this
  // host only handles the createTodo flow. The openTodos branch is consumed by
  // the Kanban filter (see R4 task 030).
  const createTodoContext =
    launchContext?.action === "createTodo" ? launchContext : undefined;
  const isCreateTodoLaunch = createTodoContext !== undefined;

  const [wizardOpen, setWizardOpen] = React.useState<boolean>(isCreateTodoLaunch);
  const [isAuthReady, setIsAuthReady] = React.useState<boolean>(false);
  const [bffBaseUrl, setBffBaseUrl] = React.useState<string>("");

  // Initialise auth ONLY when we have a createTodo launch (zero-cost on normal loads)
  React.useEffect(() => {
    if (!isCreateTodoLaunch) return;

    let cancelled = false;
    void (async () => {
      try {
        const config = await resolveRuntimeConfig();
        await initAuth({
          clientId: config.msalClientId,
          bffBaseUrl: config.bffBaseUrl,
          bffApiScope: config.bffOAuthScope,
          tenantId: config.tenantId || undefined,
          proactiveRefresh: true,
        });
        if (!cancelled) {
          setBffBaseUrl(config.bffBaseUrl);
          setIsAuthReady(true);
        }
      } catch (err) {
        console.error("[SmartTodo] LaunchCreateTodoWizardHost: auth init failed", err);
        // Even on auth failure, allow the wizard to open — the create call
        // will surface the error to the user (defensive degrade per FR-16).
        if (!cancelled) setIsAuthReady(true);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [isCreateTodoLaunch]);

  // Stable adapter singletons (only constructed once the launch context exists)
  const dataService = React.useMemo(
    () => (isCreateTodoLaunch ? createXrmDataService() : null),
    [isCreateTodoLaunch],
  );
  const navigationService = React.useMemo(
    () => (isCreateTodoLaunch ? createXrmNavigationService() : null),
    [isCreateTodoLaunch],
  );

  const resolveSpeContainerId = React.useCallback(async (): Promise<string> => {
    const webApi = getWebApi();
    if (!webApi) return "";
    return getSpeContainerIdFromBusinessUnit(webApi);
  }, []);

  // smart-todo-r5 UAT 2026-08-17 (item #1) — default "Assigned To" to the
  // current user's contact here too (Outlook / parent-ribbon wizard launch),
  // matching the CreateTodoWizard Code Page + the OOB "+ New Task" form.
  const { contactId: currentContactId, contactName: currentContactName } =
    useCurrentContactId({ webApi: getWebApi(), userId: getUserId() });

  const handleClose = React.useCallback(() => {
    setWizardOpen(false);
  }, []);

  // Render nothing for normal loads (no behavioral change — regression safe)
  if (!isCreateTodoLaunch || !dataService || !navigationService) return null;

  // Hold rendering until auth has had a chance to init (keeps `authenticatedFetch`
  // from being called against an uninitialised provider). A failed init still flips
  // isAuthReady to true so the user sees the wizard rather than a silent hang.
  if (!isAuthReady) return null;

  return (
    <CreateTodoWizard
      open={wizardOpen}
      onClose={handleClose}
      dataService={dataService}
      navigationService={navigationService}
      initialRegarding={createTodoContext?.initialRegarding}
      authenticatedFetch={authenticatedFetch}
      bffBaseUrl={bffBaseUrl}
      resolveSpeContainerId={resolveSpeContainerId}
      defaultAssignedTo={
        currentContactId
          ? { contactId: currentContactId, contactName: currentContactName ?? undefined }
          : undefined
      }
    />
  );
}

// ---------------------------------------------------------------------------
// Exported component (wraps in TodoProvider)
// ---------------------------------------------------------------------------

export function SmartTodoApp(): React.ReactElement {
  return (
    <TodoProvider>
      <SmartTodoLayout />
      <LaunchCreateTodoWizardHost />
    </TodoProvider>
  );
}
