/**
 * PermissionPanel — full CRUD management component for SPE container permissions.
 *
 * Displays a data grid of current permission assignments (principal, role, actions)
 * and provides Add, Edit, and Remove dialogs for managing permissions via the
 * /api/spe/containers/{containerId}/permissions BFF endpoints.
 *
 * Used by ContainerDetail's Permissions tab when the administrator needs to manage
 * (not just view) container access.
 *
 * Layout:
 *   - Toolbar: Add Permission (primary), Edit (single-select), Remove (single-select)
 *   - Table: principal display name, UPN, role badge, actions column
 *   - AddPermissionDialog: email input + role dropdown
 *   - EditPermissionDialog: role dropdown (pre-filled)
 *   - RemovePermissionDialog: Fluent v9 Dialog for simple yes/no confirmation
 *
 * ADR-012: ChoiceDialog from @spaarke/ui-components is designed for 2-4 exclusive
 *   option choices. For a simple destructive-action confirmation (yes/no), the
 *   standard Fluent v9 Dialog is the appropriate pattern.
 * ADR-021: All styles use makeStyles + design tokens. No hard-coded colors.
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
  Field,
  Input,
  Select,
  Toolbar,
  ToolbarButton,
  ToolbarDivider,
  shorthands,
} from "@fluentui/react-components";
import {
  ArrowClockwise20Regular,
  PersonAdd20Regular,
  Edit20Regular,
  Delete20Regular,
  Person20Regular,
} from "@fluentui/react-icons";
import { speApiClient, ApiError } from "../../services/speApiClient";
import type { ContainerPermission, ContainerRole } from "../../types/spe";

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface PermissionPanelProps {
  /** ID of the container whose permissions are managed. */
  containerId: string;
  /** Config ID for scoping all API calls. */
  configId: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

const ROLE_OPTIONS: { label: string; value: ContainerRole }[] = [
  { label: "Reader", value: "reader" },
  { label: "Writer", value: "writer" },
  { label: "Manager", value: "manager" },
  { label: "Owner", value: "owner" },
];

// ─────────────────────────────────────────────────────────────────────────────
// Utilities
// ─────────────────────────────────────────────────────────────────────────────

/** Extract the principal display name from a permission entry. */
function getPrincipalDisplayName(perm: ContainerPermission): string {
  const identity =
    perm.grantedToV2?.user ??
    perm.grantedToV2?.group ??
    perm.grantedToV2?.siteUser;
  return identity?.displayName ?? identity?.userPrincipalName ?? perm.id;
}

/** Extract the UPN (email) from a permission entry. */
function getPrincipalUpn(perm: ContainerPermission): string | undefined {
  const identity =
    perm.grantedToV2?.user ??
    perm.grantedToV2?.group ??
    perm.grantedToV2?.siteUser;
  return identity?.userPrincipalName;
}

