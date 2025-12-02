# Sprint 6 - Phase 2: Enhanced Universal Grid Implementation
**Date:** October 4, 2025
**Sprint:** 6 - SDAP + Universal Dataset Grid Integration
**Phase:** 2 - Enhanced Universal Grid with Custom Commands
**Status:** 🔴 READY TO START
**Estimated Duration:** 16 hours (2 days)

---

## Phase 2 Overview

This phase enhances the minimal Universal Dataset Grid PCF control with custom command support, configuration parsing, and command execution framework to prepare for SDAP integration.

**Dependencies:**
- ✅ Phase 1 Technical Specification complete
- ✅ Minimal PCF control deployed and working
- ✅ SDAP API endpoints validated
- ✅ Document entity schema confirmed

**Deliverables:**
1. Enhanced PCF control with custom command bar
2. Configuration parsing module
3. Command execution framework
4. Updated control manifest
5. Build and deployment scripts
6. Unit tests for new functionality

---

## Task Breakdown

### Task 2.1: Add Configuration Support (3 hours)

**Objective:** Implement configuration parsing and validation for the PCF control.

**Acceptance Criteria:**
- [ ] Parse `configJson` parameter
- [ ] Validate configuration schema
- [ ] Handle missing/invalid configuration gracefully
- [ ] Store configuration in control state
- [ ] Log configuration errors to console

**Implementation Steps:**

1. **Create Configuration Interface**

File: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/types/Config.ts`

```typescript
export interface GridConfiguration {
    entityName: string;
    apiConfig: ApiConfiguration;
    fileConfig: FileConfiguration;
    customCommands: CustomCommandsConfiguration;
    fieldMappings: FieldMappings;
    ui: UiConfiguration;
    permissions: PermissionsConfiguration;
}

export interface ApiConfiguration {
    baseUrl: string;
    version: string;
    timeout: number;
}

export interface FileConfiguration {
    maxFileSize: number;
    allowedExtensions: string[];
    uploadChunkSize: number;
}

export interface CustomCommandsConfiguration {
    enabled: boolean;
    commands: string[];
}

export interface FieldMappings {
    documentId: string;
    hasFile: string;
    fileName: string;
    fileSize: string;
    mimeType: string;
    graphItemId: string;
    graphDriveId: string;
    containerId: string;
    sharepointUrl: string;
}

export interface UiConfiguration {
    showCommandBar: boolean;
    showSharePointLink: boolean;
    linkColumn: string;
    linkLabel: string;
}

export interface PermissionsConfiguration {
    checkPermissions: boolean;
    requiredRoles: string[];
}
```

2. **Create Configuration Parser**

File: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/ConfigParser.ts`

```typescript
import { GridConfiguration } from "../types/Config";

export class ConfigParser {
    static parse(configJson: string): GridConfiguration | null {
        try {
            if (!configJson || configJson.trim() === "") {
                console.warn("No configuration provided, using defaults");
                return this.getDefaultConfig();
            }

            const config = JSON.parse(configJson);
            const validation = this.validate(config);

            if (!validation.isValid) {
                console.error("Configuration validation failed:", validation.errors);
                return null;
            }

            return config as GridConfiguration;
        } catch (error) {
            console.error("Failed to parse configuration:", error);
            return null;
        }
    }

    static validate(config: any): { isValid: boolean; errors: string[] } {
        const errors: string[] = [];

        // Required fields
        if (!config.entityName) errors.push("entityName is required");
        if (!config.apiConfig?.baseUrl) errors.push("apiConfig.baseUrl is required");
        if (!config.fieldMappings) errors.push("fieldMappings is required");

        // File size validation
        if (config.fileConfig?.maxFileSize > 4194304) {
            errors.push("maxFileSize cannot exceed 4MB (4194304 bytes)");
        }

        // Field mappings validation
        const requiredMappings = ["documentId", "hasFile", "fileName", "graphItemId", "graphDriveId"];
        for (const field of requiredMappings) {
            if (!config.fieldMappings?.[field]) {
                errors.push(`fieldMappings.${field} is required`);
            }
        }

        return {
            isValid: errors.length === 0,
            errors: errors
        };
    }

    static getDefaultConfig(): GridConfiguration {
        return {
            entityName: "sprk_document",
            apiConfig: {
                baseUrl: "https://spe-bff-api-dev.azurewebsites.net",
                version: "v1",
                timeout: 300000
            },
            fileConfig: {
                maxFileSize: 4194304,
                allowedExtensions: [".pdf", ".docx", ".xlsx", ".pptx", ".txt", ".jpg", ".png"],
                uploadChunkSize: 327680
            },
            customCommands: {
                enabled: true,
                commands: ["addFile", "removeFile", "updateFile", "downloadFile"]
            },
            fieldMappings: {
                documentId: "sprk_documentid",
                hasFile: "sprk_hasfile",
                fileName: "sprk_filename",
                fileSize: "sprk_filesize",
                mimeType: "sprk_mimetype",
                graphItemId: "sprk_graphitemid",
                graphDriveId: "sprk_graphdriveid",
                containerId: "sprk_containerid",
                sharepointUrl: "sprk_sharepointurl"
            },
            ui: {
                showCommandBar: true,
                showSharePointLink: true,
                linkColumn: "sprk_sharepointurl",
                linkLabel: "Open in SharePoint"
            },
            permissions: {
                checkPermissions: true,
                requiredRoles: []
            }
        };
    }
}
```

