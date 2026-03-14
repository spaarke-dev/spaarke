import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Spinner,
  Tooltip,
  DataGrid,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridBody,
  DataGridRow,
  DataGridCell,
  TableColumnDefinition,
  createTableColumn,
  TableCellLayout,
  TableRowId,
  MessageBar,
  MessageBarBody,
  MessageBarActions,
  Dialog,
  DialogTrigger,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
} from "@fluentui/react-components";
import {
  Storage20Regular,
  Delete20Regular,
  LockClosed20Regular,
  ArrowDownload20Regular,
} from "@fluentui/react-icons";
import type { ContainerSearchResult, BulkOperationStatus } from "../../types/spe";
import { speApiClient } from "../../services/speApiClient";
import { BulkOperationProgress } from "../bulk/BulkOperationProgress";

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Format bytes into a human-readable string.
 * e.g. 1234567 → "1.2 MB"
 */
function formatBytes(bytes?: number): string {
  if (bytes === undefined || bytes === null) return "—";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024)
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`;
}

/**
 * Format an ISO timestamp to a compact local date/time string.
 * e.g. "2026-03-14 09:31 AM"
 */
function formatDateTime(iso: string): string {
  try {
    return new Date(iso).toLocaleString(undefined, {
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
    });
  } catch {
    return iso;
  }
}

/**
 * Generate and trigger download of a CSV file from container search results.
 *
 * Columns exported: Name, Container Type ID, Status, Storage Used, Created Date
 */
function exportToCsv(results: ContainerSearchResult[]): void {
  const header = ["Name", "Container Type ID", "Status", "Storage Used (bytes)", "Created Date"];

  const csvRows = [
    header.join(","),
    ...results.map(({ container }) => {
      const name = `"${container.displayName.replace(/"/g, '""')}"`;
      const typeId = container.containerTypeId;
      const status = container.status;
      const storage = container.storageUsedInBytes ?? 0;
      const created = container.createdDateTime;
      return [name, typeId, status, storage, created].join(",");
    }),
  ];

  const csv = csvRows.join("\n");
  const blob = new Blob([csv], { type: "text/csv;charset=utf-8;" });
  const url = URL.createObjectURL(blob);

  const link = document.createElement("a");
  link.href = url;
  link.download = `container-search-results-${new Date().toISOString().slice(0, 10)}.csv`;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  /** Root flex column — fills parent's available space */
  root: {
    display: "flex",
    flexDirection: "column",
    flex: "1 1 auto",
    overflow: "hidden",
    gap: tokens.spacingVerticalS,
  },

  /** Toolbar row: action buttons + selection count */
  toolbar: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flexShrink: 0,
  },

  /** Spacer to push count badge to the right */
  toolbarSpacer: {
    flex: "1 1 auto",
  },

  /** Selection count text — subdued */
  selectionCount: {
    color: tokens.colorNeutralForeground2,
    flexShrink: 0,
  },

  /** DataGrid scrollable wrapper */
  gridWrapper: {
    flex: "1 1 auto",
    overflow: "auto",
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftWidth: "1px",
    borderLeftStyle: "solid",
    borderLeftColor: tokens.colorNeutralStroke2,
    borderRightWidth: "1px",
    borderRightStyle: "solid",
    borderRightColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
  },

  /** Truncate long text in grid cells */
  cellText: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    maxWidth: "240px",
  },

  /** Pagination row */
  pagination: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "flex-end",
    gap: tokens.spacingHorizontalS,
    flexShrink: 0,
  },

  /** Status feedback bar wrapper */
  feedbackBar: {
    flexShrink: 0,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface ContainerResultsGridProps {
  /** Container search results to display */
  results: ContainerSearchResult[];

  /**
   * Config ID used to scope API calls (delete, lock).
   * Required for toolbar actions.
   */
  configId: string;

  /**
   * Whether more results are available for pagination.
   * When true, a "Load More" button is shown.
   */
  hasMore?: boolean;

  /** Whether a page-load is in progress (disables pagination button) */
  isLoadingMore?: boolean;

  /**
   * Called when the user requests the next page.
   * The parent is responsible for fetching with the next skipToken
   * and appending results to the `results` prop.
   */
  onLoadMore?: () => void;

  /**
   * Called after a successful delete so the parent can refresh results.
   * Receives the IDs of the deleted containers.
   */
  onDeleted?: (containerIds: string[]) => void;

  /**
   * Called after a successful lock so the parent can refresh results.
   * Receives the IDs of the locked containers.
   */
  onLocked?: (containerIds: string[]) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Column Definitions
// ─────────────────────────────────────────────────────────────────────────────

type ResultColumn = TableColumnDefinition<ContainerSearchResult>;

function buildColumns(styles: ReturnType<typeof useStyles>): ResultColumn[] {
  return [
    createTableColumn<ContainerSearchResult>({
      columnId: "name",
      compare: (a, b) =>
        a.container.displayName.localeCompare(b.container.displayName),
      renderHeaderCell: () => "Name",
      renderCell: ({ container }) => (
        <TableCellLayout media={<Storage20Regular />}>
          <Tooltip content={container.displayName} relationship="description">
            <Text className={styles.cellText} weight="semibold">
              {container.displayName}
            </Text>
          </Tooltip>
        </TableCellLayout>
      ),
    }),
    createTableColumn<ContainerSearchResult>({
      columnId: "containerTypeId",
      compare: (a, b) =>
        a.container.containerTypeId.localeCompare(b.container.containerTypeId),
      renderHeaderCell: () => "Container Type",
      renderCell: ({ container }) => (
        <TableCellLayout>
          <Tooltip
            content={container.containerTypeId}
            relationship="description"
          >
            <Text size={200} className={styles.cellText}>
              {container.containerTypeId}
            </Text>
          </Tooltip>
        </TableCellLayout>
      ),
    }),
    createTableColumn<ContainerSearchResult>({
      columnId: "status",
      compare: (a, b) =>
        a.container.status.localeCompare(b.container.status),
      renderHeaderCell: () => "Status",
      renderCell: ({ container }) => (
        <TableCellLayout>
          <Text size={200}>{container.status}</Text>
        </TableCellLayout>
      ),
    }),
    createTableColumn<ContainerSearchResult>({
      columnId: "storage",
      compare: (a, b) =>
        (a.container.storageUsedInBytes ?? 0) -
        (b.container.storageUsedInBytes ?? 0),
      renderHeaderCell: () => "Storage Used",
      renderCell: ({ container }) => (
        <TableCellLayout>
          <Text size={200}>{formatBytes(container.storageUsedInBytes)}</Text>
        </TableCellLayout>
      ),
    }),
    createTableColumn<ContainerSearchResult>({
      columnId: "createdDateTime",
      compare: (a, b) =>
        new Date(a.container.createdDateTime).getTime() -
        new Date(b.container.createdDateTime).getTime(),
      renderHeaderCell: () => "Created",
      renderCell: ({ container }) => (
        <TableCellLayout>
          <Text size={200} style={{ whiteSpace: "nowrap" }}>
            {formatDateTime(container.createdDateTime)}
          </Text>
        </TableCellLayout>
      ),
    }),
  ];
}

// ─────────────────────────────────────────────────────────────────────────────
// ContainerResultsGrid Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * ContainerResultsGrid — displays container search results with multi-row
 * selection and a management toolbar.
 *
 * Toolbar actions:
 *   - Delete: moves selected containers to the recycle bin (soft delete).
 *     Requires confirmation via dialog before executing.
 *   - Lock: sets selected containers to read-only/locked status.
 *   - Export CSV: downloads a CSV file of all current search results.
 *
 * Pagination: when `hasMore` is true, a "Load More" button is shown at the
 * bottom. The parent provides the next page via `onLoadMore`.
 *
 * ADR compliance:
 *   - ADR-006: React Code Page (React 18, bundled) — not PCF
 *   - ADR-012: speApiClient for all API calls; shared library patterns
 *   - ADR-021: Fluent v9 makeStyles + design tokens; no hard-coded colors; dark mode
 */
export const ContainerResultsGrid: React.FC<ContainerResultsGridProps> = ({
  results,
  configId,
  hasMore = false,
  isLoadingMore = false,
  onLoadMore,
  onDeleted,
  onLocked,
}) => {
  const styles = useStyles();

  // ── Multi-row selection ──────────────────────────────────────────────────

  const [selectedItems, setSelectedItems] = React.useState<Set<TableRowId>>(
    new Set()
  );

  /** Selection count for toolbar label and dialog */
  const selectionCount = selectedItems.size;

  /** IDs of selected containers — resolved from selectedItems row IDs */
  const selectedContainerIds = React.useMemo<string[]>(() => {
    return results
      .filter(({ container }) => selectedItems.has(container.id))
      .map(({ container }) => container.id);
  }, [results, selectedItems]);

  // ── Action state ─────────────────────────────────────────────────────────

  const [isDeleting, setIsDeleting] = React.useState(false);
  const [isLocking, setIsLocking] = React.useState(false);

  /** Whether the delete confirmation dialog is open */
  const [deleteDialogOpen, setDeleteDialogOpen] = React.useState(false);

  /** Feedback message shown below the toolbar after an action */
  const [feedback, setFeedback] = React.useState<{
    intent: "success" | "error";
    message: string;
  } | null>(null);

  /**
   * Active bulk operation ID for the progress panel.
   * Set when a bulk delete or bulk permission assignment is enqueued.
   * Cleared when the user dismisses the panel or a new operation starts.
   */
  const [bulkOperationId, setBulkOperationId] = React.useState<string | null>(null);

  // ── Column definitions (memoized) ────────────────────────────────────────

  const columns = React.useMemo(() => buildColumns(styles), [styles]);

  // ── Action handlers ──────────────────────────────────────────────────────

  /**
   * Enqueue a bulk delete operation after user confirms the dialog.
   * Uses POST /api/spe/bulk/delete which processes containers in the background.
   * BulkOperationProgress component polls for progress and shows results.
   */
  const handleDeleteConfirm = React.useCallback(async () => {
    if (selectedContainerIds.length === 0) return;
    setIsDeleting(true);
    setDeleteDialogOpen(false);
    setFeedback(null);
    setBulkOperationId(null);

    try {
      const accepted = await speApiClient.bulk.enqueuDelete({
        containerIds: selectedContainerIds,
        configId,
      });
      setBulkOperationId(accepted.operationId);
      setSelectedItems(new Set());
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Unknown error enqueueing bulk delete";
      setFeedback({ intent: "error", message: msg });
    } finally {
      setIsDeleting(false);
    }
  }, [configId, selectedContainerIds]);

  /**
   * Lock all selected containers sequentially (individual API calls — no bulk endpoint for lock).
   * Calls POST /api/spe/containers/{id}/lock for each selected container.
   */
  const handleLock = React.useCallback(async () => {
    if (selectedContainerIds.length === 0) return;
    setIsLocking(true);
    setFeedback(null);

    const errors: string[] = [];
    for (const containerId of selectedContainerIds) {
      try {
        await speApiClient.containers.lock(containerId, configId);
      } catch (err) {
        const msg = err instanceof Error ? err.message : "Unknown error";
        errors.push(msg);
      }
    }

    setIsLocking(false);

    if (errors.length === 0) {
      setFeedback({
        intent: "success",
        message: `Successfully locked ${selectedContainerIds.length} container${
          selectedContainerIds.length !== 1 ? "s" : ""
        }.`,
      });
      setSelectedItems(new Set());
      onLocked?.(selectedContainerIds);
    } else {
      setFeedback({
        intent: "error",
        message: `Lock completed with errors: ${errors.join("; ")}`,
      });
    }
  }, [configId, selectedContainerIds, onLocked]);

  /**
   * Called when the BulkOperationProgress panel signals the operation is done.
   * Surfaces a summary message and notifies the parent to refresh results.
   */
  const handleBulkComplete = React.useCallback((status: BulkOperationStatus) => {
    const succeeded = status.completed;
    const failed = status.failed;
    const total = status.total;

    if (failed === 0) {
      setFeedback({
        intent: "success",
        message: `Bulk operation complete: ${succeeded} of ${total} container${total !== 1 ? "s" : ""} processed successfully.`,
      });
      onDeleted?.([]); // Notify parent to refresh (IDs already cleared on enqueue)
    } else {
      setFeedback({
        intent: "error",
        message: `Bulk operation complete with errors: ${succeeded} succeeded, ${failed} failed.`,
      });
      onDeleted?.([]);
    }
  }, [onDeleted]);

  /**
   * Export all current search results as a CSV file.
   * Downloads the file client-side — no API call required.
   */
  const handleExportCsv = React.useCallback(() => {
    exportToCsv(results);
  }, [results]);

  // ── Busy state (any action in progress) ──────────────────────────────────

  const isBusy = isDeleting || isLocking;
  const hasSelection = selectionCount > 0;

  // ── Render ───────────────────────────────────────────────────────────────

  return (
    <div className={styles.root}>
      {/* ── Toolbar ── */}
      <div className={styles.toolbar}>
        {/* Delete with confirmation */}
        <Dialog open={deleteDialogOpen}>
          <DialogTrigger disableButtonEnhancement>
            <Button
              appearance="subtle"
              icon={
                isDeleting ? (
                  <Spinner size="tiny" />
                ) : (
                  <Delete20Regular />
                )
              }
              disabled={!hasSelection || isBusy}
              onClick={() => setDeleteDialogOpen(true)}
              aria-label="Delete selected containers"
            >
              Delete
            </Button>
          </DialogTrigger>
          <DialogSurface aria-describedby="delete-dialog-desc">
            <DialogBody>
              <DialogTitle>Delete Containers</DialogTitle>
              <DialogContent id="delete-dialog-desc">
                <Text>
                  Are you sure you want to delete{" "}
                  <Text weight="semibold">{selectionCount}</Text> container
                  {selectionCount !== 1 ? "s" : ""}? They will be moved to the
                  recycle bin.
                </Text>
              </DialogContent>
              <DialogActions>
                <Button
                  appearance="primary"
                  onClick={() => void handleDeleteConfirm()}
                  aria-label="Confirm delete"
                >
                  Delete
                </Button>
                <Button
                  appearance="secondary"
                  onClick={() => setDeleteDialogOpen(false)}
                  aria-label="Cancel delete"
                >
                  Cancel
                </Button>
              </DialogActions>
            </DialogBody>
          </DialogSurface>
        </Dialog>

        {/* Lock action */}
        <Button
          appearance="subtle"
          icon={
            isLocking ? (
              <Spinner size="tiny" />
            ) : (
              <LockClosed20Regular />
            )
          }
          disabled={!hasSelection || isBusy}
          onClick={() => void handleLock()}
          aria-label="Lock selected containers"
        >
          Lock
        </Button>

        {/* Export CSV — always enabled when results exist */}
        <Button
          appearance="subtle"
          icon={<ArrowDownload20Regular />}
          disabled={results.length === 0}
          onClick={handleExportCsv}
          aria-label="Export results as CSV"
        >
          Export CSV
        </Button>

        <div className={styles.toolbarSpacer} />

        {/* Selection count indicator */}
        {hasSelection && (
          <Text size={200} className={styles.selectionCount}>
            {selectionCount} selected
          </Text>
        )}
      </div>

      {/* ── Feedback bar (success/error after action) ── */}
      {feedback && (
        <div className={styles.feedbackBar}>
          <MessageBar intent={feedback.intent}>
            <MessageBarBody>{feedback.message}</MessageBarBody>
            <MessageBarActions>
              <Button
                appearance="transparent"
                size="small"
                onClick={() => setFeedback(null)}
                aria-label="Dismiss"
              >
                Dismiss
              </Button>
            </MessageBarActions>
          </MessageBar>
        </div>
      )}

      {/* ── Bulk operation progress panel ── */}
      {bulkOperationId && (
        <BulkOperationProgress
          operationId={bulkOperationId}
          onComplete={handleBulkComplete}
          onDismiss={() => setBulkOperationId(null)}
        />
      )}

      {/* ── Data Grid ── */}
      <div className={styles.gridWrapper}>
        <DataGrid
          items={results}
          columns={columns}
          sortable
          selectionMode="multiselect"
          selectedItems={selectedItems}
          onSelectionChange={(_e, data) =>
            setSelectedItems(new Set(data.selectedItems))
          }
          getRowId={(item: ContainerSearchResult) => item.container.id}
          style={{ width: "100%" }}
          aria-label="Container search results"
        >
          <DataGridHeader>
            <DataGridRow>
              {({ renderHeaderCell }) => (
                <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
              )}
            </DataGridRow>
          </DataGridHeader>
          <DataGridBody<ContainerSearchResult>>
            {({ item, rowId }) => (
              <DataGridRow<ContainerSearchResult>
                key={rowId}
                selectionCell={{ "aria-label": "Select row" }}
              >
                {({ renderCell }) => (
                  <DataGridCell>{renderCell(item)}</DataGridCell>
                )}
              </DataGridRow>
            )}
          </DataGridBody>
        </DataGrid>
      </div>

      {/* ── Pagination ── */}
      {hasMore && (
        <div className={styles.pagination}>
          <Button
            appearance="outline"
            disabled={isLoadingMore || isBusy}
            onClick={onLoadMore}
            icon={isLoadingMore ? <Spinner size="tiny" /> : undefined}
            aria-label="Load more results"
          >
            {isLoadingMore ? "Loading…" : "Load More"}
          </Button>
        </div>
      )}
    </div>
  );
};
