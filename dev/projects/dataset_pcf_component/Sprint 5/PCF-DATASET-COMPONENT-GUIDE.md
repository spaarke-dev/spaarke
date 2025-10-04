# PCF Dataset Component Guide for Spaarke

**Sprint 5 Design Reference**
**Date:** 2025-10-03
**Based On:** Microsoft DataSetGrid Sample ([PowerApps-Samples](https://github.com/microsoft/PowerApps-Samples/tree/master/component-framework/DataSetGrid))

---

## Overview

This guide provides a concise reference for building flexible, configurable PCF Dataset components that can be reused throughout the Spaarke solution. Dataset components bind to Dataverse tables and display multiple records with custom UI/UX.

### Key Use Cases for Spaarke
- **Document Library Views** - Display SPE documents with custom metadata
- **Workflow/Job Status Grids** - Show background job processing status
- **User Assignment Lists** - Manage user permissions and roles
- **Audit/Activity Logs** - Display system activity with filtering

---

## Architecture Pattern

### Component Lifecycle

```typescript
┌─────────────────────────────────────────────────────────┐
│ 1. init()                                               │
│    - Create DOM structure                               │
│    - Register event handlers                            │
│    - Enable container resize tracking                   │
└─────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────┐
│ 2. updateView() - Called on data changes                │
│    - Retrieve dataset columns and records               │
│    - Render/update UI based on current data             │
│    - Handle pagination state                            │
└─────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────┐
│ 3. getOutputs() - Return control outputs                │
│    - Selected records                                   │
│    - Filter state                                       │
│    - Any user interactions                              │
└─────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────┐
│ 4. destroy() - Cleanup on removal                       │
│    - Remove event listeners                             │
│    - Clear references                                   │
└─────────────────────────────────────────────────────────┘
```

---

## Manifest Configuration

### ControlManifest.Input.xml

**Minimal Dataset Binding:**

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control
    namespace="Spaarke"
    constructor="DocumentGrid"
    version="1.0.0"
    display-name-key="DocumentGrid_Display_Key"
    description-key="DocumentGrid_Desc_Key"
    control-type="standard">

    <!-- Dataset Binding -->
    <data-set
      name="documents"
      display-name-key="Documents_Display_Key">
    </data-set>

    <!-- Resources -->
    <resources>
      <code path="index.ts" order="1" />
      <css path="css/DocumentGrid.css" order="2" />
      <resx path="strings/DocumentGrid.1033.resx" version="1.0.0" />
    </resources>
  </control>
</manifest>
```

**Key Elements:**
- `<data-set>` - Binds to Dataverse table/view
- `name="documents"` - Property name accessed in code via `context.parameters.documents`
- `control-type="standard"` - Renders in model-driven apps (not canvas apps)

### Adding Configuration Properties

For **flexible, configurable** components, add input properties:

```xml
<property
  name="viewMode"
  display-name-key="ViewMode_Display_Key"
  description-key="ViewMode_Desc_Key"
  of-type="Enum"
  usage="input"
  required="false"
  default-value="grid">
  <value name="grid" display-name-key="ViewMode_Grid">Grid</value>
  <value name="tiles" display-name-key="ViewMode_Tiles">Tiles</value>
  <value name="list" display-name-key="ViewMode_List">List</value>
</property>

<property
  name="pageSize"
  display-name-key="PageSize_Display_Key"
  description-key="PageSize_Desc_Key"
  of-type="Whole.None"
  usage="input"
  required="false"
  default-value="25">
</property>

<property
  name="enableFiltering"
  display-name-key="EnableFiltering_Display_Key"
  of-type="TwoOptions"
  usage="input"
  required="false"
  default-value="true">
</property>
```

**Accessed in code:**
```typescript
const viewMode = context.parameters.viewMode.raw; // "grid" | "tiles" | "list"
const pageSize = context.parameters.pageSize.raw; // number
const enableFiltering = context.parameters.enableFiltering.raw; // boolean
```

---

## Core Implementation Pattern

### Class Structure

```typescript
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import DataSetInterfaces = ComponentFramework.PropertyHelper.DataSetApi;
type DataSet = ComponentFramework.PropertyTypes.DataSet;

export class DocumentGrid implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    // Context & State
    private context: ComponentFramework.Context<IInputs>;
    private notifyOutputChanged: () => void;

    // DOM Elements
    private mainContainer: HTMLDivElement;
    private gridContainer: HTMLDivElement;
    private loadMoreButton: HTMLButtonElement;

    // Configuration (from manifest properties)
    private viewMode: string;
    private pageSize: number;

    constructor() {}

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        this.context = context;
        this.notifyOutputChanged = notifyOutputChanged;

        // Enable responsive sizing
        context.mode.trackContainerResize(true);

        // Create DOM structure
        this.mainContainer = document.createElement("div");
        this.gridContainer = document.createElement("div");
        this.loadMoreButton = document.createElement("button");

        // Configure load more button
        this.loadMoreButton.innerText = "Load More";
        this.loadMoreButton.addEventListener("click", () => {
            context.parameters.documents.paging.loadNextPage();
        });

        // Assemble DOM
        this.mainContainer.appendChild(this.gridContainer);
        this.mainContainer.appendChild(this.loadMoreButton);
        container.appendChild(this.mainContainer);
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;
        const dataset = context.parameters.documents;

        // Don't render while loading
        if (dataset.loading) return;

        // Clear existing content
        while (this.gridContainer.firstChild) {
            this.gridContainer.removeChild(this.gridContainer.firstChild);
        }

        // Render records
        this.renderRecords(dataset);

        // Toggle load more button
        this.loadMoreButton.style.display =
            dataset.paging.hasNextPage ? "block" : "none";
    }

    public getOutputs(): IOutputs {
        return {};
    }

    public destroy(): void {
        // Cleanup
    }

    private renderRecords(dataset: DataSet): void {
        // Implementation in next section
    }
}
```

---

## Dataset API - Key Operations

### 1. Accessing Columns

```typescript
// Get all columns
const allColumns = context.parameters.documents.columns;

