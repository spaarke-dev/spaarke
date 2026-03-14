/**
 * ContainerTypeDetail — slide-in detail panel for a selected SPE container type.
 *
 * Opens from the right when an administrator clicks a container type row in
 * ContainerTypesPage. Provides two tabs:
 *
 *   Settings — editable form for sharing capability, versioning, storage, search.
 *   Permissions — read-only list of registered app permissions (from SPE-054).
 *
 * Save:
 *   Calls PUT /api/spe/containertypes/{typeId}/settings via speApiClient.
 *   Shows a success toast on save and resets the dirty flag.
 *
 * Unsaved changes:
 *   Tracks dirty state; shows a confirmation dialog if the user tries to
 *   close with unsaved changes.
 *
 * Layout:
 *   Uses SidePaneShell (ADR-012) for the fixed-header / scrollable-body layout.
 *   Fixed overlay on the right side — matches ContainerDetail.tsx pattern.
 *
 * ADR-021: All styles use Fluent UI v9 makeStyles + design tokens.
 * ADR-012: SidePaneShell reused from @spaarke/ui-components.
 * ADR-006: Code Page — React 18 patterns; no PCF / ComponentFramework deps.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
  Badge,
  TabList,
  Tab,
  type SelectTabData,
  type SelectTabEvent,
  Button,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Divider,
  shorthands,
  Table,
  TableHeader,
  TableHeaderCell,
  TableBody,
  TableRow,
  TableCell,
} from "@fluentui/react-components";
import {
  Dismiss20Regular,
  ArrowClockwise20Regular,
  Settings20Regular,
  LockClosed20Regular,
  Save20Regular,
  Warning20Regular,
  People20Regular,
} from "@fluentui/react-icons";
import { SidePaneShell } from "@spaarke/ui-components";
import { useBuContext } from "../../contexts/BuContext";
import { speApiClient, ApiError } from "../../services/speApiClient";
import {
  ContainerTypeSettingsForm,
  type ContainerTypeSettings,
  type SharingCapabilityValue,
} from "./ContainerTypeSettingsForm";
import { ConsumingTenantsPanel } from "./ConsumingTenantsPanel";
import type { ContainerType, ContainerTypePermission } from "../../types/spe";

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface ContainerTypeDetailProps {
  /** ID of the container type to display, or null when no type is selected. */
  containerTypeId: string | null;
  /** Callback to close the panel. */
  onClose: () => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Tab identifiers
// ─────────────────────────────────────────────────────────────────────────────

type TabId = "settings" | "permissions" | "consumers";

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/** Format an ISO date string to a localised short date. */
function formatDate(iso: string | undefined): string {
  if (!iso) return "—";
  try {
    return new Intl.DateTimeFormat(undefined, {
      year: "numeric",
      month: "short",
      day: "numeric",
    }).format(new Date(iso));
  } catch {
    return iso;
  }
}

/** Map billing classification to a Fluent Badge color. */
function billingBadgeColor(
  classification: string
): "success" | "warning" | "informative" {
  switch (classification) {
    case "standard":
      return "success";
    case "trial":
      return "warning";
    default:
      return "informative";
  }
}

/** Capitalize the first letter of a string for display. */
function capitalize(s: string): string {
  if (!s) return s;
  return s.charAt(0).toUpperCase() + s.slice(1);
}

/**
 * Extract ContainerTypeSettings from a SpeContainerTypeConfig (selectedConfig).
 * The config holds the Dataverse-side settings for the container type.
 */
