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

    // Sync selection with Power Apps
    const handleSelectionChange = React.useCallback((recordIds: string[]) => {
        setSelectedRecordIds(recordIds);
        dataset.setSelectedRecordIds(recordIds);
        notifyOutputChanged();
    }, [dataset, notifyOutputChanged]);

    // Update selection when context changes
    React.useEffect(() => {
        const contextSelection = dataset.getSelectedRecordIds() || [];
        if (JSON.stringify(contextSelection) !== JSON.stringify(selectedRecordIds)) {
            setSelectedRecordIds(contextSelection);
        }
    }, [dataset, selectedRecordIds]);

    // Handle command execution
    const handleCommandExecute = React.useCallback((commandId: string) => {
        console.log(`[UniversalDatasetGridRoot] Command executed: ${commandId}`);

        switch (commandId) {
            case 'addFile':
                console.log('Add File - will implement in SDAP phase');
                break;
            case 'removeFile':
                console.log('Remove File - will implement in SDAP phase');
                break;
            case 'updateFile':
                console.log('Update File - will implement in SDAP phase');
                break;
            case 'downloadFile':
                console.log('Download File - will implement in SDAP phase');
                break;
            default:
                console.warn(`Unknown command: ${commandId}`);
        }
    }, []);

    // Get selected records for command bar
    const selectedRecords = React.useMemo(() => {
        return selectedRecordIds
            .map(id => dataset.records[id])
            .filter(record => record != null);
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
                width: '100%'
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
        </div>
    );
};
