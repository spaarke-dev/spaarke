/**
 * TodoAISummaryDialog — AI Summary dialog for to-do items showing a
 * Priority x Effort scoring grid (Block 4D).
 *
 * Layout:
 *   DialogTitle:
 *     SparkleRegular icon + "AI Summary" text + subtitle (event title)
 *     + dismiss button (top-right)
 *   DialogContent:
 *     1. Scoring grid — 2-column CSS Grid:
 *          Left:  PriorityScoreCard (score, level badge, factor breakdown table)
 *          Right: EffortScoreCard (score, level badge, base effort, multiplier checklist)
 *          Stacks to single column on narrow viewports (≤480px)
 *     2. Analysis card — Fluent v9 Card with AI-generated analysis text
 *     3. Suggested actions list — clickable action buttons
 *   DialogActions:
 *     Close button
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) only for custom styles
 *   - Icon-only buttons MUST have aria-label
 *   - Supports light, dark, and high-contrast modes automatically via token system
 *   - Fluent v9 Dialog components only
 *   - @fluentui/react-icons only
 *
 * Wire-up:
 *   The consuming component (SmartToDo) owns a useTodoScoring() hook instance
 *   and passes relevant props. The dialog is purely presentational — all fetch
 *   logic lives in the hook.
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
} from '@fluentui/react-components';
import {
  SparkleRegular,
  DismissRegular,
  ArrowUpRegular,
  PersonSwapRegular,
  MoneyRegular,
  TaskListSquareLtrRegular,
  FolderOpenRegular,
} from '@fluentui/react-icons';
import type { FluentIcon } from '@fluentui/react-icons';
import { PriorityScoreCard } from './PriorityScoreCard';
import { EffortScoreCard } from './EffortScoreCard';
import type {
  ITodoScoringResult,
  ITodoScoringAction,
} from '../../hooks/useTodoScoring';

// ---------------------------------------------------------------------------
// Icon resolution
// ---------------------------------------------------------------------------

/**
 * Resolve a string icon key to the corresponding Fluent icon component.
 * Kept local to avoid dynamic imports — the set is small and known at compile time.
 */
