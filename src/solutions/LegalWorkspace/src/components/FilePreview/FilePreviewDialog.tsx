/**
 * FilePreviewDialog.tsx — LegalWorkspace adapter for `@spaarke/ui-components` `RichFilePreviewDialog`.
 *
 * Wraps the shared rich 2-column preview dialog with LegalWorkspace-specific
 * services (BFF preview URL, open-links cascade, copy-link, workspace flag,
 * email send via SendEmailDialog).
 *
 * Preserves the existing `IFilePreviewDialogProps` contract so consumers
 * (`DocumentCard.tsx`) don't need import changes.
 */

import * as React from 'react';
import { SendEmailDialog } from '@spaarke/ui-components';
import { RichFilePreviewDialog } from '@spaarke/ui-components/components/FilePreview/RichFilePreviewDialog';
import { getDocumentPreviewUrl, getDocumentOpenLinks } from '../../services/DocumentApiService';
import { createXrmNavigationService } from '@spaarke/ui-components';
import { copyDocumentLink, setWorkspaceFlag } from './filePreviewService';
import { searchUsersAsLookup } from '../CreateMatter/matterService';
import { getXrm } from '../../services/xrmProvider';
import { authenticatedFetch } from '../../services/authInit';
import { getBffBaseUrl } from '../../config/runtimeConfig';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IFilePreviewDialogProps {
  open: boolean;
  documentId: string;
  documentName: string;
  onClose: () => void;
  /** Whether this document is currently in the user's workspace. */
  isInWorkspace?: boolean;
  /** Called when the workspace flag changes. */
  onWorkspaceFlagChanged?: (newFlag: boolean) => void;
  /** File summary text for email body pre-population. */
  fileSummary?: string;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function buildEmailSubject(documentName: string): string {
  return `Document: ${documentName}`;
}

function buildEmailBody(documentName: string, fileSummary?: string): string {
  const summaryBlock = fileSummary || 'No summary available.';
  return (
    `Dear Colleague,\n\n` +
    `Please find the following document for your review:\n\n` +
    `Document: ${documentName}\n\n` +
    `────\n\n` +
    `${summaryBlock}\n\n` +
    `────\n\n` +
    `Kind regards`
  );
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const FilePreviewDialog: React.FC<IFilePreviewDialogProps> = ({
  open,
  documentId,
  documentName,
  onClose,
  isInWorkspace,
  onWorkspaceFlagChanged,
  fileSummary,
}) => {
  const [inWorkspace, setInWorkspace] = React.useState(isInWorkspace ?? false);
  const [emailDialogOpen, setEmailDialogOpen] = React.useState(false);

  React.useEffect(() => {
    setInWorkspace(isInWorkspace ?? false);
  }, [isInWorkspace]);

  const fetchPreviewUrl = React.useCallback(
    () => getDocumentPreviewUrl(documentId),
    [documentId]
  );

  // Open File: desktop protocol for Office files; download via BFF for others.
  const handleOpenFile = React.useCallback(async () => {
    const links = await getDocumentOpenLinks(documentId);
    if (links?.desktopUrl) {
      window.location.href = links.desktopUrl;
      return;
    }
    try {
      const contentUrl = `${getBffBaseUrl()}/api/documents/${encodeURIComponent(documentId)}/content`;
      const response = await authenticatedFetch(contentUrl);
      if (response.ok) {
        const blob = await response.blob();
        const objectUrl = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = objectUrl;
        a.download = links?.fileName ?? documentName ?? 'document';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(objectUrl);
      }
    } catch (err) {
      console.error('[FilePreviewDialog] Download failed:', err);
    }
  }, [documentId, documentName]);

  const handleOpenRecord = React.useCallback(() => {
    // #717 item 1 (owner decision 2026-08-05): the retired local helper opened
    // the record with `openInNewWindow: true` so the workspace was never
    // navigated away; task 091's repoint to the shared adapter's `openRecord()`
    // (openForm, same-tab) clobbered the workspace tab. Resolution: the
    // MODAL-DECISION-CRITERIA Layout-1 standard — `openRecordModal` (85%×85%
    // `navigateTo` target:2 overlay; host page stays) — same no-clobber outcome
    // as new-window, in-app and standard-conformant. `openRecord` remains the
    // fallback only for a host service without the optional member.
    const nav = createXrmNavigationService();
    (nav.openRecordModal
      ? nav.openRecordModal('sprk_document', documentId)
      : nav.openRecord('sprk_document', documentId)
    ).catch((err) => {
      console.error('[FilePreviewDialog] open record failed:', err);
    });
  }, [documentId]);

  const handleCopyLink = React.useCallback(async () => {
    await copyDocumentLink(documentId);
  }, [documentId]);

  const handleToggleWorkspace = React.useCallback(async () => {
    const newFlag = !inWorkspace;
    setInWorkspace(newFlag);
    const success = await setWorkspaceFlag(documentId, newFlag);
    if (success) {
      onWorkspaceFlagChanged?.(newFlag);
    } else {
      setInWorkspace(!newFlag);
    }
  }, [documentId, inWorkspace, onWorkspaceFlagChanged]);

  const handleEmailDocument = React.useCallback(() => {
    setEmailDialogOpen(true);
  }, []);

  const handleSearchUsers = React.useCallback(async (query: string) => {
    const xrm = getXrm();
    if (!xrm?.WebApi) return [];
    return searchUsersAsLookup(xrm.WebApi, query);
  }, []);

  return (
    <>
      <RichFilePreviewDialog
        open={open}
        documentId={documentId}
        documentName={documentName}
        onClose={onClose}
        fetchPreviewUrl={fetchPreviewUrl}
        onOpenFile={handleOpenFile}
        onOpenRecord={handleOpenRecord}
        onEmailDocument={handleEmailDocument}
        onCopyLink={handleCopyLink}
        onToggleWorkspace={handleToggleWorkspace}
        isInWorkspace={inWorkspace}
      />
      <SendEmailDialog
        open={emailDialogOpen}
        onClose={() => setEmailDialogOpen(false)}
        initialSubject={buildEmailSubject(documentName)}
        initialBody={buildEmailBody(documentName, fileSummary)}
        initialBodyFormat="PlainText"
        associations={[{ entityType: 'sprk_document', entityId: documentId }]}
        onSearchRecipients={handleSearchUsers}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={getBffBaseUrl()}
        onError={err => console.error('[FilePreviewDialog] Send failed:', err.detail)}
      />
    </>
  );
};

FilePreviewDialog.displayName = 'FilePreviewDialog';
