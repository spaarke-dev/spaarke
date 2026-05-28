/**
 * ListView component (FR-DOC-04)
 *
 * Tabular list view for the Documents PCF. Renders a Fluent v9 `DataGrid`.
 *
 * v1.1.54 column order (Item 6 standardization):
 *
 *   Select | Pin | Document | Relationship | Similarity | Type | Modified | Menu
 *
 * - Document: file-type icon + name (was "Name" in v1.1.49).
 * - Relationship: Fluent v9 `Badge` showing "Same Matter" (color="success",
 *   green) when the row's `relationship` tag is 'associated', or "Semantic"
 *   (color="brand", blue) when 'semantic'. Determined by
 *   `SemanticSearchApiService.searchUnion` tagging.
 * - Similarity: was "Match" in v1.1.49. Pill background changes based on
 *   the row's relationship — 'associated' rows show a green "100%" pill
 *   (v1.1.54 Item 4 — was: blank blue pill); 'semantic' rows show a
 *   light-yellow Marigold pill with the combinedScore percentage.
 * - Type: file-type icon only.
 * - AI column REMOVED (v1.1.54 Item 6) — AI Summary action is hidden across
 *   card + row + dialog surfaces this round.
 * - Modified by REMOVED (Item 3) — auxiliary; surfaced in preview metadata.
 *
 * Sortable columns: Document (name), Modified, Similarity. Relationship +
 * Type are non-sortable. Pin lifts to the top regardless of sort.
 *
 * v1.1.50 (Items 1 + 2):
 * - Lazy-load IntersectionObserver sentinel row appended below the
 *   DataGrid body (Item 1). When the host wires `onLoadMoreSentinel`, the
 *   sentinel fires the callback on viewport intersection (mirrors the
 *   card-view ResultsList behavior; same threshold + rootMargin).
 * - List-view preview dialog can now route through the SAME host-level
 *   FilePreviewDialog as the card view (Item 2). When the parent supplies
 *   `onOpenPreview`, ListView no longer mounts its own FilePreviewDialog;
 *   instead it emits the docId up so the host's single shared dialog
 *   instance owns Prev/Next navigation across both views. Back-compat:
 *   when `onOpenPreview` is omitted, the legacy local dialog still renders.
 *
 * v1.1.45 (UAT round 2):
 *   • Switched from `Table` → `DataGrid` so columns become user-resizable.
 *     `columnSizingOptions` provides intrinsic widths; the parent owns the
 *     ColumnWidths map (persisted via `useDocumentListPrefs`) so user sizes
 *     survive reload + cross-tab opens.
 *   • Row height raised to ~56 px via vertical token padding on cells
 *     (`tokens.spacingVerticalM`) to improve readability and match the UAT
 *     mockup. Visible separators between rows keep scan-ability.
 *   • Name column truncates with ellipsis + `title={doc.name}` for hover
 *     full-name read-out (no more bleed into the Modified column).
 *
 * Sortable columns: Name, Modified, Match. Pin is non-sortable and the icon
 * is rendered ONLY on pinned rows. Pinned rows always sort to the top
 * regardless of the active column sort.
 *
 * Selection state is owned by the parent (`SemanticSearchControl`) so it
 * persists across list/card view toggles. Pin state + column-width prefs
 * are also owned by the parent (via `useDocumentListPrefs`) so both survive
 * reload.
 *
 * Per FR-DOC-01 (task 040), the 3-dot menu reuses the shared
 * `DocumentRowMenu` consumed via the same deep-path import the Card view's
 * `ResultCard` uses (React 16 + Lexical-via-barrel conflict — see ResultCard
 * for full rationale).
 *
 * Standards:
 *   - ADR-006  PCF UI surface
 *   - ADR-012  Shared `DocumentRowMenu` consumed (not re-implemented)
 *   - ADR-021  Fluent v9 semantic tokens only; dark-mode safe
 *   - ADR-022  React 16/17-safe — no React 18+ exclusive APIs
 *
 * @see spec.md FR-DOC-04
 */

