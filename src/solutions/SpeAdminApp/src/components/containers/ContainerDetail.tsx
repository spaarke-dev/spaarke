/**
 * ContainerDetail — slide-in detail panel for a selected SPE container.
 *
 * Opens from the right when an administrator clicks a container row in the
 * ContainersPage grid. Displays comprehensive container information across
 * four tabs: Details, Permissions, Columns, and Custom Properties.
 *
 * Data loading strategy:
 *   - Container detail (GET /api/spe/containers/{id}) is loaded immediately
 *     when the panel opens (containerId prop changes to a non-null value).
 *   - Permissions, Columns, and Custom Properties are loaded lazily the first
 *     time the corresponding tab is selected to avoid unnecessary API calls.
 *
 * Layout:
 *   - Uses SidePaneShell (ADR-012) for the fixed-header / scrollable-body layout.
 *   - Positioned as a fixed overlay panel on the right side of the viewport.
 *   - Fluent DrawerBody animation is approximated with CSS transition on the
 *     panel wrapper (Fluent v9 InlineDrawer used for the slide effect).
 *
 * ADR-021: All styles use Fluent UI v9 makeStyles + design tokens.
 * ADR-012: SidePaneShell reused from @spaarke/ui-components.
 * ADR-006: Code Page — React 18 patterns, no PCF / ComponentFramework deps.
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
  Divider,
  shorthands,
} from "@fluentui/react-components";
import {
  Dismiss20Regular,
  ArrowClockwise20Regular,
  Person20Regular,
  ColumnTriple20Regular,
  Settings20Regular,
  Info20Regular,
} from "@fluentui/react-icons";
import { SidePaneShell } from "@spaarke/ui-components";
import { useBuContext } from "../../contexts/BuContext";
import { speApiClient, ApiError } from "../../services/speApiClient";
import { PermissionPanel } from "./PermissionPanel";
import { ColumnEditor } from "./ColumnEditor";
import { CustomPropertyEditor } from "./CustomPropertyEditor";
import type {
  Container,
  ColumnDefinition,
  ContainerStatus,
} from "../../types/spe";

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface ContainerDetailProps {
  /** ID of the container to display, or null when no container is selected. */
  containerId: string | null;
  /** Callback to close the panel. */
  onClose: () => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Tab identifiers
// ─────────────────────────────────────────────────────────────────────────────

type TabId = "details" | "permissions" | "columns" | "customProperties";

// ─────────────────────────────────────────────────────────────────────────────
// Utilities
// ─────────────────────────────────────────────────────────────────────────────

/** Format bytes to a human-readable size string (e.g. "1.2 GB"). */
function formatBytes(bytes: number | undefined): string {
  if (bytes === undefined || bytes === null) return "—";
  if (bytes === 0) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  let value = bytes;
  let unitIndex = 0;
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex++;
  }
  return `${value.toFixed(1)} ${units[unitIndex]}`;
}

/** Format an ISO date string to a localised short date + time. */
function formatDateTime(iso: string | undefined): string {
  if (!iso) return "—";
  try {
    return new Intl.DateTimeFormat(undefined, {
      year: "numeric",
      month: "short",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    }).format(new Date(iso));
  } catch {
    return iso;
  }
}

