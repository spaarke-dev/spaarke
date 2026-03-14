import * as React from "react";
import {
  makeStyles,
  tokens,
  Button,
  Text,
  Tooltip,
  NavDrawer,
  NavDrawerBody,
  NavDrawerHeader,
  shorthands,
} from "@fluentui/react-components";
import {
  PanelLeftExpand20Regular,
  PanelLeftContract20Regular,
} from "@fluentui/react-icons";
import { NavigationPanel } from "./NavigationPanel";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Navigation page identifiers for the SPE Admin App.
 * These correspond to the main sections of the admin interface.
 */
export type SpeAdminPage =
  | "dashboard"
  | "containers"
  | "container-types"
  | "file-browser"
  | "recycle-bin"
  | "audit-log"
  | "security"
  | "settings"
  | "search";

/**
 * Props for the AppShell component.
 */
export interface AppShellProps {
  /**
   * The content to render in the main content area.
   * Typically this will be the active page component.
   */
  children: React.ReactNode;

  /**
   * The currently active navigation page.
   * Used to highlight the active nav item.
   */
  activePage?: SpeAdminPage;

  /**
   * Callback when the user navigates to a different page.
   * The consumer is responsible for rendering the appropriate page content.
   */
  onNavigate?: (page: SpeAdminPage) => void;

  /**
   * App version string displayed in the nav footer.
   * @default "1.0.0"
   */
  version?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Breakpoints
// ─────────────────────────────────────────────────────────────────────────────

/** Width (px) below which the nav panel collapses automatically. */
const NARROW_BREAKPOINT = 768;

/** Width (px) of the expanded nav drawer. */
const NAV_OPEN_WIDTH = 220;

/** Width (px) of the collapsed nav drawer (icon-only rail). */
const NAV_COLLAPSED_WIDTH = 48;

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  /**
   * Root container: full viewport height, flex row.
   * Background and foreground colors use Fluent design tokens so they adapt
   * to light / dark / high-contrast themes automatically (ADR-021).
   */
  root: {
    display: "flex",
    flexDirection: "row",
    height: "100%",
    width: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },

  /**
   * NavDrawer host — fixed-width panel on the left.
   * We rely on the NavDrawer component for the open/closed transition.
   */
  navDrawer: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground2,
    borderRightWidth: "1px",
    borderRightStyle: "solid",
    borderRightColor: tokens.colorNeutralStroke2,
  },

  navHeader: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalS),
    padding: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalS,
    minHeight: "52px",
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
  },

  navHeaderTitle: {
    flex: "1 1 auto",
    overflow: "hidden",
    whiteSpace: "nowrap",
    textOverflow: "ellipsis",
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },

  navHeaderCollapsed: {
    justifyContent: "center",
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
  },

  navBody: {
    flex: "1 1 auto",
    overflowY: "auto",
    overflowX: "hidden",
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },

  navFooter: {
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
  },

  navFooterText: {
    color: tokens.colorNeutralForeground4,
    whiteSpace: "nowrap",
    overflow: "hidden",
    textOverflow: "ellipsis",
  },

  /**
   * Main content area: fills remaining horizontal space.
   * Uses flex column so page content can stretch to full height.
   */
  content: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
    minWidth: 0,
    backgroundColor: tokens.colorNeutralBackground1,
  },

  /**
   * Mobile header bar (only visible when the nav is fully collapsed on narrow screens).
   * Contains the hamburger button to open the drawer.
   */
  mobileHeader: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalS),
    padding: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
  },

  mobileHeaderTitle: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },

  /**
   * Content scroll area inside the main content region.
   * Pages are responsible for their own internal layout.
   */
  contentInner: {
    flex: "1 1 auto",
    overflow: "auto",
    minHeight: 0,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Hooks
// ─────────────────────────────────────────────────────────────────────────────

/**
 * useIsNarrow — returns true when the viewport is narrower than NARROW_BREAKPOINT.
 * Responds to window resize events.
 */
function useIsNarrow(): boolean {
  const [isNarrow, setIsNarrow] = React.useState(
    () => window.innerWidth < NARROW_BREAKPOINT
  );

  React.useEffect(() => {
    const handler = () => {
      setIsNarrow(window.innerWidth < NARROW_BREAKPOINT);
    };
    window.addEventListener("resize", handler);
    return () => window.removeEventListener("resize", handler);
  }, []);

  return isNarrow;
}

// ─────────────────────────────────────────────────────────────────────────────
// AppShell Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * AppShell — root layout component for the SPE Admin App.
 *
 * Provides:
 * - Left navigation panel (NavDrawer) with icons for all admin sections
 * - Responsive behavior: auto-collapses on narrow viewports (<768px)
 * - Dark mode support via Fluent v9 design tokens (ADR-021)
 *   Theme is inherited from the FluentProvider in App.tsx — no extra wiring needed.
 * - Content area that renders children (active page component)
 *
 * Usage:
 * ```tsx
 * const [page, setPage] = React.useState<SpeAdminPage>("dashboard");
 *
 * <AppShell activePage={page} onNavigate={setPage}>
 *   {page === "dashboard" && <DashboardPage />}
 *   {page === "containers" && <ContainersPage />}
 * </AppShell>
 * ```
 *
 * @remarks
 * The dark mode theme is applied by the parent FluentProvider (App.tsx), so all
 * Fluent design tokens used here automatically reflect the current theme.
 * No theme-specific style overrides are needed in AppShell.
 */
export const AppShell: React.FC<AppShellProps> = ({
  children,
  activePage = "dashboard",
  onNavigate,
  version = "1.0.0",
}) => {
  const styles = useStyles();
  const isNarrow = useIsNarrow();

  // Nav open state:
  //   - Wide viewports: open by default
  //   - Narrow viewports: closed by default (overlay drawer behavior)
  const [isOpen, setIsOpen] = React.useState(!isNarrow);

  // When viewport crosses the breakpoint, update open state automatically.
  React.useEffect(() => {
    setIsOpen(!isNarrow);
  }, [isNarrow]);

  const toggleNav = React.useCallback(() => {
    setIsOpen((prev) => !prev);
  }, []);

  const handleNavItemClick = React.useCallback(
    (page: SpeAdminPage) => {
      onNavigate?.(page);
      // On narrow viewports, close the drawer after navigation
      if (isNarrow) {
        setIsOpen(false);
      }
    },
    [onNavigate, isNarrow]
  );

  // ── Narrow viewport: use OverlayDrawer mode ──────────────────────────────
  // On wide viewports: inline drawer (always visible, collapses to icon rail)
  // On narrow viewports: overlay drawer (toggles over content, full close)

  const navWidth = isOpen ? NAV_OPEN_WIDTH : NAV_COLLAPSED_WIDTH;

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div className={styles.root}>
      {/* ── Navigation Drawer ── */}
      {/*
       * On wide viewports we render an inline drawer that always occupies space.
       * On narrow viewports the drawer overlays the content (position: absolute/fixed).
       * We use NavDrawer from Fluent v9 for accessible keyboard/focus management.
       */}
      <NavDrawer
        open={isOpen}
        type={isNarrow ? "overlay" : "inline"}
        onOpenChange={(_e, { open }) => setIsOpen(open)}
        style={{ width: `${navWidth}px`, minWidth: `${navWidth}px` }}
      >
        {/* Header with app title and collapse toggle */}
        <NavDrawerHeader>
          <div
            className={`${styles.navHeader} ${!isOpen ? styles.navHeaderCollapsed : ""}`}
          >
            {isOpen && (
              <Text className={styles.navHeaderTitle} size={400}>
                SPE Admin
              </Text>
            )}
            <Tooltip
              content={isOpen ? "Collapse navigation" : "Expand navigation"}
              relationship="label"
            >
              <Button
                appearance="subtle"
                icon={
                  isOpen ? (
                    <PanelLeftContract20Regular />
                  ) : (
                    <PanelLeftExpand20Regular />
                  )
                }
                onClick={toggleNav}
                aria-label={isOpen ? "Collapse navigation" : "Expand navigation"}
              />
            </Tooltip>
          </div>
        </NavDrawerHeader>

        {/* Navigation items — rendered by NavigationPanel (task 031) */}
        <NavDrawerBody className={styles.navBody}>
          <NavigationPanel
            activePage={activePage}
            onNavigate={handleNavItemClick}
            isOpen={isOpen}
          />
        </NavDrawerBody>

        {/* Footer: version info (only when expanded) */}
        {isOpen && (
          <div className={styles.navFooter}>
            <Text size={100} className={styles.navFooterText}>
              v{version}
            </Text>
          </div>
        )}
      </NavDrawer>

      {/* ── Main Content Area ── */}
      <div className={styles.content}>
        {/*
         * Mobile header bar: shown only on narrow viewports when the drawer is closed.
         * Provides a hamburger button to open the nav overlay.
         */}
        {isNarrow && !isOpen && (
          <div className={styles.mobileHeader}>
            <Tooltip content="Open navigation" relationship="label">
              <Button
                appearance="subtle"
                icon={<PanelLeftExpand20Regular />}
                onClick={toggleNav}
                aria-label="Open navigation"
              />
            </Tooltip>
            <Text className={styles.mobileHeaderTitle} size={400}>
              SPE Admin
            </Text>
          </div>
        )}

        {/* Page content — rendered by the consumer */}
        <div className={styles.contentInner}>{children}</div>
      </div>
    </div>
  );
};
