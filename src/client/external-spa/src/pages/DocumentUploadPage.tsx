/**
 * DocumentUploadPage.tsx
 *
 * SPA page that hosts a document upload wizard for external users (CIAM partners and workforce users alike).
 * Composes the shared WizardShell with FileUploadZone and UploadedFileList, and uploads each file through the
 * EXTERNAL plane's record document route (spaarke-SPA-external-access-platform-r3 task 042, FR-18, #1566):
 *
 *   POST /api/v1/external/{projects|matters|workassignments|servicerequests}/{id}/documents   (task 031, FR-24)
 *
 * via `bffApiCall` — the active backend's URL and token (task 041), the same call on both planes. The server checks
 * the record against the caller's accessible set (Read, then Create; a service request's requester only), resolves
 * the storage container from the record, and stamps the creator. Before task 042 this page built its own
 * token-acquiring fetch with the CIAM acquirer and a shared adapter that targeted an OBO route, so it failed on the
 * workforce plane and called a route outside the external plane.
 *
 * Route: /upload?entityType={entityType}&entityId={entityId}
 *
 * Query Parameters:
 *   - entityType (required): sprk_project | sprk_matter | sprk_workassignment | sprk_servicerequest
 *   - entityId   (required): the record GUID
 *   - containerId — IGNORED. The server derives the container from the record; a client never names one (#858).
 *
 * Refusals: a 401 shows the sign-in state and a 403 the denied state (BffAccessState), beside the list of files that
 * had already uploaded; an unsupported type or a malformed id is refused before any request; no active backend → the
 * upload fails closed (bffApiCall sends nothing). A per-file failure shows the server's ProblemDetails detail; a batch
 * in which nothing uploaded ends on a warning, not the green checkmark.
 *
 * ADR-021: All styles use Fluent v9 design tokens. No hard-coded colors.
 * ADR-022: React 18 functional component (createRoot is in main.tsx).
 * ADR-007: File uploads go through BFF API -> SpeFileStore facade.
 * ADR-012: Reuses shared FileUpload and Wizard components from @spaarke/ui-components.
 */
import * as React from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { makeStyles, tokens, Text, MessageBar, MessageBarBody, Button } from '@fluentui/react-components';
import { CheckmarkCircleRegular, WarningRegular } from '@fluentui/react-icons';

import { WizardShell } from '@spaarke/ui-components/components/Wizard';
import type {
  IWizardStepConfig,
  IWizardSuccessConfig,
} from '@spaarke/ui-components/components/Wizard/wizardShellTypes';
import { FileUploadZone, UploadedFileList } from '@spaarke/ui-components/components/FileUpload';
import type { IUploadedFile, IFileValidationError } from '@spaarke/ui-components/components/FileUpload/fileUploadTypes';

import { bffApiCall, problemMessage } from '../auth/bff-client';
import { ApiError } from '../types';
import { PageContainer, NavigationBar } from '../components';
import { BffAccessState, accessFailureOf, type BffAccessFailure } from '../components/auth/BffAccessState';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  errorContainer: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalL,
  },
  stepContent: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  validationErrors: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  uploadProgress: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    paddingTop: tokens.spacingVerticalM,
  },
  uploadItem: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  uploadItemName: {
    flex: '1 1 auto',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  fileCount: {
    color: tokens.colorNeutralForeground3,
  },
  summarySection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
  },
});

// ---------------------------------------------------------------------------
// External document roots and query param parsing
// ---------------------------------------------------------------------------

/** A record type that has an external document route (task 031). */
export interface ExternalDocumentRoot {
  /** The route segment under /api/v1/external. */
  segment: 'projects' | 'matters' | 'workassignments' | 'servicerequests';
  /** The noun used in the page copy. */
  noun: string;
}

/** Dataverse logical name → external document route. Any other type has no external upload route. */
const EXTERNAL_DOCUMENT_ROOTS: Readonly<Record<string, ExternalDocumentRoot>> = {
  sprk_project: { segment: 'projects', noun: 'project' },
  sprk_matter: { segment: 'matters', noun: 'matter' },
  sprk_workassignment: { segment: 'workassignments', noun: 'work assignment' },
  sprk_servicerequest: { segment: 'servicerequests', noun: 'service request' },
};

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

type UploadPageParams =
  | { kind: 'ok'; entityType: string; entityId: string; root: ExternalDocumentRoot }
  | { kind: 'missing' }
  | { kind: 'unsupported'; entityType: string }
  | { kind: 'invalid-id' };

