/**
 * DocumentLibrary component — Document management for the Project page Documents tab.
 *
 * Displays project documents in a Fluent UI v9 DataGrid. Shows pre-computed AI
 * summaries from the sprk_document.sprk_summary field (Document Profile pipeline).
 * Upload and download are restricted by access level — View Only users (100000000)
 * cannot upload or download. Collaborate (100000001) and Full Access (100000002)
 * users have full upload and download capabilities.
 *
 * Version history is fetched via BFF API: GET /api/v1/external/documents/{id}/versions
 *
 * ADR-021: All styles use Fluent v9 design tokens exclusively. No hard-coded colors.
 * ADR-022: React 18 functional component (createRoot is in main.tsx).
 */

import * as React from "react";
import {
  DataGrid,
  DataGridHeader,
  DataGridRow,
  DataGridHeaderCell,
  DataGridBody,
  DataGridCell,
  TableColumnDefinition,
  createTableColumn,
  makeStyles,
  tokens,
  Button,
  Spinner,
  Text,
  Badge,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogActions,
  DialogContent,
  Tooltip,
  MessageBar,
  MessageBarBody,
  Popover,
  PopoverSurface,
  PopoverTrigger,
  Divider,
} from "@fluentui/react-components";
import {
  ArrowDownloadRegular,
  ArrowUploadRegular,
  HistoryRegular,
  DocumentRegular,
  SparkleRegular,
  DismissRegular,
} from "@fluentui/react-icons";
import { getDocuments, ODataDocument } from "../api/web-api-client";
import { bffApiCall } from "../auth/bff-client";
import { AccessLevel } from "../types";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    width: "100%",
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
  },
  toolbar: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: tokens.spacingHorizontalM,
  },
  toolbarLeft: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  toolbarRight: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  gridContainer: {
    width: "100%",
    overflowX: "auto",
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
  },
  documentName: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorBrandForeground1,
    cursor: "pointer",
    ":hover": {
      textDecoration: "underline",
    },
  },
  documentNameIcon: {
    flexShrink: "0",
    color: tokens.colorNeutralForeground3,
  },
  summaryCell: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    maxWidth: "300px",
  },
  summaryText: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  summaryIcon: {
    flexShrink: "0",
    color: tokens.colorBrandForeground2,
  },
  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "240px",
    gap: tokens.spacingVerticalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingHorizontalXL,
  },
  emptyStateIcon: {
    fontSize: "40px",
    color: tokens.colorNeutralForeground4,
  },
  emptyStateText: {
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
  },
  loadingContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "200px",
    gap: tokens.spacingVerticalM,
  },
  actionButtons: {
    display: "flex",
    flexDirection: "row",
    gap: tokens.spacingHorizontalXS,
  },
  versionList: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    minWidth: "320px",
    maxHeight: "360px",
    overflowY: "auto",
  },
  versionItem: {
    display: "flex",
    flexDirection: "column",
    gap: "2px",
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },
  versionLabel: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  uploadInput: {
    display: "none",
  },
  uploadDialogContent: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
  },
  uploadDropZone: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    borderWidth: "2px",
    borderStyle: "dashed",
    borderColor: tokens.colorNeutralStroke1,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingHorizontalXXL,
    gap: tokens.spacingVerticalM,
    cursor: "pointer",
    backgroundColor: tokens.colorNeutralBackground2,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground3,
      borderColor: tokens.colorBrandStroke1,
    },
  },
  uploadDropZoneActive: {
    backgroundColor: tokens.colorNeutralBackground3,
    borderColor: tokens.colorBrandStroke1,
  },
  uploadFileInfo: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
  },
  uploadFileName: {
    flex: "1",
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  summaryPopoverContent: {
    maxWidth: "400px",
    padding: tokens.spacingHorizontalS,
  },
  summaryPopoverText: {
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase400,
  },
  noSummaryText: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontStyle: "italic",
  },
});

// ---------------------------------------------------------------------------
// BFF API response types
// ---------------------------------------------------------------------------

interface DocumentVersion {
  /** SPE version ID */
  versionId: string;
  /** Version label (e.g. "1.0", "2.0") */
  versionLabel: string;
  /** ISO date string when this version was created */
  createdAt: string;
  /** Name of the user who created this version */
  createdByName?: string;
  /** File size in bytes */
  fileSizeBytes?: number;
}

