/**
 * Files — the Invoice record's files (spec FR-01; task 005).
 *
 * P1 INTERIM: this is R3's invoice documents block, unchanged (`DocumentLibrary` on the `invoices` root, owner decision
 * 2026-10-11 option A: list, download, upload by the list's `canCreate`, versions, delete-own). Task 021 (FR-13) replaces
 * it with ebilling's `GET /invoices/{id}/files` routes (list and download only, no upload and no versions) once those
 * ship.
 *
 * ACCESS (task 006 fix round 1, FR-07). An invoice's toolbar has no Upload, so the list keeps its own Upload button, shown
 * by its list's `canCreate`. That right is still part of what the caller may do on the record: it is reported to the
 * template (`reportRight('filesUpload')`), which counts it in "Your access" and gives this card its read-only line when the
 * list says no. It adds nothing to the toolbar.
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
  readOnlyNote,
  reportRight,
}) => {
  if (!detail.documentsRoot) return null;
  return (
    <RecordSectionCard id="files" note={readOnlyNote}>
      <RecordDocumentList
        root={detail.documentsRoot}
        recordId={recordId}
        callerContext={callerContext}
        caller={caller}
        refreshSignal={refreshSignal}
        onBusyChange={onBusyChange}
        onUploadRightChange={reportRight?.('filesUpload')}
      />
    </RecordSectionCard>
  );
};
