/**
 * DocumentsTab — embedded tab wrapper for the Documents record list.
 *
 * Uses useDocumentsTabList (broad filter) and renders DocumentCard items.
 *
 * Two rendering modes:
 *   - DEFAULT: vertical RecordCardList (stacked cards with IntersectionObserver
 *     windowing). Used by the standalone Corporate Workspace.
 *   - GRID: when `gridMode` is provided, renders a CSS grid (e.g. 2 wide × 10
 *     tall = 20 cards max). Used by the R8 Wave 3 Dataverse-seeded "Documents"
 *     system workspace embedded via WorkspaceLayoutWidget → LegalWorkspaceApp.
 *     Operator (R8, 2026-05-22): the embedded Documents widget shows a fixed
 *     2×10 grid with NO scrollbar; documents past the cap are reachable via
 *     the section toolbar's "Open documents list" affordance.
 */

import * as React from "react";
import { Button, makeStyles, tokens } from "@fluentui/react-components";
import { DataverseService } from "../../services/DataverseService";
import { useDocumentsTabList } from "../../hooks/useDocumentsTabList";
import { DocumentCard } from "./DocumentCard";
import { RecordCardList } from "./RecordCardList";

// ---------------------------------------------------------------------------
// Styles (grid mode only — vertical mode uses RecordCardList's own styles)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  gridContainer: {
    display: "grid",
    gridTemplateColumns: "repeat(2, minmax(0, 1fr))",
    gridAutoRows: "auto",
    gap: tokens.spacingHorizontalM,
    flex: "1 1 0",
    // Hide scrollbars defensively — the item cap below ensures no overflow,
    // but if any ancestor introduces overflow, no visible bar should render.
    // Mirrors the WorkspaceTabManagerComponent treatment shipped in task 107.
    overflow: "hidden",
    scrollbarWidth: "none",
    "::-webkit-scrollbar": { display: "none" },
  },
  viewAllContainer: {
    display: "flex",
    justifyContent: "center",
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },
  emptyMessage: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flex: "1 1 0",
    color: tokens.colorNeutralForeground3,
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
  },
  errorMessage: {
    color: tokens.colorPaletteRedForeground3,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IGridMode {
  /** Number of grid columns (e.g. 2). */
  columns: number;
  /** Maximum visible rows (e.g. 10). Effective cap = columns × maxRows. */
  maxRows: number;
}

export interface IDocumentsTabProps {
  service: DataverseService;
  userId: string;
  onCountChange?: (count: number) => void;
  onRefetchReady?: (refetch: () => void) => void;
  /** Maximum rows to display. */
  maxVisible?: number;
  /** Called when "Show more" is clicked. */
  onShowMore?: () => void;
  /** When set, fetches documents using this saved view's filter/order. */
  selectedViewId?: string;
  /** View type: 'savedquery' or 'userquery'. */
  selectedViewType?: string;
  /** Record scope. */
  scope?: 'my' | 'all';
  /** Business unit ID. */
  businessUnitId?: string;
  /**
   * When provided, renders cards in a CSS grid instead of the default
   * vertical list. The effective cap is `columns × maxRows` and items past
   * the cap are hidden (no scroll). The `onShowMore` callback (if provided)
   * is wired to a "View all..." link below the grid for overflow access.
   */
  gridMode?: IGridMode;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const DocumentsTab: React.FC<IDocumentsTabProps> = ({
  service,
  userId,
  onCountChange,
  onRefetchReady,
  maxVisible,
  onShowMore,
  selectedViewId,
  selectedViewType,
  scope,
  businessUnitId,
  gridMode,
}) => {
  const styles = useStyles();
  const { documents, isLoading, error, totalCount, refetch } =
    useDocumentsTabList(service, userId, { top: 50, selectedViewId, selectedViewType, scope, businessUnitId });

  // Report count to parent
  React.useEffect(() => {
    onCountChange?.(totalCount);
  }, [totalCount, onCountChange]);

  // Expose refetch to parent
  React.useEffect(() => {
    onRefetchReady?.(refetch);
  }, [refetch, onRefetchReady]);

  // -----------------------------------------------------------------------
  // GRID MODE: 2 wide × N tall, no scrollbar, cap = columns × maxRows
  // -----------------------------------------------------------------------
  if (gridMode) {
    const cap = Math.max(1, gridMode.columns * gridMode.maxRows);
    const visibleDocs = documents.slice(0, cap);
    const hasOverflow = documents.length > cap;

    if (isLoading) {
      return (
        <div className={styles.emptyMessage}>
          Loading...
        </div>
      );
    }

    if (error) {
      return (
        <div className={styles.emptyMessage}>
          <span className={styles.errorMessage}>{error}</span>
        </div>
      );
    }

    if (totalCount === 0) {
      return (
        <div className={styles.emptyMessage}>
          No documents found.
        </div>
      );
    }

    return (
      <>
        <div
          className={styles.gridContainer}
          role="list"
          aria-label="Documents grid"
          style={{ gridTemplateColumns: `repeat(${gridMode.columns}, minmax(0, 1fr))` }}
        >
          {visibleDocs.map((doc) => (
            <DocumentCard key={doc.sprk_documentid} document={doc} />
          ))}
        </div>
        {hasOverflow && onShowMore && (
          <div className={styles.viewAllContainer}>
            <Button appearance="subtle" size="small" onClick={onShowMore}>
              View all ({totalCount - cap} more)...
            </Button>
          </div>
        )}
      </>
    );
  }

  // -----------------------------------------------------------------------
  // DEFAULT MODE: vertical list with windowing (unchanged behavior)
  // -----------------------------------------------------------------------
  const visibleDocs = maxVisible ? documents.slice(0, maxVisible) : documents;

  return (
    <>
      <RecordCardList
        totalCount={totalCount}
        isLoading={isLoading}
        error={error}
        ariaLabel="Documents list"
      >
        {visibleDocs.map((doc) => (
          <DocumentCard key={doc.sprk_documentid} document={doc} />
        ))}
      </RecordCardList>
      {onShowMore && documents.length > (maxVisible ?? Infinity) && (
        <div style={{ display: "flex", justifyContent: "center", padding: "8px" }}>
          <Button appearance="subtle" size="small" onClick={onShowMore}>
            Show more ({documents.length - (maxVisible ?? 0)} more)
          </Button>
        </div>
      )}
    </>
  );
};
