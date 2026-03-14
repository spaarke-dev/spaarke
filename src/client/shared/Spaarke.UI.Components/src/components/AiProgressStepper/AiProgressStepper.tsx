/**
 * AiProgressStepper
 *
 * Fluent v9 compliant multi-step progress indicator for AI analysis operations.
 * Displays a horizontal step track with all steps visible — active (blue),
 * completed (green), pending (grey). The active step's description and a short
 * indeterminate ProgressBar appear below the track.
 *
 * Variants:
 *   - `card`: absolute-positioned overlay with semi-transparent backdrop
 *   - `inline`: flat layout embedded directly in parent container
 *
 * @see ADR-021 - Fluent UI v9 design system
 * @see ADR-012 - Shared Component Library conventions
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  ProgressBar,
  Button,
} from "@fluentui/react-components";
import {
  CheckmarkCircle16Filled,
  ErrorCircle16Filled,
  Dismiss20Regular,
} from "@fluentui/react-icons";
import type {
  AiProgressStepperProps,
  AiProgressStepStatus,
} from "./AiProgressStepper.types";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // Card variant — absolute overlay
  backdrop: {
    position: "absolute",
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: tokens.colorNeutralBackgroundAlpha2,
    zIndex: 10,
  },
  card: {
    width: "560px",
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusLarge,
    boxShadow: tokens.shadow16,
    paddingTop: tokens.spacingVerticalXL,
    paddingBottom: tokens.spacingVerticalXL,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
  },

  // Inline variant — flat layout
  inline: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    width: "100%",
  },

  // Header row (title + optional cancel button)
  header: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    minHeight: "28px",
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },

  // ── Horizontal step track ─────────────────────────────────────────────
  stepTrack: {
    display: "flex",
    alignItems: "flex-start",
    justifyContent: "center", // Center chips regardless of step count
    width: "100%",
  },

  // Each step chip: indicator circle + label below, centered
  stepChip: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    gap: tokens.spacingVerticalXS,
    flexShrink: 0,
    minWidth: "64px",
  },

  // Connector line between chips — fixed width so 2-step looks same as 5-step
  connector: {
    width: "48px",
    height: "1px",
    backgroundColor: tokens.colorNeutralStroke2,
    marginTop: "9px", // vertically centers with the 20px indicator (20/2 - 1/2)
    flexShrink: 0,
  },
  connectorCompleted: {
    backgroundColor: tokens.colorPaletteGreenBackground3,
  },

  // Step indicators (circles)
  indicatorPending: {
    width: "20px",
    height: "20px",
    borderRadius: "50%",
    borderTopWidth: "2px",
    borderRightWidth: "2px",
    borderBottomWidth: "2px",
    borderLeftWidth: "2px",
    borderTopStyle: "solid",
    borderRightStyle: "solid",
    borderBottomStyle: "solid",
    borderLeftStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke1,
    borderRightColor: tokens.colorNeutralStroke1,
    borderBottomColor: tokens.colorNeutralStroke1,
    borderLeftColor: tokens.colorNeutralStroke1,
    backgroundColor: "transparent",
    flexShrink: 0,
    boxSizing: "border-box",
  },
  indicatorActive: {
    width: "20px",
    height: "20px",
    borderRadius: "50%",
    backgroundColor: tokens.colorBrandBackground,
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
  },
  activeInnerDot: {
    width: "8px",
    height: "8px",
    borderRadius: "50%",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  indicatorCompleted: {
    width: "20px",
    height: "20px",
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    color: tokens.colorPaletteGreenForeground1,
  },
  indicatorError: {
    width: "20px",
    height: "20px",
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    color: tokens.colorPaletteRedForeground1,
  },

  // Step label text (centered below indicator)
  labelPending: {
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
  },
  labelActive: {
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
    textAlign: "center",
  },
  labelCompleted: {
    color: tokens.colorNeutralForeground2,
    textAlign: "center",
  },
  labelError: {
    color: tokens.colorPaletteRedForeground1,
    textAlign: "center",
  },

  // ── Active step detail (description + short progress bar) ─────────────
  activeDetail: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalXS,
  },
  description: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },
  progressBarWrap: {
    maxWidth: "240px",
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function getStepStatus(
  stepId: string,
  activeStepId: string | null,
  completedStepIds: string[],
  errorStepId?: string | null,
): AiProgressStepStatus {
  if (errorStepId === stepId) return "error";
  if (completedStepIds.includes(stepId)) return "completed";
  if (activeStepId === stepId) return "active";
  return "pending";
}

// ---------------------------------------------------------------------------
// StepIndicator sub-component
// ---------------------------------------------------------------------------

interface StepIndicatorProps {
  status: AiProgressStepStatus;
}

const StepIndicator: React.FC<StepIndicatorProps> = ({ status }) => {
  const styles = useStyles();

  if (status === "completed") {
    return (
      <span className={styles.indicatorCompleted} aria-hidden="true">
        <CheckmarkCircle16Filled />
      </span>
    );
  }
  if (status === "error") {
    return (
      <span className={styles.indicatorError} aria-hidden="true">
        <ErrorCircle16Filled />
      </span>
    );
  }
  if (status === "active") {
    return (
      <span className={styles.indicatorActive} aria-hidden="true">
        <span className={styles.activeInnerDot} />
      </span>
    );
  }
  return <span className={styles.indicatorPending} aria-hidden="true" />;
};

// ---------------------------------------------------------------------------
// AiProgressStepper (exported)
// ---------------------------------------------------------------------------

export function AiProgressStepper({
  steps,
  activeStepId,
  completedStepIds,
  errorStepId,
  title,
  onCancel,
  variant = "card",
}: AiProgressStepperProps): JSX.Element {
  const styles = useStyles();

  const activeStep = steps.find((s) => s.id === activeStepId) ?? null;

  const header = (
    <div className={styles.header}>
      <Text size={400} className={styles.title}>
        {title ?? "Analyzing..."}
      </Text>
      {onCancel && variant === "card" && (
        <Button
          appearance="subtle"
          icon={<Dismiss20Regular />}
          size="small"
          onClick={onCancel}
          aria-label="Cancel analysis"
        />
      )}
    </div>
  );

  // Horizontal step track: chip → connector → chip → connector → ...
  const stepTrack = (
    <div
      className={styles.stepTrack}
      role="list"
      aria-label="Analysis progress steps"
    >
      {steps.map((step, index) => {
        const status = getStepStatus(
          step.id,
          activeStepId,
          completedStepIds,
          errorStepId,
        );
        const isLast = index === steps.length - 1;
        // Connector is "completed" color when the step after it is complete or active
        const nextStatus = !isLast
          ? getStepStatus(steps[index + 1].id, activeStepId, completedStepIds, errorStepId)
          : null;
        const connectorCompleted =
          nextStatus === "completed" || nextStatus === "active";

        const labelClass =
          status === "active"
            ? styles.labelActive
            : status === "completed"
              ? styles.labelCompleted
              : status === "error"
                ? styles.labelError
                : styles.labelPending;

        return (
          <React.Fragment key={step.id}>
            <div
              className={styles.stepChip}
              role="listitem"
              aria-current={status === "active" ? "step" : undefined}
              aria-label={`${step.label}, ${status}`}
            >
              <StepIndicator status={status} />
              <Text size={100} className={labelClass}>
                {step.label}
              </Text>
            </div>

            {!isLast && (
              <div
                className={
                  connectorCompleted
                    ? `${styles.connector} ${styles.connectorCompleted}`
                    : styles.connector
                }
                aria-hidden="true"
              />
            )}
          </React.Fragment>
        );
      })}
    </div>
  );

  // Description + short progress bar for the active step
  const activeDetail = activeStep ? (
    <div className={styles.activeDetail}>
      {activeStep.description && (
        <Text className={styles.description}>{activeStep.description}</Text>
      )}
      <div className={styles.progressBarWrap}>
        <ProgressBar
          thickness="medium"
          aria-label={`${activeStep.label} in progress`}
        />
      </div>
    </div>
  ) : null;

  const content = (
    <>
      {header}
      {stepTrack}
      {activeDetail}
    </>
  );

  if (variant === "card") {
    return (
      <div
        className={styles.backdrop}
        role="status"
        aria-live="polite"
        aria-label="Analysis in progress"
      >
        <div className={styles.card}>{content}</div>
      </div>
    );
  }

  return (
    <div
      className={styles.inline}
      role="status"
      aria-live="polite"
      aria-label="Analysis in progress"
    >
      {content}
    </div>
  );
}
