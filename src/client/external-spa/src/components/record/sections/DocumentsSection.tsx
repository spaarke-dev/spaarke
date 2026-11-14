/**
 * Documents — the record's documents (R3 task 054, FR-24; moved from R3's record-detail modal into the template, task 005).
 *
 * The shared `DocumentLibrary` on the record's task 031 route (`/api/v1/external/{root}/{id}/documents`): list, download,
 * version history, upload (gated by `uploadOverride` below) and delete-own (the creator stamp against `/me`). Download
 * needs only Read, which opening the record already required. The props, the requests and the texts are R3's.
 *
 * Refresh (or Try again) remounts the library, which re-reads the list.
 *
 * UPLOAD (task 004, FR-03). When the record's toolbar lists Upload, the toolbar button is the entry point: the library
 * offers it through `offerAction('upload')` (shown under the same rule as its own button, which it then does not draw) and
 * renders the upload dialog (a `FormModal`) itself, inside this section, so the dialog is a descendant of the record
 * modal. An upload in flight holds browsing, Close and Refresh (`onBusyChange`).
 *
 * WHO MAY UPLOAD (task 006, FR-06 / FR-07). Inside the template the library offers Upload only when `createRightOf`
 * (recordAccess.ts) says `true`: the list's `canCreate`, and for a level-decided project caller the `/me` level
 * (`uploadOverride`) agreeing with it. Otherwise the card shows the template's read-only line (`readOnlyNote`).
 */
import * as React from 'react';
import type { CallerIdentity } from '../../../api/delete-own';
import type { DocumentRoot } from '../../../api/web-api-client';
import type { ExternalUserContextResponse } from '../../../auth/bff-client';
import { canUpload as levelCanUpload } from '../../../hooks/useAccessLevel';
import { decidingLevelOf } from '../recordAccess';
import { DocumentLibrary } from '../../DocumentLibrary';
import type { CreateActionReporter } from '../../createAction';
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
  // Task 036 fix round 1: a per-record caller's level says only what they may see; the list's `canCreate` decides. Task
  // 006: the level is the one `decidingLevelOf` finds (shared with the to-do and event lists; unchanged rule: an unknown
  // `/me` or a missing entry is View Only); with none (not a project, a per-record caller) the list decides.
  const level = decidingLevelOf(root, id, context);
  return level === undefined ? undefined : levelCanUpload(level);
}

export interface RecordDocumentListProps {
  root: DocumentRoot;
  recordId: string;
  callerContext: ExternalUserContextResponse | null;
  caller: CallerIdentity | null;
  refreshSignal: number;
  /** Offer the toolbar's Upload instead of drawing the library's own button (the record template, task 004). */
  onCreateActionChange?: CreateActionReporter;
  /** Report an upload in flight, so the record modal holds. */
  onBusyChange?: (busy: boolean) => void;
  /** Tell the template the Upload right without offering a toolbar action (the invoice Files list; task 006 fix round 1). */
  onUploadRightChange?: (right: boolean | null) => void;
}

/** The R3 documents block: `DocumentLibrary` with R3's props, under R3's test id. Shared by Documents and (P1) Files. */
export const RecordDocumentList: React.FC<RecordDocumentListProps> = ({
  root,
  recordId,
  callerContext,
  caller,
  refreshSignal,
  onCreateActionChange,
  onBusyChange,
  onUploadRightChange,
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
      onCreateActionChange={onCreateActionChange}
      onBusyChange={onBusyChange}
      onUploadRightChange={onUploadRightChange}
    />
  </div>
);

export const DocumentsSection: React.FC<RecordSectionContext> = ({
  detail,
  recordId,
  callerContext,
  caller,
  refreshSignal,
  onBusyChange,
  offerAction,
  readOnlyNote,
}) => {
  // A record with no documents route has no Documents section (no config lists one; this keeps a wrong config inert).
  if (!detail.documentsRoot) return null;
  return (
    <RecordSectionCard id="documents" note={readOnlyNote}>
      <RecordDocumentList
        root={detail.documentsRoot}
        recordId={recordId}
        callerContext={callerContext}
        caller={caller}
        refreshSignal={refreshSignal}
        onCreateActionChange={offerAction('upload')}
        onBusyChange={onBusyChange}
      />
    </RecordSectionCard>
  );
};
