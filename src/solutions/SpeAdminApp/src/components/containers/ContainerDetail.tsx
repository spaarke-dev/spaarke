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
  Link,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Tooltip,
  Divider,
  shorthands,
} from "@fluentui/react-components";
import {
  Dismiss20Regular,
  ArrowClockwise20Regular,
  FolderOpen20Regular,
  Person20Regular,
  ColumnTriple20Regular,
  Settings20Regular,
  Delete20Regular,
  Info20Regular,
  Copy16Regular,
  CheckmarkCircle16Filled,
  Open16Regular,
} from "@fluentui/react-icons";
import { SidePaneShell } from "@spaarke/ui-components";
import { useBuContext } from "../../contexts/BuContext";
import { speApiClient, describeApiError } from "../../services/speApiClient";
import { copyToClipboard } from "../../services/clipboard";
import { PermissionPanel } from "./PermissionPanel";
import { ColumnEditor } from "./ColumnEditor";
import {
  PURVIEW_PORTAL_URL,
  PURVIEW_GUIDANCE_TITLE,
  PURVIEW_GUIDANCE_BODY,
  PURVIEW_GUIDANCE_STEPS,
  CONTAINER_URL_PURPOSE,
  CONTAINER_URL_LABEL,
  CONTAINER_URL_ABSENT_LABEL,
  CONTAINER_URL_ABSENT_TOOLTIP,
} from "./containerCompliance";
import { CustomPropertyEditor } from "./CustomPropertyEditor";
import { ContainerItemRecycleBin } from "../recycle-bin/ContainerItemRecycleBin";
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
  /** Optional callback to open the container in the file browser. */
  onBrowseFiles?: (containerId: string, containerName?: string) => void;
  /**
   * Pane height in pixels, owned by the host's `useResizablePane` so the splitter above can drag
   * it. Omitted falls back to the CSS default in `styles.panel`.
   */
  paneHeight?: number;
}

// ─────────────────────────────────────────────────────────────────────────────
// Tab identifiers
// ─────────────────────────────────────────────────────────────────────────────

type TabId = "details" | "permissions" | "columns" | "customProperties" | "recycleBin";

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

/**
 * Renders a byte count, or an explicit "Not reported" when Graph did not supply one (FR-E02).
 *
 * The distinction is load-bearing and this project has paid for it twice: an em-dash reads as "the
 * column is broken" and a substituted `0 B` reads as "this container is empty", which is a different
 * and false claim. Null means we were not told (spec NFR-06).
 */
