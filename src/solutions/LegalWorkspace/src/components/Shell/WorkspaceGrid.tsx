import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Badge,
  Button,
  Spinner,
} from "@fluentui/react-components";
import { OpenRegular, ArrowClockwiseRegular, AddRegular } from "@fluentui/react-icons";
import { GetStartedRow } from "../GetStarted";
import { QuickSummaryRow } from "../QuickSummary";
import { ActivityFeed } from "../ActivityFeed/ActivityFeed";
import { SmartToDo } from "../SmartToDo/SmartToDo";
import { DocumentsTab } from "../RecordCards/DocumentsTab";
import { useDataverseService } from "../../hooks/useDataverseService";
import { navigateToEntityList } from "../../utils/navigation";
import {
  createQuickStartHandlers,
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

const LazyProjectWizardDialog = React.lazy(
  () => import("../CreateProject/ProjectWizardDialog")
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

const LazyQuickStartWizardDialog = React.lazy(
  () => import("../QuickStart/QuickStartWizardDialog")
);

const LazySummarizeFilesDialog = React.lazy(
  () => import("../SummarizeFiles/SummarizeFilesDialog")
);

const LazyFindSimilarDialog = React.lazy(
  () => import("../FindSimilar/FindSimilarDialog")
);

const LazyEventWizardDialog = React.lazy(
  () => import("../CreateEvent/EventWizardDialog")
);

const LazyTodoWizardDialog = React.lazy(
  () => import("../CreateTodo/TodoWizardDialog")
);

const LazyWorkAssignmentWizardDialog = React.lazy(
  () => import("../CreateWorkAssignment/WorkAssignmentWizardDialog")
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
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftColor: tokens.colorNeutralStroke2,
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
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftColor: tokens.colorNeutralStroke2,
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
  toolbarDivider: {
    width: "1px",
    height: "20px",
    backgroundColor: tokens.colorNeutralStroke2,
    marginLeft: tokens.spacingHorizontalXS,
    marginRight: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
  toolbarRightGroup: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: "15px",
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
  // Record counts for section badges
  // -------------------------------------------------------------------------

  const [feedCount, setFeedCount] = React.useState<number>(0);
  const [todoCount, setTodoCount] = React.useState<number>(0);
  const [docCount, setDocCount] = React.useState<number>(0);

  // SmartToDo refetch ref — used to sync workspace Kanban after dialog close
  const todoRefetchRef = React.useRef<(() => void) | null>(null);
  const handleTodoRefetchReady = React.useCallback((refetch: () => void) => {
    todoRefetchRef.current = refetch;
  }, []);

  // Activity Feed refetch ref — used to sync after Event wizard close
  const feedRefetchRef = React.useRef<(() => void) | null>(null);
  const handleFeedRefetchReady = React.useCallback((refetch: () => void) => {
    feedRefetchRef.current = refetch;
  }, []);

  // Documents refetch ref — used to refresh docs grid
  const docRefetchRef = React.useRef<(() => void) | null>(null);
  const handleDocRefetchReady = React.useCallback((refetch: () => void) => {
    docRefetchRef.current = refetch;
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
  // Create New Project wizard dialog state
  // -------------------------------------------------------------------------

  const [isProjectWizardOpen, setIsProjectWizardOpen] = React.useState(false);
  const handleOpenProjectWizard = React.useCallback(() => setIsProjectWizardOpen(true), []);
  const handleCloseProjectWizard = React.useCallback(() => setIsProjectWizardOpen(false), []);

  // -------------------------------------------------------------------------
  // Summarize New File(s) wizard dialog state
  // -------------------------------------------------------------------------

  const [isSummarizeOpen, setIsSummarizeOpen] = React.useState(false);
  const handleOpenSummarize = React.useCallback(() => setIsSummarizeOpen(true), []);
  const handleCloseSummarize = React.useCallback(() => setIsSummarizeOpen(false), []);

  // -------------------------------------------------------------------------
  // Find Similar wizard dialog state
  // -------------------------------------------------------------------------

  const [isFindSimilarOpen, setIsFindSimilarOpen] = React.useState(false);
  const handleOpenFindSimilar = React.useCallback(() => setIsFindSimilarOpen(true), []);
  const handleCloseFindSimilar = React.useCallback(() => setIsFindSimilarOpen(false), []);

  // -------------------------------------------------------------------------
  // Create New Event wizard dialog state
  // -------------------------------------------------------------------------

  const [isEventWizardOpen, setIsEventWizardOpen] = React.useState(false);
  const handleOpenEventWizard = React.useCallback(() => setIsEventWizardOpen(true), []);
  const handleCloseEventWizard = React.useCallback(() => {
    setIsEventWizardOpen(false);
    feedRefetchRef.current?.();
  }, []);

  // -------------------------------------------------------------------------
  // Create New To Do wizard dialog state
  // -------------------------------------------------------------------------

  const [isTodoWizardOpen, setIsTodoWizardOpen] = React.useState(false);
  const handleOpenTodoWizard = React.useCallback(() => setIsTodoWizardOpen(true), []);
  const handleCloseTodoWizard = React.useCallback(() => {
    setIsTodoWizardOpen(false);
    todoRefetchRef.current?.();
  }, []);

  // -------------------------------------------------------------------------
  // Work Assignment wizard dialog state
  // -------------------------------------------------------------------------

  const [isWorkAssignmentWizardOpen, setIsWorkAssignmentWizardOpen] = React.useState(false);
  const handleOpenWorkAssignmentWizard = React.useCallback(() => setIsWorkAssignmentWizardOpen(true), []);
  const handleCloseWorkAssignmentWizard = React.useCallback(() => {
    setIsWorkAssignmentWizardOpen(false);
  }, []);

  // -------------------------------------------------------------------------
  // Quick Start wizard dialog state
  // -------------------------------------------------------------------------

  const [wizardIntent, setWizardIntent] = React.useState<string | null>(null);
  const handleOpenQuickStartWizard = React.useCallback((intent: string) => {
    setWizardIntent(intent);
  }, []);
  const handleCloseQuickStartWizard = React.useCallback(() => {
    setWizardIntent(null);
  }, []);

  // -------------------------------------------------------------------------
  // Quick Start handlers for the 5 non-Create-Matter/Project cards
  // -------------------------------------------------------------------------

  const quickStartHandlers = React.useMemo(
    () =>
      createQuickStartHandlers({
        onOpenWizard: handleOpenQuickStartWizard,
      }),
    [handleOpenQuickStartWizard]
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

  // Open DocumentUploadWizard Code Page dialog (Integration Pattern C — frame-walking)
  const handleAddDocument = React.useCallback(async () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm: any =
      (window as any)?.Xrm ??
      (window.parent as any)?.Xrm ??
      (window.top as any)?.Xrm;
    if (!xrm?.Navigation?.navigateTo) {
      console.warn("[WorkspaceGrid] Xrm.Navigation not available");
      return;
    }

    // Resolve container ID from business unit
    let containerId = "";
    try {
      const userSettings = xrm.Utility.getGlobalContext().userSettings;
      const uid = userSettings.userId.replace(/[{}]/g, "");
      const user = await xrm.WebApi.retrieveRecord(
        "systemuser", uid, "?$select=_businessunitid_value"
      );
      const buId = user["_businessunitid_value"] as string;
      if (buId) {
        const bu = await xrm.WebApi.retrieveRecord(
          "businessunit", buId, "?$select=sprk_containerid"
        );
        containerId = (bu["sprk_containerid"] as string) ?? "";
      }
    } catch (err) {
      console.warn("[WorkspaceGrid] Failed to resolve container ID:", err);
    }
    if (!containerId) {
      console.error("[WorkspaceGrid] No container ID available");
      return;
    }

    // Detect theme
    let theme = "light";
    try {
      const bodyBg = window.getComputedStyle(document.body).backgroundColor;
      const rgbMatch = bodyBg.match(/rgb\((\d+),\s*(\d+),\s*(\d+)\)/);
      if (rgbMatch) {
        const luminance =
          0.299 * parseInt(rgbMatch[1]) +
          0.587 * parseInt(rgbMatch[2]) +
          0.114 * parseInt(rgbMatch[3]);
        if (luminance < 128) theme = "dark";
      }
    } catch { /* ignore */ }

    const dataString =
      "parentEntityType=sprk_document" +
      "&parentEntityId=" +
      "&parentEntityName=" +
      "&containerId=" + containerId +
      "&theme=" + theme;

    try {
      await xrm.Navigation.navigateTo(
        {
          pageType: "webresource",
          webresourceName: "sprk_documentuploadwizard",
          data: encodeURIComponent(dataString),
        },
        {
          target: 2,
          width: { value: 60, unit: "%" },
          height: { value: 70, unit: "%" },
        }
      );
    } catch (error: any) {
      if (error?.errorCode !== 2) {
        console.error("[WorkspaceGrid] Upload dialog error:", error);
      }
    }
  }, []);

  // -------------------------------------------------------------------------
  // Full card click handler map: Create Matter (wizard) + 6 Analysis Builder
  // -------------------------------------------------------------------------

  const cardClickHandlers = React.useMemo(
    () => ({
      ...quickStartHandlers,
      // Explicit handlers AFTER spread to prevent overwrite
      "create-new-matter": handleOpenWizard,
      "create-new-project": handleOpenProjectWizard,
      "summarize-new-files": handleOpenSummarize,
      "find-similar": handleOpenFindSimilar,
      "assign-to-counsel": handleOpenWorkAssignmentWizard,
    }),
    [quickStartHandlers, handleOpenWizard, handleOpenProjectWizard, handleOpenSummarize, handleOpenFindSimilar, handleOpenWorkAssignmentWizard]
  );

  // -------------------------------------------------------------------------
  // Layout
  // -------------------------------------------------------------------------

  return (
    <>
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
                icon={<ArrowClockwiseRegular />}
                aria-label="Refresh Quick Summary"
              />
              <span className={styles.toolbarDivider} aria-hidden="true" />
              <div style={{ flex: 1 }} />
              <div className={styles.toolbarRightGroup}>
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<OpenRegular />}
                  onClick={handleDashboardOpen}
                  aria-label="Open Quick Summary Dashboard"
                />
              </div>
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
            hideOverflowMenu
            onCountChange={setFeedCount}
            onOpenAll={handleOpenAllUpdates}
            onRefetchReady={handleFeedRefetchReady}
            onCreateNew={handleOpenEventWizard}
          />
        </div>

        {/* Row 3: My To Do List (left) + My Documents (right) — 50/50 split */}
        <div className={styles.row3}>
          <div className={styles.sectionCard} style={{ height: "560px" }}>
            <div className={styles.row2TitleArea} style={{ paddingTop: tokens.spacingVerticalM, paddingBottom: tokens.spacingVerticalS, paddingLeft: tokens.spacingHorizontalM, paddingRight: tokens.spacingHorizontalM }}>
              <Text size={400} weight="semibold">
                My To Do List
              </Text>
              {todoCount > 0 && (
                <Badge appearance="filled" color="brand" size="small">
                  {todoCount}
                </Badge>
              )}
            </div>
            <div className={styles.toolbar} role="toolbar" aria-label="To Do toolbar">
              <Button
                appearance="subtle"
                size="small"
                icon={<ArrowClockwiseRegular />}
                onClick={() => todoRefetchRef.current?.()}
                aria-label="Refresh To Do list"
              />
              <span className={styles.toolbarDivider} aria-hidden="true" />
              <div style={{ flex: 1 }} />
              <div className={styles.toolbarRightGroup}>
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<AddRegular />}
                  onClick={handleOpenTodoWizard}
                  aria-label="Create new to do"
                />
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<OpenRegular />}
                  onClick={handleOpenTodoDialog}
                  aria-label="Open full To Do list"
                />
              </div>
            </div>
            <div className={styles.sectionContent}>
              <SmartToDo
                embedded
                webApi={webApi}
                userId={userId}
                disableSidePane
                onCountChange={setTodoCount}
                onRefetchReady={handleTodoRefetchReady}
              />
            </div>
          </div>
          <div className={styles.sectionCard} style={{ minHeight: "auto", overflow: "visible" }}>
            <div className={styles.row2TitleArea} style={{ paddingTop: tokens.spacingVerticalM, paddingBottom: tokens.spacingVerticalS, paddingLeft: tokens.spacingHorizontalM, paddingRight: tokens.spacingHorizontalM }}>
              <Text size={400} weight="semibold">
                My Documents
              </Text>
              {docCount > 0 && (
                <Badge appearance="filled" color="brand" size="small">
                  {docCount}
                </Badge>
              )}
            </div>
            <div className={styles.toolbar} role="toolbar" aria-label="Documents toolbar">
              <Button
                appearance="subtle"
                size="small"
                icon={<ArrowClockwiseRegular />}
                onClick={() => docRefetchRef.current?.()}
                aria-label="Refresh documents"
              />
              <span className={styles.toolbarDivider} aria-hidden="true" />
              <div style={{ flex: 1 }} />
              <div className={styles.toolbarRightGroup}>
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<AddRegular />}
                  onClick={handleAddDocument}
                  aria-label="Add document"
                />
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<OpenRegular />}
                  onClick={handleOpenDocumentsDialog}
                  aria-label="Open all documents"
                />
              </div>
            </div>
            <div className={styles.sectionContent} style={{ overflow: "visible" }}>
              <DocumentsTab
                service={service}
                userId={userId}
                maxVisible={6}
                onCountChange={setDocCount}
                onRefetchReady={handleDocRefetchReady}
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

      {/* Create New Project wizard dialog — single-step project creation.
          Lazy-loaded: chunk only fetched on first user interaction. */}
      {isProjectWizardOpen && (
        <React.Suspense fallback={<DialogLoadingFallback />}>
          <LazyProjectWizardDialog open={isProjectWizardOpen} onClose={handleCloseProjectWizard} webApi={webApi} />
        </React.Suspense>
      )}

      {/* Summarize New File(s) wizard dialog — dedicated file summary wizard.
          Lazy-loaded: chunk only fetched on first user interaction. */}
      {isSummarizeOpen && (
        <React.Suspense fallback={<DialogLoadingFallback />}>
          <LazySummarizeFilesDialog open={isSummarizeOpen} onClose={handleCloseSummarize} webApi={webApi} />
        </React.Suspense>
      )}

      {/* Find Similar wizard dialog — dedicated semantic search wizard.
          Lazy-loaded: chunk only fetched on first user interaction. */}
      {isFindSimilarOpen && (
        <React.Suspense fallback={<DialogLoadingFallback />}>
          <LazyFindSimilarDialog open={isFindSimilarOpen} onClose={handleCloseFindSimilar} />
        </React.Suspense>
      )}

      {/* Quick Start Playbook Wizard dialog — config-driven wizard for action cards.
          Lazy-loaded: chunk only fetched on first user interaction. */}
      {wizardIntent !== null && (
        <React.Suspense fallback={<DialogLoadingFallback />}>
          <LazyQuickStartWizardDialog
            open={wizardIntent !== null}
            onClose={handleCloseQuickStartWizard}
            intent={wizardIntent}
          />
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

      {/* Create New Event wizard dialog */}
      {isEventWizardOpen && (
        <React.Suspense fallback={<DialogLoadingFallback />}>
          <LazyEventWizardDialog open={isEventWizardOpen} onClose={handleCloseEventWizard} webApi={webApi} />
        </React.Suspense>
      )}

      {/* Create New To Do wizard dialog */}
      {isTodoWizardOpen && (
        <React.Suspense fallback={<DialogLoadingFallback />}>
          <LazyTodoWizardDialog open={isTodoWizardOpen} onClose={handleCloseTodoWizard} webApi={webApi} />
        </React.Suspense>
      )}

      {/* Work Assignment wizard dialog */}
      {isWorkAssignmentWizardOpen && (
        <React.Suspense fallback={<DialogLoadingFallback />}>
          <LazyWorkAssignmentWizardDialog open={isWorkAssignmentWizardOpen} onClose={handleCloseWorkAssignmentWizard} webApi={webApi} />
        </React.Suspense>
      )}
    </>
  );
};
