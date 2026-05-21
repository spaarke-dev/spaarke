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
} from "@spaarke/ai-widgets";
import type { WorkspacePaneEvent, ConversationPaneEvent } from "@spaarke/ai-widgets";
import { WorkspaceTabManager } from "./WorkspaceTabManager";
import type { WorkspaceTabManagerState } from "./WorkspaceTabManager";
import { WorkspaceTabManagerComponent } from "./WorkspaceTabManagerComponent";
import { WorkspaceHomeTab } from "./WorkspaceHomeTab";
import { WorkspacePaneMenu } from "./WorkspacePaneMenu";

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
  // Tab manager — single instance per WorkspacePane mount
  // ---------------------------------------------------------------------------

  // Stable manager reference — never recreated across re-renders.
  const managerRef = React.useRef<WorkspaceTabManager>(new WorkspaceTabManager());

  // React state mirrors the manager's snapshot; triggers re-renders.
  const [tabState, setTabState] = React.useState<WorkspaceTabManagerState>(() =>
    managerRef.current.getSnapshot()
  );

  /** Sync React state with the current manager snapshot. */
  const syncState = React.useCallback((): void => {
    setTabState(managerRef.current.getSnapshot());
  }, []);

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

      // Retrieve optional metadata for the display name.
      const meta = getWorkspaceWidgetMetadata(widgetType);
      const displayName = meta?.displayName ?? widgetType;

      // Add the tab — this enforces MAX_WORKSPACE_TABS eviction internally.
      const tabId = manager.addTab(widgetType, widgetData, displayName);
      syncState();

      // Lazy-resolve the widget component; update the tab once resolved.
      resolveWorkspaceWidget(widgetType).then((Component) => {
        const resolvedMeta = getWorkspaceWidgetMetadata(widgetType);
        manager.resolveTabComponent(tabId, Component, resolvedMeta?.displayName);
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