3. **Update Control to Use Configuration**

Update `index.ts`:

```typescript
import { ConfigParser } from "./services/ConfigParser";
import { GridConfiguration } from "./types/Config";

export class UniversalDatasetGrid implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement;
    private context: ComponentFramework.Context<IInputs>;
    private config: GridConfiguration | null = null;

    public init(context: ComponentFramework.Context<IInputs>, notifyOutputChanged: () => void, state: ComponentFramework.Dictionary, container: HTMLDivElement): void {
        this.context = context;
        this.container = container;

        // Parse configuration
        const configJson = context.parameters.configJson?.raw || "";
        this.config = ConfigParser.parse(configJson);

        if (!this.config) {
            this.showConfigurationError();
            return;
        }

        console.log("Grid initialized with config:", this.config);
    }

    private showConfigurationError(): void {
        const errorDiv = document.createElement("div");
        errorDiv.className = "config-error";
        errorDiv.innerHTML = `
            <h3>Configuration Error</h3>
            <p>The grid control is not configured correctly. Please check the console for details.</p>
        `;
        errorDiv.style.cssText = "padding: 20px; background: #fff3cd; border: 1px solid #ffc107; color: #856404;";
        this.container.appendChild(errorDiv);
    }
}
```

**Testing:**
- [ ] Test with valid configuration JSON
- [ ] Test with invalid configuration (missing fields)
- [ ] Test with malformed JSON
- [ ] Test with default configuration (empty string)

---

### Task 2.2: Create Custom Command Bar UI (4 hours)

**Objective:** Render custom command buttons in the grid header.

**Acceptance Criteria:**
- [ ] Command bar renders above grid
- [ ] Buttons render with correct labels
- [ ] Buttons styled consistently
- [ ] Buttons enable/disable based on selection
- [ ] Buttons show tooltips on hover

**Implementation Steps:**

1. **Create Command Button Component**

File: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/CommandButton.ts`

```typescript
export interface CommandButtonConfig {
    id: string;
    label: string;
    tooltip: string;
    enabled: boolean;
    onClick: () => void;
}

export class CommandButton {
    private button: HTMLButtonElement;

    constructor(config: CommandButtonConfig) {
        this.button = document.createElement("button");
        this.button.className = "command-button";
        this.button.setAttribute("data-command-id", config.id);
        this.button.textContent = config.label;
        this.button.title = config.tooltip;
        this.button.disabled = !config.enabled;
        this.button.onclick = config.onClick;

        this.applyStyles();
    }

    private applyStyles(): void {
        this.button.style.cssText = `
            padding: 8px 16px;
            margin-right: 8px;
            border: 1px solid #ddd;
            background: #fff;
            color: #333;
            cursor: pointer;
            border-radius: 4px;
            font-size: 14px;
            font-weight: 500;
            transition: all 0.2s ease;
        `;
    }

