# Sprint 7B - Task 2A: Multi-File Upload Enhancement (Adaptive Strategy)

**Sprint**: 7B - Universal Quick Create with SPE Upload
**Task**: 2A (Enhancement to Task 2)
**Estimated Time**: 1 day (additional to Task 2)
**Priority**: High
**Status**: Pending
**Depends On**: Sprint 7B Task 2 (Single File Upload)

---

## Task Overview

Enhance the Universal Quick Create PCF from single-file upload to **multi-file upload with adaptive strategy**. Users can select 1-10 files in a single Quick Create operation, creating multiple Dataverse Document records with shared metadata. The system automatically selects the optimal upload strategy based on file size and count.

**This is an ENHANCEMENT to Task 2, not a replacement.** Task 2 provides the baseline single-file upload infrastructure. This task adds multi-file capability on top.

---

## Success Criteria

- ✅ User can select 1-10 files in file picker (HTML5 `multiple` attribute)
- ✅ Adaptive strategy selection based on file size/count
- ✅ **Sync-parallel upload**: 1-3 files, <10MB each, <20MB total (~3-4 seconds)
- ✅ **Long-running batched upload**: >3 files OR >10MB OR >20MB total (batched with progress)
- ✅ Progress indicators for both strategies
- ✅ One Document record created per file
- ✅ All documents share the same metadata from form
- ✅ Partial success handling (if 4 of 5 files upload, show summary)
- ✅ MSAL token caching benefits multi-file performance (5ms per file after first)
- ✅ All operations logged appropriately
- ✅ Zero breaking changes to single-file functionality

---

## Context & Background

### The Enhancement

**Current (Task 2):** Single file upload
```
User → Selects 1 file → Uploads → Creates 1 Document record
```

**Enhanced (Task 2A):** Multi-file upload with adaptive strategy
```
User → Selects 3 files → Auto-detects small/few → Sync-parallel → Creates 3 Documents (4s)
User → Selects 5 large files → Auto-detects large → Long-running batched → Creates 5 Documents (25s)
```

### Adaptive Strategy Decision Tree

```
User selects files
    ↓
Analyze: count, individual size, total size
    ↓
Decision:
    ├─ 1-3 files AND all <10MB AND total <20MB
    │       ↓
    │   Sync-Parallel Upload (Fast Path)
    │   - Upload all files in parallel
    │   - Create Dataverse records sequentially
    │   - Show simple spinner
    │   - Complete in 3-4 seconds
    │
    └─ >3 files OR any >10MB OR total >20MB
            ↓
        Long-Running Batched Upload (Safe Path)
        - Calculate adaptive batch size (2-5 based on file size)
        - Upload files in batches (parallel within batch)
        - Create Dataverse records sequentially
        - Show detailed progress (file-by-file status)
        - Complete in 15-35 seconds
```

### Why Two Strategies?

**Sync-Parallel (Fast Path):**
- ✅ Minimal user wait time (3-4 seconds)
- ✅ Simple UX (just a spinner)
- ✅ Best for common case (1-3 small files)
- ❌ Risky for large files (high memory, browser crashes)
- ❌ No detailed progress

