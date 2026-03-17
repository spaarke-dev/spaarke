/**
 * PlanPreviewCard - Renders a proposed multi-step plan with action controls.
 *
 * Displays a plan title, ordered steps with status indicators, and three
 * action buttons: Proceed (executes the plan), Edit Plan (opens inline
 * text input to send a modification message), and Cancel (aborts).
 *
 * Steps update in real-time as execution progresses — passing updated
 * `steps` props re-renders the card with current statuses and partial results.
 *
 * This card is the gate for Phase 2F compound intent execution:
 * no plan executes until the user clicks Proceed.
 *
 * @see ADR-012 - Shared Component Library (callback-based, no Xrm)
 * @see ADR-021 - Fluent UI v9 design tokens, dark mode support
 * @see spec-2E / spec-2F - Plan preview and execution gate requirements
 */

import * as React from 'react';
import {
  Card,
  makeStyles,
  tokens,
  Text,
  Button,
  Spinner,
  Input,
  mergeClasses,
} from '@fluentui/react-components';
import {
  CheckmarkCircle20Regular,
  DismissCircle20Regular,
  ArrowRight20Regular,
  Edit20Regular,
  Dismiss20Regular,
} from '@fluentui/react-icons';

// ─────────────────────────────────────────────────────────────────────────────
// Types (exported for consumers)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Status of an individual plan step during execution.
 * - pending:   Not yet started (shows a numbered circle)
 * - running:   Currently executing (shows a Spinner)
 * - completed: Finished successfully (shows CheckmarkCircle)
 * - failed:    Finished with an error (shows DismissCircle)
 */
export type PlanStepStatus = 'pending' | 'running' | 'completed' | 'failed';

/**
 * A single step within a proposed plan.
 */
export interface PlanStep {
  /** Stable unique identifier for this step. */
  id: string;
  /** Human-readable description of what this step does. */
  description: string;
  /** Current execution status; defaults to 'pending' before execution begins. */
  status: PlanStepStatus;
  /**
   * Optional partial result text streamed in while the step is running or
   * the full result after the step completes. Rendered as muted text below
   * the step description.
   */
  result?: string;
}

/**
 * Props for the PlanPreviewCard component.
 */
