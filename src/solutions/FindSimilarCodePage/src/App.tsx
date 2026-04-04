/**
 * FindSimilarApp — Main component for the Find Similar Documents dialog.
 *
 * Two mutually exclusive paths:
 *   Path A: Select existing Document via Xrm lookup → open DocumentRelationshipViewer
 *   Path B: Upload file → POST /api/ai/visualization/related-from-content → open viewer
 *
 * Selecting a record clears the file and vice versa.
 */

import * as React from "react";
import {
  Button,
  Text,
  Divider,
  Spinner,
  makeStyles,
  tokens,
  shorthands,
} from "@fluentui/react-components";
import {
  DocumentSearchRegular,
  ArrowUploadRegular,
  DismissRegular,
  SearchRegular,
} from "@fluentui/react-icons";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IFindSimilarAppProps {
  apiBaseUrl: string;
  tenantId: string;
  authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    ...shorthands.padding("24px"),
    boxSizing: "border-box",
  },
  title: {
    fontSize: tokens.fontSizeBase500,
    fontWeight: tokens.fontWeightSemibold,
    marginBottom: "8px",
  },
  subtitle: {
    color: tokens.colorNeutralForeground2,
    marginBottom: "20px",
  },
  section: {
    display: "flex",
    flexDirection: "column",
    gap: "12px",
  },
  lookupRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: "8px",
  },
  lookupDisplay: {
    flex: 1,
    ...shorthands.padding("8px", "12px"),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.border("1px", "solid", tokens.colorNeutralStroke1),
    backgroundColor: tokens.colorNeutralBackground1,
    minHeight: "32px",
    display: "flex",
    alignItems: "center",
  },
  dividerRow: {
    display: "flex",
    alignItems: "center",
    gap: "12px",
    marginTop: "16px",
    marginBottom: "16px",
  },
  dividerLine: {
    flex: 1,
  },
  dropZone: {
    ...shorthands.border("2px", "dashed", tokens.colorNeutralStroke1),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.padding("24px"),
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: "8px",
    cursor: "pointer",
    minHeight: "80px",
    transition: "border-color 0.2s, background-color 0.2s",
    "&:hover": {
      borderColor: tokens.colorBrandStroke1,
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  dropZoneDragOver: {
    borderColor: tokens.colorBrandStroke1,
    backgroundColor: tokens.colorBrandBackground2,
  },
  fileInfo: {
    display: "flex",
    alignItems: "center",
    gap: "8px",
    ...shorthands.padding("8px", "12px"),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    backgroundColor: tokens.colorNeutralBackground3,
  },
  footer: {
    display: "flex",
    justifyContent: "flex-end",
    gap: "8px",
    marginTop: "auto",
    paddingTop: "20px",
  },
  errorText: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
  },
  spacer: {
    flex: 1,
  },
});

// ---------------------------------------------------------------------------
// Allowed file types
// ---------------------------------------------------------------------------

