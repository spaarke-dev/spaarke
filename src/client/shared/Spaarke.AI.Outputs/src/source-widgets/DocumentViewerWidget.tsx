/**
 * DocumentViewerWidget
 *
 * Renders a SharePoint Embedded document preview using a pre-resolved URL.
 * The widget receives a fully-resolved document URL — it does NOT call the BFF
 * API or Graph SDK directly (ADR-007: no Graph SDK types leak above the facade).
 * The calling code page is responsible for resolving the URL via the BFF, which
 * uses SpeFileStore internally.
 *
 * Supported formats:
 * - PDFs: rendered via <object> element for best browser support.
 * - All other types: rendered via <iframe>.
 *
 * NOT PCF-safe — React 19.
 */

import React, { useCallback } from 'react';
import { makeStyles, tokens, Button, Text, mergeClasses } from '@fluentui/react-components';
import { ArrowDownloadRegular, DocumentRegular } from '@fluentui/react-icons';
import type { SourceWidgetProps } from '../types/widget-types';

// ---------------------------------------------------------------------------
// Payload type
// ---------------------------------------------------------------------------

export interface DocumentViewerData {
  /** Pre-resolved document URL (from BFF / SpeFileStore). */
  documentUrl: string;
  /** Display name of the document. */
  fileName: string;
  /** MIME type — used to pick iframe vs. object rendering. */
  mimeType?: string;
  /** Whether to show the download button. Defaults to false. */
  canDownload?: boolean;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
  },
  toolbar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke1,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
  },
  fileName: {
    fontWeight: tokens.fontWeightSemibold,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    flexGrow: 1,
    marginRight: tokens.spacingHorizontalM,
  },
  viewerContainer: {
    flexGrow: 1,
    overflow: 'hidden',
    position: 'relative',
  },
  iframe: {
    width: '100%',
    height: '100%',
    border: 'none',
    display: 'block',
  },
  objectEl: {
    width: '100%',
    height: '100%',
    display: 'block',
  },
  fallback: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },
  errorText: {
    color: tokens.colorPaletteCranberryForeground2,
    paddingTop: tokens.spacingVerticalM,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

function DocumentViewerWidget(props: SourceWidgetProps<DocumentViewerData>) {
  const { data, isLoading, error, className } = props;
  const styles = useStyles();

  const isPdf = data?.mimeType === 'application/pdf';

  const handleDownload = useCallback(() => {
    if (!data?.documentUrl) return;
    const link = document.createElement('a');
    link.href = data.documentUrl;
    link.download = data.fileName ?? 'document';
    link.click();
  }, [data?.documentUrl, data?.fileName]);

  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.fallback}>
          <DocumentRegular fontSize={40} />
          <Text>Loading document…</Text>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.fallback}>
          <DocumentRegular fontSize={40} />
          <Text className={styles.errorText}>{error}</Text>
        </div>
      </div>
    );
  }

  return (
    <div className={mergeClasses(styles.root, className)}>
      <div className={styles.toolbar}>
        <Text className={styles.fileName} title={data?.fileName}>
          {data?.fileName ?? 'Document'}
        </Text>
        {data?.canDownload && (
          <Button
            appearance="subtle"
            icon={<ArrowDownloadRegular />}
            onClick={handleDownload}
            aria-label={`Download ${data?.fileName ?? 'document'}`}
          >
            Download
          </Button>
        )}
      </div>

      <div className={styles.viewerContainer}>
        {isPdf ? (
          <object
            className={styles.objectEl}
            data={data?.documentUrl}
            type="application/pdf"
            aria-label={data?.fileName ?? 'PDF document'}
          >
            {/* Fallback for browsers that cannot render PDF inline */}
            <div className={styles.fallback}>
              <DocumentRegular fontSize={40} />
              <Text>
                PDF preview unavailable.{' '}
                {data?.canDownload && (
                  <Button appearance="transparent" onClick={handleDownload}>
                    Download to view.
                  </Button>
                )}
              </Text>
            </div>
          </object>
        ) : (
          <iframe
            className={styles.iframe}
            src={data?.documentUrl}
            title={data?.fileName ?? 'Document preview'}
            sandbox="allow-scripts allow-same-origin allow-forms"
          />
        )}
      </div>
    </div>
  );
}

export default DocumentViewerWidget;
