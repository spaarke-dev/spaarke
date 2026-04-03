/**
 * SourceViewerPanel Component
 *
 * Center panel of the AnalysisWorkspace layout. Displays the original source
 * document (PDF, Word, etc.) for reference during analysis review. The panel
 * is collapsible via a button in its header — hiding it expands the editor panel.
 *
 * The "Open" button opens the document in a full FilePreviewDialog (shared
 * component) rather than navigating to a new tab.
 *
 * Task 065: Replaced PH-061-B placeholder with real document viewer using
 * BFF API preview URL. Supports PDF (direct iframe) and Office docs (Office
 * Online embed URL).
 *
 * @see ADR-007 - Document access through BFF API (SpeFileStore facade)
 * @see ADR-021 - Fluent UI v9 design system
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { makeStyles, Button, Spinner, Text, tokens } from '@fluentui/react-components';
import {
  DocumentRegular,
  ChevronRightRegular,
  ArrowClockwiseRegular,
  OpenRegular,
  ErrorCircle20Regular,
} from '@fluentui/react-icons';
import { FilePreviewDialog } from '@spaarke/ui-components';
import type { IFilePreviewServices } from '@spaarke/ui-components';
import type { DocumentMetadata, AnalysisError } from '../types';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface SourceViewerPanelProps {
  /** Callback to hide the source panel (editor will expand) */
  onCollapse: () => void;
  /** Document metadata loaded from BFF API (null while loading or unavailable) */
  documentMetadata?: DocumentMetadata | null;
  /** Whether the document is currently loading */
  isLoading?: boolean;
  /** Error from document loading */
  documentError?: AnalysisError | null;
  /** Retry loading the document */
  onRetry?: () => void;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Timeout for iframe loading (milliseconds) */
