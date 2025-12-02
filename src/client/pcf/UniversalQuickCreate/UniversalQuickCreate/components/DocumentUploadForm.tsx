/**
 * Document Upload Form
 *
 * Main Fluent UI v9 form for uploading multiple documents.
 *
 * Features:
 * - File selection with validation (10 files, 10MB each, 100MB total)
 * - Metadata fields (Description, Document Type)
 * - Upload progress tracking
 * - Error handling
 *
 * ADR Compliance:
 * - ADR-001: Fluent UI v9 Components (makeStyles, tokens, NO v8)
 * - ADR-002: TypeScript Strict Mode
 * - ADR-003: Separation of Concerns
 *
 * @version 2.0.0.0
 */

import * as React from 'react';
import {
    Button,
    Field,
    Textarea,
    Input,
    makeStyles,
    tokens,
    MessageBar,
    MessageBarBody,
    MessageBarTitle
} from '@fluentui/react-components';
import { Dismiss24Regular } from '@fluentui/react-icons';
import { FileSelectionField } from './FileSelectionField';
import { UploadProgressBar } from './UploadProgressBar';
import { ErrorMessageList } from './ErrorMessageList';
import { FILE_UPLOAD_LIMITS, ParentContext, UploadedFileMetadata, CreateResult } from '../types';
import { MultiFileUploadService } from '../services/MultiFileUploadService';
import { DocumentRecordService } from '../services/DocumentRecordService';
import { logInfo, logError } from '../utils/logger';

/**
 * Component Props
 */
export interface DocumentUploadFormProps {
    /** Parent context from Custom Page */
    parentContext: ParentContext;

    /** Services */
    multiFileService: MultiFileUploadService;
    documentRecordService: DocumentRecordService;

    /** Callback to close dialog */
    onClose: () => void;
}

/**
 * Styles using Fluent UI v9 makeStyles
 */
const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        height: '100vh',
        backgroundColor: tokens.colorNeutralBackground1
    },
    header: {
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
        padding: tokens.spacingHorizontalXL,
        borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
        backgroundColor: tokens.colorNeutralBackground1
    },
    headerTitle: {
        fontSize: tokens.fontSizeBase500,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        margin: 0
    },
    closeButton: {
        minWidth: 'auto'
    },
    content: {
        flex: 1,
        overflowY: 'auto',
        padding: tokens.spacingHorizontalXXL,
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXL
    },
    infoBanner: {
        marginBottom: tokens.spacingVerticalM
    },
    fieldSection: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalL
    },
    footer: {
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
        padding: tokens.spacingHorizontalXL,
        borderTop: `1px solid ${tokens.colorNeutralStroke1}`,
        backgroundColor: tokens.colorNeutralBackground1
    },
    footerButtons: {
        display: 'flex',
        gap: tokens.spacingHorizontalM,
        marginLeft: 'auto'
    },
    versionText: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3
    }
});

/**
 * Document Upload Form Component
 */
