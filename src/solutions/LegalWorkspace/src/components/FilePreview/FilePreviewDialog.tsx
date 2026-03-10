/**
 * FilePreviewDialog.tsx
 * Full-screen modal dialog for document preview with toolbar actions.
 *
 * Features:
 *   - Fluent UI v9 Dialog (85vw x 85vh, max 880px)
 *   - Iframe preview with Spinner during load, error + retry on failure
 *   - Icon-only toolbar (right-justified): Open File, Open Document,
 *     Email Document, Copy Link, Add/Remove Workspace
 *   - Open File: lazy-fetch open links, cascade desktop -> web -> download
 *   - Email Document: opens SendEmailDialog from shared library
 */

import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  Button,
  Toolbar,
  ToolbarButton,
  Tooltip,
  Spinner,
  Text,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import {
  Dismiss24Regular,
  Open24Regular,
  DocumentRegular,
  MailRegular,
  LinkRegular,
  StarRegular,
  StarFilled,
} from '@fluentui/react-icons';
import { SendEmailDialog, type ISendEmailPayload } from '@spaarke/ui-components';
import { getDocumentPreviewUrl, getDocumentOpenLinks } from '../../services/DocumentApiService';
import { navigateToEntity } from '../../utils/navigation';
import { copyDocumentLink, setWorkspaceFlag } from './filePreviewService';
import { searchUsersAsLookup } from '../CreateMatter/matterService';
import { getXrm } from '../../services/xrmProvider';
import { authenticatedFetch } from '../../services/authInit';
import { getBffBaseUrl } from '../../config/bffConfig';

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

/**
 * Extract email address from a user name like "John Doe (john@example.com)".
 */
