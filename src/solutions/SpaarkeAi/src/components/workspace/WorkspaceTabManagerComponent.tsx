/**
 * WorkspaceTabManagerComponent.tsx — Tab bar + active widget renderer for WorkspacePane.
 *
 * Renders the Fluent v9 TabList tab bar and the active workspace widget below it.
 * Each tab shows the widget's displayName and a close button. The active tab's
 * resolved widget component is mounted; inactive tabs are unmounted (not hidden)
 * to avoid accumulating memory and network connections from multiple live widgets.
 *
 * Props are driven entirely by WorkspaceTabManagerState from WorkspaceTabManager.
 * This component has no internal state — it is a pure renderer driven by WorkspacePane.
 *
 * Loading state: when a tab's Component is null (registry promise still pending),
 * a Fluent Spinner is rendered in the content area so the user has immediate
 * feedback that the widget is being loaded.
 *
 * @see WorkspacePane          — owner component that manages tab state
 * @see WorkspaceTabManager    — plain TS class that manages tab array state
 * @see ADR-021 — Fluent v9 tokens only, dark mode, no hardcoded colors
 */

import * as React from "react";
import {
  makeStyles,
  mergeClasses,
  tokens,
  TabList,
  Tab,
  Spinner,
  Text,
  Button,
  Tooltip,
} from "@fluentui/react-components";
import {
  Dismiss12Regular,
  WarningRegular,
} from "@fluentui/react-icons";
import type { WorkspaceTab } from "./WorkspaceTabManager";
import type { WorkspaceWidgetProps } from "@spaarke/ai-widgets";

// NOTE (task 098 — 2026-05-22): the per-tab pin button was removed from
// every tab row. Pin state is still owned by `services/pinnedWorkspaces.ts`
// (localStorage `spaarke:workspace:pinned-list`), but the only UI surface for
// toggling it is now the WorkspacePaneMenu dropdown. Auto-open of pinned
// workspaces on cold load is unchanged (see WorkspacePane mount effect).

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
  },

  // Tab bar strip — sits at the top, never shrinks.
  tabBar: {
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke1,
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    minHeight: "40px",
    overflowX: "auto",
    overflowY: "hidden",
  },

  // The TabList itself — let it grow to fill available width.
  tabList: {
    flexGrow: 1,
  },

  // Individual Tab inner wrapper — label + close button.
  tabContent: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    maxWidth: "160px",
  },

  // Tab title — task 098 (2026-05-22): bumped one Fluent v9 step
  // (fontSizeBase200 → fontSizeBase300) per operator feedback. The tab is
  // still visually a tab (TabList size="small") but the label is now slightly
  // more prominent, matching the pane title proportions polished in Wave 1.
  tabLabel: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
  },

  // Loading badge inside the tab (replaces label while resolving).
  // Kept at fontSizeBase200 — the spinner + ellipsis row is intentionally
  // less prominent than the resolved title; bumping it would crowd the row.
  tabLoadingBadge: {
    display: "inline-flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXXS,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },

  // Close button — task 098 (2026-05-22): downsized via Dismiss12Regular
  // (the 12px icon variant) so the × is visually subordinate to the bumped
  // tab title. Button hit area kept at 16×16 for accessibility (WCAG min
  // target ~24px is relaxed for icon-inside-tab affordances per Fluent v9
  // tab pattern; the surrounding tab itself is the primary 40px target).
  closeButton: {
    minWidth: "unset",
    height: "16px",
    width: "16px",
    padding: "0",
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
    ":hover": {
      color: tokens.colorNeutralForeground1,
      backgroundColor: tokens.colorNeutralBackground3Hover,
    },
  },

  // Content area — grows to fill remaining height.
  content: {
    flex: 1,
    overflow: "auto",
    backgroundColor: tokens.colorNeutralBackground2,
  },

  // Loading state within the content area.
  loadingState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },

  // Error state within the content area.
  errorState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    gap: tokens.spacingVerticalS,
    color: tokens.colorPaletteRedForeground1,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    textAlign: "center",
  },

  errorIcon: {
    fontSize: "32px",
    color: tokens.colorPaletteRedForeground1,
  },

  // Widget wrapper — fills the content area.
  widgetWrapper: {
    height: "100%",
    width: "100%",
  },
});

// ---------------------------------------------------------------------------
// WorkspaceTabManagerComponentProps
// ---------------------------------------------------------------------------

export interface WorkspaceTabManagerComponentProps {
  /** Current ordered list of tabs from WorkspaceTabManager.getSnapshot(). */
  tabs: WorkspaceTab[];
  /** Id of the currently active tab, or null if no tabs exist. */
  activeTabId: string | null;
  /** Called when the user clicks a different tab in the tab bar. */
  onTabChange: (tabId: string) => void;
  /** Called when the user clicks the close button on a tab. */
  onTabClose: (tabId: string) => void;
}