// Get columns visible in view (order >= 0)
const visibleColumns = allColumns.filter(col => col.order >= 0);

// Sort columns by display order
visibleColumns.sort((a, b) => a.order - b.order);

// Column properties
column.name            // Logical name (e.g., "spe_documentid")
column.displayName     // Display name (e.g., "Document Name")
column.dataType        // Type (e.g., "SingleLine.Text", "Lookup", "DateTime")
column.order           // Display order (>= 0 means visible)
column.alias           // Alias in view
```

### 2. Accessing Records

```typescript
const dataset = context.parameters.documents;

// Get all record IDs (sorted by view order)
const recordIds = dataset.sortedRecordIds; // string[]

// Access individual record
const recordId = recordIds[0];
const record = dataset.records[recordId];

// Get formatted value (respects Dataverse formatting)
const documentName = record.getFormattedValue("spe_documentname");
const createdDate = record.getFormattedValue("createdon"); // "1/15/2025 3:42 PM"

// Get raw value (unformatted)
const rawDate = record.getValue("createdon"); // Date object

// Get record reference (for navigation)
const entityRef = record.getNamedReference();
// { id: { guid: "..." }, name: "spe_document" }

// Get record ID
const recordId = record.getRecordId();
```

### 3. Pagination

```typescript
const paging = dataset.paging;

// Check if more records available
if (paging.hasNextPage) {
    paging.loadNextPage(); // Triggers updateView() with new data
}

// Check total record count (if available)
const totalRecords = paging.totalResultCount; // May be -1 if unknown

// Current page size
const pageSize = paging.pageSize; // Typically 25, 50, or 100
```

### 4. Sorting

```typescript
const sorting = dataset.sorting;

// Get current sort columns
const sortedColumns = sorting.getColumns();
// Returns: Array<{ name: string, sortDirection: 0 (None) | 1 (Asc) | 2 (Desc) }>

// Example: Check if sorted by created date descending
const sortByCreatedDate = sortedColumns.find(s => s.name === "createdon");
if (sortByCreatedDate?.sortDirection === 2) {
    // Sorted descending
}
```

### 5. Filtering

```typescript
const filtering = dataset.filtering;

