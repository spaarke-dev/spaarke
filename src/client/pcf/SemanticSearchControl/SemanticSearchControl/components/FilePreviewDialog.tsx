/**
 * FilePreviewDialog — Modal for document preview.
 *
 * Per FR-DOC-03 (task 044), the dialog uses a 2-column body layout
 * (640 px iframe · 320 px metadata pane) clamped to 960 px max-width.
 * The metadata pane renders three sections top→bottom:
 *   1. AI summary  (sparkle icon + paragraph; rendered when `onFetchSummary`
 *      is provided — falls back to a friendly empty state otherwise)
 *   2. Tags        (single Fluent v9 `Tag` chip from `documentType`)
 *   3. Details     (Created by · Created · Size · Type)
 *
 * v1.1.45 (UAT round 2):
 *   • The 2-column grid is now stable through both loading AND loaded states
 *     (regression fix — the earlier rendering wrapped each cell in
 *     `DialogContent`, whose internal padding/overflow rules collapsed the
 *     grid when the inner content was the iframe). The metadata pane is
 *     ALWAYS visible on the right; the iframe (or its in-cell spinner)
 *     renders strictly inside the left cell.
 *   • The 3-dot menu now hides `toggleWorkspace` from the dialog surface
 *     (the dialog IS already in workspace context — the affordance was
 *     unreachable and confused users).
 *   • Footer simplified to a single `Close` button. The "Find similar"
 *     and "Open file" actions remain reachable from the 3-dot menu.
 *
 * Per FR-DOC-01 (task 040), the title-bar 3-dot menu is `DocumentRowMenu`.
 * Task 044 enables the menu actions the dialog can now service
 * (`download`, `email`, `copyLink`, plus `aiSummary` / `findSimilar` when
 * the corresponding callbacks are wired). `preview` stays hidden because
 * the dialog IS the preview surface; `pinToTop` / `rename` / `delete`
 * stay hidden because no handler exists at the PCF surface yet.
 *
 * Iframe preview pipeline is unchanged from task 040: `fetchPreviewUrl`
 * runs on open, the URL feeds the existing sandboxed iframe.
 *
 * @see ADR-012 - Shared component library (DocumentRowMenu, AiSummaryPopover)
 * @see ADR-021 - Fluent UI v9 (semantic tokens, dark-mode parity)
 * @see ADR-022 - React 16/17 compatible (no React 18-only APIs)
 * @see spec.md FR-DOC-01, FR-DOC-03
 */

