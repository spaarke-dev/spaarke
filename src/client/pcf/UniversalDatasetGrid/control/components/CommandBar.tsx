/**
 * Command Bar component for Universal Dataset Grid
 * Provides file operation buttons using Fluent UI v9 Toolbar component
 * Pure React component - no wrapper class needed with single React root
 */

import * as React from 'react';
import {
    Toolbar,
    ToolbarButton,
    ToolbarDivider,
    Tooltip,
    tokens
} from '@fluentui/react-components';
import {
    Add24Regular,
    Delete24Regular,
    ArrowUpload24Regular,
    ArrowDownload24Regular,
    ArrowClockwise24Regular
} from '@fluentui/react-icons';
import { GridConfiguration } from '../types';

/**
 * Props for CommandBar.
 */
interface CommandBarProps {
    /** Grid configuration */
    config: GridConfiguration;

    /** Selected record IDs */
    selectedRecordIds: string[];

    /** Selected records (PCF dataset records) */
    selectedRecords: ComponentFramework.PropertyHelper.DataSetApi.EntityRecord[];

    /** Command execution callback */
    onCommandExecute: (commandId: string) => void;

    /** Refresh callback */
    onRefresh: () => void;
}

/**
 * Fluent UI v9 command bar using Toolbar component.
 *
 * Provides file operation buttons with proper layout, spacing, and theming.
 * Buttons are enabled/disabled based on selection state and file attachment status.
 */
export const CommandBar: React.FC<CommandBarProps> = ({
    config,
    selectedRecordIds,
    selectedRecords,
    onCommandExecute,
    onRefresh
}) => {
    console.log('[CommandBar] Component rendering', {
        selectedRecordIds,
        selectedRecordCount: selectedRecordIds.length,
        selectedRecordsCount: selectedRecords.length
    });

    const selectedCount = selectedRecordIds.length;
    const selectedRecord = selectedCount === 1 ? selectedRecords[0] : null;

    // Get hasFile status from the selected record
    const hasFileValue = selectedRecord?.getValue(config.fieldMappings.hasFile);
    const hasFile = hasFileValue === true || hasFileValue === 1 || hasFileValue === '1';

    // Debug logging
    if (selectedRecord) {
        console.log('[CommandBar] Selected record debug:', {
            recordId: selectedRecord.getRecordId(),
            hasFileFieldName: config.fieldMappings.hasFile,
            hasFileRawValue: hasFileValue,
            hasFileResult: hasFile
        });
    }

    return (
        <div>
            <div style={{
                backgroundColor: 'red',
                color: 'white',
                padding: '10px',
                fontSize: '20px',
                fontWeight: 'bold'
            }}>
                🔴 VERSION 2.1.4 - BFF API SCOPE FIXED 🔴
            </div>
            <Toolbar
                aria-label="File operations toolbar"
                style={{
                    borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
                    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`
                }}
            >
            {/* Add File Button */}
            <Tooltip content="Upload a file to the selected document" relationship="label">
                <ToolbarButton
                    appearance="primary"
                    icon={<Add24Regular />}
                    disabled={false}
                    onClick={() => onCommandExecute('addFile')}
                >
                    Add File
                </ToolbarButton>
            </Tooltip>

            <ToolbarDivider />

            {/* Remove File Button */}
            <Tooltip content="Delete the file from the selected document" relationship="label">
                <ToolbarButton
                    icon={<Delete24Regular />}
                    disabled={false}
                    onClick={() => onCommandExecute('removeFile')}
                >
                    Remove File
                </ToolbarButton>
            </Tooltip>

            {/* Update File Button */}
            <Tooltip content="Replace the file in the selected document" relationship="label">
                <ToolbarButton
                    icon={<ArrowUpload24Regular />}
                    disabled={false}
                    onClick={() => onCommandExecute('updateFile')}
                >
                    Update File
                </ToolbarButton>
            </Tooltip>

            {/* Download Button */}
            <Tooltip content="Download the selected file(s)" relationship="label">
                <ToolbarButton
                    icon={<ArrowDownload24Regular />}
                    disabled={false}
                    onClick={() => onCommandExecute('downloadFile')}
                >
                    Download
                </ToolbarButton>
            </Tooltip>

            <ToolbarDivider />

            {/* Refresh Button */}
            <Tooltip content="Refresh the dataset" relationship="label">
                <ToolbarButton
                    icon={<ArrowClockwise24Regular />}
                    onClick={onRefresh}
                >
                    Refresh
                </ToolbarButton>
            </Tooltip>

            {/* Selection Counter - Always rendered to prevent layout shift */}
            <ToolbarDivider style={{ visibility: selectedCount > 0 ? 'visible' : 'hidden' }} />
            <div
                style={{
                    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
                    color: tokens.colorNeutralForeground2,
                    visibility: selectedCount > 0 ? 'visible' : 'hidden',
                    minWidth: '100px' // Reserve space to prevent horizontal shift
                }}
            >
                {selectedCount > 0 ? `${selectedCount} selected` : '\u00A0'}
            </div>
        </Toolbar>
        </div>
    );
};