/** Map a ContainerRole to a Fluent Badge color. */
function roleBadgeColor(
  role: ContainerRole
): "brand" | "informative" | "warning" | "subtle" {
  switch (role) {
    case "owner":
      return "brand";
    case "manager":
      return "informative";
    case "writer":
      return "warning";
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

  /** Empty state when no permissions exist. */
  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingVerticalS),
    paddingTop: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground3,
  },

  /** UPN subtitle below display name in the grid. */
  upn: {
    display: "block",
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
    lineHeight: tokens.lineHeightBase100,
  },

  dialogContent: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalM),
    paddingTop: tokens.spacingVerticalS,
  },

  /** Destructive text colour for the Remove dialog. */
  removeWarning: {
    color: tokens.colorPaletteRedForeground1,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Add Permission Dialog
// ─────────────────────────────────────────────────────────────────────────────

interface AddPermissionDialogProps {
  open: boolean;
  saving: boolean;
  error: string | null;
  onAdd: (userPrincipalName: string, role: ContainerRole) => Promise<void>;
  onDismiss: () => void;
}

const AddPermissionDialog: React.FC<AddPermissionDialogProps> = ({
  open,
  saving,
  error,
  onAdd,
  onDismiss,
}) => {
  const styles = useStyles();

  const [upn, setUpn] = React.useState("");
  const [role, setRole] = React.useState<ContainerRole>("reader");
  const [validationError, setValidationError] = React.useState<string | null>(null);

  // Reset form when dialog opens
  React.useEffect(() => {
    if (open) {
      setUpn("");
      setRole("reader");
      setValidationError(null);
    }
  }, [open]);

  const handleConfirm = React.useCallback(async () => {
    const trimmed = upn.trim();
    if (!trimmed) {
      setValidationError("Email / UPN is required.");
      return;
    }
    if (!trimmed.includes("@")) {
      setValidationError("Enter a valid email address or UPN.");
      return;
    }
    setValidationError(null);
    await onAdd(trimmed, role);
  }, [upn, role, onAdd]);

  return (
    <Dialog open={open} onOpenChange={(_, data) => !data.open && onDismiss()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>Add Permission</DialogTitle>
          <DialogContent className={styles.dialogContent}>
            <Text size={200}>
              Enter the user or group email address and select the role to assign.
            </Text>

            {error && (
              <MessageBar intent="error">
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            )}

            <Field
              label="User / Group Email (UPN)"
              required
              validationState={validationError ? "error" : "none"}
              validationMessage={validationError ?? undefined}
            >
              <Input
                value={upn}
                onChange={(_, data) => setUpn(data.value)}
                placeholder="user@contoso.com"
                type="email"
                disabled={saving}
              />
            </Field>

            <Field label="Role" required>
              <Select
                value={role}
                onChange={(_, data) => setRole(data.value as ContainerRole)}
                disabled={saving}
              >
                {ROLE_OPTIONS.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </Select>
            </Field>
          </DialogContent>
          <DialogActions>
            <Button
              appearance="primary"
              onClick={handleConfirm}
              disabled={saving}
              icon={saving ? <Spinner size="tiny" /> : undefined}
            >
              {saving ? "Adding…" : "Add Permission"}
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
// Edit Permission Dialog
// ─────────────────────────────────────────────────────────────────────────────

interface EditPermissionDialogProps {
  open: boolean;
  permission: ContainerPermission | null;
  saving: boolean;
  error: string | null;
  onEdit: (permId: string, role: ContainerRole) => Promise<void>;
  onDismiss: () => void;
}

const EditPermissionDialog: React.FC<EditPermissionDialogProps> = ({
  open,
  permission,
  saving,
  error,
  onEdit,
  onDismiss,
}) => {
  const styles = useStyles();

  const currentRole = permission?.roles[0] ?? "reader";
  const [role, setRole] = React.useState<ContainerRole>(currentRole as ContainerRole);

  // Sync role when dialog opens with a different permission
  React.useEffect(() => {
    if (open && permission) {
      setRole((permission.roles[0] ?? "reader") as ContainerRole);
    }
  }, [open, permission]);

  const principalName = permission ? getPrincipalDisplayName(permission) : "";
  const principalUpn = permission ? getPrincipalUpn(permission) : undefined;

  const handleConfirm = React.useCallback(async () => {
    if (!permission) return;
    await onEdit(permission.id, role);
  }, [permission, role, onEdit]);

  return (
    <Dialog open={open} onOpenChange={(_, data) => !data.open && onDismiss()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>Edit Permission</DialogTitle>
          <DialogContent className={styles.dialogContent}>
            <Text size={200}>
              Change the role assigned to{" "}
              <Text weight="semibold">{principalName}</Text>
              {principalUpn && principalUpn !== principalName && (
                <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                  {" "}({principalUpn})
                </Text>
              )}
              .
            </Text>

            {error && (
              <MessageBar intent="error">
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            )}

            <Field label="Role" required>
              <Select
                value={role}
                onChange={(_, data) => setRole(data.value as ContainerRole)}
                disabled={saving}
              >
                {ROLE_OPTIONS.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </Select>
            </Field>
          </DialogContent>
          <DialogActions>
            <Button
              appearance="primary"
              onClick={handleConfirm}
              disabled={saving}
              icon={saving ? <Spinner size="tiny" /> : undefined}
            >
              {saving ? "Saving…" : "Save Changes"}
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
// Remove Permission Dialog
// ─────────────────────────────────────────────────────────────────────────────

interface RemovePermissionDialogProps {
  open: boolean;
  permission: ContainerPermission | null;
  saving: boolean;
  error: string | null;
  onRemove: (permId: string) => Promise<void>;
  onDismiss: () => void;
}

const RemovePermissionDialog: React.FC<RemovePermissionDialogProps> = ({
  open,
  permission,
  saving,
  error,
  onRemove,
  onDismiss,
}) => {
  const styles = useStyles();

  const principalName = permission ? getPrincipalDisplayName(permission) : "";
  const principalUpn = permission ? getPrincipalUpn(permission) : undefined;

  const handleConfirm = React.useCallback(async () => {
    if (!permission) return;
    await onRemove(permission.id);
  }, [permission, onRemove]);

  return (
    <Dialog open={open} onOpenChange={(_, data) => !data.open && onDismiss()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>Remove Permission</DialogTitle>
          <DialogContent className={styles.dialogContent}>
            <Text size={300} className={styles.removeWarning} weight="semibold">
              This will revoke access immediately.
            </Text>
            <Text size={200}>
              Remove the permission assignment for{" "}
              <Text weight="semibold">{principalName}</Text>
              {principalUpn && principalUpn !== principalName && (
                <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                  {" "}({principalUpn})
                </Text>
              )}
              ? This action cannot be undone.
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
              style={{ backgroundColor: tokens.colorPaletteRedBackground3, color: tokens.colorNeutralForegroundOnBrand }}
              onClick={handleConfirm}
              disabled={saving}
              icon={saving ? <Spinner size="tiny" /> : <Delete20Regular />}
            >
              {saving ? "Removing…" : "Remove"}
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
// PermissionPanel (main component)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * PermissionPanel renders a permissions grid with functional Add/Edit/Remove
 * dialogs that call the container permission CRUD API endpoints.
 *
 * Designed to be embedded in ContainerDetail's Permissions tab or used standalone.
 */
export const PermissionPanel: React.FC<PermissionPanelProps> = ({
  containerId,
  configId,
}) => {
  const styles = useStyles();

  // ── Permission list state ─────────────────────────────────────────────────

  const [permissions, setPermissions] = React.useState<ContainerPermission[]>([]);
  const [loading, setLoading] = React.useState(false);
  const [loadError, setLoadError] = React.useState<string | null>(null);

  // ── Selection state (single row) ──────────────────────────────────────────

  const [selectedPermId, setSelectedPermId] = React.useState<string | null>(null);

  // ── Dialog state ──────────────────────────────────────────────────────────

  const [addOpen, setAddOpen] = React.useState(false);
  const [addSaving, setAddSaving] = React.useState(false);
  const [addError, setAddError] = React.useState<string | null>(null);

  const [editOpen, setEditOpen] = React.useState(false);
  const [editSaving, setEditSaving] = React.useState(false);
  const [editError, setEditError] = React.useState<string | null>(null);

  const [removeOpen, setRemoveOpen] = React.useState(false);
  const [removeSaving, setRemoveSaving] = React.useState(false);
  const [removeError, setRemoveError] = React.useState<string | null>(null);

  // ── Derived: selected permission object ───────────────────────────────────

  const selectedPermission = React.useMemo(
    () => permissions.find((p) => p.id === selectedPermId) ?? null,
    [permissions, selectedPermId]
  );

  // ── Load permissions ──────────────────────────────────────────────────────

  const loadPermissions = React.useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      const data = await speApiClient.permissions.list(containerId, configId);
      setPermissions(data);
      // Clear selection if the selected item is no longer in the list
      setSelectedPermId((prev) =>
        data.some((p) => p.id === prev) ? prev : null
      );
    } catch (err) {
      const message =
        err instanceof ApiError
          ? err.message
          : "Failed to load permissions.";
      setLoadError(message);
    } finally {
      setLoading(false);
    }
  }, [containerId, configId]);

  // Load on mount and when containerId/configId change
  React.useEffect(() => {
    void loadPermissions();
  }, [loadPermissions]);

  // ── Add Permission ────────────────────────────────────────────────────────

  const handleAdd = React.useCallback(
    async (userPrincipalName: string, role: ContainerRole) => {
      setAddSaving(true);
      setAddError(null);
      try {
        const newPerm = await speApiClient.permissions.add(
          containerId,
          configId,
          { userPrincipalName, role }
        );
        setPermissions((prev) => [...prev, newPerm]);
        setAddOpen(false);
      } catch (err) {
        const message =
          err instanceof ApiError
            ? err.message
            : "Failed to add permission.";
        setAddError(message);
      } finally {
        setAddSaving(false);
      }
    },
    [containerId, configId]
  );

  // ── Edit Permission ───────────────────────────────────────────────────────

  const handleEdit = React.useCallback(
    async (permId: string, role: ContainerRole) => {
      setEditSaving(true);
      setEditError(null);
      try {
        const updated = await speApiClient.permissions.update(
          containerId,
          permId,
          configId,
          { role }
        );
        setPermissions((prev) =>
          prev.map((p) => (p.id === permId ? updated : p))
        );
        setEditOpen(false);
      } catch (err) {
        const message =
          err instanceof ApiError
            ? err.message
            : "Failed to update permission.";
        setEditError(message);
      } finally {
        setEditSaving(false);
      }
    },
    [containerId, configId]
  );

  // ── Remove Permission ─────────────────────────────────────────────────────

  const handleRemove = React.useCallback(
    async (permId: string) => {
      setRemoveSaving(true);
      setRemoveError(null);
      try {
        await speApiClient.permissions.remove(containerId, permId, configId);
        setPermissions((prev) => prev.filter((p) => p.id !== permId));
        setSelectedPermId(null);
        setRemoveOpen(false);
      } catch (err) {
        const message =
          err instanceof ApiError
            ? err.message
            : "Failed to remove permission.";
        setRemoveError(message);
      } finally {
        setRemoveSaving(false);
      }
    },
    [containerId, configId]
  );

  // ── Toolbar actions ───────────────────────────────────────────────────────

  const handleOpenAdd = React.useCallback(() => {
    setAddError(null);
    setAddOpen(true);
  }, []);

  const handleOpenEdit = React.useCallback(() => {
    setEditError(null);
    setEditOpen(true);
  }, []);

  const handleOpenRemove = React.useCallback(() => {
    setRemoveError(null);
    setRemoveOpen(true);
  }, []);

  // ── Row click to select ───────────────────────────────────────────────────

  const handleRowClick = React.useCallback((permId: string) => {
    setSelectedPermId((prev) => (prev === permId ? null : permId));
  }, []);

  // ── Render ────────────────────────────────────────────────────────────────

  // Loading state
  if (loading && permissions.length === 0) {
    return (
      <div className={styles.feedback}>
        <Spinner size="small" label="Loading permissions…" />
      </div>
    );
  }

  // Error state (no data loaded at all)
  if (loadError && permissions.length === 0) {
    return (
      <div className={styles.feedback}>
        <MessageBar intent="error">
          <MessageBarBody>{loadError}</MessageBarBody>
        </MessageBar>
        <Button
          size="small"
          appearance="secondary"
          icon={<ArrowClockwise20Regular />}
          onClick={loadPermissions}
        >
          Retry
        </Button>
      </div>
    );
  }

  return (
    <div className={styles.root}>
      {/* Toolbar */}
      <Toolbar className={styles.toolbar} aria-label="Permission actions">
        <ToolbarButton
          appearance="primary"
          icon={<PersonAdd20Regular />}
          onClick={handleOpenAdd}
        >
          Add Permission
        </ToolbarButton>
        <ToolbarDivider />
        <ToolbarButton
          icon={<Edit20Regular />}
          disabled={!selectedPermId}
          onClick={handleOpenEdit}
        >
          Edit
        </ToolbarButton>
        <ToolbarButton
          icon={<Delete20Regular />}
          disabled={!selectedPermId}
          onClick={handleOpenRemove}
        >
          Remove
        </ToolbarButton>
        <ToolbarDivider />
        <ToolbarButton
          icon={<ArrowClockwise20Regular />}
          onClick={loadPermissions}
          disabled={loading}
          aria-label="Refresh permissions"
        />
      </Toolbar>

      {/* Stale-data error banner (data already visible) */}
      {loadError && permissions.length > 0 && (
        <MessageBar intent="warning">
          <MessageBarBody>
            Could not refresh: {loadError}
          </MessageBarBody>
        </MessageBar>
      )}

      {/* Empty state */}
      {permissions.length === 0 && !loading ? (
        <div className={styles.emptyState}>
          <Person20Regular style={{ fontSize: "32px", opacity: 0.4 }} />
          <Text size={300}>No permissions assigned</Text>
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            Use the Add Permission button to grant access.
          </Text>
        </div>
      ) : (
        <Table className={styles.table} size="small" aria-label="Container permissions">
          <TableHeader>
            <TableRow>
              {/* Selection indicator column */}
              <TableHeaderCell style={{ width: "12px" }} />
              <TableHeaderCell>Principal</TableHeaderCell>
              <TableHeaderCell>Role</TableHeaderCell>
            </TableRow>
          </TableHeader>
          <TableBody>
            {permissions.map((perm) => {
              const displayName = getPrincipalDisplayName(perm);
              const upn = getPrincipalUpn(perm);
              const role = (perm.roles[0] ?? "reader") as ContainerRole;
              const isSelected = perm.id === selectedPermId;

              return (
                <TableRow
                  key={perm.id}
                  onClick={() => handleRowClick(perm.id)}
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

                  {/* Principal column */}
                  <TableCell>
                    <TableCellLayout>
                      <Text size={200} weight={isSelected ? "semibold" : "regular"}>
                        {displayName}
                      </Text>
                      {upn && upn !== displayName && (
                        <Text className={styles.upn}>{upn}</Text>
                      )}
                    </TableCellLayout>
                  </TableCell>

                  {/* Role column */}
                  <TableCell>
                    <Badge
                      appearance="outline"
                      size="small"
                      color={roleBadgeColor(role)}
                    >
                      {role.charAt(0).toUpperCase() + role.slice(1)}
                    </Badge>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      )}

      {/* ── Dialogs ───────────────────────────────────────────────────────── */}

      <AddPermissionDialog
        open={addOpen}
        saving={addSaving}
        error={addError}
        onAdd={handleAdd}
        onDismiss={() => setAddOpen(false)}
      />

      <EditPermissionDialog
        open={editOpen}
        permission={selectedPermission}
        saving={editSaving}
        error={editError}
        onEdit={handleEdit}
        onDismiss={() => setEditOpen(false)}
      />

      <RemovePermissionDialog
        open={removeOpen}
        permission={selectedPermission}
        saving={removeSaving}
        error={removeError}
        onRemove={handleRemove}
        onDismiss={() => setRemoveOpen(false)}
      />
    </div>
  );
};
