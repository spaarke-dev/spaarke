/**
 * EnvironmentConfig — manages SPE environment configuration records.
 *
 * Displays a data grid listing all sprk_speenvironment records, with a
 * command toolbar for add, edit, and delete operations.
 *
 * Fields managed:
 *   name         — display name (required)
 *   tenantId     — Azure AD tenant GUID (required)
 *   tenantName   — Tenant display name
 *   rootSiteUrl  — SharePoint root site URL (required)
 *   graphEndpoint — Microsoft Graph API base URL
 *   isDefault    — toggle: one default per tenant
 *   status       — active | inactive dropdown
 *
 * Validation: name, tenantId, rootSiteUrl are required.
 * Delete shows a confirmation dialog before removing.
 *
 * ADR-021: All styles use Fluent UI v9 makeStyles + design tokens (no hard-coded colours).
 * ADR-012: Fluent v9 components only; shared library barrel imports safe for Code Pages.
 * ADR-006: Code Page — React 18 patterns, no PCF / ComponentFramework dependencies.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  shorthands,
  Text,
  Spinner,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  MessageBarActions,
  Badge,
  Toolbar,
  ToolbarButton,
  ToolbarDivider,
  Tooltip,
  DataGrid,
  DataGridBody,
  DataGridCell,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridRow,
  createTableColumn,
  type TableColumnDefinition,
  type OnSelectionChangeData,
  type TableRowId,
  useTableFeatures,
  useTableSelection,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Button,
  Input,
  Field,
  Select,
  Switch,
} from "@fluentui/react-components";
import {
  Add20Regular,
  Edit20Regular,
  Delete20Regular,
  ArrowClockwise20Regular,
  CloudDatabase20Regular,
  StarFilled,
} from "@fluentui/react-icons";
import { speApiClient, ApiError } from "../../services/speApiClient";
import type { SpeEnvironment, SpeEnvironmentUpsert, ActiveStatus } from "../../types/spe";

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021 — Fluent tokens only)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    overflow: "hidden",
  },

  toolbar: {
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    minHeight: "44px",
    display: "flex",
    alignItems: "center",
    flexShrink: 0,
  },

  content: {
    flex: "1 1 auto",
    overflow: "auto",
    minHeight: 0,
  },

  feedback: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalXXL,
    ...shorthands.gap(tokens.spacingVerticalM),
    height: "100%",
    color: tokens.colorNeutralForeground2,
  },

  messageBarWrapper: {
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
  },

  dataGrid: {
    width: "100%",
  },

  buttonLabel: {
    marginLeft: tokens.spacingHorizontalXS,
  },

  defaultBadge: {
    display: "inline-flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorBrandForeground1,
  },

  // Form dialog fields
  formField: {
    marginTop: tokens.spacingVerticalS,
  },

  switchRow: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    marginTop: tokens.spacingVerticalM,
  },

  dialogContent: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Column Definitions
// ─────────────────────────────────────────────────────────────────────────────

function buildColumns(
  styles: ReturnType<typeof useStyles>
): TableColumnDefinition<SpeEnvironment>[] {
  return [
    createTableColumn<SpeEnvironment>({
      columnId: "name",
      renderHeaderCell: () => "Name",
      renderCell: (env) => (
        <span style={{ display: "flex", alignItems: "center", gap: tokens.spacingHorizontalXS }}>
          <Text weight="semibold" truncate>
            {env.name}
          </Text>
          {env.isDefault && (
            <span className={styles.defaultBadge}>
              <StarFilled style={{ fontSize: "14px" }} />
              <Text size={100}>Default</Text>
            </span>
          )}
        </span>
      ),
    }),
    createTableColumn<SpeEnvironment>({
      columnId: "tenantName",
      renderHeaderCell: () => "Tenant",
      renderCell: (env) => (
        <Text truncate title={env.tenantId}>
          {env.tenantName || env.tenantId}
        </Text>
      ),
    }),
    createTableColumn<SpeEnvironment>({
      columnId: "rootSiteUrl",
      renderHeaderCell: () => "Root Site URL",
      renderCell: (env) => (
        <Text size={200} truncate title={env.rootSiteUrl}>
          {env.rootSiteUrl}
        </Text>
      ),
    }),
    createTableColumn<SpeEnvironment>({
      columnId: "status",
      renderHeaderCell: () => "Status",
      renderCell: (env) => (
        <Badge
          color={env.status === "active" ? "success" : "warning"}
          appearance="filled"
          size="small"
        >
          {env.status.charAt(0).toUpperCase() + env.status.slice(1)}
        </Badge>
      ),
    }),
  ];
}

// ─────────────────────────────────────────────────────────────────────────────
// Form state helpers
// ─────────────────────────────────────────────────────────────────────────────

interface EnvironmentFormState {
  name: string;
  tenantId: string;
  tenantName: string;
  rootSiteUrl: string;
  graphEndpoint: string;
  isDefault: boolean;
  status: ActiveStatus;
}

const defaultFormState: EnvironmentFormState = {
  name: "",
  tenantId: "",
  tenantName: "",
  rootSiteUrl: "",
  graphEndpoint: "https://graph.microsoft.com/v1.0",
  isDefault: false,
  status: "active",
};

function formStateFromEnv(env: SpeEnvironment): EnvironmentFormState {
  return {
    name: env.name,
    tenantId: env.tenantId,
    tenantName: env.tenantName,
    rootSiteUrl: env.rootSiteUrl,
    graphEndpoint: env.graphEndpoint,
    isDefault: env.isDefault,
    status: env.status,
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Validation
// ─────────────────────────────────────────────────────────────────────────────

interface FormErrors {
  name?: string;
  tenantId?: string;
  rootSiteUrl?: string;
}

function validate(form: EnvironmentFormState): FormErrors {
  const errors: FormErrors = {};
  if (!form.name.trim()) errors.name = "Name is required.";
  if (!form.tenantId.trim()) errors.tenantId = "Tenant ID is required.";
  if (!form.rootSiteUrl.trim()) errors.rootSiteUrl = "Root Site URL is required.";
  return errors;
}

// ─────────────────────────────────────────────────────────────────────────────
// EnvironmentFormDialog — Add / Edit dialog
// ─────────────────────────────────────────────────────────────────────────────

interface EnvironmentFormDialogProps {
  open: boolean;
  isSaving: boolean;
  mode: "add" | "edit";
  initialValues: EnvironmentFormState;
  onClose: () => void;
  onSubmit: (form: EnvironmentFormState) => void;
}

const EnvironmentFormDialog: React.FC<EnvironmentFormDialogProps> = ({
  open,
  isSaving,
  mode,
  initialValues,
  onClose,
  onSubmit,
}) => {
  const styles = useStyles();
  const [form, setForm] = React.useState<EnvironmentFormState>(initialValues);
  const [errors, setErrors] = React.useState<FormErrors>({});

  // Reset form when dialog opens with new initial values
  React.useEffect(() => {
    if (open) {
      setForm(initialValues);
      setErrors({});
    }
  }, [open, initialValues]);

  const setField = React.useCallback(
    <K extends keyof EnvironmentFormState>(key: K, value: EnvironmentFormState[K]) => {
      setForm((prev) => ({ ...prev, [key]: value }));
      // Clear the error for this field when user types
      setErrors((prev) => {
        if (key in prev) {
          const next = { ...prev };
          delete (next as Record<string, string | undefined>)[key];
          return next;
        }
        return prev;
      });
    },
    []
  );

  const handleSubmit = React.useCallback(() => {
    const trimmed: EnvironmentFormState = {
      ...form,
      name: form.name.trim(),
      tenantId: form.tenantId.trim(),
      tenantName: form.tenantName.trim(),
      rootSiteUrl: form.rootSiteUrl.trim(),
      graphEndpoint: form.graphEndpoint.trim(),
    };
    const validationErrors = validate(trimmed);
    if (Object.keys(validationErrors).length > 0) {
      setErrors(validationErrors);
      return;
    }
    onSubmit(trimmed);
  }, [form, onSubmit]);

  const title = mode === "add" ? "Add Environment" : "Edit Environment";
  const submitLabel = isSaving
    ? mode === "add"
      ? "Adding…"
      : "Saving…"
    : mode === "add"
      ? "Add"
      : "Save";

  return (
    <Dialog open={open} onOpenChange={(_e, { open: isOpen }) => { if (!isOpen) onClose(); }}>
      <DialogSurface style={{ maxWidth: "520px" }}>
        <DialogTitle>{title}</DialogTitle>
        <DialogBody>
          <DialogContent>
            <div className={styles.dialogContent}>
              {/* Name */}
              <Field
                label="Name"
                required
                validationMessage={errors.name}
                validationState={errors.name ? "error" : "none"}
              >
                <Input
                  value={form.name}
                  onChange={(_e, d) => setField("name", d.value)}
                  placeholder="e.g. Production"
                  disabled={isSaving}
                  autoFocus
                />
              </Field>

              {/* Tenant ID */}
              <Field
                className={styles.formField}
                label="Tenant ID"
                required
                validationMessage={errors.tenantId}
                validationState={errors.tenantId ? "error" : "none"}
                hint="Azure AD tenant GUID (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)"
              >
                <Input
                  value={form.tenantId}
                  onChange={(_e, d) => setField("tenantId", d.value)}
                  placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                  disabled={isSaving}
                  style={{ fontFamily: "monospace" }}
                />
              </Field>

              {/* Tenant Name */}
              <Field
                className={styles.formField}
                label="Tenant Name"
                hint="Display name of the Azure AD tenant"
              >
                <Input
                  value={form.tenantName}
                  onChange={(_e, d) => setField("tenantName", d.value)}
                  placeholder="e.g. Contoso Legal, Inc."
                  disabled={isSaving}
                />
              </Field>

              {/* Root Site URL */}
              <Field
                className={styles.formField}
                label="Root Site URL"
                required
                validationMessage={errors.rootSiteUrl}
                validationState={errors.rootSiteUrl ? "error" : "none"}
                hint="SharePoint root site URL for this tenant"
              >
                <Input
                  value={form.rootSiteUrl}
                  onChange={(_e, d) => setField("rootSiteUrl", d.value)}
                  placeholder="https://contoso.sharepoint.com"
                  disabled={isSaving}
                />
              </Field>

              {/* Graph Endpoint */}
              <Field
                className={styles.formField}
                label="Graph Endpoint"
                hint="Microsoft Graph API base URL (default: https://graph.microsoft.com/v1.0)"
              >
                <Input
                  value={form.graphEndpoint}
                  onChange={(_e, d) => setField("graphEndpoint", d.value)}
                  placeholder="https://graph.microsoft.com/v1.0"
                  disabled={isSaving}
                />
              </Field>

              {/* Status */}
              <Field className={styles.formField} label="Status">
                <Select
                  value={form.status}
                  onChange={(_e, d) => setField("status", d.value as ActiveStatus)}
                  disabled={isSaving}
                >
                  <option value="active">Active</option>
                  <option value="inactive">Inactive</option>
                </Select>
              </Field>

              {/* isDefault toggle */}
              <div className={styles.switchRow}>
                <Switch
                  checked={form.isDefault}
                  onChange={(_e, d) => setField("isDefault", d.checked)}
                  disabled={isSaving}
                  label="Set as default environment"
                />
              </div>
            </div>
          </DialogContent>

          <DialogActions>
            <Button appearance="secondary" onClick={onClose} disabled={isSaving}>
              Cancel
            </Button>
            <Button
              appearance="primary"
              onClick={handleSubmit}
              disabled={isSaving}
              icon={isSaving ? <Spinner size="tiny" /> : undefined}
            >
              {submitLabel}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// DeleteConfirmDialog
