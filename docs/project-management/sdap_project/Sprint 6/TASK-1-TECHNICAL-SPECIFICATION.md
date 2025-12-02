# Sprint 6 - Phase 1: Technical Specification
**Date:** October 4, 2025
**Sprint:** 6 - SDAP + Universal Dataset Grid Integration
**Phase:** 1 - Configuration & Planning
**Status:** ✅ COMPLETE

---

## Executive Summary

This document provides the complete technical specification for integrating the SDAP (SharePoint Document Access Platform) with the Universal Dataset Grid PCF control. The integration will enable users to manage SharePoint Embedded files directly from the Document entity grid with four custom commands: Add File, Remove File, Update File, and Download File.

**Key Validation Results:**
- ✅ SDAP API fully supports all required file operations
- ✅ Document entity schema confirmed with all required fields
- ✅ JavaScript integration patterns validated from Sprint 2
- ✅ Configuration schema design validated and optimized
- ✅ **Ready to proceed with Phase 2 implementation**

---

## 1. SDAP API Capabilities Assessment

### 1.1 Available API Endpoints

Based on analysis of `DocumentsEndpoints.cs` and `DataverseDocumentsEndpoints.cs`, the following endpoints are available:

#### **File Operations (SharePoint Embedded - MI Auth)**

| Endpoint | Method | Purpose | Auth Required |
|----------|--------|---------|---------------|
| `/api/drives/{driveId}/upload` | PUT | Upload small file (<4MB) | `canwritefiles` |
| `/api/drives/{driveId}/items/{itemId}/content` | GET | Download file | `canmanagecontainers` |
| `/api/drives/{driveId}/items/{itemId}` | GET | Get file metadata | `canmanagecontainers` |
| `/api/drives/{driveId}/items/{itemId}` | DELETE | Delete file | `canwritefiles` |
| `/api/drives/{driveId}/children` | GET | List drive children | `canmanagecontainers` |

#### **Document Management (Dataverse - OBO Auth)**

| Endpoint | Method | Purpose | Auth Required |
|----------|--------|---------|---------------|
| `/api/v1/documents` | POST | Create document record | Yes |
| `/api/v1/documents/{id}` | GET | Get document metadata | Yes |
| `/api/v1/documents/{id}` | PUT | Update document record | Yes |
| `/api/v1/documents/{id}` | DELETE | Delete document record | Yes |
| `/api/v1/documents?containerId={id}` | GET | List documents in container | Yes |

### 1.2 Custom Command to API Mapping

| Custom Command | Primary API Call | Secondary API Calls | Notes |
|----------------|------------------|---------------------|-------|
| **Add File** | `PUT /api/drives/{driveId}/upload` | `PUT /api/v1/documents/{id}` | Upload file, then update Dataverse record |
| **Download File** | `GET /api/drives/{driveId}/items/{itemId}/content` | - | Direct file stream download |
| **Update File** | `PUT /api/drives/{driveId}/upload` | `PUT /api/v1/documents/{id}` | Replace file (same as add), update record |
| **Remove File** | `DELETE /api/drives/{driveId}/items/{itemId}` | `PUT /api/v1/documents/{id}` | Delete from SPE, clear Dataverse fields |

### 1.3 API Capability Validation

✅ **All required operations are supported:**
- File upload (small files <4MB via PUT)
- File download with streaming
- File deletion
- Metadata updates
- Chunked upload for large files (via upload session - future enhancement)

⚠️ **Limitations Identified:**
1. **File Size Limit:** Current implementation uses `UploadSmallAsync` (4MB limit)
   - **Mitigation:** Document size limit, plan Phase 3 enhancement for chunked upload
2. **Authentication:** Mixed MI and OBO authentication required
   - **Solution:** JavaScript layer handles token acquisition for both contexts
3. **Rate Limiting:** All endpoints have rate limiting configured
   - **Impact:** User operations may be throttled under heavy load
   - **Mitigation:** Show appropriate error messages to users

---

## 2. Document Entity Schema Verification

### 2.1 DocumentEntity Fields (from Models.cs)

