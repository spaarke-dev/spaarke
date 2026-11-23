/**
 * Mark Sent to AP, opened on one batch from the Billing Export form (ebilling-r1 task 151c-1b, EB-D-182). The batch, then
 * one button. The server decides everything (Read and Write on the batch, a Completed batch, a date set once).
 */

import * as React from 'react';
import { Button, MessageBar, MessageBarBody, Text, makeStyles, tokens } from '@fluentui/react-components';
import type { ApExportApi } from '../services/apExportApi';
import { formatDateTime, type BatchInfo } from '../services/batch';
import { DialogLayout } from './DialogLayout';
import { MarkSentSection } from './MarkSentSection';

const useStyles = makeStyles({
  facts: {
    display: 'grid',
    gridTemplateColumns: 'max-content 1fr',
    columnGap: tokens.spacingHorizontalL,
    rowGap: tokens.spacingVerticalXS,
  },
  label: { color: tokens.colorNeutralForeground3 },
});

export interface MarkSentDialogProps {
  api: ApExportApi;
  batch: BatchInfo;
  readFailed?: boolean;
  onClose: () => void;
}

export const MarkSentDialog: React.FC<MarkSentDialogProps> = ({ api, batch, readFailed = false, onClose }) => {
  const s = useStyles();
  return (
    <DialogLayout footerStart={<Button onClick={onClose}>Close</Button>}>
      {readFailed && (
        <MessageBar intent="warning">
          <MessageBarBody>The batch details could not be read. The server still checks every step.</MessageBarBody>
        </MessageBar>
      )}
      <div className={s.facts}>
        <Text className={s.label}>Batch</Text>
        <Text>{batch.name ?? batch.batchId}</Text>
        {batch.invoiceCount !== null && (
          <>
            <Text className={s.label}>Invoices</Text>
            <Text>{batch.invoiceCount}</Text>
          </>
        )}
        {batch.totalLabel && (
          <>
            <Text className={s.label}>Total</Text>
            <Text>{batch.totalLabel}</Text>
          </>
        )}
        {batch.sentToApDate && (
          <>
            <Text className={s.label}>Sent to AP Date</Text>
            <Text>{formatDateTime(batch.sentToApDate)}</Text>
          </>
        )}
      </div>
      {batch.completed === false ? (
        <MessageBar intent="warning">
          <MessageBarBody>This batch is not Completed, so it cannot be marked Sent to AP.</MessageBarBody>
        </MessageBar>
      ) : (
        <MarkSentSection api={api} batchId={batch.batchId} />
      )}
    </DialogLayout>
  );
};
