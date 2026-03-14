import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
  Button,
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
  MessageBar,
  MessageBarBody,
  Dialog,
  DialogTrigger,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  SelectionItemId,
  TableRowId,
} from "@fluentui/react-components";
import {
  ArrowClockwise20Regular,
  ArrowUndo20Regular,
  Delete20Regular,
  DeleteDismiss20Regular,
} from "@fluentui/react-icons";
import type { DeletedContainer } from "../../types/spe";
import { speApiClient } from "../../services/speApiClient";
import { useBuContext } from "../../contexts/BuContext";

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Format an ISO timestamp to a compact local date/time string.
 * e.g. "2026-03-14 09:31 AM"
 */
function formatTimestamp(iso: string): string {
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

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  /**
   * Full-height page container (flex column).
   * Fits inside AppShell's contentInner scroll area.
   */
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
  },

  /** Page header row: title + refresh button */
  header: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalM,
    padding: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalM,
    flexShrink: 0,
  },

  headerTitle: {
    flex: "1 1 auto",
    color: tokens.colorNeutralForeground1,
  },

  headerSubtitle: {
    color: tokens.colorNeutralForeground2,
    paddingLeft: tokens.spacingVerticalL,
    paddingRight: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalS,
    flexShrink: 0,
  },

  /** Toolbar row: Restore and Permanent Delete buttons */
  toolbar: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    paddingLeft: tokens.spacingVerticalL,
    paddingRight: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalM,
    flexShrink: 0,
  },

  /** Grid area — fills remaining height */
  gridArea: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
    paddingLeft: tokens.spacingVerticalL,
    paddingRight: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalM,
    gap: tokens.spacingVerticalS,
  },

  /** DataGrid wrapper — scrollable */
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

  /** Loading / error / empty state overlay */
  stateContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalXXL,
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground2,
    flex: "1 1 auto",
  },

  /** Truncate long text in cells */
  cellText: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    maxWidth: "260px",
  },

  /** Monospace style for container type ID display */
  monoText: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    maxWidth: "220px",
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },

  /** Row count indicator */
  rowCount: {
    color: tokens.colorNeutralForeground2,
    flexShrink: 0,
  },

  /** Warning text in the permanent delete dialog */
  warningText: {
    color: tokens.colorPaletteRedForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Column Definitions
// ─────────────────────────────────────────────────────────────────────────────

type RecycleBinColumn = TableColumnDefinition<DeletedContainer>;

function buildColumns(styles: ReturnType<typeof useStyles>): RecycleBinColumn[] {
  return [
    createTableColumn<DeletedContainer>({
      columnId: "displayName",
      compare: (a, b) => a.displayName.localeCompare(b.displayName),
      renderHeaderCell: () => "Container Name",
      renderCell: (item) => (
        <TableCellLayout>
          <Tooltip content={item.displayName} relationship="description">
            <Text className={styles.cellText} weight="semibold">
              {item.displayName || "(unnamed)"}
            </Text>
          </Tooltip>
        </TableCellLayout>
      ),
    }),
    createTableColumn<DeletedContainer>({
      columnId: "containerTypeId",
      compare: (a, b) => a.containerTypeId.localeCompare(b.containerTypeId),
      renderHeaderCell: () => "Container Type",
      renderCell: (item) => (
        <TableCellLayout>
          <Tooltip
            content={`Container Type ID: ${item.containerTypeId}`}
            relationship="description"
          >
            <Text className={styles.monoText}>
              {item.containerTypeId || "—"}
            </Text>
          </Tooltip>
        </TableCellLayout>
      ),
    }),
    createTableColumn<DeletedContainer>({
      columnId: "deletedDateTime",
      compare: (a, b) => {
        if (!a.deletedDateTime) return 1;
        if (!b.deletedDateTime) return -1;
        return (
          new Date(a.deletedDateTime).getTime() -
          new Date(b.deletedDateTime).getTime()
        );
      },
      renderHeaderCell: () => "Deleted On",
      renderCell: (item) => (
        <TableCellLayout>
          {item.deletedDateTime ? (
            <Tooltip content={item.deletedDateTime} relationship="description">
              <Text size={200} style={{ whiteSpace: "nowrap" }}>
                {formatTimestamp(item.deletedDateTime)}
              </Text>
            </Tooltip>
          ) : (
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
              Unknown
            </Text>
          )}
        </TableCellLayout>
      ),
    }),
  ];
}

// ─────────────────────────────────────────────────────────────────────────────
// RecycleBinPage Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * RecycleBinPage — displays deleted containers with restore and permanent delete actions.
 *
 * Features:
 * - Grid of deleted containers: name, container type, deletion date
 * - Restore action: POST /api/spe/recyclebin/{id}/restore — recovers the container
 * - Permanent Delete: DELETE /api/spe/recyclebin/{id} — irreversible, requires confirmation
 * - Empty state when no deleted containers
 * - Loading and error states
 * - Toolbar buttons disabled when no row is selected
 *
 * ADR compliance:
 *   - ADR-006: Code Page pattern (React 18, bundled)
 *   - ADR-021: All UI via Fluent v9 makeStyles + design tokens; dark mode via tokens
 *   - ADR-012: speApiClient.recycleBin.* for API calls
 */
export const RecycleBinPage: React.FC = () => {
  const styles = useStyles();
  const { selectedConfig, selectedBu } = useBuContext();

  // ── Data state ──────────────────────────────────────────────────────────

  const [items, setItems] = React.useState<DeletedContainer[]>([]);
  const [isLoading, setIsLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // ── Selection state ──────────────────────────────────────────────────────

  const [selectedRows, setSelectedRows] = React.useState<Set<TableRowId>>(
    new Set()
  );

  /** The single selected item (only one row is selectable at a time) */
  const selectedItem = React.useMemo<DeletedContainer | null>(() => {
    if (selectedRows.size !== 1) return null;
    const id = Array.from(selectedRows)[0] as string;
    return items.find((c) => c.id === id) ?? null;
  }, [selectedRows, items]);

  // ── Action state ────────────────────────────────────────────────────────

  const [isRestoring, setIsRestoring] = React.useState(false);
  const [isDeleting, setIsDeleting] = React.useState(false);

  /** Controls the permanent delete confirmation dialog */
  const [deleteDialogOpen, setDeleteDialogOpen] = React.useState(false);

  // ── Notification state ──────────────────────────────────────────────────

  const [successMessage, setSuccessMessage] = React.useState<string | null>(null);
  const [actionError, setActionError] = React.useState<string | null>(null);

  // ── Column definitions ──────────────────────────────────────────────────

  const columns = React.useMemo(() => buildColumns(styles), [styles]);

  // ── Load data ───────────────────────────────────────────────────────────

  const loadData = React.useCallback(async () => {
    if (!selectedConfig) return;

    setIsLoading(true);
    setError(null);
    setSelectedRows(new Set());
    setSuccessMessage(null);
    setActionError(null);

    try {
      const results = await speApiClient.recycleBin.list(selectedConfig.id);
      setItems(results);
    } catch (err) {
      const msg =
        err instanceof Error
          ? err.message
          : "Failed to load recycle bin contents.";
      setError(msg);
      setItems([]);
    } finally {
      setIsLoading(false);
    }
  }, [selectedConfig]);

  // Load on mount and when selectedConfig changes
  React.useEffect(() => {
    if (selectedConfig) {
      loadData();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedConfig]);

  // ── Restore action ──────────────────────────────────────────────────────

  const handleRestore = React.useCallback(async () => {
    if (!selectedItem || !selectedConfig) return;

    setIsRestoring(true);
    setSuccessMessage(null);
    setActionError(null);

    try {
      await speApiClient.recycleBin.restore(selectedItem.id, selectedConfig.id);
      setSuccessMessage(
        `"${selectedItem.displayName}" has been restored successfully.`
      );
      // Refresh grid to remove restored container from the list
      await loadData();
    } catch (err) {
      const msg =
        err instanceof Error
          ? err.message
          : "Failed to restore container. Please try again.";
      setActionError(msg);
    } finally {
      setIsRestoring(false);
    }
  }, [selectedItem, selectedConfig, loadData]);

  // ── Permanent delete action ─────────────────────────────────────────────

  const handlePermanentDeleteConfirm = React.useCallback(async () => {
    if (!selectedItem || !selectedConfig) return;

    setIsDeleting(true);
    setDeleteDialogOpen(false);
    setSuccessMessage(null);
    setActionError(null);

    try {
      await speApiClient.recycleBin.permanentDelete(
        selectedItem.id,
        selectedConfig.id
      );
      setSuccessMessage(
        `"${selectedItem.displayName}" has been permanently deleted.`
      );
      // Refresh grid to remove permanently deleted container
      await loadData();
    } catch (err) {
      const msg =
        err instanceof Error
          ? err.message
          : "Failed to permanently delete container. Please try again.";
      setActionError(msg);
    } finally {
      setIsDeleting(false);
    }
  }, [selectedItem, selectedConfig, loadData]);

  // ── Selection change ────────────────────────────────────────────────────

  const handleSelectionChange = React.useCallback(
    (
      _e: React.MouseEvent | React.KeyboardEvent,
      data: { selectedItems: Set<SelectionItemId> }
    ) => {
      setSelectedRows(data.selectedItems as Set<TableRowId>);
    },
    []
  );

  // ── Render helpers ──────────────────────────────────────────────────────

  const renderNoConfig = () => (
    <div className={styles.stateContainer}>
      <DeleteDismiss20Regular
        style={{
          fontSize: "40px",
          color: tokens.colorNeutralForeground3,
        }}
      />
      <Text size={400} weight="semibold">
        No Config Selected
      </Text>
      <Text
        size={300}
        style={{
          color: tokens.colorNeutralForeground2,
          textAlign: "center",
          maxWidth: "360px",
        }}
      >
        Select a Business Unit and Container Type Config using the BU picker to
        view the recycle bin.
        {selectedBu && !selectedConfig
          ? ` Business Unit "${selectedBu.name}" is selected — please also select a Config.`
          : ""}
      </Text>
    </div>
  );

  const renderLoading = () => (
    <div className={styles.stateContainer}>
      <Spinner size="medium" label="Loading recycle bin..." />
    </div>
  );

  const renderError = () => (
    <MessageBar intent="error" style={{ marginBottom: tokens.spacingVerticalS }}>
      <MessageBarBody>
        {error}
        <Button
          appearance="transparent"
          size="small"
          onClick={loadData}
          style={{ marginLeft: tokens.spacingHorizontalS }}
        >
          Retry
        </Button>
      </MessageBarBody>
    </MessageBar>
  );

  const renderEmpty = () => (
    <div className={styles.stateContainer}>
      <DeleteDismiss20Regular
        style={{
          fontSize: "40px",
          color: tokens.colorNeutralForeground3,
        }}
      />
      <Text size={400} weight="semibold">
        Recycle Bin is Empty
      </Text>
      <Text
        size={300}
        style={{ color: tokens.colorNeutralForeground2, textAlign: "center" }}
      >
        No deleted containers found. Containers moved to the recycle bin will
        appear here and can be restored within the retention period.
      </Text>
    </div>
  );

  // ── Toolbar button states ───────────────────────────────────────────────

  const hasSelection = !!selectedItem;
  const isActionInProgress = isRestoring || isDeleting;

  // ── Main render ─────────────────────────────────────────────────────────

  return (
    <div className={styles.root}>
      {/* ── Page Header ── */}
      <div className={styles.header}>
        <Text className={styles.headerTitle} size={500} weight="semibold">
          Recycle Bin
        </Text>
        {selectedConfig && (
          <Tooltip content="Refresh recycle bin" relationship="label">
            <Button
              appearance="subtle"
              icon={<ArrowClockwise20Regular />}
              onClick={loadData}
              disabled={isLoading || isActionInProgress}
              aria-label="Refresh recycle bin"
            />
          </Tooltip>
        )}
      </div>

      {/* ── Config context breadcrumb ── */}
      {selectedConfig && (
        <Text size={200} className={styles.headerSubtitle}>
          {selectedBu?.name && `${selectedBu.name} / `}
          {selectedConfig.name}
        </Text>
      )}

      {/* ── Success / action error notifications ── */}
      {successMessage && (
        <div
          style={{
            paddingLeft: tokens.spacingVerticalL,
            paddingRight: tokens.spacingVerticalL,
            paddingBottom: tokens.spacingVerticalS,
            flexShrink: 0,
          }}
        >
          <MessageBar intent="success">
            <MessageBarBody>{successMessage}</MessageBarBody>
          </MessageBar>
        </div>
      )}
      {actionError && (
        <div
          style={{
            paddingLeft: tokens.spacingVerticalL,
            paddingRight: tokens.spacingVerticalL,
            paddingBottom: tokens.spacingVerticalS,
            flexShrink: 0,
          }}
        >
          <MessageBar intent="error">
            <MessageBarBody>{actionError}</MessageBarBody>
          </MessageBar>
        </div>
      )}

      {/* ── Toolbar (only shown when a config is selected) ── */}
      {selectedConfig && !isLoading && (
        <div className={styles.toolbar} role="toolbar" aria-label="Recycle bin actions">
          {/* Restore button */}
          <Tooltip
            content={
              hasSelection
                ? `Restore "${selectedItem!.displayName}" to active containers`
                : "Select a container to restore"
            }
            relationship="description"
          >
            <Button
              appearance="primary"
              icon={<ArrowUndo20Regular />}
              onClick={handleRestore}
              disabled={!hasSelection || isActionInProgress}
              aria-label="Restore selected container"
            >
              {isRestoring ? "Restoring..." : "Restore"}
            </Button>
          </Tooltip>

          {/* Permanent Delete button — opens confirmation dialog */}
          <Dialog open={deleteDialogOpen} onOpenChange={(_e, data) => setDeleteDialogOpen(data.open)}>
            <DialogTrigger disableButtonEnhancement>
              <Tooltip
                content={
                  hasSelection
                    ? `Permanently delete "${selectedItem!.displayName}" — this action is irreversible`
                    : "Select a container to permanently delete"
                }
                relationship="description"
              >
                <Button
                  appearance="secondary"
                  icon={<Delete20Regular />}
                  disabled={!hasSelection || isActionInProgress}
                  aria-label="Permanently delete selected container"
                  style={{ color: hasSelection ? tokens.colorPaletteRedForeground1 : undefined }}
                >
                  {isDeleting ? "Deleting..." : "Permanent Delete"}
                </Button>
              </Tooltip>
            </DialogTrigger>

            {/* Confirmation dialog */}
            <DialogSurface>
              <DialogBody>
                <DialogTitle>Permanently Delete Container</DialogTitle>
                <DialogContent>
                  <Text>
                    Are you sure you want to permanently delete{" "}
                    <strong>{selectedItem?.displayName}</strong>?
                  </Text>
                  <br />
                  <br />
                  <Text className={styles.warningText}>
                    This action is irreversible. The container and all its
                    contents will be permanently destroyed and cannot be
                    recovered.
                  </Text>
                </DialogContent>
                <DialogActions>
                  <DialogTrigger disableButtonEnhancement>
                    <Button appearance="secondary">Cancel</Button>
                  </DialogTrigger>
                  <Button
                    appearance="primary"
                    onClick={handlePermanentDeleteConfirm}
                    style={{ backgroundColor: tokens.colorPaletteRedBackground3 }}
                  >
                    Delete Permanently
                  </Button>
                </DialogActions>
              </DialogBody>
            </DialogSurface>
          </Dialog>
        </div>
      )}

      {/* ── Main content area ── */}
      <div className={styles.gridArea}>
        {/* No config selected */}
        {!selectedConfig && renderNoConfig()}

        {/* Loading state */}
        {selectedConfig && isLoading && renderLoading()}

        {/* Error state */}
        {selectedConfig && !isLoading && error && renderError()}

        {/* Empty state */}
        {selectedConfig && !isLoading && !error && items.length === 0 && renderEmpty()}

        {/* Data grid */}
        {selectedConfig && !isLoading && !error && items.length > 0 && (
          <>
            {/* Row count */}
            <Text size={200} className={styles.rowCount}>
              {items.length} deleted container{items.length !== 1 ? "s" : ""}
              {hasSelection ? ` — 1 selected` : ""}
            </Text>

            <div className={styles.gridWrapper}>
              <DataGrid
                items={items}
                columns={columns}
                sortable
                selectionMode="single"
                selectedItems={selectedRows}
                onSelectionChange={handleSelectionChange}
                getRowId={(item: DeletedContainer) => item.id}
                style={{ width: "100%" }}
                aria-label="Deleted containers"
              >
                <DataGridHeader>
                  <DataGridRow>
                    {({ renderHeaderCell }) => (
                      <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
                    )}
                  </DataGridRow>
                </DataGridHeader>
                <DataGridBody<DeletedContainer>>
                  {({ item, rowId }) => (
                    <DataGridRow<DeletedContainer>
                      key={rowId}
                      aria-selected={selectedRows.has(rowId)}
                    >
                      {({ renderCell }) => (
                        <DataGridCell>{renderCell(item)}</DataGridCell>
                      )}
                    </DataGridRow>
                  )}
                </DataGridBody>
              </DataGrid>
            </div>
          </>
        )}
      </div>
    </div>
  );
};
