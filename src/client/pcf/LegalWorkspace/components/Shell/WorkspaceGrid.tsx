import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Text,
  Toaster,
  Toast,
  ToastTitle,
  ToastBody,
  useToastController,
  useId,
  Spinner,
} from "@fluentui/react-components";
import { GetStartedRow } from "../GetStarted";
import { PortfolioHealthStrip } from "../PortfolioHealth";
import { ActivityFeed } from "../ActivityFeed";
import { SmartToDo } from "../SmartToDo";
import { usePortfolioHealth } from "../../hooks/usePortfolioHealth";
import { useQuickSummary } from "../../hooks/useQuickSummary";
import {
  createAnalysisBuilderHandlers,
  getAnalysisBuilderUnavailableMessage,
} from "../GetStarted/ActionCardHandlers";

// ---------------------------------------------------------------------------
// Lazy-loaded dialog components (bundle-size optimization — Task 033)
//
// These dialogs are modal overlays only opened on user interaction.
// Using React.lazy() defers their JavaScript from the initial bundle chunk.
// The PCF platform bundles as a single file (esbuild/webpack), so lazy chunks
// are emitted separately and loaded on first open.
//
// Suspense fallback: Fluent Spinner centred in a fixed overlay so the layout
// does not shift while the chunk loads.
// ---------------------------------------------------------------------------

const LazyWizardDialog = React.lazy(
  () => import("../CreateMatter/WizardDialog")
);

const LazyBriefingDialog = React.lazy(
  () => import("../GetStarted/BriefingDialog")
);

// ---------------------------------------------------------------------------
// Suspense fallback: Fluent Spinner shown while lazy chunk loads
// ---------------------------------------------------------------------------

const DialogLoadingFallback: React.FC = () => (
  <div
    style={{
      position: "fixed",
      inset: 0,
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      backgroundColor: "rgba(0,0,0,0.12)",
      zIndex: 1000,
    }}
    aria-live="polite"
    aria-label="Loading dialog"
  >
    <Spinner size="large" label="Loading..." labelPosition="below" />
  </div>
);

export interface IWorkspaceGridProps {
  allocatedWidth: number;
  allocatedHeight: number;
  /** Xrm.WebApi reference from PCF framework context, forwarded to data-bound blocks */
  webApi: ComponentFramework.WebApi;
  /** GUID of the current user (context.userSettings.userId) */
  userId: string;
}

const useStyles = makeStyles({
  grid: {
    display: "grid",
    gridTemplateColumns: "1fr",
    gap: tokens.spacingVerticalL,
    "@media (min-width: 1024px)": {
      gridTemplateColumns: "1fr 1fr",
    },
  },
  leftColumn: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
  },
  rightColumn: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
  },
  placeholderCard: {
    minHeight: "120px",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderWidth("1px"),
    ...shorthands.borderStyle("dashed"),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingVerticalL,
  },
  placeholderLabel: {
    color: tokens.colorNeutralForeground4,
  },
});

interface IPlaceholderBlockProps {
  label: string;
}

const PlaceholderBlock: React.FC<IPlaceholderBlockProps> = ({ label }) => {
  const styles = useStyles();
  return (
    <div className={styles.placeholderCard} aria-label={label} role="region">
      <Text className={styles.placeholderLabel} size={200}>
        {label}
      </Text>
    </div>
  );
};

/**
 * PortfolioHealthBlock — thin wrapper that wires usePortfolioHealth data into
 * PortfolioHealthStrip for use inside WorkspaceGrid.
 *
 * bffBaseUrl and accessToken are left undefined until the BFF (task 008) is
 * deployed — the hook returns isLoading=true so skeletons render gracefully.
 *
 * Accepts an optional `onRefresh` callback so the parent can trigger a refresh
 * after data-changing operations (e.g. matter status changes, to-do completions).
 */
interface IPortfolioHealthBlockProps {
  /** Expose the refetch function to the parent for cross-block coordination */
  onRefetchReady?: (refetch: () => void) => void;
}

