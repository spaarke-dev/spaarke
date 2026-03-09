/**
 * FileUploadStep.tsx
 * Step 1 of the Create Document wizard — drag-and-drop file upload zone.
 *
 * Accepted types: PDF (.pdf), DOCX (.docx), XLSX (.xlsx)
 * Maximum size:   10 MB per file
 *
 * Implements IWizardStepConfig pattern: canAdvance returns true when
 * at least one file is selected.
 *
 * Based on LegalWorkspace FileUploadZone pattern.
 *
 * @see ADR-021 - Fluent UI v9 design system (makeStyles + semantic tokens)
 */

import { useState, useCallback, useRef } from "react";
import {
    makeStyles,
    tokens,
    Text,
    Button,
    mergeClasses,
    MessageBar,
    MessageBarBody,
    ProgressBar,
} from "@fluentui/react-components";
import {
    ArrowUploadRegular,
    DocumentRegular,
    DismissCircleRegular,
    DocumentPdfRegular,
} from "@fluentui/react-icons";
import type {
    IUploadedFile,
    IFileValidationError,
    UploadedFileType,
    AcceptedMimeType,
} from "../types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const MAX_FILE_SIZE_BYTES = 10 * 1024 * 1024; // 10 MB

const ACCEPTED_EXTENSIONS: ReadonlySet<string> = new Set([".pdf", ".docx", ".xlsx"]);

const MIME_TO_FILE_TYPE: ReadonlyMap<AcceptedMimeType, UploadedFileType> = new Map([
    ["application/pdf", "pdf"],
    ["application/vnd.openxmlformats-officedocument.wordprocessingml.document", "docx"],
    ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx"],
]);

const EXTENSION_TO_FILE_TYPE: ReadonlyMap<string, UploadedFileType> = new Map([
    [".pdf", "pdf"],
    [".docx", "docx"],
    [".xlsx", "xlsx"],
]);

const INPUT_ACCEPT =
    ".pdf,.docx,.xlsx,application/pdf,application/vnd.openxmlformats-officedocument.wordprocessingml.document,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IFileUploadStepProps {
    /** Currently accepted files. */
    files: IUploadedFile[];
    /** Called when new files are accepted. */
    onFilesAdded: (files: IUploadedFile[]) => void;
    /** Called when a file is removed. */
    onFileRemoved: (fileId: string) => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalL,
    },
    stepTitle: {
        color: tokens.colorNeutralForeground1,
    },
    stepSubtitle: {
        color: tokens.colorNeutralForeground3,
    },
    zone: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        gap: tokens.spacingVerticalS,
        borderTopWidth: "2px",
        borderRightWidth: "2px",
        borderBottomWidth: "2px",
        borderLeftWidth: "2px",
        borderTopStyle: "dashed",
        borderRightStyle: "dashed",
        borderBottomStyle: "dashed",
        borderLeftStyle: "dashed",
        borderTopColor: tokens.colorNeutralStroke1,
        borderRightColor: tokens.colorNeutralStroke1,
        borderBottomColor: tokens.colorNeutralStroke1,
        borderLeftColor: tokens.colorNeutralStroke1,
        borderRadius: tokens.borderRadiusMedium,
        paddingTop: tokens.spacingVerticalXXL,
        paddingBottom: tokens.spacingVerticalXXL,
        paddingLeft: tokens.spacingHorizontalXL,
        paddingRight: tokens.spacingHorizontalXL,
        cursor: "pointer",
        transition: "border-color 0.15s ease, background-color 0.15s ease",
        backgroundColor: tokens.colorNeutralBackground2,
        outline: "none",
        ":focus-visible": {
            outlineWidth: "2px",
            outlineStyle: "solid",
            outlineColor: tokens.colorBrandStroke1,
            outlineOffset: "2px",
        },
    },
    zoneDragOver: {
        borderTopColor: tokens.colorBrandStroke1,
        borderRightColor: tokens.colorBrandStroke1,
        borderBottomColor: tokens.colorBrandStroke1,
        borderLeftColor: tokens.colorBrandStroke1,
        backgroundColor: tokens.colorBrandBackground2,
    },
    uploadIcon: {
        color: tokens.colorNeutralForeground3,
        fontSize: "32px",
    },
    uploadIconActive: {
        color: tokens.colorBrandForeground1,
    },
    primaryText: {
        color: tokens.colorNeutralForeground1,
        textAlign: "center",
    },
    linkText: {
        color: tokens.colorBrandForeground1,
        fontWeight: "600" as const,
    },
    helpText: {
        color: tokens.colorNeutralForeground4,
        textAlign: "center",
        marginTop: tokens.spacingVerticalXS,
    },
    hiddenInput: {
        display: "none",
    },
    fileList: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
    },
    fileRow: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalM,
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground2,
    },
    fileIcon: {
        color: tokens.colorBrandForeground1,
        flexShrink: 0,
    },
    fileInfo: {
        display: "flex",
        flexDirection: "column",
        flex: 1,
        minWidth: 0,
    },
    fileName: {
        color: tokens.colorNeutralForeground1,
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
    },
    fileSize: {
        color: tokens.colorNeutralForeground3,
    },
    progressBar: {
        marginTop: tokens.spacingVerticalXS,
    },
    removeButton: {
        flexShrink: 0,
    },
    errorBar: {
        flexShrink: 0,
    },
});

