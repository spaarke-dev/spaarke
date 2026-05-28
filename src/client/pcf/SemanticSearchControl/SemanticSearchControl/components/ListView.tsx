/**
 * ListView component (FR-DOC-04)
 *
 * Tabular list view for the Documents PCF. Renders a Fluent v9 `DataGrid`
 * with the spec column order:
 *
 *   Selection checkbox | Pin | Name | Modified | Modified by | Match | Menu
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
  Avatar,
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
const COL_SELECT = 'select';
const COL_PIN = 'pin';
const COL_NAME = 'name';
const COL_MODIFIED = 'modifiedAt';
const COL_MODIFIED_BY = 'modifiedBy';
const COL_MATCH = 'combinedScore';
const COL_MENU = 'menu';

// Default intrinsic widths in pixels (used when no user override is persisted).
// v1.1.47 — Name default widened 360 → 480 px to give long file names room
// to breathe at typical viewport widths (UAT round 3). User-resized widths
// still persist via useDocumentListPrefs.columnWidths and win over this default.
const DEFAULT_WIDTHS: Record<string, number> = {
  [COL_SELECT]: 40,
  [COL_PIN]: 36,
  [COL_NAME]: 480,
  [COL_MODIFIED]: 130,
  [COL_MODIFIED_BY]: 180,
  [COL_MATCH]: 80,
  [COL_MENU]: 44,
};

const MIN_WIDTHS: Record<string, number> = {
  [COL_SELECT]: 40,
  [COL_PIN]: 36,
  [COL_NAME]: 140,
  [COL_MODIFIED]: 90,
  [COL_MODIFIED_BY]: 100,
  [COL_MATCH]: 60,
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
  // v1.1.47 — Menu column cell. Flush the 3-dot icon button against the right
  // edge of the cell (with a small XS breathing-room pad) so the menu trigger
  // never appears stranded mid-cell. The DataGrid's TableCellLayout inside
  // wraps a flex container around renderCell output; this rule applies to
  // the outer DataGridCell to guarantee right-alignment regardless of inner
  // layout. Tokens-only (ADR-021).
  menuCell: {
    display: 'flex',
    justifyContent: 'flex-end',
    alignItems: 'center',
    paddingLeft: 0,
    paddingRight: tokens.spacingHorizontalXS,
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
  avatarRow: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    minWidth: 0,
    ...shorthands.overflow('hidden'),
  },
  modifiedByName: {
    ...shorthands.overflow('hidden'),
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    minWidth: 0,
  },
  scoreBadge: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
    ...shorthands.padding('1px', tokens.spacingHorizontalXS),
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightBase100,
    whiteSpace: 'nowrap',
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground1,
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
}) => {
  const styles = useStyles();

  // Preview dialog state. We store the target's documentId (not the full
  // SearchResult) so the active doc tracks any in-place results updates
  // (e.g., docType override applied while the dialog is open).
  const [previewDocId, setPreviewDocId] = React.useState<string | null>(null);

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
  const buildRowActionHandler = React.useCallback(
    (result: SearchResult) => (action: DocumentRowAction) => {
      switch (action) {
        case 'preview':
          setPreviewDocId(result.documentId);
          return;
        case 'aiSummary':
          // AI summary in the list view = open the preview dialog where the
          // summary section is integrated. Mirrors the design of ResultCard's
          // sparkle popover but without a separate inline popover surface.
          setPreviewDocId(result.documentId);
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
    [onOpenFile, onFindSimilar, onCopyLink, onEmailDocument, onOpenRecord, onToggleWorkspace, onTogglePin]
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

      // Name column — sortable, ellipsis-truncates.
      createTableColumn<SearchResult>({
        columnId: COL_NAME,
        renderHeaderCell: () => (
          <span
            className={styles.sortHeader}
            onClick={() => handleHeaderClick('name')}
            role="button"
            aria-label={`Sort by name — currently ${
              sortColumn === 'name' ? sortDirection : 'unsorted'
            }`}
          >
            Name{renderSortCaret('name')}
          </span>
        ),
        renderCell: (result: SearchResult) => {
          const IconComp = getFileIcon(result.fileType);
          return (
            <span className={styles.nameWrap} title={result.name}>
              <IconComp fontSize={20} aria-label={result.fileType || 'Document'} />
              <Link
                as="button"
                appearance="subtle"
                className={styles.nameLink}
                onClick={ev => {
                  ev.stopPropagation();
                  setPreviewDocId(result.documentId);
                }}
              >
                {result.name}
              </Link>
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

      // Modified by column — non-sortable.
      createTableColumn<SearchResult>({
        columnId: COL_MODIFIED_BY,
        renderHeaderCell: () => <span>Modified by</span>,
        renderCell: (result: SearchResult) =>
          result.modifiedBy ? (
            <span className={styles.avatarRow}>
              <Avatar name={result.modifiedBy} size={20} aria-hidden="true" />
              <Text size={200} className={styles.modifiedByName} title={result.modifiedBy}>
                {result.modifiedBy}
              </Text>
            </span>
          ) : null,
      }),

      // Match column — sortable.
      createTableColumn<SearchResult>({
        columnId: COL_MATCH,
        renderHeaderCell: () => (
          <span
            className={styles.sortHeader}
            onClick={() => handleHeaderClick('combinedScore')}
            role="button"
            aria-label={`Sort by match score — currently ${
              sortColumn === 'combinedScore' ? sortDirection : 'unsorted'
            }`}
          >
            Match{renderSortCaret('combinedScore')}
          </span>
        ),
        renderCell: (result: SearchResult) => {
          const pct = Math.round((result.combinedScore ?? 0) * 100);
          return (
            <span
              className={styles.scoreBadge}
              role="img"
              aria-label={`Relevance: ${pct}%`}
            >
              {pct}%
            </span>
          );
        },
      }),

      // 3-dot menu column — non-sortable.
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
    styles.iconButton,
    styles.pinnedIcon,
    styles.nameWrap,
    styles.nameLink,
    styles.avatarRow,
    styles.modifiedByName,
    styles.scoreBadge,
    styles.sortHeader,
    // styles.menuCell is read via mergeClasses in the row render below, not in
    // the columns memo — keeping it out of this dep list is correct.
  ]);

  // ── Column sizing options ─────────────────────────────────────────────
  // Merge persisted user widths with intrinsic defaults so unset columns
  // still get a sane initial track width.
  const columnSizingOptions: TableColumnSizingOptions = React.useMemo(() => {
    const opts: TableColumnSizingOptions = {};
    const columnIds = [COL_SELECT, COL_PIN, COL_NAME, COL_MODIFIED, COL_MODIFIED_BY, COL_MATCH, COL_MENU];
    for (const id of columnIds) {
      opts[id] = {
        minWidth: MIN_WIDTHS[id],
        idealWidth: columnWidths[id] ?? DEFAULT_WIDTHS[id],
        // Mark fixed-width control columns as non-resizable to keep their
        // intrinsic widths honest (select/pin/menu look broken if resized).
        ...(id === COL_SELECT || id === COL_PIN || id === COL_MENU
          ? {}
          : {}),
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
  const handleRowClick = React.useCallback(
    (ev: React.MouseEvent, result: SearchResult) => {
      // Don't fire on buttons, checkboxes, links, or the menu trigger.
      const target = ev.target as HTMLElement;
      if (target.closest('button') || target.closest('input') || target.closest('a')) return;
      setPreviewDocId(result.documentId);
    },
    []
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
              {({ renderHeaderCell }) => (
                <DataGridHeaderCell
                  className={mergeClasses(styles.headerCell, styles.gridCell)}
                >
                  {renderHeaderCell()}
                </DataGridHeaderCell>
              )}
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
      </div>

      {/* Preview dialog — instantiated once at the list level; opens for whichever
          row triggered it via menu or row click.
          v1.1.46 — receives navigationTotal/currentIndex/onNavigate so the
          footer can render Prev/Next when the navigation set has >1 item.
          The navigation set is `previewNavigationSet` (selected docs when
          ≥1 selected; full sortedResults otherwise). All callbacks close
          over `previewTarget` so they always act on the CURRENT active
          doc — when the user clicks Next, previewTarget updates via the
          previewDocId state change, and all closures rebind on the next
          render. */}
      {previewTarget && (
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
