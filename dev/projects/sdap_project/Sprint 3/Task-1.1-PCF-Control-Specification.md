# PCF Dataset Control Specification
## Permission-Based Document Management UI

**Related Task**: Task 1.1 - Granular AccessRights Authorization
**Component Type**: PCF Dataset Control
**Target Entity**: `sprk_document`
**Framework**: Power Apps Component Framework (PCF)
**Estimated Effort**: 5-7 days

---

## Overview

PCF Dataset Control for the `sprk_document` entity that provides permission-based UI for file management. The control dynamically shows/hides command bar buttons based on the current user's Dataverse permissions retrieved from the Spe.Bff.Api.

**Key Innovation**: UI adapts to user permissions in real-time, ensuring users only see actions they can actually perform.

---

## Business Requirements

### User Experience Goals

1. **Conditional Command Bar**: Show only buttons the user can actually use
   - Read-only user sees: [👁️ Preview]
   - Editor sees: [👁️ Preview] [⬇️ Download] [⬆️ Upload]
   - Full access user sees: [👁️ Preview] [⬇️ Download] [⬆️ Upload] [🗑️ Delete] [🔗 Share]

2. **Performance**: Batch permission queries for gallery views (don't query per record)

3. **Responsive**: Real-time updates when user selects different records

4. **Clear Feedback**: Show why action is disabled (tooltip: "You need Write access to download")

### Security Requirements

1. **Client-side enforcement is UX only** - Server still enforces (defense in depth)
2. **No security bypasses** - Hiding button doesn't change actual permissions
3. **Audit trail** - All permission checks logged server-side

---

## Architecture

###  Component Flow

```
User selects document(s) in gallery
              ↓
PCF Control detects selection change
              ↓
GET /api/documents/{id}/permissions
              ↓
Spe.Bff.Api checks Dataverse permissions
              ↓
Returns DocumentCapabilities { canPreview, canDownload, canDelete, ... }
              ↓
PCF Control updates command bar button visibility
              ↓
User clicks visible button
              ↓
API enforces permission (server-side validation)
              ↓
Action succeeds or fails with 403
```

### Batch Performance Optimization

```
User views gallery with 50 documents
              ↓
PCF Control calls POST /api/documents/permissions/batch
              ↓
Pass array of 50 document IDs
              ↓
API returns capabilities for all 50 in one request
              ↓
PCF Control caches capabilities per document
              ↓
User selects document → instant button update (no API call)
```

---

## API Contract

### GET /api/documents/{documentId}/permissions

**Request**:
```http
GET /api/documents/12345678-1234-1234-1234-123456789012/permissions HTTP/1.1
Host: spaarke-api.azurewebsites.net
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...
```

**Response** (200 OK):
```json
{
  "documentId": "12345678-1234-1234-1234-123456789012",
  "userId": "user-guid-from-token",
  "canPreview": true,
  "canDownload": true,
  "canUpload": true,
  "canReplace": true,
  "canDelete": false,
  "canReadMetadata": true,
  "canUpdateMetadata": true,
  "canShare": false,
  "accessRights": "Read, Write"
}
```

### POST /api/documents/permissions/batch

**Request**:
```http
POST /api/documents/permissions/batch HTTP/1.1
Host: spaarke-api.azurewebsites.net
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...
Content-Type: application/json

{
  "documentIds": [
    "doc-id-1",
    "doc-id-2",
    "doc-id-3"
  ]
}
```

**Response** (200 OK):
```json
{
  "permissions": [
    {
      "documentId": "doc-id-1",
      "userId": "user-guid",
      "canPreview": true,
      "canDownload": true,
      "canDelete": false,
      ...
    },
    {
      "documentId": "doc-id-2",
      "userId": "user-guid",
      "canPreview": true,
      "canDownload": false,
      "canDelete": false,
      ...
    }
  ]
}
```

---

## PCF Control Implementation

### Control Manifest (ControlManifest.Input.xml)

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke" constructor="DocumentDatasetControl" version="1.0.0" display-name-key="DocumentDatasetControl_Display_Key" description-key="DocumentDatasetControl_Desc_Key" control-type="standard">

    <!-- Dataset binding to sprk_document entity -->
    <data-set name="dataset" display-name-key="Dataset_Display_Key">
      <property-set name="sprk_documentid" display-name-key="DocumentId_Key" of-type="SingleLine.Text" usage="bound" required="true" />
      <property-set name="sprk_name" display-name-key="Name_Key" of-type="SingleLine.Text" usage="bound" required="true" />
      <property-set name="sprk_description" display-name-key="Description_Key" of-type="Multiple" usage="bound" required="false" />
      <property-set name="createdon" display-name-key="CreatedOn_Key" of-type="DateAndTime.DateAndTime" usage="bound" required="false" />
      <property-set name="modifiedon" display-name-key="ModifiedOn_Key" of-type="DateAndTime.DateAndTime" usage="bound" required="false" />
    </data-set>

    <!-- Configuration properties -->
    <property name="apiBaseUrl" display-name-key="ApiBaseUrl_Display_Key" description-key="ApiBaseUrl_Desc_Key" of-type="SingleLine.Text" usage="input" required="true" default-value="https://spaarke-api.azurewebsites.net" />

    <property name="enableBatchPermissions" display-name-key="EnableBatch_Display_Key" description-key="EnableBatch_Desc_Key" of-type="TwoOptions" usage="input" required="false" default-value="true" />

    <!-- Resources -->
    <resources>
      <code path="index.ts" order="1"/>
      <css path="css/DocumentDatasetControl.css" order="1" />
      <resx path="strings/DocumentDatasetControl.1033.resx" version="1.0.0" />
    </resources>

    <!-- Features -->
    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
    </feature-usage>
  </control>
</manifest>
```

### TypeScript Implementation (index.ts)

```typescript
import { IInputs, IOutputs } from "./generated/ManifestTypes";

interface DocumentCapabilities {
    documentId: string;
    userId: string;
    canPreview: boolean;
    canDownload: boolean;
    canUpload: boolean;
    canReplace: boolean;
    canDelete: boolean;
    canReadMetadata: boolean;
    canUpdateMetadata: boolean;
    canShare: boolean;
    accessRights: string;
}

interface BatchPermissionsResponse {
    permissions: DocumentCapabilities[];
}

export class DocumentDatasetControl implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private _context: ComponentFramework.Context<IInputs>;
    private _container: HTMLDivElement;
    private _apiBaseUrl: string;
    private _capabilitiesCache: Map<string, DocumentCapabilities>;
    private _selectedRecordId: string | null;

    // Command bar buttons
    private _previewButton: HTMLButtonElement;
    private _downloadButton: HTMLButtonElement;
    private _uploadButton: HTMLButtonElement;
    private _deleteButton: HTMLButtonElement;
    private _shareButton: HTMLButtonElement;

    constructor() {
        this._capabilitiesCache = new Map();
        this._selectedRecordId = null;
    }

    /**
     * Called when control is initialized
     */
    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        this._context = context;
        this._container = container;
        this._apiBaseUrl = context.parameters.apiBaseUrl.raw || "https://spaarke-api.azurewebsites.net";

        // Create command bar
        this.createCommandBar();

        // Create dataset grid
        this.createDatasetGrid();

        // Initial permissions load (if records already loaded)
        if (context.parameters.dataset.sortedRecordIds.length > 0) {
            this.loadBatchPermissions();
        }
    }

    /**
     * Called when control needs to update view (selection changed, data changed, etc.)
     */
    public async updateView(context: ComponentFramework.Context<IInputs>): Promise<void> {
        this._context = context;

        // Check if selection changed
        const selectedRecordIds = context.parameters.dataset.getSelectedRecordIds();

        if (selectedRecordIds.length === 1) {
            const documentId = selectedRecordIds[0];

            if (documentId !== this._selectedRecordId) {
                this._selectedRecordId = documentId;
                await this.updateCommandBarForDocument(documentId);
            }
        } else {
            // Multiple or no selection - disable all buttons
            this._selectedRecordId = null;
            this.disableAllButtons();
        }

        // Refresh grid if dataset changed
        this.refreshDatasetGrid();
    }

    /**
     * Creates the command bar with all action buttons
     */
    private createCommandBar(): void {
        const commandBar = document.createElement("div");
        commandBar.className = "command-bar";

        // Preview button
        this._previewButton = this.createButton("preview", "👁️ Preview", "Preview document", () => this.handlePreview());

        // Download button
        this._downloadButton = this.createButton("download", "⬇️ Download", "Download document", () => this.handleDownload());

        // Upload button
        this._uploadButton = this.createButton("upload", "⬆️ Upload", "Upload new version", () => this.handleUpload());

        // Delete button
        this._deleteButton = this.createButton("delete", "🗑️ Delete", "Delete document", () => this.handleDelete());

        // Share button
        this._shareButton = this.createButton("share", "🔗 Share", "Share document", () => this.handleShare());

        commandBar.appendChild(this._previewButton);
        commandBar.appendChild(this._downloadButton);
        commandBar.appendChild(this._uploadButton);
        commandBar.appendChild(this._deleteButton);
        commandBar.appendChild(this._shareButton);

        this._container.appendChild(commandBar);

        // Initially disable all buttons (no selection)
        this.disableAllButtons();
    }

    /**
     * Creates a command bar button
     */
    private createButton(id: string, text: string, tooltip: string, onClick: () => void): HTMLButtonElement {
        const button = document.createElement("button");
        button.id = `btn-${id}`;
        button.className = "command-button";
        button.textContent = text;
        button.title = tooltip;
        button.onclick = onClick;
        button.disabled = true;
        return button;
    }

    /**
     * Creates the dataset grid
     */
    private createDatasetGrid(): void {
        const gridContainer = document.createElement("div");
        gridContainer.className = "dataset-grid";
        gridContainer.id = "dataset-grid";

        // Grid will be populated by refreshDatasetGrid()
        this._container.appendChild(gridContainer);
    }

    /**
     * Refreshes the dataset grid with current records
     */
    private refreshDatasetGrid(): void {
        const gridContainer = document.getElementById("dataset-grid");
        if (!gridContainer) return;

        // Clear existing content
        gridContainer.innerHTML = "";

        // Create table
        const table = document.createElement("table");
        table.className = "dataset-table";

        // Create header
        const thead = document.createElement("thead");
        const headerRow = document.createElement("tr");
        headerRow.innerHTML = `
            <th>Name</th>
            <th>Description</th>
            <th>Created On</th>
            <th>Modified On</th>
            <th>Access</th>
        `;
        thead.appendChild(headerRow);
        table.appendChild(thead);

        // Create body
        const tbody = document.createElement("tbody");

        const recordIds = this._context.parameters.dataset.sortedRecordIds;
        recordIds.forEach(recordId => {
            const record = this._context.parameters.dataset.records[recordId];
            const row = document.createElement("tr");
            row.onclick = () => this.handleRecordSelection(recordId);

            // Check if this record is selected
            if (this._context.parameters.dataset.getSelectedRecordIds().includes(recordId)) {
                row.className = "selected";
            }

            // Get capabilities from cache (if available)
            const capabilities = this._capabilitiesCache.get(recordId);
            const accessText = capabilities ? this.formatAccessRights(capabilities) : "Loading...";

            row.innerHTML = `
                <td>${record.getValue("sprk_name") || ""}</td>
                <td>${record.getValue("sprk_description") || ""}</td>
                <td>${this.formatDate(record.getValue("createdon"))}</td>
                <td>${this.formatDate(record.getValue("modifiedon"))}</td>
                <td class="access-rights">${accessText}</td>
            `;

            tbody.appendChild(row);
        });

        table.appendChild(tbody);
        gridContainer.appendChild(table);
    }

    /**
     * Handles record selection
     */
    private handleRecordSelection(recordId: string): void {
        // Clear previous selection
        this._context.parameters.dataset.clearSelectedRecordIds();

        // Select new record
        this._context.parameters.dataset.setSelectedRecordIds([recordId]);

        // This will trigger updateView()
    }

    /**
     * Updates command bar buttons based on document capabilities
     */
    private async updateCommandBarForDocument(documentId: string): Promise<void> {
        try {
            // Check cache first
            let capabilities = this._capabilitiesCache.get(documentId);

            // If not in cache, fetch from API
            if (!capabilities) {
                capabilities = await this.getDocumentPermissions(documentId);
                this._capabilitiesCache.set(documentId, capabilities);
            }

            // Update button visibility and enabled state
            this.updateButton(this._previewButton, capabilities.canPreview, "Preview document");
            this.updateButton(this._downloadButton, capabilities.canDownload, "Download document (requires Write access)");
            this.updateButton(this._uploadButton, capabilities.canUpload, "Upload new version (requires Write + Create access)");
            this.updateButton(this._deleteButton, capabilities.canDelete, "Delete document (requires Delete access)");
            this.updateButton(this._shareButton, capabilities.canShare, "Share document (requires Share access)");

        } catch (error) {
            console.error("Failed to get document permissions:", error);
            this.disableAllButtons();
            this._context.navigation.openAlertDialog({
                text: "Failed to load permissions. Please refresh and try again.",
                title: "Error"
            });
        }
    }

    /**
     * Updates a button's enabled state and tooltip
     */
    private updateButton(button: HTMLButtonElement, canPerform: boolean, tooltipIfEnabled: string): void {
        button.disabled = !canPerform;
        button.title = canPerform ? tooltipIfEnabled : `${tooltipIfEnabled} - You don't have permission`;
        button.className = canPerform ? "command-button enabled" : "command-button disabled";
    }

    /**
     * Disables all command bar buttons
     */
    private disableAllButtons(): void {
        this._previewButton.disabled = true;
        this._downloadButton.disabled = true;
        this._uploadButton.disabled = true;
        this._deleteButton.disabled = true;
        this._shareButton.disabled = true;
    }

    /**
     * Gets permissions for a single document
     */
    private async getDocumentPermissions(documentId: string): Promise<DocumentCapabilities> {
        const token = await this.getAuthToken();

        const response = await fetch(`${this._apiBaseUrl}/api/documents/${documentId}/permissions`, {
            method: "GET",
            headers: {
                "Authorization": `Bearer ${token}`,
                "Content-Type": "application/json"
            }
        });

        if (!response.ok) {
            throw new Error(`Failed to get permissions: ${response.status} ${response.statusText}`);
        }

        return await response.json();
    }

    /**
     * Loads permissions for all visible documents in batch
     */
    private async loadBatchPermissions(): Promise<void> {
        if (!this._context.parameters.enableBatchPermissions.raw) {
            return; // Batch mode disabled
        }

        try {
            const recordIds = this._context.parameters.dataset.sortedRecordIds;

            // Only fetch if we have records and they're not already cached
            const uncachedIds = recordIds.filter(id => !this._capabilitiesCache.has(id));

            if (uncachedIds.length === 0) return;

            const token = await this.getAuthToken();

            const response = await fetch(`${this._apiBaseUrl}/api/documents/permissions/batch`, {
                method: "POST",
                headers: {
                    "Authorization": `Bearer ${token}`,
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    documentIds: uncachedIds
                })
            });

            if (!response.ok) {
                console.error(`Batch permissions failed: ${response.status}`);
                return;
            }

            const batchResponse: BatchPermissionsResponse = await response.json();

            // Cache all capabilities
            batchResponse.permissions.forEach(cap => {
                this._capabilitiesCache.set(cap.documentId, cap);
            });

            // Refresh grid to show access rights
            this.refreshDatasetGrid();

        } catch (error) {
            console.error("Failed to load batch permissions:", error);
        }
    }

    /**
     * Gets authentication token from context
     */
    private async getAuthToken(): Promise<string> {
        // Use PCF's built-in authentication
        const token = await this._context.webAPI.retrieveMultipleRecords("systemuser", "?$top=1")
            .then(() => {
                // If this succeeds, we have a valid session
                // In real implementation, get actual bearer token from context
                // For now, return placeholder
                return "token-from-context";
            });

        // TODO: Get actual bearer token from Power Apps context
        // May need to use: this._context.parameters.apiBaseUrl or custom authentication
        return token;
    }

    /**
     * Formats access rights for display
     */
    private formatAccessRights(capabilities: DocumentCapabilities): string {
        const rights: string[] = [];
        if (capabilities.canPreview) rights.push("Preview");
        if (capabilities.canDownload) rights.push("Download");
        if (capabilities.canDelete) rights.push("Delete");
        if (capabilities.canShare) rights.push("Share");

        return rights.length > 0 ? rights.join(", ") : "No access";
    }

    /**
     * Formats date for display
     */
    private formatDate(value: any): string {
        if (!value) return "";
        const date = new Date(value);
        return date.toLocaleDateString();
    }

    // ===== Command Handlers =====

    private handlePreview(): void {
        if (!this._selectedRecordId) return;

        const url = `${this._apiBaseUrl}/api/documents/${this._selectedRecordId}/preview`;
        window.open(url, "_blank");
    }

    private async handleDownload(): Promise<void> {
        if (!this._selectedRecordId) return;

        try {
            const token = await this.getAuthToken();
            const url = `${this._apiBaseUrl}/api/documents/${this._selectedRecordId}/download`;

            const response = await fetch(url, {
                headers: { "Authorization": `Bearer ${token}` }
            });

            if (!response.ok) {
                throw new Error(`Download failed: ${response.status}`);
            }

            // Trigger browser download
            const blob = await response.blob();
            const downloadUrl = window.URL.createObjectURL(blob);
            const a = document.createElement("a");
            a.href = downloadUrl;
            a.download = "document"; // Get filename from response headers
            a.click();

        } catch (error) {
            console.error("Download failed:", error);
            this._context.navigation.openAlertDialog({
                text: "Failed to download document. You may not have permission.",
                title: "Download Error"
            });
        }
    }

    private handleUpload(): void {
        // Open file picker and upload
        // Implementation depends on requirements
        this._context.navigation.openAlertDialog({
            text: "Upload functionality not yet implemented",
            title: "Upload"
        });
    }

    private async handleDelete(): Promise<void> {
        if (!this._selectedRecordId) return;

        // Confirm deletion
        this._context.navigation.openConfirmDialog({
            title: "Delete Document",
            text: "Are you sure you want to delete this document? This action cannot be undone."
        }).then(async (confirmed) => {
            if (!confirmed.confirmed) return;

            try {
                const token = await this.getAuthToken();
                const response = await fetch(`${this._apiBaseUrl}/api/documents/${this._selectedRecordId}`, {
                    method: "DELETE",
                    headers: { "Authorization": `Bearer ${token}` }
                });

                if (!response.ok) {
                    throw new Error(`Delete failed: ${response.status}`);
                }

                // Refresh dataset
                this._context.parameters.dataset.refresh();

                this._context.navigation.openAlertDialog({
                    text: "Document deleted successfully",
                    title: "Success"
                });

            } catch (error) {
                console.error("Delete failed:", error);
                this._context.navigation.openAlertDialog({
                    text: "Failed to delete document. You may not have permission.",
                    title: "Delete Error"
                });
            }
        });
    }

    private handleShare(): void {
        // Open sharing dialog
        // Implementation depends on requirements
        this._context.navigation.openAlertDialog({
            text: "Share functionality not yet implemented",
            title: "Share"
        });
    }

    /**
     * Called when control will be destroyed
     */
    public destroy(): void {
        // Cleanup
        this._capabilitiesCache.clear();
    }

    /**
     * Called when any value in the property bag has changed
     */
    public getOutputs(): IOutputs {
        return {};
    }
}
```

### CSS Styling (DocumentDatasetControl.css)

```css
/* Command Bar */
.command-bar {
    display: flex;
    gap: 10px;
    padding: 10px;
    background-color: #f3f2f1;
    border-bottom: 1px solid #d2d0ce;
}

