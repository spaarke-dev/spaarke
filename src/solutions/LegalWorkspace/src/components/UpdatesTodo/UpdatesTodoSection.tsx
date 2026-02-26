/**
 * UpdatesTodoSection — tabbed container combining the Updates Feed and Smart
 * To Do list into a single card with Fluent UI v9 TabList navigation.
 *
 * Layout:
 *   [Header: section title + refresh button]
 *   [TabList: Updates | To Do — each with count badge]
 *   [Tab panel: ActivityFeed or SmartToDo (embedded mode)]
 *
 * Both tab panels are kept mounted at all times (display toggling) so that:
 *   - Scroll position and filter state are preserved across tab switches
 *   - FeedTodoSync subscriptions remain active in both components
 *   - Optimistic UI updates are not lost when switching tabs
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) only for custom styles
 *   - Dark mode + high-contrast supported automatically via token system
 *   - Follows Microsoft Fluent 2 design system patterns
 */

import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Text,
  Badge,
  Button,
  TabList,
  Tab,
  SelectTabData,
  SelectTabEvent,
} from "@fluentui/react-components";
import {
  AlertRegular,
  CheckboxCheckedRegular,
  ArrowClockwiseRegular,
} from "@fluentui/react-icons";
import { ActivityFeed } from "../ActivityFeed";
import { SmartToDo } from "../SmartToDo";
import type { IWebApi } from "../../types/xrm";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

type TabValue = "updates" | "todo";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const SECTION_HEIGHT = "560px";

const useStyles = makeStyles({
  card: {
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderWidth("1px"),
    ...shorthands.borderStyle("solid"),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    overflow: "hidden",
    flex: "1 1 auto",
    minHeight: SECTION_HEIGHT,
  },

  // ── Header row ──────────────────────────────────────────────────────────
  header: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalS,
    gap: tokens.spacingHorizontalS,
    flexShrink: 0,
  },
  headerTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  headerActions: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXXS,
    paddingBottom: tokens.spacingVerticalXS,
  },

  // ── TabList ─────────────────────────────────────────────────────────────
  tabList: {
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalXS,
    columnGap: tokens.spacingHorizontalM,
    flexShrink: 0,
  },
  tabLabel: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },

  // ── Tab content panels ──────────────────────────────────────────────────
  tabPanel: {
    display: "flex",
    flexDirection: "column",
    flex: "1 1 0",
    overflow: "hidden",
  },
  tabPanelHidden: {
    display: "none",
  },
});

// ---------------------------------------------------------------------------
// TabLabel sub-component (with icon + count badge)
// ---------------------------------------------------------------------------

interface ITabLabelProps {
  label: string;
  icon: React.ReactElement;
  count?: number;
}

const TabLabel: React.FC<ITabLabelProps> = ({ label, icon, count }) => {
  const styles = useStyles();
  return (
    <span className={styles.tabLabel}>
      <span aria-hidden="true">{icon}</span>
      {label}
      {count !== undefined && count > 0 && (
        <Badge
          appearance="filled"
          color="brand"
          size="small"
          aria-label={`${count} item${count === 1 ? "" : "s"}`}
        >
          {count}
        </Badge>
      )}
    </span>
  );
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IUpdatesTodoSectionProps {
  /** Xrm.WebApi reference from the PCF framework context */
  webApi: IWebApi;
  /** GUID of the current user (context.userSettings.userId) */
  userId: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const UpdatesTodoSection: React.FC<IUpdatesTodoSectionProps> = ({
  webApi,
  userId,
}) => {
  const styles = useStyles();

  // Active tab state
  const [activeTab, setActiveTab] = React.useState<TabValue>("updates");

  // Count badges — reported by embedded children
  const [feedCount, setFeedCount] = React.useState<number | undefined>(undefined);
  const [todoCount, setTodoCount] = React.useState<number | undefined>(undefined);

  // Refetch functions — exposed by embedded children
  const feedRefetchRef = React.useRef<(() => void) | null>(null);
  const todoRefetchRef = React.useRef<(() => void) | null>(null);

  const handleFeedRefetchReady = React.useCallback((refetch: () => void) => {
    feedRefetchRef.current = refetch;
  }, []);

  const handleTodoRefetchReady = React.useCallback((refetch: () => void) => {
    todoRefetchRef.current = refetch;
  }, []);

  // Tab switch handler — refetch data when switching tabs so newly
  // flagged/created items appear without a full page reload.
  // Also closes the To Do detail side pane when leaving the To Do tab.
  const handleTabSelect = React.useCallback(
    (_event: SelectTabEvent, data: SelectTabData) => {
      const tab = data.value as TabValue;
      setActiveTab(tab);
      if (tab === "todo") {
        todoRefetchRef.current?.();
      } else if (tab === "updates") {
        feedRefetchRef.current?.();
        // Close the To Do detail side pane when navigating away
        try {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          const xrm = (window.parent as any)?.Xrm ?? (window as any)?.Xrm;
          const pane = xrm?.App?.sidePanes?.getPane("todoDetailPane");
          if (pane) pane.close();
        } catch {
          // Side pane API unavailable — ignore
        }
      }
    },
    []
  );

  // Refresh button — routes to active tab's refetch
  const handleRefresh = React.useCallback(() => {
    if (activeTab === "updates") {
      feedRefetchRef.current?.();
    } else {
      todoRefetchRef.current?.();
    }
  }, [activeTab]);

  const refreshLabel =
    activeTab === "updates" ? "Refresh updates feed" : "Refresh to-do list";

  return (
    <div className={styles.card} role="region" aria-label="Updates and To Do">
      {/* ── Header ────────────────────────────────────────────────────── */}
      <div className={styles.header}>
        <Text className={styles.headerTitle} size={400}>
          Activity
        </Text>
        <div className={styles.headerActions}>
          <Button
            appearance="subtle"
            size="small"
            icon={<ArrowClockwiseRegular />}
            onClick={handleRefresh}
            aria-label={refreshLabel}
          />
        </div>
      </div>

      {/* ── TabList ───────────────────────────────────────────────────── */}
      <TabList
        selectedValue={activeTab}
        onTabSelect={handleTabSelect}
        className={styles.tabList}
        size="medium"
        aria-label="Activity view"
      >
        <Tab value="updates">
          <TabLabel
            label="Updates"
            icon={<AlertRegular fontSize={16} />}
            count={feedCount}
          />
        </Tab>
        <Tab value="todo">
          <TabLabel
            label="To Do"
            icon={<CheckboxCheckedRegular fontSize={16} />}
            count={todoCount}
          />
        </Tab>
      </TabList>

      {/* ── Tab panels — both always mounted, visibility toggled ──── */}
      <div
        className={activeTab === "updates" ? styles.tabPanel : styles.tabPanelHidden}
        role="tabpanel"
        aria-label="Updates feed"
      >
        <ActivityFeed
          embedded
          webApi={webApi}
          userId={userId}
          onCountChange={setFeedCount}
          onRefetchReady={handleFeedRefetchReady}
        />
      </div>

      <div
        className={activeTab === "todo" ? styles.tabPanel : styles.tabPanelHidden}
        role="tabpanel"
        aria-label="To do list"
      >
        <SmartToDo
          embedded
          webApi={webApi}
          userId={userId}
          onCountChange={setTodoCount}
          onRefetchReady={handleTodoRefetchReady}
        />
      </div>
    </div>
  );
};

UpdatesTodoSection.displayName = "UpdatesTodoSection";
