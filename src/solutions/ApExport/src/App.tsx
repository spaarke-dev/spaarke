/**
 * AP Export dialog (ebilling-r1 task 151b, EB-D-181): reads the launch envelope, reads the selection for display, and
 * shows the Export to AP or the Mark Paid dialog. Task 151c-1b: opened on one batch (`mode=email|marksent`) it reads the
 * batch for display and shows the Email to AP step or the Mark Sent to AP dialog.
 */

import * as React from 'react';
import { MessageBar, MessageBarBody, Button, Spinner } from '@fluentui/react-components';
import { getXrm } from '@spaarke/ui-components';
import type { ApExportApi } from './services/apExportApi';
import type { Launch } from './services/launch';
import { readInvoices, type InvoiceRow, type WebApiLike } from './services/invoices';
import { DialogLayout } from './components/DialogLayout';
import { ExportDialog } from './components/ExportDialog';
import { MarkPaidDialog } from './components/MarkPaidDialog';
import { BatchEmailView } from './components/BatchEmailView';
import { MarkSentDialog } from './components/MarkSentDialog';
import { readBatch, type BatchInfo, type RecordReaderLike } from './services/batch';

export interface AppProps {
  api: ApExportApi;
  launch: Launch;
  onClose: () => void;
}

export const App: React.FC<AppProps> = ({ api, launch, onClose }) => {
  const [loaded, setLoaded] = React.useState<{ rows: InvoiceRow[]; readFailed: boolean } | null>(null);
  const [loadedBatch, setLoadedBatch] = React.useState<{ batch: BatchInfo; readFailed: boolean } | null>(null);

  React.useEffect(() => {
    if (!launch.ok) return;
    let cancelled = false;
    const webApi = getXrm('webApi')?.WebApi;
    if ('batchId' in launch) {
      void readBatch(webApi as unknown as RecordReaderLike | undefined, launch.batchId).then(result => {
        if (!cancelled) setLoadedBatch(result);
      });
    } else {
      void readInvoices(webApi as unknown as WebApiLike | undefined, launch.invoiceIds).then(result => {
        if (!cancelled) setLoaded(result);
      });
    }
    return () => {
      cancelled = true;
    };
  }, [launch]);

  if (!launch.ok) {
    return (
      <DialogLayout footerStart={<Button onClick={onClose}>Close</Button>}>
        <MessageBar intent="error">
          <MessageBarBody>{launch.error}</MessageBarBody>
        </MessageBar>
      </DialogLayout>
    );
  }

  if ('batchId' in launch) {
    if (!loadedBatch) {
      return (
        <DialogLayout footerStart={<Button onClick={onClose}>Cancel</Button>}>
          <Spinner size="small" label="Reading the export batch…" labelPosition="after" />
        </DialogLayout>
      );
    }
    return launch.mode === 'email' ? (
      <BatchEmailView api={api} batch={loadedBatch.batch} readFailed={loadedBatch.readFailed} onClose={onClose} />
    ) : (
      <MarkSentDialog api={api} batch={loadedBatch.batch} readFailed={loadedBatch.readFailed} onClose={onClose} />
    );
  }

  if (!loaded) {
    return (
      <DialogLayout footerStart={<Button onClick={onClose}>Cancel</Button>}>
        <Spinner size="small" label="Reading the selected invoices…" labelPosition="after" />
      </DialogLayout>
    );
  }

  const props = { api, invoiceIds: launch.invoiceIds, rows: loaded.rows, readFailed: loaded.readFailed, onClose };
  return launch.mode === 'export' ? <ExportDialog {...props} /> : <MarkPaidDialog {...props} />;
};
