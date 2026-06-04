/**
 * @spaarke/ai-widgets — DocumentViewerWidget
 *
 * Workspace widget that renders a rich document preview using the shared
 * `RichFilePreview` renderer from `@spaarke/ui-components`. This widget is
 * the Workspace destination for Assistant → Workspace `widget_load` dispatch
 * (R4 FR-02 / OC-R4-07).
 *
 * Upgrade history:
 *   - R4 task 042 (W-4): shipped as a SHIM viewer rendering file metadata +
 *     a monospace text preview of already-extracted content. The shim's
 *     purpose was to validate the end-to-end widget_load pipeline before
 *     real preview UX existed.
 *   - R5 task 013 (D2-08): extracted the canonical preview renderer
 *     (`RichFilePreview`) from `RichFilePreviewDialog` into
 *     `@spaarke/ui-components` so non-modal consumers can mount it without
 *     the modal Dialog envelope.
 *   - R5 task 022 (D2-08 follow-on, THIS CHANGE): replaces the R4 monospace
 *     shim with consumption of the extracted `RichFilePreview` renderer.
 *     Per R5 CLAUDE.md §3.1 (reuse mandate) the renderer is CONSUMED, not
 *     rebuilt; no parallel preview component was authored.
 *
 * Back-compat surface (R5 task 022):
 *   - The widget registration (`DOCUMENT_VIEWER_WIDGET_TYPE = 'document-viewer'`,
 *     displayName, category, icon, allowMultiple, defaultOrder) is UNCHANGED
 *     in `register-document-viewer-widget.ts` so every R4 `workspace.widget_load`
 *     dispatch site continues to resolve to this widget without modification.
 *   - `DocumentViewerWidgetData` retains its R4 fields (`filename`,
 *     `contentType`, `sizeBytes?`, `textContent`) as accepted (still part of
 *     the type so R4 dispatchers compile unchanged); new R5 fields
 *     (`documentId?`, `documentType?`, `createdBy?`, `createdAt?`,
 *     `previewUrl?`, `fetchPreviewUrl?`) are ADDITIVE + OPTIONAL.
 *   - When no preview URL source is supplied (R4-style text-only payloads),
 *     the widget passes `fetchPreviewUrl: async () => null` and the renderer
 *     renders its existing empty/error state. R4 dispatch sites mount the
 *     renderer instead of the prior monospace `<pre>`, but DO NOT crash.
 *
 * 3-dot menu defaults: the renderer's `DEFAULT_RICH_FILE_PREVIEW_DISABLED_ACTIONS`
 * applies — `preview` (the renderer IS the preview), `aiSummary`,
 * `toggleWorkspace` (the renderer IS the workspace surface for this document),
 * `rename`. `findSimilar` is auto-hidden when no `onFindSimilar` callback is
 * supplied. Workspace dispatch sites currently do not pass `onFindSimilar`.
 *
 * ADR compliance:
 *   - ADR-006: no PCF control change.
 *   - ADR-012: widget lives in `@spaarke/ai-widgets`; imports renderer from
 *     `@spaarke/ui-components` via the package boundary (NOT relative path).
 *   - ADR-021: Fluent v9 semantic tokens only — no hex / rgba / Fluent v8.
 *     The renderer itself enforces semantic-token usage for its subtree;
 *     the widget wrapper introduces no hard-coded colors.
 *   - ADR-022: React 19, functional component + hooks only.
 *   - ADR-028: no token snapshots; the widget itself makes no BFF call.
 *     Future `fetchPreviewUrl` closures (supplied by R5 tasks 020 / 021
 *     dispatch sites) are responsible for fresh-token retrieval.
 *   - ADR-030: typed `widgetData` shape, no `any` casts; no new event-bus
 *     channels or event types added by this widget (consumer only).
 *
 * React 19, NOT PCF-safe.
 */

