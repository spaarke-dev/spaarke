/**
 * Universal Dataset Grid PCF Control
 * Version 2.0.0 - With Fluent UI v9 and SDAP Integration
 */

import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { ThemeProvider } from "./providers/ThemeProvider";
import { CommandBar } from "./components/CommandBar";
import { DEFAULT_GRID_CONFIG, GridConfiguration } from "./types";

export class UniversalDatasetGrid implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement;
    private context: ComponentFramework.Context<IInputs>;
    private notifyOutputChanged: () => void;
    private selectedRecordIds: string[] = [];
    private themeProvider: ThemeProvider;
    private contentContainer: HTMLElement | null = null;
    private commandBar: CommandBar;
    private config: GridConfiguration;

    constructor() {
        try {
            console.log('[UniversalDatasetGrid] Constructor starting...');
            this.themeProvider = new ThemeProvider();
            console.log('[UniversalDatasetGrid] ThemeProvider created');

            this.config = DEFAULT_GRID_CONFIG;
            console.log('[UniversalDatasetGrid] Config set');

            this.commandBar = new CommandBar(this.config);
            console.log('[UniversalDatasetGrid] CommandBar created');

            console.log('[UniversalDatasetGrid] Constructor completed');
        } catch (error) {
            console.error('[UniversalDatasetGrid] CONSTRUCTOR ERROR:', error);
            throw error;
        }
    }

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        try {
            console.log('[UniversalDatasetGrid] Starting init...', {
                container,
                containerType: typeof container,
                hasContainer: !!container,
                controlType: 'standard'
            });

            this.context = context;
            this.notifyOutputChanged = notifyOutputChanged;
            console.log('[UniversalDatasetGrid] Context and notifyOutputChanged set');

            // For virtual controls, container might be null in some scenarios
            // Store it if available, but don't fail if it's not
            if (container) {
                console.log('[UniversalDatasetGrid] Container provided, initializing UI');
                this.container = container;

                this.selectedRecordIds = context.parameters.dataset.getSelectedRecordIds() || [];
                console.log('[UniversalDatasetGrid] Selected record IDs retrieved:', this.selectedRecordIds.length);

                // Initialize Fluent UI theme provider
                console.log('[UniversalDatasetGrid] Initializing ThemeProvider...');
                this.themeProvider.initialize(container);
                console.log('[UniversalDatasetGrid] ThemeProvider initialized');

                // Wait for content container to be available (React 18 createRoot is async)
                const waitForContainer = () => {
                    if (this.themeProvider.isInitialized()) {
                        this.contentContainer = this.themeProvider.getContentContainer();
                        console.log('[UniversalDatasetGrid] Content container retrieved');
                        console.log('[UniversalDatasetGrid] Init completed successfully');
                    } else {
                        console.log('[UniversalDatasetGrid] Waiting for content container...');
                        setTimeout(waitForContainer, 10);
                    }
                };
                waitForContainer();
            } else {
                console.warn('[UniversalDatasetGrid] No container provided - this is expected for virtual controls in some scenarios');
                console.log('[UniversalDatasetGrid] Init completed (no container mode)');
            }

        } catch (error) {
            console.error('[UniversalDatasetGrid] INIT ERROR:', error);
            console.error('[UniversalDatasetGrid] Error details:', JSON.stringify(error, Object.getOwnPropertyNames(error)));

            // Display error in container if available
            if (container) {
                container.innerHTML = `<div style="padding: 20px; color: red; border: 2px solid red;">
                    <h3>UniversalDatasetGrid Init Error</h3>
                    <p><strong>Message:</strong> ${error instanceof Error ? error.message : String(error)}</p>
                    <p><strong>Stack:</strong> <pre>${error instanceof Error ? error.stack : 'N/A'}</pre></p>
                </div>`;
            }

            throw error; // Re-throw to let Power Apps know there was an error
        }
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        console.log('[UniversalDatasetGrid] updateView called', {
            hasContentContainer: !!this.contentContainer,
            isInitialized: this.themeProvider?.isInitialized()
        });

        this.context = context;
        this.selectedRecordIds = context.parameters.dataset.getSelectedRecordIds() || [];

        // Wait for contentContainer to be ready if still initializing
        if (!this.contentContainer && this.themeProvider?.isInitialized()) {
            console.log('[UniversalDatasetGrid] Getting content container in updateView');
            this.contentContainer = this.themeProvider.getContentContainer();
        }

        // Clear content container (not root container - that has FluentProvider)
        if (this.contentContainer) {
            console.log('[UniversalDatasetGrid] Rendering content');
            while (this.contentContainer.firstChild) {
                this.contentContainer.removeChild(this.contentContainer.firstChild);
            }

            // Render command bar
            this.renderCommandBar();

            // Render grid inside Fluent UI theme context
            this.renderMinimalGrid();
        } else {
            console.warn('[UniversalDatasetGrid] updateView called but contentContainer not ready yet');
            // Retry after a short delay
            setTimeout(() => {
                if (this.contentContainer) {
                    this.updateView(context);
                }
            }, 50);
        }
    }

    private renderCommandBar(): void {
        if (!this.contentContainer) {
            return;
        }

        // Get selected records
        const dataset = this.context.parameters.dataset;
        const selectedRecords = this.selectedRecordIds.map(id => dataset.records[id]).filter(r => r);

        // Render command bar
        this.commandBar.render(
            this.selectedRecordIds,
            selectedRecords,
            (commandId: string) => this.handleCommand(commandId)
        );

        // Add command bar to content container
        this.contentContainer.appendChild(this.commandBar.getElement());
    }

    private handleCommand(commandId: string): void {
        const selectedCount = this.selectedRecordIds.length;

        switch (commandId) {
            case 'addFile':
                if (selectedCount === 1) {
                    this.handleAddFile(this.selectedRecordIds[0]);
                }
                break;

            case 'removeFile':
                if (selectedCount === 1) {
                    this.handleRemoveFile(this.selectedRecordIds[0]);
                }
                break;

            case 'updateFile':
                if (selectedCount === 1) {
                    this.handleUpdateFile(this.selectedRecordIds[0]);
                }
                break;

            case 'downloadFile':
                if (selectedCount > 0) {
                    this.handleDownloadFile();
                }
                break;

            default:
                console.warn(`Unknown command: ${commandId}`);
        }
    }

    private handleAddFile(recordId: string): void {
        console.log('Add File command - will implement in Phase 3 (Task 3.2)', recordId);
        // TODO: Implement in Phase 3 - Task 3.2 (File Upload with Progress)
    }

    private handleRemoveFile(recordId: string): void {
        console.log('Remove File command - will implement in Phase 3 (Task 3.4)', recordId);
        // TODO: Implement in Phase 3 - Task 3.4 (Delete Operation)
    }

    private handleUpdateFile(recordId: string): void {
        console.log('Update File command - will implement in Phase 3 (Task 3.5)', recordId);
        // TODO: Implement in Phase 3 - Task 3.5 (Update/Replace File)
    }

    private handleDownloadFile(): void {
        console.log('Download File command - will implement in Phase 3 (Task 3.3)');
        // TODO: Implement in Phase 3 - Task 3.3 (Download Operation)
    }

    private renderMinimalGrid(): void {
        const dataset = this.context.parameters.dataset;

        // Create container
        const gridContainer = document.createElement("div");
        gridContainer.style.width = "100%";
        gridContainer.style.height = "100%";
        gridContainer.style.overflow = "auto";
        gridContainer.style.fontFamily = "Segoe UI, sans-serif";
        gridContainer.style.fontSize = "14px";

        // Create toolbar
        const toolbar = this.createToolbar();
        gridContainer.appendChild(toolbar);

        // Create table
        const table = document.createElement("table");
        table.style.width = "100%";
        table.style.borderCollapse = "collapse";
        table.style.marginTop = "8px";

        // Create header
        const thead = document.createElement("thead");
        const headerRow = document.createElement("tr");
        headerRow.style.backgroundColor = "#f3f2f1";
        headerRow.style.borderBottom = "2px solid #edebe9";

        // Add checkbox column
        const checkboxHeader = document.createElement("th");
        checkboxHeader.style.width = "40px";
        checkboxHeader.style.padding = "8px";
        checkboxHeader.style.textAlign = "center";
        headerRow.appendChild(checkboxHeader);

        // Add column headers
        for (const column of dataset.columns) {
            const th = document.createElement("th");
            th.textContent = column.displayName;
            th.style.padding = "8px";
            th.style.textAlign = "left";
            th.style.fontWeight = "600";
            th.style.color = "#323130";
            headerRow.appendChild(th);
        }

        thead.appendChild(headerRow);
        table.appendChild(thead);

        // Create body
        const tbody = document.createElement("tbody");

        const sortedRecordIds = dataset.sortedRecordIds;
        for (const recordId of sortedRecordIds) {
            const record = dataset.records[recordId];
            const row = document.createElement("tr");
            row.style.borderBottom = "1px solid #edebe9";
            row.style.cursor = "pointer";

            // Hover effect
            row.onmouseenter = () => {
                row.style.backgroundColor = "#f3f2f1";
            };
            row.onmouseleave = () => {
                row.style.backgroundColor = "";
            };

            // Click to open
            row.onclick = () => {
                dataset.openDatasetItem(record.getNamedReference());
            };

            // Checkbox cell
            const checkboxCell = document.createElement("td");
            checkboxCell.style.padding = "8px";
            checkboxCell.style.textAlign = "center";
            const checkbox = document.createElement("input");
            checkbox.type = "checkbox";
            checkbox.checked = this.selectedRecordIds.indexOf(recordId) > -1;
            checkbox.onclick = (e) => {
                e.stopPropagation();
                this.toggleSelection(recordId);
            };
            checkboxCell.appendChild(checkbox);
            row.appendChild(checkboxCell);

            // Data cells
            for (const column of dataset.columns) {
                const td = document.createElement("td");
                const value = record.getFormattedValue(column.name);
                td.textContent = value || "";
                td.style.padding = "8px";
                td.style.color = "#323130";
                row.appendChild(td);
            }

            tbody.appendChild(row);
        }

        table.appendChild(tbody);
        gridContainer.appendChild(table);

        // Add to content container (inside FluentProvider)
        if (this.contentContainer) {
            this.contentContainer.appendChild(gridContainer);
        }
    }

    private createToolbar(): HTMLElement {
        const toolbar = document.createElement("div");
        toolbar.style.display = "flex";
        toolbar.style.gap = "8px";
        toolbar.style.marginBottom = "8px";
        toolbar.style.padding = "8px";
        toolbar.style.backgroundColor = "#faf9f8";
        toolbar.style.borderBottom = "1px solid #edebe9";

        // Refresh button
        const refreshBtn = this.createButton("Refresh", () => {
            this.context.parameters.dataset.refresh();
        });
        toolbar.appendChild(refreshBtn);

        // Selection info
        if (this.selectedRecordIds.length > 0) {
            const selectionInfo = document.createElement("span");
            selectionInfo.textContent = `${this.selectedRecordIds.length} selected`;
            selectionInfo.style.padding = "6px 12px";
            selectionInfo.style.color = "#605e5c";
            selectionInfo.style.lineHeight = "32px";
            toolbar.appendChild(selectionInfo);

            // Clear selection button
            const clearBtn = this.createButton("Clear Selection", () => {
                this.selectedRecordIds = [];
                this.context.parameters.dataset.clearSelectedRecordIds();
                this.notifyOutputChanged();
                this.updateView(this.context);
            });
            toolbar.appendChild(clearBtn);
        }

        return toolbar;
    }

    private createButton(text: string, onClick: () => void): HTMLButtonElement {
        const button = document.createElement("button");
        button.textContent = text;
        button.onclick = onClick;
        button.style.padding = "6px 12px";
        button.style.border = "1px solid #8a8886";
        button.style.backgroundColor = "#ffffff";
        button.style.color = "#323130";
        button.style.cursor = "pointer";
        button.style.borderRadius = "2px";
        button.style.fontSize = "14px";
        button.style.fontFamily = "Segoe UI, sans-serif";

        button.onmouseenter = () => {
            button.style.backgroundColor = "#f3f2f1";
        };
        button.onmouseleave = () => {
            button.style.backgroundColor = "#ffffff";
        };

        return button;
    }

    private toggleSelection(recordId: string): void {
        const index = this.selectedRecordIds.indexOf(recordId);
        if (index > -1) {
            this.selectedRecordIds.splice(index, 1);
        } else {
            this.selectedRecordIds.push(recordId);
        }
        this.context.parameters.dataset.setSelectedRecordIds(this.selectedRecordIds);
        this.notifyOutputChanged();
        this.updateView(this.context);
    }

    public getOutputs(): IOutputs {
        return {};
    }

    public destroy(): void {
        // Clean up command bar
        this.commandBar.destroy();

        // Clean up theme provider and unmount React components
        this.themeProvider.destroy();

        // Clear references
        this.contentContainer = null;
    }
}