    public setEnabled(enabled: boolean): void {
        this.button.disabled = !enabled;
        if (!enabled) {
            this.button.style.opacity = "0.5";
            this.button.style.cursor = "not-allowed";
        } else {
            this.button.style.opacity = "1";
            this.button.style.cursor = "pointer";
        }
    }

    public getElement(): HTMLButtonElement {
        return this.button;
    }
}
```

2. **Create Command Bar Component**

File: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/CommandBar.ts`

```typescript
import { CommandButton, CommandButtonConfig } from "./CommandButton";
import { GridConfiguration } from "../types/Config";

export class CommandBar {
    private container: HTMLDivElement;
    private buttons: Map<string, CommandButton> = new Map();

    constructor(
        private config: GridConfiguration,
        private getSelectedRecords: () => any[]
    ) {
        this.container = document.createElement("div");
        this.container.className = "command-bar";
        this.applyStyles();
        this.renderButtons();
    }

    private applyStyles(): void {
        this.container.style.cssText = `
            display: flex;
            padding: 12px;
            background: #f5f5f5;
            border-bottom: 1px solid #ddd;
            gap: 8px;
        `;
    }

    private renderButtons(): void {
        if (!this.config.customCommands.enabled) return;

        const commandConfigs: CommandButtonConfig[] = [
            {
                id: "addFile",
                label: "+ Add File",
                tooltip: "Upload a file to the selected document",
                enabled: false,
                onClick: () => this.executeCommand("addFile")
            },
            {
                id: "removeFile",
                label: "- Remove File",
                tooltip: "Delete the file from the selected document",
                enabled: false,
                onClick: () => this.executeCommand("removeFile")
            },
            {
                id: "updateFile",
                label: "^ Update File",
                tooltip: "Replace the file in the selected document",
                enabled: false,
                onClick: () => this.executeCommand("updateFile")
            },
            {
                id: "downloadFile",
                label: "↓ Download",
                tooltip: "Download the selected file(s)",
                enabled: false,
                onClick: () => this.executeCommand("downloadFile")
            }
        ];

        for (const config of commandConfigs) {
            if (this.config.customCommands.commands.includes(config.id)) {
                const button = new CommandButton(config);
                this.buttons.set(config.id, button);
                this.container.appendChild(button.getElement());
            }
        }
    }

    public updateButtonStates(selectedRecords: any[]): void {
        const selectedCount = selectedRecords.length;

        // Add File: enabled if exactly 1 selected and no file
        const addButton = this.buttons.get("addFile");
        if (addButton) {
            const enabled = selectedCount === 1 && !this.hasFile(selectedRecords[0]);
            addButton.setEnabled(enabled);
        }

        // Remove File: enabled if exactly 1 selected and has file
        const removeButton = this.buttons.get("removeFile");
        if (removeButton) {
            const enabled = selectedCount === 1 && this.hasFile(selectedRecords[0]);
            removeButton.setEnabled(enabled);
        }

        // Update File: enabled if exactly 1 selected and has file
        const updateButton = this.buttons.get("updateFile");
        if (updateButton) {
            const enabled = selectedCount === 1 && this.hasFile(selectedRecords[0]);
            updateButton.setEnabled(enabled);
        }

        // Download File: enabled if 1+ selected and all have files
        const downloadButton = this.buttons.get("downloadFile");
        if (downloadButton) {
            const enabled = selectedCount > 0 && selectedRecords.every(r => this.hasFile(r));
            downloadButton.setEnabled(enabled);
        }
    }

    private hasFile(record: any): boolean {
        const hasFileField = this.config.fieldMappings.hasFile;
        return record.getValue(hasFileField) === true;
    }

    private executeCommand(commandId: string): void {
        console.log(`Executing command: ${commandId}`);
        const selectedRecords = this.getSelectedRecords();

        // Call JavaScript web resource function
        const win = window as any;
        if (win.Spaarke?.DocumentGrid?.[commandId]) {
            win.Spaarke.DocumentGrid[commandId](selectedRecords, this.config);
        } else {
            console.error(`Command handler not found: Spaarke.DocumentGrid.${commandId}`);
            alert(`Command handler not implemented: ${commandId}`);
        }
    }

    public getElement(): HTMLDivElement {
        return this.container;
    }
}
```

