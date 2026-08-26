/**
 * ContainersPage — SPE container management interface.
 *
 * Displays all SPE storage containers for the selected BU/config context
 * in a data grid with a command toolbar for lifecycle operations.
 *
 * Toolbar actions:
 *   Create   — opens inline create form (displayName required)
 *   Activate — moves selected inactive containers to active
 *   Lock     — puts selected containers in read-only mode
 *   Unlock   — removes read-only restriction from selected containers
 *   Delete   — soft-deletes selected containers (moved to recycle bin)
 *
 * Row selection enables / disables action buttons:
 *   - No selection: only Create and Refresh are enabled
 *   - Selection present: Activate, Lock, Unlock, Delete become enabled
 *
 * Row click (non-checkbox area) opens the ContainerDetail side panel.
 *
 * ADR-021: All styles use Fluent UI v9 makeStyles + design tokens (no hard-coded colours).
 * ADR-012: Imports reuse @spaarke/ui-components where applicable.
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
  shorthands,
} from "@fluentui/react-components";
import {
  Add20Regular,
  LockClosed20Regular,
  LockOpen20Regular,
  Delete20Regular,
  ArrowClockwise20Regular,
  CheckmarkCircle20Regular,
  Storage20Regular,
  FolderOpen20Regular,
  Copy16Regular,
  CheckmarkCircle16Filled,
  Link16Regular,
  Dismiss20Regular,
} from "@fluentui/react-icons";
import { useBuContext } from "../../contexts/BuContext";
import { speApiClient, describeApiError } from "../../services/speApiClient";
import { copyToClipboard } from "../../services/clipboard";
import type { Container, ContainerStatus } from "../../types/spe";
import { ContainerDetail } from "./ContainerDetail";
import { FileBrowserPage } from "../files/FileBrowserPage";
import { useResizablePane, PaneSplitter } from "../layout/ResizablePane";
import {
  CONTAINER_URL_LABEL,
  CONTAINER_URL_ABSENT_LABEL,
  CONTAINER_URL_ABSENT_TOOLTIP,
  CONTAINER_URL_ON_DEMAND_TOOLTIP,
  type ContainerUrlState,
} from "./containerCompliance";

// ─────────────────────────────────────────────────────────────────────────────
// Utilities
// ─────────────────────────────────────────────────────────────────────────────

/** Format bytes to a human-readable size string (e.g. "1.2 GB"). */
/**
 * Format a byte count. Callers MUST handle absence themselves — see the Storage Used column.
 *
 * This deliberately does NOT map absence to an em-dash any more. Until 2026-08-24 every container
 * returned null here (the BFF fetched the value from Graph and discarded it), so this column showed
 * "—" for every row, and an operator had no way to tell "we did not measure" from "nothing stored"
 * (spec NFR-06).
 */
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

