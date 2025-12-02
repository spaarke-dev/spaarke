# Phase 1: Configuration & Planning

**Sprint**: 6 - SDAP + Universal Dataset Grid Integration
**Phase**: 1 of 6
**Duration**: 8 hours (1-2 days)
**Status**: 🚀 READY TO START

---

## Phase Overview

Phase 1 establishes the foundation for integrating SDAP file operations into the Universal Dataset Grid. This phase focuses on design, configuration, and planning to ensure smooth implementation in subsequent phases.

---

## Objectives

1. ✅ Define custom commands for all file operations
2. ✅ Design configuration schema for document grid
3. ✅ Plan JavaScript API integration architecture
4. ✅ Define field mappings and data flow
5. ✅ Create detailed testing plan
6. ✅ Establish success criteria

---

## Task Breakdown

### TASK 1.1: Custom Commands Specification (2 hours)

**Objective**: Define all custom commands for document file operations

#### Commands to Define

**1. Add File Command**
```json
{
  "commandId": "addFile",
  "label": "+ Add File",
  "tooltip": "Upload a new file to SharePoint",
  "icon": "Add",
  "actionType": "javascript",
  "actionHandler": "Spaarke.DocumentGrid.addFile",
  "requiresSelection": false,
  "requiresPermission": "Create",
  "accessRight": "Write",
  "confirmMessage": null,
  "successMessage": "File uploaded successfully",
  "errorMessage": "Failed to upload file"
}
```

**2. Remove File Command**
```json
{
  "commandId": "removeFile",
  "label": "- Remove File",
  "tooltip": "Delete file from SharePoint",
  "icon": "Delete",
  "actionType": "javascript",
  "actionHandler": "Spaarke.DocumentGrid.removeFile",
  "requiresSelection": true,
  "selectionMode": "single",
  "requiresPermission": "Delete",
  "accessRight": "Delete",
  "confirmMessage": "Are you sure you want to delete this file? This action cannot be undone.",
  "successMessage": "File deleted successfully",
  "errorMessage": "Failed to delete file"
}
```

**3. Update File Command**
```json
{
  "commandId": "updateFile",
  "label": "^ Update File",
  "tooltip": "Replace file with a new version",
  "icon": "Upload",
  "actionType": "javascript",
  "actionHandler": "Spaarke.DocumentGrid.updateFile",
  "requiresSelection": true,
  "selectionMode": "single",
  "requiresPermission": "Write",
  "accessRight": "Write",
  "confirmMessage": "Are you sure you want to replace this file?",
  "successMessage": "File updated successfully",
  "errorMessage": "Failed to update file"
}
```

**4. Download File Command**
```json
{
  "commandId": "downloadFile",
  "label": "↓ Download",
  "tooltip": "Download file to your computer",
  "icon": "Download",
  "actionType": "javascript",
  "actionHandler": "Spaarke.DocumentGrid.downloadFile",
  "requiresSelection": true,
  "selectionMode": "single",
  "requiresPermission": "Read",
  "accessRight": "Read",
  "confirmMessage": null,
  "successMessage": "File downloaded successfully",
  "errorMessage": "Failed to download file"
}
```

#### Command Visibility Rules

```javascript
// Command visibility logic
function isCommandVisible(command, context) {
  // Check user permissions
  if (!hasPermission(context.user, command.requiresPermission)) {
    return false;
  }

  // Check access rights on selected record
  if (command.requiresSelection) {
    const selectedRecord = context.selectedRecords[0];
    if (!hasAccessRight(selectedRecord, command.accessRight)) {
      return false;
    }
  }

  // Check selection requirements
  if (command.requiresSelection && context.selectedRecords.length === 0) {
    return false;
  }

  if (command.selectionMode === 'single' && context.selectedRecords.length > 1) {
    return false;
  }

  return true;
}
```

#### Deliverables
- [ ] Complete command specification document
- [ ] Command visibility rules defined
- [ ] Permission mappings documented

---

### TASK 1.2: Configuration Schema Design (2 hours)

**Objective**: Design comprehensive configuration schema for sprk_document entity

#### Full Configuration Schema