function extractSettingsFromConfig(
  config: NonNullable<ReturnType<typeof useBuContext>["selectedConfig"]>
): ContainerTypeSettings {
  return {
    sharingCapability: (config.sharingCapability as SharingCapabilityValue) ?? "disabled",
    isItemVersioningEnabled: config.isItemVersioningEnabled ?? false,
    itemMajorVersionLimit: config.itemMajorVersionLimit ?? 100,
    maxStoragePerBytes: config.maxStoragePerBytes ?? 1_073_741_824,
    isSearchEnabled: true, // Graph API search is enabled by default; no Dataverse field yet
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021 — Fluent tokens only)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  /** Translucent backdrop covering the page behind the panel. */
  backdrop: {
    position: "fixed",
    inset: 0,
    zIndex: 200,
    backgroundColor: tokens.colorBackgroundOverlay,
  },

  /** Panel container — fixed right-side overlay, 440px wide. */
  panel: {
    position: "fixed",
    top: 0,
    right: 0,
    bottom: 0,
    width: "440px",
    zIndex: 201,
    boxShadow: tokens.shadow64,
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
  },

  /** Header rendered inside SidePaneShell's header slot. */
  header: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
    backgroundColor: tokens.colorNeutralBackground1,
  },

  headerTitle: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalXXS),
    minWidth: 0,
    flex: "1 1 auto",
    marginRight: tokens.spacingHorizontalS,
  },

  headerName: {
    color: tokens.colorNeutralForeground1,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },

  headerMeta: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalS),
    flexWrap: "wrap",
  },

  headerSubtext: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },

  /** Tab list sits between the header and the scrollable content. */
  tabList: {
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
    backgroundColor: tokens.colorNeutralBackground1,
  },

  /** Scrollable content of the current tab. */
  tabContent: {
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalM),
  },

  /** Footer with Save / Discard buttons. */
  footer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "flex-end",
    ...shorthands.gap(tokens.spacingHorizontalS),
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
    flexShrink: 0,
  },

  /** Dirty indicator dot shown next to the Save button when there are unsaved changes. */
  dirtyIndicator: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalXS),
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    flex: "1 1 auto",
  },

  /** Empty footer when on the Permissions tab (no save). */
  emptyFooter: {
    height: 0,
  },

  /** Loading / error / empty feedback area. */
  feedback: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.gap(tokens.spacingVerticalM),
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    color: tokens.colorNeutralForeground2,
    textAlign: "center",
  },

  /** Individual field display row (label + value pair). */
  fieldRow: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalXXS),
  },

  fieldLabel: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },

  fieldValue: {
    color: tokens.colorNeutralForeground1,
  },

  /** Two-column info grid for the details section at the top of Settings tab. */
  infoGrid: {
    display: "grid",
    gridTemplateColumns: "1fr 1fr",
    ...shorthands.gap(tokens.spacingVerticalM, tokens.spacingHorizontalM),
  },

  /** Permissions table. */
  permTable: {
    width: "100%",
  },

  noPermissions: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.gap(tokens.spacingVerticalS),
    paddingTop: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground2,
    textAlign: "center",
  },

  successBanner: {
    marginBottom: tokens.spacingVerticalS,
  },

  errorBanner: {
    marginBottom: tokens.spacingVerticalS,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// ContainerTypeDetail
// ─────────────────────────────────────────────────────────────────────────────

/**
 * ContainerTypeDetail — detail panel for a selected container type.
 *
 * Renders as a fixed overlay on the right side of the viewport, with a
 * semi-transparent backdrop. Only visible when containerTypeId is non-null.
 */
export const ContainerTypeDetail: React.FC<ContainerTypeDetailProps> = ({
  containerTypeId,
  onClose,
}) => {
  const styles = useStyles();
  const { selectedConfig } = useBuContext();

  // ── Data State ─────────────────────────────────────────────────────────────

  const [containerType, setContainerType] = React.useState<ContainerType | null>(null);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // ── Permissions State ──────────────────────────────────────────────────────

  const [permissions, setPermissions] = React.useState<ContainerTypePermission[]>([]);
  const [permissionsLoading, setPermissionsLoading] = React.useState(false);
  const [permissionsError, setPermissionsError] = React.useState<string | null>(null);
  const [permissionsLoaded, setPermissionsLoaded] = React.useState(false);

  // ── Settings / Dirty State ─────────────────────────────────────────────────

  const [settings, setSettings] = React.useState<ContainerTypeSettings>({
    sharingCapability: "disabled",
    isItemVersioningEnabled: false,
    itemMajorVersionLimit: 100,
    maxStoragePerBytes: 1_073_741_824,
    isSearchEnabled: true,
  });
  const [savedSettings, setSavedSettings] = React.useState<ContainerTypeSettings>(settings);
  const [saving, setSaving] = React.useState(false);
  const [saveSuccess, setSaveSuccess] = React.useState(false);
  const [saveError, setSaveError] = React.useState<string | null>(null);

  // isDirty: true if settings diverge from what was last loaded/saved
  const isDirty = React.useMemo(
    () => JSON.stringify(settings) !== JSON.stringify(savedSettings),
    [settings, savedSettings]
  );

  // ── Tab State ──────────────────────────────────────────────────────────────

  const [activeTab, setActiveTab] = React.useState<TabId>("settings");

  // ── Close Confirmation State ───────────────────────────────────────────────

  const [confirmCloseOpen, setConfirmCloseOpen] = React.useState(false);

  // ── Load Container Type ────────────────────────────────────────────────────

  React.useEffect(() => {
    if (!containerTypeId || !selectedConfig) {
      setContainerType(null);
      setError(null);
      return;
    }

    setLoading(true);
    setError(null);
    setSaveSuccess(false);
    setSaveError(null);
    setPermissionsLoaded(false);
    setPermissions([]);
    setActiveTab("settings");

    speApiClient.containerTypes
      .get(containerTypeId, selectedConfig.id)
      .then((ct) => {
        setContainerType(ct);
        // Initialise settings from selectedConfig (which holds the Dataverse settings)
        const initial = extractSettingsFromConfig(selectedConfig);
        setSettings(initial);
        setSavedSettings(initial);
      })
      .catch((err) => {
        const message =
          err instanceof ApiError
            ? err.message
            : "Failed to load container type details. Please try again.";
        setError(message);
      })
      .finally(() => {
        setLoading(false);
      });
  }, [containerTypeId, selectedConfig]);

  // ── Load Permissions (lazy, on first tab switch to Permissions) ─────────────

  const loadPermissions = React.useCallback(async () => {
    if (!containerTypeId || !selectedConfig || permissionsLoaded) return;
    setPermissionsLoading(true);
    setPermissionsError(null);
    try {
      const perms = await speApiClient.containerTypes.listPermissions(
        containerTypeId,
        selectedConfig.id
      );
      setPermissions(perms);
      setPermissionsLoaded(true);
    } catch (err) {
      const message =
        err instanceof ApiError
          ? err.message
          : "Failed to load permissions. Please try again.";
      setPermissionsError(message);
    } finally {
      setPermissionsLoading(false);
    }
  }, [containerTypeId, selectedConfig, permissionsLoaded]);

  // ── Tab Selection ──────────────────────────────────────────────────────────

  const handleTabSelect = React.useCallback(
    (_e: SelectTabEvent, data: SelectTabData) => {
      const tab = data.value as TabId;
      setActiveTab(tab);
      if (tab === "permissions") {
        void loadPermissions();
      }
    },
    [loadPermissions]
  );

  // ── Save Handler ───────────────────────────────────────────────────────────

  const handleSave = React.useCallback(async () => {
    if (!containerTypeId || !selectedConfig) return;
    setSaving(true);
    setSaveError(null);
    setSaveSuccess(false);
    try {
      await speApiClient.containerTypes.updateSettings(
        containerTypeId,
        selectedConfig.id,
        {
          sharingCapability: settings.sharingCapability,
          isItemVersioningEnabled: settings.isItemVersioningEnabled,
          itemMajorVersionLimit: settings.itemMajorVersionLimit,
          maxStoragePerBytes: settings.maxStoragePerBytes,
          isSearchEnabled: settings.isSearchEnabled,
        }
      );
      setSavedSettings({ ...settings });
      setSaveSuccess(true);
    } catch (err) {
      const message =
        err instanceof ApiError
          ? err.message
          : "Failed to save settings. Please try again.";
      setSaveError(message);
    } finally {
      setSaving(false);
    }
  }, [containerTypeId, selectedConfig, settings]);

  // ── Close Handler (with dirty check) ──────────────────────────────────────

  const handleCloseRequest = React.useCallback(() => {
    if (isDirty) {
      setConfirmCloseOpen(true);
    } else {
      onClose();
    }
  }, [isDirty, onClose]);

  const handleConfirmClose = React.useCallback(() => {
    setConfirmCloseOpen(false);
    onClose();
  }, [onClose]);

  // ── Keyboard: Escape to close ──────────────────────────────────────────────

  React.useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape" && containerTypeId) {
        handleCloseRequest();
      }
    };
    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [containerTypeId, handleCloseRequest]);

  // ── Not open ───────────────────────────────────────────────────────────────

  if (!containerTypeId) return null;

  // ── Sub-components ─────────────────────────────────────────────────────────

  const headerNode = (
    <div className={styles.header}>
      <div className={styles.headerTitle}>
        {loading ? (
          <Spinner size="tiny" label="Loading…" />
        ) : (
          <>
            <Text
              as="h2"
              weight="semibold"
              size={400}
              className={styles.headerName}
              title={containerType?.displayName ?? "Container Type"}
            >
              {containerType?.displayName ?? "Container Type"}
            </Text>
            <div className={styles.headerMeta}>
              {containerType?.billingClassification && (
                <Badge
                  color={billingBadgeColor(containerType.billingClassification)}
                  appearance="filled"
                  size="small"
                >
                  {capitalize(containerType.billingClassification)}
                </Badge>
              )}
              {containerType?.containerTypeId && (
                <Text
                  className={styles.headerSubtext}
                  title={`Container Type ID: ${containerType.containerTypeId}`}
                >
                  {containerType.containerTypeId}
                </Text>
              )}
            </div>
          </>
        )}
      </div>
      <Button
        appearance="subtle"
        icon={<Dismiss20Regular />}
        onClick={handleCloseRequest}
        aria-label="Close container type panel"
        size="small"
      />
    </div>
  );

  const tabListNode = (
    <div className={styles.tabList}>
      <TabList
        selectedValue={activeTab}
        onTabSelect={handleTabSelect}
        size="small"
      >
        <Tab value="settings" icon={<Settings20Regular />}>
          Settings
        </Tab>
        <Tab value="permissions" icon={<LockClosed20Regular />}>
          Permissions
        </Tab>
        <Tab value="consumers" icon={<People20Regular />}>
          Consuming Apps
        </Tab>
      </TabList>
    </div>
  );

  const footerNode =
    activeTab === "settings" ? (
      <div className={styles.footer}>
        <div className={styles.dirtyIndicator}>
          {isDirty && (
            <>
              <Warning20Regular style={{ color: tokens.colorPaletteYellowForeground1 }} />
              <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                Unsaved changes
              </Text>
            </>
          )}
        </div>
        <Button
          appearance="secondary"
          onClick={() => {
            setSettings({ ...savedSettings });
            setSaveSuccess(false);
            setSaveError(null);
          }}
          disabled={!isDirty || saving}
        >
          Discard
        </Button>
        <Button
          appearance="primary"
          icon={saving ? <Spinner size="tiny" /> : <Save20Regular />}
          onClick={() => { void handleSave(); }}
          disabled={!isDirty || saving}
        >
          {saving ? "Saving…" : "Save"}
        </Button>
      </div>
    ) : (
      <div className={styles.emptyFooter} />
    );

  // ── Render ──────────────────────────────────────────────────────────────────

  return (
    <>
      {/* Backdrop */}
      <div
        className={styles.backdrop}
        onClick={handleCloseRequest}
        aria-hidden="true"
      />

      {/* Panel */}
      <div className={styles.panel} role="complementary" aria-label="Container type detail">
        <SidePaneShell
          header={
            <>
              {headerNode}
              {tabListNode}
            </>
          }
          footer={footerNode}
        >
          {/* ── Settings Tab ── */}
          {activeTab === "settings" && (
            <div className={styles.tabContent}>
              {/* Save success / error banners */}
              {saveSuccess && (
                <MessageBar intent="success" className={styles.successBanner}>
                  <MessageBarBody>Settings saved successfully.</MessageBarBody>
                </MessageBar>
              )}
              {saveError && (
                <MessageBar intent="error" className={styles.errorBanner}>
                  <MessageBarBody>
                    <MessageBarTitle>Save failed</MessageBarTitle>
                    {saveError}
                  </MessageBarBody>
                </MessageBar>
              )}

              {loading ? (
                <div className={styles.feedback}>
                  <Spinner size="medium" label="Loading details…" />
                </div>
              ) : error ? (
                <div className={styles.feedback}>
                  <MessageBar intent="error">
                    <MessageBarBody>
                      <MessageBarTitle>Failed to load container type</MessageBarTitle>
                      {error}
                    </MessageBarBody>
                  </MessageBar>
                  <Button
                    appearance="secondary"
                    icon={<ArrowClockwise20Regular />}
                    onClick={() => {
                      if (containerTypeId && selectedConfig) {
                        setLoading(true);
                        setError(null);
                        speApiClient.containerTypes
                          .get(containerTypeId, selectedConfig.id)
                          .then((ct) => {
                            setContainerType(ct);
                            const initial = extractSettingsFromConfig(selectedConfig);
                            setSettings(initial);
                            setSavedSettings(initial);
                          })
                          .catch((err) => {
                            setError(
                              err instanceof ApiError ? err.message : "Failed to load."
                            );
                          })
                          .finally(() => setLoading(false));
                      }
                    }}
                  >
                    Retry
                  </Button>
                </div>
              ) : (
                <>
                  {/* Container type read-only info */}
                  {containerType && (
                    <>
                      <div className={styles.infoGrid}>
                        <div className={styles.fieldRow}>
                          <Text className={styles.fieldLabel}>Created</Text>
                          <Text className={styles.fieldValue}>
                            {formatDate(containerType.createdDateTime)}
                          </Text>
                        </div>
                        <div className={styles.fieldRow}>
                          <Text className={styles.fieldLabel}>Owning App ID</Text>
                          <Text
                            className={styles.fieldValue}
                            style={{
                              fontSize: tokens.fontSizeBase200,
                              color: tokens.colorNeutralForeground3,
                              wordBreak: "break-all",
                            }}
                          >
                            {containerType.owningAppId ?? "—"}
                          </Text>
                        </div>
                        {containerType.expiryDateTime && (
                          <div className={styles.fieldRow}>
                            <Text className={styles.fieldLabel}>Trial Expiry</Text>
                            <Text
                              className={styles.fieldValue}
                              style={{ color: tokens.colorPaletteRedForeground1 }}
                            >
                              {formatDate(containerType.expiryDateTime)}
                            </Text>
                          </div>
                        )}
                        <div className={styles.fieldRow}>
                          <Text className={styles.fieldLabel}>Registered</Text>
                          <Text className={styles.fieldValue}>
                            {containerType.isRegistered === true ? "Yes" : "No"}
                          </Text>
                        </div>
                      </div>
                      <Divider />
                    </>
                  )}

                  {/* Editable settings form */}
                  <ContainerTypeSettingsForm
                    settings={settings}
                    onChange={setSettings}
                    disabled={saving}
                  />
                </>
              )}
            </div>
          )}

          {/* ── Consuming Tenants Tab ── */}
          {activeTab === "consumers" && selectedConfig && containerTypeId && (
            <div className={styles.tabContent}>
              <ConsumingTenantsPanel
                containerTypeId={containerTypeId}
                configId={selectedConfig.id}
              />
            </div>
          )}

          {/* ── Permissions Tab ── */}
          {activeTab === "permissions" && (
            <div className={styles.tabContent}>
              {permissionsLoading ? (
                <div className={styles.feedback}>
                  <Spinner size="medium" label="Loading permissions…" />
                </div>
              ) : permissionsError ? (
                <div className={styles.feedback}>
                  <MessageBar intent="error">
                    <MessageBarBody>
                      <MessageBarTitle>Failed to load permissions</MessageBarTitle>
                      {permissionsError}
                    </MessageBarBody>
                  </MessageBar>
                  <Button
                    appearance="secondary"
                    icon={<ArrowClockwise20Regular />}
                    onClick={() => {
                      setPermissionsLoaded(false);
                      void loadPermissions();
                    }}
                  >
                    Retry
                  </Button>
                </div>
              ) : permissions.length === 0 ? (
                <div className={styles.noPermissions}>
                  <LockClosed20Regular style={{ fontSize: "48px", opacity: 0.3 }} />
                  <Text size={400} weight="semibold">
                    No permissions registered
                  </Text>
                  <Text size={300}>
                    Use the Register action to register this container type and
                    grant permissions to your application.
                  </Text>
                </div>
              ) : (
                <>
                  <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                    Application permissions registered for this container type.
                    These are read-only — use the Register action to update.
                  </Text>
                  <Table className={styles.permTable} size="small" aria-label="Registered permissions">
                    <TableHeader>
                      <TableRow>
                        <TableHeaderCell>App ID</TableHeaderCell>
                        <TableHeaderCell>Delegated</TableHeaderCell>
                        <TableHeaderCell>Application</TableHeaderCell>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {permissions.map((perm, idx) => (
                        <TableRow key={perm.appId ?? idx}>
                          <TableCell>
                            <Text
                              size={200}
                              style={{
                                color: tokens.colorNeutralForeground2,
                                wordBreak: "break-all",
                              }}
                            >
                              {perm.appId}
                              {perm.appDisplayName && (
                                <Text
                                  block
                                  size={200}
                                  style={{ color: tokens.colorNeutralForeground1 }}
                                >
                                  {perm.appDisplayName}
                                </Text>
                              )}
                            </Text>
                          </TableCell>
                          <TableCell>
                            <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                              {perm.delegatedPermissions.length > 0
                                ? perm.delegatedPermissions.join(", ")
                                : "—"}
                            </Text>
                          </TableCell>
                          <TableCell>
                            <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                              {perm.applicationPermissions.length > 0
                                ? perm.applicationPermissions.join(", ")
                                : "—"}
                            </Text>
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </>
              )}
            </div>
          )}
        </SidePaneShell>
      </div>

      {/* ── Unsaved Changes Confirmation Dialog ── */}
      <Dialog
        open={confirmCloseOpen}
        onOpenChange={(_e, { open: isOpen }) => {
          if (!isOpen) setConfirmCloseOpen(false);
        }}
      >
        <DialogSurface>
          <DialogTitle>Unsaved Changes</DialogTitle>
          <DialogBody>
            <DialogContent>
              You have unsaved changes to the container type settings. If you
              close this panel, your changes will be lost.
            </DialogContent>
            <DialogActions>
              <Button
                appearance="secondary"
                onClick={() => setConfirmCloseOpen(false)}
              >
                Keep Editing
              </Button>
              <Button appearance="primary" onClick={handleConfirmClose}>
                Discard Changes
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </>
  );
};
