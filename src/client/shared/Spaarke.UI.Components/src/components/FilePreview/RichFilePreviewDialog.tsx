/**
 * RichFilePreviewDialog — Document preview modal with 2-column layout, prev/next
 * navigation, AI summary section (optional), Tags chip, Details grid, and 3-dot
 * row-action menu integration.
 *
 * Originally authored as the SemanticSearchControl PCF's `FilePreviewDialog.tsx`
 * for the `spaarke-matter-ui-enhancement-r1` project, then promoted to
 * `@spaarke/ui-components` so other Spaarke surfaces (LegalWorkspace,
 * DocumentRelationshipViewer, Office Add-ins) can consume the same rich UX.
 *
 * Coexistence note: the simpler `FilePreviewDialog` (services-injection API,
 * 880px, single column) at `./FilePreviewDialog.tsx` is retained for back-compat
 * with `FindSimilarResultsStep` and its downstream consumers. New surfaces
 * should prefer `RichFilePreviewDialog`.
 *
 * Layout: 1280px max-width × 85vh, 2-column body (1fr iframe | 320px metadata pane).
 * The metadata pane renders:
 *   1. Tags    — single Fluent v9 `Tag` chip from `documentType`
 *   2. Details — Created by · Created · Size · Type
 *
 * Optional features (degrade gracefully when callbacks are omitted):
 *   - `onFetchSummary` — enables `aiSummary` menu item (section not rendered inline)
 *   - `navigationTotal` + `currentIndex` + `onNavigate` — enables Prev/Next nav in title bar
 *   - `onFindSimilar` — enables `findSimilar` menu item + footer button
 *   - `onToggleWorkspace` + `isInWorkspace` — workspace flag (hidden in this dialog by default)
 *
 * The 3-dot title-bar menu is `DocumentRowMenu` (from this library). Hidden by
 * default: `preview` (the dialog IS the preview), `aiSummary`, `toggleWorkspace`,
 * `rename` (no handler at most surfaces).
 *
 * @see ADR-012 - Shared component library
 * @see ADR-021 - Fluent UI v9 (semantic tokens, dark-mode parity)
 * @see ADR-022 - React 16/17 compatible (no React 18-only APIs)
 */

import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogActions,
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
  /**
   * Navigation set total. When provided alongside `currentIndex`
   * + `onNavigate`, the title bar renders Prev / Next + "N of M". When
   * omitted, the dialog renders without navigation chrome.
   *
   * The dialog itself never inspects the documents — only `navigationTotal`
   * matters for the position indicator + disabled-state logic. The parent
   * owns the navigation set and recomputes it as needed.
   */
  navigationTotal?: number;
  /**
   * 0-based position of the currently-shown document inside the parent's
   * navigation set. Drives the "N of M" position indicator + Prev/Next
   * disabled state. Required when `navigationTotal` is supplied; ignored
   * otherwise.
   */
  currentIndex?: number;
  /**
   * Navigate to a different document inside the parent's navigation set.
   * Receives the 0-based target index. The parent is responsible for
   * swapping the dialog's content (documentName / documentId / fetchPreviewUrl /
   * etc.) to reflect the new active document. The dialog resets its
   * iframe-load state automatically when `documentId` changes.
   */
  onNavigate?: (nextIndex: number) => void;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