import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogActions,
  Button,
  Tooltip,
  Spinner,
  Text,
  Tag,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import {
  Dismiss24Regular,
  Sparkle20Filled,
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
// Types
// ---------------------------------------------------------------------------

/**
 * Shape of the AI-summary payload returned by `onFetchSummary`.
 * Matches the shape used by `AiSummaryPopover` (`ISummaryData`) so callers
 * can reuse the same fetch closure across surfaces.
 */
export interface IFilePreviewDialogSummary {
  summary: string | null;
  tldr: string | null;
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IFilePreviewDialogProps {
  open: boolean;
  documentName: string;
  /** Stable document identifier — required for the 3-dot menu's aria-label. */
  documentId: string;
  /** Optional document type (label, e.g. "Contract"). Drives the Tag chip. */
  documentType?: string;
  /** Optional "Created by" display name for the Details section. */
  createdBy?: string | null;
  /** Optional ISO date string for the Details section "Created" row. */
  createdAt?: string | null;
  /** Optional file size in bytes for the Details section "Size" row. */
  fileSize?: number | null;
  onClose: () => void;
  /** Fetch the preview embed URL. Called when the dialog opens. */
  fetchPreviewUrl: () => Promise<string | null>;
  /**
   * Fetch the AI summary payload. When provided, the AI summary section
   * renders the returned tldr/summary. When omitted, the section shows a
   * friendly empty-state line ("Summary not available for this document.").
   */
  onFetchSummary?: () => Promise<IFilePreviewDialogSummary>;
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
  /**
   * Open the "Find similar" surface for this document. When provided, the
   * Find similar footer button + `findSimilar` menu item are enabled; when
   * omitted, both are hidden.
   */
  onFindSimilar?: () => void;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const THUMBNAIL_COLUMN_WIDTH = '640px';
const METADATA_COLUMN_WIDTH = '320px';
const DIALOG_MAX_WIDTH = '960px';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  surface: {
    // v1.1.45 — pin the surface to exactly DIALOG_MAX_WIDTH so the 2-col
    // grid below always has the room it needs. Earlier `width: '100%'`
    // allowed the surface to shrink inside narrow viewports, which in
    // turn caused the grid to collapse (the user-reported flicker where
    // the metadata pane briefly disappeared on iframe load).
    width: DIALOG_MAX_WIDTH,
    maxWidth: DIALOG_MAX_WIDTH,
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
    borderBottomWidth: tokens.strokeWidthThin,
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
  // 2-column body grid: 640 px thumbnail | 320 px metadata pane.
  // v1.1.45 — rendered as a plain <div> (no DialogBody wrapper) so the
  // grid's column tracks never collapse, regardless of which loading state
  // the iframe is in. DialogBody's own padding/overflow rules previously
  // overrode the grid track widths once the iframe mounted, which is what
  // the user observed as "metadata pane disappears after preview loads".
  body: {
    ...shorthands.padding('0px'),
    flex: 1,
    minHeight: 0,
    display: 'grid',
    // Explicit `auto-flow: column` + `width: 100%` + `gridTemplateRows: 1fr`
    // belt-and-braces the layout: even if a child accidentally stretches its
    // inline-size to the surface width, the grid still allocates exactly two
    // tracks of THUMBNAIL_COLUMN_WIDTH | METADATA_COLUMN_WIDTH respectively.
    gridTemplateColumns: `${THUMBNAIL_COLUMN_WIDTH} ${METADATA_COLUMN_WIDTH}`,
    gridTemplateRows: '1fr',
    gridAutoFlow: 'column' as const,
    width: '100%',
    ...shorthands.overflow('hidden'),
  },
  // Iframe container — fills the left column.
  // `minWidth: 0` is the Grid-collapse fix: without it, a child iframe's
  // intrinsic size can force the cell wider than its track allocation.
  thumbnailCell: {
    position: 'relative' as const,
    minWidth: 0,
    height: '100%',
    ...shorthands.overflow('hidden'),
    borderRightWidth: tokens.strokeWidthThin,
    borderRightStyle: 'solid',
    borderRightColor: tokens.colorNeutralStroke2,
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
    ...shorthands.padding(tokens.spacingHorizontalL),
    textAlign: 'center' as const,
  },
  // Metadata pane — scrolls if content overflows. `minWidth: 0` again to
  // prevent text content from forcing the cell wider than 320 px.
  metadataPane: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    minWidth: 0,
    height: '100%',
    overflowY: 'auto',
    overflowX: 'hidden',
    backgroundColor: tokens.colorNeutralBackground2,
    boxSizing: 'border-box',
  },
  // Section wrapper
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  sectionHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  // AI summary content
  summaryTldr: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  summaryBody: {
    whiteSpace: 'pre-wrap' as const,
    color: tokens.colorNeutralForeground2,
  },
  // Tags chip wrapper
  tagWrap: {
    display: 'flex',
    flexWrap: 'wrap' as const,
    gap: tokens.spacingHorizontalS,
  },
  // Details grid — labels on left, values on right
  detailsGrid: {
    display: 'grid',
    gridTemplateColumns: 'minmax(80px, auto) 1fr',
    columnGap: tokens.spacingHorizontalM,
    rowGap: tokens.spacingVerticalXS,
    alignItems: 'baseline',
  },
  detailsLabel: {
    color: tokens.colorNeutralForeground3,
  },
  detailsValue: {
    color: tokens.colorNeutralForeground1,
    ...shorthands.overflow('hidden'),
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  // Footer action bar
  footer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderTopWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—';
  try {
    const d = new Date(iso);
    if (isNaN(d.getTime())) return '—';
    return d.toLocaleDateString(undefined, {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  } catch {
    return '—';
  }
}

function formatFileSize(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined || isNaN(bytes) || bytes < 0) return '—';
  if (bytes === 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let unitIdx = 0;
  while (value >= 1024 && unitIdx < units.length - 1) {
    value /= 1024;
    unitIdx += 1;
  }
  // 1 decimal for KB+, no decimal for B
  const formatted = unitIdx === 0 ? value.toString() : value.toFixed(1);
  return `${formatted} ${units[unitIdx]}`;
}

function nonEmpty(value: string | null | undefined): string {
  return value && value.trim().length > 0 ? value : '—';
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const FilePreviewDialog: React.FC<IFilePreviewDialogProps> = ({
  open,
  documentName,
  documentId,
  documentType,
  createdBy,
  createdAt,
  fileSize,
  onClose,
  fetchPreviewUrl,
  onFetchSummary,
  onOpenFile,
  onOpenRecord,
  onEmailDocument,
  onCopyLink,
  onToggleWorkspace,
  isInWorkspace,
  onFindSimilar,
}) => {
  const styles = useStyles();

  const [previewUrl, setPreviewUrl] = React.useState<string | null>(null);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState(false);

  // AI summary state — lazily fetched once per dialog open. Reset on close.
  const [summary, setSummary] = React.useState<IFilePreviewDialogSummary | null>(null);
  const [summaryLoading, setSummaryLoading] = React.useState(false);
  const [summaryError, setSummaryError] = React.useState(false);

  // Fetch preview URL when dialog opens — pipeline unchanged from task 040.
  React.useEffect(() => {
    if (!open) {
      setPreviewUrl(null);
      setError(false);
      // Also reset summary state on close so the next open re-fetches.
      setSummary(null);
      setSummaryError(false);
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

  // Fetch AI summary when dialog opens (only if caller provided a fetcher).
  React.useEffect(() => {
    if (!open || !onFetchSummary) return;

    let cancelled = false;
    setSummaryLoading(true);
    setSummaryError(false);

    void onFetchSummary()
      .then(data => {
        if (cancelled) return;
        setSummary(data);
        setSummaryLoading(false);
      })
      .catch(() => {
        if (cancelled) return;
        setSummaryError(true);
        setSummaryLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [open, onFetchSummary]);

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
  // 3-dot menu dispatch — task 040 wired the menu; task 044 enables actions
  // the dialog can now service:
  //   • download  → reuses `onOpenFile('desktop')` (matches ResultCard
  //                 convention — see ResultCard.tsx case 'download':)
  //   • email     → existing handler
  //   • copyLink  → existing handler
  //   • aiSummary → already visible inline; menu item is a no-op when
  //                 `onFetchSummary` isn't provided (hidden via
  //                 `disabledActions` below).
  //   • findSimilar → routed to `onFindSimilar` when provided.
  // The following stay hidden because the dialog surface cannot service
  // them today (no PCF-level handler exists, and `preview` would re-open
  // the dialog the user is already inside):
  //   • preview, pinToTop, rename, delete
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
        case 'download':
          // Mirrors ResultCard.tsx 'download' handling — the existing
          // open-file pipeline already streams the SPE blob and triggers
          // the browser download for file types without a desktop protocol.
          onOpenFile('desktop');
          return;
        case 'findSimilar':
          onFindSimilar?.();
          return;
        case 'aiSummary':
          // The AI summary section is already rendered in the metadata
          // pane (when `onFetchSummary` is provided). No popover to open
          // from inside the dialog — the menu item is hidden via
          // `disabledActions` when no fetcher is available. Otherwise
          // it's still a no-op here because the section is in-view.
          return;
        case 'preview':
        case 'pinToTop':
        case 'rename':
        case 'delete':
          // Not reachable from the dialog surface — hidden via
          // `disabledActions` below. The cases keep the exhaustive
          // `never` check happy.
          return;
        default: {
          const _never: never = action;
          void _never;
          return;
        }
      }
    },
    [
      onOpenFile,
      onOpenRecord,
      onEmailDocument,
      onCopyLink,
      onToggleWorkspace,
      onFindSimilar,
    ]
  );

  // Hide only the actions the dialog cannot service.
  // `preview` is always hidden (dialog IS the preview).
  // `pinToTop` / `rename` / `delete` are hidden until handlers exist at the
  // PCF surface (scoped to follow-on Phase 4 tasks per project plan).
  // `aiSummary` / `findSimilar` are hidden when no callback was provided.
  //
  // v1.1.45 — `toggleWorkspace` is ALWAYS hidden from this dialog's menu.
  // The dialog itself IS the workspace surface for the document (the user
  // has already drilled into a document detail view), so the menu item was
  // visually present but functionally a no-op, which UAT flagged as
  // confusing. Row-context still exposes `toggleWorkspace` via
  // ResultCard.tsx + ListView.tsx — only the dialog hides it.
  const dialogDisabledActions = React.useMemo<DocumentRowAction[]>(() => {
    const hidden: DocumentRowAction[] = [
      'preview',
      'pinToTop',
      'rename',
      'delete',
      'toggleWorkspace',
    ];
    if (!onFetchSummary) hidden.push('aiSummary');
    if (!onFindSimilar) hidden.push('findSimilar');
    return hidden;
  }, [onFetchSummary, onFindSimilar]);

  // -------------------------------------------------------------------------
  // Render helpers
  // -------------------------------------------------------------------------

  const renderPreviewArea = (): React.ReactElement => {
    if (loading) {
      return (
        <div className={styles.centerContent}>
          <Spinner size="large" label="Loading preview..." labelPosition="below" />
        </div>
      );
    }
    if (error) {
      return (
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
      );
    }
    if (previewUrl) {
      return (
        <iframe
          src={previewUrl}
          title={`Preview: ${documentName}`}
          className={styles.iframe}
          sandbox="allow-scripts allow-same-origin allow-forms allow-popups"
        />
      );
    }
    return <div className={styles.centerContent} />;
  };

  const renderSummarySection = (): React.ReactElement => {
    // No fetcher provided — render the empty state without firing any request.
    if (!onFetchSummary) {
      return (
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          Summary not available for this document.
        </Text>
      );
    }
    if (summaryLoading) {
      return <Spinner size="small" label="Loading summary..." labelPosition="after" />;
    }
    if (summaryError) {
      return (
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          Summary not available for this document.
        </Text>
      );
    }
    if (!summary || (!summary.tldr && !summary.summary)) {
      return (
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          No summary available for this document.
        </Text>
      );
    }
    return (
      <>
        {summary.tldr && (
          <Text className={styles.summaryTldr} size={300}>
            {summary.tldr}
          </Text>
        )}
        {summary.summary && (
          <Text className={styles.summaryBody} size={200}>
            {summary.summary}
          </Text>
        )}
      </>
    );
  };

  const renderTagSection = (): React.ReactElement => {
    if (!documentType || documentType.trim().length === 0) {
      return (
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          —
        </Text>
      );
    }
    return (
      <div className={styles.tagWrap}>
        <Tag appearance="filled" shape="rounded" size="small">
          {documentType}
        </Tag>
      </div>
    );
  };

  const renderDetailsSection = (): React.ReactElement => (
    <div className={styles.detailsGrid} role="list" aria-label="Document details">
      <Text size={200} className={styles.detailsLabel} role="listitem">
        Created by
      </Text>
      <Text size={200} className={styles.detailsValue} title={nonEmpty(createdBy)}>
        {nonEmpty(createdBy)}
      </Text>

      <Text size={200} className={styles.detailsLabel} role="listitem">
        Created
      </Text>
      <Text size={200} className={styles.detailsValue}>
        {formatDate(createdAt)}
      </Text>

      <Text size={200} className={styles.detailsLabel} role="listitem">
        Size
      </Text>
      <Text size={200} className={styles.detailsValue}>
        {formatFileSize(fileSize)}
      </Text>

      <Text size={200} className={styles.detailsLabel} role="listitem">
        Type
      </Text>
      <Text size={200} className={styles.detailsValue} title={nonEmpty(documentType)}>
        {nonEmpty(documentType)}
      </Text>
    </div>
  );

  return (
    <Dialog
      open={open}
      onOpenChange={(_, data) => {
        if (!data.open) onClose();
      }}
    >
      <DialogSurface className={styles.surface}>
        {/* Title bar — 3-dot menu replaces the inline action Toolbar (task 040) */}
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

        {/* 2-column body — iframe (left) | metadata pane (right).
            v1.1.45: rendered as a plain <div> instead of <DialogBody> +
            <DialogContent> wrappers; the wrappers' default padding /
            overflow were causing the grid tracks to collapse when the
            iframe mounted (the visible flicker UAT reported). */}
        <div className={styles.body} role="group" aria-label="Document preview body">
          <div className={styles.thumbnailCell}>{renderPreviewArea()}</div>
          <div className={styles.metadataPane}>
            {/* Section 1: AI summary */}
            <section className={styles.section} aria-labelledby="fpd-summary-heading">
              <Text id="fpd-summary-heading" className={styles.sectionHeader} size={300}>
                <Sparkle20Filled aria-hidden="true" />
                AI summary
              </Text>
              {renderSummarySection()}
            </section>

            {/* Section 2: Tags */}
            <section className={styles.section} aria-labelledby="fpd-tags-heading">
              <Text id="fpd-tags-heading" className={styles.sectionHeader} size={300}>
                Tags
              </Text>
              {renderTagSection()}
            </section>

            {/* Section 3: Details */}
            <section className={styles.section} aria-labelledby="fpd-details-heading">
              <Text id="fpd-details-heading" className={styles.sectionHeader} size={300}>
                Details
              </Text>
              {renderDetailsSection()}
            </section>
          </div>
        </div>

        {/* Footer: single Close button (v1.1.45).
            "Find similar" and "Open file" remain reachable via the 3-dot
            menu in the title bar — the redundant footer affordances were
            removed per UAT feedback. */}
        <DialogActions className={styles.footer}>
          <Button appearance="primary" onClick={onClose}>
            Close
          </Button>
        </DialogActions>
      </DialogSurface>
    </Dialog>
  );
};

FilePreviewDialog.displayName = 'FilePreviewDialog';
