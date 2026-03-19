/**
 * DocumentUploadPage.tsx
 *
 * Power Pages SPA page that hosts a document upload wizard for external users.
 * Composes the shared WizardShell with FileUploadZone and UploadedFileList
 * components, backed by BFF API adapters for authentication and file upload.
 *
 * Route: #/upload?entityType={entityType}&entityId={entityId}&containerId={containerId}
 *
 * Query Parameters:
 *   - entityType  (required): Dataverse entity logical name (e.g., "sprk_project")
 *   - entityId    (required): Dataverse record GUID
 *   - containerId (optional): SPE container ID (resolved via BFF if not provided)
 *
 * The page:
 *   1. Parses route query params for entity context
 *   2. Creates BFF upload service using the SPA's MSAL-authenticated fetch
 *   3. Renders a 2-step wizard: Select Files -> Upload & Confirm
 *   4. On completion, navigates back to the project page or closes
 *
 * ADR-021: All styles use Fluent v9 design tokens. No hard-coded colors.
 * ADR-022: React 18 functional component (createRoot is in main.tsx).
 * ADR-007: File uploads go through BFF API -> SpeFileStore facade.
 * ADR-012: Reuses shared FileUpload and Wizard components from @spaarke/ui-components.
 */
import * as React from "react";
import { useNavigate, useLocation } from "react-router-dom";
import {
  makeStyles,
  tokens,
  Text,
  MessageBar,
  MessageBarBody,
  Spinner,
  Badge,
  Button,
} from "@fluentui/react-components";
import {
  CheckmarkCircleRegular,
  DocumentRegular,
  ArrowUploadRegular,
} from "@fluentui/react-icons";

import { WizardShell } from "@spaarke/ui-components/components/Wizard";
import type {
  IWizardStepConfig,
  IWizardSuccessConfig,
} from "@spaarke/ui-components/components/Wizard/wizardShellTypes";
import {
  FileUploadZone,
  UploadedFileList,
} from "@spaarke/ui-components/components/FileUpload";
import type {
  IUploadedFile,
  IFileValidationError,
} from "@spaarke/ui-components/components/FileUpload/fileUploadTypes";
import { createBffUploadService } from "@spaarke/ui-components/utils/adapters/bffUploadServiceAdapter";
import type { AuthenticatedFetch } from "@spaarke/ui-components/utils/adapters/bffDataServiceAdapter";