```json
{
  "$schema": "https://spaarke.com/schemas/universal-grid-config-v1.json",
  "schemaVersion": "1.0",
  "configName": "Document Management Grid Configuration",
  "configVersion": "1.0.0",
  "lastModified": "2025-10-04",

  "defaultConfig": {
    "viewMode": "Grid",
    "enabledCommands": ["open", "refresh"],
    "compactToolbar": false,
    "enableVirtualization": false,
    "virtualizationThreshold": 50
  },

  "entityConfigs": {
    "sprk_document": {
      "displayName": "Documents",
      "viewMode": "Grid",
      "enabledCommands": ["open", "refresh"],
      "compactToolbar": false,

      "customCommands": {
        "addFile": {
          "label": "+ Add File",
          "tooltip": "Upload a new file to SharePoint",
          "icon": "Add",
          "actionType": "javascript",
          "actionHandler": "Spaarke.DocumentGrid.addFile",
          "requiresSelection": false,
          "requiresPermission": "Create",
          "accessRight": "Write",
          "position": 1,
          "group": "fileOperations"
        },
        "removeFile": {
          "label": "- Remove File",
          "tooltip": "Delete file from SharePoint",
          "icon": "Delete",
          "actionType": "javascript",
          "actionHandler": "Spaarke.DocumentGrid.removeFile",
          "requiresSelection": true,
          "selectionMode": "single",
          "requiresPermission": "Delete",
          "accessRight": "Delete",
          "confirmMessage": "Are you sure you want to delete this file? This action cannot be undone.",
          "position": 2,
          "group": "fileOperations"
        },
        "updateFile": {
          "label": "^ Update File",
          "tooltip": "Replace file with a new version",
          "icon": "Upload",
          "actionType": "javascript",
          "actionHandler": "Spaarke.DocumentGrid.updateFile",
          "requiresSelection": true,
          "selectionMode": "single",
          "requiresPermission": "Write",
          "accessRight": "Write",
          "confirmMessage": "Are you sure you want to replace this file?",
          "position": 3,
          "group": "fileOperations"
        },
        "downloadFile": {
          "label": "↓ Download",
          "tooltip": "Download file to your computer",
          "icon": "Download",
          "actionType": "javascript",
          "actionHandler": "Spaarke.DocumentGrid.downloadFile",
          "requiresSelection": true,
          "selectionMode": "single",
          "requiresPermission": "Read",
          "accessRight": "Read",
          "position": 4,
          "group": "fileOperations"
        }
      },

      "columns": {
        "include": [
          "sprk_name",
          "sprk_filename",
          "sprk_filetype",
          "sprk_filesize",
          "sprk_sharepointurl",
          "sprk_uploadedon",
          "sprk_uploadedby"
        ],
        "customRenderers": {
          "sprk_sharepointurl": {
            "type": "link",
            "label": "Open",
            "target": "_blank",
            "title": "Open file in SharePoint"
          },
          "sprk_filesize": {
            "type": "filesize",
            "format": "human",
            "decimals": 1
          },
          "sprk_uploadedon": {
            "type": "datetime",
            "format": "short"
          }
        }
      },

      "apiIntegration": {
        "baseUrl": "${environment.apiBaseUrl}",
        "endpoints": {
          "upload": "/api/documents/{id}/upload",
          "download": "/api/documents/{id}/download",
          "delete": "/api/documents/{id}/delete",
          "update": "/api/documents/{id}/update"
        },
        "authentication": {
          "type": "obo",
          "tokenSource": "powerplatform"
        },
        "timeout": 300000,
        "maxFileSize": 104857600,
        "allowedFileTypes": [".pdf", ".docx", ".xlsx", ".pptx", ".txt", ".jpg", ".png", ".gif", ".zip"]
      },

      "fieldMappings": {
        "afterUpload": {
          "sprk_filename": "{{file.name}}",
          "sprk_filetype": "{{file.extension}}",
          "sprk_filesize": "{{file.size}}",
          "sprk_sharepointurl": "{{response.sharepointUrl}}",
          "sprk_sharepointfileid": "{{response.fileId}}",
          "sprk_uploadedon": "{{timestamp}}",
          "sprk_uploadedby": "{{currentUser}}"
        },
        "afterUpdate": {
          "sprk_filename": "{{file.name}}",
          "sprk_filesize": "{{file.size}}",
          "sprk_modifiedon": "{{timestamp}}"
        },
        "afterDelete": {
          "sprk_sharepointurl": null,
          "sprk_sharepointfileid": null
        }
      },

      "userFeedback": {
        "showProgressIndicator": true,
        "progressMessages": {
          "upload": "Uploading file...",
          "download": "Downloading file...",
          "delete": "Deleting file...",
          "update": "Updating file..."
        },
        "showToastNotifications": true,
        "enableErrorLogging": true
      }
    }
  }
}
```