| Field Name | Data Type | Purpose | Required | Updated By |
|------------|-----------|---------|----------|------------|
| `Id` | string (GUID) | Unique document identifier | Yes | Dataverse |
| `Name` | string | Document name/title | Yes | User |
| `Description` | string? | Document description | No | User |
| `ContainerId` | string? | Parent container GUID | No | User |
| `HasFile` | bool | Flag indicating file attached | No | JavaScript |
| `FileName` | string? | Original file name | No | JavaScript |
| `FileSize` | long? | File size in bytes | No | JavaScript |
| `MimeType` | string? | File MIME type | No | JavaScript |
| `GraphItemId` | string? | SPE DriveItem ID | No | JavaScript |
| `GraphDriveId` | string? | SPE Drive ID | No | JavaScript |
| `Status` | DocumentStatus | Processing status | No | System |
| `CreatedOn` | DateTime | Creation timestamp | No | Dataverse |
| `ModifiedOn` | DateTime | Last modified timestamp | No | Dataverse |

### 2.2 Field Mappings for File Operations

#### **After Add File Operation:**
```json
{
  "HasFile": true,
  "FileName": "<uploaded-file-name>",
  "FileSize": <file-size-bytes>,
  "MimeType": "<file-mime-type>",
  "GraphItemId": "<spe-item-id>",
  "GraphDriveId": "<spe-drive-id>",
  "Status": "Active"
}
```

#### **After Remove File Operation:**
```json
{
  "HasFile": false,
  "FileName": null,
  "FileSize": null,
  "MimeType": null,
  "GraphItemId": null,
  "GraphDriveId": null,
  "Status": "Draft"
}
```

#### **After Update File Operation:**
```json
{
  "HasFile": true,
  "FileName": "<new-file-name>",
  "FileSize": <new-file-size>,
  "MimeType": "<new-mime-type>",
  "GraphItemId": "<new-item-id>",
  "GraphDriveId": "<drive-id>",
  "Status": "Active"
}
```

### 2.3 SharePoint URL Generation

**SharePoint link format:**
```
https://{tenant}.sharepoint.com/contentstorage/{driveId}/items/{itemId}
```

**Implementation:**
- Store `GraphDriveId` and `GraphItemId` in Dataverse
- JavaScript constructs clickable URL using template
- Opens in new browser tab when clicked

---

## 3. JavaScript Integration Architecture

### 3.1 Integration Pattern (from Sprint 2 Task 3.2)

**Namespace:** `Spaarke.Documents`

**Key Components:**
1. **Configuration Management** - `Spaarke.Documents.Config`
2. **Utility Functions** - `Spaarke.Documents.Utils`
3. **File Operations** - `UploadFile`, `DownloadFile`, `ReplaceFile`, `DeleteFile`
4. **Authentication** - `getAuthToken()`, `acquireToken()`
5. **Error Handling** - `Spaarke.Documents.ErrorHandler`

### 3.2 New Namespace for Grid Integration

**Namespace:** `Spaarke.DocumentGrid`

This namespace will extend the existing `Spaarke.Documents` pattern specifically for grid-based operations.

```javascript
var Spaarke = Spaarke || {};
Spaarke.DocumentGrid = Spaarke.DocumentGrid || {};

// Configuration specific to grid operations
Spaarke.DocumentGrid.Config = {
    apiBaseUrl: null,  // Inherited from environment config
    maxFileSize: 4 * 1024 * 1024,  // 4MB limit for small upload
    allowedFileTypes: ['.pdf', '.docx', '.xlsx', '.pptx', '.txt', '.jpg', '.png'],
    bffApiVersion: "v1"
};
```

### 3.3 PCF Control Integration Points

**From PCF Control → JavaScript Web Resource:**

The minimal PCF control will expose global functions that call into the JavaScript web resource:

```javascript
// In PCF control index.ts (minimal version)
(window as any).Spaarke = (window as any).Spaarke || {};
(window as any).Spaarke.DocumentGrid = (window as any).Spaarke.DocumentGrid || {};

// Expose grid context to JavaScript
(window as any).Spaarke.DocumentGrid.getGridContext = () => {
    return {
        selectedRecords: this.selectedRecordIds,
        entityName: "sprk_document",
        context: this.context
    };
};
```