// ─────────────────────────────────────────────────────────────────────────────

interface DeleteConfirmDialogProps {
  open: boolean;
  isDeleting: boolean;
  environmentName: string;
  onClose: () => void;
  onConfirm: () => void;
}

const DeleteConfirmDialog: React.FC<DeleteConfirmDialogProps> = ({
  open,
  isDeleting,
  environmentName,
  onClose,
  onConfirm,
}) => (
  <Dialog open={open} onOpenChange={(_e, { open: isOpen }) => { if (!isOpen) onClose(); }}>
    <DialogSurface>
      <DialogTitle>Delete Environment</DialogTitle>
      <DialogBody>
        <DialogContent>
          <Text>
            Are you sure you want to delete{" "}
            <Text weight="semibold">{environmentName}</Text>? This action cannot be undone.
          </Text>
        </DialogContent>
        <DialogActions>
          <Button appearance="secondary" onClick={onClose} disabled={isDeleting}>
            Cancel
          </Button>
          <Button
            appearance="primary"
            style={{ backgroundColor: tokens.colorPaletteRedBackground3, color: tokens.colorNeutralForegroundOnBrand }}
            onClick={onConfirm}
            disabled={isDeleting}
            icon={isDeleting ? <Spinner size="tiny" /> : undefined}
          >
            {isDeleting ? "Deleting…" : "Delete"}
          </Button>
        </DialogActions>
      </DialogBody>
    </DialogSurface>
  </Dialog>
);