function parseQueryParams(search: string): UploadPageParams {
  const params = new URLSearchParams(search);
  const entityType = params.get('entityType');
  const entityId = params.get('entityId');

  if (!entityType || !entityId) return { kind: 'missing' };
  const root = Object.prototype.hasOwnProperty.call(EXTERNAL_DOCUMENT_ROOTS, entityType)
    ? EXTERNAL_DOCUMENT_ROOTS[entityType]
    : undefined;
  if (!root) return { kind: 'unsupported', entityType };
  if (!GUID_PATTERN.test(entityId)) return { kind: 'invalid-id' };
  return { kind: 'ok', entityType, entityId, root };
}

/**
 * Upload one file to a record through its external document route — `bffApiCall` only (the active backend's URL and
 * token on either plane; no page-built fetch). Rejects with the ApiError of a refusal.
 */
export async function uploadDocumentToRecord(root: ExternalDocumentRoot, entityId: string, file: File): Promise<void> {
  if (!GUID_PATTERN.test(entityId)) throw new Error('Invalid record id.');
  const formData = new FormData();
  formData.append('file', file);
  // Omit Content-Type: the browser sets multipart/form-data with its boundary (bff-client drops the JSON default).
  await bffApiCall<unknown>(`/api/v1/external/${root.segment}/${entityId}/documents`, {
    method: 'POST',
    body: formData,
    headers: {},
  });
}

/**
 * The per-file message for a non-auth failure (a 401/403 ends the whole upload instead). The server's ProblemDetails
 * `detail` is shown when it wrote one (`problemMessage`): the collision 409 names the file, the owner-refusal 409
 * ("Record owner unresolved") says why the record cannot take documents, a 503 says to retry. The status-based text
 * is only the fallback for a response that carries no detail (a proxy error, an empty body).
 */
function uploadFailureMessage(err: unknown, fileName: string, noun: string): string {
  const statusCode = err instanceof ApiError ? err.statusCode : undefined;
  let fallback = 'Upload failed.';
  if (statusCode === 409)
    fallback = `"${fileName}" could not be uploaded because of a conflict (for example a file with that name already exists). Nothing was changed.`;
  else if (statusCode === 413) fallback = `"${fileName}" is too large to upload.`;
  else if (statusCode === 422) fallback = `This ${noun} has no document storage configured yet.`;
  else if (statusCode !== undefined && statusCode >= 500) fallback = 'Upload failed. Please try again later.';
  return problemMessage(err, fallback);
}

// ---------------------------------------------------------------------------
// Utility: format bytes
// ---------------------------------------------------------------------------

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

// ---------------------------------------------------------------------------
// DocumentUploadPage
// ---------------------------------------------------------------------------

export interface DocumentUploadPageProps {
  /** True inside the Teams host — the sign-in state reloads the tab instead of redirecting. */
  teamsHost?: boolean;
}

/**
 * DocumentUploadPage renders a 2-step wizard for uploading documents to a record through the external plane.
 *
 * Step 1 - Select Files: Drag-and-drop or browse to select files.
 * Step 2 - Review & Upload: Review selected files, then upload on Finish.
 *
 * The wizard uses the shared WizardShell in standard (dialog overlay) mode.
 * The page itself is a full route — the wizard opens automatically when the
 * page mounts and navigates back on close.
 */