interface DocumentVersionsResponse {
  versions: DocumentVersion[];
}

// ---------------------------------------------------------------------------
// Helper functions
// ---------------------------------------------------------------------------

/**
 * Format a file size in bytes to a human-readable string.
 */
function formatFileSize(bytes: number | null | undefined): string {
  if (bytes == null || bytes <= 0) return "—";
  const units = ["B", "KB", "MB", "GB"];
  let value = bytes;
  let unitIndex = 0;
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex++;
  }
  return `${value.toFixed(1)} ${units[unitIndex]}`;
}

/**
 * Format a Dataverse ISO date string for display.
 */
function formatDate(isoDate: string | null | undefined): string {
  if (!isoDate) return "—";
  try {
    return new Intl.DateTimeFormat("en-US", {
      year: "numeric",
      month: "short",
      day: "numeric",
    }).format(new Date(isoDate));
  } catch {
    return isoDate;
  }
}

/**
 * Returns true when the access level allows upload and download.
 */
function canUploadOrDownload(accessLevel: AccessLevel): boolean {
  return (
    accessLevel === AccessLevel.Collaborate ||
    accessLevel === AccessLevel.FullAccess
  );
}

// ---------------------------------------------------------------------------
// AI Summary popover sub-component
// ---------------------------------------------------------------------------

interface AiSummaryPopoverProps {
  summary: string | null | undefined;
}

const AiSummaryCell: React.FC<AiSummaryPopoverProps> = ({ summary }) => {
  const styles = useStyles();

  if (!summary) {
    return (
      <Text className={styles.noSummaryText} size={200}>
        No summary
      </Text>
    );
  }

  return (
    <Popover withArrow>
      <PopoverTrigger disableButtonEnhancement>
        <div className={styles.summaryCell} title={summary}>
          <SparkleRegular className={styles.summaryIcon} fontSize={14} />
          <Text className={styles.summaryText} size={200}>
            {summary}
          </Text>
        </div>
      </PopoverTrigger>
      <PopoverSurface className={styles.summaryPopoverContent}>
        <Text size={200} className={styles.summaryPopoverText}>
          {summary}
        </Text>
      </PopoverSurface>
    </Popover>
  );
};

// ---------------------------------------------------------------------------
// Version history panel sub-component
// ---------------------------------------------------------------------------

interface VersionHistoryPanelProps {
  documentId: string;
  documentName: string;
  open: boolean;
  onClose: () => void;
}

