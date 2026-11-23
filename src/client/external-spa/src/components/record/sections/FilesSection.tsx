/**
 * Files — the Invoice record's files (spec FR-13; R4 task 021).
 *
 * READ-ONLY. The list is ebilling's `GET /api/v1/external/invoices/{id}/files` and its `.../{fileId}/content` download
 * (`InvoiceFilesList`): no Upload, no Version history and no Delete exist here, and the invoice template lists no section
 * action for Files. Refresh remounts the list, which re-reads it.
 */
import * as React from 'react';
import type { RecordSectionContext } from '../recordTemplate.types';
import { InvoiceFilesList } from './InvoiceFilesList';
import { RecordSectionCard } from './RecordSectionCard';

export const FilesSection: React.FC<RecordSectionContext> = ({ recordId, refreshSignal }) => (
  <RecordSectionCard id="files">
    <InvoiceFilesList key={refreshSignal} invoiceId={recordId} />
  </RecordSectionCard>
);