3. **Integrate Command Bar into Control**

Update `index.ts`:

```typescript
import { CommandBar } from "./components/CommandBar";

export class UniversalDatasetGrid implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement;
    private context: ComponentFramework.Context<IInputs>;
    private config: GridConfiguration | null = null;
    private commandBar: CommandBar | null = null;
    private gridContainer: HTMLDivElement;
    private selectedRecordIds: string[] = [];

    public init(/* ... */): void {
        // ... existing code ...

        // Create grid container
        this.gridContainer = document.createElement("div");
        this.gridContainer.className = "grid-container";

        // Create command bar
        if (this.config && this.config.ui.showCommandBar) {
            this.commandBar = new CommandBar(
                this.config,
                () => this.getSelectedRecordData()
            );
            this.container.appendChild(this.commandBar.getElement());
        }

        this.container.appendChild(this.gridContainer);
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;

        // Render grid (existing code)
        this.renderMinimalGrid();

        // Update command bar button states
        if (this.commandBar) {
            const selectedRecords = this.getSelectedRecordData();
            this.commandBar.updateButtonStates(selectedRecords);
        }
    }

    private getSelectedRecordData(): any[] {
        const dataset = this.context.parameters.dataset;
        return this.selectedRecordIds.map(id => dataset.records[id]);
    }

    private renderMinimalGrid(): void {
        // Clear grid container
        this.gridContainer.innerHTML = "";

        const dataset = this.context.parameters.dataset;
        const table = document.createElement("table");
        table.className = "dataset-grid";
        table.style.cssText = `
            width: 100%;
            border-collapse: collapse;
            background: white;
        `;

        // Header
        const thead = document.createElement("thead");
        const headerRow = document.createElement("tr");
        headerRow.style.cssText = "background: #f5f5f5; border-bottom: 2px solid #ddd;";

        // Add checkbox column
        const checkboxHeader = document.createElement("th");
        checkboxHeader.style.cssText = "padding: 8px; width: 30px;";
        const selectAllCheckbox = document.createElement("input");
        selectAllCheckbox.type = "checkbox";
        selectAllCheckbox.onchange = () => this.toggleSelectAll(selectAllCheckbox.checked);
        checkboxHeader.appendChild(selectAllCheckbox);
        headerRow.appendChild(checkboxHeader);

        for (const column of dataset.columns) {
            const th = document.createElement("th");
            th.textContent = column.displayName;
            th.style.cssText = "padding: 8px; text-align: left; font-weight: 600;";
            headerRow.appendChild(th);
        }

        thead.appendChild(headerRow);
        table.appendChild(thead);

        // Body
        const tbody = document.createElement("tbody");

        for (const recordId of dataset.sortedRecordIds) {
            const record = dataset.records[recordId];
            const row = document.createElement("tr");
            row.style.cssText = "border-bottom: 1px solid #eee;";

            // Checkbox cell
            const checkboxCell = document.createElement("td");
            checkboxCell.style.cssText = "padding: 8px;";
            const checkbox = document.createElement("input");
            checkbox.type = "checkbox";
            checkbox.checked = this.selectedRecordIds.includes(recordId);
            checkbox.onchange = () => this.toggleRowSelection(recordId, checkbox.checked);
            checkboxCell.appendChild(checkbox);
            row.appendChild(checkboxCell);

            // Data cells
            for (const column of dataset.columns) {
                const cell = document.createElement("td");
                cell.textContent = record.getFormattedValue(column.name) || "";
                cell.style.cssText = "padding: 8px;";
                row.appendChild(cell);
            }

            // Row click handler
            row.onclick = (e) => {
                if ((e.target as HTMLElement).tagName !== "INPUT") {
                    dataset.openDatasetItem(record.getNamedReference());
                }
            };

            tbody.appendChild(row);
        }

        table.appendChild(tbody);
        this.gridContainer.appendChild(table);
    }

    private toggleRowSelection(recordId: string, selected: boolean): void {
        if (selected) {
            if (!this.selectedRecordIds.includes(recordId)) {
                this.selectedRecordIds.push(recordId);
            }
        } else {
            this.selectedRecordIds = this.selectedRecordIds.filter(id => id !== recordId);
        }

        // Update command bar
        if (this.commandBar) {
            this.commandBar.updateButtonStates(this.getSelectedRecordData());
        }
    }

    private toggleSelectAll(selected: boolean): void {
        const dataset = this.context.parameters.dataset;
        if (selected) {
            this.selectedRecordIds = [...dataset.sortedRecordIds];
        } else {
            this.selectedRecordIds = [];
        }

        // Re-render grid to update checkboxes
        this.renderMinimalGrid();

        // Update command bar
        if (this.commandBar) {
            this.commandBar.updateButtonStates(this.getSelectedRecordData());
        }
    }
}
```

