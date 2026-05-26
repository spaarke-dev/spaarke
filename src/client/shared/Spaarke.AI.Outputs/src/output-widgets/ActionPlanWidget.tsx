/**
 * ActionPlanWidget
 *
 * Renders a multi-step action plan as a Fluent v9 Checkbox checklist in the
 * AI output pane. Users can mark steps complete — completion state is local
 * (useState, not persisted) unless an onStepComplete callback is provided.
 *
 * Props:
 *   - data.title?: string — optional widget heading
 *   - data.steps[]  — list of steps with id, label, optional description,
 *                      and optional initial completed value
 *   - onStepComplete?: (stepId, completed) => void — called on toggle, if provided
 *
 * Completion state is initialised from steps[].completed on mount and tracks
 * locally thereafter. The widget does NOT read back data.steps after the
 * initial render (it is the source of truth for checked state).
 *
 * All colors and spacing use Fluent v9 design tokens (ADR-021). Dark mode is
 * supported automatically via FluentProvider theme switching.
 *
 * NOT PCF-safe — requires React 19 and Fluent UI v9.
 *
 * @see ADR-021 — Fluent UI v9 design system (no hard-coded colors)
 * @see ADR-012 — Shared component library
 */

import * as React from 'react';
import { makeStyles, mergeClasses, tokens, Text, Checkbox, Spinner } from '@fluentui/react-components';
import type { CheckboxOnChangeData } from '@fluentui/react-components';
import type { OutputWidgetProps } from '../types';

// ---------------------------------------------------------------------------
// Data types
// ---------------------------------------------------------------------------

/** A single step in the action plan. */
export interface ActionPlanStep {
  /** Unique identifier for this step (used as the Checkbox key and callback arg). */
  id: string;
  /** Short label shown next to the checkbox. */
  label: string;
  /** Optional longer description shown below the label. */
  description?: string;
  /** Initial completion state. Defaults to false when omitted. */
  completed?: boolean;
}

export interface ActionPlanData {
  /** Optional widget title / heading. */
  title?: string;
  /** Ordered list of action plan steps. */
  steps: ActionPlanStep[];
}

export type ActionPlanWidgetProps = OutputWidgetProps<ActionPlanData> & {
  /**
   * Optional callback fired when a step's completion state is toggled.
   * When not provided, completion is tracked locally only.
   *
   * @param stepId    - The id of the step that was toggled.
   * @param completed - The new completion state after the toggle.
   */
  onStepComplete?: (stepId: string, completed: boolean) => void;
};

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalL,
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
  },
  stepList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  stepRow: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    paddingLeft: tokens.spacingHorizontalXS,
  },
  stepDescription: {
    // Indent to align with the checkbox label text (accounts for checkbox width)
    paddingLeft: tokens.spacingHorizontalXL,
    color: tokens.colorNeutralForeground2,
  },
  completedLabel: {
    textDecorationLine: 'line-through',
    color: tokens.colorNeutralForeground4,
  },
  errorText: {
    color: tokens.colorStatusDangerForeground1,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Build the initial completion state Record from step definitions.
 */
function buildInitialState(steps: ActionPlanStep[]): Record<string, boolean> {
  const state: Record<string, boolean> = {};
  for (const step of steps) {
    state[step.id] = step.completed ?? false;
  }
  return state;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * ActionPlanWidget renders an ordered checklist of action plan steps.
 * Each step uses a Fluent v9 Checkbox. Completion state is tracked locally
 * and optionally reported to a parent via the onStepComplete callback.
 */
export default function ActionPlanWidget({
  data,
  isLoading,
  error,
  className,
  onStepComplete,
}: ActionPlanWidgetProps): React.ReactElement {
  const styles = useStyles();

  // Local completion state — initialised from data.steps[].completed.
  // We intentionally do NOT use data.steps as a dependency in an effect;
  // the widget owns completion state after mount.
  const [completedState, setCompletedState] = React.useState<Record<string, boolean>>(() =>
    buildInitialState(data.steps)
  );

  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Spinner size="medium" label="Loading action plan..." />
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Text className={styles.errorText}>{error}</Text>
      </div>
    );
  }

  const handleChange = (
    stepId: string,
    _ev: React.ChangeEvent<HTMLInputElement>,
    checkboxData: CheckboxOnChangeData
  ): void => {
    const newCompleted = checkboxData.checked === true;
    setCompletedState(prev => ({ ...prev, [stepId]: newCompleted }));
    onStepComplete?.(stepId, newCompleted);
  };

  return (
    <div className={mergeClasses(styles.root, className)}>
      {data.title && (
        <Text size={500} className={styles.title}>
          {data.title}
        </Text>
      )}

      <div className={styles.stepList}>
        {data.steps.map(step => {
          const isCompleted = completedState[step.id] ?? false;
          return (
            <div key={step.id} className={styles.stepRow}>
              <Checkbox
                checked={isCompleted}
                label={
                  <Text size={300} className={isCompleted ? styles.completedLabel : undefined}>
                    {step.label}
                  </Text>
                }
                onChange={(ev, checkboxData) => handleChange(step.id, ev, checkboxData)}
              />
              {step.description && (
                <Text size={200} className={styles.stepDescription}>
                  {step.description}
                </Text>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