const IFRAME_LOAD_TIMEOUT_MS = 15_000;

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground3,
    minHeight: '40px',
    flexShrink: 0,
  },
  headerTitle: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    overflow: 'hidden',
  },
  headerActions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
  docName: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    maxWidth: '200px',
  },
  viewerContent: {
    flex: 1,
    overflow: 'hidden',
    position: 'relative',
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },
  emptyIcon: {
    fontSize: '48px',
  },
  loadingState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    gap: tokens.spacingVerticalM,
  },
  errorState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalL,
    textAlign: 'center',
  },
  errorIcon: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: '48px',
  },
  iframe: {
    width: '100%',
    height: '100%',
    border: 'none',
  },
  iframeLoading: {
    position: 'absolute',
    top: '50%',
    left: '50%',
    transform: 'translate(-50%, -50%)',
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function SourceViewerPanel({
  onCollapse,
  documentMetadata = null,
  isLoading = false,
  documentError = null,
  onRetry,
}: SourceViewerPanelProps): JSX.Element {
  const styles = useStyles();

  const [isIframeLoading, setIsIframeLoading] = useState(false);
  const [iframeError, setIframeError] = useState<string | null>(null);
  const [isPreviewOpen, setIsPreviewOpen] = useState(false);
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Clean up iframe timeout on unmount
  useEffect(() => {
    return () => {
      if (timeoutRef.current) {
        clearTimeout(timeoutRef.current);
      }
    };
  }, []);

  // Start iframe loading when document metadata arrives with a viewUrl
  useEffect(() => {
    if (documentMetadata?.viewUrl) {
      setIsIframeLoading(true);
      setIframeError(null);

      // Set a timeout for iframe loading
      timeoutRef.current = setTimeout(() => {
        setIsIframeLoading(false);
        setIframeError('Document preview timed out. Please try again.');
      }, IFRAME_LOAD_TIMEOUT_MS);
    }
  }, [documentMetadata?.viewUrl]);

  const handleIframeLoad = useCallback(() => {
    if (timeoutRef.current) {
      clearTimeout(timeoutRef.current);
      timeoutRef.current = null;
    }
    setIsIframeLoading(false);
  }, []);

  const handleIframeError = useCallback(() => {
    if (timeoutRef.current) {
      clearTimeout(timeoutRef.current);
      timeoutRef.current = null;
    }
    setIsIframeLoading(false);
    setIframeError('Failed to load document preview.');
  }, []);

  const handleRefresh = useCallback(() => {
    setIframeError(null);
    onRetry?.();
  }, [onRetry]);

  const handleOpenPreview = useCallback(() => {
    setIsPreviewOpen(true);
  }, []);

  const handleClosePreview = useCallback(() => {
    setIsPreviewOpen(false);
  }, []);

  // Build a minimal IFilePreviewServices adapter using the already-loaded viewUrl.
  // The panel does not have BFF access for open links or Xrm for navigation — those
  // are stubbed with no-ops so FilePreviewDialog can still render the iframe preview.
  const previewServices = useMemo<IFilePreviewServices>(
    () => ({
      getDocumentPreviewUrl: async (_documentId: string) =>
        documentMetadata?.viewUrl ?? null,
      getDocumentOpenLinks: async (_documentId: string) => null,
      navigateToEntity: (_params: { action: 'openRecord'; entityName: string; entityId: string; openInNewWindow?: boolean }) => { /* not available in AnalysisWorkspace context */ },
      copyDocumentLink: async (_documentId: string) => false,
      setWorkspaceFlag: async (_documentId: string, _flag: boolean) => false,
    }),
    [documentMetadata?.viewUrl]
  );

  // ---- Determine viewer content ----
  let viewerContent: JSX.Element;

  if (isLoading) {
    viewerContent = (
      <div className={styles.loadingState}>
        <Spinner size="medium" label="Loading document..." />
      </div>
    );
  } else if (documentError) {
    viewerContent = (
      <div className={styles.errorState}>
        <ErrorCircle20Regular className={styles.errorIcon} />
        <Text weight="semibold">Failed to load document</Text>
        <Text size={200}>{documentError.message}</Text>
        {onRetry && (
          <Button appearance="outline" onClick={handleRefresh}>
            Retry
          </Button>
        )}
      </div>
    );
  } else if (iframeError) {
    viewerContent = (
      <div className={styles.errorState}>
        <ErrorCircle20Regular className={styles.errorIcon} />
        <Text weight="semibold">Preview Error</Text>
        <Text size={200}>{iframeError}</Text>
        <Button appearance="outline" onClick={handleRefresh}>
          Retry
        </Button>
      </div>
    );
  } else if (documentMetadata?.viewUrl) {
    viewerContent = (
      <>
        {isIframeLoading && (
          <div className={styles.iframeLoading}>
            <Spinner size="medium" label="Rendering document..." />
          </div>
        )}
        <iframe
          className={styles.iframe}
          style={{ visibility: isIframeLoading ? 'hidden' : 'visible' }}
          src={documentMetadata.viewUrl}
          title={`Document Preview: ${documentMetadata.name}`}
          sandbox="allow-same-origin allow-scripts allow-forms allow-popups allow-popups-to-escape-sandbox"
          onLoad={handleIframeLoad}
          onError={handleIframeError}
        />
      </>
    );
  } else {
    viewerContent = (
      <div className={styles.emptyState}>
        <DocumentRegular className={styles.emptyIcon} />
        <Text weight="semibold">No document loaded</Text>
        <Text size={200}>
          The source document viewer will display the original document for reference during analysis.
        </Text>
      </div>
    );
  }

  return (
    <div className={styles.root}>
      {/* Panel header */}
      <div className={styles.header}>
        <div className={styles.headerTitle}>
          <Text weight="semibold">SOURCE DOCUMENT</Text>
          {documentMetadata?.name && (
            <Text size={200} className={styles.docName} title={documentMetadata.name}>
              {documentMetadata.name}
            </Text>
          )}
        </div>
        <div className={styles.headerActions}>
          {documentMetadata?.viewUrl && (
            <>
              <Button
                icon={<ArrowClockwiseRegular />}
                appearance="subtle"
                size="small"
                onClick={handleRefresh}
                title="Refresh"
                aria-label="Refresh document"
              />
              <Button
                icon={<OpenRegular />}
                appearance="subtle"
                size="small"
                onClick={handleOpenPreview}
                title="Open in preview dialog"
                aria-label="Open document in preview dialog"
              />
            </>
          )}
          <Button
            icon={<ChevronRightRegular />}
            appearance="subtle"
            size="small"
            onClick={onCollapse}
            title="Hide source viewer"
            aria-label="Hide source viewer"
          />
        </div>
      </div>

      {/* Viewer content area */}
      <div className={styles.viewerContent}>{viewerContent}</div>

      {/* FilePreviewDialog — opened by the Open button in the header */}
      {documentMetadata && (
        <FilePreviewDialog
          open={isPreviewOpen}
          documentId={documentMetadata.id}
          documentName={documentMetadata.name}
          onClose={handleClosePreview}
          services={previewServices}
        />
      )}
    </div>
  );
}