function extractEmailFromUserName(name: string): string {
  const match = name.match(/\(([^)]+@[^)]+)\)/);
  return match ? match[1] : name;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  surface: {
    width: '85vw',
    maxWidth: '880px',
    height: '85vh',
    maxHeight: '85vh',
    ...shorthands.padding('0px'),
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.overflow('hidden'),
    ...shorthands.borderRadius(tokens.borderRadiusXLarge),
  },
  titleBar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalS,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  titleText: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    flex: 1,
    minWidth: 0,
  },
  toolbar: {
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
    justifyContent: 'flex-end',
    minHeight: '36px',
    gap: tokens.spacingHorizontalS,
  },
  body: {
    ...shorthands.padding('0px'),
    flex: 1,
    minHeight: 0,
    position: 'relative' as const,
    ...shorthands.overflow('hidden'),
  },
  iframe: {
    position: 'absolute' as const,
    top: 0,
    left: 0,
    width: '100%',
    height: '100%',
    borderWidth: '0px',
  },
  centerContent: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    width: '100%',
    height: '100%',
    gap: tokens.spacingVerticalM,
  },
});

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
  const styles = useStyles();

  // Preview state
  const [previewUrl, setPreviewUrl] = React.useState<string | null>(null);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState(false);

  // Workspace flag local state (track optimistically)
  const [inWorkspace, setInWorkspace] = React.useState(isInWorkspace ?? false);

  // Email dialog state
  const [emailDialogOpen, setEmailDialogOpen] = React.useState(false);

  // Sync prop changes
  React.useEffect(() => {
    setInWorkspace(isInWorkspace ?? false);
  }, [isInWorkspace]);

  // Fetch preview URL when dialog opens
  React.useEffect(() => {
    if (!open || !documentId) {
      setPreviewUrl(null);
      setError(false);
      return;
    }

    let cancelled = false;
    setLoading(true);
    setError(false);

    (async () => {
      const url = await getDocumentPreviewUrl(documentId);
      if (cancelled) return;
      if (url) {
        setPreviewUrl(url);
      } else {
        setError(true);
      }
      setLoading(false);
    })();

    return () => {
      cancelled = true;
    };
  }, [open, documentId]);

  // Retry handler
  const handleRetry = React.useCallback(() => {
    if (!documentId) return;
    setLoading(true);
    setError(false);
    setPreviewUrl(null);
    (async () => {
      const url = await getDocumentPreviewUrl(documentId);
      if (url) {
        setPreviewUrl(url);
      } else {
        setError(true);
      }
      setLoading(false);
    })();
  }, [documentId]);

  // Open File: fetch open links and cascade desktop → web → preview fallback.
  // PDFs lack a desktop protocol URL — skip desktopUrl for PDF MIME types
  // to avoid Access Denied errors from protocol handlers.
  const handleOpenFile = React.useCallback(async () => {
    const links = await getDocumentOpenLinks(documentId);
    if (links) {
      const isPdf = links.mimeType?.toLowerCase() === 'application/pdf';
      if (!isPdf && links.desktopUrl) {
        window.location.href = links.desktopUrl;
        return;
      }
      if (links.webUrl) {
        window.open(links.webUrl, '_blank', 'noopener,noreferrer');
        return;
      }
    }
    if (previewUrl) {
      window.open(previewUrl, '_blank', 'noopener,noreferrer');
    }
  }, [documentId, previewUrl]);

  // Open Document Record (new tab)
  const handleOpenRecord = React.useCallback(() => {
    navigateToEntity({
      action: 'openRecord',
      entityName: 'sprk_document',
      entityId: documentId,
      openInNewWindow: true,
    });
  }, [documentId]);

  // Copy Link
  const handleCopyLink = React.useCallback(async () => {
    await copyDocumentLink(documentId);
  }, [documentId]);

  // Toggle workspace flag
  const handleToggleWorkspace = React.useCallback(async () => {
    const newFlag = !inWorkspace;
    setInWorkspace(newFlag); // optimistic
    const success = await setWorkspaceFlag(documentId, newFlag);
    if (success) {
      onWorkspaceFlagChanged?.(newFlag);
    } else {
      setInWorkspace(!newFlag); // revert
    }
  }, [documentId, inWorkspace, onWorkspaceFlagChanged]);

  // Email — search users
  const handleSearchUsers = React.useCallback(async (query: string) => {
    const xrm = getXrm();
    if (!xrm?.WebApi) return [];
    return searchUsersAsLookup(xrm.WebApi, query);
  }, []);

  // Email — send
  const handleSendEmail = React.useCallback(async (payload: ISendEmailPayload) => {
    const emailAddress = extractEmailFromUserName(payload.to.name);
    const response = await authenticatedFetch(
      `${getBffBaseUrl()}/communications/send`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          to: [emailAddress],
          subject: payload.subject,
          body: payload.body,
          bodyFormat: 'Text',
          associations: [{ entityType: 'sprk_document', entityId: documentId }],
        }),
      }
    );
    if (!response.ok) {
      throw new Error(`Send failed (${response.status})`);
    }
  }, [documentId]);

  return (
    <>
      <Dialog
        open={open}
        onOpenChange={(_, data) => {
          if (!data.open) onClose();
        }}
      >
        <DialogSurface className={styles.surface}>
          {/* Title bar */}
          <div className={styles.titleBar}>
            <DialogTitle action={null} className={styles.titleText}>
              {documentName || 'Document Preview'}
            </DialogTitle>
            <Tooltip content="Close" relationship="label">
              <Button
                appearance="subtle"
                icon={<Dismiss24Regular />}
                aria-label="Close"
                onClick={onClose}
              />
            </Tooltip>
          </div>

          {/* Toolbar — icon-only, right-justified */}
          <Toolbar className={styles.toolbar} size="small">
            <Tooltip content="Open file" relationship="label">
              <ToolbarButton
                icon={<Open24Regular />}
                aria-label="Open file"
                onClick={handleOpenFile}
              />
            </Tooltip>
            <Tooltip content="Open document record" relationship="label">
              <ToolbarButton
                icon={<DocumentRegular />}
                aria-label="Open document record"
                onClick={handleOpenRecord}
              />
            </Tooltip>
            <Tooltip content="Email document" relationship="label">
              <ToolbarButton
                icon={<MailRegular />}
                aria-label="Email document"
                onClick={() => setEmailDialogOpen(true)}
              />
            </Tooltip>
            <Tooltip content="Copy link" relationship="label">
              <ToolbarButton
                icon={<LinkRegular />}
                aria-label="Copy link"
                onClick={handleCopyLink}
              />
            </Tooltip>
            <Tooltip
              content={inWorkspace ? 'Remove from workspace' : 'Add to workspace'}
              relationship="label"
            >
              <ToolbarButton
                icon={inWorkspace ? <StarFilled /> : <StarRegular />}
                aria-label={inWorkspace ? 'Remove from workspace' : 'Add to workspace'}
                onClick={handleToggleWorkspace}
              />
            </Tooltip>
          </Toolbar>

          {/* Preview content */}
          <DialogBody className={styles.body}>
            <DialogContent className={styles.body}>
              {loading && (
                <div className={styles.centerContent}>
                  <Spinner size="large" label="Loading preview..." labelPosition="below" />
                </div>
              )}
              {error && !loading && (
                <div className={styles.centerContent}>
                  <Text size={400} weight="semibold">
                    Preview not available
                  </Text>
                  <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                    Unable to load the document preview. The file may be unsupported or temporarily unavailable.
                  </Text>
                  <Button appearance="primary" onClick={handleRetry}>
                    Retry
                  </Button>
                </div>
              )}
              {previewUrl && !loading && !error && (
                <iframe
                  src={previewUrl}
                  title={`Preview: ${documentName}`}
                  className={styles.iframe}
                  sandbox="allow-scripts allow-same-origin allow-forms allow-popups"
                />
              )}
            </DialogContent>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      {/* Send Email Dialog */}
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
