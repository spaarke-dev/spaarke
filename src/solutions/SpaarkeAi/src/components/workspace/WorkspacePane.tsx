/**
 * WorkspacePane.tsx — Center pane for the SpaarkeAi three-pane shell (R2).
 *
 * Subscribes to the 'workspace' PaneEventBus channel via usePaneEvent and
 * delegates all tab lifecycle work to WorkspaceTabManager. Widget components
 * are resolved lazily from WorkspaceWidgetRegistry — no widget code is bundled
 * at shell startup.
 *
 * Handled PaneEventBus events:
 *   workspace / widget_load       — add new tab, resolve widget component, activate tab
 *   workspace / widget_update     — update existing tab's data payload
 *   workspace / widget_action     — forward action to the active tab's widget via ref
 *   conversation / playbook-selected — clear tabs (if exclusive) + seed defaultWidgets (AIPU2-102)
 *
 * Dispatched PaneEventBus events:
 *   workspace / tab_change       — emitted when the active tab changes so
 *                                  ContextPaneController can adapt its view
 *   workspace / tab_count_change — emitted when the number of open tabs changes
 *                                  so ShellStageManager can drive Stage 3↔4
 *
 * This component replaces R1's OutputPanel.tsx.
 *
 * @see WorkspaceTabManager    — tab state management (plain TS class)
 * @see WorkspaceTabManagerComponent — tab bar + active widget renderer
 * @see resolveWorkspaceWidget — lazy widget registry
 * @see ADR-021 — Fluent v9 tokens only, dark mode, no hardcoded colors
 * @see ADR-022 — React 19, functional components
 */

import * as React from "react";
import { makeStyles, tokens, Spinner } from "@fluentui/react-components";
import { AppsListRegular } from "@fluentui/react-icons";
import { PaneHeader } from "@spaarke/ui-components";
import {
  usePaneEvent,
  useDispatchPaneEvent,
  resolveWorkspaceWidget,
  getWorkspaceWidgetMetadata,
  useAiSession,
} from "@spaarke/ai-widgets";
import type { WorkspacePaneEvent, ConversationPaneEvent } from "@spaarke/ai-widgets";
import { buildBffApiUrl } from "@spaarke/auth";
import { WorkspaceTabManager } from "./WorkspaceTabManager";
import type {
  ActiveTabSnapshot,
  WorkspaceTabManagerState,
  WorkspaceTabPersistenceSnapshot,
} from "./WorkspaceTabManager";
import { WorkspaceTabManagerComponent } from "./WorkspaceTabManagerComponent";
import { WorkspaceHomeTab } from "./WorkspaceHomeTab";
import { WorkspacePaneMenu } from "./WorkspacePaneMenu";
import {
  logTelemetryError,
  TELEMETRY_TAB_RESTORE_LOAD_FAILURE,
  TELEMETRY_TAB_RESTORE_SAVE_FAILURE,
} from "../../telemetry/errorTelemetry";

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground2,
  },

  // ── First-paint placeholder (rendered for the one tick before the Home tab
  //    effect installs the Home tab) ────────────────────────────────────────
  firstPaintPlaceholder: {
    flex: 1,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
  },
});

// ---------------------------------------------------------------------------
// WorkspacePane
// ---------------------------------------------------------------------------

/**
 * WorkspacePane — center pane for the SpaarkeAi three-pane shell (R2).
 *
 * Owns the WorkspaceTabManager instance and drives React state from it.
 * Delegates tab bar rendering and active widget display to
 * WorkspaceTabManagerComponent.
 */
