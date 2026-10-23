/**
 * DocumentLibrary component — Document management for the Project page Documents tab AND the record detail view
 * (task 054: project, matter, work assignment, service request, invoice - the task 031 routes).
 *
 * ROOT (task 054). `root` + `recordId` pick the task 031 routes (`/api/v1/external/{projects|matters|workassignments|
 * servicerequests}/{id}/documents[/...]`); a service request's documents are reachable ONLY through its own route. The
 * existing `projectId` prop is the project shorthand and behaves exactly as before.
 *
 * RIGHTS (task 054). Upload needs Create and download needs Read on the record. A host that knows better passes
 * `canUpload` / `canDownload`; otherwise download follows `accessLevel` (Collaborate / Full Access), and Upload follows
 * the BFF's `canCreate` on the record's document list (a matter, work assignment or service request) or `accessLevel`
 * (a project). For a caller whose write access is decided per record (`writeAccessPerRecord`, task 036), download and
 * version history are offered wherever the list rendered (the list already passed the Read the download routes need), and
 * a project's Upload follows its list's `canCreate`. This is only what is SHOWN: the server decides, and a refused upload
 * shows its 403 as an error.
 *
 * DELETE-OWN (task 054, FR-26). With a `caller` identity, a row the caller created (creator stamp = the signed-in
 * principal) shows Delete, which asks first (shared ConfirmModal). 204 removes the row; a 403 / 409 / 503 / 500 shows a
 * message and the row stays. Without `caller` there is no Delete.
 *
 * Displays the record's documents in a Fluent UI v9 DataGrid. Shows pre-computed AI
 * summaries from the sprk_document.sprk_summary field (Document Profile pipeline).
 * Upload and download are restricted by access level — View Only users (100000000)
 * cannot upload or download. Collaborate (100000001) and Full Access (100000002)
 * users have full upload and download capabilities.
 *
 * Version history is fetched via BFF API: GET /api/v1/external/{root}/{id}/documents/{documentId}/versions
 *
 * ADR-021: All styles use Fluent v9 design tokens exclusively. No hard-coded colors.
 * ADR-022: React 18 functional component (createRoot is in main.tsx).
 */

import * as React from 'react';
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
  shorthands,
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
  Divider,
} from '@fluentui/react-components';
import {
  ArrowDownloadRegular,
  ArrowUploadRegular,
  DeleteRegular,
  HistoryRegular,
  DocumentRegular,
  DismissRegular,
} from '@fluentui/react-icons';
// Deep component imports — avoids loading Virtualized* components that need react-window
import { FileUploadZone, UploadedFileList } from '@spaarke/ui-components/components/FileUpload';
import type { IUploadedFile, IFileValidationError } from '@spaarke/ui-components/components/FileUpload';
import { AiSummaryPopover } from '@spaarke/ui-components/components/AiSummaryPopover';
import {
  getDocuments,
  getRecordDocuments,
  deleteOwnDocument,
  documentsPath,
  documentContentPath,
  documentVersionsPath,
  type DocumentRoot,
  type ODataDocument,
} from '../api/web-api-client';
import { isCreatedByCaller, type CallerIdentity } from '../api/delete-own';
import { useDeleteOwn, DeleteOwnConfirm, DeleteFailureNotice } from './DeleteOwn';
import { TruncatedListNotice } from './TruncatedListNotice';
import { bffApiCall, bffApiBlob, problemMessage } from '../auth/bff-client';
import { AccessLevel, ApiError } from '../types';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    width: '100%',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  toolbar: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
  },
  toolbarLeft: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  toolbarRight: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  gridContainer: {
    width: '100%',
    overflowX: 'auto',
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
  },
  documentName: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorBrandForeground1,
    cursor: 'pointer',
    ':hover': {
      textDecoration: 'underline',
    },
  },
  documentNameIcon: {
    flexShrink: '0',
    color: tokens.colorNeutralForeground3,
  },
  summaryText: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '240px',
    gap: tokens.spacingVerticalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingHorizontalXL,
  },
  emptyStateIcon: {
    fontSize: '40px',
    color: tokens.colorNeutralForeground4,
  },
  emptyStateText: {
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
  loadingContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '200px',
    gap: tokens.spacingVerticalM,
  },
  actionButtons: {
    display: 'flex',
    flexDirection: 'row',
    gap: tokens.spacingHorizontalXS,
  },
  versionList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    minWidth: '320px',
    maxHeight: '360px',
    overflowY: 'auto',
  },
  versionItem: {
    display: 'flex',
    flexDirection: 'column',
    gap: '2px',
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },
  versionLabel: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  uploadDialogContent: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  noSummaryText: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontStyle: 'italic',
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
  if (bytes == null || bytes <= 0) return '—';
  const units = ['B', 'KB', 'MB', 'GB'];
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
  if (!isoDate) return '—';
  try {
    return new Intl.DateTimeFormat('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    }).format(new Date(isoDate));
  } catch {
    return isoDate;
  }
}