function resolveActionIcon(iconKey: ITodoScoringAction['icon']): FluentIcon {
  switch (iconKey) {
    case 'ArrowUpRegular':       return ArrowUpRegular;
    case 'PersonSwapRegular':    return PersonSwapRegular;
    case 'MoneyRegular':         return MoneyRegular;
    case 'TaskListSquareRegular':return TaskListSquareLtrRegular;
    case 'FolderOpenRegular':
    default:                     return FolderOpenRegular;
  }
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // ── Dialog surface ─────────────────────────────────────────────────────────
  dialogSurface: {
    maxWidth: '640px',
    width: '92vw',
  },

  // ── Title row ─────────────────────────────────────────────────────────────
  titleRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flex: '1 1 0',
    minWidth: 0,
  },
  titleIcon: {
    color: tokens.colorBrandForeground1,
    fontSize: '20px',
    flexShrink: 0,
  },
  titleText: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    flexShrink: 0,
  },
  titleSubtitle: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    color: tokens.colorNeutralForeground3,
    minWidth: 0,
    flex: '1 1 0',
  },

  // ── Content area ──────────────────────────────────────────────────────────
  contentArea: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },

  // ── Loading state ─────────────────────────────────────────────────────────
  loadingContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    gap: tokens.spacingVerticalS,
    minHeight: '200px',
  },

  // ── Scoring grid: 2-column, stacks on narrow ──────────────────────────────
  scoringGrid: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: tokens.spacingHorizontalM,
    // Stack vertically on narrow dialog widths
    '@media (max-width: 480px)': {
      gridTemplateColumns: '1fr',
    },
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
  mockDataNotice: {
    display: 'block',
    color: tokens.colorNeutralForeground4,
    fontStyle: 'italic',
    marginTop: tokens.spacingVerticalXS,
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

  // ── Footer actions row ────────────────────────────────────────────────────
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

/** Single suggested action button */
interface ISuggestedActionItemProps {
  action: ITodoScoringAction;
  onActionClick: (label: string) => void;
}

const SuggestedActionItem: React.FC<ISuggestedActionItemProps> = React.memo(
  ({ action, onActionClick }) => {
    const styles = useStyles();
    const IconComponent = resolveActionIcon(action.icon);

    const handleClick = React.useCallback(() => {
      onActionClick(action.label);
    }, [onActionClick, action.label]);

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

/** Result body: scoring grid + analysis + suggested actions */
interface IResultBodyProps {
  result: ITodoScoringResult;
  onActionClick: (label: string) => void;
}

const ResultBody: React.FC<IResultBodyProps> = ({ result, onActionClick }) => {
  const styles = useStyles();

  return (
    <>
      {/* ── Scoring grid: Priority (left) + Effort (right) ── */}
      <div className={styles.scoringGrid}>
        <PriorityScoreCard
          priority={result.priority}
          isMockData={result.isMockData}
        />
        <EffortScoreCard
          effort={result.effort}
          isMockData={result.isMockData}
        />
      </div>

      {/* ── Analysis card ── */}
      <Card appearance="subtle" className={styles.analysisCard}>
        <CardHeader
          className={styles.analysisCardHeader}
          header={
            <Text size={200} weight="semibold" style={{ color: tokens.colorNeutralForeground2 }}>
              Analysis
            </Text>
          }
        />
        <Text size={300} className={styles.analysisText}>
          {result.analysis}
        </Text>
        {result.isMockData && (
          <Text size={100} className={styles.mockDataNotice}>
            Preview data — connect to BFF for live AI analysis
          </Text>
        )}
      </Card>

      {/* ── Suggested actions ── */}
      {result.suggestedActions.length > 0 && (
        <div className={styles.suggestedActionsSection}>
          <Text size={200} className={styles.suggestedActionsLabel}>
            Suggested actions
          </Text>
          <ul
            className={styles.actionsList}
            role="list"
            aria-label="Suggested actions"
          >
            {result.suggestedActions.map((action) => (
              <SuggestedActionItem
                key={action.label}
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

export interface ITodoAISummaryDialogProps {
  /** Whether the dialog is open */
  isOpen: boolean;
  /** Subject / title of the to-do event (shown beneath the dialog title) */
  eventTitle: string;
  /** Scoring result from useTodoScoring hook */
  result: ITodoScoringResult | null;
  /** True while fetching from BFF or simulated mock delay */
  isLoading: boolean;
  /** User-friendly error message from the hook */
  error: string | null;
  /** Close the dialog */
  onClose: () => void;
  /** Retry after error */
  onRetry: () => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const TodoAISummaryDialog: React.FC<ITodoAISummaryDialogProps> = ({
  isOpen,
  eventTitle,
  result,
  isLoading,
  error,
  onClose,
  onRetry,
}) => {
  const styles = useStyles();

  // Handle suggested action clicks.
  // In R1 these are visual only — future tasks will wire real navigation.
  const handleActionClick = React.useCallback(
    (label: string) => {
      if (process.env.NODE_ENV !== 'production') {
        // eslint-disable-next-line no-console
        console.info(`[TodoAISummaryDialog] Suggested action clicked: "${label}"`);
      }
    },
    []
  );

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
          {/* ── Title ─────────────────────────────────────────────────── */}
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
              {eventTitle && (
                <Text
                  size={200}
                  className={styles.titleSubtitle}
                  title={eventTitle}
                >
                  {eventTitle}
                </Text>
              )}
            </div>
          </DialogTitle>

          {/* ── Content ───────────────────────────────────────────────── */}
          <DialogContent>
            <div className={styles.contentArea}>
              {/* Loading state */}
              {isLoading && (
                <div
                  className={styles.loadingContainer}
                  aria-live="polite"
                  aria-busy="true"
                >
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
                <ResultBody
                  result={result}
                  onActionClick={handleActionClick}
                />
              )}
            </div>
          </DialogContent>

          {/* ── Actions ───────────────────────────────────────────────── */}
          <DialogActions>
            <div className={styles.footerDivider}>
              <Button
                appearance="secondary"
                size="small"
                onClick={onClose}
                aria-label="Close AI Summary dialog"
              >
                Close
              </Button>
            </div>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

TodoAISummaryDialog.displayName = 'TodoAISummaryDialog';

// Default export enables React.lazy() dynamic import for bundle-size optimization (Task 033).
// Named export TodoAISummaryDialog above is preserved for direct imports in tests.
export default TodoAISummaryDialog;