**Testing:**
- [ ] Command bar renders correctly
- [ ] Buttons enable/disable based on selection
- [ ] Add File enabled only when 1 record selected without file
- [ ] Remove/Update enabled only when 1 record selected with file
- [ ] Download enabled when 1+ records selected with files
- [ ] Tooltips display on hover

---

### Task 2.3: Implement Command Execution Framework (3 hours)

**Objective:** Create framework for executing commands and communicating with JavaScript web resource.

**Acceptance Criteria:**
- [ ] Commands call JavaScript web resource functions
- [ ] Context passed to JavaScript functions
- [ ] Error handling for missing JavaScript functions
- [ ] Logging for command execution

**Implementation Steps:**

1. **Create Command Executor Interface**

File: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/CommandExecutor.ts`

```typescript
import { GridConfiguration } from "../types/Config";

export interface CommandContext {
    selectedRecordIds: string[];
    selectedRecords: any[];
    config: GridConfiguration;
    refreshGrid: () => void;
}

export class CommandExecutor {
    constructor(
        private config: GridConfiguration,
        private getContext: () => CommandContext
    ) {
        this.ensureGlobalNamespace();
    }

    private ensureGlobalNamespace(): void {
        const win = window as any;
        win.Spaarke = win.Spaarke || {};
        win.Spaarke.DocumentGrid = win.Spaarke.DocumentGrid || {};

        // Provide fallback implementations
        win.Spaarke.DocumentGrid.addFile = win.Spaarke.DocumentGrid.addFile || this.notImplemented("addFile");
        win.Spaarke.DocumentGrid.removeFile = win.Spaarke.DocumentGrid.removeFile || this.notImplemented("removeFile");
        win.Spaarke.DocumentGrid.updateFile = win.Spaarke.DocumentGrid.updateFile || this.notImplemented("updateFile");
        win.Spaarke.DocumentGrid.downloadFile = win.Spaarke.DocumentGrid.downloadFile || this.notImplemented("downloadFile");
    }

    private notImplemented(commandId: string): () => void {
        return () => {
            console.error(`Command not implemented: ${commandId}`);
            console.error("Please ensure sprk_DocumentGridIntegration.js web resource is loaded");
            alert(`The ${commandId} command is not yet implemented.\n\nPlease ensure the JavaScript web resource is properly configured.`);
        };
    }

    public executeCommand(commandId: string): void {
        try {
            const context = this.getContext();

            console.log(`Executing command: ${commandId}`, {
                selectedRecordIds: context.selectedRecordIds,
                recordCount: context.selectedRecords.length
            });

            const win = window as any;
            const handler = win.Spaarke?.DocumentGrid?.[commandId];

            if (typeof handler === "function") {
                handler(context);
            } else {
                console.error(`Command handler not found: ${commandId}`);
                this.notImplemented(commandId)();
            }
        } catch (error) {
            console.error(`Command execution failed: ${commandId}`, error);
            alert(`Failed to execute command: ${commandId}\n\n${error}`);
        }
    }
}
```

2. **Update Command Bar to Use Executor**

Update `CommandBar.ts`:

```typescript
import { CommandExecutor } from "../services/CommandExecutor";

