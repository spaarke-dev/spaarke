import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
  ProgressBar,
  MessageBar,
  MessageBarBody,
  MessageBarActions,
  Badge,
  Spinner,
  Accordion,
  AccordionItem,
  AccordionHeader,
  AccordionPanel,
} from "@fluentui/react-components";
import {
  CheckmarkCircle20Regular,
  ErrorCircle20Regular,
  Dismiss20Regular,
} from "@fluentui/react-icons";
import type { BulkOperationStatus, BulkOperationType } from "../../types/spe";
import { speApiClient } from "../../services/speApiClient";

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Polling interval in milliseconds while the operation is running */
const POLL_INTERVAL_MS = 2000;

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021: Fluent v9 makeStyles + design tokens — no hard-coded colors)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalM,
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
  },

  header: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },

  title: {
    flex: "1 1 auto",
  },

  progressRow: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
  },

  progressBar: {
    width: "100%",
  },

  counters: {
    display: "flex",
    flexDirection: "row",
    gap: tokens.spacingHorizontalM,
    alignItems: "center",
  },

  counterItem: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },

  errorList: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
  },

  errorItem: {
    display: "flex",
    flexDirection: "column",
    gap: "2px",
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

function operationTypeLabel(type: BulkOperationType): string {
  switch (type) {
    case "Delete":
      return "Bulk Delete";
    case "AssignPermissions":
      return "Bulk Permission Assignment";
    default:
      return "Bulk Operation";
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface BulkOperationProgressProps {
  /** The operation ID returned by the enqueue endpoint to track. */
  operationId: string;

  /**
   * Called when the operation completes (isFinished === true) with the final status.
   * Parent can use this to refresh a grid or display a summary notification.
   */
  onComplete?: (status: BulkOperationStatus) => void;

  /**
   * Called when the user explicitly dismisses the progress panel.
   * Parent should remove this component from the DOM on dismiss.
   */
  onDismiss?: () => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// BulkOperationProgress Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * BulkOperationProgress — polls a bulk operation and displays a progress bar
 * with current/total counts, success/failure badges, and expandable error details.
 *
 * Polling:
 *   - Starts immediately when mounted.
 *   - Polls every 2 seconds while isFinished === false.
 *   - Stops automatically when isFinished === true.
 *   - Stops on component unmount (cleanup via useEffect return).
 *
 * ADR compliance:
 *   - ADR-021: Fluent v9 makeStyles + design tokens — no hard-coded colors, dark mode
 *   - ADR-012: Uses speApiClient for all API calls
 */
export const BulkOperationProgress: React.FC<BulkOperationProgressProps> = ({
  operationId,
  onComplete,
  onDismiss,
}) => {
  const styles = useStyles();

  // ── State ─────────────────────────────────────────────────────────────────

  const [status, setStatus] = React.useState<BulkOperationStatus | null>(null);
  const [pollError, setPollError] = React.useState<string | null>(null);

  // ── Polling ───────────────────────────────────────────────────────────────

  React.useEffect(() => {
    let cancelled = false;

    const pollOnce = async (): Promise<void> => {
      try {
        const result = await speApiClient.bulk.getStatus(operationId);
        if (cancelled) return;

        setStatus(result);
        setPollError(null);

        if (result.isFinished) {
          onComplete?.(result);
        }
      } catch (err) {
        if (cancelled) return;
        const msg = err instanceof Error ? err.message : "Failed to fetch operation status";
        setPollError(msg);
      }
    };

    // Poll immediately, then on interval
    void pollOnce();

    const interval = setInterval(() => {
      if (status?.isFinished) return;
      void pollOnce();
    }, POLL_INTERVAL_MS);

    return () => {
      cancelled = true;
      clearInterval(interval);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [operationId]);

  // ── Derived state ─────────────────────────────────────────────────────────

  const total = status?.total ?? 0;
  const completed = status?.completed ?? 0;
  const failed = status?.failed ?? 0;
  const isFinished = status?.isFinished ?? false;
  const errors = status?.errors ?? [];

  /** Progress value 0–1 for ProgressBar */
  const progressValue = total > 0 ? (completed + failed) / total : 0;

  const hasErrors = errors.length > 0;
  const allFailed = isFinished && failed === total && total > 0;
  const partialFailure = isFinished && failed > 0 && failed < total;
  const allSucceeded = isFinished && failed === 0;

  // ── Render ────────────────────────────────────────────────────────────────

  if (pollError && !status) {
    // Initial load failed — show error bar
    return (
      <div className={styles.root}>
        <MessageBar intent="error">
          <MessageBarBody>Failed to load operation status: {pollError}</MessageBarBody>
          {onDismiss && (
            <MessageBarActions>
              <Button
                appearance="transparent"
                size="small"
                icon={<Dismiss20Regular />}
                onClick={onDismiss}
                aria-label="Dismiss"
              />
            </MessageBarActions>
          )}
        </MessageBar>
      </div>
    );
  }

  return (
    <div className={styles.root} role="status" aria-live="polite" aria-label="Bulk operation progress">
      {/* ── Header ── */}
      <div className={styles.header}>
        {!isFinished && <Spinner size="tiny" aria-hidden="true" />}
        {allSucceeded && <CheckmarkCircle20Regular color={tokens.colorPaletteGreenForeground1} aria-hidden="true" />}
        {(allFailed || partialFailure) && <ErrorCircle20Regular color={tokens.colorPaletteRedForeground1} aria-hidden="true" />}

        <Text weight="semibold" className={styles.title}>
          {status ? operationTypeLabel(status.operationType) : "Loading…"}
        </Text>

        {onDismiss && isFinished && (
          <Button
            appearance="transparent"
            size="small"
            icon={<Dismiss20Regular />}
            onClick={onDismiss}
            aria-label="Dismiss progress panel"
          />
        )}
      </div>

      {/* ── Progress bar ── */}
      {status && (
        <div className={styles.progressRow}>
          <ProgressBar
            className={styles.progressBar}
            value={progressValue}
            color={allFailed ? "error" : partialFailure ? "warning" : "brand"}
            aria-label={`${Math.round(progressValue * 100)}% complete`}
          />

          {/* ── Counters ── */}
          <div className={styles.counters}>
            <div className={styles.counterItem}>
              <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                Progress:
              </Text>
              <Text size={200} weight="semibold">
                {completed + failed} / {total}
              </Text>
            </div>

            {completed > 0 && (
              <div className={styles.counterItem}>
                <Badge
                  appearance="tint"
                  color="success"
                  size="small"
                  aria-label={`${completed} succeeded`}
                >
                  {completed} succeeded
                </Badge>
              </div>
            )}

            {failed > 0 && (
              <div className={styles.counterItem}>
                <Badge
                  appearance="tint"
                  color="danger"
                  size="small"
                  aria-label={`${failed} failed`}
                >
                  {failed} failed
                </Badge>
              </div>
            )}

            {!isFinished && (
              <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                Processing…
              </Text>
            )}
          </div>
        </div>
      )}

      {/* ── Completion summary ── */}
      {isFinished && (
        <MessageBar intent={allSucceeded ? "success" : partialFailure ? "warning" : "error"}>
          <MessageBarBody>
            {allSucceeded && `All ${total} container${total !== 1 ? "s" : ""} processed successfully.`}
            {partialFailure && `${completed} of ${total} containers succeeded. ${failed} failed — see details below.`}
            {allFailed && `All ${total} container${total !== 1 ? "s" : ""} failed. See details below.`}
          </MessageBarBody>
        </MessageBar>
      )}

      {/* ── Error details (expandable) ── */}
      {hasErrors && (
        <Accordion collapsible>
          <AccordionItem value="errors">
            <AccordionHeader>
              <Text size={200}>
                Error details ({errors.length} item{errors.length !== 1 ? "s" : ""})
              </Text>
            </AccordionHeader>
            <AccordionPanel>
              <div className={styles.errorList}>
                {errors.map((err, idx) => (
                  <div key={idx} className={styles.errorItem}>
                    <Text size={100} weight="semibold" style={{ color: tokens.colorPaletteRedForeground1 }}>
                      {err.containerId}
                    </Text>
                    <Text size={100} style={{ color: tokens.colorNeutralForeground2 }}>
                      {err.errorMessage}
                    </Text>
                  </div>
                ))}
              </div>
            </AccordionPanel>
          </AccordionItem>
        </Accordion>
      )}

      {/* ── Poll error overlay (non-fatal, keeps showing last known state) ── */}
      {pollError && status && (
        <Text size={100} style={{ color: tokens.colorPaletteRedForeground2 }}>
          Status refresh failed: {pollError}
        </Text>
      )}
    </div>
  );
};