import * as React from 'react';
import { makeStyles, mergeClasses, tokens, Spinner, Text } from '@fluentui/react-components';
import { RichFilePreview, type IRichFilePreviewProps } from '@spaarke/ui-components';
import type { WorkspaceWidgetProps } from '../../types/widget-types';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * Typed `widgetData` payload consumed by DocumentViewerWidget.
 *
 * R4 fields (back-compat — required for dispatch sites that already build
 * this payload via `useChatFileAttachment` → ConversationPane →
 * `workspace.widget_load`):
 *   - `filename`, `contentType` — used for display name + 3-dot menu target
 *   - `sizeBytes?` — surfaced in the renderer's Details section when present
 *   - `textContent` — RETAINED for back-compat. Not rendered by the upgraded
 *     widget (the renderer iframes the original file via `fetchPreviewUrl`);
 *     existing dispatchers compile unchanged because the field is still part
 *     of the type.
 *
 * R5 additions (ADDITIVE + OPTIONAL — populated by future dispatch sites in
 * tasks 020 / 021 / R6 SharePoint Embedded integration):
 *   - `documentId?` — stable identifier; required by the renderer's 3-dot
 *     menu aria-label. When omitted, the widget synthesizes a deterministic
 *     id from the filename so the renderer still mounts safely.
 *   - `documentType?` — drives the Tag chip in the metadata pane.
 *   - `createdBy?` — populates the "Created by" row in the Details section.
 *   - `createdAt?` — populates the "Created" row in the Details section.
 *   - `previewUrl?` — convenience field for static-URL dispatch sites. The
 *     widget converts this into a `fetchPreviewUrl` closure internally.
 *   - `fetchPreviewUrl?` — async closure that resolves to a preview URL or
 *     null. Takes precedence over `previewUrl` when both are supplied. R5
 *     dispatch sites that need fresh-token retrieval (per ADR-028) own this
 *     closure; the widget never snapshots tokens.
 */