// surface widened to 1280 px so a US-Letter PDF (~816 px content
// width) + PDF viewer chrome fits in the left iframe cell without a
// horizontal scrollbar. The metadata pane is unchanged at 320 px; the
// iframe column is now fluid (`1fr`) rather than a hard-coded width, so
// the layout collapses gracefully on narrower viewports (Fluent v9's
// DialogSurface clamps to `width: 100%` below the max-width).
const METADATA_COLUMN_WIDTH = '320px';
const DIALOG_MAX_WIDTH = '1280px';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  surface: {
    // surface is no longer pinned to an exact width: it uses
    // `width: 100%` + `maxWidth: 1280px` so it clamps gracefully on smaller
    // viewports (laptops below 1280 px wide) without horizontal overflow.
    // The 2-col grid below uses `1fr 320px` so the iframe cell always
    // consumes the remaining horizontal space regardless of surface width.
    width: '100%',
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
  // Prev/Next nav cluster relocated from footer (DialogActions) to
  // the title bar's right side, just before the 3-dot DocumentRowMenu.
  titleNav: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },
  // Subtle vertical separator between the nav cluster and the menu.
  // Fluent's <Divider vertical /> is the canonical hairline; margin
  // gives breathing room without crowding the icons.
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
  // iframe column is now `1fr` so it expands to fill the wider
  // 1280 px surface (previously a hard 640 px, which left a wide gap at
  // the right edge once the surface widened). The metadata column stays
  // at 320 px — its intrinsic content (AI summary text, Tag chip, key/value
  // grid) doesn't benefit from extra width.
  // rendered as a plain <div> (no DialogBody wrapper) so the
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
    // tracks of `1fr | METADATA_COLUMN_WIDTH` respectively.
    gridTemplateColumns: `1fr ${METADATA_COLUMN_WIDTH}`,
    gridTemplateRows: '1fr',
    gridAutoFlow: 'column' as const,
    width: '100%',
    ...shorthands.overflow('hidden'),
  },
  // Iframe container — fills the left column.
  // `minWidth: 0` is the Grid-collapse fix: without it, a child iframe's
  // intrinsic size can force the cell wider than its track allocation.
  //
  // visible vertical scrollbar HIDDEN. The iframe content
  // (PDF viewer, Word web view, etc.) renders its OWN scrollbar inside
  // the iframe, so the outer cell's scrollbar was redundant chrome that
  // UAT flagged. Scroll BEHAVIOR is preserved — mouse wheel, keyboard
  // (arrow + Page keys), touch swipe all still scroll the cell if the
  // iframe ever overflows it. CSS pattern: `scrollbarWidth: 'none'`
  // (Firefox standard) + `msOverflowStyle: 'none'` (legacy IE/Edge) +
  // `::-webkit-scrollbar { display: none }` (Chromium/Safari/Edge).
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
  // Metadata pane — scrolls if content overflows. `minWidth: 0` again to
  // prevent text content from forcing the cell wider than 320 px.
  // `gap` bumped from `spacingVerticalL` to `spacingVerticalXXL`
  // so AI summary · Tags · Details are visually distinct sections. UAT
  // round 1 felt the three sections "ran together"; the larger inter-
  // section breathing room fixes that without crowding the dialog.
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
  // Footer action bar.
  // uses `space-between` so the optional Prev/Next nav group
  // sits at the leading edge (left) and the Close button stays at the
  // trailing edge (right). The Close-only path uses the same container
  // (Prev/Next group is conditionally rendered) so the footer chrome
  // is identical regardless of whether navigation is enabled.
  footer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
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
  // (Footer nav cluster is rendered in the title bar — see titleNav + titleNavCounter above.)
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