const StorageValue: React.FC<{ bytes: number | null | undefined }> = ({ bytes }) =>
  bytes === undefined || bytes === null ? (
    <Tooltip
      content="Microsoft Graph did not report a figure for this container. This is not the same as zero."
      relationship="label"
    >
      <Text italic style={{ color: tokens.colorNeutralForeground3 }}>
        Not reported
      </Text>
    </Tooltip>
  ) : (
    <Text>{formatBytes(bytes)}</Text>
  );

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
  /**
   * Panel container — a docked BOTTOM pane, full width of the page.
   *
   * 🔴 Changed 2026-08-26 (UAT), matching `ContainerTypeDetail`. This was a 420px fixed overlay on
   * the right with a modal backdrop. The same pattern is now used on both list screens so it is
   * learned once: select a row, its detail docks beneath the list, the list stays live above it.
   *
   * Sized by the flex column in `ContainersPage` — hence `flex: 0 0 auto` rather than `position:
   * fixed`. Renders null when no container is selected, so the list reclaims the height on close.
   */
  panel: {
    flex: "0 0 auto",
    height: "45%",
    minHeight: "260px",
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    boxShadow: tokens.shadow16,
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

/**
 * The container URL with a copy affordance, or an explicit absent state.
 *
 * `webUrl` is undefined here only when Graph was asked and reported none — the BFF omits the key
 * from LIST rows entirely, and this panel always renders from a GET-single. So the absent state is
 * honest, and is a labelled state rather than a blank cell (NFR-06).
 */
const ContainerUrlRow: React.FC<{ webUrl?: string }> = ({ webUrl }) => {
  const [copied, setCopied] = React.useState(false);
  const [copyFailed, setCopyFailed] = React.useState(false);

  const handleCopy = React.useCallback(async () => {
    if (!webUrl) return;
    const ok = await copyToClipboard(webUrl);
    // Report what actually happened. A "Copied!" that did not copy is only discovered when the
    // admin pastes into Purview and gets nothing.
    setCopied(ok);
    setCopyFailed(!ok);
    setTimeout(() => {
      setCopied(false);
      setCopyFailed(false);
    }, 2000);
  }, [webUrl]);

  if (!webUrl) {
    return (
      <PropertyRow label={CONTAINER_URL_LABEL}>
        <Tooltip content={CONTAINER_URL_ABSENT_TOOLTIP} relationship="label">
          <Text italic style={{ color: tokens.colorNeutralForeground3 }}>
            {CONTAINER_URL_ABSENT_LABEL}
          </Text>
        </Tooltip>
      </PropertyRow>
    );
  }

  return (
    <PropertyRow label={CONTAINER_URL_LABEL}>
      <div style={{ display: "flex", alignItems: "flex-start", gap: tokens.spacingHorizontalXS }}>
        <Text size={200} style={{ wordBreak: "break-all", flex: 1 }}>
          {decodeURIComponent(webUrl)}
        </Text>
        <Tooltip
          content={
            copyFailed
              ? "Could not copy — select the text and copy manually"
              : "Copy the container URL"
          }
          relationship="label"
        >
          <Button
            appearance="subtle"
            size="small"
            icon={copied ? <CheckmarkCircle16Filled /> : <Copy16Regular />}
            onClick={() => void handleCopy()}
            aria-label="Copy the container URL"
          />
        </Tooltip>
      </div>
      {copyFailed && (
        <Text size={100} style={{ color: tokens.colorPaletteRedForeground1 }}>
          Copy was blocked by the browser — select the URL and copy it manually.
        </Text>
      )}
    </PropertyRow>
  );
};

/**
 * Routes an admin looking for hold / retention / eDiscovery to Purview (FR-C11, spec §4.2c).
 *
 * R2 deliberately builds no compliance MANAGEMENT — see containerCompliance.ts. This section exists
 * so the boundary reads as a design decision with a next step, not as a missing feature.
 */
const ComplianceSection: React.FC<{ webUrl?: string }> = ({ webUrl }) => {
  const styles = useStyles();
  return (
    <>
      <Text
        size={200}
        weight="semibold"
        className={styles.sectionTitle}
        style={{ marginTop: tokens.spacingVerticalM }}
      >
        Compliance
      </Text>
      <Divider />
      <MessageBar intent="info" style={{ marginTop: tokens.spacingVerticalS }}>
        <MessageBarBody>
          <MessageBarTitle>{PURVIEW_GUIDANCE_TITLE}</MessageBarTitle>
          <div style={{ marginTop: tokens.spacingVerticalXS }}>{PURVIEW_GUIDANCE_BODY}</div>
          <div style={{ marginTop: tokens.spacingVerticalXS }}>
            {webUrl ? CONTAINER_URL_PURPOSE : null}
          </div>
          <ol style={{ marginTop: tokens.spacingVerticalXS, paddingLeft: "1.2em" }}>
            {PURVIEW_GUIDANCE_STEPS.map((step) => (
              <li key={step}>
                <Text size={200}>{step}</Text>
              </li>
            ))}
          </ol>
          <Link href={PURVIEW_PORTAL_URL} target="_blank" rel="noopener noreferrer">
            Open the Microsoft Purview portal <Open16Regular />
          </Link>
        </MessageBarBody>
      </MessageBar>
    </>
  );
};

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
      <ContainerUrlRow webUrl={container.webUrl} />
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
        {/*
          Graph DOES return status on the detail fetch (measured live 2026-08-27) — unlike the list,
          where it is always absent. So this normally renders a real badge. The absent branch is not
          defensive padding: until 2026-08-27 the server discarded Graph's value and substituted
          "active" on every path, so this row asserted "Active" for containers Graph had reported as
          inactive. If the value is ever genuinely missing, saying so beats inventing one.
        */}
        {container.status ? (
          <Badge color={statusBadgeColor(container.status)} appearance="filled" size="small">
            {container.status.charAt(0).toUpperCase() + container.status.slice(1)}
          </Badge>
        ) : (
          <Text italic style={{ color: tokens.colorNeutralForeground3 }}>
            Not reported
          </Text>
        )}
      </PropertyRow>
      {/* Archive state (FR-E01) — a separate dimension from Status; shown only when there is one. */}
      {container.archiveStatus && (
        <PropertyRow label="Archive">
          <Badge
            color={container.archiveStatus === "reactivating" ? "informative" : "warning"}
            appearance="outline"
            size="small"
          >
            {container.archiveStatus === "fullyArchived"
              ? "Archived"
              : container.archiveStatus === "recentlyArchived"
                ? "Archiving…"
                : "Restoring…"}
          </Badge>
        </PropertyRow>
      )}
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
      {/*
        Storage (FR-E02, task 051).

        `quota.used` is preferred over `storageUsedInBytes` here because Graph does NOT return
        storageUsedInBytes on a single-container GET at all — it is beta-only AND list-only (tasks
        020/024). The quota facet, expanded from the drive, is the only consumption figure this view
        can get. Falls back to storageUsedInBytes so the row still works if the drive expand is ever
        dropped from the response.
      */}
      <PropertyRow label="Storage Used">
        <StorageValue
          bytes={container.quota?.used ?? container.storageUsedInBytes}
        />
      </PropertyRow>

      {/*
        The ceiling. Deliberately labelled "Storage Limit (per container)" with an explanatory note,
        NOT "Storage Limit for this container".

        Graph has no per-container ceiling: `maxStoragePerContainerInBytes` lives on the container
        TYPE and applies uniformly to every container of that type. A container-scope PATCH returns
        200 and silently discards the value (measured live 2026-08-27, notes/task-051-findings.md §1).
        So this value is identical across every container here, and presenting it as this container's
        own cap would invite an admin to look for an edit control that cannot exist.
      */}
      {container.quota?.total !== undefined && container.quota?.total !== null && (
        <PropertyRow label="Storage Limit">
          <div>
            <Text>{formatBytes(container.quota.total)}</Text>
            <br />
            <Text
              size={200}
              italic
              style={{ color: tokens.colorNeutralForeground3 }}
            >
              Set on the container type — applies to every container of this type
            </Text>
          </div>
        </PropertyRow>
      )}

      {container.quota?.remaining !== undefined && container.quota?.remaining !== null && (
        <PropertyRow label="Remaining">
          {/* Graph's own figure, not total − used: deleted items still count against the quota. */}
          <Text>{formatBytes(container.quota.remaining)}</Text>
        </PropertyRow>
      )}

      {Boolean(container.quota?.deleted) && (
        <PropertyRow label="Held by deleted items">
          <Tooltip
            content="Deleted items still count against the storage quota until they are permanently removed."
            relationship="label"
          >
            <Text>{formatBytes(container.quota?.deleted ?? undefined)}</Text>
          </Tooltip>
        </PropertyRow>
      )}

      <ComplianceSection webUrl={container.webUrl} />
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
  onBrowseFiles,
  paneHeight,
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
        describeApiError(err, "Failed to load container details.");
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
        describeApiError(err, "Failed to load columns.");
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
            {container.status && (
              <Badge
                color={statusBadgeColor(container.status)}
                appearance="filled"
                size="small"
                style={{ alignSelf: "flex-start" }}
              >
                {container.status.charAt(0).toUpperCase() + container.status.slice(1)}
              </Badge>
            )}
          </>
        ) : (
          <Text size={400} weight="semibold" className={styles.headerName}>
            Container Details
          </Text>
        )}
      </div>
      {onBrowseFiles && containerId && (
        <Button
          appearance="subtle"
          icon={<FolderOpen20Regular />}
          onClick={() => onBrowseFiles(containerId, container?.displayName)}
          aria-label="Browse files in this container"
          title="Browse files"
        />
      )}
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
      {/* Panel — docked beneath the list. No backdrop, and no longer `aria-modal`: the grid above
          stays both interactive and reachable by assistive tech while this is open. */}
      <div
        className={styles.panel}
        style={paneHeight !== undefined ? { height: `${paneHeight}px` } : undefined}
        role="complementary"
        aria-label="Container details"
      >
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
            {/*
              The per-container ITEM recycle bin (FR-E03). Deliberately lives here rather than on
              the top-level Recycle Bin screen, which lists deleted CONTAINERS — spec D3 keeps the
              two distinct, and a deleted file only has meaning relative to the container it was
              deleted from.
            */}
            <Tab value="recycleBin" icon={<Delete20Regular />}>
              Recycle Bin
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
              {activeTab === "recycleBin" && selectedConfig && (
                <div style={{ padding: tokens.spacingVerticalM, paddingLeft: tokens.spacingHorizontalL, paddingRight: tokens.spacingHorizontalL }}>
                  <ContainerItemRecycleBin
                    containerId={container.id}
                    configId={selectedConfig.id}
                    containerName={container.displayName}
                    isActive={activeTab === "recycleBin"}
                  />
                </div>
              )}
            </>
          ) : null}
        </SidePaneShell>
      </div>
    </>
  );
};
