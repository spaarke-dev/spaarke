/**
 * ColumnEditor — full CRUD management component for container column definitions.
 *
 * Displays a table of existing column definitions (name, type, description,
 * required flag) with toolbar actions for Add, Edit, and Delete. Invoked as
 * the Columns tab in ContainerDetail.
 *
 * Data strategy:
 *   - Columns are loaded lazily by ContainerDetail and passed in via props.
 *   - ColumnEditor calls onColumnsChange after each successful CRUD operation
 *     so ContainerDetail can keep its state in sync.
 *   - All API calls use speApiClient.columns.{create|update|delete}.
 *
 * Layout:
 *   - Toolbar: Add Column (primary), Edit (single-select), Delete (single-select), Refresh
 *   - Table: Name, Type badge, Description, Required
 *   - AddEditColumnDialog: full-featured dialog with type-specific fields
 *   - DeleteColumnDialog: simple yes/no confirmation
 *
 * ADR-021: All styles use makeStyles + design tokens. No hard-coded colors.
 * ADR-012: Fluent UI v9 components exclusively; @spaarke/ui-components not needed here
 *          (the grid is small enough that native Fluent Table is appropriate).
 * ADR-006: Code Page — React 18, no PCF / ComponentFramework dependencies.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
  Badge,
  Button,
  Table,
  TableHeader,
  TableHeaderCell,
  TableBody,
  TableRow,
  TableCell,
  TableCellLayout,
  MessageBar,
  MessageBarBody,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Toolbar,
  ToolbarButton,
  ToolbarDivider,
  shorthands,
} from "@fluentui/react-components";
import {
  Add20Regular,
  Edit20Regular,
  Delete20Regular,
  ArrowClockwise20Regular,
  ColumnTriple20Regular,
} from "@fluentui/react-icons";
import { speApiClient, ApiError } from "../../services/speApiClient";
import type { ColumnDefinition, ColumnDefinitionUpsert } from "../../types/spe";
import { AddEditColumnDialog } from "./AddEditColumnDialog";

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface ColumnEditorProps {
  /** ID of the container whose columns are managed. */
  containerId: string;
  /** Config ID for scoping all API calls. */
  configId: string;
  /** Current list of columns (may be null if not yet loaded). */
  columns: ColumnDefinition[] | null;
  /** Whether the initial column list is still loading. */
  loading: boolean;
  /** Error message if the initial load failed. */
  error: string | null;
  /** Called when the columns list changes (add/edit/delete). */
  onColumnsChange: (columns: ColumnDefinition[]) => void;
  /** Called to trigger a reload of columns (e.g. after an error). */
  onRetry: () => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Utilities
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Derive a human-readable type label from a ColumnDefinition.
 * Checks the column facet properties to identify the type.
 */
function getColumnTypeLabel(col: ColumnDefinition): string {
  if (col.boolean !== undefined) return "Boolean";
  if (col.dateTime !== undefined) return "Date & Time";
  if (col.choice !== undefined) return "Choice";
  if (col.number !== undefined) {
    const numFacet = col.number as Record<string, unknown>;
    if (numFacet?.currencyLocale) return "Currency";
    return "Number";
  }
  if (col.text !== undefined) return "Text";
  const group = col.columnGroup?.toLowerCase() ?? "";
  if (group.includes("person") || group.includes("user")) return "Person / Group";
  if (group.includes("currency")) return "Currency";
  if (group.includes("hyperlink") || group.includes("picture")) return "Hyperlink / Picture";
  return "Text";
}

/**
 * Map a column type label to a Badge color for visual distinction.
 */
