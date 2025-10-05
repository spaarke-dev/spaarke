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

    // Update selection when context changes
    React.useEffect(() => {
        const contextSelection = dataset.getSelectedRecordIds() || [];
        setSelectedRecordIds(contextSelection);
    }, [dataset]);

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

    return (
        <div
            style={{
                display: 'flex',
                flexDirection: 'column',
                height: '100%',
                width: '100%'
            }}
        >
            {/* Command Bar - will be replaced with Fluent Toolbar in Task A.3 */}
            <CommandBar
                config={config}
                selectedRecordIds={selectedRecordIds}
                selectedRecords={selectedRecords}
                onCommandExecute={handleCommandExecute}
            />

            {/* Grid - will be replaced with Fluent DataGrid in Task A.2 */}
            <div style={{ flex: 1, overflow: 'auto', padding: '20px' }}>
                <div style={{ textAlign: 'center' }}>
                    <h3>Dataset Grid</h3>
                    <p>Records: {dataset.sortedRecordIds.length}</p>
                    <p>Selected: {selectedRecordIds.length}</p>
                    <p style={{ marginTop: '20px', color: '#666' }}>
                        Task A.2 will implement Fluent UI DataGrid here
                    </p>
                </div>
            </div>
        </div>
    );
};
