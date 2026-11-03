/**
 * The header "Awaiting" line (ebilling-r1 task 111, FR-35; task 103 GET approval): the people the invoice waits for, or how it closed. A
 * zero-approver invoice is flagged (FR-14: nobody may approve it). Fills 110's `headerExtras` slot.
 */

import * as React from 'react';
import { Caption1, makeStyles, tokens } from '@fluentui/react-components';
import { Warning16Filled } from '@fluentui/react-icons';
import type { InvoiceReview } from '../../services/invoiceReviewApi';
import { approvalSignature, awaitingLine } from '../../services/decisionModel';
import { useApproval } from './ApprovalProvider';

const useStyles = makeStyles({
  line: { display: 'inline-flex', alignItems: 'center', columnGap: tokens.spacingHorizontalXS },
  warn: { color: tokens.colorPaletteDarkOrangeForeground1 },
  muted: { color: tokens.colorNeutralForeground3 },
});

/** Re-reads the approval state when the review's approval-relevant parts change. */
export function useApprovalSync(review: InvoiceReview): void {
  const { sync } = useApproval();
  React.useEffect(() => {
    sync(approvalSignature(review));
  }, [sync, review]);
}

export function AwaitingApprovers({ review }: { review: InvoiceReview }) {
  const s = useStyles();
  const { state, failure, loading } = useApproval();
  useApprovalSync(review);

  if (loading) return null;
  if (!state) {
    return failure ? (
      <Caption1 className={s.muted} data-testid="awaiting">
        Approvers are not available right now.
      </Caption1>
    ) : null;
  }
  const line = awaitingLine(state);
  if (!line) return null;
  return (
    <span className={s.line} data-testid="awaiting" data-tone={line.tone}>
      {line.tone === 'warning' && <Warning16Filled className={s.warn} aria-hidden />}
      <Caption1 className={line.tone === 'warning' ? s.warn : undefined} title={state.eligibleApprovers.join(', ')}>
        {line.text}
      </Caption1>
    </span>
  );
}