**Long-Running Batched (Safe Path):**
- ✅ Safe for large files (controlled memory)
- ✅ Detailed progress (user knows what's happening)
- ✅ Resumable on error (partial success)
- ❌ Slightly slower (batching overhead)
- ❌ More complex UX

**Adaptive Strategy:** Automatically picks the right one!

---

## Performance Analysis

### Scenario 1: Few Small Files (Sync-Parallel)

**Input:** 3 files × 2MB each (6MB total)

**Strategy:** Sync-Parallel

**Breakdown:**
```
File 1 upload: 0.8s (MSAL token: 420ms + upload: 380ms)
File 2 upload: 0.4s (MSAL cached: 5ms + upload: 395ms)
File 3 upload: 0.4s (MSAL cached: 5ms + upload: 395ms)
Parallel total: 0.8s (longest file)

Dataverse records (sequential):
Record 1: 0.5s
Record 2: 0.5s
Record 3: 0.5s
Sequential total: 1.5s

Total time: 0.8s + 1.5s = 2.3s ✅
```

**User Experience:** "Creating documents..." spinner, completes in ~3 seconds

---

### Scenario 2: Many Large Files (Long-Running)

**Input:** 5 files × 15MB each (75MB total)

**Strategy:** Long-Running (batch size: 2)

**Breakdown:**
```
Batch 1 (files 1-2):
  File 1: 5.2s (MSAL: 420ms + upload: 4.8s)
  File 2: 4.9s (MSAL: 5ms + upload: 4.895s)
  Parallel: 5.2s
  Dataverse records: 1.0s (2 records × 0.5s)
  Batch total: 6.2s

Batch 2 (files 3-4):
  File 3: 4.9s
  File 4: 4.9s
  Parallel: 4.9s
  Dataverse records: 1.0s
  Batch total: 5.9s

Batch 3 (file 5):
  File 5: 4.9s
  Dataverse record: 0.5s
  Batch total: 5.4s

Total time: 6.2s + 5.9s + 5.4s = 17.5s ✅
```

**User Experience:** Progress bar showing "Uploading 5 files... 3 of 5 complete", completes in ~18 seconds

---

## Deliverables

### 1. Multi-File Upload Service (services/MultiFileUploadService.ts)

```typescript
import { SdapApiClient } from './SdapApiClient';
import { SpeFileMetadata } from '../types';
import { logger } from '../utils/logger';

/**
 * Multi-file upload service with adaptive strategy selection.
 *
 * Automatically selects between:
 * - Sync-parallel: Fast path for 1-3 small files
 * - Long-running: Safe path for large/many files
 *
 * @example
 * const service = new MultiFileUploadService(apiClient, context);
 *
 * const result = await service.uploadFiles({
 *     files: [file1, file2, file3],
 *     driveId: 'container-123',
 *     sharedMetadata: { description: 'Q3 Reports' }
 * });
 *
 * console.log(`${result.successCount} of ${result.totalFiles} uploaded`);
 */

export interface UploadFilesRequest {
    files: File[];
    driveId: string;
    sharedMetadata: Record<string, any>;
}

export interface UploadFilesResult {
    success: boolean;
    totalFiles: number;
    successCount: number;
    failureCount: number;
    documentRecords: DocumentRecord[];
    errors: UploadError[];
}

export interface DocumentRecord {
    recordId: string;
    fileName: string;
    speMetadata: SpeFileMetadata;
}

export interface UploadError {
    fileName: string;
    error: string;
}

export type UploadStrategy = 'sync-parallel' | 'long-running';

export interface UploadProgress {
    current: number;      // Current file number (1-based)
    total: number;        // Total files
    fileName: string;     // Current file name
    status: 'uploading' | 'creating-record' | 'complete' | 'failed';
}

export class MultiFileUploadService {
    constructor(
        private apiClient: SdapApiClient,
        private context: ComponentFramework.Context<any>
    ) {}

    /**
     * Main entry point: Upload multiple files with adaptive strategy
     *
     * @param request - Upload request with files, container ID, and shared metadata
     * @param onProgress - Optional progress callback for long-running uploads
     * @returns Upload result with success/failure details
     */
    async uploadFiles(
        request: UploadFilesRequest,
        onProgress?: (progress: UploadProgress) => void
    ): Promise<UploadFilesResult> {
        try {
            logger.info('MultiFileUploadService', 'Starting multi-file upload', {
                fileCount: request.files.length,
                totalSize: request.files.reduce((sum, f) => sum + f.size, 0),
                driveId: request.driveId
            });

            // Determine upload strategy
            const strategy = this.determineUploadStrategy(request.files);

            logger.info('MultiFileUploadService', `Using ${strategy} upload strategy`, {
                fileCount: request.files.length,
                largestFile: Math.max(...request.files.map(f => f.size)),
                totalSize: request.files.reduce((sum, f) => sum + f.size, 0)
            });

            // Execute strategy
            let result: UploadFilesResult;

            if (strategy === 'sync-parallel') {
                result = await this.handleSyncParallelUpload(request, onProgress);
            } else {
                result = await this.handleLongRunningUpload(request, onProgress);
            }

            logger.info('MultiFileUploadService', 'Multi-file upload complete', {
                totalFiles: result.totalFiles,
                successCount: result.successCount,
                failureCount: result.failureCount
            });

            return result;

        } catch (error) {
            logger.error('MultiFileUploadService', 'Multi-file upload failed', error);

            return {
                success: false,
                totalFiles: request.files.length,
                successCount: 0,
                failureCount: request.files.length,
                documentRecords: [],
                errors: request.files.map(f => ({
                    fileName: f.name,
                    error: error instanceof Error ? error.message : 'Unknown error'
                }))
            };
        }
    }

    /**
     * Determine upload strategy based on file count and size
     *
     * Decision Logic:
     * - Sync-Parallel: 1-3 files AND all <10MB AND total <20MB
     * - Long-Running: Everything else
     *
     * @param files - Files to upload
     * @returns Selected strategy
     */
    determineUploadStrategy(files: File[]): UploadStrategy {
        const totalFiles = files.length;
        const largestFile = Math.max(...files.map(f => f.size));
        const totalSize = files.reduce((sum, f) => sum + f.size, 0);

        // Thresholds
        const MAX_FILES_SYNC = 3;
        const MAX_FILE_SIZE_SYNC = 10 * 1024 * 1024;   // 10MB
        const MAX_TOTAL_SIZE_SYNC = 20 * 1024 * 1024;  // 20MB

        // Sync-Parallel: Small and few
        if (
            totalFiles <= MAX_FILES_SYNC &&
            largestFile < MAX_FILE_SIZE_SYNC &&
            totalSize < MAX_TOTAL_SIZE_SYNC
        ) {
            return 'sync-parallel';
        }

        // Long-Running: Large or many
        return 'long-running';
    }

    /**
     * Handle sync-parallel upload strategy (Fast Path)
     *
     * Upload all files in parallel, then create Dataverse records sequentially.
     * Best for 1-3 small files (<10MB each, <20MB total).
     *
     * Performance: ~3-4 seconds for 3 × 2MB files
     *
     * @param request - Upload request
     * @param onProgress - Progress callback (optional, simple updates only)
     * @returns Upload result
     */
    private async handleSyncParallelUpload(
        request: UploadFilesRequest,
        onProgress?: (progress: UploadProgress) => void
    ): Promise<UploadFilesResult> {
        logger.info('MultiFileUploadService', 'Starting sync-parallel upload', {
            fileCount: request.files.length
        });

        const documentRecords: DocumentRecord[] = [];
        const errors: UploadError[] = [];

        try {
            // PHASE 1: Upload all files in parallel (FAST!)
            logger.info('MultiFileUploadService', 'Uploading files in parallel...');

            const uploadPromises = request.files.map(async (file, index) => {
                try {
                    onProgress?.({
                        current: index + 1,
                        total: request.files.length,
                        fileName: file.name,
                        status: 'uploading'
                    });

                    const speMetadata = await this.apiClient.uploadFile({
                        file,
                        driveId: request.driveId,
                        fileName: file.name
                    });

                    logger.info('MultiFileUploadService', 'File uploaded', {
                        fileName: file.name,
                        fileSize: file.size,
                        driveItemId: speMetadata.driveItemId
                    });

                    return { file, speMetadata, error: null };

                } catch (error) {
                    logger.error('MultiFileUploadService', 'File upload failed', {
                        fileName: file.name,
                        error
                    });

                    return {
                        file,
                        speMetadata: null,
                        error: error instanceof Error ? error.message : 'Unknown error'
                    };
                }
            });

            // Wait for all uploads to complete
            const uploadResults = await Promise.all(uploadPromises);

            logger.info('MultiFileUploadService', 'All files uploaded', {
                successCount: uploadResults.filter(r => r.speMetadata !== null).length,
                failureCount: uploadResults.filter(r => r.error !== null).length
            });

            // PHASE 2: Create Dataverse records sequentially (avoid throttling)
            logger.info('MultiFileUploadService', 'Creating Dataverse records...');

            for (let i = 0; i < uploadResults.length; i++) {
                const result = uploadResults[i];

                if (result.speMetadata) {
                    try {
                        onProgress?.({
                            current: i + 1,
                            total: request.files.length,
                            fileName: result.file.name,
                            status: 'creating-record'
                        });

                        const recordId = await this.createDocumentRecord(
                            result.file,
                            result.speMetadata,
                            request.driveId,
                            request.sharedMetadata
                        );

                        documentRecords.push({
                            recordId,
                            fileName: result.file.name,
                            speMetadata: result.speMetadata
                        });

                        onProgress?.({
                            current: i + 1,
                            total: request.files.length,
                            fileName: result.file.name,
                            status: 'complete'
                        });

                        logger.info('MultiFileUploadService', 'Document record created', {
                            fileName: result.file.name,
                            recordId
                        });

                    } catch (error) {
                        logger.error('MultiFileUploadService', 'Dataverse record creation failed', {
                            fileName: result.file.name,
                            error
                        });

                        errors.push({
                            fileName: result.file.name,
                            error: error instanceof Error ? error.message : 'Failed to create record'
                        });
                    }
                } else {
                    // Upload failed for this file
                    errors.push({
                        fileName: result.file.name,
                        error: result.error || 'Upload failed'
                    });
                }
            }

            return {
                success: errors.length === 0,
                totalFiles: request.files.length,
                successCount: documentRecords.length,
                failureCount: errors.length,
                documentRecords,
                errors
            };

        } catch (error) {
            logger.error('MultiFileUploadService', 'Sync-parallel upload failed', error);

            return {
                success: false,
                totalFiles: request.files.length,
                successCount: documentRecords.length,
                failureCount: request.files.length - documentRecords.length,
                documentRecords,
                errors: errors.length > 0 ? errors : [{
                    fileName: 'Unknown',
                    error: error instanceof Error ? error.message : 'Unknown error'
                }]
            };
        }
    }

    /**
     * Handle long-running batched upload strategy (Safe Path)
     *
     * Upload files in adaptive batches, with detailed progress updates.
     * Best for large files or many files.
     *
     * Performance: ~17 seconds for 5 × 15MB files
     *
     * @param request - Upload request
     * @param onProgress - Progress callback (detailed updates)
     * @returns Upload result
     */
    private async handleLongRunningUpload(
        request: UploadFilesRequest,
        onProgress?: (progress: UploadProgress) => void
    ): Promise<UploadFilesResult> {
        logger.info('MultiFileUploadService', 'Starting long-running batched upload', {
            fileCount: request.files.length
        });

        const documentRecords: DocumentRecord[] = [];
        const errors: UploadError[] = [];

        try {
            // Calculate adaptive batch size
            const batchSize = this.calculateBatchSize(request.files);

            logger.info('MultiFileUploadService', 'Batch configuration', {
                totalFiles: request.files.length,
                batchSize,
                estimatedBatches: Math.ceil(request.files.length / batchSize)
            });

            // Split files into batches
            const batches = this.chunkArray(request.files, batchSize);

            logger.info('MultiFileUploadService', `Processing ${batches.length} batches`);

            // Process each batch
            for (let batchIndex = 0; batchIndex < batches.length; batchIndex++) {
                const batch = batches[batchIndex];

                logger.info('MultiFileUploadService', `Processing batch ${batchIndex + 1}/${batches.length}`, {
                    filesInBatch: batch.length
                });

                // Upload files in this batch in parallel
                const uploadPromises = batch.map(async (file) => {
                    try {
                        const currentFileIndex = request.files.indexOf(file) + 1;

                        onProgress?.({
                            current: currentFileIndex,
                            total: request.files.length,
                            fileName: file.name,
                            status: 'uploading'
                        });

                        const speMetadata = await this.apiClient.uploadFile({
                            file,
                            driveId: request.driveId,
                            fileName: file.name
                        });

                        logger.info('MultiFileUploadService', 'File uploaded', {
                            fileName: file.name,
                            batch: batchIndex + 1,
                            driveItemId: speMetadata.driveItemId
                        });

                        return { file, speMetadata, error: null };

                    } catch (error) {
                        logger.error('MultiFileUploadService', 'File upload failed', {
                            fileName: file.name,
                            batch: batchIndex + 1,
                            error
                        });

                        return {
                            file,
                            speMetadata: null,
                            error: error instanceof Error ? error.message : 'Unknown error'
                        };
                    }
                });

                // Wait for batch uploads to complete
                const batchResults = await Promise.all(uploadPromises);

                logger.info('MultiFileUploadService', `Batch ${batchIndex + 1} uploads complete`, {
                    successCount: batchResults.filter(r => r.speMetadata !== null).length,
                    failureCount: batchResults.filter(r => r.error !== null).length
                });

                // Create Dataverse records for this batch (sequential to avoid throttling)
                for (const result of batchResults) {
                    if (result.speMetadata) {
                        try {
                            const currentFileIndex = request.files.indexOf(result.file) + 1;

                            onProgress?.({
                                current: currentFileIndex,
                                total: request.files.length,
                                fileName: result.file.name,
                                status: 'creating-record'
                            });

                            const recordId = await this.createDocumentRecord(
                                result.file,
                                result.speMetadata,
                                request.driveId,
                                request.sharedMetadata
                            );

                            documentRecords.push({
                                recordId,
                                fileName: result.file.name,
                                speMetadata: result.speMetadata
                            });

                            onProgress?.({
                                current: currentFileIndex,
                                total: request.files.length,
                                fileName: result.file.name,
                                status: 'complete'
                            });

                            logger.info('MultiFileUploadService', 'Document record created', {
                                fileName: result.file.name,
                                recordId,
                                batch: batchIndex + 1
                            });

                        } catch (error) {
                            logger.error('MultiFileUploadService', 'Dataverse record creation failed', {
                                fileName: result.file.name,
                                error
                            });

                            errors.push({
                                fileName: result.file.name,
                                error: error instanceof Error ? error.message : 'Failed to create record'
                            });

                            onProgress?.({
                                current: request.files.indexOf(result.file) + 1,
                                total: request.files.length,
                                fileName: result.file.name,
                                status: 'failed'
                            });
                        }
                    } else {
                        // Upload failed for this file
                        errors.push({
                            fileName: result.file.name,
                            error: result.error || 'Upload failed'
                        });

                        onProgress?.({
                            current: request.files.indexOf(result.file) + 1,
                            total: request.files.length,
                            fileName: result.file.name,
                            status: 'failed'
                        });
                    }
                }

                logger.info('MultiFileUploadService', `Batch ${batchIndex + 1} complete`);
            }

            return {
                success: errors.length === 0,
                totalFiles: request.files.length,
                successCount: documentRecords.length,
                failureCount: errors.length,
                documentRecords,
                errors
            };

        } catch (error) {
            logger.error('MultiFileUploadService', 'Long-running upload failed', error);

            return {
                success: false,
                totalFiles: request.files.length,
                successCount: documentRecords.length,
                failureCount: request.files.length - documentRecords.length,
                documentRecords,
                errors: errors.length > 0 ? errors : [{
                    fileName: 'Unknown',
                    error: error instanceof Error ? error.message : 'Unknown error'
                }]
            };
        }
    }

    /**
     * Calculate adaptive batch size based on average file size
     *
     * Strategy:
     * - Small files (<1MB): Batch of 5 (fast uploads)
     * - Medium files (1-5MB): Batch of 3 (balanced)
     * - Large files (>5MB): Batch of 2 (safe, controlled memory)
     *
     * @param files - Files to upload
     * @returns Batch size
     */
    private calculateBatchSize(files: File[]): number {
        const avgSize = files.reduce((sum, f) => sum + f.size, 0) / files.length;

        if (avgSize < 1_000_000) {
            // <1MB: batch of 5
            return 5;
        } else if (avgSize < 5_000_000) {
            // 1-5MB: batch of 3
            return 3;
        } else {
            // >5MB: batch of 2
            return 2;
        }
    }

    /**
     * Split array into chunks of specified size
     *
     * @param array - Array to chunk
     * @param size - Chunk size
     * @returns Array of chunks
     */
    private chunkArray<T>(array: T[], size: number): T[][] {
        const chunks: T[][] = [];
        for (let i = 0; i < array.length; i += size) {
            chunks.push(array.slice(i, i + size));
        }
        return chunks;
    }

    /**
     * Create Dataverse Document record
     *
     * @param file - Original file
     * @param speMetadata - SharePoint Embedded metadata
     * @param driveId - Container/drive ID
     * @param sharedMetadata - Shared metadata from form
     * @returns Created record ID
     */
    private async createDocumentRecord(
        file: File,
        speMetadata: SpeFileMetadata,
        driveId: string,
        sharedMetadata: Record<string, any>
    ): Promise<string> {
        // Build record data
        const recordData: any = {
            ...sharedMetadata,
            sprk_sharepointurl: speMetadata.sharePointUrl,
            sprk_driveitemid: speMetadata.driveItemId,
            sprk_filename: speMetadata.fileName,
            sprk_filesize: speMetadata.fileSize,
            sprk_createddate: speMetadata.createdDateTime,
            sprk_modifieddate: speMetadata.lastModifiedDateTime,
            sprk_containerid: driveId
        };

        // If title not provided, use file name
        if (!recordData.sprk_documenttitle) {
            recordData.sprk_documenttitle = file.name;
        }

        // Create record via Dataverse Web API
        const result = await this.context.webAPI.createRecord('sprk_document', recordData);

        return result.id;
    }
}
```

---

### 2. Update File Picker Component (components/FilePickerField.tsx)

```typescript
import * as React from 'react';
import { Field, Input, makeStyles, Button } from '@fluentui/react-components';
import { Dismiss24Regular } from '@fluentui/react-icons';
import { logger } from '../utils/logger';

const useStyles = makeStyles({
    fileInfo: {
        marginTop: '8px',
        fontSize: '12px',
        color: '#666'
    },
    fileList: {
        marginTop: '12px',
        display: 'flex',
        flexDirection: 'column',
        gap: '8px'
    },
    fileItem: {
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
        padding: '8px 12px',
        backgroundColor: '#f3f2f1',
        borderRadius: '4px',
        fontSize: '13px'
    },
    fileItemName: {
        flex: 1,
        fontWeight: 500
    },
    fileItemSize: {
        marginLeft: '12px',
        color: '#666'
    },
    removeButton: {
        marginLeft: '12px',
        minWidth: '24px',
        padding: '4px'
    },
    summary: {
        marginTop: '8px',
        padding: '8px 12px',
        backgroundColor: '#e1dfdd',
        borderRadius: '4px',
        fontSize: '12px',
        fontWeight: 600
    }
});

export interface FilePickerFieldProps {
    value?: File[];                // 🔄 Changed from File to File[]
    onChange: (files: File[]) => void;  // 🔄 Changed from File | undefined
    required?: boolean;
    multiple?: boolean;            // 🆕 Allow multiple files
    maxFiles?: number;             // 🆕 Max file limit (default: 10)
}

export const FilePickerField: React.FC<FilePickerFieldProps> = ({
    value = [],
    onChange,
    required = false,
    multiple = true,
    maxFiles = 10
}) => {
    const styles = useStyles();

    const handleFileChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        const selectedFiles = Array.from(e.target.files || []);

        if (selectedFiles.length === 0) {
            onChange([]);
            return;
        }

        // Check max files limit
        if (selectedFiles.length > maxFiles) {
            logger.warn('FilePickerField', 'Too many files selected', {
                selected: selectedFiles.length,
                max: maxFiles
            });

            // Show error to user (you can integrate with form error state)
            alert(`Maximum ${maxFiles} files allowed. Please select fewer files.`);
            return;
        }

        onChange(selectedFiles);

        logger.info('FilePickerField', 'Files selected', {
            count: selectedFiles.length,
            totalSize: selectedFiles.reduce((sum, f) => sum + f.size, 0),
            files: selectedFiles.map(f => ({ name: f.name, size: f.size }))
        });

    }, [onChange, maxFiles]);

    const handleRemoveFile = React.useCallback((index: number) => {
        const newFiles = [...value];
        newFiles.splice(index, 1);
        onChange(newFiles);

        logger.info('FilePickerField', 'File removed', {
            index,
            remainingCount: newFiles.length
        });
    }, [value, onChange]);

    const formatFileSize = (bytes: number): string => {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
        return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    };

    const totalSize = value.reduce((sum, f) => sum + f.size, 0);

    return (
        <Field
            label={multiple ? `Select File(s) (up to ${maxFiles})` : 'Select File'}
            required={required}
        >
            <Input
                type="file"
                multiple={multiple}  // 🆕 HTML5 multiple attribute
                onChange={handleFileChange}
            />

            {/* File list */}
            {value.length > 0 && (
                <div className={styles.fileList}>
                    {value.map((file, index) => (
                        <div key={index} className={styles.fileItem}>
                            <span className={styles.fileItemName}>{file.name}</span>
                            <span className={styles.fileItemSize}>
                                {formatFileSize(file.size)}
                            </span>
                            <Button
                                appearance="subtle"
                                icon={<Dismiss24Regular />}
                                className={styles.removeButton}
                                onClick={() => handleRemoveFile(index)}
                                title="Remove file"
                            />
                        </div>
                    ))}

                    {/* Summary */}
                    {value.length > 1 && (
                        <div className={styles.summary}>
                            {value.length} files • {formatFileSize(totalSize)} total
                        </div>
                    )}
                </div>
            )}
        </Field>
    );
};
```

---

### 3. Upload Progress Component (components/UploadProgress.tsx)

```typescript
import * as React from 'react';
import { makeStyles, ProgressBar, Spinner } from '@fluentui/react-components';
import {
    Checkmark24Filled,
    ErrorCircle24Filled,
    ArrowSync24Regular
} from '@fluentui/react-icons';
import { UploadProgress as UploadProgressData } from '../services/MultiFileUploadService';

const useStyles = makeStyles({
    container: {
        padding: '16px',
        backgroundColor: '#f3f2f1',
        borderRadius: '8px',
        marginTop: '16px'
    },
    header: {
        fontSize: '16px',
        fontWeight: 600,
        marginBottom: '12px',
        color: '#323130'
    },
    progressBar: {
        marginBottom: '12px'
    },
    progressText: {
        fontSize: '13px',
        color: '#605e5c',
        marginBottom: '16px'
    },
    fileList: {
        maxHeight: '200px',
        overflowY: 'auto',
        display: 'flex',
        flexDirection: 'column',
        gap: '4px'
    },
    fileItem: {
        display: 'flex',
        alignItems: 'center',
        gap: '8px',
        padding: '6px 8px',
        fontSize: '12px',
        borderRadius: '4px',
        transition: 'background-color 0.2s'
    },
    fileItemActive: {
        backgroundColor: '#deecf9',
        fontWeight: 600
    },
    fileItemComplete: {
        color: '#107c10'
    },
    fileItemFailed: {
        color: '#a4262c'
    },
    icon: {
        flexShrink: 0
    },
    fileName: {
        flex: 1,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap'
    },
    footer: {
        marginTop: '12px',
        padding: '8px',
        backgroundColor: '#fff4ce',
        borderRadius: '4px',
        fontSize: '12px',
        color: '#605e5c',
        textAlign: 'center'
    }
});

export interface UploadProgressProps {
    files: File[];
    currentProgress: UploadProgressData | null;
    completedFiles: string[];
    failedFiles: string[];
}

export const UploadProgressComponent: React.FC<UploadProgressProps> = ({
    files,
    currentProgress,
    completedFiles,
    failedFiles
}) => {
    const styles = useStyles();

    const progressPercent = currentProgress
        ? (currentProgress.current / currentProgress.total) * 100
        : 0;

    const getFileStatus = (fileName: string): 'complete' | 'failed' | 'uploading' | 'waiting' => {
        if (completedFiles.includes(fileName)) return 'complete';
        if (failedFiles.includes(fileName)) return 'failed';
        if (currentProgress?.fileName === fileName) return 'uploading';
        return 'waiting';
    };

    const getFileIcon = (status: string) => {
        switch (status) {
            case 'complete':
                return <Checkmark24Filled className={styles.icon} style={{ color: '#107c10' }} />;
            case 'failed':
                return <ErrorCircle24Filled className={styles.icon} style={{ color: '#a4262c' }} />;
            case 'uploading':
                return <Spinner size="tiny" className={styles.icon} />;
            default:
                return <ArrowSync24Regular className={styles.icon} style={{ color: '#8a8886' }} />;
        }
    };

    const getFileStatusText = (status: string, currentStatus?: string): string => {
        if (status === 'complete') return '✓ Uploaded';
        if (status === 'failed') return '✗ Failed';
        if (status === 'uploading') {
            if (currentStatus === 'uploading') return 'Uploading...';
            if (currentStatus === 'creating-record') return 'Creating record...';
            return 'Processing...';
        }
        return 'Waiting...';
    };

    return (
        <div className={styles.container}>
            <div className={styles.header}>
                Uploading {files.length} file{files.length > 1 ? 's' : ''}...
            </div>

            <ProgressBar
                value={progressPercent / 100}
                className={styles.progressBar}
            />

            <div className={styles.progressText}>
                {currentProgress
                    ? `${currentProgress.current} of ${currentProgress.total} files`
                    : 'Preparing...'}
                {' • '}
                {Math.round(progressPercent)}% complete
            </div>

            <div className={styles.fileList}>
                {files.map((file, index) => {
                    const status = getFileStatus(file.name);
                    const isActive = currentProgress?.fileName === file.name;

                    return (
                        <div
                            key={index}
                            className={`${styles.fileItem} ${
                                isActive ? styles.fileItemActive : ''
                            } ${
                                status === 'complete' ? styles.fileItemComplete : ''
                            } ${
                                status === 'failed' ? styles.fileItemFailed : ''
                            }`}
                        >
                            {getFileIcon(status)}
                            <span className={styles.fileName}>{file.name}</span>
                            <span>
                                {getFileStatusText(status, currentProgress?.status)}
                            </span>
                        </div>
                    );
                })}
            </div>

            <div className={styles.footer}>
                ⚠️ Please keep this window open until upload completes
            </div>
        </div>
    );
};
```

---

### 4. Update QuickCreateForm.tsx

```typescript
// In QuickCreateForm.tsx

import * as React from 'react';
import {
    FluentProvider,
    webLightTheme,
    makeStyles,
    Button,
    Field,
    Input,
    Textarea,
    Spinner,
    MessageBar,
    MessageBarBody
} from '@fluentui/react-components';
import { logger } from '../utils/logger';
import { FilePickerField } from './FilePickerField';
import { UploadProgressComponent } from './UploadProgress';
import { UploadProgress } from '../services/MultiFileUploadService';

const useStyles = makeStyles({
    container: {
        padding: '20px',
        maxWidth: '600px',
        margin: '0 auto'
    },
    form: {
        display: 'flex',
        flexDirection: 'column',
        gap: '16px'
    },
    actions: {
        display: 'flex',
        gap: '12px',
        justifyContent: 'flex-end',
        marginTop: '24px'
    },
    loadingOverlay: {
        position: 'fixed',
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: 'rgba(255, 255, 255, 0.8)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: 1000
    },
    successSummary: {
        marginTop: '16px',
        padding: '12px',
        backgroundColor: '#dff6dd',
        borderLeft: '4px solid #107c10',
        borderRadius: '4px',
        fontSize: '14px'
    },
    partialSuccessSummary: {
        marginTop: '16px',
        padding: '12px',
        backgroundColor: '#fff4ce',
        borderLeft: '4px solid #797775',
        borderRadius: '4px',
        fontSize: '14px'
    }
});

export interface QuickCreateFormProps {
    entityName: string;
    parentEntityName: string;
    parentRecordId: string;
    defaultValues: Record<string, any>;
    enableFileUpload: boolean;
    sdapApiBaseUrl: string;
    context: ComponentFramework.Context<any>;
    onSave: (formData: Record<string, any>, files?: File[], onProgress?: (progress: UploadProgress) => void) => Promise<void>;
    onCancel: () => void;
}

export const QuickCreateForm: React.FC<QuickCreateFormProps> = ({
    entityName,
    parentEntityName,
    parentRecordId,
    defaultValues,
    enableFileUpload,
    sdapApiBaseUrl,
    context,
    onSave,
    onCancel
}) => {
    const styles = useStyles();

    // Form state
    const [formData, setFormData] = React.useState<Record<string, any>>(defaultValues);
    const [selectedFiles, setSelectedFiles] = React.useState<File[]>([]);  // 🔄 Changed to File[]
    const [isSaving, setIsSaving] = React.useState(false);
    const [error, setError] = React.useState<string | null>(null);

    // 🆕 Upload progress state
    const [uploadProgress, setUploadProgress] = React.useState<UploadProgress | null>(null);
    const [completedFiles, setCompletedFiles] = React.useState<string[]>([]);
    const [failedFiles, setFailedFiles] = React.useState<string[]>([]);
    const [uploadSummary, setUploadSummary] = React.useState<string | null>(null);

    // Update form data when default values change
    React.useEffect(() => {
        setFormData(defaultValues);
        logger.info('QuickCreateForm', 'Default values applied', defaultValues);
    }, [defaultValues]);

    const handleFileChange = React.useCallback((files: File[]) => {  // 🔄 Changed signature
        setSelectedFiles(files);
        logger.info('QuickCreateForm', 'Files selected', {
            count: files.length,
            totalSize: files.reduce((sum, f) => sum + f.size, 0)
        });
    }, []);

    const handleFieldChange = React.useCallback((fieldName: string, value: any) => {
        setFormData(prev => ({
            ...prev,
            [fieldName]: value
        }));
    }, []);

    const handleSubmit = React.useCallback(async (e: React.FormEvent) => {
        e.preventDefault();
        setError(null);
        setUploadProgress(null);
        setCompletedFiles([]);
        setFailedFiles([]);
        setUploadSummary(null);

        // Validate file upload if required
        if (enableFileUpload && selectedFiles.length === 0) {
            setError('Please select at least one file to upload.');
            return;
        }

        setIsSaving(true);

        try {
            logger.info('QuickCreateForm', 'Submitting form', {
                formData,
                fileCount: selectedFiles.length,
                totalSize: selectedFiles.reduce((sum, f) => sum + f.size, 0)
            });

            // Call onSave with progress callback
            await onSave(
                formData,
                selectedFiles,
                (progress: UploadProgress) => {
                    setUploadProgress(progress);

                    // Track completed/failed files
                    if (progress.status === 'complete') {
                        setCompletedFiles(prev => [...prev, progress.fileName]);
                    } else if (progress.status === 'failed') {
                        setFailedFiles(prev => [...prev, progress.fileName]);
                    }

                    logger.debug('QuickCreateForm', 'Upload progress', progress);
                }
            );

            // Show success summary
            const successCount = completedFiles.length;
            const totalCount = selectedFiles.length;

            if (successCount === totalCount) {
                setUploadSummary(`✓ All ${totalCount} files uploaded successfully!`);
            } else {
                setUploadSummary(`⚠️ ${successCount} of ${totalCount} files uploaded successfully. ${totalCount - successCount} failed.`);
            }

            logger.info('QuickCreateForm', 'Form submitted successfully', {
                successCount,
                totalCount
            });

            // Auto-close form after 2 seconds
            setTimeout(() => {
                // Form will close automatically (Power Apps behavior)
            }, 2000);

        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'Unknown error occurred';
            logger.error('QuickCreateForm', 'Form submission failed', err);
            setError(errorMessage);
        } finally {
            setIsSaving(false);
        }
    }, [formData, selectedFiles, enableFileUpload, onSave, completedFiles]);

    const isMultiFileUpload = selectedFiles.length > 1;

    return (
        <FluentProvider theme={webLightTheme}>
            <div className={styles.container}>
                <form onSubmit={handleSubmit} className={styles.form}>
                    {/* File Upload Field (if enabled) */}
                    {enableFileUpload && (
                        <FilePickerField
                            value={selectedFiles}
                            onChange={handleFileChange}
                            required={true}
                            multiple={true}      // 🆕 Enable multi-file
                            maxFiles={10}        // 🆕 Max 10 files
                        />
                    )}

                    {/* Dynamic form fields based on entity type */}
                    {entityName === 'sprk_document' && (
                        <>
                            <Field label="Description">
                                <Textarea
                                    value={formData.sprk_description || ''}
                                    onChange={(e, data) => handleFieldChange('sprk_description', data.value)}
                                    rows={3}
                                    placeholder="This description will be applied to all uploaded files"
                                />
                            </Field>
                        </>
                    )}

                    {/* Upload progress indicator (multi-file) */}
                    {isSaving && isMultiFileUpload && uploadProgress && (
                        <UploadProgressComponent
                            files={selectedFiles}
                            currentProgress={uploadProgress}
                            completedFiles={completedFiles}
                            failedFiles={failedFiles}
                        />
                    )}

                    {/* Upload summary */}
                    {uploadSummary && (
                        <div className={
                            completedFiles.length === selectedFiles.length
                                ? styles.successSummary
                                : styles.partialSuccessSummary
                        }>
                            {uploadSummary}
                        </div>
                    )}

                    {/* Error message */}
                    {error && (
                        <MessageBar intent="error">
                            <MessageBarBody>{error}</MessageBarBody>
                        </MessageBar>
                    )}

                    {/* Actions */}
                    <div className={styles.actions}>
                        <Button
                            appearance="secondary"
                            onClick={onCancel}
                            disabled={isSaving}
                        >
                            Cancel
                        </Button>
                        <Button
                            appearance="primary"
                            type="submit"
                            disabled={isSaving}
                        >
                            {isSaving
                                ? isMultiFileUpload
                                    ? `Uploading ${selectedFiles.length} files...`
                                    : 'Saving...'
                                : 'Save'}
                        </Button>
                    </div>
                </form>

                {/* Loading overlay (single file or initial state) */}
                {isSaving && !isMultiFileUpload && (
                    <div className={styles.loadingOverlay}>
                        <Spinner label="Saving..." size="large" />
                    </div>
                )}
            </div>
        </FluentProvider>
    );
};
```

---

### 5. Update UniversalQuickCreatePCF.ts (handleSave)

```typescript
// In UniversalQuickCreatePCF.ts

import { MultiFileUploadService, UploadProgress } from './services/MultiFileUploadService';

export class UniversalQuickCreate implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    // ... existing properties ...

    private multiFileUploadService: MultiFileUploadService | null = null;

    public async init(context: ComponentFramework.Context<IInputs>): Promise<void> {
        // ... existing init code ...

        // Initialize services
        const sdapApiClient = SdapApiClientFactory.create(this.sdapApiBaseUrl);

        // 🆕 Initialize multi-file upload service
        this.multiFileUploadService = new MultiFileUploadService(sdapApiClient, context);

        logger.info('UniversalQuickCreate', 'Multi-file upload service initialized');
    }

    /**
     * Handle save - supports both single and multi-file upload
     *
     * @param formData - Form field values
     * @param files - Files to upload (1-10 files)
     * @param onProgress - Progress callback for multi-file uploads
     */
    private async handleSave(
        formData: Record<string, any>,
        files?: File[],  // 🔄 Changed from File to File[]
        onProgress?: (progress: UploadProgress) => void
    ): Promise<void> {
        logger.info('UniversalQuickCreate', 'Save requested', {
            formData,
            fileCount: files?.length || 0
        });

        try {
            // Get container ID from form data or parent record
            const containerId = formData.sprk_containerid ||
                               this.parentRecordData?.sprk_containerid;

            if (!containerId) {
                throw new Error('Container ID not found - cannot upload file to SharePoint');
            }

            // Upload files (if provided)
            if (files && files.length > 0 && this.multiFileUploadService) {
                logger.info('UniversalQuickCreate', 'Starting multi-file upload', {
                    fileCount: files.length,
                    containerId
                });

                const result = await this.multiFileUploadService.uploadFiles(
                    {
                        files,
                        driveId: containerId,
                        sharedMetadata: formData
                    },
                    onProgress
                );

                if (!result.success) {
                    // Partial or complete failure
                    if (result.successCount > 0) {
                        // Partial success
                        logger.warn('UniversalQuickCreate', 'Partial upload success', {
                            successCount: result.successCount,
                            failureCount: result.failureCount
                        });

                        throw new Error(
                            `${result.successCount} of ${result.totalFiles} files uploaded successfully. ` +
                            `${result.failureCount} failed.`
                        );
                    } else {
                        // Complete failure
                        throw new Error(
                            `All ${result.totalFiles} files failed to upload. ` +
                            `Error: ${result.errors[0]?.error || 'Unknown error'}`
                        );
                    }
                }

                logger.info('UniversalQuickCreate', 'Multi-file upload complete', {
                    successCount: result.successCount,
                    documentRecords: result.documentRecords.length
                });
            }

            // Step 3: Close Quick Create form (Power Apps handles this automatically)
            logger.info('UniversalQuickCreate', 'Save complete - closing form');

            // Note: Power Apps will automatically close the Quick Create form
            // and refresh the parent grid when the save operation completes

        } catch (error) {
            logger.error('UniversalQuickCreate', 'Save failed', error);
            throw error; // Re-throw to show error in UI
        }
    }
}
```

---

## Implementation Steps

### Step 1: Create MultiFileUploadService (2 hours)

1. Create `services/MultiFileUploadService.ts`
2. Implement all methods from Deliverables section
3. Add comprehensive logging
4. Test strategy decision logic

### Step 2: Update FilePickerField Component (1 hour)

1. Update `components/FilePickerField.tsx`
2. Add `multiple` attribute support
3. Add file list display with remove buttons
4. Add max files validation

### Step 3: Create UploadProgress Component (1 hour)

1. Create `components/UploadProgress.tsx`
2. Implement file-by-file status display
3. Add progress bar
4. Add status icons (complete, failed, uploading, waiting)

### Step 4: Update QuickCreateForm (1 hour)

1. Update `components/QuickCreateForm.tsx`
2. Change state from `File` to `File[]`
3. Add upload progress tracking
4. Add success/partial success summary

### Step 5: Update PCF Wrapper (30 min)

1. Update `UniversalQuickCreatePCF.ts`
2. Initialize `MultiFileUploadService` in `init()`
3. Update `handleSave()` signature and implementation
4. Add error handling for partial success

### Step 6: Build & Test (2 hours)

**Test Scenarios:**
1. Single file (baseline - ensure no breaking changes)
2. 3 small files (sync-parallel)
3. 5 large files (long-running)
4. Threshold boundaries (strategy selection)
5. 10 files (maximum stress test)
6. Partial failure (error handling)

### Step 7: Bundle Size Validation (30 min)

```bash
npm run build

# Check bundle size
ls -lh out/controls
```

**Target:** <500 KB (MSAL adds ~200 KB, multi-file adds ~50 KB)

---

## Testing Checklist

### Single File (Baseline - No Breaking Changes)

- [ ] Upload single 5MB file
- [ ] Verify sync-parallel strategy used
- [ ] Verify 1 Document record created
- [ ] Form closes automatically
- [ ] Grid refreshes

### Multi-File Sync-Parallel (Fast Path)

- [ ] Upload 3 files × 2MB each
- [ ] Console: "Using sync-parallel upload strategy"
- [ ] Upload completes in ~3-4 seconds
- [ ] All 3 files upload in parallel
- [ ] MSAL token cached after first file (5ms vs 420ms)
- [ ] 3 Document records created with same metadata
- [ ] Form closes automatically

### Multi-File Long-Running (Safe Path)

- [ ] Upload 5 files × 15MB each
- [ ] Console: "Using long-running upload strategy"
- [ ] Console: "Batch size: 2" (adaptive)
- [ ] Progress indicator shows file-by-file status
- [ ] Upload completes in ~17-25 seconds
- [ ] 5 Document records created
- [ ] Form closes automatically after upload

### Strategy Threshold Testing

- [ ] 3 files × 10MB each → Long-running (exceeds 20MB total)
- [ ] 4 files × 2MB each → Long-running (exceeds 3 file count)
- [ ] 2 files × 11MB each → Long-running (exceeds 10MB per file)
- [ ] 3 files × 6MB each (18MB total) → Sync-parallel (under all thresholds)

### Error Handling

- [ ] 5 files (1 invalid type) → 4 succeed, 1 fails
- [ ] Summary shows "4 of 5 files uploaded successfully"
- [ ] 4 Document records created
- [ ] Error message shows which file failed

### MSAL Token Caching

- [ ] First file: Console shows "Token acquired in 420ms"
- [ ] Files 2-10: Console shows "Token retrieved from cache (5ms)"
- [ ] Verify 82x performance improvement

---

## Success Metrics

### Performance

- ✅ Sync-parallel: 3 files in 3-4 seconds
- ✅ Long-running: 5 large files in 17-25 seconds
- ✅ MSAL token caching: 5ms per file (after first)
- ✅ No browser crashes (even with 10 × 50MB files)

### Reliability

- ✅ Partial success handling works
- ✅ Error messages clear and actionable
- ✅ No orphaned files (if Dataverse fails, files still uploaded)
- ✅ No data loss

### Usability

- ✅ File picker allows multi-select (HTML5)
- ✅ User can remove files before upload
- ✅ Progress indicator shows what's happening
- ✅ Success summary shows results
- ✅ Form closes automatically after upload

---

## References

- **Sprint 7B Task 2 (Baseline):** [TASK-7B-2-FILE-UPLOAD-SPE.md](TASK-7B-2-FILE-UPLOAD-SPE.md)
- **Sprint 7B Overview:** [SPRINT-7B-OVERVIEW.md](SPRINT-7B-OVERVIEW.md)
- **Sprint 7B Updates Summary:** [SPRINT-7B-UPDATES-SUMMARY.md](SPRINT-7B-UPDATES-SUMMARY.md)
- **MSAL Auth Provider:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/auth/MsalAuthProvider.ts`

---

**AI Coding Prompt:**

```
Implement multi-file upload enhancement for Universal Quick Create PCF:

1. Create MultiFileUploadService with adaptive strategy:
   - determineUploadStrategy(): Select sync-parallel or long-running
   - handleSyncParallelUpload(): Fast path for 1-3 small files
   - handleLongRunningUpload(): Safe path for large/many files
   - calculateBatchSize(): Adaptive batching (2-5 based on file size)

2. Update FilePickerField component:
   - Change value from File to File[]
   - Add multiple attribute to input
   - Display file list with remove buttons
   - Show total size summary

3. Create UploadProgress component:
   - Show file-by-file status (✓ uploaded, ↻ uploading, ⏳ waiting, ✗ failed)
   - Progress bar with percentage
   - Current file indicator

4. Update QuickCreateForm:
   - Change selectedFiles state to File[]
   - Add upload progress tracking
   - Show UploadProgress component for multi-file
   - Show success/partial success summary

5. Update UniversalQuickCreatePCF:
   - Initialize MultiFileUploadService in init()
   - Update handleSave() to accept File[]
   - Handle partial success errors

Use existing SdapApiClient (MSAL-enabled) for all uploads.
All logging via logger utility.
Follow existing code patterns from Sprint 7B Task 2.
```
