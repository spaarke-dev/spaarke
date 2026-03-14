/**
 * ConsumingTenantsPanel — grid of consuming application registrations for a container type.
 *
 * Displays all consuming applications registered for the selected container type and
 * allows administrators to add, edit permissions for, and remove consuming app registrations.
 *
 * In multi-tenant SharePoint Embedded scenarios, a single container type (owned by one app)
 * can be consumed by multiple applications from different tenants. This panel provides
 * full CRUD management of those consuming app registrations.
 *
 * State machine:
 *   - idle:     Showing the grid of consumers (initial state after load)
 *   - loading:  Fetching consumers from the API
 *   - adding:   Dialog open for registering a new consuming app
 *   - editing:  Dialog open for editing an existing consuming app's permissions
 *   - confirm-remove: Confirmation dialog for removing a consuming app
 *   - error:    Error state with retry option
 *
 * ADR-021: All styles use Fluent UI v9 makeStyles + design tokens — no hard-coded colors.
 * ADR-012: Uses shared Fluent v9 components consistently with the rest of the admin app.
 * ADR-006: Code Page — React 18 patterns; no PCF / ComponentFramework deps.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
  Button,
  Badge,
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
  Label,
  Table,
  TableHeader,
  TableHeaderCell,
  TableBody,
  TableRow,
  TableCell,
  TableCellLayout,
  Checkbox,
  Tooltip,
  shorthands,
} from "@fluentui/react-components";
import {
  Add20Regular,
  Delete20Regular,
  Edit20Regular,
  ArrowClockwise20Regular,
  Warning20Regular,
  People20Regular,
} from "@fluentui/react-icons";
import { speApiClient, ApiError } from "../../services/speApiClient";
import type {
  ConsumingTenant,
  RegisterConsumingTenantRequest,
  UpdateConsumingTenantRequest,
} from "../../types/spe";

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface ConsumingTenantsPanelProps {
  /** ID of the container type to manage consuming tenants for. */
  containerTypeId: string;
  /** Config ID used to authenticate Graph API calls. */
  configId: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Known SPE permission levels
// ─────────────────────────────────────────────────────────────────────────────

const SPE_PERMISSION_LEVELS = [
  "none",
  "readContent",
  "writeContent",
  "manageContent",
  "managePermissions",
  "full",
] as const;

