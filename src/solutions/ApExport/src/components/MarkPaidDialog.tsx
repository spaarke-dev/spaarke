/**
 * Mark Paid (ebilling-r1 task 151b, EB-D-181; FR-42). A confirm step (the selection, its count and total), then one
 * `POST /api/finance/invoices/{id}/mark-paid` (151a) per invoice, in order, with each invoice's answer shown as it
 * arrives: Paid, or not changed with the reason (a 409 names the invoice's current status). One refusal never stops
 * the others, except a sign-in failure or eBilling turned off (the rest are listed as not attempted).
 */

import * as React from 'react';
import { Button, MessageBar, MessageBarBody, MessageBarTitle, ProgressBar, Text } from '@fluentui/react-components';
import type { ApExportApi } from '../services/apExportApi';
import { runMarkPaid, summarizeMarkPaid, type MarkPaidKind, type MarkPaidOutcome } from '../services/markPaidFlow';
import { summarize, type InvoiceRow } from '../services/invoices';
import { DialogLayout } from './DialogLayout';
import { InvoiceTable, type OutcomeTone, type RowOutcome } from './InvoiceTable';
import { SelectionSummary } from './SelectionSummary';

export interface MarkPaidDialogProps {
  api: ApExportApi;
  invoiceIds: string[];
  rows: InvoiceRow[];
  readFailed: boolean;
  onClose: () => void;
}

const TONE: Record<MarkPaidKind, OutcomeTone> = {
  paid: 'success',
  refused: 'warning',
  unavailable: 'warning',
  failed: 'danger',
  skipped: 'subtle',
};

const LABEL: Record<MarkPaidKind, string> = {
  paid: 'Paid',
  refused: 'Not changed',
  unavailable: 'Not available',
  failed: 'Failed',
  skipped: 'Not attempted',
};

function toRowOutcome(o: MarkPaidOutcome): RowOutcome {
  return { label: LABEL[o.kind], tone: TONE[o.kind], detail: o.kind === 'paid' ? undefined : o.message };
}

export const MarkPaidDialog: React.FC<MarkPaidDialogProps> = ({ api, invoiceIds, rows, readFailed, onClose }) => {
  const [phase, setPhase] = React.useState<'confirm' | 'running' | 'done'>('confirm');
  const [outcomes, setOutcomes] = React.useState<MarkPaidOutcome[]>([]);

  const summary = React.useMemo(() => summarize(rows), [rows]);
  const outcomeMap = React.useMemo(() => new Map(outcomes.map(o => [o.invoiceId, toRowOutcome(o)] as const)), [outcomes]);

  const start = React.useCallback(async () => {
    setPhase('running');
    setOutcomes([]);
    const all = await runMarkPaid(invoiceIds, id => api.markPaid(id), outcome => setOutcomes(prev => [...prev, outcome]));
    setOutcomes(all);
    setPhase('done');
  }, [api, invoiceIds]);

  if (phase === 'confirm') {
    const n = invoiceIds.length;
    return (
      <DialogLayout
        footerStart={<Button onClick={onClose}>Cancel</Button>}
        footer={
          <Button appearance="primary" onClick={() => void start()}>
            Mark {n} invoice{n === 1 ? '' : 's'} Paid
          </Button>
        }
      >
        <Text>
          Record that AP has paid {n === 1 ? 'this invoice' : `these ${n} invoices`}. Only an invoice that is Sent to AP can be
          marked Paid; a Paid invoice cannot be reopened. Each invoice is checked on its own: one that cannot be marked Paid is
          left unchanged and the others are still marked.
        </Text>
        {readFailed && (
          <MessageBar intent="warning">
            <MessageBarBody>The invoice details could not be read. The server checks each invoice.</MessageBarBody>
          </MessageBar>
        )}
        <SelectionSummary summary={summary} />
        <InvoiceTable rows={rows} />
      </DialogLayout>
    );
  }

  const done = phase === 'done';
  const result = summarizeMarkPaid(outcomes);
  return (
    <DialogLayout footerStart={<Button onClick={onClose} disabled={!done}>Close</Button>}>
      {!done && (
        <>
          <Text>
            Marking Paid: {outcomes.length} of {invoiceIds.length}
          </Text>
          <ProgressBar value={invoiceIds.length === 0 ? 1 : outcomes.length / invoiceIds.length} />
        </>
      )}
      {done && (
        <MessageBar intent={result.notPaid === 0 ? 'success' : result.paid === 0 ? 'error' : 'warning'}>
          <MessageBarBody>
            <MessageBarTitle>{result.paid === 0 ? 'Nothing was marked Paid' : 'Done'}</MessageBarTitle>
            {result.text}
          </MessageBarBody>
        </MessageBar>
      )}
      <InvoiceTable rows={rows} outcomes={outcomeMap} />
    </DialogLayout>
  );
};