/** Map ContainerStatus to a Fluent Badge color. */
function statusBadgeColor(
  status: ContainerStatus
): "success" | "warning" | "danger" | "informative" {
  switch (status) {
    case "active":
      return "success";
    case "inactive":
      return "warning";
    case "deleted":
      return "danger";
    default:
      return "informative";
  }
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

  /** Panel container — fixed right-side overlay, 420px wide. */
  panel: {
    position: "fixed",
    top: 0,
    right: 0,
    bottom: 0,
    width: "420px",
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
    padding: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalM),
  },

  /** Empty footer (SidePaneShell requires a footer prop). */
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
    color: tokens.colorNeutralForeground2,
  },

  /** Two-column property row for Details tab. */
  propertyRow: {
    display: "grid",
    gridTemplateColumns: "140px 1fr",
    ...shorthands.gap(tokens.spacingHorizontalS),
    alignItems: "flex-start",
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },

  propertyLabel: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    paddingTop: "2px",
  },

  propertyValue: {
    color: tokens.colorNeutralForeground1,
    wordBreak: "break-all",
  },

  sectionTitle: {
    color: tokens.colorNeutralForeground2,
    textTransform: "uppercase",
    letterSpacing: "0.04em",
    marginBottom: tokens.spacingVerticalXS,
  },

  /** Full-width table inside tabs. */
  table: {
    width: "100%",
  },

  /** Empty state for a tab with no data. */
  emptyTabState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingVerticalS),
    paddingTop: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground3,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Sub-components
// ─────────────────────────────────────────────────────────────────────────────

