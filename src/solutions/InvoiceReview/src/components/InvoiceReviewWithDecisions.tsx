/**
 * Invoice Review with its decisions (ebilling-r1 task 111): task 110's page with its four slots filled and nothing restructured.
 *
 *   paneTabs       Context, Related work, History
 *   headerExtras   Awaiting approvers
 *   banners        the blocked-submission view (Override, Return); a decision stranded before its record (Finish, ISS-032)
 *   invoiceActions Decide invoice… / View decision
 *
 * One `ApprovalProvider` holds the approval state (task 103) that the header line, the action and the wizard share; the slots are called
 * inline by the page, so each returns an element and keeps its hooks in its own component.
 */

import * as React from 'react';
import { Button, MessageBar, MessageBarActions, MessageBarBody } from '@fluentui/react-components';
import { InvoiceReviewPage, type InvoiceReviewPageProps, type PaneTabSlot } from './InvoiceReviewPage';
import { ApprovalProvider, useApproval } from './decision/ApprovalProvider';
import { AwaitingApprovers } from './decision/AwaitingApprovers';
import { BlockedBanner } from './decision/BlockedBanner';
import { DecideInvoiceAction, IncompleteDecisionBanner } from './decision/DecideInvoiceAction';
import { SubmitCorrectedInvoiceAction, type OpenCorrectionWizard } from './decision/SubmitCorrectedInvoiceAction';
import type { NotifyStep } from './decision/InvoiceDecisionWizard';
import { ContextTab } from './tabs/ContextTab';
import { HistoryTab } from './tabs/HistoryTab';
import { RelatedWorkTab } from './tabs/RelatedWorkTab';
import type { InvoiceDecisionApi } from '../services/decisionApi';

export interface InvoiceReviewWithDecisionsProps extends Pick<InvoiceReviewPageProps, 'invoiceId' | 'initialLineId' | 'api' | 'openRecord' | 'pollIntervalMs'> {
  decisionApi: InvoiceDecisionApi;
  /** The optional firm-notice step of a reject or return (a slot: nothing is built in it). Absent: the wizard has no notify step. */
  notifyStep?: NotifyStep;
  /**
   * Task 095f-2e2b: opens the Create Invoice wizard in Correction mode for an invoice and resolves when it closes. Absent: the page has no
   * "Submit corrected invoice" action (as before).
   */
  openCorrectionWizard?: OpenCorrectionWizard;
}

/**
 * The page with the approval state's `firmNeeded` (EB-D-185 / EB-D-197) fed in: the line decisions, adjustments and corrections are disabled
 * while the invoice needs its firm. The state is read inside the provider, so this sits between the provider and the page.
 */
function ReviewPageWithApproval(props: InvoiceReviewPageProps) {
  const { state } = useApproval();
  return <InvoiceReviewPage {...props} firmNeeded={state?.firmNeeded === true} />;
}

export function InvoiceReviewWithDecisions({ invoiceId, initialLineId, api, decisionApi, openRecord, pollIntervalMs, notifyStep, openCorrectionWizard }: InvoiceReviewWithDecisionsProps) {
  // The plain sentence of a refused "Submit corrected invoice" (shown with the page's banners, not in the footer's one row).
  const [correctionNotice, setCorrectionNotice] = React.useState<string | null>(null);

  const paneTabs = React.useMemo<PaneTabSlot[]>(
    () => [
      { value: 'context', label: 'Context', render: () => <ContextTab api={decisionApi} invoiceId={invoiceId} /> },
      { value: 'related', label: 'Related work', render: ({ line }) => <RelatedWorkTab api={decisionApi} invoiceId={invoiceId} line={line} /> },
      { value: 'history', label: 'History', render: ({ review }) => <HistoryTab review={review} api={api} invoiceId={invoiceId} /> },
    ],
    [api, decisionApi, invoiceId]
  );

  return (
    <ApprovalProvider api={decisionApi} invoiceId={invoiceId}>
      <ReviewPageWithApproval
        invoiceId={invoiceId}
        initialLineId={initialLineId}
        api={api}
        openRecord={openRecord}
        pollIntervalMs={pollIntervalMs}
        paneTabs={paneTabs}
        headerExtras={ctx => <AwaitingApprovers review={ctx.review} />}
        banners={ctx => (
          <>
            <BlockedBanner review={ctx.review} reload={ctx.reload} />
            {/* ISS-032: a decision stranded before its Decision Record, and its Finish. */}
            <IncompleteDecisionBanner review={ctx.review} reload={ctx.reload} />
            {correctionNotice && (
              <MessageBar intent="warning" role="alert" data-testid="correction-notice">
                <MessageBarBody>{correctionNotice}</MessageBarBody>
                <MessageBarActions>
                  <Button size="small" onClick={() => setCorrectionNotice(null)}>
                    Dismiss
                  </Button>
                </MessageBarActions>
              </MessageBar>
            )}
          </>
        )}
        invoiceActions={ctx => (
          <>
            <DecideInvoiceAction review={ctx.review} canWrite={ctx.canWrite} reload={ctx.reload} notifyStep={notifyStep} />
            {openCorrectionWizard && (
              <SubmitCorrectedInvoiceAction
                review={ctx.review}
                canWrite={ctx.canWrite}
                invoiceId={invoiceId}
                api={decisionApi}
                openCorrectionWizard={openCorrectionWizard}
                reload={ctx.reload}
                onNotice={setCorrectionNotice}
              />
            )}
          </>
        )}
      />
    </ApprovalProvider>
  );
}
