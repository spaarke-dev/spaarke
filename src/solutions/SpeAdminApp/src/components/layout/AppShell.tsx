/**
 * AppShell.tsx
 *
 * Root layout for the SPE Admin App.
 *
 * Layout:
 *   ┌──────────────────────────────────────────────────────────────────┐
 *   │  SPE Admin       [BU ▾] [Config ▾]  · [Env badge]    v1.0.0     │  ← header
 *   ├──────────────────────────────────────────────────────────────────┤
 *   │  Dashboard  Containers  Container Types  File Browser  …          │  ← TabList
 *   ├──────────────────────────────────────────────────────────────────┤
 *   │  [Page content]                                                  │
 *   └──────────────────────────────────────────────────────────────────┘
 *
 * ADR-021: All styles via makeStyles + tokens. No hard-coded colors.
 * ADR-022: React 18 Code Page — bundled Fluent v9. Not PCF.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  TabList,
  Tab,
  shorthands,
} from "@fluentui/react-components";
import type { SelectTabData } from "@fluentui/react-components";
import {
  Home20Regular,
  Home20Filled,
  Storage20Regular,
  Storage20Filled,
  DocumentBulletList20Regular,
  DocumentBulletList20Filled,
  Search20Regular,
  Search20Filled,
  DeleteDismiss20Regular,
  DeleteDismiss20Filled,
  Shield20Regular,
  Shield20Filled,
  TextBulletList20Regular,
  TextBulletList20Filled,
  Settings20Regular,
  Settings20Filled,
} from "@fluentui/react-icons";
import { BuContextPicker } from "./BuContextPicker";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/** Navigation page identifiers for the SPE Admin App. */
export type SpeAdminPage =
  | "dashboard"
  | "containers"
  | "container-types"
  | "file-browser"
  | "search"
  | "recycle-bin"
  | "security"
  | "audit-log"
  | "settings";