export const DocumentUploadForm: React.FC<DocumentUploadFormProps> = ({
    parentContext,
    multiFileService,
    documentRecordService,
    onClose
}) => {
    const styles = useStyles();

    // State
    const [selectedFiles, setSelectedFiles] = React.useState<File[]>([]);
    const [description, setDescription] = React.useState<string>('');
    const [documentType, setDocumentType] = React.useState<string>('');
    const [isUploading, setIsUploading] = React.useState<boolean>(false);
    const [uploadProgress, setUploadProgress] = React.useState<{ current: number; total: number }>({ current: 0, total: 0 });
    const [errors, setErrors] = React.useState<Array<{ fileName: string; error: string }>>([]);
    const [successCount, setSuccessCount] = React.useState<number>(0);

    /**
     * Handle file selection
     */
    const handleFilesChange = (files: File[]) => {
        setSelectedFiles(files);
        setErrors([]); // Clear previous errors
    };

    /**
     * Handle Upload & Save
     */
    const handleUploadAndSave = async () => {
        if (selectedFiles.length === 0) {
            return;
        }

        setIsUploading(true);
        setErrors([]);
        setSuccessCount(0);
        setUploadProgress({ current: 0, total: selectedFiles.length });

        try {
            logInfo('DocumentUploadForm', 'Starting upload and record creation', {
                fileCount: selectedFiles.length,
                parentEntityName: parentContext.parentEntityName,
                containerId: parentContext.containerId
            });

            // Phase 1: Upload files to SharePoint Embedded
            const uploadResult = await multiFileService.uploadFiles(
                {
                    files: selectedFiles,
                    containerId: parentContext.containerId
                },
                (progress) => {
                    setUploadProgress({ current: progress.current, total: progress.total });
                }
            );

            logInfo('DocumentUploadForm', 'File upload complete', {
                successCount: uploadResult.successCount,
                failureCount: uploadResult.failureCount
            });

            // Check if any files uploaded successfully
            if (uploadResult.uploadedFiles.length === 0) {
                setErrors(uploadResult.errors);
                setIsUploading(false);
                return;
            }

            // Phase 2: Create Dataverse records
            const formData = {
                documentName: '', // Auto-generated from file name
                description: description || undefined,
                documentType: documentType || undefined
            };

            const createResults = await documentRecordService.createDocuments(
                uploadResult.uploadedFiles,
                parentContext,
                formData
            );

            // Process results
            const successRecords = createResults.filter(r => r.success);
            const failedRecords = createResults.filter(r => !r.success);

            setSuccessCount(successRecords.length);
            setErrors([
                ...uploadResult.errors,
                ...failedRecords.map(r => ({ fileName: r.fileName, error: r.error || 'Unknown error' }))
            ]);

            logInfo('DocumentUploadForm', 'Record creation complete', {
                successCount: successRecords.length,
                failureCount: failedRecords.length
            });

            // If all successful, close dialog after brief delay
            if (failedRecords.length === 0) {
                setTimeout(() => {
                    onClose();
                }, 1500);
            } else {
                setIsUploading(false);
            }

        } catch (error) {
            logError('DocumentUploadForm', 'Upload and save failed', error);
            setErrors([{ fileName: 'System Error', error: (error as Error).message }]);
            setIsUploading(false);
        }
    };

    /**
     * Handle Cancel
     */
    const handleCancel = () => {
        onClose();
    };

    // Button text and state
    const mainButtonText = selectedFiles.length === 0
        ? 'Select Files to Continue'
        : isUploading
            ? 'Uploading...'
            : `Upload & Create Document${selectedFiles.length > 1 ? 's' : ''}`;

    const mainButtonDisabled = selectedFiles.length === 0 || isUploading;

    return (
        <div className={styles.container}>
            {/* Header */}
            <div className={styles.header}>
                <h1 className={styles.headerTitle}>
                    Add Documents to {parentContext.parentDisplayName}
                </h1>
                <Button
                    appearance="subtle"
                    icon={<Dismiss24Regular />}
                    onClick={handleCancel}
                    className={styles.closeButton}
                    disabled={isUploading}
                />
            </div>

            {/* Content */}
            <div className={styles.content}>
                {/* Info Banner */}
                <MessageBar intent="info" className={styles.infoBanner}>
                    <MessageBarBody>
                        Select up to {FILE_UPLOAD_LIMITS.MAX_FILES} files (max{' '}
                        {FILE_UPLOAD_LIMITS.MAX_FILE_SIZE / (1024 * 1024)}MB each, total{' '}
                        {FILE_UPLOAD_LIMITS.MAX_TOTAL_SIZE / (1024 * 1024)}MB)
                    </MessageBarBody>
                </MessageBar>

                {/* Success Message */}
                {successCount > 0 && (
                    <MessageBar intent="success">
                        <MessageBarBody>
                            Successfully created {successCount} document{successCount > 1 ? 's' : ''}!
                        </MessageBarBody>
                    </MessageBar>
                )}

                {/* Error Messages */}
                {errors.length > 0 && <ErrorMessageList errors={errors} />}

                {/* File Selection */}
                <FileSelectionField
                    selectedFiles={selectedFiles}
                    onFilesChange={handleFilesChange}
                    disabled={isUploading}
                />

                {/* Upload Progress */}
                {isUploading && (
                    <UploadProgressBar
                        current={uploadProgress.current}
                        total={uploadProgress.total}
                    />
                )}

                {/* Metadata Fields */}
                <div className={styles.fieldSection}>
                    <Field label="Document Type (Optional)">
                        <Input
                            value={documentType}
                            onChange={(e, data) => setDocumentType(data.value)}
                            placeholder="Enter document type"
                            disabled={isUploading}
                        />
                    </Field>

                    <Field label="Description (Optional)">
                        <Textarea
                            value={description}
                            onChange={(e, data) => setDescription(data.value)}
                            placeholder="Enter description..."
                            rows={4}
                            disabled={isUploading}
                        />
                    </Field>
                </div>
            </div>

            {/* Footer */}
            <div className={styles.footer}>
                <span className={styles.versionText}>
                    v2.0.0.0 • Built {new Date().toLocaleDateString()}
                </span>
                <div className={styles.footerButtons}>
                    <Button
                        appearance="primary"
                        onClick={handleUploadAndSave}
                        disabled={mainButtonDisabled}
                    >
                        {mainButtonText}
                    </Button>
                    <Button
                        appearance="secondary"
                        onClick={handleCancel}
                        disabled={isUploading}
                    >
                        Cancel
                    </Button>
                </div>
            </div>
        </div>
    );
};