import { BFF_API_URL } from "../config";
import { acquireBffToken } from "../auth/msal-auth";
import { PageContainer, NavigationBar } from "../components";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  errorContainer: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalL,
  },
  stepContent: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
  },
  validationErrors: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
  },
  uploadProgress: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
    paddingTop: tokens.spacingVerticalM,
  },
  uploadItem: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  uploadItemName: {
    flex: "1 1 auto",
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  fileCount: {
    color: tokens.colorNeutralForeground3,
  },
  summarySection: {
    display: "flex",
    flexDirection: "column",
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
// Authenticated fetch wrapper for BFF adapters
// ---------------------------------------------------------------------------

/**
 * Creates an authenticated fetch function that attaches MSAL Bearer tokens.
 * Used by BFF adapter factory functions.
 */
function createAuthenticatedFetch(): AuthenticatedFetch {
  return async (url: string, init?: RequestInit): Promise<Response> => {
    const token = await acquireBffToken();
    return fetch(url, {
      ...init,
      headers: {
        ...init?.headers,
        Authorization: `Bearer ${token}`,
      },
    });
  };
}

// ---------------------------------------------------------------------------
// Query param parsing
// ---------------------------------------------------------------------------

interface UploadPageParams {
  entityType: string;
  entityId: string;
  containerId?: string;
}

function parseQueryParams(search: string): UploadPageParams | null {
  const params = new URLSearchParams(search);
  const entityType = params.get("entityType");
  const entityId = params.get("entityId");

  if (!entityType || !entityId) {
    return null;
  }

  return {
    entityType,
    entityId,
    containerId: params.get("containerId") ?? undefined,
  };
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

/**
 * DocumentUploadPage renders a 2-step wizard for uploading documents to a
 * Dataverse entity's SPE container via the BFF API.
 *
 * Step 1 - Select Files: Drag-and-drop or browse to select files.
 * Step 2 - Review & Upload: Review selected files, then upload on Finish.
 *
 * The wizard uses the shared WizardShell in standard (dialog overlay) mode.
 * The page itself is a full route — the wizard opens automatically when the
 * page mounts and navigates back on close.
 */
export const DocumentUploadPage: React.FC = () => {
  const styles = useStyles();
  const navigate = useNavigate();
  const location = useLocation();

  // Parse query params from the hash route's search string
  const pageParams = React.useMemo(
    () => parseQueryParams(location.search),
    [location.search]
  );

  // ── File state ──────────────────────────────────────────────────────────
  const [selectedFiles, setSelectedFiles] = React.useState<IUploadedFile[]>([]);
  const [validationErrors, setValidationErrors] = React.useState<
    IFileValidationError[]
  >([]);

  // ── BFF services (stable refs) ──────────────────────────────────────────
  const authenticatedFetch = React.useMemo(() => createAuthenticatedFetch(), []);

  const uploadService = React.useMemo(
    () => createBffUploadService(authenticatedFetch, BFF_API_URL, acquireBffToken),
    [authenticatedFetch]
  );

  // ── File handlers ───────────────────────────────────────────────────────
  const handleFilesAccepted = React.useCallback(
    (files: IUploadedFile[]) => {
      setSelectedFiles((prev) => [...prev, ...files]);
      // Clear validation errors when new files are accepted
      setValidationErrors([]);
    },
    []
  );

  const handleValidationErrors = React.useCallback(
    (errors: IFileValidationError[]) => {
      setValidationErrors(errors);
    },
    []
  );

  const handleRemoveFile = React.useCallback((fileId: string) => {
    setSelectedFiles((prev) => prev.filter((f) => f.id !== fileId));
  }, []);

  // ── Navigation ──────────────────────────────────────────────────────────
  const handleClose = React.useCallback(() => {
    // Navigate back to the project page if we have an entityId, otherwise home
    if (pageParams?.entityId) {
      navigate(`/project/${pageParams.entityId}`);
    } else {
      navigate("/");
    }
  }, [navigate, pageParams]);

  // ── Upload (finish handler) ─────────────────────────────────────────────
  const handleFinish = React.useCallback(async (): Promise<
    IWizardSuccessConfig | void
  > => {
    if (!pageParams || selectedFiles.length === 0) {
      return;
    }

    const { entityType, entityId } = pageParams;
    const results: { name: string; success: boolean; error?: string }[] = [];

    for (const uploadedFile of selectedFiles) {
      try {
        await uploadService.uploadFile(entityType, entityId, uploadedFile.file, {
          onProgress: (_loaded, _total) => {
            // Progress tracking could be added here with additional state
          },
        });
        results.push({ name: uploadedFile.name, success: true });
      } catch (err) {
        const message =
          err instanceof Error ? err.message : "Upload failed";
        results.push({ name: uploadedFile.name, success: false, error: message });
      }
    }

    const successCount = results.filter((r) => r.success).length;
    const failedCount = results.filter((r) => !r.success).length;
    const warnings = results
      .filter((r) => !r.success)
      .map((r) => `${r.name}: ${r.error}`);

    return {
      icon: <CheckmarkCircleRegular style={{ fontSize: "48px", color: tokens.colorPaletteGreenForeground1 }} />,
      title:
        failedCount === 0
          ? `${successCount} file${successCount !== 1 ? "s" : ""} uploaded successfully`
          : `${successCount} of ${results.length} files uploaded`,
      body: (
        <Text size={300}>
          {failedCount === 0
            ? "All files have been uploaded to the project document library."
            : `${failedCount} file${failedCount !== 1 ? "s" : ""} failed to upload. See warnings below.`}
        </Text>
      ),
      actions: (
        <Button appearance="primary" onClick={handleClose}>
          Return to Project
        </Button>
      ),
      warnings: warnings.length > 0 ? warnings : undefined,
    };
  }, [pageParams, selectedFiles, uploadService, handleClose]);

  // ── Guard: missing params ───────────────────────────────────────────────
  if (!pageParams) {
    return (
      <PageContainer>
        <NavigationBar
          items={[
            { label: "My Projects", href: "#/" },
            { label: "Upload Documents" },
          ]}
        />
        <div className={styles.errorContainer}>
          <MessageBar intent="error">
            <MessageBarBody>
              Missing required parameters. This page requires entityType and
              entityId query parameters. Please navigate from the project
              documents tab.
            </MessageBarBody>
          </MessageBar>
        </div>
      </PageContainer>
    );
  }

  // ── Total file size ─────────────────────────────────────────────────────
  const totalSize = selectedFiles.reduce((sum, f) => sum + f.sizeBytes, 0);

  // ── Wizard step configs ─────────────────────────────────────────────────
  const stepConfigs: IWizardStepConfig[] = [
    {
      id: "select-files",
      label: "Select Files",
      renderContent: () => (
        <div className={styles.stepContent}>
          <Text size={400} weight="semibold">
            Select files to upload
          </Text>
          <Text size={300}>
            Drag and drop files below, or click to browse. Supported formats:
            PDF, DOCX, XLSX (max 10 MB each).
          </Text>

          <FileUploadZone
            onFilesAccepted={handleFilesAccepted}
            onValidationErrors={handleValidationErrors}
          />

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
                {selectedFiles.length !== 1 ? "s" : ""} selected (
                {formatBytes(totalSize)} total)
              </Text>
              <UploadedFileList
                files={selectedFiles}
                onRemove={handleRemoveFile}
              />
            </>
          )}
        </div>
      ),
      canAdvance: () => selectedFiles.length > 0,
    },
    {
      id: "review-upload",
      label: "Review & Upload",
      renderContent: () => (
        <div className={styles.stepContent}>
          <Text size={400} weight="semibold">
            Review and confirm upload
          </Text>
          <Text size={300}>
            The following files will be uploaded to the project document library.
            Click Finish to start the upload.
          </Text>

          <div className={styles.summarySection}>
            <Text size={300} weight="semibold">
              Upload Summary
            </Text>
            <Text size={200}>
              Entity: {pageParams.entityType} ({pageParams.entityId.substring(0, 8)}...)
            </Text>
            <Text size={200}>
              Files: {selectedFiles.length} ({formatBytes(totalSize)} total)
            </Text>
            {pageParams.containerId && (
              <Text size={200}>
                Container: {pageParams.containerId.substring(0, 8)}...
              </Text>
            )}
          </div>

          <UploadedFileList
            files={selectedFiles}
            onRemove={handleRemoveFile}
          />
        </div>
      ),
      canAdvance: () => selectedFiles.length > 0,
    },
  ];

  // ── Render ──────────────────────────────────────────────────────────────
  return (
    <PageContainer>
      <NavigationBar
        items={[
          { label: "My Projects", href: "#/" },
          ...(pageParams.entityId
            ? [{ label: "Project", href: `#/project/${pageParams.entityId}` }]
            : []),
          { label: "Upload Documents" },
        ]}
      />

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
