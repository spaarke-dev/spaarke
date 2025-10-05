/**
 * Command Bar component for Universal Dataset Grid
 * Provides file operation buttons with Fluent UI v9 components
 * Uses selective imports from @fluentui/react-components
 */

import * as React from 'react';
import * as ReactDOM from 'react-dom/client';
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
 * Props for CommandBarComponent.
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
 * Fluent UI command bar with file operation buttons.
 *
 * Renders buttons for Add File, Remove File, Update File, and Download.
 * Buttons are enabled/disabled based on selection state and file attachment status.
 */
const CommandBarComponent: React.FC<CommandBarProps> = ({
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

/**
 * Command bar wrapper class for PCF integration.
 *
 * Manages React component lifecycle and provides simple interface
 * for the main PCF control to render and update the command bar.
 */
export class CommandBar {
    private container: HTMLDivElement;
    private root: ReactDOM.Root | null = null;
    private config: GridConfiguration;

    /**
     * Creates a new CommandBar instance.
     *
     * @param config - Grid configuration
     */
    constructor(config: GridConfiguration) {
        this.container = document.createElement('div');
        this.config = config;
    }

    /**
     * Render the command bar with current state.
     *
     * @param selectedRecordIds - IDs of selected records
     * @param selectedRecords - Full selected record objects
     * @param onCommandExecute - Callback for command execution
     */
    public render(
        selectedRecordIds: string[],
        selectedRecords: ComponentFramework.PropertyHelper.DataSetApi.EntityRecord[],
        onCommandExecute: (commandId: string) => void
    ): void {
        // Create root on first render
        if (!this.root) {
            this.root = ReactDOM.createRoot(this.container);
        }

        // Render using React 18 API
        this.root.render(
            React.createElement(CommandBarComponent, {
                config: this.config,
                selectedRecordIds,
                selectedRecords,
                onCommandExecute
            })
        );
    }

    /**
     * Get the DOM element containing the command bar.
     *
     * @returns Command bar container element
     */
    public getElement(): HTMLDivElement {
        return this.container;
    }

    /**
     * Clean up and unmount the command bar.
     */
    public destroy(): void {
        if (this.root) {
            this.root.unmount();
            this.root = null;
        }
    }
}
