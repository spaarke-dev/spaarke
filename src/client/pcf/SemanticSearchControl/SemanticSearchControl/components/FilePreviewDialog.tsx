/**
 * FilePreviewDialog — Modal for document preview.
 *
 * Per FR-DOC-03 (task 044), the dialog uses a 2-column body layout
 * (left iframe · 320 px metadata pane) clamped to a max-width.
 * The metadata pane renders three sections top→bottom:
 *   1. AI summary  (sparkle icon + paragraph; rendered when `onFetchSummary`
 *      is provided — falls back to a friendly empty state otherwise)
 *   2. Tags        (single Fluent v9 `Tag` chip from `documentType`)
 *   3. Details     (Created by · Created · Size · Type)
 *
 * v1.1.46 (UAT polish round 2):
 *   • Surface widened to 1280 px max-width with a fluid (`1fr 320px`)
 *     iframe column so a US-Letter PDF + PDF viewer chrome fits without a
 *     horizontal scrollbar at the iframe edge.
 *   • Left iframe cell hides its visible vertical scrollbar (mouse-wheel /
 *     keyboard / touch scroll still works) — the iframe content is itself
 *     scrollable; the outer cell's chrome added a redundant gutter scrollbar
 *     that UAT flagged as distracting.
 *   • Right metadata pane uses `spacingVerticalXXL` between its three
 *     sections so AI summary · Tags · Details are visually distinct
 *     (round 1 used `spacingVerticalL` which read as one stacked block).
 *   • Title-bar `X` close icon removed — the dialog already has a Close
 *     button in the footer and the in-title-bar `X` was duplicative.
 *     The 3-dot DocumentRowMenu in the upper-right corner is preserved.
 *   • Footer adds Previous / Next navigation when the caller supplies a
 *     `documents` + `currentIndex` + `onNavigate` triplet. ListView
 *     computes the navigation set from `selectedIds` (selection wins) or
 *     the full sorted+filtered results; the dialog displays "N of M" and
 *     drives navigation via arrow buttons + keyboard (←/→). When the
 *     triplet is omitted, the footer renders Close only (back-compat
 *     used by ResultCard's per-card preview).
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
  Sparkle20Filled,
  ChevronLeft20Regular,
  ChevronRight20Regular,
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
  /**
   * Navigation set total (v1.1.46). When provided alongside `currentIndex`
   * + `onNavigate`, the footer renders Prev / Next + "N of M". When
   * omitted, the footer renders Close only (back-compat — ResultCard's
   * per-card preview uses this code path).
   *
   * The dialog itself never inspects the documents — only `navigationTotal`
   * matters for the position indicator + disabled-state logic. The parent
   * (ListView in v1.1.46) owns the SearchResult[] navigation set and is
   * the one that needs to recompute it when `selectedIds` changes.
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

// v1.1.46 — surface widened to 1280 px so a US-Letter PDF (~816 px content
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
    // v1.1.46 — surface is no longer pinned to an exact width: it uses
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
  // 2-column body grid: fluid iframe column | 320 px metadata pane.
  // v1.1.46 — iframe column is now `1fr` so it expands to fill the wider
  // 1280 px surface (previously a hard 640 px, which left a wide gap at
  // the right edge once the surface widened). The metadata column stays
  // at 320 px — its intrinsic content (AI summary text, Tag chip, key/value
  // grid) doesn't benefit from extra width.
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
  // v1.1.46 — visible vertical scrollbar HIDDEN. The iframe content
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
  // v1.1.46 — `gap` bumped from `spacingVerticalL` to `spacingVerticalXXL`
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
  // v1.1.46 — uses `space-between` so the optional Prev/Next nav group
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
  // Leading-edge nav cluster: [Prev] [N of M] [Next]. Centered counter
  // uses neutral foreground so it reads as auxiliary metadata, not an
  // interactive control.
  footerNav: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  footerCounter: {
    color: tokens.colorNeutralForeground3,
    // Minimum width keeps the counter from flickering when the index
    // changes single-digit → double-digit (e.g., "9 of 12" → "10 of 12").
    minWidth: '64px',
    textAlign: 'center' as const,
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
  navigationTotal,
  currentIndex,
  onNavigate,
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
  // v1.1.46 — when the parent navigates (Prev/Next) and swaps `documentId` +
  // `fetchPreviewUrl`, this effect re-fires. We also reset `previewUrl` and
  // `summary` to null at the top so the old iframe + old summary text don't
  // briefly flash while the new ones are fetching. Adding `documentId` to
  // deps is belt-and-braces: parent SHOULD also pass a fresh fetchPreviewUrl
  // closure on navigation, but if any caller forgets, the documentId change
  // alone still triggers the refresh.
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

  // Fetch AI summary when dialog opens (only if caller provided a fetcher).
  // v1.1.46 — also re-fires when `documentId` changes so Prev/Next swaps
  // the summary text alongside the preview iframe.
  React.useEffect(() => {
    if (!open || !onFetchSummary) return;

    let cancelled = false;
    setSummary(null);
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
  }, [open, documentId, onFetchSummary]);

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
  // Prev/Next navigation (v1.1.46)
  //
  // Whether the footer renders Prev/Next is gated on `navigationTotal > 1` —
  // a single-doc navigation set is meaningless (no Prev, no Next). When the
  // caller omits `navigationTotal` entirely, the footer renders Close only,
  // matching the pre-v1.1.46 behavior used by ResultCard's per-card dialog.
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
        if (
          tag === 'INPUT' ||
          tag === 'TEXTAREA' ||
          tag === 'SELECT' ||
          target.isContentEditable
        ) {
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
        {/* Title bar — 3-dot menu replaces the inline action Toolbar (task 040).
            v1.1.46: the title-bar `X` close icon is removed. The Close button
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
            aria-label={
              isInWorkspace ? 'Document actions (in workspace)' : 'Document actions'
            }
          >
            <DocumentRowMenu
              document={target}
              onAction={handleRowAction}
              disabledActions={dialogDisabledActions}
            />
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

        {/* Footer (v1.1.46): leading-edge Prev/Next nav cluster (when the
            parent supplies navigationTotal+currentIndex+onNavigate),
            trailing-edge Close. When nav is NOT enabled, the leading edge
            renders a zero-width spacer so the footer chrome (border-top,
            padding, justify-content: space-between) stays consistent and
            the Close button stays right-aligned.
            "Find similar" and "Open file" remain reachable via the 3-dot
            menu in the title bar — the redundant footer affordances were
            removed per UAT round 1 feedback. */}
        <DialogActions className={styles.footer}>
          {navigationEnabled ? (
            <div className={styles.footerNav} role="group" aria-label="Document navigation">
              <Tooltip content="Previous document" relationship="label">
                <Button
                  appearance="subtle"
                  icon={<ChevronLeft20Regular />}
                  aria-label="Previous document"
                  disabled={prevDisabled}
                  onClick={handlePrev}
                />
              </Tooltip>
              <Text size={200} className={styles.footerCounter} aria-live="polite">
                {(currentIndex ?? 0) + 1} of {navigationTotal}
              </Text>
              <Tooltip content="Next document" relationship="label">
                <Button
                  appearance="subtle"
                  icon={<ChevronRight20Regular />}
                  aria-label="Next document"
                  disabled={nextDisabled}
                  onClick={handleNext}
                />
              </Tooltip>
            </div>
          ) : (
            <span aria-hidden="true" />
          )}
          <Button appearance="primary" onClick={onClose}>
            Close
          </Button>
        </DialogActions>
      </DialogSurface>
    </Dialog>
  );
};

FilePreviewDialog.displayName = 'FilePreviewDialog';
