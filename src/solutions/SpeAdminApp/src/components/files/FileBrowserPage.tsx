import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Toolbar,
  ToolbarButton,
  ToolbarDivider,
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbButton,
  BreadcrumbDivider,
  DataGrid,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridBody,
  DataGridRow,
  DataGridCell,
  createTableColumn,
  TableColumnDefinition,
  Spinner,
  MessageBar,
  MessageBarBody,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  DialogTrigger,
  Input,
  Field,
  Tooltip,
  Badge,
} from "@fluentui/react-components";
import {
  ArrowUploadRegular,
  FolderAddRegular,
  DeleteRegular,
  ArrowDownloadRegular,
  ArrowLeft20Regular,
  Document20Regular,
  Folder20Regular,
  ArrowClockwise20Regular,
} from "@fluentui/react-icons";
import { FileUploadZone } from "@spaarke/ui-components";
import type { IUploadedFile, IFileValidationError } from "@spaarke/ui-components";
import { speApiClient } from "../../services/speApiClient";
import type { DriveItem } from "../../types/spe";

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021: makeStyles + Fluent design tokens; dark mode automatic)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
  },

  // ── Page Header ────────────────────────────────────────────────────────────

  pageHeader: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    flexShrink: 0,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
  },

  pageTitle: {
    color: tokens.colorNeutralForeground1,
  },

  containerLabel: {
    color: tokens.colorNeutralForeground3,
  },

  // ── Toolbar ────────────────────────────────────────────────────────────────

  toolbar: {
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    flexShrink: 0,
    backgroundColor: tokens.colorNeutralBackground1,
  },

  // ── Breadcrumb Bar ─────────────────────────────────────────────────────────

  breadcrumbBar: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    flexShrink: 0,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
  },

  // ── Content Area ──────────────────────────────────────────────────────────

  contentArea: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
  },

  gridArea: {
    flex: "1 1 auto",
    overflowY: "auto",
    overflowX: "auto",
  },

  // ── Drop Zone ──────────────────────────────────────────────────────────────

  uploadSection: {
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    flexShrink: 0,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
  },

  uploadLabel: {
    color: tokens.colorNeutralForeground3,
    display: "block",
    marginBottom: tokens.spacingVerticalS,
  },

  uploadErrors: {
    marginTop: tokens.spacingVerticalS,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
  },

  // ── Status / Empty States ──────────────────────────────────────────────────

  centered: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    flex: "1 1 auto",
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
  },

  folderNameCell: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },

  fileNameCell: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground1,
  },

  selectedRow: {
    backgroundColor: tokens.colorBrandBackground2,
  },

  uploadingIndicator: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground2,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/** A breadcrumb segment in the folder navigation path. */
interface BreadcrumbSegment {
  /** Graph item ID for the folder (undefined = root). */
  id: string | undefined;
  /** Display label shown in the breadcrumb. */
  label: string;
}