type SpePermissionLevel = (typeof SPE_PERMISSION_LEVELS)[number];

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021 — Fluent tokens only, no hard-coded colors)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalM),
    height: "100%",
  },

  toolbar: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    flexShrink: 0,
  },

  toolbarActions: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalS),
  },

  tableContainer: {
    flex: "1 1 auto",
    overflowY: "auto",
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
  },

  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.gap(tokens.spacingVerticalS),
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground3,
  },

  emptyStateIcon: {
    fontSize: "32px",
    color: tokens.colorNeutralForeground4,
  },

  actionCell: {
    display: "flex",
    ...shorthands.gap(tokens.spacingHorizontalXS),
    justifyContent: "flex-end",
  },

  permissionsBadges: {
    display: "flex",
    flexWrap: "wrap",
    ...shorthands.gap(tokens.spacingHorizontalXS),
  },

  dialogField: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalXS),
    marginBottom: tokens.spacingVerticalM,
  },

  permissionsGroup: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalXXS),
    marginBottom: tokens.spacingVerticalM,
  },

  permissionsGrid: {
    display: "grid",
    gridTemplateColumns: "1fr 1fr",
    ...shorthands.gap(tokens.spacingVerticalXS, tokens.spacingHorizontalM),
  },

  sectionLabel: {
    color: tokens.colorNeutralForeground2,
    fontWeight: tokens.fontWeightSemibold,
    marginBottom: tokens.spacingVerticalXS,
  },

  warningIcon: {
    color: tokens.colorPaletteRedForeground1,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/** Format a permission level string for human display. */
function formatPermission(p: string): string {
  switch (p) {
    case "none":
      return "None";
    case "readContent":
      return "Read";
    case "writeContent":
      return "Write";
    case "manageContent":
      return "Manage";
    case "managePermissions":
      return "Manage Permissions";
    case "full":
      return "Full Control";
    default:
      return p;
  }
}

/** Truncate a GUID-like app ID for display. */
function shortAppId(appId: string): string {
  if (appId.length <= 13) return appId;
  return appId.substring(0, 8) + "…" + appId.substring(appId.length - 4);
}

// ─────────────────────────────────────────────────────────────────────────────
// ConsumingTenantsPanel Component
// ─────────────────────────────────────────────────────────────────────────────

export const ConsumingTenantsPanel: React.FC<ConsumingTenantsPanelProps> = ({
  containerTypeId,
  configId,
}) => {
  const styles = useStyles();

  // ── Data state ──
  const [consumers, setConsumers] = React.useState<ConsumingTenant[]>([]);
  const [isLoading, setIsLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // ── Dialog state ──
  const [addDialogOpen, setAddDialogOpen] = React.useState(false);
  const [editDialogOpen, setEditDialogOpen] = React.useState(false);
  const [removeDialogOpen, setRemoveDialogOpen] = React.useState(false);
  const [selectedConsumer, setSelectedConsumer] = React.useState<ConsumingTenant | null>(null);
  const [isSaving, setIsSaving] = React.useState(false);
  const [dialogError, setDialogError] = React.useState<string | null>(null);

  // ── Add form state ──
  const [addAppId, setAddAppId] = React.useState("");
  const [addDisplayName, setAddDisplayName] = React.useState("");
  const [addTenantId, setAddTenantId] = React.useState("");
  const [addDelegated, setAddDelegated] = React.useState<Set<string>>(new Set());
  const [addApplication, setAddApplication] = React.useState<Set<string>>(new Set());

  // ── Edit form state ──
  const [editDelegated, setEditDelegated] = React.useState<Set<string>>(new Set());
  const [editApplication, setEditApplication] = React.useState<Set<string>>(new Set());

  // ── Load consumers ──
  const loadConsumers = React.useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const result = await speApiClient.containerTypes.listConsumers(containerTypeId, configId);
      setConsumers(result.items);
    } catch (err) {
      const message = err instanceof ApiError ? err.detail ?? err.message : String(err);
      setError(message || "Failed to load consuming tenant registrations.");
    } finally {
      setIsLoading(false);
    }
  }, [containerTypeId, configId]);

  React.useEffect(() => {
    void loadConsumers();
  }, [loadConsumers]);

  // ── Add consumer ──
  const handleAddOpen = () => {
    setAddAppId("");
    setAddDisplayName("");
    setAddTenantId("");
    setAddDelegated(new Set());
    setAddApplication(new Set());
    setDialogError(null);
    setAddDialogOpen(true);
  };

  const handleAddSave = async () => {
    if (!addAppId.trim()) {
      setDialogError("App ID is required.");
      return;
    }

    const request: RegisterConsumingTenantRequest = {
      appId: addAppId.trim(),
      displayName: addDisplayName.trim() || undefined,
      tenantId: addTenantId.trim() || undefined,
      delegatedPermissions: Array.from(addDelegated),
      applicationPermissions: Array.from(addApplication),
    };

    setIsSaving(true);
    setDialogError(null);
    try {
      await speApiClient.containerTypes.registerConsumer(containerTypeId, configId, request);
      setAddDialogOpen(false);
      await loadConsumers();
    } catch (err) {
      const message = err instanceof ApiError ? err.detail ?? err.message : String(err);
      setDialogError(message || "Failed to register consuming app.");
    } finally {
      setIsSaving(false);
    }
  };

  // ── Edit consumer ──
  const handleEditOpen = (consumer: ConsumingTenant) => {
    setSelectedConsumer(consumer);
    setEditDelegated(new Set(consumer.delegatedPermissions));
    setEditApplication(new Set(consumer.applicationPermissions));
    setDialogError(null);
    setEditDialogOpen(true);
  };

  const handleEditSave = async () => {
    if (!selectedConsumer) return;

    const request: UpdateConsumingTenantRequest = {
      delegatedPermissions: Array.from(editDelegated),
      applicationPermissions: Array.from(editApplication),
    };

    setIsSaving(true);
    setDialogError(null);
    try {
      await speApiClient.containerTypes.updateConsumer(
        containerTypeId,
        selectedConsumer.appId,
        configId,
        request,
      );
      setEditDialogOpen(false);
      await loadConsumers();
    } catch (err) {
      const message = err instanceof ApiError ? err.detail ?? err.message : String(err);
      setDialogError(message || "Failed to update permissions.");
    } finally {
      setIsSaving(false);
    }
  };

  // ── Remove consumer ──
  const handleRemoveOpen = (consumer: ConsumingTenant) => {
    setSelectedConsumer(consumer);
    setDialogError(null);
    setRemoveDialogOpen(true);
  };

  const handleRemoveConfirm = async () => {
    if (!selectedConsumer) return;

    setIsSaving(true);
    setDialogError(null);
    try {
      await speApiClient.containerTypes.removeConsumer(
        containerTypeId,
        selectedConsumer.appId,
        configId,
      );
      setRemoveDialogOpen(false);
      await loadConsumers();
    } catch (err) {
      const message = err instanceof ApiError ? err.detail ?? err.message : String(err);
      setDialogError(message || "Failed to remove consuming app.");
    } finally {
      setIsSaving(false);
    }
  };

  // ── Permission toggle helpers ──
  const togglePermission = (
    set: Set<string>,
    setter: React.Dispatch<React.SetStateAction<Set<string>>>,
    value: string,
    checked: boolean,
  ) => {
    const next = new Set(set);
    if (checked) {
      next.add(value);
    } else {
      next.delete(value);
    }
    setter(next);
  };

  // ─────────────────────────────────────────────────────────────────────────
  // Render
  // ─────────────────────────────────────────────────────────────────────────

  return (
    <div className={styles.root}>
      {/* Toolbar */}
      <div className={styles.toolbar}>
        <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
          {consumers.length > 0
            ? `${consumers.length} consuming app${consumers.length !== 1 ? "s" : ""} registered`
            : "No consuming apps registered"}
        </Text>
        <div className={styles.toolbarActions}>
          <Button
            appearance="subtle"
            icon={<ArrowClockwise20Regular />}
            onClick={() => void loadConsumers()}
            disabled={isLoading}
            title="Refresh"
          />
          <Button
            appearance="primary"
            icon={<Add20Regular />}
            onClick={handleAddOpen}
          >
            Add Consuming App
          </Button>
        </div>
      </div>

      {/* Error state */}
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {/* Loading state */}
      {isLoading && (
        <div style={{ display: "flex", justifyContent: "center", paddingTop: tokens.spacingVerticalXL }}>
          <Spinner size="small" label="Loading consuming apps..." />
        </div>
      )}

      {/* Grid */}
      {!isLoading && !error && (
        <div className={styles.tableContainer}>
          {consumers.length === 0 ? (
            <div className={styles.emptyState}>
              <People20Regular className={styles.emptyStateIcon} />
              <Text weight="semibold">No consuming apps registered</Text>
              <Text size={200}>
                Add a consuming application to grant it access to this container type from another
                tenant.
              </Text>
            </div>
          ) : (
            <Table size="small">
              <TableHeader>
                <TableRow>
                  <TableHeaderCell>App ID</TableHeaderCell>
                  <TableHeaderCell>Display Name</TableHeaderCell>
                  <TableHeaderCell>Delegated Permissions</TableHeaderCell>
                  <TableHeaderCell>App Permissions</TableHeaderCell>
                  <TableHeaderCell style={{ width: "90px" }} />
                </TableRow>
              </TableHeader>
              <TableBody>
                {consumers.map((consumer) => (
                  <TableRow key={consumer.appId}>
                    <TableCell>
                      <TableCellLayout>
                        <Tooltip content={consumer.appId} relationship="label">
                          <Text size={200} style={{ fontFamily: "monospace" }}>
                            {shortAppId(consumer.appId)}
                          </Text>
                        </Tooltip>
                      </TableCellLayout>
                    </TableCell>
                    <TableCell>
                      <Text size={200}>{consumer.displayName ?? "—"}</Text>
                    </TableCell>
                    <TableCell>
                      <div className={styles.permissionsBadges}>
                        {consumer.delegatedPermissions.length > 0 ? (
                          consumer.delegatedPermissions.map((p) => (
                            <Badge key={p} appearance="tint" color="informative" size="small">
                              {formatPermission(p)}
                            </Badge>
                          ))
                        ) : (
                          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                            None
                          </Text>
                        )}
                      </div>
                    </TableCell>
                    <TableCell>
                      <div className={styles.permissionsBadges}>
                        {consumer.applicationPermissions.length > 0 ? (
                          consumer.applicationPermissions.map((p) => (
                            <Badge key={p} appearance="tint" color="warning" size="small">
                              {formatPermission(p)}
                            </Badge>
                          ))
                        ) : (
                          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                            None
                          </Text>
                        )}
                      </div>
                    </TableCell>
                    <TableCell>
                      <div className={styles.actionCell}>
                        <Tooltip content="Edit permissions" relationship="label">
                          <Button
                            appearance="subtle"
                            icon={<Edit20Regular />}
                            size="small"
                            onClick={() => handleEditOpen(consumer)}
                          />
                        </Tooltip>
                        <Tooltip content="Remove" relationship="label">
                          <Button
                            appearance="subtle"
                            icon={<Delete20Regular />}
                            size="small"
                            onClick={() => handleRemoveOpen(consumer)}
                          />
                        </Tooltip>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </div>
      )}

      {/* ── Add Consuming App Dialog ── */}
      <Dialog open={addDialogOpen} onOpenChange={(_, d) => setAddDialogOpen(d.open)}>
        <DialogSurface>
          <DialogTitle>Register Consuming Application</DialogTitle>
          <DialogBody>
            <DialogContent>
              <Text size={200} style={{ color: tokens.colorNeutralForeground2, display: "block", marginBottom: tokens.spacingVerticalM }}>
                Grant a consuming application access to this container type. The app can be from
                the same tenant or a different tenant (multi-tenant scenario).
              </Text>

              {dialogError && (
                <MessageBar intent="error" style={{ marginBottom: tokens.spacingVerticalM }}>
                  <MessageBarBody>{dialogError}</MessageBarBody>
                </MessageBar>
              )}

              <div className={styles.dialogField}>
                <Field label="App ID (Client ID)" required>
                  <Input
                    value={addAppId}
                    onChange={(_, d) => setAddAppId(d.value)}
                    placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                    disabled={isSaving}
                  />
                </Field>
              </div>

              <div className={styles.dialogField}>
                <Field label="Display Name (optional)">
                  <Input
                    value={addDisplayName}
                    onChange={(_, d) => setAddDisplayName(d.value)}
                    placeholder="My Consuming App"
                    disabled={isSaving}
                  />
                </Field>
              </div>

              <div className={styles.dialogField}>
                <Field label="Tenant ID (optional)">
                  <Input
                    value={addTenantId}
                    onChange={(_, d) => setAddTenantId(d.value)}
                    placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                    disabled={isSaving}
                  />
                </Field>
              </div>

              <div className={styles.permissionsGroup}>
                <Label className={styles.sectionLabel}>Delegated Permissions</Label>
                <div className={styles.permissionsGrid}>
                  {SPE_PERMISSION_LEVELS.map((p) => (
                    <Checkbox
                      key={p}
                      label={formatPermission(p)}
                      checked={addDelegated.has(p)}
                      onChange={(_, d) =>
                        togglePermission(addDelegated, setAddDelegated, p, d.checked === true)
                      }
                      disabled={isSaving}
                    />
                  ))}
                </div>
              </div>

              <div className={styles.permissionsGroup}>
                <Label className={styles.sectionLabel}>Application Permissions</Label>
                <div className={styles.permissionsGrid}>
                  {SPE_PERMISSION_LEVELS.map((p) => (
                    <Checkbox
                      key={p}
                      label={formatPermission(p)}
                      checked={addApplication.has(p)}
                      onChange={(_, d) =>
                        togglePermission(addApplication, setAddApplication, p, d.checked === true)
                      }
                      disabled={isSaving}
                    />
                  ))}
                </div>
              </div>
            </DialogContent>
            <DialogActions>
              <Button
                appearance="secondary"
                onClick={() => setAddDialogOpen(false)}
                disabled={isSaving}
              >
                Cancel
              </Button>
              <Button
                appearance="primary"
                onClick={() => void handleAddSave()}
                disabled={isSaving || !addAppId.trim()}
              >
                {isSaving ? "Registering…" : "Register"}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      {/* ── Edit Permissions Dialog ── */}
      <Dialog open={editDialogOpen} onOpenChange={(_, d) => setEditDialogOpen(d.open)}>
        <DialogSurface>
          <DialogTitle>Edit Permissions</DialogTitle>
          <DialogBody>
            <DialogContent>
              {selectedConsumer && (
                <Text size={200} style={{ color: tokens.colorNeutralForeground2, display: "block", marginBottom: tokens.spacingVerticalM }}>
                  Update permissions for{" "}
                  <strong>{selectedConsumer.displayName ?? selectedConsumer.appId}</strong>.
                </Text>
              )}

              {dialogError && (
                <MessageBar intent="error" style={{ marginBottom: tokens.spacingVerticalM }}>
                  <MessageBarBody>{dialogError}</MessageBarBody>
                </MessageBar>
              )}

              <div className={styles.permissionsGroup}>
                <Label className={styles.sectionLabel}>Delegated Permissions</Label>
                <div className={styles.permissionsGrid}>
                  {SPE_PERMISSION_LEVELS.map((p) => (
                    <Checkbox
                      key={p}
                      label={formatPermission(p)}
                      checked={editDelegated.has(p)}
                      onChange={(_, d) =>
                        togglePermission(editDelegated, setEditDelegated, p, d.checked === true)
                      }
                      disabled={isSaving}
                    />
                  ))}
                </div>
              </div>

              <div className={styles.permissionsGroup}>
                <Label className={styles.sectionLabel}>Application Permissions</Label>
                <div className={styles.permissionsGrid}>
                  {SPE_PERMISSION_LEVELS.map((p) => (
                    <Checkbox
                      key={p}
                      label={formatPermission(p)}
                      checked={editApplication.has(p)}
                      onChange={(_, d) =>
                        togglePermission(editApplication, setEditApplication, p, d.checked === true)
                      }
                      disabled={isSaving}
                    />
                  ))}
                </div>
              </div>
            </DialogContent>
            <DialogActions>
              <Button
                appearance="secondary"
                onClick={() => setEditDialogOpen(false)}
                disabled={isSaving}
              >
                Cancel
              </Button>
              <Button
                appearance="primary"
                onClick={() => void handleEditSave()}
                disabled={isSaving}
              >
                {isSaving ? "Saving…" : "Save"}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      {/* ── Remove Confirmation Dialog ── */}
      <Dialog open={removeDialogOpen} onOpenChange={(_, d) => setRemoveDialogOpen(d.open)}>
        <DialogSurface>
          <DialogTitle>
            <Warning20Regular className={styles.warningIcon} /> Remove Consuming App
          </DialogTitle>
          <DialogBody>
            <DialogContent>
              {selectedConsumer && (
                <Text>
                  Remove{" "}
                  <strong>{selectedConsumer.displayName ?? selectedConsumer.appId}</strong> from
                  this container type? This will revoke all permissions granted to the consuming
                  application.
                </Text>
              )}

              {dialogError && (
                <MessageBar intent="error" style={{ marginTop: tokens.spacingVerticalM }}>
                  <MessageBarBody>{dialogError}</MessageBarBody>
                </MessageBar>
              )}
            </DialogContent>
            <DialogActions>
              <Button
                appearance="secondary"
                onClick={() => setRemoveDialogOpen(false)}
                disabled={isSaving}
              >
                Cancel
              </Button>
              <Button
                appearance="primary"
                onClick={() => void handleRemoveConfirm()}
                disabled={isSaving}
                style={{ backgroundColor: tokens.colorPaletteRedBackground3, color: tokens.colorNeutralForegroundOnBrand }}
              >
                {isSaving ? "Removing…" : "Remove"}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
};

export default ConsumingTenantsPanel;
