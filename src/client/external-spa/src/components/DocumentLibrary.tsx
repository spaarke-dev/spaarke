/**
 * DocumentLibrary component — Document management for the Project page Documents tab AND the record detail view
 * (task 054: project, matter, work assignment, service request - the task 031 routes).
 *
 * ROOT (task 054). `root` + `recordId` pick the task 031 routes (`/api/v1/external/{projects|matters|workassignments|
 * servicerequests}/{id}/documents[/...]`); a service request's documents are reachable ONLY through its own route. The
 * existing `projectId` prop is the project shorthand and behaves exactly as before.
 *
 * RIGHTS (task 054). Upload needs Create and download needs Read on the record. A host that knows better passes
 * `canUpload` / `canDownload`; otherwise download follows `accessLevel` (Collaborate / Full Access), and Upload follows
 * the BFF's `canCreate` on the record's document list (a matter, work assignment or service request), or, on a project,
 * that `canCreate` AND `accessLevel` (R4 D-9: the shared `listCreateRightOf` rule; a list that answered without
 * `canCreate` follows the level; nothing is offered before the list answers). For a caller whose write access is decided
 * per record (`writeAccessPerRecord`, task 036), download and version history are offered wherever the list rendered (the
 * list already passed the Read the download routes need), and a project's Upload follows its list's `canCreate` alone.
 * This is only what is SHOWN: the server decides, and a refused upload shows its 403 as an error.
 *
 * DELETE-OWN (task 054, FR-26). With a `caller` identity AND the same write verdict that shows Upload, a row the caller
 * created (creator stamp = the signed-in principal) shows Delete, which asks first (shared ConfirmModal). 204 removes the
 * row; a 403 / 409 / 503 / 500 shows a message and the row stays. Without `caller`, or for a View-only caller, there is no
 * Delete.
 *
 * Displays the record's documents on the SHARED grid (`<ExternalDataGrid configId={RECORD_DOCUMENTS_GRID}>`, R4 task 023c,
 * FR-16): the grid takes its columns, resize, ellipsis + tooltip and lazy scroll from the "External record documents" grid
 * configuration, and its rows from this component's `rowSource`, which is the record route's list read (the authorised
 * path: Read on the parent, the secure-roots rule, invoice-linked hiding, `truncated`, `canCreate`). Download, Version
 * history and Delete are the grid's row actions; the name opens the document's record in the record template. The list is
 * read once per load or refresh (the same response gives the rows and `canCreate`); an upload or a delete bumps
 * `rowSourceKey`, which re-reads.
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
  makeStyles,
  tokens,
  Button,
  Spinner,
  Text,
  MessageBar,
  MessageBarBody,
  Divider,
} from '@fluentui/react-components';
import {
  ArrowDownloadRegular,
  ArrowUploadRegular,
  DeleteRegular,
  DocumentRegular,
  HistoryRegular,
} from '@fluentui/react-icons';
// Deep component imports — avoids loading Virtualized* components that need react-window
import { EmptyState } from '@spaarke/ui-components/components/EmptyState';
import { FileUploadZone, UploadedFileList } from '@spaarke/ui-components/components/FileUpload';
import type { IUploadedFile, IFileValidationError } from '@spaarke/ui-components/components/FileUpload';
import { FormModal, SprkModal } from '@spaarke/ui-components/components/SprkModal';
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
import {
  currentFocusTarget,
  type FocusTarget,
  restoreFocusTo,
  useChildDialog,
  useOfferCreateAction,
  type CreateActionReporter,
} from './createAction';
import { bffApiCall, bffApiBlob, problemMessage } from '../auth/bff-client';
import { levelVerdictOf, listCreateRightOf } from './record/recordAccess';
import { gridDataverseClient } from '../services/gridDataverseClient';
import { ExternalDataGrid } from '../widgets/ExternalDataGrid';
import { useGridSettings } from '../widgets/useGridSettings';
import { useAmbientTheme } from '../widgets/useAmbientTheme';
import { saveBlobAs } from './saveBlobAs';
import { AccessLevel, ApiError } from '../types';

/**
 * The `sprk_gridconfiguration` record the record Documents list renders with ("External record documents": Name, Type,
 * Date Added, AI Summary; `config/external-record-documents.gridconfiguration.json`, written live by task 028; its id must be
 * in the BFF's `OutsideCounselGridConfigurationIds`, task 023b). The ONE place the SPA names it.
 */