const PortfolioHealthBlock: React.FC<IPortfolioHealthBlockProps> = ({
  onRefetchReady,
}) => {
  const { data, isLoading, error, refetch } = usePortfolioHealth({
    // TODO (task 008): supply bffBaseUrl and accessToken once BFF is deployed
    // bffBaseUrl: "https://spe-api-dev-67e2xz.azurewebsites.net",
    // accessToken: "<token from MSAL auth provider>",
  });

  // Expose refetch to parent on mount (stable — refetch identity never changes)
  React.useEffect(() => {
    if (onRefetchReady) {
      onRefetchReady(refetch);
    }
    // onRefetchReady is a stable callback from the parent; refetch is stable from hook
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refetch]);

  return (
    <PortfolioHealthStrip
      health={data}
      isLoading={isLoading}
      error={error}
    />
  );
};

export const WorkspaceGrid: React.FC<IWorkspaceGridProps> = ({
  allocatedWidth,
  allocatedHeight: _allocatedHeight,
  webApi,
  userId,
}) => {
  const styles = useStyles();

  // -------------------------------------------------------------------------
  // Quick Summary — fetches /api/workspace/briefing from the BFF.
  // bffBaseUrl and accessToken are left undefined until BFF (task 008) is
  // deployed. When absent, useQuickSummary returns null data (not loading),
  // so QuickSummaryCard renders its "No summary data available" state.
  // -------------------------------------------------------------------------

  const {
    data: quickSummary,
    isLoading: isQuickSummaryLoading,
    error: quickSummaryError,
    refetch: refetchQuickSummary,
  } = useQuickSummary({
    // TODO (task 008): supply bffBaseUrl and accessToken once BFF is deployed
    // bffBaseUrl: "https://spe-api-dev-67e2xz.azurewebsites.net",
    // accessToken: "<token from MSAL auth provider>",
  });

  // -------------------------------------------------------------------------
  // Briefing dialog state — opened from QuickSummaryCard "Full briefing" link
  // -------------------------------------------------------------------------

  const [isBriefingOpen, setIsBriefingOpen] = React.useState(false);
  const handleOpenBriefing = React.useCallback(() => setIsBriefingOpen(true), []);
  const handleCloseBriefing = React.useCallback(() => setIsBriefingOpen(false), []);

  // -------------------------------------------------------------------------
  // Portfolio Health — exposed refetch for cross-block refresh coordination.
  // The ref holds the refetch function once PortfolioHealthBlock mounts.
  // -------------------------------------------------------------------------

  const portfolioHealthRefetchRef = React.useRef<(() => void) | null>(null);
  const handlePortfolioHealthRefetchReady = React.useCallback((refetch: () => void) => {
    portfolioHealthRefetchRef.current = refetch;
  }, []);

  /**
   * Refresh all aggregate blocks (Quick Summary + Portfolio Health) after a
   * data-changing operation. Called externally once to-do CRUD / matter create
   * is wired up in tasks 015 and 024.
   */
  const refreshAggregateBlocks = React.useCallback(() => {
    refetchQuickSummary();
    if (portfolioHealthRefetchRef.current) {
      portfolioHealthRefetchRef.current();
    }
  }, [refetchQuickSummary]);

  // Suppress "declared but never used" lint warning until task 015 wires this.
  void refreshAggregateBlocks;

  // -------------------------------------------------------------------------
  // Create New Matter wizard dialog state
  // -------------------------------------------------------------------------

  const [isWizardOpen, setIsWizardOpen] = React.useState(false);
  const handleOpenWizard = React.useCallback(() => setIsWizardOpen(true), []);
  const handleCloseWizard = React.useCallback(() => setIsWizardOpen(false), []);

  // -------------------------------------------------------------------------
  // Toaster for "Analysis Builder unavailable" informational messages
  // -------------------------------------------------------------------------

  const toasterId = useId("workspace-toaster");
  const { dispatchToast } = useToastController(toasterId);

  const handleAnalysisBuilderUnavailable = React.useCallback(
    (displayName: string, intent: string) => {
      dispatchToast(
        <Toast>
          <ToastTitle>Feature Available in Full Workspace</ToastTitle>
          <ToastBody>
            {getAnalysisBuilderUnavailableMessage(displayName)}
          </ToastBody>
        </Toast>,
        { intent: "info", timeout: 6000 }
      );
      // Log the missed intent so developers can verify which intents need
      // to be registered in the AI Playbook platform
      console.info(
        `[WorkspaceGrid] Analysis Builder unavailable for intent "${intent}" (card: "${displayName}").`
      );
    },
    [dispatchToast]
  );

  // -------------------------------------------------------------------------
  // Analysis Builder handlers for the 6 non-Create-Matter cards
  // -------------------------------------------------------------------------

  const analysisBuilderHandlers = React.useMemo(
    () =>
      createAnalysisBuilderHandlers({
        onUnavailable: handleAnalysisBuilderUnavailable,
      }),
    [handleAnalysisBuilderUnavailable]
  );

  // -------------------------------------------------------------------------
  // Full card click handler map: Create Matter (wizard) + 6 Analysis Builder
  // -------------------------------------------------------------------------

  const cardClickHandlers = React.useMemo(
    () => ({
      // "Create New Matter" → opens the 3-step wizard dialog (task 022)
      "create-new-matter": handleOpenWizard,
      // The remaining 6 cards → launch Analysis Builder with intent payloads
      ...analysisBuilderHandlers,
    }),
    [handleOpenWizard, analysisBuilderHandlers]
  );

  // -------------------------------------------------------------------------
  // Layout
  // -------------------------------------------------------------------------

  // Inline breakpoint override: when PCF reports a narrow allocated width
  // (e.g. initial render before media query fires), force single-column via
  // an inline style so layout is correct from the first paint.
  const isSingleColumn = allocatedWidth > 0 && allocatedWidth < 1024;
  const gridStyle: React.CSSProperties = isSingleColumn
    ? { gridTemplateColumns: "1fr" }
    : {};

  return (
    <>
      {/* Toaster for informational messages (e.g. Analysis Builder unavailable) */}
      <Toaster toasterId={toasterId} position="bottom-end" />

      <div className={styles.grid} style={gridStyle}>
        {/* Left column: Blocks 1-4 */}
        <div className={styles.leftColumn}>
          {/* Block 1 — Get Started (action cards + quick summary).
              Quick Summary metrics come from the BFF briefing endpoint via
              useQuickSummary. When the BFF is not configured (dev/pre-deploy)
              summary is null and the card renders its "No data" empty state. */}
          <GetStartedRow
            summary={quickSummary ?? undefined}
            isSummaryLoading={isQuickSummaryLoading}
            summaryError={quickSummaryError ?? undefined}
            onOpenBriefing={handleOpenBriefing}
            onCardClick={cardClickHandlers}
          />
          {/* Block 2 — Portfolio Health Summary.
              onRefetchReady captures the refetch function for cross-block
              refresh coordination via refreshAggregateBlocks(). */}
          <PortfolioHealthBlock onRefetchReady={handlePortfolioHealthRefetchReady} />
          {/* Block 3 — Updates Feed */}
          <ActivityFeed webApi={webApi} userId={userId} />
          {/* Block 4 — Smart To Do list */}
          <SmartToDo webApi={webApi} userId={userId} />
        </div>

        {/* Right column: Block 5 */}
        <div className={styles.rightColumn}>
          <PlaceholderBlock label="Block 5 — Placeholder" />
        </div>
      </div>

      {/* Create New Matter wizard dialog — rendered outside the grid so it
          can overlay the full viewport regardless of grid column position.
          Lazy-loaded: chunk only fetched on first user interaction (Task 033). */}
      {isWizardOpen && (
        <React.Suspense fallback={<DialogLoadingFallback />}>
          <LazyWizardDialog open={isWizardOpen} onClose={handleCloseWizard} webApi={webApi} />
        </React.Suspense>
      )}

      {/* Full Portfolio Briefing dialog — opened from QuickSummaryCard "Full briefing" link.
          Lazy-loaded: chunk only fetched on first user interaction (Task 033). */}
      {isBriefingOpen && (
        <React.Suspense fallback={<DialogLoadingFallback />}>
          <LazyBriefingDialog
            open={isBriefingOpen}
            onClose={handleCloseBriefing}
            summary={quickSummary ?? undefined}
            // bffBaseUrl and accessToken left undefined until BFF is deployed (task 008)
            // bffBaseUrl: "https://spe-api-dev-67e2xz.azurewebsites.net",
            // accessToken: "<token from MSAL auth provider>",
          />
        </React.Suspense>
      )}
    </>
  );
};