/** How each root's record is named in user-facing text. */
const RECORD_NOUN: Readonly<Record<DocumentRoot, string>> = {
  projects: 'project',
  matters: 'matter',
  workassignments: 'work assignment',
  servicerequests: 'service request',
  invoices: 'invoice',
};

/**
 * Returns true when the access level allows upload and download.
 */
function canUploadOrDownload(accessLevel: AccessLevel): boolean {
  return accessLevel === AccessLevel.Collaborate || accessLevel === AccessLevel.FullAccess;
}

// ---------------------------------------------------------------------------
// Download (shared with the record detail's document view, R3 task 066)
// ---------------------------------------------------------------------------

/**
 * Downloads one document through the content route of the record it is filed to and hands the bytes to the browser under
 * the document's name. The BFF streams the file itself (application/octet-stream) from the record-scoped route; it
 * deliberately never returns a signed URL or an SPE pointer, so this reads the bytes and uses an object URL.
 *
 * Fixed 2026-09-01: this previously called `/api/v1/external/documents/{id}/download` expecting `{ downloadUrl }`. That
 * route does not exist (guaranteed 404) and the response shape it assumed is one the server is designed never to produce.
 *
 * Throws on any failure (an `ApiError` for a refused or failed read): the caller shows it (`downloadFailureMessage`).
 */
export async function downloadDocumentFile(
  root: DocumentRoot,
  recordId: string,
  documentId: string,
  fileName: string
): Promise<void> {
  let objectUrl: string | undefined;
  try {
    const blob = await bffApiBlob(documentContentPath(root, recordId, documentId));

    objectUrl = window.URL.createObjectURL(blob);

    // Trigger browser download via a temporary anchor element
    const anchor = window.document.createElement('a');
    anchor.href = objectUrl;
    anchor.download = fileName;
    anchor.style.display = 'none';
    window.document.body.appendChild(anchor);
    anchor.click();
    window.document.body.removeChild(anchor);
  } finally {
    // Revoke on the next tick - revoking synchronously can cancel the in-flight navigation the anchor click just started.
    if (objectUrl) {
      const toRevoke = objectUrl;
      window.setTimeout(() => window.URL.revokeObjectURL(toRevoke), 0);
    }
  }
}

/**
 * Downloads a document filed to several records (record detail, task 066): asks each parent's content route in order and
 * moves to the next ONLY when the server refuses with 403 (that parent is not readable by the caller, or does not hold the
 * document). Any other failure stops at once (a 5xx on one parent says nothing about the next, and retrying hides it).
 * When every parent refuses, the last 403 is thrown. An empty list is a programming error: the host offers no Download then.
 */
export async function downloadDocumentFromParents(
  parents: ReadonlyArray<{ root: DocumentRoot; id: string }>,
  documentId: string,
  fileName: string
): Promise<void> {
  let lastRefusal: unknown = new Error('No parent record to download the document through.');
  for (const parent of parents) {
    try {
      await downloadDocumentFile(parent.root, parent.id, documentId, fileName);
      return;
    } catch (err) {
      if (!(err instanceof ApiError) || err.statusCode !== 403) throw err;
      lastRefusal = err;
    }
  }
  throw lastRefusal;
}

/**
 * What a failed download says. By status, never the server's detail text (it names the record and rule, and a download
 * refusal must not be usable to tell whether a document exists): 403 access, 404 no file, 5xx try later, else generic.
 */
