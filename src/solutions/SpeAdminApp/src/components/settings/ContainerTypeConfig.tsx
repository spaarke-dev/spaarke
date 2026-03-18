/**
 * ContainerTypeConfig — SPE container type configuration management.
 *
 * Displays all sprk_specontainertypeconfig records in a data grid and
 * provides an add/edit drawer with the full field set:
 *   - Name, Status
 *   - Business Unit lookup (LookupField)
 *   - Environment lookup (LookupField)
 *   - Container Type ID + display name
 *   - Billing classification
 *   - Owning app ID + display name
 *   - Key Vault secret name (text — NEVER shows or accepts actual secrets)
 *   - Optional consuming app ID + Key Vault secret name
 *   - Delegated permissions checkboxes
 *   - Application permissions checkboxes
 *   - Sharing capability, versioning settings
 *   - Notes
 *
 * ADR-021: All styles use Fluent UI v9 makeStyles + design tokens (no hard-coded colours).
 * ADR-012: LookupField reused from @spaarke/ui-components.
 * ADR-006: Code Page — React 18 patterns, no PCF / ComponentFramework dependencies.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
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
  Checkbox,
  Switch,
  Textarea,
  Divider,
  Subtitle2,
  Label,
} from "@fluentui/react-components";
import {
  Add20Regular,
  Edit20Regular,
  Delete20Regular,
  ArrowClockwise20Regular,
  LockClosed20Regular,
} from "@fluentui/react-icons";
import { LookupField } from "@spaarke/ui-components";
import type { ILookupItem } from "@spaarke/ui-components";
import { speApiClient, ApiError } from "../../services/speApiClient";
import type {
  SpeContainerTypeConfig,
  SpeContainerTypeConfigUpsert,
  BusinessUnit,
  SpeEnvironment,
  BillingClassification,
  SharingCapability,
} from "../../types/spe";

// ─────────────────────────────────────────────────────────────────────────────
// Constants: permission option sets
// ─────────────────────────────────────────────────────────────────────────────

/** Delegated permission options for SPE container types (Microsoft Graph). */
const DELEGATED_PERMISSION_OPTIONS: string[] = [
  "FileStorageContainer.Selected",
  "Sites.Read.All",
  "Sites.ReadWrite.All",
  "Files.Read.All",
  "Files.ReadWrite.All",
  "User.Read",
  "offline_access",
];

/** Application permission options for SPE container types (Microsoft Graph). */
const APPLICATION_PERMISSION_OPTIONS: string[] = [
  "FileStorageContainer.Selected",
  "Sites.Read.All",
  "Sites.ReadWrite.All",
  "Files.Read.All",
  "Files.ReadWrite.All",
];

const BILLING_OPTIONS: { value: BillingClassification; label: string }[] = [
  { value: "trial", label: "Trial" },
  { value: "standard", label: "Standard" },
  { value: "directToCustomer", label: "Direct to Customer" },
];

const SHARING_CAPABILITY_OPTIONS: { value: SharingCapability; label: string }[] = [
  { value: "disabled", label: "Disabled" },
  { value: "externalUserSharingOnly", label: "External User Sharing Only" },
  { value: "existingExternalUserSharingOnly", label: "Existing External User Sharing Only" },
  { value: "externalUserAndGuestSharing", label: "External User and Guest Sharing" },
];

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/** Form state for the add/edit dialog. Matches SpeContainerTypeConfigUpsert fields. */
interface ConfigFormState {
  name: string;
  businessUnitId: string;
  businessUnitName: string;
  environmentId: string;
  environmentName: string;
  containerTypeId: string;
  containerTypeName: string;
  billingClassification: BillingClassification;
  owningAppId: string;
  owningAppDisplayName: string;
  /** Key Vault secret name — NEVER the actual secret value */
  keyVaultSecretName: string;
  consumingAppId: string;
  /** Key Vault secret name for the consuming app — NEVER the actual secret value */
  consumingAppKeyVaultSecret: string;
  delegatedPermissions: string[];
  applicationPermissions: string[];
  maxStoragePerBytes: number;
  sharingCapability: SharingCapability;
  isItemVersioningEnabled: boolean;
  itemMajorVersionLimit: number;
  status: "active" | "inactive";
  notes: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    height: "100%",
    overflow: "hidden",
  },

  toolbar: {
    paddingBottom: tokens.spacingVerticalS,
    flexShrink: 0,
  },

  gridWrapper: {
    flex: "1 1 auto",
    overflow: "auto",
    minHeight: 0,
  },

  // Dialog form layout
  dialogSurface: {
    width: "720px",
    maxWidth: "90vw",
  },

  formGrid: {
    display: "grid",
    gridTemplateColumns: "1fr 1fr",
    gap: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
  },

  formFullWidth: {
    gridColumn: "1 / -1",
  },

  sectionDivider: {
    gridColumn: "1 / -1",
    marginTop: tokens.spacingVerticalXS,
    marginBottom: tokens.spacingVerticalXS,
  },

  sectionHeading: {
    gridColumn: "1 / -1",
    color: tokens.colorNeutralForeground2,
    marginBottom: tokens.spacingVerticalXXS,
  },

  permissionsGroup: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
  },

  permissionCheckboxRow: {
    display: "flex",
    flexWrap: "wrap",
    gap: `${tokens.spacingVerticalXXS} ${tokens.spacingHorizontalL}`,
  },

  secretNameHelper: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    marginTop: tokens.spacingVerticalXXS,
    display: "block",
  },

  /** Apply to a wrapping div to give an inner Input monospace font */
  monoField: {
    "& input": {
      fontFamily: "monospace",
    },
  },

  // Status badge colour helpers
  badgeActive: {
    backgroundColor: tokens.colorPaletteGreenBackground1,
    color: tokens.colorPaletteGreenForeground1,
  },

  // Validation error
  fieldError: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
    marginTop: "2px",
  },

  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalXXXL,
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Utility helpers
// ─────────────────────────────────────────────────────────────────────────────