**From JavaScript Web Resource → PCF Control:**

JavaScript will call PCF refresh methods:

```javascript
// Trigger grid refresh after file operation
Spaarke.DocumentGrid.refreshGrid = function() {
    // Access PCF control's refresh method
    if (window.Xrm && window.Xrm.Page) {
        window.Xrm.Page.data.refresh();
    }
};
```

---

## 4. Custom Commands Specification

### 4.1 Command Definitions

#### **Command 1: Add File (+)**

```json
{
  "id": "addFile",
  "label": "+ Add File",
  "icon": "AddDocument",
  "actionType": "javascript",
  "actionHandler": "Spaarke.DocumentGrid.addFile",
  "requiresSelection": true,
  "selectionMode": "single",
  "enableRule": {
    "condition": "and",
    "rules": [
      {"field": "sprk_hasfile", "operator": "equals", "value": false},
      {"permission": "Write"}
    ]
  },
  "confirmMessage": null,
  "successMessage": "File uploaded successfully to {fileName}",
  "errorMessages": {
    "noSelection": "Please select a document to add a file",
    "multipleSelection": "Please select only one document",
    "alreadyHasFile": "This document already has a file. Use Update File instead.",
    "noPermission": "You don't have permission to add files",
    "fileTooLarge": "File size exceeds 4MB limit",
    "uploadFailed": "Failed to upload file: {error}"
  }
}
```

**Implementation Flow:**
1. Validate single record selected
2. Check `sprk_hasfile` = false
3. Show file picker dialog
4. Validate file size < 4MB
5. Call `PUT /api/drives/{driveId}/upload`
6. Update Dataverse: `PUT /api/v1/documents/{id}` with file metadata
7. Refresh grid
8. Show success notification

#### **Command 2: Remove File (-)**

```json
{
  "id": "removeFile",
  "label": "- Remove File",
  "icon": "Delete",
  "actionType": "javascript",
  "actionHandler": "Spaarke.DocumentGrid.removeFile",
  "requiresSelection": true,
  "selectionMode": "single",
  "enableRule": {
    "condition": "and",
    "rules": [
      {"field": "sprk_hasfile", "operator": "equals", "value": true},
      {"permission": "Delete"}
    ]
  },
  "confirmMessage": "Are you sure you want to delete this file? This action cannot be undone.",
  "successMessage": "File removed successfully",
  "errorMessages": {
    "noSelection": "Please select a document to remove file",
    "multipleSelection": "Please select only one document",
    "noFile": "This document does not have a file",
    "noPermission": "You don't have permission to remove files",
    "deleteFailed": "Failed to remove file: {error}"
  }
}
```

**Implementation Flow:**
1. Validate single record selected
2. Check `sprk_hasfile` = true
3. Show confirmation dialog
4. Call `DELETE /api/drives/{driveId}/items/{itemId}`
5. Update Dataverse: `PUT /api/v1/documents/{id}` clear file fields
6. Refresh grid
7. Show success notification

#### **Command 3: Update File (^)**

```json
{
  "id": "updateFile",
  "label": "^ Update File",
  "icon": "Refresh",
  "actionType": "javascript",
  "actionHandler": "Spaarke.DocumentGrid.updateFile",
  "requiresSelection": true,
  "selectionMode": "single",
  "enableRule": {
    "condition": "and",
    "rules": [
      {"field": "sprk_hasfile", "operator": "equals", "value": true},
      {"permission": "Write"}
    ]
  },
  "confirmMessage": "This will replace the existing file. Continue?",
  "successMessage": "File updated successfully",
  "errorMessages": {
    "noSelection": "Please select a document to update file",
    "multipleSelection": "Please select only one document",
    "noFile": "This document does not have a file to update",
    "noPermission": "You don't have permission to update files",
    "fileTooLarge": "File size exceeds 4MB limit",
    "updateFailed": "Failed to update file: {error}"
  }
}
```

