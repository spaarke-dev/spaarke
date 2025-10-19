/**
 * File Selection Field
 *
 * File picker with validation and selected file list.
 *
 * Features:
 * - Multiple file selection
 * - Validation (count, size, extensions)
 * - File list with remove buttons
 * - Error messages
 *
 * ADR Compliance:
 * - ADR-001: Fluent UI v9 Components
 *
 * @version 2.0.0.0
 */

import * as React from 'react';
import {
    Button,
    Field,
    makeStyles,
    tokens,
    Text
} from '@fluentui/react-components';
import { Dismiss24Regular, Document24Regular, FolderOpen24Regular } from '@fluentui/react-icons';
import { FILE_UPLOAD_LIMITS, FileValidationError } from '../types';
import { logWarn } from '../utils/logger';

/**
 * Component Props
 */
export interface FileSelectionFieldProps {
    /** Selected files */
    selectedFiles: File[];

    /** Callback when files change */
    onFilesChange: (files: File[]) => void;

    /** Disabled state */
    disabled?: boolean;
}

/**
 * Styles
 */
const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM
    },
    fileInputWrapper: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalM
    },
    hiddenInput: {
        display: 'none'
    },
    fileList: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS,
        padding: tokens.spacingHorizontalM,
        backgroundColor: tokens.colorNeutralBackground2,
        borderRadius: tokens.borderRadiusMedium,
        border: `1px solid ${tokens.colorNeutralStroke1}`
    },
    fileItem: {
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
        padding: tokens.spacingVerticalS,
        backgroundColor: tokens.colorNeutralBackground1,
        borderRadius: tokens.borderRadiusSmall,
        border: `1px solid ${tokens.colorNeutralStroke1}`
    },
    fileInfo: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        flex: 1
    },
    fileName: {
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1
    },
    fileSize: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase200
    },
    removeButton: {
        minWidth: 'auto'
    },
    errorText: {
        color: tokens.colorPaletteRedForeground1,
        fontSize: tokens.fontSizeBase200
    },
    emptyState: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase300,
        fontStyle: 'italic'
    }
});

/**
 * File Selection Field Component
 */
export const FileSelectionField: React.FC<FileSelectionFieldProps> = ({
    selectedFiles,
    onFilesChange,
    disabled = false
}) => {
    const styles = useStyles();
    const fileInputRef = React.useRef<HTMLInputElement>(null);
    const [validationErrors, setValidationErrors] = React.useState<FileValidationError[]>([]);

    /**
     * Validate files
     */
    const validateFiles = (files: File[]): FileValidationError[] => {
        const errors: FileValidationError[] = [];

        // Check file count
        if (files.length > FILE_UPLOAD_LIMITS.MAX_FILES) {
            errors.push({
                fileName: 'File Count',
                message: `Maximum ${FILE_UPLOAD_LIMITS.MAX_FILES} files allowed. You selected ${files.length} files.`
            });
            return errors; // Return early, don't check individual files
        }

        // Check total size
        const totalSize = files.reduce((sum, file) => sum + file.size, 0);
        if (totalSize > FILE_UPLOAD_LIMITS.MAX_TOTAL_SIZE) {
            const totalMB = (totalSize / (1024 * 1024)).toFixed(1);
            const maxMB = (FILE_UPLOAD_LIMITS.MAX_TOTAL_SIZE / (1024 * 1024)).toFixed(0);
            errors.push({
                fileName: 'Total Size',
                message: `Total file size (${totalMB}MB) exceeds limit of ${maxMB}MB.`
            });
        }

        // Check individual files
        files.forEach(file => {
            // Check file size
            if (file.size > FILE_UPLOAD_LIMITS.MAX_FILE_SIZE) {
                const sizeMB = (file.size / (1024 * 1024)).toFixed(1);
                const maxMB = (FILE_UPLOAD_LIMITS.MAX_FILE_SIZE / (1024 * 1024)).toFixed(0);
                errors.push({
                    fileName: file.name,
                    message: `File size (${sizeMB}MB) exceeds limit of ${maxMB}MB.`
                });
            }

            // Check dangerous extensions
            const extension = file.name.substring(file.name.lastIndexOf('.')).toLowerCase();
            if ((FILE_UPLOAD_LIMITS.DANGEROUS_EXTENSIONS as readonly string[]).includes(extension)) {
                errors.push({
                    fileName: file.name,
                    message: `File type ${extension} is not allowed for security reasons.`
                });
            }
        });

        return errors;
    };

    /**
     * Handle file input change
     */
    const handleFileInputChange = (event: React.ChangeEvent<HTMLInputElement>) => {
        const files = Array.from(event.target.files || []);

        if (files.length === 0) {
            return;
        }

        // Validate files
        const errors = validateFiles(files);
        setValidationErrors(errors);

        if (errors.length > 0) {
            logWarn('FileSelectionField', 'File validation failed', errors);
            // Clear input
            if (fileInputRef.current) {
                fileInputRef.current.value = '';
            }
            return;
        }

        // Update selected files
        onFilesChange(files);
        setValidationErrors([]);
    };

    /**
     * Handle Choose Files button click
     */
    const handleChooseFiles = () => {
        fileInputRef.current?.click();
    };

    /**
     * Handle remove file
     */
    const handleRemoveFile = (index: number) => {
        const newFiles = selectedFiles.filter((_, i) => i !== index);
        onFilesChange(newFiles);
        setValidationErrors([]);
    };

    /**
     * Format file size
     */
    const formatFileSize = (bytes: number): string => {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
        return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    };

    return (
        <div className={styles.container}>
            {/* File Input Field */}
            <Field label="Select Files *" required>
                <div className={styles.fileInputWrapper}>
                    <input
                        ref={fileInputRef}
                        type="file"
                        multiple
                        onChange={handleFileInputChange}
                        className={styles.hiddenInput}
                        disabled={disabled}
                    />
                    <Button
                        appearance="secondary"
                        icon={<FolderOpen24Regular />}
                        onClick={handleChooseFiles}
                        disabled={disabled}
                    >
                        Choose Files
                    </Button>
                    <Text className={styles.emptyState}>
                        {selectedFiles.length === 0
                            ? 'No files selected'
                            : `${selectedFiles.length} file${selectedFiles.length > 1 ? 's' : ''} selected`}
                    </Text>
                </div>
            </Field>

            {/* Validation Errors */}
            {validationErrors.length > 0 && (
                <div>
                    {validationErrors.map((error, index) => (
                        <Text key={index} className={styles.errorText}>
                            • {error.fileName}: {error.message}
                        </Text>
                    ))}
                </div>
            )}

            {/* Selected Files List */}
            {selectedFiles.length > 0 && (
                <div>
                    <Text weight="semibold" style={{ marginBottom: tokens.spacingVerticalS }}>
                        Selected Files ({selectedFiles.length})
                    </Text>
                    <div className={styles.fileList}>
                        {selectedFiles.map((file, index) => (
                            <div key={index} className={styles.fileItem}>
                                <div className={styles.fileInfo}>
                                    <Document24Regular />
                                    <div>
                                        <Text className={styles.fileName}>{file.name}</Text>
                                        <Text className={styles.fileSize}> ({formatFileSize(file.size)})</Text>
                                    </div>
                                </div>
                                <Button
                                    appearance="subtle"
                                    icon={<Dismiss24Regular />}
                                    onClick={() => handleRemoveFile(index)}
                                    className={styles.removeButton}
                                    disabled={disabled}
                                    aria-label={`Remove ${file.name}`}
                                />
                            </div>
                        ))}
                    </div>
                </div>
            )}
        </div>
    );
};