export const RichFilePreviewDialog: React.FC<IFilePreviewDialogProps> = ({
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
  navigationTotal,
  currentIndex,
  onNavigate,
}) => {
  const styles = useStyles();

  const [previewUrl, setPreviewUrl] = React.useState<string | null>(null);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState(false);

  // AI summary state + effect REMOVED. The summary
  // section no longer renders inline; the AI Summary is reachable via
  // the list-view sparkle column, card-view sparkle icon, and the 3-dot
  // menu's "AI summary" item (when `onFetchSummary` is wired). The
  // `onFetchSummary` prop is retained on the API surface for back-compat
  // and still drives the menu-item visibility through `disabledActions`.

  // Fetch preview URL when dialog opens — pipeline unchanged from task 040.
  // when the parent navigates (Prev/Next) and swaps `documentId` +
  // `fetchPreviewUrl`, this effect re-fires. We also reset `previewUrl` to
  // null at the top so the old iframe doesn't briefly flash while the new
  // one is fetching. Adding `documentId` to deps is belt-and-braces.
  React.useEffect(() => {
    if (!open) {
      setPreviewUrl(null);
      setError(false);
      return;
    }

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
  }, [open, documentId, fetchPreviewUrl]);

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
  //
  // Whether the dialog renders Prev/Next is gated on `navigationTotal > 1` —
  // a single-doc navigation set is meaningless (no Prev, no Next). When the
  // caller omits `navigationTotal` entirely, the dialog renders without nav.
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
  // on the dialog chrome OR an inner control.
  React.useEffect(() => {
    if (!open || !navigationEnabled) return;

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
  }, [open, navigationEnabled, handlePrev, handleNext]);

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
        case 'rename':
          // Hidden via `disabledActions` — defensive no-op cases keep
          // the exhaustive `never` check happy.
          return;
        case 'pinToTop':
        case 'delete':
          // now VISIBLE in the dialog menu, but the
          // handlers are still no-ops at the PCF surface (Phase 4
          // follow-on tasks). Visibility was the user-requested change
          // this round.
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

  // Hide only the actions the dialog cannot service.
  // `preview` is always hidden (dialog IS the preview).
  // `findSimilar` is hidden when no callback was provided.
  //
  // `toggleWorkspace` is ALWAYS hidden from this dialog's menu.
  // The dialog itself IS the workspace surface for the document (the user
  // has already drilled into a document detail view), so the menu item was
  // visually present but functionally a no-op, which UAT flagged as
  // confusing. Row-context still exposes `toggleWorkspace` via
  // ResultCard.tsx + ListView.tsx — only the dialog hides it.
  //
  // `aiSummary` is ALWAYS hidden — the AI summary is reachable via
  // sibling surfaces (list/card sparkle columns) instead of inline.
  //
  // Menu standardized across card + row + dialog
  // surfaces. Dialog hides: preview, aiSummary, toggleWorkspace, rename
  // (rename added this round per user request). `pinToTop` + `delete`
  // are NOW visible in the dialog menu (previously hidden as "Phase 4
  // follow-on"); the handlers are still no-ops in `handleRowAction`
  // but visibility was the user-requested change for this round.
  const dialogDisabledActions = React.useMemo<DocumentRowAction[]>(() => {
    const hidden: DocumentRowAction[] = [
      'preview', // dialog IS the preview surface
      'aiSummary', // hidden across all surfaces (Item 6)
      'toggleWorkspace', // dialog IS the workspace surface
      'rename', // hidden across all surfaces (Item 6)
    ];
    if (!onFindSimilar) hidden.push('findSimilar');
    return hidden;
  }, [onFindSimilar]);

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

  // `renderSummarySection` helper REMOVED.
  // The inline AI summary section is no longer rendered (see metadata
  // pane render below). The `onFetchSummary` prop is preserved for
  // back-compat and still gates the 3-dot menu's "AI summary" item via
  // `disabledActions`.

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
        {/* Title bar — 3-dot menu replaces the inline action Toolbar (task 040).
            the title-bar `X` close icon is removed. The Close button
            in the footer is the single close affordance for this dialog —
            keeping both was duplicative chrome (UAT round 2). The 3-dot
            DocumentRowMenu remains in place. The Esc-to-close DialogTitle
            default + the explicit onOpenChange handler on the Dialog still
            keep keyboard close working. */}
        <div className={styles.titleBar}>
          <DialogTitle action={null} className={styles.titleText}>
            {documentName || 'Document Preview'}
          </DialogTitle>
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
            <DocumentRowMenu document={target} onAction={handleRowAction} disabledActions={dialogDisabledActions} />
          </div>
        </div>

        {/* 2-column body — iframe (left) | metadata pane (right).
            rendered as a plain <div> instead of <DialogBody> +
            <DialogContent> wrappers; the wrappers' default padding /
            overflow were causing the grid tracks to collapse when the
            iframe mounted (the visible flicker UAT reported). */}
        <div className={styles.body} role="group" aria-label="Document preview body">
          <div className={styles.thumbnailCell}>{renderPreviewArea()}</div>
          <div className={styles.metadataPane}>
            {/*  (Item 6)AI summary section REMOVED from the
                metadata pane. The summary is still reachable via:
                  - The 3-dot menu's "AI summary" item (visible when
                    `onFetchSummary` is wired) which the host opens with
                    the AiSummaryPopover trigger ref.
                  - The list-view sparkle column AND the card-view
                    sparkle icon (both share the AiSummaryPopover surface).
                The `renderSummarySection` helper + onFetchSummary prop
                are retained for back-compat of the prop API (callers can
                still pass the fetcher even though the dialog no longer
                renders the section inline). The `aiSummary` menu item is
                still hidden via `disabledActions` when no fetcher exists,
                so behavior is unchanged on that path. */}

            {/* Section 1: Tags */}
            <section className={styles.section} aria-labelledby="fpd-tags-heading">
              <Text id="fpd-tags-heading" className={styles.sectionHeader} size={300}>
                Tags
              </Text>
              {renderTagSection()}
            </section>

            {/* Section 2: Details */}
            <section className={styles.section} aria-labelledby="fpd-details-heading">
              <Text id="fpd-details-heading" className={styles.sectionHeader} size={300}>
                Details
              </Text>
              {renderDetailsSection()}
            </section>
          </div>
        </div>

        {/* Footer: Close only. Prev/Next nav lives in the title bar
            (right side, before the 3-dot menu) so document navigation
            sits adjacent to the document-context actions. */}
        <DialogActions className={styles.footer}>
          <Button appearance="primary" onClick={onClose}>
            Close
          </Button>
        </DialogActions>
      </DialogSurface>
    </Dialog>
  );
};

RichFilePreviewDialog.displayName = 'RichFilePreviewDialog';
