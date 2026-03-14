/**
 * ContainerTypesPage — SPE container type management interface.
 *
 * Displays all SPE container types for the selected BU/config context
 * in a Fluent v9 DataGrid. Administrators can:
 *   - View all container types (name, description, billing classification, created date)
 *   - Create a new container type via the "New" toolbar button
 *   - Initiate registration of a container type via the "Register" toolbar button
 *   - Click a row to open the ContainerTypeDetail panel (SPE-062)
 *
 * ADR-006: Code Page — React 18 patterns, no PCF / ComponentFramework dependencies.
 * ADR-012: Reuses Fluent v9 DataGrid (same as ContainersPage).
 * ADR-021: All styles use makeStyles + Fluent design tokens (no hard-coded colors).
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
  Button,
  shorthands,
} from "@fluentui/react-components";
import {
  Add20Regular,
  ArrowClockwise20Regular,
  CloudLink20Regular,
  DocumentBulletList20Regular,
} from "@fluentui/react-icons";
import { useBuContext } from "../../contexts/BuContext";
import { speApiClient, ApiError } from "../../services/speApiClient";
import type { ContainerType } from "../../types/spe";
import { CreateContainerTypeDialog } from "./CreateContainerTypeDialog";

// ─────────────────────────────────────────────────────────────────────────────
// Utilities
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

/** Map billing classification to a badge color. */
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

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021 — Fluent tokens only)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },

  header: {
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    flexShrink: 0,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
  },

  pageTitle: {
    marginBottom: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground1,
  },

  pageSubtitle: {
    color: tokens.colorNeutralForeground2,
  },

  /** Command toolbar sits between the page header and the data grid. */
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

  /** Scrollable content area containing the data grid. */
  content: {
    flex: "1 1 auto",
    overflow: "auto",
    minHeight: 0,
  },

  /** Feedback area (loading / error / empty state). */
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

  /** Message bar wrapper */
  messageBarWrapper: {
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
  },

  /** DataGrid fills its parent. */
  dataGrid: {
    width: "100%",
  },

  /** DataGrid header row uses a subtle background. */
  dataGridHeaderRow: {
    backgroundColor: tokens.colorNeutralBackground2,
  },

  /** Row hover highlight — cursor indicates clickability. */
  dataGridRow: {
    cursor: "pointer",
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },

  buttonLabel: {
    marginLeft: tokens.spacingHorizontalXS,
  },

  /** Context-prompt shown when no BU/config is selected. */
  noContextBanner: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalM,
    height: "100%",
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground2,
  },

  /** Owning app ID shown in muted color. */
  mutedText: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Column Definitions
// ─────────────────────────────────────────────────────────────────────────────

/** Build typed Fluent DataGrid column definitions for ContainerType rows. */
function buildColumns(): TableColumnDefinition<ContainerType>[] {
  return [
    createTableColumn<ContainerType>({
      columnId: "displayName",
      renderHeaderCell: () => "Name",
      renderCell: (ct) => (
        <Text weight="semibold" truncate>
          {ct.displayName}
        </Text>
      ),
    }),
    createTableColumn<ContainerType>({
      columnId: "billingClassification",
      renderHeaderCell: () => "Billing Classification",
      renderCell: (ct) => (
        <Badge
          color={billingBadgeColor(ct.billingClassification)}
          appearance="filled"
          size="small"
        >
          {capitalize(ct.billingClassification)}
        </Badge>
      ),
    }),
    createTableColumn<ContainerType>({
      columnId: "owningAppId",
      renderHeaderCell: () => "Owning App",
      renderCell: (ct) => (
        <Text truncate style={{ color: tokens.colorNeutralForeground2 }}>
          {ct.owningAppId}
        </Text>
      ),
    }),
    createTableColumn<ContainerType>({
      columnId: "isRegistered",
      renderHeaderCell: () => "Registered",
      renderCell: (ct) => (
        <Badge
          color={ct.isRegistered === true ? "success" : "warning"}
          appearance="filled"
          size="small"
        >
          {ct.isRegistered === true ? "Yes" : "No"}
        </Badge>
      ),
    }),
    createTableColumn<ContainerType>({
      columnId: "createdDateTime",
      renderHeaderCell: () => "Created",
      renderCell: (ct) => <Text>{formatDate(ct.createdDateTime)}</Text>,
    }),
  ];
}