/** Parse a comma-separated permissions string into an array. */
function parsePermissions(raw: string): string[] {
  return raw
    .split(",")
    .map(s => s.trim())
    .filter(Boolean);
}

/** Join a permissions array into a comma-separated string. */
function joinPermissions(perms: string[]): string {
  return perms.join(",");
}

/** Build a default empty ConfigFormState. */
function emptyFormState(): ConfigFormState {
  return {
    name: "",
    businessUnitId: "",
    businessUnitName: "",
    environmentId: "",
    environmentName: "",
    containerTypeId: "",
    containerTypeName: "",
    billingClassification: "standard",
    owningAppId: "",
    owningAppDisplayName: "",
    keyVaultSecretName: "",
    consumingAppId: "",
    consumingAppKeyVaultSecret: "",
    delegatedPermissions: [],
    applicationPermissions: [],
    maxStoragePerBytes: 0,
    sharingCapability: "disabled",
    isItemVersioningEnabled: false,
    itemMajorVersionLimit: 500,
    status: "active",
    notes: "",
  };
}

/** Map a SpeContainerTypeConfig record into ConfigFormState. */
function configToFormState(config: SpeContainerTypeConfig): ConfigFormState {
  return {
    name: config.name ?? "",
    businessUnitId: config.businessUnitId ?? "",
    businessUnitName: config.businessUnitName ?? "",
    environmentId: config.environmentId ?? "",
    environmentName: config.environmentName ?? "",
    containerTypeId: config.containerTypeId ?? "",
    containerTypeName: config.containerTypeName ?? "",
    billingClassification: (config.billingClassification as BillingClassification) ?? "standard",
    owningAppId: config.owningAppId ?? "",
    owningAppDisplayName: config.owningAppDisplayName ?? "",
    keyVaultSecretName: config.keyVaultSecretName ?? "",
    consumingAppId: config.consumingAppId ?? "",
    consumingAppKeyVaultSecret: config.consumingAppKeyVaultSecret ?? "",
    delegatedPermissions: parsePermissions(config.delegatedPermissions ?? ""),
    applicationPermissions: parsePermissions(config.applicationPermissions ?? ""),
    maxStoragePerBytes: config.maxStoragePerBytes ?? 0,
    sharingCapability: (config.sharingCapability as SharingCapability) ?? "disabled",
    isItemVersioningEnabled: config.isItemVersioningEnabled ?? false,
    itemMajorVersionLimit: config.itemMajorVersionLimit ?? 500,
    status: config.status ?? "active",
    notes: config.notes ?? "",
  };
}

