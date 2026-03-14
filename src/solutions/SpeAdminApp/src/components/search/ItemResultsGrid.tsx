import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Spinner,
  DataGrid,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridBody,
  DataGridRow,
  DataGridCell,
  DataGridSelectionCell,
  TableColumnDefinition,
  createTableColumn,
  TableCellLayout,
  TableSelectionCell,
  Tooltip,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  MenuDivider,
  Divider,
  Badge,
  SelectionItemId,
} from "@fluentui/react-components";
import {
  Delete20Regular,
  ArrowDownload20Regular,
  PeopleCommunity20Regular,
  TableFreezeColumn20Regular,
  Document20Regular,
  Open20Regular,
  Copy20Regular,
  Info20Regular,
  ChevronLeft20Regular,
  ChevronRight20Regular,
} from "@fluentui/react-icons";
import type { DriveItemSearchResult } from "../../types/spe";
import { speApiClient } from "../../services/speApiClient";
import { useBuContext } from "../../contexts/BuContext";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  /** Root container — flex column filling available height */
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    overflow: "hidden",
    gap: tokens.spacingVerticalS,
  },

  /** Toolbar row: action buttons + selection count badge */
  toolbar: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },

  /** Spacer pushes pagination to the right */
  toolbarSpacer: {
    flex: "1 1 auto",
  },

  /** Pagination controls */
  pagination: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
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

  /** Truncate long text in cells */
  cellText: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    maxWidth: "220px",
  },

  /** Subdued hit-highlight summary text */
  highlightText: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    maxWidth: "260px",
    color: tokens.colorNeutralForeground2,
    fontStyle: "italic",
  },

  /** Empty state container (centered) */
  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalXXL,
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground2,
    flex: "1 1 auto",
  },

  /** Operation in-progress overlay text */
  operationStatus: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground2,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/** Format bytes as human-readable string */