/** Labelled property row for the Details tab. */
const PropertyRow: React.FC<{ label: string; children: React.ReactNode }> = ({
  label,
  children,
}) => {
  const styles = useStyles();
  return (
    <div className={styles.propertyRow}>
      <Text className={styles.propertyLabel}>{label}</Text>
      <div className={styles.propertyValue}>{children}</div>
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Details Tab
// ─────────────────────────────────────────────────────────────────────────────

const DetailsTab: React.FC<{ container: Container }> = ({ container }) => {
  const styles = useStyles();
  return (
    <div className={styles.tabContent}>
      <Text size={200} weight="semibold" className={styles.sectionTitle}>
        Identity
      </Text>
      <Divider />
      <PropertyRow label="Name">
        <Text>{container.displayName}</Text>
      </PropertyRow>
      <PropertyRow label="Container ID">
        <Text
          size={200}
          style={{ fontFamily: tokens.fontFamilyMonospace, wordBreak: "break-all" }}
        >
          {container.id}
        </Text>
      </PropertyRow>
      <PropertyRow label="Container Type">
        <Text
          size={200}
          style={{ fontFamily: tokens.fontFamilyMonospace, wordBreak: "break-all" }}
        >
          {container.containerTypeId}
        </Text>
      </PropertyRow>
      {container.description && (
        <PropertyRow label="Description">
          <Text>{container.description}</Text>
        </PropertyRow>
      )}

      <Text size={200} weight="semibold" className={styles.sectionTitle} style={{ marginTop: tokens.spacingVerticalM }}>
        Status
      </Text>
      <Divider />
      <PropertyRow label="Status">
        <Badge color={statusBadgeColor(container.status)} appearance="filled" size="small">
          {container.status.charAt(0).toUpperCase() + container.status.slice(1)}
        </Badge>
      </PropertyRow>
      <PropertyRow label="Versioning">
        <Text>{container.isItemVersioningEnabled ? "Enabled" : "Disabled"}</Text>
      </PropertyRow>
      {container.settings?.majorVersionLimit !== undefined && (
        <PropertyRow label="Max Versions">
          <Text>{container.settings.majorVersionLimit}</Text>
        </PropertyRow>
      )}

      <Text size={200} weight="semibold" className={styles.sectionTitle} style={{ marginTop: tokens.spacingVerticalM }}>
        Dates
      </Text>
      <Divider />
      <PropertyRow label="Created">
        <Text>{formatDateTime(container.createdDateTime)}</Text>
      </PropertyRow>
      <PropertyRow label="Last Modified">
        <Text>{formatDateTime(container.lastModifiedDateTime)}</Text>
      </PropertyRow>

      <Text size={200} weight="semibold" className={styles.sectionTitle} style={{ marginTop: tokens.spacingVerticalM }}>
        Storage
      </Text>
      <Divider />
      <PropertyRow label="Storage Used">
        <Text>{formatBytes(container.storageUsedInBytes)}</Text>
      </PropertyRow>
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// ContainerDetail (main component)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Slide-in detail panel for a selected SPE container.
 *
 * Rendered as a portal-style fixed overlay with a backdrop. The parent
 * (ContainersPage) controls visibility by setting containerId to null or a value.
 */
export const ContainerDetail: React.FC<ContainerDetailProps> = ({
  containerId,
  onClose,
}) => {
  const styles = useStyles();
  const { selectedConfig } = useBuContext();

  // ── Active Tab ────────────────────────────────────────────────────────────

  const [activeTab, setActiveTab] = React.useState<TabId>("details");

  // ── Container Detail Data ─────────────────────────────────────────────────

  const [container, setContainer] = React.useState<Container | null>(null);
  const [containerLoading, setContainerLoading] = React.useState(false);
  const [containerError, setContainerError] = React.useState<string | null>(null);

  // ── Lazy-Loaded Tab Data ──────────────────────────────────────────────────
  // Note: Permissions tab now uses PermissionPanel which manages its own state.

  const [columns, setColumns] = React.useState<ColumnDefinition[] | null>(null);
  const [columnsLoading, setColumnsLoading] = React.useState(false);
  const [columnsError, setColumnsError] = React.useState<string | null>(null);
  const [columnsLoaded, setColumnsLoaded] = React.useState(false);

  // ── Load Container Detail on Open ────────────────────────────────────────

  const loadContainer = React.useCallback(async (id: string) => {
    if (!selectedConfig) return;
    setContainerLoading(true);
    setContainerError(null);
    try {
      const data = await speApiClient.containers.get(id, selectedConfig.id);
      setContainer(data);
    } catch (err) {
      const message =
        err instanceof ApiError ? err.message : "Failed to load container details.";
      setContainerError(message);
    } finally {
      setContainerLoading(false);
    }
  }, [selectedConfig]);

  // Reset state when containerId changes (new container selected)
  React.useEffect(() => {
    if (!containerId) {
      // Panel closing — reset all state
      setContainer(null);
      setContainerError(null);
      setActiveTab("details");
      setColumns(null);
      setColumnsLoaded(false);
      setColumnsError(null);
      return;
    }
    // New container — load detail immediately
    void loadContainer(containerId);
    setActiveTab("details");
    setColumns(null);
    setColumnsLoaded(false);
  }, [containerId, loadContainer]);

  // ── Lazy Load: Columns ────────────────────────────────────────────────────

  const loadColumns = React.useCallback(async () => {
    if (!containerId || !selectedConfig || columnsLoaded) return;
    setColumnsLoading(true);
    setColumnsError(null);
    try {
      const data = await speApiClient.columns.list(containerId, selectedConfig.id);
      setColumns(data);
      setColumnsLoaded(true);
    } catch (err) {
      const message =
        err instanceof ApiError ? err.message : "Failed to load columns.";
      setColumnsError(message);
    } finally {
      setColumnsLoading(false);
    }
  }, [containerId, selectedConfig, columnsLoaded]);

  // ── Tab Change Handler ────────────────────────────────────────────────────

  const handleTabSelect = React.useCallback(
    (_e: SelectTabEvent, data: SelectTabData) => {
      const tab = data.value as TabId;
      setActiveTab(tab);
      // Trigger lazy load for the columns tab.
      // Permissions tab uses PermissionPanel which loads its own data.
      // Custom Properties tab uses CustomPropertyEditor which loads its own data.
      if (tab === "columns" && !columnsLoaded) {
        void loadColumns();
      }
    },
    [columnsLoaded, loadColumns],
  );

  // ── Retry handlers for each tab ───────────────────────────────────────────

  const handleRetryColumns = React.useCallback(() => {
    setColumnsLoaded(false);
    void loadColumns();
  }, [loadColumns]);

  // ── Column change handler (from ColumnEditor CRUD operations) ─────────────

  const handleColumnsChange = React.useCallback((updated: ColumnDefinition[]) => {
    setColumns(updated);
  }, []);

  // ── Keyboard close on Escape ──────────────────────────────────────────────

  React.useEffect(() => {
    if (!containerId) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [containerId, onClose]);

  // ── Don't render when no container selected ───────────────────────────────

  if (!containerId) return null;

  // ── Compose SidePaneShell slots ───────────────────────────────────────────

  const panelHeader = (
    <div className={styles.header}>
      <div className={styles.headerTitle}>
        {container ? (
          <>
            <Text size={400} weight="semibold" className={styles.headerName}>
              {container.displayName}
            </Text>
            <Badge
              color={statusBadgeColor(container.status)}
              appearance="filled"
              size="small"
              style={{ alignSelf: "flex-start" }}
            >
              {container.status.charAt(0).toUpperCase() + container.status.slice(1)}
            </Badge>
          </>
        ) : (
          <Text size={400} weight="semibold" className={styles.headerName}>
            Container Details
          </Text>
        )}
      </div>
      <Button
        appearance="subtle"
        icon={<Dismiss20Regular />}
        onClick={onClose}
        aria-label="Close container details"
      />
    </div>
  );

  const panelFooter = <div className={styles.emptyFooter} />;

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <>
      {/* Translucent backdrop — click to close */}
      <div
        className={styles.backdrop}
        onClick={onClose}
        role="presentation"
        aria-hidden="true"
      />

      {/* Panel */}
      <div className={styles.panel} role="dialog" aria-modal="true" aria-label="Container details">
        <SidePaneShell header={panelHeader} footer={panelFooter}>
          {/* Tab list */}
          <TabList
            selectedValue={activeTab}
            onTabSelect={handleTabSelect}
            className={styles.tabList}
            size="medium"
          >
            <Tab value="details" icon={<Info20Regular />}>
              Details
            </Tab>
            <Tab value="permissions" icon={<Person20Regular />}>
              Permissions
            </Tab>
            <Tab value="columns" icon={<ColumnTriple20Regular />}>
              Columns
            </Tab>
            <Tab value="customProperties" icon={<Settings20Regular />}>
              Custom Properties
            </Tab>
          </TabList>

          {/* Tab content area */}
          {containerLoading ? (
            <div className={styles.feedback}>
              <Spinner size="medium" label="Loading container…" />
            </div>
          ) : containerError ? (
            <div className={styles.feedback}>
              <MessageBar intent="error">
                <MessageBarBody>{containerError}</MessageBarBody>
              </MessageBar>
              <Button
                appearance="secondary"
                icon={<ArrowClockwise20Regular />}
                onClick={() => containerId && void loadContainer(containerId)}
              >
                Retry
              </Button>
            </div>
          ) : container ? (
            <>
              {activeTab === "details" && <DetailsTab container={container} />}
              {activeTab === "permissions" && selectedConfig && (
                <div style={{ padding: tokens.spacingVerticalM, paddingLeft: tokens.spacingHorizontalL, paddingRight: tokens.spacingHorizontalL }}>
                  <PermissionPanel
                    containerId={container.id}
                    configId={selectedConfig.id}
                  />
                </div>
              )}
              {activeTab === "columns" && selectedConfig && (
                <div style={{ padding: tokens.spacingVerticalM, paddingLeft: tokens.spacingHorizontalL, paddingRight: tokens.spacingHorizontalL }}>
                  <ColumnEditor
                    containerId={container.id}
                    configId={selectedConfig.id}
                    columns={columns}
                    loading={columnsLoading}
                    error={columnsError}
                    onColumnsChange={handleColumnsChange}
                    onRetry={handleRetryColumns}
                  />
                </div>
              )}
              {activeTab === "customProperties" && (
                <CustomPropertyEditor
                  containerId={container.id}
                  isActive={activeTab === "customProperties"}
                />
              )}
            </>
          ) : null}
        </SidePaneShell>
      </div>
    </>
  );
};
