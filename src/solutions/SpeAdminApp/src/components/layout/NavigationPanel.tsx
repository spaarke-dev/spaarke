import * as React from "react";
import {
  makeStyles,
  tokens,
  NavItem,
  NavSectionHeader,
  NavDivider,
  Tooltip,
} from "@fluentui/react-components";
import {
  Home20Regular,
  Storage20Regular,
  FolderOpen20Regular,
  TextBulletList20Regular,
  Settings20Regular,
  Search20Regular,
  Shield20Regular,
  Home20Filled,
  Storage20Filled,
  FolderOpen20Filled,
  TextBulletList20Filled,
  Settings20Filled,
  Search20Filled,
  Shield20Filled,
  DeleteDismiss20Regular,
  DeleteDismiss20Filled,
  DocumentBulletList20Regular,
  DocumentBulletList20Filled,
} from "@fluentui/react-icons";
import type { SpeAdminPage } from "./AppShell";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Props for the NavigationPanel component.
 */
export interface NavigationPanelProps {
  /**
   * The currently active navigation page.
   * Used to highlight the active nav item with filled icon + accent style.
   */
  activePage: SpeAdminPage;

  /**
   * Callback when the user selects a navigation item.
   * The consumer (AppShell) is responsible for switching the content area.
   */
  onNavigate: (page: SpeAdminPage) => void;

  /**
   * Whether the navigation drawer is expanded.
   * When false (collapsed), only icons are shown and tooltips appear on hover.
   * @default true
   */
  isOpen?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Nav Item Definitions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Internal shape of a navigation item definition.
 */
interface NavItemDef {
  /** Page identifier — maps to SpeAdminPage union type */
  id: SpeAdminPage;
  /** Human-readable label shown when nav is expanded */
  label: string;
  /** Icon shown in inactive / default state */
  icon: React.ReactElement;
  /** Icon shown when this item is the active page */
  iconActive: React.ReactElement;
}

/**
 * Ordered list of primary navigation sections.
 * Icons sourced from @fluentui/react-icons; Regular variant for inactive,
 * Filled variant for active — standard Fluent v9 nav pattern (ADR-021).
 */
const NAV_ITEMS: NavItemDef[] = [
  {
    id: "dashboard",
    label: "Dashboard",
    icon: <Home20Regular />,
    iconActive: <Home20Filled />,
  },
  {
    id: "containers",
    label: "Containers",
    icon: <Storage20Regular />,
    iconActive: <Storage20Filled />,
  },
  {
    id: "container-types",
    label: "Container Types",
    icon: <DocumentBulletList20Regular />,
    iconActive: <DocumentBulletList20Filled />,
  },
  {
    id: "file-browser",
    label: "File Browser",
    icon: <FolderOpen20Regular />,
    iconActive: <FolderOpen20Filled />,
  },
  {
    id: "audit-log",
    label: "Audit Log",
    icon: <TextBulletList20Regular />,
    iconActive: <TextBulletList20Filled />,
  },
  {
    id: "recycle-bin",
    label: "Recycle Bin",
    icon: <DeleteDismiss20Regular />,
    iconActive: <DeleteDismiss20Filled />,
  },
  {
    id: "search",
    label: "Search",
    icon: <Search20Regular />,
    iconActive: <Search20Filled />,
  },
  {
    id: "security",
    label: "Security",
    icon: <Shield20Regular />,
    iconActive: <Shield20Filled />,
  },
  {
    id: "settings",
    label: "Settings",
    icon: <Settings20Regular />,
    iconActive: <Settings20Filled />,
  },
];

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  /**
   * Wrapper for the nav item list.
   * flex: 1 so this fills available body space; overflow-y for long lists.
   */
  navBody: {
    flex: "1 1 auto",
    overflowY: "auto",
    overflowX: "hidden",
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },

  /**
   * Active nav item receives an accent-colored left border via design tokens.
   * Fluent's NavItem does not expose a built-in "selected" visual; we layer
   * our own indicator using a pseudo-element approach.
   * Color uses colorBrandForeground1 (adapts to light / dark / high-contrast).
   */
  navItemActive: {
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// NavigationPanel Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * NavigationPanel — the primary section navigation for the SPE Admin App.
 *
 * Renders five nav items (Dashboard, Containers, File Browser, Audit Log,
 * Settings) with appropriate Fluent v9 icons. Highlights the active item
 * using Filled icon variants and brand accent color (ADR-021).
 *
 * When `isOpen` is false (collapsed icon rail):
 * - Labels are hidden
 * - Tooltips appear on hover/focus to maintain accessibility
 *
 * Usage — within AppShell:
 * ```tsx
 * <NavigationPanel
 *   activePage={activePage}
 *   onNavigate={onNavigate}
 *   isOpen={isNavOpen}
 * />
 * ```
 *
 * @remarks
 * This component is intentionally presentation-only. It does not manage its
 * own navigation state — the consumer (AppShell) owns `activePage` and
 * passes `onNavigate` to update it. This keeps page-switching logic in one
 * place (App.tsx → AppShell → NavigationPanel).
 */
export const NavigationPanel: React.FC<NavigationPanelProps> = ({
  activePage,
  onNavigate,
  isOpen = true,
}) => {
  const styles = useStyles();

  return (
    <div className={styles.navBody} role="navigation" aria-label="Main navigation">
      {/* Section header — only visible when nav is expanded */}
      <NavSectionHeader>{isOpen ? "Administration" : ""}</NavSectionHeader>

      {NAV_ITEMS.map((item) => {
        const isActive = activePage === item.id;

        return (
          /*
           * Tooltip: visible only when nav is collapsed (icon-only rail).
           * When nav is open, labels are displayed — tooltip would duplicate.
           * `visible={false}` prevents tooltip from appearing when expanded.
           */
          <Tooltip
            key={item.id}
            content={item.label}
            relationship="label"
            positioning="after"
            visible={!isOpen ? undefined : false}
          >
            <NavItem
              icon={isActive ? item.iconActive : item.icon}
              value={item.id}
              onClick={() => onNavigate(item.id)}
              aria-current={isActive ? "page" : undefined}
              className={isActive ? styles.navItemActive : undefined}
            >
              {/* Label hidden in collapsed (icon-only) mode */}
              {isOpen ? item.label : ""}
            </NavItem>
          </Tooltip>
        );
      })}

      <NavDivider />
    </div>
  );
};