export interface DocumentViewerWidgetData {
  // R4 fields (back-compat, unchanged)
  filename: string;
  contentType: string;
  sizeBytes?: number;
  textContent: string;
  // R5 additions (all optional)
  documentId?: string;
  documentType?: string;
  createdBy?: string | null;
  createdAt?: string | null;
  previewUrl?: string | null;
  fetchPreviewUrl?: () => Promise<string | null>;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  envelopeMessage: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalXL,
    textAlign: 'center',
    color: tokens.colorNeutralForeground3,
  },
  envelopeError: {
    color: tokens.colorPaletteRedForeground1,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Defensive type guard for the widget payload. Accepts the R4 minimum
 * required fields. New R5 optional fields are NOT required by the guard;
 * they pass through unchecked here and are normalized in
 * `mapPayloadToRendererProps`.
 */
function isDocumentViewerData(value: unknown): value is DocumentViewerWidgetData {
  if (value === null || typeof value !== 'object') return false;
  const obj = value as Record<string, unknown>;
  return (
    typeof obj.filename === 'string' &&
    typeof obj.contentType === 'string' &&
    typeof obj.textContent === 'string'
  );
}

/**
 * Resolve the `fetchPreviewUrl` closure for the renderer. Precedence:
 *   1. `data.fetchPreviewUrl` (caller-provided closure — full control over
 *      async/auth behavior; ADR-028 fresh-token retrieval lives here).
 *   2. `data.previewUrl` (static URL — wrap in an async resolver).
 *   3. Fallback: async resolver returning null. The renderer's existing
 *      error/retry path handles this case gracefully (per task 013
 *      acceptance criteria), so R4 text-only dispatch sites continue to
 *      mount safely without crashing.
 */
function resolveFetchPreviewUrl(data: DocumentViewerWidgetData): () => Promise<string | null> {
  if (typeof data.fetchPreviewUrl === 'function') {
    return data.fetchPreviewUrl;
  }
  if (typeof data.previewUrl === 'string' && data.previewUrl.length > 0) {
    const url = data.previewUrl;
    return async () => url;
  }
  return async () => null;
}

/**
 * Synthesize a stable document id when the payload does not provide one.
 * The renderer's 3-dot menu aria-label requires a non-empty id; falling
 * back to the filename keeps R4 text-only payloads renderable without
 * crashing.
 */
function resolveDocumentId(data: DocumentViewerWidgetData): string {
  if (typeof data.documentId === 'string' && data.documentId.length > 0) {
    return data.documentId;
  }
  if (data.filename.length > 0) {
    return `document-viewer:${data.filename}`;
  }
  return 'document-viewer:unknown';
}

/**
 * Map the widget's typed payload onto the renderer's `IRichFilePreviewProps`.
 * Pure function; no React hooks (called from inside `useMemo`).
 *
 * Default action callbacks log a dev-time `console.warn` so future R5
 * dispatch sites (tasks 020 / 021) know to supply real callbacks. They DO
 * NOT throw — the widget is a back-compat surface and must not regress R4
 * dispatch behavior.
 */
function mapPayloadToRendererProps(data: DocumentViewerWidgetData): IRichFilePreviewProps {
  const documentId = resolveDocumentId(data);
  const fetchPreviewUrl = resolveFetchPreviewUrl(data);

  const warnUnwired = (action: string): void => {
    // Dev-only signal. Production builds suppress this via console
    // re-routing; tests assert against the no-op behavior, not the warning.
    if (typeof console !== 'undefined' && typeof console.warn === 'function') {
      console.warn(
        `[DocumentViewerWidget] No '${action}' handler supplied by dispatch site (documentId=${documentId}). ` +
          `R5 dispatch sites (tasks 020 / 021) should wire real callbacks; defaulting to no-op.`
      );
    }
  };

  return {
    documentName: data.filename,
    documentId,
    documentType: data.documentType,
    createdBy: data.createdBy ?? null,
    createdAt: data.createdAt ?? null,
    fileSize: typeof data.sizeBytes === 'number' ? data.sizeBytes : null,
    fetchPreviewUrl,
    onOpenFile: () => warnUnwired('onOpenFile'),
    onOpenRecord: () => warnUnwired('onOpenRecord'),
    onEmailDocument: () => warnUnwired('onEmailDocument'),
    onCopyLink: () => warnUnwired('onCopyLink'),
    // Intentionally omitted: onFetchSummary, onToggleWorkspace, onFindSimilar.
    // The renderer auto-hides menu items whose callbacks are absent (see
    // DEFAULT_RICH_FILE_PREVIEW_DISABLED_ACTIONS + `findSimilar` auto-hide).
  };
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * DocumentViewerWidget — workspace tab that mounts the canonical
 * `RichFilePreview` renderer for an in-chat file attachment.
 *
 * The widget is a thin `WorkspaceWidgetProps<DocumentViewerWidgetData>`
 * consumer: it defensively narrows the payload, maps it onto the renderer's
 * `IRichFilePreviewProps`, and mounts `<RichFilePreview />` inside the
 * widget's `className` / `data-widget-type` / `data-testid` envelope.
 *
 * Loading / error envelope: when the widget's host (the Workspace pane)
 * passes `isLoading` or `error` props (the standard `WorkspaceWidgetProps`
 * envelope), the widget surfaces those before mounting the renderer. The
 * renderer has its own internal loading/error states for the preview URL
 * fetch; the envelope props are for the surrounding host (e.g. while the
 * dispatch payload itself is being resolved).
 */
const DocumentViewerWidget: React.FC<WorkspaceWidgetProps<DocumentViewerWidgetData>> = ({
  data,
  widgetType,
  isLoading,
  error,
  className,
}) => {
  const styles = useStyles();

  // Defensive narrowing at the widget boundary. When narrowing fails we
  // still mount the renderer with a synthesized "Unknown file" name + a
  // null-resolving fetch — the renderer's empty state surfaces and the
  // widget does not crash.
  const isValid = isDocumentViewerData(data);

  const rendererProps = React.useMemo<IRichFilePreviewProps>(() => {
    if (isValid) {
      return mapPayloadToRendererProps(data);
    }
    return {
      documentName: 'Unknown file',
      documentId: 'document-viewer:unknown',
      fetchPreviewUrl: async () => null,
      onOpenFile: () => undefined,
      onOpenRecord: () => undefined,
      onEmailDocument: () => undefined,
      onCopyLink: () => undefined,
    };
  }, [isValid, data]);

  return (
    <div
      className={mergeClasses(styles.root, className)}
      data-widget-type={widgetType}
      data-testid="document-viewer-widget"
    >
      {isLoading ? (
        <div className={styles.envelopeMessage}>
          <Spinner size="medium" label="Loading preview…" labelPosition="below" />
        </div>
      ) : error ? (
        <div className={mergeClasses(styles.envelopeMessage, styles.envelopeError)}>
          <Text>{error}</Text>
        </div>
      ) : (
        <RichFilePreview {...rendererProps} />
      )}
    </div>
  );
};

DocumentViewerWidget.displayName = 'DocumentViewerWidget';

export default DocumentViewerWidget;