// Get current filter expression (read-only in most cases)
const currentFilter = filtering.getFilter();
// Returns FetchXML filter node

// Note: PCF controls typically don't modify filters directly
// Users filter via view configuration in model-driven apps
```

---

## Rendering Patterns

### Pattern 1: Tile/Card View (Microsoft Sample)

**Use Case:** Visual browsing, dashboards, document libraries

```typescript
private renderRecords(dataset: DataSet): void {
    const columns = this.getSortedColumns(dataset);

    for (const recordId of dataset.sortedRecordIds) {
        const record = dataset.records[recordId];

        // Create tile
        const tile = document.createElement("div");
        tile.classList.add("grid-tile");
        tile.setAttribute("data-record-id", recordId);
        tile.addEventListener("click", () => this.openRecord(recordId));

        // Render each column as label-value pair
        columns.forEach(column => {
            const label = document.createElement("p");
            label.classList.add("tile-label");
            label.textContent = `${column.displayName}:`;

            const value = document.createElement("p");
            value.classList.add("tile-value");
            value.textContent = record.getFormattedValue(column.name) || "-";

            tile.appendChild(label);
            tile.appendChild(value);
        });

        this.gridContainer.appendChild(tile);
    }

    // Handle empty state
    if (dataset.sortedRecordIds.length === 0) {
        const emptyMsg = document.createElement("div");
        emptyMsg.textContent = "No records found";
        this.gridContainer.appendChild(emptyMsg);
    }
}

private openRecord(recordId: string): void {
    const record = this.context.parameters.documents.records[recordId];
    const entityRef = record.getNamedReference();

    this.context.navigation.openForm({
        entityName: entityRef.name,
        entityId: entityRef.id.guid
    });
}
```

### Pattern 2: Table/Grid View

**Use Case:** Data-heavy views, bulk operations, sorting

```typescript
private renderRecords(dataset: DataSet): void {
    const columns = this.getSortedColumns(dataset);

    // Create table
    const table = document.createElement("table");
    table.classList.add("data-grid-table");

    // Header row
    const thead = document.createElement("thead");
    const headerRow = document.createElement("tr");
    columns.forEach(column => {
        const th = document.createElement("th");
        th.textContent = column.displayName;
        headerRow.appendChild(th);
    });
    thead.appendChild(headerRow);
    table.appendChild(thead);

    // Body rows
    const tbody = document.createElement("tbody");
    for (const recordId of dataset.sortedRecordIds) {
        const record = dataset.records[recordId];
        const row = document.createElement("tr");
        row.setAttribute("data-record-id", recordId);
        row.addEventListener("click", () => this.openRecord(recordId));

        columns.forEach(column => {
            const td = document.createElement("td");
            td.textContent = record.getFormattedValue(column.name) || "-";
            row.appendChild(td);
        });

        tbody.appendChild(row);
    }
    table.appendChild(tbody);

    this.gridContainer.appendChild(table);
}
```

### Pattern 3: List View with Icons

**Use Case:** Document libraries, file browsers, compact lists

```typescript
private renderRecords(dataset: DataSet): void {
    const columns = this.getSortedColumns(dataset);

    for (const recordId of dataset.sortedRecordIds) {
        const record = dataset.records[recordId];

        // Create list item
        const listItem = document.createElement("div");
        listItem.classList.add("list-item");
        listItem.addEventListener("click", () => this.openRecord(recordId));

        // Icon (based on file type or record type)
        const icon = document.createElement("span");
        icon.classList.add("list-item-icon");
        icon.textContent = this.getIconForRecord(record); // 📄 📊 📷
        listItem.appendChild(icon);

        // Primary column (usually name)
        const primaryColumn = columns[0];
        const primaryValue = document.createElement("div");
        primaryValue.classList.add("list-item-primary");
        primaryValue.textContent = record.getFormattedValue(primaryColumn.name);
        listItem.appendChild(primaryValue);

        // Secondary metadata
        const metadata = document.createElement("div");
        metadata.classList.add("list-item-metadata");
        metadata.textContent = columns.slice(1, 3)
            .map(col => record.getFormattedValue(col.name))
            .filter(v => v)
            .join(" • ");
        listItem.appendChild(metadata);

        this.gridContainer.appendChild(listItem);
    }
}
```

---

## Configuration Strategy for Spaarke

### Design Principle: Single Component, Multiple Configurations

Instead of creating separate components for each use case, build **one flexible Dataset component** with configuration properties:

```xml
<!-- Manifest Properties for Flexibility -->
<property name="viewMode" of-type="Enum" usage="input">
  <value name="table">Table</value>
  <value name="tiles">Tiles</value>
  <value name="list">List</value>
