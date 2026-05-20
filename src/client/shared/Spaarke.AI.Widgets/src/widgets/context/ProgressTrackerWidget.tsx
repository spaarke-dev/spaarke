/**
 * @spaarke/ai-widgets — ProgressTrackerWidget
 *
 * Context pane widget that renders workflow step progress during multi-step
 * AI operations (contract review pipelines, due diligence workflows, etc.).
 *
 * Data shape (ProgressTrackerData):
 *   { title, steps: WorkflowStep[], currentStepIndex, totalSteps }
 *
 * Step states:
 *   - completed  — CheckmarkCircle icon, colorPaletteGreenForeground1
 *   - active     — Spinner (Fluent v9), brand color, subtle pulse ring
 *   - pending    — Circle icon, colorNeutralForeground4 (muted)
 *
 * Real-time updates: subscribes to 'context' channel context_update events.
 * Each event carries a full replacement payload (no cumulative merging).
 *
 * All-completed transition: when every step reaches 'completed', the widget
 * dispatches a context_update to the 'related-items' stage on PaneEventBus
 * after a 1 500 ms delay to let the user see the final state.
 *
 * All styling via makeStyles + Fluent v9 tokens — no hard-coded colors (ADR-021).
 *
 * React 19, NOT PCF-safe.
 *
 * Task: AIPU2-088
 * AC:   FR-201 (context pane), FR-204 (progress tracking)
 */

import React, { useState, useEffect, useRef, useCallback } from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
  mergeClasses,
} from '@fluentui/react-components';
import {
  CheckmarkCircleRegular,
  CircleRegular,
} from '@fluentui/react-icons';
import { usePaneEvent } from '../../events/usePaneEvent';
import { useDispatchPaneEvent } from '../../events/useDispatchPaneEvent';
import type { ContextWidgetProps } from '../../types/widget-types';
import type { ContextPaneEvent } from '../../events/PaneEventTypes';

// ---------------------------------------------------------------------------
// Domain types
// ---------------------------------------------------------------------------

/**
 * A single workflow step displayed in the progress tracker.
 */
export interface WorkflowStep {
  /** Stable identifier used as React key and for test assertions. */
  id: string;
  /** Human-readable step label shown in the list. */
  label: string;
  /** Current execution state of this step. */
  status: 'completed' | 'active' | 'pending';
  /**
   * Optional sub-step detail text shown below the label.
   * Collapsed by default, expands when the user clicks the step row.
   */
  detail?: string;
}

/**
 * Data payload delivered to ProgressTrackerWidget via context_update events.
 *
 * The server sends the full state on every update — the client replaces,
 * never merges. This matches the task spec (step 5) and simplifies state.
 */
export interface ProgressTrackerData {
  /** Widget header title (e.g. "Contract Review Pipeline"). */
  title: string;
  /** Ordered list of workflow steps. */
  steps: WorkflowStep[];
  /** Zero-based index of the currently active step. */
  currentStepIndex: number;
  /** Total number of steps (may exceed steps.length during streaming). */
  totalSteps: number;
}

