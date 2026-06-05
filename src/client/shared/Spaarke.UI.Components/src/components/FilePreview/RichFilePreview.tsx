/**
 * RichFilePreview — Document preview renderer with 2-column layout, prev/next
 * navigation, Tags chip, Details grid, and 3-dot row-action menu integration.
 *
 * This is the EXTRACTED renderer core from `RichFilePreviewDialog.tsx`. It
 * hosts the title-bar (with optional Prev/Next + DocumentRowMenu), the
 * 2-column body grid (iframe + metadata pane), and all preview-lifecycle
 * state + effects (preview-URL fetch, keyboard nav, retry-on-error). It
 * deliberately does NOT include the Fluent v9 modal `Dialog`/`DialogSurface`/
 * `DialogActions` envelope — that wrapper continues to live in
 * `RichFilePreviewDialog.tsx`, which now imports + mounts this renderer
 * inside its modal chrome.
 *
 * Extraction performed by R5 task 013 (D2-08) to unblock:
 *  - Task 018 (D2-09) `FilePreviewContextWidget` — non-modal Context-pane mount
 *  - Task 022 (D2-08 follow-on) `DocumentViewerWidget` upgrade — non-modal
 *    Workspace-pane mount
 *
 * Per R5 CLAUDE.md §3.1 the renderer was EXTRACTED (lines moved, not
 * duplicated). Authoring a parallel preview component is explicitly
 * prohibited.
 *
 * Layout note: this renderer is layout-agnostic — the caller controls the
 * outer container's width/height (the modal dialog clamps to 1280px × 85vh;
 * non-modal hosts pick their own dimensions).
 *
 * @see ADR-012 - Shared component library boundary
 * @see ADR-021 - Fluent UI v9 (semantic tokens, dark-mode parity)
 * @see ADR-022 - React 19 (no React 18-only APIs)
 */