export const RECORD_DOCUMENTS_GRID = '286be8b4-f96f-4d5a-b1d5-82fd83ca9693';

/** The grid's row-source type, read off the wrapper's props (`typeof` is the one non-JSX use the grid guard allows). */
type DocumentsRowSource = NonNullable<React.ComponentProps<typeof ExternalDataGrid>['rowSource']>;
type DocumentsRecordAction = NonNullable<React.ComponentProps<typeof ExternalDataGrid>['onRecordAction']>;
type DocumentsRecordOpen = NonNullable<React.ComponentProps<typeof ExternalDataGrid>['onRecordOpen']>;

/** Ids of the grid's row actions (`onRecordAction`'s `actionId`). */
const ACTION_DOWNLOAD = 'download';
const ACTION_VERSIONS = 'versions';
const ACTION_DELETE = 'delete';

/**
 * A document as a grid row. The configuration's primary name column is `sprk_documentname`, but the record route names the
 * document `sprk_name`: the row carries both. Every other member (id, type, summary, date, `hasFile`, the creator stamps)
 * is the route's own, so the row actions read them back from the row the grid hands over.
 */
function gridRowOf(doc: ODataDocument): Record<string, unknown> {
  return { ...doc, sprk_documentname: doc.sprk_name };
}

/** The document a grid row stands for (the row is `gridRowOf`'s: the route's document plus the grid's own members). */
function documentOfRow(row: Record<string, unknown>): ODataDocument {
  return row as unknown as ODataDocument;
}

