/**
 * AiToolbar — Playbook-driven AI toolbar for the Secure Project Workspace SPA.
 *
 * Exposes three AI actions as toolbar buttons:
 *   1. Summarize Document — generates a structured summary for the selected document
 *   2. Summarize Project  — generates a structured project-level summary
 *   3. Run Analysis       — runs a configurable analysis playbook on the project
 *
 * Each button invokes a playbook via the BFF API. The BFF orchestrates the entire
 * AI pipeline server-side and returns structured results (sections). No playbook
 * definitions, prompt templates, or model details are ever sent to or stored in
 * the client — protecting Spaarke's AI IP and preventing prompt injection.
 *
 * Access control:
 *   - The toolbar is NOT rendered for ViewOnly users (AccessLevel.ViewOnly).
 *   - Only Collaborate (100000001) and Full Access (100000002) users see it.
 *
 * ADR-013: AI features via BFF API only — no separate AI service, no client-side AI.
 * ADR-021: All styles use Fluent v9 design tokens exclusively. No hard-coded colors.
 * ADR-022: React 18 functional component (createRoot is in main.tsx).
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Button,
  Spinner,
  Text,
  Divider,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  MessageBar,
  MessageBarBody,
  Toolbar,
  ToolbarButton,
  Tooltip,
} from "@fluentui/react-components";
import {
  SparkleRegular,
  DocumentTextRegular,
  FolderOpenRegular,
  SearchInfoRegular,
  DismissRegular,
} from "@fluentui/react-icons";
import { AccessLevel } from "../types";
import {
  usePlaybookExecution,
  PlaybookResult,
  PlaybookResultSection,
} from "../hooks/usePlaybookExecution";

// ---------------------------------------------------------------------------
// Stable playbook IDs (opaque identifiers — no content, definitions, or prompts)
// These IDs are resolved server-side by the BFF. The client never sees the
// playbook definitions, prompt templates, or AI model configuration.
// ---------------------------------------------------------------------------

const PLAYBOOK_SUMMARIZE_DOCUMENT = "summarize-document";
const PLAYBOOK_SUMMARIZE_PROJECT = "summarize-project";
const PLAYBOOK_RUN_ANALYSIS = "run-analysis";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  toolbar: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
    backgroundColor: tokens.colorNeutralBackground2,
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
  },
  toolbarLabel: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    paddingRight: tokens.spacingHorizontalXS,
    userSelect: "none",
  },
  divider: {
    height: "20px",
    marginLeft: tokens.spacingHorizontalXS,
    marginRight: tokens.spacingHorizontalXS,
  },
  resultDialogBody: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    maxHeight: "60vh",
    overflowY: "auto",
    paddingRight: tokens.spacingHorizontalXS,
  },
  resultHeader: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    color: tokens.colorBrandForeground1,
  },
  resultHeaderIcon: {
    flexShrink: "0",
    fontSize: "18px",
  },
  resultSection: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
  },
  sectionTitle: {
    color: tokens.colorNeutralForeground1,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    paddingBottom: tokens.spacingVerticalXS,
    marginBottom: tokens.spacingVerticalXS,
  },
  sectionContent: {
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase400,
    whiteSpace: "pre-wrap",
  },
  metaText: {
    color: tokens.colorNeutralForeground4,
    fontSize: tokens.fontSizeBase100,
    marginTop: tokens.spacingVerticalS,
  },
  loadingContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "120px",
    gap: tokens.spacingVerticalM,
  },
  emptyResult: {
    color: tokens.colorNeutralForeground3,
    fontStyle: "italic",
    textAlign: "center",
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
  },
});

// ---------------------------------------------------------------------------
// Result dialog — renders structured playbook output
// ---------------------------------------------------------------------------

interface ResultDialogProps {
  open: boolean;
  onClose: () => void;
  result: PlaybookResult | null;
  isExecuting: boolean;
  error: string | null;
}

const ResultDialog: React.FC<ResultDialogProps> = ({
  open,
  onClose,
  result,
  isExecuting,
  error,
}) => {
  const styles = useStyles();

  const handleClose = () => {
    if (!isExecuting) {
      onClose();
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(_ev, data) => {
        if (!data.open) handleClose();
      }}
    >
      <DialogSurface style={{ minWidth: "560px", maxWidth: "720px" }}>
        <DialogTitle
          action={
            <Button
              appearance="subtle"
              aria-label="Close"
              icon={<DismissRegular />}
              onClick={handleClose}
              disabled={isExecuting}
            />
          }
        >
          <div className={styles.resultHeader}>
            <SparkleRegular className={styles.resultHeaderIcon} />
            <span>{result?.playbookLabel ?? "AI Analysis"}</span>
          </div>
        </DialogTitle>

        <DialogBody>
          <DialogContent>
            {/* Loading state */}
            {isExecuting && (
              <div className={styles.loadingContainer}>
                <Spinner size="medium" label="Running AI analysis..." />
                <Text size={200} className={styles.metaText}>
                  This may take a few seconds
                </Text>
              </div>
            )}

            {/* Error state */}
            {!isExecuting && error && (
              <MessageBar intent="error">
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            )}

            {/* Result sections — structured output, not raw chat */}
            {!isExecuting && !error && result && (
              <div className={styles.resultDialogBody}>
                {result.sections.length === 0 ? (
                  <Text className={styles.emptyResult} size={300}>
                    No results were returned for this analysis.
                  </Text>
                ) : (
                  result.sections.map(
                    (section: PlaybookResultSection, index: number) => (
                      <React.Fragment key={`${section.title}-${index}`}>
                        {index > 0 && <Divider />}
                        <div className={styles.resultSection}>
                          <Text
                            size={400}
                            weight="semibold"
                            className={styles.sectionTitle}
                            as="h3"
                          >
                            {section.title}
                          </Text>
                          <Text size={300} className={styles.sectionContent}>
                            {section.content}
                          </Text>
                        </div>
                      </React.Fragment>
                    )
                  )
                )}

                {/* Execution metadata */}
                {result.executedAt && (
                  <Text className={styles.metaText} size={100}>
                    Generated{" "}
                    {new Intl.DateTimeFormat("en-US", {
                      dateStyle: "medium",
                      timeStyle: "short",
                    }).format(new Date(result.executedAt))}
                  </Text>
                )}
              </div>
            )}
          </DialogContent>
        </DialogBody>

        <DialogActions>
          <Button
            appearance="secondary"
            onClick={handleClose}
            disabled={isExecuting}
          >
            Close
          </Button>
        </DialogActions>
      </DialogSurface>
    </Dialog>
  );
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface AiToolbarProps {
  /** Secure Project record ID — required for all playbooks */
  projectId: string;
  /** The authenticated user's access level — toolbar hidden for ViewOnly */
  accessLevel: AccessLevel;
  /**
   * Optional: the currently selected document ID in the document library.
   * When provided, "Summarize Document" is enabled.
   */
  selectedDocumentId?: string | null;
}

