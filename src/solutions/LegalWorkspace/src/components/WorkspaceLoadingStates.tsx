/**
 * WorkspaceLoadingStates — Fluent UI v9 loading state components for the workspace.
 *
 * Three distinct states are handled:
 *
 *   1. **WorkspaceSkeleton** — Returning user with saved layouts. Shows a Fluent
 *      Skeleton/shimmer grid matching the 3-row-mixed template (2 cols | 1 col | 2 cols)
 *      while layout data is being fetched.
 *
 *   2. **PersonalizeBanner** — First visit (no user-created layouts). Renders a
 *      dismissable Fluent MessageBar with "Personalize your workspace" CTA that
 *      opens the Layout Wizard via Xrm.Navigation.navigateTo.
 *
 *   3. **FetchErrorBar** — Fetch failure. Shows an inline Fluent MessageBar with
 *      intent="warning" that auto-dismisses after 5 seconds.
 *
 * All components use Fluent v9 design tokens for automatic dark/light/high-contrast
 * theme support per ADR-021.
 *
 * Standards: ADR-021 (Fluent v9), ADR-012 (shared components)
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Skeleton,
  SkeletonItem,
  shorthands,
  MessageBar,
  MessageBarBody,
  MessageBarActions,
  Button,
  Link,
} from "@fluentui/react-components";
import { DismissRegular } from "@fluentui/react-icons";
import { getBffBaseUrl } from "../config/runtimeConfig";

// ---------------------------------------------------------------------------
// Session storage key for banner dismissal
// ---------------------------------------------------------------------------

const BANNER_DISMISSED_KEY = "sprk_workspace_banner_dismissed";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // WorkspaceSkeleton: grid mimicking the 3-row-mixed template
  skeletonGrid: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    flex: "1 1 auto",
  },

  skeletonRow: {
    display: "grid",
    gap: tokens.spacingHorizontalM,
  },

  skeletonCell: {
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderWidth("1px"),
    ...shorthands.borderStyle("solid"),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    overflow: "hidden",
  },

  skeletonCellHeader: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
  },

  skeletonCellBody: {
    display: "flex",
    flexDirection: "column",
    padding: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    gap: tokens.spacingVerticalS,
  },

  skeletonBodyRow: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },

  // PersonalizeBanner
  bannerContainer: {
    marginBottom: tokens.spacingVerticalM,
  },

  // FetchErrorBar
  errorBarContainer: {
    marginBottom: tokens.spacingVerticalM,
  },
});

// ---------------------------------------------------------------------------
// WorkspaceSkeleton — shows shimmer grid matching 3-row-mixed layout
// ---------------------------------------------------------------------------

interface SkeletonCellProps {
  /** Height of the body area (sets min-height for visual consistency). */
  bodyHeight: string;
  /** Number of shimmer rows inside the body. */
  rows: number;
}

/** A single skeleton cell (header + body rows). */
const SkeletonCell: React.FC<SkeletonCellProps> = ({ bodyHeight, rows }) => {
  const styles = useStyles();
  const widths = ["65%", "50%", "72%", "58%", "45%", "68%"];
  return (
    <div className={styles.skeletonCell}>
      <div className={styles.skeletonCellHeader}>
        <Skeleton>
          <SkeletonItem size={16} style={{ width: "40%" }} />
        </Skeleton>
        <Skeleton>
          <SkeletonItem shape="circle" size={24} />
        </Skeleton>
      </div>
      <div className={styles.skeletonCellBody} style={{ minHeight: bodyHeight }}>
        {Array.from({ length: rows }).map((_, i) => (
          <div key={i} className={styles.skeletonBodyRow}>
            <Skeleton>
              <SkeletonItem shape="circle" size={16} />
            </Skeleton>
            <Skeleton style={{ flex: "1 1 auto" }}>
              <SkeletonItem size={16} style={{ width: widths[i % widths.length] }} />
            </Skeleton>
          </div>
        ))}
      </div>
    </div>
  );
};

/**
 * WorkspaceSkeleton — Fluent v9 skeleton grid matching the 3-row-mixed workspace layout.
 *
 * Structure:
 *   Row 1: 2 cells (1fr 1fr) — e.g., Get Started | Quick Summary
 *   Row 2: 1 cell  (1fr)     — e.g., Latest Updates
 *   Row 3: 2 cells (1fr 1fr) — e.g., To Do | Documents
 */