export class CommandBar {
    private container: HTMLDivElement;
    private buttons: Map<string, CommandButton> = new Map();
    private commandExecutor: CommandExecutor;

    constructor(
        private config: GridConfiguration,
        private getCommandContext: () => CommandContext
    ) {
        this.container = document.createElement("div");
        this.container.className = "command-bar";
        this.commandExecutor = new CommandExecutor(config, getCommandContext);
        this.applyStyles();
        this.renderButtons();
    }

    private executeCommand(commandId: string): void {
        this.commandExecutor.executeCommand(commandId);
    }
}
```

3. **Update Control to Provide Command Context**

Update `index.ts`:

```typescript
public init(/* ... */): void {
    // ... existing code ...

    if (this.config && this.config.ui.showCommandBar) {
        this.commandBar = new CommandBar(
            this.config,
            () => this.getCommandContext()
        );
        this.container.appendChild(this.commandBar.getElement());
    }
}

private getCommandContext(): CommandContext {
    return {
        selectedRecordIds: this.selectedRecordIds,
        selectedRecords: this.getSelectedRecordData(),
        config: this.config!,
        refreshGrid: () => this.refreshGrid()
    };
}

private refreshGrid(): void {
    this.context.parameters.dataset.refresh();
    this.renderMinimalGrid();
}
```

**Testing:**
- [ ] Commands execute when buttons clicked
- [ ] Fallback message shown if JavaScript not loaded
- [ ] Command context passed correctly
- [ ] Grid refreshes after command execution
- [ ] Errors logged to console

---

### Task 2.4: Update Control Manifest (2 hours)

**Objective:** Update ControlManifest.Input.xml with new configuration properties.

**Acceptance Criteria:**
- [ ] configJson property defined
- [ ] Property description added
- [ ] Property marked as required/optional appropriately
- [ ] Build succeeds with updated manifest

**Implementation Steps:**

1. **Update ControlManifest.Input.xml**

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke.UI.Components" constructor="UniversalDatasetGrid" version="1.1.0" display-name-key="Universal Dataset Grid" description-key="Configurable dataset grid with custom command support" control-type="standard">

    <!-- Dataset parameter -->
    <data-set name="dataset" display-name-key="Dataset" description-key="Dataset to display in the grid">
      <property-set name="columns" display-name-key="Columns" description-key="Columns to display" usage="bound" required="true" />
    </data-set>

    <!-- Configuration parameter -->
    <property name="configJson" display-name-key="Configuration JSON" description-key="JSON configuration for grid behavior and commands" of-type="Multiple" usage="input" required="false" />

    <!-- Resources -->
    <resources>
      <code path="index.ts" order="1" />
      <css path="styles.css" order="1" />
    </resources>

    <!-- Feature usage -->
    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
    </feature-usage>
  </control>
</manifest>
```

2. **Add Styles File**

File: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/styles.css`

```css
/* Command Bar Styles */
.command-bar {
    display: flex;
    padding: 12px;
    background: #f5f5f5;
    border-bottom: 1px solid #ddd;
    gap: 8px;
    flex-wrap: wrap;
}

.command-button {
    padding: 8px 16px;
    border: 1px solid #ddd;
    background: #fff;
    color: #333;
    cursor: pointer;
    border-radius: 4px;
    font-size: 14px;
    font-weight: 500;
    transition: all 0.2s ease;
}

.command-button:hover:not(:disabled) {
    background: #0078d4;
    color: #fff;
    border-color: #0078d4;
}

.command-button:active:not(:disabled) {
    transform: scale(0.98);
}

.command-button:disabled {
    opacity: 0.5;
    cursor: not-allowed;
}

/* Grid Styles */
.grid-container {
    overflow: auto;
    max-height: calc(100vh - 200px);
}

.dataset-grid {
    width: 100%;
    border-collapse: collapse;
    background: white;
}

.dataset-grid thead {
    position: sticky;
    top: 0;
    background: #f5f5f5;
    z-index: 10;
}