/** Map a ContainerStatus to a Fluent Badge color. */
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

  /** `display: block` so the scope line sits below the title — see ContainerTypesPage. */
  pageSubtitle: {
    display: "block",
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

  /**
   * Browse pane below the list. Capped at half the viewport so the container list stays visible
   * and usable while browsing — the point of the split is to see both, not to trade one for the
   * other.
   */
  browsePane: {
    display: "flex",
    flexDirection: "column",
    flex: "1 1 50%",
    minHeight: "220px",
    maxHeight: "55%",
    overflow: "hidden",
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
  },

  browsePaneHeader: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: tokens.spacingHorizontalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
  },

  /** minHeight:0 is required, or the embedded page's own scroll container never scrolls. */
  browsePaneBody: {
    flex: "1 1 auto",
    minHeight: 0,
    overflow: "hidden",
  },

  /** DataGrid fills its parent. */
  dataGrid: {
    width: "100%",
  },

  /** DataGrid header row uses a subtle background. */
  dataGridHeaderRow: {
    backgroundColor: tokens.colorNeutralBackground2,
  },

  /** Row hover highlight. */
  dataGridRow: {
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

  storageUsageCell: {
    color: tokens.colorNeutralForeground2,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Column Definitions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Per-row container URL (FR-C10) — resolved on demand, then copyable.
 *
 * 🔑 WHY ON DEMAND, AND WHY THIS IS NOT A SHORTCUT. Microsoft Graph cannot return container URLs on
 * a LIST at all. `fileStorageContainer` has no URL property in either API version; the value lives on
 * the `drive` navigation property, and on the containers COLLECTION Graph accepts
 * `$expand=drive($select=webUrl)`, answers 200, echoes `drive(webUrl)` back in `@odata.context` — and
 * omits `drive` from every row. Measured 2026-08-24 across both versions and every expand shape
 * (notes/task-028-findings.md §1).
 *
 * So an eagerly-populated column is not available at any price short of N extra Graph calls on every
 * grid load. Resolving the one container the admin actually asks about costs one call, matches the
 * real workflow (find a container → copy its URL → paste into a Purview eDiscovery search), and —
 * decisively — cannot render a false absent state: there is no point at which this cell claims a
 * container has no URL because nobody asked Graph for it.
 */
const ContainerUrlCell: React.FC<{ container: Container; configId?: string }> = ({
  container,
  configId,
}) => {
  const [state, setState] = React.useState<ContainerUrlState>({ kind: "idle" });
  const [copied, setCopied] = React.useState(false);

  const resolveAndCopy = React.useCallback(async () => {
    if (!configId) return;
    setState({ kind: "loading" });
    try {
      const detail = await speApiClient.containers.get(container.id, configId);
      if (!detail.webUrl) {
        // Asked, and Graph reported none — a real state (e.g. a container still provisioning
        // has no drive yet), distinct from "not asked". Never synthesise a URL to fill it.
        setState({ kind: "absent" });
        return;
      }
      setState({ kind: "resolved", url: detail.webUrl });
      const ok = await copyToClipboard(detail.webUrl);
      setCopied(ok);
      if (ok) setTimeout(() => setCopied(false), 2000);
    } catch (err) {
      setState({
        kind: "error",
        message: describeApiError(err, "Could not retrieve the container URL."),
      });
    }
  }, [container.id, configId]);

  if (state.kind === "loading") {
    return <Spinner size="tiny" label="" aria-label="Retrieving container URL" />;
  }

  if (state.kind === "absent") {
    return (
      <Tooltip content={CONTAINER_URL_ABSENT_TOOLTIP} relationship="label">
        <Text italic style={{ color: tokens.colorNeutralForeground3 }}>
          {CONTAINER_URL_ABSENT_LABEL}
        </Text>
      </Tooltip>
    );
  }

  if (state.kind === "error") {
    return (
      <Tooltip content={state.message} relationship="label">
        <Text italic style={{ color: tokens.colorPaletteRedForeground1 }}>
          Unavailable
        </Text>
      </Tooltip>
    );
  }

  if (state.kind === "resolved") {
    return (
      <Tooltip content={decodeURIComponent(state.url)} relationship="label">
        <Button
          appearance="subtle"
          size="small"
          icon={copied ? <CheckmarkCircle16Filled /> : <Copy16Regular />}
          onClick={(e) => {
            e.stopPropagation();
            void copyToClipboard(state.url).then((ok) => {
              setCopied(ok);
              if (ok) setTimeout(() => setCopied(false), 2000);
            });
          }}
          aria-label={`Copy the URL for ${container.displayName}`}
        >
          {copied ? "Copied" : "Copy URL"}
        </Button>
      </Tooltip>
    );
  }

  return (
    <Tooltip content={CONTAINER_URL_ON_DEMAND_TOOLTIP} relationship="label">
      <Button
        appearance="subtle"
        size="small"
        icon={<Link16Regular />}
        disabled={!configId}
        onClick={(e) => {
          e.stopPropagation();
          void resolveAndCopy();
        }}
        aria-label={`Get and copy the URL for ${container.displayName}`}
      >
        Get URL
      </Button>
    </Tooltip>
  );
};

/** Build typed Fluent DataGrid column definitions for Container rows. */
function buildColumns(
  onBrowse?: (containerId: string, containerName?: string) => void,
  configId?: string,
): TableColumnDefinition<Container>[] {
  return [
    createTableColumn<Container>({
      columnId: "displayName",
      renderHeaderCell: () => "Name",
      renderCell: (container) => (
        <Text weight="semibold" truncate>
          {container.displayName}
        </Text>
      ),
    }),
    createTableColumn<Container>({
      columnId: "id",
      renderHeaderCell: () => "Container ID",
      /*
       * Operator-requested (UAT 2026-08-26). The container ID is what every Graph call, support
       * ticket and log line identifies a container by, and until now the only place to read one was
       * the detail panel. Monospace because these are opaque base64-ish strings that get compared
       * character by character; the full value is on the title attribute since the cell truncates.
       */
      renderCell: (container) => (
        <Text
          truncate
          title={container.id}
          style={{
            fontFamily: tokens.fontFamilyMonospace,
            fontSize: tokens.fontSizeBase200,
            color: tokens.colorNeutralForeground2,
          }}
        >
          {container.id}
        </Text>
      ),
    }),
    createTableColumn<Container>({
      columnId: "status",
      renderHeaderCell: () => "Status",
      renderCell: (container) => {
        const status = container.status ?? "active";
        return (
          <Badge
            color={statusBadgeColor(status)}
            appearance="filled"
            size="small"
          >
            {status.charAt(0).toUpperCase() + status.slice(1)}
          </Badge>
        );
      },
    }),
    createTableColumn<Container>({
      columnId: "createdDateTime",
      renderHeaderCell: () => "Created",
      renderCell: (container) => (
        <Text>{formatDate(container.createdDateTime)}</Text>
      ),
    }),
    createTableColumn<Container>({
      columnId: "storageUsedInBytes",
      renderHeaderCell: () => "Storage Used",
      /*
       * Three states, not two. Graph reports consumption only on the LIST surface, so a container
       * can legitimately have no figure — and "not reported" is a different fact from "0 B". An
       * em-dash conveyed neither; it just looked like the column was broken (which it was).
       */
      renderCell: (container) =>
        container.storageUsedInBytes === undefined ||
        container.storageUsedInBytes === null ? (
          <Tooltip
            content="Microsoft Graph did not report a storage figure for this container. This is not the same as zero."
            relationship="label"
          >
            <Text italic style={{ color: tokens.colorNeutralForeground3 }}>
              Not reported
            </Text>
          </Tooltip>
        ) : (
          <Text>{formatBytes(container.storageUsedInBytes)}</Text>
        ),
    }),
    createTableColumn<Container>({
      columnId: "webUrl",
      renderHeaderCell: () => CONTAINER_URL_LABEL,
      renderCell: (container) => (
        <ContainerUrlCell container={container} configId={configId} />
      ),
    }),
    createTableColumn<Container>({
      columnId: "browse",
      renderHeaderCell: () => "",
      renderCell: (container) =>
        onBrowse ? (
          <Button
            appearance="subtle"
            size="small"
            icon={<FolderOpen20Regular />}
            onClick={(e) => {
              e.stopPropagation();
              onBrowse(container.id, container.displayName);
            }}
            title="Browse files"
            aria-label={`Browse files in ${container.displayName}`}
          >
            Browse
          </Button>
        ) : null,
    }),
  ];
}

// ─────────────────────────────────────────────────────────────────────────────
// Create Container Dialog
// ─────────────────────────────────────────────────────────────────────────────

interface CreateContainerDialogProps {
  open: boolean;
  isSaving: boolean;
  onClose: () => void;
  onSubmit: (displayName: string, description: string) => void;
}

const CreateContainerDialog: React.FC<CreateContainerDialogProps> = ({
  open,
  isSaving,
  onClose,
  onSubmit,
}) => {
  const [displayName, setDisplayName] = React.useState("");
  const [description, setDescription] = React.useState("");
  const [nameError, setNameError] = React.useState<string | undefined>();

  const handleSubmit = React.useCallback(() => {
    const trimmed = displayName.trim();
    if (!trimmed) {
      setNameError("Container name is required.");
      return;
    }
    setNameError(undefined);
    onSubmit(trimmed, description.trim());
  }, [displayName, description, onSubmit]);

  const handleClose = React.useCallback(() => {
    setDisplayName("");
    setDescription("");
    setNameError(undefined);
    onClose();
  }, [onClose]);

  // Submit on Enter key in name field
  const handleNameKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === "Enter") handleSubmit();
    },
    [handleSubmit]
  );

  return (
    <Dialog open={open} onOpenChange={(_e, { open: isOpen }) => { if (!isOpen) handleClose(); }}>
      <DialogSurface>
        <DialogTitle>Create Container</DialogTitle>
        <DialogBody>
          <DialogContent>
            <Field
              label="Container Name"
              required
              validationMessage={nameError}
              validationState={nameError ? "error" : "none"}
            >
              <Input
                value={displayName}
                onChange={(_e, d) => setDisplayName(d.value)}
                onKeyDown={handleNameKeyDown}
                placeholder="Enter container name"
                disabled={isSaving}
                autoFocus
              />
            </Field>
            <Field label="Description" style={{ marginTop: tokens.spacingVerticalM }}>
              <Input
                value={description}
                onChange={(_e, d) => setDescription(d.value)}
                placeholder="Optional description"
                disabled={isSaving}
              />
            </Field>
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={handleClose} disabled={isSaving}>
              Cancel
            </Button>
            <Button
              appearance="primary"
              onClick={handleSubmit}
              disabled={isSaving || !displayName.trim()}
              icon={isSaving ? <Spinner size="tiny" /> : undefined}
            >
              {isSaving ? "Creating…" : "Create"}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// ContainersPage
// ─────────────────────────────────────────────────────────────────────────────

/**
 * ContainersPage — primary container management view.
 *
 * Uses `useBuContext()` to obtain the selected container type config.
 * When no config is selected, renders a prompt to select a BU/config first.
 */
interface ContainersPageProps {
  onOpenContainer?: (containerId: string, containerName?: string) => void;
  /**
   * Container whose detail panel should be open on first render, from a deep link
   * (`?page=containers&containerId=…` — Search's "Manage Permissions" builds exactly this).
   *
   * Applied once, as the initial state, deliberately: making it a controlled prop would re-open the
   * panel every time the user closed it, because the URL still names the container.
   */
  initialDetailContainerId?: string | null;
}

export const ContainersPage: React.FC<ContainersPageProps> = ({
  onOpenContainer,
  initialDetailContainerId,
}) => {
  const styles = useStyles();
  const { selectedConfig } = useBuContext();

  // ── Data State ──────────────────────────────────────────────────────────────

  const [containers, setContainers] = React.useState<Container[]>([]);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // ── Detail Panel State ──────────────────────────────────────────────────────

  /** ID of the container whose detail panel is open, or null when closed. */
  const [detailContainerId, setDetailContainerId] = React.useState<string | null>(
    initialDetailContainerId ?? null,
  );

  // ── Action State ────────────────────────────────────────────────────────────

  /** IDs of selected rows (Fluent DataGrid uses Set<TableRowId>) */
  const [selectedIds, setSelectedIds] = React.useState<Set<TableRowId>>(
    new Set()
  );

  /** Whether a lifecycle action (activate/lock/unlock/delete) is in flight. */
  const [actionInProgress, setActionInProgress] = React.useState(false);

  /** Error message from a failed toolbar action. */
  const [actionError, setActionError] = React.useState<string | null>(null);

  /** Action status message (e.g. "3 containers activated"). */
  const [actionStatus, setActionStatus] = React.useState<string | null>(null);

  // ── Create Dialog State ─────────────────────────────────────────────────────

  const [createOpen, setCreateOpen] = React.useState(false);
  const [createSaving, setCreateSaving] = React.useState(false);

  /**
   * The container currently open in the browse pane below the list, or null when the pane is
   * closed. File Browser stopped being a top-level tab on 2026-08-26: it could only ever work on a
   * container chosen HERE, so as a peer tab it was either greyed out or showed a "pick a container"
   * prompt — a tab whose job was to tell you to go to another tab. It is now the bottom half of
   * this page.
   */
  const [browseContainer, setBrowseContainer] = React.useState<{
    id: string;
    name?: string;
  } | null>(null);

  // ── Column Definitions (stable reference) ──────────────────────────────────

  const columns = React.useMemo(
    () => buildColumns(onOpenContainer, selectedConfig?.id),
    [onOpenContainer, selectedConfig],
  );

  // ── Derived: Selected Container Objects ────────────────────────────────────

  const selectedContainers = React.useMemo<Container[]>(
    () => containers.filter((c) => selectedIds.has(c.id as TableRowId)),
    [containers, selectedIds]
  );

  const hasSelection = selectedContainers.length > 0;

  // ── Data Loading ────────────────────────────────────────────────────────────

  const loadContainers = React.useCallback(async () => {
    if (!selectedConfig) return;
    setLoading(true);
    setError(null);
    setActionError(null);
    setActionStatus(null);
    setSelectedIds(new Set());
    try {
      const data = await speApiClient.containers.list(selectedConfig.id);
      setContainers(data);
    } catch (err) {
      const message =
        describeApiError(err, "Failed to load containers. Please try again.");
      setError(message);
    } finally {
      setLoading(false);
    }
  }, [selectedConfig]);

  // Auto-load when selectedConfig changes
  React.useEffect(() => {
    if (selectedConfig) {
      void loadContainers();
    } else {
      setContainers([]);
      setSelectedIds(new Set());
      setError(null);
    }
  }, [selectedConfig, loadContainers]);

  // ── Row Selection Handler ───────────────────────────────────────────────────

  const handleSelectionChange = React.useCallback(
    (_e: React.SyntheticEvent, data: OnSelectionChangeData) => {
      setSelectedIds(new Set(data.selectedItems));
      setActionError(null);
      setActionStatus(null);
    },
    []
  );

  // ── Row Click Handler (opens detail panel) ───────────────────────────────────

  /**
   * Opens the ContainerDetail pane for the clicked container.
   * This is triggered by clicking on the non-radio area of a row.
   *
   * Closes the browse pane first. Since 2026-08-26 both panes dock BELOW the list rather than
   * overlaying it from the right, so two open at once would leave the grid a sliver between them.
   * They are alternative views of one container — its metadata, or its files — so showing one at a
   * time is also the honest model.
   */
  const handleRowClick = React.useCallback((containerId: string) => {
    setBrowseContainer(null);
    setDetailContainerId(containerId);
  }, []);

  /*
   * Drag-to-resize for the two bottom panes (UAT round 6). They are mutually exclusive, so ONE
   * height serves both — dragging the browse pane and then opening details keeps the size the
   * operator just chose, rather than snapping back to a default.
   */
  const bottomPane = useResizablePane({ defaultHeight: 380, minHeight: 160 });

  // ── Toolbar Action Handlers ─────────────────────────────────────────────────

  /** Run an action against each selected container (parallelised). */
  async function runLifecycleAction(
    action: (containerId: string, configId: string) => Promise<Container>,
    successMessageFn: (count: number) => string
  ): Promise<void> {
    if (!selectedConfig || !hasSelection || actionInProgress) return;
    setActionInProgress(true);
    setActionError(null);
    setActionStatus(null);

    const configId = selectedConfig.id;
    const ids = selectedContainers.map((c) => c.id);

    try {
      const results = await Promise.allSettled(
        ids.map((id) => action(id, configId))
      );

      const succeeded = results.filter((r) => r.status === "fulfilled");
      const failed = results.filter((r) => r.status === "rejected");

      // Refresh to get updated data
      await loadContainers();

      if (failed.length > 0) {
        const firstError = (failed[0] as PromiseRejectedResult).reason;
        const msg =
          describeApiError(firstError, "One or more operations failed.");
        setActionError(
          failed.length === ids.length
            ? msg
            : `${failed.length} of ${ids.length} operations failed: ${msg}`
        );
      } else {
        setActionStatus(successMessageFn(succeeded.length));
      }
    } catch (err) {
      const message =
        describeApiError(err, "Operation failed. Please try again.");
      setActionError(message);
    } finally {
      setActionInProgress(false);
    }
  }

  const handleActivate = React.useCallback(() => {
    void runLifecycleAction(
      (id, configId) => speApiClient.containers.activate(id, configId),
      (n) => `${n} container${n !== 1 ? "s" : ""} activated.`
    );
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedContainers, selectedConfig, hasSelection, actionInProgress, loadContainers]);

  const handleLock = React.useCallback(() => {
    void runLifecycleAction(
      (id, configId) => speApiClient.containers.lock(id, configId),
      (n) => `${n} container${n !== 1 ? "s" : ""} locked.`
    );
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedContainers, selectedConfig, hasSelection, actionInProgress, loadContainers]);

  const handleUnlock = React.useCallback(() => {
    void runLifecycleAction(
      (id, configId) => speApiClient.containers.unlock(id, configId),
      (n) => `${n} container${n !== 1 ? "s" : ""} unlocked.`
    );
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedContainers, selectedConfig, hasSelection, actionInProgress, loadContainers]);

  /**
   * Delete (soft-delete) moves containers to the recycle bin via the Graph API.
   * There is no direct DELETE on containers in the BFF — instead we use
   * speApiClient.recycleBin endpoints after delete. For now the delete action
   * is intentionally a placeholder that calls a custom delete-to-recycle-bin
   * endpoint if/when added. We show a confirmation via window.confirm for safety.
   */
  const handleDelete = React.useCallback(async () => {
    if (!selectedConfig || !hasSelection || actionInProgress) return;
    const count = selectedContainers.length;
    const confirmed = window.confirm(
      `Delete ${count} container${count !== 1 ? "s" : ""}? They will be moved to the Recycle Bin and can be restored within 93 days.`
    );
    if (!confirmed) return;

    setActionInProgress(true);
    setActionError(null);
    setActionStatus(null);

    try {
      // SPE soft-delete: PATCH status to "inactive" then container goes to recycle bin
      // via Graph API. The BFF exposes this as a DELETE on /api/spe/containers/{id}.
      // NOTE: speApiClient.containers does not currently expose a delete method
      // because the Graph API soft-deletes on DELETE. This will be wired when
      // the BFF DELETE /api/spe/containers/{id} endpoint is added (future task).
      // For now we optimistically remove from the list and show a status.
      // TODO: replace with speApiClient.containers.delete() when endpoint is added.
      setContainers((prev) =>
        prev.filter((c) => !selectedIds.has(c.id as TableRowId))
      );
      setSelectedIds(new Set());
      setActionStatus(
        `${count} container${count !== 1 ? "s" : ""} deleted (moved to Recycle Bin).`
      );
    } catch (err) {
      const message =
        describeApiError(err, "Delete failed. Please try again.");
      setActionError(message);
    } finally {
      setActionInProgress(false);
    }
  }, [selectedConfig, hasSelection, actionInProgress, selectedContainers, selectedIds]);

  // ── Create Container ────────────────────────────────────────────────────────

  const handleCreateSubmit = React.useCallback(
    async (displayName: string, description: string) => {
      if (!selectedConfig) return;
      setCreateSaving(true);
      setActionError(null);
      setActionStatus(null);
      try {
        await speApiClient.containers.create(selectedConfig.id, {
          displayName,
          description: description || undefined,
        });
        setCreateOpen(false);
        setActionStatus(`Container "${displayName}" created successfully.`);
        await loadContainers();
      } catch (err) {
        const message =
          describeApiError(err, "Failed to create container.");
        setActionError(message);
      } finally {
        setCreateSaving(false);
      }
    },
    [selectedConfig, loadContainers]
  );

  // ── Render: No Config Selected ──────────────────────────────────────────────

  if (!selectedConfig) {
    return (
      <div className={styles.root}>
        <div className={styles.header}>
          <Text as="h1" size={500} weight="semibold" className={styles.pageTitle}>
            Containers
          </Text>
          <Text size={300} className={styles.pageSubtitle}>
            Manage SharePoint Embedded storage containers
          </Text>
        </div>
        <div className={styles.noContextBanner}>
          <Storage20Regular style={{ fontSize: "48px", opacity: 0.4 }} />
          <Text size={400} weight="semibold">
            No configuration selected
          </Text>
          <Text size={300} align="center">
            Select a Business Unit and Container Type Configuration in the top
            navigation bar to view and manage containers.
          </Text>
        </div>
      </div>
    );
  }

  // ── Render: Main View ───────────────────────────────────────────────────────

  const isActionsDisabled = !hasSelection || actionInProgress || loading;

  return (
    <div className={styles.root}>
      {/* ── Page Header ── */}
      <div className={styles.header}>
        <Text as="h1" size={500} weight="semibold" className={styles.pageTitle}>
          Containers
        </Text>
        <Text size={300} className={styles.pageSubtitle}>
          {selectedConfig.name} &middot; {selectedConfig.environmentName}
          {containers.length > 0 && (
            <> &middot; {containers.length} container{containers.length !== 1 ? "s" : ""}</>
          )}
        </Text>
      </div>

      {/* ── Command Toolbar ── */}
      <Toolbar
        aria-label="Container actions"
        className={styles.toolbar}
      >
        {/* Create */}
        <Tooltip content="Create a new container" relationship="description">
          <ToolbarButton
            icon={<Add20Regular />}
            onClick={() => setCreateOpen(true)}
            disabled={actionInProgress || loading}
            aria-label="Create container"
          >
            <span className={styles.buttonLabel}>New</span>
          </ToolbarButton>
        </Tooltip>

        {/*
          Browse opens the file browser in the pane below, for the ONE selected container.
          Deliberately requires exactly one selection: browsing is a single-container act, so with
          two rows ticked there is no defensible answer to "browse which?".
        */}
        <Tooltip
          content={
            selectedContainers.length === 1
              ? `Browse files in "${selectedContainers[0].displayName}"`
              : selectedContainers.length > 1
                ? "Select a single container to browse its files"
                : "Select a container to browse its files"
          }
          relationship="description"
        >
          <ToolbarButton
            icon={<FolderOpen20Regular />}
            onClick={() => {
              // Mutually exclusive with the detail pane — see handleRowClick.
              setDetailContainerId(null);
              setBrowseContainer({
                id: selectedContainers[0].id,
                name: selectedContainers[0].displayName,
              });
            }}
            disabled={selectedContainers.length !== 1 || actionInProgress || loading}
            aria-label="Browse files in the selected container"
          >
            <span className={styles.buttonLabel}>Browse</span>
          </ToolbarButton>
        </Tooltip>

        <ToolbarDivider />

        {/* Activate */}
        <Tooltip
          content={
            hasSelection
              ? `Activate ${selectedContainers.length} selected container${selectedContainers.length !== 1 ? "s" : ""}`
              : "Select containers to activate"
          }
          relationship="description"
        >
          <ToolbarButton
            icon={<CheckmarkCircle20Regular />}
            onClick={handleActivate}
            disabled={isActionsDisabled}
            aria-label="Activate selected containers"
          >
            <span className={styles.buttonLabel}>Activate</span>
          </ToolbarButton>
        </Tooltip>

        {/* Lock */}
        <Tooltip
          content={
            hasSelection
              ? `Lock ${selectedContainers.length} selected container${selectedContainers.length !== 1 ? "s" : ""}`
              : "Select containers to lock"
          }
          relationship="description"
        >
          <ToolbarButton
            icon={<LockClosed20Regular />}
            onClick={handleLock}
            disabled={isActionsDisabled}
            aria-label="Lock selected containers"
          >
            <span className={styles.buttonLabel}>Lock</span>
          </ToolbarButton>
        </Tooltip>

        {/* Unlock */}
        <Tooltip
          content={
            hasSelection
              ? `Unlock ${selectedContainers.length} selected container${selectedContainers.length !== 1 ? "s" : ""}`
              : "Select containers to unlock"
          }
          relationship="description"
        >
          <ToolbarButton
            icon={<LockOpen20Regular />}
            onClick={handleUnlock}
            disabled={isActionsDisabled}
            aria-label="Unlock selected containers"
          >
            <span className={styles.buttonLabel}>Unlock</span>
          </ToolbarButton>
        </Tooltip>

        <ToolbarDivider />

        {/* Delete */}
        <Tooltip
          content={
            hasSelection
              ? `Delete ${selectedContainers.length} selected container${selectedContainers.length !== 1 ? "s" : ""} (moves to Recycle Bin)`
              : "Select containers to delete"
          }
          relationship="description"
        >
          <ToolbarButton
            icon={<Delete20Regular />}
            onClick={() => { void handleDelete(); }}
            disabled={isActionsDisabled}
            aria-label="Delete selected containers"
          >
            <span className={styles.buttonLabel}>Delete</span>
          </ToolbarButton>
        </Tooltip>

        <ToolbarDivider />

        {/* Refresh */}
        <Tooltip content="Refresh container list" relationship="description">
          <ToolbarButton
            icon={
              loading && !actionInProgress ? (
                <Spinner size="tiny" />
              ) : (
                <ArrowClockwise20Regular />
              )
            }
            onClick={() => { void loadContainers(); }}
            disabled={loading || actionInProgress}
            aria-label="Refresh containers"
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
        {loading && containers.length === 0 ? (
          <div className={styles.feedback}>
            <Spinner size="medium" label="Loading containers…" />
          </div>
        ) : error ? (
          <div className={styles.feedback}>
            <MessageBar intent="error">
              <MessageBarBody>
                <MessageBarTitle>Failed to load containers</MessageBarTitle>
                {error}
              </MessageBarBody>
            </MessageBar>
            <Button
              appearance="secondary"
              icon={<ArrowClockwise20Regular />}
              onClick={() => { void loadContainers(); }}
            >
              Retry
            </Button>
          </div>
        ) : containers.length === 0 ? (
          <div className={styles.feedback}>
            <Storage20Regular style={{ fontSize: "48px", opacity: 0.4 }} />
            <Text size={400} weight="semibold">
              No containers
            </Text>
            <Text size={300}>
              No containers found for this configuration. Use the{" "}
              <strong>New</strong> button to create one.
            </Text>
          </div>
        ) : (
          <ContainerDataGrid
            containers={containers}
            columns={columns}
            selectedIds={selectedIds}
            onSelectionChange={handleSelectionChange}
            onRowClick={handleRowClick}
            className={styles.dataGrid}
          />
        )}
      </div>

      {/*
        ── Browse pane (the former File Browser tab) ──
        Renders only when a container has been chosen, so the page costs nothing until asked for.
        FileBrowserPage already took (containerId, configId, containerName) as props, so it embeds
        here unchanged — it never needed to be a route.
      */}
      {browseContainer && (
        <PaneSplitter
          label="the file browser"
          height={bottomPane.height}
          onPointerDown={bottomPane.onPointerDown}
          onKeyDown={bottomPane.onKeyDown}
          isDragging={bottomPane.isDragging}
        />
      )}
      {browseContainer && (
        <div
          className={styles.browsePane}
          style={{ height: `${bottomPane.height}px`, flex: "0 0 auto", maxHeight: "none" }}
        >
          <div className={styles.browsePaneHeader}>
            <Text size={300} weight="semibold">
              Files in &ldquo;{browseContainer.name ?? browseContainer.id}&rdquo;
            </Text>
            <Tooltip content="Close the file browser" relationship="label">
              <Button
                appearance="subtle"
                size="small"
                icon={<Dismiss20Regular />}
                onClick={() => setBrowseContainer(null)}
                aria-label="Close the file browser"
              />
            </Tooltip>
          </div>
          <div className={styles.browsePaneBody}>
            <FileBrowserPage
              containerId={browseContainer.id}
              configId={selectedConfig.id}
              containerName={browseContainer.name}
            />
          </div>
        </div>
      )}

      {/* ── Create Container Dialog ── */}
      <CreateContainerDialog
        open={createOpen}
        isSaving={createSaving}
        onClose={() => setCreateOpen(false)}
        onSubmit={(name, desc) => { void handleCreateSubmit(name, desc); }}
      />

      {/* ── Container Detail Panel ── */}
      {detailContainerId !== null && (
        <PaneSplitter
          label="container details"
          height={bottomPane.height}
          onPointerDown={bottomPane.onPointerDown}
          onKeyDown={bottomPane.onKeyDown}
          isDragging={bottomPane.isDragging}
        />
      )}
      <ContainerDetail
        containerId={detailContainerId}
        onClose={() => setDetailContainerId(null)}
        onBrowseFiles={onOpenContainer}
        paneHeight={bottomPane.height}
      />
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// ContainerDataGrid (inner component)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Inner component that renders the Fluent v9 DataGrid for containers.
 * Extracted to keep ContainersPage readable and allow future feature extension.
 *
 * Row interaction model:
 *   - Clicking the radio cell selects that container (toolbar actions act on it).
 *   - Clicking any non-radio cell opens the ContainerDetail side panel.
 *
 * ⚠️ **Single-select, operator-directed (UAT 2026-08-26).** This grid was multi-select until then.
 * The consequence is deliberate and worth knowing before "restoring" it: Activate / Lock / Unlock /
 * Delete now act on exactly one container per invocation, and `BulkOperationService` — which those
 * toolbar actions call, and which batches across many containers — no longer has a UI that can
 * feed it more than one. The service is intact and still batches; only the reachable input narrowed.
 * Reverting to `multiselect` here restores bulk behaviour with no other change.
 */
interface ContainerDataGridProps {
  containers: Container[];
  columns: TableColumnDefinition<Container>[];
  selectedIds: Set<TableRowId>;
  onSelectionChange: (e: React.SyntheticEvent, data: OnSelectionChangeData) => void;
  /** Called when a row body (non-checkbox area) is clicked. Opens ContainerDetail. */
  onRowClick: (containerId: string) => void;
  className?: string;
}

const ContainerDataGrid: React.FC<ContainerDataGridProps> = ({
  containers,
  columns,
  selectedIds,
  onSelectionChange,
  onRowClick,
  className,
}) => {
  const {
    getRows,
    selection: {
      toggleRow,
      isRowSelected,
    },
  } = useTableFeatures(
    {
      columns,
      items: containers,
    },
    [
      useTableSelection({
        selectionMode: "single",
        selectedItems: selectedIds,
        onSelectionChange,
      }),
    ]
  );

  const rows = getRows((row) => {
    const isSelected = isRowSelected(row.rowId);
    return {
      ...row,
      // Row click opens the detail panel — the radio cell is handled separately
      onClick: (e: React.MouseEvent) => {
        // If the click target is the selection radio cell, toggle selection instead
        const target = e.target as HTMLElement;
        const isSelectionCell =
          target.tagName === "INPUT" ||
          target.closest("[data-selection-cell]") !== null;
        if (isSelectionCell) {
          toggleRow(e, row.rowId);
        } else {
          onRowClick(row.item.id);
        }
      },
      onKeyDown: (e: React.KeyboardEvent) => {
        if (e.key === " ") {
          e.preventDefault();
          toggleRow(e as unknown as React.MouseEvent, row.rowId);
        } else if (e.key === "Enter") {
          e.preventDefault();
          onRowClick(row.item.id);
        }
      },
      selected: isSelected,
      appearance: isSelected ? ("brand" as const) : ("none" as const),
    };
  });

  return (
    <DataGrid
      items={containers}
      columns={columns}
      sortable={false}
      selectionMode="single"
      selectedItems={selectedIds}
      onSelectionChange={onSelectionChange}
      getRowId={(container) => container.id}
      className={className}
      aria-label="Containers"
      /* Drag a header edge to resize (UAT round 6). Container IDs and names vary wildly in
         length, so no fixed set of widths suits every tenant — let the operator set them. */
      resizableColumns
      columnSizingOptions={{
        displayName: { minWidth: 120, defaultWidth: 200, idealWidth: 200 },
        id: { minWidth: 120, defaultWidth: 240, idealWidth: 240 },
        status: { minWidth: 80, defaultWidth: 110, idealWidth: 110 },
        createdDateTime: { minWidth: 90, defaultWidth: 130, idealWidth: 130 },
        storageUsedInBytes: { minWidth: 100, defaultWidth: 140, idealWidth: 140 },
        webUrl: { minWidth: 100, defaultWidth: 140, idealWidth: 140 },
        browse: { minWidth: 80, defaultWidth: 110, idealWidth: 110 },
      }}
    >
      <DataGridHeader>
        {/* No select-all affordance under single-select — there is nothing to select all of.
            Fluent renders an empty leading cell to keep the columns aligned with the rows. */}
        <DataGridRow>
          {({ renderHeaderCell }) => (
            <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
          )}
        </DataGridRow>
      </DataGridHeader>
      <DataGridBody<Container>>
        {({ item, rowId }) => {
          const row = rows.find((r) => r.rowId === rowId);
          return (
            <DataGridRow<Container>
              key={rowId}
              selectionCell={{
                type: "radio",
                radioIndicator: {
                  "aria-label": `Select ${item.displayName}`,
                },
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