</property>

<property name="primaryColumn" of-type="SingleLine.Text" usage="input" />
<property name="showIcons" of-type="TwoOptions" usage="input" />
<property name="enableInlineEdit" of-type="TwoOptions" usage="input" />
<property name="customCssClass" of-type="SingleLine.Text" usage="input" />
```

### Implementation Strategy

```typescript
public updateView(context: ComponentFramework.Context<IInputs>): void {
    const dataset = context.parameters.documents;
    if (dataset.loading) return;

    // Read configuration
    const viewMode = context.parameters.viewMode.raw;
    const primaryColumn = context.parameters.primaryColumn.raw;

    // Clear container
    this.clearContainer();

    // Render based on configuration
    switch (viewMode) {
        case "table":
            this.renderTableView(dataset);
            break;
        case "tiles":
            this.renderTileView(dataset);
            break;
        case "list":
            this.renderListView(dataset, primaryColumn);
            break;
    }

    // Apply custom CSS class if specified
    const customClass = context.parameters.customCssClass.raw;
    if (customClass) {
        this.mainContainer.classList.add(customClass);
    }
}
```

### Reusability Across Spaarke

| Use Case | Configuration |
|----------|---------------|
| **Document Library** | `viewMode="list"`, `showIcons=true`, `primaryColumn="spe_documentname"` |
| **Job Status Grid** | `viewMode="table"`, `enableInlineEdit=false` |
| **User Management** | `viewMode="tiles"`, `primaryColumn="fullname"` |
| **Audit Logs** | `viewMode="table"`, `showIcons=false`, `customCssClass="compact-table"` |

---

## Styling Best Practices

### CSS Scoping

PCF controls use **namespace-based CSS scoping** to prevent style conflicts:

```css
/* Bad: Global styles */
.grid-item {
    background-color: blue;
}

/* Good: Namespaced styles */
.Spaarke\.DocumentGrid .grid-item {
    background-color: blue;
}
```

**Pattern from Microsoft sample:**
```css
.SampleNamespace\.DataSetGrid .DataSetControl_main-container {
    overflow-y: auto;
}

.SampleNamespace\.DataSetGrid .DataSetControl_grid-item {
    margin: 5px;
    width: 200px;
    height: 200px;
    background-color: rgb(59, 121, 183);
}
```

### Responsive Design

```typescript
// Track container resize
context.mode.trackContainerResize(true);

// Access allocated dimensions in updateView
const allocatedWidth = context.mode.allocatedWidth;
const allocatedHeight = context.mode.allocatedHeight;

// Adjust layout dynamically
if (allocatedWidth < 600) {
    this.gridContainer.classList.add("compact-mode");
} else {
    this.gridContainer.classList.remove("compact-mode");
}
```

### Theme Integration

Use Dataverse theme variables for consistency:

```css
.Spaarke\.DocumentGrid .grid-item {
    background-color: var(--themePrimary); /* Dataverse primary color */
    color: var(--themeText);
}

