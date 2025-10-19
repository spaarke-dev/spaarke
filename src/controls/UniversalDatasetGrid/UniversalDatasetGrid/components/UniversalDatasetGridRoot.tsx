/**
 * Universal Dataset Grid Root Component
 *
 * Main React component for the Universal Dataset Grid.
 * Receives PCF context as props and manages the component tree.
 */

import * as React from 'react';
import { IInputs } from '../generated/ManifestTypes';
import { GridConfiguration } from '../types';
import { CommandBar } from './CommandBar';
import { DatasetGrid } from './DatasetGrid';
import { ConfirmDialog } from './ConfirmDialog';
import { SdapApiClientFactory } from '../services/SdapApiClientFactory';
import { FileDownloadService } from '../services/FileDownloadService';
import { FileDeleteService } from '../services/FileDeleteService';
import { FileReplaceService } from '../services/FileReplaceService';
import { logger } from '../utils/logger';

/**
 * Debounce utility to limit function call frequency
 */
function debounce<T extends (...args: unknown[]) => void>(
    func: T,
    wait: number
): (...args: Parameters<T>) => void {
    let timeout: ReturnType<typeof setTimeout> | null = null;

    return (...args: Parameters<T>) => {
        if (timeout) {
            clearTimeout(timeout);
        }
        timeout = setTimeout(() => {
            func(...args);
        }, wait);
    };
}

interface UniversalDatasetGridRootProps {
    /** PCF context - passed from index.ts */
    context: ComponentFramework.Context<IInputs>;

    /** Callback to notify Power Apps of state changes */
    notifyOutputChanged: () => void;

    /** Grid configuration */
    config: GridConfiguration;
}

/**
 * Main React component for Universal Dataset Grid.
 *
 * This component is rendered once in init() and receives updated
 * props in updateView() - no DOM recreation.
 */