// ---------------------------------------------------------------------------
// ActiveWidgetContent — renders the active tab's resolved widget
// ---------------------------------------------------------------------------

interface ActiveWidgetContentProps {
  tab: WorkspaceTab;
  styles: ReturnType<typeof useStyles>;
}

function ActiveWidgetContent({ tab, styles }: ActiveWidgetContentProps): React.JSX.Element {
  // Loading — registry promise not yet resolved.
  if (tab.isLoading || tab.Component === null) {
    return (
      <div className={styles.loadingState}>
        <Spinner size="medium" label={`Loading ${tab.displayName}…`} />
      </div>
    );
  }

  const Widget = tab.Component as React.ComponentType<WorkspaceWidgetProps>;

  return (
    <div className={styles.widgetWrapper}>
      <Widget
        data={tab.widgetData}
        widgetType={tab.widgetType}
        isLoading={false}
      />
    </div>
  );
}

// ---------------------------------------------------------------------------
// WorkspaceTabManagerComponent
// ---------------------------------------------------------------------------

/**
 * Pure presenter — renders the tab bar and the active widget content area.
 *
 * All state is owned by WorkspacePane / WorkspaceTabManager. This component
 * is stateless: every user interaction fires a callback prop (onTabChange,
 * onTabClose) so WorkspacePane can update the manager and pass new props down.
 */
export function WorkspaceTabManagerComponent({
  tabs,
  activeTabId,
  onTabChange,
  onTabClose,
}: WorkspaceTabManagerComponentProps): React.JSX.Element {
  const styles = useStyles();

  // Resolve the active tab record.
  const activeTab = tabs.find((t) => t.id === activeTabId) ?? null;

  // ---------------------------------------------------------------------------
  // Tab close — stop propagation so clicking the X does not also activate the tab.
  // ---------------------------------------------------------------------------

  const handleCloseClick = React.useCallback(
    (e: React.MouseEvent, tabId: string): void => {
      e.stopPropagation();
      onTabClose(tabId);
    },
    [onTabClose]
  );

  // ---------------------------------------------------------------------------
  // Pin toggle previously lived here (task 092). Removed in task 098 — pin UX
  // now lives only in WorkspacePaneMenu's "Select Workspace" section. The
  // localStorage contract (`spaarke:workspace:pinned-list`) and the cold-load
  // auto-open behavior in WorkspacePane are unchanged.
  // ---------------------------------------------------------------------------
  // Fluent TabList value — must be a string matching the selected Tab's value.
  // ---------------------------------------------------------------------------

  const handleTabListSelect = React.useCallback(
    (_e: React.SyntheticEvent, data: { value: unknown }): void => {
      if (typeof data.value === "string") {
        onTabChange(data.value);
      }
    },
    [onTabChange]
  );

  return (
    <div className={styles.root}>
      {/* ------------------------------------------------------------------ */}
      {/* Tab bar                                                              */}
      {/* ------------------------------------------------------------------ */}
      <div className={styles.tabBar}>
        <TabList
          className={styles.tabList}
          selectedValue={activeTabId ?? undefined}
          onTabSelect={handleTabListSelect}
          size="small"
          appearance="subtle"
        >
          {tabs.map((tab) => {
            // Task 098 (2026-05-22): the inline per-tab pin button was
            // removed (operator: "pin belongs in the workspace selection
            // surface, not on every open tab"). Tab rows now contain only
            // the label + close affordance.
            return (
              <Tab
                key={tab.id}
                value={tab.id}
                data-testid={`workspace-tab-${tab.id}`}
              >
                <div className={styles.tabContent}>
                  {tab.isLoading ? (
                    <span className={styles.tabLoadingBadge}>
                      <Spinner size="extra-tiny" />
                      <span className={styles.tabLabel}>{tab.displayName}</span>
                    </span>
                  ) : (
                    <span className={styles.tabLabel} title={tab.displayName}>
                      {tab.displayName}
                    </span>
                  )}

                  <Tooltip
                    content={`Close ${tab.displayName}`}
                    relationship="label"
                    positioning="below"
                  >
                    <Button
                      className={mergeClasses(styles.closeButton)}
                      appearance="subtle"
                      icon={<Dismiss12Regular />}
                      size="small"
                      aria-label={`Close ${tab.displayName}`}
                      data-testid={`workspace-tab-close-${tab.id}`}
                      onClick={(e) => handleCloseClick(e, tab.id)}
                    />
                  </Tooltip>
                </div>
              </Tab>
            );
          })}
        </TabList>
      </div>

      {/* ------------------------------------------------------------------ */}
      {/* Active tab content                                                   */}
      {/* ------------------------------------------------------------------ */}
      <div className={styles.content}>
        {activeTab !== null ? (
          <ActiveWidgetContent tab={activeTab} styles={styles} />
        ) : (
          <div className={styles.errorState}>
            <WarningRegular className={styles.errorIcon} />
            <Text size={300}>No active tab</Text>
          </div>
        )}
      </div>
    </div>
  );
}