// ---------------------------------------------------------------------------
// AiToolbar — main component
// ---------------------------------------------------------------------------

/**
 * AiToolbar — playbook-driven AI toolbar for the Project page.
 *
 * Renders three toolbar buttons for Collaborate and Full Access users:
 *   - Summarize Document: requires a selected document
 *   - Summarize Project: project-scope, always enabled
 *   - Run Analysis: configurable analysis, always enabled
 *
 * Playbook invocations send only stable opaque IDs to the BFF. No playbook
 * definitions, prompt templates, or AI model parameters are present in the
 * client — all AI orchestration happens server-side (ADR-013, IP protection).
 *
 * The toolbar is not rendered at all for ViewOnly users (AccessLevel.ViewOnly).
 *
 * @example
 * ```tsx
 * <AiToolbar
 *   projectId={project.sprk_projectid}
 *   accessLevel={userAccessLevel}
 *   selectedDocumentId={selectedDoc?.sprk_documentid}
 * />
 * ```
 */
export const AiToolbar: React.FC<AiToolbarProps> = ({
  projectId,
  accessLevel,
  selectedDocumentId,
}) => {
  const styles = useStyles();

  // Toolbar is invisible to ViewOnly users — enforced at render level
  if (accessLevel === AccessLevel.ViewOnly) {
    return null;
  }

  return (
    <AiToolbarInner
      projectId={projectId}
      selectedDocumentId={selectedDocumentId}
    />
  );
};