import * as React from 'react';
import {
  Badge,
  Button,
  Checkbox,
  DataGrid,
  DataGridBody,
  DataGridCell,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridRow,
  Link,
  TableCellLayout,
  type TableColumnDefinition,
  type TableColumnSizingOptions,
  Text,
  Tooltip,
  createTableColumn,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import {
  DocumentRegular,
  DocumentPdfRegular,
  DocumentTextRegular,
  TableRegular,
  SlideTextRegular,
  ImageRegular,
  MailRegular,
  Pin20Filled,
} from '@fluentui/react-icons';
// Deep-path imports (NOT the barrel) — the barrel pulls in RichTextEditor →
// `@lexical/react` ESM modules that don't resolve `react/jsx-runtime` under
// React 16's resolution (PCF target per ADR-022). Matches the pattern used by
// ResultCard.tsx for `DocumentRowMenu` and `RecordCardShell`.
import {
  DocumentRowMenu,
  type DocumentRowAction,
  type IDocumentRowMenuTarget,
} from '@spaarke/ui-components/dist/components/DocumentRowMenu';
// v1.1.54 (Item 6) — `AiSummaryPopover` import removed; the COL_AI sparkle
// column is dropped this round as part of the menu/AI standardization
// (AI Summary hidden across card + row + dialog surfaces).
import { SearchResult, SummaryData } from '../types';
import { FilePreviewDialog } from './FilePreviewDialog';
import type { ColumnWidths } from '../hooks/useDocumentListPrefs';

// ---------------------------------------------------------------------------
// Sort contract
// ---------------------------------------------------------------------------

/** Columns the user can sort by. Pin is intentionally not sortable. */
export type ListSortColumn = 'name' | 'modifiedAt' | 'combinedScore';

/** Sort direction toggle. */
export type ListSortDirection = 'asc' | 'desc';

// Internal DataGrid column ids — string literals so localStorage keys are stable.
//
// v1.1.50 (Items 3 + 4 + 5):
// - COL_NAME → COL_DOCUMENT (label changes from "Name" → "Document"; sort
//   key still resolves to `'name'` so persisted user prefs from earlier
//   versions still order correctly).
// - COL_MODIFIED_BY removed (auxiliary; reachable via preview metadata).
// - COL_RELATIONSHIP added (Item 5).
// - COL_TYPE added (Item 3).
// - COL_AI added (Item 4).
//
// v1.1.54 (Item 6) — COL_AI removed. The AI Summary sparkle column is
// dropped as part of the menu/AI standardization (AI Summary hidden
// across card + row + dialog surfaces). User-persisted COL_AI widths
// linger as orphan keys in localStorage but are no longer iterated, so
// they do not affect rendering. No data migration needed.
// COL_MATCH renamed to COL_SIMILARITY in the user-facing header (the
// internal id stays 'combinedScore' so persisted sort prefs are preserved).
const COL_SELECT = 'select';
const COL_PIN = 'pin';
const COL_DOCUMENT = 'name';
const COL_RELATIONSHIP = 'relationship';
const COL_SIMILARITY = 'combinedScore';
const COL_TYPE = 'documentType';
const COL_MODIFIED = 'modifiedAt';
const COL_MENU = 'menu';

// Default intrinsic widths in pixels (used when no user override is persisted).
// v1.1.51 (Item 3) — COL_DOCUMENT shrunk from 400 → 260 so the typical Matter
// form viewport can fit all nine columns (Select + Pin + Document +
// Relationship + Similarity + Type + Modified + AI + Menu) without a
// horizontal scrollbar. The DataGrid's resizable columns + per-user width
// persistence (via `setColumnWidth`) means users can still widen Document
// manually if their viewport is wide enough.
// v1.1.59 — defaults trimmed to eliminate horizontal scroll at 1920x1080
// (sum 688px + DataGrid internal padding ~30px ≈ 720px, comfortably
// fits inside a typical Matter form section). User-persisted widths
// from earlier rounds are invalidated by the v2 localStorage key
// prefix bump (see useDocumentListPrefs.ts).
// v1.1.64 — COL_DOCUMENT reverted 480 → 240. The v1.1.61 bump (240→480)
// was meant to "fill" the empty space to the right of the menu column on
// wide Matter form sections, but it caused a horizontal scrollbar to
// appear on slightly narrower form widths. Now that v1.1.63 anchors the
// menu cell to the row's right edge via `marginInlineStart: 'auto'`
// (Fluent v9 DataGrid rows are flexbox; the auto-margin consumes any
// leftover inline space), there's no need to inflate the Document
// column to fill. Reverting gives us a comfortable 688px column total
// + ~30px chrome ≈ 720px — comfortably under typical Matter form
// section widths (~900-1050px), so NO horizontal scroll, and the menu
// still ends up flush right because of the auto-margin.
const DEFAULT_WIDTHS: Record<string, number> = {
  [COL_SELECT]: 40,
  [COL_PIN]: 36,
  [COL_DOCUMENT]: 240,
  [COL_RELATIONSHIP]: 110,
  [COL_SIMILARITY]: 70,
  // v1.1.53 (Item 4 Part B) — Type column now renders just the file icon
  // (no text), so its default width drops from 120 → 48 to free space
  // for the wider Document / Relationship columns.
  [COL_TYPE]: 48,
  [COL_MODIFIED]: 100,
  [COL_MENU]: 44,
};

const MIN_WIDTHS: Record<string, number> = {
  [COL_SELECT]: 40,
  [COL_PIN]: 36,
  // v1.1.51 (Item 3) — relaxed from 160 → 140 so the Document column can be
  // sized down to match tighter viewports without losing legibility.
  [COL_DOCUMENT]: 140,
  [COL_RELATIONSHIP]: 110,
  [COL_SIMILARITY]: 80,
  // v1.1.53 (Item 4 Part B) — Type column min width shrunk from 80 → 40 to
  // match the new icon-only rendering footprint.
  [COL_TYPE]: 40,
  [COL_MODIFIED]: 90,
  [COL_MENU]: 44,
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

type IconComponent = typeof DocumentRegular;

function getFileIcon(fileType: string): IconComponent {
  const ext = fileType?.toLowerCase().trim() ?? '';
  switch (ext) {
    case 'pdf':
      return DocumentPdfRegular;
    case 'doc':
    case 'docx':
    case 'rtf':
    case 'odt':
    case 'txt':
      return DocumentTextRegular;
    case 'xls':
    case 'xlsx':
    case 'csv':
      return TableRegular;
    case 'ppt':
    case 'pptx':
      return SlideTextRegular;
    case 'jpg':
    case 'jpeg':
    case 'png':
    case 'gif':
    case 'bmp':
    case 'svg':
      return ImageRegular;
    case 'msg':
    case 'eml':
      return MailRegular;
    default:
      return DocumentRegular;
  }
}

function formatShortDate(dateString: string | null): string {
  if (!dateString) return '';
  try {
    const d = new Date(dateString);
    if (isNaN(d.getTime())) return '';
    return d.toLocaleDateString(undefined, {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  } catch {
    return '';
  }
}

/**
 * Sort comparator that always lifts pinned rows above all others, then sorts
 * the rest by the active column + direction. Defensive against null fields.
 */
function sortResults(
  results: ReadonlyArray<SearchResult>,
  sortColumn: ListSortColumn,
  sortDirection: ListSortDirection,
  pinnedIds: Set<string>
): SearchResult[] {
  const arr = [...results];
  const dirMul = sortDirection === 'asc' ? 1 : -1;

  arr.sort((a, b) => {
    // Pin lift — pinned rows always sort first, regardless of column sort.
    const aPinned = pinnedIds.has(a.documentId);
    const bPinned = pinnedIds.has(b.documentId);
    if (aPinned !== bPinned) return aPinned ? -1 : 1;

    // Within-group sort by active column.
    let av: string | number;
    let bv: string | number;
    switch (sortColumn) {
      case 'name':
        av = (a.name ?? '').toLocaleLowerCase();
        bv = (b.name ?? '').toLocaleLowerCase();
        break;
      case 'modifiedAt':
        // Sort by timestamp (numeric). Missing values land at the bottom regardless
        // of direction so the user sees populated rows first.
        av = a.modifiedAt ? new Date(a.modifiedAt).getTime() : Number.NaN;
        bv = b.modifiedAt ? new Date(b.modifiedAt).getTime() : Number.NaN;
        if (Number.isNaN(av) && Number.isNaN(bv)) return 0;
        if (Number.isNaN(av)) return 1;
        if (Number.isNaN(bv)) return -1;
        break;
      case 'combinedScore':
        av = a.combinedScore ?? 0;
        bv = b.combinedScore ?? 0;
        break;
      default: {
        const _never: never = sortColumn;
        void _never;
        return 0;
      }
    }
    if (av < bv) return -1 * dirMul;
    if (av > bv) return 1 * dirMul;
    return 0;
  });

  return arr;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    flex: 1,
    overflowY: 'auto',
    overflowX: 'auto',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  // v1.1.45 — row height bump.
  // DataGrid cells get extra vertical padding (M token) on both top + bottom,
  // which lands rows at ~56 px depending on the cell content. Token-only so
  // dark mode + Spaarke brand themes still resolve correctly.
  gridCell: {
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  // Header gets only a single divider underline (no row separators above).
  headerCell: {
    fontWeight: tokens.fontWeightSemibold,
  },
  // Selection column — narrow, just enough for the checkbox.
  selectCell: {
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
  },
  // Pin column cell — keep the row-level click target tight.
  pinCell: {
    paddingLeft: 0,
    paddingRight: 0,
    cursor: 'pointer',
    textAlign: 'center',
  },
  // v1.1.63 — Menu cell is anchored to the right edge of the visible row
  // regardless of total column width vs container width.
  //
  // Why this works: Fluent v9 DataGridRow is rendered with
  // `display: flex; alignItems: center` (see @fluentui/react-table
  // useTableRowStyles.styles.raw.js — flex layout via
  // `noNativeElements`, NOT CSS grid). Each DataGridCell receives an
  // inline `style: { width, minWidth, maxWidth }` from the
  // `columnSizing_unstable` plugin so user-resized widths still apply.
  // Because the layout is flexbox, `margin-inline-start: auto` on the
  // last cell consumes ALL remaining inline space in the row, pushing
  // the menu cell flush against the row's right edge whenever the
  // column total is narrower than the container.
  //
  // When the sum of fixed widths EXCEEDS the container (horizontal
  // scroll case), the auto-margin collapses to 0 (no remaining space)
  // and the v1.1.59 `position: sticky; right: 0` keeps the menu cell
  // pinned to the visible-scroll right edge as the user scrolls
  // horizontally. Both behaviors compose cleanly.
  //
  // Two earlier approaches considered + rejected:
  // 1. `gridTemplateColumns` override on `[role="row"]` — does nothing
  //    because Fluent rows use FLEX, not GRID (verified in node_modules).
  // 2. `width: 100%` on the row — would let the cell stretch but would
  //    also distort other column widths. Auto-margin on the LAST cell
  //    is the surgical fix.
  menuCell: {
    display: 'flex',
    justifyContent: 'flex-end',
    alignItems: 'center',
    paddingLeft: 0,
    paddingRight: tokens.spacingHorizontalXS,
    // v1.1.63 — anchor to the row's right edge in the no-overflow case.
    marginInlineStart: 'auto',
    // v1.1.59 — keep sticky behavior for the horizontal-overflow case.
    position: 'sticky',
    right: 0,
    backgroundColor: tokens.colorNeutralBackground1,
    zIndex: 1,
  },
  // v1.1.45 — name cell truncates with ellipsis and bleeds NO further than its
  // column track (the user-reported regression). `minWidth: 0` is required on
  // CSS Grid/Flex descendants for `text-overflow: ellipsis` to engage.
  nameWrap: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    minWidth: 0,
    width: '100%',
    ...shorthands.overflow('hidden'),
  },
  nameLink: {
    cursor: 'pointer',
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorBrandForegroundLink,
    minWidth: 0,
    flex: 1,
    // Force the link to participate in ellipsis truncation.
    ...shorthands.overflow('hidden'),
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    display: 'block',
  },
  // v1.1.50 — Similarity pill base. Padding/typography only; per-row
  // background + foreground colors are layered via `similaritySemantic`
  // or `similarityAssociated` so the same chrome carries both palettes.
  // All tokens (ADR-021); dark-mode safe.
  similarityBase: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
    ...shorthands.padding('1px', tokens.spacingHorizontalS),
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightBase100,
    whiteSpace: 'nowrap',
  },
  // 'semantic' rows — light yellow (Marigold) palette for both light AND
  // dark themes; the Fluent v9 Marigond background/foreground tokens
  // are the closest token-pair to "light yellow" that reads cleanly on
  // both surfaces (mirrors the card-view mid-tier badge).
  similaritySemantic: {
    backgroundColor: tokens.colorPaletteMarigoldBackground2,
    color: tokens.colorPaletteMarigoldForeground2,
  },
  // v1.1.54 (Item 4) — 'associated' rows now show "100%" text in a GREEN
  // pill (matches the Relationship "Same Matter" tint so the row reads
  // as one cohesive Same-Matter+100% block). Previously: blank brand-blue
  // pill. Switched palette to colorPaletteGreenBackground2/Foreground2
  // for contrast parity with the text rendering and to match the card
  // view (ResultCard.tsx) chip styling. Tokens-only per ADR-021.
  similarityAssociated: {
    backgroundColor: tokens.colorPaletteGreenBackground2,
    color: tokens.colorPaletteGreenForeground2,
  },
  iconButton: {
    minWidth: 'auto',
    ...shorthands.padding('0px'),
  },
  selectedRow: {
    backgroundColor: tokens.colorNeutralBackground1Selected,
  },
  pinnedIcon: {
    color: tokens.colorBrandForeground1,
  },
  // Sort indicator on header — small caret rendered after the column label.
  sortHeader: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    cursor: 'pointer',
    userSelect: 'none',
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IListViewProps {
  /** Sorted/filtered results to render. Caller must apply tag filters first;
   *  ListView applies its own sort + pin lift via `sortResults`. */
  results: SearchResult[];

  /** Selected document IDs (owned by parent — persists across view toggles). */
  selectedIds: Set<string>;
  onSelectionChange: (next: Set<string>) => void;

  /** Pinned document IDs (owned by parent via useDocumentListPrefs). */
  pinnedIds: Set<string>;
  onTogglePin: (documentId: string) => void;

  /** Active sort column + direction (owned by parent). */
  sortColumn: ListSortColumn;
  sortDirection: ListSortDirection;
  onSortChange: (next: { column: ListSortColumn; direction: ListSortDirection }) => void;

  /**
   * Persisted column-width overrides (v1.1.45) — keyed by the column IDs
   * exposed at the top of this file (COL_NAME, COL_MODIFIED, etc.).
   * Empty object → use intrinsic defaults.
   */
  columnWidths: ColumnWidths;
  /** Persist a single column-width override. */
  onColumnWidthChange: (columnId: string, width: number) => void;

  /** Row action handlers (mirrors ResultCard.tsx — same DocumentRowMenu dispatch). */
  onOpenFile: (result: SearchResult, mode: 'web' | 'desktop') => void;
  onOpenRecord: (result: SearchResult, inModal: boolean) => void;
  onFindSimilar: (result: SearchResult) => void;
  onPreview: (result: SearchResult) => Promise<string | null>;
  onSummary: (result: SearchResult) => Promise<SummaryData>;
  onEmailDocument: (result: SearchResult) => void;
  onCopyLink: (result: SearchResult) => void;
  onToggleWorkspace: (result: SearchResult) => void;
  isInWorkspace: (result: SearchResult) => boolean;

  /**
   * v1.1.50 (Item 2) — When provided, ListView routes preview-open through
   * the SAME host-mounted FilePreviewDialog used by the card view (Item 6
   * from v1.1.49). The local `<FilePreviewDialog />` instance is suppressed
   * so list AND card views share one navigation set across the entire PCF
   * surface (selection wins; otherwise full sorted/filtered results).
   *
   * When omitted, the legacy local FilePreviewDialog still renders, so
   * any standalone caller / unit test that doesn't wire host preview still
   * works (back-compat).
   */
  onOpenPreview?: (documentId: string) => void;

  /**
   * v1.1.50 (Item 1) — Lazy-load infinite-scroll sentinel. When supplied,
   * a 1-px sentinel row is appended below the DataGrid body and an
   * IntersectionObserver fires this callback when the sentinel enters the
   * viewport. Mirrors `ResultsList.onLoadMoreSentinel` so the host can
   * route both views through one load-more pipeline.
   *
   * When omitted, no sentinel is mounted; existing pagination semantics
   * are unchanged.
   */
  onLoadMoreSentinel?: () => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ListView: React.FC<IListViewProps> = ({
  results,
  selectedIds,
  onSelectionChange,
  pinnedIds,
  onTogglePin,
  sortColumn,
  sortDirection,
  onSortChange,
  columnWidths,
  onColumnWidthChange,
  onOpenFile,
  onOpenRecord,
  onFindSimilar,
  onPreview,
  onSummary,
  onEmailDocument,
  onCopyLink,
  onToggleWorkspace,
  isInWorkspace,
  onOpenPreview,
  onLoadMoreSentinel,
}) => {
  const styles = useStyles();

  // v1.1.50 (Item 2) — Host-level preview routing.
  // When `onOpenPreview` is wired, the host owns the FilePreviewDialog and
  // the local instance is suppressed. We still keep the local-dialog state
  // path for back-compat when callers don't pass `onOpenPreview`.
  const useHostPreview = typeof onOpenPreview === 'function';
  const openPreview = React.useCallback(
    (docId: string) => {
      if (useHostPreview) {
        onOpenPreview!(docId);
      } else {
        setPreviewDocId(docId);
      }
    },
    [useHostPreview, onOpenPreview]
  );

  // Preview dialog state. We store the target's documentId (not the full
  // SearchResult) so the active doc tracks any in-place results updates
  // (e.g., docType override applied while the dialog is open).
  // Only used when useHostPreview === false (back-compat path).
  const [previewDocId, setPreviewDocId] = React.useState<string | null>(null);

  // v1.1.50 (Item 1) — IntersectionObserver sentinel for lazy load.
  // Attached only when the host wired `onLoadMoreSentinel`. Same
  // threshold + rootMargin as ResultsList so both views feel identical
  // when scrolling into the long tail.
  const sentinelRef = React.useRef<HTMLDivElement | null>(null);
  React.useEffect(() => {
    if (!onLoadMoreSentinel) return;
    const node = sentinelRef.current;
    if (!node) return;
    const observer = new IntersectionObserver(
      entries => {
        const [entry] = entries;
        if (entry.isIntersecting) {
          onLoadMoreSentinel();
        }
      },
      { threshold: 0.1, rootMargin: '200px' }
    );
    observer.observe(node);
    return () => observer.disconnect();
  }, [onLoadMoreSentinel]);

  // Sort the results in render (cheap — caller already filtered).
  const sortedResults = React.useMemo(
    () => sortResults(results, sortColumn, sortDirection, pinnedIds),
    [results, sortColumn, sortDirection, pinnedIds]
  );

  // ── Preview navigation set (v1.1.46) ───────────────────────────────────
  // When ≥1 row is selected, the navigation set = the selected docs in the
  // current sort order. Otherwise it's the full sorted result set. This
  // matches the user's mental model: "I selected these 5 docs, Next/Prev
  // walks through the 5" vs "no selection, Next/Prev walks through all".
  //
  // We filter sortedResults (not selectedResults directly) so the nav set
  // honors the current sort/pin order. The fallback to sortedResults is
  // unselected → "browse all" mode.
  const previewNavigationSet = React.useMemo<SearchResult[]>(() => {
    if (selectedIds.size > 0) {
      return sortedResults.filter(r => selectedIds.has(r.documentId));
    }
    return sortedResults;
  }, [selectedIds, sortedResults]);

  // Resolve the currently-shown document from the navigation set. If the
  // active doc was filtered out (e.g., user toggled selection off after
  // opening), fall back to looking it up in the full sortedResults — the
  // dialog stays open on the same doc but loses Prev/Next nav until the
  // user re-selects it (graceful, no jarring re-open on a different doc).
  const previewTarget = React.useMemo<SearchResult | null>(() => {
    if (!previewDocId) return null;
    return (
      previewNavigationSet.find(r => r.documentId === previewDocId) ??
      sortedResults.find(r => r.documentId === previewDocId) ??
      null
    );
  }, [previewDocId, previewNavigationSet, sortedResults]);

  const previewCurrentIndex = React.useMemo<number>(() => {
    if (!previewDocId) return -1;
    return previewNavigationSet.findIndex(r => r.documentId === previewDocId);
  }, [previewDocId, previewNavigationSet]);

  // Navigate the preview dialog. The dialog passes the next 0-based index;
  // we resolve it to a documentId via the nav set and swap state. The
  // dialog's internal effects (preview-url fetch + summary fetch) refire
  // when `documentId` changes.
  const handlePreviewNavigate = React.useCallback(
    (nextIndex: number) => {
      if (nextIndex < 0 || nextIndex >= previewNavigationSet.length) return;
      setPreviewDocId(previewNavigationSet[nextIndex].documentId);
    },
    [previewNavigationSet]
  );

  // ── Header click — toggle direction or switch column ──────────────────
  const handleHeaderClick = React.useCallback(
    (col: ListSortColumn) => {
      if (col === sortColumn) {
        onSortChange({ column: col, direction: sortDirection === 'asc' ? 'desc' : 'asc' });
      } else {
        // Switching column — start at desc for date/score, asc for name (familiar Outlook/Excel default).
        const defaultDir: ListSortDirection = col === 'name' ? 'asc' : 'desc';
        onSortChange({ column: col, direction: defaultDir });
      }
    },
    [sortColumn, sortDirection, onSortChange]
  );

  // ── Selection helpers ─────────────────────────────────────────────────
  const handleToggleRow = React.useCallback(
    (documentId: string) => {
      const next = new Set(selectedIds);
      if (next.has(documentId)) {
        next.delete(documentId);
      } else {
        next.add(documentId);
      }
      onSelectionChange(next);
    },
    [selectedIds, onSelectionChange]
  );

  const allSelected =
    sortedResults.length > 0 && sortedResults.every(r => selectedIds.has(r.documentId));
  const someSelected =
    sortedResults.some(r => selectedIds.has(r.documentId)) && !allSelected;

  const handleToggleAll = React.useCallback(() => {
    if (allSelected) {
      // Deselect every currently-rendered row, preserving any selections outside
      // the current filter set (defensive — selectedIds is parent-owned).
      const next = new Set(selectedIds);
      for (const r of sortedResults) next.delete(r.documentId);
      onSelectionChange(next);
    } else {
      const next = new Set(selectedIds);
      for (const r of sortedResults) next.add(r.documentId);
      onSelectionChange(next);
    }
  }, [allSelected, selectedIds, sortedResults, onSelectionChange]);

  // ── Sort indicator label (▲ / ▼) appended to sortable headers ─────────
  const renderSortCaret = (col: ListSortColumn): string => {
    if (col !== sortColumn) return '';
    return sortDirection === 'asc' ? ' ▲' : ' ▼';
  };

  // ── 3-dot menu dispatch — mirrors ResultCard's handler ────────────────
  // v1.1.50 — `preview` + `aiSummary` route through `openPreview` so they
  // honor the host-level preview dialog when wired (Item 2). When the
  // host isn't wired, openPreview falls back to setPreviewDocId locally.
  const buildRowActionHandler = React.useCallback(
    (result: SearchResult) => (action: DocumentRowAction) => {
      switch (action) {
        case 'preview':
          openPreview(result.documentId);
          return;
        case 'aiSummary':
          // AI summary in the list view = open the preview dialog where the
          // summary section is integrated. Mirrors the design of ResultCard's
          // sparkle popover but without a separate inline popover surface.
          openPreview(result.documentId);
          return;
        case 'openFile':
          onOpenFile(result, 'desktop');
          return;
        case 'findSimilar':
          onFindSimilar(result);
          return;
        case 'download':
          // Download = open in desktop app (same convention as ResultCard).
          onOpenFile(result, 'desktop');
          return;
        case 'copyLink':
          onCopyLink(result);
          return;
        case 'email':
          onEmailDocument(result);
          return;
        case 'openRecord':
          onOpenRecord(result, false);
          return;
        case 'toggleWorkspace':
          onToggleWorkspace(result);
          return;
        case 'pinToTop':
          // Wire pinToTop here so the menu drives the same pin state as the
          // dedicated pin column (FR-DOC-04 Owner Clarification).
          onTogglePin(result.documentId);
          return;
        case 'rename':
        case 'delete':
          // Not yet wired in the PCF surface (Phase 4 follow-on tasks).
          return;
        default: {
          const _never: never = action;
          void _never;
          return;
        }
      }
    },
    [openPreview, onOpenFile, onFindSimilar, onCopyLink, onEmailDocument, onOpenRecord, onToggleWorkspace, onTogglePin]
  );

  // ── DataGrid column definitions ───────────────────────────────────────
  // Build once per render (cheap — closures capture state by reference).
  const columns = React.useMemo<TableColumnDefinition<SearchResult>[]>(() => {
    return [
      // Selection column.
      createTableColumn<SearchResult>({
        columnId: COL_SELECT,
        renderHeaderCell: () => (
          <Checkbox
            aria-label={allSelected ? 'Deselect all documents' : 'Select all documents'}
            checked={allSelected ? true : someSelected ? 'mixed' : false}
            onChange={handleToggleAll}
          />
        ),
        renderCell: (result: SearchResult) => {
          const isSelected = selectedIds.has(result.documentId);
          return (
            <Checkbox
              aria-label={`Select ${result.name}`}
              checked={isSelected}
              onChange={() => handleToggleRow(result.documentId)}
              onClick={ev => ev.stopPropagation()}
            />
          );
        },
      }),

      // Pin column.
      createTableColumn<SearchResult>({
        columnId: COL_PIN,
        renderHeaderCell: () => <span aria-label="Pinned" />,
        renderCell: (result: SearchResult) => {
          const isPinned = pinnedIds.has(result.documentId);
          if (!isPinned) return <span aria-hidden="true" />;
          return (
            <Tooltip content="Unpin" relationship="label">
              <Button
                appearance="subtle"
                size="small"
                className={mergeClasses(styles.iconButton, styles.pinnedIcon)}
                icon={<Pin20Filled aria-label="Pinned" />}
                onClick={ev => {
                  ev.stopPropagation();
                  onTogglePin(result.documentId);
                }}
                aria-label={`Unpin ${result.name}`}
              />
            </Tooltip>
          );
        },
      }),

      // Document column (v1.1.50 — renamed from "Name") — sortable, ellipsis-truncates.
      // Sort key still resolves to 'name' so persisted user prefs survive.
      createTableColumn<SearchResult>({
        columnId: COL_DOCUMENT,
        renderHeaderCell: () => (
          <span
            className={styles.sortHeader}
            onClick={() => handleHeaderClick('name')}
            role="button"
            aria-label={`Sort by document name — currently ${
              sortColumn === 'name' ? sortDirection : 'unsorted'
            }`}
          >
            Document{renderSortCaret('name')}
          </span>
        ),
        renderCell: (result: SearchResult) => {
          // v1.1.53 (Item 4 Part A) — File-type icon removed from the
          // Document column; the icon now lives in the dedicated Type
          // column. The cell shows the document name link only.
          return (
            <span className={styles.nameWrap} title={result.name}>
              <Link
                as="button"
                appearance="subtle"
                className={styles.nameLink}
                onClick={ev => {
                  ev.stopPropagation();
                  openPreview(result.documentId);
                }}
              >
                {result.name}
              </Link>
            </span>
          );
        },
      }),

      // Relationship column (v1.1.50, Item 5) — NEW, non-sortable.
      // v1.1.51 (Items 2 + 7):
      //   - Tag union extended to `'both'` to handle docs that appear in
      //     BOTH the Dataverse-direct AND semantic paths. 'both' renders
      //     "Same Matter" (the canonical/stronger label, per UAT) but the
      //     Similarity column still shows the semantic % (Item 2 fix).
      //   - Badges switched from `appearance="filled"` to
      //     `appearance="tint"` so the green/blue pills read lighter
      //     (Item 7 UAT — "too bright").
      createTableColumn<SearchResult>({
        columnId: COL_RELATIONSHIP,
        renderHeaderCell: () => <span>Relationship</span>,
        renderCell: (result: SearchResult) => {
          const rel: 'associated' | 'semantic' | 'both' =
            result.relationship ??
            ((result.combinedScore ?? 0) === 0 ? 'associated' : 'semantic');
          // 'both' renders as Same Matter — the direct association is the
          // canonical, stronger relationship signal even when a semantic
          // match exists.
          if (rel === 'associated' || rel === 'both') {
            return (
              <Badge appearance="tint" color="success" size="medium" shape="rounded">
                Same Matter
              </Badge>
            );
          }
          return (
            <Badge appearance="tint" color="brand" size="medium" shape="rounded">
              Semantic
            </Badge>
          );
        },
      }),

      // Similarity column (v1.1.50 — renamed from "Match") — sortable.
      // Sort key still resolves to 'combinedScore' so persisted user prefs
      // survive. Pill styling depends on the row's relationship:
      //   - 'associated' → green pill with "100%" text (v1.1.54 Item 4 —
      //                    direct associations are always 100% match; the
      //                    green palette matches the "Same Matter" pill so
      //                    the row reads as a single Same-Matter+100% block)
      //   - 'semantic'   → light-yellow (Marigold) pill, WITH percentage
      //   - 'both'       → light-yellow (Marigold) pill WITH semantic %
      //                    (v1.1.51 Item 2 — surfaces the semantic match
      //                    score even when the doc is also directly
      //                    associated, per the user's UAT request)
      createTableColumn<SearchResult>({
        columnId: COL_SIMILARITY,
        renderHeaderCell: () => (
          <span
            className={styles.sortHeader}
            onClick={() => handleHeaderClick('combinedScore')}
            role="button"
            aria-label={`Sort by similarity — currently ${
              sortColumn === 'combinedScore' ? sortDirection : 'unsorted'
            }`}
          >
            Similarity{renderSortCaret('combinedScore')}
          </span>
        ),
        renderCell: (result: SearchResult) => {
          const rel: 'associated' | 'semantic' | 'both' =
            result.relationship ??
            ((result.combinedScore ?? 0) === 0 ? 'associated' : 'semantic');
          if (rel === 'associated') {
            // v1.1.54 (Item 4) — direct-association rows render "100%" in
            // a green pill (was: blank brand-blue pill). Mirrors the card
            // view's similarityAssociated rendering.
            return (
              <span
                className={mergeClasses(styles.similarityBase, styles.similarityAssociated)}
                role="img"
                aria-label="Direct association: 100%"
              >
                100%
              </span>
            );
          }
          // 'semantic' or 'both' — both surface the semantic % score.
          const pct = Math.round((result.combinedScore ?? 0) * 100);
          return (
            <span
              className={mergeClasses(styles.similarityBase, styles.similaritySemantic)}
              role="img"
              aria-label={`Semantic similarity: ${pct}%`}
            >
              {pct}%
            </span>
          );
        },
      }),

      // Type column (v1.1.50, Item 3) — NEW, non-sortable.
      // v1.1.53 (Item 4 Part B) — Renders the file-type icon only (no
      // text). The icon was moved here from the Document column. The
      // surrounding span carries the type label as title + aria-label
      // so screen-readers still read the type when navigating.
      createTableColumn<SearchResult>({
        columnId: COL_TYPE,
        renderHeaderCell: () => <span>Type</span>,
        renderCell: (result: SearchResult) => {
          const IconComp = getFileIcon(result.fileType);
          const typeLabel =
            (result.documentType && result.documentType.trim().length > 0
              ? result.documentType
              : result.fileType) || 'Document';
          return (
            <span title={typeLabel} aria-label={typeLabel}>
              <IconComp fontSize={20} aria-hidden="true" />
            </span>
          );
        },
      }),

      // Modified column — sortable.
      createTableColumn<SearchResult>({
        columnId: COL_MODIFIED,
        renderHeaderCell: () => (
          <span
            className={styles.sortHeader}
            onClick={() => handleHeaderClick('modifiedAt')}
            role="button"
            aria-label={`Sort by modified date — currently ${
              sortColumn === 'modifiedAt' ? sortDirection : 'unsorted'
            }`}
          >
            Modified{renderSortCaret('modifiedAt')}
          </span>
        ),
        renderCell: (result: SearchResult) => (
          <Text size={200}>{formatShortDate(result.modifiedAt)}</Text>
        ),
      }),

      // v1.1.54 (Item 6) — COL_AI (AI Summary sparkle) column removed.
      // The AI Summary action is hidden across all surfaces (also hidden
      // via `disabledActions` in the row menu below). Reduces column set
      // to: Select / Pin / Document / Relationship / Similarity / Type /
      // Modified / Menu.

      // 3-dot menu column — non-sortable.
      // v1.1.54 (Item 6) — `disabledActions` standardized to match card
      // view + dialog: hide AI Summary, Toggle workspace, Rename.
      // Visible: Preview, Open File, Find Similar, Download, Copy link,
      // Email, Open Record, Pin to top, Delete.
      createTableColumn<SearchResult>({
        columnId: COL_MENU,
        renderHeaderCell: () => <span aria-label="Actions" />,
        renderCell: (result: SearchResult) => {
          const target: IDocumentRowMenuTarget = {
            id: result.documentId,
            name: result.name,
            documentType: result.documentType,
          };
          return (
            <DocumentRowMenu
              document={target}
              onAction={buildRowActionHandler(result)}
              disabledActions={['aiSummary', 'toggleWorkspace', 'rename']}
            />
          );
        },
      }),
    ];
  }, [
    allSelected,
    someSelected,
    handleToggleAll,
    selectedIds,
    handleToggleRow,
    pinnedIds,
    onTogglePin,
    sortColumn,
    sortDirection,
    handleHeaderClick,
    buildRowActionHandler,
    openPreview,
    // v1.1.54 (Item 6) — `onSummary` removed from this dep list; the
    // COL_AI cell that consumed it is gone. The prop is still used by
    // the local fallback FilePreviewDialog mounted below.
    styles.iconButton,
    styles.pinnedIcon,
    styles.nameWrap,
    styles.nameLink,
    styles.similarityBase,
    styles.similaritySemantic,
    styles.similarityAssociated,
    styles.sortHeader,
    // styles.menuCell is read via mergeClasses in the row render below, not in
    // the columns memo — keeping it out of this dep list is correct.
  ]);

  // ── Column sizing options ─────────────────────────────────────────────
  // Merge persisted user widths with intrinsic defaults so unset columns
  // still get a sane initial track width.
  const columnSizingOptions: TableColumnSizingOptions = React.useMemo(() => {
    const opts: TableColumnSizingOptions = {};
    // v1.1.50 — new column set (Items 3 + 4 + 5). COL_MODIFIED_BY removed,
    // COL_RELATIONSHIP / COL_TYPE / COL_AI added. Same iteration order as
    // the createTableColumn() order above so sizing keys stay in sync.
    // v1.1.54 (Item 6) — COL_AI dropped from this iteration; user-
    // persisted COL_AI widths linger as orphan keys in localStorage but
    // do not affect rendering.
    const columnIds = [
      COL_SELECT,
      COL_PIN,
      COL_DOCUMENT,
      COL_RELATIONSHIP,
      COL_SIMILARITY,
      COL_TYPE,
      COL_MODIFIED,
      COL_MENU,
    ];
    for (const id of columnIds) {
      opts[id] = {
        minWidth: MIN_WIDTHS[id],
        idealWidth: columnWidths[id] ?? DEFAULT_WIDTHS[id],
      };
    }
    return opts;
  }, [columnWidths]);

  // DataGrid `onColumnResize` fires on every drag tick; we persist on each
  // event (writes-through to localStorage). Cheap enough — the volume is
  // limited by user drag speed, not by render.
  const handleColumnResize = React.useCallback(
    (_ev: unknown, data: { columnId: unknown; width: number }) => {
      const id = data.columnId;
      if (typeof id === 'string' && typeof data.width === 'number') {
        onColumnWidthChange(id, data.width);
      }
    },
    [onColumnWidthChange]
  );

  // Row click → open preview (matches Card view's row-click behavior in ResultCard).
  // v1.1.50 — routes through `openPreview` so the host-level dialog is used
  // when wired (Item 2).
  const handleRowClick = React.useCallback(
    (ev: React.MouseEvent, result: SearchResult) => {
      // Don't fire on buttons, checkboxes, links, or the menu trigger.
      const target = ev.target as HTMLElement;
      if (target.closest('button') || target.closest('input') || target.closest('a')) return;
      openPreview(result.documentId);
    },
    [openPreview]
  );

  return (
    <>
      <div className={styles.container} role="region" aria-label="Document list">
        <DataGrid
          items={sortedResults}
          columns={columns}
          getRowId={(item: SearchResult) => item.documentId}
          resizableColumns
          columnSizingOptions={columnSizingOptions}
          resizableColumnsOptions={{
            autoFitColumns: false,
          }}
          onColumnResize={handleColumnResize}
          size="medium"
          aria-label="Documents"
        >
          <DataGridHeader>
            <DataGridRow>
              {({ renderHeaderCell, columnId }) => {
                // v1.1.63 — header menu cell mirrors the body's
                // `marginInlineStart: auto` so the header alignment tracks
                // the right-edge flush of the body menu cell. See styles.menuCell.
                const isMenuHeader = columnId === COL_MENU;
                return (
                  <DataGridHeaderCell
                    className={mergeClasses(
                      styles.headerCell,
                      styles.gridCell,
                      isMenuHeader && styles.menuCell
                    )}
                  >
                    {renderHeaderCell()}
                  </DataGridHeaderCell>
                );
              }}
            </DataGridRow>
          </DataGridHeader>
          <DataGridBody<SearchResult>>
            {({ item, rowId }) => {
              const isSelected = selectedIds.has(item.documentId);
              return (
                <DataGridRow<SearchResult>
                  key={rowId}
                  className={isSelected ? styles.selectedRow : undefined}
                  onClick={(ev: React.MouseEvent) => handleRowClick(ev, item)}
                >
                  {({ renderCell, columnId }) => {
                    const isSelectCell = columnId === COL_SELECT;
                    const isPinCell = columnId === COL_PIN;
                    const isMenuCell = columnId === COL_MENU;
                    // v1.1.54 (Item 6) — COL_AI removed; the `isAiCell`
                    // branch is gone. Only the menu cell now gets the
                    // flush-right styling.
                    const cellClass = mergeClasses(
                      styles.gridCell,
                      isSelectCell && styles.selectCell,
                      isPinCell && styles.pinCell,
                      // v1.1.47 — flush the menu icon to the right edge.
                      isMenuCell && styles.menuCell
                    );
                    return (
                      <DataGridCell
                        className={cellClass}
                        onClick={isPinCell ? (ev: React.MouseEvent) => {
                          ev.stopPropagation();
                          onTogglePin(item.documentId);
                        } : undefined}
                      >
                        <TableCellLayout truncate={!isSelectCell && !isPinCell && !isMenuCell}>
                          {renderCell(item)}
                        </TableCellLayout>
                      </DataGridCell>
                    );
                  }}
                </DataGridRow>
              );
            }}
          </DataGridBody>
        </DataGrid>

        {/* v1.1.50 (Item 1) — Lazy-load sentinel.
            Rendered INSIDE the scrollable `styles.container` so the
            IntersectionObserver fires relative to the DataGrid scroll
            surface, not the page viewport. Same threshold + rootMargin
            as ResultsList for parity. Renders only when the host wired
            `onLoadMoreSentinel` — back-compat: when omitted, no observer
            is attached and pagination semantics are unchanged. */}
        {onLoadMoreSentinel && (
          <div
            ref={sentinelRef}
            style={{ height: '1px', width: '100%' }}
            aria-hidden="true"
          />
        )}
      </div>

      {/* Preview dialog — instantiated once at the list level; opens for whichever
          row triggered it via menu or row click.
          v1.1.50 (Item 2) — suppressed when the host wired `onOpenPreview`.
          In that mode the host owns a single FilePreviewDialog shared with
          the card view so Prev/Next navigates one cross-view nav set.
          Back-compat path: when `onOpenPreview` is omitted, the local
          dialog still renders.
          v1.1.46 — receives navigationTotal/currentIndex/onNavigate so the
          footer can render Prev/Next when the navigation set has >1 item.
          The navigation set is `previewNavigationSet` (selected docs when
          ≥1 selected; full sortedResults otherwise). All callbacks close
          over `previewTarget` so they always act on the CURRENT active
          doc — when the user clicks Next, previewTarget updates via the
          previewDocId state change, and all closures rebind on the next
          render. */}
      {!useHostPreview && previewTarget && (
        <FilePreviewDialog
          open={!!previewTarget}
          documentName={previewTarget.name}
          documentId={previewTarget.documentId}
          documentType={previewTarget.documentType}
          createdAt={previewTarget.createdAt}
          createdBy={previewTarget.createdBy}
          onClose={() => setPreviewDocId(null)}
          fetchPreviewUrl={() => onPreview(previewTarget)}
          onFetchSummary={() => onSummary(previewTarget)}
          onOpenFile={mode => onOpenFile(previewTarget, mode)}
          onOpenRecord={() => onOpenRecord(previewTarget, false)}
          onEmailDocument={() => onEmailDocument(previewTarget)}
          onCopyLink={() => onCopyLink(previewTarget)}
          onToggleWorkspace={() => onToggleWorkspace(previewTarget)}
          isInWorkspace={isInWorkspace(previewTarget)}
          onFindSimilar={() => onFindSimilar(previewTarget)}
          navigationTotal={previewNavigationSet.length}
          currentIndex={previewCurrentIndex >= 0 ? previewCurrentIndex : undefined}
          onNavigate={handlePreviewNavigate}
        />
      )}
    </>
  );
};

export default ListView;