**Implementation Flow:**
1. Validate single record selected
2. Check `sprk_hasfile` = true
3. Show confirmation dialog
4. Show file picker
5. Validate file size < 4MB
6. Call `DELETE /api/drives/{driveId}/items/{itemId}` (old file)
7. Call `PUT /api/drives/{driveId}/upload` (new file)
8. Update Dataverse: `PUT /api/v1/documents/{id}` with new metadata
9. Refresh grid
10. Show success notification

#### **Command 4: Download File (↓)**

```json
{
  "id": "downloadFile",
  "label": "↓ Download File",
  "icon": "Download",
  "actionType": "javascript",
  "actionHandler": "Spaarke.DocumentGrid.downloadFile",
  "requiresSelection": true,
  "selectionMode": "multiple",
  "enableRule": {
    "condition": "and",
    "rules": [
      {"field": "sprk_hasfile", "operator": "equals", "value": true},
      {"permission": "Read"}
    ]
  },
  "confirmMessage": null,
  "successMessage": "Download started",
  "errorMessages": {
    "noSelection": "Please select document(s) to download",
    "noFile": "Selected document does not have a file",
    "noPermission": "You don't have permission to download files",
    "downloadFailed": "Failed to download file: {error}"
  }
}
```

**Implementation Flow:**
1. Get selected record(s)
2. For each record: Check `sprk_hasfile` = true
3. Call `GET /api/drives/{driveId}/items/{itemId}/content`
4. Trigger browser download
5. Show notification for each download

### 4.2 Command Rendering in PCF Control

**Minimal PCF Control Implementation:**

```typescript
// Add custom command buttons to grid header
private renderCommandBar(): void {
    const commandBar = document.createElement("div");
    commandBar.className = "command-bar";

    // Add File button
    const addBtn = this.createButton("+ Add File", "addFile");
    addBtn.onclick = () => (window as any).Spaarke.DocumentGrid.addFile(this.context);

    // Remove File button
    const removeBtn = this.createButton("- Remove File", "removeFile");
    removeBtn.onclick = () => (window as any).Spaarke.DocumentGrid.removeFile(this.context);

    // Update File button
    const updateBtn = this.createButton("^ Update File", "updateFile");
    updateBtn.onclick = () => (window as any).Spaarke.DocumentGrid.updateFile(this.context);

    // Download File button
    const downloadBtn = this.createButton("↓ Download", "downloadFile");
    downloadBtn.onclick = () => (window as any).Spaarke.DocumentGrid.downloadFile(this.context);

    commandBar.append(addBtn, removeBtn, updateBtn, downloadBtn);
    this.container.prepend(commandBar);
}

private createButton(label: string, commandId: string): HTMLButtonElement {
    const button = document.createElement("button");
    button.textContent = label;
    button.className = `command-button ${commandId}`;
    button.setAttribute("data-command-id", commandId);
    return button;
}
```

---

## 5. Configuration Schema

### 5.1 PCF Control Configuration JSON

The PCF control accepts a `configJson` property for entity-specific configuration:

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
    "allowedExtensions": [".pdf", ".docx", ".xlsx", ".pptx", ".txt", ".jpg", ".png", ".gif"],
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
    "requiredRoles": ["Document Manager", "System Administrator"]
  }
}
```

### 5.2 Configuration Loading Strategy

**Option A: Static Configuration (Recommended for Sprint 6)**
- Configuration stored in PCF control properties
- Set during form/view customization
- No runtime configuration changes
- Simple, predictable, secure

**Option B: Dynamic Configuration (Future Enhancement)**
- Configuration stored in Dataverse custom settings entity
- Loaded at runtime via Web API
- Allows admin changes without republishing control
- More complex, requires additional security

**Decision:** Use Option A for Sprint 6, plan Option B for Sprint 7.

### 5.3 Configuration Validation

JavaScript web resource will validate configuration on load:

```javascript
Spaarke.DocumentGrid.validateConfig = function(config) {
    const errors = [];

    // Required fields
    if (!config.entityName) errors.push("entityName is required");
    if (!config.apiConfig?.baseUrl) errors.push("apiConfig.baseUrl is required");

    // File size validation
    if (config.fileConfig?.maxFileSize > 4194304) {
        errors.push("maxFileSize cannot exceed 4MB (4194304 bytes)");
    }

    // Field mappings validation
    const requiredMappings = ["documentId", "hasFile", "fileName", "graphItemId", "graphDriveId"];
    for (const field of requiredMappings) {
        if (!config.fieldMappings[field]) {
            errors.push(`fieldMappings.${field} is required`);
        }
    }

    return {
        isValid: errors.length === 0,
        errors: errors
    };
};
```

---

## 6. Data Flow Diagrams

### 6.1 Add File Operation Flow

```
┌─────────────┐
│ User        │
│ (Grid)      │
└──────┬──────┘
       │ 1. Clicks "+ Add File"
       ↓