.Spaarke\.DocumentGrid .grid-item:hover {
    background-color: var(--themeDark);
}
```

---

## Navigation & Interactions

### Opening Records

```typescript
private openRecord(recordId: string): void {
    const record = this.context.parameters.documents.records[recordId];
    const entityRef = record.getNamedReference();

    // Open in form
    this.context.navigation.openForm({
        entityName: entityRef.name,
        entityId: entityRef.id.guid,
        openInNewWindow: false // or true for popup
    });
}
```

### Creating New Records

```typescript
private createNewRecord(): void {
    const dataset = this.context.parameters.documents;
    const entityName = dataset.getTargetEntityType();

    this.context.navigation.openForm({
        entityName: entityName,
        useQuickCreateForm: true // Optional: use quick create
    });
}
```

### Triggering Actions

```typescript
private deleteRecord(recordId: string): void {
    // Note: PCF cannot delete records directly
    // Use Web API via context.webAPI

    this.context.webAPI.deleteRecord(
        this.context.parameters.documents.getTargetEntityType(),
        recordId
    ).then(() => {
        // Refresh dataset
        this.context.parameters.documents.refresh();
    });
}
```

---

## Error Handling & Edge Cases

### Handle Missing Data

```typescript
public updateView(context: ComponentFramework.Context<IInputs>): void {
    const dataset = context.parameters.documents;

    // 1. Check if dataset is loading
    if (dataset.loading) {
        this.showLoadingSpinner();
        return;
    }

    // 2. Check if columns available
    const columns = this.getSortedColumns(dataset);
    if (!columns || columns.length === 0) {
        this.showError("No columns configured. Please add columns to the view.");
        return;
    }

    // 3. Handle empty record set
    if (dataset.sortedRecordIds.length === 0) {
        this.showEmptyState("No records found.");
        return;
    }

    // 4. Render records
    this.renderRecords(dataset);
}
```

### Handle Null/Missing Values

```typescript
// Use getFormattedValue with fallback
const value = record.getFormattedValue(column.name) || "-";

// Or check explicitly
const rawValue = record.getValue(column.name);
if (rawValue == null || rawValue === "") {
    // Display placeholder
    element.textContent = "N/A";
} else {
    element.textContent = record.getFormattedValue(column.name);
}
```

### Error Boundaries

```typescript
public updateView(context: ComponentFramework.Context<IInputs>): void {
    try {
        // Rendering logic
        this.renderRecords(context.parameters.documents);
    } catch (error) {
        console.error("Error rendering dataset:", error);
        this.showError("An error occurred while rendering the grid.");
    }
}

private showError(message: string): void {
    this.gridContainer.innerHTML = "";
    const errorDiv = document.createElement("div");
    errorDiv.classList.add("error-message");
    errorDiv.textContent = message;
    this.gridContainer.appendChild(errorDiv);
}
```

---

## Performance Optimization

### 1. Virtual Scrolling for Large Datasets

```typescript
// Only render visible records (simplified example)
private renderVisibleRecords(dataset: DataSet): void {
    const scrollTop = this.gridContainer.scrollTop;
    const containerHeight = this.gridContainer.clientHeight;
    const itemHeight = 60; // Fixed item height

    const startIndex = Math.floor(scrollTop / itemHeight);
    const endIndex = Math.ceil((scrollTop + containerHeight) / itemHeight);

    const visibleRecordIds = dataset.sortedRecordIds.slice(startIndex, endIndex);

    // Render only visible records
    visibleRecordIds.forEach((recordId, index) => {
        const item = this.createListItem(dataset.records[recordId]);
        item.style.position = "absolute";
        item.style.top = `${(startIndex + index) * itemHeight}px`;
        this.gridContainer.appendChild(item);
    });

    // Set container height for scrollbar
    this.gridContainer.style.height = `${dataset.sortedRecordIds.length * itemHeight}px`;
}
```

### 2. Debounce Expensive Operations

```typescript
private debounceTimer: number | null = null;

private onContainerScroll(): void {
    if (this.debounceTimer) {
        clearTimeout(this.debounceTimer);
    }

    this.debounceTimer = window.setTimeout(() => {
        this.renderVisibleRecords(this.context.parameters.documents);
    }, 100); // 100ms debounce
}
```

### 3. Cache Column Metadata

```typescript
private cachedColumns: DataSetInterfaces.Column[] | null = null;

private getSortedColumns(dataset: DataSet): DataSetInterfaces.Column[] {
    // Columns don't change often, cache them
    if (!this.cachedColumns) {
        this.cachedColumns = dataset.columns
            .filter(col => col.order >= 0)
            .sort((a, b) => a.order - b.order);
    }
    return this.cachedColumns;
}