.command-button {
    padding: 8px 16px;
    border: 1px solid #8a8886;
    background-color: #ffffff;
    cursor: pointer;
    font-size: 14px;
    border-radius: 2px;
    transition: all 0.2s;
}

.command-button.enabled {
    background-color: #0078d4;
    color: #ffffff;
    border-color: #0078d4;
}

.command-button.enabled:hover {
    background-color: #106ebe;
    border-color: #106ebe;
}

.command-button.disabled {
    opacity: 0.4;
    cursor: not-allowed;
}

/* Dataset Grid */
.dataset-grid {
    padding: 10px;
    overflow: auto;
}

.dataset-table {
    width: 100%;
    border-collapse: collapse;
    font-size: 14px;
}

.dataset-table th {
    background-color: #f3f2f1;
    padding: 10px;
    text-align: left;
    font-weight: 600;
    border-bottom: 2px solid #d2d0ce;
}

.dataset-table td {
    padding: 10px;
    border-bottom: 1px solid #edebe9;
}

.dataset-table tr:hover {
    background-color: #f3f2f1;
    cursor: pointer;
}

.dataset-table tr.selected {
    background-color: #deecf9;
}

.access-rights {
    font-size: 12px;
    color: #605e5c;
}
```

---

## Testing Plan

### Unit Tests
1. Test `formatAccessRights` with various capabilities
2. Test `formatDate` formatting
3. Test button update logic

### Integration Tests
1. Test permissions API calls (mock responses)
2. Test batch permissions loading
3. Test command bar updates on selection change
4. Test button click handlers

### Manual Testing
1. **Read-only user**:
   - Select document
   - Verify only Preview button enabled
   - Click Preview → Opens preview
   - Verify Download/Delete buttons disabled

2. **Editor user**:
   - Select document
   - Verify Preview, Download, Upload buttons enabled
   - Click Download → File downloads
   - Verify Delete button disabled

3. **Full access user**:
   - Select document
   - Verify all buttons enabled
   - Test each operation

4. **Performance**:
   - Load gallery with 50 documents
   - Verify batch permissions call (not 50 individual calls)
   - Verify fast button updates on selection change (cached)

---

## Deployment

### Prerequisites
1. Spe.Bff.Api deployed with permissions endpoints
2. Azure AD authentication configured
3. PCF solution imported to environment

### Build & Deploy
```bash
# Build PCF control
npm install
npm run build

