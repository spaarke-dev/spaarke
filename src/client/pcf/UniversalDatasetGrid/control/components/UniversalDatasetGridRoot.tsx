/**
 * Universal Dataset Grid Root Component
 *
 * Main React component for the Universal Dataset Grid.
 * Receives PCF context as props and manages the component tree.
 */

import * as React from 'react';
import { IInputs } from '../generated/ManifestTypes';
import {
    GridConfiguration,
    CalendarFilter,
    OptimisticRowUpdateRequest,
    OptimisticUpdateResult,
    SpaarkeGridApi
} from '../types';
import { CommandBar } from './CommandBar';
import { DatasetGrid } from './DatasetGrid';
import { ConfirmDialog } from './ConfirmDialog';
import { SdapApiClientFactory } from '../services/SdapApiClientFactory';
import { FileDownloadService } from '../services/FileDownloadService';
import { FileDeleteService } from '../services/FileDeleteService';
import { FileReplaceService } from '../services/FileReplaceService';
import { logger } from '../utils/logger';
import { applyDateFilter } from '../utils/dateFilter';

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

    /**
     * Calendar filter (Task 010)
     * Parsed from calendarFilter property JSON string
     * Used for filtering grid data by date
     */
    calendarFilter?: CalendarFilter | null;

    /**
     * Callback when a row is clicked (Task 012 - bi-directional sync).
     * Emits the due date for calendar highlighting.
     * @param date - ISO date string (YYYY-MM-DD) or null if no date
     */
    onRowClick?: (date: string | null) => void;
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
    config,
    calendarFilter,
    onRowClick
}) => {
    // Get dataset from context
    const dataset = context.parameters.dataset;

    // Track previous filter to detect changes (avoid unnecessary re-filtering)
    const prevFilterRef = React.useRef<string | null>(null);

    // Apply calendar filter to dataset when it changes (Task 011)
    React.useEffect(() => {
        // Serialize filter for comparison
        const currentFilterJson = calendarFilter ? JSON.stringify(calendarFilter) : null;
        const prevFilterJson = prevFilterRef.current;

        // Skip if filter hasn't changed
        if (currentFilterJson === prevFilterJson) {
            return;
        }

        // Update ref for next comparison
        prevFilterRef.current = currentFilterJson;

        // Apply or clear the filter
        if (calendarFilter) {
            logger.info('UniversalDatasetGridRoot', 'Applying calendar filter:', calendarFilter);
            const wasApplied = applyDateFilter(dataset, calendarFilter);
            logger.info('UniversalDatasetGridRoot', `Filter ${wasApplied ? 'applied' : 'cleared'}`);
        } else {
            logger.debug('UniversalDatasetGridRoot', 'No calendar filter - clearing any existing filter');
            applyDateFilter(dataset, null);
        }
    }, [calendarFilter, dataset]);

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

    // =========================================================================
    // Optimistic Row Update (Task 015)
    // =========================================================================

    /**
     * Reference to the optimistic update handler from DatasetGrid.
     * This is set when DatasetGrid registers its handler via callback.
     */
    const optimisticUpdateHandlerRef = React.useRef<
        ((request: OptimisticRowUpdateRequest) => OptimisticUpdateResult) | null
    >(null);

    /**
     * Callback for DatasetGrid to register its optimistic update handler.
     * Called during DatasetGrid initialization.
     */
    const handleRegisterOptimisticUpdate = React.useCallback(
        (updateFn: (request: OptimisticRowUpdateRequest) => OptimisticUpdateResult) => {
            optimisticUpdateHandlerRef.current = updateFn;
            logger.info('UniversalDatasetGridRoot', 'Optimistic update handler registered');
        },
        []
    );

    /**
     * Expose the grid API via window.spaarkeGrid for Side Pane access.
     * This allows external components to trigger optimistic updates.
     */
    React.useEffect(() => {
        // Create the API object
        const gridApi: SpaarkeGridApi = {
            updateRow: (request: OptimisticRowUpdateRequest): OptimisticUpdateResult => {
                if (!optimisticUpdateHandlerRef.current) {
                    logger.warn('UniversalDatasetGridRoot', 'Optimistic update called before handler registered');
                    return {
                        success: false,
                        error: 'Grid not ready - optimistic update handler not registered',
                        rollback: () => {}
                    };
                }

                logger.info('UniversalDatasetGridRoot', `window.spaarkeGrid.updateRow called for record ${request.recordId}`);
                return optimisticUpdateHandlerRef.current(request);
            },
            refresh: () => {
                logger.info('UniversalDatasetGridRoot', 'window.spaarkeGrid.refresh called');
                dataset.refresh();
            }
        };

        // Attach to window
        window.spaarkeGrid = gridApi;
        logger.info('UniversalDatasetGridRoot', 'window.spaarkeGrid API exposed');

        // Cleanup on unmount
        return () => {
            logger.info('UniversalDatasetGridRoot', 'Cleaning up window.spaarkeGrid API');
            delete window.spaarkeGrid;
        };
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

            {/* Fluent UI DataGrid - Task A.2, Task 014 (checkbox column), Task 012 (row click), Task 015 (optimistic update) */}
            <DatasetGrid
                dataset={dataset}
                selectedRecordIds={selectedRecordIds}
                onSelectionChange={handleSelectionChange}
                enableCheckboxSelection={config.enableCheckboxSelection ?? true}
                onRowClick={onRowClick}
                onRegisterOptimisticUpdate={handleRegisterOptimisticUpdate}
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
                v2.2.0
            </div>
        </div>
    );
};
