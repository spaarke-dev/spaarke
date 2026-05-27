/**
 * FilePreviewDialog — Full-screen modal for document preview.
 *
 * Mirrors the LegalWorkspace FilePreviewDialog but uses callbacks
 * instead of importing service modules directly, so it works within
 * the PCF control's service layer.
 *
 * Per FR-DOC-01, the previous inline toolbar (Open File / Open Record /
 * Email / Copy Link / Workspace) is replaced by a single 3-dot
 * `DocumentRowMenu` shared component. Actions not reachable from the
 * dialog surface (preview/findSimilar/aiSummary/pinToTop/rename/delete/
 * download) are hidden via the menu's `disabledActions` prop until those
 * handlers are introduced by follow-on Phase 4 tasks (044 dialog
 * restructure).
 *
 * @see ADR-012 - Shared component library
 * @see ADR-021 - Fluent UI v9 (semantic tokens, dark-mode parity)
 * @see spec.md FR-DOC-01
 */

import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  Button,
  Tooltip,
  Spinner,
  Text,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import {
  Dismiss24Regular,
} from '@fluentui/react-icons';
// Deep-path import (not the barrel) — the barrel pulls in RichTextEditor →
// `@lexical/react` ESM modules that don't resolve under React 16 (PCF target
// per ADR-022). Matches the deep-path pattern used by sibling components.
import {
  DocumentRowMenu,
  type DocumentRowAction,
  type IDocumentRowMenuTarget,
} from '@spaarke/ui-components/dist/components/DocumentRowMenu';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IFilePreviewDialogProps {
  open: boolean;
  documentName: string;
  /** Stable document identifier — required for the 3-dot menu's aria-label. */
  documentId: string;
  /** Optional document type (file extension or category) for menu context. */
  documentType?: string;
  onClose: () => void;
  /** Fetch the preview embed URL. Called when the dialog opens. */
  fetchPreviewUrl: () => Promise<string | null>;
  /** Open the file in desktop or web app. */
  onOpenFile: (mode: 'desktop' | 'web') => void;
  /** Open the Dataverse record in a new tab. */
  onOpenRecord: () => void;
  /** Open the email document dialog. */
  onEmailDocument: () => void;
  /** Copy the document link to clipboard. */
  onCopyLink: () => void;
  /** Toggle workspace flag. */
  onToggleWorkspace?: () => void;
  /** Whether document is currently in workspace. */
  isInWorkspace?: boolean;
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
    ...shorthands.overflow('hidden'),
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    flex: 1,
    minWidth: 0,
  },
  titleActions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
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
    ...shorthands.borderWidth('0px'),
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
  documentName,
  documentId,
  documentType,
  onClose,
  fetchPreviewUrl,
  onOpenFile,
  onOpenRecord,
  onEmailDocument,
  onCopyLink,
  onToggleWorkspace,
  isInWorkspace,
}) => {
  const styles = useStyles();

  const [previewUrl, setPreviewUrl] = React.useState<string | null>(null);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState(false);

  // Fetch preview URL when dialog opens
  React.useEffect(() => {
    if (!open) {
      setPreviewUrl(null);
      setError(false);
      return;
    }

    let cancelled = false;
    setLoading(true);
    setError(false);

    void (async () => {
      const url = await fetchPreviewUrl();
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
  }, [open, fetchPreviewUrl]);

  const handleRetry = React.useCallback(() => {
    setLoading(true);
    setError(false);
    setPreviewUrl(null);
    void (async () => {
      const url = await fetchPreviewUrl();
      if (url) {
        setPreviewUrl(url);
      } else {
        setError(true);
      }
      setLoading(false);
    })();
  }, [fetchPreviewUrl]);

  // -------------------------------------------------------------------------
  // 3-dot menu dispatch — replaces the previous Toolbar of inline icons.
  //
  // The dialog surface only owns 5 of the 12 canonical row actions
  // (openFile, openRecord, email, copyLink, toggleWorkspace). Until task
  // 044 (FR-DOC-03 dialog restructure) introduces handlers for the rest,
  // we hide unsupported actions via `disabledActions`. The remaining 5
  // are wired to the existing dialog handlers — no orphaned affordances.
  // -------------------------------------------------------------------------

  const target = React.useMemo<IDocumentRowMenuTarget>(
    () => ({
      id: documentId,
      name: documentName,
      documentType,
    }),
    [documentId, documentName, documentType]
  );

  const handleRowAction = React.useCallback(
    (action: DocumentRowAction) => {
      switch (action) {
        case 'openFile':
          onOpenFile('desktop');
          return;
        case 'openRecord':
          onOpenRecord();
          return;
        case 'email':
          onEmailDocument();
          return;
        case 'copyLink':
          onCopyLink();
          return;
        case 'toggleWorkspace':
          onToggleWorkspace?.();
          return;
        // The following actions are not reachable from the dialog surface
        // today; they are hidden by `disabledActions` below. Including the
        // cases keeps the exhaustive `never` check happy.
        case 'preview':
        case 'aiSummary':
        case 'findSimilar':
        case 'download':
        case 'pinToTop':
        case 'rename':
        case 'delete':
          return;
        default: {
          const _never: never = action;
          void _never;
          return;
        }
      }
    },
    [onOpenFile, onOpenRecord, onEmailDocument, onCopyLink, onToggleWorkspace]
  );

  // Hide actions the dialog cannot service today. Including `toggleWorkspace`
  // in the hidden set when no callback was provided keeps the menu honest.
  const dialogDisabledActions = React.useMemo<DocumentRowAction[]>(() => {
    const hidden: DocumentRowAction[] = [
      'preview',
      'aiSummary',
      'findSimilar',
      'download',
      'pinToTop',
      'rename',
      'delete',
    ];
    if (!onToggleWorkspace) {
      hidden.push('toggleWorkspace');
    }
    return hidden;
  }, [onToggleWorkspace]);

  return (
    <Dialog
      open={open}
      onOpenChange={(_, data) => {
        if (!data.open) onClose();
      }}
    >
      <DialogSurface className={styles.surface}>
        {/* Title bar — 3-dot menu replaces the inline action Toolbar */}
        <div className={styles.titleBar}>
          <DialogTitle action={null} className={styles.titleText}>
            {documentName || 'Document Preview'}
          </DialogTitle>
          <div
            className={styles.titleActions}
            aria-label={
              isInWorkspace ? 'Document actions (in workspace)' : 'Document actions'
            }
          >
            <DocumentRowMenu
              document={target}
              onAction={handleRowAction}
              disabledActions={dialogDisabledActions}
            />
            <Tooltip content="Close" relationship="label">
              <Button
                appearance="subtle"
                icon={<Dismiss24Regular />}
                aria-label="Close"
                onClick={onClose}
              />
            </Tooltip>
          </div>
        </div>

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
  );
};

FilePreviewDialog.displayName = 'FilePreviewDialog';