/** Map ConfigFormState to an SpeContainerTypeConfigUpsert payload. */
function formStateToUpsert(form: ConfigFormState): SpeContainerTypeConfigUpsert {
  return {
    name: form.name.trim(),
    businessUnitId: form.businessUnitId,
    environmentId: form.environmentId,
    containerTypeId: form.containerTypeId.trim(),
    containerTypeName: form.containerTypeName.trim(),
    billingClassification: form.billingClassification,
    owningAppId: form.owningAppId.trim(),
    owningAppDisplayName: form.owningAppDisplayName.trim(),
    keyVaultSecretName: form.keyVaultSecretName.trim(),
    consumingAppId: form.consumingAppId.trim() || undefined,
    consumingAppKeyVaultSecret: form.consumingAppKeyVaultSecret.trim() || undefined,
    delegatedPermissions: joinPermissions(form.delegatedPermissions),
    applicationPermissions: joinPermissions(form.applicationPermissions),
    maxStoragePerBytes: form.maxStoragePerBytes,
    sharingCapability: form.sharingCapability,
    isItemVersioningEnabled: form.isItemVersioningEnabled,
    itemMajorVersionLimit: form.itemMajorVersionLimit,
    status: form.status,
    notes: form.notes.trim() || undefined,
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Sub-component: PermissionCheckboxGroup
// ─────────────────────────────────────────────────────────────────────────────

interface PermissionCheckboxGroupProps {
  label: string;
  options: string[];
  selected: string[];
  onChange: (updated: string[]) => void;
}

const PermissionCheckboxGroup: React.FC<PermissionCheckboxGroupProps> = ({
  label,
  options,
  selected,
  onChange,
}) => {
  const styles = useStyles();

  const handleChange = React.useCallback(
    (option: string, checked: boolean) => {
      if (checked) {
        onChange([...selected, option]);
      } else {
        onChange(selected.filter(p => p !== option));
      }
    },
    [selected, onChange],
  );

  return (
    <div className={styles.permissionsGroup}>
      <Label>{label}</Label>
      <div className={styles.permissionCheckboxRow}>
        {options.map(option => (
          <Checkbox
            key={option}
            label={option}
            checked={selected.includes(option)}
            onChange={(_e, data) => handleChange(option, !!data.checked)}
          />
        ))}
      </div>
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Sub-component: ConfigFormDialog
// ─────────────────────────────────────────────────────────────────────────────

interface ConfigFormDialogProps {
  open: boolean;
  editingConfig: SpeContainerTypeConfig | null;
  onClose: () => void;
  onSaved: (saved: SpeContainerTypeConfig) => void;
}

const ConfigFormDialog: React.FC<ConfigFormDialogProps> = ({
  open,
  editingConfig,
  onClose,
  onSaved,
}) => {
  const styles = useStyles();
  const isEdit = editingConfig !== null;

  // Form state
  const [form, setForm] = React.useState<ConfigFormState>(emptyFormState);
  const [errors, setErrors] = React.useState<Partial<Record<keyof ConfigFormState, string>>>({});
  const [saving, setSaving] = React.useState(false);
  const [saveError, setSaveError] = React.useState<string | null>(null);

  // Lookup helper state — pre-loaded when dialog opens
  const [businessUnits, setBusinessUnits] = React.useState<BusinessUnit[]>([]);
  const [environments, setEnvironments] = React.useState<SpeEnvironment[]>([]);
  const [lookupsLoading, setLookupsLoading] = React.useState(false);

  // BU and environment as ILookupItem (for LookupField controlled value)
  const [selectedBu, setSelectedBu] = React.useState<ILookupItem | null>(null);
  const [selectedEnv, setSelectedEnv] = React.useState<ILookupItem | null>(null);

  // ── Initialise form when dialog opens ──────────────────────────────────
  React.useEffect(() => {
    if (!open) return;

    const init = editingConfig ? configToFormState(editingConfig) : emptyFormState();
    setForm(init);
    setErrors({});
    setSaveError(null);

    // Pre-seed the LookupField controlled values from the loaded record
    if (editingConfig) {
      setSelectedBu({ id: editingConfig.businessUnitId, name: editingConfig.businessUnitName });
      setSelectedEnv({ id: editingConfig.environmentId, name: editingConfig.environmentName });
    } else {
      setSelectedBu(null);
      setSelectedEnv(null);
    }

    // Load BU and environment lists for lookup support
    setLookupsLoading(true);
    Promise.all([speApiClient.businessUnits.list(), speApiClient.environments.list()])
      .then(([bus, envs]) => {
        setBusinessUnits(bus);
        setEnvironments(envs);
      })
      .catch(err => {
        console.error("[ContainerTypeConfig] Failed to load lookup data:", err);
      })
      .finally(() => setLookupsLoading(false));
  }, [open, editingConfig]);

  // ── LookupField search callbacks ───────────────────────────────────────

  const searchBusinessUnits = React.useCallback(
    async (query: string): Promise<ILookupItem[]> => {
      const lq = query.toLowerCase();
      const source = businessUnits.length ? businessUnits : await speApiClient.businessUnits.list();
      return source
        .filter(bu => bu.name.toLowerCase().includes(lq))
        .map(bu => ({ id: bu.businessUnitId, name: bu.name }));
    },
    [businessUnits],
  );

  const searchEnvironments = React.useCallback(
    async (query: string): Promise<ILookupItem[]> => {
      const lq = query.toLowerCase();
      const source = environments.length ? environments : await speApiClient.environments.list();
      return source
        .filter(env => env.name.toLowerCase().includes(lq))
        .map(env => ({ id: env.id, name: env.name }));
    },
    [environments],
  );

  // ── BU / environment selection handlers ────────────────────────────────

  const handleBuChange = React.useCallback((item: ILookupItem | null) => {
    setSelectedBu(item);
    setForm(prev => ({
      ...prev,
      businessUnitId: item?.id ?? "",
      businessUnitName: item?.name ?? "",
    }));
  }, []);

  const handleEnvChange = React.useCallback((item: ILookupItem | null) => {
    setSelectedEnv(item);
    setForm(prev => ({
      ...prev,
      environmentId: item?.id ?? "",
      environmentName: item?.name ?? "",
    }));
  }, []);

  // ── Generic field updater ──────────────────────────────────────────────

  function setField<K extends keyof ConfigFormState>(key: K, value: ConfigFormState[K]) {
    setForm(prev => ({ ...prev, [key]: value }));
    setErrors(prev => ({ ...prev, [key]: undefined }));
  }

  // ── Validation ─────────────────────────────────────────────────────────

  function validate(): boolean {
    const newErrors: Partial<Record<keyof ConfigFormState, string>> = {};

    if (!form.name.trim()) newErrors.name = "Name is required.";
    if (!form.businessUnitId) newErrors.businessUnitId = "Business Unit is required.";
    if (!form.environmentId) newErrors.environmentId = "Environment is required.";
    if (!form.containerTypeId.trim()) newErrors.containerTypeId = "Container Type ID is required.";
    if (!form.owningAppId.trim()) newErrors.owningAppId = "Owning App ID is required.";
    if (!form.keyVaultSecretName.trim())
      newErrors.keyVaultSecretName = "Key Vault secret name is required.";

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  }

  // ── Save handler ───────────────────────────────────────────────────────

  const handleSave = React.useCallback(async () => {
    if (!validate()) return;

    setSaving(true);
    setSaveError(null);

    try {
      const payload = formStateToUpsert(form);
      const saved = isEdit
        ? await speApiClient.configs.update(editingConfig!.id, payload)
        : await speApiClient.configs.create(payload);
      onSaved(saved);
    } catch (err) {
      const message =
        err instanceof ApiError
          ? err.message
          : err instanceof Error
            ? err.message
            : "An unexpected error occurred.";
      setSaveError(message);
    } finally {
      setSaving(false);
    }
  }, [form, isEdit, editingConfig, onSaved]);

  // ─────────────────────────────────────────────────────────────────────────
  // Render
  // ─────────────────────────────────────────────────────────────────────────

  return (
    <Dialog open={open} onOpenChange={(_e, data) => { if (!data.open) onClose(); }}>
      <DialogSurface className={styles.dialogSurface}>
        <DialogTitle>{isEdit ? "Edit Container Type Config" : "Add Container Type Config"}</DialogTitle>
        <DialogBody>
          <DialogContent>
            {saveError && (
              <MessageBar intent="error" style={{ marginBottom: tokens.spacingVerticalM }}>
                <MessageBarBody>
                  <MessageBarTitle>Save failed</MessageBarTitle>
                  {saveError}
                </MessageBarBody>
              </MessageBar>
            )}

            {lookupsLoading && (
              <Spinner size="tiny" label="Loading lookup data..." style={{ marginBottom: tokens.spacingVerticalM }} />
            )}

            <div className={styles.formGrid}>
              {/* ── Name ── */}
              <Field
                label="Name"
                required
                validationMessage={errors.name}
                validationState={errors.name ? "error" : "none"}
              >
                <Input
                  value={form.name}
                  onChange={(_e, d) => setField("name", d.value)}
                  placeholder="e.g. Acme Corp – Production"
                />
              </Field>

              {/* ── Status ── */}
              <Field label="Status">
                <Select
                  value={form.status}
                  onChange={(_e, d) => setField("status", d.value as "active" | "inactive")}
                >
                  <option value="active">Active</option>
                  <option value="inactive">Inactive</option>
                </Select>
              </Field>

              {/* ── BU Lookup ── */}
              <div className={styles.formFullWidth}>
                <LookupField
                  label="Business Unit"
                  required
                  value={selectedBu}
                  onChange={handleBuChange}
                  onSearch={searchBusinessUnits}
                  placeholder="Search business units..."
                />
                {errors.businessUnitId && (
                  <span className={styles.fieldError}>{errors.businessUnitId}</span>
                )}
              </div>

              {/* ── Environment Lookup ── */}
              <div className={styles.formFullWidth}>
                <LookupField
                  label="Environment"
                  required
                  value={selectedEnv}
                  onChange={handleEnvChange}
                  onSearch={searchEnvironments}
                  placeholder="Search environments..."
                />
                {errors.environmentId && (
                  <span className={styles.fieldError}>{errors.environmentId}</span>
                )}
              </div>

              {/* ── Section: Container Type ── */}
              <div className={styles.sectionDivider}>
                <Divider />
              </div>
              <Subtitle2 className={styles.sectionHeading}>Container Type</Subtitle2>

              {/* ── Container Type ID ── */}
              <div className={styles.monoField}>
                <Field
                  label="Container Type ID"
                  required
                  validationMessage={errors.containerTypeId}
                  validationState={errors.containerTypeId ? "error" : "none"}
                >
                  <Input
                    value={form.containerTypeId}
                    onChange={(_e, d) => setField("containerTypeId", d.value)}
                    placeholder="GUID from Azure Portal"
                  />
                </Field>
              </div>

              {/* ── Container Type Name ── */}
              <Field label="Container Type Display Name">
                <Input
                  value={form.containerTypeName}
                  onChange={(_e, d) => setField("containerTypeName", d.value)}
                  placeholder="e.g. Legal Documents"
                />
              </Field>

              {/* ── Billing Classification ── */}
              <Field label="Billing Classification">
                <Select
                  value={form.billingClassification}
                  onChange={(_e, d) =>
                    setField("billingClassification", d.value as BillingClassification)
                  }
                >
                  {BILLING_OPTIONS.map(opt => (
                    <option key={opt.value} value={opt.value}>
                      {opt.label}
                    </option>
                  ))}
                </Select>
              </Field>

              {/* ── Section: App Registration ── */}
              <div className={styles.sectionDivider}>
                <Divider />
              </div>
              <Subtitle2 className={styles.sectionHeading}>Owning App Registration</Subtitle2>

              {/* ── Owning App ID ── */}
              <div className={styles.monoField}>
                <Field
                  label="Owning App Client ID"
                  required
                  validationMessage={errors.owningAppId}
                  validationState={errors.owningAppId ? "error" : "none"}
                >
                  <Input
                    value={form.owningAppId}
                    onChange={(_e, d) => setField("owningAppId", d.value)}
                    placeholder="Azure App Registration Client ID"
                  />
                </Field>
              </div>

              {/* ── Owning App Display Name ── */}
              <Field label="Owning App Display Name">
                <Input
                  value={form.owningAppDisplayName}
                  onChange={(_e, d) => setField("owningAppDisplayName", d.value)}
                  placeholder="e.g. Spaarke SPE Owning App"
                />
              </Field>

              {/* ── Key Vault Secret Name ── */}
              <div className={styles.formFullWidth}>
                <Field
                  label="Key Vault Secret Name"
                  required
                  hint="Enter only the Key Vault secret name — never paste or enter the actual secret value here."
                  validationMessage={errors.keyVaultSecretName}
                  validationState={errors.keyVaultSecretName ? "error" : "none"}
                >
                  <Input
                    value={form.keyVaultSecretName}
                    onChange={(_e, d) => setField("keyVaultSecretName", d.value)}
                    placeholder="e.g. spe-owning-app-secret"
                    aria-describedby="kv-secret-hint"
                  />
                </Field>
                <span id="kv-secret-hint" className={styles.secretNameHelper}>
                  The system retrieves the actual credential from Azure Key Vault at runtime using this name.
                  Do not enter the credential value itself.
                </span>
              </div>

              {/* ── Section: Consuming App (optional) ── */}
              <div className={styles.sectionDivider}>
                <Divider />
              </div>
              <Subtitle2 className={styles.sectionHeading}>Consuming App Registration (optional)</Subtitle2>

              {/* ── Consuming App ID ── */}
              <div className={styles.monoField}>
                <Field label="Consuming App Client ID">
                  <Input
                    value={form.consumingAppId}
                    onChange={(_e, d) => setField("consumingAppId", d.value)}
                    placeholder="Azure App Registration Client ID (optional)"
                  />
                </Field>
              </div>

              {/* ── Consuming App KV Secret ── */}
              <div>
                <Field
                  label="Consuming App Key Vault Secret Name"
                  hint="Enter only the Key Vault secret name — never paste the actual secret."
                >
                  <Input
                    value={form.consumingAppKeyVaultSecret}
                    onChange={(_e, d) => setField("consumingAppKeyVaultSecret", d.value)}
                    placeholder="e.g. spe-consuming-app-secret"
                  />
                </Field>
                <span className={styles.secretNameHelper}>
                  Credential retrieved from Azure Key Vault at runtime using this name.
                </span>
              </div>

              {/* ── Section: Permissions ── */}
              <div className={styles.sectionDivider}>
                <Divider />
              </div>
              <Subtitle2 className={styles.sectionHeading}>Permissions</Subtitle2>

              {/* ── Delegated Permissions ── */}
              <div className={styles.formFullWidth}>
                <PermissionCheckboxGroup
                  label="Delegated Permissions"
                  options={DELEGATED_PERMISSION_OPTIONS}
                  selected={form.delegatedPermissions}
                  onChange={perms => setField("delegatedPermissions", perms)}
                />
              </div>

              {/* ── Application Permissions ── */}
              <div className={styles.formFullWidth}>
                <PermissionCheckboxGroup
                  label="Application Permissions"
                  options={APPLICATION_PERMISSION_OPTIONS}
                  selected={form.applicationPermissions}
                  onChange={perms => setField("applicationPermissions", perms)}
                />
              </div>

              {/* ── Section: Storage & Sharing Settings ── */}
              <div className={styles.sectionDivider}>
                <Divider />
              </div>
              <Subtitle2 className={styles.sectionHeading}>Storage &amp; Sharing</Subtitle2>

              {/* ── Max Storage ── */}
              <Field label="Max Storage Per Container (bytes)">
                <Input
                  type="number"
                  value={String(form.maxStoragePerBytes)}
                  onChange={(_e, d) =>
                    setField("maxStoragePerBytes", parseInt(d.value || "0", 10))
                  }
                  placeholder="0 = unlimited"
                />
              </Field>

              {/* ── Sharing Capability ── */}
              <Field label="Sharing Capability">
                <Select
                  value={form.sharingCapability}
                  onChange={(_e, d) =>
                    setField("sharingCapability", d.value as SharingCapability)
                  }
                >
                  {SHARING_CAPABILITY_OPTIONS.map(opt => (
                    <option key={opt.value} value={opt.value}>
                      {opt.label}
                    </option>
                  ))}
                </Select>
              </Field>

              {/* ── Versioning ── */}
              <Field label="Item Versioning">
                <Switch
                  checked={form.isItemVersioningEnabled}
                  onChange={(_e, d) => setField("isItemVersioningEnabled", d.checked)}
                  label={form.isItemVersioningEnabled ? "Enabled" : "Disabled"}
                />
              </Field>

              {form.isItemVersioningEnabled && (
                <Field label="Major Version Limit">
                  <Input
                    type="number"
                    value={String(form.itemMajorVersionLimit)}
                    onChange={(_e, d) =>
                      setField("itemMajorVersionLimit", parseInt(d.value || "500", 10))
                    }
                    placeholder="500"
                  />
                </Field>
              )}

              {/* ── Notes ── */}
              <div className={styles.formFullWidth}>
                <Field label="Notes">
                  <Textarea
                    value={form.notes}
                    onChange={(_e, d) => setField("notes", d.value)}
                    placeholder="Optional admin notes about this config"
                    rows={3}
                  />
                </Field>
              </div>
            </div>
          </DialogContent>

          <DialogActions>
            <Button appearance="secondary" onClick={onClose} disabled={saving}>
              Cancel
            </Button>
            <Button appearance="primary" onClick={handleSave} disabled={saving}>
              {saving ? <Spinner size="tiny" /> : isEdit ? "Save Changes" : "Add Config"}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Sub-component: ConfigDataGrid
// ─────────────────────────────────────────────────────────────────────────────

interface ConfigDataGridProps {
  configs: SpeContainerTypeConfig[];
  selectedIds: Set<TableRowId>;
  onSelectionChange: (e: React.MouseEvent | React.KeyboardEvent, data: OnSelectionChangeData) => void;
}

const ConfigDataGrid: React.FC<ConfigDataGridProps> = ({
  configs,
  selectedIds,
  onSelectionChange,
}) => {
  const columns: TableColumnDefinition<SpeContainerTypeConfig>[] = [
    createTableColumn<SpeContainerTypeConfig>({
      columnId: "name",
      compare: (a, b) => a.name.localeCompare(b.name),
      renderHeaderCell: () => "Name",
      renderCell: item => (
        <Text size={200} weight="semibold">
          {item.name}
        </Text>
      ),
    }),
    createTableColumn<SpeContainerTypeConfig>({
      columnId: "businessUnit",
      compare: (a, b) => (a.businessUnitName ?? "").localeCompare(b.businessUnitName ?? ""),
      renderHeaderCell: () => "Business Unit",
      renderCell: item => <Text size={200}>{item.businessUnitName}</Text>,
    }),
    createTableColumn<SpeContainerTypeConfig>({
      columnId: "environment",
      compare: (a, b) => (a.environmentName ?? "").localeCompare(b.environmentName ?? ""),
      renderHeaderCell: () => "Environment",
      renderCell: item => <Text size={200}>{item.environmentName}</Text>,
    }),
    createTableColumn<SpeContainerTypeConfig>({
      columnId: "containerType",
      compare: (a, b) => (a.containerTypeName ?? "").localeCompare(b.containerTypeName ?? ""),
      renderHeaderCell: () => "Container Type",
      renderCell: item => <Text size={200}>{item.containerTypeName || item.containerTypeId}</Text>,
    }),
    createTableColumn<SpeContainerTypeConfig>({
      columnId: "billing",
      renderHeaderCell: () => "Billing",
      renderCell: item => (
        <Text size={200} style={{ textTransform: "capitalize" }}>
          {item.billingClassification}
        </Text>
      ),
    }),
    createTableColumn<SpeContainerTypeConfig>({
      columnId: "registered",
      renderHeaderCell: () => "Registered",
      renderCell: item => (
        <Badge
          appearance="tint"
          color={item.isRegistered ? "success" : "warning"}
          size="small"
        >
          {item.isRegistered ? "Yes" : "No"}
        </Badge>
      ),
    }),
    createTableColumn<SpeContainerTypeConfig>({
      columnId: "status",
      compare: (a, b) => a.status.localeCompare(b.status),
      renderHeaderCell: () => "Status",
      renderCell: item => (
        <Badge
          appearance="tint"
          color={item.status === "active" ? "success" : "warning"}
          size="small"
        >
          {item.status === "active" ? "Active" : "Inactive"}
        </Badge>
      ),
    }),
  ];

  return (
    <DataGrid
      items={configs}
      columns={columns}
      getRowId={(item) => item.id}
      sortable
      selectionMode="multiselect"
      selectedItems={selectedIds}
      onSelectionChange={onSelectionChange}
      focusMode="composite"
      aria-label="Container type configurations"
    >
      <DataGridHeader>
        <DataGridRow
          selectionCell={{
            checkboxIndicator: { "aria-label": "Select all rows" },
          }}
        >
          {({ renderHeaderCell }) => (
            <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
          )}
        </DataGridRow>
      </DataGridHeader>
      <DataGridBody<SpeContainerTypeConfig>>
        {({ item, rowId }) => (
          <DataGridRow<SpeContainerTypeConfig>
            key={rowId}
            selectionCell={{
              checkboxIndicator: { "aria-label": `Select ${item.name}` },
            }}
          >
            {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
          </DataGridRow>
        )}
      </DataGridBody>
    </DataGrid>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Main Component: ContainerTypeConfig
// ─────────────────────────────────────────────────────────────────────────────

/**
 * ContainerTypeConfig — manages container type configuration records (Business
 * Unit to Container Type mapping, authentication parameters). Renders within
 * the Settings page Container Type Configs tab.
 *
 * Key security constraint: the component displays and accepts only Key Vault
 * secret *names*, never actual secret values. The BFF retrieves credentials
 * from Azure Key Vault at runtime using the stored name.
 */
export const ContainerTypeConfig: React.FC = () => {
  const styles = useStyles();

  // ── State ───────────────────────────────────────────────────────────────
  const [configs, setConfigs] = React.useState<SpeContainerTypeConfig[]>([]);
  const [loading, setLoading] = React.useState(true);
  const [loadError, setLoadError] = React.useState<string | null>(null);

  const [selectedIds, setSelectedIds] = React.useState<Set<TableRowId>>(new Set());
  const [dialogOpen, setDialogOpen] = React.useState(false);
  const [editingConfig, setEditingConfig] = React.useState<SpeContainerTypeConfig | null>(null);

  const [deleteError, setDeleteError] = React.useState<string | null>(null);
  const [deleting, setDeleting] = React.useState(false);
  const [editLoading, setEditLoading] = React.useState(false);

  // ── Derived values ──────────────────────────────────────────────────────

  /** True when exactly one config is selected (enables Edit button). */
  const singleSelected = selectedIds.size === 1;
  /** True when at least one config is selected (enables Delete button). */
  const anySelected = selectedIds.size > 0;

  // ── Load configs ────────────────────────────────────────────────────────

  const loadConfigs = React.useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      const data = await speApiClient.configs.list();
      setConfigs(data);
    } catch (err) {
      const message =
        err instanceof ApiError
          ? err.message
          : err instanceof Error
            ? err.message
            : "Failed to load container type configs.";
      setLoadError(message);
    } finally {
      setLoading(false);
    }
  }, []);

  React.useEffect(() => {
    void loadConfigs();
  }, [loadConfigs]);

  // ── Selection ───────────────────────────────────────────────────────────

  const handleSelectionChange = React.useCallback(
    (_e: React.MouseEvent | React.KeyboardEvent, data: OnSelectionChangeData) => {
      setSelectedIds(new Set(data.selectedItems));
    },
    [],
  );

  // ── Open add dialog ─────────────────────────────────────────────────────

  const handleAdd = React.useCallback(() => {
    setEditingConfig(null);
    setDialogOpen(true);
  }, []);

  // ── Open edit dialog ────────────────────────────────────────────────────

  const handleEdit = React.useCallback(async () => {
    if (!singleSelected) return;
    const [selectedId] = selectedIds;
    setEditLoading(true);
    try {
      const config = await speApiClient.configs.get(String(selectedId));
      setEditingConfig(config);
      setDialogOpen(true);
    } catch (err) {
      const message =
        err instanceof ApiError ? err.message :
        err instanceof Error ? err.message :
        "Failed to load config detail.";
      setDeleteError(message);
    } finally {
      setEditLoading(false);
    }
  }, [singleSelected, selectedIds]);

  // ── Close dialog ─────────────────────────────────────────────────────────

  const handleDialogClose = React.useCallback(() => {
    setDialogOpen(false);
    setEditingConfig(null);
  }, []);

  // ── Save callback ────────────────────────────────────────────────────────

  const handleSaved = React.useCallback(
    (saved: SpeContainerTypeConfig) => {
      setConfigs(prev => {
        const index = prev.findIndex(c => c.id === saved.id);
        if (index >= 0) {
          const updated = [...prev];
          updated[index] = saved;
          return updated;
        }
        return [...prev, saved];
      });
      setSelectedIds(new Set());
      setDialogOpen(false);
      setEditingConfig(null);
    },
    [],
  );

  // ── Delete selected ──────────────────────────────────────────────────────

  const handleDelete = React.useCallback(async () => {
    if (!anySelected) return;

    const names = [...selectedIds]
      .map(id => configs.find(c => c.id === id)?.name ?? String(id))
      .join(", ");

    if (!window.confirm(`Delete ${selectedIds.size === 1 ? `"${names}"` : `${selectedIds.size} configs`}? This action cannot be undone.`)) {
      return;
    }

    setDeleting(true);
    setDeleteError(null);

    const results = await Promise.allSettled(
      [...selectedIds].map(id => speApiClient.configs.delete(String(id))),
    );

    const failures = results
      .map((r, i) => ({ result: r, id: [...selectedIds][i] }))
      .filter(({ result }) => result.status === "rejected");

    if (failures.length > 0) {
      setDeleteError(`${failures.length} of ${selectedIds.size} deletes failed. Reload to see current state.`);
    }

    // Remove successfully deleted ids from state
    const failedIds = new Set(failures.map(({ id }) => id));
    setConfigs(prev => prev.filter(c => !selectedIds.has(c.id) || failedIds.has(c.id)));
    setSelectedIds(new Set(failedIds));
    setDeleting(false);
  }, [anySelected, selectedIds, configs]);

  // ─────────────────────────────────────────────────────────────────────────
  // Render
  // ─────────────────────────────────────────────────────────────────────────

  return (
    <div className={styles.root}>
      {/* ── Toolbar ── */}
      <Toolbar className={styles.toolbar} aria-label="Container type config actions">
        <Tooltip content="Add a new container type config" relationship="label">
          <ToolbarButton
            icon={<Add20Regular />}
            onClick={handleAdd}
            appearance="primary"
          >
            Add Config
          </ToolbarButton>
        </Tooltip>

        <Tooltip
          content={singleSelected ? "Edit selected config" : "Select one config to edit"}
          relationship="label"
        >
          <ToolbarButton
            icon={editLoading ? <Spinner size="tiny" /> : <Edit20Regular />}
            onClick={handleEdit}
            disabled={!singleSelected || editLoading}
          >
            Edit
          </ToolbarButton>
        </Tooltip>

        <ToolbarDivider />

        <Tooltip
          content={anySelected ? `Delete ${selectedIds.size} selected config(s)` : "Select config(s) to delete"}
          relationship="label"
        >
          <ToolbarButton
            icon={<Delete20Regular />}
            onClick={handleDelete}
            disabled={!anySelected || deleting}
            appearance="subtle"
          >
            {deleting ? <Spinner size="tiny" /> : "Delete"}
          </ToolbarButton>
        </Tooltip>

        <ToolbarDivider />

        <Tooltip content="Refresh config list" relationship="label">
          <ToolbarButton
            icon={<ArrowClockwise20Regular />}
            onClick={loadConfigs}
            disabled={loading}
          >
            Refresh
          </ToolbarButton>
        </Tooltip>

        {selectedIds.size > 0 && (
          <Text size={200} style={{ marginLeft: tokens.spacingHorizontalS, color: tokens.colorNeutralForeground2 }}>
            {selectedIds.size} selected
          </Text>
        )}
      </Toolbar>

      {/* ── Delete error ── */}
      {deleteError && (
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Delete failed</MessageBarTitle>
            {deleteError}
          </MessageBarBody>
          <MessageBarActions>
            <Button size="small" onClick={() => setDeleteError(null)}>
              Dismiss
            </Button>
          </MessageBarActions>
        </MessageBar>
      )}

      {/* ── Load error ── */}
      {loadError && (
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Load failed</MessageBarTitle>
            {loadError}
          </MessageBarBody>
          <MessageBarActions>
            <Button size="small" onClick={loadConfigs}>
              Retry
            </Button>
          </MessageBarActions>
        </MessageBar>
      )}

      {/* ── Loading ── */}
      {loading && (
        <Spinner label="Loading container type configs..." size="medium" />
      )}

      {/* ── Empty state ── */}
      {!loading && !loadError && configs.length === 0 && (
        <div className={styles.emptyState}>
          <LockClosed20Regular fontSize={40} color={tokens.colorNeutralForeground4} />
          <Text size={400} weight="semibold">
            No container type configs
          </Text>
          <Text size={300} style={{ color: tokens.colorNeutralForeground3, textAlign: "center" }}>
            Add a config to link a Business Unit to an SPE container type with its Azure app registration and credentials.
          </Text>
          <Button appearance="primary" icon={<Add20Regular />} onClick={handleAdd}>
            Add Config
          </Button>
        </div>
      )}

      {/* ── Data grid ── */}
      {!loading && configs.length > 0 && (
        <div className={styles.gridWrapper}>
          <ConfigDataGrid
            configs={configs}
            selectedIds={selectedIds}
            onSelectionChange={handleSelectionChange}
          />
        </div>
      )}

      {/* ── Add / Edit dialog ── */}
      <ConfigFormDialog
        open={dialogOpen}
        editingConfig={editingConfig}
        onClose={handleDialogClose}
        onSaved={handleSaved}
      />
    </div>
  );
};