export function WorkspacePane(): React.JSX.Element {
  const styles = useStyles();
  const dispatch = useDispatchPaneEvent();

  // ---------------------------------------------------------------------------
  // Auth surface — NFR-09 tab persistence (task 065)
  //
  // Per ADR-028: `authenticatedFetch` is obtained from useAiSession() (never
  // snapshotted as a prop or token string). `bffBaseUrl` + `chatSessionId`
  // also come from the session provider so write-through targets the correct
  // session and we can no-op cleanly when no session id is set yet.
  // ---------------------------------------------------------------------------

  const { bffBaseUrl, authenticatedFetch, chatSessionId, isAuthenticated } =
    useAiSession();

  // ---------------------------------------------------------------------------
  // Tab manager — single instance per WorkspacePane mount
  // ---------------------------------------------------------------------------

  // Forwarding ref: the manager's onPersistChange callback dereferences this
  // on every mutation. The actual `persistTabs` function below is rebuilt with
  // useCallback (it captures sessionId/bffBaseUrl) and assigned into the ref
  // on each render — so the manager always calls the latest persistTabs.
  const persistTabsRef = React.useRef<
    ((snapshot: WorkspaceTabPersistenceSnapshot) => void) | null
  >(null);

  // Round 4 Fix 4: Forwarding ref for the active-tab-change signal. Same
  // pattern as persistTabsRef — keeps the manager construction stable while
  // letting the dispatch closure capture the latest `dispatch` reference.
  const activeTabChangeRef = React.useRef<
    ((snapshot: ActiveTabSnapshot) => void) | null
  >(null);

  // Stable manager reference — never recreated across re-renders.
  // The onPersistChange / onActiveTabChange callbacks are themselves stable;
  // they just dispatch through the current ref values (so updates to deps
  // refresh cleanly without re-instantiating the manager).
  const managerRef = React.useRef<WorkspaceTabManager>(
    new WorkspaceTabManager({
      onPersistChange: (snapshot) => {
        persistTabsRef.current?.(snapshot);
      },
      onActiveTabChange: (snapshot) => {
        activeTabChangeRef.current?.(snapshot);
      },
    }),
  );

  // React state mirrors the manager's snapshot; triggers re-renders.
  const [tabState, setTabState] = React.useState<WorkspaceTabManagerState>(() =>
    managerRef.current.getSnapshot()
  );

  /** Sync React state with the current manager snapshot. */
  const syncState = React.useCallback((): void => {
    setTabState(managerRef.current.getSnapshot());
  }, []);

  // ---------------------------------------------------------------------------
  // Debounced write-through — NFR-09 (task 065)
  //
  // The manager fires onPersistChange synchronously on every mutation. We
  // coalesce rapid bursts (e.g. FIFO eviction adding + removing) by buffering
  // the latest snapshot in a ref and flushing once per ~200ms tick. The
  // write-through is best-effort: on failure we log telemetry and continue
  // (in-memory state remains correct, restore on next mount may be stale).
  // ---------------------------------------------------------------------------

  const pendingSnapshotRef =
    React.useRef<WorkspaceTabPersistenceSnapshot | null>(null);
  const persistTimerRef = React.useRef<number | null>(null);

  const persistTabs = React.useCallback(
    (snapshot: WorkspaceTabPersistenceSnapshot): void => {
      pendingSnapshotRef.current = snapshot;
      if (persistTimerRef.current !== null) {
        window.clearTimeout(persistTimerRef.current);
      }
      persistTimerRef.current = window.setTimeout(async () => {
        persistTimerRef.current = null;
        const snap = pendingSnapshotRef.current;
        pendingSnapshotRef.current = null;
        if (!snap) return;
        if (!chatSessionId || !bffBaseUrl || !isAuthenticated) return;

        try {
          const url = buildBffApiUrl(
            bffBaseUrl,
            `/ai/chat/sessions/${encodeURIComponent(chatSessionId)}/tabs`,
          );
          const response = await authenticatedFetch(url, {
            method: "PATCH",
            headers: {
              "Content-Type": "application/json",
              Accept: "application/json",
            },
            body: JSON.stringify(snap),
          });
          // 404 = session not yet known to BFF — treat as benign (best-effort).
          if (!response.ok && response.status !== 404) {
            throw new Error(`HTTP ${response.status}`);
          }
        } catch (err) {
          logTelemetryError(TELEMETRY_TAB_RESTORE_SAVE_FAILURE, {
            sessionId: chatSessionId,
            message: err instanceof Error ? err.message : String(err),
          });
          // Continue — write-through is best-effort. In-memory state is the
          // source of truth until the next successful save.
        }
      }, 200);
    },
    [chatSessionId, bffBaseUrl, isAuthenticated, authenticatedFetch],
  );

  // Update the forwarding ref every render so the manager calls the latest
  // persistTabs (which captures the latest sessionId/bffBaseUrl deps).
  React.useEffect(() => {
    persistTabsRef.current = persistTabs;
  }, [persistTabs]);

  // ---------------------------------------------------------------------------
  // Active-tab signal — Round 4 Fix 4 (2026-05-21)
  //
  // Foundation signal for cross-pane coordination: when the active workspace
  // tab changes, broadcast `active_widget_changed` on the `workspace` channel
  // so future subscribers (Assistant + Context panes) can scope themselves to
  // the active workspace context. NO consumers are wired in this task — this
  // is the signal infrastructure only.
  //
  // The dispatch is mediated by activeTabChangeRef so the WorkspaceTabManager
  // ref stays stable across renders even as `dispatch` evolves.
  // ---------------------------------------------------------------------------

  const broadcastActiveTabChange = React.useCallback(
    (snapshot: ActiveTabSnapshot): void => {
      // Skip events that have no active tab — they're a "no active context"
      // state that subscribers can derive from a separate `session_reset` or
      // `tabs_clear` event when needed.
      if (!snapshot.tabId || !snapshot.widgetType) return;

      dispatch("workspace", {
        type: "active_widget_changed",
        widgetType: snapshot.widgetType,
        widgetData: snapshot.widgetData,
        tabId: snapshot.tabId,
        displayName: snapshot.displayName ?? snapshot.widgetType,
      });
    },
    [dispatch],
  );

  React.useEffect(() => {
    activeTabChangeRef.current = broadcastActiveTabChange;
  }, [broadcastActiveTabChange]);

  // On unmount: cancel any pending timer to avoid late writes against a stale
  // session id. The in-memory snapshot is discarded; the most recent
  // successful write to BFF remains authoritative.
  React.useEffect(() => {
    return () => {
      if (persistTimerRef.current !== null) {
        window.clearTimeout(persistTimerRef.current);
        persistTimerRef.current = null;
      }
    };
  }, []);

  // ---------------------------------------------------------------------------
  // Restore on mount — NFR-09 (task 065)
  //
  // Fetches the persisted tab snapshot for the current chat session and
  // hydrates the manager. 404 is benign (no tabs to restore). Other failures
  // emit telemetry and leave the workspace in its default Home-only state.
  // Guard: restoreFromPersistence() itself no-ops if a non-Home tab is
  // already open, so an in-flight session won't be clobbered if the user
  // opens a tab during the restore window.
  // ---------------------------------------------------------------------------

  React.useEffect(() => {
    if (!chatSessionId || !bffBaseUrl || !isAuthenticated) return;

    let cancelled = false;
    (async () => {
      try {
        const url = buildBffApiUrl(
          bffBaseUrl,
          `/ai/chat/sessions/${encodeURIComponent(chatSessionId)}/tabs`,
        );
        const response = await authenticatedFetch(url, {
          method: "GET",
          headers: { Accept: "application/json" },
        });
        if (cancelled) return;
        if (response.status === 404) return; // no tabs to restore — benign
        if (!response.ok) throw new Error(`HTTP ${response.status}`);

        const snapshot =
          (await response.json()) as WorkspaceTabPersistenceSnapshot;
        if (cancelled) return;

        await managerRef.current.restoreFromPersistence(
          snapshot,
          resolveWorkspaceWidget,
        );
        if (cancelled) return;
        syncState();

        // Notify ShellStageManager about the restored tab count so it can
        // advance to the appropriate stage (Stage 3 / Stage 4).
        const snap = managerRef.current.getSnapshot();
        dispatch("workspace", {
          type: "tab_count_change",
          tabCount: snap.tabs.length,
        });
      } catch (err) {
        if (cancelled) return;
        logTelemetryError(TELEMETRY_TAB_RESTORE_LOAD_FAILURE, {
          sessionId: chatSessionId,
          message: err instanceof Error ? err.message : String(err),
        });
        // Degrade gracefully — workspace continues with Home-only state.
      }
    })();

    return () => {
      cancelled = true;
    };
    // authenticatedFetch is a stable module-level function from @spaarke/auth
    // (returned by useAiSession() but identical reference across renders).
    // Including it in deps would re-fire the effect needlessly.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [chatSessionId, bffBaseUrl, isAuthenticated]);

  // ---------------------------------------------------------------------------
  // Home tab — FR-11: embed the LegalWorkspace experience as a non-closable
  // Home tab containing the user's default workspace layout. The Home tab is
  // installed eagerly on mount via WorkspaceTabManager.ensureHomeTab() (task
  // 011 shipped this factory). WorkspaceHomeTab handles its own per-request
  // BFF fetch via authenticatedFetch (ADR-028).
  // ---------------------------------------------------------------------------

  React.useEffect(() => {
    const manager = managerRef.current;
    const homeTabId = manager.ensureHomeTab(
      "Home",
      /* widgetData */ null,
      /* Component */ WorkspaceHomeTab,
    );
    // If no tab is currently active, activate the Home tab so the user sees
    // workspace content on first paint instead of an empty-state placeholder.
    if (manager.getActiveTab() === null) {
      manager.setActiveTab(homeTabId);
    }
    syncState();
    // Dispatch tab_count_change so ShellStageManager can adapt. The Home tab
    // counts as one tab; subsequent widget tabs add to this count.
    dispatch("workspace", {
      type: "tab_count_change",
      tabCount: manager.getSnapshot().tabs.length,
    });
    // Intentionally empty dep array: install Home tab once per mount. dispatch
    // is stable per useDispatchPaneEvent contract and syncState is stable.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ---------------------------------------------------------------------------
  // PaneEventBus subscription — 'workspace' channel
  // ---------------------------------------------------------------------------

  usePaneEvent("workspace", (event: WorkspacePaneEvent): void => {
    const manager = managerRef.current;

    if (event.type === "widget_load" && !event.tabId) {
      // Guard: ignore our own re-dispatched widget_load confirmations (which carry tabId).
      // Only the server-initiated events (no tabId) should open a new tab.
      const widgetType = event.widgetType ?? "unknown";
      const widgetData = event.widgetData ?? null;

      // Resolve the tab display name with this precedence:
      //   1. Event payload `displayName` (Round 4 Fix 4: lets the menu set the
      //      tab title to a per-instance label such as "Corporate Workspace"
      //      rather than the generic registry label "Workspace").
      //   2. Registry metadata `displayName`.
      //   3. The raw widgetType string as last resort.
      const meta = getWorkspaceWidgetMetadata(widgetType);
      const displayName =
        event.displayName ?? meta?.displayName ?? widgetType;

      // Add the tab — this enforces MAX_WORKSPACE_TABS eviction internally.
      const tabId = manager.addTab(widgetType, widgetData, displayName);
      syncState();

      // Lazy-resolve the widget component; update the tab once resolved.
      resolveWorkspaceWidget(widgetType).then((Component) => {
        const resolvedMeta = getWorkspaceWidgetMetadata(widgetType);
        // Round 4 Fix 4: preserve a per-instance displayName from the event
        // payload (e.g. "Corporate Workspace") over the registry's generic
        // label (e.g. "Workspace"). Pass `undefined` for displayName when the
        // event carried one so resolveTabComponent does not overwrite it.
        manager.resolveTabComponent(
          tabId,
          Component,
          event.displayName ? undefined : resolvedMeta?.displayName,
        );
        syncState();

        // Snapshot the current tab count after resolution so ShellStageManager
        // can advance stage (Stage 2 → Stage 3 / Stage 4).
        const snapshot = manager.getSnapshot();
        const currentTabCount = snapshot.tabs.length;

        // Dispatch widget_load WITH tabId so ShellStageManager reacts to it
        // (server-initiated events carry no tabId; this is the confirmation).
        // tabCount is included so ShellStageManager can also derive Stage 4.
        dispatch("workspace", {
          type: "widget_load",
          widgetType,
          tabId,
          ...(currentTabCount > 0 ? { tabCount: currentTabCount } : {}),
        });

        // Dispatch tab_count_change so ShellStageManager can drive Stage 3↔4.
        dispatch("workspace", {
          type: "tab_count_change",
          tabCount: currentTabCount,
        });
      });
    } else if (event.type === "widget_update") {
      if (event.tabId) {
        manager.updateTab(event.tabId, event.widgetData ?? null);
        syncState();
      }
    } else if (event.type === "widget_action") {
      // Forward widget_action events are handled by the widget itself via
      // the bus — WorkspacePane is a transparent router here.
      // No tab-manager state change needed.
    }
  });

  // ---------------------------------------------------------------------------
  // PaneEventBus subscription — 'conversation' channel (AIPU2-102)
  //
  // Receives `playbook-selected` events dispatched by PlaybookGalleryWidget
  // when the user picks a playbook from the gallery in the Context pane.
  //
  // Behaviour:
  //   isExclusive === true  → clear all existing tabs, then seed defaultWidgets
  //   isExclusive === false → keep existing tabs, then seed defaultWidgets (additive)
  //   defaultWidgets empty  → no tab seeding (workspace retains current state)
  //
  // Each defaultWidget follows the same addTab → resolveWorkspaceWidget path
  // used by server-initiated widget_load events, ensuring identical tab lifecycle.
  // ---------------------------------------------------------------------------

  usePaneEvent("conversation", (event: ConversationPaneEvent): void => {
    if (event.type !== "playbook-selected") return;

    const manager = managerRef.current;
    const defaultWidgets = event.defaultWidgets ?? [];
    const isExclusive = event.isExclusive ?? false;

    // Clear all existing tabs when the playbook is exclusive (guardrail mode).
    if (isExclusive && manager.getSnapshot().tabs.length > 0) {
      manager.clearAllTabs();
      syncState();
      // Emit tabs_clear so subscribers (e.g. ContextPaneController) can reset.
      dispatch("workspace", { type: "tabs_clear" });
    }

    // Seed each default widget as a new tab.
    // When defaultWidgets is empty the workspace retains its current state.
    for (const widgetConfig of defaultWidgets) {
      const widgetType = widgetConfig.widgetType;
      const widgetData = widgetConfig.widgetData ?? null;
      const meta = getWorkspaceWidgetMetadata(widgetType);
      const displayName = widgetConfig.displayName ?? meta?.displayName ?? widgetType;

      const tabId = manager.addTab(widgetType, widgetData, displayName);
      syncState();

      // Lazy-resolve the widget component — same pattern as workspace channel.
      resolveWorkspaceWidget(widgetType).then((Component) => {
        const resolvedMeta = getWorkspaceWidgetMetadata(widgetType);
        manager.resolveTabComponent(tabId, Component, resolvedMeta?.displayName);
        syncState();

        // Dispatch widget_load (with tabId) so ShellStageManager can advance stage.
        dispatch("workspace", { type: "widget_load", widgetType, tabId });
      });
    }
  });

  // ---------------------------------------------------------------------------
  // Tab change handler — called by WorkspaceTabManagerComponent
  // ---------------------------------------------------------------------------

  const handleTabChange = React.useCallback(
    (tabId: string): void => {
      const manager = managerRef.current;
      manager.setActiveTab(tabId);
      syncState();

      // Find the newly active tab to include widget info in the event.
      const activeTab = manager.getActiveTab();

      // Dispatch tab_change so ContextPaneController can adapt its view.
      dispatch("workspace", {
        type: "tab_change",
        tabId,
        widgetType: activeTab?.widgetType,
        widgetData: activeTab?.widgetData,
      });
    },
    [dispatch, syncState]
  );

  // ---------------------------------------------------------------------------
  // Tab close handler — called by WorkspaceTabManagerComponent
  // ---------------------------------------------------------------------------

  const handleTabClose = React.useCallback(
    (tabId: string): void => {
      const manager = managerRef.current;
      const newActiveId = manager.closeTab(tabId);
      syncState();

      const snapshot = manager.getSnapshot();
      const currentTabCount = snapshot.tabs.length;

      // Dispatch tab_count_change so ShellStageManager can revert Stage 4 → Stage 3
      // when the user closes tabs down to one, or Stage 3 → Stage 1 when all tabs close.
      dispatch("workspace", {
        type: "tab_count_change",
        tabCount: currentTabCount,
      });

      // If closing the tab changed the active tab, dispatch a tab_change so
      // ContextPaneController can adapt its view to the new active widget.
      if (newActiveId !== null) {
        const newActive = manager.getActiveTab();
        dispatch("workspace", {
          type: "tab_change",
          tabId: newActiveId,
          widgetType: newActive?.widgetType,
          widgetData: newActive?.widgetData,
        });
      }
    },
    [dispatch, syncState]
  );

  // ---------------------------------------------------------------------------
  // Render
  //
  // FR-10: Render the shared <PaneHeader> at the top of every paint, with the
  // brand-colored AppsListRegular icon.
  //
  // FR-12 (task 032): `PaneHeader.rightSlot` hosts `WorkspacePaneMenu` — a
  // Fluent v9 Dropdown that replaces the legacy tab bar. It surfaces (1) open
  // tabs with close affordances, (2) the pinned Home tab, (3) workspace
  // switching + "+ New Workspace" wizard launch, and (4) edit current
  // workspace. The menu is fed tab state from `WorkspaceTabManager` snapshots
  // via the `tabs` / `activeTabId` props and dispatches selection / close back
  // through the existing `handleTabChange` / `handleTabClose` callbacks.
  //
  // FR-11: The Home tab is installed eagerly in the mount effect above, so
  // `tabs.length === 0` is only reachable during the single render that
  // happens before that effect runs. We render a minimal Spinner placeholder
  // under the standard <PaneHeader> in that window so there's no flash of
  // missing content. Task 031 removed the legacy Stage-1 landing widget that
  // previously occupied this branch.
  // ---------------------------------------------------------------------------

  const { tabs, activeTabId } = tabState;

  const header = (
    <PaneHeader
      title="Workspace"
      icon={<AppsListRegular />}
      rightSlot={
        <WorkspacePaneMenu
          tabs={tabs}
          activeTabId={activeTabId}
          onTabSelect={handleTabChange}
          onTabClose={handleTabClose}
        />
      }
    />
  );

  if (tabs.length === 0) {
    // First-paint placeholder. With the Home tab installed in the mount
    // effect, this branch is reachable only for the single render before the
    // effect commits.
    return (
      <div className={styles.root} data-testid="workspace-first-paint">
        {header}
        <div className={styles.firstPaintPlaceholder}>
          <Spinner size="tiny" />
        </div>
      </div>
    );
  }

  return (
    <div className={styles.root}>
      {header}
      <WorkspaceTabManagerComponent
        tabs={tabs}
        activeTabId={activeTabId}
        onTabChange={handleTabChange}
        onTabClose={handleTabClose}
      />
    </div>
  );
}