┌─────────────────────┐
│ PCF Control         │
│ (Minimal JS)        │
└──────┬──────────────┘
       │ 2. Call Spaarke.DocumentGrid.addFile(context)
       ↓
┌─────────────────────────────┐
│ JavaScript Web Resource     │
│ (sprk_DocumentGridIntegration) │
└──────┬──────────────────────┘
       │ 3. Validate selection
       │ 4. Show file picker
       │ 5. Validate file
       ↓
┌─────────────────┐
│ SDAP BFF API    │
│ (MI Auth)       │
└──────┬──────────┘
       │ 6. PUT /api/drives/{driveId}/upload
       │ 7. Returns DriveItem metadata
       ↓
┌─────────────────┐
│ SDAP BFF API    │
│ (OBO Auth)      │
└──────┬──────────┘
       │ 8. PUT /api/v1/documents/{id}
       │ 9. Update file metadata fields
       ↓
┌─────────────────────────────┐
│ JavaScript Web Resource     │
└──────┬──────────────────────┘
       │ 10. Refresh grid
       │ 11. Show success message
       ↓
┌─────────────┐
│ User        │
│ (Grid)      │
└─────────────┘
```

### 6.2 Download File Operation Flow

```
┌─────────────┐
│ User        │
│ (Grid)      │
└──────┬──────┘
       │ 1. Clicks "↓ Download File"
       ↓
┌─────────────────────┐
│ PCF Control         │
└──────┬──────────────┘
       │ 2. Call Spaarke.DocumentGrid.downloadFile(context)
       ↓
┌─────────────────────────────┐
│ JavaScript Web Resource     │
└──────┬──────────────────────┘
       │ 3. Validate selection
       │ 4. Get GraphDriveId, GraphItemId from selected record
       ↓
┌─────────────────┐
│ SDAP BFF API    │
│ (MI Auth)       │
└──────┬──────────┘
       │ 5. GET /api/drives/{driveId}/items/{itemId}/content
       │ 6. Returns file stream
       ↓
┌─────────────────────────────┐
│ JavaScript Web Resource     │
└──────┬──────────────────────┘
       │ 7. Create blob URL
       │ 8. Trigger browser download
       ↓
┌─────────────┐
│ User        │
│ (Browser)   │
└─────────────┘
```

### 6.3 Remove File Operation Flow

```
┌─────────────┐
│ User        │
│ (Grid)      │
└──────┬──────┘
       │ 1. Clicks "- Remove File"
       ↓
┌─────────────────────┐
│ PCF Control         │
└──────┬──────────────┘
       │ 2. Call Spaarke.DocumentGrid.removeFile(context)
       ↓
┌─────────────────────────────┐
│ JavaScript Web Resource     │
└──────┬──────────────────────┘
       │ 3. Validate selection
       │ 4. Show confirmation dialog
       ↓
┌─────────────────┐
│ SDAP BFF API    │
│ (MI Auth)       │
└──────┬──────────┘
       │ 5. DELETE /api/drives/{driveId}/items/{itemId}
       │ 6. Returns 204 No Content
       ↓
┌─────────────────┐
│ SDAP BFF API    │
│ (OBO Auth)      │
└──────┬──────────┘
       │ 7. PUT /api/v1/documents/{id}
       │ 8. Clear file metadata fields
       ↓
