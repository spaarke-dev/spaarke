/**
 * Command Bar component for Universal Dataset Grid
 * Provides file operation buttons with Fluent UI v9 components
 * Pure React component - no wrapper class needed with single React root
 */

import * as React from 'react';
import {
    Button,
    Tooltip,
    tokens
} from '@fluentui/react-components';
import {
    Add24Regular,
    Delete24Regular,
    ArrowUpload24Regular,
    ArrowDownload24Regular
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
}

/**
 * Fluent UI v9 command bar with file operation buttons.
 *
 * Renders buttons for Add File, Remove File, Update File, and Download.
 * Buttons are enabled/disabled based on selection state and file attachment status.
 */
export const CommandBar: React.FC<CommandBarProps> = ({
    config,
    selectedRecordIds,
    selectedRecords,
    onCommandExecute
}) => {
    const selectedCount = selectedRecordIds.length;
    const selectedRecord = selectedCount === 1 ? selectedRecords[0] : null;

    // Get hasFile status from the selected record
    const hasFile = selectedRecord
        ? (selectedRecord.getValue(config.fieldMappings.hasFile) as boolean) === true
        : false;

    return (
        <div
            style={{
                display: 'flex',
                padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
                background: tokens.colorNeutralBackground2,
                gap: tokens.spacingHorizontalS,
                borderBottom: `1px solid ${tokens.colorNeutralStroke1}`
            }}
        >
            <Tooltip content="Upload a file to the selected document" relationship="label">
                <Button
                    appearance="primary"
                    icon={<Add24Regular />}
                    disabled={selectedCount !== 1 || hasFile}
                    onClick={() => onCommandExecute('addFile')}
                >
                    Add File
                </Button>
            </Tooltip>

            <Tooltip content="Delete the file from the selected document" relationship="label">
                <Button
                    appearance="secondary"
                    icon={<Delete24Regular />}
                    disabled={selectedCount !== 1 || !hasFile}
                    onClick={() => onCommandExecute('removeFile')}
                >
                    Remove File
                </Button>
            </Tooltip>

            <Tooltip content="Replace the file in the selected document" relationship="label">
                <Button
                    appearance="secondary"
                    icon={<ArrowUpload24Regular />}
                    disabled={selectedCount !== 1 || !hasFile}
                    onClick={() => onCommandExecute('updateFile')}
                >
                    Update File
                </Button>
            </Tooltip>

            <Tooltip content="Download the selected file(s)" relationship="label">
                <Button
                    appearance="secondary"
                    icon={<ArrowDownload24Regular />}
                    disabled={selectedCount === 0 || (selectedRecord !== null && !hasFile)}
                    onClick={() => onCommandExecute('downloadFile')}
                >
                    Download
                </Button>
            </Tooltip>

            {selectedCount > 0 && (
                <span
                    style={{
                        padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
                        color: tokens.colorNeutralForeground2,
                        lineHeight: '32px',
                        marginLeft: 'auto'
                    }}
                >
                    {selectedCount} selected
                </span>
            )}
        </div>
    );
};

