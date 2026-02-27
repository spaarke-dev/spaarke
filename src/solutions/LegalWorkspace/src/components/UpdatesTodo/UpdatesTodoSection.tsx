/**
 * UpdatesTodoSection — tabbed container combining the Updates Feed, Smart
 * To Do list, and Matters/Projects/Invoices record tabs into a single card
 * with Fluent UI v9 TabList navigation.
 *
 * Layout:
 *   [TabList: Latest Updates | To Do List | Matters | Projects | Invoices]
 *   [Tab panel — embedded component]
 *
 * All tab panels are kept mounted at all times (display toggling) so that:
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
  Badge,
  TabList,
  Tab,
  SelectTabData,
  SelectTabEvent,
} from "@fluentui/react-components";
import {
  AlertRegular,
  CheckboxCheckedRegular,
  GavelRegular,
  TaskListSquareLtrRegular,
  ReceiptRegular,
  InfoRegular,
} from "@fluentui/react-icons";
import { ActivityFeed } from "../ActivityFeed";
import { SmartToDo } from "../SmartToDo";
import { MattersTab, ProjectsTab, InvoicesTab } from "../RecordCards";
import type { IWebApi } from "../../types/xrm";
import { useDataverseService } from "../../hooks/useDataverseService";
import { useUserContactId } from "../../hooks/useUserContactId";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

type TabValue = "updates" | "todo" | "matters" | "projects" | "invoices";

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

  // ── TabList ─────────────────────────────────────────────────────────────
  tabList: {
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingTop: "10px",
    paddingBottom: tokens.spacingVerticalS,
    columnGap: tokens.spacingHorizontalM,
    flexShrink: 0,
  },
  tabLabel: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
  },

  // ── Toolbar (Matters / Projects / Invoices only) ────────────────────────
  toolbar: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "flex-end",
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
    minHeight: "36px",
  },
  toolbarIcon: {
    color: tokens.colorNeutralForeground3,
    cursor: "pointer",
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
  const service = useDataverseService(webApi);
  const { contactId } = useUserContactId(service, userId);

  // Active tab state
  const [activeTab, setActiveTab] = React.useState<TabValue>("updates");

  // Count badges — reported by embedded children
  const [feedCount, setFeedCount] = React.useState<number | undefined>(undefined);
  const [todoCount, setTodoCount] = React.useState<number | undefined>(undefined);
  const [mattersCount, setMattersCount] = React.useState<number | undefined>(undefined);
  const [projectsCount, setProjectsCount] = React.useState<number | undefined>(undefined);
  const [invoicesCount, setInvoicesCount] = React.useState<number | undefined>(undefined);

  // Refetch functions — exposed by embedded children
  const feedRefetchRef = React.useRef<(() => void) | null>(null);
  const todoRefetchRef = React.useRef<(() => void) | null>(null);
  const mattersRefetchRef = React.useRef<(() => void) | null>(null);
  const projectsRefetchRef = React.useRef<(() => void) | null>(null);
  const invoicesRefetchRef = React.useRef<(() => void) | null>(null);

  const handleFeedRefetchReady = React.useCallback((refetch: () => void) => {
    feedRefetchRef.current = refetch;
  }, []);

  const handleTodoRefetchReady = React.useCallback((refetch: () => void) => {
    todoRefetchRef.current = refetch;
  }, []);

  const handleMattersRefetchReady = React.useCallback((refetch: () => void) => {
    mattersRefetchRef.current = refetch;
  }, []);

  const handleProjectsRefetchReady = React.useCallback((refetch: () => void) => {
    projectsRefetchRef.current = refetch;
  }, []);

  const handleInvoicesRefetchReady = React.useCallback((refetch: () => void) => {
    invoicesRefetchRef.current = refetch;
  }, []);

  // Tab switch handler — refetch data when switching tabs so newly
  // flagged/created items appear without a full page reload.
  // Also closes the To Do detail side pane when leaving the To Do tab.
  const handleTabSelect = React.useCallback(
    (_event: SelectTabEvent, data: SelectTabData) => {
      const tab = data.value as TabValue;
      setActiveTab(tab);

      // Close the To Do detail side pane when navigating away from To Do tab
      if (tab !== "todo") {
        try {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          const xrm = (window.top as any)?.Xrm ?? (window.parent as any)?.Xrm ?? (window as any)?.Xrm;
          const pane = xrm?.App?.sidePanes?.getPane("todoDetailPane");
          if (pane) pane.close();
        } catch {
          // Side pane API unavailable — ignore
        }
      }

      // Refetch the target tab's data
      switch (tab) {
        case "updates":
          feedRefetchRef.current?.();
          break;
        case "todo":
          todoRefetchRef.current?.();
          break;
        case "matters":
          mattersRefetchRef.current?.();
          break;
        case "projects":
          projectsRefetchRef.current?.();
          break;
        case "invoices":
          invoicesRefetchRef.current?.();
          break;
      }
    },
    []
  );

  return (
    <div className={styles.card} role="region" aria-label="Activity and Records">
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
            label="Latest Updates"
            icon={<AlertRegular fontSize={16} />}
            count={feedCount}
          />
        </Tab>
        <Tab value="todo">
          <TabLabel
            label="To Do List"
            icon={<CheckboxCheckedRegular fontSize={16} />}
            count={todoCount}
          />
        </Tab>
        <Tab value="matters">
          <TabLabel
            label="Matters"
            icon={<GavelRegular fontSize={16} />}
            count={mattersCount}
          />
        </Tab>
        <Tab value="projects">
          <TabLabel
            label="Projects"
            icon={<TaskListSquareLtrRegular fontSize={16} />}
            count={projectsCount}
          />
        </Tab>
        <Tab value="invoices">
          <TabLabel
            label="Invoices"
            icon={<ReceiptRegular fontSize={16} />}
            count={invoicesCount}
          />
        </Tab>
      </TabList>

      {/* ── Tab panels — all always mounted, visibility toggled ──── */}
      <div
        className={activeTab === "updates" ? styles.tabPanel : styles.tabPanelHidden}
        role="tabpanel"
        aria-label="Latest updates feed"
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

      <div
        className={activeTab === "matters" ? styles.tabPanel : styles.tabPanelHidden}
        role="tabpanel"
        aria-label="Matters list"
      >
        <div className={styles.toolbar} role="toolbar" aria-label="Matters toolbar">
          <InfoRegular fontSize={18} className={styles.toolbarIcon} aria-label="Information" />
        </div>
        <MattersTab
          service={service}
          userId={userId}
          contactId={contactId}
          onCountChange={setMattersCount}
          onRefetchReady={handleMattersRefetchReady}
        />
      </div>

      <div
        className={activeTab === "projects" ? styles.tabPanel : styles.tabPanelHidden}
        role="tabpanel"
        aria-label="Projects list"
      >
        <div className={styles.toolbar} role="toolbar" aria-label="Projects toolbar">
          <InfoRegular fontSize={18} className={styles.toolbarIcon} aria-label="Information" />
        </div>
        <ProjectsTab
          service={service}
          userId={userId}
          contactId={contactId}
          onCountChange={setProjectsCount}
          onRefetchReady={handleProjectsRefetchReady}
        />
      </div>

      <div
        className={activeTab === "invoices" ? styles.tabPanel : styles.tabPanelHidden}
        role="tabpanel"
        aria-label="Invoices list"
      >
        <div className={styles.toolbar} role="toolbar" aria-label="Invoices toolbar">
          <InfoRegular fontSize={18} className={styles.toolbarIcon} aria-label="Information" />
        </div>
        <InvoicesTab
          service={service}
          userId={userId}
          contactId={contactId}
          onCountChange={setInvoicesCount}
          onRefetchReady={handleInvoicesRefetchReady}
        />
      </div>
    </div>
  );
};

UpdatesTodoSection.displayName = "UpdatesTodoSection";