// ─────────────────────────────────────────────────────────────────────────────
// ContainerTypesPage
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Props for ContainerTypesPage.
 *
 * onOpenDetail — called when a row is clicked.
 * The parent (App.tsx) will eventually wire this to open a ContainerTypeDetail
 * panel (SPE-062). For now the prop is optional; clicking a row stores the
 * selected type ID in state for the parent to observe.
 */
export interface ContainerTypesPageProps {
  /**
   * Called when the user clicks a row to open the detail panel (SPE-062).
   * If omitted the row click is a no-op (detail panel not yet wired).
   */
  onOpenDetail?: (containerTypeId: string) => void;
}

/**
 * ContainerTypesPage — primary container type management view.
 *
 * Uses `useBuContext()` to obtain the selected container type config.
 * When no config is selected, renders a prompt to select a BU/config first.
 */
export const ContainerTypesPage: React.FC<ContainerTypesPageProps> = ({
  onOpenDetail,
}) => {
  const styles = useStyles();
  const { selectedConfig } = useBuContext();

  // ── Data State ──────────────────────────────────────────────────────────────

  const [containerTypes, setContainerTypes] = React.useState<ContainerType[]>([]);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // ── Action State ────────────────────────────────────────────────────────────

  /** ID of the selected container type (set on row click). */
  const [selectedTypeId, setSelectedTypeId] = React.useState<string | null>(null);

  /** Action status or error messages from toolbar actions. */
  const [actionError, setActionError] = React.useState<string | null>(null);
  const [actionStatus, setActionStatus] = React.useState<string | null>(null);

  // ── Create Dialog State ─────────────────────────────────────────────────────

  const [createOpen, setCreateOpen] = React.useState(false);
  const [createSaving, setCreateSaving] = React.useState(false);

  // ── Column Definitions (stable reference) ──────────────────────────────────

  const columns = React.useMemo(() => buildColumns(), []);

  // ── Data Loading ────────────────────────────────────────────────────────────

  const loadContainerTypes = React.useCallback(async () => {
    if (!selectedConfig) return;
    setLoading(true);
    setError(null);
    setActionError(null);
    setActionStatus(null);
    setSelectedTypeId(null);
    try {
      const data = await speApiClient.containerTypes.list(selectedConfig.id);
      setContainerTypes(data);
    } catch (err) {
      const message =
        err instanceof ApiError
          ? err.message
          : "Failed to load container types. Please try again.";
      setError(message);
    } finally {
      setLoading(false);
    }
  }, [selectedConfig]);

  // Auto-load when selectedConfig changes
  React.useEffect(() => {
    if (selectedConfig) {
      void loadContainerTypes();
    } else {
      setContainerTypes([]);
      setSelectedTypeId(null);
      setError(null);
    }
  }, [selectedConfig, loadContainerTypes]);

  // ── Row Click Handler (opens detail panel) ──────────────────────────────────

  const handleRowClick = React.useCallback(
    (typeId: string) => {
      setSelectedTypeId(typeId);
      // Wire to SPE-062 ContainerTypeDetail when implemented
      onOpenDetail?.(typeId);
    },
    [onOpenDetail]
  );

  // ── Register Action Handler ─────────────────────────────────────────────────

  /**
   * Register the selected container type on the consuming tenant.
   * Uses delegated/application permissions from the selectedConfig.
   */
  const handleRegister = React.useCallback(async () => {
    if (!selectedConfig || !selectedTypeId) return;
    setActionError(null);
    setActionStatus(null);

    const ct = containerTypes.find((t) => t.containerTypeId === selectedTypeId);
    if (!ct) return;

    // Parse permissions from the config (comma-separated strings → string[])
    const delegated = selectedConfig.delegatedPermissions
      ? selectedConfig.delegatedPermissions.split(",").map((p) => p.trim()).filter(Boolean)
      : [];
    const application = selectedConfig.applicationPermissions
      ? selectedConfig.applicationPermissions.split(",").map((p) => p.trim()).filter(Boolean)
      : [];

    try {
      await speApiClient.containerTypes.register(selectedTypeId, selectedConfig.id, {
        delegatedPermissions: delegated,
        applicationPermissions: application,
      });
      setActionStatus(`Container type "${ct.displayName}" registered successfully.`);
      await loadContainerTypes();
    } catch (err) {
      const message =
        err instanceof ApiError
          ? err.message
          : "Failed to register container type. Please try again.";
      setActionError(message);
    }
  }, [selectedConfig, selectedTypeId, containerTypes, loadContainerTypes]);

  // ── Create Container Type ───────────────────────────────────────────────────

  const handleCreateSubmit = React.useCallback(
    async (displayName: string, billingClassification: string) => {
      if (!selectedConfig) return;
      setCreateSaving(true);
      setActionError(null);
      setActionStatus(null);
      try {
        await speApiClient.containerTypes.create(selectedConfig.id, {
          displayName,
          billingClassification,
        });
        setCreateOpen(false);
        setActionStatus(`Container type "${displayName}" created successfully.`);
        await loadContainerTypes();
      } catch (err) {
        const message =
          err instanceof ApiError
            ? err.message
            : "Failed to create container type.";
        setActionError(message);
      } finally {
        setCreateSaving(false);
      }
    },
    [selectedConfig, loadContainerTypes]
  );

  // ── Render: No Config Selected ──────────────────────────────────────────────

  if (!selectedConfig) {
    return (
      <div className={styles.root}>
        <div className={styles.header}>
          <Text as="h1" size={500} weight="semibold" className={styles.pageTitle}>
            Container Types
          </Text>
          <Text size={300} className={styles.pageSubtitle}>
            Manage SharePoint Embedded container type definitions
          </Text>
        </div>
        <div className={styles.noContextBanner}>
          <DocumentBulletList20Regular style={{ fontSize: "48px", opacity: 0.4 }} />
          <Text size={400} weight="semibold">
            No configuration selected
          </Text>
          <Text size={300} align="center">
            Select a Business Unit and Container Type Configuration in the top
            navigation bar to view and manage container types.
          </Text>
        </div>
      </div>
    );
  }

  // ── Render: Main View ───────────────────────────────────────────────────────

  const hasSelectedType = !!selectedTypeId;

  return (
    <div className={styles.root}>
      {/* ── Page Header ── */}
      <div className={styles.header}>
        <Text as="h1" size={500} weight="semibold" className={styles.pageTitle}>
          Container Types
        </Text>
        <Text size={300} className={styles.pageSubtitle}>
          {selectedConfig.name} &middot; {selectedConfig.environmentName}
          {containerTypes.length > 0 && (
            <>
              {" "}
              &middot; {containerTypes.length} type
              {containerTypes.length !== 1 ? "s" : ""}
            </>
          )}
        </Text>
      </div>

      {/* ── Command Toolbar ── */}
      <Toolbar aria-label="Container type actions" className={styles.toolbar}>
        {/* Create */}
        <Tooltip content="Create a new container type" relationship="description">
          <ToolbarButton
            icon={<Add20Regular />}
            onClick={() => setCreateOpen(true)}
            disabled={loading}
            aria-label="Create container type"
          >
            <span className={styles.buttonLabel}>New</span>
          </ToolbarButton>
        </Tooltip>

        <ToolbarDivider />

        {/* Register */}
        <Tooltip
          content={
            hasSelectedType
              ? "Register selected container type on consuming tenant"
              : "Click a row to select a container type to register"
          }
          relationship="description"
        >
          <ToolbarButton
            icon={<CloudLink20Regular />}
            onClick={() => { void handleRegister(); }}
            disabled={!hasSelectedType || loading}
            aria-label="Register container type"
          >
            <span className={styles.buttonLabel}>Register</span>
          </ToolbarButton>
        </Tooltip>

        <ToolbarDivider />

        {/* Refresh */}
        <Tooltip content="Refresh container type list" relationship="description">
          <ToolbarButton
            icon={
              loading ? <Spinner size="tiny" /> : <ArrowClockwise20Regular />
            }
            onClick={() => { void loadContainerTypes(); }}
            disabled={loading}
            aria-label="Refresh container types"
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
        {loading && containerTypes.length === 0 ? (
          <div className={styles.feedback}>
            <Spinner size="medium" label="Loading container types…" />
          </div>
        ) : error ? (
          <div className={styles.feedback}>
            <MessageBar intent="error">
              <MessageBarBody>
                <MessageBarTitle>Failed to load container types</MessageBarTitle>
                {error}
              </MessageBarBody>
            </MessageBar>
            <Button
              appearance="secondary"
              icon={<ArrowClockwise20Regular />}
              onClick={() => { void loadContainerTypes(); }}
            >
              Retry
            </Button>
          </div>
        ) : containerTypes.length === 0 ? (
          <div className={styles.feedback}>
            <DocumentBulletList20Regular style={{ fontSize: "48px", opacity: 0.4 }} />
            <Text size={400} weight="semibold">
              No container types
            </Text>
            <Text size={300}>
              No container types found for this configuration. Use the{" "}
              <strong>New</strong> button to create one.
            </Text>
          </div>
        ) : (
          <ContainerTypeDataGrid
            containerTypes={containerTypes}
            columns={columns}
            selectedTypeId={selectedTypeId}
            onRowClick={handleRowClick}
            className={styles.dataGrid}
          />
        )}
      </div>

      {/* ── Create Container Type Dialog ── */}
      <CreateContainerTypeDialog
        open={createOpen}
        isSaving={createSaving}
        onClose={() => setCreateOpen(false)}
        onSubmit={(name, billing) => { void handleCreateSubmit(name, billing); }}
      />
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// ContainerTypeDataGrid (inner component)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Inner component that renders the Fluent v9 DataGrid for container types.
 *
 * Row interaction model:
 *   - Clicking any row selects that type (single-select — for the Register action).
 *   - The selected row is highlighted with the "brand" appearance.
 *   - Row click also calls onRowClick to open the ContainerTypeDetail panel (SPE-062).
 */
interface ContainerTypeDataGridProps {
  containerTypes: ContainerType[];
  columns: TableColumnDefinition<ContainerType>[];
  selectedTypeId: string | null;
  onRowClick: (typeId: string) => void;
  className?: string;
}

const ContainerTypeDataGrid: React.FC<ContainerTypeDataGridProps> = ({
  containerTypes,
  columns,
  selectedTypeId,
  onRowClick,
  className,
}) => {
  return (
    <DataGrid
      items={containerTypes}
      columns={columns}
      sortable={false}
      selectionMode="single"
      selectedItems={selectedTypeId ? new Set([selectedTypeId]) : new Set()}
      getRowId={(ct) => ct.containerTypeId}
      className={className}
      aria-label="Container types"
    >
      <DataGridHeader>
        <DataGridRow>
          {({ renderHeaderCell }) => (
            <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
          )}
        </DataGridRow>
      </DataGridHeader>
      <DataGridBody<ContainerType>>
        {({ item, rowId }) => {
          const isSelected = item.containerTypeId === selectedTypeId;
          return (
            <DataGridRow<ContainerType>
              key={rowId}
              aria-selected={isSelected}
              appearance={isSelected ? "brand" : "none"}
              style={{ cursor: "pointer" }}
              onClick={() => onRowClick(item.containerTypeId)}
              onKeyDown={(e: React.KeyboardEvent) => {
                if (e.key === "Enter" || e.key === " ") {
                  e.preventDefault();
                  onRowClick(item.containerTypeId);
                }
              }}
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
