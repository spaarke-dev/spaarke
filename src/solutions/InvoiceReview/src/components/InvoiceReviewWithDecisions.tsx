/**
 * Invoice Review with its decisions (ebilling-r1 task 111): task 110's page with its four slots filled and nothing restructured.
 *
 *   paneTabs       Context, Related work, History
 *   headerExtras   Awaiting approvers
 *   banners        the blocked-submission view (Override, Return)
 *   invoiceActions Decide invoice… / View decision
 *
 * One `ApprovalProvider` holds the approval state (task 103) that the header line, the action and the wizard share; the slots are called
 * inline by the page, so each returns an element and keeps its hooks in its own component.
 */

import * as React from 'react';
import { InvoiceReviewPage, type InvoiceReviewPageProps, type PaneTabSlot } from './InvoiceReviewPage';
import { ApprovalProvider } from './decision/ApprovalProvider';
import { AwaitingApprovers } from './decision/AwaitingApprovers';
import { BlockedBanner } from './decision/BlockedBanner';
import { DecideInvoiceAction } from './decision/DecideInvoiceAction';
import type { NotifyStep } from './decision/InvoiceDecisionWizard';
import { ContextTab } from './tabs/ContextTab';
import { HistoryTab } from './tabs/HistoryTab';
import { RelatedWorkTab } from './tabs/RelatedWorkTab';
import type { InvoiceDecisionApi } from '../services/decisionApi';

export interface InvoiceReviewWithDecisionsProps extends Pick<InvoiceReviewPageProps, 'invoiceId' | 'api' | 'openRecord' | 'pollIntervalMs'> {
  decisionApi: InvoiceDecisionApi;
  /** The optional firm-notice step of a reject or return (a slot: nothing is built in it). Absent: the wizard has no notify step. */
  notifyStep?: NotifyStep;
}

export function InvoiceReviewWithDecisions({ invoiceId, api, decisionApi, openRecord, pollIntervalMs, notifyStep }: InvoiceReviewWithDecisionsProps) {
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
      <InvoiceReviewPage
        invoiceId={invoiceId}
        api={api}
        openRecord={openRecord}
        pollIntervalMs={pollIntervalMs}
        paneTabs={paneTabs}
        headerExtras={ctx => <AwaitingApprovers review={ctx.review} />}
        banners={ctx => <BlockedBanner review={ctx.review} reload={ctx.reload} />}
        invoiceActions={ctx => <DecideInvoiceAction review={ctx.review} canWrite={ctx.canWrite} reload={ctx.reload} notifyStep={notifyStep} />}
      />
    </ApprovalProvider>
  );
}