public updateView(context: ComponentFramework.Context<IInputs>): void {
    // Clear cache if columns changed
    const dataset = context.parameters.documents;
    if (context.updatedProperties.includes("dataset.columns")) {
        this.cachedColumns = null;
    }
    // ... rest of updateView
}
```

---

## Localization (i18n)

### Resource Strings (RESX)

**DataSetGrid.1033.resx** (English - US):
```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="DocumentGrid_Display_Key">
    <value>Document Grid</value>
  </data>
  <data name="DocumentGrid_Desc_Key">
    <value>Displays documents in a customizable grid layout</value>
  </data>
  <data name="LoadMore_ButtonLabel">
    <value>Load More</value>
  </data>
  <data name="NoRecords_Message">
    <value>No documents found</value>
  </data>
</root>
```

### Accessing Strings in Code

```typescript
// In updateView or other methods
const loadMoreText = this.context.resources.getString("LoadMore_ButtonLabel");
this.loadPageButton.innerText = loadMoreText;

const noRecordsMsg = this.context.resources.getString("NoRecords_Message");
emptyStateDiv.textContent = noRecordsMsg;
```

### Supporting Multiple Languages

Create additional RESX files:
- `DataSetGrid.1033.resx` - English (US)
- `DataSetGrid.1036.resx` - French
- `DataSetGrid.1031.resx` - German
- `DataSetGrid.1041.resx` - Japanese

Framework automatically loads correct file based on user's language preference.

---

## Testing & Debugging

### Local Testing with `pcf-start`

```bash
# Install dependencies
npm install

# Start test harness
npm start watch

# Opens browser at http://localhost:8181
```

**Test harness features:**
- Live reload on code changes
- Mock dataset with configurable records
- Test different screen sizes
- Simulate pagination

### Debugging Tips

```typescript
// Log dataset state
console.log("Dataset Info:", {
    loading: dataset.loading,
    recordCount: dataset.sortedRecordIds.length,
    hasNextPage: dataset.paging.hasNextPage,
    columns: dataset.columns.map(c => c.name)
});

// Log record details
dataset.sortedRecordIds.forEach(recordId => {
    const record = dataset.records[recordId];
    console.log("Record:", recordId, {
        values: dataset.columns.map(col => ({
            name: col.name,
            formatted: record.getFormattedValue(col.name),
            raw: record.getValue(col.name)
        }))
    });
});
```

### Browser Developer Tools

- **Inspect elements** - Verify DOM structure and CSS
- **Network tab** - Monitor Dataverse API calls
- **Console** - View logs and errors
- **Performance tab** - Profile rendering performance

---

## Deployment to Dataverse

### Build for Production

```bash
# Build optimized bundle
npm run build

# Output: out/controls/DocumentGrid directory
# Contains: bundle.js, ControlManifest.xml, etc.
```

### Create Solution

```bash
# Initialize solution
pac solution init --publisher-name Spaarke --publisher-prefix spe

# Add control reference
pac solution add-reference --path ../DocumentGrid

