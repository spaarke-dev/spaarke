/**
 * BlockedSubmissionPanel — the findings summary and the Override / Return to firm decision of a held internal submission
 * (ebilling-r1 task 052; FR-19, D-24, D-73, D-76; server: task 105).
 *
 * Mounted in the report slot of the shared Processing / Result screen when the submission is Rejected. It reads
 * `GET /api/finance/submissions/{id}/blocked` and shows what the server says:
 *   - the findings (blocking ones first, with the rule, clause and why it holds the invoice);
 *   - "Override" ONLY when the server's own answer `canOverride` is true (the caller holds the eBilling Override privilege and
 *     the invoice is still held). The client never reads a role to decide this (D-76). A 403 on the call is still shown, not bypassed;
 *   - "Return to firm" when `canReturn` is true (no privilege, D-73).
 * Both decisions open the canonical `FormModal` (ADR-050) with a REQUIRED reason: nothing is sent while the reason is blank, and a
 * refusal (403 not permitted, 409 not held any more, 503) is shown in the modal while the screen behind it stays as it was.
 *
 * Wizards preview only (ADR-002): the decision is made by the server, which writes the Decision Record and moves the invoice.
 * ADR-021: Fluent v9 tokens only; dark mode follows the host's FluentProvider.
 */
import * as React from 'react';
import {
  Button,
  Field,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Text,
  Textarea,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { FormModal } from '../SprkModal/presets/FormModal';
import {
  MAX_DECISION_REASON_LENGTH,
  blockedDecisionErrorMessage,
  type BlockedDecisionVerb,
  type BlockedFinding,
  type BlockedSubmissionView,
  type InternalInvoiceSubmission,
} from './internalInvoiceSubmission';

/** Blocking findings listed before the rest are summarised as a count. */
const MAX_FINDINGS_SHOWN = 8;

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    width: '100%',
    minWidth: 0,
    textAlign: 'left',
  },
  findings: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    margin: 0,
    paddingLeft: tokens.spacingHorizontalL,
  },
  finding: {
    overflowWrap: 'anywhere',
  },
  meta: {
    color: tokens.colorNeutralForeground3,
  },
  actions: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
  },
  modalBody: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
});

export interface BlockedSubmissionPanelProps {
  /** The held submission. The panel reads it once per id (the host's polling re-renders the panel but does not reload it). */
  submissionId: string;
  /** The two server calls the panel makes. */
  client: Pick<InternalInvoiceSubmission, 'getBlocked' | 'decide'>;
  /** Called after the server accepted an Override or a Return. */
  onDecided?: (verb: BlockedDecisionVerb) => void;
}

const MODAL: Readonly<Record<BlockedDecisionVerb, { title: string; hint: string; submit: string; done: string }>> = {
  override: {
    title: 'Override the blocking failure',
    hint: 'The invoice is accepted despite its blocking failure and goes on to approval. Your reason is recorded in the Decision Record.',
    submit: 'Override and accept',
    done: 'Override recorded. The invoice is accepted.',
  },
  return: {
    title: 'Return the invoice to the firm',
    hint: 'The invoice is closed as Returned to Firm and the firm is told why. Your reason is recorded in the Decision Record.',
    submit: 'Return to firm',
    done: 'Returned to the firm.',
  },
};

function findingTitle(finding: BlockedFinding): string {
  if (finding.detailWithheld) return 'Duplicate of an invoice you cannot open';
  return finding.clause?.trim() || finding.reasonCode?.trim() || finding.ruleType?.trim() || 'Finding';
}

function findingMeta(finding: BlockedFinding): string {
  const parts = [
    finding.policyCode,
    finding.clauseLocation,
    finding.subject,
    finding.line != null ? `line ${finding.line}` : null,
    finding.heldBecause,
  ].filter((part): part is string => typeof part === 'string' && part.trim() !== '');
  return parts.join(' · ');
}

