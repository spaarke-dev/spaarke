/**
 * Documents — the record's documents (R3 task 054, FR-24; moved from R3's record-detail modal into the template, task 005).
 *
 * The shared `DocumentLibrary` on the record's task 031 route (`/api/v1/external/{root}/{id}/documents`): list, download,
 * version history, upload (gated by `uploadOverride` below) and delete-own (the creator stamp against `/me`). Download
 * needs only Read, which opening the record already required. The props, the requests and the texts are R3's.
 *
 * Refresh (or Try again) remounts the library, which re-reads the list.
 */
import * as React from 'react';
import type { CallerIdentity } from '../../../api/delete-own';
import type { DocumentRoot } from '../../../api/web-api-client';
import type { ExternalUserContextResponse } from '../../../auth/bff-client';
import { canUpload as levelCanUpload, mapAccessLevelString } from '../../../hooks/useAccessLevel';
import { DocumentLibrary } from '../../DocumentLibrary';
import type { RecordSectionContext } from '../recordTemplate.types';
import { RecordSectionCard } from './RecordSectionCard';

/**
 * Whether the host decides the Upload control. Only a project does: its level is in `/me`. Every other record leaves it
 * to `DocumentLibrary`, which follows the BFF's `canCreate` on that record's document list (the right the upload route
 * checks - a ViewOnly reader of a matter or work assignment is not offered an upload that can only fail; a service
 * request's requester always holds Create). The server still enforces Create on every upload.
 */
export function uploadOverride(
  root: DocumentRoot,
  id: string,
  context: ExternalUserContextResponse | null
): boolean | undefined {
  if (root !== 'projects') return undefined;
  // Task 036 fix round 1: a per-record caller's level says only what they may see; the list's `canCreate` decides.
  if (context?.writeAccessPerRecord === true) return undefined;
  const entry = context?.projects.find(p => p.projectId.toLowerCase() === id.toLowerCase());
  return levelCanUpload(mapAccessLevelString(entry?.accessLevel));
}

export interface RecordDocumentListProps {
  root: DocumentRoot;
  recordId: string;
  callerContext: ExternalUserContextResponse | null;
  caller: CallerIdentity | null;
  refreshSignal: number;
}

/** The R3 documents block: `DocumentLibrary` with R3's props, under R3's test id. Shared by Documents and (P1) Files. */
export const RecordDocumentList: React.FC<RecordDocumentListProps> = ({
  root,
  recordId,
  callerContext,
  caller,
  refreshSignal,
}) => (
  <div data-testid="record-detail-documents">
    <DocumentLibrary
      key={refreshSignal}
      root={root}
      recordId={recordId}
      canUpload={uploadOverride(root, recordId, callerContext)}
      canDownload
      caller={caller}
      writeAccessPerRecord={callerContext?.writeAccessPerRecord === true}
    />
  </div>
);

export const DocumentsSection: React.FC<RecordSectionContext> = ({
  detail,
  recordId,
  callerContext,
  caller,
  refreshSignal,
}) => {
  // A record with no documents route has no Documents section (no config lists one; this keeps a wrong config inert).
  if (!detail.documentsRoot) return null;
  return (
    <RecordSectionCard id="documents">
      <RecordDocumentList
        root={detail.documentsRoot}
        recordId={recordId}
        callerContext={callerContext}
        caller={caller}
        refreshSignal={refreshSignal}
      />
    </RecordSectionCard>
  );
};