export const DocumentUploadPage: React.FC<DocumentUploadPageProps> = ({ teamsHost = false }) => {
  const styles = useStyles();
  const navigate = useNavigate();
  const location = useLocation();

  // Parse query params from the route's search string (BrowserRouter clean URL)
  const pageParams = React.useMemo(() => parseQueryParams(location.search), [location.search]);
  const target = pageParams.kind === 'ok' ? pageParams : null;
  const projectId = target?.root.segment === 'projects' ? target.entityId : null;

  // ── File state ──────────────────────────────────────────────────────────
  const [selectedFiles, setSelectedFiles] = React.useState<IUploadedFile[]>([]);
  const [validationErrors, setValidationErrors] = React.useState<IFileValidationError[]>([]);
  // A 401 / 403 from the upload route replaces the wizard with the sign-in / denied state.
  const [accessFailure, setAccessFailure] = React.useState<BffAccessFailure | null>(null);
  // When the refusal came part-way through a batch: which files had already uploaded and which had not been sent.
  const [batchOutcome, setBatchOutcome] = React.useState<{ uploaded: string[]; notUploaded: string[] } | null>(null);

  // ── File handlers ───────────────────────────────────────────────────────
  const handleFilesAccepted = React.useCallback((files: IUploadedFile[]) => {
    setSelectedFiles(prev => [...prev, ...files]);
    // Clear validation errors when new files are accepted
    setValidationErrors([]);
  }, []);

  const handleValidationErrors = React.useCallback((errors: IFileValidationError[]) => {
    setValidationErrors(errors);
  }, []);

  const handleRemoveFile = React.useCallback((fileId: string) => {
    setSelectedFiles(prev => prev.filter(f => f.id !== fileId));
  }, []);

  // ── Navigation ──────────────────────────────────────────────────────────
  const handleClose = React.useCallback(() => {
    // Back to the project page for a project (the only record type with a page route here), otherwise home
    navigate(projectId ? `/project/${projectId}` : '/');
  }, [navigate, projectId]);

  // ── Upload (finish handler) ─────────────────────────────────────────────
  const handleFinish = React.useCallback(async (): Promise<IWizardSuccessConfig | void> => {
    if (!target || selectedFiles.length === 0) {
      return;
    }

    const results: { name: string; success: boolean; error?: string }[] = [];

    for (const [index, uploadedFile] of selectedFiles.entries()) {
      try {
        await uploadDocumentToRecord(target.root, target.entityId, uploadedFile.file);
        results.push({ name: uploadedFile.name, success: true });
      } catch (err) {
        const failure = accessFailureOf(err);
        if (failure) {
          // Not signed in / not allowed: stop — every further file would be refused the same way. The files that
          // already went through stay uploaded: say so beside the refusal, so nobody re-sends them.
          setBatchOutcome({
            uploaded: results.filter(r => r.success).map(r => r.name),
            notUploaded: [
              ...results.filter(r => !r.success).map(r => r.name),
              ...selectedFiles.slice(index).map(f => f.name),
            ],
          });
          setAccessFailure(failure);
          throw new Error(failure === 'signin' ? 'Sign in required.' : 'Access denied.');
        }
        results.push({
          name: uploadedFile.name,
          success: false,
          error: uploadFailureMessage(err, uploadedFile.name, target.root.noun),
        });
      }
    }

    const successCount = results.filter(r => r.success).length;
    const failedCount = results.filter(r => !r.success).length;
    const warnings = results.filter(r => !r.success).map(r => `${r.name}: ${r.error}`);

    return {
      // Nothing uploaded is not a success: a warning, never the green checkmark.
      icon:
        successCount === 0 ? (
          <WarningRegular
            data-testid="upload-result-warning-icon"
            style={{ fontSize: '48px', color: tokens.colorPaletteYellowForeground1 }}
          />
        ) : (
          <CheckmarkCircleRegular
            data-testid="upload-result-success-icon"
            style={{ fontSize: '48px', color: tokens.colorPaletteGreenForeground1 }}
          />
        ),
      title:
        failedCount === 0
          ? `${successCount} file${successCount !== 1 ? 's' : ''} uploaded successfully`
          : `${successCount} of ${results.length} files uploaded`,
      body: (
        <Text size={300}>
          {failedCount === 0
            ? `All files have been uploaded to the ${target.root.noun}'s document library.`
            : `${failedCount} file${failedCount !== 1 ? 's' : ''} failed to upload. See warnings below.`}
        </Text>
      ),
      actions: (
        <Button appearance="primary" onClick={handleClose}>
          {projectId ? 'Return to Project' : 'Return to Workspace'}
        </Button>
      ),
      warnings: warnings.length > 0 ? warnings : undefined,
    };
  }, [target, selectedFiles, handleClose, projectId]);

  // ── Guard: missing / unsupported params (no request is made) ───────────
  if (!target) {
    const message =
      pageParams.kind === 'unsupported'
        ? `Documents cannot be uploaded to this record type (${pageParams.entityType}) here.`
        : pageParams.kind === 'invalid-id'
          ? 'The record id in the link is not valid. Please navigate from the record.'
          : 'Missing required parameters. This page requires entityType and entityId query parameters. Please navigate from the record.';
    return (
      <PageContainer>
        <NavigationBar items={[{ label: 'My Projects', href: '/' }, { label: 'Upload Documents' }]} />
        <div className={styles.errorContainer}>
          <MessageBar intent="error">
            <MessageBarBody>{message}</MessageBarBody>
          </MessageBar>
        </div>
      </PageContainer>
    );
  }

  const breadcrumbs = [
    { label: 'My Projects', href: '/' },
    ...(projectId ? [{ label: 'Project', href: `/project/${projectId}` }] : []),
    { label: 'Upload Documents' },
  ];

  // ── Refused by the server: sign-in (401) or denied (403) ───────────────
  if (accessFailure) {
    return (
      <PageContainer>
        <NavigationBar items={breadcrumbs} />
        <div className={styles.errorContainer}>
          {batchOutcome && (batchOutcome.uploaded.length > 0 || batchOutcome.notUploaded.length > 0) && (
            <MessageBar
              intent={batchOutcome.uploaded.length > 0 ? 'info' : 'warning'}
              data-testid="upload-batch-outcome"
            >
              <MessageBarBody>
                {batchOutcome.uploaded.length > 0 && (
                  <div data-testid="upload-batch-uploaded">
                    Already uploaded before this stopped: {batchOutcome.uploaded.join(', ')}.
                  </div>
                )}
                {batchOutcome.notUploaded.length > 0 && (
                  <div data-testid="upload-batch-not-uploaded">
                    Not uploaded: {batchOutcome.notUploaded.join(', ')}.
                  </div>
                )}
              </MessageBarBody>
            </MessageBar>
          )}
          <BffAccessState
            failure={accessFailure}
            teamsHost={teamsHost}
            deniedMessage={`You do not have permission to upload documents to this ${target.root.noun}. If you believe this is an error, please contact the ${target.root.noun} team.`}
          />
        </div>
      </PageContainer>
    );
  }

  // ── Total file size ─────────────────────────────────────────────────────
  const totalSize = selectedFiles.reduce((sum, f) => sum + f.sizeBytes, 0);

  // ── Wizard step configs ─────────────────────────────────────────────────
  const stepConfigs: IWizardStepConfig[] = [
    {
      id: 'select-files',
      label: 'Select Files',
      renderContent: () => (
        <div className={styles.stepContent}>
          <Text size={400} weight="semibold">
            Select files to upload
          </Text>
          <Text size={300}>
            Drag and drop files below, or click to browse. Supported formats: PDF, DOCX, XLSX (max 10 MB each).
          </Text>

          <FileUploadZone onFilesAccepted={handleFilesAccepted} onValidationErrors={handleValidationErrors} />

          {/* Validation error messages */}
          {validationErrors.length > 0 && (
            <div className={styles.validationErrors}>
              {validationErrors.map((err, i) => (
                <MessageBar key={i} intent="warning">
                  <MessageBarBody>
                    {err.fileName}: {err.reason}
                  </MessageBarBody>
                </MessageBar>
              ))}
            </div>
          )}

          {/* List of accepted files */}
          {selectedFiles.length > 0 && (
            <>
              <Text size={200} className={styles.fileCount}>
                {selectedFiles.length} file
                {selectedFiles.length !== 1 ? 's' : ''} selected ({formatBytes(totalSize)} total)
              </Text>
              <UploadedFileList files={selectedFiles} onRemove={handleRemoveFile} />
            </>
          )}
        </div>
      ),
      canAdvance: () => selectedFiles.length > 0,
    },
    {
      id: 'review-upload',
      label: 'Review & Upload',
      renderContent: () => (
        <div className={styles.stepContent}>
          <Text size={400} weight="semibold">
            Review and confirm upload
          </Text>
          <Text size={300}>
            The following files will be uploaded to the {target.root.noun}&apos;s document library. Click Upload to
            start.
          </Text>

          <div className={styles.summarySection}>
            <Text size={300} weight="semibold">
              Upload Summary
            </Text>
            <Text size={200}>
              Record: {target.root.noun} ({target.entityId.substring(0, 8)}...)
            </Text>
            <Text size={200}>
              Files: {selectedFiles.length} ({formatBytes(totalSize)} total)
            </Text>
          </div>

          <UploadedFileList files={selectedFiles} onRemove={handleRemoveFile} />
        </div>
      ),
      canAdvance: () => selectedFiles.length > 0,
    },
  ];

  // ── Render ──────────────────────────────────────────────────────────────
  return (
    <PageContainer>
      <NavigationBar items={breadcrumbs} />

      <WizardShell
        open={true}
        title="Upload Documents"
        steps={stepConfigs}
        onClose={handleClose}
        onFinish={handleFinish}
        finishLabel="Upload"
        finishingLabel="Uploading..."
      />
    </PageContainer>
  );
};

export default DocumentUploadPage;
