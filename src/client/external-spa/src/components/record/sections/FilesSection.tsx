/**
 * Files — the Invoice record's files (spec FR-01; task 005).
 *
 * P1 INTERIM: this is R3's invoice documents block, unchanged (`DocumentLibrary` on the `invoices` root, owner decision
 * 2026-10-11 option A: list, download, upload by the list's `canCreate`, versions, delete-own). Task 021 (FR-13) replaces
 * it with ebilling's `GET /invoices/{id}/files` routes (list and download only, no upload and no versions) once those
 * ship.
 */
import * as React from 'react';
import type { RecordSectionContext } from '../recordTemplate.types';
import { RecordDocumentList } from './DocumentsSection';
import { RecordSectionCard } from './RecordSectionCard';

export const FilesSection: React.FC<RecordSectionContext> = ({
  detail,
  recordId,
  callerContext,
  caller,
  refreshSignal,
  onBusyChange,
}) => {
  if (!detail.documentsRoot) return null;
  return (
    <RecordSectionCard id="files">
      <RecordDocumentList
        root={detail.documentsRoot}
        recordId={recordId}
        callerContext={callerContext}
        caller={caller}
        refreshSignal={refreshSignal}
        onBusyChange={onBusyChange}
      />
    </RecordSectionCard>
  );
};
