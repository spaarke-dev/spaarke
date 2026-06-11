/**
 * SmartTodoApp — Main layout component for the SmartTodo Code Page.
 *
 * Integrates the Kanban board (left panel) with a collapsible TodoDetailPanel
 * (right panel) separated by a draggable PanelSplitter. The detail panel
 * automatically opens when a to-do item is selected and closes when deselected.
 *
 * Layout:
 *   TodoProvider (shared state)
 *     ├── Left panel  — SmartToDo (Kanban board)
 *     ├── PanelSplitter (draggable, keyboard-accessible)
 *     └── Right panel — TodoDetailPanel (collapsible)
 *
 * Panel behaviour:
 *   - Default detail width: 400px (persisted via localStorage)
 *   - Panel open/close animation < 200ms (NFR-01)
 *   - Splitter supports mouse drag, keyboard (Arrow keys), and double-click reset
 *   - Panel state persisted to localStorage under 'smarttodo-panel-layout'
 *
 * @see ADR-012 - PanelSplitter and useTwoPanelLayout from @spaarke/ui-components
 * @see ADR-021 - Fluent UI v9 design system (makeStyles + tokens only)
 */

import * as React from "react";
import { makeStyles, mergeClasses, tokens } from "@fluentui/react-components";
import { PanelSplitter } from "@spaarke/ui-components/PanelSplitter";
import { useTwoPanelLayout } from "@spaarke/ui-components/hooks";
import { CreateTodoWizard } from "@spaarke/ui-components";
import type { ToolbarAction } from "@spaarke/ui-components";
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
import { ListView } from "./components/ListView";
import { TodoDetailPanel } from "./components/TodoDetailPanel";
import { Header } from "./components/Header";
import { createToolbarActions, OPEN_TODOS_EVENT } from "./components/Toolbar";
import type { ITodoActionWebApi, OpenTodosEventDetail } from "./components/Toolbar";
import { SmartTodoModal, todosToModalRecords } from "./components/Modal";
import { getWebApi, getUserId, getSpeContainerIdFromBusinessUnit } from "./services/xrmProvider";
import { useLaunchContext } from "./hooks/useLaunchContext";
import { useUserPreferences } from "./hooks/useUserPreferences";
import type { SmartTodoViewMode } from "./hooks/useUserPreferences";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * Outer page frame — vertical stack: 4-row Header on top, two-pane
   * (kanban + detail) `container` below. New in R4 task 030 (FR-06).
   */
  page: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  /** Two-pane row — fills remaining height under the header. */
  container: {
    display: "flex",
    flexGrow: 1,
    minHeight: 0,
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  primaryPanel: {
    overflow: "hidden",
    minWidth: 0,
    height: "100%",
  },
  detailPanel: {
    overflow: "hidden",
    minWidth: 0,
    display: "flex",
    flexDirection: "column",
  },
  /** Smooth collapse/expand animation for panel toggle transitions.
   *  Applied only when NOT dragging to avoid janky animation during resize. */
  detailPanelAnimated: {
    transitionProperty: "width",
    transitionDuration: "150ms", // NFR-01: < 200ms
    transitionTimingFunction: tokens.curveEasyEase,
    "@media (prefers-reduced-motion: reduce)": {
      transitionDuration: "0ms",
    },
  },
});

// ---------------------------------------------------------------------------
// Inner layout (needs TodoContext access)
// ---------------------------------------------------------------------------