┌─────────────────────────────┐
│ JavaScript Web Resource     │
└──────┬──────────────────────┘
       │ 9. Refresh grid
       │ 10. Show success message
       ↓
┌─────────────┐
│ User        │
│ (Grid)      │
└─────────────┘
```

---

## 7. Security Considerations

### 7.1 Authentication Flow

**Two Authentication Contexts:**

1. **Managed Identity (MI)** - For SharePoint Embedded operations
   - Used by BFF API to call Graph API
   - Transparent to client JavaScript
   - Configured in Azure App Service

2. **On-Behalf-Of (OBO)** - For Dataverse operations
   - User's Azure AD token passed from Power Platform
   - Acquired by JavaScript via Xrm.WebApi or MSAL.js
   - Passed in `Authorization: Bearer {token}` header

**JavaScript Token Acquisition:**

```javascript
Spaarke.DocumentGrid.getAuthToken = async function() {
    try {
        // For Power Platform context, use current user's token
        const globalContext = Xrm.Utility.getGlobalContext();
        const accessToken = await globalContext.getCurrentAppUrl(); // Placeholder - actual implementation varies

        // Alternative: Use MSAL.js for explicit token acquisition
        // const token = await msalInstance.acquireTokenSilent({
        //     scopes: ["https://spaarkedev1.crm.dynamics.com/.default"]
        // });

        return accessToken;
    } catch (error) {
        console.error("Token acquisition failed:", error);
        throw new Error("Authentication failed. Please refresh and try again.");
    }
};
```

### 7.2 Permission Checks

**Server-Side Authorization (SDAP API):**
- All endpoints require authorization policies
- `canwritefiles` - Upload, Update, Delete
- `canmanagecontainers` - Download, List
- Enforced via `DocumentAuthorizationFilter`

**Client-Side Permission Checks (JavaScript):**
- Query user's AccessRights via `/api/v1/documents/{id}/permissions`
- Enable/disable buttons based on permissions
- Show appropriate error messages

```javascript
Spaarke.DocumentGrid.checkPermissions = async function(documentId) {
    try {
        const token = await Spaarke.DocumentGrid.getAuthToken();
        const response = await fetch(
            `${config.apiBaseUrl}/api/v1/documents/${documentId}/permissions`,
            {
                headers: { "Authorization": `Bearer ${token}` }
            }
        );

        if (!response.ok) return { canWrite: false, canDelete: false, canRead: false };

        const permissions = await response.json();
        return {
            canWrite: (permissions.accessRights & AccessRights.Write) !== 0,
            canDelete: (permissions.accessRights & AccessRights.Delete) !== 0,
            canRead: (permissions.accessRights & AccessRights.Read) !== 0
        };
    } catch (error) {
        console.error("Permission check failed:", error);
        return { canWrite: false, canDelete: false, canRead: false };
    }
};
```

### 7.3 Input Validation

**Client-Side (JavaScript):**
- File type whitelist
- File size limit (4MB)
- File name sanitization
- MIME type validation

**Server-Side (SDAP API):**
- Request validation via data annotations
- GUID validation
- Path traversal prevention
- Content-Type validation

### 7.4 CORS Configuration

**Required CORS Settings (Already configured in SDAP):**
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("PowerPlatform", policy =>
    {
        policy.WithOrigins("https://spaarkedev1.crm.dynamics.com")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});
```

---

## 8. Error Handling Strategy

### 8.1 Error Categories

| Error Type | HTTP Status | User Message | Logging Level |
|------------|-------------|--------------|---------------|
| Validation Error | 400 | "Invalid file type. Please select a .pdf, .docx, or .xlsx file." | Warning |
| Authentication Error | 401 | "Authentication failed. Please refresh the page and try again." | Error |
| Permission Error | 403 | "You don't have permission to perform this action." | Warning |
| Not Found | 404 | "Document or file not found." | Warning |
| File Too Large | 413 | "File size exceeds 4MB limit." | Warning |
| Server Error | 500 | "An unexpected error occurred. Please contact support." | Error |
| Network Error | - | "Network connection failed. Please check your connection." | Error |

