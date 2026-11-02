/**
 * Export to AP (ebilling-r1 task 151b, EB-D-181; FR-41). Steps:
 *   1. the selection (read for display) with its count and approved total;
 *   2. Export: one `POST /api/finance/export-batches` (task 150) for the whole selection;
 *   3a. created: the batch, its count and total, Download CSV / Download JSON (151a) and Email to AP (151c, not yet
 *       available, shown disabled);
 *   3b. refused per invoice: each refused invoice with its reason; nothing was exported; the user may export the rest
 *       (a new request, which the server checks again);
 *   3c. any other refusal: the server's message, and Try again when it is transient.
 */

import * as React from 'react';
import {
  Button,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Text,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { ArrowDownload20Regular, Mail20Regular } from '@fluentui/react-icons';
import type { ApExportApi, ExportFormat } from '../services/apExportApi';
import { downloadFailureMessage, exportOutcome, type ExportOutcome } from '../services/exportFlow';
import { formatAmount, summarize, type InvoiceRow } from '../services/invoices';
import { saveBlob } from '../services/download';
import { DialogLayout } from './DialogLayout';
import { InvoiceTable, type RowOutcome } from './InvoiceTable';
import { SelectionSummary } from './SelectionSummary';

const useStyles = makeStyles({
  facts: {
    display: 'grid',
    gridTemplateColumns: 'max-content 1fr',
    columnGap: tokens.spacingHorizontalL,
    rowGap: tokens.spacingVerticalXS,
  },
  label: { color: tokens.colorNeutralForeground3 },
  actions: { display: 'flex', flexWrap: 'wrap', gap: tokens.spacingHorizontalS },
});

export interface ExportDialogProps {
  api: ApExportApi;
  invoiceIds: string[];
  rows: InvoiceRow[];
  readFailed: boolean;
  onClose: () => void;
}

type Phase = { name: 'review' } | { name: 'sending' } | { name: 'done'; outcome: ExportOutcome; requested: string[] };

export const ExportDialog: React.FC<ExportDialogProps> = ({ api, invoiceIds, rows, readFailed, onClose }) => {
  const s = useStyles();
  const [selection, setSelection] = React.useState<string[]>(invoiceIds);
  const [phase, setPhase] = React.useState<Phase>({ name: 'review' });
  const [downloading, setDownloading] = React.useState<ExportFormat | null>(null);
  const [downloadError, setDownloadError] = React.useState<string | null>(null);

  const rowsById = React.useMemo(() => new Map(rows.map(r => [r.id, r] as const)), [rows]);
  const selectedRows = React.useMemo(() => selection.map(id => rowsById.get(id)).filter((r): r is InvoiceRow => !!r), [selection, rowsById]);
  const summary = React.useMemo(() => summarize(selectedRows), [selectedRows]);

  const send = React.useCallback(
    async (ids: string[]) => {
      setSelection(ids);
      setPhase({ name: 'sending' });
      const result = await api.createBatch(ids);
      setPhase({ name: 'done', outcome: exportOutcome(result, ids), requested: ids });
    },
    [api]
  );

  const download = React.useCallback(
    async (batchId: string, format: ExportFormat) => {
      setDownloading(format);
      setDownloadError(null);
      const result = await api.downloadFile(batchId, format);
      setDownloading(null);
      if (result.ok) saveBlob(result.value.blob, result.value.fileName);
      else setDownloadError(downloadFailureMessage(result.failure));
    },
    [api]
  );

  const close = <Button onClick={onClose}>{phase.name === 'done' && phase.outcome.kind === 'created' ? 'Close' : 'Cancel'}</Button>;

  if (phase.name === 'review' || phase.name === 'sending') {
    const sending = phase.name === 'sending';
    return (
      <DialogLayout
        footerStart={<Button onClick={onClose} disabled={sending}>Cancel</Button>}
        footer={
          <Button appearance="primary" disabled={sending || selection.length === 0} onClick={() => void send(selection)}>
            {sending ? 'Exporting…' : `Export ${selection.length} invoice${selection.length === 1 ? '' : 's'}`}
          </Button>
        }
      >
        <Text>
          The selected invoices are exported to AP as one batch: a CSV and a JSON file are written, and each invoice moves to Sent
          to AP. The server checks every invoice first; if any cannot be exported, nothing is exported.
        </Text>
        {readFailed && (
          <MessageBar intent="warning">
            <MessageBarBody>The invoice details could not be read. The selection can still be exported; the server checks it.</MessageBarBody>
          </MessageBar>
        )}
        <SelectionSummary summary={summary} />
        {sending && <Spinner size="small" label="Creating the export batch…" labelPosition="after" />}
        <InvoiceTable rows={selectedRows} />
      </DialogLayout>
    );
  }

  const { outcome, requested } = phase;
  const requestedRows = requested.map(id => rowsById.get(id)).filter((r): r is InvoiceRow => !!r);

  if (outcome.kind === 'created') {
    const { batch, formats } = outcome;
    const currencies = Array.from(new Set(requestedRows.map(r => r.currency).filter((c): c is string => !!c)));
    const total = formatAmount(batch.totalAmount, currencies.length === 1 ? currencies[0] : null);
    return (
      <DialogLayout footerStart={close}>
        <MessageBar intent="success">
          <MessageBarBody>
            <MessageBarTitle>Exported to AP</MessageBarTitle>
            {batch.invoiceCount} invoice{batch.invoiceCount === 1 ? '' : 's'} are now Sent to AP.
          </MessageBarBody>
        </MessageBar>
        <div className={s.facts}>
          <Text className={s.label}>Batch</Text>
          <Text>{batch.name}</Text>
          <Text className={s.label}>Invoices</Text>
          <Text>{batch.invoiceCount}</Text>
          <Text className={s.label}>Total</Text>
          <Text>{total}</Text>
        </div>
        <div className={s.actions}>
          {formats.map(format => (
            <Button
              key={format}
              icon={<ArrowDownload20Regular />}
              disabled={downloading !== null}
              onClick={() => void download(batch.batchId, format)}
            >
              {downloading === format ? 'Downloading…' : `Download ${format.toUpperCase()}`}
            </Button>
          ))}
          <Tooltip content="Email to AP is not available yet." relationship="description">
            <Button icon={<Mail20Regular />} disabled data-testid="email-to-ap">
              Email to AP
            </Button>
          </Tooltip>
        </div>
        {formats.length === 0 && (
          <MessageBar intent="warning">
            <MessageBarBody>This batch lists no file to download yet. Open the Billing Export record to download it later.</MessageBarBody>
          </MessageBar>
        )}
        {downloadError && (
          <MessageBar intent="error">
            <MessageBarBody>{downloadError}</MessageBarBody>
          </MessageBar>
        )}
        <InvoiceTable rows={requestedRows} />
      </DialogLayout>
    );
  }

  if (outcome.kind === 'refused') {
    const outcomes = new Map<string, RowOutcome>(
      outcome.refused.map(r => [r.invoiceId, { label: 'Refused', tone: 'danger', detail: r.text }] as const)
    );
    const remainingRows = outcome.remaining.map(id => rowsById.get(id)).filter((r): r is InvoiceRow => !!r);
    const remainingSummary = summarize(remainingRows);
    const unknown = outcome.refused.filter(r => !rowsById.has(r.invoiceId));
    return (
      <DialogLayout
        footerStart={close}
        footer={
          outcome.remaining.length > 0 && (
            <Button appearance="primary" onClick={() => void send(outcome.remaining)}>
              Export the other {outcome.remaining.length}
            </Button>
          )
        }
      >
        <MessageBar intent="warning">
          <MessageBarBody>
            <MessageBarTitle>Some invoices cannot be exported</MessageBarTitle>
            {outcome.message}
          </MessageBarBody>
        </MessageBar>
        {outcome.remaining.length > 0 && <SelectionSummary summary={remainingSummary} label="Can be exported" />}
        <InvoiceTable rows={requestedRows} outcomes={outcomes} outcomeHeader="Export check" />
        {unknown.map(r => (
          <Text key={r.invoiceId}>
            {r.invoiceId}: {r.text}
          </Text>
        ))}
      </DialogLayout>
    );
  }

  return (
    <DialogLayout
      footerStart={close}
      footer={
        outcome.retryable && (
          <Button appearance="primary" onClick={() => void send(requested)}>
            Try again
          </Button>
        )
      }
    >
      <MessageBar intent="error">
        <MessageBarBody>
          <MessageBarTitle>Not exported</MessageBarTitle>
          {outcome.message}
        </MessageBarBody>
      </MessageBar>
      <InvoiceTable rows={requestedRows} />
    </DialogLayout>
  );
};