function SmartTodoLayout(): React.ReactElement {
  const styles = useStyles();
  const { selectedEventId, refetch, items, selectItem } = useTodoContext();

  // ── R4 task 033 — Persisted list/card view mode (FR-09) ──────────────────
  //
  // The view mode round-trips through `useUserPreferences` (extended in this
  // task to carry a `viewMode` field on the existing kanban-prefs JSON
  // envelope — see `hooks/useUserPreferences.ts`). Default = "card" on first
  // visit (no preference record). The SmartToDo Kanban surface ALSO calls
  // `useUserPreferences` for its threshold + filter-mode round-trip; both
  // hook instances read the same preferencetype record, so the next fetch on
  // either surface sees the latest viewMode persisted here.
  const { preferences: viewPrefs, updatePreferences: updateViewPrefs } =
    useUserPreferences({ webApi: getWebApi(), userId: getUserId() });
  const viewMode = viewPrefs.viewMode;
  const handleViewModeChange = React.useCallback(
    (mode: SmartTodoViewMode) => {
      void updateViewPrefs({ viewMode: mode });
    },
    [updateViewPrefs],
  );

  // ── R4 task 030 — Header state (App-level, per task brief) ───────────────
  //
  // `searchQuery` is owned here so future tasks (031 facets, 033 list view)
  // can read it. `selectedIds` is the **multi-select** set driving Row 4 of
  // the header (card affordances — task 060). It is INDEPENDENT of
  // `selectedEventId` (which drives the detail panel, task 060+).
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

  // ── R4 task 040 — Card-open modal state ──────────────────────────────────
  //
  // `modalTodoId` is the GUID of the to-do currently rendered inside the
  // <SmartTodoModal>. `null` means the modal is closed. Setting it (via the
  // `OPEN_TODOS_EVENT` window listener below) mounts the modal; the modal's
  // `<` / `>` nav writes back to update the iframe src.
  //
  // Spec FR-16 / FR-17 — the hybrid modal works in BOTH MDA context (this
  // Code Page launched from a parent-form subgrid / Visual Host
  // drill-through) AND Code Page context (standalone Code Page). The shell
  // is host-agnostic by construction (no MDA-only API calls).
  const [modalTodoId, setModalTodoId] = React.useState<string | null>(null);

  // ── R4 task 040 — Filter-set projection for modal navigation ─────────────
  // The modal's `<` / `>` nav walks the CURRENT filter set — the items array
  // from TodoContext (which reflects search + facets per task 031). We
  // memoize the projection so the modal sees a stable record list per
  // items-array identity.
  const modalRecords = React.useMemo(
    () => todosToModalRecords(items),
    [items],
  );

  // ── R4 task 040 — Subscribe to OPEN_TODOS_EVENT ──────────────────────────
  // Task 032's `createToolbarActions` Open handler dispatches a window
  // `CustomEvent` with shape `{selectedIds: string[], firstId: string}`
  // whenever the user clicks the toolbar Open. Task 060 will additionally
  // dispatch the same event from card double-click + the per-card Open icon.
  // Subscribing here makes the modal the SINGLE consumer of the event — any
  // future launcher just dispatches.
  React.useEffect(() => {
    const handler = (ev: Event): void => {
      const detail = (ev as CustomEvent<OpenTodosEventDetail>).detail;
      if (detail?.firstId) {
        setModalTodoId(detail.firstId);
      }
    };
    window.addEventListener(OPEN_TODOS_EVENT, handler);
    return () => {
      window.removeEventListener(OPEN_TODOS_EVENT, handler);
    };
  }, []);

  // ── R4 task 032 — Selection-aware toolbar actions (Open / Delete / Email /
  // Pin) wired via `createToolbarActions` from `./components/Toolbar`
  // (spec FR-08). The action factory closes over `ctx` by reference so each
  // handler reads the LATEST items + selectedIds at click time (avoids
  // recreating handlers on every render).
  //
  // Open  — dispatches `sprk-smarttodo:open-todos` on window. Task 040 will
  //         subscribe and route to <RecordNavigationModalShell> + To Do main
  //         form iframe. Until 040's listener lands, the event is observed
  //         only by `console.info` (smoke-test safe).
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

  // Refresh → re-query todos via the existing context refetch (task 022).
  const handleRefresh = React.useCallback(() => {
    refetch();
  }, [refetch]);

  // "+ New" — stub for task 040. The CreateTodoWizard host already handles
  // the Outlook ribbon `createTodo` launch path (see LaunchCreateTodoWizardHost
  // below); the toolbar "+ New" button will reuse that wizard in a later
  // task. For task 030 we log + leave the wiring open.
  const handleCreateTodo = React.useCallback(() => {
    console.log("[SmartTodo] + New clicked — wired by task 040");
  }, []);

  const {
    primaryWidth,
    detailWidth,
    isDetailVisible,
    showDetail,
    hideDetail,
    splitterHandlers,
    isDragging,
    containerRef,
    currentRatio,
  } = useTwoPanelLayout({
    defaultDetailWidth: 400,
    storageKey: "smarttodo-panel-layout",
  });

  // Wire selectedEventId to panel visibility:
  // open when an item is selected, close when deselected.
  // Use refs to avoid infinite loop — showDetail/hideDetail change refs
  // when isDetailVisible changes, which would re-trigger this effect.
  const showDetailRef = React.useRef(showDetail);
  const hideDetailRef = React.useRef(hideDetail);
  showDetailRef.current = showDetail;
  hideDetailRef.current = hideDetail;

  React.useEffect(() => {
    if (selectedEventId !== null) {
      showDetailRef.current();
    } else {
      hideDetailRef.current();
    }
  }, [selectedEventId]);

  return (
    <div className={styles.page}>
      {/* ── R4 task 030 — 4-row header (FR-06) ──────────────────────────── */}
      <Header
        searchQuery={searchQuery}
        onSearchChange={setSearchQuery}
        onRefresh={handleRefresh}
        onCreateTodo={handleCreateTodo}
        selectedCount={selectedIds.size}
        toolbarActions={toolbarActions}
        viewMode={viewMode}
        onViewModeChange={handleViewModeChange}
      />

      {/* ── Two-pane content — kanban/list + (optional) detail panel ────── */}
      <div ref={containerRef} className={styles.container}>
        {/* Left panel — Kanban Board (default) OR List View (R4 task 033 / FR-09) */}
        <div className={styles.primaryPanel} style={{ width: primaryWidth }}>
          {viewMode === "list" ? (
            <ListView
              items={items}
              onItemClick={selectItem}
              selectedTodoId={selectedEventId}
            />
          ) : (
            <SmartToDo webApi={getWebApi()} userId={getUserId()} />
          )}
        </div>

        {/* Splitter + Detail Panel (only when visible) */}
        {isDetailVisible && (
          <>
            <PanelSplitter
              onMouseDown={splitterHandlers.onMouseDown}
              onKeyDown={splitterHandlers.onKeyDown}
              onDoubleClick={splitterHandlers.onDoubleClick}
              isDragging={isDragging}
              currentRatio={currentRatio}
            />
            <div
              className={mergeClasses(
                styles.detailPanel,
                !isDragging && styles.detailPanelAnimated,
              )}
              style={{ width: detailWidth }}
            >
              <TodoDetailPanel />
            </div>
          </>
        )}
      </div>

      {/* ── R4 task 040 — Card-open modal (HYBRID modal pattern, FR-13/16/17)
          ──────────────────────────────────────────────────────────────────
          Conditionally mounted so the iframe / dialog DOM only exists when
          the modal is open. Closing the modal (X / ESC / backdrop) unmounts
          and disposes the iframe so the next open is a fresh load. */}
      {modalTodoId !== null && (
        <SmartTodoModal
          todos={modalRecords}
          currentId={modalTodoId}
          onNavigateToId={setModalTodoId}
          onClose={() => setModalTodoId(null)}
        />
      )}
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