# Build solution (creates .zip)
msbuild /t:build /restore
```

### Import to Dataverse

1. Navigate to **Power Apps** > **Solutions**
2. Click **Import solution**
3. Upload the `.zip` file
4. Wait for import to complete

### Add to Model-Driven App

1. Open app designer
2. Navigate to view where control should appear
3. Click **+ Component** > **Get more components**
4. Select **DocumentGrid** from gallery
5. Configure properties (viewMode, pageSize, etc.)
6. **Save** and **Publish**

---

## Common Patterns for Spaarke

### 1. Document Library with Icons

**Configuration:**
- Dataset: SPE Documents (`spe_document`)
- View Mode: List
- Show Icons: True
- Primary Column: `spe_documentname`

**Custom Icon Logic:**
```typescript
private getIconForDocument(record: DataSetInterfaces.EntityRecord): string {
    const fileName = record.getFormattedValue("spe_documentname") || "";
    const extension = fileName.split('.').pop()?.toLowerCase();

    const iconMap: Record<string, string> = {
        'pdf': '📄',
        'docx': '📝',
        'xlsx': '📊',
        'pptx': '📽️',
        'jpg': '📷',
        'png': '🖼️',
        'zip': '📦'
    };

    return iconMap[extension || ''] || '📄';
}
```

### 2. Job Status Grid with Color Coding

**Configuration:**
- Dataset: Background Jobs (`spe_backgroundjob`)
- View Mode: Table
- Custom CSS: Status-based colors

**Status Indicator:**
```typescript
private renderStatusCell(record: DataSetInterfaces.EntityRecord): HTMLElement {
    const status = record.getValue("spe_status") as number;
    const statusText = record.getFormattedValue("spe_status");

    const cell = document.createElement("td");
    const badge = document.createElement("span");
    badge.classList.add("status-badge");

    // Status values from option set
    switch (status) {
        case 1: // Pending
            badge.classList.add("status-pending");
            break;
        case 2: // In Progress
            badge.classList.add("status-inprogress");
            break;
        case 3: // Completed
            badge.classList.add("status-completed");
            break;
        case 4: // Failed
            badge.classList.add("status-failed");
            break;
    }

    badge.textContent = statusText;
    cell.appendChild(badge);
    return cell;
}
```

**CSS:**
```css
.Spaarke\.JobStatusGrid .status-badge {
    padding: 4px 12px;
    border-radius: 12px;
    font-size: 12px;
    font-weight: bold;
}

.Spaarke\.JobStatusGrid .status-pending {
    background-color: #ffc107;
    color: #000;
}

.Spaarke\.JobStatusGrid .status-inprogress {
    background-color: #2196f3;
    color: #fff;
}

.Spaarke\.JobStatusGrid .status-completed {
    background-color: #4caf50;
    color: #fff;
}

.Spaarke\.JobStatusGrid .status-failed {
    background-color: #f44336;
    color: #fff;
}
```

### 3. User Assignment with Quick Actions

**Configuration:**
- Dataset: System Users with custom filtering
- View Mode: Tiles
- Enable Actions: True

**Action Buttons:**
```typescript
private renderUserTile(record: DataSetInterfaces.EntityRecord): HTMLElement {
    const tile = document.createElement("div");
    tile.classList.add("user-tile");

    // User info
    const name = document.createElement("div");
    name.classList.add("user-name");
    name.textContent = record.getFormattedValue("fullname");
    tile.appendChild(name);

    const email = document.createElement("div");
    email.classList.add("user-email");
    email.textContent = record.getFormattedValue("internalemailaddress");
    tile.appendChild(email);

    // Action buttons
    const actions = document.createElement("div");
    actions.classList.add("user-actions");

    const assignButton = document.createElement("button");
    assignButton.textContent = "Assign Role";
    assignButton.addEventListener("click", (e) => {
        e.stopPropagation(); // Prevent tile click
        this.assignRole(record.getRecordId());
    });

    const viewButton = document.createElement("button");
    viewButton.textContent = "View Profile";
    viewButton.addEventListener("click", (e) => {
        e.stopPropagation();
        this.openRecord(record.getRecordId());
    });

    actions.appendChild(assignButton);
    actions.appendChild(viewButton);
    tile.appendChild(actions);

    return tile;
}