#### Configuration Storage Options

**Option A: Control Property** (Recommended for Phase 1)
- Store configuration in PCF control's `configJson` property
- Easy to deploy and modify
- Doesn't require additional Dataverse entities

**Option B: Environment Variable**
- Store as `spaarke_DocumentGridConfig` environment variable
- Centralized configuration
- Requires admin to update

**Option C: Custom Configuration Entity**
- Create `sprk_gridconfiguration` entity
- Most flexible
- Can have entity-specific configs
- Requires additional development

**Decision**: Use Option A for Phase 1, plan Option C for future

#### Deliverables
- [ ] Complete configuration schema documented
- [ ] Storage option selected and documented
- [ ] Sample configuration JSON created

---

### TASK 1.3: JavaScript API Integration Design (2 hours)

**Objective**: Design the architecture for JavaScript-API-PCF integration

#### Integration Architecture

```
User Action (Click Button)
        ↓
PCF Control (Minimal Version)
        ↓
JavaScript Call: window.Spaarke.DocumentGrid.addFile(context, recordId)
        ↓
JavaScript Web Resource (sprk_DocumentGridIntegration.js)
        ├─→ Show file picker
        ├─→ Validate file
        ├─→ Call SDAP API (fetch)
        ├─→ Handle response
        └─→ Update Dataverse record (Xrm.WebApi)
        ↓
Notify PCF Control (Custom Event)
        ↓
PCF Control Refreshes Grid
```

#### JavaScript Web Resource Structure