// ---------------------------------------------------------------------------
// Inner toolbar — separated to keep hooks out of the conditional return path
// ---------------------------------------------------------------------------

interface AiToolbarInnerProps {
  projectId: string;
  selectedDocumentId?: string | null;
}

const AiToolbarInner: React.FC<AiToolbarInnerProps> = ({
  projectId,
  selectedDocumentId,
}) => {
  const styles = useStyles();
  const { isExecuting, result, error, execute, reset } = usePlaybookExecution();
  const [dialogOpen, setDialogOpen] = React.useState<boolean>(false);

  // -------------------------------------------------------------------------
  // Handlers
  // -------------------------------------------------------------------------

  const handleSummarizeDocument = React.useCallback(() => {
    if (!selectedDocumentId) return;
    setDialogOpen(true);
    void execute({
      playbookId: PLAYBOOK_SUMMARIZE_DOCUMENT,
      projectId,
      documentId: selectedDocumentId,
    });
  }, [execute, projectId, selectedDocumentId]);

  const handleSummarizeProject = React.useCallback(() => {
    setDialogOpen(true);
    void execute({
      playbookId: PLAYBOOK_SUMMARIZE_PROJECT,
      projectId,
    });
  }, [execute, projectId]);

  const handleRunAnalysis = React.useCallback(() => {
    setDialogOpen(true);
    void execute({
      playbookId: PLAYBOOK_RUN_ANALYSIS,
      projectId,
    });
  }, [execute, projectId]);

  const handleDialogClose = React.useCallback(() => {
    if (!isExecuting) {
      setDialogOpen(false);
      reset();
    }
  }, [isExecuting, reset]);

  // -------------------------------------------------------------------------
  // Derived state
  // -------------------------------------------------------------------------

  const hasSelectedDocument = !!selectedDocumentId;
  const summarizeDocumentDisabled = isExecuting || !hasSelectedDocument;

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------

  return (
    <>
      <div className={styles.toolbar} role="toolbar" aria-label="AI Actions">
        <SparkleRegular
          fontSize={16}
          style={{ color: tokens.colorBrandForeground1, flexShrink: 0 }}
          aria-hidden="true"
        />
        <Text className={styles.toolbarLabel} aria-hidden="true">
          AI
        </Text>

        <Divider
          vertical
          className={styles.divider}
          aria-hidden="true"
        />

        {/* Summarize Document */}
        <Tooltip
          content={
            hasSelectedDocument
              ? "Summarize the selected document"
              : "Select a document first to summarize it"
          }
          relationship="label"
        >
          <ToolbarButton
            aria-label="Summarize Document"
            icon={
              isExecuting && result === null && !error ? (
                <Spinner size="tiny" />
              ) : (
                <DocumentTextRegular />
              )
            }
            disabled={summarizeDocumentDisabled}
            onClick={handleSummarizeDocument}
          >
            Summarize Document
          </ToolbarButton>
        </Tooltip>

        {/* Summarize Project */}
        <Tooltip
          content="Generate an AI-powered summary of the entire project"
          relationship="label"
        >
          <ToolbarButton
            aria-label="Summarize Project"
            icon={<FolderOpenRegular />}
            disabled={isExecuting}
            onClick={handleSummarizeProject}
          >
            Summarize Project
          </ToolbarButton>
        </Tooltip>

        {/* Run Analysis */}
        <Tooltip
          content="Run an AI analysis on project documents and activities"
          relationship="label"
        >
          <ToolbarButton
            aria-label="Run Analysis"
            icon={<SearchInfoRegular />}
            disabled={isExecuting}
            onClick={handleRunAnalysis}
          >
            Run Analysis
          </ToolbarButton>
        </Tooltip>
      </div>

      {/* Result dialog — structured output, never raw AI/prompt content */}
      <ResultDialog
        open={dialogOpen}
        onClose={handleDialogClose}
        result={result}
        isExecuting={isExecuting}
        error={error}
      />
    </>
  );
};

export default AiToolbar;