import * as React from 'react';
import {
  Button,
  Divider,
  Tooltip,
  Spinner,
  Text,
  Tag,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import { ChevronLeft20Regular, ChevronRight20Regular } from '@fluentui/react-icons';
import { DocumentRowMenu, type DocumentRowAction, type IDocumentRowMenuTarget } from '../DocumentRowMenu';

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

/**
 * Props for {@link RichFilePreview}.
 *
 * NOTE: This renderer is open/close-agnostic — it does NOT accept `open`
 * or `onClose` props. The modal wrapper `RichFilePreviewDialog` owns those.
 * Non-modal consumers (Context-pane widget, Workspace viewer widget) mount
 * the renderer directly without any open/close concept.
 */
export interface IRichFilePreviewProps {
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
  /** Fetch the preview embed URL. Called on mount / when `documentId` changes. */
  fetchPreviewUrl: () => Promise<string | null>;
  /**
   * Fetch the AI summary payload. When provided, the `aiSummary` menu item
   * is reachable (subject to default `disabledActions`). When omitted, the
   * `aiSummary` menu item is hidden by default.
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
   * `findSimilar` menu item is visible; when omitted, it is hidden.
   */
  onFindSimilar?: () => void;
  /**
   * Navigation set total. When provided alongside `currentIndex` +
   * `onNavigate`, the title bar renders Prev/Next + "N of M". When omitted,
   * the renderer renders without navigation chrome.
   */
  navigationTotal?: number;
  /**
   * 0-based position of the currently-shown document inside the parent's
   * navigation set. Required when `navigationTotal` is supplied.
   */
  currentIndex?: number;
  /**
   * Navigate to a different document inside the parent's navigation set.
   * The renderer resets its iframe-load state automatically when `documentId`
   * changes.
   */
  onNavigate?: (nextIndex: number) => void;
  /**
   * Optional override of the 3-dot menu's hidden actions. When omitted, the
   * default hide-set applies: `preview`, `aiSummary`, `toggleWorkspace`,
   * `rename`, plus `findSimilar` when `onFindSimilar` is not provided.
   * Non-modal consumers (e.g. Context-pane widget) may need to expose
   * `preview` or `toggleWorkspace`; pass an empty array to show all
   * actions, or a tailored array to override per-mount.
   */
  disabledActions?: DocumentRowAction[];
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const METADATA_COLUMN_WIDTH = '320px';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // Top-level wrapper — fills the parent container. Non-modal consumers
  // size their own outer container; the modal dialog wraps this in
  // <DialogSurface> with its own width/height clamp.
  root: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    minHeight: 0,
    width: '100%',
    height: '100%',
    ...shorthands.overflow('hidden'),
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
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  titleActions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
  // Prev/Next nav cluster relocated from footer (DialogActions) to
  // the title bar's right side, just before the 3-dot DocumentRowMenu.
  titleNav: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },
  // Subtle vertical separator between the nav cluster and the menu.
  titleNavDivider: {
    height: '20px',
    marginLeft: tokens.spacingHorizontalXS,
    marginRight: tokens.spacingHorizontalXS,
  },
  titleNavCounter: {
    color: tokens.colorNeutralForeground2,
    paddingLeft: tokens.spacingHorizontalXXS,
    paddingRight: tokens.spacingHorizontalXXS,
    fontVariantNumeric: 'tabular-nums',
  },
  // 2-column body grid: fluid iframe column | 320 px metadata pane.
  body: {
    ...shorthands.padding('0px'),
    flex: 1,
    minHeight: 0,
    display: 'grid',
    gridTemplateColumns: `1fr ${METADATA_COLUMN_WIDTH}`,
    gridTemplateRows: '1fr',
    gridAutoFlow: 'column' as const,
    width: '100%',
    ...shorthands.overflow('hidden'),
  },
  // Iframe container — fills the left column.
  // `minWidth: 0` is the Grid-collapse fix.
  // Visible vertical scrollbar HIDDEN (iframe content renders its own).
  thumbnailCell: {
    position: 'relative' as const,
    minWidth: 0,
    height: '100%',
    overflowY: 'auto',
    overflowX: 'hidden',
    scrollbarWidth: 'none',
    msOverflowStyle: 'none',
    '::-webkit-scrollbar': {
      display: 'none',
    },
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
  // Metadata pane — scrolls if content overflows.
  metadataPane: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXL,
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
  const formatted = unitIdx === 0 ? value.toString() : value.toFixed(1);
  return `${formatted} ${units[unitIdx]}`;
}

function nonEmpty(value: string | null | undefined): string {
  return value && value.trim().length > 0 ? value : '—';
}

// ---------------------------------------------------------------------------
// Default disabled actions — the same set the dialog wrapper applied before
// extraction. Exposed as a constant so non-modal consumers can call out from
// it (e.g. spread it and re-add `preview`).
// ---------------------------------------------------------------------------

/**
 * Default 3-dot menu actions hidden by `RichFilePreview`:
 *  - `preview`         — the renderer IS the preview surface
 *  - `aiSummary`       — hidden across all surfaces (sparkle column is the entry point)
 *  - `toggleWorkspace` — the renderer IS the workspace surface for this document
 *  - `rename`          — hidden across all surfaces
 *
 * Note: `findSimilar` is also auto-hidden by the component when
 * `onFindSimilar` is not provided. To override the entire default set,
 * pass a `disabledActions` prop to the renderer.
 */
export const DEFAULT_RICH_FILE_PREVIEW_DISABLED_ACTIONS: ReadonlyArray<DocumentRowAction> = Object.freeze([
  'preview',
  'aiSummary',
  'toggleWorkspace',
  'rename',
]);

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const RichFilePreview: React.FC<IRichFilePreviewProps> = ({
  documentName,
  documentId,
  documentType,
  createdBy,
  createdAt,
  fileSize,
  fetchPreviewUrl,
  onFetchSummary: _onFetchSummary,
  onOpenFile,
  onOpenRecord,
  onEmailDocument,
  onCopyLink,
  onToggleWorkspace,
  isInWorkspace,
  onFindSimilar,
  navigationTotal,
  currentIndex,
  onNavigate,
  disabledActions: disabledActionsOverride,
}) => {
  const styles = useStyles();

  const [previewUrl, setPreviewUrl] = React.useState<string | null>(null);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState(false);

  // Fetch preview URL when the renderer mounts or `documentId` changes.
  // When the parent navigates (Prev/Next) and swaps `documentId` +
  // `fetchPreviewUrl`, this effect re-fires. We reset `previewUrl` to null
  // at the top so the old iframe doesn't briefly flash while the new one
  // is fetching.
  React.useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(false);
    setPreviewUrl(null);

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
  }, [documentId, fetchPreviewUrl]);

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
  // Prev/Next navigation
  // -------------------------------------------------------------------------

  const navigationEnabled =
    typeof navigationTotal === 'number' &&
    navigationTotal > 1 &&
    typeof currentIndex === 'number' &&
    typeof onNavigate === 'function';

  const prevDisabled = !navigationEnabled || currentIndex === 0;
  const nextDisabled =
    !navigationEnabled ||
    currentIndex === undefined ||
    navigationTotal === undefined ||
    currentIndex >= navigationTotal - 1;

  const handlePrev = React.useCallback(() => {
    if (!navigationEnabled || currentIndex === undefined || currentIndex <= 0) return;
    onNavigate?.(currentIndex - 1);
  }, [navigationEnabled, currentIndex, onNavigate]);

  const handleNext = React.useCallback(() => {
    if (
      !navigationEnabled ||
      currentIndex === undefined ||
      navigationTotal === undefined ||
      currentIndex >= navigationTotal - 1
    ) {
      return;
    }
    onNavigate?.(currentIndex + 1);
  }, [navigationEnabled, currentIndex, navigationTotal, onNavigate]);

  // Keyboard shortcuts — ←/→ navigate when nav is enabled and focus is NOT
  // in a text input / textarea / contenteditable surface (avoids hijacking
  // text-edit caret navigation inside the metadata pane or iframe overlays).
  // Listener is attached at the document level so it works whether focus is
  // on the renderer chrome OR an inner control.
  React.useEffect(() => {
    if (!navigationEnabled) return;

    const handler = (ev: KeyboardEvent): void => {
      if (ev.key !== 'ArrowLeft' && ev.key !== 'ArrowRight') return;

      const target = ev.target as HTMLElement | null;
      if (target) {
        const tag = target.tagName;
        if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable) {
          return;
        }
      }

      if (ev.key === 'ArrowLeft') {
        handlePrev();
      } else {
        handleNext();
      }
    };

    document.addEventListener('keydown', handler);
    return () => {
      document.removeEventListener('keydown', handler);
    };
  }, [navigationEnabled, handlePrev, handleNext]);

  // -------------------------------------------------------------------------
  // 3-dot menu dispatch
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
          // No popover from inside the renderer — sparkle columns in
          // list/card views are the canonical entry point for AI summary.
          // The menu item is hidden via `disabledActions` by default.
          return;
        case 'preview':
        case 'rename':
          // Hidden via default `disabledActions` — defensive no-op cases
          // keep the exhaustive `never` check happy.
          return;
        case 'pinToTop':
        case 'delete':
          // Visible in the menu, but the handlers are no-ops at the
          // PCF surface (Phase 4 follow-on tasks).
          return;
        default: {
          const _never: never = action;
          void _never;
          return;
        }
      }
    },
    [onOpenFile, onOpenRecord, onEmailDocument, onCopyLink, onToggleWorkspace, onFindSimilar]
  );

  // Resolve effective disabled actions: caller override wins; otherwise
  // apply the default set + auto-hide `findSimilar` when no callback.
  const effectiveDisabledActions = React.useMemo<DocumentRowAction[]>(() => {
    if (disabledActionsOverride) {
      return [...disabledActionsOverride];
    }
    const hidden: DocumentRowAction[] = [...DEFAULT_RICH_FILE_PREVIEW_DISABLED_ACTIONS];
    if (!onFindSimilar) hidden.push('findSimilar');
    return hidden;
  }, [disabledActionsOverride, onFindSimilar]);

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
    <div className={styles.root}>
      {/* Title bar — document name + optional Prev/Next + 3-dot DocumentRowMenu. */}
      <div className={styles.titleBar}>
        <Text as="h2" className={styles.titleText} size={400}>
          {documentName || 'Document Preview'}
        </Text>
        <div
          className={styles.titleActions}
          aria-label={isInWorkspace ? 'Document actions (in workspace)' : 'Document actions'}
        >
          {navigationEnabled && (
            <>
              <div className={styles.titleNav} role="group" aria-label="Document navigation">
                <Tooltip content="Previous document" relationship="label">
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={<ChevronLeft20Regular />}
                    aria-label="Previous document"
                    disabled={prevDisabled}
                    onClick={handlePrev}
                  />
                </Tooltip>
                <Text size={200} className={styles.titleNavCounter} aria-live="polite">
                  {(currentIndex ?? 0) + 1} of {navigationTotal}
                </Text>
                <Tooltip content="Next document" relationship="label">
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={<ChevronRight20Regular />}
                    aria-label="Next document"
                    disabled={nextDisabled}
                    onClick={handleNext}
                  />
                </Tooltip>
              </div>
              <Divider vertical className={styles.titleNavDivider} />
            </>
          )}
          <DocumentRowMenu document={target} onAction={handleRowAction} disabledActions={effectiveDisabledActions} />
        </div>
      </div>

      {/* 2-column body — iframe (left) | metadata pane (right). */}
      <div className={styles.body} role="group" aria-label="Document preview body">
        <div className={styles.thumbnailCell}>{renderPreviewArea()}</div>
        <div className={styles.metadataPane}>
          {/* Section 1: Tags */}
          <section className={styles.section} aria-labelledby="rfp-tags-heading">
            <Text id="rfp-tags-heading" className={styles.sectionHeader} size={300}>
              Tags
            </Text>
            {renderTagSection()}
          </section>

          {/* Section 2: Details */}
          <section className={styles.section} aria-labelledby="rfp-details-heading">
            <Text id="rfp-details-heading" className={styles.sectionHeader} size={300}>
              Details
            </Text>
            {renderDetailsSection()}
          </section>
        </div>
      </div>
    </div>
  );
};

RichFilePreview.displayName = 'RichFilePreview';