export interface AppShellProps {
  /** Page content rendered in the main area. */
  children: React.ReactNode;
  /** Currently active page — highlights the corresponding tab. */
  activePage?: SpeAdminPage;
  /** Called when the user clicks a tab. */
  onNavigate?: (page: SpeAdminPage) => void;
  /** App version shown in the header. @default "1.0.0" */
  version?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Navigation items
// ─────────────────────────────────────────────────────────────────────────────

interface NavItem {
  page: SpeAdminPage;
  label: string;
  icon: React.ReactElement;
  iconActive: React.ReactElement;
}

/**
 * Tab order follows the administrator's mental model rather than the order the pages were built in:
 * a container type must exist before containers can live under it, so Container Types precedes
 * Containers. (Operator-directed, UAT 2026-08-26.)
 */
const NAV_ITEMS: NavItem[] = [
  { page: "dashboard",       label: "Dashboard",        icon: <Home20Regular />,                iconActive: <Home20Filled /> },
  { page: "container-types", label: "Container Types",  icon: <DocumentBulletList20Regular />,  iconActive: <DocumentBulletList20Filled /> },
  { page: "containers",      label: "Containers",       icon: <Storage20Regular />,             iconActive: <Storage20Filled /> },
  // "file-browser" is intentionally ABSENT from the tab bar as of 2026-08-26. It only ever worked
  // on a container selected on the Containers page, so as a peer tab it was permanently greyed out
  // or showed a "select a container" prompt — a tab whose only content was directions to another
  // tab. It now lives in the browse pane beneath the container list, reached from the Browse
  // toolbar button. The PAGE and its route are deliberately KEPT so existing `?page=file-browser`
  // deep links continue to resolve rather than 404 into the placeholder.
  { page: "search",          label: "Search",           icon: <Search20Regular />,              iconActive: <Search20Filled /> },
  { page: "recycle-bin",     label: "Recycle Bin",      icon: <DeleteDismiss20Regular />,       iconActive: <DeleteDismiss20Filled /> },
  { page: "security",        label: "Security",         icon: <Shield20Regular />,              iconActive: <Shield20Filled /> },
  { page: "audit-log",       label: "Audit Log",        icon: <TextBulletList20Regular />,      iconActive: <TextBulletList20Filled /> },
  // Labelled "Environments", not "Settings": container-type configs moved to the Container Types
  // page (2026-08-26), leaving environments as this tab's only subject. A tab called "Settings"
  // that holds exactly one kind of record is a category pretending to be a page.
  { page: "settings",        label: "Environments",     icon: <Settings20Regular />,            iconActive: <Settings20Filled /> },
];

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  /** Root: full height, column layout. */
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },

  /**
   * App header bar: title on the left, BuContextPicker in the center/right, version on far right.
   */
  header: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalM),
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
    minHeight: "48px",
  },

  appTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    whiteSpace: "nowrap",
    flexShrink: 0,
  },

  headerDivider: {
    width: "1px",
    height: "24px",
    backgroundColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },

  /** Pushes version text to the far right. */
  headerSpacer: {
    flex: "1 1 auto",
  },

  versionText: {
    color: tokens.colorNeutralForeground4,
    whiteSpace: "nowrap",
    flexShrink: 0,
  },

  /**
   * Tab navigation bar below the header.
   * Uses horizontal overflow-x: auto so tabs scroll on narrow viewports
   * rather than wrapping or overflowing the layout.
   */
  tabBar: {
    display: "flex",
    flexDirection: "row",
    alignItems: "stretch",
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
    overflowX: "auto",
    overflowY: "hidden",
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
  },

  /**
   * Main content area — fills remaining vertical space.
   * Pages manage their own internal scroll.
   */
  content: {
    flex: "1 1 auto",
    overflow: "auto",
    minHeight: 0,
    backgroundColor: tokens.colorNeutralBackground1,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// AppShell Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * AppShell — root layout for the SPE Admin App.
 *
 * Renders a header (title + BuContextPicker + version) and a horizontal
 * tab bar (TabList) above the page content area. This layout avoids
 * conflict with the model-driven app's own left-side navigation pane.
 *
 * ADR-021: Fluent v9 TabList/Tab for navigation. All colors via tokens.
 */
export const AppShell: React.FC<AppShellProps> = ({
  children,
  activePage = "dashboard",
  onNavigate,
  version = "1.0.0",
}) => {
  const styles = useStyles();

  const handleTabSelect = React.useCallback(
    (_: React.SyntheticEvent, data: SelectTabData) => {
      onNavigate?.(data.value as SpeAdminPage);
    },
    [onNavigate]
  );

  return (
    <div className={styles.root}>
      {/*
        ── Header ──
        Title row: product name on the left, scope pickers right-aligned, version last.
        The spacer moved ABOVE the pickers (it used to sit after them, which left-packed the
        pickers against the title). Operator-directed, UAT 2026-08-26.
      */}
      <div className={styles.header}>
        <Text className={styles.appTitle} size={400}>
          SharePoint Embedded Admin
        </Text>

        <div className={styles.headerSpacer} />

        <BuContextPicker variant="compact" />

        <div className={styles.headerDivider} aria-hidden="true" />

        <Text className={styles.versionText} size={100}>
          v{version}
        </Text>
      </div>

      {/* ── Tab Navigation ── */}
      <div className={styles.tabBar}>
        <TabList
          selectedValue={activePage}
          onTabSelect={handleTabSelect}
          appearance="subtle"
          size="medium"
        >
          {NAV_ITEMS.map((item) => (
            <Tab
              key={item.page}
              value={item.page}
              icon={activePage === item.page ? item.iconActive : item.icon}
            >
              {item.label}
            </Tab>
          ))}
        </TabList>
      </div>

      {/* ── Page Content ── */}
      <div className={styles.content}>
        {children}
      </div>
    </div>
  );
};
