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
    Checkbox,
    Text,
    makeStyles,
    tokens,
    MessageBar,
    MessageBarBody,
    MessageBarTitle
} from '@fluentui/react-components';
import { SparkleRegular } from '@fluentui/react-icons';
import { FileSelectionField } from './FileSelectionField';
import { UploadProgressBar } from './UploadProgressBar';
import { ErrorMessageList } from './ErrorMessageList';
import { AiSummaryCarousel } from './AiSummaryCarousel';
import { FILE_UPLOAD_LIMITS, ParentContext, UploadedFileMetadata, CreateResult } from '../types';
import { MultiFileUploadService } from '../services/MultiFileUploadService';
import { DocumentRecordService } from '../services/DocumentRecordService';
import { useAiSummary, SummaryDocument } from '../services/useAiSummary';
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

    /** API base URL for AI services (optional) */
    apiBaseUrl?: string;

    /** Authorization token for AI services (optional) */
    authToken?: string;
}

/**
 * Styles using Fluent UI v9 makeStyles
 */
const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        height: '100%', // Changed from 100vh - let Custom Page control height
        backgroundColor: tokens.colorNeutralBackground1,
        color: tokens.colorNeutralForeground1,
        fontSize: tokens.fontSizeBase300, // 14px base - matches Power Apps MDA
        fontFamily: tokens.fontFamilyBase
    },
    // Header styles removed - Custom Page provides header
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
    },
    // AI Summary section styles
    aiSummarySection: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
        paddingTop: tokens.spacingVerticalL,
        borderTop: `1px solid ${tokens.colorNeutralStroke2}`
    },
    aiCheckboxRow: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS
    },
    aiIcon: {
        color: tokens.colorBrandForeground1
    },
    aiInfoText: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        paddingLeft: '28px' // Align with checkbox label
    }
});

/**
 * Document Upload Form Component
 */
export const DocumentUploadForm: React.FC<DocumentUploadFormProps> = ({
    parentContext,
    multiFileService,
    documentRecordService,
    onClose,
    apiBaseUrl = '',
    authToken
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

    // AI Summary state
    const [runAiSummary, setRunAiSummary] = React.useState<boolean>(true);

    // AI Summary hook
    const aiSummary = useAiSummary({
        apiBaseUrl,
        token: authToken,
        maxConcurrent: 3,
        autoStart: true
    });

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

            // Phase 3: Start AI Summary if opted-in and has successful records
            if (runAiSummary && successRecords.length > 0 && apiBaseUrl) {
                const summaryDocs: SummaryDocument[] = successRecords
                    .filter(r => r.documentId && r.driveId && r.itemId)
                    .map(r => ({
                        documentId: r.documentId!,
                        driveId: r.driveId!,
                        itemId: r.itemId!,
                        fileName: r.fileName
                    }));

                if (summaryDocs.length > 0) {
                    logInfo('DocumentUploadForm', 'Starting AI summaries', {
                        documentCount: summaryDocs.length
                    });
                    aiSummary.addDocuments(summaryDocs);
                }
            }

            setIsUploading(false);

            // Don't auto-close if AI summary is running - let user see progress
            // User can close manually, which will enqueue incomplete summaries

        } catch (error) {
            logError('DocumentUploadForm', 'Upload and save failed', error);
            setErrors([{ fileName: 'System Error', error: (error as Error).message }]);
            setIsUploading(false);
        }
    };

    /**
     * Handle Cancel/Close
     * Enqueues incomplete summaries if opted-in before closing
     */
    const handleCancel = async () => {
        // If AI summary is enabled and there are incomplete summaries, enqueue them
        if (runAiSummary && aiSummary.hasIncomplete && apiBaseUrl) {
            logInfo('DocumentUploadForm', 'Enqueueing incomplete summaries before close', {
                incompleteCount: aiSummary.documents.filter(
                    d => d.status !== 'complete' && d.status !== 'skipped' && d.status !== 'not-supported'
                ).length
            });
            await aiSummary.enqueueIncomplete();
        }
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
            {/* Content - Header removed since Custom Page provides its own */}
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

                {/* AI Summary Section */}
                {apiBaseUrl && (
                    <div className={styles.aiSummarySection}>
                        <div className={styles.aiCheckboxRow}>
                            <SparkleRegular className={styles.aiIcon} />
                            <Checkbox
                                checked={runAiSummary}
                                onChange={(e, data) => setRunAiSummary(data.checked === true)}
                                label="Run AI Summary"
                                disabled={isUploading || aiSummary.documents.length > 0}
                            />
                        </div>

                        {runAiSummary && aiSummary.hasIncomplete && (
                            <Text className={styles.aiInfoText}>
                                You can close anytime - summaries will complete in the background
                            </Text>
                        )}

                        {runAiSummary && aiSummary.documents.length > 0 && (
                            <AiSummaryCarousel
                                documents={aiSummary.documents}
                                onRetry={aiSummary.retry}
                            />
                        )}
                    </div>
                )}
            </div>

            {/* Footer */}
            <div className={styles.footer}>
                <span className={styles.versionText}>
                    v3.1.2 • Built 2025-12-07
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
