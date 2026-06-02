/**
 * @spaarke/ai-widgets — DocumentViewerWidget
 *
 * Minimal workspace widget that renders a preview of an in-memory chat
 * attachment (file uploaded by the user in the Assistant pane). Created in
 * R4 task 042 (W-4) as the receiving widget for the first end-to-end
 * Assistant → Workspace `widget_load` demo per FR-02 / OC-R4-07.
 *
 * Demo scope (Risk R-7 in plan.original.md §8):
 *   This is intentionally a SHIM viewer that shows file metadata + a text
 *   preview (already extracted client-side by `useChatFileAttachment`). It
 *   does NOT render the original PDF binary, fetch from SPE, or invoke any
 *   BFF endpoint — text extraction has already happened upstream and lives
 *   in memory only (OC-02). Broader viewer fidelity (PDF.js page render,
 *   image preview, RecordViewer integration) is OUT OF SCOPE per the task
 *   scope guard.
 *
 * Pattern reference: Calendar widget Pattern D (R3 task 115). This file is
 * the SHARED-LIB widget; mount happens via the registry (see
 * `register-document-viewer-widget.ts`), so no consumer-side import is
 * required.
 *
 * ADR compliance:
 *   - ADR-012: lives in `@spaarke/ai-widgets`; context-agnostic
 *   - ADR-021: Fluent v9 semantic tokens only — no hex / rgba / Fluent v8
 *   - ADR-022: React 19, functional component + hooks only
 *   - ADR-028: no token snapshots; no BFF call in v1 (text already extracted)
 *   - ADR-030: typed `widgetData` shape, no `any` casts
 *
 * React 19, NOT PCF-safe.
 */

import * as React from 'react';
import { makeStyles, mergeClasses, tokens, Card, CardHeader, Text, Badge } from '@fluentui/react-components';
import { DocumentRegular, DocumentPdfRegular } from '@fluentui/react-icons';
import type { WorkspaceWidgetProps } from '../../types/widget-types';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * Typed `widgetData` payload consumed by DocumentViewerWidget.
 *
 * Dispatched by the Assistant pane (R4 task 042 / W-4) when the user attaches
 * a file in the chat. ConversationPane constructs this payload from the
 * `useChatFileAttachment` hook's `attachments` result and embeds it inside
 * a `WorkspaceWidgetLoadEvent` payload on the `workspace` channel.
 *
 * Future extensions (out of scope for task 042 — defer to follow-up):
 *   - Re-fetch original binary from a session-scoped BFF endpoint for true
 *     PDF rendering (would require ADR-028 `authenticatedFetch`).
 *   - Stream chunked text from BFF instead of inlining the full preview.
 *   - Page navigation for paginated documents.
 */
export interface DocumentViewerWidgetData {
  /** Original filename (e.g. "Contract.pdf"). */
  filename: string;
  /** Original MIME type (e.g. "application/pdf"). */
  contentType: string;
  /** Original file size in bytes — informational only. */
  sizeBytes?: number;
  /**
   * Extracted text content (already produced client-side by
   * `useChatFileAttachment`). Empty string is valid for unsupported MIME
   * types. NEVER includes the original binary.
   *
   * NOTE: This MAY be large (PDFs cap at ~200 pages → potentially MBs of
   * text). For the v1 demo we render the first ~50 KB inline; further
   * paging is out of scope.
   */
  textContent: string;
}

