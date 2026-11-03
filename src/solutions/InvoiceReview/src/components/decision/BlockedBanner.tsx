/**
 * The blocked-submission view (ebilling-r1 task 111; D-101, D-73, D-76, FR-35): fills 110's `banners` slot while the invoice is held In Review
 * because its internal submission was blocked. It shows why (the blocking findings the server returns, with the clause) and offers:
 *
 *   Override…        only when the server says `canOverride` (the caller holds the eBilling Override privilege, D-76). A reason is required.
 *   Return to firm…  only when the server says `canReturn` (no privilege needed, D-73). A reason is required.
 *
 * Each is one reason on `FormModal` (a single-field form, ADR-050). A refusal (403 without the privilege, 409 already decided, 503 partly
 * saved) is shown in the form with its code and nothing changes on the page; a 503 is finished by submitting the same request again. The
 * invoice's own state is the server's: after success the page re-reads and the banner goes away with the hold.
 *
 * No navigation entry or form command is needed for this: the page opens on the invoice, the banner reads the submission the review names.
 */

import * as React from 'react';
import {
  Button,
  Caption1,
  Field,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Text,
  Textarea,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { FormModal } from '@spaarke/ui-components';
import { decisionFailureMessage, type BlockedFinding, type BlockedSubmission } from '../../services/decisionApi';
import type { ApiFailure, InvoiceReview } from '../../services/invoiceReviewApi';
import { useApproval } from './ApprovalProvider';

const REASON_MAX = 2000;

const useStyles = makeStyles({
  findings: { margin: 0, paddingLeft: tokens.spacingHorizontalL, display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalXXS },
  muted: { color: tokens.colorNeutralForeground3 },
  form: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalM },
});

/** The submission the review names (its type is `unknown` on 110's contract: the id is all this reads). */
export function submissionIdOf(review: Pick<InvoiceReview, 'submission'>): string | null {
  const sub = review.submission as { submissionId?: unknown } | null | undefined;
  return sub && typeof sub.submissionId === 'string' ? sub.submissionId : null;
}

/** One finding as one line: the policy and clause, why it holds the invoice, and what to do about its detail when withheld. */
export function findingLine(f: BlockedFinding): string {
  const head = [f.policyCode, f.clause].filter(Boolean).join(' · ');
  const where = f.line !== null ? `line ${f.line}` : f.subject ?? '';
  const why = f.heldBecause ?? f.reasonCode ?? f.consequence ?? '';
  const parts = [head, where, why].filter(p => p && p.length > 0);
  const base = parts.length > 0 ? parts.join(' — ') : 'A blocking finding';
  return f.detailWithheld ? `${base} (detail withheld for your role)` : base;
}

type Action = 'override' | 'return';

export interface BlockedBannerProps {
  review: InvoiceReview;
  reload: () => Promise<unknown>;
}

export function BlockedBanner({ review, reload }: BlockedBannerProps) {
  const s = useStyles();
  const { api, refresh } = useApproval();
  const held = review.header.held;
  const submissionId = submissionIdOf(review);

  const [blocked, setBlocked] = React.useState<BlockedSubmission | null>(null);
  const [failure, setFailure] = React.useState<ApiFailure | null>(null);
  const [action, setAction] = React.useState<Action | null>(null);
  const [reason, setReason] = React.useState('');
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const alive = React.useRef(true);
  React.useEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
    };
  }, []);

  const load = React.useCallback(async () => {
    if (!submissionId) return;
    const r = await api.getBlocked(submissionId);
    if (!alive.current) return;
    if (r.ok) {
      setBlocked(r.value);
      setFailure(null);
    } else {
      setFailure(r.failure);
    }
  }, [api, submissionId]);

  React.useEffect(() => {
    if (held) void load();
  }, [held, load]);

  if (!held || !submissionId) return null;

  const open = (a: Action) => {
    setReason('');
    setError(null);
    setAction(a);
  };

  const submit = async () => {
    if (!action || reason.trim() === '') return;
    setBusy(true);
    setError(null);
    const r = action === 'override' ? await api.overrideBlocked(submissionId, reason.trim()) : await api.returnBlocked(submissionId, reason.trim());
    if (!alive.current) return;
    if (r.ok) {
      setBusy(false);
      setAction(null);
      await Promise.all([reload(), refresh(), load()]);
      return;
    }
    // Shown in the form with the server's detail and code; nothing on the page changes. A 503 is repeated by submitting again.
    setBusy(false);
    setError(decisionFailureMessage(r.failure));
  };

  const blocking = (blocked?.findings ?? []).filter(f => f.blocking && !f.overridden);
  const shown = blocking.slice(0, 5);
  const title = action === 'override' ? 'Override the block' : 'Return to firm';

  return (
    <>
      <MessageBar intent="warning" layout="multiline" data-testid="blocked-banner">
        <MessageBarBody>
          <MessageBarTitle>This invoice is on hold</MessageBarTitle>
          <Text as="p">Its submission was blocked by a rule. It stays In Review and cannot be approved until the block is overridden or the invoice is returned to the firm.</Text>
          {failure && (
            <Caption1 className={s.muted} data-testid="blocked-unavailable">
              The reasons could not be loaded: {decisionFailureMessage(failure)}
            </Caption1>
          )}
          {shown.length > 0 && (
            <ul className={s.findings} data-testid="blocked-findings" aria-label="Why this invoice is held">
              {shown.map(f => (
                <li key={f.index}>
                  <Caption1>{findingLine(f)}</Caption1>
                </li>
              ))}
              {blocking.length > shown.length && <li><Caption1>and {blocking.length - shown.length} more</Caption1></li>}
            </ul>
          )}
          {blocked && !blocked.canOverride && (
            <Caption1 className={s.muted} data-testid="no-override">
              Overriding a block needs the eBilling Override privilege, which you do not hold.
            </Caption1>
          )}
        </MessageBarBody>
        {blocked && (blocked.canOverride || blocked.canReturn) && (
          <MessageBarActions>
            {blocked.canOverride && (
              <Button size="small" onClick={() => open('override')} data-testid="override-block">
                Override…
              </Button>
            )}
            {blocked.canReturn && (
              <Button size="small" onClick={() => open('return')} data-testid="return-blocked">
                Return to firm…
              </Button>
            )}
          </MessageBarActions>
        )}
      </MessageBar>

      {action && (
        <FormModal
          open
          size="sm"
          title={title}
          submitLabel={action === 'override' ? 'Override' : 'Return'}
          submitDisabled={reason.trim() === ''}
          busy={busy}
          onClose={() => {
            if (!busy) setAction(null);
          }}
          onSubmit={() => void submit()}
        >
          <div className={s.form}>
            <Text>
              {action === 'override'
                ? 'The invoice is released into the normal review. A reason is required and is recorded with your name.'
                : 'The invoice goes back to the firm, which can correct and resubmit it. A reason is required and the firm sees it.'}
            </Text>
            <Field label="Reason" required>
              <Textarea
                value={reason}
                onChange={(_, d) => setReason(d.value)}
                resize="vertical"
                rows={3}
                maxLength={REASON_MAX}
                aria-label="Reason"
                data-testid="blocked-reason"
              />
            </Field>
            {error && (
              <MessageBar intent="error" layout="multiline" data-testid="blocked-error">
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            )}
          </div>
        </FormModal>
      )}
    </>
  );
}