.dataset-grid th {
    padding: 8px;
    text-align: left;
    font-weight: 600;
    border-bottom: 2px solid #ddd;
}

.dataset-grid td {
    padding: 8px;
    border-bottom: 1px solid #eee;
}

.dataset-grid tbody tr:hover {
    background: #f9f9f9;
    cursor: pointer;
}

.config-error {
    padding: 20px;
    background: #fff3cd;
    border: 1px solid #ffc107;
    color: #856404;
    border-radius: 4px;
    margin: 20px;
}

.config-error h3 {
    margin-top: 0;
}
```

**Testing:**
- [ ] Build succeeds with updated manifest
- [ ] configJson parameter visible in Power Apps
- [ ] Styles apply correctly
- [ ] Control loads without errors

---

### Task 2.5: Build and Test Enhanced Control (2 hours)

**Objective:** Build enhanced PCF control and test locally.

**Acceptance Criteria:**
- [ ] Build completes without errors
- [ ] Control renders in test harness
- [ ] Configuration parsing works
- [ ] Command bar renders
- [ ] Buttons respond to clicks
- [ ] Bundle size under 50 KB

**Implementation Steps:**

1. **Build Control**

```bash
cd src/controls/UniversalDatasetGrid/UniversalDatasetGrid
npm run build
```

2. **Check Bundle Size**

```bash
# Check output size
ls -lh out/controls/ | grep bundle.js

# Target: < 50 KB (we're using vanilla JS/TS)
# Current minimal version: ~10 KB
# Enhanced version target: < 50 KB
```

3. **Run Test Harness**

```bash
npm start watch
```

4. **Test Configuration**

Create test configuration file: `test-config.json`

```json
{
  "entityName": "sprk_document",
  "apiConfig": {
    "baseUrl": "https://spe-bff-api-dev.azurewebsites.net",
    "version": "v1",
    "timeout": 300000
  },
  "fileConfig": {
    "maxFileSize": 4194304,
    "allowedExtensions": [".pdf", ".docx", ".xlsx"],
    "uploadChunkSize": 327680
  },
  "customCommands": {
    "enabled": true,
    "commands": ["addFile", "removeFile", "updateFile", "downloadFile"]
  },
  "fieldMappings": {
    "documentId": "sprk_documentid",
    "hasFile": "sprk_hasfile",
    "fileName": "sprk_filename",
    "fileSize": "sprk_filesize",
    "mimeType": "sprk_mimetype",
    "graphItemId": "sprk_graphitemid",
    "graphDriveId": "sprk_graphdriveid",
    "containerId": "sprk_containerid",
    "sharepointUrl": "sprk_sharepointurl"
  },
  "ui": {
    "showCommandBar": true,
    "showSharePointLink": true,
    "linkColumn": "sprk_sharepointurl",
    "linkLabel": "Open in SharePoint"
  },
  "permissions": {
    "checkPermissions": true,
    "requiredRoles": []
  }
}
```

5. **Manual Testing Checklist**

- [ ] Control loads without errors
- [ ] Configuration error shows if invalid JSON
- [ ] Default configuration used if no JSON provided
- [ ] Command bar renders with 4 buttons
- [ ] Buttons start disabled
- [ ] Selecting 1 row enables Add File (if no file)
- [ ] Selecting 1 row with file enables Remove/Update/Download
- [ ] Selecting multiple rows enables only Download (if all have files)
- [ ] Clicking button shows "not implemented" message
- [ ] Grid renders data correctly
- [ ] Row selection works
- [ ] Select all checkbox works

**Testing:**
- [ ] All manual tests pass
- [ ] Console shows no errors
- [ ] Bundle size acceptable
- [ ] Performance acceptable (< 1s render time)

---

### Task 2.6: Deploy Enhanced Control to Dataverse (2 hours)

**Objective:** Deploy enhanced PCF control to development environment.

**Acceptance Criteria:**
- [ ] Control deployed successfully
- [ ] Control appears in component library
- [ ] Configuration property visible
- [ ] Control works on Document entity grid
- [ ] No console errors in browser

**Implementation Steps:**

1. **Build for Production**

```bash
cd src/controls/UniversalDatasetGrid/UniversalDatasetGrid
npm run build
```

2. **Deploy to Dataverse**

```bash
# Authenticate to SPAARKE DEV 1
pac auth create --name "SPAARKE DEV 1" --url https://spaarkedev1.crm.dynamics.com