private assignRole(userId: string): void {
    // Custom logic to open role assignment dialog
    // Could use context.navigation.openDialog or custom modal
}
```

---

## Integration with Spaarke BFF

### Scenario: Display SPE Documents from BFF

While PCF controls typically bind to Dataverse tables, you can integrate with external APIs:

**Option 1: Sync BFF Data to Dataverse Table**
- Background job syncs SPE metadata from BFF to `spe_document` table
- PCF control binds to Dataverse table (standard approach)
- **Pros:** Full PCF features (sorting, filtering, pagination)
- **Cons:** Data latency, sync complexity

**Option 2: Virtual Table (Recommended)**
- Create Dataverse Virtual Table that calls BFF API
- PCF control binds to virtual table
- **Pros:** Real-time data, no sync needed
- **Cons:** Limited filtering/sorting support

**Option 3: Custom API Calls**
- PCF calls BFF directly via `fetch()` or `XMLHttpRequest`
- **Pros:** Full control over API interaction
- **Cons:** Must implement pagination/filtering manually, no dataset binding

**Implementation (Option 3):**
```typescript
public async updateView(context: ComponentFramework.Context<IInputs>): Promise<void> {
    this.context = context;

    try {
        // Call Spaarke BFF API
        const response = await fetch(`${BFF_BASE_URL}/api/spe/containers/${containerId}/items`, {
            headers: {
                'Authorization': `Bearer ${this.getUserToken()}`
            }
        });

        if (!response.ok) {
            throw new Error(`BFF API error: ${response.status}`);
        }

        const documents = await response.json();

        // Render documents
        this.renderDocumentsFromBFF(documents);

    } catch (error) {
        console.error("Error fetching documents from BFF:", error);
        this.showError("Failed to load documents");
    }
}

private getUserToken(): string {
    // Get token from context (requires authentication setup)
    return this.context.parameters.userToken?.raw || "";
}
```

**Note:** For Spaarke, **Option 2 (Virtual Table)** is recommended as it leverages Dataverse's dataset capabilities while providing real-time access to SPE documents.

---

## Summary: Key Takeaways for Spaarke

### 1. **One Component, Many Configurations**
- Build a single flexible `DatasetGrid` component
- Use manifest properties for view mode, styling, behavior
- Reuse across Document Libraries, Job Status, User Management, Audit Logs

### 2. **Core Dataset Operations**
```typescript
// Access columns
const columns = dataset.columns.filter(c => c.order >= 0).sort((a,b) => a.order - b.order);

// Access records
dataset.sortedRecordIds.forEach(id => {
    const record = dataset.records[id];
    const value = record.getFormattedValue(column.name);
});

// Pagination
if (dataset.paging.hasNextPage) {
    dataset.paging.loadNextPage();
}

// Navigation
const entityRef = record.getNamedReference();
context.navigation.openForm({ entityName: entityRef.name, entityId: entityRef.id.guid });
```

### 3. **Performance Best Practices**
- Cache column metadata
- Use virtual scrolling for large datasets
- Debounce scroll/resize handlers
- Minimize DOM manipulations

### 4. **Integration Strategy**
- **Dataverse Tables:** Use standard dataset binding
- **SPE Documents:** Create Virtual Table pointing to BFF API
- **External Data:** Use custom fetch() calls if needed

### 5. **Development Workflow**
```bash
# 1. Create component
pac pcf init --namespace Spaarke --name DatasetGrid --template dataset

# 2. Develop locally
npm install
npm start watch

# 3. Build for production
npm run build

# 4. Create solution
pac solution init --publisher-name Spaarke
pac solution add-reference --path ./DatasetGrid
msbuild /t:build /restore

# 5. Deploy to Dataverse
# Import solution .zip via Power Apps portal
```

### 6. **Next Steps for Sprint 5**
1. **Design Component Specification** - Define exact features needed (view modes, filters, actions)
2. **Create Virtual Table for SPE** - Map BFF API to Dataverse Virtual Table
3. **Implement Base Component** - Start with simple list view
4. **Add Configuration Options** - View modes, theming, custom actions
5. **Integrate with Model-Driven App** - Test in real Spaarke environment
6. **Optimize & Polish** - Performance tuning, error handling, UX refinement

---

## References

- **Microsoft Sample:** [dev/research/pcf-samples/DataSetGrid/](../research/pcf-samples/DataSetGrid/)
- **Official Docs:** [PCF Dataset Component Guide](https://learn.microsoft.com/power-apps/developer/component-framework/sample-controls/data-set-grid-control)
- **PCF Framework Docs:** [Component Framework Overview](https://learn.microsoft.com/power-apps/developer/component-framework/overview)
- **Virtual Tables:** [Create and edit virtual tables](https://learn.microsoft.com/power-apps/maker/data-platform/create-edit-virtual-entities)

---

**Document Version:** 1.0
**Last Updated:** 2025-10-03
**Author:** Spaarke Development Team
**Sprint:** 5 - PCF Dataset Component
