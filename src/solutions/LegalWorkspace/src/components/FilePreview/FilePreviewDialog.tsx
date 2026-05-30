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
import { SendEmailDialog, type ISendEmailPayload } from '@spaarke/ui-components';
import { RichFilePreviewDialog } from '@spaarke/ui-components/components/FilePreview/RichFilePreviewDialog';
import { getDocumentPreviewUrl, getDocumentOpenLinks } from '../../services/DocumentApiService';
import { navigateToEntity } from '../../utils/navigation';
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

function extractEmailFromUserName(name: string): string {
  const match = name.match(/\(([^)]+@[^)]+)\)/);
  return match ? match[1] : name;
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
    navigateToEntity({
      action: 'openRecord',
      entityName: 'sprk_document',
      entityId: documentId,
      openInNewWindow: true,
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

  const handleSendEmail = React.useCallback(
    async (payload: ISendEmailPayload) => {
      const emailAddress = extractEmailFromUserName(payload.to.name);
      const response = await authenticatedFetch(
        `${getBffBaseUrl()}/api/communications/send`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            to: [emailAddress],
            subject: payload.subject,
            body: payload.body,
            bodyFormat: 'PlainText',
            associations: [{ entityType: 'sprk_document', entityId: documentId }],
          }),
        }
      );
      if (!response.ok) {
        throw new Error(`Send failed (${response.status})`);
      }
    },
    [documentId]
  );

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
        defaultSubject={buildEmailSubject(documentName)}
        defaultBody={buildEmailBody(documentName, fileSummary)}
        onSearchUsers={handleSearchUsers}
        onSend={handleSendEmail}
      />
    </>
  );
};

FilePreviewDialog.displayName = 'FilePreviewDialog';