### 8.2 Error Handling Implementation

```javascript
Spaarke.DocumentGrid.handleError = function(error, operation, context) {
    let userMessage = "An unexpected error occurred.";
    let logLevel = "error";

    // Parse error response
    if (error.response) {
        const status = error.response.status;

        if (status === 400) {
            userMessage = error.response.detail || "Invalid request.";
            logLevel = "warning";
        } else if (status === 401) {
            userMessage = "Please sign in again.";
        } else if (status === 403) {
            userMessage = "You don't have permission for this action.";
            logLevel = "warning";
        } else if (status === 404) {
            userMessage = "Document or file not found.";
            logLevel = "warning";
        } else if (status === 413) {
            userMessage = "File size exceeds 4MB limit.";
            logLevel = "warning";
        }
    }

    // Log error with context
    Spaarke.Documents.ErrorHandler.logError(error, {
        operation: operation,
        documentId: context.selectedRecordIds[0],
        timestamp: new Date().toISOString()
    }, Spaarke.Documents.Utils.generateCorrelationId());

    // Show user-friendly message
    Spaarke.Documents.Utils.showError(
        `${operation} Failed`,
        userMessage
    );
};
```

### 8.3 Retry Logic

For transient failures (429 Too Many Requests, 503 Service Unavailable):

```javascript
Spaarke.DocumentGrid.withRetry = async function(operation, maxRetries = 3) {
    for (let attempt = 1; attempt <= maxRetries; attempt++) {
        try {
            return await operation();
        } catch (error) {
            if (attempt === maxRetries) throw error;

            // Check if error is retryable
            if (error.response?.status === 429 || error.response?.status === 503) {
                const retryAfter = error.response.headers.get("Retry-After") || (attempt * 2);
                await Spaarke.Documents.Utils.sleep(retryAfter * 1000);
                continue;
            }

            throw error;
        }
    }
};
```

---

## 9. Testing Strategy

### 9.1 Unit Testing (JavaScript)

**Test Framework:** Jest or Mocha

**Test Cases:**
1. Configuration validation
2. File validation (size, type)
3. Permission checking logic
4. Error message formatting
5. URL construction (SharePoint links)

### 9.2 Integration Testing

**Test Scenarios:**

| Test Case | Steps | Expected Result |
|-----------|-------|-----------------|
| **Add File - Success** | 1. Select document without file<br>2. Click Add File<br>3. Select 2MB PDF<br>4. Confirm | File uploaded, grid refreshed, success message |
| **Add File - Already Has File** | 1. Select document with file<br>2. Click Add File | Error: "Document already has file" |
| **Add File - File Too Large** | 1. Select document<br>2. Select 5MB file | Error: "File size exceeds 4MB" |
| **Download File - Success** | 1. Select document with file<br>2. Click Download | File downloads to browser |
| **Remove File - Success** | 1. Select document with file<br>2. Click Remove<br>3. Confirm | File deleted, grid refreshed, fields cleared |
| **Remove File - No File** | 1. Select document without file<br>2. Click Remove | Error: "Document has no file" |
| **Update File - Success** | 1. Select document with file<br>2. Click Update<br>3. Select new file<br>4. Confirm | Old file replaced, metadata updated |
| **Permission Denied** | 1. Select document (read-only user)<br>2. Click Add File | Error: "No permission" |

### 9.3 End-to-End Testing

**User Acceptance Testing (UAT) Scenarios:**
1. Document manager uploads contract PDF
2. Legal team downloads contract
3. Manager updates contract with new version
4. Admin removes outdated document file
5. User clicks SharePoint link to view file in browser

### 9.4 Performance Testing

**Metrics to Measure:**
- File upload time (< 30 seconds for 4MB)
- Grid refresh time (< 2 seconds)
- Button enable/disable responsiveness (< 500ms)
- Concurrent user operations (10+ users)

---

## 10. Deployment Plan

### 10.1 Deployment Sequence