/** Props for the FileBrowserPage component. */
export interface FileBrowserPageProps {
  /**
   * The SPE container ID to browse.
   * If undefined, the page shows a "select a container" prompt.
   */
  containerId: string | undefined;
  /**
   * The container type config ID — required for all API calls.
   */
  configId: string | undefined;
  /**
   * Optional display name for the container (shown in the header).
   */
  containerName?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Utilities
// ─────────────────────────────────────────────────────────────────────────────

/** Format byte count as human-readable string. */
function formatFileSize(bytes: number | undefined): string {
  if (bytes === undefined) return "—";
  if (bytes === 0) return "0 B";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024)
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

/** Format ISO date string to a short locale date+time. */
function formatDate(iso: string | undefined): string {
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

/** Returns true if a DriveItem represents a folder. */
function isFolder(item: DriveItem): boolean {
  return !!item.folder;
}

// ─────────────────────────────────────────────────────────────────────────────
// Column definitions for the Fluent v9 DataGrid
// ─────────────────────────────────────────────────────────────────────────────

const gridColumns: TableColumnDefinition<DriveItem>[] = [
  createTableColumn<DriveItem>({
    columnId: "name",
    compare: (a, b) => {
      // Folders sort before files, then alphabetically by name
      const aIsFolder = isFolder(a) ? 0 : 1;
      const bIsFolder = isFolder(b) ? 0 : 1;
      if (aIsFolder !== bIsFolder) return aIsFolder - bIsFolder;
      return a.name.localeCompare(b.name);
    },
    renderHeaderCell: () => "Name",
    renderCell: (item) => item.name, // placeholder — actual rendering in body
  }),
  createTableColumn<DriveItem>({
    columnId: "type",
    compare: (a, b) => {
      const aType = isFolder(a) ? "Folder" : (a.file?.mimeType ?? "File");
      const bType = isFolder(b) ? "Folder" : (b.file?.mimeType ?? "File");
      return aType.localeCompare(bType);
    },
    renderHeaderCell: () => "Type",
    renderCell: (item) => (isFolder(item) ? "Folder" : "File"),
  }),
  createTableColumn<DriveItem>({
    columnId: "size",
    compare: (a, b) => (a.size ?? 0) - (b.size ?? 0),
    renderHeaderCell: () => "Size",
    renderCell: (item) => formatFileSize(item.size),
  }),
  createTableColumn<DriveItem>({
    columnId: "modified",
    compare: (a, b) =>
      new Date(a.lastModifiedDateTime).getTime() -
      new Date(b.lastModifiedDateTime).getTime(),
    renderHeaderCell: () => "Modified",
    renderCell: (item) => formatDate(item.lastModifiedDateTime),
  }),
  createTableColumn<DriveItem>({
    columnId: "modifiedBy",
    compare: (a, b) =>
      (a.lastModifiedBy?.user?.displayName ?? "").localeCompare(
        b.lastModifiedBy?.user?.displayName ?? ""
      ),
    renderHeaderCell: () => "Modified By",
    renderCell: (item) => item.lastModifiedBy?.user?.displayName ?? "—",
  }),
];

// ─────────────────────────────────────────────────────────────────────────────
// FileBrowserPage component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * FileBrowserPage — full file and folder browsing experience within an SPE container.
 *
 * Features:
 * - Breadcrumb navigation (root → folder → subfolder...)
 * - Sortable file/folder grid (Name, Type, Size, Modified, Modified By)
 * - Command toolbar: Upload, New Folder, Delete, Download, Refresh
 * - Single-click row selection; Ctrl+click multi-select
 * - Double-click folder navigation
 * - Drag-drop file upload via FileUploadZone (ADR-012)
 * - All operations wired to speApiClient
 *
 * ADR compliance:
 * - ADR-021: All UI uses Fluent v9 makeStyles + design tokens; dark mode automatic
 * - ADR-012: FileUploadZone reused from @spaarke/ui-components shared library
 * - ADR-006: Code Page (React 18 + bundled, not PCF)
 */
export const FileBrowserPage: React.FC<FileBrowserPageProps> = ({
  containerId,
  configId,
  containerName,
}) => {
  const styles = useStyles();

  // ── Navigation state ──────────────────────────────────────────────────────

  /**
   * Breadcrumb stack: [{id: undefined, label: "Root"}, {id: "abc", label: "Documents"}, ...]
   * The last entry is always the current folder.
   */
  const [breadcrumbs, setBreadcrumbs] = React.useState<BreadcrumbSegment[]>([
    { id: undefined, label: "Root" },
  ]);

  const currentFolderId = breadcrumbs[breadcrumbs.length - 1]?.id;

  // ── Data state ────────────────────────────────────────────────────────────

  const [items, setItems] = React.useState<DriveItem[]>([]);
  const [loading, setLoading] = React.useState(false);
  const [loadError, setLoadError] = React.useState<string | null>(null);

  // ── Selection state ───────────────────────────────────────────────────────

  const [selectedIds, setSelectedIds] = React.useState<Set<string>>(
    new Set()
  );

  // ── Upload state ──────────────────────────────────────────────────────────

  const [uploadErrors, setUploadErrors] = React.useState<
    IFileValidationError[]
  >([]);
  const [uploadingCount, setUploadingCount] = React.useState(0);

  // ── New Folder dialog state ───────────────────────────────────────────────

  const [newFolderOpen, setNewFolderOpen] = React.useState(false);
  const [newFolderName, setNewFolderName] = React.useState("");
  const [creatingFolder, setCreatingFolder] = React.useState(false);
  const [folderError, setFolderError] = React.useState<string | null>(null);

  // ── Delete dialog state ───────────────────────────────────────────────────

  const [deleteOpen, setDeleteOpen] = React.useState(false);
  const [deleting, setDeleting] = React.useState(false);

  // ── Load items ────────────────────────────────────────────────────────────

  const loadItems = React.useCallback(async () => {
    if (!containerId || !configId) return;

    setLoading(true);
    setLoadError(null);
    setSelectedIds(new Set());

    try {
      const result = await speApiClient.items.list(containerId, configId, {
        folderId: currentFolderId,
      });
      setItems(result);
    } catch (err) {
      const message =
        err instanceof Error ? err.message : "Failed to load items.";
      setLoadError(message);
    } finally {
      setLoading(false);
    }
  }, [containerId, configId, currentFolderId]);

  // Reload when container or current folder changes
  React.useEffect(() => {
    loadItems();
  }, [loadItems]);

  // ── Breadcrumb / folder navigation ───────────────────────────────────────

  /** Navigate into a folder (double-click). */
  const handleFolderOpen = React.useCallback((item: DriveItem) => {
    if (!isFolder(item)) return;
    setBreadcrumbs((prev) => [...prev, { id: item.id, label: item.name }]);
    setSelectedIds(new Set());
  }, []);

  /** Navigate to a breadcrumb segment by index. */
  const handleBreadcrumbNavigate = React.useCallback((index: number) => {
    setBreadcrumbs((prev) => prev.slice(0, index + 1));
    setSelectedIds(new Set());
  }, []);

  /** Navigate up one level. */
  const handleNavigateUp = React.useCallback(() => {
    setBreadcrumbs((prev) => (prev.length > 1 ? prev.slice(0, -1) : prev));
    setSelectedIds(new Set());
  }, []);

  // ── Row selection ─────────────────────────────────────────────────────────

  const handleRowClick = React.useCallback(
    (e: React.MouseEvent, item: DriveItem) => {
      if (e.ctrlKey || e.metaKey) {
        // Ctrl/Cmd+click: toggle this item in the selection
        setSelectedIds((prev) => {
          const next = new Set(prev);
          if (next.has(item.id)) {
            next.delete(item.id);
          } else {
            next.add(item.id);
          }
          return next;
        });
      } else {
        // Plain click: select only this item (or deselect if already selected alone)
        setSelectedIds((prev) => {
          if (prev.size === 1 && prev.has(item.id)) return new Set();
          return new Set([item.id]);
        });
      }
    },
    []
  );

  const handleRowDoubleClick = React.useCallback(
    (item: DriveItem) => {
      if (isFolder(item)) {
        handleFolderOpen(item);
      }
    },
    [handleFolderOpen]
  );

  // ── Upload ────────────────────────────────────────────────────────────────

  /**
   * Accept-all validation config for FileUploadZone.
   * SPE containers can hold any file type, not just PDF/DOCX/XLSX.
   * We override acceptedExtensions and customValidator to accept everything,
   * and raise the size limit to 250 MB.
   */
  const uploadValidationConfig = React.useMemo(
    () => ({
      acceptedExtensions: ["*"] as string[],
      inputAccept: "*/*",
      maxFileSizeBytes: 250 * 1024 * 1024, // 250 MB
      customValidator: (_file: File): null => null, // accept all
    }),
    []
  );

  const handleFilesAccepted = React.useCallback(
    async (accepted: IUploadedFile[]) => {
      if (!containerId || !configId) return;
      setUploadErrors([]);
      setUploadingCount((c) => c + accepted.length);

      const errors: IFileValidationError[] = [];

      await Promise.allSettled(
        accepted.map(async (uploadedFile) => {
          try {
            const formData = new FormData();
            formData.append("file", uploadedFile.file, uploadedFile.name);
            await speApiClient.items.upload(containerId, configId, formData, {
              folderId: currentFolderId,
            });
          } catch (err) {
            const reason =
              err instanceof Error ? err.message : "Upload failed.";
            errors.push({ fileName: uploadedFile.name, reason });
          } finally {
            setUploadingCount((c) => Math.max(0, c - 1));
          }
        })
      );

      if (errors.length > 0) {
        setUploadErrors(errors);
      }

      // Refresh grid after all uploads settle
      await loadItems();
    },
    [containerId, configId, currentFolderId, loadItems]
  );

  const handleValidationErrors = React.useCallback(
    (errors: IFileValidationError[]) => {
      setUploadErrors((prev) => [...prev, ...errors]);
    },
    []
  );

  // ── New Folder ────────────────────────────────────────────────────────────

  const handleCreateFolder = React.useCallback(async () => {
    if (!containerId || !configId || !newFolderName.trim()) return;

    setCreatingFolder(true);
    setFolderError(null);

    try {
      await speApiClient.items.createFolder(
        containerId,
        configId,
        { name: newFolderName.trim() },
        { parentId: currentFolderId }
      );
      setNewFolderOpen(false);
      setNewFolderName("");
      await loadItems();
    } catch (err) {
      setFolderError(
        err instanceof Error ? err.message : "Failed to create folder."
      );
    } finally {
      setCreatingFolder(false);
    }
  }, [containerId, configId, newFolderName, currentFolderId, loadItems]);

  const handleNewFolderClose = React.useCallback(() => {
    setNewFolderOpen(false);
    setNewFolderName("");
    setFolderError(null);
  }, []);

  // ── Delete ────────────────────────────────────────────────────────────────

  const handleDelete = React.useCallback(async () => {
    if (!containerId || !configId || selectedIds.size === 0) return;

    setDeleting(true);

    try {
      await Promise.all(
        Array.from(selectedIds).map((id) =>
          speApiClient.items.delete(containerId, configId, id)
        )
      );
      setDeleteOpen(false);
      setSelectedIds(new Set());
      await loadItems();
    } catch (err) {
      console.error("Delete failed:", err);
    } finally {
      setDeleting(false);
    }
  }, [containerId, configId, selectedIds, loadItems]);

  // ── Download ──────────────────────────────────────────────────────────────

  const handleDownload = React.useCallback(async () => {
    if (!containerId || !configId) return;

    const filesToDownload = items.filter(
      (item) => selectedIds.has(item.id) && !isFolder(item)
    );

    for (const item of filesToDownload) {
      try {
        const response = await speApiClient.items.download(
          containerId,
          configId,
          item.id
        );
        const blob = await response.blob();
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = item.name;
        document.body.appendChild(anchor);
        anchor.click();
        document.body.removeChild(anchor);
        URL.revokeObjectURL(url);
      } catch (err) {
        console.error(`Download failed for ${item.name}:`, err);
      }
    }
  }, [containerId, configId, selectedIds, items]);

  // ── Guard: no container selected ──────────────────────────────────────────

  if (!containerId || !configId) {
    return (
      <div className={styles.root}>
        <div className={styles.centered}>
          <Folder20Regular style={{ fontSize: "48px" }} />
          <Text size={400} weight="semibold">
            No container selected
          </Text>
          <Text size={300}>
            Navigate to the Containers page and open a container to browse its
            files.
          </Text>
        </div>
      </div>
    );
  }

  // ── Derived values ────────────────────────────────────────────────────────

  const isUploading = uploadingCount > 0;
  const hasSelection = selectedIds.size > 0;
  const selectedFiles = items.filter(
    (item) => selectedIds.has(item.id) && !isFolder(item)
  );
  const canDownload = selectedFiles.length > 0;

  // ─────────────────────────────────────────────────────────────────────────
  // Render
  // ─────────────────────────────────────────────────────────────────────────

  return (
    <div className={styles.root}>

      {/* ── Page Header ── */}
      <div className={styles.pageHeader}>
        <div>
          <Text size={500} weight="semibold" className={styles.pageTitle}>
            File Browser
          </Text>
          {containerName && (
            <Text size={300} className={styles.containerLabel} block>
              Container: {containerName}
            </Text>
          )}
        </div>
        <Tooltip content="Refresh" relationship="label">
          <Button
            appearance="subtle"
            icon={<ArrowClockwise20Regular />}
            onClick={loadItems}
            disabled={loading}
            aria-label="Refresh"
          />
        </Tooltip>
      </div>

      {/* ── Command Toolbar ── */}
      <Toolbar className={styles.toolbar} aria-label="File browser commands">
        <Tooltip content="Upload files to the current folder" relationship="label">
          <ToolbarButton
            icon={<ArrowUploadRegular />}
            disabled={isUploading}
            onClick={() => {
              // Scroll the FileUploadZone into view so the user can
              // drop or click it. The zone's built-in click handler
              // opens the file picker.
              document
                .getElementById("spe-file-browser-upload-zone")
                ?.scrollIntoView({ behavior: "smooth" });
            }}
          >
            Upload
          </ToolbarButton>
        </Tooltip>

        <Tooltip content="Create a new folder" relationship="label">
          <ToolbarButton
            icon={<FolderAddRegular />}
            onClick={() => setNewFolderOpen(true)}
          >
            New Folder
          </ToolbarButton>
        </Tooltip>

        <ToolbarDivider />

        <Tooltip
          content={
            canDownload
              ? `Download ${selectedFiles.length} file(s)`
              : "Select files to download"
          }
          relationship="label"
        >
          <ToolbarButton
            icon={<ArrowDownloadRegular />}
            disabled={!canDownload}
            onClick={handleDownload}
          >
            Download
          </ToolbarButton>
        </Tooltip>

        <Tooltip
          content={
            hasSelection
              ? `Delete ${selectedIds.size} item(s)`
              : "Select items to delete"
          }
          relationship="label"
        >
          <ToolbarButton
            icon={<DeleteRegular />}
            disabled={!hasSelection}
            onClick={() => setDeleteOpen(true)}
          >
            Delete
          </ToolbarButton>
        </Tooltip>
      </Toolbar>

      {/* ── Breadcrumb Navigation ── */}
      <div className={styles.breadcrumbBar}>
        {breadcrumbs.length > 1 && (
          <Tooltip content="Go up one level" relationship="label">
            <Button
              appearance="subtle"
              size="small"
              icon={<ArrowLeft20Regular />}
              onClick={handleNavigateUp}
              aria-label="Navigate up one level"
            />
          </Tooltip>
        )}

        <Breadcrumb aria-label="Current folder path">
          {breadcrumbs.map((crumb, index) => {
            const isLast = index === breadcrumbs.length - 1;
            return (
              <React.Fragment key={`${index}-${crumb.label}`}>
                {index > 0 && <BreadcrumbDivider />}
                <BreadcrumbItem>
                  <BreadcrumbButton
                    current={isLast}
                    onClick={() => handleBreadcrumbNavigate(index)}
                    disabled={isLast}
                  >
                    {crumb.label}
                  </BreadcrumbButton>
                </BreadcrumbItem>
              </React.Fragment>
            );
          })}
        </Breadcrumb>

        {hasSelection && (
          <Badge appearance="tint" color="brand">
            {selectedIds.size} selected
          </Badge>
        )}
      </div>

      {/* ── Main Content Area ── */}
      <div className={styles.contentArea}>

        {/* Error state */}
        {loadError && (
          <MessageBar intent="error">
            <MessageBarBody>{loadError}</MessageBarBody>
          </MessageBar>
        )}

        {/* Upload in-progress */}
        {isUploading && (
          <MessageBar intent="info">
            <MessageBarBody>
              <span className={styles.uploadingIndicator}>
                <Spinner size="tiny" />
                Uploading {uploadingCount} file(s)...
              </span>
            </MessageBarBody>
          </MessageBar>
        )}

        {/* Loading state */}
        {loading && (
          <div className={styles.centered}>
            <Spinner size="medium" label="Loading items..." />
          </div>
        )}

        {/* Empty state */}
        {!loading && !loadError && items.length === 0 && (
          <div className={styles.centered}>
            <Folder20Regular style={{ fontSize: "48px" }} />
            <Text size={400} weight="semibold">
              This folder is empty
            </Text>
            <Text size={300}>
              Upload files or create folders using the toolbar above.
            </Text>
          </div>
        )}

        {/* File / Folder Grid */}
        {!loading && items.length > 0 && (
          <div className={styles.gridArea}>
            <DataGrid
              items={items}
              columns={gridColumns}
              sortable
              resizableColumns
              getRowId={(item) => item.id}
              focusMode="composite"
              aria-label="Files and folders"
            >
              <DataGridHeader>
                <DataGridRow>
                  {({ renderHeaderCell }) => (
                    <DataGridHeaderCell>
                      {renderHeaderCell()}
                    </DataGridHeaderCell>
                  )}
                </DataGridRow>
              </DataGridHeader>

              <DataGridBody<DriveItem>>
                {({ item, rowId }) => {
                  const selected = selectedIds.has(item.id);
                  const itemIsFolder = isFolder(item);

                  return (
                    <DataGridRow<DriveItem>
                      key={rowId}
                      style={{
                        backgroundColor: selected
                          ? tokens.colorBrandBackground2
                          : undefined,
                        cursor: itemIsFolder ? "pointer" : "default",
                      }}
                      aria-selected={selected}
                      onClick={(e: React.MouseEvent) =>
                        handleRowClick(e, item)
                      }
                      onDoubleClick={() => handleRowDoubleClick(item)}
                    >
                      {({ columnId }) => (
                        <DataGridCell>
                          {/* Name column — folder/file icon + name */}
                          {columnId === "name" && (
                            itemIsFolder ? (
                              <span className={styles.folderNameCell}>
                                <Folder20Regular />
                                {item.name}
                              </span>
                            ) : (
                              <span className={styles.fileNameCell}>
                                <Document20Regular />
                                {item.name}
                              </span>
                            )
                          )}

                          {/* Type column */}
                          {columnId === "type" && (
                            itemIsFolder ? "Folder" : "File"
                          )}

                          {/* Size column — dash for folders */}
                          {columnId === "size" && (
                            itemIsFolder ? (
                              <Text
                                style={{ color: tokens.colorNeutralForeground3 }}
                              >
                                —
                              </Text>
                            ) : (
                              formatFileSize(item.size)
                            )
                          )}

                          {/* Modified date */}
                          {columnId === "modified" &&
                            formatDate(item.lastModifiedDateTime)}

                          {/* Modified by */}
                          {columnId === "modifiedBy" &&
                            (item.lastModifiedBy?.user?.displayName ?? "—")}
                        </DataGridCell>
                      )}
                    </DataGridRow>
                  );
                }}
              </DataGridBody>
            </DataGrid>
          </div>
        )}
      </div>

      {/* ── Drag-Drop Upload Zone (ADR-012: FileUploadZone from @spaarke/ui-components) ── */}
      <div
        id="spe-file-browser-upload-zone"
        className={styles.uploadSection}
      >
        <Text size={200} className={styles.uploadLabel}>
          Drop files here to upload to the current folder
        </Text>
        <FileUploadZone
          onFilesAccepted={handleFilesAccepted}
          onValidationErrors={handleValidationErrors}
          validationConfig={uploadValidationConfig}
          disabled={isUploading || loading}
        />
        {uploadErrors.length > 0 && (
          <div className={styles.uploadErrors}>
            {uploadErrors.map((err, i) => (
              <MessageBar key={i} intent="warning">
                <MessageBarBody>
                  <strong>{err.fileName}</strong>: {err.reason}
                </MessageBarBody>
              </MessageBar>
            ))}
          </div>
        )}
      </div>

      {/* ── New Folder Dialog ── */}
      <Dialog
        open={newFolderOpen}
        onOpenChange={(_e, { open }) => {
          if (!open) handleNewFolderClose();
        }}
      >
        <DialogSurface>
          <DialogBody>
            <DialogTitle>New Folder</DialogTitle>
            <DialogContent>
              <Field
                label="Folder name"
                validationMessage={folderError ?? undefined}
                validationState={folderError ? "error" : "none"}
              >
                <Input
                  value={newFolderName}
                  onChange={(_e, { value }) => setNewFolderName(value)}
                  placeholder="Enter folder name"
                  onKeyDown={(e) => {
                    if (e.key === "Enter") void handleCreateFolder();
                  }}
                  autoFocus
                />
              </Field>
            </DialogContent>
            <DialogActions>
              <DialogTrigger disableButtonEnhancement>
                <Button
                  appearance="secondary"
                  onClick={handleNewFolderClose}
                >
                  Cancel
                </Button>
              </DialogTrigger>
              <Button
                appearance="primary"
                onClick={() => void handleCreateFolder()}
                disabled={!newFolderName.trim() || creatingFolder}
                icon={creatingFolder ? <Spinner size="tiny" /> : undefined}
              >
                {creatingFolder ? "Creating..." : "Create Folder"}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      {/* ── Delete Confirmation Dialog ── */}
      <Dialog
        open={deleteOpen}
        onOpenChange={(_e, { open }) => {
          if (!open && !deleting) setDeleteOpen(false);
        }}
      >
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Confirm Delete</DialogTitle>
            <DialogContent>
              <Text>
                Are you sure you want to delete {selectedIds.size}{" "}
                {selectedIds.size === 1 ? "item" : "items"}? This cannot be
                undone.
              </Text>
            </DialogContent>
            <DialogActions>
              <DialogTrigger disableButtonEnhancement>
                <Button
                  appearance="secondary"
                  onClick={() => setDeleteOpen(false)}
                  disabled={deleting}
                >
                  Cancel
                </Button>
              </DialogTrigger>
              <Button
                appearance="primary"
                onClick={() => void handleDelete()}
                disabled={deleting}
                icon={deleting ? <Spinner size="tiny" /> : undefined}
              >
                {deleting ? "Deleting..." : "Delete"}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
};