const ALLOWED_EXTENSIONS = [".pdf", ".docx", ".txt", ".md"];
const ALLOWED_MIME_TYPES = [
  "application/pdf",
  "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
  "text/plain",
  "text/markdown",
];
const MAX_FILE_SIZE_MB = 50;
const MAX_FILE_SIZE_BYTES = MAX_FILE_SIZE_MB * 1024 * 1024;

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function FindSimilarApp(props: IFindSimilarAppProps) {
  const { apiBaseUrl, tenantId, authenticatedFetch } = props;
  const styles = useStyles();
  const fileInputRef = React.useRef<HTMLInputElement>(null);

  // State: selected record (Path A)
  const [selectedRecord, setSelectedRecord] = React.useState<{
    id: string;
    name: string;
  } | null>(null);

  // State: uploaded file (Path B)
  const [selectedFile, setSelectedFile] = React.useState<File | null>(null);
  const [isDragOver, setIsDragOver] = React.useState(false);

  // Processing state
  const [isProcessing, setIsProcessing] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // Mutually exclusive: selecting record clears file, uploading file clears record
  const handleRecordSelected = React.useCallback(
    (id: string, name: string) => {
      setSelectedRecord({ id, name });
      setSelectedFile(null);
      setError(null);
    },
    []
  );

  const handleFileSelected = React.useCallback((file: File) => {
    // Validate extension
    const ext = "." + (file.name.split(".").pop()?.toLowerCase() ?? "");
    if (!ALLOWED_EXTENSIONS.includes(ext)) {
      setError(
        `File type '${ext}' is not supported. Allowed: ${ALLOWED_EXTENSIONS.join(", ")}`
      );
      return;
    }
    // Validate size
    if (file.size > MAX_FILE_SIZE_BYTES) {
      setError(
        `File size (${(file.size / (1024 * 1024)).toFixed(1)} MB) exceeds ${MAX_FILE_SIZE_MB} MB limit`
      );
      return;
    }
    setSelectedFile(file);
    setSelectedRecord(null);
    setError(null);
  }, []);

  // Xrm Lookup dialog for selecting a Document record
  const handleOpenLookup = React.useCallback(async () => {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (window.parent as any)?.Xrm ?? (window as any)?.Xrm;
      if (!xrm?.Utility?.lookupObjects) {
        setError("Xrm lookup is not available in this context");
        return;
      }
      const results = await xrm.Utility.lookupObjects({
        entityTypes: ["sprk_document"],
        allowMultiSelect: false,
      });
      if (results && results.length > 0) {
        const record = results[0];
        handleRecordSelected(
          record.id.replace(/[{}]/g, ""),
          record.name || "Selected Document"
        );
      }
    } catch (err) {
      console.warn("[FindSimilar] Lookup cancelled or failed:", err);
    }
  }, [handleRecordSelected]);

  // File input change handler
  const handleFileInputChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const file = e.target.files?.[0];
      if (file) handleFileSelected(file);
      // Reset input so re-selecting same file triggers change
      if (fileInputRef.current) fileInputRef.current.value = "";
    },
    [handleFileSelected]
  );

  // Drag-and-drop handlers
  const handleDragOver = React.useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      e.stopPropagation();
      setIsDragOver(true);
    },
    []
  );

  const handleDragLeave = React.useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      e.stopPropagation();
      setIsDragOver(false);
    },
    []
  );

  const handleDrop = React.useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      e.stopPropagation();
      setIsDragOver(false);
      const file = e.dataTransfer.files?.[0];
      if (file) handleFileSelected(file);
    },
    [handleFileSelected]
  );

  // Clear selected file
  const handleClearFile = React.useCallback(() => {
    setSelectedFile(null);
    setError(null);
  }, []);

  // Clear selected record
  const handleClearRecord = React.useCallback(() => {
    setSelectedRecord(null);
    setError(null);
  }, []);

  // ---------------------------------------------------------------------------
  // Find Similar action
  // ---------------------------------------------------------------------------

  const canSubmit = !isProcessing && (selectedRecord !== null || selectedFile !== null);

  const handleFindSimilar = React.useCallback(async () => {
    if (!canSubmit) return;
    setIsProcessing(true);
    setError(null);

    try {
      let documentId: string;

      if (selectedRecord) {
        // Path A: use existing document ID directly
        documentId = selectedRecord.id;
      } else if (selectedFile) {
        // Path B: upload file to BFF, get temp documentId
        const formData = new FormData();
        formData.append("file", selectedFile);

        const url = `${apiBaseUrl}/api/ai/visualization/related-from-content?tenantId=${encodeURIComponent(tenantId)}`;
        const response = await authenticatedFetch(url, {
          method: "POST",
          body: formData,
        });

        if (!response.ok) {
          const problem = await response.json().catch(() => null);
          throw new Error(
            problem?.detail ?? `Upload failed (${response.status})`
          );
        }

        const result = await response.json();
        if (!result.success || !result.documentId) {
          throw new Error(result.errorMessage ?? "Upload failed");
        }
        documentId = result.documentId;
      } else {
        return;
      }

      // Open DocumentRelationshipViewer with the documentId
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (window.parent as any)?.Xrm ?? (window as any)?.Xrm;
      if (xrm?.Navigation?.navigateTo) {
        // Resolve theme for viewer
        const themeParam =
          document.documentElement.getAttribute("data-theme") ?? "light";

        await xrm.Navigation.navigateTo(
          {
            pageType: "webresource",
            webresourceName: "sprk_documentrelationshipviewer",
            data: `documentId=${documentId}&tenantId=${encodeURIComponent(tenantId)}&theme=${themeParam}`,
          },
          {
            target: 2,
            width: { value: 85, unit: "%" },
            height: { value: 85, unit: "%" },
          }
        );
      } else {
        setError("Cannot open viewer: Xrm.Navigation is not available");
      }
    } catch (err) {
      const message =
        err instanceof Error ? err.message : "An unexpected error occurred";
      setError(message);
      console.error("[FindSimilar] Error:", err);
    } finally {
      setIsProcessing(false);
    }
  }, [
    canSubmit,
    selectedRecord,
    selectedFile,
    apiBaseUrl,
    tenantId,
    authenticatedFetch,
  ]);

  // ---------------------------------------------------------------------------
  // Cancel / Close dialog
  // ---------------------------------------------------------------------------

  const handleCancel = React.useCallback(() => {
    try {
      // Find dialog close button in parent DOM (proven pattern from wizards)
      const parentDoc =
        window.parent?.document ?? window.frameElement?.ownerDocument;
      if (parentDoc) {
        const closeBtn = parentDoc.querySelector<HTMLElement>(
          '[data-id="dialogCloseIconButton"]'
        );
        if (closeBtn) {
          closeBtn.click();
          return;
        }
      }
    } catch {
      /* cross-origin */
    }
    window.close();
  }, []);

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  return (
    <div className={styles.root}>
      <Text className={styles.title}>Find Similar Documents</Text>
      <Text className={styles.subtitle}>
        Select a document or upload a file to find similar content.
      </Text>

      {/* Path A: Document Lookup */}
      <div className={styles.section}>
        <Text weight="semibold">Select a document:</Text>
        <div className={styles.lookupRow}>
          <div className={styles.lookupDisplay}>
            {selectedRecord ? (
              <>
                <DocumentSearchRegular
                  style={{ marginRight: 8, flexShrink: 0 }}
                />
                <Text truncate wrap={false} style={{ flex: 1 }}>
                  {selectedRecord.name}
                </Text>
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<DismissRegular />}
                  onClick={handleClearRecord}
                  aria-label="Clear selection"
                />
              </>
            ) : (
              <Text
                style={{ color: tokens.colorNeutralForeground4 }}
              >
                No document selected
              </Text>
            )}
          </div>
          <Button
            appearance="secondary"
            icon={<SearchRegular />}
            onClick={handleOpenLookup}
            disabled={isProcessing}
          >
            Select Record
          </Button>
        </div>
      </div>

      {/* Divider */}
      <div className={styles.dividerRow}>
        <Divider className={styles.dividerLine} />
        <Text
          size={200}
          style={{ color: tokens.colorNeutralForeground3 }}
        >
          or
        </Text>
        <Divider className={styles.dividerLine} />
      </div>

      {/* Path B: File Upload */}
      <div className={styles.section}>
        <Text weight="semibold">Upload a file to compare:</Text>
        {selectedFile ? (
          <div className={styles.fileInfo}>
            <ArrowUploadRegular />
            <Text truncate wrap={false} style={{ flex: 1 }}>
              {selectedFile.name} (
              {(selectedFile.size / (1024 * 1024)).toFixed(1)} MB)
            </Text>
            <Button
              appearance="subtle"
              size="small"
              icon={<DismissRegular />}
              onClick={handleClearFile}
              aria-label="Remove file"
            />
          </div>
        ) : (
          <div
            className={`${styles.dropZone} ${isDragOver ? styles.dropZoneDragOver : ""}`}
            onClick={() => fileInputRef.current?.click()}
            onDragOver={handleDragOver}
            onDragLeave={handleDragLeave}
            onDrop={handleDrop}
            role="button"
            tabIndex={0}
            onKeyDown={(e) => {
              if (e.key === "Enter" || e.key === " ")
                fileInputRef.current?.click();
            }}
          >
            <ArrowUploadRegular
              style={{ fontSize: 24, color: tokens.colorNeutralForeground3 }}
            />
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
              Drop a file here or click to browse
            </Text>
            <Text
              size={100}
              style={{ color: tokens.colorNeutralForeground4 }}
            >
              PDF, DOCX, TXT, MD (max {MAX_FILE_SIZE_MB} MB)
            </Text>
          </div>
        )}
        <input
          ref={fileInputRef}
          type="file"
          accept={ALLOWED_MIME_TYPES.join(",")}
          style={{ display: "none" }}
          onChange={handleFileInputChange}
        />
      </div>

      {/* Error message */}
      {error && (
        <Text className={styles.errorText} style={{ marginTop: 8 }}>
          {error}
        </Text>
      )}

      {/* Footer */}
      <div className={styles.footer}>
        <Button appearance="secondary" onClick={handleCancel} disabled={isProcessing}>
          Cancel
        </Button>
        <Button
          appearance="primary"
          onClick={handleFindSimilar}
          disabled={!canSubmit}
          icon={isProcessing ? <Spinner size="tiny" /> : <SearchRegular />}
        >
          {isProcessing ? "Processing..." : "Find Similar"}
        </Button>
      </div>
    </div>
  );
}
