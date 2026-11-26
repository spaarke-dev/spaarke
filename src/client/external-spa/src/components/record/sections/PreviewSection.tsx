/**
 * Preview — the Document record's file (spec FR-01; task 005, FR-14 / FR-15; task 022).
 *
 * The inline preview (`DocumentPreview`) of a PDF or an image up to 25 MB; any other file, a larger one and a document with
 * no file get a short note. The one action, Download, is the record toolbar's (`DocumentDownloadAction`).
 *
 * Whether a document offers Download is decided once, by `documentFileOf` (R3 task 066 + ISS-011 #1886): it needs a usable
 * parent link (the content route is asked per parent, in the BFF's order) and a file (`hasFile !== false`; absent, from an
 * older BFF, keeps Download). The toolbar action and this card use the same answer.
 */
import * as React from 'react';
import {
  EMPTY_VALUE,
  documentParentCandidates,
  formatDetailValue,
  type DetailRecord,
  type DocumentParentCandidate,
} from '../../detail/RecordDetailFields';
import type { RecordSectionContext } from '../recordTemplate.types';
import { DocumentPreview } from './DocumentPreview';
import { RecordSectionCard } from './RecordSectionCard';

/** A document's downloadable file: the parents to ask (BFF order) and the name to save it under. */
export interface DocumentFile {
  parents: DocumentParentCandidate[];
  name: string;
}

/** The document's downloadable file, or null when it offers no Download (no usable parent link, or no file). */
export function documentFileOf(record: DetailRecord): DocumentFile | null {
  const parents = documentParentCandidates(record);
  if (parents.length === 0) return null;
  // R3 ISS-011 (#1886): a document with no file is still shown (its fields), but offers no Download: it could only answer
  // 409 "No file attached". Only an explicit false hides it; absent (an older BFF) keeps Download.
  if (record.hasFile === false) return null;
  const shownName = formatDetailValue(record, { attribute: 'sprk_documentname' });
  return { parents, name: shownName !== EMPTY_VALUE ? shownName : 'document' };
}

/** The row's `sprk_filetype` (read through `RECORD_DETAIL_ENTITIES.sprk_document.extraColumns`), or null when absent. */
function fileTypeOf(record: DetailRecord): string | null {
  const value = record.sprk_filetype;
  return typeof value === 'string' ? value : null;
}

export const PreviewSection: React.FC<RecordSectionContext> = ({ record, recordId }) => {
  const file = React.useMemo(() => documentFileOf(record), [record]);
  const fileType = fileTypeOf(record);
  // Task 022 (FR-14 / FR-15): previewable files render inline; every other state (other type, over 25 MB, no file, a failed
  // read) is DocumentPreview's note. Download stays the toolbar's action either way.
  return (
    <RecordSectionCard id="preview">
      <DocumentPreview documentId={recordId} file={file} fileType={fileType} />
    </RecordSectionCard>
  );
};
