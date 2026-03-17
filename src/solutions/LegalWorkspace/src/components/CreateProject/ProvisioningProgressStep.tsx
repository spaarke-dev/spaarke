/**
 * ProvisioningProgressStep.tsx
 * Shows real-time provisioning progress during Secure Project infrastructure setup.
 *
 * Displayed in the wizard after project record creation when sprk_issecure = true.
 * Renders an animated list of provisioning steps with a spinner for the active step
 * and a checkmark / error icon for completed steps.
 *
 * States:
 *   - pending   → neutral text
 *   - active    → spinner + blue text
 *   - done      → green checkmark
 *   - error     → red X + error message
 *
 * Constraints:
 *   - Fluent v9 only: Spinner, Text, makeStyles, tokens
 *   - makeStyles with semantic tokens — ZERO hard-coded colours (ADR-021)
 *   - Supports light, dark, and high-contrast modes
 */

import * as React from 'react';
import {
  Spinner,
  Text,
  makeStyles,
  tokens,
  MessageBar,
  MessageBarBody,
} from '@fluentui/react-components';
import {
  CheckmarkCircleFilled,
  DismissCircleFilled,
} from '@fluentui/react-icons';
import { PROVISIONING_STEPS, type ProvisioningStepKey } from './provisioningService';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export type ProvisioningStepStatus = 'pending' | 'active' | 'done' | 'error';

export interface IProvisioningStepState {
  key: ProvisioningStepKey;
  status: ProvisioningStepStatus;
}

export interface IProvisioningProgressStepProps {
  /** Current state of each provisioning step. */
  steps: IProvisioningStepState[];
  /**
   * Error message to display below the steps when provisioning fails.
   * When set, the step with status 'error' is highlighted.
   */
  errorMessage?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },

  // ── Header ────────────────────────────────────────────────────────────────
  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  title: {
    color: tokens.colorNeutralForeground1,
  },
  subtitle: {
    color: tokens.colorNeutralForeground3,
  },

  // ── Step list ──────────────────────────────────────────────────────────────
  stepList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
  },

  // ── Individual step row ────────────────────────────────────────────────────
  stepRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    minHeight: '28px',
  },

  stepIconPending: {
    width: '16px',
    height: '16px',
    borderRadius: '50%',
    border: `2px solid ${tokens.colorNeutralStroke2}`,
    flexShrink: 0,
  },
  stepIconActive: {
    flexShrink: 0,
  },
  stepIconDone: {
    color: tokens.colorPaletteGreenForeground1,
    flexShrink: 0,
    fontSize: '16px',
    display: 'flex',
    alignItems: 'center',
  },
  stepIconError: {
    color: tokens.colorPaletteRedForeground1,
    flexShrink: 0,
    fontSize: '16px',
    display: 'flex',
    alignItems: 'center',
  },

  stepTextPending: {
    color: tokens.colorNeutralForeground3,
  },
  stepTextActive: {
    color: tokens.colorBrandForeground1,
    fontWeight: '600',
  },
  stepTextDone: {
    color: tokens.colorNeutralForeground2,
  },
  stepTextError: {
    color: tokens.colorPaletteRedForeground1,
  },

  // ── Error bar ──────────────────────────────────────────────────────────────
  errorBar: {
    borderRadius: tokens.borderRadiusMedium,
  },
});

// ---------------------------------------------------------------------------
// ProvisioningProgressStep (exported)
// ---------------------------------------------------------------------------

export const ProvisioningProgressStep: React.FC<IProvisioningProgressStepProps> = ({
  steps,
  errorMessage,
}) => {
  const styles = useStyles();

  const isComplete = steps.every((s) => s.status === 'done');
  const hasError = !!errorMessage || steps.some((s) => s.status === 'error');

  return (
    <div className={styles.root}>
      {/* Header */}
      <div className={styles.header}>
        <Text as="h2" size={500} weight="semibold" className={styles.title}>
          {hasError
            ? 'Provisioning failed'
            : isComplete
            ? 'Infrastructure provisioned!'
            : 'Setting up Secure Project\u2026'}
        </Text>
        <Text size={200} className={styles.subtitle}>
          {hasError
            ? 'An error occurred during provisioning. The project record has been created but some infrastructure may need manual setup.'
            : isComplete
            ? 'All infrastructure has been provisioned. The project is ready for external access.'
            : 'Please wait while we provision the required infrastructure. This may take a few seconds.'}
        </Text>
      </div>

      {/* Step list */}
      <div className={styles.stepList} role="list" aria-label="Provisioning steps">
        {steps.map((step) => {
          const stepDef = PROVISIONING_STEPS.find((s) => s.key === step.key);
          const label = stepDef?.label ?? step.key;

          return (
            <div key={step.key} className={styles.stepRow} role="listitem">
              {/* Status icon */}
              {step.status === 'pending' && (
                <div className={styles.stepIconPending} aria-hidden="true" />
              )}
              {step.status === 'active' && (
                <Spinner
                  size="extra-tiny"
                  className={styles.stepIconActive}
                  aria-label="In progress"
                />
              )}
              {step.status === 'done' && (
                <span className={styles.stepIconDone} aria-label="Complete">
                  <CheckmarkCircleFilled fontSize={16} />
                </span>
              )}
              {step.status === 'error' && (
                <span className={styles.stepIconError} aria-label="Failed">
                  <DismissCircleFilled fontSize={16} />
                </span>
              )}

              {/* Step label */}
              <Text
                size={300}
                className={
                  step.status === 'pending'
                    ? styles.stepTextPending
                    : step.status === 'active'
                    ? styles.stepTextActive
                    : step.status === 'done'
                    ? styles.stepTextDone
                    : styles.stepTextError
                }
              >
                {step.status === 'done'
                  ? label.replace('\u2026', '')
                  : label}
              </Text>
            </div>
          );
        })}
      </div>

      {/* Error detail bar */}
      {errorMessage && (
        <MessageBar intent="error" className={styles.errorBar}>
          <MessageBarBody>
            <Text size={200}>{errorMessage}</Text>
          </MessageBarBody>
        </MessageBar>
      )}
    </div>
  );
};

export default ProvisioningProgressStep;