```javascript
// File: sprk_DocumentGridIntegration.js

(function() {
  "use strict";

  // Namespace
  window.Spaarke = window.Spaarke || {};
  window.Spaarke.DocumentGrid = {};

  // Configuration
  const config = {
    apiBaseUrl: null, // Set from environment
    maxFileSize: 100 * 1024 * 1024, // 100MB
    allowedTypes: ['.pdf', '.docx', '.xlsx', '.pptx', '.txt', '.jpg', '.png', '.gif'],
    timeout: 300000 // 5 minutes
  };

  // Initialize
  Spaarke.DocumentGrid.init = function() {
    // Get API URL from environment
    const globalContext = Xrm.Utility.getGlobalContext();
    const clientUrl = globalContext.getClientUrl();

    if (clientUrl.includes('crm.dynamics.com')) {
      config.apiBaseUrl = "https://api.spaarke.com"; // Production
    } else {
      config.apiBaseUrl = "https://localhost:7034"; // Dev
    }

    console.log("Spaarke.DocumentGrid initialized");
  };

  // Add File
  Spaarke.DocumentGrid.addFile = async function(context, recordId) {
    try {
      // 1. Show file picker
      const file = await showFilePicker();

      // 2. Validate file
      validateFile(file);

      // 3. Show progress
      showProgress("Uploading file...");

      // 4. Upload to SDAP API
      const response = await uploadFile(recordId, file);

      // 5. Update Dataverse record
      await updateRecord(recordId, {
        sprk_filename: file.name,
        sprk_filetype: getFileExtension(file.name),
        sprk_filesize: file.size,
        sprk_sharepointurl: response.sharepointUrl,
        sprk_sharepointfileid: response.fileId
      });

      // 6. Notify success
      hideProgress();
      showSuccess("File uploaded successfully");

      // 7. Trigger grid refresh
      triggerGridRefresh(context);

    } catch (error) {
      hideProgress();
      showError("Failed to upload file: " + error.message);
    }
  };

  // Remove File
  Spaarke.DocumentGrid.removeFile = async function(context, recordId) {
    try {
      // 1. Confirm deletion
      const confirmed = await confirm(
        "Are you sure you want to delete this file? This action cannot be undone."
      );
      if (!confirmed) return;

      // 2. Show progress
      showProgress("Deleting file...");

      // 3. Delete from SDAP API
      await deleteFile(recordId);

      // 4. Clear Dataverse fields
      await updateRecord(recordId, {
        sprk_sharepointurl: null,
        sprk_sharepointfileid: null
      });

      // 5. Notify success
      hideProgress();
      showSuccess("File deleted successfully");

      // 6. Trigger grid refresh
      triggerGridRefresh(context);

    } catch (error) {
      hideProgress();
      showError("Failed to delete file: " + error.message);
    }
  };

  // Update File
  Spaarke.DocumentGrid.updateFile = async function(context, recordId) {
    // Similar to addFile but calls update endpoint
  };

  // Download File
  Spaarke.DocumentGrid.downloadFile = async function(context, recordId) {
    // Calls download endpoint and triggers browser download
  };

  // Helper Functions
  async function showFilePicker() {
    return new Promise((resolve, reject) => {
      const input = document.createElement('input');
      input.type = 'file';
      input.accept = config.allowedTypes.join(',');
      input.onchange = (e) => {
        const file = e.target.files[0];
        if (file) resolve(file);
        else reject(new Error("No file selected"));
      };
      input.click();
    });
  }

  function validateFile(file) {
    if (file.size > config.maxFileSize) {
      throw new Error(`File size exceeds maximum allowed size of ${config.maxFileSize / 1024 / 1024}MB`);
    }

    const ext = getFileExtension(file.name);
    if (!config.allowedTypes.includes(ext)) {
      throw new Error(`File type ${ext} is not allowed`);
    }
  }

  async function uploadFile(recordId, file) {
    const formData = new FormData();
    formData.append('file', file);

    const response = await fetch(`${config.apiBaseUrl}/api/documents/${recordId}/upload`, {
      method: 'POST',
      body: formData,
      headers: {
        'Authorization': 'Bearer ' + await getToken()
      }
    });

    if (!response.ok) {
      throw new Error(`Upload failed: ${response.statusText}`);
    }

    return await response.json();
  }

  async function updateRecord(recordId, fields) {
    return await Xrm.WebApi.updateRecord("sprk_document", recordId, fields);
  }

  function triggerGridRefresh(context) {
    // Trigger custom event to notify PCF control
    const event = new CustomEvent('spaarke:grid:refresh', {
      detail: { entityName: 'sprk_document' }
    });
    window.dispatchEvent(event);

    // Also try to refresh the grid directly if context available
    if (context && context.refresh) {
      context.refresh();
    }
  }

  function showProgress(message) {
    Xrm.Utility.showProgressIndicator(message);
  }

  function hideProgress() {
    Xrm.Utility.closeProgressIndicator();
  }

  function showSuccess(message) {
    Xrm.Navigation.openAlertDialog({ text: message });
  }

  function showError(message) {
    Xrm.Navigation.openErrorDialog({ message: message });
  }

  async function confirm(message) {
    const result = await Xrm.Navigation.openConfirmDialog({ text: message });
    return result.confirmed;
  }

  function getFileExtension(filename) {
    return '.' + filename.split('.').pop().toLowerCase();
  }

  async function getToken() {
    // Get OBO token from Power Platform
    // Implementation depends on authentication setup
    return "dummy-token"; // Placeholder
  }

  // Auto-initialize
  if (typeof Xrm !== 'undefined') {
    Spaarke.DocumentGrid.init();
  }
})();
```

#### PCF Control Integration Points

**Method 1: Direct Function Call** (Simplest)
```javascript
// In PCF control's minimal version (index.ts)
private executeCustomCommand(commandKey: string, context: any): void {
  const selectedIds = this.selectedRecordIds;
  const recordId = selectedIds.length > 0 ? selectedIds[0] : null;

  // Call JavaScript function
  if (window.Spaarke && window.Spaarke.DocumentGrid) {
    switch(commandKey) {
      case 'addFile':
        window.Spaarke.DocumentGrid.addFile(this.context, recordId);
        break;
      case 'removeFile':
        window.Spaarke.DocumentGrid.removeFile(this.context, recordId);
        break;
      case 'updateFile':
        window.Spaarke.DocumentGrid.updateFile(this.context, recordId);
        break;
      case 'downloadFile':
        window.Spaarke.DocumentGrid.downloadFile(this.context, recordId);
        break;
    }
  }
}
```

**Method 2: Custom Events** (More decoupled)
```javascript
// PCF dispatches event
const event = new CustomEvent('spaarke:command:execute', {
  detail: {
    commandKey: 'addFile',
    recordId: recordId,
    context: this.context
  }
});
window.dispatchEvent(event);

// JavaScript listens for event
window.addEventListener('spaarke:command:execute', async (event) => {
  const { commandKey, recordId, context } = event.detail;
  await Spaarke.DocumentGrid[commandKey](context, recordId);
});
```