/** Type guard: checks that an unknown value conforms to ProgressTrackerData. */
function isProgressTrackerData(value: unknown): value is ProgressTrackerData {
  if (!value || typeof value !== 'object') return false;
  const v = value as Record<string, unknown>;
  return (
    typeof v['title'] === 'string' &&
    Array.isArray(v['steps']) &&
    typeof v['currentStepIndex'] === 'number' &&
    typeof v['totalSteps'] === 'number'
  );
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    padding: tokens.spacingHorizontalM,
    gap: tokens.spacingVerticalS,
    boxSizing: 'border-box',
    overflowY: 'auto',
  },

  // --- Header ---

  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalS,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },

  title: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    color: tokens.colorNeutralForeground1,
  },

  // --- Step list ---

  stepList: {
    display: 'flex',
    flexDirection: 'column',
    gap: '0px',
    flex: '1 1 auto',
    minHeight: 0,
  },

  stepRow: {
    display: 'flex',
    flexDirection: 'column',
    position: 'relative',
    cursor: 'default',
  },

  /** Clickable row when a detail string is present. */
  stepRowExpandable: {
    cursor: 'pointer',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground2,
      borderRadius: tokens.borderRadiusMedium,
    },
  },

  stepRowInner: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalXS}`,
    minHeight: '36px',
  },

  // --- Connector line between steps ---

  connectorWrap: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'stretch',
    paddingLeft: `calc(${tokens.spacingHorizontalXS} + 10px)`, // icon half-width
  },

  connector: {
    width: '1px',
    minHeight: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralStroke2,
    marginLeft: '0px',
  },

  // --- Icons ---

  iconCompleted: {
    color: tokens.colorPaletteGreenForeground1,
    fontSize: '20px',
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
  },

  iconActive: {
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
    // Pulse ring rendered via box-shadow on the spinner wrapper
    borderRadius: '50%',
    animationName: {
      '0%': { boxShadow: `0 0 0 0 ${tokens.colorBrandBackground}40` },
      '70%': { boxShadow: `0 0 0 6px ${tokens.colorBrandBackground}00` },
      '100%': { boxShadow: `0 0 0 0 ${tokens.colorBrandBackground}00` },
    },
    animationDuration: '2s',
    animationTimingFunction: 'ease-out',
    animationIterationCount: 'infinite',
  },

  iconPending: {
    color: tokens.colorNeutralForeground4,
    fontSize: '20px',
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
  },

  // --- Step text ---

  stepLabel: {
    flex: '1 1 auto',
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
  },

  stepLabelActive: {
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },

  stepLabelCompleted: {
    color: tokens.colorNeutralForeground3,
  },

  stepLabelPending: {
    color: tokens.colorNeutralForeground3,
  },

  stepDetail: {
    paddingLeft: `calc(${tokens.spacingHorizontalXS} + 20px + ${tokens.spacingHorizontalS})`,
    paddingBottom: tokens.spacingVerticalXS,
    paddingRight: tokens.spacingHorizontalXS,
  },

  stepDetailText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    lineHeight: tokens.lineHeightBase300,
  },

  // --- Empty / loading state ---

  centered: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    flex: '1 1 auto',
    minHeight: '80px',
  },

  // --- Footer summary ---

  footer: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'flex-end',
    paddingTop: tokens.spacingVerticalS,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    marginTop: 'auto',
  },

  footerText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------

interface StepIconProps {
  status: WorkflowStep['status'];
  styles: ReturnType<typeof useStyles>;
}

const StepIcon: React.FC<StepIconProps> = ({ status, styles }) => {
  switch (status) {
    case 'completed':
      return (
        <span className={styles.iconCompleted} aria-label="Completed">
          <CheckmarkCircleRegular fontSize={20} />
        </span>
      );
    case 'active':
      return (
        <span className={styles.iconActive} aria-label="In progress">
          <Spinner size="tiny" aria-label="Step in progress" />
        </span>
      );
    case 'pending':
      return (
        <span className={styles.iconPending} aria-label="Pending">
          <CircleRegular fontSize={20} />
        </span>
      );
  }
};

interface StepItemProps {
  step: WorkflowStep;
  isLast: boolean;
  styles: ReturnType<typeof useStyles>;
}

const StepItem: React.FC<StepItemProps> = ({ step, isLast, styles }) => {
  const [expanded, setExpanded] = useState(false);

  const labelClass = mergeClasses(
    styles.stepLabel,
    step.status === 'active' && styles.stepLabelActive,
    step.status === 'completed' && styles.stepLabelCompleted,
    step.status === 'pending' && styles.stepLabelPending
  );

  const rowClass = mergeClasses(
    styles.stepRow,
    step.detail !== undefined && styles.stepRowExpandable
  );

  const handleClick = useCallback(() => {
    if (step.detail !== undefined) {
      setExpanded((prev) => !prev);
    }
  }, [step.detail]);

  return (
    <div className={rowClass} data-testid={`step-${step.id}`}>
      <div
        className={styles.stepRowInner}
        onClick={handleClick}
        aria-expanded={step.detail !== undefined ? expanded : undefined}
        role={step.detail !== undefined ? 'button' : undefined}
      >
        <StepIcon status={step.status} styles={styles} />
        <Text className={labelClass}>{step.label}</Text>
      </div>

      {/* Expandable detail */}
      {step.detail !== undefined && expanded && (
        <div className={styles.stepDetail}>
          <Text className={styles.stepDetailText}>{step.detail}</Text>
        </div>
      )}

      {/* Connector line to next step */}
      {!isLast && (
        <div className={styles.connectorWrap}>
          <div className={styles.connector} />
        </div>
      )}
    </div>
  );
};

// ---------------------------------------------------------------------------
// Main component
// ---------------------------------------------------------------------------

/**
 * ProgressTrackerWidget renders multi-step workflow progress in the Context pane.
 *
 * Receives initial data via props (contextData from the first context_update),
 * then subscribes to subsequent context_update events on the 'context' channel
 * to replace the step list reactively.
 *
 * When all steps reach 'completed', dispatches a context_update event targeting
 * the 'related-items' stage on PaneEventBus after a 1 500 ms delay.
 */
const ProgressTrackerWidget: React.FC<ContextWidgetProps<ProgressTrackerData | unknown>> = ({
  data: initialData,
  isLoading,
  className,
}) => {
  const styles = useStyles();
  const dispatch = useDispatchPaneEvent();

  // -------------------------------------------------------------------------
  // State
  //
  // `trackerData` is initialised from props and then replaced wholesale on
  // each context_update event. The server always sends full state.
  // -------------------------------------------------------------------------

  const [trackerData, setTrackerData] = useState<ProgressTrackerData | null>(() => {
    return isProgressTrackerData(initialData) ? initialData : null;
  });

  // Keep a ref so the all-completed effect closure can read the latest data
  // without triggering effect re-runs.
  const trackerDataRef = useRef(trackerData);
  trackerDataRef.current = trackerData;

  // -------------------------------------------------------------------------
  // Subscribe to context_update events
  // -------------------------------------------------------------------------

  usePaneEvent('context', (event: ContextPaneEvent) => {
    if (event.type !== 'context_update') return;
    if (!isProgressTrackerData(event.contextData)) return;
    setTrackerData(event.contextData);
  });

  // -------------------------------------------------------------------------
  // All-completed transition
  //
  // When every step transitions to 'completed', wait 1 500 ms then dispatch
  // a context_update signalling the 'related-items' stage.
  // -------------------------------------------------------------------------

  const allCompletedTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (!trackerData || trackerData.steps.length === 0) return;

    const allDone = trackerData.steps.every((s) => s.status === 'completed');
    if (!allDone) return;

    // Guard: don't re-fire if a timer is already running.
    if (allCompletedTimerRef.current !== null) return;

    allCompletedTimerRef.current = setTimeout(() => {
      allCompletedTimerRef.current = null;
      dispatch('context', {
        type: 'context_update',
        contextType: 'related-items',
        contextData: {
          source: 'progress-tracker',
          completedSteps: trackerDataRef.current?.steps.length ?? 0,
        },
      });
    }, 1500);

    return () => {
      if (allCompletedTimerRef.current !== null) {
        clearTimeout(allCompletedTimerRef.current);
        allCompletedTimerRef.current = null;
      }
    };
  }, [trackerData, dispatch]);

  // -------------------------------------------------------------------------
  // Render: loading
  // -------------------------------------------------------------------------

  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.centered}>
          <Spinner size="medium" label="Loading progress…" />
        </div>
      </div>
    );
  }

  // -------------------------------------------------------------------------
  // Render: empty (no data yet)
  //
  // Per AC: "Empty step list renders a Spinner only — not a blank pane."
  // -------------------------------------------------------------------------

  if (!trackerData || trackerData.steps.length === 0) {
    return (
      <div className={mergeClasses(styles.root, className)} data-testid="progress-tracker-empty">
        <div className={styles.centered}>
          <Spinner size="medium" label="Waiting for workflow steps…" />
        </div>
      </div>
    );
  }

  // -------------------------------------------------------------------------
  // Render: step list
  // -------------------------------------------------------------------------

  const { title, steps, currentStepIndex, totalSteps } = trackerData;
  const summaryStep = Math.min(currentStepIndex + 1, totalSteps);

  return (
    <div
      className={mergeClasses(styles.root, className)}
      data-testid="progress-tracker-root"
      aria-label={`Workflow progress: ${title}`}
    >
      {/* Header */}
      <div className={styles.header}>
        <Text className={styles.title}>{title}</Text>
      </div>

      {/* Step list */}
      <div className={styles.stepList} role="list" aria-label="Workflow steps">
        {steps.map((step, index) => (
          <StepItem
            key={step.id}
            step={step}
            isLast={index === steps.length - 1}
            styles={styles}
          />
        ))}
      </div>

      {/* Footer summary */}
      <div className={styles.footer}>
        <Text className={styles.footerText} aria-live="polite">
          {`Step ${summaryStep} of ${totalSteps}`}
        </Text>
      </div>
    </div>
  );
};

export default ProgressTrackerWidget;