const VersionHistoryPanel: React.FC<VersionHistoryPanelProps> = ({
  documentId,
  documentName,
  open,
  onClose,
}) => {
  const styles = useStyles();
  const [versions, setVersions] = React.useState<DocumentVersion[]>([]);
  const [loading, setLoading] = React.useState<boolean>(false);
  const [error, setError] = React.useState<string | null>(null);

  React.useEffect(() => {
    if (!open) return;

    let cancelled = false;

    const fetchVersions = async () => {
      setLoading(true);
      setError(null);
      setVersions([]);

      try {
        const response = await bffApiCall<DocumentVersionsResponse>(
          `/api/v1/external/documents/${documentId}/versions`
        );
        if (!cancelled) {
          setVersions(response.versions ?? []);
        }
      } catch (err) {
        if (!cancelled) {
          setError("Failed to load version history. Please try again.");
          console.error("[DocumentLibrary] Version history fetch failed:", err);
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    };

    void fetchVersions();

    return () => {
      cancelled = true;
    };
  }, [documentId, open]);

  return (
    <Dialog open={open} onOpenChange={(_ev, data) => { if (!data.open) onClose(); }}>
      <DialogSurface>
        <DialogTitle
          action={
            <Button
              appearance="subtle"
              aria-label="Close"
              icon={<DismissRegular />}
              onClick={onClose}
            />
          }
        >
          Version History — {documentName}
        </DialogTitle>
        <DialogBody>
          <DialogContent>
            {loading && (
              <div className={styles.loadingContainer}>
                <Spinner size="small" label="Loading versions..." />
              </div>
            )}

            {error && !loading && (
              <MessageBar intent="error">
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            )}

            {!loading && !error && versions.length === 0 && (
              <Text size={300} className={styles.emptyStateText}>
                No version history available for this document.
              </Text>
            )}

            {!loading && !error && versions.length > 0 && (
              <div className={styles.versionList}>
                {versions.map((version, index) => (
                  <React.Fragment key={version.versionId}>
                    {index > 0 && <Divider />}
                    <div className={styles.versionItem}>
                      <Text size={300} weight="semibold">
                        Version {version.versionLabel}
                      </Text>
                      <Text size={200} className={styles.versionLabel}>
                        {formatDate(version.createdAt)}
                        {version.createdByName ? ` · ${version.createdByName}` : ""}
                        {version.fileSizeBytes != null
                          ? ` · ${formatFileSize(version.fileSizeBytes)}`
                          : ""}
                      </Text>
                    </div>
                  </React.Fragment>
                ))}
              </div>
            )}
          </DialogContent>
        </DialogBody>
        <DialogActions>
          <Button appearance="secondary" onClick={onClose}>
            Close
          </Button>
        </DialogActions>
      </DialogSurface>
    </Dialog>
  );
};

// ---------------------------------------------------------------------------
// Upload dialog sub-component
// ---------------------------------------------------------------------------

interface UploadDialogProps {
  projectId: string;
  open: boolean;
  onClose: () => void;
  onUploadComplete: () => void;
}

const UploadDialog: React.FC<UploadDialogProps> = ({
  projectId,
  open,
  onClose,
  onUploadComplete,
}) => {
  const styles = useStyles();
  const fileInputRef = React.useRef<HTMLInputElement>(null);
  const [selectedFile, setSelectedFile] = React.useState<File | null>(null);
  const [isDragOver, setIsDragOver] = React.useState<boolean>(false);
  const [uploading, setUploading] = React.useState<boolean>(false);
  const [uploadError, setUploadError] = React.useState<string | null>(null);

  const handleFileSelect = (file: File) => {
    setSelectedFile(file);
    setUploadError(null);
  };

  const handleFileInputChange = (
    e: React.ChangeEvent<HTMLInputElement>
  ) => {
    const file = e.target.files?.[0];
    if (file) handleFileSelect(file);
  };

  const handleDrop = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    setIsDragOver(false);
    const file = e.dataTransfer.files?.[0];
    if (file) handleFileSelect(file);
  };

  const handleDragOver = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    setIsDragOver(true);
  };

  const handleDragLeave = () => {
    setIsDragOver(false);
  };

  const handleUpload = async () => {
    if (!selectedFile) return;

    setUploading(true);
    setUploadError(null);

    try {
      const formData = new FormData();
      formData.append("file", selectedFile);
      formData.append("projectId", projectId);

      // Use bffApiCall for authenticated upload — override Content-Type so browser
      // sets the correct multipart/form-data boundary automatically.
      await bffApiCall<void>("/api/v1/external/documents/upload", {
        method: "POST",
        body: formData,
        headers: {
          // Intentionally omit Content-Type — fetch sets it with the correct boundary
        },
      });

      setSelectedFile(null);
      onUploadComplete();
      onClose();
    } catch (err) {
      console.error("[DocumentLibrary] Upload failed:", err);
      setUploadError(
        "Failed to upload the document. Please check the file and try again."
      );
    } finally {
      setUploading(false);
    }
  };

  const handleDialogClose = () => {
    if (uploading) return;
    setSelectedFile(null);
    setUploadError(null);
    onClose();
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(_ev, data) => {
        if (!data.open) handleDialogClose();
      }}
    >
      <DialogSurface>
        <DialogTitle
          action={
            <Button
              appearance="subtle"
              aria-label="Close"
              icon={<DismissRegular />}
              onClick={handleDialogClose}
              disabled={uploading}
            />
          }
        >
          Upload Document
        </DialogTitle>
        <DialogBody>
          <DialogContent>
            <div className={styles.uploadDialogContent}>
              {uploadError && (
                <MessageBar intent="error">
                  <MessageBarBody>{uploadError}</MessageBarBody>
                </MessageBar>
              )}

              {!selectedFile ? (
                <div
                  className={`${styles.uploadDropZone}${isDragOver ? ` ${styles.uploadDropZoneActive}` : ""}`}
                  onDrop={handleDrop}
                  onDragOver={handleDragOver}
                  onDragLeave={handleDragLeave}
                  onClick={() => fileInputRef.current?.click()}
                  role="button"
                  tabIndex={0}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                      fileInputRef.current?.click();
                    }
                  }}
                  aria-label="Drop a file here or click to select"
                >
                  <ArrowUploadRegular style={{ fontSize: "32px", color: tokens.colorNeutralForeground3 }} />
                  <Text size={300} weight="semibold">
                    Drop a file here
                  </Text>
                  <Text size={200} className={styles.emptyStateText}>
                    or click to select a file from your computer
                  </Text>
                  <input
                    ref={fileInputRef}
                    type="file"
                    className={styles.uploadInput}
                    onChange={handleFileInputChange}
                    aria-hidden="true"
                  />
                </div>
              ) : (
                <div className={styles.uploadFileInfo}>
                  <DocumentRegular fontSize={20} />
                  <Text className={styles.uploadFileName} size={300}>
                    {selectedFile.name}
                  </Text>
                  <Text size={200} className={styles.versionLabel}>
                    {formatFileSize(selectedFile.size)}
                  </Text>
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={<DismissRegular />}
                    onClick={() => setSelectedFile(null)}
                    disabled={uploading}
                    aria-label="Remove selected file"
                  />
                </div>
              )}
            </div>
          </DialogContent>
        </DialogBody>
        <DialogActions>
          <Button
            appearance="primary"
            icon={uploading ? <Spinner size="tiny" /> : <ArrowUploadRegular />}
            onClick={() => void handleUpload()}
            disabled={!selectedFile || uploading}
          >
            {uploading ? "Uploading…" : "Upload"}
          </Button>
          <Button
            appearance="secondary"
            onClick={handleDialogClose}
            disabled={uploading}
          >
            Cancel
          </Button>
        </DialogActions>
      </DialogSurface>
    </Dialog>
  );
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface DocumentLibraryProps {
  /** Dataverse GUID of the owning sprk_project record */
  projectId: string;
  /** The authenticated user's access level for this project */
  accessLevel: AccessLevel;
}