export const BlockedSubmissionPanel: React.FC<BlockedSubmissionPanelProps> = ({ submissionId, client, onDecided }) => {
  const styles = useStyles();
  const [view, setView] = React.useState<BlockedSubmissionView | null>(null);
  const [loadError, setLoadError] = React.useState<string | null>(null);
  const [reload, setReload] = React.useState(0);
  // The decision last asked for, kept after the form closes: the FormModal is closed with `open={false}`, never unmounted
  // while open, which would leave the page aria-hidden (issue 2658). `seq` keys each opening, so every opening starts fresh.
  const [decision, setDecision] = React.useState<{ verb: BlockedDecisionVerb; open: boolean; seq: number } | null>(
    null
  );
  const verb = decision?.open ? decision.verb : null;
  const openDecision = React.useCallback((next: BlockedDecisionVerb) => {
    setDecision(prev => (prev?.open ? { ...prev, verb: next } : { verb: next, open: true, seq: (prev?.seq ?? 0) + 1 }));
  }, []);
  const [reason, setReason] = React.useState('');
  const [busy, setBusy] = React.useState(false);
  const [failure, setFailure] = React.useState<string | null>(null);
  const [doneNote, setDoneNote] = React.useState<string | null>(null);

  const clientRef = React.useRef(client);
  clientRef.current = client;

  React.useEffect(() => {
    let cancelled = false;
    setLoadError(null);
    clientRef.current
      .getBlocked(submissionId)
      .then(next => {
        if (!cancelled) setView(next);
      })
      .catch(err => {
        if (!cancelled) setLoadError(blockedDecisionErrorMessage(err));
      });
    return () => {
      cancelled = true;
    };
  }, [submissionId, reload]);

  const closeModal = React.useCallback(() => {
    setDecision(prev => (prev ? { ...prev, open: false } : prev));
    setReason('');
    setFailure(null);
  }, []);

  const reasonTrimmed = reason.trim();

  const submit = React.useCallback(async () => {
    if (!verb || busy) return;
    // The reason is mandatory BEFORE the call (FR-19): nothing is sent for a blank one.
    if (reasonTrimmed === '') {
      setFailure('Enter a reason.');
      return;
    }
    setBusy(true);
    setFailure(null);
    try {
      await clientRef.current.decide(verb, submissionId, reasonTrimmed);
    } catch (err) {
      // A refusal (403 not permitted, 409, 503) is shown and nothing changes here: the server is the authority.
      setFailure(blockedDecisionErrorMessage(err));
      setBusy(false);
      return;
    }
    setBusy(false);
    setDoneNote(MODAL[verb].done);
    closeModal();
    setReload(n => n + 1);
    onDecided?.(verb);
  }, [verb, busy, reasonTrimmed, submissionId, closeModal, onDecided]);

  if (loadError && !view) {
    return (
      <div className={styles.root}>
        <MessageBar intent="error" role="alert" data-testid="blocked-load-error">
          <MessageBarBody>{loadError}</MessageBarBody>
        </MessageBar>
        <div className={styles.actions}>
          <Button appearance="secondary" onClick={() => setReload(n => n + 1)}>
            Try again
          </Button>
        </div>
      </div>
    );
  }

  if (!view) {
    return (
      <div className={styles.root}>
        <Spinner size="small" label="Loading the findings" />
      </div>
    );
  }

  const blocking = view.findings.filter(f => f.blocking);
  const others = view.findings.length - blocking.length;
  const shown = blocking.slice(0, MAX_FINDINGS_SHOWN);
  const held = view.state === 'Blocked';

  return (
    <div className={styles.root} data-testid="blocked-submission-panel">
      {view.findings.length > 0 && (
        <>
          <Text size={300} weight="semibold">
            {blocking.length > 0
              ? `Why this invoice is held (${blocking.length} blocking ${blocking.length === 1 ? 'finding' : 'findings'})`
              : 'Findings'}
          </Text>
          <ul className={styles.findings} data-testid="blocked-findings">
            {(shown.length > 0 ? shown : view.findings.slice(0, MAX_FINDINGS_SHOWN)).map(finding => (
              <li key={finding.index} className={styles.finding}>
                <Text size={300}>{findingTitle(finding)}</Text>
                {findingMeta(finding) && (
                  <div>
                    <Text size={200} className={styles.meta}>
                      {findingMeta(finding)}
                    </Text>
                  </div>
                )}
              </li>
            ))}
          </ul>
          {blocking.length > MAX_FINDINGS_SHOWN && (
            <Text size={200} className={styles.meta}>
              {`and ${blocking.length - MAX_FINDINGS_SHOWN} more blocking ${blocking.length - MAX_FINDINGS_SHOWN === 1 ? 'finding' : 'findings'}`}
            </Text>
          )}
          {others > 0 && blocking.length > 0 && (
            <Text size={200} className={styles.meta}>
              {`${others} other ${others === 1 ? 'finding does' : 'findings do'} not hold the invoice.`}
            </Text>
          )}
        </>
      )}

      {doneNote && held && (
        <MessageBar intent="success" data-testid="blocked-done">
          <MessageBarBody>{doneNote}</MessageBarBody>
        </MessageBar>
      )}

      {view.state === 'Overridden' && (
        <MessageBar intent="success" data-testid="blocked-state-overridden">
          <MessageBarBody>
            <MessageBarTitle>Accepted by override</MessageBarTitle>
            {view.override?.reason ? `Reason: ${view.override.reason}` : null}
          </MessageBarBody>
        </MessageBar>
      )}
      {view.state === 'Returned' && (
        <MessageBar intent="warning" data-testid="blocked-state-returned">
          <MessageBarBody>
            <MessageBarTitle>Returned to the firm</MessageBarTitle>
            {view.return?.reason ? `Reason: ${view.return.reason}` : null}
          </MessageBarBody>
        </MessageBar>
      )}

      {held && (view.canOverride || view.canReturn) && (
        <div className={styles.actions}>
          {view.canOverride && (
            <Button appearance="primary" onClick={() => openDecision('override')}>
              Override
            </Button>
          )}
          {view.canReturn && (
            <Button appearance="secondary" onClick={() => openDecision('return')}>
              Return to firm
            </Button>
          )}
        </div>
      )}
      {held && !view.canOverride && !view.canReturn && (
        <Text size={200} className={styles.meta}>
          You cannot decide this submission.
        </Text>
      )}

      {decision && (
        <FormModal
          key={decision.seq}
          open={decision.open}
          onClose={busy ? () => undefined : closeModal}
          onSubmit={() => void submit()}
          title={MODAL[decision.verb].title}
          submitLabel={MODAL[decision.verb].submit}
          submitDisabled={reasonTrimmed === ''}
          busy={busy}
        >
          <div className={styles.modalBody} data-testid="blocked-decision-form">
            <Text size={300}>{MODAL[decision.verb].hint}</Text>
            {failure && (
              <MessageBar intent="error" role="alert" data-testid="blocked-decision-error">
                <MessageBarBody>{failure}</MessageBarBody>
              </MessageBar>
            )}
            <Field label="Reason" required hint="Required. Say why this decision is right.">
              <Textarea
                value={reason}
                maxLength={MAX_DECISION_REASON_LENGTH}
                resize="vertical"
                onChange={(_e, data) => setReason(data.value)}
              />
            </Field>
          </div>
        </FormModal>
      )}
    </div>
  );
};

BlockedSubmissionPanel.displayName = 'BlockedSubmissionPanel';