export const WorkspaceSkeleton: React.FC = React.memo(() => {
  const styles = useStyles();
  return (
    <div
      className={styles.skeletonGrid}
      role="status"
      aria-busy="true"
      aria-label="Loading workspace layout"
    >
      {/* Row 1: 2 columns */}
      <div className={styles.skeletonRow} style={{ gridTemplateColumns: "1fr 1fr" }}>
        <SkeletonCell bodyHeight="120px" rows={3} />
        <SkeletonCell bodyHeight="120px" rows={3} />
      </div>

      {/* Row 2: 1 column */}
      <div className={styles.skeletonRow} style={{ gridTemplateColumns: "1fr" }}>
        <SkeletonCell bodyHeight="180px" rows={5} />
      </div>

      {/* Row 3: 2 columns */}
      <div className={styles.skeletonRow} style={{ gridTemplateColumns: "1fr 1fr" }}>
        <SkeletonCell bodyHeight="160px" rows={4} />
        <SkeletonCell bodyHeight="160px" rows={4} />
      </div>
    </div>
  );
});

WorkspaceSkeleton.displayName = "WorkspaceSkeleton";

// ---------------------------------------------------------------------------
// PersonalizeBanner — first visit CTA to open Layout Wizard
// ---------------------------------------------------------------------------

/**
 * PersonalizeBanner — A dismissable Fluent MessageBar shown on first visit
 * (user has no custom layouts). Links to the Layout Wizard via navigateTo.
 *
 * Dismissal is persisted to sessionStorage so the banner does not reappear
 * within the same browser session.
 */
export const PersonalizeBanner: React.FC = React.memo(() => {
  const styles = useStyles();

  // Check sessionStorage to see if already dismissed this session
  const [isDismissed, setIsDismissed] = React.useState(() => {
    try {
      return sessionStorage.getItem(BANNER_DISMISSED_KEY) === "true";
    } catch {
      return false;
    }
  });

  const handleDismiss = React.useCallback(() => {
    setIsDismissed(true);
    try {
      sessionStorage.setItem(BANNER_DISMISSED_KEY, "true");
    } catch {
      // sessionStorage unavailable — dismiss in-memory only
    }
  }, []);

  const handleOpenWizard = React.useCallback(() => {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm: any =
        (window as any)?.Xrm ??
        (window.parent as any)?.Xrm ??
        (window.top as any)?.Xrm;
      if (!xrm?.Navigation?.navigateTo) return;

      xrm.Navigation.navigateTo(
        {
          pageType: "webresource",
          webresourceName: "sprk_workspacelayoutwizard",
          data: `mode=create&bffBaseUrl=${encodeURIComponent(getBffBaseUrl())}`,
        },
        {
          target: 2,
          width: { value: 80, unit: "%" },
          height: { value: 80, unit: "%" },
          title: "Create New Workspace",
        },
      ).catch(() => { /* user cancelled */ });
    } catch {
      // Navigation not available
    }
  }, []);

  if (isDismissed) return null;

  return (
    <div className={styles.bannerContainer}>
      <MessageBar intent="info">
        <MessageBarBody>
          Personalize your workspace.{" "}
          <Link inline onClick={handleOpenWizard}>
            Open the Layout Wizard
          </Link>{" "}
          to arrange sections the way you work.
        </MessageBarBody>
        <MessageBarActions
          containerAction={
            <Button
              appearance="transparent"
              icon={<DismissRegular />}
              size="small"
              aria-label="Dismiss banner"
              onClick={handleDismiss}
            />
          }
        />
      </MessageBar>
    </div>
  );
});

PersonalizeBanner.displayName = "PersonalizeBanner";

// ---------------------------------------------------------------------------
// FetchErrorBar — inline warning when layout fetch fails
// ---------------------------------------------------------------------------

/**
 * FetchErrorBar — Inline Fluent MessageBar with intent="warning" shown when
 * layout fetch fails. The workspace continues rendering with a fallback layout
 * (cached or system default). Auto-dismisses after 5 seconds.
 */
export const FetchErrorBar: React.FC = React.memo(() => {
  const styles = useStyles();
  const [isVisible, setIsVisible] = React.useState(true);

  // Auto-dismiss after 5 seconds
  React.useEffect(() => {
    const timer = setTimeout(() => setIsVisible(false), 5000);
    return () => clearTimeout(timer);
  }, []);

  const handleDismiss = React.useCallback(() => {
    setIsVisible(false);
  }, []);

  if (!isVisible) return null;

  return (
    <div className={styles.errorBarContainer}>
      <MessageBar intent="warning">
        <MessageBarBody>
          Couldn't load your workspace settings. Showing default layout.
        </MessageBarBody>
        <MessageBarActions
          containerAction={
            <Button
              appearance="transparent"
              icon={<DismissRegular />}
              size="small"
              aria-label="Dismiss notification"
              onClick={handleDismiss}
            />
          }
        />
      </MessageBar>
    </div>
  );
});

FetchErrorBar.displayName = "FetchErrorBar";