// ---------------------------------------------------------------------------
// DocumentLibrary — main component
// ---------------------------------------------------------------------------

/**
 * DocumentLibrary — document management tab for the Project page.
 *
 * Features:
 * - Fetches documents from Dataverse via Power Pages Web API (getDocuments)
 * - Displays documents in a Fluent v9 DataGrid
 * - Columns: Name, Type, Size, Modified Date, AI Summary
 * - AI summaries are pre-computed (from sprk_summary field) — not generated on demand
 * - Upload button: Collaborate and Full Access only
 * - Download button: Collaborate and Full Access only
 * - View Only users (AccessLevel.ViewOnly) see read-only list with no action buttons
 * - Version history dialog via BFF API GET /api/v1/external/documents/{id}/versions
 * - Empty state when no documents
 * - Loading state while fetching
 *
 * ADR-021: Fluent UI v9 only. makeStyles + tokens. No hard-coded colors.
 */
export const DocumentLibrary: React.FC<DocumentLibraryProps> = ({
  projectId,
  accessLevel,
}) => {
  const styles = useStyles();

  // ---------------------------------------------------------------------------
  // State
  // ---------------------------------------------------------------------------

  const [documents, setDocuments] = React.useState<ODataDocument[]>([]);
  const [loading, setLoading] = React.useState<boolean>(true);
  const [error, setError] = React.useState<string | null>(null);
  const [uploadDialogOpen, setUploadDialogOpen] = React.useState<boolean>(false);
  const [versionHistoryDoc, setVersionHistoryDoc] =
    React.useState<ODataDocument | null>(null);
  const [downloadingId, setDownloadingId] = React.useState<string | null>(null);

  const canActOnDocuments = canUploadOrDownload(accessLevel);

  // ---------------------------------------------------------------------------
  // Data fetching
  // ---------------------------------------------------------------------------

  const fetchDocuments = React.useCallback(async () => {
    if (!projectId) return;

    setLoading(true);
    setError(null);

    try {
      const data = await getDocuments(projectId);
      setDocuments(data);
    } catch (err) {
      console.error("[DocumentLibrary] Failed to load documents:", err);
      setError("Failed to load documents. Please try refreshing the page.");
    } finally {
      setLoading(false);
    }
  }, [projectId]);

  React.useEffect(() => {
    void fetchDocuments();
  }, [fetchDocuments]);

  // ---------------------------------------------------------------------------
  // Download handler
  // ---------------------------------------------------------------------------

  const handleDownload = React.useCallback(
    async (doc: ODataDocument) => {
      if (!canActOnDocuments) return;

      setDownloadingId(doc.sprk_documentid);

      try {
        // BFF returns a signed download URL or the file bytes.
        // We request a download URL from the BFF and open it.
        const result = await bffApiCall<{ downloadUrl: string }>(
          `/api/v1/external/documents/${doc.sprk_documentid}/download`
        );

        // Trigger browser download via a temporary anchor element
        const anchor = window.document.createElement("a");
        anchor.href = result.downloadUrl;
        anchor.download = doc.sprk_name;
        anchor.style.display = "none";
        window.document.body.appendChild(anchor);
        anchor.click();
        window.document.body.removeChild(anchor);
      } catch (err) {
        console.error(
          `[DocumentLibrary] Download failed for document ${doc.sprk_documentid}:`,
          err
        );
      } finally {
        setDownloadingId(null);
      }
    },
    [canActOnDocuments]
  );

  // ---------------------------------------------------------------------------
  // Column definitions
  // ---------------------------------------------------------------------------

  const columns = React.useMemo(
    (): TableColumnDefinition<ODataDocument>[] => [
      // --- Name ---
      createTableColumn<ODataDocument>({
        columnId: "name",
        compare: (a, b) =>
          (a.sprk_name ?? "").localeCompare(b.sprk_name ?? ""),
        renderHeaderCell: () => "Name",
        renderCell: (item) => (
          <div className={styles.documentName}>
            <DocumentRegular className={styles.documentNameIcon} fontSize={16} />
            <Text size={300} weight="semibold" truncate wrap={false}>
              {item.sprk_name}
            </Text>
          </div>
        ),
      }),

      // --- Type ---
      createTableColumn<ODataDocument>({
        columnId: "type",
        compare: (a, b) =>
          (a.sprk_documenttype ?? "").localeCompare(b.sprk_documenttype ?? ""),
        renderHeaderCell: () => "Type",
        renderCell: (item) =>
          item.sprk_documenttype ? (
            <Badge appearance="tint" size="small">
              {item.sprk_documenttype}
            </Badge>
          ) : (
            <Text size={200} className={styles.noSummaryText}>
              —
            </Text>
          ),
      }),

      // --- Modified Date ---
      createTableColumn<ODataDocument>({
        columnId: "modifiedDate",
        compare: (a, b) =>
          (a.createdon ?? "").localeCompare(b.createdon ?? ""),
        renderHeaderCell: () => "Date Added",
        renderCell: (item) => (
          <Text size={200} className={styles.versionLabel}>
            {formatDate(item.createdon)}
          </Text>
        ),
      }),

      // --- AI Summary ---
      createTableColumn<ODataDocument>({
        columnId: "aiSummary",
        compare: (a, b) =>
          (a.sprk_summary ?? "").localeCompare(b.sprk_summary ?? ""),
        renderHeaderCell: () => "AI Summary",
        renderCell: (item) => (
          <AiSummaryCell summary={item.sprk_summary} />
        ),
      }),

      // --- Actions ---
      ...(canActOnDocuments
        ? [
            createTableColumn<ODataDocument>({
              columnId: "actions",
              renderHeaderCell: () => "",
              renderCell: (item) => (
                <div className={styles.actionButtons}>
                  <Tooltip content="Download" relationship="label">
                    <Button
                      appearance="subtle"
                      size="small"
                      icon={
                        downloadingId === item.sprk_documentid ? (
                          <Spinner size="tiny" />
                        ) : (
                          <ArrowDownloadRegular />
                        )
                      }
                      disabled={downloadingId === item.sprk_documentid}
                      onClick={() => void handleDownload(item)}
                      aria-label={`Download ${item.sprk_name}`}
                    />
                  </Tooltip>
                  <Tooltip content="Version history" relationship="label">
                    <Button
                      appearance="subtle"
                      size="small"
                      icon={<HistoryRegular />}
                      onClick={() => setVersionHistoryDoc(item)}
                      aria-label={`View version history for ${item.sprk_name}`}
                    />
                  </Tooltip>
                </div>
              ),
            }),
          ]
        : []),
    ],
    [
      canActOnDocuments,
      downloadingId,
      handleDownload,
      styles.documentName,
      styles.documentNameIcon,
      styles.noSummaryText,
      styles.versionLabel,
      styles.actionButtons,
    ]
  );

  // ---------------------------------------------------------------------------
  // Render — loading state
  // ---------------------------------------------------------------------------

  if (loading) {
    return (
      <div className={styles.loadingContainer}>
        <Spinner size="medium" label="Loading documents..." />
      </div>
    );
  }

  // ---------------------------------------------------------------------------
  // Render — error state
  // ---------------------------------------------------------------------------

  if (error) {
    return (
      <MessageBar intent="error">
        <MessageBarBody>{error}</MessageBarBody>
      </MessageBar>
    );
  }

  // ---------------------------------------------------------------------------
  // Render — empty state
  // ---------------------------------------------------------------------------

  if (documents.length === 0) {
    return (
      <div className={styles.root}>
        {/* Toolbar — show upload button even when empty so users can add their first doc */}
        {canActOnDocuments && (
          <div className={styles.toolbar}>
            <div className={styles.toolbarLeft}>
              <Text size={300} className={styles.emptyStateText}>
                No documents found for this project.
              </Text>
            </div>
            <div className={styles.toolbarRight}>
              <Button
                appearance="primary"
                icon={<ArrowUploadRegular />}
                onClick={() => setUploadDialogOpen(true)}
              >
                Upload Document
              </Button>
            </div>
          </div>
        )}

        <div className={styles.emptyState}>
          <DocumentRegular className={styles.emptyStateIcon} />
          <Text size={400} weight="semibold">
            No Documents
          </Text>
          <Text size={300} className={styles.emptyStateText}>
            {canActOnDocuments
              ? "No documents have been uploaded to this project yet. Use the Upload button to add your first document."
              : "No documents have been uploaded to this project yet."}
          </Text>
        </div>

        {/* Upload dialog */}
        <UploadDialog
          projectId={projectId}
          open={uploadDialogOpen}
          onClose={() => setUploadDialogOpen(false)}
          onUploadComplete={() => void fetchDocuments()}
        />
      </div>
    );
  }

  // ---------------------------------------------------------------------------
  // Render — document list
  // ---------------------------------------------------------------------------

  return (
    <div className={styles.root}>
      {/* Toolbar */}
      <div className={styles.toolbar}>
        <div className={styles.toolbarLeft}>
          <Text size={300} className={styles.versionLabel}>
            {documents.length} document{documents.length !== 1 ? "s" : ""}
          </Text>
        </div>
        <div className={styles.toolbarRight}>
          {canActOnDocuments && (
            <Button
              appearance="primary"
              icon={<ArrowUploadRegular />}
              onClick={() => setUploadDialogOpen(true)}
            >
              Upload Document
            </Button>
          )}
        </div>
      </div>

      {/* Document grid */}
      <div className={styles.gridContainer}>
        <DataGrid
          items={documents}
          columns={columns}
          sortable
          getRowId={(item) => item.sprk_documentid}
          focusMode="composite"
        >
          <DataGridHeader>
            <DataGridRow>
              {({ renderHeaderCell }) => (
                <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
              )}
            </DataGridRow>
          </DataGridHeader>
          <DataGridBody<ODataDocument>>
            {({ item, rowId }) => (
              <DataGridRow<ODataDocument> key={rowId}>
                {({ renderCell }) => (
                  <DataGridCell>{renderCell(item)}</DataGridCell>
                )}
              </DataGridRow>
            )}
          </DataGridBody>
        </DataGrid>
      </div>

      {/* Upload dialog */}
      <UploadDialog
        projectId={projectId}
        open={uploadDialogOpen}
        onClose={() => setUploadDialogOpen(false)}
        onUploadComplete={() => void fetchDocuments()}
      />

      {/* Version history dialog */}
      {versionHistoryDoc && (
        <VersionHistoryPanel
          documentId={versionHistoryDoc.sprk_documentid}
          documentName={versionHistoryDoc.sprk_name}
          open={versionHistoryDoc !== null}
          onClose={() => setVersionHistoryDoc(null)}
        />
      )}
    </div>
  );
};

export default DocumentLibrary;
