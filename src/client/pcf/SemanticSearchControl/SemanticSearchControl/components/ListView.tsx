/**
 * ListView component (FR-DOC-04)
 *
 * Tabular list view for the Documents PCF. Renders a Fluent v9 `Table` with
 * the spec column order:
 *
 *   Selection checkbox | Pin | Name | Modified | Modified by | Match | Menu
 *
 * Sortable columns: Name, Modified, Match. Pin is non-sortable and the icon
 * is rendered ONLY on pinned rows. Pinned rows always sort to the top
 * regardless of the active column sort.
 *
 * Selection state is owned by the parent (`SemanticSearchControl`) so it
 * persists across list/card view toggles. Pin state is also owned by the
 * parent (via `useDocumentListPrefs`) so it survives reload.
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
  Link,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Text,
  Tooltip,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import {
  ArrowSortDown20Regular,
  ArrowSortUp20Regular,
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

// ---------------------------------------------------------------------------
// Sort contract
// ---------------------------------------------------------------------------

/** Columns the user can sort by. Pin is intentionally not sortable. */
export type ListSortColumn = 'name' | 'modifiedAt' | 'combinedScore';

/** Sort direction toggle. */
export type ListSortDirection = 'asc' | 'desc';

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
    overflowX: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  // Selection column — narrow, just enough for the checkbox.
  selectCell: {
    width: '40px',
    minWidth: '40px',
  },
  // Pin column — narrow. Icon only renders on pinned rows.
  pinCell: {
    width: '32px',
    minWidth: '32px',
    cursor: 'pointer',
    textAlign: 'center',
  },
  // Match column — keeps the score badge compact.
  matchCell: {
    width: '80px',
    minWidth: '80px',
  },
  modifiedCell: {
    width: '120px',
    minWidth: '120px',
  },
  modifiedByCell: {
    minWidth: '140px',
  },
  // Menu column — single icon button.
  menuCell: {
    width: '40px',
    minWidth: '40px',
  },
  headerSortable: {
    cursor: 'pointer',
    userSelect: 'none',
  },
  headerContent: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
  iconCell: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    minWidth: 0,
  },
  nameLink: {
    cursor: 'pointer',
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorBrandForegroundLink,
  },
  avatarRow: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    minWidth: 0,
  },
  modifiedByName: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  scoreBadge: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
    paddingTop: '1px',
    paddingBottom: '1px',
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

  // Preview dialog state (mirrors ResultCard's per-row dialog so we don't
  // touch FilePreviewDialog.tsx — task 044 owns that file).
  const [previewTarget, setPreviewTarget] = React.useState<SearchResult | null>(null);

  // Sort the results in render (cheap — caller already filtered).
  const sortedResults = React.useMemo(
    () => sortResults(results, sortColumn, sortDirection, pinnedIds),
    [results, sortColumn, sortDirection, pinnedIds]
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

  // ── Sort indicator for column headers ─────────────────────────────────
  const renderSortIndicator = (col: ListSortColumn): React.ReactNode => {
    if (col !== sortColumn) return null;
    return sortDirection === 'asc' ? (
      <ArrowSortUp20Regular aria-hidden="true" />
    ) : (
      <ArrowSortDown20Regular aria-hidden="true" />
    );
  };

  // ── 3-dot menu dispatch — mirrors ResultCard's handler ────────────────
  const buildRowActionHandler = React.useCallback(
    (result: SearchResult) => (action: DocumentRowAction) => {
      switch (action) {
        case 'preview':
          setPreviewTarget(result);
          return;
        case 'aiSummary':
          // AI summary in the list view = open the preview dialog where the
          // summary section is integrated. Mirrors the design of ResultCard's
          // sparkle popover but without a separate inline popover surface.
          setPreviewTarget(result);
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

  // Row click → open preview (matches Card view's row-click behavior in ResultCard).
  const handleRowClick = React.useCallback(
    (ev: React.MouseEvent, result: SearchResult) => {
      // Don't fire on buttons, checkboxes, links, or the menu trigger.
      const target = ev.target as HTMLElement;
      if (target.closest('button') || target.closest('input') || target.closest('a')) return;
      setPreviewTarget(result);
    },
    []
  );

  return (
    <>
      <div className={styles.container} role="region" aria-label="Document list">
        <Table aria-label="Documents" size="small">
          <TableHeader>
            <TableRow>
              {/* Selection column header — master checkbox */}
              <TableHeaderCell className={styles.selectCell}>
                <Checkbox
                  aria-label={allSelected ? 'Deselect all documents' : 'Select all documents'}
                  checked={allSelected ? true : someSelected ? 'mixed' : false}
                  onChange={handleToggleAll}
                />
              </TableHeaderCell>

              {/* Pin column — non-sortable, icon-free header */}
              <TableHeaderCell className={styles.pinCell} aria-label="Pinned">
                {/* Empty header — pin status is per-row only */}
              </TableHeaderCell>

              {/* Name column — sortable */}
              <TableHeaderCell
                className={styles.headerSortable}
                aria-sort={
                  sortColumn === 'name'
                    ? sortDirection === 'asc'
                      ? 'ascending'
                      : 'descending'
                    : 'none'
                }
                onClick={() => handleHeaderClick('name')}
                role="columnheader button"
              >
                <span className={styles.headerContent}>
                  Name
                  {renderSortIndicator('name')}
                </span>
              </TableHeaderCell>

              {/* Modified column — sortable */}
              <TableHeaderCell
                className={`${styles.modifiedCell} ${styles.headerSortable}`}
                aria-sort={
                  sortColumn === 'modifiedAt'
                    ? sortDirection === 'asc'
                      ? 'ascending'
                      : 'descending'
                    : 'none'
                }
                onClick={() => handleHeaderClick('modifiedAt')}
                role="columnheader button"
              >
                <span className={styles.headerContent}>
                  Modified
                  {renderSortIndicator('modifiedAt')}
                </span>
              </TableHeaderCell>

              {/* Modified by column — non-sortable */}
              <TableHeaderCell className={styles.modifiedByCell}>Modified by</TableHeaderCell>

              {/* Match column — sortable (relevance score) */}
              <TableHeaderCell
                className={`${styles.matchCell} ${styles.headerSortable}`}
                aria-sort={
                  sortColumn === 'combinedScore'
                    ? sortDirection === 'asc'
                      ? 'ascending'
                      : 'descending'
                    : 'none'
                }
                onClick={() => handleHeaderClick('combinedScore')}
                role="columnheader button"
              >
                <span className={styles.headerContent}>
                  Match
                  {renderSortIndicator('combinedScore')}
                </span>
              </TableHeaderCell>

              {/* Menu column header — empty */}
              <TableHeaderCell className={styles.menuCell} aria-label="Actions" />
            </TableRow>
          </TableHeader>

          <TableBody>
            {sortedResults.map(result => {
              const IconComp = getFileIcon(result.fileType);
              const isSelected = selectedIds.has(result.documentId);
              const isPinned = pinnedIds.has(result.documentId);
              const target: IDocumentRowMenuTarget = {
                id: result.documentId,
                name: result.name,
                documentType: result.documentType,
              };
              const pct = Math.round((result.combinedScore ?? 0) * 100);

              return (
                <TableRow
                  key={result.documentId}
                  className={isSelected ? styles.selectedRow : undefined}
                  onClick={ev => handleRowClick(ev, result)}
                >
                  {/* Selection checkbox */}
                  <TableCell className={styles.selectCell}>
                    <Checkbox
                      aria-label={`Select ${result.name}`}
                      checked={isSelected}
                      onChange={() => handleToggleRow(result.documentId)}
                      onClick={ev => ev.stopPropagation()}
                    />
                  </TableCell>

                  {/* Pin icon — visible ONLY on pinned rows per spec FR-DOC-04 */}
                  <TableCell
                    className={styles.pinCell}
                    onClick={ev => {
                      ev.stopPropagation();
                      onTogglePin(result.documentId);
                    }}
                  >
                    {isPinned ? (
                      <Tooltip content="Unpin" relationship="label">
                        <Button
                          appearance="subtle"
                          size="small"
                          className={`${styles.iconButton} ${styles.pinnedIcon}`}
                          icon={<Pin20Filled aria-label="Pinned" />}
                          onClick={ev => {
                            ev.stopPropagation();
                            onTogglePin(result.documentId);
                          }}
                          aria-label={`Unpin ${result.name}`}
                        />
                      </Tooltip>
                    ) : (
                      // Empty cell on unpinned rows — pin affordance discovered via the 3-dot menu's "Pin to top".
                      <span aria-hidden="true" />
                    )}
                  </TableCell>

                  {/* Name — file icon + link-styled name */}
                  <TableCell>
                    <span className={styles.iconCell}>
                      <IconComp fontSize={20} aria-label={result.fileType || 'Document'} />
                      <Link
                        as="button"
                        appearance="subtle"
                        className={styles.nameLink}
                        onClick={ev => {
                          ev.stopPropagation();
                          setPreviewTarget(result);
                        }}
                      >
                        {result.name}
                      </Link>
                    </span>
                  </TableCell>

                  {/* Modified date */}
                  <TableCell className={styles.modifiedCell}>
                    <Text size={200}>{formatShortDate(result.modifiedAt)}</Text>
                  </TableCell>

                  {/* Modified by — avatar + name */}
                  <TableCell className={styles.modifiedByCell}>
                    {result.modifiedBy ? (
                      <span className={styles.avatarRow}>
                        <Avatar name={result.modifiedBy} size={20} aria-hidden="true" />
                        <Text size={200} className={styles.modifiedByName}>
                          {result.modifiedBy}
                        </Text>
                      </span>
                    ) : null}
                  </TableCell>

                  {/* Match score */}
                  <TableCell className={styles.matchCell}>
                    <span
                      className={styles.scoreBadge}
                      role="img"
                      aria-label={`Relevance: ${pct}%`}
                    >
                      {pct}%
                    </span>
                  </TableCell>

                  {/* 3-dot menu (reuses shared DocumentRowMenu) */}
                  <TableCell
                    className={styles.menuCell}
                    onClick={ev => ev.stopPropagation()}
                  >
                    <DocumentRowMenu
                      document={target}
                      onAction={buildRowActionHandler(result)}
                    />
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>

      {/* Preview dialog — instantiated once at the list level; opens for whichever
          row triggered it via menu or row click. We deliberately do NOT touch
          FilePreviewDialog.tsx (task 044 is running in parallel on that file). */}
      {previewTarget && (
        <FilePreviewDialog
          open={!!previewTarget}
          documentName={previewTarget.name}
          documentId={previewTarget.documentId}
          documentType={previewTarget.documentType}
          createdAt={previewTarget.createdAt}
          createdBy={previewTarget.createdBy}
          onClose={() => setPreviewTarget(null)}
          fetchPreviewUrl={() => onPreview(previewTarget)}
          onFetchSummary={() => onSummary(previewTarget)}
          onOpenFile={mode => onOpenFile(previewTarget, mode)}
          onOpenRecord={() => onOpenRecord(previewTarget, false)}
          onEmailDocument={() => onEmailDocument(previewTarget)}
          onCopyLink={() => onCopyLink(previewTarget)}
          onToggleWorkspace={() => onToggleWorkspace(previewTarget)}
          isInWorkspace={isInWorkspace(previewTarget)}
          onFindSimilar={() => onFindSimilar(previewTarget)}
        />
      )}
    </>
  );
};

export default ListView;
