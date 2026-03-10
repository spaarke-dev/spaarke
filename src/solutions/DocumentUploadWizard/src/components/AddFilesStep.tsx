/**
 * AddFilesStep.tsx
 * Step 1 of the Document Upload Wizard — file selection via drag-and-drop or browse.
 *
 * Uses shared FileUploadZone and UploadedFileList components from
 * @spaarke/ui-components. Adds a "Related To" info bar showing which
 * parent entity the files will be uploaded to.
 *
 * File validation accepts common document types:
 *   PDF, DOCX, XLSX, PPTX, TXT, MSG, EML
 *
 * @see ADR-006  - Code Pages for standalone dialogs
 * @see ADR-021  - Fluent UI v9 design system (makeStyles + semantic tokens)
 */

import { useMemo, useCallback } from "react";
import {
    makeStyles,
    tokens,
    Text,
    MessageBar,
    MessageBarBody,
} from "@fluentui/react-components";
import { LinkRegular } from "@fluentui/react-icons";

import { FileUploadZone } from "@spaarke/ui-components/components/FileUpload";
import { UploadedFileList } from "@spaarke/ui-components/components/FileUpload";
import type {
    IUploadedFile,
    IFileValidationError,
    IFileValidationConfig,
} from "@spaarke/ui-components/components/FileUpload";

import type { IAddFilesStepProps } from "../types";

// ---------------------------------------------------------------------------
// Accepted file types — extended beyond shared defaults
// ---------------------------------------------------------------------------

/**
 * Accepted extensions for document upload.
 * Extends the shared library defaults (PDF, DOCX, XLSX) with additional
 * common document types: PPTX, TXT, MSG, EML.
 */
const ACCEPTED_EXTENSIONS: string[] = [
    ".pdf",
    ".docx",
    ".xlsx",
    ".pptx",
    ".txt",
    ".msg",
    ".eml",
];

/**
 * HTML accept attribute value for the hidden file input.
 * Includes MIME types for recognized formats and extensions for all.
 */
const INPUT_ACCEPT = [
    // MIME types for well-known formats
    "application/pdf",
    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    "text/plain",
    "application/vnd.ms-outlook",
    "message/rfc822",
    // Extensions as fallback
    ...ACCEPTED_EXTENSIONS,
].join(",");

/** Maximum file size: 25 MB per file. */
const MAX_FILE_SIZE_BYTES = 25 * 1024 * 1024;

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
        flex: 1,
        minHeight: 0,
    },
    header: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
        flexShrink: 0,
    },
    stepTitle: {
        color: tokens.colorNeutralForeground1,
    },
    stepSubtitle: {
        color: tokens.colorNeutralForeground3,
    },
    relatedToBar: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground3,
        flexShrink: 0,
    },
    relatedToIcon: {
        color: tokens.colorNeutralForeground3,
        fontSize: "16px",
        flexShrink: 0,
    },
    relatedToLabel: {
        color: tokens.colorNeutralForeground3,
    },
    relatedToValue: {
        color: tokens.colorNeutralForeground1,
        fontWeight: tokens.fontWeightSemibold as unknown as string,
    },
    errorBar: {
        flexShrink: 0,
    },
    fileListContainer: {
        flex: "1 1 auto",
        minHeight: 0,
        overflowY: "auto",
    },
});

// ---------------------------------------------------------------------------
// Helper: format entity type for display
// ---------------------------------------------------------------------------

/**
 * Convert a Dataverse entity logical name to a human-readable label.
 * Example: "sprk_document" -> "Document", "sprk_matter" -> "Matter"
 */
function formatEntityTypeLabel(entityType: string): string {
    if (!entityType) return "Record";
    // Strip common prefixes and capitalize
    const stripped = entityType.replace(/^sprk_/, "").replace(/^new_/, "");
    return stripped.charAt(0).toUpperCase() + stripped.slice(1);
}

// ---------------------------------------------------------------------------
// AddFilesStep component
// ---------------------------------------------------------------------------

export function AddFilesStep({
    files,
    onFilesAdded,
    onFileRemoved,
    parentEntityName,
    parentEntityType,
    validationErrors = [],
    onClearErrors,
    isUnassociated = false,
}: IAddFilesStepProps): JSX.Element {
    const styles = useStyles();

    // -- Validation config for FileUploadZone --
    const validationConfig: IFileValidationConfig = useMemo(
        () => ({
            maxFileSizeBytes: MAX_FILE_SIZE_BYTES,
            acceptedExtensions: ACCEPTED_EXTENSIONS,
            inputAccept: INPUT_ACCEPT,
        }),
        []
    );

    // -- Callbacks --
    const handleFilesAccepted = useCallback(
        (accepted: IUploadedFile[]) => {
            onFilesAdded(accepted);
        },
        [onFilesAdded]
    );

    const handleValidationErrors = useCallback(
        (_errors: IFileValidationError[]) => {
            // Validation errors are surfaced through the parent's state
            // via onFilesAdded flow — the parent dispatches SET_VALIDATION_ERRORS.
            // This callback is required by FileUploadZone but error display
            // is handled via the validationErrors prop from the parent.
        },
        []
    );

    const handleRemoveFile = useCallback(
        (fileId: string) => {
            onFileRemoved(fileId);
        },
        [onFileRemoved]
    );

    const entityLabel = formatEntityTypeLabel(parentEntityType);

    return (
        <div className={styles.root}>
            {/* Header */}
            <div className={styles.header}>
                <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
                    Add Files
                </Text>
                <Text size={200} className={styles.stepSubtitle}>
                    Select documents to upload. Drag and drop files or click to browse.
                </Text>
            </div>

            {/* Related To info bar */}
            {parentEntityName && (
                <div className={styles.relatedToBar}>
                    <LinkRegular className={styles.relatedToIcon} aria-hidden="true" />
                    <Text size={200} className={styles.relatedToLabel}>
                        Related to:
                    </Text>
                    <Text size={200} className={styles.relatedToValue}>
                        {parentEntityName}
                    </Text>
                    <Text size={200} className={styles.relatedToLabel}>
                        ({entityLabel})
                    </Text>
                </div>
            )}

            {/* Unassociated upload indicator (standalone mode, no parent) */}
            {!parentEntityName && isUnassociated && (
                <div className={styles.relatedToBar}>
                    <LinkRegular className={styles.relatedToIcon} aria-hidden="true" />
                    <Text size={200} className={styles.relatedToLabel}>
                        Uploading to:
                    </Text>
                    <Text size={200} className={styles.relatedToValue}>
                        General Container (no parent record)
                    </Text>
                </div>
            )}

            {/* Validation errors */}
            {validationErrors.length > 0 && (
                <MessageBar
                    intent="error"
                    className={styles.errorBar}
                    onMouseEnter={onClearErrors}
                >
                    <MessageBarBody>
                        {validationErrors.map((err, i) => (
                            <div key={i}>
                                <strong>{err.fileName}</strong>: {err.reason}
                            </div>
                        ))}
                    </MessageBarBody>
                </MessageBar>
            )}

            {/* Drop zone */}
            <FileUploadZone
                onFilesAccepted={handleFilesAccepted}
                onValidationErrors={handleValidationErrors}
                validationConfig={validationConfig}
            />

            {/* Selected files list */}
            {files.length > 0 && (
                <div className={styles.fileListContainer}>
                    <UploadedFileList files={files} onRemove={handleRemoveFile} />
                </div>
            )}
        </div>
    );
}
