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
  reload: () => Promise<InvoiceReview | null>;
  notifyStep?: NotifyStep;
}

export function DecideInvoiceAction({ review, canWrite, reload, notifyStep }: DecideInvoiceActionProps) {
  const { state, api, invoiceId, refresh } = useApproval();
  useApprovalSync(review);
  // The wizard is closed with `open={false}`, never unmounted while open, which would leave the page aria-hidden (#2658).
  // `seq` keys each opening and changes only while closed, so the form, the steps and the read-only choice start fresh.
  const [wizard, setWizard] = React.useState<{ open: boolean; seq: number } | null>(null);
  const access = decisionAccess(review, state, canWrite);

  const refreshApproval = React.useCallback(() => refresh(), [refresh]);
  const openWizard = React.useCallback(() => {
    setWizard(prev => (prev?.open ? prev : { open: true, seq: (prev?.seq ?? 0) + 1 }));
  }, []);
  const closeWizard = React.useCallback(() => {
    setWizard(prev => (prev ? { ...prev, open: false } : prev));
  }, []);

  return (
    <>
      {access === 'decide' && (
        <Button appearance="primary" onClick={openWizard} data-testid="decide-invoice">
          Decide invoice…
        </Button>
      )}
      {access === 'view' && (
        <Button onClick={openWizard} data-testid="view-decision">
          View decision
        </Button>
      )}
      {/* Keyed per opening: the form, the steps and the read-only choice start fresh each time. */}
      {wizard && state && (
        <InvoiceDecisionWizard
          key={wizard.seq}
          open={wizard.open}
          onClose={closeWizard}
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