// ─────────────────────────────────────────────────────────────────────────────
// EnvironmentDataGrid (inner component)
// ─────────────────────────────────────────────────────────────────────────────

interface EnvironmentDataGridProps {
  environments: SpeEnvironment[];
  columns: TableColumnDefinition<SpeEnvironment>[];
  selectedIds: Set<TableRowId>;
  onSelectionChange: (e: React.SyntheticEvent, data: OnSelectionChangeData) => void;
  className?: string;
}

const EnvironmentDataGrid: React.FC<EnvironmentDataGridProps> = ({
  environments,
  columns,
  selectedIds,
  onSelectionChange,
  className,
}) => {
  const {
    getRows,
    selection: {
      allRowsSelected,
      toggleAllRows,
      toggleRow,
      isRowSelected,
    },
  } = useTableFeatures(
    { columns, items: environments },
    [
      useTableSelection({
        selectionMode: "multiselect",
        selectedItems: selectedIds,
        onSelectionChange,
      }),
    ]
  );

  const rows = getRows((row) => {
    const isSelected = isRowSelected(row.rowId);
    return {
      ...row,
      onClick: (e: React.MouseEvent) => toggleRow(e, row.rowId),
      onKeyDown: (e: React.KeyboardEvent) => {
        if (e.key === " " || e.key === "Enter") {
          e.preventDefault();
          toggleRow(e as unknown as React.MouseEvent, row.rowId);
        }
      },
      selected: isSelected,
      appearance: isSelected ? ("brand" as const) : ("none" as const),
    };
  });

  return (
    <DataGrid
      items={environments}
      columns={columns}
      sortable={false}
      selectionMode="multiselect"
      selectedItems={selectedIds}
      onSelectionChange={onSelectionChange}
      getRowId={(env) => env.id}
      className={className}
      aria-label="Environments"
    >
      <DataGridHeader>
        <DataGridRow
          selectionCell={{
            checkboxIndicator: { "aria-label": "Select all environments" },
          }}
          aria-selected={allRowsSelected}
          onClick={(e: React.MouseEvent<HTMLTableRowElement>) => toggleAllRows(e)}
          onKeyDown={(e: React.KeyboardEvent<HTMLTableRowElement>) => {
            if (e.key === " " || e.key === "Enter") {
              e.preventDefault();
              toggleAllRows(e as unknown as React.MouseEvent);
            }
          }}
        >
          {({ renderHeaderCell }) => (
            <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
          )}
        </DataGridRow>
      </DataGridHeader>
      <DataGridBody<SpeEnvironment>>
        {({ item, rowId }) => {
          const row = rows.find((r) => r.rowId === rowId);
          return (
            <DataGridRow<SpeEnvironment>
              key={rowId}
              selectionCell={{
                checkboxIndicator: { "aria-label": `Select ${item.name}` },
              }}
              aria-selected={row?.selected}
              onClick={row?.onClick}
              onKeyDown={row?.onKeyDown}
              appearance={row?.appearance}
              tabIndex={0}
            >
              {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
            </DataGridRow>
          );
        }}
      </DataGridBody>
    </DataGrid>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// EnvironmentConfig — main component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * EnvironmentConfig — SPE environment CRUD management panel.
 *
 * Renders within the Settings page Environments tab.
 * Loads environment records from the BFF API on mount.
 * Toolbar provides Add, Edit, Delete, and Refresh operations.
 */
export const EnvironmentConfig: React.FC = () => {
  const styles = useStyles();

  // ── Data State ──────────────────────────────────────────────────────────────

  const [environments, setEnvironments] = React.useState<SpeEnvironment[]>([]);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // ── Selection State ─────────────────────────────────────────────────────────

  const [selectedIds, setSelectedIds] = React.useState<Set<TableRowId>>(new Set());

  // ── Action Feedback State ───────────────────────────────────────────────────

  const [actionError, setActionError] = React.useState<string | null>(null);
  const [actionStatus, setActionStatus] = React.useState<string | null>(null);

  // ── Dialog State ────────────────────────────────────────────────────────────

  const [addOpen, setAddOpen] = React.useState(false);
  const [editOpen, setEditOpen] = React.useState(false);
  const [deleteOpen, setDeleteOpen] = React.useState(false);
  const [isSaving, setIsSaving] = React.useState(false);
  const [isDeleting, setIsDeleting] = React.useState(false);

  /** The environment being edited (null when adding). */
  const [editTarget, setEditTarget] = React.useState<SpeEnvironment | null>(null);

  // ── Column Definitions (stable reference) ──────────────────────────────────

  const columns = React.useMemo(() => buildColumns(styles), [styles]);

  // ── Derived: selected environment objects ───────────────────────────────────

  const selectedEnvironments = React.useMemo<SpeEnvironment[]>(
    () => environments.filter((e) => selectedIds.has(e.id as TableRowId)),
    [environments, selectedIds]
  );

  const hasSingleSelection = selectedEnvironments.length === 1;

  // ── Data Loading ────────────────────────────────────────────────────────────

  const loadEnvironments = React.useCallback(async () => {
    setLoading(true);
    setError(null);
    setActionError(null);
    setActionStatus(null);
    setSelectedIds(new Set());
    try {
      const data = await speApiClient.environments.list();
      setEnvironments(data);
    } catch (err) {
      const message =
        err instanceof ApiError
          ? err.message
          : "Failed to load environments. Please try again.";
      setError(message);
    } finally {
      setLoading(false);
    }
  }, []);

  React.useEffect(() => {
    void loadEnvironments();
  }, [loadEnvironments]);

  // ── Row Selection Handler ───────────────────────────────────────────────────

  const handleSelectionChange = React.useCallback(
    (_e: React.SyntheticEvent, data: OnSelectionChangeData) => {
      setSelectedIds(new Set(data.selectedItems));
      setActionError(null);
      setActionStatus(null);
    },
    []
  );

  // ── Add Environment ─────────────────────────────────────────────────────────

  const handleAddSubmit = React.useCallback(
    async (form: EnvironmentFormState) => {
      setIsSaving(true);
      setActionError(null);
      setActionStatus(null);
      try {
        const payload: SpeEnvironmentUpsert = {
          name: form.name,
          tenantId: form.tenantId,
          tenantName: form.tenantName,
          rootSiteUrl: form.rootSiteUrl,
          graphEndpoint: form.graphEndpoint,
          isDefault: form.isDefault,
          status: form.status,
        };
        await speApiClient.environments.create(payload);
        setAddOpen(false);
        setActionStatus(`Environment "${form.name}" added successfully.`);
        await loadEnvironments();
      } catch (err) {
        const message =
          err instanceof ApiError ? err.message : "Failed to add environment.";
        setActionError(message);
      } finally {
        setIsSaving(false);
      }
    },
    [loadEnvironments]
  );

  // ── Edit Environment ────────────────────────────────────────────────────────

  const handleOpenEdit = React.useCallback(() => {
    if (!hasSingleSelection) return;
    setEditTarget(selectedEnvironments[0]);
    setEditOpen(true);
  }, [hasSingleSelection, selectedEnvironments]);

  const handleEditSubmit = React.useCallback(
    async (form: EnvironmentFormState) => {
      if (!editTarget) return;
      setIsSaving(true);
      setActionError(null);
      setActionStatus(null);
      try {
        const payload: SpeEnvironmentUpsert = {
          name: form.name,
          tenantId: form.tenantId,
          tenantName: form.tenantName,
          rootSiteUrl: form.rootSiteUrl,
          graphEndpoint: form.graphEndpoint,
          isDefault: form.isDefault,
          status: form.status,
        };
        await speApiClient.environments.update(editTarget.id, payload);
        setEditOpen(false);
        setEditTarget(null);
        setActionStatus(`Environment "${form.name}" updated successfully.`);
        await loadEnvironments();
      } catch (err) {
        const message =
          err instanceof ApiError ? err.message : "Failed to update environment.";
        setActionError(message);
      } finally {
        setIsSaving(false);
      }
    },
    [editTarget, loadEnvironments]
  );

  // ── Delete Environment ──────────────────────────────────────────────────────

  const handleOpenDelete = React.useCallback(() => {
    if (!hasSingleSelection) return;
    setEditTarget(selectedEnvironments[0]);
    setDeleteOpen(true);
  }, [hasSingleSelection, selectedEnvironments]);

  const handleDeleteConfirm = React.useCallback(async () => {
    if (!editTarget) return;
    setIsDeleting(true);
    setActionError(null);
    setActionStatus(null);
    try {
      await speApiClient.environments.delete(editTarget.id);
      setDeleteOpen(false);
      const deletedName = editTarget.name;
      setEditTarget(null);
      setActionStatus(`Environment "${deletedName}" deleted.`);
      await loadEnvironments();
    } catch (err) {
      const message =
        err instanceof ApiError ? err.message : "Failed to delete environment.";
      setActionError(message);
      setDeleteOpen(false);
    } finally {
      setIsDeleting(false);
    }
  }, [editTarget, loadEnvironments]);

  // ── Toolbar disabled states ──────────────────────────────────────────────────

  const isEditDisabled = !hasSingleSelection || loading || isSaving;
  const isDeleteDisabled = !hasSingleSelection || loading || isSaving;

  // ── Render ───────────────────────────────────────────────────────────────────

  return (
    <div className={styles.root}>
      {/* ── Command Toolbar ── */}
      <Toolbar aria-label="Environment actions" className={styles.toolbar}>
        {/* Add */}
        <Tooltip content="Add a new environment" relationship="description">
          <ToolbarButton
            icon={<Add20Regular />}
            onClick={() => setAddOpen(true)}
            disabled={loading || isSaving}
            aria-label="Add environment"
          >
            <span className={styles.buttonLabel}>Add</span>
          </ToolbarButton>
        </Tooltip>

        {/* Edit */}
        <Tooltip
          content={hasSingleSelection ? "Edit selected environment" : "Select one environment to edit"}
          relationship="description"
        >
          <ToolbarButton
            icon={<Edit20Regular />}
            onClick={handleOpenEdit}
            disabled={isEditDisabled}
            aria-label="Edit environment"
          >
            <span className={styles.buttonLabel}>Edit</span>
          </ToolbarButton>
        </Tooltip>

        <ToolbarDivider />

        {/* Delete */}
        <Tooltip
          content={hasSingleSelection ? "Delete selected environment" : "Select one environment to delete"}
          relationship="description"
        >
          <ToolbarButton
            icon={<Delete20Regular />}
            onClick={handleOpenDelete}
            disabled={isDeleteDisabled}
            aria-label="Delete environment"
          >
            <span className={styles.buttonLabel}>Delete</span>
          </ToolbarButton>
        </Tooltip>

        <ToolbarDivider />

        {/* Refresh */}
        <Tooltip content="Refresh environment list" relationship="description">
          <ToolbarButton
            icon={
              loading && !isSaving ? (
                <Spinner size="tiny" />
              ) : (
                <ArrowClockwise20Regular />
              )
            }
            onClick={() => { void loadEnvironments(); }}
            disabled={loading || isSaving}
            aria-label="Refresh environments"
          >
            <span className={styles.buttonLabel}>Refresh</span>
          </ToolbarButton>
        </Tooltip>
      </Toolbar>

      {/* ── Status / Error Banners ── */}
      {(actionError || actionStatus) && (
        <div className={styles.messageBarWrapper}>
          {actionError && (
            <MessageBar intent="error">
              <MessageBarBody>
                <MessageBarTitle>Action failed</MessageBarTitle>
                {actionError}
              </MessageBarBody>
              <MessageBarActions
                containerAction={
                  <Button
                    appearance="transparent"
                    size="small"
                    onClick={() => setActionError(null)}
                    aria-label="Dismiss error"
                  >
                    Dismiss
                  </Button>
                }
              />
            </MessageBar>
          )}
          {actionStatus && !actionError && (
            <MessageBar intent="success">
              <MessageBarBody>{actionStatus}</MessageBarBody>
              <MessageBarActions
                containerAction={
                  <Button
                    appearance="transparent"
                    size="small"
                    onClick={() => setActionStatus(null)}
                    aria-label="Dismiss"
                  >
                    Dismiss
                  </Button>
                }
              />
            </MessageBar>
          )}
        </div>
      )}

      {/* ── Content: Loading / Error / Grid ── */}
      <div className={styles.content}>
        {loading && environments.length === 0 ? (
          <div className={styles.feedback}>
            <Spinner size="medium" label="Loading environments…" />
          </div>
        ) : error ? (
          <div className={styles.feedback}>
            <MessageBar intent="error">
              <MessageBarBody>
                <MessageBarTitle>Failed to load environments</MessageBarTitle>
                {error}
              </MessageBarBody>
            </MessageBar>
            <Button
              appearance="secondary"
              icon={<ArrowClockwise20Regular />}
              onClick={() => { void loadEnvironments(); }}
            >
              Retry
            </Button>
          </div>
        ) : environments.length === 0 ? (
          <div className={styles.feedback}>
            <CloudDatabase20Regular style={{ fontSize: "48px", opacity: 0.4 }} />
            <Text size={400} weight="semibold">
              No environments configured
            </Text>
            <Text size={300}>
              Use the <strong>Add</strong> button to configure your first SPE environment.
            </Text>
          </div>
        ) : (
          <EnvironmentDataGrid
            environments={environments}
            columns={columns}
            selectedIds={selectedIds}
            onSelectionChange={handleSelectionChange}
            className={styles.dataGrid}
          />
        )}
      </div>

      {/* ── Add Environment Dialog ── */}
      <EnvironmentFormDialog
        open={addOpen}
        isSaving={isSaving}
        mode="add"
        initialValues={defaultFormState}
        onClose={() => setAddOpen(false)}
        onSubmit={(form) => { void handleAddSubmit(form); }}
      />

      {/* ── Edit Environment Dialog ── */}
      <EnvironmentFormDialog
        open={editOpen}
        isSaving={isSaving}
        mode="edit"
        initialValues={editTarget ? formStateFromEnv(editTarget) : defaultFormState}
        onClose={() => {
          setEditOpen(false);
          setEditTarget(null);
        }}
        onSubmit={(form) => { void handleEditSubmit(form); }}
      />

      {/* ── Delete Confirmation Dialog ── */}
      <DeleteConfirmDialog
        open={deleteOpen}
        isDeleting={isDeleting}
        environmentName={editTarget?.name ?? ""}
        onClose={() => {
          setDeleteOpen(false);
          setEditTarget(null);
        }}
        onConfirm={() => { void handleDeleteConfirm(); }}
      />
    </div>
  );
};