/** What the grid's error banner says when the list could not be read (a message fit for users: no status, no detail). */
const LIST_LOAD_FAILED = 'Failed to load documents. Please try refreshing the page.';

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
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalM,
  },
  // The shared grid fills its parent (height 100%, its rows scroll inside). A list in a record card has no height of its
  // own, so this host gives the grid a ceiling: with a few rows the grid is as tall as its rows, with many it scrolls.
  gridHost: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    minWidth: 0,
    maxHeight: 'min(70vh, 720px)',
  },
  // The shared EmptyState leaves the icon's colour to its caller (R4 task 024, FR-17).
  emptyIcon: {
    color: tokens.colorNeutralForeground4,
  },
  loadingContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '200px',
    gap: tokens.spacingVerticalM,
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
export function formatFileSize(bytes: number | null | undefined): string {
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
export function formatDate(isoDate: string | null | undefined): string {
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
  saveBlobAs(await bffApiBlob(documentContentPath(root, recordId, documentId)), fileName);
}

// saveBlobAs lives in its own file: the view-selector guard (ExternalSpaGridViewSelectorGuardTests) refuses DOM element
// creation calls in any file that mounts the shared grid, and this file does. Re-exported for its other users.
export { saveBlobAs };

/** A record a document is filed to: the content and versions routes are scoped by it. */
export interface DocumentParent {
  root: DocumentRoot;
  id: string;
}

/**
 * Runs `read` through each parent of a document filed to several records, in order (R4 task 023c, D-23 #2928: the one loop
 * the download and the preview share). It moves to the next parent ONLY when the server refuses with 403 (that parent is not
 * readable by the caller, or does not hold the document). Any other failure stops at once (a 5xx on one parent says nothing
 * about the next, and retrying hides it). When every parent refuses, the last 403 is thrown. An empty list is a programming
 * error: the host offers no Download or preview then.
 */
export async function readFromParents<T>(
  parents: ReadonlyArray<DocumentParent>,
  read: (parent: DocumentParent) => Promise<T>
): Promise<T> {
  let lastRefusal: unknown = new Error('No parent record to read the document through.');
  for (const parent of parents) {
    try {
      return await read(parent);
    } catch (err) {
      if (!(err instanceof ApiError) || err.statusCode !== 403) throw err;
      lastRefusal = err;
    }
  }
  throw lastRefusal;
}

/**
 * Downloads a document filed to several records (record detail, task 066) through `readFromParents`: each parent's content
 * route in order, the next only after a 403.
 */
export async function downloadDocumentFromParents(
  parents: ReadonlyArray<DocumentParent>,
  documentId: string,
  fileName: string
): Promise<void> {
  await readFromParents(parents, parent => downloadDocumentFile(parent.root, parent.id, documentId, fileName));
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

  // A read-only list: the base SprkModal (task 004, FR-03). `BrowseModal` is a record-browse preset (a stage + metadata grid
  // with a "N of M" nav), which a version list is not. Light dismiss (Esc, backdrop, ×) closes it, and the footer's single
  // Close is the right-hand primary, as the other Close-only presets (`PreviewModal`, `BrowseModal`) place it.
  return (
    <SprkModal
      open={open}
      onClose={onClose}
      title={`Version History — ${documentName}`}
      size="sm"
      footer={
        <Button appearance="primary" onClick={onClose}>
          Close
        </Button>
      }
    >
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
        <EmptyState
          size="compact"
          icon={<HistoryRegular className={styles.emptyIcon} />}
          heading="No version history"
          description="No version history available for this document."
        />
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
    </SprkModal>
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
  /** Called with `true` while the upload is in flight and `false` otherwise (and on unmount), so a host can hold. */
  onBusyChange?: (busy: boolean) => void;
}

const UploadDialog: React.FC<UploadDialogProps> = ({
  root,
  recordId,
  open,
  onClose,
  onUploadComplete,
  onBusyChange,
}) => {
  const styles = useStyles();
  const noun = RECORD_NOUN[root];
  const [selectedFiles, setSelectedFiles] = React.useState<IUploadedFile[]>([]);
  const [validationErrors, setValidationErrors] = React.useState<IFileValidationError[]>([]);
  const [uploading, setUploading] = React.useState<boolean>(false);
  const [uploadError, setUploadError] = React.useState<string | null>(null);

  // The record template holds browsing, Close and Refresh while the post is in flight (task 004). The latest callback is
  // read through a ref so a new function identity never re-fires the effect, and unmount always reports "not busy".
  const onBusyChangeRef = React.useRef(onBusyChange);
  onBusyChangeRef.current = onBusyChange;
  React.useEffect(() => {
    onBusyChangeRef.current?.(uploading);
  }, [uploading]);
  React.useEffect(
    () => () => {
      onBusyChangeRef.current?.(false);
    },
    []
  );

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

  // A FormModal (task 004, FR-03): Cancel in the left footer slot, Upload on the right. Esc does nothing (modal
  // standard: an explicit dialog keeps a draft), so typed input survives a stray Esc.
  return (
    <FormModal
      open={open}
      onClose={handleDialogClose}
      onSubmit={() => void handleUpload()}
      title="Upload Document"
      submitLabel={uploading ? 'Uploading…' : 'Upload'}
      submitDisabled={selectedFiles.length === 0}
      busy={uploading}
    >
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
    </FormModal>
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
   * The level's verdict on Upload, decided by the host. When the host passes none: a project uses `accessLevel`
   * (Collaborate / Full Access) for a caller whose write access is not per record; any other record follows the BFF's
   * `canCreate` on its document list (the right the upload route checks), else `accessLevel`. On a project (and inside the
   * record template) the list's `canCreate` must agree (D-9). The server enforces Create either way.
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
  /**
   * Record template only (task 004, FR-03; task 009). When given, the Upload in the Documents section's header is the
   * entry point: this list shows no "Upload Document" button of its own, offers its Upload action through this callback
   * (shown exactly when its own button would have been) and renders the upload dialog itself, so the dialog is a
   * descendant of the host.
   * Omit it everywhere else (the project page keeps its button).
   */
  onCreateActionChange?: CreateActionReporter;
  /** Called with `true` while an upload is in flight and `false` otherwise (and on unmount), so the host can hold. */
  onBusyChange?: (busy: boolean) => void;
  /**
   * Opens the document's record (task 013, owner 2026-11-14; the Project page too since the task 023c review). A click on
   * a document's name calls this with the document's id and the control that was activated (focus returns to it when the
   * record opened over this one closes; null when focus is on no control); a document with no file (`hasFile === false`)
   * is still opened (FR-15: its record shows Information only). The record template passes its child-record opener, the
   * Project page the shell's `useOpenRecord`. With none, the name is still the grid's link (the shared grid has no
   * plain-text name) and a click does nothing; no host in the app omits it where a Documents list can be shown.
   */
  onOpenDocument?: (documentId: string, trigger: HTMLElement | null) => void;
};

// ---------------------------------------------------------------------------
// DocumentLibrary — main component
// ---------------------------------------------------------------------------

/** The grid's noun for its rows (its incomplete-list notice says "only 1 document could be shown"). */
const DOCUMENT_NOUN = { one: 'document', other: 'documents' } as const;

/**
 * DocumentLibrary — a record's documents: the Project page's Documents card and the record template's Documents section.
 *
 * Features:
 * - The list is the shared grid (`RECORD_DOCUMENTS_GRID`) over the record route's list read (`getDocuments` /
 *   `getRecordDocuments`), one page; the grid shows the loading, error, empty and incomplete-list states
 * - Columns (the grid configuration): Name, Type, Date Added, AI Summary; resize, ellipsis and tooltip are the grid's
 * - Row actions: Download and Version history (not on a row with `hasFile === false`), Delete (the caller's own rows, and
 *   only for a caller who may upload)
 * - Upload button: Collaborate and Full Access only (on the Project page; the record template offers its own)
 * - Version history dialog via BFF API GET /api/v1/external/{root}/{id}/documents/{documentId}/versions
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
  onCreateActionChange,
  onBusyChange,
  onOpenDocument,
}) => {
  const styles = useStyles();
  const theme = useAmbientTheme();
  // The props type refuses a call with neither; an empty id that still arrives (a caller that casts, or a blank id from
  // upstream) is an error state below: the grid is not mounted, so nothing is read and nothing spins.
  const recordId = recordIdProp ?? projectId ?? '';

  // ---------------------------------------------------------------------------
  // State
  // ---------------------------------------------------------------------------

  // The BFF's raw `canCreate` on this record's document list (null = not said, or not loaded yet). How it is read depends
  // on `writeAccessPerRecord`, which is applied at render (below), never baked in by the read: `/me` may answer after the
  // list did, and a stored, already-interpreted value would then be stale (task 036 fix round 2).
  const [listCanCreate, setListCanCreate] = React.useState<boolean | null>(null);
  // True once a list read has answered, so a re-read after an upload does not withdraw the section's Upload (and with it
  // the control focus returns to).
  const [loadedOnce, setLoadedOnce] = React.useState<boolean>(false);
  // Why the last list read failed (null = it did not); the grid shows the same text in its banner.
  const [error, setError] = React.useState<string | null>(null);
  // Bumped after an upload or a delete: the grid re-reads page 1 (`rowSourceKey`).
  const [reloadCount, setReloadCount] = React.useState<number>(0);
  const upload = useChildDialog();
  const [versionHistoryDoc, setVersionHistoryDoc] = React.useState<ODataDocument | null>(null);
  // The control that opened the version history, so focus returns to it when the dialog closes.
  const versionInvoker = React.useRef<FocusTarget | null>(null);
  // Documents whose download is in flight: a second click on the same row waits for the first.
  const downloading = React.useRef<Set<string>>(new Set());
  // Task 066: why the last download failed (null = none); cleared when the next download starts.
  const [downloadError, setDownloadError] = React.useState<string | null>(null);

  // The Upload right (task 006, FR-06: `listCreateRightOf` / `createRightOf`, the rule EventsCalendar shares
  // too): offered exactly when it is known to be `true`. The list must have answered (a re-read after an upload keeps
  // the last answer, so the action does not blink out) and its `canCreate` must allow. A level that decides must allow
  // too: the host's `canUpload` (the record template's `uploadOverride`), else, on a project, the `/me` level for a caller
  // whose write access is not per record (`levelVerdictOf`; a per-record caller follows the list alone, task 036 fix round
  // 1). D-9 (#2716): the project page used to offer Upload on that level alone, so a `canCreate: false` (a workforce role
  // ceiling) was offered an upload the POST refuses with 403.
  const levelAllows =
    canUpload ?? (root === 'projects' ? levelVerdictOf(accessLevel, writeAccessPerRecord) : undefined);
  const { right, allowed: uploadAllowed } = listCreateRightOf({
    loaded: loadedOnce && !error,
    listCanCreate,
    levelAllows,
  });
  // The list's own button (outside the template). A project follows the shared
  // rule; every other record keeps its R3 rule, unchanged: the host's `canUpload`, else the list's `canCreate`, else
  // `accessLevel`.
  const showUpload =
    root === 'projects' ? uploadAllowed : (canUpload ?? listCanCreate ?? canUploadOrDownload(accessLevel));
  // In the record template the section's Upload replaces this list's button (task 004 / 009), and the dialog stays here.
  const ownUploadButton = onCreateActionChange === undefined;
  useOfferCreateAction(onCreateActionChange, uploadAllowed, upload.show, right);
  // Whether an Upload is offered at all: this list's own button, or (in the record template) the section's. The empty
  // state names the action only then (hide, don't disable; task 024).
  const uploadOffered = ownUploadButton ? showUpload : uploadAllowed;
  const noun = RECORD_NOUN[root];
  // Download / version history need only Read on the record (the server checks it). A per-record caller's level says only
  // what they may SEE, and the list rendering at all proves that Read, so they are offered (task 036 fix round 2).
  const canActOnDocuments = canDownload ?? (writeAccessPerRecord || canUploadOrDownload(accessLevel));

  // Delete-own (task 054): the server's 204 re-reads the list (the grid's `rowSourceKey`); any refusal keeps the row and is
  // shown.
  const del = useDeleteOwn<ODataDocument>({
    noun: 'document',
    remove: doc => deleteOwnDocument(doc.sprk_documentid),
    onRemoved: () => setReloadCount(n => n + 1),
  });
  const askDelete = del.ask;

  // ---------------------------------------------------------------------------
  // Rows: the grid's row source is the record route's list read (one call per load or refresh)
  // ---------------------------------------------------------------------------

  const loadRows = React.useCallback<DocumentsRowSource>(
    async ({ signal }) => {
      try {
        // The project read keeps its own client function (`getDocuments`); the other roots share the task 031 route and
        // also say whether the caller may upload.
        const data = root === 'projects' ? await getDocuments(recordId) : await getRecordDocuments(root, recordId);
        if (!signal.aborted) {
          setListCanCreate(data.canCreate ?? null);
          setLoadedOnce(true);
          setError(null);
        }
        // One page: the route answers its capped list at once, and says when it was cut short (`truncated`).
        return { rows: data.items.map(gridRowOf), hasMore: false, truncated: data.truncated };
      } catch (err) {
        console.error('[DocumentLibrary] Failed to load documents:', err);
        if (!signal.aborted) setError(LIST_LOAD_FAILED);
        // The grid shows this message as is: no status code, no server detail.
        throw new Error(LIST_LOAD_FAILED);
      }
    },
    [root, recordId]
  );
  const rowSourceKey = `${root}:${recordId}:${reloadCount}`;

  // ---------------------------------------------------------------------------
  // Download handler
  // ---------------------------------------------------------------------------

  const handleDownload = React.useCallback(
    async (doc: ODataDocument) => {
      if (!canActOnDocuments) return;
      const documentId = doc.sprk_documentid;
      if (downloading.current.has(documentId)) return;
      downloading.current.add(documentId);
      setDownloadError(null);

      try {
        await downloadDocumentFile(root, recordId, documentId, doc.sprk_name);
      } catch (err) {
        console.error(`[DocumentLibrary] Download failed for document ${documentId}: ${downloadFailureLogText(err)}`);
        // Task 066: a failed download is shown, not only logged.
        setDownloadError(downloadFailureMessage(err));
      } finally {
        downloading.current.delete(documentId);
      }
    },
    [canActOnDocuments, root, recordId]
  );

  // ---------------------------------------------------------------------------
  // Row actions and row open (the grid's seams)
  // ---------------------------------------------------------------------------

  // Hide, don't disable (FR-06): an action is in the list only when the caller may use it at all, and `isVisible` hides it on
  // the rows it does not apply to. R3 ISS-011 (#1886): a document with no file stays listed but offers neither Download nor
  // Version history (both would answer 409 no_file_attached). Only an explicit false hides them: a BFF that does not send
  // `hasFile` is an old BFF, and its rows keep today's behaviour (FR-15). Delete is in the list only for a caller whose
  // write verdict is a known yes (`uploadAllowed`, the verdict that shows Upload: a View-only caller is offered no delete
  // the server would refuse), and `isVisible` shows it only on a row the caller created (the server re-checks; a refusal
  // is shown as an error).
  const rowActions = React.useMemo(() => {
    const hasFile = (row: Record<string, unknown>): boolean => row.hasFile !== false;
    return [
      ...(canActOnDocuments
        ? [
            { id: ACTION_DOWNLOAD, label: 'Download', icon: <ArrowDownloadRegular />, isVisible: hasFile },
            { id: ACTION_VERSIONS, label: 'Version history', icon: <HistoryRegular />, isVisible: hasFile },
          ]
        : []),
      ...(caller && uploadAllowed
        ? [
            {
              id: ACTION_DELETE,
              label: 'Delete',
              icon: <DeleteRegular />,
              isVisible: (row: Record<string, unknown>) => isCreatedByCaller(documentOfRow(row), caller),
            },
          ]
        : []),
    ];
  }, [canActOnDocuments, caller, uploadAllowed]);

  const handleRecordAction = React.useCallback<DocumentsRecordAction>(
    (actionId, _recordId, row) => {
      const doc = documentOfRow(row);
      switch (actionId) {
        case ACTION_DOWNLOAD:
          void handleDownload(doc);
          break;
        case ACTION_VERSIONS:
          versionInvoker.current = currentFocusTarget();
          setVersionHistoryDoc(doc);
          break;
        case ACTION_DELETE:
          askDelete(doc, doc.sprk_name);
          break;
        default:
          break;
      }
    },
    [handleDownload, askDelete]
  );

  // The name is the grid's primary-name link. The record template opens the document's record over this one and the Project
  // page through the shell's opener. With no opener the click is a no-op: the grid cannot draw the name as plain text, and
  // its own default (Xrm navigation) must not run in this Xrm-free host, so a handler is always passed.
  const handleRecordOpen = React.useCallback<DocumentsRecordOpen>(
    documentId => {
      if (!onOpenDocument) return;
      const active = document.activeElement;
      onOpenDocument(documentId, active instanceof HTMLElement && active !== document.body ? active : null);
    },
    [onOpenDocument]
  );

  // The grid's column widths and sort are remembered per browser and "Clear all" (which also clears the column filters) is a command of its toolbar (FR-19, task 032).
  const { gridProps: gridSettingsProps, resetCommand } = useGridSettings(RECORD_DOCUMENTS_GRID);
  const gridCommands = React.useMemo(() => [resetCommand], [resetCommand]);

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  if (!recordId) {
    return (
      <MessageBar intent="error">
        <MessageBarBody>Documents cannot be loaded: no record was given.</MessageBarBody>
      </MessageBar>
    );
  }

  return (
    <div className={styles.root}>
      <DeleteFailureNotice del={del} />
      {downloadError && (
        <MessageBar intent="error" data-testid="document-download-error">
          <MessageBarBody>{downloadError}</MessageBarBody>
        </MessageBar>
      )}

      {/* Toolbar: the list's own Upload (outside the template, once the list has answered). */}
      {ownUploadButton && loadedOnce && !error && showUpload && (
        <div className={styles.toolbar}>
          <Button appearance="primary" icon={<ArrowUploadRegular />} onClick={upload.show}>
            Upload Document
          </Button>
        </div>
      )}

      {/* Document grid: columns, resize, ellipsis + tooltip, lazy scroll and the incomplete-list notice are the shared grid's. */}
      <div className={styles.gridHost}>
        <ExternalDataGrid
          configId={RECORD_DOCUMENTS_GRID}
          dataverseClient={gridDataverseClient}
          theme={theme}
          rowSource={loadRows}
          rowSourceKey={rowSourceKey}
          rowActions={rowActions}
          onRecordAction={handleRecordAction}
          onRecordOpen={handleRecordOpen}
          truncateWithTooltip
          hostCommands={gridCommands}
          {...gridSettingsProps}
          rowNoun={DOCUMENT_NOUN}
          emptyState={
            <EmptyState
              size="compact"
              icon={<DocumentRegular className={styles.emptyIcon} />}
              heading="No Documents"
              description={
                uploadOffered
                  ? ownUploadButton
                    ? `No documents have been uploaded to this ${noun} yet. Use the Upload button to add your first document.`
                    : `No documents have been uploaded to this ${noun} yet. Use Upload above to add your first document.`
                  : `No documents have been uploaded to this ${noun} yet.`
              }
            />
          }
        />
      </div>

      {/* Upload dialog */}
      <UploadDialog
        root={root}
        recordId={recordId}
        open={upload.open}
        onClose={upload.hide}
        onUploadComplete={() => setReloadCount(n => n + 1)}
        onBusyChange={onBusyChange}
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
          onClose={() => {
            setVersionHistoryDoc(null);
            restoreFocusTo(versionInvoker.current);
            versionInvoker.current = null;
          }}
        />
      )}
    </div>
  );
};

export default DocumentLibrary;
