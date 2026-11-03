/**
 * InvoiceSubmissionOutcome — the Processing and Result screens of an invoice submission (ebilling-r1 task 051,
 * FR-18).
 *
 * Reads one submission's status through the host's `getStatus` callback, polls while the server says Received or
 * Processing, and renders the terminal answer:
 *   - Accepted: the tracking number.
 *   - Rejected: the reason code and a report slot the host renders (`renderReport`); a placeholder until task 134.
 *   - Failed: retry guidance.
 *
 * Everything shown comes from the server, so leaving the page and returning (the host mounts this with the same
 * submission id) shows the same answer. No SPA auth, Xrm or BFF URL import (ADR-012); Fluent v9 tokens only (ADR-021).
 */
import * as React from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
} from '@fluentui/react-components';
import { StatusBadge } from '../StatusBadge';
import type { StatusBadgeTone } from '../StatusBadge';
import { invoiceSubmissionErrorMessage } from './messages';
import { InvoiceSubmissionError, isTerminalStatus } from './types';
import type { InvoiceSubmissionStatusView } from './types';

const DEFAULT_POLL_INTERVAL_MS = 2000;

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    width: '100%',
    minWidth: 0,
    textAlign: 'left',
  },
  header: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  tracking: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    overflowWrap: 'anywhere',
  },
  caption: {
    color: tokens.colorNeutralForeground3,
  },
  processing: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  reportSlot: {
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    borderTopWidth: tokens.strokeWidthThin,
    borderRightWidth: tokens.strokeWidthThin,
    borderBottomWidth: tokens.strokeWidthThin,
    borderLeftWidth: tokens.strokeWidthThin,
    borderTopStyle: 'dashed',
    borderRightStyle: 'dashed',
    borderBottomStyle: 'dashed',
    borderLeftStyle: 'dashed',
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftColor: tokens.colorNeutralStroke2,
    color: tokens.colorNeutralForeground3,
  },
});

export interface InvoiceSubmissionOutcomeProps {
  /** The submission to show. */
  submissionId: string;
  /** The tracking number already known (the 202 body), shown until the first status read returns. */
  trackingNumber?: string | null;
  /** The host's status callback. */
  getStatus: (submissionId: string) => Promise<InvoiceSubmissionStatusView>;
  /** Milliseconds between status reads while the submission is still being processed. Default 2000. */
  pollIntervalMs?: number;
  /**
   * The host's slot for a Rejected submission's on-screen report. Absent: a placeholder is shown (the real report
   * and PDF download arrive with task 134).
   */
  renderReport?: (status: InvoiceSubmissionStatusView) => React.ReactNode;
}

function toneFor(status: string | null | undefined): StatusBadgeTone {
  if (status === 'Accepted') return 'success';
  if (status === 'Rejected') return 'critical';
  if (status === 'Failed') return 'warning';
  return 'info';
}

/** Received and Processing (and a status the BFF could not name) all read as Processing. */
function labelFor(status: string | null | undefined): string {
  return isTerminalStatus(status) ? (status as string) : 'Processing';
}

export const InvoiceSubmissionOutcome: React.FC<InvoiceSubmissionOutcomeProps> = ({
  submissionId,
  trackingNumber,
  getStatus,
  pollIntervalMs = DEFAULT_POLL_INTERVAL_MS,
  renderReport,
}) => {
  const styles = useStyles();
  const [view, setView] = React.useState<InvoiceSubmissionStatusView | null>(null);
  // A read the server refused for good (not found / no access): polling stops and the message is shown.
  const [loadError, setLoadError] = React.useState<string | null>(null);
  // A read that failed in transit: the last known state stays on screen and polling continues.
  const [stale, setStale] = React.useState(false);

  // The latest callback, without restarting the poll when the host re-renders with a new function identity.
  const getStatusRef = React.useRef(getStatus);
  getStatusRef.current = getStatus;

  React.useEffect(() => {
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;

    const tick = async (): Promise<void> => {
      try {
        const next = await getStatusRef.current(submissionId);
        if (cancelled) return;
        setView(next);
        setLoadError(null);
        setStale(false);
        if (isTerminalStatus(next.status)) return;
      } catch (err) {
        if (cancelled) return;
        if (err instanceof InvoiceSubmissionError && (err.statusCode === 403 || err.statusCode === 404)) {
          setLoadError(invoiceSubmissionErrorMessage(err));
          return;
        }
        setStale(true);
      }
      timer = setTimeout(() => void tick(), pollIntervalMs);
    };

    void tick();
    return () => {
      cancelled = true;
      if (timer !== undefined) clearTimeout(timer);
    };
  }, [submissionId, pollIntervalMs]);

  if (loadError) {
    return (
      <div className={styles.root}>
        <MessageBar intent="error" role="alert">
          <MessageBarBody>{loadError}</MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  const status = view?.status ?? null;
  const tracking = view?.trackingNumber ?? trackingNumber ?? null;
  const terminal = isTerminalStatus(status);

  return (
    <div className={styles.root} data-testid="invoice-submission-outcome">
      <div className={styles.header}>
        <StatusBadge label={labelFor(status)} tone={toneFor(status)} />
        {view?.firmInvoiceNumber && (
          <Text size={200} className={styles.caption}>
            Invoice {view.firmInvoiceNumber}
          </Text>
        )}
      </div>

      {tracking && (
        <div className={styles.tracking}>
          <Text size={200} className={styles.caption}>
            Tracking number
          </Text>
          <Text size={500} weight="semibold" data-testid="invoice-tracking-number">
            {tracking}
          </Text>
        </div>
      )}

      {!terminal && (
        <div className={styles.processing} role="status">
          <Spinner size="tiny" />
          <Text size={300}>Processing</Text>
        </div>
      )}
      {!terminal && (
        <Text size={200} className={styles.caption}>
          You can leave this page. The result will be here when you come back.
        </Text>
      )}

      {stale && (
        <MessageBar intent="warning">
          <MessageBarBody>The latest status could not be loaded. Trying again.</MessageBarBody>
        </MessageBar>
      )}

      {status === 'Accepted' && (
        <MessageBar intent="success">
          <MessageBarBody>
            <MessageBarTitle>Accepted</MessageBarTitle>
            The invoice was accepted{tracking ? `. Quote tracking number ${tracking} if you contact us about it.` : '.'}
          </MessageBarBody>
        </MessageBar>
      )}

      {status === 'Rejected' && view && (
        <>
          <MessageBar intent="error" role="alert">
            <MessageBarBody>
              <MessageBarTitle>Rejected</MessageBarTitle>
              {view.outcomeReasonCode ? `Reason: ${view.outcomeReasonCode}` : 'The invoice was rejected.'}
            </MessageBarBody>
          </MessageBar>
          {renderReport ? (
            renderReport(view)
          ) : (
            <div className={styles.reportSlot} data-testid="invoice-report-placeholder">
              <Text size={200}>The detailed report for this invoice will be shown here.</Text>
            </div>
          )}
        </>
      )}

      {status === 'Failed' && (
        <MessageBar intent="warning" role="alert">
          <MessageBarBody>
            <MessageBarTitle>We could not process this invoice</MessageBarTitle>
            Submit it again. If it keeps failing, contact the team
            {tracking ? ` and quote tracking number ${tracking}` : ''}.
          </MessageBarBody>
        </MessageBar>
      )}
    </div>
  );
};

InvoiceSubmissionOutcome.displayName = 'InvoiceSubmissionOutcome';