**Decision**: Use Method 1 for simplicity

#### Deliverables
- [ ] Integration architecture diagram
- [ ] JavaScript web resource structure defined
- [ ] PCF integration method selected
- [ ] Communication protocol documented

---

### TASK 1.4: Field Mappings & Data Flow (1 hour)

**Objective**: Document all field mappings and data flow for file operations

#### sprk_document Entity Fields

| Field Name | Type | Purpose | Populated By | When |
|------------|------|---------|--------------|------|
| `sprk_documentid` | GUID | Primary key | Dataverse | On create |
| `sprk_name` | String | Document name | User | On create |
| `sprk_description` | String | Description | User | On create |
| `sprk_filename` | String | File name | JavaScript | After upload |
| `sprk_filetype` | String | Extension | JavaScript | After upload |
| `sprk_filesize` | Integer | Size in bytes | JavaScript | After upload |
| `sprk_sharepointurl` | String (URL) | SharePoint link | SDAP API | After upload |
| `sprk_sharepointfileid` | String | SPE file ID | SDAP API | After upload |
| `sprk_containerid` | Lookup | Container | User/System | On create |
| `sprk_uploadedon` | DateTime | Upload timestamp | JavaScript | After upload |
| `sprk_uploadedby` | Lookup | User who uploaded | JavaScript | After upload |
| `modifiedon` | DateTime | Last modified | Dataverse | On update |

#### Data Flow Diagrams

**Upload Flow**:
```
1. User clicks "+ Add File"
2. JavaScript shows file picker
3. User selects file
4. JavaScript validates file (size, type)
5. JavaScript calls: POST /api/documents/{id}/upload
   ├─ SDAP API receives file
   ├─ SDAP API uploads to SharePoint Embedded (Graph API)
   ├─ SDAP API gets SharePoint URL and File ID
   └─ SDAP API returns response: { sharepointUrl, fileId }
6. JavaScript updates Dataverse record:
   ├─ sprk_filename = file.name
   ├─ sprk_filetype = file.extension
   ├─ sprk_filesize = file.size
   ├─ sprk_sharepointurl = response.sharepointUrl
   └─ sprk_sharepointfileid = response.fileId
7. JavaScript triggers grid refresh
8. User sees updated record with clickable link
```

**Download Flow**:
```
1. User selects record and clicks "Download"
2. JavaScript calls: GET /api/documents/{id}/download
3. SDAP API retrieves file from SharePoint Embedded
4. SDAP API streams file to JavaScript
5. JavaScript triggers browser download
6. File saved to user's Downloads folder
```

**Delete Flow**:
```
1. User selects record and clicks "- Remove File"
2. JavaScript shows confirmation dialog
3. User confirms
4. JavaScript calls: DELETE /api/documents/{id}/delete
5. SDAP API deletes file from SharePoint Embedded
6. JavaScript updates Dataverse record:
   ├─ sprk_sharepointurl = null
   └─ sprk_sharepointfileid = null
7. JavaScript triggers grid refresh
8. User sees record with no file link
```

**Update Flow**:
```
1. User selects record and clicks "^ Update File"
2. JavaScript shows file picker
3. User selects new file
4. JavaScript confirms replacement
5. JavaScript calls: PUT /api/documents/{id}/update
6. SDAP API replaces file in SharePoint Embedded
7. SDAP API returns new file metadata
8. JavaScript updates Dataverse record (similar to upload)
9. JavaScript triggers grid refresh
10. User sees updated file information
```

#### Deliverables
- [ ] Field mappings table completed
- [ ] Data flow diagrams created
- [ ] Update logic documented

---

### TASK 1.5: Testing Plan (1 hour)

**Objective**: Create comprehensive testing plan for all phases

#### Test Categories

**1. Unit Tests** (Phase 2-3)
- Custom command parsing
- Configuration validation
- JavaScript helper functions
- Field mapping logic

**2. Integration Tests** (Phase 4)
- PCF → JavaScript communication
- JavaScript → SDAP API calls
- API → SharePoint operations
- Dataverse field updates

**3. End-to-End Tests** (Phase 5)
- Complete upload workflow
- Complete download workflow
- Complete delete workflow
- Complete update workflow