# Create solution package
pac solution init --publisher-name Spaarke --publisher-prefix sprk
pac solution add-reference --path ./
pac solution pack

# Import to environment
pac solution import --path ./bin/Release/SpaarkeControls.zip
```

### Configuration
1. Add control to `sprk_document` main form
2. Set `apiBaseUrl` property to your API URL
3. Enable `enableBatchPermissions` for performance
4. Publish customizations

---

## Future Enhancements

1. **Inline editing**: Edit metadata directly in grid
2. **Drag-and-drop upload**: Upload files by dragging into control
3. **Bulk operations**: Select multiple documents for bulk delete/share
4. **Real-time updates**: Use SignalR for live permission changes
5. **Offline support**: Cache permissions for offline scenarios
6. **Accessibility**: Full keyboard navigation and screen reader support

---

## Related Documentation

- [Task 1.1 REVISED: AccessRights Authorization](Task-1.1-REVISED-AccessRights-Authorization.md)
- [PCF Control Framework Documentation](https://docs.microsoft.com/powerapps/developer/component-framework/overview)
- [Web API Reference](https://learn.microsoft.com/power-apps/developer/data-platform/webapi/overview)

---

**Document Version**: 1.0
**Last Updated**: 2025-10-01
**Maintained By**: Spaarke Development Team
