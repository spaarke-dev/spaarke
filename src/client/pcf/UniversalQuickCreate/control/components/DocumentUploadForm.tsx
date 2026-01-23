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
    Checkbox,
    Text,
    makeStyles,
    tokens,
    MessageBar,
    MessageBarBody,
    TabList,
    Tab,
    SelectTabEvent,
    SelectTabData
} from '@fluentui/react-components';
import { SparkleRegular, DocumentRegular, CheckmarkCircle20Regular } from '@fluentui/react-icons';
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

    /** Function to get auth token for AI services (optional) */
    getAuthToken?: () => Promise<string>;

    /** Function to get tenant ID for RAG indexing (optional) */
    getTenantId?: () => string | null;
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
    // Tab header area
    tabHeader: {
        padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalXXL}`,
        borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
        backgroundColor: tokens.colorNeutralBackground1
    },
    tabList: {
        backgroundColor: 'transparent'
    },
    disabledTab: {
        opacity: 0.5,
        cursor: 'not-allowed'
    },
    // Tab content area
    tabContent: {
        flex: 1,
        overflowY: 'auto',
        padding: tokens.spacingHorizontalXXL,
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalL
    },
    infoBanner: {
        marginBottom: tokens.spacingVerticalM
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
    // AI Summary section styles (for File Upload tab)
    aiCheckboxSection: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
        paddingTop: tokens.spacingVerticalXL,
        marginTop: tokens.spacingVerticalL,
        borderTop: `1px solid ${tokens.colorNeutralStroke2}`
    },
    aiCheckboxRow: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalM
    },
    aiIcon: {
        color: tokens.colorBrandForeground1,
        fontSize: '24px'
    },
    aiCheckboxLabel: {
        fontSize: tokens.fontSizeBase400,
        fontWeight: tokens.fontWeightSemibold
    },
    aiInfoText: {
        fontSize: tokens.fontSizeBase300,
        color: tokens.colorNeutralForeground3,
        paddingLeft: '48px' // Align with checkbox label (larger icon + gap)
    },
    // Summary tab content
    summaryTabContent: {
        flex: 1,
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalL
    },
    summaryPlaceholder: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        padding: tokens.spacingVerticalXXL,
        gap: tokens.spacingVerticalM,
        color: tokens.colorNeutralForeground3
    },
    summaryPlaceholderText: {
        fontSize: tokens.fontSizeBase400,
        textAlign: 'center'
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
    getAuthToken,
    getTenantId
}) => {
    const styles = useStyles();

    // State
    const [selectedFiles, setSelectedFiles] = React.useState<File[]>([]);
    const [isUploading, setIsUploading] = React.useState<boolean>(false);
    const [uploadProgress, setUploadProgress] = React.useState<{ current: number; total: number }>({ current: 0, total: 0 });
    const [errors, setErrors] = React.useState<Array<{ fileName: string; error: string }>>([]);
    const [successCount, setSuccessCount] = React.useState<number>(0);

    // AI Summary state
    const [runAiSummary, setRunAiSummary] = React.useState<boolean>(true);
    const [uploadCompleted, setUploadCompleted] = React.useState<boolean>(false);

    // Tab state
    const [selectedTab, setSelectedTab] = React.useState<string>('upload');

    /**
     * Handle summary completion - save to Dataverse
     */
    const handleSummaryComplete = React.useCallback(async (documentId: string, summary: string) => {
        logInfo('DocumentUploadForm', 'Summary completed, saving to Dataverse', { documentId });
        await documentRecordService.updateSummary(documentId, summary);
    }, [documentRecordService]);

    // AI Summary hook - uses getToken for dynamic token acquisition
    const aiSummary = useAiSummary({
        apiBaseUrl,
        getToken: getAuthToken,
        maxConcurrent: 3,
        autoStart: true,
        onSummaryComplete: handleSummaryComplete
    });

    // Determine if AI processing is complete
    const aiComplete = uploadCompleted && runAiSummary && aiSummary.documents.length > 0 && !aiSummary.isProcessing;

    /**
     * Convert Dataverse entity logical name to semantic search entity type.
     * Maps "sprk_matter" → "matter", "sprk_project" → "project", etc.
     */
    const getEntityTypeFromLogicalName = (logicalName: string): string | null => {
        // Map Dataverse logical names to semantic search entity types
        const entityTypeMap: Record<string, string> = {
            'sprk_matter': 'matter',
            'sprk_project': 'project',
            'sprk_invoice': 'invoice',
            'account': 'account',
            'contact': 'contact'
        };
        return entityTypeMap[logicalName.toLowerCase()] ?? null;
    };

    /**
     * Index documents to RAG for semantic search.
     * Non-blocking: failures are logged as warnings, not shown to users.
     *
     * @param documents Documents to index (with driveId, itemId, fileName, documentId)
     */
    const indexDocumentsToRag = React.useCallback(async (
        documents: Array<{ driveId: string; itemId: string; fileName: string; documentId?: string }>
    ) => {
        if (!apiBaseUrl || !getAuthToken || !getTenantId) {
            logInfo('DocumentUploadForm', 'RAG indexing skipped: missing apiBaseUrl, getAuthToken, or getTenantId');
            return;
        }

        const tenantId = getTenantId();
        if (!tenantId) {
            logInfo('DocumentUploadForm', 'RAG indexing skipped: no tenantId available');
            return;
        }

        // Build parent entity context for entity-scoped search
        const entityType = getEntityTypeFromLogicalName(parentContext.parentEntityName);
        const parentEntity = entityType ? {
            entityType: entityType,
            entityId: parentContext.parentRecordId,
            entityName: parentContext.parentDisplayName
        } : null;

        // Index each document independently (non-blocking, fire-and-forget)
        for (const doc of documents) {
            // Fire and forget - don't await, don't block on failures
            (async () => {
                try {
                    const token = await getAuthToken();
                    const response = await fetch(`${apiBaseUrl}/ai/rag/index-file`, {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json',
                            'Authorization': `Bearer ${token}`
                        },
                        body: JSON.stringify({
                            driveId: doc.driveId,
                            itemId: doc.itemId,
                            fileName: doc.fileName,
                            tenantId: tenantId,
                            documentId: doc.documentId,
                            parentEntity: parentEntity
                        })
                    });

                    if (!response.ok) {
                        console.warn(`RAG indexing failed for ${doc.fileName}: ${response.status} ${response.statusText}`);
                    } else {
                        logInfo('DocumentUploadForm', 'RAG indexing enqueued', { fileName: doc.fileName, entityType });
                    }
                } catch (err) {
                    console.warn(`RAG indexing failed for ${doc.fileName}:`, err);
                }
            })();
        }
    }, [apiBaseUrl, getAuthToken, getTenantId, parentContext]);

    // Auto-switch to Summary tab when upload completes with AI summaries
    React.useEffect(() => {
        if (uploadCompleted && runAiSummary && aiSummary.documents.length > 0) {
            setSelectedTab('summary');
        }
    }, [uploadCompleted, runAiSummary, aiSummary.documents.length]);

    /**
     * Handle tab selection
     */
    const handleTabSelect = (_event: SelectTabEvent, data: SelectTabData) => {
        // Only allow switching to summary tab if upload is completed
        if (data.value === 'summary' && !uploadCompleted) {
            return;
        }
        setSelectedTab(data.value as string);
    };

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
                documentName: '' // Auto-generated from file name
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

            // Phase 4: Index documents to RAG for semantic search (non-blocking)
            // Runs in background - failures logged as warnings, not shown to users
            const ragDocs = successRecords
                .filter(r => r.driveId && r.itemId)
                .map(r => ({
                    driveId: r.driveId!,
                    itemId: r.itemId!,
                    fileName: r.fileName,
                    documentId: r.documentId
                }));

            if (ragDocs.length > 0) {
                logInfo('DocumentUploadForm', 'Starting RAG indexing', {
                    documentCount: ragDocs.length
                });
                // Fire and forget - don't await, let it run in background
                indexDocumentsToRag(ragDocs).catch(err => {
                    console.warn('RAG indexing batch failed:', err);
                });
            }

            setIsUploading(false);
            setUploadCompleted(true);

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
            : uploadCompleted
                ? 'Upload Complete'
                : `Upload & Create Document${selectedFiles.length > 1 ? 's' : ''}`;

    const mainButtonDisabled = selectedFiles.length === 0 || isUploading || uploadCompleted;

    return (
        <div className={styles.container}>
            {/* Tab Header */}
            <div className={styles.tabHeader}>
                <TabList
                    selectedValue={selectedTab}
                    onTabSelect={handleTabSelect}
                    className={styles.tabList}
                >
                    <Tab
                        value="upload"
                        icon={<DocumentRegular />}
                    >
                        Upload Files
                    </Tab>
                    <Tab
                        value="summary"
                        icon={uploadCompleted && runAiSummary ? <CheckmarkCircle20Regular /> : <SparkleRegular />}
                        disabled={!uploadCompleted}
                        className={!uploadCompleted ? styles.disabledTab : undefined}
                    >
                        AI Summary
                    </Tab>
                </TabList>
            </div>

            {/* Tab Content */}
            <div className={styles.tabContent}>
                {/* File Upload Tab */}
                {selectedTab === 'upload' && (
                    <>
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
                            disabled={isUploading || uploadCompleted}
                        />

                        {/* Upload Progress */}
                        {isUploading && (
                            <UploadProgressBar
                                current={uploadProgress.current}
                                total={uploadProgress.total}
                            />
                        )}

                        {/* AI Summary Checkbox */}
                        {apiBaseUrl && (
                            <div className={styles.aiCheckboxSection}>
                                <div className={styles.aiCheckboxRow}>
                                    <SparkleRegular className={styles.aiIcon} />
                                    <Checkbox
                                        checked={runAiSummary}
                                        onChange={(e, data) => setRunAiSummary(data.checked === true)}
                                        label={<span className={styles.aiCheckboxLabel}>Run AI Summary after upload</span>}
                                        disabled={isUploading || uploadCompleted}
                                    />
                                </div>
                                {runAiSummary && !uploadCompleted && (
                                    <Text className={styles.aiInfoText}>
                                        AI will analyze uploaded files and extract summaries, keywords, and entities
                                    </Text>
                                )}
                            </div>
                        )}
                    </>
                )}

                {/* AI Summary Tab */}
                {selectedTab === 'summary' && (
                    <div className={styles.summaryTabContent}>
                        {runAiSummary && aiSummary.documents.length > 0 ? (
                            <>
                                {aiSummary.hasIncomplete && (
                                    <MessageBar intent="info">
                                        <MessageBarBody>
                                            You can close anytime - summaries will complete in the background
                                        </MessageBarBody>
                                    </MessageBar>
                                )}
                                <AiSummaryCarousel
                                    documents={aiSummary.documents}
                                    onRetry={aiSummary.retry}
                                />
                            </>
                        ) : (
                            <div className={styles.summaryPlaceholder}>
                                <SparkleRegular style={{ fontSize: 48 }} />
                                <Text className={styles.summaryPlaceholderText}>
                                    {!uploadCompleted
                                        ? 'Upload files to generate AI summaries'
                                        : !runAiSummary
                                            ? 'AI Summary was not enabled for this upload'
                                            : 'Processing files...'}
                                </Text>
                            </div>
                        )}
                    </div>
                )}
            </div>

            {/* Footer */}
            <div className={styles.footer}>
                <span className={styles.versionText}>
                    v3.12.0 • Built 2026-01-23
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
                        {(uploadCompleted && (aiComplete || !runAiSummary)) ? 'Close' : 'Cancel'}
                    </Button>
                </div>
            </div>

        </div>
    );
};