**4. Permission Tests** (Phase 5)
- Command visibility based on permissions
- API authorization checks
- Field-level security
- Row-level security

**5. Error Scenario Tests** (Phase 5)
- File too large
- Invalid file type
- Network error
- API timeout
- SharePoint unavailable
- Permission denied

**6. Performance Tests** (Phase 5)
- Large file upload (90MB)
- Multiple simultaneous operations
- Grid with 100+ records
- Slow network simulation

**7. Browser Compatibility Tests** (Phase 5)
- Edge (latest)
- Chrome (latest)
- Firefox (latest)

**8. User Acceptance Tests** (Phase 5-6)
- Real user workflows
- Feedback collection
- Usability testing

#### Test Data Requirements
- [ ] Test container in SharePoint Embedded
- [ ] 10+ test document records
- [ ] Test files (various sizes: 1KB, 1MB, 10MB, 50MB)
- [ ] Test users with different permissions
- [ ] Test files (various types: PDF, DOCX, XLSX, JPG, PNG)

#### Success Criteria Per Operation
- **Upload**: File appears in SharePoint, all fields populated, link clickable
- **Download**: File downloads correctly, same size/content as original
- **Delete**: File removed from SharePoint, fields cleared
- **Update**: File replaced, metadata updated, old version gone

#### Deliverables
- [ ] Complete testing plan
- [ ] Test data requirements list
- [ ] Success criteria defined

---

### TASK 1.6: Documentation & Handoff (1 hour)

**Objective**: Document all Phase 1 decisions and prepare for Phase 2

#### Documentation to Create

**1. Phase 1 Summary**
- All decisions made
- Chosen approaches
- Rationale for decisions
- Any deviations from original plan

**2. Phase 2 Task Breakdown**
- Detailed tasks for PCF control enhancement
- Acceptance criteria for each task
- Estimated time for each task

**3. Risk Register**
- Identified risks
- Mitigation strategies
- Contingency plans

**4. Dependencies**
- SDAP API endpoints required
- Dataverse fields required
- Permissions required
- Test environment requirements

#### Deliverables
- [ ] Phase 1 summary document
- [ ] Phase 2 task breakdown
- [ ] Risk register
- [ ] Dependencies checklist

---

## Phase 1 Deliverables Summary

### Documents
1. ✅ Custom Commands Specification
2. ✅ Configuration Schema Design
3. ✅ JavaScript API Integration Design
4. ✅ Field Mappings & Data Flow Documentation
5. ✅ Comprehensive Testing Plan
6. ✅ Phase 1 Summary Report
7. ✅ Phase 2 Task Breakdown

### Decisions Made
1. ✅ Command structure and metadata
2. ✅ Configuration storage method
3. ✅ JavaScript integration approach
4. ✅ Field mapping strategy
5. ✅ Testing approach

### Artifacts
1. ✅ Configuration JSON sample
2. ✅ JavaScript structure outline
3. ✅ Data flow diagrams
4. ✅ Test plan template

---

## Success Criteria

### Phase 1 Complete When:
- [ ] All 6 tasks completed
- [ ] All deliverables created
- [ ] All decisions documented
- [ ] Phase 2 tasks defined
- [ ] Stakeholder approval received

### Quality Gates
- [ ] Configuration schema validated against real `sprk_document` entity
- [ ] JavaScript design reviewed for security and performance
- [ ] Testing plan covers all critical scenarios
- [ ] No blocking issues identified

---

## Timeline

| Task | Duration | Dependencies |
|------|----------|--------------|
| 1.1 - Custom Commands | 2 hours | None |
| 1.2 - Configuration Schema | 2 hours | Task 1.1 |
| 1.3 - JavaScript Integration | 2 hours | Task 1.1 |
| 1.4 - Field Mappings | 1 hour | Task 1.2, 1.3 |
| 1.5 - Testing Plan | 1 hour | All above |
| 1.6 - Documentation | 1 hour | All above |
| **Total** | **9 hours** | - |

*(Includes 1 hour buffer)*

---

## Next Steps

After Phase 1 completion:
1. Review all documentation
2. Get stakeholder approval
3. Proceed to Phase 2: Enhanced Universal Grid

---

**Status**: Ready to begin
**First Task**: TASK 1.1 - Custom Commands Specification
**Estimated Start**: Immediately upon approval
