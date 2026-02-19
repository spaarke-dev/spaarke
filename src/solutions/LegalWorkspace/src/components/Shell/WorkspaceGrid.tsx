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
import { UpdatesTodoSection } from "../UpdatesTodo";
import { SummaryPanel } from "../SummaryPanel";
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

const useStyles = makeStyles({
  grid: {
    display: "grid",
    gridTemplateColumns: "1fr",
    gap: tokens.spacingVerticalL,
    "@media (min-width: 1024px)": {
      gridTemplateColumns: "3fr 2fr",
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

export const WorkspaceGrid: React.FC<IWorkspaceGridProps> = ({
  allocatedWidth,
  allocatedHeight: _allocatedHeight,
  webApi,
  userId,
}) => {
  const styles = useStyles();

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
        {/* Left column (60%): Get Started + tabbed Updates/To Do */}
        <div className={styles.leftColumn}>
          {/* Block 1 — Get Started (action cards) */}
          <GetStartedRow onCardClick={cardClickHandlers} />
          {/* Block 2 — Updates + To Do (tabbed section) */}
          <UpdatesTodoSection webApi={webApi} userId={userId} />
        </div>

        {/* Right column (40%): AI Summary panel */}
        <div className={styles.rightColumn}>
          <SummaryPanel webApi={webApi} userId={userId} />
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
    </>
  );
};