export const UniversalDatasetGridRoot: React.FC<UniversalDatasetGridRootProps> = ({
    context,
    notifyOutputChanged,
    config
}) => {
    // Get dataset from context
    const dataset = context.parameters.dataset;

    // Track selected record IDs in React state
    const [selectedRecordIds, setSelectedRecordIds] = React.useState<string[]>(
        dataset.getSelectedRecordIds() || []
    );

    // State for delete confirmation dialog
    const [deleteDialogOpen, setDeleteDialogOpen] = React.useState(false);
    const [fileToDelete, setFileToDelete] = React.useState<{
        documentId: string;
        driveId: string;
        itemId: string;
        fileName: string;
    } | null>(null);

    // Debounce notifyOutputChanged to reduce PCF call frequency (Task C.2)
    const debouncedNotify = React.useMemo(
        () => debounce(notifyOutputChanged, 300),
        [notifyOutputChanged]
    );

    // Sync selection with Power Apps
    const handleSelectionChange = React.useCallback((recordIds: string[]) => {
        setSelectedRecordIds(recordIds);
        dataset.setSelectedRecordIds(recordIds);
        debouncedNotify(); // Debounced to prevent excessive PCF calls
    }, [dataset, debouncedNotify]);

    // Update selection when context changes
    React.useEffect(() => {
        const contextSelection = dataset.getSelectedRecordIds() || [];
        console.log('[UniversalDatasetGridRoot] Selection changed:', {
            contextSelection,
            currentSelection: selectedRecordIds,
            recordCount: Object.keys(dataset.records).length
        });
        if (JSON.stringify(contextSelection) !== JSON.stringify(selectedRecordIds)) {
            setSelectedRecordIds(contextSelection);
        }
    }, [dataset, selectedRecordIds]);

    /**
     * Handle file download command
     */
    const handleDownloadFile = React.useCallback(async () => {
        try {
            // Validate selection
            if (selectedRecordIds.length === 0) {
                logger.warn('UniversalDatasetGridRoot', 'Download requires at least one selected record');
                return;
            }

            logger.info('UniversalDatasetGridRoot', `Downloading ${selectedRecordIds.length} file(s)`);

            // Get SDAP API base URL from config
            const baseUrl = config.sdapConfig.baseUrl;

            // Create API client and download service
            const apiClient = SdapApiClientFactory.create(baseUrl);
            const downloadService = new FileDownloadService(apiClient);

            // Download each selected file
            for (const recordId of selectedRecordIds) {
                const record = dataset.records[recordId];

                if (!record) {
                    logger.warn('UniversalDatasetGridRoot', `Record not found: ${recordId}`);
                    continue;
                }

                // Get file metadata from Dataverse record
                const driveId = record.getFormattedValue(config.fieldMappings.graphDriveId);
                const itemId = record.getFormattedValue(config.fieldMappings.graphItemId);
                const fileName = record.getFormattedValue(config.fieldMappings.fileName);

                if (!driveId || !itemId || !fileName) {
                    logger.error('UniversalDatasetGridRoot', 'Missing required fields for download', {
                        recordId,
                        hasDriveId: !!driveId,
                        hasItemId: !!itemId,
                        hasFileName: !!fileName
                    });
                    continue;
                }

                // Download file
                const result = await downloadService.downloadFile(driveId, itemId, fileName);

                if (result.success) {
                    logger.info('UniversalDatasetGridRoot', `File downloaded: ${fileName}`);
                } else {
                    logger.error('UniversalDatasetGridRoot', `Download failed: ${result.error}`);
                }

                // Small delay between downloads if multiple files
                if (selectedRecordIds.length > 1) {
                    await new Promise(resolve => setTimeout(resolve, 200));
                }
            }

            logger.info('UniversalDatasetGridRoot', 'Download complete');

        } catch (error) {
            logger.error('UniversalDatasetGridRoot', 'Download handler error', error);
        }
    }, [selectedRecordIds, dataset, context, config]);

    /**
     * Handle file delete command (shows confirmation dialog)
     */
    const handleDeleteFile = React.useCallback(async () => {
        try {
            // Validate selection
            if (selectedRecordIds.length !== 1) {
                logger.warn('UniversalDatasetGridRoot', 'Delete requires exactly one selected record');
                return;
            }

            const record = dataset.records[selectedRecordIds[0]];

            if (!record) {
                logger.warn('UniversalDatasetGridRoot', 'Record not found');
                return;
            }

            // Get file metadata from Dataverse record
            const documentId = record.getRecordId();
            const driveId = record.getFormattedValue(config.fieldMappings.graphDriveId);
            const itemId = record.getFormattedValue(config.fieldMappings.graphItemId);
            const fileName = record.getFormattedValue(config.fieldMappings.fileName);

            if (!documentId || !driveId || !itemId || !fileName) {
                logger.error('UniversalDatasetGridRoot', 'Missing required fields for delete', {
                    hasDocumentId: !!documentId,
                    hasDriveId: !!driveId,
                    hasItemId: !!itemId,
                    hasFileName: !!fileName
                });
                return;
            }

            // Store delete info and show confirmation dialog
            setFileToDelete({ documentId, driveId, itemId, fileName });
            setDeleteDialogOpen(true);

        } catch (error) {
            logger.error('UniversalDatasetGridRoot', 'Delete click handler error', error);
        }
    }, [selectedRecordIds, dataset, config]);

    /**
     * Handle delete confirmation (executes delete)
     */
    const handleDeleteConfirm = React.useCallback(async () => {
        if (!fileToDelete) return;

        try {
            logger.info('UniversalDatasetGridRoot', `Confirming delete: ${fileToDelete.fileName}`);

            // Get SDAP API base URL from config
            const baseUrl = config.sdapConfig.baseUrl;

            // Create API client and delete service
            const apiClient = SdapApiClientFactory.create(baseUrl);
            const deleteService = new FileDeleteService(apiClient, context);

            // Execute delete
            const result = await deleteService.deleteFile(
                fileToDelete.documentId,
                fileToDelete.driveId,
                fileToDelete.itemId,
                fileToDelete.fileName
            );

            if (result.success) {
                logger.info('UniversalDatasetGridRoot', 'File deleted successfully');

                // Refresh grid to show updated record (hasFile = false)
                dataset.refresh();
                notifyOutputChanged();

            } else {
                logger.error('UniversalDatasetGridRoot', `Delete failed: ${result.error}`);
            }

        } catch (error) {
            logger.error('UniversalDatasetGridRoot', 'Delete confirmation handler error', error);
        } finally {
            // Close dialog and clear state
            setDeleteDialogOpen(false);
            setFileToDelete(null);
        }
    }, [fileToDelete, context, config, dataset, notifyOutputChanged]);

    /**
     * Handle delete cancellation
     */
    const handleDeleteCancel = React.useCallback(() => {
        logger.debug('UniversalDatasetGridRoot', 'Delete cancelled');
        setDeleteDialogOpen(false);
        setFileToDelete(null);
    }, []);

    /**
     * Handle file replace command
     */
    const handleReplaceFile = React.useCallback(async () => {
        try {
            // Validate selection
            if (selectedRecordIds.length !== 1) {
                logger.warn('UniversalDatasetGridRoot', 'Replace requires exactly one selected record');
                return;
            }

            const record = dataset.records[selectedRecordIds[0]];

            if (!record) {
                logger.warn('UniversalDatasetGridRoot', 'Record not found');
                return;
            }

            // Get file metadata from Dataverse record
            const documentId = record.getRecordId();
            const driveId = record.getFormattedValue(config.fieldMappings.graphDriveId);
            const itemId = record.getFormattedValue(config.fieldMappings.graphItemId);

            if (!documentId || !driveId || !itemId) {
                logger.error('UniversalDatasetGridRoot', 'Missing required fields for replace', {
                    hasDocumentId: !!documentId,
                    hasDriveId: !!driveId,
                    hasItemId: !!itemId
                });
                return;
            }

            logger.info('UniversalDatasetGridRoot', 'Starting file replace');

            // Get SDAP API base URL from config
            const baseUrl = config.sdapConfig.baseUrl;

            // Create API client and replace service
            const apiClient = SdapApiClientFactory.create(baseUrl);
            const replaceService = new FileReplaceService(apiClient, context);

            // Show file picker and execute replace
            const result = await replaceService.pickAndReplaceFile(documentId, driveId, itemId);

            if (result.success) {
                logger.info('UniversalDatasetGridRoot', 'File replaced successfully');

                // Refresh grid to show updated record
                dataset.refresh();
                notifyOutputChanged();

            } else {
                // User may have cancelled or error occurred
                if (result.error !== 'Replace cancelled by user') {
                    logger.error('UniversalDatasetGridRoot', `Replace failed: ${result.error}`);
                }
            }

        } catch (error) {
            logger.error('UniversalDatasetGridRoot', 'Replace handler error', error);
        }
    }, [selectedRecordIds, dataset, context, config, notifyOutputChanged]);

    // Handle command execution
    const handleCommandExecute = React.useCallback(async (commandId: string) => {
        logger.info('UniversalDatasetGridRoot', `Command executed: ${commandId}`);

        switch (commandId) {
            case 'addFile':
                logger.info('UniversalDatasetGridRoot', 'Add File - will implement in future task');
                break;
            case 'removeFile':
                await handleDeleteFile();
                break;
            case 'updateFile':
                await handleReplaceFile();
                break;
            case 'downloadFile':
                await handleDownloadFile();
                break;
            default:
                logger.warn('UniversalDatasetGridRoot', `Unknown command: ${commandId}`);
        }
    }, [handleDownloadFile, handleDeleteFile, handleReplaceFile]);

    // Get selected records for command bar
    const selectedRecords = React.useMemo(() => {
        const records = selectedRecordIds
            .map(id => dataset.records[id])
            .filter(record => record != null);
        console.log('[UniversalDatasetGridRoot] Selected records for CommandBar:', {
            selectedRecordIds,
            recordsFound: records.length,
            totalRecords: Object.keys(dataset.records).length
        });
        return records;
    }, [selectedRecordIds, dataset.records]);

    // Handle dataset refresh
    const handleRefresh = React.useCallback(() => {
        console.log('[UniversalDatasetGridRoot] Refreshing dataset');
        dataset.refresh();
    }, [dataset]);

    return (
        <div
            style={{
                display: 'flex',
                flexDirection: 'column',
                height: '100%',
                width: '100%',
                position: 'relative'
            }}
        >
            {/* Fluent UI Toolbar - Task A.3 */}
            <CommandBar
                config={config}
                selectedRecordIds={selectedRecordIds}
                selectedRecords={selectedRecords}
                onCommandExecute={handleCommandExecute}
                onRefresh={handleRefresh}
            />

            {/* Fluent UI DataGrid - Task A.2 */}
            <DatasetGrid
                dataset={dataset}
                selectedRecordIds={selectedRecordIds}
                onSelectionChange={handleSelectionChange}
            />

            {/* Delete Confirmation Dialog - Task 3 */}
            <ConfirmDialog
                open={deleteDialogOpen}
                title="Delete File"
                message={`Are you sure you want to delete "${fileToDelete?.fileName}"? This action cannot be undone.`}
                confirmLabel="Delete"
                cancelLabel="Cancel"
                onConfirm={handleDeleteConfirm}
                onCancel={handleDeleteCancel}
            />

            {/* Version indicator */}
            <div style={{
                position: 'absolute',
                bottom: '2px',
                right: '5px',
                fontSize: '8px',
                color: '#666',
                userSelect: 'none',
                pointerEvents: 'none',
                zIndex: 1000
            }}>
                v2.0.9
            </div>
        </div>
    );
};
