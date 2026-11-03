/**
 * "Decide invoice…" / "View decision" (ebilling-r1 task 111; FR-35): fills 110's `invoiceActions` slot. The button shows only when the
 * server's own signals say it can do something (`decisionAccess`): an eligible approver on a pending, not-held invoice gets "Decide invoice…";
 * a decided invoice gets "View decision" (the wizard reopens read-only); everyone else sees nothing. The server stays authoritative: a
 * refused decision is shown in the wizard with its code and nothing on the page changes.
 */

import * as React from 'react';
import { Button } from '@fluentui/react-components';
import type { InvoiceReview } from '../../services/invoiceReviewApi';
import { decisionAccess } from '../../services/decisionModel';
import { InvoiceDecisionWizard, type NotifyStep } from './InvoiceDecisionWizard';
import { useApprovalSync } from './AwaitingApprovers';
import { useApproval } from './ApprovalProvider';

export interface DecideInvoiceActionProps {
  review: InvoiceReview;
  canWrite: boolean;
  reload: () => Promise<unknown>;
  notifyStep?: NotifyStep;
}

export function DecideInvoiceAction({ review, canWrite, reload, notifyStep }: DecideInvoiceActionProps) {
  const { state, api, invoiceId, refresh } = useApproval();
  useApprovalSync(review);
  const [open, setOpen] = React.useState(false);
  const access = decisionAccess(review, state, canWrite);

  const refreshApproval = React.useCallback(() => refresh(), [refresh]);

  return (
    <>
      {access === 'decide' && (
        <Button appearance="primary" onClick={() => setOpen(true)} data-testid="decide-invoice">
          Decide invoice…
        </Button>
      )}
      {access === 'view' && (
        <Button onClick={() => setOpen(true)} data-testid="view-decision">
          View decision
        </Button>
      )}
      {/* Mounted per opening: the form, the steps and the read-only choice start fresh each time. */}
      {open && state && (
        <InvoiceDecisionWizard
          open
          onClose={() => setOpen(false)}
          review={review}
          approval={state}
          api={api}
          invoiceId={invoiceId}
          reload={reload}
          refreshApproval={refreshApproval}
          notifyStep={notifyStep}
        />
      )}
    </>
  );
}
