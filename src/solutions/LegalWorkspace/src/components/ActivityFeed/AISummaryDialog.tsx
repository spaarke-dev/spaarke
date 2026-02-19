/**
 * AISummaryDialog — Fluent v9 Dialog presenting an AI-generated summary for a
 * single Updates Feed event (Block 3E).
 *
 * Layout:
 *   DialogTitle:    SparkleRegular icon  +  "AI Summary"  +  dismiss button
 *   DialogContent:
 *     • Loading state:  Spinner with "Analyzing…" label
 *     • Error state:    MessageBar with user-friendly text + Retry button
 *     • Result state:
 *         – Analysis card (subtle Card with AI-generated text + confidence pill)
 *         – Suggested Actions list (clickable items with type-specific icons)
 *   DialogActions:  "Add to To Do" button (flags the event via onAddToTodo)
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) only for custom styles
 *   - Icon-only buttons MUST have aria-label
 *   - Supports light, dark, and high-contrast modes automatically via token system
 *   - Fluent v9 Dialog components only (Dialog, DialogSurface, DialogBody, etc.)
 *   - @fluentui/react-icons only
 *
 * Wire-up:
 *   The consuming component (ActivityFeedList or ActivityFeed) owns a
 *   useAISummary() hook instance and passes the relevant props here.
 *   The dialog is purely presentational — all fetch logic lives in the hook.
 */

import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  Spinner,
  Card,
  CardHeader,
  Text,
  MessageBar,
  MessageBarBody,
  MessageBarActions,
  makeStyles,
  shorthands,
  tokens,
  mergeClasses,
} from '@fluentui/react-components';
import {
  SparkleRegular,
  DismissRegular,
  ArrowReplyRegular,
  TaskListSquareLtrRegular,
  FolderOpenRegular,
  DocumentCheckmarkRegular,
  CheckmarkCircleRegular,
} from '@fluentui/react-icons';
import type { FluentIcon } from '@fluentui/react-icons';
import type { IAISuggestedAction, IAISummaryResult } from '../../hooks/useAISummary';

// ---------------------------------------------------------------------------
// Icon resolution
// ---------------------------------------------------------------------------

/**
 * Resolve a string icon key to the corresponding Fluent icon component.
 * Kept local to avoid dynamic imports — the set is small and known at compile time.
 */
function resolveIcon(iconKey: IAISuggestedAction['iconKey']): FluentIcon {
  switch (iconKey) {
    case 'ArrowReplyRegular':
      return ArrowReplyRegular;
    case 'TaskListSquareRegular':
      return TaskListSquareLtrRegular;
    case 'FolderOpenRegular':
      return FolderOpenRegular;
    case 'DocumentCheckmarkRegular':
      return DocumentCheckmarkRegular;
    case 'CheckmarkCircleRegular':
      return CheckmarkCircleRegular;
    default:
      return TaskListSquareLtrRegular;
  }
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // ── Dialog surface — constrained width to match design ───────────────────
  dialogSurface: {
    maxWidth: '480px',
    width: '90vw',
  },

  // ── Title row ─────────────────────────────────────────────────────────────
  titleRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flex: '1 1 0',
  },
  titleIcon: {
    color: tokens.colorBrandForeground1,
    fontSize: '20px',
    flexShrink: 0,
  },
  titleText: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },

  // ── Content area ──────────────────────────────────────────────────────────
  contentArea: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    minHeight: '120px',
  },

  // ── Loading state ─────────────────────────────────────────────────────────
  loadingContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    paddingTop: tokens.spacingVerticalXL,
    paddingBottom: tokens.spacingVerticalXL,
    gap: tokens.spacingVerticalS,
  },

  // ── Analysis card ─────────────────────────────────────────────────────────
  analysisCard: {
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
  },
  analysisCardHeader: {
    paddingBottom: tokens.spacingVerticalXS,
  },
  analysisText: {
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase400,
  },
  confidencePill: {
    display: 'inline-flex',
    alignItems: 'center',
    borderRadius: tokens.borderRadiusMedium,
    paddingTop: '2px',
    paddingBottom: '2px',
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    backgroundColor: tokens.colorNeutralBackground4,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightBase100,
    whiteSpace: 'nowrap',
    marginTop: tokens.spacingVerticalXS,
  },
  confidencePillHigh: {
    backgroundColor: tokens.colorPaletteGreenBackground2,
    color: tokens.colorPaletteGreenForeground1,
  },
  confidencePillMedium: {
    backgroundColor: tokens.colorPaletteYellowBackground2,
    color: tokens.colorNeutralForeground1,
  },

  // ── Mock data notice ──────────────────────────────────────────────────────
  mockDataNotice: {
    color: tokens.colorNeutralForeground4,
    fontStyle: 'italic',
  },

  // ── Suggested actions section ─────────────────────────────────────────────
  suggestedActionsSection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  suggestedActionsLabel: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },
  actionsList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    paddingLeft: '0px',
    margin: '0px',
    listStyleType: 'none',
  },
  actionItem: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
  },
  actionButton: {
    justifyContent: 'flex-start',
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    color: tokens.colorBrandForeground1,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground2Hover,
      color: tokens.colorBrandForeground2,
    },
  },

  // ── Footer actions row ─────────────────────────────────────────────────────
  footerDivider: {
    borderTopWidth: '1px',
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    paddingTop: tokens.spacingVerticalS,
    display: 'flex',
    justifyContent: 'flex-end',
  },
});

// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------

/** Confidence badge displayed beneath the AI-generated summary text */
interface IConfidencePillProps {
  confidence: number;
}

const ConfidencePill: React.FC<IConfidencePillProps> = ({ confidence }) => {
  const styles = useStyles();
  const pct = Math.round(confidence * 100);
  const isHigh = pct >= 85;
  const isMedium = pct >= 65 && pct < 85;

  return (
    <span
      className={mergeClasses(
        styles.confidencePill,
        isHigh && styles.confidencePillHigh,
        isMedium && styles.confidencePillMedium
      )}
      aria-label={`AI confidence: ${pct}%`}
    >
      {`${pct}% confidence`}
    </span>
  );
};

/** Single suggested action row */
interface ISuggestedActionItemProps {
  action: IAISuggestedAction;
  onActionClick: (actionType: IAISuggestedAction['type']) => void;
}

const SuggestedActionItem: React.FC<ISuggestedActionItemProps> = React.memo(
  ({ action, onActionClick }) => {
    const styles = useStyles();
    const IconComponent = resolveIcon(action.iconKey);

    const handleClick = React.useCallback(() => {
      onActionClick(action.type);
    }, [onActionClick, action.type]);

    return (
      <li className={styles.actionItem}>
        <Button
          appearance="subtle"
          size="small"
          icon={<IconComponent aria-hidden="true" />}
          className={styles.actionButton}
          onClick={handleClick}
        >
          {action.label}
        </Button>
      </li>
    );
  }
);

SuggestedActionItem.displayName = 'SuggestedActionItem';

/** Analysis result body: card + actions list */
interface IResultBodyProps {
  result: IAISummaryResult;
  onActionClick: (actionType: IAISuggestedAction['type']) => void;
}