/** Maximum number of characters rendered inline in the v1 viewer (50 KB). */
const MAX_INLINE_PREVIEW_CHARS = 50_000;

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  card: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    minHeight: 0,
  },
  headerIcon: {
    color: tokens.colorBrandForeground1,
    fontSize: tokens.fontSizeBase600,
    flexShrink: 0,
  },
  headerTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  headerSubtitle: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  metadataRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexShrink: 0,
  },
  previewContainer: {
    flex: 1,
    overflow: 'auto',
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    padding: tokens.spacingHorizontalM,
    minHeight: 0,
  },
  previewText: {
    margin: 0,
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground1,
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
  },
  emptyState: {
    flex: 1,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    color: tokens.colorNeutralForeground3,
    padding: tokens.spacingHorizontalXL,
    textAlign: 'center',
  },
  truncationNotice: {
    flexShrink: 0,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorPaletteYellowForeground2,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Convert raw byte count to a short human-readable string (e.g. "1.5 MB"). */
function formatFileSize(bytes?: number): string | null {
  if (typeof bytes !== 'number' || !Number.isFinite(bytes) || bytes < 0) return null;
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB'];
  let size = bytes / 1024;
  let i = 0;
  while (size >= 1024 && i < units.length - 1) {
    size /= 1024;
    i += 1;
  }
  return `${size.toFixed(1)} ${units[i]}`;
}

/** Narrow PDF MIME — drives the icon selection. */
function isPdf(contentType: string): boolean {
  return contentType === 'application/pdf';
}

/** Type guard for the widget payload — defensive narrowing at the boundary. */
function isDocumentViewerData(value: unknown): value is DocumentViewerWidgetData {
  if (value === null || typeof value !== 'object') return false;
  const obj = value as Record<string, unknown>;
  return typeof obj.filename === 'string' && typeof obj.contentType === 'string' && typeof obj.textContent === 'string';
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * DocumentViewerWidget — workspace tab for an in-chat file attachment preview.
 *
 * Reads pre-extracted text from `data.textContent` (produced client-side by
 * `useChatFileAttachment` in SprkChat) and renders it as a scrollable
 * monospace preview. Shows file metadata (name, type, size) in the header.
 *
 * No BFF calls in v1 — the text content is already in memory before this
 * widget mounts.
 */
const DocumentViewerWidget: React.FC<WorkspaceWidgetProps<DocumentViewerWidgetData>> = ({
  data,
  widgetType,
  isLoading,
  error,
  className,
}) => {
  const styles = useStyles();

  // Defensive: subscribers may pass `unknown` payloads through; narrow here.
  const isValid = isDocumentViewerData(data);

  const filename = isValid ? data.filename : 'Unknown file';
  const contentType = isValid ? data.contentType : '';
  const sizeBytes = isValid ? data.sizeBytes : undefined;
  const textContent = isValid ? data.textContent : '';

  const sizeLabel = formatFileSize(sizeBytes);
  const headerIcon = isPdf(contentType) ? (
    <DocumentPdfRegular className={styles.headerIcon} />
  ) : (
    <DocumentRegular className={styles.headerIcon} />
  );

  // Truncate inline preview to stay under MAX_INLINE_PREVIEW_CHARS — keeps
  // the workspace tab snappy even for ~200-page PDFs.
  const isTruncated = textContent.length > MAX_INLINE_PREVIEW_CHARS;
  const displayedText = isTruncated
    ? `${textContent.slice(0, MAX_INLINE_PREVIEW_CHARS)}\n\n[Preview truncated — first ${MAX_INLINE_PREVIEW_CHARS.toLocaleString()} characters shown]`
    : textContent;

  return (
    <div
      className={mergeClasses(styles.root, className)}
      data-widget-type={widgetType}
      data-testid="document-viewer-widget"
    >
      <Card className={styles.card}>
        <CardHeader
          image={headerIcon}
          header={
            <Text className={styles.headerTitle} title={filename}>
              {filename}
            </Text>
          }
          description={
            <div className={styles.metadataRow}>
              {contentType ? (
                <Badge appearance="tint" size="small">
                  {contentType}
                </Badge>
              ) : null}
              {sizeLabel ? <Text className={styles.headerSubtitle}>{sizeLabel}</Text> : null}
            </div>
          }
        />

        {/* Loading state */}
        {isLoading && (
          <div className={styles.emptyState}>
            <Text>Loading preview…</Text>
          </div>
        )}

        {/* Error state */}
        {error && (
          <div className={styles.emptyState}>
            <Text style={{ color: tokens.colorPaletteRedForeground1 }}>{error}</Text>
          </div>
        )}

        {/* Empty / not-yet-extracted */}
        {!isLoading && !error && textContent.length === 0 && (
          <div className={styles.emptyState}>
            <Text>
              No preview available for this file. The attachment is still in the chat and will be sent with your next
              message.
            </Text>
          </div>
        )}

        {/* Preview */}
        {!isLoading && !error && textContent.length > 0 && (
          <div className={styles.previewContainer} data-testid="document-viewer-preview">
            <pre className={styles.previewText}>{displayedText}</pre>
          </div>
        )}
      </Card>
    </div>
  );
};

export default DocumentViewerWidget;
