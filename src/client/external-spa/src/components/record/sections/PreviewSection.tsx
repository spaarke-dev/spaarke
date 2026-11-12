/**
 * Preview — the Document record's file (spec FR-01; task 005).
 *
 * P1 INTERIM: R3's file block. Its one action, Download, is now the record toolbar's Download (`DocumentDownloadAction`);
 * this card says whether there is a file to download. Task 022 (FR-14) adds the inline preview here.
 *
 * Whether a document offers Download is decided once, by `documentFileOf` (R3 task 066 + ISS-011 #1886): it needs a usable
 * parent link (the content route is asked per parent, in the BFF's order) and a file (`hasFile !== false`; absent, from an
 * older BFF, keeps Download). The toolbar action and this card use the same answer.
 */
import * as React from 'react';
import { makeStyles, tokens, Text } from '@fluentui/react-components';
import {
  EMPTY_VALUE,
  documentParentCandidates,
  formatDetailValue,
  type DetailRecord,
  type DocumentParentCandidate,
} from '../../detail/RecordDetailFields';
import type { RecordSectionContext } from '../recordTemplate.types';
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

const useStyles = makeStyles({
  text: {
    color: tokens.colorNeutralForeground2,
  },
});

export const PreviewSection: React.FC<RecordSectionContext> = ({ record }) => {
  const styles = useStyles();
  const file = React.useMemo(() => documentFileOf(record), [record]);
  return (
    <RecordSectionCard id="preview">
      {file ? (
        <Text className={styles.text} data-testid="record-detail-download">
          Use Download in the toolbar to open this file.
        </Text>
      ) : (
        <Text className={styles.text} data-testid="record-preview-no-file">
          There is no file to show for this document.
        </Text>
      )}
    </RecordSectionCard>
  );
};