1. **Deploy JavaScript Web Resource**
   - File: `sprk_DocumentGridIntegration.js`
   - Publish to Dataverse
   - Add to Document entity form/grid

2. **Update PCF Control**
   - Add custom command buttons
   - Update configuration schema
   - Build and deploy with `pac pcf push`

3. **Configure Document Entity**
   - Add web resource to form libraries
   - Configure custom buttons in ribbon
   - Set configJson property on PCF control

4. **Test in Development Environment**
   - Run integration tests
   - User acceptance testing
   - Performance validation

5. **Deploy to Production**
   - Export solution package
   - Import to production
   - Monitor for errors

### 10.2 Rollback Plan

**If deployment fails:**
1. Remove custom command buttons from PCF control
2. Revert to previous PCF control version
3. Disable JavaScript web resource
4. Communicate to users

**Rollback Trigger:** >5% error rate or critical security issue

---

## 11. Documentation Deliverables

### 11.1 Technical Documentation

- [x] This Technical Specification Document
- [ ] JavaScript API Reference (Phase 2)
- [ ] PCF Control Developer Guide (Phase 2)
- [ ] Deployment Runbook (Phase 6)

### 11.2 User Documentation

- [ ] End User Guide - File Operations (Phase 5)
- [ ] Admin Guide - Configuration (Phase 5)
- [ ] Troubleshooting Guide (Phase 5)

### 11.3 Training Materials

- [ ] Video: Adding Files to Documents (Phase 5)
- [ ] Quick Reference Card (Phase 5)
- [ ] FAQ Document (Phase 5)

---

## 12. Success Criteria

### 12.1 Phase 1 Success Criteria

✅ **All Complete:**
- [x] SDAP API capabilities validated
- [x] Document entity schema verified
- [x] Custom commands defined
- [x] Configuration schema designed
- [x] Data flow diagrams created
- [x] Security considerations documented
- [x] Error handling strategy defined
- [x] Testing strategy planned

### 12.2 Sprint 6 Success Criteria (Overall)

**Functional:**
- [ ] All 4 custom commands working in grid
- [ ] Files upload/download/update/delete successfully
- [ ] SharePoint links clickable and functional
- [ ] Permissions enforced correctly

**Non-Functional:**
- [ ] File operations complete within performance targets
- [ ] Error messages user-friendly
- [ ] Grid refresh smooth and responsive
- [ ] No security vulnerabilities

**User Acceptance:**
- [ ] UAT sign-off from 3+ stakeholders
- [ ] Zero critical bugs in production
- [ ] User satisfaction score > 4/5

---

## 13. Next Steps

### 13.1 Immediate Actions (Phase 2)

1. **Enhance PCF Control** (8 hours)
   - Add custom command buttons
   - Implement configuration parsing
   - Add command execution framework

2. **Create JavaScript Web Resource** (12 hours)
   - Implement 4 file operation functions
   - Add error handling
   - Add progress indicators

### 13.2 Phase Dependencies

- **Phase 2** depends on Phase 1 (this document) ✅ Complete
- **Phase 3** depends on Phase 2 (PCF control enhanced)
- **Phase 4** depends on Phase 3 (JavaScript integration working)
- **Phase 5** depends on Phase 4 (Field updates functional)
- **Phase 6** depends on Phase 5 (Testing complete)

### 13.3 Risk Mitigation

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| File size limits too restrictive | Medium | Medium | Document limit, plan chunked upload for Sprint 7 |
| Authentication complexity | Low | High | Reuse Sprint 2 patterns, test early |
| PCF bundle size exceeds limit | Low | High | Keep using minimal vanilla JS approach |
| CORS issues in production | Low | High | Test in staging environment first |
| Performance degradation | Medium | Medium | Implement caching, optimize API calls |

---

## 14. Approval and Sign-Off

**Phase 1 Status:** ✅ **COMPLETE**

**Reviewed By:**
- Technical Lead: [Pending]
- Security Team: [Pending]
- Product Owner: [Pending]

**Approved to Proceed to Phase 2:** [Pending]

**Date:** October 4, 2025

---

**End of Technical Specification**
