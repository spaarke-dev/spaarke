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
import {
  createXrmDataService,
  createXrmNavigationService,
} from "@spaarke/ui-components/utils";
import { resolveRuntimeConfig, initAuth, authenticatedFetch } from "@spaarke/auth";
import { TodoProvider, useTodoContext } from "./context/TodoContext";
import { SmartToDo } from "./components/SmartToDo";
import { TodoDetailPanel } from "./components/TodoDetailPanel";
import { getWebApi, getUserId, getSpeContainerIdFromBusinessUnit } from "./services/xrmProvider";
import { useLaunchContext } from "./hooks/useLaunchContext";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: "flex",
    height: "100%",
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
  const { selectedEventId } = useTodoContext();

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
    <div ref={containerRef} className={styles.container}>
      {/* Left panel — Kanban Board */}
      <div className={styles.primaryPanel} style={{ width: primaryWidth }}>
        <SmartToDo webApi={getWebApi()} userId={getUserId()} />
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
  const isCreateTodoLaunch = launchContext?.action === "createTodo";

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
      initialRegarding={launchContext?.initialRegarding}
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