const ResultBody: React.FC<IResultBodyProps> = ({ result, onActionClick }) => {
  const styles = useStyles();

  return (
    <>
      {/* Analysis card */}
      <Card
        appearance="subtle"
        className={styles.analysisCard}
      >
        <CardHeader
          className={styles.analysisCardHeader}
          header={
            <Text size={200} weight="semibold" style={{ color: tokens.colorNeutralForeground2 }}>
              Analysis
            </Text>
          }
        />
        <Text size={300} className={styles.analysisText}>
          {result.summary}
        </Text>
        <div>
          <ConfidencePill confidence={result.confidence} />
        </div>
        {result.isMockData && (
          <Text size={100} className={styles.mockDataNotice}>
            Preview data — connect to BFF for live AI analysis
          </Text>
        )}
      </Card>

      {/* Suggested actions */}
      {result.suggestedActions.length > 0 && (
        <div className={styles.suggestedActionsSection}>
          <Text size={200} className={styles.suggestedActionsLabel}>
            Suggested actions
          </Text>
          <ul className={styles.actionsList} role="list" aria-label="Suggested actions">
            {result.suggestedActions.map((action) => (
              <SuggestedActionItem
                key={action.type}
                action={action}
                onActionClick={onActionClick}
              />
            ))}
          </ul>
        </div>
      )}
    </>
  );
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IAISummaryDialogProps {
  /** Whether the dialog is open */
  isOpen: boolean;
  /** The event being summarised */
  eventId: string;
  /** Event type string (e.g. "email", "invoice") for labelling */
  eventType: string | undefined;
  /** Event subject/title shown in the dialog title area */
  eventTitle: string;
  /** AI Summary result from useAISummary hook */
  result: import('../../hooks/useAISummary').IAISummaryResult | null;
  /** True while fetching */
  isLoading: boolean;
  /** Error message from the hook */
  error: string | null;
  /** Close the dialog */
  onClose: () => void;
  /** Retry after error */
  onRetry: () => void;
  /**
   * Called when the user clicks "Add to To Do".
   * The parent should flag the event via Dataverse or FeedTodoSyncContext.
   */
  onAddToTodo?: (eventId: string) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const AISummaryDialog: React.FC<IAISummaryDialogProps> = ({
  isOpen,
  eventId,
  eventType,
  eventTitle,
  result,
  isLoading,
  error,
  onClose,
  onRetry,
  onAddToTodo,
}) => {
  const styles = useStyles();

  // Handle suggested action clicks.
  // In R1 these are visual only — future tasks will wire real navigation.
  const handleActionClick = React.useCallback(
    (actionType: IAISuggestedAction['type']) => {
      // Placeholder: in future tasks, dispatch to Xrm navigation or BFF action.
      // For now, "create-task" and "open-matter" are no-ops with console info.
      if (process.env.NODE_ENV !== 'production') {
        // eslint-disable-next-line no-console
        console.info(`[AISummaryDialog] Suggested action clicked: ${actionType} for event ${eventId}`);
      }
    },
    [eventId]
  );

  const handleAddToTodo = React.useCallback(() => {
    if (onAddToTodo) {
      onAddToTodo(eventId);
    }
    onClose();
  }, [onAddToTodo, eventId, onClose]);

  // Derive a human-readable event type label for screen readers
  const eventTypeLabel = React.useMemo(() => {
    const typeMap: Record<string, string> = {
      email: 'Email',
      document: 'Document',
      documentreview: 'Document Review',
      invoice: 'Invoice',
      task: 'Task',
      meeting: 'Meeting',
      analysis: 'Analysis',
      'financial-alert': 'Financial Alert',
      'status-change': 'Status Change',
    };
    return typeMap[(eventType ?? '').toLowerCase()] ?? 'Event';
  }, [eventType]);

  return (
    <Dialog
      open={isOpen}
      onOpenChange={(_event, data) => {
        if (!data.open) {
          onClose();
        }
      }}
    >
      <DialogSurface className={styles.dialogSurface}>
        <DialogBody>
          {/* ── Title ───────────────────────────────────────────────────── */}
          <DialogTitle
            action={
              <Button
                appearance="subtle"
                aria-label="Close AI Summary dialog"
                size="small"
                icon={<DismissRegular aria-hidden="true" />}
                onClick={onClose}
              />
            }
          >
            <div className={styles.titleRow}>
              <SparkleRegular
                className={styles.titleIcon}
                aria-hidden="true"
              />
              <Text size={400} className={styles.titleText}>
                AI Summary
              </Text>
              <Text
                size={200}
                style={{ color: tokens.colorNeutralForeground3 }}
                aria-label={`Event type: ${eventTypeLabel}`}
              >
                {eventTypeLabel}
              </Text>
            </div>
          </DialogTitle>

          {/* ── Content ─────────────────────────────────────────────────── */}
          <DialogContent>
            {/* Event title hint */}
            {eventTitle && (
              <Text
                size={200}
                style={{
                  display: 'block',
                  color: tokens.colorNeutralForeground3,
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                  marginBottom: tokens.spacingVerticalXS,
                }}
                title={eventTitle}
              >
                {eventTitle}
              </Text>
            )}

            <div className={styles.contentArea}>
              {/* Loading state */}
              {isLoading && (
                <div className={styles.loadingContainer} aria-live="polite" aria-busy="true">
                  <Spinner size="medium" />
                  <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                    Analyzing…
                  </Text>
                </div>
              )}

              {/* Error state */}
              {!isLoading && error && (
                <MessageBar intent="error" layout="multiline">
                  <MessageBarBody>
                    <Text size={200}>{error}</Text>
                  </MessageBarBody>
                  <MessageBarActions
                    containerAction={
                      <Button
                        appearance="transparent"
                        size="small"
                        onClick={onRetry}
                      >
                        Retry
                      </Button>
                    }
                  />
                </MessageBar>
              )}

              {/* Result state */}
              {!isLoading && !error && result && (
                <ResultBody result={result} onActionClick={handleActionClick} />
              )}
            </div>
          </DialogContent>

          {/* ── Actions ─────────────────────────────────────────────────── */}
          <DialogActions>
            <div className={styles.footerDivider}>
              <Button
                appearance="primary"
                size="small"
                icon={<TaskListSquareLtrRegular aria-hidden="true" />}
                onClick={handleAddToTodo}
                disabled={isLoading}
                aria-label="Add this event to your To Do list"
              >
                Add to To Do
              </Button>
            </div>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

AISummaryDialog.displayName = 'AISummaryDialog';

// Default export enables React.lazy() dynamic import for bundle-size optimization (Task 033).
// Named export AISummaryDialog above is preserved for direct imports in tests.
export default AISummaryDialog;
