/**
 * WorkspacePane.tsx — Center pane for the SpaarkeAi three-pane shell (R2).
 *
 * Subscribes to the 'workspace' PaneEventBus channel via usePaneEvent and
 * delegates all tab lifecycle work to WorkspaceTabManager. Widget components
 * are resolved lazily from WorkspaceWidgetRegistry — no widget code is bundled
 * at shell startup.
 *
 * Handled PaneEventBus events:
 *   widget_load   — add new tab, resolve widget component, activate tab
 *   widget_update — update existing tab's data payload
 *   widget_action — forward action to the active tab's widget via ref
 *
 * Dispatched PaneEventBus events:
 *   workspace / tab_change — emitted when the active tab changes so
 *                            ContextPaneController can adapt its view
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
import { makeStyles, tokens, Text } from "@fluentui/react-components";
import { BrainCircuitRegular } from "@fluentui/react-icons";
import {
  usePaneEvent,
  useDispatchPaneEvent,
  resolveWorkspaceWidget,
  getWorkspaceWidgetMetadata,
} from "@spaarke/ai-widgets";
import type { WorkspacePaneEvent } from "@spaarke/ai-widgets";
import { WorkspaceTabManager } from "./WorkspaceTabManager";
import type { WorkspaceTabManagerState } from "./WorkspaceTabManager";
import { WorkspaceTabManagerComponent } from "./WorkspaceTabManagerComponent";

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

  emptyState: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    textAlign: "center",
    color: tokens.colorNeutralForeground3,
  },

  emptyIcon: {
    fontSize: "40px",
    color: tokens.colorNeutralForeground4,
    marginBottom: tokens.spacingVerticalS,
  },

  emptyTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },

  emptySubtitle: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    maxWidth: "280px",
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

      // Add the tab — this enforces MAX_TABS eviction internally.
      const tabId = manager.addTab(widgetType, widgetData, displayName);
      syncState();

      // Lazy-resolve the widget component; update the tab once resolved.
      resolveWorkspaceWidget(widgetType).then((Component) => {
        const resolvedMeta = getWorkspaceWidgetMetadata(widgetType);
        manager.resolveTabComponent(tabId, Component, resolvedMeta?.displayName);
        syncState();

        // Dispatch widget_load to the bus so ShellStageManager can advance stage.
        dispatch("workspace", {
          type: "widget_load",
          widgetType,
          tabId,
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

      // If closing the tab changed the active tab, dispatch a tab_change.
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
  // ---------------------------------------------------------------------------

  const { tabs, activeTabId } = tabState;

  if (tabs.length === 0) {
    return (
      <div className={styles.root}>
        <div className={styles.emptyState}>
          <BrainCircuitRegular className={styles.emptyIcon} />
          <Text className={styles.emptyTitle} size={400}>
            Workspace
          </Text>
          <Text className={styles.emptySubtitle} size={200}>
            AI analysis results, document views, and structured outputs will
            appear here as tabs when you run an action.
          </Text>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.root}>
      <WorkspaceTabManagerComponent
        tabs={tabs}
        activeTabId={activeTabId}
        onTabChange={handleTabChange}
        onTabClose={handleTabClose}
      />
    </div>
  );
}