function formatBytes(bytes?: number): string {
  if (bytes === undefined || bytes === null) return "—";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024)
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`;
}

/** Format ISO timestamp to compact local date/time */
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

/** Escape a value for CSV output */
function csvEscape(value: string): string {
  if (value.includes(",") || value.includes('"') || value.includes("\n")) {
    return '"' + value.replace(/"/g, '""') + '"';
  }
  return value;
}

const PAGE_SIZE = 25;

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface ItemResultsGridProps {
  /** The search query (used for pagination and re-search) */
  query: string;
  /** Initial page of results already fetched by parent */
  initialResults: DriveItemSearchResult[];
  /** Whether this tab is visible/active (avoids loading when hidden) */
  isActive: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// ItemResultsGrid Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * ItemResultsGrid — displays drive item (file) search results with management actions.
 *
 * Features:
 *   - Multi-row selection via DataGrid selectionMode="multiselect"
 *   - Toolbar: Delete, Download, Manage Permissions, Export CSV
 *   - Context menu (right-click): View Details, Copy ID, Open in Browser
 *   - Delete confirmation dialog
 *   - Single and multi-file download (via speApiClient.items.download)
 *   - CSV export of all visible results
 *   - Skip-based pagination (PAGE_SIZE = 25)
 *
 * ADR compliance:
 *   - ADR-006: React Code Page (React 18, bundled) — not PCF
 *   - ADR-012: speApiClient for all API calls; @spaarke/ui-components barrel import OK
 *   - ADR-021: Fluent v9 makeStyles + design tokens; no hard-coded colors
 */
export const ItemResultsGrid: React.FC<ItemResultsGridProps> = ({
  query,
  initialResults,
  isActive,
}) => {
  const styles = useStyles();
  const { selectedConfig } = useBuContext();

  // ── Results state ──────────────────────────────────────────────────────────

  const [results, setResults] = React.useState<DriveItemSearchResult[]>(initialResults);
  const [currentPage, setCurrentPage] = React.useState(0);
  const [hasMore, setHasMore] = React.useState(initialResults.length === PAGE_SIZE);

  // ── Loading / operation state ─────────────────────────────────────────────

  const [isLoadingPage, setIsLoadingPage] = React.useState(false);
  const [isDeleting, setIsDeleting] = React.useState(false);
  const [isDownloading, setIsDownloading] = React.useState(false);
  const [operationError, setOperationError] = React.useState<string | null>(null);

  // ── Selection state ────────────────────────────────────────────────────────

  const [selectedItems, setSelectedItems] = React.useState(
    new Set<SelectionItemId>()
  );

  // ── Delete confirmation dialog ─────────────────────────────────────────────

  const [showDeleteDialog, setShowDeleteDialog] = React.useState(false);

  // ── Context menu state ─────────────────────────────────────────────────────

  const [contextItem, setContextItem] =
    React.useState<DriveItemSearchResult | null>(null);
  const [contextMenuOpen, setContextMenuOpen] = React.useState(false);
  const [contextMenuPos, setContextMenuPos] = React.useState({ x: 0, y: 0 });

  // ── Sync results when initialResults changes (new search) ─────────────────

  React.useEffect(() => {
    setResults(initialResults);
    setCurrentPage(0);
    setHasMore(initialResults.length === PAGE_SIZE);
    setSelectedItems(new Set());
    setOperationError(null);
  }, [initialResults]);

  // ── Helpers ────────────────────────────────────────────────────────────────

  /** Get the currently selected DriveItemSearchResult objects */
  const getSelectedResults = React.useCallback((): DriveItemSearchResult[] => {
    return results.filter((r) => selectedItems.has(r.item.id));
  }, [results, selectedItems]);

  const selectedCount = selectedItems.size;

  // ── Column definitions ─────────────────────────────────────────────────────

  type ItemColumn = TableColumnDefinition<DriveItemSearchResult>;

  const columns = React.useMemo((): ItemColumn[] => {
    return [
      createTableColumn<DriveItemSearchResult>({
        columnId: "name",
        compare: (a, b) => a.item.name.localeCompare(b.item.name),
        renderHeaderCell: () => "Name",
        renderCell: (row) => (
          <TableCellLayout media={<Document20Regular />}>
            <Tooltip content={row.item.name} relationship="description">
              <Text className={styles.cellText} weight="semibold">
                {row.item.name}
              </Text>
            </Tooltip>
          </TableCellLayout>
        ),
      }),
      createTableColumn<DriveItemSearchResult>({
        columnId: "highlight",
        compare: () => 0,
        renderHeaderCell: () => "Summary",
        renderCell: (row) => (
          <TableCellLayout>
            {row.hitHighlightedSummary ? (
              <Tooltip
                content={row.hitHighlightedSummary}
                relationship="description"
              >
                <Text className={styles.highlightText}>
                  {row.hitHighlightedSummary}
                </Text>
              </Tooltip>
            ) : (
              <Text
                size={200}
                style={{ color: tokens.colorNeutralForeground3 }}
              >
                —
              </Text>
            )}
          </TableCellLayout>
        ),
      }),
      createTableColumn<DriveItemSearchResult>({
        columnId: "size",
        compare: (a, b) => (a.item.size ?? 0) - (b.item.size ?? 0),
        renderHeaderCell: () => "Size",
        renderCell: (row) => (
          <TableCellLayout>
            <Text size={200}>{formatBytes(row.item.size)}</Text>
          </TableCellLayout>
        ),
      }),
      createTableColumn<DriveItemSearchResult>({
        columnId: "modified",
        compare: (a, b) =>
          new Date(a.item.lastModifiedDateTime).getTime() -
          new Date(b.item.lastModifiedDateTime).getTime(),
        renderHeaderCell: () => "Modified",
        renderCell: (row) => (
          <TableCellLayout>
            <Text size={200} style={{ whiteSpace: "nowrap" }}>
              {formatDateTime(row.item.lastModifiedDateTime)}
            </Text>
          </TableCellLayout>
        ),
      }),
      createTableColumn<DriveItemSearchResult>({
        columnId: "container",
        compare: (a, b) => a.containerId.localeCompare(b.containerId),
        renderHeaderCell: () => "Container ID",
        renderCell: (row) => (
          <TableCellLayout>
            <Tooltip content={row.containerId} relationship="description">
              <Text size={200} className={styles.cellText}>
                {row.containerId}
              </Text>
            </Tooltip>
          </TableCellLayout>
        ),
      }),
    ];
  }, [styles]);

  // ── Pagination ─────────────────────────────────────────────────────────────

  /**
   * Load a specific page of results.
   * Uses skip-based pagination: skip = page * PAGE_SIZE.
   */
  const loadPage = React.useCallback(
    async (page: number) => {
      if (!selectedConfig || !query.trim()) return;

      setIsLoadingPage(true);
      setOperationError(null);
      setSelectedItems(new Set());

      try {
        const newResults = await speApiClient.search.items(selectedConfig.id, {
          query: query.trim(),
          top: PAGE_SIZE,
          skip: page * PAGE_SIZE,
        });
        setResults(newResults);
        setCurrentPage(page);
        setHasMore(newResults.length === PAGE_SIZE);
      } catch (err) {
        const msg =
          err instanceof Error
            ? err.message
            : "Failed to load page. Please try again.";
        setOperationError(msg);
      } finally {
        setIsLoadingPage(false);
      }
    },
    [selectedConfig, query]
  );

  const handlePreviousPage = React.useCallback(() => {
    if (currentPage > 0) void loadPage(currentPage - 1);
  }, [currentPage, loadPage]);

  const handleNextPage = React.useCallback(() => {
    if (hasMore) void loadPage(currentPage + 1);
  }, [hasMore, currentPage, loadPage]);

  // ── Delete action ──────────────────────────────────────────────────────────

  /**
   * Execute deletion of all selected items.
   * Deletes sequentially to avoid overwhelming the API.
   * Items that fail to delete are logged; successful deletes are removed from state.
   */
  const handleConfirmDelete = React.useCallback(async () => {
    if (!selectedConfig) return;

    const toDelete = getSelectedResults();
    setShowDeleteDialog(false);
    setIsDeleting(true);
    setOperationError(null);

    const errors: string[] = [];

    for (const result of toDelete) {
      try {
        await speApiClient.items.delete(
          result.containerId,
          result.item.id,
          selectedConfig.id
        );
      } catch (err) {
        const msg =
          err instanceof Error
            ? err.message
            : `Failed to delete "${result.item.name}"`;
        errors.push(msg);
      }
    }

    // Remove successfully deleted items from local state
    const deletedIds = new Set(
      toDelete
        .filter((r) => !errors.some((e) => e.includes(r.item.name)))
        .map((r) => r.item.id)
    );
    setResults((prev) => prev.filter((r) => !deletedIds.has(r.item.id)));
    setSelectedItems(new Set());

    if (errors.length > 0) {
      setOperationError(
        `${errors.length} item(s) failed to delete: ${errors.slice(0, 2).join("; ")}${
          errors.length > 2 ? " …" : ""
        }`
      );
    }

    setIsDeleting(false);
  }, [selectedConfig, getSelectedResults]);

  // ── Download action ────────────────────────────────────────────────────────

  /**
   * Download selected items.
   *
   * For each item:
   *   1. Try the pre-signed `@microsoft.graph.downloadUrl` (no auth needed, direct link).
   *   2. Fall back to speApiClient.items.download() → blob URL.
   *
   * Multiple files are downloaded sequentially with a short delay to avoid
   * the browser blocking simultaneous downloads.
   */
  const handleDownload = React.useCallback(async () => {
    if (!selectedConfig) return;

    const toDownload = getSelectedResults();
    setIsDownloading(true);
    setOperationError(null);

    const errors: string[] = [];

    for (const result of toDownload) {
      try {
        const downloadUrl =
          result.item["@microsoft.graph.downloadUrl"];

        if (downloadUrl) {
          // Direct pre-signed URL — no auth required
          const anchor = document.createElement("a");
          anchor.href = downloadUrl;
          anchor.download = result.item.name;
          anchor.style.display = "none";
          document.body.appendChild(anchor);
          anchor.click();
          document.body.removeChild(anchor);
        } else {
          // Authenticated download through BFF
          const response = await speApiClient.items.download(
            result.containerId,
            result.item.id,
            selectedConfig.id
          );
          const blob = await response.blob();
          const blobUrl = URL.createObjectURL(blob);
          const anchor = document.createElement("a");
          anchor.href = blobUrl;
          anchor.download = result.item.name;
          anchor.style.display = "none";
          document.body.appendChild(anchor);
          anchor.click();
          document.body.removeChild(anchor);
          // Revoke after a short delay to allow the download to start
          setTimeout(() => URL.revokeObjectURL(blobUrl), 5000);
        }

        // Delay between downloads to prevent browser blocking
        if (toDownload.length > 1) {
          await new Promise((resolve) => setTimeout(resolve, 300));
        }
      } catch (err) {
        const msg =
          err instanceof Error
            ? err.message
            : `Failed to download "${result.item.name}"`;
        errors.push(msg);
      }
    }

    if (errors.length > 0) {
      setOperationError(
        `${errors.length} file(s) failed to download: ${errors.slice(0, 2).join("; ")}${
          errors.length > 2 ? " …" : ""
        }`
      );
    }

    setIsDownloading(false);
  }, [selectedConfig, getSelectedResults]);

  // ── Manage Permissions action ──────────────────────────────────────────────

  /**
   * Manage Permissions — navigates to the container detail page.
   * Since permissions are managed at the container level (not item level),
   * this opens the container that owns the selected item(s).
   */
  const handleManagePermissions = React.useCallback(() => {
    const selected = getSelectedResults();
    if (selected.length === 0 || !selectedConfig) return;

    // Open the first selected item's container in the file browser
    const containerId = selected[0].containerId;
    const url = new URL(window.location.href);
    url.searchParams.set("page", "containers");
    url.searchParams.set("containerId", containerId);
    window.open(url.toString(), "_blank");
  }, [getSelectedResults, selectedConfig]);

  // ── Export CSV action ──────────────────────────────────────────────────────

  /**
   * Export all current page results to CSV.
   * Columns: Name, Size, Modified, Modified By, Container ID, Summary, Item ID
   */
  const handleExportCsv = React.useCallback(() => {
    if (results.length === 0) return;

    const headers = [
      "Name",
      "Size (bytes)",
      "Last Modified",
      "Modified By",
      "Container ID",
      "Summary",
      "Item ID",
    ];

    const rows = results.map((r) => [
      csvEscape(r.item.name),
      csvEscape(String(r.item.size ?? "")),
      csvEscape(r.item.lastModifiedDateTime),
      csvEscape(r.item.lastModifiedBy?.user?.displayName ?? ""),
      csvEscape(r.containerId),
      csvEscape(r.hitHighlightedSummary ?? ""),
      csvEscape(r.item.id),
    ]);

    const csvContent = [headers.join(","), ...rows.map((r) => r.join(","))].join("\n");

    const blob = new Blob([csvContent], { type: "text/csv;charset=utf-8;" });
    const blobUrl = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = blobUrl;
    anchor.download = `item-search-results-page${currentPage + 1}.csv`;
    anchor.style.display = "none";
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    setTimeout(() => URL.revokeObjectURL(blobUrl), 5000);
  }, [results, currentPage]);

  // ── Context menu handlers ──────────────────────────────────────────────────

  /**
   * Handle right-click on a grid row to show context menu.
   */
  const handleRowContextMenu = React.useCallback(
    (e: React.MouseEvent, result: DriveItemSearchResult) => {
      e.preventDefault();
      setContextItem(result);
      setContextMenuPos({ x: e.clientX, y: e.clientY });
      setContextMenuOpen(true);
    },
    []
  );

  const handleContextViewDetails = React.useCallback(() => {
    if (!contextItem) return;
    setContextMenuOpen(false);

    // Show basic item details via an alert (proper detail panel would be task 069+)
    const item = contextItem.item;
    const details = [
      `Name: ${item.name}`,
      `ID: ${item.id}`,
      `Size: ${formatBytes(item.size)}`,
      `Created: ${formatDateTime(item.createdDateTime)}`,
      `Modified: ${formatDateTime(item.lastModifiedDateTime)}`,
      `Modified By: ${item.lastModifiedBy?.user?.displayName ?? "—"}`,
      `Container ID: ${contextItem.containerId}`,
      `Web URL: ${item.webUrl ?? "—"}`,
    ].join("\n");

    window.alert(details);
  }, [contextItem]);

  const handleContextCopyId = React.useCallback(() => {
    if (!contextItem) return;
    setContextMenuOpen(false);

    void navigator.clipboard
      .writeText(contextItem.item.id)
      .catch(() => {
        // Fallback for environments where clipboard API is blocked
        const textarea = document.createElement("textarea");
        textarea.value = contextItem.item.id;
        textarea.style.position = "fixed";
        textarea.style.opacity = "0";
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand("copy");
        document.body.removeChild(textarea);
      });
  }, [contextItem]);

  const handleContextOpenInBrowser = React.useCallback(() => {
    if (!contextItem?.item.webUrl) return;
    setContextMenuOpen(false);
    window.open(contextItem.item.webUrl, "_blank", "noopener,noreferrer");
  }, [contextItem]);

  // ── Selection change handler ───────────────────────────────────────────────

  const handleSelectionChange = React.useCallback(
    (
      _e: React.SyntheticEvent,
      data: { selectedItems: Set<SelectionItemId> }
    ) => {
      setSelectedItems(data.selectedItems);
    },
    []
  );

  // ── Guard: not active ──────────────────────────────────────────────────────

  if (!isActive) return null;

  // ── Empty state ─────────────────────────────────────────────────────────────

  if (results.length === 0 && !isLoadingPage) {
    return (
      <div className={styles.emptyState}>
        <Document20Regular style={{ fontSize: "32px" }} />
        <Text size={400} weight="semibold">
          No Items Found
        </Text>
        <Text
          size={300}
          style={{ color: tokens.colorNeutralForeground2, textAlign: "center" }}
        >
          No files matched the search query. Try a different search term.
        </Text>
      </div>
    );
  }

  // ── Main render ─────────────────────────────────────────────────────────────

  return (
    <div className={styles.root}>
      {/* ── Toolbar ── */}
      <div className={styles.toolbar}>
        {/* Delete */}
        <Tooltip
          content={
            selectedCount === 0
              ? "Select items to delete"
              : `Delete ${selectedCount} item(s)`
          }
          relationship="label"
        >
          <Button
            appearance="subtle"
            size="small"
            icon={<Delete20Regular />}
            disabled={selectedCount === 0 || isDeleting || isDownloading}
            onClick={() => setShowDeleteDialog(true)}
            aria-label="Delete selected items"
          >
            Delete
            {selectedCount > 0 && (
              <Badge
                appearance="filled"
                color="danger"
                size="small"
                style={{ marginLeft: tokens.spacingHorizontalXS }}
              >
                {selectedCount}
              </Badge>
            )}
          </Button>
        </Tooltip>

        {/* Download */}
        <Tooltip
          content={
            selectedCount === 0
              ? "Select items to download"
              : `Download ${selectedCount} file(s)`
          }
          relationship="label"
        >
          <Button
            appearance="subtle"
            size="small"
            icon={<ArrowDownload20Regular />}
            disabled={selectedCount === 0 || isDeleting || isDownloading}
            onClick={() => void handleDownload()}
            aria-label="Download selected items"
          >
            Download
          </Button>
        </Tooltip>

        {/* Manage Permissions */}
        <Tooltip
          content={
            selectedCount === 0
              ? "Select an item to manage permissions"
              : "Open container permissions"
          }
          relationship="label"
        >
          <Button
            appearance="subtle"
            size="small"
            icon={<PeopleCommunity20Regular />}
            disabled={selectedCount === 0 || isDeleting || isDownloading}
            onClick={handleManagePermissions}
            aria-label="Manage container permissions"
          >
            Manage Permissions
          </Button>
        </Tooltip>

        {/* Export CSV */}
        <Tooltip content="Export results to CSV" relationship="label">
          <Button
            appearance="subtle"
            size="small"
            icon={<TableFreezeColumn20Regular />}
            disabled={results.length === 0 || isDeleting || isDownloading}
            onClick={handleExportCsv}
            aria-label="Export to CSV"
          >
            Export CSV
          </Button>
        </Tooltip>

        {/* Operation status */}
        {(isDeleting || isDownloading) && (
          <div className={styles.operationStatus}>
            <Spinner size="extra-tiny" />
            <Text size={200}>
              {isDeleting ? "Deleting…" : "Downloading…"}
            </Text>
          </div>
        )}

        <div className={styles.toolbarSpacer} />

        {/* Pagination */}
        <div className={styles.pagination}>
          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
            Page {currentPage + 1}
          </Text>
          <Button
            appearance="subtle"
            size="small"
            icon={<ChevronLeft20Regular />}
            disabled={currentPage === 0 || isLoadingPage}
            onClick={handlePreviousPage}
            aria-label="Previous page"
          />
          <Button
            appearance="subtle"
            size="small"
            icon={<ChevronRight20Regular />}
            disabled={!hasMore || isLoadingPage}
            onClick={handleNextPage}
            aria-label="Next page"
          />
        </div>
      </div>

      {/* ── Operation error ── */}
      {operationError && (
        <Text
          size={200}
          style={{
            color: tokens.colorStatusDangerForeground1,
            paddingLeft: tokens.spacingHorizontalXS,
            flexShrink: 0,
          }}
        >
          {operationError}
        </Text>
      )}

      <Divider style={{ flexShrink: 0 }} />

      {/* ── Loading page spinner ── */}
      {isLoadingPage && (
        <div className={styles.emptyState}>
          <Spinner size="medium" label="Loading…" />
        </div>
      )}

      {/* ── DataGrid ── */}
      {!isLoadingPage && (
        <div className={styles.gridWrapper}>
          <DataGrid
            items={results}
            columns={columns}
            sortable
            selectionMode="multiselect"
            selectedItems={selectedItems}
            onSelectionChange={handleSelectionChange}
            getRowId={(item: DriveItemSearchResult) => item.item.id}
            style={{ width: "100%" }}
            aria-label="Item search results"
          >
            <DataGridHeader>
              <DataGridRow>
                {({ renderHeaderCell }) => (
                  <>
                    <TableSelectionCell type="checkbox" />
                    <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
                  </>
                )}
              </DataGridRow>
            </DataGridHeader>
            <DataGridBody<DriveItemSearchResult>>
              {({ item, rowId }) => (
                <DataGridRow<DriveItemSearchResult>
                  key={rowId}
                  onContextMenu={(e) => handleRowContextMenu(e, item)}
                  style={{ cursor: "context-menu" }}
                >
                  {({ renderCell }) => (
                    <>
                      <DataGridSelectionCell type="checkbox" />
                      <DataGridCell>{renderCell(item)}</DataGridCell>
                    </>
                  )}
                </DataGridRow>
              )}
            </DataGridBody>
          </DataGrid>
        </div>
      )}

      {/* ── Delete Confirmation Dialog ── */}
      <Dialog open={showDeleteDialog}>
        <DialogSurface>
          <DialogTitle>Delete {selectedCount} Item(s)?</DialogTitle>
          <DialogBody>
            <DialogContent>
              <Text>
                Are you sure you want to delete{" "}
                <Text weight="semibold">{selectedCount} item(s)</Text>? This
                action cannot be undone.
              </Text>
              {selectedCount <= 5 && (
                <ul style={{ marginTop: tokens.spacingVerticalS }}>
                  {getSelectedResults().map((r) => (
                    <li key={r.item.id}>
                      <Text size={200}>{r.item.name}</Text>
                    </li>
                  ))}
                </ul>
              )}
            </DialogContent>
            <DialogActions>
              <Button
                appearance="secondary"
                onClick={() => setShowDeleteDialog(false)}
              >
                Cancel
              </Button>
              <Button
                appearance="primary"
                style={{ backgroundColor: tokens.colorStatusDangerBackground3 }}
                onClick={() => void handleConfirmDelete()}
              >
                Delete
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      {/* ── Context Menu (right-click) ── */}
      {contextMenuOpen && contextItem && (
        <Menu
          open={contextMenuOpen}
          onOpenChange={(_e, data) => setContextMenuOpen(data.open)}
          positioning={{
            target: {
              getBoundingClientRect: () => ({
                top: contextMenuPos.y,
                left: contextMenuPos.x,
                bottom: contextMenuPos.y,
                right: contextMenuPos.x,
                width: 0,
                height: 0,
                x: contextMenuPos.x,
                y: contextMenuPos.y,
                toJSON: () => ({}),
              }),
            },
          }}
        >
          <MenuTrigger>
            {/* Invisible trigger — menu is opened programmatically */}
            <span style={{ display: "none" }} />
          </MenuTrigger>
          <MenuPopover>
            <MenuList>
              <MenuItem
                icon={<Info20Regular />}
                onClick={handleContextViewDetails}
              >
                View Details
              </MenuItem>
              <MenuItem
                icon={<Copy20Regular />}
                onClick={handleContextCopyId}
              >
                Copy ID
              </MenuItem>
              <MenuDivider />
              <MenuItem
                icon={<Open20Regular />}
                onClick={handleContextOpenInBrowser}
                disabled={!contextItem.item.webUrl}
              >
                Open in Browser
              </MenuItem>
            </MenuList>
          </MenuPopover>
        </Menu>
      )}
    </div>
  );
};