export function downloadFailureMessage(err: unknown): string {
  const status = err instanceof ApiError ? err.statusCode : undefined;
  if (status === 403) return 'You do not have access to download this document.';
  if (status === 404) return "This document's file is not available.";
  if (status !== undefined && status >= 500)
    return 'The document could not be downloaded just now. Please try again later.';
  return 'The document could not be downloaded. Please try again.';
}

/** What a failed download logs: the status only, never the error body (task 066 review K3). */
export function downloadFailureLogText(err: unknown): string {
  return err instanceof ApiError ? `HTTP ${err.statusCode}` : 'no HTTP status (network or client error)';
}

// AiSummaryCell is now rendered inline using AiSummaryPopover from @spaarke/ui-components.

// ---------------------------------------------------------------------------
// Version history panel sub-component
// ---------------------------------------------------------------------------

interface VersionHistoryPanelProps {
  root: DocumentRoot;
  recordId: string;
  documentId: string;
  documentName: string;
  open: boolean;
  onClose: () => void;
}

const VersionHistoryPanel: React.FC<VersionHistoryPanelProps> = ({
  root,
  recordId,
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
        // Fixed 2026-09-02: was `/api/v1/external/documents/{id}/versions`, which does not
        // exist. Version history — like content download — is scoped by the record the document is filed to, so the
        // server can enforce document→record containment before touching SPE.
        const response = await bffApiCall<DocumentVersionsResponse>(documentVersionsPath(root, recordId, documentId));
        if (!cancelled) {
          setVersions(response.versions ?? []);
        }
      } catch (err) {
        if (!cancelled) {
          setError('Failed to load version history. Please try again.');
          console.error('[DocumentLibrary] Version history fetch failed:', err);
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
  }, [root, recordId, documentId, open]);

  return (
    <Dialog
      open={open}
      onOpenChange={(_ev, data) => {
        if (!data.open) onClose();
      }}
    >
      <DialogSurface>
        <DialogTitle
          action={<Button appearance="subtle" aria-label="Close" icon={<DismissRegular />} onClick={onClose} />}
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
                        {version.createdByName ? ` · ${version.createdByName}` : ''}
                        {version.fileSizeBytes != null ? ` · ${formatFileSize(version.fileSizeBytes)}` : ''}
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
  root: DocumentRoot;
  recordId: string;
  open: boolean;
  onClose: () => void;
  onUploadComplete: () => void;
}

const UploadDialog: React.FC<UploadDialogProps> = ({ root, recordId, open, onClose, onUploadComplete }) => {
  const styles = useStyles();
  const noun = RECORD_NOUN[root];
  const [selectedFiles, setSelectedFiles] = React.useState<IUploadedFile[]>([]);
  const [validationErrors, setValidationErrors] = React.useState<IFileValidationError[]>([]);
  const [uploading, setUploading] = React.useState<boolean>(false);
  const [uploadError, setUploadError] = React.useState<string | null>(null);

  const handleFilesAccepted = (files: IUploadedFile[]) => {
    // Replace existing selection — dialog uploads one file at a time
    setSelectedFiles(files);
    setValidationErrors([]);
    setUploadError(null);
  };

  const handleValidationErrors = (errors: IFileValidationError[]) => {
    setValidationErrors(errors);
  };

  const handleRemoveFile = (fileId: string) => {
    setSelectedFiles(prev => prev.filter(f => f.id !== fileId));
  };

  const handleUpload = async () => {
    if (selectedFiles.length === 0) return;

    setUploading(true);
    setUploadError(null);

    try {
      const formData = new FormData();
      formData.append('file', selectedFiles[0].file);
      // NOTE: the record id is NOT sent in the body — it is the route parameter, and the server derives
      // the storage container from it. A client must never name a container (#858).

      // Fixed 2026-09-02: was `POST /api/v1/external/documents/upload`, which is mapped NOWHERE in
      // the BFF — every upload from this dialog 404'd. The real route is project-scoped.
      // Use bffApiCall for authenticated upload — omit Content-Type so the browser
      // sets the correct multipart/form-data boundary automatically.
      // Response body is the created document row; it is ignored here because onUploadComplete
      // refetches the list, which is the single source of truth for what the project contains.
      // The 201's Location header points at a URL with no GET (task 031): it is never followed.
      await bffApiCall<unknown>(documentsPath(root, recordId), {
        method: 'POST',
        body: formData,
        headers: {},
      });

      setSelectedFiles([]);
      onUploadComplete();
      onClose();
    } catch (err) {
      console.error('[DocumentLibrary] Upload failed:', err);
      // The server's ProblemDetails `detail` is shown when it wrote one (`problemMessage`): the collision 409 names the
      // file, the owner-refusal 409 ("Record owner unresolved") says why the record cannot take documents, a 503 says
      // to retry. The text below is only the fallback for a response that carries no detail. ApiError exposes
      // `statusCode` (there is no `status`/`detail`; its `message` is the raw response text).
      const statusCode = err instanceof ApiError ? err.statusCode : undefined;
      let fallback = 'Failed to upload the document. Please check the file and try again.';
      if (statusCode === 409) {
        // A NAME COLLISION is not a failure: nothing was uploaded and the existing document is intact.
        fallback =
          `"${selectedFiles[0].name}" could not be uploaded because of a conflict (for example a file with that ` +
          `name already exists in this ${noun}). Nothing was uploaded or changed.`;
      } else if (statusCode === 413) {
        fallback = 'The file is too large to upload.';
      } else if (statusCode === 422) {
        fallback = `This ${noun} has no storage configured, so documents cannot be uploaded yet. Please contact the ${noun} owner.`;
      } else if (statusCode === 403) {
        fallback = `Your access level does not permit uploading documents to this ${noun}.`;
      } else if (statusCode !== undefined && statusCode >= 500) {
        fallback = 'The document could not be uploaded just now. Please try again later.';
      }
      setUploadError(problemMessage(err, fallback));
    } finally {
      setUploading(false);
    }
  };

  const handleDialogClose = () => {
    if (uploading) return;
    setSelectedFiles([]);
    setValidationErrors([]);
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

              {validationErrors.length > 0 && (
                <MessageBar intent="warning">
                  <MessageBarBody>{validationErrors.map(e => `${e.fileName}: ${e.reason}`).join(' · ')}</MessageBarBody>
                </MessageBar>
              )}

              <FileUploadZone
                onFilesAccepted={handleFilesAccepted}
                onValidationErrors={handleValidationErrors}
                disabled={uploading}
              />

              <UploadedFileList files={selectedFiles} onRemove={handleRemoveFile} disabled={uploading} />
            </div>
          </DialogContent>
        </DialogBody>
        <DialogActions>
          <Button
            appearance="primary"
            icon={uploading ? <Spinner size="tiny" /> : <ArrowUploadRegular />}
            onClick={() => void handleUpload()}
            disabled={selectedFiles.length === 0 || uploading}
          >
            {uploading ? 'Uploading…' : 'Upload'}
          </Button>
          <Button appearance="secondary" onClick={handleDialogClose} disabled={uploading}>
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

/** Which record's documents: a project by its id (the shorthand), or any record by root + id. Never neither. */
export type DocumentLibraryTarget =
  | {
      /** Dataverse GUID of the owning sprk_project record (the project shorthand). */
      projectId: string;
      root?: undefined;
      recordId?: undefined;
    }
  | {
      /** Which record type the documents are filed to. */
      root: DocumentRoot;
      /** The record's id for `root`. */
      recordId: string;
      projectId?: undefined;
    };

export type DocumentLibraryProps = DocumentLibraryTarget & {
  /** The authenticated user's access level for this record: the default for `canUpload` / `canDownload`. */
  accessLevel?: AccessLevel;
  /**
   * Show the Upload control. When the host passes none: a project follows `accessLevel` (Collaborate / Full Access); any
   * other record follows the BFF's `canCreate` on its document list (the right the upload route checks), else
   * `accessLevel`. The server enforces Create either way.
   */
  canUpload?: boolean;
  /**
   * Show Download and Version history. Default: Collaborate / Full Access, or — for a per-record caller
   * (`writeAccessPerRecord`) — wherever the list rendered. The server enforces Read either way.
   */
  canDownload?: boolean;
  /** The signed-in principal. With it, rows the caller created show Delete; without it, no row does (task 054). */
  caller?: CallerIdentity | null;
  /**
   * The caller's write access is decided per record (`/me` `writeAccessPerRecord`, task 036 fix round 1): a project's
   * Upload then follows the BFF's `canCreate` on its document list, like every other record, instead of `accessLevel`;
   * Download and Version history are offered wherever the list rendered (fix round 2), since the list read already passed
   * the Read those routes check.
   */
  writeAccessPerRecord?: boolean;
};

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
  root = 'projects',
  recordId: recordIdProp,
  accessLevel = AccessLevel.ViewOnly,
  canUpload,
  canDownload,
  caller,
  writeAccessPerRecord = false,
}) => {
  const styles = useStyles();
  // The props type refuses a call with neither; an empty id that still arrives (a caller that casts, or a blank id from
  // upstream) is an error state below, never a spinner that cannot end.
  const recordId = recordIdProp ?? projectId ?? '';
  const noun = RECORD_NOUN[root];

  // ---------------------------------------------------------------------------
  // State
  // ---------------------------------------------------------------------------

  const [documents, setDocuments] = React.useState<ODataDocument[]>([]);
  // Task 105: the BFF said the list was cut short — shown, never presented as the whole list.
  const [truncated, setTruncated] = React.useState<boolean>(false);
  // The BFF's raw `canCreate` on this record's document list (null = not said, or not loaded yet). How it is read depends
  // on `writeAccessPerRecord`, which is applied at render (below), never baked in by the fetch: `/me` may answer after the
  // list did, and a stored, already-interpreted value would then be stale (task 036 fix round 2).
  const [listCanCreate, setListCanCreate] = React.useState<boolean | null>(null);
  const [loading, setLoading] = React.useState<boolean>(true);
  const [error, setError] = React.useState<string | null>(null);
  const [uploadDialogOpen, setUploadDialogOpen] = React.useState<boolean>(false);
  const [versionHistoryDoc, setVersionHistoryDoc] = React.useState<ODataDocument | null>(null);
  const [downloadingId, setDownloadingId] = React.useState<string | null>(null);
  // Task 066: why the last download failed (null = none); cleared when the next download starts.
  const [downloadError, setDownloadError] = React.useState<string | null>(null);

  // A project follows the access level unless the caller's write access is per record (task 036 fix round 1); then it
  // follows `canCreate` like every other record, and a missing one reads as "cannot".
  const uploadFromList = root === 'projects' ? (writeAccessPerRecord ? (listCanCreate ?? false) : null) : listCanCreate;
  const showUpload = canUpload ?? uploadFromList ?? canUploadOrDownload(accessLevel);
  // Download / version history need only Read on the record (the server checks it). A per-record caller's level says only
  // what they may SEE, and the list rendering at all proves that Read, so they are offered (task 036 fix round 2).
  const canActOnDocuments = canDownload ?? (writeAccessPerRecord || canUploadOrDownload(accessLevel));

  // Delete-own (task 054): the row leaves the list on the server's 204; any refusal keeps it and is shown.
  const del = useDeleteOwn<ODataDocument>({
    noun: 'document',
    remove: doc => deleteOwnDocument(doc.sprk_documentid),
    onRemoved: doc => setDocuments(prev => prev.filter(d => d.sprk_documentid !== doc.sprk_documentid)),
  });
  const askDelete = del.ask;

  // ---------------------------------------------------------------------------
  // Data fetching
  // ---------------------------------------------------------------------------

  const fetchDocuments = React.useCallback(async () => {
    if (!recordId) {
      setLoading(false);
      setError('Documents cannot be loaded: no record was given.');
      return;
    }

    setLoading(true);
    setError(null);

    try {
      // The project read keeps its own client function (`getDocuments`); the other roots share the task 031 route and
      // also say whether the caller may upload.
      if (root === 'projects') {
        const data = await getDocuments(recordId);
        setDocuments(data.items);
        setTruncated(data.truncated);
        setListCanCreate(data.canCreate ?? null);
      } else {
        const data = await getRecordDocuments(root, recordId);
        setDocuments(data.items);
        setTruncated(data.truncated);
        setListCanCreate(data.canCreate ?? null);
      }
    } catch (err) {
      console.error('[DocumentLibrary] Failed to load documents:', err);
      setError('Failed to load documents. Please try refreshing the page.');
    } finally {
      setLoading(false);
    }
  }, [root, recordId]);

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
      setDownloadError(null);

      try {
        await downloadDocumentFile(root, recordId, doc.sprk_documentid, doc.sprk_name);
      } catch (err) {
        console.error(
          `[DocumentLibrary] Download failed for document ${doc.sprk_documentid}: ${downloadFailureLogText(err)}`
        );
        // Task 066: a failed download is shown, not only logged.
        setDownloadError(downloadFailureMessage(err));
      } finally {
        setDownloadingId(null);
      }
    },
    [canActOnDocuments, root, recordId]
  );

  // ---------------------------------------------------------------------------
  // Column definitions
  // ---------------------------------------------------------------------------

  const columns = React.useMemo(
    (): TableColumnDefinition<ODataDocument>[] => [
      // --- Name ---
      createTableColumn<ODataDocument>({
        columnId: 'name',
        compare: (a, b) => (a.sprk_name ?? '').localeCompare(b.sprk_name ?? ''),
        renderHeaderCell: () => 'Name',
        renderCell: item => (
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
        columnId: 'type',
        compare: (a, b) => (a.sprk_documenttype ?? '').localeCompare(b.sprk_documenttype ?? ''),
        renderHeaderCell: () => 'Type',
        renderCell: item =>
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
        columnId: 'modifiedDate',
        compare: (a, b) => (a.createdon ?? '').localeCompare(b.createdon ?? ''),
        renderHeaderCell: () => 'Date Added',
        renderCell: item => (
          <Text size={200} className={styles.versionLabel}>
            {formatDate(item.createdon)}
          </Text>
        ),
      }),

      // --- AI Summary ---
      createTableColumn<ODataDocument>({
        columnId: 'aiSummary',
        compare: (a, b) => (a.sprk_summary ?? '').localeCompare(b.sprk_summary ?? ''),
        renderHeaderCell: () => 'AI Summary',
        renderCell: item => {
          if (!item.sprk_summary) {
            return (
              <Text className={styles.noSummaryText} size={200}>
                No summary
              </Text>
            );
          }
          return (
            <AiSummaryPopover
              trigger={
                <Button appearance="subtle" size="small" style={{ maxWidth: '300px' }}>
                  <Text className={styles.summaryText} size={200}>
                    {item.sprk_summary}
                  </Text>
                </Button>
              }
              onFetchSummary={() => Promise.resolve({ summary: item.sprk_summary ?? null, tldr: null })}
              positioning="before"
            />
          );
        },
      }),

      // --- Actions ---
      ...(canActOnDocuments || caller
        ? [
            createTableColumn<ODataDocument>({
              columnId: 'actions',
              renderHeaderCell: () => '',
              renderCell: item => (
                <div className={styles.actionButtons}>
                  {/* R3 ISS-011 (#1886): a document with no file stays listed but offers neither action (both would answer
                      409 no_file_attached). Only an explicit false hides them: a BFF that does not send hasFile is an old
                      BFF, and its rows keep today's behaviour. */}
                  {canActOnDocuments && item.hasFile !== false && (
                    <>
                      <Tooltip content="Download" relationship="label">
                        <Button
                          appearance="subtle"
                          size="small"
                          icon={
                            downloadingId === item.sprk_documentid ? <Spinner size="tiny" /> : <ArrowDownloadRegular />
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
                    </>
                  )}
                  {/* Only a row the caller created (the server re-checks; a refusal is shown as an error). */}
                  {isCreatedByCaller(item, caller) && (
                    <Tooltip content="Delete" relationship="label">
                      <Button
                        appearance="subtle"
                        size="small"
                        icon={<DeleteRegular />}
                        onClick={() => askDelete(item, item.sprk_name)}
                        aria-label={`Delete ${item.sprk_name}`}
                      />
                    </Tooltip>
                  )}
                </div>
              ),
            }),
          ]
        : []),
    ],
    [
      canActOnDocuments,
      caller,
      askDelete,
      downloadingId,
      handleDownload,
      styles.documentName,
      styles.documentNameIcon,
      styles.noSummaryText,
      styles.summaryText,
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
        <TruncatedListNotice truncated={truncated} shown={0} noun="documents" />
        <DeleteFailureNotice del={del} />
        {/* Same slot as in the list render, so the children below keep their positions (and the delete confirmation its
            state) when the last document is removed: React matches siblings by index. */}
        {downloadError && (
          <MessageBar intent="error" data-testid="document-download-error">
            <MessageBarBody>{downloadError}</MessageBarBody>
          </MessageBar>
        )}
        {/* Toolbar — show upload button even when empty so users can add their first doc */}
        {showUpload && (
          <div className={styles.toolbar}>
            <div className={styles.toolbarLeft}>
              <Text size={300} className={styles.emptyStateText}>
                No documents found for this {noun}.
              </Text>
            </div>
            <div className={styles.toolbarRight}>
              <Button appearance="primary" icon={<ArrowUploadRegular />} onClick={() => setUploadDialogOpen(true)}>
                Upload Document
              </Button>
            </div>
          </div>
        )}

        {/* Task 105: an incomplete empty list is not "no documents" — the notice above says it could not be read. */}
        {!truncated && (
          <div className={styles.emptyState}>
            <DocumentRegular className={styles.emptyStateIcon} />
            <Text size={400} weight="semibold">
              No Documents
            </Text>
            <Text size={300} className={styles.emptyStateText}>
              {showUpload
                ? `No documents have been uploaded to this ${noun} yet. Use the Upload button to add your first document.`
                : `No documents have been uploaded to this ${noun} yet.`}
            </Text>
          </div>
        )}

        {/* Upload dialog */}
        <UploadDialog
          root={root}
          recordId={recordId}
          open={uploadDialogOpen}
          onClose={() => setUploadDialogOpen(false)}
          onUploadComplete={() => void fetchDocuments()}
        />
        <DeleteOwnConfirm del={del} />
      </div>
    );
  }

  // ---------------------------------------------------------------------------
  // Render — document list
  // ---------------------------------------------------------------------------

  return (
    <div className={styles.root}>
      <TruncatedListNotice truncated={truncated} shown={documents.length} noun="documents" />
      <DeleteFailureNotice del={del} />
      {downloadError && (
        <MessageBar intent="error" data-testid="document-download-error">
          <MessageBarBody>{downloadError}</MessageBarBody>
        </MessageBar>
      )}

      {/* Toolbar */}
      <div className={styles.toolbar}>
        <div className={styles.toolbarLeft}>
          <Text size={300} className={styles.versionLabel}>
            {documents.length} document{documents.length !== 1 ? 's' : ''}
          </Text>
        </div>
        <div className={styles.toolbarRight}>
          {showUpload && (
            <Button appearance="primary" icon={<ArrowUploadRegular />} onClick={() => setUploadDialogOpen(true)}>
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
          getRowId={item => item.sprk_documentid}
          focusMode="composite"
        >
          <DataGridHeader>
            <DataGridRow>
              {({ renderHeaderCell }) => <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>}
            </DataGridRow>
          </DataGridHeader>
          <DataGridBody<ODataDocument>>
            {({ item, rowId }) => (
              <DataGridRow<ODataDocument> key={rowId}>
                {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
              </DataGridRow>
            )}
          </DataGridBody>
        </DataGrid>
      </div>

      {/* Upload dialog */}
      <UploadDialog
        root={root}
        recordId={recordId}
        open={uploadDialogOpen}
        onClose={() => setUploadDialogOpen(false)}
        onUploadComplete={() => void fetchDocuments()}
      />

      {/* Delete confirmation (shared modal system, ADR-050) */}
      <DeleteOwnConfirm del={del} />

      {/* Version history dialog */}
      {versionHistoryDoc && (
        <VersionHistoryPanel
          root={root}
          recordId={recordId}
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