function typeBadgeColor(
  typeLabel: string
): "brand" | "informative" | "success" | "warning" | "danger" | "subtle" {
  switch (typeLabel) {
    case "Text":
      return "subtle";
    case "Number":
    case "Currency":
      return "success";
    case "Boolean":
      return "warning";
    case "Date & Time":
      return "informative";
    case "Choice":
      return "brand";
    case "Person / Group":
      return "informative";
    case "Hyperlink / Picture":
      return "subtle";
    default:
      return "subtle";
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021 — Fluent tokens only)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalS),
  },

  toolbar: {
    paddingBottom: tokens.spacingVerticalXS,
  },

  table: {
    width: "100%",
  },

  /** Loading / error / empty feedback area. */
  feedback: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.gap(tokens.spacingVerticalM),
    paddingTop: tokens.spacingVerticalXL,
    paddingBottom: tokens.spacingVerticalXL,
    color: tokens.colorNeutralForeground2,
  },

  /** Empty state when no columns exist. */
  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingVerticalS),
    paddingTop: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground3,
  },

  /** Description cell — clamped to 2 lines. */
  descriptionCell: {
    display: "-webkit-box",
    WebkitLineClamp: "2",
    WebkitBoxOrient: "vertical",
    overflow: "hidden",
    color: tokens.colorNeutralForeground2,
  },

  /** Name column: two-line layout (display name + internal name). */
  internalName: {
    display: "block",
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
    lineHeight: tokens.lineHeightBase100,
    fontFamily: tokens.fontFamilyMonospace,
  },

  /** Dialog confirmation content. */
  deleteDialogContent: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalM),
    paddingTop: tokens.spacingVerticalS,
  },

  deleteWarning: {
    color: tokens.colorPaletteRedForeground1,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Delete Confirmation Dialog
// ─────────────────────────────────────────────────────────────────────────────

interface DeleteColumnDialogProps {
  open: boolean;
  column: ColumnDefinition | null;
  saving: boolean;
  error: string | null;
  onDelete: (colId: string) => Promise<void>;
  onDismiss: () => void;
}

const DeleteColumnDialog: React.FC<DeleteColumnDialogProps> = ({
  open,
  column,
  saving,
  error,
  onDelete,
  onDismiss,
}) => {
  const styles = useStyles();

  const handleConfirm = React.useCallback(async () => {
    if (!column) return;
    await onDelete(column.id);
  }, [column, onDelete]);

  return (
    <Dialog open={open} onOpenChange={(_, data) => !data.open && onDismiss()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>Delete Column</DialogTitle>
          <DialogContent className={styles.deleteDialogContent}>
            <Text size={300} className={styles.deleteWarning} weight="semibold">
              This action cannot be undone.
            </Text>
            <Text size={200}>
              Delete the column{" "}
              <Text weight="semibold">{column?.displayName ?? column?.name ?? ""}</Text>
              ? All metadata stored in this column will be permanently removed from items in the
              container.
            </Text>

            {error && (
              <MessageBar intent="error">
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            )}
          </DialogContent>
          <DialogActions>
            <Button
              appearance="primary"
              style={{
                backgroundColor: tokens.colorPaletteRedBackground3,
                color: tokens.colorNeutralForegroundOnBrand,
              }}
              onClick={handleConfirm}
              disabled={saving}
              icon={saving ? <Spinner size="tiny" /> : <Delete20Regular />}
            >
              {saving ? "Deleting…" : "Delete Column"}
            </Button>
            <Button appearance="secondary" onClick={onDismiss} disabled={saving}>
              Cancel
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// ColumnEditor (main component)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * ColumnEditor renders a columns grid with functional Add/Edit/Delete dialogs
 * that call the container column CRUD API endpoints.
 *
 * Designed to replace the read-only ColumnsTab in ContainerDetail with full
 * administrative editing capabilities.
 */
export const ColumnEditor: React.FC<ColumnEditorProps> = ({
  containerId,
  configId,
  columns,
  loading,
  error,
  onColumnsChange,
  onRetry,
}) => {
  const styles = useStyles();

  // ── Selection state (single row) ──────────────────────────────────────────

  const [selectedColId, setSelectedColId] = React.useState<string | null>(null);

  // ── Dialog state ──────────────────────────────────────────────────────────

  const [addEditOpen, setAddEditOpen] = React.useState(false);
  const [editingColumn, setEditingColumn] = React.useState<ColumnDefinition | null>(null);
  const [addEditSaving, setAddEditSaving] = React.useState(false);
  const [addEditError, setAddEditError] = React.useState<string | null>(null);

  const [deleteOpen, setDeleteOpen] = React.useState(false);
  const [deleteSaving, setDeleteSaving] = React.useState(false);
  const [deleteError, setDeleteError] = React.useState<string | null>(null);

  // ── Derived: selected column object ───────────────────────────────────────

  const selectedColumn = React.useMemo(
    () => (columns ?? []).find((c) => c.id === selectedColId) ?? null,
    [columns, selectedColId]
  );

  // ── Clear selection when column list changes ───────────────────────────────

  React.useEffect(() => {
    if (!columns) return;
    setSelectedColId((prev) => (columns.some((c) => c.id === prev) ? prev : null));
  }, [columns]);

  // ── Toolbar handlers ──────────────────────────────────────────────────────

  const handleOpenAdd = React.useCallback(() => {
    setEditingColumn(null);
    setAddEditError(null);
    setAddEditOpen(true);
  }, []);

  const handleOpenEdit = React.useCallback(() => {
    if (!selectedColumn) return;
    setEditingColumn(selectedColumn);
    setAddEditError(null);
    setAddEditOpen(true);
  }, [selectedColumn]);

  const handleOpenDelete = React.useCallback(() => {
    setDeleteError(null);
    setDeleteOpen(true);
  }, []);

  // ── Row click to select ───────────────────────────────────────────────────

  const handleRowClick = React.useCallback((colId: string) => {
    setSelectedColId((prev) => (prev === colId ? null : colId));
  }, []);

  // ── Add / Edit Save ───────────────────────────────────────────────────────

  const handleSave = React.useCallback(
    async (payload: ColumnDefinitionUpsert) => {
      setAddEditSaving(true);
      setAddEditError(null);
      try {
        if (editingColumn) {
          // Edit: PATCH /api/spe/containers/{id}/columns/{colId}
          const updated = await speApiClient.columns.update(
            containerId,
            editingColumn.id,
            configId,
            payload
          );
          onColumnsChange(
            (columns ?? []).map((c) => (c.id === editingColumn.id ? updated : c))
          );
        } else {
          // Add: POST /api/spe/containers/{id}/columns
          const created = await speApiClient.columns.create(containerId, configId, payload);
          onColumnsChange([...(columns ?? []), created]);
        }
        setAddEditOpen(false);
        setEditingColumn(null);
      } catch (err) {
        const message =
          err instanceof ApiError
            ? err.message
            : editingColumn
            ? "Failed to update column."
            : "Failed to create column.";
        setAddEditError(message);
      } finally {
        setAddEditSaving(false);
      }
    },
    [editingColumn, containerId, configId, columns, onColumnsChange]
  );

  // ── Delete ────────────────────────────────────────────────────────────────

  const handleDelete = React.useCallback(
    async (colId: string) => {
      setDeleteSaving(true);
      setDeleteError(null);
      try {
        await speApiClient.columns.delete(containerId, colId, configId);
        onColumnsChange((columns ?? []).filter((c) => c.id !== colId));
        setSelectedColId(null);
        setDeleteOpen(false);
      } catch (err) {
        const message =
          err instanceof ApiError ? err.message : "Failed to delete column.";
        setDeleteError(message);
      } finally {
        setDeleteSaving(false);
      }
    },
    [containerId, configId, columns, onColumnsChange]
  );

  // ── Render: loading state (initial) ──────────────────────────────────────

  if (loading && !columns) {
    return (
      <div className={styles.feedback}>
        <Spinner size="small" label="Loading columns…" />
      </div>
    );
  }

  // ── Render: error state (initial load failed, no data yet) ────────────────

  if (error && !columns) {
    return (
      <div className={styles.feedback}>
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
        <Button
          size="small"
          appearance="secondary"
          icon={<ArrowClockwise20Regular />}
          onClick={onRetry}
        >
          Retry
        </Button>
      </div>
    );
  }

  // ── Render: table (with toolbar) ──────────────────────────────────────────

  const hasColumns = (columns ?? []).length > 0;

  return (
    <div className={styles.root}>
      {/* Toolbar */}
      <Toolbar className={styles.toolbar} aria-label="Column actions">
        <ToolbarButton
          appearance="primary"
          icon={<Add20Regular />}
          onClick={handleOpenAdd}
        >
          Add Column
        </ToolbarButton>
        <ToolbarDivider />
        <ToolbarButton
          icon={<Edit20Regular />}
          disabled={!selectedColId}
          onClick={handleOpenEdit}
        >
          Edit
        </ToolbarButton>
        <ToolbarButton
          icon={<Delete20Regular />}
          disabled={!selectedColId}
          onClick={handleOpenDelete}
        >
          Delete
        </ToolbarButton>
        <ToolbarDivider />
        <ToolbarButton
          icon={<ArrowClockwise20Regular />}
          onClick={onRetry}
          disabled={loading}
          aria-label="Refresh columns"
        />
      </Toolbar>

      {/* Stale-data error banner (data already visible) */}
      {error && columns && (
        <MessageBar intent="warning">
          <MessageBarBody>Could not refresh: {error}</MessageBarBody>
        </MessageBar>
      )}

      {/* Empty state */}
      {!hasColumns ? (
        <div className={styles.emptyState}>
          <ColumnTriple20Regular style={{ fontSize: "32px", opacity: 0.4 }} />
          <Text size={300}>No custom columns defined</Text>
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            Use the Add Column button to define the metadata schema for this container.
          </Text>
        </div>
      ) : (
        <Table className={styles.table} size="small" aria-label="Container columns">
          <TableHeader>
            <TableRow>
              {/* Selection indicator column */}
              <TableHeaderCell style={{ width: "12px" }} />
              <TableHeaderCell>Name</TableHeaderCell>
              <TableHeaderCell>Type</TableHeaderCell>
              <TableHeaderCell>Description</TableHeaderCell>
              <TableHeaderCell style={{ width: "70px" }}>Required</TableHeaderCell>
            </TableRow>
          </TableHeader>
          <TableBody>
            {(columns ?? []).map((col) => {
              const typeLabel = getColumnTypeLabel(col);
              const isSelected = col.id === selectedColId;

              return (
                <TableRow
                  key={col.id}
                  onClick={() => handleRowClick(col.id)}
                  aria-selected={isSelected}
                  style={{
                    cursor: "pointer",
                    backgroundColor: isSelected
                      ? tokens.colorNeutralBackground1Selected
                      : undefined,
                  }}
                >
                  {/* Selection indicator */}
                  <TableCell style={{ width: "12px" }}>
                    {isSelected && (
                      <span
                        style={{
                          display: "inline-block",
                          width: "4px",
                          height: "20px",
                          borderRadius: "2px",
                          backgroundColor: tokens.colorBrandBackground,
                        }}
                      />
                    )}
                  </TableCell>

                  {/* Name column: display name + internal name */}
                  <TableCell>
                    <TableCellLayout>
                      <Text size={200} weight={isSelected ? "semibold" : "regular"}>
                        {col.displayName}
                      </Text>
                      <Text className={styles.internalName}>{col.name}</Text>
                    </TableCellLayout>
                  </TableCell>

                  {/* Type column */}
                  <TableCell>
                    <Badge
                      appearance="tinted"
                      size="small"
                      color={typeBadgeColor(typeLabel)}
                    >
                      {typeLabel}
                    </Badge>
                  </TableCell>

                  {/* Description column */}
                  <TableCell>
                    {col.description ? (
                      <Text size={200} className={styles.descriptionCell}>
                        {col.description}
                      </Text>
                    ) : (
                      <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                        —
                      </Text>
                    )}
                  </TableCell>

                  {/* Required column */}
                  <TableCell style={{ width: "70px" }}>
                    {col.required ? (
                      <Badge appearance="outline" size="small" color="danger">
                        Yes
                      </Badge>
                    ) : (
                      <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                        No
                      </Text>
                    )}
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      )}

      {/* ── Dialogs ─────────────────────────────────────────────────────────── */}

      <AddEditColumnDialog
        open={addEditOpen}
        column={editingColumn}
        saving={addEditSaving}
        error={addEditError}
        onSave={handleSave}
        onDismiss={() => { setAddEditOpen(false); setEditingColumn(null); }}
      />

      <DeleteColumnDialog
        open={deleteOpen}
        column={selectedColumn}
        saving={deleteSaving}
        error={deleteError}
        onDelete={handleDelete}
        onDismiss={() => setDeleteOpen(false)}
      />
    </div>
  );
};