export interface PlanPreviewCardProps {
  /** Display title for the plan (e.g., "Analyze Contract Risk and Summarize Findings"). */
  planTitle: string;
  /** Ordered list of plan steps. Update this array to reflect execution progress. */
  steps: PlanStep[];
  /**
   * Whether the plan is currently executing.
   * When true, the Proceed button is disabled to prevent double-submission.
   */
  isExecuting: boolean;
  /**
   * Called when the user confirms they want to execute the plan.
   * The parent should begin streaming execution and update step statuses.
   */
  onProceed: () => void;
  /**
   * Called when the user wants to cancel the plan.
   * The parent should abort any pending execution and dismiss the card.
   */
  onCancel: () => void;
  /**
   * Called when the user submits an edit message to modify the plan.
   * The parent should send this message to the BFF as a new chat message.
   * @param editMessage - Free-text modification request from the user.
   */
  onEditPlan: (editMessage: string) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  card: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingBottom: tokens.spacingVerticalM,
  },
  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  planTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    color: tokens.colorNeutralForeground1,
  },
  stepList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    listStyle: 'none',
    margin: '0',
    padding: '0',
  },
  stepItem: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  stepRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
  },
  stepIndicator: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    flexShrink: 0,
    width: '20px',
    height: '20px',
    marginTop: '2px',
  },
  stepNumberCircle: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '18px',
    height: '18px',
    borderRadius: '50%',
    border: `1px solid ${tokens.colorNeutralForeground3}`,
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    lineHeight: '1',
  },
  stepNumberCircleRunning: {
    border: `1px solid ${tokens.colorBrandForeground1}`,
    color: tokens.colorBrandForeground1,
  },
  iconCompleted: {
    color: tokens.colorPaletteGreenForeground1,
  },
  iconFailed: {
    color: tokens.colorPaletteRedForeground1,
  },
  stepDescription: {
    flex: 1,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300,
  },
  stepDescriptionRunning: {
    fontWeight: tokens.fontWeightSemibold,
  },
  stepDescriptionCompleted: {
    color: tokens.colorNeutralForeground2,
  },
  stepDescriptionFailed: {
    color: tokens.colorPaletteRedForeground1,
  },
  stepResult: {
    marginLeft: `calc(${tokens.spacingHorizontalS} + 20px)`,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    lineHeight: tokens.lineHeightBase200,
    fontStyle: 'italic',
    display: '-webkit-box',
    WebkitLineClamp: 3,
    WebkitBoxOrient: 'vertical' as const,
    overflow: 'hidden',
  },
  actionRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
    marginTop: tokens.spacingVerticalXS,
  },
  editPlanSection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    marginTop: tokens.spacingVerticalXS,
  },
  editPlanRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  editPlanInput: {
    flex: 1,
  },
  divider: {
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    marginTop: tokens.spacingVerticalXS,
    marginBottom: tokens.spacingVerticalXS,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

interface StepIndicatorProps {
  step: PlanStep;
  order: number;
  styles: ReturnType<typeof useStyles>;
}

const StepIndicator: React.FC<StepIndicatorProps> = ({ step, order, styles }) => {
  switch (step.status) {
    case 'running':
      return (
        <div className={styles.stepIndicator}>
          <Spinner size="tiny" aria-label="Running" />
        </div>
      );
    case 'completed':
      return (
        <div className={styles.stepIndicator}>
          <CheckmarkCircle20Regular className={styles.iconCompleted} aria-label="Completed" />
        </div>
      );
    case 'failed':
      return (
        <div className={styles.stepIndicator}>
          <DismissCircle20Regular className={styles.iconFailed} aria-label="Failed" />
        </div>
      );
    case 'pending':
    default:
      return (
        <div className={styles.stepIndicator}>
          <span className={styles.stepNumberCircle} aria-label={`Step ${order}`}>
            {order}
          </span>
        </div>
      );
  }
};

function getDescriptionClass(
  status: PlanStepStatus,
  styles: ReturnType<typeof useStyles>
): string {
  switch (status) {
    case 'running':
      return mergeClasses(styles.stepDescription, styles.stepDescriptionRunning);
    case 'completed':
      return mergeClasses(styles.stepDescription, styles.stepDescriptionCompleted);
    case 'failed':
      return mergeClasses(styles.stepDescription, styles.stepDescriptionFailed);
    default:
      return styles.stepDescription;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * PlanPreviewCard
 *
 * Renders a proposed multi-step AI plan with Proceed/Edit Plan/Cancel controls
 * and per-step progress indicators. Designed to be the execution gate for
 * compound AI intents: the plan does not execute until the user clicks Proceed.
 *
 * @example
 * ```tsx
 * <PlanPreviewCard
 *   planTitle="Analyze Contract Risk and Summarize Findings"
 *   steps={[
 *     { id: 's1', description: 'Extract key clauses', status: 'completed', result: '5 clauses found' },
 *     { id: 's2', description: 'Assess risk level', status: 'running' },
 *     { id: 's3', description: 'Generate summary', status: 'pending' },
 *   ]}
 *   isExecuting={true}
 *   onProceed={handleProceed}
 *   onCancel={handleCancel}
 *   onEditPlan={handleEditPlan}
 * />
 * ```
 */
export const PlanPreviewCard: React.FC<PlanPreviewCardProps> = ({
  planTitle,
  steps,
  isExecuting,
  onProceed,
  onCancel,
  onEditPlan,
}) => {
  const styles = useStyles();

  // Edit Plan mode state
  const [isEditMode, setIsEditMode] = React.useState(false);
  const [editMessage, setEditMessage] = React.useState('');

  const handleEditPlanToggle = React.useCallback(() => {
    setIsEditMode(prev => !prev);
    setEditMessage('');
  }, []);

  const handleEditSubmit = React.useCallback(() => {
    const trimmed = editMessage.trim();
    if (trimmed) {
      onEditPlan(trimmed);
      setIsEditMode(false);
      setEditMessage('');
    }
  }, [editMessage, onEditPlan]);

  const handleEditKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        handleEditSubmit();
      }
      if (e.key === 'Escape') {
        setIsEditMode(false);
        setEditMessage('');
      }
    },
    [handleEditSubmit]
  );

  return (
    <Card className={styles.card} role="region" aria-label={`Plan preview: ${planTitle}`}>
      {/* Header */}
      <div className={styles.header}>
        <Text className={styles.planTitle}>{planTitle}</Text>
      </div>

      {/* Step List */}
      <ol className={styles.stepList} aria-label="Plan steps">
        {steps.map((step, index) => (
          <li key={step.id} className={styles.stepItem}>
            <div className={styles.stepRow}>
              <StepIndicator step={step} order={index + 1} styles={styles} />
              <Text className={getDescriptionClass(step.status, styles)}>{step.description}</Text>
            </div>
            {step.result && (
              <Text className={styles.stepResult} aria-label={`Step ${index + 1} result`}>
                {step.result}
              </Text>
            )}
          </li>
        ))}
      </ol>

      <div className={styles.divider} role="separator" />

      {/* Edit Plan Section (toggled) */}
      {isEditMode && (
        <div className={styles.editPlanSection}>
          <div className={styles.editPlanRow}>
            <Input
              className={styles.editPlanInput}
              placeholder="How would you like to change this plan?"
              value={editMessage}
              onChange={(_e, data) => setEditMessage(data.value)}
              onKeyDown={handleEditKeyDown}
              autoFocus
              aria-label="Edit plan message"
            />
            <Button
              appearance="primary"
              size="small"
              onClick={handleEditSubmit}
              disabled={!editMessage.trim()}
              aria-label="Submit plan edit"
            >
              Submit
            </Button>
            <Button
              appearance="subtle"
              size="small"
              icon={<Dismiss20Regular />}
              onClick={handleEditPlanToggle}
              aria-label="Cancel edit"
            />
          </div>
        </div>
      )}

      {/* Action Buttons */}
      <div className={styles.actionRow}>
        <Button
          appearance="primary"
          icon={<ArrowRight20Regular />}
          onClick={onProceed}
          disabled={isExecuting}
          aria-label="Proceed with plan"
          aria-disabled={isExecuting}
        >
          {isExecuting ? 'Executing...' : 'Proceed'}
        </Button>

        <Button
          appearance="secondary"
          icon={<Edit20Regular />}
          onClick={handleEditPlanToggle}
          disabled={isExecuting}
          aria-label="Edit plan"
          aria-pressed={isEditMode}
        >
          Edit Plan
        </Button>

        <Button
          appearance="subtle"
          icon={<Dismiss20Regular />}
          onClick={onCancel}
          aria-label="Cancel plan"
        >
          Cancel
        </Button>
      </div>
    </Card>
  );
};
