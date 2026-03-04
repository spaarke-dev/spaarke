import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Badge,
  Button,
  Toaster,
  Toast,
  ToastTitle,
  ToastBody,
  useToastController,
  useId,
  Spinner,
} from "@fluentui/react-components";
import { OpenRegular, ArrowClockwiseRegular } from "@fluentui/react-icons";
import { GetStartedRow } from "../GetStarted";
import { QuickSummaryRow } from "../QuickSummary";
import { ActivityFeed } from "../ActivityFeed/ActivityFeed";
import { SmartToDo } from "../SmartToDo/SmartToDo";
import { DocumentsTab } from "../RecordCards/DocumentsTab";
import { useDataverseService } from "../../hooks/useDataverseService";
import { navigateToEntityList } from "../../utils/navigation";
import {
  createAnalysisBuilderHandlers,
  getAnalysisBuilderUnavailableMessage,
} from "../GetStarted/ActionCardHandlers";
import type { IWebApi } from "../../types/xrm";

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

const LazyGetStartedExpandDialog = React.lazy(() =>
  import("../GetStarted/GetStartedExpandDialog").then((m) => ({
    default: m.GetStartedExpandDialog,
  }))
);

const LazyQuickSummaryDashboardDialog = React.lazy(() =>
  import("../QuickSummary/QuickSummaryDashboardDialog").then((m) => ({
    default: m.QuickSummaryDashboardDialog,
  }))
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
  webApi: IWebApi;
  /** GUID of the current user (context.userSettings.userId) */
  userId: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  grid: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXL,
    flex: "1 1 auto",
    minHeight: 0,
    overflow: "auto",
  },
  row1: {
    display: "grid",
    gridTemplateColumns: "1fr 1fr",
    gap: tokens.spacingHorizontalL,
    "@media (max-width: 767px)": {
      gridTemplateColumns: "1fr",
    },
  },
  row2Card: {
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
    overflow: "hidden",
    minHeight: "325px",
  },
  row2Header: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  row2TitleArea: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  row3: {
    display: "grid",
    gridTemplateColumns: "1fr 1fr",
    gap: tokens.spacingHorizontalL,
    "@media (max-width: 767px)": {
      gridTemplateColumns: "1fr",
    },
  },
  sectionCard: {
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
    overflow: "hidden",
    minHeight: "400px",
  },
  sectionTitle: {
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  sectionContent: {
    display: "flex",
    flexDirection: "column",
    flex: "1 1 0",
    overflow: "hidden",
  },
  toolbar: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "flex-end",
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
    minHeight: "36px",
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const WorkspaceGrid: React.FC<IWorkspaceGridProps> = ({
  allocatedWidth: _allocatedWidth,
  allocatedHeight: _allocatedHeight,
  webApi,
  userId,
}) => {
  const styles = useStyles();

  // -------------------------------------------------------------------------
  // DataverseService for DocumentsTab
  // -------------------------------------------------------------------------

  const service = useDataverseService(webApi);

  // -------------------------------------------------------------------------
  // Feed count for Latest Updates badge
  // -------------------------------------------------------------------------

  const [feedCount, setFeedCount] = React.useState<number>(0);

  // SmartToDo refetch ref — used to sync workspace Kanban after dialog close
  const todoRefetchRef = React.useRef<(() => void) | null>(null);
  const handleTodoRefetchReady = React.useCallback((refetch: () => void) => {
    todoRefetchRef.current = refetch;
  }, []);

  // Open all updates → navigates to sprk_event entity list dialog
  const handleOpenAllUpdates = React.useCallback(() => {
    navigateToEntityList("sprk_event", "9399ba21-2e17-f111-8343-7c1e520aa4df");
  }, []);

  // -------------------------------------------------------------------------
  // Get Started expand dialog state
  // -------------------------------------------------------------------------

  const [isExpandOpen, setIsExpandOpen] = React.useState(false);
  const handleExpandClick = React.useCallback(() => setIsExpandOpen(true), []);
  const handleExpandClose = React.useCallback(() => setIsExpandOpen(false), []);

  // -------------------------------------------------------------------------
  // Quick Summary Dashboard dialog state
  // -------------------------------------------------------------------------

  const [isDashboardOpen, setIsDashboardOpen] = React.useState(false);
  const handleDashboardOpen = React.useCallback(() => setIsDashboardOpen(true), []);
  const handleDashboardClose = React.useCallback(() => setIsDashboardOpen(false), []);

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
  // To Do and Documents: open full list dialog handlers
  // -------------------------------------------------------------------------

  const handleOpenTodoDialog = React.useCallback(() => {
    // Open the workspace web resource in dialog mode with full Kanban + inline detail panel
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm: any =
        (window as any)?.Xrm ??
        (window.parent as any)?.Xrm ??
        (window.top as any)?.Xrm;
      if (xrm?.Navigation?.navigateTo) {
        xrm.Navigation.navigateTo(
          {
            pageType: "webresource",
            webresourceName: "sprk_corporateworkspace",
            data: "mode=todo",
          },
          { target: 2, width: { value: 90, unit: "%" }, height: { value: 90, unit: "%" } }
        ).then(() => {
          // Dialog closed — refetch workspace Kanban to sync any changes made in dialog
          todoRefetchRef.current?.();
        }).catch(() => { /* user cancelled or navigation failed */ });
      }
    } catch (err) {
      console.error("[WorkspaceGrid] Failed to open To Do dialog:", err);
    }
  }, []);

  const handleOpenDocumentsDialog = React.useCallback(() => {
    navigateToEntityList("sprk_document", "1ab4a862-3317-f111-8342-7c1e525abd8b");
  }, []);

  // -------------------------------------------------------------------------
  // Full card click handler map: Create Matter (wizard) + 6 Analysis Builder
  // -------------------------------------------------------------------------

  const cardClickHandlers = React.useMemo(
    () => ({
      // "Create New Matter" opens the 3-step wizard dialog (task 022)
      "create-new-matter": handleOpenWizard,
      // The remaining 6 cards launch Analysis Builder with intent payloads
      ...analysisBuilderHandlers,
    }),
    [handleOpenWizard, analysisBuilderHandlers]
  );

  // -------------------------------------------------------------------------
  // Layout
  // -------------------------------------------------------------------------

  return (
    <>
      {/* Toaster for informational messages (e.g. Analysis Builder unavailable) */}
      <Toaster toasterId={toasterId} position="bottom-end" />

      <div className={styles.grid}>
        {/* Row 1: Get Started (left) + Quick Summary (right) — 50/50 split */}
        <div className={styles.row1}>
          <div className={styles.sectionCard} style={{ minHeight: "auto" }}>
            <Text className={styles.sectionTitle} size={400} weight="semibold">
              Get Started
            </Text>
            <div className={styles.toolbar} role="toolbar" aria-label="Get Started toolbar">
              <Button
                appearance="subtle"
                size="small"
                icon={<OpenRegular />}
                onClick={handleExpandClick}
                aria-label="Open Playbook Library"
                style={{ marginLeft: "auto" }}
              />
            </div>
            <div style={{ padding: tokens.spacingHorizontalM, paddingTop: tokens.spacingVerticalS, paddingBottom: tokens.spacingVerticalM }}>
              <GetStartedRow
                onCardClick={cardClickHandlers}
                maxVisible={4}
              />
            </div>
          </div>
          <div className={styles.sectionCard} style={{ minHeight: "auto" }}>
            <Text className={styles.sectionTitle} size={400} weight="semibold">
              Quick Summary
            </Text>
            <div className={styles.toolbar} role="toolbar" aria-label="Quick Summary toolbar">
              <Button
                appearance="subtle"
                size="small"
                icon={<OpenRegular />}
                onClick={handleDashboardOpen}
                aria-label="Open Quick Summary Dashboard"
                style={{ marginLeft: "auto" }}
              />
            </div>
            <div style={{ padding: tokens.spacingHorizontalM, paddingTop: tokens.spacingVerticalS, paddingBottom: tokens.spacingVerticalM }}>
              <QuickSummaryRow webApi={webApi} userId={userId} />
            </div>
          </div>
        </div>

        {/* Row 2: Latest Updates — full-width bordered card */}
        <div className={styles.row2Card}>
          <div className={styles.row2Header}>
            <div className={styles.row2TitleArea}>
              <Text size={400} weight="semibold">
                Latest Updates
              </Text>
              {feedCount > 0 && (
                <Badge appearance="filled" color="brand" size="small">
                  {feedCount}
                </Badge>
              )}
            </div>
          </div>
          <ActivityFeed
            embedded
            webApi={webApi}
            userId={userId}
            textOnlyFilter
            gridLayout
            onCountChange={setFeedCount}
            onOpenAll={handleOpenAllUpdates}
          />
        </div>

        {/* Row 3: My To Do List (left) + My Documents (right) — 50/50 split */}
        <div className={styles.row3}>
          <div className={styles.sectionCard} style={{ height: "560px" }}>
            <Text className={styles.sectionTitle} size={400} weight="semibold">
              My To Do List
            </Text>
            <div className={styles.toolbar} role="toolbar" aria-label="To Do toolbar">
              <Button
                appearance="subtle"
                size="small"
                icon={<ArrowClockwiseRegular />}
                onClick={() => todoRefetchRef.current?.()}
                aria-label="Refresh To Do list"
                style={{ marginLeft: "auto" }}
              />
              <Button
                appearance="subtle"
                size="small"
                icon={<OpenRegular />}
                onClick={handleOpenTodoDialog}
                aria-label="Open full To Do list"
              />
            </div>
            <div className={styles.sectionContent}>
              <SmartToDo
                embedded
                webApi={webApi}
                userId={userId}
                disableSidePane
                onShowMore={handleOpenTodoDialog}
                onRefetchReady={handleTodoRefetchReady}
              />
            </div>
          </div>
          <div className={styles.sectionCard} style={{ minHeight: "auto", overflow: "visible" }}>
            <Text className={styles.sectionTitle} size={400} weight="semibold">
              My Documents
            </Text>
            <div className={styles.toolbar} role="toolbar" aria-label="Documents toolbar">
              <Button
                appearance="subtle"
                size="small"
                icon={<OpenRegular />}
                onClick={handleOpenDocumentsDialog}
                aria-label="Open all documents"
                style={{ marginLeft: "auto" }}
              />
            </div>
            <div className={styles.sectionContent} style={{ overflow: "visible" }}>
              <DocumentsTab
                service={service}
                userId={userId}
                maxVisible={6}
                onShowMore={handleOpenDocumentsDialog}
              />
            </div>
          </div>
        </div>
      </div>

      {/* GetStarted expand dialog — shows all 7 action cards in a grid */}
      {isExpandOpen && (
        <React.Suspense fallback={<DialogLoadingFallback />}>
          <LazyGetStartedExpandDialog
            open={isExpandOpen}
            onClose={handleExpandClose}
            onCardClick={cardClickHandlers}
          />
        </React.Suspense>
      )}

      {/* Create New Matter wizard dialog — rendered outside the grid so it
          can overlay the full viewport regardless of grid position.
          Lazy-loaded: chunk only fetched on first user interaction (Task 033). */}
      {isWizardOpen && (
        <React.Suspense fallback={<DialogLoadingFallback />}>
          <LazyWizardDialog open={isWizardOpen} onClose={handleCloseWizard} webApi={webApi} />
        </React.Suspense>
      )}

      {/* Quick Summary Dashboard dialog — Coming Soon placeholder */}
      {isDashboardOpen && (
        <React.Suspense fallback={<DialogLoadingFallback />}>
          <LazyQuickSummaryDashboardDialog
            open={isDashboardOpen}
            onClose={handleDashboardClose}
          />
        </React.Suspense>
      )}
    </>
  );
};