// ---------------------------------------------------------------------------
// Utilities
// ---------------------------------------------------------------------------

function getExtension(fileName: string): string {
    const lastDot = fileName.lastIndexOf(".");
    if (lastDot === -1) return "";
    return fileName.slice(lastDot).toLowerCase();
}

function formatBytes(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function generateFileId(): string {
    return `file-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
}

function validateFile(
    file: File,
): { valid: true; result: IUploadedFile } | { valid: false; error: IFileValidationError } {
    const ext = getExtension(file.name);
    const mimeType = file.type as AcceptedMimeType;

    const mimeValid = MIME_TO_FILE_TYPE.has(mimeType);
    const extValid = ACCEPTED_EXTENSIONS.has(ext);

    if (!mimeValid && !extValid) {
        return {
            valid: false,
            error: {
                fileName: file.name,
                reason: "File type not supported. Only PDF, DOCX, and XLSX files are accepted.",
            },
        };
    }

    const fileType: UploadedFileType | undefined =
        MIME_TO_FILE_TYPE.get(mimeType) ?? EXTENSION_TO_FILE_TYPE.get(ext);

    if (!fileType) {
        return {
            valid: false,
            error: {
                fileName: file.name,
                reason: "File type not supported. Only PDF, DOCX, and XLSX files are accepted.",
            },
        };
    }

    if (file.size > MAX_FILE_SIZE_BYTES) {
        return {
            valid: false,
            error: {
                fileName: file.name,
                reason: `File exceeds the 10 MB limit (${formatBytes(file.size)}).`,
            },
        };
    }

    return {
        valid: true,
        result: {
            id: generateFileId(),
            name: file.name,
            sizeBytes: file.size,
            fileType,
            file,
            uploadStatus: "pending",
        },
    };
}

function processFileList(fileList: FileList | null): {
    accepted: IUploadedFile[];
    errors: IFileValidationError[];
} {
    const accepted: IUploadedFile[] = [];
    const errors: IFileValidationError[] = [];
    if (!fileList) return { accepted, errors };

    Array.from(fileList).forEach((file) => {
        const result = validateFile(file);
        if (result.valid) {
            accepted.push(result.result);
        } else {
            errors.push(result.error);
        }
    });

    return { accepted, errors };
}

// ---------------------------------------------------------------------------
// FileUploadStep component
// ---------------------------------------------------------------------------

export function FileUploadStep({
    files,
    onFilesAdded,
    onFileRemoved,
}: IFileUploadStepProps): JSX.Element {
    const styles = useStyles();
    const [isDragOver, setIsDragOver] = useState(false);
    const [validationErrors, setValidationErrors] = useState<IFileValidationError[]>([]);
    const fileInputRef = useRef<HTMLInputElement>(null);
    const [inputKey, setInputKey] = useState(0);

    // -- Drag handlers --
    const handleDragEnter = useCallback((e: React.DragEvent<HTMLDivElement>) => {
        e.preventDefault();
        e.stopPropagation();
        setIsDragOver(true);
    }, []);

    const handleDragOver = useCallback((e: React.DragEvent<HTMLDivElement>) => {
        e.preventDefault();
        e.stopPropagation();
        e.dataTransfer.dropEffect = "copy";
        setIsDragOver(true);
    }, []);

    const handleDragLeave = useCallback((e: React.DragEvent<HTMLDivElement>) => {
        e.preventDefault();
        e.stopPropagation();
        if (!e.currentTarget.contains(e.relatedTarget as Node)) {
            setIsDragOver(false);
        }
    }, []);

    const handleDrop = useCallback(
        (e: React.DragEvent<HTMLDivElement>) => {
            e.preventDefault();
            e.stopPropagation();
            setIsDragOver(false);

            const { accepted, errors } = processFileList(e.dataTransfer.files);
            if (errors.length > 0) setValidationErrors(errors);
            if (accepted.length > 0) onFilesAdded(accepted);
        },
        [onFilesAdded],
    );

    // -- Click-to-browse --
    const handleClick = useCallback(() => {
        fileInputRef.current?.click();
    }, []);

    const handleKeyDown = useCallback((e: React.KeyboardEvent<HTMLDivElement>) => {
        if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            fileInputRef.current?.click();
        }
    }, []);

    const handleInputChange = useCallback(
        (e: React.ChangeEvent<HTMLInputElement>) => {
            const inputFiles = e.target.files;
            if (!inputFiles || inputFiles.length === 0) return;

            const { accepted, errors } = processFileList(inputFiles);
            if (errors.length > 0) setValidationErrors(errors);
            if (accepted.length > 0) onFilesAdded(accepted);
            setInputKey((k) => k + 1);
        },
        [onFilesAdded],
    );

    const zoneClass = mergeClasses(styles.zone, isDragOver && styles.zoneDragOver);
    const iconClass = mergeClasses(styles.uploadIcon, isDragOver && styles.uploadIconActive);

    return (
        <div className={styles.root}>
            <div>
                <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
                    Upload files
                </Text>
                <Text size={200} className={styles.stepSubtitle}>
                    Select documents to upload. Supported formats: PDF, DOCX, XLSX (max 10 MB each).
                </Text>
            </div>

            {validationErrors.length > 0 && (
                <MessageBar
                    intent="error"
                    className={styles.errorBar}
                    onMouseEnter={() => setValidationErrors([])}
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

            {/* Hidden file input */}
            <input
                key={inputKey}
                ref={fileInputRef}
                type="file"
                multiple
                accept={INPUT_ACCEPT}
                className={styles.hiddenInput}
                onChange={handleInputChange}
                aria-hidden="true"
                tabIndex={-1}
            />

            {/* Drop zone */}
            <div
                className={zoneClass}
                role="button"
                tabIndex={0}
                aria-label="Drop files here or click to browse. Accepted file types: PDF, DOCX, XLSX. Maximum 10 MB per file."
                onClick={handleClick}
                onKeyDown={handleKeyDown}
                onDragEnter={handleDragEnter}
                onDragOver={handleDragOver}
                onDragLeave={handleDragLeave}
                onDrop={handleDrop}
            >
                <ArrowUploadRegular className={iconClass} />
                <Text size={300} className={styles.primaryText}>
                    Drop files here or{" "}
                    <span className={styles.linkText}>click to browse</span>
                </Text>
                <Text size={200} className={styles.helpText}>
                    Supported: PDF, DOCX, XLSX (max 10 MB each)
                </Text>
            </div>

            {/* File list */}
            {files.length > 0 && (
                <div className={styles.fileList} role="list" aria-label="Uploaded files">
                    {files.map((f) => (
                        <div key={f.id} className={styles.fileRow} role="listitem">
                            <span className={styles.fileIcon}>
                                {f.fileType === "pdf" ? (
                                    <DocumentPdfRegular fontSize={24} />
                                ) : (
                                    <DocumentRegular fontSize={24} />
                                )}
                            </span>
                            <div className={styles.fileInfo}>
                                <Text size={300} className={styles.fileName}>
                                    {f.name}
                                </Text>
                                <Text size={200} className={styles.fileSize}>
                                    {formatBytes(f.sizeBytes)}
                                    {f.uploadStatus === "complete" && " - Uploaded"}
                                    {f.uploadStatus === "error" && ` - Error: ${f.uploadError ?? "Upload failed"}`}
                                </Text>
                                {f.uploadStatus === "uploading" && f.progress !== undefined && (
                                    <ProgressBar
                                        className={styles.progressBar}
                                        value={f.progress / 100}
                                        thickness="medium"
                                    />
                                )}
                            </div>
                            <Button
                                className={styles.removeButton}
                                appearance="subtle"
                                icon={<DismissCircleRegular />}
                                size="small"
                                onClick={() => onFileRemoved(f.id)}
                                aria-label={`Remove ${f.name}`}
                            />
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
}