# Deploy control
pac pcf push --publisher-prefix sprk
```

3. **Configure on Document Entity**

- Open Power Apps (https://make.powerapps.com)
- Navigate to SPAARKE DEV 1 environment
- Open Document entity
- Edit "Active Documents" view
- Add/replace grid control with "sprk_Spaarke.UI.Components.UniversalDatasetGrid"
- Set configJson property with test configuration

4. **Test in Dataverse**

- Open Document entity
- Verify grid renders
- Verify command bar appears
- Select records and verify button states
- Click buttons and verify "not implemented" messages appear
- Check browser console for errors

**Testing:**
- [ ] Deployment succeeds
- [ ] Control visible in component library
- [ ] Grid renders on entity
- [ ] Command bar functional
- [ ] Configuration applied correctly
- [ ] No errors in production

---

## Phase 2 Deliverables

### Code Deliverables

- [ ] `types/Config.ts` - Configuration interfaces
- [ ] `services/ConfigParser.ts` - Configuration parsing logic
- [ ] `services/CommandExecutor.ts` - Command execution framework
- [ ] `components/CommandButton.ts` - Button component
- [ ] `components/CommandBar.ts` - Command bar component
- [ ] `index.ts` - Updated control implementation
- [ ] `styles.css` - Grid and command bar styles
- [ ] `ControlManifest.Input.xml` - Updated manifest

### Documentation Deliverables

- [ ] Configuration Schema Reference
- [ ] Developer Guide - Adding Custom Commands
- [ ] Deployment Guide - Enhanced Control

### Testing Deliverables

- [ ] Test Configuration JSON
- [ ] Manual Test Checklist
- [ ] Test Results Report

---

## Acceptance Criteria

**Phase 2 is complete when:**

1. ✅ **Configuration Support**
   - configJson parameter parsed successfully
   - Configuration validation working
   - Default configuration used when needed

2. ✅ **Command Bar**
   - Command bar renders in grid header
   - 4 command buttons displayed
   - Buttons enable/disable based on selection
   - Tooltips display correctly

3. ✅ **Command Execution**
   - Commands call JavaScript web resource functions
   - Context passed to JavaScript correctly
   - Error handling for missing functions
   - Grid refresh after command execution

4. ✅ **Build and Deployment**
   - Build completes without errors
   - Bundle size under 50 KB
   - Control deploys to Dataverse successfully
   - Control works on Document entity

5. ✅ **Testing**
   - All manual tests pass
   - No console errors
   - Performance acceptable
   - User experience smooth

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Bundle size too large | Use vanilla JS/TS, avoid heavy libraries |
| Configuration too complex | Provide sensible defaults, validate early |
| Button state logic bugs | Thorough testing with various selection scenarios |
| Performance degradation | Profile rendering, optimize DOM operations |
| Deployment failures | Test in isolated environment first |

---

## Next Steps

After Phase 2 completion:

1. **Phase 3: JavaScript Integration** (20 hours)
   - Create `sprk_DocumentGridIntegration.js` web resource
   - Implement 4 file operation functions
   - Add authentication and API calls
   - Add progress indicators
   - Add error handling

2. **Phase 4: Field Updates & Links** (8 hours)
   - Auto-populate file metadata fields
   - Create clickable SharePoint links
   - Handle field updates after operations

3. **Phase 5: Testing & Refinement** (16 hours)
   - End-to-end testing
   - Error scenario testing
   - Performance testing
   - User acceptance testing

4. **Phase 6: Deployment & Documentation** (8 hours)
   - Production deployment
   - User documentation
   - Admin guides
   - Training materials

---

**Phase 2 Total Effort:** 16 hours (2 days)

**Ready to Start:** ✅ All Phase 1 dependencies complete
