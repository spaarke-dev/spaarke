# Architecture: Universal Quick Create Custom Page

**Sprint Goal:** Replace Model-Driven App Main Form with Custom Page for document upload UI

**Current Version:** 2.3.0 (Phase 7 - Dynamic Metadata Discovery Complete)
**Target Version:** 3.0.0 (Custom Page Migration)

---

## Executive Summary

This sprint migrates the Universal Document Upload functionality from a **Model-Driven App Quick Create form** to a **Custom Page** dialog, providing a modern, consistent UI experience while maintaining all existing SDAP (SharePoint Document Architecture Platform) capabilities.

**Current State:** Document upload uses Quick Create form (model-driven form with limited UI control)
**Target State:** Document upload uses Custom Page with embedded PCF control (full UI control)

---

## Table of Contents

1. [System Overview](#system-overview)
2. [Current Architecture (v2.3.0)](#current-architecture-v230)
3. [Target Architecture (v3.0.0)](#target-architecture-v300)
4. [Migration Strategy](#migration-strategy)
5. [Component Architecture](#component-architecture)
6. [Data Flow](#data-flow)
7. [Authentication & Security](#authentication--security)
8. [Error Handling](#error-handling)
9. [Deployment](#deployment)
10. [Testing Strategy](#testing-strategy)

---

## System Overview

The Universal Document Upload system enables users to upload multiple files to SharePoint Embedded (SPE) and create corresponding Document records in Dataverse, linked to any parent entity type (Matter, Project, Invoice, Account, Contact).

### Key Features

- **Multi-file upload** (up to 10 files per batch)
- **Multi-entity support** (Matter, Project, Invoice, Account, Contact)
- **Phase 7: Dynamic metadata discovery** (automatic navigation property detection)
- **15-minute caching** (reduces BFF API calls by 88%)
- **Real-time progress tracking**
- **Comprehensive error handling**

### SDAP Architecture Integration

This component is part of the larger SDAP ecosystem:

```
┌──────────────────────────────────────────────────────────────────┐
│                    SDAP Ecosystem                                 │
│                                                                    │
│  ┌──────────────────────┐     ┌───────────────────────┐          │
│  │ UniversalQuickCreate │     │ UniversalDatasetGrid  │          │
│  │ (THIS COMPONENT)     │     │ (Dataset Grid PCF)    │          │
│  └──────────────────────┘     └───────────────────────┘          │
│            │                             │                        │
│            └──────────────┬──────────────┘                        │
│                           │                                       │
│                    ┌──────▼──────┐                                │
│                    │  BFF API    │ ← Phase 7: NavMapEndpoints    │
│                    │  (OBO Flow) │                                │
│                    └──────┬──────┘                                │
│                           │                                       │
│            ┌──────────────┼──────────────┐                       │
│            │              │              │                        │
│      ┌─────▼─────┐  ┌────▼────┐  ┌──────▼──────┐                │
│      │ Microsoft │  │ Azure   │  │ Dataverse   │                │
│      │ Graph     │  │ Redis   │  │ (Metadata)  │                │
│      │ (SPE)     │  │ (Cache) │  │             │                │
│      └───────────┘  └─────────┘  └─────────────┘                │
└──────────────────────────────────────────────────────────────────┘
```

**See:** [SDAP-ARCHITECTURE-GUIDE.md](../../docs/SDAP-ARCHITECTURE-GUIDE.md) for complete SDAP architecture

---

## Current Architecture (v2.3.0)

### Current User Experience

```
User clicks "New Document" button in Documents subgrid
  ↓
Model-Driven App Quick Create form opens (side panel)
  ↓
User fills form fields:
  - Document Name (text)
  - Description (multi-line)
  - File Upload (custom PCF control)
  ↓
User clicks "Save" (Quick Create form button)
  ↓
⚠️ LIMITATION: Form validation required before PCF control can execute
  ↓
Documents created in Dataverse
```

### Current Limitations (Why We're Migrating)

1. **Limited UI Control**
   - Quick Create form enforces model-driven styling
   - Cannot customize layout freely
   - Limited Fluent UI v9 integration

2. **Form Dependency**
   - PCF control execution tied to form save event
   - Cannot control upload flow independently
   - Business rules/validations interfere with upload logic

3. **User Experience Issues**
   - Side panel feels disconnected
   - Progress tracking limited by form chrome
   - Cancel/close behavior constrained by form lifecycle

4. **Deployment Complexity**
   - Must manage both form and PCF control versions
   - Form security roles separate from PCF security
   - Multiple deployment artifacts

---

## Target Architecture (v3.0.0)

### Target User Experience

```
User clicks "New Document" button in Documents subgrid
  ↓
Xrm.Navigation.navigateTo(customPage, {
  parentEntityName, parentRecordId, containerId
}) → Opens Custom Page as dialog
  ↓
Custom Page dialog opens (modal, centered)
  ↓
Embedded PCF control renders immediately (no form)
  ↓
User selects files and clicks "Upload & Create"
  ↓
PCF control handles entire workflow:
  - Phase 1: Upload files to SPE (parallel)
  - Phase 2: Create Dataverse records (sequential)
  ↓
Dialog closes, subgrid refreshes automatically
```

### Benefits of Custom Page Approach

1. **Full UI Control**
   - Custom layout using Fluent UI v9
   - Modern dialog experience
   - Complete control over progress tracking

2. **Independent Execution**
   - PCF control owns entire workflow
   - No form save dependency
   - Simpler state management

3. **Better User Experience**
   - Modal dialog with clear focus
   - Real-time progress updates
   - Intuitive cancel/close behavior

4. **Simplified Deployment**
   - Single solution artifact (Custom Page + PCF)
   - Unified security model
   - Easier version management

---

## High-Level Architecture Diagram

### Target Architecture (v3.0.0)

```
┌──────────────────────────────────────────────────────────────────┐
│                  Parent Entity Form                               │
│               (Matter / Project / Invoice / etc.)                 │
│                                                                    │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  Documents Subgrid (UniversalDatasetGrid PCF)              │  │
│  │  ┌──────────────────────────────────────────────────────┐  │  │
│  │  │  [+ Upload Documents] ← Command Button              │  │  │
│  │  └──────────────────────────────────────────────────────┘  │  │
│  │  ┌──────────────────────────────────────────────────────┐  │  │
│  │  │  Document 1.pdf       📄                             │  │  │
│  │  │  Document 2.docx      📄                             │  │  │
│  │  │  Document 3.xlsx      📄                             │  │  │
│  │  └──────────────────────────────────────────────────────┘  │  │
│  └────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────┘
                            ↓ (User clicks button)
                            ↓
        Xrm.Navigation.navigateTo(customPage, dialogOptions)
                            ↓
┌──────────────────────────────────────────────────────────────────┐
│        Custom Page Dialog: sprk_DocumentUploadDialog             │
│        (Modal, Centered, 800px width, auto height)               │
│                                                                    │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  PCF Control: UniversalQuickCreate                          │  │
│  │                                                              │  │
│  │  Input Parameters (passed via navigation):                  │  │
│  │  • parentEntityName: "sprk_matter"                          │  │
│  │  • parentRecordId: "{GUID}"                                 │  │
│  │  • containerId: "{SPE-CONTAINER-ID}"                        │  │
│  │  • parentDisplayName: "Matter #12345"                       │  │
│  │  • sdapApiBaseUrl: "https://spe-api-dev.azurewebsites.net" │  │
│  │                                                              │  │
│  │  ┌──────────────────────────────────────────────────────┐  │  │
│  │  │  Upload Documents                                     │  │  │
│  │  │  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  │  │  │
│  │  │                                                        │  │  │
│  │  │  📁 Select Files                                      │  │  │
│  │  │  [Browse...] or drag files here                       │  │  │
│  │  │  Maximum 10 files, 10MB each, 100MB total            │  │  │
│  │  │                                                        │  │  │
│  │  ├────────────────────────────────────────────────────────┤  │  │
│  │  │  📄 document1.pdf (2.5 MB)                [✕]         │  │  │
│  │  │  📄 document2.docx (1.2 MB)               [✕]         │  │  │
│  │  │  📄 document3.xlsx (0.8 MB)               [✕]         │  │  │
│  │  ├────────────────────────────────────────────────────────┤  │  │
│  │  │  Document Description (optional)                       │  │  │
│  │  │  ┌──────────────────────────────────────────────────┐ │  │  │
│  │  │  │ Supporting documents for Matter #12345           │ │  │  │
│  │  │  └──────────────────────────────────────────────────┘ │  │  │
│  │  ├────────────────────────────────────────────────────────┤  │  │
│  │  │  ⏳ Uploading: 2 of 3 files                           │  │  │
│  │  │  ████████████░░░░░░░░░░░░░░░░░░ 67%                  │  │  │
│  │  │  ✓ document1.pdf uploaded                            │  │  │
│  │  │  ✓ document2.docx uploaded                           │  │  │
│  │  │  ⏳ document3.xlsx uploading...                       │  │  │
│  │  └────────────────────────────────────────────────────────┘  │  │
│  │                                                              │  │
│  │  [Upload & Create]  [Cancel]                                │  │
│  │                                                              │  │
│  │  v3.0.0.0 - Custom Page Mode                                │  │
│  └────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────┘
                            ↓ (Upload workflow begins)
                            ↓
                  ┌───────────────────────┐
                  │  Phase 1: Upload      │
                  │  Files to SPE         │
                  │  (Parallel)           │
                  └───────────────────────┘
                            ↓
            SdapApiClient.uploadFile() × 3 (parallel)
                            ↓
            MsalAuthProvider → BFF API (OAuth2 OBO)
                            ↓
                  Microsoft Graph API
                            ↓
          SharePoint Embedded Container
        (Files stored, metadata returned)
                            ↓
                  ┌───────────────────────┐
                  │  Phase 2: Create      │
                  │  Dataverse Records    │
                  │  (Sequential)         │
                  └───────────────────────┘
                            ↓
    Phase 7: NavMapClient.getLookupNavigation() → BFF API
      → Dataverse EntityDefinitions (cached 15min)
      → Returns { navigationPropertyName: "sprk_Matter" }
                            ↓
    context.webAPI.createRecord("sprk_document", payload) × 3
      payload includes: @odata.bind with correct case
                            ↓
              Dataverse Web API
    (Document records created with parent lookup)
                            ↓
                  ┌───────────────────────┐
                  │  Dialog Closes        │
                  │  Subgrid Refreshes    │
                  └───────────────────────┘
```

---

## Component Architecture

### Layer 1: Custom Page (NEW for v3.0.0)

**File:** `sprk_DocumentUploadDialog.json`

```json
{
  "schemaVersion": "1.0.0.0",
  "name": "sprk_documentuploaddialog",
  "displayName": "Document Upload",
  "description": "Custom page for uploading documents to SharePoint Embedded",
  "type": "Dialog",
  "components": [
    {
      "id": "uploadControl",
      "name": "UniversalQuickCreate",
      "type": "pcf",
      "control": "sprk_Spaarke.Controls.UniversalQuickCreate",
      "properties": {
        "parentEntityName": "{Input.parentEntityName}",
        "parentRecordId": "{Input.parentRecordId}",
        "containerId": "{Input.containerId}",
        "parentDisplayName": "{Input.parentDisplayName}",
        "sdapApiBaseUrl": "https://spe-api-dev-67e2xz.azurewebsites.net"
      }
    }
  ],
  "onClose": {
    "action": "RefreshParent"
  }
}
```

**Navigation Code** (in ribbon button JavaScript):

```javascript
function openDocumentUploadDialog(primaryControl, selected) {
    const formContext = primaryControl;
    const entityName = formContext.data.entity.getEntityName();
    const recordId = formContext.data.entity.getId().replace(/[{}]/g, '');
    const containerId = formContext.getAttribute('sprk_containerid').getValue();

    // Get display name (entity-specific field)
    const displayNameField = getDisplayNameField(entityName);
    const displayName = formContext.getAttribute(displayNameField).getValue();

    // Navigation options
    const pageInput = {
        pageType: 'custom',
        name: 'sprk_documentuploaddialog',
        data: {
            parentEntityName: entityName,
            parentRecordId: recordId,
            containerId: containerId,
            parentDisplayName: displayName
        }
    };

    const navigationOptions = {
        target: 2,  // Dialog
        position: 1, // Center
        width: { value: 800, unit: 'px' },
        height: { value: 600, unit: 'px' }
    };

    Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
        function success() {
            // Dialog closed - refresh subgrid
            formContext.getControl('DocumentsGrid').refresh();
        },
        function error(err) {
            console.error('Failed to open dialog:', err);
        }
    );
}
```

---

### Layer 2: PCF Control Logic (EXISTING - Minimal Changes)

**File:** [UniversalQuickCreate/index.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts)

**Key Change for v3.0.0:** Remove form save dependency, add direct workflow execution

```typescript
export class UniversalQuickCreate implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    // Input Parameters (from Custom Page)
    private parentEntityName: string;
    private parentRecordId: string;
    private containerId: string;
    private parentDisplayName: string;
    private sdapApiBaseUrl: string;

    // Services (Phase 7)
    private fileUploadService: FileUploadService;
    private multiFileService: MultiFileUploadService;
    private documentRecordService: DocumentRecordService;
    private navMapClient: NavMapClient;  // Phase 7: Dynamic metadata
    private msalAuthProvider: MsalAuthProvider;

    // State
    private selectedFiles: File[];
    private uploadProgress: UploadProgress;
    private isUploading: boolean;

    public init(context: ComponentFramework.Context<IInputs>): void {
        // Read input parameters from Custom Page
        this.parentEntityName = context.parameters.parentEntityName.raw || '';
        this.parentRecordId = context.parameters.parentRecordId.raw || '';
        this.containerId = context.parameters.containerId.raw || '';
        this.parentDisplayName = context.parameters.parentDisplayName.raw || '';
        this.sdapApiBaseUrl = context.parameters.sdapApiBaseUrl.raw || '';

        // Initialize services (including Phase 7 NavMapClient)
        this.initializeServices(context);

        // Render React UI
        this.renderUI(context);
    }

    /**
     * Main upload workflow (triggered by "Upload & Create" button)
     * NO LONGER depends on form save - fully autonomous
     */
    private async handleUploadAndCreate(files: File[], description: string): Promise<void> {
        this.isUploading = true;
        this.notifyOutputChanged();

        try {
            // Phase 1: Upload files to SPE (parallel)
            const uploadResults = await this.multiFileService.uploadFiles(files, this.containerId);

            if (uploadResults.allFailed) {
                throw new Error('All file uploads failed');
            }

            // Phase 2: Create Dataverse records (sequential, Phase 7 metadata)
            const parentContext: ParentContext = {
                parentEntityName: this.parentEntityName,
                parentRecordId: this.parentRecordId,
                containerId: this.containerId,
                parentDisplayName: this.parentDisplayName
            };

            const formData: FormData = {
                description: description || null
            };

            const createResults = await this.documentRecordService.createDocuments(
                uploadResults.successfulUploads,
                parentContext,
                formData
            );

            // Show summary
            this.showResultSummary(createResults);

            // Close dialog if all successful
            if (createResults.every(r => r.success)) {
                this.closeDialog();
            }

        } catch (error) {
            this.showError('Upload failed', error);
        } finally {
            this.isUploading = false;
            this.notifyOutputChanged();
        }
    }

    /**
     * Close Custom Page dialog programmatically
     */
    private closeDialog(): void {
        // Custom Page API to close dialog
        if (this.context.navigation && this.context.navigation.close) {
            this.context.navigation.close();
        }
    }
}
```

---

### Layer 3: Business Logic Services (EXISTING - No Changes)

All service layer code remains **unchanged** from v2.3.0:

#### Phase 7 Services (Dynamic Metadata Discovery)

1. **[NavMapClient.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/services/NavMapClient.ts)**
   - Queries BFF API `/api/navmap/{childEntity}/{relationship}/lookup`
   - Returns `{ navigationPropertyName: "sprk_Matter" }` (correct case!)
   - 15-minute Redis cache (88% API call reduction)

2. **[DocumentRecordService.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DocumentRecordService.ts)**
   - Creates Document records using `context.webAPI.createRecord()`
   - Calls `NavMapClient.getLookupNavigation()` for each entity type
   - Uses dynamic navigation property in `@odata.bind` syntax

#### File Upload Services

3. **[MultiFileUploadService.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/services/MultiFileUploadService.ts)**
   - Orchestrates parallel file uploads
   - Tracks progress across multiple files
   - Handles partial failures

4. **[FileUploadService.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/services/FileUploadService.ts)**
   - Single file upload to SPE via BFF API
   - Uses `SdapApiClient`

5. **[SdapApiClient.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/services/SdapApiClient.ts)**
   - HTTP client for BFF API
   - OAuth token injection via MSAL

6. **[MsalAuthProvider.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/services/MsalAuthProvider.ts)**
   - User authentication (MSAL.js)
   - Token acquisition for BFF API scope

**See:** [SDAP-ARCHITECTURE-GUIDE.md § Component Architecture](../../docs/SDAP-ARCHITECTURE-GUIDE.md#component-architecture) for service details

---

### Layer 4: Configuration & Types (EXISTING - No Changes)

#### [EntityDocumentConfig.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts)

Configuration-driven entity support (Phase 7):

```typescript
export interface EntityDocumentConfig {
    entityName: string;              // 'sprk_matter'
    lookupFieldName: string;         // 'sprk_matter' (on Document)
    relationshipSchemaName: string;  // 'sprk_matter_document' (for metadata query)
    containerIdField: string;        // 'sprk_containerid' (on parent)
    displayNameField: string;        // 'sprk_matternumber' (on parent)
    entitySetName: string;           // 'sprk_matters' (OData)
}

export const ENTITY_DOCUMENT_CONFIGS: Record<string, EntityDocumentConfig> = {
    'sprk_matter': { /* ... */ },
    'sprk_project': { /* ... */ },
    'sprk_invoice': { /* ... */ },
    'account': { /* ... */ },
    'contact': { /* ... */ }
};
```

**Phase 7 Benefit:** Only `relationshipSchemaName` is needed per entity. Navigation property name is discovered dynamically!

---

## Data Flow

### Phase 1: File Upload to SharePoint Embedded

**Flow remains unchanged from v2.3.0:**

```typescript
User selects files in Custom Page UI
  → FileSelectionField validates (size, type, count)
  → User clicks "Upload & Create"
  → handleUploadAndCreate() triggered

// Parallel upload
files.forEach(file =>
  MultiFileUploadService.uploadFiles()
    → FileUploadService.uploadFile(file)
      → SdapApiClient.uploadFile()
        → MsalAuthProvider.getAccessToken(['api://BFF-APP-ID/user_impersonation'])
        → fetch("PUT /api/obo/containers/{id}/files/{name}")
          → BFF API (OAuth2 OBO flow)
            → Microsoft Graph API
              → SharePoint Embedded
)

// Results
Promise.allSettled(uploads) → { successfulUploads, failedUploads }
```

**Performance:** ~5-10 seconds for 100MB total (network dependent)

---

### Phase 2: Create Dataverse Records (Phase 7 Metadata Discovery)

**Phase 7 enhancement - dynamic navigation property lookup:**

```typescript
// Sequential creation (one at a time for error handling)
uploadedFiles.forEach(async file => {

  // PHASE 7: Query navigation metadata dynamically
  const navMetadata = await NavMapClient.getLookupNavigation(
    'sprk_document',                    // childEntity
    config.relationshipSchemaName       // e.g., "sprk_matter_document"
  );

  // Returns (with 15-min cache):
  {
    navigationPropertyName: "sprk_Matter",  // ⚠️ CAPITAL M - discovered!
    targetEntity: "sprk_matter",
    source: "cache"  // or "metadata_query" on first call
  }

  // Build payload with CORRECT case
  const payload = {
    sprk_documentname: file.name,
    sprk_filename: file.name,
    sprk_graphdriveid: containerId,
    sprk_graphitemid: file.id,
    sprk_filesize: file.size,
    sprk_documentdescription: formData.description,

    // CRITICAL: Uses discovered navigation property (case-sensitive!)
    [`${navMetadata.navigationPropertyName}@odata.bind`]:
      `/${navMetadata.targetEntity}s(${parentRecordId})`
    // Example: "sprk_Matter@odata.bind": "/sprk_matters(guid)"
  };

  // Create record
  await context.webAPI.createRecord('sprk_document', payload);
});

// Results
CreateResult[] (success/failure per file)
```

**Performance:** ~1-2 seconds per record (sequential) = 10-20 seconds for 10 files

**Caching Impact:**
- First upload: Queries BFF API → Dataverse metadata
- Subsequent uploads (15 min): Uses Redis cache (88% faster!)

**See:** [PHASE-7-DEPLOYMENT-STATUS.md](../../docs/PHASE-7-DEPLOYMENT-STATUS.md) for Phase 7 details

---

## Entity Relationship Diagram

**No changes from v2.3.0** - same Dataverse schema:

```
┌──────────────────────────────────────────────────────────────┐
│  Parent Entities (any of):                                   │
│  • sprk_matter                                               │
│  • sprk_project                                              │
│  • sprk_invoice                                              │
│  • account                                                   │
│  • contact                                                   │
│                                                               │
│  Common Fields:                                              │
│  • sprk_containerid (Single Line Text) - SPE Container ID    │
│  • [display name field] - Entity-specific primary name       │
└──────────────────────────────────────────────────────────────┘
                          │
                          │ 1:N Relationship
                          ↓
┌──────────────────────────────────────────────────────────────┐
│  sprk_document (Document Entity)                             │
│                                                               │
│  Lookup Fields (ONE populated per record):                   │
│  • sprk_matter (Lookup to sprk_matter)                       │
│  • sprk_project (Lookup to sprk_project)                     │
│  • sprk_invoice (Lookup to sprk_invoice)                     │
│  • sprk_account (Lookup to account)                          │
│  • sprk_contact (Lookup to contact)                          │
│                                                               │
│  SPE Metadata:                                               │
│  • sprk_documentname (Single Line Text) - Display name       │
│  • sprk_filename (Single Line Text) - Original file name     │
│  • sprk_graphdriveid (Single Line Text) - SPE Container ID   │
│  • sprk_graphitemid (Single Line Text) - SPE Item ID         │
│  • sprk_filesize (Whole Number) - File size in bytes         │
│                                                               │
│  User-Editable:                                              │
│  • sprk_documentdescription (Multi-line Text) - Notes        │
│  • ownerid (Lookup to systemuser) - Document owner           │
└──────────────────────────────────────────────────────────────┘
```

---

## Authentication & Security

**No changes from v2.3.0** - same OAuth2 OBO flow:

```
User opens Custom Page from Parent form
  → Custom Page receives parentRecordId via navigation parameters
  → PCF Control initializes
    → MsalAuthProvider.initialize()
      → PublicClientApplication.loginPopup() (if needed)
      → acquireTokenSilent(['api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation'])
    → User delegated token obtained

User clicks "Upload & Create"
  → File Upload (uses user token)
    → SdapApiClient sends token to BFF API
      → BFF API exchanges token (OAuth2 OBO flow)
        → Microsoft Graph API (user context)
          → SharePoint Embedded

  → Metadata Query (Phase 7 - uses BFF API credentials)
    → NavMapClient sends user token to BFF API
      → BFF API uses ClientSecretCredential (app-only)
        → Dataverse EntityDefinitions API
          → Returns navigation metadata

  → Record Creation (uses user context)
    → context.webAPI.createRecord()
      → Uses current user's Dataverse session
      → Dataverse Web API (user permissions)
```

**Security Checks:**
1. User must have Dataverse **Create** permission on `sprk_document`
2. User must have **Read** access to parent record (Matter/Project/etc.)
3. User must have SPE permissions via OAuth2 scope
4. BFF API validates user token before file upload
5. BFF API App Registration has Dynamics CRM API permission (Phase 7)

**See:** [SDAP-ARCHITECTURE-GUIDE.md § Authentication & Security](../../docs/SDAP-ARCHITECTURE-GUIDE.md#authentication--security)

---

## Error Handling

**No changes from v2.3.0** - comprehensive error handling maintained:

### Validation Errors (Pre-Upload)

```typescript
// File count validation
if (files.length > 10) {
    showError("Maximum 10 files allowed");
    return;
}

// File size validation (per file)
const oversizedFiles = files.filter(f => f.size > 10 * 1024 * 1024);
if (oversizedFiles.length > 0) {
    showError(`Files exceed 10MB limit: ${oversizedFiles.map(f => f.name).join(', ')}`);
    return;
}

// Total size validation
const totalSize = files.reduce((sum, f) => sum + f.size, 0);
if (totalSize > 100 * 1024 * 1024) {
    showError(`Total size exceeds 100MB limit`);
    return;
}

// File type validation
const blockedExtensions = ['.exe', '.dll', '.bat', '.cmd', '.ps1', '.vbs', '.js', '.jar'];
const dangerousFiles = files.filter(f =>
    blockedExtensions.some(ext => f.name.toLowerCase().endsWith(ext))
);
if (dangerousFiles.length > 0) {
    showError(`File types not allowed: ${dangerousFiles.map(f => f.name).join(', ')}`);
    return;
}
```

### Upload Errors (Phase 1)

```typescript
const uploadResults = await Promise.allSettled(uploads);

const successfulUploads = uploadResults.filter(r => r.status === 'fulfilled' && r.value.success);
const failedUploads = uploadResults.filter(r => r.status === 'rejected' || !r.value.success);

if (failedUploads.length > 0) {
    if (successfulUploads.length === 0) {
        showError("All uploads failed");
        return;
    } else {
        // Partial failure - ask user
        const proceed = await confirmDialog(
            `${successfulUploads.length} files uploaded successfully.\n` +
            `${failedUploads.length} files failed.\n\nContinue?`
        );
        if (!proceed) return;
    }
}
```

### Record Creation Errors (Phase 2)

```typescript
const createResults: CreateResult[] = [];

for (const file of successfulUploads) {
    try {
        const recordId = await documentRecordService.createDocument(file, parentContext, formData);
        createResults.push({ success: true, recordId, fileName: file.name });
    } catch (error) {
        createResults.push({ success: false, fileName: file.name, error: error.message });
    }
}

// Summary
const successCount = createResults.filter(r => r.success).length;
const failureCount = createResults.filter(r => !r.success).length;

if (failureCount > 0) {
    showSummary({
        success: successCount,
        failed: failureCount,
        errors: createResults.filter(r => !r.success)
    });
}
```

---

## Migration Strategy

### Phase 1: Create Custom Page (Sprint Goal)

**Tasks:**

1. **Create Custom Page Definition**
   - File: `sprk_DocumentUploadDialog.json`
   - Type: Dialog
   - Embed: UniversalQuickCreate PCF control
   - Input parameters: parentEntityName, parentRecordId, containerId, parentDisplayName, sdapApiBaseUrl

2. **Update PCF Control for Custom Page Mode**
   - Add detection for Custom Page context (vs Quick Create form)
   - Implement `closeDialog()` method using Custom Page API
   - Remove form save dependency
   - Add direct workflow execution on button click

3. **Update Ribbon Button Command**
   - Change action from "Open Quick Create" to "Open Custom Page"
   - Use `Xrm.Navigation.navigateTo()` instead of `Xrm.Utility.openQuickCreate()`
   - Pass navigation parameters (entity name, record ID, container ID)

4. **Update Solution Package**
   - Add Custom Page to solution
   - Update PCF control version to 3.0.0
   - Update ribbon customizations
   - Export solution: `SpaarkeDocumentUpload_3_0_0_0.zip`

### Phase 2: Testing & Validation

**Test Cases:**

1. **Functional Testing**
   - Test on each entity (Matter, Project, Invoice, Account, Contact)
   - Verify dialog opens correctly
   - Verify file uploads work
   - Verify Document records created with correct lookup
   - Verify dialog closes and subgrid refreshes

2. **Phase 7 Validation**
   - Verify metadata discovery works (correct navigation property case)
   - Verify caching works (check Redis cache hits)
   - Test with new entity to verify no code changes needed

3. **Error Scenarios**
   - Test validation errors
   - Test upload failures
   - Test record creation failures
   - Verify error messages displayed correctly

4. **User Acceptance Testing**
   - Real users test in DEV environment
   - Collect feedback on UI/UX
   - Compare to previous Quick Create experience

### Phase 3: Deployment

**Deployment Steps:**

1. **DEV Environment**
   - Import solution `SpaarkeDocumentUpload_3_0_0_0.zip`
   - Publish all customizations
   - Update command buttons on all entity forms
   - Test thoroughly

2. **UAT Environment**
   - Import solution
   - User acceptance testing
   - Performance validation
   - Security testing

3. **Production Environment**
   - Schedule deployment window
   - Import solution
   - Publish customizations
   - Monitor Application Insights for errors
   - Verify with pilot users

### Phase 4: Deprecation (Future)

**Once v3.0.0 validated:**

1. Remove Quick Create form (sprk_document quick create form)
2. Archive v2.x solution
3. Update documentation
4. Notify users of new UI

---

## Deployment Architecture

### Solution Package Structure (v3.0.0)

```
SpaarkeDocumentUpload_3_0_0_0.zip
│
├── Controls/
│   └── sprk_UniversalQuickCreate.xml (version 3.0.0.0)
│
├── CustomPages/
│   └── sprk_DocumentUploadDialog.json (NEW - version 3.0.0.0)
│
├── WebResources/
│   ├── sprk_subgrid_commands.js (updated for Custom Page navigation)
│   └── sprk_entity_document_config.json (unchanged)
│
├── RibbonCustomizations/
│   └── sprk_document_upload_button.xml (updated command actions)
│
└── solution.xml (version 3.0.0.0)
```

### Deployment Checklist

- [ ] Import solution into target environment
- [ ] Publish all customizations
- [ ] Verify Custom Page appears in solution
- [ ] Test command button on each entity
- [ ] Verify dialog opens correctly
- [ ] Test full upload workflow
- [ ] Check Application Insights for errors
- [ ] Verify version number in dialog footer (v3.0.0.0)

**See:** [PHASE-7-DEPLOYMENT-STATUS.md § Deployment Procedures](../../docs/PHASE-7-DEPLOYMENT-STATUS.md#deployment-procedures)

---

## Testing Strategy

### Unit Testing (TypeScript)

**No changes from v2.3.0:**
- File validation logic
- Payload building logic
- Configuration resolution
- Navigation parameter parsing

### Integration Testing (Manual)

**Custom Page specific:**
1. Dialog opens from each entity form
2. Input parameters passed correctly
3. Upload workflow completes successfully
4. Dialog closes on success
5. Subgrid refreshes automatically
6. Cancel button closes dialog without saving

**Multi-entity testing:**
1. Upload from Matter → Verify sprk_matter lookup
2. Upload from Project → Verify sprk_project lookup
3. Upload from Invoice → Verify sprk_invoice lookup
4. Upload from Account → Verify sprk_account lookup
5. Upload from Contact → Verify sprk_contact lookup

**Phase 7 testing:**
1. First upload queries metadata (check Network tab)
2. Second upload uses cache (no metadata API call)
3. Correct navigation property case used (e.g., "sprk_Matter" with capital M)

### Performance Testing

**Benchmarks (same as v2.3.0):**
- Upload 1 file: ~3-5 seconds
- Upload 10 files (100MB total): ~15-25 seconds
  - Phase 1 (upload): ~5-10 seconds
  - Phase 2 (records): ~10-15 seconds
- Metadata query (first): ~200-500ms
- Metadata query (cached): <10ms

### User Acceptance Testing

**Scenarios:**
- Real Matter/Project/Invoice/Account/Contact records
- Real SPE containers
- Production-like data volumes
- Multiple concurrent users

---

## Monitoring & Logging

**No changes from v2.3.0:**

### Client-Side Logging

```typescript
logger.info('DocumentUpload', 'Dialog opened', { parentEntityName, parentRecordId });
logger.info('DocumentUpload', 'Upload started', { fileCount, totalSize });
logger.info('DocumentUpload', 'Phase 1 complete', { successCount, failureCount });
logger.info('DocumentUpload', '[Phase 7] Using navigation property', { navigationPropertyName, source });
logger.info('DocumentUpload', 'Phase 2 complete', { recordsCreated });
logger.info('DocumentUpload', 'Dialog closed', { success: true });
logger.error('DocumentUpload', 'Upload failed', { error, fileName });
```

### Server-Side Monitoring (BFF API)

**Application Insights Queries:**

```kusto
// Custom Page dialog opens
customEvents
| where name == "DocumentUploadDialogOpened"
| summarize count() by parentEntityName
| order by count_ desc

// Phase 7 metadata queries
requests
| where url contains "/api/navmap/"
| summarize count(), avg(duration) by resultCode
| order by count_ desc

// Cache hit rate
dependencies
| where name contains "Redis" and name contains "NavMap"
| summarize hits = countif(success == true), misses = countif(success == false)
| extend hit_rate = (hits * 100.0) / (hits + misses)

// Upload success rate
requests
| where url contains "/api/obo/containers/"
| summarize success = countif(resultCode == 200), failure = countif(resultCode >= 400)
| extend success_rate = (success * 100.0) / (success + failure)
```

**See:** [PHASE-7-DEPLOYMENT-STATUS.md § Monitoring](../../docs/PHASE-7-DEPLOYMENT-STATUS.md#monitoring)

---

## Comparison: Quick Create vs Custom Page

| Aspect | Quick Create Form (v2.x) | Custom Page Dialog (v3.0) |
|--------|--------------------------|---------------------------|
| **UI Framework** | Model-driven form chrome | Fluent UI v9 (full control) |
| **Layout Control** | Limited by form layout | Complete flexibility |
| **Workflow Execution** | Tied to form save event | Independent control logic |
| **Progress Tracking** | Limited by form chrome | Real-time, custom UI |
| **Cancel/Close** | Form-managed | Control-managed |
| **Deployment** | Form + PCF + Ribbon | Custom Page + PCF + Ribbon |
| **User Experience** | Side panel (disconnected) | Modal dialog (focused) |
| **Version Management** | Multiple artifacts | Single solution package |
| **Extensibility** | Limited by form rules | Fully extensible |
| **Performance** | Same | Same (no change) |

---

## Future Enhancements

### v3.1.0 (Post-Migration)
- Drag-and-drop file selection
- File preview thumbnails
- Improved progress indicators
- Toast notifications for background uploads

### v3.2.0
- Document version history
- Duplicate detection (prevent re-uploading same file)
- Bulk metadata editing (apply description to multiple files)

### v4.0.0
- Large file support (>100MB with chunked upload)
- Unlimited file count (streaming upload)
- Background upload (close dialog, upload continues)
- Integration with Power Automate flows

---

## Related Documentation

### Sprint Documentation
- **[SPRINT-PLAN.md](./SPRINT-PLAN.md)** - Sprint backlog and task breakdown
- **[MIGRATION-GUIDE.md](./MIGRATION-GUIDE.md)** - Detailed migration steps
- **[TESTING-PLAN.md](./TESTING-PLAN.md)** - Comprehensive testing strategy

### SDAP Architecture
- **[SDAP-ARCHITECTURE-GUIDE.md](../../docs/SDAP-ARCHITECTURE-GUIDE.md)** - Complete SDAP architecture
- **[PHASE-7-DEPLOYMENT-STATUS.md](../../docs/PHASE-7-DEPLOYMENT-STATUS.md)** - Phase 7 deployment details
- **[HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md](../../docs/HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md)** - Adding new entities

### Dataverse Documentation
- **[KM-PCF-NAVIGATION-API-CONTROL.md](../../docs/KM-PCF-NAVIGATION-API-CONTROL.md)** - Custom Page navigation
- **[KM-DATAVERSE-AUTHENTICATION-GUIDE.md](../../docs/KM-DATAVERSE-AUTHENTICATION-GUIDE.md)** - Dataverse auth patterns

### Microsoft Documentation
- [Custom Pages Overview](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/model-app-page-overview)
- [Xrm.Navigation.navigateTo](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto)
- [PCF Custom Pages](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/component-framework-for-canvas-apps)

---

**Document Version:** 3.0.0-draft
**Last Updated:** 2025-10-20
**Authors:** Development Team
**Sprint:** Custom Page Migration
**Status:** 🟡 In Progress

---

## Change Log

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 1.0.0 | 2025-01-10 | Initial architecture (Quick Create form) | Dev Team |
| 2.0.0 | 2025-10-10 | Multi-entity support added | Dev Team |
| 2.3.0 | 2025-10-20 | Phase 7: Dynamic metadata discovery | Dev Team |
| 3.0.0 | 2025-10-20 | **Custom Page migration (THIS SPRINT)** | Dev Team |
